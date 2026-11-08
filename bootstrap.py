"""Fetch hash-pinned public build dependencies. Never writes to a game/save folder."""
from pathlib import Path, PurePosixPath
import argparse, hashlib, json, shutil, subprocess, urllib.request, zipfile

ROOT = Path(__file__).resolve().parent
LOCK = json.loads((ROOT/'dependencies.lock.json').read_text(encoding='utf-8'))['files']

def digest(path, algorithm):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, algorithm).hexdigest()

def download(name):
    item=LOCK[name]
    if name=='unity':
        bundled=ROOT/'vendor/UnitySupport/2022.3.62.zip'
        if bundled.is_file():
            if digest(bundled,item['algorithm']).lower()!=item['hash'].lower():
                raise RuntimeError('Bundled Unity reference ZIP hash mismatch')
            return bundled
    cache=ROOT/'.downloads'/name
    cache.parent.mkdir(exist_ok=True)
    if not cache.is_file():
        temporary=cache.with_suffix('.partial')
        print('Downloading',name,item['url'],flush=True)
        with urllib.request.urlopen(item['url'],timeout=120) as source, temporary.open('wb') as target:
            shutil.copyfileobj(source,target)
        if digest(temporary,item['algorithm']).lower()!=item['hash'].lower():
            raise RuntimeError('Downloaded hash mismatch: '+name)
        temporary.replace(cache)
    if digest(cache,item['algorithm']).lower()!=item['hash'].lower():
        raise RuntimeError('Cached dependency hash mismatch: '+name)
    return cache

def extract(archive,destination,accept=lambda relative:True):
    destination.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive) as source:
        for entry in source.infolist():
            relative=PurePosixPath(entry.filename.replace('\\','/'))
            if relative.is_absolute() or '..' in relative.parts or ':' in entry.filename:
                raise RuntimeError('Unsafe dependency ZIP path')
            if not accept(relative):continue
            target=destination.joinpath(*relative.parts)
            if entry.is_dir():target.mkdir(parents=True,exist_ok=True)
            else:
                target.parent.mkdir(parents=True,exist_ok=True)
                with source.open(entry) as src,target.open('wb') as dst:shutil.copyfileobj(src,dst)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--skip-sdks',action='store_true',help='Require existing pinned SDKs at tools/dotnet and tools/dotnet10.')
    args=parser.parse_args()
    (ROOT/'release').mkdir(exist_ok=True)
    for name,folder in [('sdk8','dotnet'),('sdk10','dotnet10')]:
        destination=ROOT/'tools'/folder
        executable=destination/'dotnet.exe'
        if not executable.is_file():
            if args.skip_sdks:raise RuntimeError('Missing SDK: '+str(executable))
            extract(download(name),destination)
        actual=subprocess.check_output([str(executable),'--version'],text=True).strip()
        if actual!=LOCK[name]['version']:raise RuntimeError(f'Expected SDK {LOCK[name]["version"]}, got {actual}')
    loader=ROOT/'packages/loader'
    marker=ROOT/'.downloads/bootstrap-state.json'
    if marker.exists():
        if json.loads(marker.read_text())!=LOCK:raise RuntimeError('Bootstrap lock changed; use a fresh build directory.')
        print('Pinned loader inputs already staged; build-loader.ps1 rebuilds the modified managed components.')
        return
    if loader.exists() and any(loader.iterdir()):
        raise RuntimeError('Loader staging already contains files without a matching bootstrap marker; use a fresh checkout.')
    base_archive,runtime_archive,unity=download('loader'),download('runtime10'),download('unity')
    extract(base_archive,loader,lambda path:not path.parts or path.parts[0]!='dotnet')
    # Doorstop loads CoreCLR from a flat directory, matching the shipped kit.
    with zipfile.ZipFile(runtime_archive) as source:
        prefix='shared/Microsoft.NETCore.App/'+LOCK['runtime10']['version']+'/'
        entries=[e for e in source.infolist() if e.filename.startswith(prefix) and not e.is_dir()]
        if len(entries)!=189:raise RuntimeError('Unexpected pinned runtime inventory')
        (loader/'dotnet').mkdir(exist_ok=True)
        for entry in entries:
            relative=PurePosixPath(entry.filename[len(prefix):])
            if len(relative.parts)!=1:raise RuntimeError('Runtime layout changed')
            (loader/'dotnet'/relative.name).write_bytes(source.read(entry))
    for destination in [loader/'BepInEx/unity-libs/2022.3.62.zip',ROOT.parent/'BepInEx/unity-libs/2022.3.62.zip']:
        # Only a support ZIP adjacent to the source checkout, never a game binary.
        destination.parent.mkdir(parents=True,exist_ok=True)
        if destination.exists() and digest(destination,'sha256')!=LOCK['unity']['hash']:
            raise RuntimeError('Existing Unity support ZIP differs: '+str(destination))
        shutil.copyfile(unity,destination)
    marker.write_text(json.dumps(LOCK,indent=2)+'\n',encoding='utf-8')
    print('Build inputs ready. Next: build-loader.ps1, then prepare local game bindings as documented in BUILD.md.')

if __name__=='__main__':main()
