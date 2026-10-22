#!/usr/bin/env python3
"""Restore only recorded installer files; refuse later local edits."""
import argparse
import json
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parent))
from install import atomic, sha

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--receipt',required=True,type=Path)
    p.add_argument('--apply',action='store_true')
    args=p.parse_args()
    folder=args.receipt.resolve().parent
    record=json.loads(args.receipt.read_text(encoding='utf-8-sig'))
    if record.get('format')!='avatar-workbench-install-rollback-v1': raise ValueError('UNSUPPORTED_ROLLBACK_RECEIPT')
    if args.apply and record.get('project'):
        lock=Path(record['project'])/'Temp/UnityLockfile'
        if lock.exists():
            try:
                with lock.open('r+b'): pass
            except OSError: raise ValueError('UNITY_IS_OPEN: save and close this project before rollback') from None
    actions=[]
    for row in record['records']:
        dest=Path(row['path'])
        if not dest.is_absolute() or not dest.is_file() or sha(dest.read_bytes())!=row['installed_sha256']:
            raise ValueError('LATER_EDIT_DETECTED: '+str(dest)+'; nothing restored')
        old=None
        if row['old_file'] is not None:
            source=(folder/row['old_file']).resolve()
            if source.parent!=folder: raise ValueError('INVALID_BACKUP_PATH')
            old=source.read_bytes()
            if sha(old)!=row['old_sha256']: raise ValueError('BACKUP_HASH_MISMATCH')
        actions.append((dest,old))
    if args.apply:
        for dest,old in reversed(actions):
            if old is None: dest.unlink()
            else: atomic(dest,old)
    print(json.dumps({'status':'RESTORED' if args.apply else 'ROLLBACK_PREVIEW','files':len(actions),
        'note':'Only recorded files; generated Unity .meta files and user assets are preserved.'}))

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    try: main()
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr); raise SystemExit(2)
