#!/usr/bin/env python3
"""Offline, project-scoped Codex integration. Preview by default; --apply writes."""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import tomllib

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.1.0'
BEGIN = '# BEGIN AvatarWorkbench Codex managed MCP\n'
END = '# END AvatarWorkbench Codex managed MCP\n'

def sha(data):
    return hashlib.sha256(data).hexdigest()

def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2)+'\n').encode('utf-8')

def read(path):
    try:
        value = json.loads(Path(path).read_text(encoding='utf-8-sig'))
        if not isinstance(value, dict): raise ValueError()
        return value
    except (ValueError, UnicodeError):
        raise ValueError('INVALID_JSON_OBJECT: '+str(path)) from None

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
    base = Path(base).resolve()
    rel = Path(relative)
    result = (base/rel).resolve()
    if rel.is_absolute() or result == base or not result.is_relative_to(base):
        raise ValueError('UNSAFE_INSTALL_PATH: '+str(relative))
    return result

def parse_toml(data, path):
    try:
        return tomllib.loads(data.decode('utf-8-sig'))
    except (ValueError, UnicodeError):
        # Do not echo a private TOML line (it can contain an API credential).
        raise ValueError('INVALID_TOML: '+str(path)) from None

def split_managed(data, path):
    text = data.decode('utf-8-sig')
    begin, end = BEGIN.strip(), END.strip()
    if begin not in text and end not in text: return data, b''
    lines = text.splitlines(keepends=True)
    starts = [i for i,l in enumerate(lines) if l.strip() == begin]
    ends = [i for i,l in enumerate(lines) if l.strip() == end]
    if len(starts)!=1 or len(ends)!=1 or ends[0]<starts[0]:
        raise ValueError('MANAGED_MARKERS_INVALID: '+str(path))
    a,b = starts[0],ends[0]+1
    # Retain original CRLF and all unrelated comments/bytes.
    prefix = b'\xef\xbb\xbf' if data.startswith(b'\xef\xbb\xbf') else b''
    return prefix+(''.join(lines[:a]+lines[b:])).encode('utf-8'), ''.join(lines[a:b]).encode('utf-8')

def mcp_kind(name, row):
    args = row.get('args', []) if isinstance(row,dict) else []
    text = ' '.join(str(x) for x in args).replace('\\','/').lower()
    norm = re.sub('[^a-z]', '', name.lower())
    if norm in ('avatarworkbench','avatarworkbenchmcp') or 'workbench_mcp.py' in text: return 'avatarWorkbench'
    if norm in ('unitymcp','unity') or ('unity-mcp' in text and ('index.js' in text or 'unity-mcp' in str(row.get('command','')))): return 'unityMCP'
    return None

def compatible(row, wanted, kind):
    if not isinstance(row,dict) or row.get('enabled') is False or row.get('url'): return False
    if row.get('env') or row.get('env_vars') or row.get('enabled_tools') or row.get('disabled_tools'): return False
    command = Path(row.get('command',''))
    args = row.get('args',[])
    if not command.is_absolute() or not command.is_file() or not isinstance(args,list): return False
    if kind == 'avatarWorkbench':
        return str(command.resolve()).lower()==wanted['command'].lower() and args==wanted['args']
    # Reuse a local UnityMCP installation only with the same compiled server bytes.
    if len(args)!=1 or command.name.lower()!='node.exe': return False
    entry=Path(args[0])
    return entry.is_absolute() and entry.is_file() and sha(entry.read_bytes())==sha(Path(wanted['args'][0]).read_bytes())

def project_folders(project):
    """Repo-scoped ancestors; a standalone Unity project does not inherit the drive root."""
    folders=[project,*project.parents]
    for index,folder in enumerate(folders):
        if (folder/'.git').exists(): return folders[:index+1]
    return [project]

# Another Unity MCP the user already runs (often a URL server) keeps its name; ours is added beside it
# under this name in the project config, so the two tables never merge and global config stays untouched.
SIDE_BY_SIDE_UNITY_MCP='unityMCPVRC'

