#!/usr/bin/env python3
"""Preview / update only Assets/AvatarWorkbench; preserve GUIDs and local edits."""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile

HERE = Path(__file__).resolve().parent


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix='.aw-update-', dir=path.parent)
    temp = Path(name)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True, type=Path)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    project = args.project.resolve()
    assets = project / 'Assets'
    target = assets / 'AvatarWorkbench'
    if not assets.is_dir() or not (project / 'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError('UNITY_PROJECT_REQUIRED')
    if not target.resolve().is_relative_to(assets.resolve()):
        raise ValueError('UNSAFE_INSTALL_DESTINATION')
    manifest = json.loads((HERE / 'AVATAR_WORKBENCH_MANIFEST.json').read_text(encoding='utf-8'))
    planned, conflicts = [], []
    for name, expected in manifest['payload_files'].items():
        relative = Path(name)
        if relative.is_absolute() or '..' in relative.parts:
            raise ValueError('INVALID_PACKAGE_PATH: ' + name)
        source = HERE / 'AvatarWorkbench' / relative
        dest = target / relative
        if not dest.resolve().is_relative_to(target.resolve()):
            raise ValueError('UNSAFE_INSTALL_DESTINATION: ' + name)
        if digest(source) != expected:
            raise ValueError('PACKAGE_HASH_MISMATCH: ' + name)
        if dest.exists():
            if not dest.is_file():
                conflicts.append(name)
                continue
            if name.endswith('.meta') or digest(dest) == expected:
                continue
            baseline = manifest['baseline_files'].get(name, [])
            allowed = [baseline] if isinstance(baseline, str) else baseline
            if digest(dest) not in allowed:
                conflicts.append(name)
                continue
        planned.append((name, source, dest))
    preview = {'status': 'PREVIEW_WITH_CONFLICTS' if conflicts else 'PREVIEW_ONLY',
               'target': str(target), 'files': [item[0] for item in planned],
               'local_merge_required': conflicts}
    if not args.apply:
        print(json.dumps(preview, ensure_ascii=False, indent=2))
        return
    if conflicts:
        print(json.dumps(preview, ensure_ascii=False, indent=2))
        raise ValueError('LOCAL_EDIT_CONFLICT: merge only listed files with Codex; nothing written')
    if not planned:
        print(json.dumps({'status': 'UNCHANGED', 'target': str(target)}))
        return
    rollback = project / '.avatar-workbench-update' / datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    old = [(name, dest, dest.read_bytes() if dest.exists() else None) for name, _, dest in planned]
    for name, _, data in old:
        if data is not None:
            atomic_write(rollback / name, data)
    written = set()
    try:
        for _, source, dest in planned:
            atomic_write(dest, source.read_bytes())
            written.add(dest)
    except Exception:
        for _, dest, data in old:
            if dest in written:
                if data is None:
                    dest.unlink(missing_ok=True)
                else:
                    atomic_write(dest, data)
        raise
    print(json.dumps({'status': 'FILES_UPDATED_NOT_UNITY_VERIFIED', 'target': str(target),
                     'updated_files': [item[0] for item in planned],
                     'old_plugin_files': str(rollback) if rollback.exists() else None,
                     'preserved': ['existing .meta', 'unknown local files', 'feedback and drafts',
                                   'task files', 'Avatar assets', 'Packages', 'Skill and MCP config']},
                    ensure_ascii=False, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2)
