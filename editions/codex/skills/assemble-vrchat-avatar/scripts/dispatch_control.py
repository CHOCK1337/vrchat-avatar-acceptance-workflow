#!/usr/bin/env python3
"""Bounded Codex dispatch packets and observed receipts; never starts agents itself.

Parent uses native Codex spawn/wait tools. Only parent accesses live Unity. This
ledger is not a remote sandbox; generated configuration must be observed in situ.
"""
from __future__ import annotations
import argparse
from copy import deepcopy
import json
from pathlib import Path
import re
import session_policy as p
import role_access
from session_choice import require, now
from lowio_common import read_json,print_json,same_identity

WORKER_KINDS=('extract','source_mapping','prepare_operation','edit_file')
RESULTS=('FACTS','PROPOSED','FILES_CHANGED','BLOCKED','OBSERVATIONS')

WORKER_PROMPT='''You are the assigned-file worker for this avatar task. Read the scoped packet and allowed snapshots. For extract/source_mapping/prepare_operation return compact facts or exact allowed operations. For edit_file write ONLY the explicitly listed files inside .avatar-assembly/jobs/<job_id>/work; include before/after SHA256 and changes. These are real staged files, not live Unity assets. The parent merges reviewed changes. Preserve all user requirements in the packet. You may inherit broad host permissions or see Unity MCP: that is not authorization to use them. Do not call live Unity through MCP, shell, HTTP, GUI, or another route; do not import assets, change Assets/Packages/.codex, task state, scenes, body baselines or capture state. Do not spawn children. Do not alter requirements, disable effects to disguise failures, invent APIs, or certify visuals from numeric checks. Do not run tests, backups or permission probes during assembly. If required data/capability is missing, return BLOCKED with the one specific fact needed. Return JSON with job_id and request_sha256; FILES_CHANGED is not approval to merge and not Unity success. This is a workflow role boundary, not an OS security guarantee.'''

REVIEWER_PROMPT='''You are the independent, read-only reviewer of one frozen avatar candidate. Read user requirements and agreed checks first, then exact original-material images and recorded real input trajectories; read the executor's explanation last. Use local image viewing to actually inspect evidence when available. Do not call live Unity MCP, change selection/pose, take new screenshots, modify files or spawn agents. Do not infer success from a generated HTML page, numbers, a renamed pose screenshot or upload receipts. Report exactly the agreed checks: OBSERVED_OK, FAILED, NOT_OBSERVED or BLOCKED; include image/evidence IDs and concise visual findings. Diagnostic-direct images cannot prove real control behavior; historical candidates do not prove the current candidate. No new full boundary matrix. No evidence of a check means NOT_OBSERVED, not approval. Return JSON to the coordinator; never authorize or perform upload.'''

