"""Build the two Windows entry points and vendor Microsoft's WebView2 SDK."""
from pathlib import Path
import shutil
import subprocess
import zipfile

VERSION = "1.0.4191.47"

def build_launchers(package, root):
    cache = root / "packaging/cache"
    cache.mkdir(parents=True, exist_ok=True)
    sdk_zip = cache / f"microsoft.web.webview2.{VERSION}.nupkg"
    setup = cache / "MicrosoftEdgeWebview2Setup.exe"
    urls = {
        sdk_zip: f"https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/{VERSION}/microsoft.web.webview2.{VERSION}.nupkg",
        setup: "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
    }
    for file, url in urls.items():
        if not file.exists():
            subprocess.run(["curl.exe", "-L", "--fail", "--retry", "2", "--output", str(file), url], check=True)
    # Validate the bootstrapper's vendor signature without executing the installer.
    # Pass the file via an environment variable to avoid shell quoting of file paths.
    import os
    env = os.environ.copy()
    env["DLSS_WEBVIEW_SETUP"] = str(setup)
    check = 'Import-Module "$PSHOME/Modules/Microsoft.PowerShell.Security/Microsoft.PowerShell.Security.psd1"; ' + "$s=Get-AuthenticodeSignature -LiteralPath $env:DLSS_WEBVIEW_SETUP; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){exit 1}"
    subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", check], env=env, check=True)
    sdk = cache / "webview2-sdk"
    with zipfile.ZipFile(sdk_zip) as archive:
        archive.extractall(sdk)
    for name in ("Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll"):
        shutil.copy2(sdk / "lib/net462" / name, package / name)
    shutil.copy2(sdk / "runtimes/win-x64/native/WebView2Loader.dll", package / "WebView2Loader.dll")
    (package / "components").mkdir(exist_ok=True)
    shutil.copy2(setup, package / "components" / setup.name)
    (package / "licenses").mkdir(exist_ok=True)
    shutil.copy2(sdk / "LICENSE.txt", package / "licenses/WebView2-LICENSE.txt")
    shutil.copy2(sdk / "NOTICE.txt", package / "licenses/WebView2-NOTICE.txt")
    source = package / "launcher-source"
    source.mkdir(exist_ok=True)
    for name in ("Launcher.cs", "DesktopShell.cs"):
        shutil.copy2(root / "packaging" / name, source / name)
    compiler = Path(os.environ["SystemRoot"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    common = [str(compiler), "/nologo", "/target:winexe", "/platform:x64", "/optimize+", "/codepage:65001",
        "/reference:System.Windows.Forms.dll", "/reference:System.Drawing.dll", "/reference:System.Web.Extensions.dll"]
    subprocess.run(common + ["/main:Program", "/out:" + str(package / "WebUI启动器.exe"), str(source / "Launcher.cs")], check=True)
    subprocess.run(common + ["/main:DesktopProgram", "/out:" + str(package / "DLSS Studio.exe"),
        "/reference:" + str(package / "Microsoft.Web.WebView2.Core.dll"),
        "/reference:" + str(package / "Microsoft.Web.WebView2.WinForms.dll"),
        str(source / "Launcher.cs"), str(source / "DesktopShell.cs")], check=True)
    print("Built DLSS Studio.exe (embedded desktop) and WebUI启动器.exe (external browser).", flush=True)

if __name__ == "__main__":
    import sys
    root = Path(__file__).resolve().parent.parent
    package = Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root / "packaging/build/DLSS-Studio-Portable"
    build_launchers(package, root)
