"""Rebuild the offline Windows x64 ZIP from the verified workspace dependencies."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
BUILD = ROOT / "packaging" / "build"
PACKAGE = BUILD / "DLSS-Studio-Portable"
CACHE = ROOT / "packaging" / "cache"
ARTIFACTS = ROOT / "outputs"
for path in (BUILD, CACHE, ARTIFACTS):
    path.mkdir(parents=True, exist_ok=True)
if PACKAGE.exists() and any(PACKAGE.iterdir()):
    raise SystemExit(f"Staging already exists; choose a fresh build directory: {PACKAGE}")
PACKAGE.mkdir(exist_ok=True)

embedded = CACHE / "python-3.10.11-embed-amd64.zip"
if not embedded.exists():
    urllib.request.urlretrieve("https://www.python.org/ftp/python/3.10.11/python-3.10.11-embed-amd64.zip", embedded)
if hashlib.md5(embedded.read_bytes()).hexdigest() != "f1c0538b060e03cbb697ab3581cb73bc":
    raise SystemExit("Python archive differs from the official published checksum")
runtime = PACKAGE / "runtime"
with zipfile.ZipFile(embedded) as archive:
    archive.extractall(runtime)
(runtime / "python310._pth").write_text("python310.zip\n.\nLib\nLib/site-packages\n../app\nimport site\n", encoding="utf-8")
ignore = shutil.ignore_patterns("__pycache__", "*.pyc", "*.pyo")
shutil.copytree(ROOT / "app/.venv/Lib/site-packages", runtime / "Lib/site-packages", ignore=ignore)
# Python's embedded distribution omits distutils, used by some installed packages.
base = Path(json.loads(subprocess.check_output([str(ROOT / "app/.venv/Scripts/python.exe"), "-c", "import sys,json;print(json.dumps(sys.base_prefix))"], text=True)))
shutil.copytree(base / "Lib/distutils", runtime / "Lib/distutils", ignore=ignore)
app = PACKAGE / "app"
app.mkdir()
for name in ("app.py", "nr_video.py", "render_engine.py", "process_runner.py", "studio_storage.py", "ui_style.py", "studio_server.py", "serve_local.py", "gpu_runtime.py", "dlss5-components.json", "requirements-installed.txt"):
    shutil.copy2(ROOT / "app" / name, app / name)
for name in ("out", "web"):
    shutil.copytree(ROOT / "app" / name, app / name, ignore=ignore)
for name in ("uploads", "ui_out"):
    (app / name).mkdir()
(PACKAGE / "logs").mkdir()
licenses = PACKAGE / "licenses"
licenses.mkdir()
for name in ("LICENSE", "THIRD-PARTY-NOTICES.md"):
    shutil.copy2(ROOT / name, PACKAGE / name)
shutil.copy2(ROOT / "app/README.md", licenses / "video2dlssnr-README.md")
shutil.copy2(ROOT / "packaging/README-package.txt", PACKAGE / "使用说明.txt")
(licenses / "THIRD-PARTY.txt").write_text("""Bundled third-party components retain their respective notices and terms.
Original engine: https://github.com/DaniilSokolyuk/video2dlssnr (release v1.3)
NVIDIA NGX / DLSS binaries: supplied unchanged from the original release.
Python 3.10.11 x64: https://www.python.org/downloads/release/python-31011/
Python license: ../runtime/LICENSE.txt
Python package licenses: ../runtime/Lib/site-packages/*dist-info/
Phosphor icons: ../app/web/assets/icons/LICENSE
FFmpeg license and build options: ffmpeg-license.txt
FFmpeg source project: https://ffmpeg.org/
This ZIP packages the existing project and its verified dependencies; it does not grant additional rights to third-party components.
""", encoding="utf-8")
result = subprocess.run([str(app / "out/ffmpeg.exe"), "-L"], capture_output=True, text=True, errors="replace")
(licenses / "ffmpeg-license.txt").write_text(result.stdout + result.stderr, encoding="utf-8")
from build_launchers import build_launchers
build_launchers(PACKAGE, ROOT)
(PACKAGE / "启动工作台.bat").write_bytes('@echo off\r\ncd /d "%~dp0"\r\nstart "" "DLSS Studio.exe"\r\n'.encode("ascii"))
manifest = {}
for file in sorted(PACKAGE.rglob("*")):
    if file.is_file():
        manifest[file.relative_to(PACKAGE).as_posix()] = {"bytes":file.stat().st_size,"sha256":hashlib.sha256(file.read_bytes()).hexdigest()}
(PACKAGE / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"STAGED={PACKAGE}", flush=True)
print(f"FILES={len(manifest)} SIZE_MIB={sum(x['bytes'] for x in manifest.values())/1024**2:.1f}", flush=True)
