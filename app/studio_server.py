"""Local Studio API: recoverable jobs, bounded logs and cancellable render pipelines."""
import hashlib
import json
import logging
import os
import re
import shutil
import subprocess
import threading
import time
import uuid
from collections import deque
from contextlib import asynccontextmanager
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Literal, get_args

from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field, ConfigDict, model_validator
from PIL import Image, ImageOps, UnidentifiedImageError
import render_engine as engine
import nr_video
import studio_storage
from gpu_runtime import GpuRuntime
from process_runner import ProcessGroup, RenderCancelled

ROOT = Path(__file__).parent.resolve()
UPLOADS, OUTPUTS = ROOT / "uploads", ROOT / "ui_out"
STATE_FILE = ROOT / "studio-state.json"
UPLOADS.mkdir(exist_ok=True)
OUTPUTS.mkdir(exist_ok=True)
@asynccontextmanager
async def lifespan(_api):
    yield
    with mutex:
        for event in cancellation.values():
            event.set()
        owned = list(runners.values())
    for group in owned:
        group.cancel()
    worker.shutdown(wait=False)


api = FastAPI(title="DLSS Studio", docs_url=None, redoc_url=None, lifespan=lifespan)
mutex = threading.RLock()
worker = ThreadPoolExecutor(max_workers=1)
runners, cancellation = {}, {}
uploading_ids = set()
ACTIVE = studio_storage.ACTIVE
try:
    assets, jobs = studio_storage.load(STATE_FILE, ROOT)
except (OSError, ValueError, KeyError, TypeError):
    logging.exception("Could not read Studio history; existing media files are retained")
    assets, jobs = {}, {}


def persist():
    # Callers hold mutex. Only lifecycle transitions hit disk, never each frame.
    studio_storage.save(STATE_FILE, ROOT, assets, jobs)


def update_job(jid, save=False, **values):
    with mutex:
        jobs[jid].update(values)
        if save:
            try:
                persist()
            except OSError:
                jobs[jid]["storage_warning"] = "任务记录暂时无法保存，请及时下载结果。"
                logging.exception("Could not persist job state")


def public_asset(item):
    return {key: value for key, value in item.items() if key != "path"}


def public_job(job):
    result = {key: value for key, value in job.items() if not key.endswith("_path")}
    for key, aid in (("asset", job.get("asset_id")), ("source_asset", job.get("source_asset_id"))):
        if aid in assets:
            result[key] = public_asset(assets[aid])
    return result


def check_nvenc(codec):
    try:
        return subprocess.run([engine.find_tool("ffmpeg"), "-v", "error", "-f", "lavfi", "-i",
            "color=s=128x128:d=0.04", "-frames:v", "1", "-c:v", codec, "-f", "null", "-"],
            capture_output=True, timeout=15,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)).returncode == 0
    except (OSError, subprocess.SubprocessError):
        return False


ENCODER_AVAILABLE = {codec: check_nvenc(codec) for codec in engine.CONTAINERS if codec.endswith("_nvenc")}
NVENC_AVAILABLE = ENCODER_AVAILABLE["h264_nvenc"]
WINDOWS_BUILD = engine.windows_build()
DEFAULT_CODEC = engine.default_video_codec(WINDOWS_BUILD, ENCODER_AVAILABLE)
GPU = GpuRuntime(ROOT)


@api.get("/api/health")
def health():
    with mutex:
        return {"app": "dlss-studio", "instance": hashlib.sha256(str(ROOT).lower().encode()).hexdigest(),
                "busy": any(j.get("status") in ACTIVE for j in jobs.values())}


OutputSize = Literal["原始尺寸（不放大）", "720p (1280×720)", "1080p (1920×1080)", "1440p (2560×1440)", "4K (3840×2160)"]


