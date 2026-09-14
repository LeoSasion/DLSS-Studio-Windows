"""Atomic, portable job metadata. Media remains in the existing workspace folders."""
import json
import os
from pathlib import Path


ACTIVE = {"queued", "running", "cancelling"}


def save(path, root, assets, jobs):
    def portable(record):
        item = dict(record)
        for key in ("path", "result_path", "preview_path", "thumbnail_path"):
            if item.get(key):
                item[key] = Path(item[key]).resolve().relative_to(root.resolve()).as_posix()
        return item
    value = {"version": 1, "assets": [portable(a) for a in assets.values()],
             "jobs": [portable(j) for j in jobs.values()]}
    temp = path.with_suffix(".tmp")
    try:
        with temp.open("w", encoding="utf-8") as out:
            json.dump(value, out, ensure_ascii=False)
            out.flush()
            os.fsync(out.fileno())
        temp.replace(path)
    finally:
        temp.unlink(missing_ok=True)


def load(path, root):
    assets, jobs = {}, {}
    if not path.exists():
        return assets, jobs
    value = json.loads(path.read_text(encoding="utf-8"))
    if value.get("version") != 1:
        raise ValueError("不支持的任务记录版本")
    for kind, target in (("assets", assets), ("jobs", jobs)):
        for saved in value.get(kind, []):
            item = dict(saved)
            for key in ("path", "result_path", "preview_path", "thumbnail_path"):
                if item.get(key):
                    resolved = (root / item[key]).resolve()
                    allowed = root / ("uploads" if key == "path" else "ui_out")
                    if not resolved.is_relative_to(allowed.resolve()):
                        raise ValueError("任务记录路径无效")
                    item[key] = str(resolved)
            if kind == "assets" and not Path(item["path"]).is_file():
                continue
            if item.get("status") in ACTIVE:
                item.update(status="error", message="上次处理因服务退出而中断，请重新开始。", phase="interrupted")
            if item.get("status") == "done" and not Path(item.get("result_path", "")).is_file():
                item.update(status="error", message="结果文件已被移动或删除，请重新处理。", phase="missing")
                item.pop("download", None)
                item.pop("preview", None)
            target[item["id"]] = item
    return assets, jobs