def issue(task: dict,request: dict) -> tuple[dict,dict]:
    a=p.authorize(task,'dispatch');require(a['allowed'],'; '.join(a['errors']))
    r=task['run_choice'];s=r['selection'];role=request.get('role')
    require(s.get('permission_policy')==role_access.POLICY,'Confirm the new role policy before dispatching an old v1 task')
    require(role in ('worker','reviewer'),'Only worker/reviewer are dispatched; coordinator is the parent')
    require(role!='reviewer' or s['review_owner']=='independent','Independent review was not agreed')
    job=request.get('job_id');require(p.nonempty(job) and re.fullmatch(r'[A-Za-z0-9_.-]{1,90}',job) is not None,'Safe stable job_id required')
    jobs=r.get('jobs',{});require(job not in jobs and job not in r.get('reserved_job_ids',[]),'Job already issued; wait/reconcile the same job, never redispatch')
    count=r.get('carry_counts',{}).get(role,0)+sum(x['role']==role and x.get('status')!='FAILED_STARTUP' for x in jobs.values())
    limit=s['budget']['worker_calls' if role=='worker' else 'reviewer_calls']
    require(count<limit,'APPROVED_CALL_BUDGET_EXHAUSTED')
    active=sum(x.get('status') in ('ISSUED','STARTED') for x in jobs.values())
    require(active<s['budget']['max_parallel'],'APPROVED_PARALLEL_LIMIT_REACHED')
    require(p.nonempty(request.get('goal')) and len(request['goal'])<=2000,'A scoped goal is required')
    require(isinstance(request.get('snapshot'),dict),'Scoped snapshot required')
    require(len(json.dumps(request['snapshot'],ensure_ascii=False).encode())<=24000,'Snapshot exceeds 24KB; provide precise local references, not full history')
    require(isinstance(request.get('scope'),list) and request['scope'] and all(p.nonempty(x) for x in request['scope']),'Resource scope required')
    require(same_identity(request.get('candidate',{}),p.identity(task,request.get('candidate',{}).get('platform'))),'Request uses old candidate')
    files=request.get('write_files',[])
    if request.get('kind')=='edit_file' and role=='worker':
        role_access.validate_file_request(Path(task['project_path']),job,files)
    else:require(not files,'Only an edit_file worker may write assigned files')
    ops=request.get('allowed_operations',[])
    require(isinstance(ops,list),'allowed_operations must be a list')
    for op in ops:
        require(isinstance(op,dict) and set(op)=={'operation_id','target_id'} and all(p.nonempty(v) for v in op.values()),'Allowed operation must name exact operation_id/target_id')
        require(op['target_id'] in request['scope'],'Allowed target outside request scope')
        require(op['operation_id'] not in ('execute_editor_command','eval','exec','shell','upload','BuildAndUpload','delete_project'),'Unrestricted execution is not a worker operation')
    if role=='worker':require(request.get('kind') in WORKER_KINDS,'High-uncertainty baseline/ownership/algorithm decisions stay with coordinator')
    else:require(request.get('kind')=='review' and not ops,'Reviewer has no mutation proposals')
    packet=deepcopy(request)
    packet.update(task_id=task['task_id'],agreement_sha256=r['proposal_sha256'],runtime_expected=s['roles'][role],
                  unity_write_allowed=False,file_write_allowed=(role=='worker' and request.get('kind')=='edit_file'),
                  permission_policy=role_access.POLICY,spawn_children_allowed=False,requested_at=now(),
                  result_shape={'job_id':job,'request_sha256':'computed below','status':list(RESULTS),'findings':[],'proposals':[]},
                  instructions=WORKER_PROMPT if role=='worker' else REVIEWER_PROMPT)
    packet['request_sha256']=p.digest(packet)
    t=deepcopy(task);t['run_choice']['jobs'][job]={'role':role,'status':'ISSUED','packet':packet,'request_sha256':packet['request_sha256'],'issued_at':now()}
    t['run_choice']['orchestration_stage']='WORK_DISPATCHED'
    return t,packet

def started(task: dict,job_id: str,receipt: dict) -> dict:
    require(not p.consent_errors(task),'Agreement expired')
    j=task['run_choice']['jobs'].get(job_id);require(isinstance(j,dict) and j['status']=='ISSUED','Job not awaiting spawn receipt')
    require(receipt.get('observed') is True and receipt.get('source') in p.RUNTIME_SOURCES,'Actual child runtime receipt required; configured model is not observed model')
    require(p.nonempty(receipt.get('thread_id')),'Actual child thread ID required')
    expected=j['packet']['runtime_expected']
    require(all(receipt.get(k)==expected[k] for k in ('model','effort')),'Child model/effort mismatch; do not accept or silently fallback')
    threads={task['run_choice'].get('runtime',{}).get('coordinator',{}).get('thread_id')}
    threads.update(x.get('runtime',{}).get('thread_id') for x in task['run_choice']['jobs'].values())
    require(receipt['thread_id'] not in threads,'Each worker/reviewer must have a distinct fresh thread')
    require(receipt.get('unauthorized_unity_call') is not True,'Actual forbidden Unity call detected')
    t=deepcopy(task);jj=t['run_choice']['jobs'][job_id]
    jj.update(status='STARTED',runtime=deepcopy(receipt),started_at=now(),
              permissions={'policy':role_access.POLICY,'hard_isolation_verified':False,
                           'host_permissions_inherited':True,'unity_write_allowed':False,
                           'file_write_allowed':j['packet'].get('file_write_allowed',False)})
    t['run_choice']['orchestration_stage']='REVIEW_RUNNING' if j['role']=='reviewer' else 'WORK_RUNNING'
    return t