class Settings(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)
    asset_id: str
    request_id: str | None = Field(None, max_length=64)
    job_kind: Literal["full", "frame", "clip"] = "full"
    clip_start: float = Field(0, ge=0)
    clip_duration: float = Field(3, gt=0)
    source_asset_id: str | None = None
    frame_time: float | None = Field(None, ge=0)
    size: OutputSize = "原始尺寸（不放大）"
    style: str = "默认"
    preset: str = "默认"
    intensity: float = Field(1, ge=0, le=2)
    local_structure: float = Field(1, ge=0, le=2)
    local_tone: float = Field(1, ge=0, le=2)
    skin: float = Field(1, ge=0, le=2)
    global_tone: float = Field(1, ge=0, le=2)
    detail: float = Field(1, ge=0, le=2)
    color: float = Field(1, ge=0, le=1)
    ui_correction: bool = False
    auto_mask: bool = False
    hdr: bool = False
    motion: bool = True
    motion_vis: bool = False
    motion_engine: str = "auto"
    codec: str = DEFAULT_CODEC
    container: str = "mp4"
    cq: int = Field(19, ge=0, le=51)
    bitrate: int = Field(0, ge=0, le=1000000)
    bitrate_mode: Literal["auto", "manual"] = "auto"
    bit_depth: int = 8 if DEFAULT_CODEC.startswith("h264_") else 10
    enc_preset: str = "p5"
    audio: str = "auto"
    prores_profile: str = "hq"
    frames: int = Field(0, ge=0)

    @model_validator(mode="before")
    @classmethod
    def preserve_explicit_bitrate(cls, values):
        # Earlier saved versions and API clients have no bitrate_mode field.
        if isinstance(values, dict) and "bitrate" in values and "bitrate_mode" not in values:
            values = {**values, "bitrate_mode": "manual"}
        return values


def validate_settings(s):
    for value, allowed in [(s.size, engine.SIZES), (s.style, engine.STYLES),
                           (s.preset, engine.PRESETS),
                           (s.codec, engine.CONTAINERS), (s.motion_engine, ["auto", "nvof", "lk"]),
                           (s.audio, engine.AUDIO), (s.prores_profile, engine.PRORES_PROFILES),
                           (s.enc_preset, [f"p{i}" for i in range(1, 8)]), (s.bit_depth, [8, 10])]:
        if value not in allowed:
            raise HTTPException(400, "参数无效，请重新选择设置。")
    if s.container not in engine.CONTAINERS[s.codec]:
        raise HTTPException(400, "所选编码与文件格式不兼容。")


def model_args(s):
    return engine.nr_model_args(s.style, s.preset, s.intensity, s.local_structure, s.local_tone,
        s.skin, s.global_tone, s.detail, s.color, s.ui_correction, s.auto_mask, s.hdr)


def build_command(s, item, folder, selection):
    sr = selection.args()
    if item["kind"] == "image":
        return [engine.EXE, "--nr-run", "--in", item["path"], "--out", str(folder)] + sr + model_args(s) + \
            engine.size_args_image(item["path"], s.size, 0, 0, 1)
    out = folder / ("result." + s.container)
    bitrate = engine.recommended_bitrate(s.size, item["width"], item["height"]) if s.bitrate_mode == "auto" else s.bitrate
    args = [os.sys.executable, engine.NRV, "--in", item["path"], "--out", str(out),
            "--nr-motion-engine", s.motion_engine, "--nr-motion", "1" if s.motion else "0",
            "--codec", s.codec, "--enc-preset", s.enc_preset, "--bit-depth", str(s.bit_depth),
            "--prores-profile", s.prores_profile, "--audio", s.audio, "--cq", str(s.cq),
            "--bitrate", str(bitrate)] + sr + model_args(s) + engine.size_args_video(s.size, 0, 0, 1)
    if s.motion_vis:
        args.append("--nr-motion-vis")
    if s.frames:
        args += ["--frames", str(s.frames)]
    if s.job_kind == "clip":
        args += ["--start", str(s.clip_start), "--duration", str(s.clip_duration)]
    return args


@api.get("/api/options")
def options():
    return {"sizes": list(get_args(OutputSize)), "hardware": GPU.public(),
            "codecs": engine.CODECS, "containers": engine.CONTAINERS,
            "nvenc_available": NVENC_AVAILABLE, "encoder_available": ENCODER_AVAILABLE,
            "video_defaults": {"codec": DEFAULT_CODEC, "container": "mp4", "bitrate_mode": "auto",
                "windows_build": WINDOWS_BUILD,
                "bitrates": {size: engine.recommended_bitrate(size) for size in get_args(OutputSize)}}, "version": 2}


def remove_folder(path, parent):
    # Only direct, generated child directories are eligible; never follow a junction.
    path, parent = Path(path), Path(parent).resolve()
    resolved = path.resolve()
    if resolved.parent != parent or path.is_symlink() or (path.lstat().st_file_attributes & 0x400 if os.name == "nt" else False):
        raise ValueError("拒绝清理缓存目录之外的文件。")
    shutil.rmtree(path)


