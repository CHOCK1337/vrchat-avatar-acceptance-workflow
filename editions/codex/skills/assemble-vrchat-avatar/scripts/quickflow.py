#!/usr/bin/env python3
"""Small offline helpers for routine avatar editing; no Unity/model/network calls.

Records are supplied by the caller. This program cannot authenticate user consent,
Unity state, image contents, or visual correctness. It never approves an avatar.
Use only when the helper replaces repeated manual work, not as a new ritual.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile
from typing import Any
import optimization

MAX_JSON = 2 * 1024 * 1024
KINDS = {'outfit', 'accessory', 'plugin', 'material', 'behavior'}
LEGACY = {'start_contract', 'assembly_manifest', 'workflow_entry', 'run_choice'}
MEASUREMENTS = {'sdk_uncompressed_bytes', 'sdk_download_bytes', 'rank', 'metrics'}
KEY_FIELDS = ('project', 'target', 'check', 'dependency_digest', 'state', 'view',
              'pipeline_digest', 'check_version')


def read_json(path: Path) -> Any:
    if path.stat().st_size > MAX_JSON:
        raise ValueError('Use the current task slice, not a whole transcript (>2 MiB)')
    return json.loads(path.read_text(encoding='utf-8-sig'))


def hash_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda: f.read(65536), b''):
            h.update(chunk)
    return h.hexdigest()


def _canonical(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False)


def _no_legacy(value: dict) -> None:
    if LEGACY.intersection(value):
        raise ValueError('LEGACY_TASK: preserve the original task and its explicit constraints; '
                         'do not convert it implicitly or invent completed evidence. See MIGRATION.md.')


def plan(request: dict) -> dict:
    """Suggest affected observations, never evaluate their success or infer consent."""
    if not isinstance(request, dict):
        raise ValueError('Expected a task object')
    _no_legacy(request)
    delivery = request.get('delivery', 'local')
    if delivery not in {'local', 'upload_private'}:
        raise ValueError('delivery must be local or upload_private; authorisation comes from the user')
    resources = request.get('resources')
    if not isinstance(resources, list) or not resources:
        raise ValueError('Provide the actual selected resources, not the whole project')
    checks = [{'id':'final_view', 'purpose':'View the final normal-material default appearance',
               'scope':'current candidate', 'when':'after this requested edit/batch'}]
    conflicts, ids = [], set()
    requested = request.get('requested_method')
    if requested is not None and not isinstance(requested, str):
        raise ValueError('requested_method is a user instruction or null')
    for r in resources:
        if not isinstance(r, dict) or r.get('kind') not in KINDS:
            raise ValueError('Resolve unknown resource kind from the actual asset before planning')
        rid, kind = r.get('id'), r['kind']
        if not isinstance(rid, str) or not re.fullmatch(r'[\w.-]{1,100}', rid) or rid in ids:
            raise ValueError('Each resource needs a unique simple identifier')
        ids.add(rid)
        def add(suffix: str, purpose: str, **detail: Any) -> None:
            checks.append({'id':rid+'/'+suffix, 'resource':rid, 'purpose':purpose, **detail})
        if kind == 'material':
            add('material','View the changed slot in normal lighting; verify other consumers were not unintentionally recolored')
        elif kind == 'accessory':
            add('placement','View the visible attachment/contact point and follow its target through one normal motion')
            if r.get('has_control'):
                add('control','Operate the actual author control once; observe the resulting attachment state')
        elif kind == 'plugin':
            add('feature','Exercise the requested entry/trigger and actual visible effect once; note unavailable runtime layers')
        elif kind == 'behavior':
            add('restore','Use real input: on -> off -> on; observe the body, clothing and still-active layers')
        else:
            add('appearance','View complete outfit and body in normal materials; inspect a relevant closeup for missing/floating parts')
            add('control','Use the author menu/control: on -> off -> on; compare with the agreed default')
            region = r.get('region','full')
            poses = {'upper':['arms_up','forward_bend'], 'lower':['sit','squat'],
                     'feet':['author_foot_pose','squat'], 'full':['arms_up','sit','squat']}
            if region not in poses:
                raise ValueError('region must be upper/lower/feet/full')
            add('motion','Actually apply relevant representative poses; color blocks diagnose only covered regions',
                states=poses[region])
            states = r.get('required_states')
            if states is not None and (not isinstance(states,list) or not states or
                                       not all(isinstance(v,str) and v for v in states)):
                raise ValueError('required_states must preserve a nonempty requested list')
            if states or r.get('body_controls'):
                add('body_controls','Inspect the affected enabled controls in the requested range; not a Cartesian product',
                    states=deepcopy(states) if states else ['current','enabled_range_endpoints'],
                    controls=deepcopy(r.get('body_controls',[])))
            if r.get('parts'):
                add('part_restore','Use each changed part control and one relevant return sequence; observe restoration',
                    parts=deepcopy(r['parts']))
        requirement = r.get('requested_method', requested)
        suggested = r.get('suggested_route')
        if requirement and requirement.lower() != 'auto' and suggested:
            req = requirement.lower().replace(' ', '')
            route = str(suggested).lower().replace(' ', '')
            equivalent = req == route or (req in {'ma','modularavatar'} and route == 'ma_native')
            if not equivalent:
                conflicts.append({'resource':rid, 'requested':requirement, 'suggested':suggested,
                                  'next':'explain_evidence_and_ask_once'})
    constraints = request.get('constraints',{})
    if not isinstance(constraints,dict):
        raise ValueError('constraints must contain explicit task requirements')
    measurements=[]
    for metric, limit in constraints.items():
        if metric not in MEASUREMENTS:
            continue
        if metric.endswith('_bytes') and (type(limit) is not int or limit <= 0):
            raise ValueError('Explicit byte limits must be positive integers')
        if metric == 'rank' and limit not in {'Excellent','Good','Medium','Poor','VeryPoor'}:
            raise ValueError('Keep the user rank requirement precise')
        if metric == 'metrics' and not isinstance(limit,dict):
            raise ValueError('metrics must be a metric-to-limit mapping')
        measurements.append({'metric':metric, 'requirement':deepcopy(limit),
                             'when':'final candidate; reuse the build-for-upload when identical'})
    optimization_stage = optimization.plan(request)
    return {'scope':'suggested task checks, not execution or acceptance', 'checks':checks,
            'optimization':optimization_stage,
            'constraints':deepcopy(constraints), 'manual_constraints':sorted(set(constraints)-MEASUREMENTS),
            'method_conflicts':conflicts, 'measurements':measurements,
            'baseline_build':False, 'build_and_test':False, 'extra_agents':False,
            'sdk_build_for_upload':delivery=='upload_private' and constraints.get('allow_sdk_build', True), 'upload_authorized_by_helper':False,
            'note':'Do not run repeated generic checks. Keep all explicit requirements even when not parsed here.'}


def observation_key(probe: dict) -> str:
    if not isinstance(probe, dict) or any(probe.get(k) in (None,'',{},[]) for k in KEY_FIELDS):
        raise ValueError('A reusable check needs target, state, complete relevant dependency and preview identities')
    body={k:probe[k] for k in KEY_FIELDS}
    return hashlib.sha256(_canonical(body).encode('utf-8')).hexdigest()


def reuse(probe: dict, history: list, user_reported_issue: bool = False) -> dict:
    """Only reuse an explicitly recorded observation. No pixel/geometry judgement."""
    base={'visual_verified_by_script':False, 'scope':'record reuse only'}
    if user_reported_issue:
        return {**base,'action':'DIAGNOSE_NOT_REPEAT','reason':'New user defect supersedes a prior recorded OK; inspect the first divergence'}
    key=observation_key(probe)
    if not isinstance(history,list):
        raise ValueError('history must be ordered oldest -> newest')
    record=next((r for r in reversed(history) if isinstance(r,dict) and r.get('key')==key), None)
    if not record:
        return {**base,'action':'OBSERVE','key':key,'reason':'No observation for these relevant inputs; first inspection is allowed without an edit'}
    if record.get('outcome')=='observed_problem':
        return {**base,'action':'DIAGNOSE_NOT_REPEAT','key':key,'reason':'The newest matching result is a known problem; do not fall back to an older OK or recapture unchanged state'}
    if record.get('outcome')!='observed_ok' or not record.get('observer') or not record.get('note'):
        return {**base,'action':'OBSERVE','key':key,'reason':'Numbers or empty records are not visual observations'}
    evidence=record.get('evidence')
    if not isinstance(evidence,list) or not evidence:
        return {**base,'action':'OBSERVE','key':key,'reason':'No original evidence files were recorded'}
    for e in evidence:
        try:
            if not isinstance(e,dict) or not e.get('path') or not e.get('sha256'):
                raise ValueError('missing evidence identity')
            if hash_file(Path(e['path']))!=e['sha256']:
                raise ValueError('changed evidence')
        except (OSError,ValueError,TypeError):
            return {**base,'action':'OBSERVE','key':key,'reason':'Original evidence missing/changed; inspect only the affected check'}
    return {**base,'action':'REUSE_SCOPED_RECORD','key':key,
            'reason':'Matching relevant inputs and existing evidence; this does not approve the whole/new candidate',
            'evidence':evidence}


def receipt(value: Any) -> dict:
    """Summarize explicit nested execution signals. Outer wrapper success is weak."""
    failed=[]; pending=False; executed=False; nodes=0
    def walk(obj: Any, depth: int = 0) -> None:
        nonlocal pending,executed,nodes
        nodes+=1
        if depth>12 or nodes>10000:
            return
        if isinstance(obj,str):
            text=obj.strip()
            if re.search(r'(?mi)^\s*(?:STATUS|RESULT)\s*=\s*(?:FAILED|FAILURE|ERROR)\s*$',text):
                failed.append('explicit FAILED terminal line')
            if text.startswith(('{','[')):
                try:walk(json.loads(text),depth+1)
                except (ValueError,RecursionError):pass
        elif isinstance(obj,list):
            for item in obj:walk(item,depth+1)
        elif isinstance(obj,dict):
            for name,v in obj.items():
                key=name.lower().replace('_','')
                if key in {'executionsuccess','succeeded','iserror'}:
                    if key=='iserror' and v is True:
                        failed.append('isError=true')
                    elif key!='iserror' and v is False:
                        failed.append(name+'=false')
                    elif key!='iserror' and v is True:
                        executed=True
                if key in {'exitcode','returncode'} and isinstance(v,int) and v!=0:
                    failed.append(name+'='+str(v))
                if key in {'status','state','executionstatus'} and isinstance(v,str):
                    s=v.lower()
                    if s in {'failed','failure','error','cancelled','canceled','aborted'}:
                        failed.append(name+'='+s)
                    if s in {'running','pending','queued','inprogress','in_progress','building'}:
                        pending=True
                    if s=='succeeded' or key=='executionstatus' and s in {'success','completed'}:
                        executed=True
                if key in {'error','exception'} and v not in (None,False,'',[],{}):
                    failed.append(str(v)[:300])
                walk(v,depth+1)
    walk(value)
    state='FAILED' if failed else 'PENDING' if pending else 'SUCCEEDED' if executed else 'UNKNOWN'
    return {'execution':state, 'visual_verified':False, 'errors':list(dict.fromkeys(failed))[:4],
            'next':{'FAILED':'inspect_actual_artifact_before_retry','PENDING':'wait_existing_job',
                    'SUCCEEDED':'inspect_affected_result','UNKNOWN':'read_terminal_receipt_once_do_not_redispatch'}[state]}


def save_notes(path: Path, patch: dict, expected: int) -> dict:
    """Atomic top-level patch, no schema/evidence gates or recursive full-task rewrites."""
    if not isinstance(patch,dict) or type(expected) is not int or expected < 0:
        raise ValueError('Expected an object patch and nonnegative expected revision')
    _no_legacy(patch)
    path=Path(path).expanduser().absolute()
    if any(p.is_symlink() for p in [path,*path.parents]):
        raise ValueError('Symlink notes path refused')
    parts={p.lower() for p in path.parts}
    if '.avatar-assembly' not in parts or parts.intersection({'assets','packages','library','temp'}):
        raise ValueError('Keep notes in project/.avatar-assembly outside Unity asset/cache directories')
    path.parent.mkdir(parents=True,exist_ok=True)
    lock=path.with_suffix(path.suffix+'.lock')
    try:
        fd=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
    except FileExistsError as e:
        raise ValueError('Notes writer already active; inspect once rather than deleting the lock blindly') from e
    temporary=None
    try:
        with os.fdopen(fd,'w',encoding='utf-8') as f:f.write(str(os.getpid()))
        old=read_json(path) if path.exists() else {}
        if not isinstance(old,dict):raise ValueError('Existing notes are not an object')
        _no_legacy(old)
        if old.get('task_revision',0)!=expected:
            raise ValueError('Stale task revision; reread this small file')
        new={**old, **deepcopy(patch)}
        # A small update must not accidentally drop an existing explicit budget.
        if isinstance(old.get('constraints'),dict) and 'constraints' in patch:
            if not isinstance(patch['constraints'],dict):
                raise ValueError('Do not erase explicit constraints with null/non-object')
            new['constraints']={**old['constraints'], **deepcopy(patch['constraints'])}
            if isinstance(old['constraints'].get('metrics'),dict) and isinstance(patch['constraints'].get('metrics'),dict):
                new['constraints']['metrics']={**old['constraints']['metrics'], **patch['constraints']['metrics']}
        for key in ('task_id','project_path'):
            if not isinstance(new.get(key),str) or not new[key].strip():
                raise ValueError(key+' is required on first write')
            if old.get(key) and new[key]!=old[key]:
                raise ValueError('Do not replace an existing task/project identity')
        project=Path(new['project_path']).expanduser().resolve(strict=True)
        if not (project/'Assets').is_dir() or not (project/'ProjectSettings').is_dir():
            raise ValueError('project_path must be the selected Unity project')
        if not path.is_relative_to(project/'.avatar-assembly'):
            raise ValueError('Notes path does not belong to the selected project')
        new['format']='aa-quicktask-v1'
        new['task_revision']=expected+1
        text=json.dumps(new,ensure_ascii=False,indent=2,allow_nan=False)+'\n'
        if len(text.encode())>MAX_JSON:raise ValueError('Notes grew too large; retain paths instead of logs')
        f=tempfile.NamedTemporaryFile('w',encoding='utf-8',newline='\n',dir=path.parent,delete=False,prefix='.notes-')
        temporary=Path(f.name)
        with f:f.write(text);f.flush();os.fsync(f.fileno())
        os.replace(temporary,path);temporary=None
        return new
    finally:
        if temporary and temporary.exists():temporary.unlink()
        lock.unlink(missing_ok=True)


def summarize(task: dict) -> dict:
    """Continuation view; do not echo per-resource logs or replace source facts."""
    if not isinstance(task,dict):raise ValueError('Expected task object')
    keys=('format','task_id','task_revision','project_path','avatar','candidate','candidate_revision',
          'candidate_sha256','delivery','requested_method','constraints','explicit_requirements',
          'source_task','operation','optimization','performance_optimization','next_action','known_issues','artifact_paths')
    out={k:task[k] for k in keys if k in task}
    resources=task.get('resources',[])
    if not isinstance(resources,list):raise ValueError('resources must be a list')
    ordered=sorted((r for r in resources if isinstance(r,dict)),
                   key=lambda r: 0 if r.get('state') in {'failed','working','pending','blocked'} else 1)
    out['resource_total']=len(resources)
    out['resources_omitted']=max(0,len(resources)-16)
    out['resources']=[{k:r[k] for k in ('id','kind','state','path') if k in r} for r in ordered[:16]]
    out['note']='Summary only; omitted fields and resources stay in the original task. Not a replacement task.'
    return out


def main() -> int:
    p=argparse.ArgumentParser(description=__doc__)
    sub=p.add_subparsers(dest='cmd',required=True)
    for name in ('plan','receipt','summary'):
        a=sub.add_parser(name);a.add_argument('--input',type=Path,required=True)
    a=sub.add_parser('reuse');a.add_argument('--input',type=Path,required=True)
    a.add_argument('--history',type=Path,required=True);a.add_argument('--user-reported-issue',action='store_true')
    a=sub.add_parser('save');a.add_argument('--task',type=Path,required=True)
    a.add_argument('--patch',type=Path,required=True);a.add_argument('--expected',type=int,required=True)
    args=p.parse_args()
    try:
        if args.cmd=='save':
            saved=save_notes(args.task,read_json(args.patch),args.expected)
            out={'status':'NOTES_SAVED','task_revision':saved['task_revision'],'path':str(args.task),
                 'acceptance_checked':False,'unity_modified':False}
        elif args.cmd=='reuse':out=reuse(read_json(args.input),read_json(args.history),args.user_reported_issue)
        else:
            out={'plan':plan,'receipt':receipt,'summary':summarize}[args.cmd](read_json(args.input))
            out['source_input']=str(args.input.resolve())
        print(json.dumps(out,ensure_ascii=False,indent=2));return 0
    except (OSError,ValueError,TypeError,KeyError,RecursionError) as e:
        print(json.dumps({'status':'HELPER_NOT_APPLIED','error':str(e),
              'next':'No Unity change. Keep explicit requirements; use the existing original receipt/notes. Do not develop a helper during assembly.'},ensure_ascii=False));return 2

if __name__=='__main__':raise SystemExit(main())