def configure_mcp(project, codex_home, wanted):
    config_path=owned_path(project,'.codex/config.toml')
    original=config_path.read_bytes() if config_path.is_file() else b''
    parse_toml(original,config_path)
    unmanaged, managed=split_managed(original,config_path)
    if managed:
        previous=parse_toml(managed,config_path).get('mcp_servers',{})
        if set(previous)-set(wanted)-{SIDE_BY_SIDE_UNITY_MCP}: raise ValueError('MANAGED_MCP_EDITED: '+str(config_path))
        # An existing managed block may only be replaced if it was recorded by this installer.
        receipt=project/'.avatar-workbench-codex/installation.json'
        recorded=read(receipt).get('managed_mcp_sha256') if receipt.is_file() else None
        if recorded!=sha(managed): raise ValueError('MANAGED_MCP_EDITED: '+str(config_path))
    sources=[(config_path,parse_toml(unmanaged,config_path))]
    known={config_path}
    for folder in project_folders(project)[1:]:
        p=(folder/'.codex/config.toml').resolve()
        if p not in known and p.is_file(): sources.append((p,parse_toml(p.read_bytes(),p))); known.add(p)
    global_config=codex_home/'config.toml'
    if global_config not in known and global_config.is_file():
        sources.append((global_config,parse_toml(global_config.read_bytes(),global_config)))
    reused={}; added={}; effective={}
    for kind,row in wanted.items():
        matches=[]
        for path,config in sources:
            servers=config.get('mcp_servers',{})
            if not isinstance(servers,dict): raise ValueError('MCP_TABLE_REQUIRED: '+str(path))
            for name,existing in servers.items():
                if mcp_kind(name,existing)==kind: matches.append((path,name,existing))
        if len(matches)>1: raise ValueError('DUPLICATE_MCP: '+kind+'; resolve existing registrations before installation')
        if matches:
            path,name,existing=matches[0]
            if not compatible(existing,row,kind):
                taken=any(SIDE_BY_SIDE_UNITY_MCP in config.get('mcp_servers',{}) for _,config in sources)
                if kind!='unityMCP' or taken:
                    raise ValueError('MCP_CONFLICT: '+kind+' in '+str(path)+'; existing configuration was preserved')
                added[SIDE_BY_SIDE_UNITY_MCP]=row
                effective[kind]=dict(row,name=SIDE_BY_SIDE_UNITY_MCP,config_source=str(config_path),
                                     coexists_with=name+' in '+str(path))
                continue
            reused[kind]={'name':name,'config':str(path)}
            effective[kind]={'name':name,'command':existing['command'],'args':existing['args'],
                             'cwd':existing.get('cwd'),'config_source':str(path)}
        else:
            added[kind]=row; effective[kind]=dict(row,name=kind,config_source=str(config_path))
    block=''
    if added:
        block=BEGIN
        for name,row in added.items():
            block+='[mcp_servers.'+name+']\n'
            for key,value in row.items(): block+=key+' = '+json.dumps(value,ensure_ascii=False)+'\n'
            block+='startup_timeout_sec = 30\ntool_timeout_sec = 300\n\n'
        block+=END
    updated=unmanaged
    if block:
        if updated and not updated.endswith(b'\n'): updated+=b'\n'
        updated+=block.encode('utf-8')
    parse_toml(updated,config_path)
    return config_path,updated,sha(block.encode()) if block else None,reused,effective

def skill_version(folder):
    try: return str(json.loads((folder/'VERSION.json').read_text(encoding='utf-8-sig')).get('version',''))
    except (OSError,ValueError,AttributeError): return ''

def skill_gaps(skill, root):
    """Files the packaged Skill has that a reused one lacks. The reused Skill itself is never changed."""
    packaged=root/'skills'/'assemble-vrchat-avatar'
    if skill.parent.resolve()==packaged.resolve(): return []
    return sorted(p.relative_to(packaged).as_posix() for p in packaged.rglob('*')
                  if p.is_file() and '__pycache__' not in p.parts and p.suffix!='.pyc' and not (skill.parent/p.relative_to(packaged)).is_file())

