from pathlib import Path
import hashlib
import json
import zipfile

root = Path(__file__).resolve().parent.parent
package = root / "packaging/build/DLSS-Studio-Portable"
manifest = {}
for file in sorted(package.rglob("*")):
    if file.is_file() and file.name != "manifest.json":
        name = file.relative_to(package).as_posix()
        assert not any(part in {"__pycache__", "uploads", "ui_out", "logs"} for part in file.relative_to(package).parts), name
        manifest[name] = {"bytes":file.stat().st_size,"sha256":hashlib.sha256(file.read_bytes()).hexdigest()}
(package / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
target = root / "outputs/DLSS-Studio-Windows-x64.zip"
with zipfile.ZipFile(target,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
    for file in sorted(package.rglob("*")):
        archive.write(file,Path(package.name) / file.relative_to(package))
digest=hashlib.sha256(target.read_bytes()).hexdigest()
(target.parent / (target.name+".sha256")).write_text(digest+"  "+target.name+"\n",encoding="ascii")
print(json.dumps({"zip":str(target),"bytes":target.stat().st_size,"sha256":digest,"payload_files":len(manifest)}),flush=True)