@api.post("/api/upload")
def upload(file: UploadFile = File(...)):
    # A sync route runs in FastAPI's thread pool. Copying, PIL and ffprobe must not
    # block the event loop serving status, cancellation and video range requests.
    name = Path(file.filename or "素材").name
    suffix = Path(name).suffix.lower()
    image_ext = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"}
    video_ext = {".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v"}
    aid = uuid.uuid4().hex
    folder = UPLOADS / aid
    try:
        if suffix not in image_ext | video_ext:
            raise HTTPException(400, "暂不支持此格式，请上传常见图片或视频文件。")
        with mutex:
            uploading_ids.add(aid)
            folder.mkdir()
        path = folder / ("source" + suffix)
        with path.open("wb") as target:
            shutil.copyfileobj(file.file, target, 1024 * 1024)
        if suffix in image_ext:
            with Image.open(path) as original:
                im = ImageOps.exif_transpose(original)
                w, h = im.size
                normalized = folder / "normalized.png"
                im.convert("RGBA").save(normalized)
            target = folder / "source.png"
            normalized.replace(target)
            if path != target:
                path.unlink()
            path, kind = target, "image"
        else:
            info = nr_video.probe(engine.find_tool("ffprobe"), str(path))
            w, h, kind = info["w"], info["h"], "video"
        item = {"id": aid, "name": name, "kind": kind, "width": w, "height": h,
                "bytes": path.stat().st_size, "path": str(path), "url": f"/api/assets/{aid}",
                "created_at": time.time()}
        if kind == "video":
            item.update(duration=info.get("duration", 0), fps=info["fps"])
        with mutex:
            assets[aid] = item
            try:
                persist()
            except Exception:
                assets.pop(aid, None)
                raise
        return public_asset(item)
    except HTTPException:
        raise
    except (UnidentifiedImageError, Image.DecompressionBombError) as exc:
        raise HTTPException(400, "图片无法读取或尺寸过大，请检查文件或缩小尺寸后上传。") from exc
    except OSError as exc:
        raise HTTPException(507, "素材无法保存，请检查磁盘空间和文件夹写入权限。") from exc
    except (ValueError, KeyError, IndexError, subprocess.SubprocessError) as exc:
        raise HTTPException(400, "无法读取这个文件，请检查文件是否完整或先转换格式。") from exc
    finally:
        file.file.close()
        with mutex:
            uploading_ids.discard(aid)
            if aid not in assets and folder.exists():
                try:
                    remove_folder(folder, UPLOADS)
                except OSError:
                    logging.exception("Could not remove failed upload")


@api.get("/api/assets/{aid}/info")
def asset_info(aid: str):
    with mutex:
        if aid not in assets:
            raise HTTPException(404, "素材已失效，请重新上传。")
        return public_asset(assets[aid])


@api.get("/api/assets/{aid}")
def asset_file(aid: str):
    with mutex:
        item = assets.get(aid)
        if not item or not Path(item["path"]).is_file():
            raise HTTPException(404, "素材已失效，请重新上传。")
        return FileResponse(item["path"])


def progress_values(line):
    match = re.search(r"(\d+)\s*/\s*(\d+)\s*\(\s*(\d+)%\)\s*([\d.]+) fps\s+ETA ([\d:]+|--:--)", line)
    if match:
        frame, total, percent, fps, eta = match.groups()
        return dict(phase="render", message="正在增强视频", progress=min(99, int(percent)),
                    frame=int(frame), total_frames=int(total), fps=float(fps), eta=eta)
    return {}


