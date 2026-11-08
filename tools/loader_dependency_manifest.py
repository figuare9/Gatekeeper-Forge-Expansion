"""Record deployed loader provenance and ship dependency licenses without private paths."""
from pathlib import Path
import json, hashlib, xml.etree.ElementTree as ET, shutil, re

root=Path(__file__).resolve().parents[1]
project=root/'vendor/BepInEx/Runtimes/Unity/BepInEx.Unity.IL2CPP'
assets=json.loads((project/'obj/project.assets.json').read_text(encoding='utf-8-sig'))
package_roots=[Path(p) for p in assets['packageFolders']]
output=root/'vendor/build-net10/Unity.IL2CPP'
notices=root/'packages/portable-notices'
records=[]
for key,library in assets['libraries'].items():
    if library.get('type')!='package':continue
    folder=next((p/library['path'] for p in package_roots if (p/library['path']).is_dir()),None)
    if folder is None:raise RuntimeError('Missing resolved dependency: '+key)
    nuspec=next(folder.glob('*.nuspec'))
    tree=ET.parse(nuspec)
    metadata=next(e for e in tree.getroot() if e.tag.split('}')[-1]=='metadata')
    children={e.tag.split('}')[-1]:e for e in metadata}
    license_element=children.get('license')
    repo=children.get('repository')
    deployed=[]
    for relative in library['files']:
        source=folder/relative
        target=output/source.name
        if source.suffix.lower()=='.dll' and target.is_file() and hashlib.sha256(source.read_bytes()).digest()==hashlib.sha256(target.read_bytes()).digest():
            deployed.append({'path':'BepInEx/core/'+target.name,'packageAsset':relative,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
    if not deployed:continue
    record={'package':key,'nugetSha512':library.get('sha512'),'repository':repo.attrib.get('url') if repo is not None else None,
        'license':license_element.text if license_element is not None else None,'files':deployed}
    records.append(record)
    for relative in library['files']:
        path=folder/relative
        if path.is_file() and re.match(r'^(license|copying|thirdpartynotices)',path.name,re.I):
            destination=notices/'loader-dependency-licenses'/key.replace('/','-')/path.name
            destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,destination)
    if license_element is not None and license_element.attrib.get('type')=='file':
        path=folder/license_element.text
        if path.is_file():
            destination=notices/'loader-dependency-licenses'/key.replace('/','-')/path.name
            destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,destination)
notices.mkdir(parents=True,exist_ok=True)
(notices/'loader-dependencies.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
shutil.copyfile(root/'packages/runtime.source.json',notices/'runtime-source.json')
print('Recorded deployed loader dependency provenance:',len(records),'packages')
