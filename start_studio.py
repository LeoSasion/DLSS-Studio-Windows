"""Idempotent desktop launcher: reuse an existing studio, otherwise launch quietly."""
from pathlib import Path
import subprocess
import time
import urllib.request
import webbrowser

root = Path(__file__).parent.resolve()
app = root / "app"
url = "http://127.0.0.1:7860/"


def ready():
    try:
        with urllib.request.urlopen(url + "api/options", timeout=1) as response:
            return response.status == 200
    except Exception:
        return False


if not ready():
    with (app / "server.log").open("w", encoding="utf-8") as out, (app / "server-error.log").open("w", encoding="utf-8") as err:
        proc = subprocess.Popen([str(app / ".venv/Scripts/python.exe"), "-u", "serve_local.py"],
            cwd=app, stdout=out, stderr=err, creationflags=subprocess.CREATE_NO_WINDOW)
        (app / "server.pid").write_text(str(proc.pid), encoding="ascii")
    for _ in range(60):
        if ready():
            break
        if proc.poll() is not None:
            raise SystemExit("启动失败，请查看 app/server-error.log。")
        time.sleep(1)
    else:
        raise SystemExit("启动超时，请查看 app/server-error.log。")
webbrowser.open(url)
