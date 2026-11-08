"""Create the Linux archive with Unix executable permissions from a Windows build."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import tarfile
import zipfile

p = argparse.ArgumentParser()
p.add_argument('--root', type=Path, required=True)
p.add_argument('--version', required=True)
a = p.parse_args()
root = a.root.resolve()
version = a.version
content_version = re.search(r'const string ContentVersion = "([^"]+)"', (root / 'src/Manager.Core/ExpansionKit.cs').read_text()).group(1)
payload = root / 'packages/portable-payload.zip'
binary = root / 'linux-app/Gatekeeper.ForgeExpansion.Linux'
if binary.read_bytes()[:4] != b'\x7fELF':
    raise SystemExit('Published installer is not a native Linux ELF executable.')
with zipfile.ZipFile(payload) as bundle:
    manifest = json.loads(bundle.read('bundle.json'))
    if manifest['Version'] != content_version:
        raise SystemExit('The payload content version does not match the Linux kit.')
    custom = bundle.read('packages/mods/Gatekeeper.CustomMods.dll')
    if hashlib.sha256(custom).hexdigest() != hashlib.sha256((root / 'packages/mods/Gatekeeper.CustomMods.dll').read_bytes()).hexdigest():
        raise SystemExit('The Linux bundle does not contain the currently packaged gameplay plugin.')
    notices = [(i.filename, bundle.read(i)) for i in bundle.infolist() if i.filename.startswith('notices/') and not i.is_dir()]
output = root / f'release/Gatekeeper.ForgeExpansion-Linux-v{version}.tar.gz'
output.parent.mkdir(exist_ok=True)
folder = f'Gatekeeper.ForgeExpansion-Linux-v{version}'
with tarfile.open(output, 'w:gz', format=tarfile.PAX_FORMAT) as tar:
    for source, name, mode in [(binary, binary.name, 0o755), (root / 'linux/Install-Forge.sh', 'Install-Forge.sh', 0o755), (root / 'linux/README-LINUX.md', 'README-LINUX.md', 0o644)]:
        info = tar.gettarinfo(str(source), f'{folder}/{name}')
        info.mode = mode; info.uid = info.gid = 0; info.uname = info.gname = ''
        with source.open('rb') as stream:
            tar.addfile(info, stream)
    import io
    for name, content in notices:
        info = tarfile.TarInfo(f'{folder}/{name}'); info.size = len(content); info.mode = 0o644
        tar.addfile(info, io.BytesIO(content))
with tarfile.open(output, 'r:gz') as tar:
    entries = tar.getmembers()
    assert all(e.isfile() and not e.name.startswith('/') and '..' not in e.name.split('/') for e in entries)
    for name in ['Install-Forge.sh', binary.name]:
        assert tar.getmember(f'{folder}/{name}').mode == 0o755
    assert tar.extractfile(f'{folder}/Install-Forge.sh').read().startswith(b'#!/bin/sh\n')
digest = hashlib.sha256(output.read_bytes()).hexdigest()
output.with_suffix(output.suffix + '.sha256').write_text(f'{digest}  {output.name}\n', encoding='ascii')
print(f'Linux testing kit: {output}\nBytes: {output.stat().st_size}\nSHA-256: {digest}')