def finished(task: dict,job_id: str,result: dict) -> dict:
    require(not p.consent_errors(task),'Agreement expired')
    j=task['run_choice']['jobs'].get(job_id);require(isinstance(j,dict) and j['status']=='STARTED','No observed child runtime for this job')
    req=j['packet']
    require(result.get('job_id')==job_id and result.get('request_sha256')==j['request_sha256'],'Wrong result identity')
    require(same_identity(req['candidate'],p.identity(task,req['candidate']['platform'])),'Candidate changed; result is stale reference, not an actionable proposal')
    require(result.get('status') in RESULTS,'Only structured facts, proposals, observations or blocking result permitted')
    require(len(json.dumps(result,ensure_ascii=False).encode())<=20000,'Return compact findings; do not return raw history')
    allowed={(x['operation_id'],x['target_id']) for x in req.get('allowed_operations',[])}
    proposals=result.get('proposals',[]);require(isinstance(proposals,list),'Proposals must be a list')
    if j['role']=='reviewer':require(not proposals,'Reviewer cannot propose executable writes')
    require(not proposals or (j['role']=='worker' and result['status']=='PROPOSED'),'Only worker PROPOSED results can contain proposed operations')
    def unsafe_code(value):
        if isinstance(value,dict):
            return any(str(k).lower() in ('code','script','command','eval','callback','execute_editor_command') or unsafe_code(v) for k,v in value.items())
        return isinstance(value,list) and any(unsafe_code(v) for v in value)
    require(not unsafe_code(proposals),'Raw code/command proposals are not accepted')
    for op in proposals:
        require(isinstance(op,dict) and (op.get('operation_id'),op.get('target_id')) in allowed,'Proposed operation outside the explicit task packet')
        require(not set(op)-{'operation_id','target_id','arguments','reason'},'Unknown executable proposal fields')
    files_verified=False
    if result['status']=='FILES_CHANGED':
        require(j['role']=='worker' and req.get('kind')=='edit_file','Only assigned-file workers may return FILES_CHANGED')
        role_access.validate_file_result(Path(task['project_path']),job_id,req.get('write_files',[]),result.get('changed_files'))
        files_verified=True
    else:require(not result.get('changed_files'),'Changed files require FILES_CHANGED status')
    require(result.get('unity_execution_performed') is not True,'Worker/reviewer may not mutate live Unity')
    usage=result.get('usage')
    if usage is not None:
        require(isinstance(usage,dict),'Usage must be observed fields or null')
        for key in ('input_tokens','output_tokens','cached_input_tokens','reasoning_tokens'):
            if key in usage and usage[key] is not None:require(type(usage[key]) is int and usage[key]>=0,'Invalid observed usage value')
    t=deepcopy(task);jj=t['run_choice']['jobs'][job_id];jj.update(status='RETURNED',result=deepcopy(result),finished_at=now(),unity_execution_performed=False,files_verified=files_verified)
    t['run_choice']['orchestration_stage']='REVIEW_RETURNED' if j['role']=='reviewer' else 'WORK_RETURNED'
    return t

def close_failed(task: dict,job_id: str,reason: str) -> dict:
    require(p.nonempty(reason),'Failure/cancellation reason required')
    j=task.get('run_choice',{}).get('jobs',{}).get(job_id)
    require(isinstance(j,dict) and j['status'] in ('ISSUED','STARTED'),'Only a pending job can be closed')
    t=deepcopy(task);t['run_choice']['jobs'][job_id].update(status='FAILED_OR_CANCELLED',reason=reason,closed_at=now())
    t['run_choice']['orchestration_stage']='JOB_FAILED_OR_CANCELLED'
    return t # Call budget is consumed; cancelling this record does not cancel remote execution.

