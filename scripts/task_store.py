#!/usr/bin/env python3
"""Atomic metadata-only task storage. Does not lock Unity or modify its assets."""
from __future__ import annotations
import argparse,json,os,tempfile
from pathlib import Path
from workflow import (batch_decision, final_decision, intake_decision,
                      installing_resource_status_errors, optimization_decision,
                      preflight_decision,
                      upload_terminal_decision)


PHASES = ('INTAKE', 'PREFLIGHT', 'INSTALLING', 'FINAL_VALIDATION', 'COMPLETE', 'BLOCKED')


def validate_workflow(data: dict) -> None:
    """Prevent task metadata from silently skipping the RC5 workflow gates."""
    phase = data.get('phase')
    if phase not in PHASES:
        raise ValueError('phase must be an RC5 workflow phase; migrate older tasks through INTAKE')
    intake = intake_decision(data)
    if phase != 'INTAKE' and intake['status'] != 'READY_FOR_UNITY_WRITE':
        raise ValueError('Five-question start contract must be confirmed before leaving INTAKE')
    if phase == 'INSTALLING':
        preflight = preflight_decision(data)
        if preflight['status'] != 'READY_FOR_INSTALLATION':
            raise ValueError('Every selected clothing resource must complete preflight before INSTALLING')
        if installing_resource_status_errors(data):
            raise ValueError('INSTALLING resources must use pre-acceptance states; LOCAL_OK is final-only')
    if phase in ('FINAL_VALIDATION', 'COMPLETE'):
        batch = batch_decision(data)
        if batch['status'] != 'READY_FOR_FINAL_VALIDATION':
            raise ValueError('All selected resources must finish install or safe isolation before final validation')
        optimization = optimization_decision(data)
        if optimization['status'] not in ('SAFE_OPTIMIZATION_COMPLETE', 'OPTIMIZATION_NOT_REQUIRED'):
            raise ValueError('Base generated-copy optimization must finish before final validation; continue automatically without another prompt')
    if phase == 'COMPLETE' and final_decision(data)['status'] != 'LOCAL_OK':
        raise ValueError('Unified final validation and required size measurement must pass before COMPLETE')
    terminal = upload_terminal_decision(data)
    if terminal['status'] not in ('NO_TERMINAL_UPLOAD_RESULT', 'UPLOAD_CONFIRMED'):
        raise ValueError('Claimed upload terminal state is inconsistent with authority, target, build, or remote receipt')


def save(path: Path, data: dict, expected: int) -> dict:
    path=path.expanduser()
    # Keep metadata out of Unity import and package-owned directories.
    parts={p.lower() for p in path.resolve().parts}
    if '.avatar-assembly' not in parts or parts.intersection({'assets','packages','library','temp'}):
        raise ValueError('Task metadata must live in project/.avatar-assembly, outside Unity asset directories')
    if not isinstance(data,dict) or not isinstance(data.get('task_id'),str) or not data['task_id']:
        raise ValueError('A nonempty task_id is required')
    validate_workflow(data)
    if path.is_symlink(): raise ValueError('Symlink task file refused')
    path.parent.mkdir(parents=True,exist_ok=True)
    lock=path.with_suffix('.lock')
    try: fd=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
    except FileExistsError as ex: raise ValueError('Task metadata lock exists; verify previous writer stopped, do not blindly remove it') from ex
    temp=None
    try:
        with os.fdopen(fd,'w',encoding='utf-8') as f:
            json.dump({'pid':os.getpid(),'task_id':data['task_id']},f)
        old=json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else {}
        if old.get('task_revision',0)!=expected: raise ValueError('Stale task revision; read current state before updating')
        if old and old.get('task_id')!=data['task_id']: raise ValueError('Cannot replace a different task identity')
        # Full new document; caller must preserve unmodified fields after reading it.
        new=dict(data);new['task_revision']=expected+1
        with tempfile.NamedTemporaryFile(mode='w',encoding='utf-8',dir=path.parent,delete=False,prefix='.task-',suffix='.tmp') as f:
            temp=Path(f.name);json.dump(new,f,ensure_ascii=False,indent=2);f.write('\n');f.flush();os.fsync(f.fileno())
        os.replace(temp,path);temp=None
        return new
    finally:
        if temp and temp.exists(): temp.unlink()
        lock.unlink(missing_ok=True)


def main() -> int:
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('task',type=Path);p.add_argument('--input',type=Path);p.add_argument('--expected',type=int)
    a=p.parse_args()
    try:
        if a.input:
            if a.expected is None: raise ValueError('--expected is required for writes')
            data=json.loads(a.input.read_text(encoding='utf-8-sig'))
            new=save(a.task,data,a.expected)
            print(json.dumps({'task_id':new['task_id'],'task_revision':new['task_revision'],'path':str(a.task)},ensure_ascii=False))
        else:
            print(a.task.read_text(encoding='utf-8-sig'))
        return 0
    except (OSError,ValueError,TypeError) as ex:
        print(json.dumps({'error':str(ex)},ensure_ascii=False));return 2
if __name__=='__main__': raise SystemExit(main())