def plan(args, root=ROOT):
    root,project=root.resolve(),args.project.resolve()
    if not (project/'Assets').is_dir() or not (project/'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError('UNITY_PROJECT_REQUIRED: select the folder containing Assets and ProjectSettings')
    for folder in ('Assets','Packages','.codex','.agents','.avatar-workbench-codex'):
        if not (project/folder).resolve().is_relative_to(project): raise ValueError('PROJECT_FOLDER_ESCAPES: '+folder)
    node=(args.node or root/'runtime/node/node.exe').resolve()
    python=(root/'runtime/python/python.exe').resolve()
    unity_mcp=(args.unity_mcp or root/'runtime/unity-mcp/build/index.js').resolve()
    for p in (node,python,unity_mcp,root/'adapter/workbench_mcp.py'):
        if not p.is_file(): raise ValueError('OFFLINE_RUNTIME_MISSING: '+str(p))
    files={}; conflicts=[]
    manifest=read(root/'unity/AVATAR_WORKBENCH_MANIFEST.json')
    target=owned_path(project,'Assets/AvatarWorkbench')
    for name,expected in manifest['payload_files'].items():
        src,dst=owned_path(root/'unity/AvatarWorkbench',name),owned_path(target,name)
        data=src.read_bytes()
        if sha(data)!=expected: raise ValueError('WORKBENCH_PAYLOAD_CORRUPT: '+name)
        if dst.exists():
            if not dst.is_file(): conflicts.append(str(dst)); continue
            old=sha(dst.read_bytes())
            if name.endswith('.meta') or old==expected: continue
            baseline=manifest['baseline_files'].get(name,[])
            if old not in ([baseline] if isinstance(baseline,str) else baseline): conflicts.append(str(dst)); continue
        files[dst]=data
    pm=read(project/'Packages/manifest.json') if (project/'Packages/manifest.json').is_file() else {}
    existing=[]
    if 'com.claude.unity-mcp' in pm.get('dependencies',{}): existing.append('manifest dependency')
    for folder in (project/'Assets',project/'Packages'):
        if folder.is_dir(): existing.extend(str(p.relative_to(project)) for p in folder.rglob('UnityMCP.Editor.asmdef'))
    packages_to_copy=[]
    if not existing: packages_to_copy.append(('UnityMCP-VRC/UnityMCPPlugin','com.claude.unity-mcp'))
    json_present='com.unity.nuget.newtonsoft-json' in pm.get('dependencies',{}) or (project/'Packages/com.unity.nuget.newtonsoft-json/package.json').is_file()
    if not json_present: packages_to_copy.append(('com.unity.nuget.newtonsoft-json','com.unity.nuget.newtonsoft-json'))
    for source,name in packages_to_copy:
        src_root=root/'dependencies'/source
        if not (src_root/'package.json').is_file(): raise ValueError('UNITY_DEPENDENCY_MISSING: '+name)
        for src in sorted(src_root.rglob('*')):
            if not src.is_file() or '__pycache__' in src.parts: continue
            dst=owned_path(project,Path('Packages')/name/src.relative_to(src_root))
            if dst.exists() and dst.read_bytes()!=src.read_bytes(): conflicts.append(str(dst))
            elif not dst.exists(): files[dst]=src.read_bytes()
    if conflicts: raise ValueError('LOCAL_EDIT_CONFLICT: '+', '.join(conflicts[:8]))
    codex_home=args.codex_home.resolve()
    skill_name='assemble-vrchat-avatar'
    skill_target=owned_path(project,'.agents/skills/'+skill_name)
    skill_candidates=[]
    if args.skill: skill_candidates=[args.skill.resolve()]
    else:
        for p in [skill_target/'SKILL.md',*[x/'.agents/skills'/skill_name/'SKILL.md' for x in project_folders(project)[1:]],
                  args.user_skills/skill_name/'SKILL.md',codex_home/'skills'/skill_name/'SKILL.md']:
            if p.is_file() and p.resolve() not in skill_candidates: skill_candidates.append(p.resolve())
    if len(skill_candidates)>1: raise ValueError('DUPLICATE_SKILL: select the intended existing SKILL.md with --skill')
    skill=skill_candidates[0] if skill_candidates else skill_target/'SKILL.md'
    if skill_candidates:
        content=skill.read_text(encoding='utf-8-sig')
        if 'name: assemble-vrchat-avatar' not in content or len(content.encode())>32768:
            raise ValueError('INCOMPATIBLE_SKILL: '+str(skill))
        if 'optimization.md' not in content or not (skill.parent/'references/optimization.md').is_file() or not (skill.parent/'scripts/optimization.py').is_file():
            raise ValueError('WORKBENCH_SKILL_HELPERS_MISSING: '+str(skill)+'; preserve the existing workflow and select a complete compatible Skill')
    else:
        for src in sorted((root/'skills'/skill_name).rglob('*')):
            if src.is_file() and '__pycache__' not in src.parts and src.suffix!='.pyc':
                dst=owned_path(project,Path('.agents/skills')/skill_name/src.relative_to(root/'skills'/skill_name))
                if dst.exists() and dst.read_bytes()!=src.read_bytes(): raise ValueError('LOCAL_EDIT_CONFLICT: '+str(dst))
                files[dst]=src.read_bytes()
    # Match C# Path.GetFullPath identity, not Windows MSIX/junction physical redirection.
    storage=Path(os.path.abspath(args.storage_root or Path(os.environ.get('LOCALAPPDATA',str(Path.home())))/'AvatarWorkbench'))
    editor=storage/'editor'/sha(str(project).encode())[:20]
    window=read(editor/'window.json') if (editor/'window.json').is_file() else {}
    binding=Path(args.binding or window.get('task_path') or editor/'selection.json')
    binding=Path(os.path.abspath(binding if binding.is_absolute() else project/binding))
    config_path=owned_path(project,'.avatar-workbench-codex/connection.json')
    wanted={
        'avatarWorkbench':{'command':str(python),'args':[str(root/'adapter/workbench_mcp.py'),'--config',str(config_path)],'cwd':str(root)},
        'unityMCP':{'command':str(node),'args':[str(unity_mcp)],'cwd':str(unity_mcp.parent)}}
    toml_path,toml_data,managed_hash,reused,effective=configure_mcp(project,codex_home,wanted)
    if toml_data or toml_path.exists(): files[toml_path]=toml_data
    config={'format':'avatar-workbench-codex-connection-v1','project':str(project),'binding':str(binding),
            'catalog_file':str(editor/'sources.json'),'storage_root':str(storage),'skill_path':str(skill),
            'package_root':str(root),'distribution_version':VERSION,'node':str(node),'python':str(python),
            'unity_mcp':str(unity_mcp),'mcp_servers':effective,'codex_config':str(toml_path),
            'session_binding':'USER_CHAT_REQUIRED'}
    files[config_path]=encoded(config)
    settings_path=editor/'model-services.json'
    settings=read(settings_path) if settings_path.is_file() else {'format':'avatar-workbench-model-services-v1',
           'active':'codex','vision':'','profiles':[]}
    settings['skill_path']=str(skill)
    files[settings_path]=encoded(settings)
    state_path=owned_path(project,'.avatar-workbench-codex/installation.json')
    files[state_path]=encoded({'format':'avatar-workbench-codex-installation-v1','distribution_version':VERSION,
        'project':str(project),'package_root':str(root),'managed_mcp_sha256':managed_hash,'skill_path':str(skill)})
    pointer=root/'local/last-install.json'
    files[pointer]=encoded({'project':str(project),'config':str(config_path)})
    files={p:b for p,b in files.items() if not p.is_file() or p.read_bytes()!=b}
    report={'status':'PREVIEW_ONLY','distribution':VERSION,'workbench':manifest['version'],'project':str(project),
        'writes':len(files),'config':str(config_path),'codex_config':str(toml_path),'skill':str(skill),
        'skill_mode':'EXISTING_REUSED' if skill_candidates else 'PROJECT_INSTALL',
        'skill_version':skill_version(skill.parent),'packaged_skill_version':skill_version(root/'skills'/skill_name),
        'reused_mcp':reused,'unity_mcp':'EXISTING_REUSED' if existing else 'EMBEDDED_INSTALL',
        'model_and_permissions_preserved':True,'credentials_copied':False,'avatar_modified':False,
        'next':'Open this project in Unity and Codex. Trust it in Codex if asked. Run Doctor after Unity compilation.'}
    # A reused Skill can predate files the workbench instructions rely on; say so instead of silently
    # handing Codex an older workflow. The handoff names the Skill and the template by exact path.
    missing=skill_gaps(skill,root) if skill_candidates else []
    if missing:
        report['skill_missing_files']=missing
        report['skill_note']=('复用了已有的 Skill（'+(report['skill_version'] or '版本未知')+'），它缺少插件自带版本（'+report['packaged_skill_version']+
            '）里的 '+str(len(missing))+' 个文件，例如 '+'、'.join(sorted(missing,key=lambda p:(p!='references/finish-check.md',p))[:3])+'；插件不会改动它。收尾检查模板随插件装在工程的 '
            'Assets/AvatarWorkbench/Tools/finish-check.md，接手指令会写出 Skill 和模板的确切路径。想改用插件自带的 Skill，重新安装时加 --skill "'+
            str(root/'skills'/skill_name/'SKILL.md')+'"。')
    return files,report

def apply_files(files,report,root=ROOT):
    if not files: return dict(report,status='UNCHANGED')
    project=Path(report['project'])
    lock=project/'Temp/UnityLockfile'
    if lock.exists():
        try:
            with lock.open('r+b'): pass
        except OSError: raise ValueError('UNITY_IS_OPEN: save and close this project before installation') from None
    backup=owned_path(project,'.avatar-workbench-codex/rollback/'+datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
    backup.mkdir(parents=True)
    receipt=dict(report,status='INSTALLED_NOT_UNITY_VERIFIED',rollback=str(backup/'restore.json'))
    files=dict(files)
    files[project/'.avatar-workbench-codex/install-receipt.json']=encoded(receipt)
    old={p:p.read_bytes() if p.exists() else None for p in files}
    records=[]
    for i,(p,data) in enumerate(old.items()):
        if data is not None: (backup/str(i)).write_bytes(data)
        records.append({'path':str(p),'old_file':str(i) if data is not None else None,
                        'old_sha256':sha(data) if data is not None else None,'installed_sha256':sha(files[p])})
    atomic(backup/'restore.json',encoded({'format':'avatar-workbench-install-rollback-v1','project':str(project),'records':records}))
    written=[]
    try:
        for p,data in files.items():
            if (p.read_bytes() if p.exists() else None)!=old[p]: raise ValueError('FILE_CHANGED_DURING_INSTALL: '+str(p))
            atomic(p,data); written.append(p)
    except Exception:
        for p in reversed(written):
            if old[p] is None: p.unlink(missing_ok=True)
            else: atomic(p,old[p])
        raise
    return receipt

def parser():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--project',type=Path,required=True)
    p.add_argument('--codex-home',type=Path,default=Path(os.environ.get('CODEX_HOME',str(Path.home()/'.codex'))))
    p.add_argument('--user-skills',type=Path,default=Path.home()/'.agents/skills')
    p.add_argument('--skill',type=Path,help='Reuse an explicitly selected existing Skill; never overwrites it')
    p.add_argument('--node',type=Path)
    p.add_argument('--unity-mcp',type=Path)
    p.add_argument('--storage-root',type=Path)
    p.add_argument('--binding',type=Path)
    p.add_argument('--apply',action='store_true')
    return p

def main():
    args=parser().parse_args()
    files,report=plan(args)
    print(json.dumps(apply_files(files,report) if args.apply else report,ensure_ascii=False,indent=2))

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8'); sys.stderr.reconfigure(encoding='utf-8')
    try: main()
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr); raise SystemExit(2)
