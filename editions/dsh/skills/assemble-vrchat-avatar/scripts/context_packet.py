#!/usr/bin/env python3
"""Project a task or an already saved tool result without printing its full payload.

Usage: context_packet.py resume AssemblyTask.json
       context_packet.py field AssemblyTask.json /start_contract/size_budget
       context_packet.py receipt raw-tool-result.json
Read-only. Never repairs, migrates, validates or authorizes a task.
"""
from __future__ import annotations
import argparse
from collections import Counter
import json
from pathlib import Path
from lowio_common import bounded,canonical_identity,execution_receipt,pointer,print_json,sha_file,validate_identity,MAX_FILE_BYTES

def summarize(data: dict) -> dict:
    if not isinstance(data,dict): raise ValueError('Task must be a JSON object')
    canonical=canonical_identity(data)
    # A canonical task's SDK/platform may be stored only in assembly_manifest/jobs;
    # keep missing fields explicit rather than guessing a current final candidate.
    rev=data.get('candidate_revision')
    has_candidate=all(isinstance(data.get(k),str) and data.get(k) for k in ('candidate','candidate_sha256')) and ((isinstance(rev,str) and bool(rev.strip())) or (type(rev) is int and rev>=0))
    resources=data.get('resources',[])
    if not isinstance(resources,list): resources=[]
    counts=Counter(str(x.get('status','UNRECORDED')) for x in resources if isinstance(x,dict))
    focus=[]
    for x in resources:
        if isinstance(x,dict) and x.get('status') not in ('LOCAL_OK','REJECTED','ISOLATED','SKIPPED'):
            focus.append({k:x[k] for k in ('id','resource_id','kind','status','reason','blocker') if k in x})
    start=data.get('start_contract')
    canonical_start=isinstance(start,dict)
    if not canonical_start: start={}
    manifest=start.get('assembly_manifest')
    target_platforms=manifest.get('target_platform') if isinstance(manifest,dict) else None
    if target_platforms is None: target_platforms=data.get('target_platforms')
    if isinstance(target_platforms,str): target_platforms=[target_platforms]
    return {
        'task_id':data.get('task_id'),'task_revision':data.get('task_revision'),
        'run_choice_summary':bounded({'state':data.get('run_choice',{}).get('state'), 'profile':data.get('run_choice',{}).get('selection',{}).get('profile'), 'html_preview':data.get('run_choice',{}).get('selection',{}).get('html_preview'), 'review_owner':data.get('run_choice',{}).get('selection',{}).get('review_owner')}) if isinstance(data.get('run_choice'),dict) else None,
        'phase_reported':data.get('phase'),'status_reported':data.get('status'),
        'contract_state':'CANONICAL_FIELDS_PRESENT' if canonical_start and has_candidate else 'LEGACY_OR_INCOMPLETE',
        'start_confirmed':start.get('confirmed') is True,
        'canonical_candidate':canonical if has_candidate else None,
        'legacy_candidate_not_promoted':bounded(data.get('current_candidate')),
        'project_path':data.get('project_path'),'target_platforms':target_platforms,
        'contracts':bounded({k:start[k] for k in ('visualization','size_budget','content_priority','performance_contract','menu_plan','delivery') if k in start},max_items=6),
        'authority_reported_not_granted':bounded({k:data[k] for k in ('mode','upload_authorized','upload_authority','cancelled','private') if k in data}),
        'resource_count':len(resources),'resource_status_counts':dict(counts),
        'resource_focus':bounded(focus[:3]),'remaining_focus_count':max(0,len(focus)-3),
        'waiting_question':bounded(data.get('waiting_question')),'open_bug':bounded(data.get('open_bug')),
        'jobs_reported':bounded(data.get('performance_measurement_jobs',{}),max_items=3),
        'acceptance_proven':False,
        'next_read':'Use field JSON Pointer for the current phase/resource. Read canonical Start Card before writes; run existing workflow.py only at phase boundaries. Never treat this packet as a gate or auto-migrate legacy fields.',
        'full_source_is_authoritative':True,
    }

def from_file(path: Path, mode: str='resume', query: str='') -> dict:
    raw=path.read_bytes()
    if len(raw)>MAX_FILE_BYTES: raise ValueError('Use a smaller scoped file (max 32 MiB)')
    text=raw.decode('utf-8-sig')
    source={'source_path':str(path.resolve()),'source_sha256':__import__('hashlib').sha256(raw).hexdigest(),'source_bytes':len(raw)}
    if mode=='receipt': result=execution_receipt(text)
    else:
        data=json.loads(text)
        result=summarize(data) if mode=='resume' else {'pointer':query,'value':bounded(pointer(data,query)),
                                                       'acceptance_proven':False,'omission_policy':'Explicit preview only; use a narrower pointer for omitted values'}
    return dict(source,**result)

def main() -> int:
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode',choices=['resume','field','receipt']);p.add_argument('file',type=Path);p.add_argument('pointer',nargs='?',default='')
    a=p.parse_args()
    try:
        if a.mode=='field' and not a.pointer: raise ValueError('field requires a nonempty JSON Pointer')
        r=from_file(a.file,a.mode,a.pointer);print_json(r)
        return 3 if r.get('execution_status')=='FAILED' else 0
    except (OSError,ValueError,TypeError,RecursionError) as e:
        print_json({'status':'PACKET_UNAVAILABLE','error':str(e)[:500],'acceptance_proven':False});return 2
if __name__=='__main__': raise SystemExit(main())
