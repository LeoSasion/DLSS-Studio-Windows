"""Pin and extract only the GPU-specific NR DLLs from the requested release."""
import hashlib
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = 'https://github.com/purkatyy/DLSS5-/releases/tag/dlss'
ASSETS = {
    'rtx30': ('RTX30xx.rar', '62427325f40aef0fc703904f53055883740ff8d2721203cfe376f33892b304b1'),
    'rtx40': ('DLSS5Tool.zip', '733c334f3b30d10d7fbd4d75f7242ff01b6e08380ac3396b39920e47eb6f65d0'),
    'rtx50': ('RTX50xx.rar', '97d5d4292937d5f7839c2a0755c63961dc5db8b755fd994e37607496e14e31dd'),
}


def prepare(root=ROOT):
    cache = root / 'packaging/cache/dlss5'
    cache.mkdir(parents=True, exist_ok=True)
    profiles = {}
    for profile, (name, digest) in ASSETS.items():
        archive = cache / name
        url = 'https://github.com/purkatyy/DLSS5-/releases/download/dlss/' + name
        if not archive.exists():
            subprocess.run(['curl.exe', '-L', '--fail', '--retry', '2', '-o', str(archive), url], check=True)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != digest:
            raise ValueError('Release checksum mismatch: ' + name)
        target = root / 'app/out/dlss5' / profile
        target.mkdir(parents=True, exist_ok=True)
        dll = target / 'nvngx_dlssnr.dll'
        if name.endswith('.zip'):
            with zipfile.ZipFile(archive) as z:
                with z.open('nvngx_dlssnr.dll') as src, dll.open('wb') as dest:
                    shutil.copyfileobj(src, dest)
        else:
            unrar = shutil.which('UnRAR') or r'C:\Program Files\WinRAR\UnRAR.exe'
            # Flatten only the named DLL; never execute or extract the other app.
            subprocess.run([unrar, 'e', '-idq', '-y', str(archive), '*nvngx_dlssnr.dll', str(target) + '/'], check=True)
        data = dll.read_bytes()
        if data[:2] != b'MZ':
            raise ValueError('Invalid DLL: ' + profile)
        profiles[profile] = {'path': dll.relative_to(root / 'app').as_posix(),
                             'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data),
                             'asset': name, 'asset_sha256': digest, 'url': url}
    manifest = {'source': SOURCE, 'tag': 'dlss', 'profiles': profiles,
                'note': 'Publisher marks RTX 30/50 variants as untested. Matching is not proof of compatibility.'}
    (root / 'app/dlss5-components.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(profiles, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    prepare()
