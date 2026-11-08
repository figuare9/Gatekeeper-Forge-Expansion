"""Prepare the original 0.15.4 inputs for inspection; never installs/launches."""
from pathlib import Path
import argparse, json, shutil
import bootstrap

ROOT=Path(__file__).resolve().parent
HISTORY=ROOT/'historical/installer-0.15.4'
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--skip-sdk-copy',action='store_true',help='Prepare inputs only; supply exact SDK 8 separately.')
args=parser.parse_args()
destination=ROOT/'.legacy-review/Workspace/GatekeeperModManager'
if destination.exists():raise SystemExit('Legacy review folder exists; preserve it or use a fresh checkout.')
for row in json.loads((HISTORY/'code-source-hashes.json').read_text(encoding='utf-8')):
    if bootstrap.digest(HISTORY/row['path'],'sha256')!=row['sha256'].lower():raise SystemExit('Legacy source hash mismatch: '+row['path'])
for row in json.loads((HISTORY/'shared-assets.json').read_text(encoding='utf-8')):
    if bootstrap.digest(ROOT/row['path'],'sha256')!=row['sha256']:raise SystemExit('Legacy asset hash mismatch: '+row['path'])
archive=bootstrap.download('loader')
shutil.copytree(HISTORY/'code',destination)
(destination/'release').mkdir(exist_ok=True)
shutil.copytree(ROOT/'src/GameMods/Assets',destination/'src/GameMods/Assets')
bootstrap.extract(archive,destination/'packages/loader')
unity=ROOT/'vendor/UnitySupport/2022.3.62.zip'
for target in [destination/'packages/loader/BepInEx/unity-libs/2022.3.62.zip',destination.parent/'BepInEx/unity-libs/2022.3.62.zip']:
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(unity,target)
if not args.skip_sdk_copy:
    sdk=ROOT/'tools/dotnet'
    if not (sdk/'dotnet.exe').is_file():raise SystemExit('Prepare SDK 8 with bootstrap.py first.')
    shutil.copytree(sdk,destination/'tools/dotnet')
print('Legacy 0.15.4 inputs prepared at',destination)
print('This intentionally retains the historical obsolete runtime and pre-hardening code. Review only; 0.15.5 is maintained.')
print('Supply locally generated legacy game bindings adjacent to the project before running its original build scripts. Read the historical guide.')
