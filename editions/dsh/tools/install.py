#!/usr/bin/env python3
"""Offline Unity/DSH integration with preflight, preservation and bounded rollback."""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def sha(data):
    return hashlib.sha256(data).hexdigest()

def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2)+'\n').encode('utf-8')

def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def atomic(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix='.aw-install-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data); stream.flush(); os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        Path(name).unlink(missing_ok=True)

def owned_path(base, relative):
    p = Path(relative)
    result = (base/p).resolve()
    if p.is_absolute() or not result.is_relative_to(base.resolve()):
        raise ValueError('UNSAFE_INSTALL_PATH: '+str(relative))
    return result

def plan(args, root=ROOT):
    root, project = root.resolve(), args.project.resolve()
    if not (project/'Assets').is_dir() or not (project/'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError('UNITY_PROJECT_REQUIRED: select the folder containing Assets and ProjectSettings')
    if not re.fullmatch(r'[A-Za-z0-9_.-]{1,80}', args.profile) or args.profile in ('.','..'):
        raise ValueError('INVALID_DSH_PROFILE_NAME')
    dsh_home = args.dsh_home.resolve()
    patch = dsh_home/'profiles'/args.profile/'cordis.patch.yml'
    if not patch.is_file():
        raise ValueError('DSH_PROFILE_NOT_FOUND: launch DSH and configure your own model; or pass --dsh-home/--profile')
    node = (args.node or root/'runtime/node/node.exe').resolve()
    unity_mcp = (args.unity_mcp or root/'runtime/unity-mcp/build/index.js').resolve()
    for p in [node, unity_mcp, root/'runtime/setup/node_modules/js-yaml/package.json']:
        if not p.is_file():
            raise ValueError('OFFLINE_RUNTIME_MISSING: extract the complete Win64 ZIP; missing '+str(p))
    files, conflicts = {}, []
    manifest = read(root/'unity/AVATAR_WORKBENCH_MANIFEST.json')
    target = project/'Assets/AvatarWorkbench'
    if not target.resolve().is_relative_to((project/'Assets').resolve()):
        raise ValueError('UNSAFE_INSTALL_PATH: AvatarWorkbench resolves outside this project Assets')
    for name, expected in manifest['payload_files'].items():
        src, dst = owned_path(root/'unity/AvatarWorkbench', name), owned_path(target, name)
        data = src.read_bytes()
        if sha(data) != expected:
            raise ValueError('WORKBENCH_PAYLOAD_CORRUPT: '+name)
        if dst.exists():
            if not dst.is_file():
                conflicts.append(str(dst)); continue
            if name.endswith('.meta') or sha(dst.read_bytes()) == expected:
                continue
            allowed = manifest['baseline_files'].get(name, [])
            if isinstance(allowed, str): allowed=[allowed]
            if sha(dst.read_bytes()) not in allowed:
                conflicts.append(str(dst)); continue
        files[dst] = data
    packages = project/'Packages'
    pm = read(packages/'manifest.json') if (packages/'manifest.json').is_file() else {}
    existing = []
    if 'com.claude.unity-mcp' in pm.get('dependencies', {}):
        existing.append('Packages/manifest.json:com.claude.unity-mcp')
    for base in (project/'Assets', packages):
        if base.is_dir():
            existing.extend(str(p.relative_to(project)) for p in base.rglob('UnityMCP.Editor.asmdef'))
    if not existing:
        src_root = root/'dependencies/UnityMCP-VRC/UnityMCPPlugin'
        dst_root = packages/'com.claude.unity-mcp'
        for src in sorted(src_root.rglob('*')):
            if not src.is_file(): continue
            dst = owned_path(dst_root, src.relative_to(src_root))
            if dst.exists() and dst.read_bytes()!=src.read_bytes(): conflicts.append(str(dst))
            elif not dst.exists(): files[dst]=src.read_bytes()
    if conflicts:
        raise ValueError('LOCAL_EDIT_CONFLICT: preserve and merge these files: '+', '.join(conflicts[:12]))
    storage = (args.storage_root or Path(os.environ.get('LOCALAPPDATA',str(Path.home())))/'AvatarWorkbench').resolve()
    editor = storage/'editor'/sha(str(project).encode())[:20]
    window = read(editor/'window.json') if (editor/'window.json').is_file() else {}
    binding = Path(args.binding or window.get('task_path') or editor/'selection.json')
    binding = (binding if binding.is_absolute() else project/binding).resolve()
    config_path = root/'local/connection.json'
    config = {'project':str(project),'binding':str(binding),'catalog_file':str(editor/'sources.json'),
              'storage_root':str(storage),'unity_mcp':str(unity_mcp),'dsh_home':str(dsh_home),
              'node':str(node),'python':str(Path(sys.executable).resolve()),'profile':args.profile,
              'package_root':str(root),'distribution_version':'0.3.0'}
    rows = [
      {'id':'avatar-workbench-skill','name':'@deepseek-ai/dsh-skill-filesystem','config':{
        'providerName':'avatar-workbench','includeDefaultRoots':False,'customSkillDirs':[str(root/'skills')],'watch':False}},
      {'id':'avatar-workbench-mcp','name':'@deepseek-ai/dsh-mcp-client','config':{
        'serverName':'avatarWorkbench','transport':'stdio','command':config['python'],
        'args':[str(root/'adapter/workbench_mcp.py'),'--config',str(config_path)],'cwd':str(root),
        'failOnStartupError':True,'reconnect':{'enabled':False}}},
      {'id':'avatar-workbench-unity-mcp','name':'@deepseek-ai/dsh-mcp-client','config':{
        'serverName':'unityMCP','transport':'stdio','command':str(node),'args':[str(unity_mcp)],
        'cwd':str(unity_mcp.parent),'toolCallTimeoutMs':300000,'failOnStartupError':True,'reconnect':{'enabled':False}}},
    ]
    original = patch.read_bytes().decode('utf-8')
    result = subprocess.run([str(node),str(root/'tools/config_patch.cjs')],
        input=json.dumps({'original':original,'rows':rows}),text=True,encoding='utf-8',capture_output=True,timeout=30)
    if result.returncode: raise ValueError('DSH_CONFIG_NOT_CHANGED: '+result.stderr.strip())
    files[patch] = json.loads(result.stdout)['updated'].encode('utf-8')
    files[config_path] = encoded(config)
    files[root/'local/dsh-overlay.yml'] = encoded([{'insert':rows}])
    files[root/'local/mcp-servers.json'] = encoded({'mcpServers':{
        r['config']['serverName']:{k:r['config'][k] for k in ('command','args','cwd')}
        for r in rows if r['name']=='@deepseek-ai/dsh-mcp-client'}})
    settings_path = editor/'model-services.json'
    settings = read(settings_path) if settings_path.is_file() else {
        'format':'avatar-workbench-model-services-v1','active':'dsh','vision':'','skill_path':'','profiles':[]}
    settings['dsh_skill_path'] = str(root/'skills/assemble-vrchat-avatar/SKILL.md')
    files[settings_path] = encoded(settings)
    files = {p:b for p,b in files.items() if not p.is_file() or p.read_bytes()!=b}
    report = {'status':'PREVIEW_ONLY','distribution':'0.3.0','workbench':manifest['version'],
        'project':str(project),'dsh_profile':str(patch),'writes':len(files),
        'unity_mcp':'EXISTING_REUSED' if existing else 'EMBEDDED_PACKAGE_WILL_BE_INSTALLED',
        'existing_unity_mcp':existing,'binding':str(binding),'model_and_permissions_preserved':True,
        'account_credentials_copied':False,'download_required':False,
        'next':'Save and close Unity/DSH before Apply. Reopen Unity and run Doctor after compilation.'}
    return files, report

def apply_files(files, report, root=ROOT):
    if not files: return dict(report,status='UNCHANGED',next='Open Unity and DSH; run Doctor.')
    lock = Path(report['project'])/'Temp/UnityLockfile'
    if lock.exists():
        try:
            with lock.open('r+b'): pass
        except PermissionError: raise ValueError('UNITY_IS_OPEN: save and close this Unity project before Apply')
    backup = root/'local/rollback'/datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup.mkdir(parents=True)
    old = {p:p.read_bytes() if p.exists() else None for p in files}
    records = []
    for index,(path,data) in enumerate(old.items()):
        if data is not None: (backup/str(index)).write_bytes(data)
        records.append({'path':str(path),'old_file':str(index) if data is not None else None,
          'old_sha256':sha(data) if data is not None else None,'installed_sha256':sha(files[path])})
    atomic(backup/'restore.json',encoded({'format':'avatar-workbench-install-rollback-v1','records':records}))
    written=[]
    try:
        for path,data in files.items():
            if (path.read_bytes() if path.exists() else None)!=old[path]:
                raise ValueError('FILE_CHANGED_DURING_INSTALL: '+str(path))
            atomic(path,data); written.append(path)
    except Exception:
        for path in reversed(written):
            if old[path] is None: path.unlink(missing_ok=True)
            else: atomic(path,old[path])
        raise
    receipt=dict(report,status='INSTALLED_NOT_UNITY_VERIFIED',rollback=str(backup),
        next='Open Unity, wait for compilation, run Doctor.cmd, then start a NEW DSH conversation.')
    atomic(root/'local/install-receipt.json',encoded(receipt))
    return receipt

def parser():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--project',required=True,type=Path)
    p.add_argument('--dsh-home',type=Path,default=Path(os.environ.get('DSH_HOME',str(Path.home()/'.dsh'))))
    p.add_argument('--profile',default='desktop')
    p.add_argument('--node',type=Path)
    p.add_argument('--unity-mcp',type=Path)
    p.add_argument('--binding',type=Path)
    p.add_argument('--storage-root',type=Path,help='Advanced/test-only: Workbench storage root')
    p.add_argument('--apply',action='store_true')
    return p

def main():
    args=parser().parse_args()
    files,report=plan(args)
    print(json.dumps(apply_files(files,report) if args.apply else report,ensure_ascii=False,indent=2))

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    try: main()
    except (ValueError,OSError,subprocess.SubprocessError) as error:
        print(str(error),file=sys.stderr); raise SystemExit(2)