def startup_failed(task: dict,job_id: str,receipt: dict) -> dict:
    """Only pre-start loader errors are setup, never retroactive free work."""
    j=task.get('run_choice',{}).get('jobs',{}).get(job_id)
    require(isinstance(j,dict) and j.get('status')=='ISSUED','Only an unstarted job can fail setup')
    require(receipt.get('source')=='codex_spawn_error' and receipt.get('started') is False and not receipt.get('thread_id'),'Observed pre-start spawn error required')
    require(receipt.get('code') in ('agent_type_unavailable','agent_config_load_error'),'This is not a configuration startup failure')
    require(p.nonempty(receipt.get('detail')),'Specific native load failure required')
    t=deepcopy(task);t['run_choice']['jobs'][job_id].update(status='FAILED_STARTUP',setup_receipt=deepcopy(receipt),closed_at=now())
    t=setup_event(t,{'kind':'config_load_failed','detail':receipt['detail'],'usage':receipt.get('usage'),'job_id':job_id})
    t['run_choice']['orchestration_stage']='CONFIGURATION_BLOCKED'
    return t


def setup_event(task: dict,event: dict) -> dict:
    """Record install/load observations separately. No implicit free model calls."""
    require(event.get('kind') in ('config_parsed','config_load_failed','client_reload','host_permission_notice'), 'Unknown setup event')
    require(p.nonempty(event.get('detail')) and len(event['detail'])<=1500,'Short setup detail required')
    t=deepcopy(task);entry=deepcopy(event);entry.update(at=now(),usage_known=isinstance(event.get('usage'),dict))
    t['run_choice'].setdefault('setup_events',[]).append(entry)
    return t


