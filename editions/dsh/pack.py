#!/usr/bin/env python3
"""Allowlisted source/full export. Never includes user profiles, tasks or avatar assets."""
import argparse
import re
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parent
ALLOWED = {'adapter', 'skills', 'unity', 'dependencies', 'licenses', 'verification', 'tools', 'docs', 'examples', 'config'}
ROOT_FILES = {'README.md','START_HERE.md','开始使用.txt','AGENTS.md','DEEPSEEK_RULES.md','LICENSE',
              'THIRD_PARTY_NOTICES.md','MIGRATION.md','PREREQUISITES.md','DSH_CONNECT.md',
              'ONE_CLICK_DEPLOY_PROMPT.md','DSH_TASK_CARD.md','Setup.cmd','Setup.ps1','Doctor.cmd',
              'Verify-Package.cmd','pack.py'}
CHECK_FILES = {'test_workflow.py','test_notifications.py','test_distribution.py','test_version.py','test_finish_gate.py',
               'DISTRIBUTION_VERIFIED.md'}
SKIP_DIRS = {'local', 'runtime', 'node_modules', '__pycache__', '.git', 'Library', 'Temp', 'tests'}
SKIP_FILES = {'LOCAL_UPDATE.json', 'MANIFEST.sha256', 'LOCAL_CONTEXT.md', 'MANIFEST.json'}

def payload(full=False):
    paths = [ROOT/name for name in sorted(ROOT_FILES)]
    for name in sorted(ALLOWED):
        for folder, directories, filenames in os.walk(ROOT / name, followlinks=False):
            directories[:] = [d for d in directories if d not in SKIP_DIRS]
            paths.extend(Path(folder) / f for f in filenames)
    for path in sorted(paths):
        rel = path.relative_to(ROOT)
        if any(part in SKIP_DIRS for part in rel.parts) or path.name in SKIP_FILES:
            continue
        if rel.parts[0] == 'verification' and path.name not in CHECK_FILES:
            continue
        if rel.parts[0] == 'adapter' and path.name not in {'workbench_mcp.py','workflow_context.py','inbox_notifications.py'}:
            continue
        if rel.parts[:2] == ('dependencies','dsh-runtime'):
            continue
        if not path.is_file() or path.suffix.lower() in {'.pyc', '.png', '.jpg', '.jpeg', '.zstd', '.zip'}:
            continue
        if len(rel.parts)>1 and rel.parts[0] not in ALLOWED:
            continue
        yield path, rel
    if full:
        for component in ('node','python','unity-mcp','setup'):
            base=ROOT/'runtime'/component
            if not base.is_dir():
                raise ValueError('Run tools/prepare_runtime.py first: '+component)
            for folder, dirs, names in os.walk(base,followlinks=False):
                dirs[:]=[d for d in dirs if d not in {'.git','__pycache__'}]
                for name in sorted(names):
                    p=Path(folder)/name
                    if p.suffix=='.pyc' or p.is_symlink():
                        continue
                    if component=='unity-mcp' and not p.relative_to(base).parts[0] in {'build','node_modules','package.json','package-lock.json'}:
                        continue
                    yield p,p.relative_to(ROOT)
        p=ROOT/'runtime/PROVENANCE.json'
        yield p,p.relative_to(ROOT)

def main():
    out = ROOT.parent / 'deliverables'
    out.mkdir(exist_ok=True)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='AvatarWorkbench_DSH_Full_v0.3.5_Win64_20261101')
    parser.add_argument('--full',action='store_true',help='Include pinned portable Win64 runtimes and compiled MCP')
    args = parser.parse_args()
    version = args.version
    if not re.fullmatch(r'[A-Za-z0-9_.-]{1,100}', version) or version in {'.', '..'}:
        raise ValueError('Invalid package name')
    clean = out / version
    if clean.exists():
        raise ValueError('Clean export already exists; inspect before replacing.')
    clean.mkdir()
    manifest = {'format': 'avatar-workbench-dsh-full-v2' if args.full else 'avatar-workbench-dsh-source-v2',
                'created': datetime.now().astimezone().isoformat(timespec='seconds'), 'distribution_version':'0.3.5',
                'workflow': '0.3.2-local5-dsh', 'workbench': '0.2.15-dsh', 'adapter': '0.2.1',
                'platform':'win32-x64' if args.full else 'source', 'dsh_desktop_included':False,
                'model_assembly_acceptance':'See docs/EXAMPLES.md; historical supervised case, not a new-user guarantee',
                'excluded': ['private local configuration', 'credentials', 'sessions', 'task and feedback data',
                             'private images', 'paid models', 'Library/Temp'], 'files': {}}
    for source, rel in payload(args.full):
        data = source.read_bytes()
        public_npmrc = rel.as_posix() == 'runtime/node/node_modules/npm/.npmrc'
        if public_npmrc and re.search(rb'(?:_auth|token|password)\s*=', data, re.I):
            raise ValueError('CREDENTIAL_IN_NPM_DEFAULTS')
        if rel.parts[0] in {'local','Library','Temp'} or source.name == '.credentials.yaml' or (source.name=='.npmrc' and not public_npmrc):
            raise ValueError('PRIVATE_PAYLOAD_FORBIDDEN: '+str(rel))
        dest = clean / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
        manifest['files'][rel.as_posix()] = hashlib.sha256(data).hexdigest()
    inventory=[]
    if args.full:
        for p in sorted((clean/'runtime').rglob('package.json')):
            if 'node_modules' not in p.parts:
                continue
            item=json.loads(p.read_text(encoding='utf-8-sig'))
            if not item.get('name') or not item.get('version'):
                continue
            inventory.append({'path':p.parent.relative_to(clean).as_posix(),'name':item['name'],
                              'version':item['version'],'license':item.get('license'),'repository':item.get('repository')})
    inv=clean/'DEPENDENCY_INVENTORY.json'
    inv.write_text(json.dumps({'packages':inventory},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    manifest['files'][inv.name]=hashlib.sha256(inv.read_bytes()).hexdigest()
    wm=json.loads((clean/'unity/AVATAR_WORKBENCH_MANIFEST.json').read_text(encoding='utf-8'))
    for name,expected in wm['payload_files'].items():
        actual=clean/'unity/AvatarWorkbench'/name
        if not actual.is_file() or hashlib.sha256(actual.read_bytes()).hexdigest()!=expected:
            raise ValueError('WORKBENCH_MANIFEST_MISMATCH: '+name)
    (clean / 'MANIFEST.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    archive = out / (version + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
        for path in sorted(clean.rglob('*')):
            if path.is_file():
                zipped.write(path, path.relative_to(clean).as_posix())
    with zipfile.ZipFile(archive) as zipped:
        if zipped.testzip():
            raise ValueError('ZIP CRC failed')
        for name, expected in manifest['files'].items():
            if hashlib.sha256(zipped.read(name)).hexdigest()!=expected:
                raise ValueError('Hash mismatch: '+name)
    sha = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix('.zip.sha256').write_text(sha+'  '+archive.name+'\n', encoding='utf-8')
    print(json.dumps({'source': str(clean), 'zip': str(archive), 'bytes': archive.stat().st_size, 'sha256': sha, 'zip_integrity': 'passed'}, ensure_ascii=False, indent=2))

if __name__=='__main__':
    import sys
    sys.stdout.reconfigure(encoding='utf-8')
    main()
