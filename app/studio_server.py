"""Local studio UI and a single-worker bridge to the bundled DLSS renderer."""
import json
import hashlib
import mimetypes
import os
import subprocess
import threading
import uuid
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field, ConfigDict
from PIL import Image, ImageOps
import app as engine
from gpu_runtime import GpuRuntime

ROOT = Path(__file__).parent.resolve()
UPLOADS = ROOT / "uploads"
OUTPUTS = ROOT / "ui_out"
UPLOADS.mkdir(exist_ok=True)
OUTPUTS.mkdir(exist_ok=True)
api = FastAPI(title="DLSS Studio", docs_url=None, redoc_url=None)
assets = {}
jobs = {}
mutex = threading.Lock()
worker = ThreadPoolExecutor(max_workers=1)


def check_nvenc():
    try:
        result = subprocess.run([engine.find_tool("ffmpeg"), "-v", "error", "-f", "lavfi", "-i",
            "color=s=64x64:d=0.04", "-frames:v", "1", "-c:v", "h264_nvenc", "-f", "null", "-"],
            capture_output=True, text=True, timeout=15,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        return result.returncode == 0
    except Exception:
        return False


NVENC_AVAILABLE = check_nvenc()
GPU = GpuRuntime(ROOT)


@api.get("/api/health")
def health():
    # Lets the portable launcher identify its own copy without disclosing paths.
    instance = hashlib.sha256(str(ROOT).lower().encode("utf-8")).hexdigest()
    with mutex:
        busy = any(job.get("status") in {"queued", "running"} for job in jobs.values())
    return {"app": "dlss-studio", "instance": instance, "busy": busy}


class Settings(BaseModel):
    model_config = ConfigDict(extra="forbid")
    asset_id: str
    size: str = "×2"
    width: int = Field(0, ge=0, le=16384)
    height: int = Field(0, ge=0, le=16384)
    scale: float = Field(2, ge=1, le=4)
    style: str = "默认"
    preset: str = "默认"
    intensity: float = Field(1, ge=0, le=2)
    local_structure: float = Field(1, ge=0, le=2)
    local_tone: float = Field(1, ge=0, le=2)
    skin: float = Field(-1, ge=-1, le=2)
    global_tone: float = Field(-1, ge=-1, le=2)
    detail: float = Field(1, ge=0, le=2)
    color: float = Field(1, ge=0, le=1)
    ui_correction: bool = False
    auto_mask: bool = False
    hdr: bool = False
    motion: bool = True
    motion_vis: bool = False
    motion_engine: str = "auto"
    codec: str = "hevc_nvenc"
    container: str = "mp4"
    cq: int = Field(19, ge=0, le=51)
    bitrate: int = Field(0, ge=0, le=1000000)
    bit_depth: int = 10
    enc_preset: str = "p5"
    audio: str = "auto"
    prores_profile: str = "hq"
    frames: int = Field(0, ge=0)


@api.get("/api/options")
def options():
    return {"sizes": list(engine.SIZES), "hardware": GPU.public(),
            "presets": list(engine.PRESETS), "codecs": engine.CODECS,
            "containers": engine.CONTAINERS, "nvenc_available": NVENC_AVAILABLE}


@api.post("/api/upload")
async def upload(file: UploadFile = File(...)):
    name = Path(file.filename or "素材").name
    suffix = Path(name).suffix.lower()
    image_ext = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"}
    video_ext = {".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v"}
    if suffix not in image_ext | video_ext:
        raise HTTPException(400, "暂不支持此文件格式，请上传常见图片或视频文件。")
    aid = uuid.uuid4().hex
    folder = UPLOADS / aid
    folder.mkdir()
    path = folder / ("source" + suffix)
    try:
        with path.open("wb") as target:
            while chunk := await file.read(1024 * 1024):
                target.write(chunk)
        if suffix in image_ext:
            with Image.open(path) as original:
                im = ImageOps.exif_transpose(original)
                w, h = im.size
                # Normalize inputs unsupported by the CLI, preserve orientation and lossless pixels.
                path = folder / "source.png"
                im.convert("RGBA").save(path)
            kind = "image"
        else:
            probe = subprocess.run([engine.find_tool("ffprobe"), "-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=width,height", "-of", "json", str(path)],
                capture_output=True, text=True, timeout=30)
            stream = json.loads(probe.stdout).get("streams", [])[0]
            w, h = stream["width"], stream["height"]
            kind = "video"
        item = {"id": aid, "name": name, "kind": kind, "width": w, "height": h,
                "bytes": path.stat().st_size, "path": str(path), "url": f"/api/assets/{aid}"}
        assets[aid] = item
        return {k: v for k, v in item.items() if k != "path"}
    except Exception:
        raise HTTPException(400, "无法读取这个文件，请检查文件是否完整，或换一个格式。")
    finally:
        await file.close()


@api.get("/api/assets/{aid}")
def asset_file(aid: str):
    item = assets.get(aid)
    if not item:
        raise HTTPException(404, "素材已失效，请重新上传。")
    return FileResponse(item["path"])


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
            engine.size_args_image(item["path"], s.size, s.width, s.height, s.scale)
    out = folder / ("result." + s.container)
    args = [os.sys.executable, engine.NRV, "--in", item["path"], "--out", str(out),
            "--nr-motion-engine", s.motion_engine, "--nr-motion", "1" if s.motion else "0",
            "--codec", s.codec, "--enc-preset", s.enc_preset, "--bit-depth", str(s.bit_depth),
            "--prores-profile", s.prores_profile, "--audio", s.audio, "--cq", str(s.cq),
            "--bitrate", str(s.bitrate)] + sr + model_args(s) + engine.size_args_video(s.size, s.width, s.height, s.scale)
    if s.motion_vis:
        args.append("--nr-motion-vis")
    if s.frames:
        args += ["--frames", str(s.frames)]
    return args


def render(jid, s, item, selection):
    job = jobs[jid]
    folder = OUTPUTS / jid
    folder.mkdir()
    try:
        job.update(status="running", message="正在初始化渲染引擎…")
        proc = subprocess.Popen(build_command(s, item, folder, selection), cwd=str(ROOT), stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", bufsize=1,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        lines = [f"DLSS 5 · {selection.name} · {selection.profile} · DXGI {selection.adapter}",
                 f"NR DLL SHA-256: {selection.sha256}"]
        for line in engine._stream(proc):
            lines.append(line)
            job["log"] = "\n".join(lines[-60:])[-6000:]
            if "fps" in line.lower() or "%" in line:
                job["message"] = line[-200:]
        code = proc.wait()
        if code:
            log = job.get("log", "")
            if any(marker in log for marker in ["OutOfDate", "0xBAD0000C", "BAD0000C"]):
                raise RuntimeError("当前显卡驱动与已匹配的 DLSS 5 组件未能完成初始化，请检查驱动和处理日志。")
            if "required nvenc API version" in log or "minimum required Nvidia driver" in log:
                raise RuntimeError("当前驱动不支持此显卡编码，请选择 CPU 编码，或更新显卡驱动后重试。")
            raise RuntimeError("处理未完成，请展开处理日志查看原因。")
        files = list(folder.glob("*_nr.png")) if item["kind"] == "image" else [folder / ("result." + s.container)]
        if not files or not files[0].is_file():
            raise RuntimeError("渲染引擎未生成结果文件，请查看处理日志。")
        result = files[0]
        GPU.mark_verified(selection)
        preview = result
        if item["kind"] == "video" and (s.container, s.codec) not in engine.BROWSER_PLAYABLE:
            job["message"] = "正在生成浏览器预览…"
            preview = engine.make_preview(str(result))
        job.update(status="done", message="处理完成，可以查看并下载结果。", result_path=str(result),
                   preview_path=str(preview) if preview else None,
                   download=f"/api/jobs/{jid}/download", preview=f"/api/jobs/{jid}/preview" if preview else None,
                   hardware=GPU.public())
    except Exception as exc:
        job.update(status="error", message=str(exc))


@api.post("/api/jobs")
def start_job(s: Settings):
    item = assets.get(s.asset_id)
    if not item:
        raise HTTPException(400, "请先上传素材。")
    validate_settings(s)
    try:
        selection = GPU.prepare()
    except RuntimeError as exc:
        raise HTTPException(503, str(exc))
    with mutex:
        if any(j["status"] in ["queued", "running"] for j in jobs.values()):
            raise HTTPException(409, "已有任务正在处理，请等待完成。")
        jid = uuid.uuid4().hex
        jobs[jid] = {"id": jid, "status": "queued", "message": "准备开始处理…", "log": ""}
        worker.submit(render, jid, s, dict(item), selection)
    return {"id": jid}


@api.get("/api/jobs/{jid}")
def job_status(jid: str):
    if jid not in jobs:
        raise HTTPException(404, "任务不存在。")
    return {k: v for k, v in jobs[jid].items() if not k.endswith("_path")}


@api.get("/api/jobs/{jid}/{action}")
def result_file(jid: str, action: str):
    job = jobs.get(jid, {})
    if job.get("status") != "done" or action not in ["download", "preview"]:
        raise HTTPException(404, "结果尚未准备好。")
    path = job.get("result_path" if action == "download" else "preview_path")
    if not path:
        raise HTTPException(404, "此格式暂无浏览器预览，请下载结果。")
    return FileResponse(path, filename=Path(path).name if action == "download" else None)


api.mount("/", StaticFiles(directory=ROOT / "web", html=True), name="studio")