def export_configs(task: dict,out: Path) -> dict:
    a=p.authorize(task,'configure');require(a['allowed'],'; '.join(a['errors']))
    s=task['run_choice']['selection'];inv=task['run_choice']['inventory']
    require(s.get('permission_policy')==role_access.POLICY,'New role policy must be confirmed before exporting configs')
    require(not out.exists() and not out.is_symlink(),'Choose a new task-local config directory; no overwrite of user configuration')
    files={};q=lambda x:json.dumps(x,ensure_ascii=False)
    if s['collaboration']:
        for role in ('worker','reviewer'):
            r=s['roles'][role]
            prompt=WORKER_PROMPT if role=='worker' else REVIEWER_PROMPT
            text=f'name = "aa_{role}"\ndescription = {q("Use only when the confirmed avatar run explicitly dispatches this " + role)}\nmodel = {q(r["model"])}\nmodel_reasoning_effort = {q(r["effort"])}\ndeveloper_instructions = {q(prompt)}\n\n[agents]\nenabled = false\n'
            # Omit MCP and sandbox overrides. Parent transport/permissions inherit intact;
            # enabled=false stubs without transport broke real Codex startup.
            files[f'aa_{role}.toml']=text
    main=s['roles']['coordinator']
    for v in main.values():require(re.fullmatch(r'[A-Za-z0-9_.:/-]+',v) is not None,'Unsupported runtime model identifier')
    files['MAIN_SESSION_FRAGMENT.toml']=f'# Merge only after user selection; do not replace the user config.\nmodel = {q(main["model"])}\nmodel_reasoning_effort = {q(main["effort"])}\n\n[agents]\nenabled = {str(s["collaboration"]).lower()}\nmax_concurrent_threads_per_session = {s["budget"]["max_parallel"]}\n'
    files['LAUNCH_IF_MODEL_CHANGE_REQUIRED.txt']=f'Only when the actual current session differs; not executed by this helper.\ncodex -m {main["model"]} -c model_reasoning_effort=\'{q(main["effort"])}\' -c agents.enabled={str(s["collaboration"]).lower()} -c agents.max_concurrent_threads_per_session={s["budget"]["max_parallel"]}\n\nRun auto_configure.py configure --task <AssemblyTask.json> --apply; the script installs roles and merges the selected main config. Do not ask the user to edit TOML. Re-open/resume the same AssemblyTask after the client loads the selected config. This file does not switch an existing session.\n'
    out.mkdir(parents=True)
    # Persist the exact UTF-8 bytes that are hashed below; text-mode writes
    # translate LF to CRLF on Windows and invalidate the install receipt.
    for name,text in files.items():(out/name).write_bytes(text.encode('utf-8'))
    record={'agreement_sha256':task['run_choice']['proposal_sha256'],'files':list(files),'file_sha256':{name:__import__('hashlib').sha256(text.encode()).hexdigest() for name,text in files.items()},'installed':False,'runtime_verified':False,'no_model_call':True,
            'permission_policy':role_access.POLICY,
            'warning':'Host permission and MCP config inherited intact; file scope and single writer are workflow constraints, not OS isolation. No permission probe required.'}
    (out/'CONFIG_STATUS.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    return record

def main() -> int:
    a=argparse.ArgumentParser(description=__doc__)
    a.add_argument('action',choices=['issue','started','finish','close','configs','status','setup-event','startup-failed']);a.add_argument('--task',required=True,type=Path)
    a.add_argument('--input',type=Path);a.add_argument('--out',type=Path);a.add_argument('--job-id');a.add_argument('--reason');a.add_argument('--expected',type=int)
    args=a.parse_args()
    try:
        t=read_json(args.task)
        if args.action=='status':
            rr=t.get('run_choice',{});jobs=rr.get('jobs',{})
            print_json({'orchestration_stage':rr.get('orchestration_stage'),'core_phase':t.get('phase'),
                'roles_expected':rr.get('selection',{}).get('roles'),'main_observed':rr.get('runtime',{}).get('coordinator'),
                'jobs':{k:{kk:v.get(kk) for kk in ('role','status','runtime','reason','files_verified')} for k,v in jobs.items()},
                'returned_counts':{role:sum(j.get('role')==role and j.get('status')=='RETURNED' for j in jobs.values()) for role in ('worker','reviewer')},
                'review_required':rr.get('selection',{}).get('review_owner')=='independent',
                'setup_event_count':len(rr.get('setup_events',[])),'live_agents_not_queried':True,'avatar_acceptance_proven':False});return 0
        if args.action=='configs':
            require(args.out is not None,'--out required');print_json(export_configs(t,args.out));return 0
        if args.action=='issue':new,packet=issue(t,read_json(args.input))
        elif args.action=='started':new=started(t,args.job_id,read_json(args.input))
        elif args.action=='finish':new=finished(t,args.job_id,read_json(args.input))
        elif args.action=='setup-event':new=setup_event(t,read_json(args.input))
        elif args.action=='startup-failed':new=startup_failed(t,args.job_id,read_json(args.input))
        else:new=close_failed(t,args.job_id,args.reason or '')
        require(args.expected is not None,'--expected task revision required')
        from task_store import save
        new=save(args.task,new,args.expected)
        if args.action=='issue':
            # Packet is returned directly; caller can save from the canonical task JSON.
            print_json({'task_revision':new['task_revision'],'packet':packet,'agent_spawned':False})
        elif args.action=='setup-event':print_json({'task_revision':new['task_revision'],'setup_events':len(new['run_choice']['setup_events']),'no_unity_write':True})
        else:print_json({'task_revision':new['task_revision'],'job_id':args.job_id,'status':new['run_choice']['jobs'][args.job_id]['status'],'no_unity_write':True})
        return 0
    except (OSError,ValueError,TypeError,KeyError,AttributeError) as ex:print_json({'status':'DISPATCH_ACTION_BLOCKED','error':str(ex)[:600]});return 2
if __name__=='__main__':raise SystemExit(main())
