#!/usr/bin/env python3
"""Read-only file hashes, archive-path scope and required entry points."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

def verify(root, strict=False):
    root=Path(root).resolve()
    manifest=json.loads((root/'MANIFEST.json').read_text(encoding='utf-8'))
    failures=[]
    for name,expected in manifest['files'].items():
        p=(root/name).resolve()
        if not p.is_relative_to(root) or Path(name).is_absolute():
            failures.append('UNSAFE_PATH: '+name);continue
        if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=expected:
            failures.append('MISSING_OR_CHANGED: '+name)
    if strict:
        actual={p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()}
        extras=actual-set(manifest['files'])-{'MANIFEST.json'}
        failures.extend('UNEXPECTED: '+x for x in sorted(extras))
    return {'status':'PASS' if not failures else 'FAIL','format':manifest['format'],
            'distribution_version':manifest['distribution_version'],'files':len(manifest['files']),
            'failures':failures,'scope':'Package integrity only, not avatar or model acceptance.'}

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    p.add_argument('--strict',action='store_true')
    args=p.parse_args()
    try:
        report=verify(args.root,args.strict)
        print(json.dumps(report,ensure_ascii=False,indent=2))
        raise SystemExit(0 if report['status']=='PASS' else 2)
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr);raise SystemExit(2)
