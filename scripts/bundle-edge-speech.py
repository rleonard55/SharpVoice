"""Build the pinned, private Windows x64 speech runtime. No system installation.
Run python scripts/bundle-edge-speech.py; downloads only hash-locked official archives.
The resulting zip is embedded by SharpVoice.Platform.Windows.csproj.
"""
import hashlib
import io
import json
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LOCK = ROOT / 'vendor/edge-speech/bundle.lock.json'
DEST = ROOT / 'src/SharpVoice.Platform.Windows/Assets/edge-speech.zip'
CACHE = ROOT / '.build-cache/edge-speech-downloads'


def build():
    CACHE.mkdir(parents=True, exist_ok=True)
    contents = {}
    for item in json.loads(LOCK.read_text()):
        path = CACHE / item['file']
        if not path.exists() or hashlib.sha256(path.read_bytes()).hexdigest() != item['sha256']:
            with urllib.request.urlopen(item['url'], timeout=90) as response:
                data = response.read(40_000_001)
            if len(data) > 40_000_000 or hashlib.sha256(data).hexdigest() != item['sha256']:
                raise RuntimeError('Archive integrity mismatch: ' + item['file'])
            path.write_bytes(data)
        with zipfile.ZipFile(path) as archive:
            for entry in archive.infolist():
                if entry.is_dir():
                    continue
                parts = Path(entry.filename).parts
                if '..' in parts or entry.filename.startswith(('/', '\\')):
                    raise RuntimeError('Unsafe upstream archive')
                name = ('' if item['target'] == 'runtime' else 'Lib/') + entry.filename
                if name in contents:
                    raise RuntimeError('Duplicate package file: ' + name)
                contents[name] = archive.read(entry)
    # Isolated interpreter: only this private standard library and vendored packages.
    contents['python313._pth'] = b'python313.zip\n.\nLib\n'
    contents['BUNDLE.lock.json'] = LOCK.read_bytes()
    DEST.parent.mkdir(parents=True, exist_ok=True)
    temporary = DEST.with_suffix('.tmp')
    with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(contents.items()):
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)
    temporary.replace(DEST)
    print(f'Bundled {len(contents)} files, {DEST.stat().st_size} bytes; SHA-256 {hashlib.sha256(DEST.read_bytes()).hexdigest()}')


if __name__ == '__main__':
    build()