def render(jid, s, item, selection):
    group = None
    event = cancellation.setdefault(jid, threading.Event())
    try:
        if event.is_set():
            raise RenderCancelled()
        folder = OUTPUTS / jid
        folder.mkdir()  # Must be inside try: a failed mkdir must release the queue.
        update_job(jid, save=True, status="running", phase="initializing", message="正在初始化渲染引擎…", progress=None)
        group = ProcessGroup(event)
        with mutex:
            runners[jid] = group
        proc = group.start(build_command(s, item, folder, selection), cwd=str(ROOT),
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", bufsize=1)
        lines = deque([f"DLSS 5 · {selection.name} · {selection.profile} · DXGI {selection.adapter}",
                       f"NR DLL SHA-256: {selection.sha256}"], maxlen=60)
        for line in engine._stream(proc):
            group.check()
            lines.append(line)
            update_job(jid, log="\n".join(lines)[-6000:], **progress_values(line))
        code = proc.wait()
        group.check()
        if code:
            log = jobs[jid].get("log", "")
            if any(marker in log for marker in ("OutOfDate", "0xBAD0000C", "BAD0000C")):
                raise RuntimeError("显卡驱动与 DLSS 5 组件未能完成初始化，请检查驱动和处理日志。")
            if "required nvenc API version" in log or "minimum required Nvidia driver" in log:
                raise RuntimeError("当前驱动不支持此编码，请选择 CPU 编码后重试。")
            raise RuntimeError("处理未完成，请展开处理日志查看原因。")
        files = list(folder.glob("*_nr.png")) if item["kind"] == "image" else [folder / ("result." + s.container)]
        if not files or not files[0].is_file() or files[0].stat().st_size == 0:
            raise RuntimeError("渲染引擎未生成结果文件，请查看处理日志。")
        result = preview = files[0]
        if item["kind"] == "image":
            with Image.open(result) as im:
                width, height = im.size
        else:
            info = nr_video.probe(engine.find_tool("ffprobe"), str(result))
            width, height = info["w"], info["h"]
            if (s.container, s.codec) not in engine.BROWSER_PLAYABLE:
                update_job(jid, phase="preview", progress=None, message="增强已完成，正在生成预览…", eta=None)
                preview = engine.make_preview(str(result), runner=group, nvenc=NVENC_AVAILABLE)
        thumbnail = result if item["kind"] == "image" else None
        if item["kind"] == "video":
            thumbnail = folder / "thumbnail.jpg"
            thumb_proc = group.start([engine.find_tool("ffmpeg"), "-v", "error", "-i", str(preview or result),
                "-frames:v", "1", "-vf", "scale=160:-2", str(thumbnail)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            thumb_proc.wait()
            group.check()
            if not thumbnail.is_file():
                thumbnail = None
        with mutex:
            group.check()
            GPU.mark_verified(selection)
            update_job(jid, save=True, status="done", phase="done", progress=100, eta=None,
                message="处理完成，可以查看并下载结果。", result_path=str(result), width=width, height=height,
                preview_path=str(preview) if preview else None, finished_at=time.time(),
                thumbnail_path=str(thumbnail) if thumbnail else None,
                thumbnail=f"/api/jobs/{jid}/thumbnail" if thumbnail else None,
                download=f"/api/jobs/{jid}/download", preview=f"/api/jobs/{jid}/preview" if preview else None,
                hardware=GPU.public())
    except RenderCancelled:
        update_job(jid, save=True, status="cancelled", phase="cancelled", message="已取消处理。", finished_at=time.time())
    except Exception as exc:
        message = "无法写入处理文件，请检查磁盘空间和文件夹权限。" if isinstance(exc, OSError) else str(exc)
        update_job(jid, save=True, status="error", phase="error", message=message, finished_at=time.time())
    finally:
        try:
            if group:
                group.close()
        finally:
            with mutex:
                runners.pop(jid, None)
                cancellation.pop(jid, None)


@api.post("/api/jobs")
def start_job(s: Settings):
    validate_settings(s)
    with mutex:
        item = assets.get(s.asset_id)
        if item and item["kind"] == "video" and s.bitrate_mode == "auto":
            s.bitrate = engine.recommended_bitrate(s.size, item["width"], item["height"])
        if s.request_id:
            previous = next((j for j in jobs.values() if j.get("request_id") == s.request_id), None)
            if previous:
                if previous.get("settings") != s.model_dump():
                    raise HTTPException(409, "重复请求的参数不一致，请重新提交。")
                return {"id": previous["id"]}
        if any(j["status"] in ACTIVE for j in jobs.values()):
            raise HTTPException(409, "已有任务正在处理，请在最近结果中恢复该任务。")
        item = assets.get(s.asset_id)
        if not item or not Path(item["path"]).is_file():
            raise HTTPException(400, "请先上传素材。")
        if s.job_kind == "frame":
            source = assets.get(s.source_asset_id)
            if item["kind"] != "image" or not source or source["kind"] != "video" or s.frame_time is None:
                raise HTTPException(400, "单帧测试缺少原视频或时间点，请重新测试。")
        if s.job_kind == "clip":
            if item["kind"] != "video" or s.frames:
                raise HTTPException(400, "短片段需要视频素材，不能同时限制帧数。")
            duration = item.get("duration") or nr_video.probe(engine.find_tool("ffprobe"), item["path"])["duration"]
            if s.clip_start >= duration or s.clip_start + s.clip_duration > duration + .001:
                raise HTTPException(400, "片段超出视频范围，请调整起点或时长。")
        try:
            selection = GPU.prepare()
        except RuntimeError as exc:
            raise HTTPException(503, str(exc)) from exc
        jid = uuid.uuid4().hex
        jobs[jid] = {"id": jid, "status": "queued", "phase": "queued", "message": "准备开始处理…", "log": "",
            "request_id": s.request_id, "asset_id": s.asset_id, "source_asset_id": s.source_asset_id,
            "job_kind": s.job_kind, "frame_time": s.frame_time, "clip_start": s.clip_start if s.job_kind == "clip" else 0,
            "settings": s.model_dump(), "created_at": time.time()}
        try:
            persist()
        except OSError as exc:
            jobs.pop(jid)
            raise HTTPException(507, "任务无法保存，请检查磁盘空间和文件夹权限。") from exc
        cancellation[jid] = threading.Event()
        try:
            worker.submit(render, jid, s, dict(item), selection)
        except RuntimeError as exc:
            update_job(jid, save=True, status="error", message="任务服务已停止，请重新打开程序。")
            cancellation.pop(jid, None)
            raise HTTPException(503, "任务服务已停止，请重新打开程序。") from exc
        return {"id": jid}


@api.get("/api/jobs")
def recent_jobs(offset: int = 0, limit: int = 20, source: str | None = None):
    with mutex:
        ordered = sorted(jobs.values(), key=lambda j: j.get("created_at", 0), reverse=True)
        if source:
            ordered = [j for j in ordered if (j.get("source_asset_id") or j.get("asset_id")) == source]
        return {"jobs": [public_job(j) for j in ordered[max(0, offset):max(0, offset)+max(1, min(limit, 50))]],
                "total": len(ordered), "active": [public_job(j) for j in ordered if j["status"] in ACTIVE]}


@api.get("/api/history/sources")
def history_sources():
    with mutex:
        groups = {}
        for job in sorted(jobs.values(), key=lambda j: j.get("created_at", 0), reverse=True):
            aid = job.get("source_asset_id") or job.get("asset_id")
            if aid not in groups:
                groups[aid] = {"id": aid, "name": assets.get(aid, {}).get("name", "素材已清理"), "count": 0}
            groups[aid]["count"] += 1
        return {"sources": list(groups.values())}


class JobLabel(BaseModel):
    model_config = ConfigDict(extra="forbid")
    name: str = Field(max_length=80)
    favorite: bool


@api.patch("/api/jobs/{jid}")
def label_job(jid: str, label: JobLabel):
    with mutex:
        if jid not in jobs:
            raise HTTPException(404, "任务不存在，可能已被清理。")
        previous = dict(jobs[jid])
        jobs[jid].update(name=label.name.strip(), favorite=label.favorite)
        try:
            persist()
        except OSError as exc:
            jobs[jid] = previous
            raise HTTPException(507, "版本标记无法保存，请检查磁盘空间。") from exc
        return public_job(jobs[jid])


@api.get("/api/jobs/{jid}")
def job_status(jid: str):
    with mutex:
        if jid not in jobs:
            raise HTTPException(404, "任务不存在，可能已被清理。")
        return public_job(jobs[jid])


@api.post("/api/jobs/{jid}/cancel")
def cancel_job(jid: str):
    with mutex:
        if jid not in jobs:
            raise HTTPException(404, "任务不存在。")
        if jobs[jid]["status"] not in ACTIVE:
            return public_job(jobs[jid])
        cancellation[jid].set()
        update_job(jid, save=True, status="cancelling", message="正在取消处理…")
        group = runners.get(jid)
    if group:
        group.cancel()
    return job_status(jid)


@api.get("/api/jobs/{jid}/{action}")
def result_file(jid: str, action: str):
    with mutex:
        job = jobs.get(jid, {})
        if job.get("status") != "done" or action not in {"download", "preview", "thumbnail"}:
            raise HTTPException(404, "结果尚未准备好。")
        path = job.get({"download": "result_path", "preview": "preview_path", "thumbnail": "thumbnail_path"}[action])
        if not path or not Path(path).is_file():
            raise HTTPException(404, "此预览或结果已失效，请重新处理。")
        return FileResponse(path, filename=Path(path).name if action == "download" else None)


class Cleanup(BaseModel):
    keep_asset_ids: list[str] = Field(default_factory=list, max_length=100)
    keep_job_ids: list[str] = Field(default_factory=list, max_length=100)
    include_results: bool = False
    preview_token: str | None = None


def folder_bytes(folder):
    def regular(path):
        stat = path.lstat()
        return not path.is_symlink() and not (getattr(stat, "st_file_attributes", 0) & 0x400)
    if not regular(folder):
        return 0
    total = 0
    for base, directories, files in os.walk(folder, followlinks=False):
        directories[:] = [name for name in directories if regular(Path(base) / name)]
        for name in files:
            path = Path(base) / name
            if regular(path):
                total += path.stat().st_size
    return total


@api.get("/api/cache")
def cache_stats():
    with mutex:
        return {"bytes": folder_bytes(UPLOADS) + folder_bytes(OUTPUTS), "assets": len(assets), "jobs": len(jobs)}


def cleanup_plan(request):
    keep_jobs = set(request.keep_job_ids) | {j["id"] for j in jobs.values() if j["status"] in ACTIVE or j.get("favorite")}
    if not request.include_results:
        keep_jobs |= {j["id"] for j in jobs.values() if j["status"] == "done"}
    keep_assets = set(request.keep_asset_ids) | uploading_ids
    for jid in keep_jobs:
        job = jobs.get(jid, {})
        keep_assets.update(a for a in (job.get("asset_id"), job.get("source_asset_id")) if a)
    keep_assets |= {a["id"] for a in assets.values() if time.time() - a.get("created_at", 0) < 3600}
    candidates = []
    for parent, keep, records in ((OUTPUTS, keep_jobs, jobs), (UPLOADS, keep_assets, assets)):
        for folder in sorted(parent.iterdir()):
            if not folder.is_dir() or folder.name in keep or not re.fullmatch(r"[0-9a-f]{32}", folder.name):
                continue
            if folder.resolve().parent != parent.resolve() or folder.is_symlink() or getattr(folder.lstat(), "st_file_attributes", 0) & 0x400:
                continue
            if parent == OUTPUTS and folder.name not in jobs and not request.include_results:
                if any(f.is_file() and f.stat().st_size for f in folder.iterdir()):
                    continue
            record = records.get(folder.name, {})
            asset = assets.get(record.get("source_asset_id") or record.get("asset_id"), {})
            candidates.append({"id": folder.name, "kind": "asset" if parent == UPLOADS else "result" if record.get("status") == "done" else "temporary",
                "name": record.get("name") or asset.get("name") or "未完成或旧版文件", "bytes": folder_bytes(folder)})
    token = hashlib.sha256(json.dumps(candidates, sort_keys=True).encode()).hexdigest()
    return candidates, keep_jobs, token


@api.post("/api/cache/preview")
def preview_cleanup(request: Cleanup):
    with mutex:
        candidates, _, token = cleanup_plan(request)
        counts = {kind: sum(i["kind"] == kind for i in candidates) for kind in ("temporary", "asset", "result")}
        return {"items": candidates, "bytes": sum(i["bytes"] for i in candidates), "counts": counts, "preview_token": token}


@api.post("/api/cache/cleanup")
def cleanup_cache(request: Cleanup):
    removed, skipped, freed = 0, 0, 0
    with mutex:
        candidates, keep_jobs, token = cleanup_plan(request)
        if request.preview_token is not None and request.preview_token != token:
            raise HTTPException(409, "清理范围已变化，请刷新预览后重试。")
        for entry in candidates:
            parent, records = (UPLOADS, assets) if entry["kind"] == "asset" else (OUTPUTS, jobs)
            try:
                remove_folder(parent / entry["id"], parent)
                records.pop(entry["id"], None)
                removed += 1
                freed += entry["bytes"]
            except (OSError, ValueError):
                skipped += 1
        for jid in list(jobs):
            if jid not in keep_jobs and not (OUTPUTS / jid).exists():
                jobs.pop(jid)
        try:
            persist()
        except OSError as exc:
            raise HTTPException(507, "文件已清理，但记录未能保存，请检查磁盘空间。") from exc
    return {"removed": removed, "freed_bytes": freed, "skipped": skipped}


api.mount("/", StaticFiles(directory=ROOT / "web", html=True), name="studio")
