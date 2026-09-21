#!/usr/bin/env python3
"""Local feedback memory commands. No model, network, Unity or background listener.

Normal assembly uses best-effort mode (default): learning errors are reported and
return success to avoid blocking the user's task. Developers can use --strict.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import sqlite3
from learning_store import LearningStore, default_home, MAX_IMPORT_BYTES


def load(path: Path):
    if path.stat().st_size > MAX_IMPORT_BYTES:raise ValueError('Input too large; submit only the current problem delta')
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--home',type=Path,help='Persistent local store outside Unity Assets and the Skill; default AA_LEARNING_HOME / local user data')
    parser.add_argument('--strict',action='store_true',help='Development only: errors use exit code 2 instead of best-effort continuation')
    sub=parser.add_subparsers(dest='command',required=True)
    sub.add_parser('path',help='Print storage location; do not create the store')
    for name in ('record','import','learn','recall'):
        p=sub.add_parser(name);p.add_argument('--input',type=Path,required=True)
        if name=='recall':p.add_argument('--limit',type=int,default=3)
    p=sub.add_parser('pending');p.add_argument('--task-id',required=True);p.add_argument('--limit',type=int,default=3)
    p=sub.add_parser('events');p.add_argument('--task-id',required=True);p.add_argument('--episode-id',required=True)
    p.add_argument('--after',type=int,default=0);p.add_argument('--limit',type=int,default=12)
    p=sub.add_parser('outcome');p.add_argument('--lesson-id',required=True);p.add_argument('--event-id',required=True)
    p=sub.add_parser('report');p.add_argument('--task-id',required=True)
    p=sub.add_parser('forget');p.add_argument('--task-id',required=True);p.add_argument('--confirm',action='store_true')
    args=parser.parse_args()
    try:
        if os.environ.get('AA_LEARNING_DISABLED','').strip().lower() in {'1','true','yes'} and args.command not in {'path','forget'}:
            print(json.dumps({'status':'LEARNING_DISABLED','next':'Continue the normal task without learning.'}))
            return 0
        store=LearningStore(args.home)
        if args.command=='path': result={'home':str(store.home),'exists':store.db.exists(),'automatic_transcript_capture':False}
        elif args.command=='record':result=store.record(load(args.input))
        elif args.command=='import':result=store.import_jsonl(args.input)
        elif args.command=='learn':result=store.learn(load(args.input))
        elif args.command=='recall':result=store.recall(load(args.input),args.limit)
        elif args.command=='pending':result=store.pending(args.task_id,args.limit)
        elif args.command=='events':result=store.events(args.task_id,args.episode_id,args.after,args.limit)
        elif args.command=='outcome':result=store.outcome(args.lesson_id,args.event_id)
        elif args.command=='report':result=store.report(args.task_id)
        else:
            if not args.confirm:raise ValueError('Explicit user deletion request requires --confirm')
            result=store.forget(args.task_id)
        print(json.dumps(result,ensure_ascii=False,indent=2))
        return 0
    except (OSError,ValueError,TypeError,KeyError,sqlite3.Error) as ex:
        print(json.dumps({'status':'LEARNING_SKIPPED','error':str(ex),
                          'next':'Continue the unchanged assembly workflow. Do not retry, repair or back up the learning database during assembly.'},ensure_ascii=False))
        return 2 if args.strict else 0

if __name__=='__main__':raise SystemExit(main())
