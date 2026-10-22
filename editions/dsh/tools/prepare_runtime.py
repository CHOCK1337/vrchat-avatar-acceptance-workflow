#!/usr/bin/env python3
"""Maintainer-only: build the pinned Win64 offline runtime, never copy a user profile."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / 'local/packaging-20261019/downloads'
RUNTIME = ROOT / 'runtime'
SOURCES = {
    'node': {
        'version': '24.15.0',
        'url': 'https://nodejs.org/download/release/v24.15.0/node-v24.15.0-win-x64.zip',
        'sha256': 'cc5149eabd53779ce1e7bdc5401643622d0c7e6800ade18928a767e940bb0e62',
        'hash_source': 'https://nodejs.org/download/release/v24.15.0/SHASUMS256.txt'},
    'python': {
        'version': '3.13.16',
        'url': 'https://www.python.org/ftp/python/3.13.16/python-3.13.16-embeddable-amd64.zip',
        'sha256': '589dc1e9d02549ca5f680710307ff9b77f6e3ffc4aa3efccd7fa54eeeafac94b',
        'hash_source': 'https://www.python.org/ftp/python/3.13.16/windows-3.13.16.json'},
}


def download(item):
    dest = CACHE / item['url'].rsplit('/', 1)[1]
    if not dest.exists():
        print('Downloading ' + dest.name, flush=True)
        with urllib.request.urlopen(item['url'], timeout=60) as response, dest.open('wb') as target:
            shutil.copyfileobj(response, target)
    if hashlib.sha256(dest.read_bytes()).hexdigest() != item['sha256']:
        raise ValueError('OFFICIAL_ARCHIVE_HASH_MISMATCH: ' + dest.name)
    return dest


def extract(source, target, prefix=''):
    with zipfile.ZipFile(source) as archive:
        if archive.testzip():
            raise ValueError('CORRUPT_ARCHIVE')
        for info in archive.infolist():
            name = info.filename
            if prefix:
                if not name.startswith(prefix):
                    continue
                name = name[len(prefix):]
            if not name or info.is_dir():
                continue
            dest = (target / name).resolve()
            if not dest.is_relative_to(target.resolve()):
                raise ValueError('UNSAFE_ARCHIVE_PATH')
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(archive.read(info))


def npm(args, directory):
    node = RUNTIME / 'node/node.exe'
    cli = RUNTIME / 'node/node_modules/npm/bin/npm-cli.js'
    env = dict(os.environ)
    env['PATH'] = str(node.parent) + os.pathsep + env.get('PATH', '')
    subprocess.run([str(node), str(cli), *args, '--cache', str(CACHE / 'npm'),
                    '--no-audit', '--no-fund'], cwd=directory, env=env, check=True)


def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    RUNTIME.mkdir(exist_ok=True)
    extract(download(SOURCES['node']), RUNTIME / 'node', 'node-v24.15.0-win-x64/')
    extract(download(SOURCES['python']), RUNTIME / 'python')
    # The embeddable interpreter deliberately excludes the calling script's directory.
    # Add only our package directories, never user/site-packages or a machine PATH.
    pth = RUNTIME / 'python/python313._pth'
    pth.write_text('python313.zip\n.\n../..\n../../adapter\n../../tools\n', encoding='utf-8')
    source = ROOT / 'dependencies/UnityMCP-VRC/unity-mcp-server'
    target = RUNTIME / 'unity-mcp'
    shutil.copytree(source, target, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns('node_modules', 'build', '__pycache__'))
    npm(['ci', '--ignore-scripts'], target)
    npm(['run', 'build'], target)
    npm(['prune', '--omit=dev', '--ignore-scripts'], target)
    setup = RUNTIME / 'setup'
    setup.mkdir(exist_ok=True)
    for name in ('package.json', 'package-lock.json'):
        src = ROOT / 'dependencies/setup-runtime' / name
        if src.exists():
            shutil.copy2(src, setup / name)
    if not (setup / 'package-lock.json').exists():
        npm(['install', '--package-lock-only', '--ignore-scripts'], setup)
        shutil.copy2(setup / 'package-lock.json', ROOT / 'dependencies/setup-runtime/package-lock.json')
    npm(['ci', '--ignore-scripts'], setup)
    receipt = {'format': 'avatar-workbench-runtime-v1', 'platform': 'win32-x64',
               'archives': SOURCES, 'mcp_sdk': '1.29.0', 'yaml': '4.3.2',
               'python_pth_modified': True, 'dsh_desktop_included': False,
               'private_configuration_copied': False}
    (RUNTIME / 'PROVENANCE.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'status': 'RUNTIME_READY', 'runtime': str(RUNTIME)}))


if __name__ == '__main__':
    main()
