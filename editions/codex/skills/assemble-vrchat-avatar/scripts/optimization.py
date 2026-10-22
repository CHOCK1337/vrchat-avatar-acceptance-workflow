#!/usr/bin/env python3
"""Final-stage optimization planning and real SDK receipt comparison.

Offline helper: never writes Unity assets, launches builds, changes SDK/MCP,
authenticates receipts or accepts an avatar. Live work uses the installed tools.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys


def read(path):
    path = Path(path)
    if path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError('Use a bounded current receipt, not the whole transcript')
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def plan(request):
    """Classify the final stage; flags are plans, not permission or execution."""
    if not isinstance(request, dict):
        raise ValueError('Use the current task request object')
    if request.get('delivery', 'local') not in ('local', 'upload_private'):
        raise ValueError('Preserve the actual local/upload_private delivery choice')
    operation = request.get('operation', 'edit')
    if operation not in ('edit', 'assemble', 'optimize'):
        raise ValueError('operation must describe the actual edit/assemble/optimize request')
    config = request.get('optimization') or {}
    if not isinstance(config, dict):
        raise ValueError('optimization must be an object')
    mode = config.get('mode', 'auto')
    if mode not in ('auto', 'conservative', 'off'):
        raise ValueError('optimization.mode must be auto/conservative/off')
    textures = config.get('textures', 'preserve')
    if textures not in ('preserve', 'selected_copies'):
        raise ValueError('textures must preserve inputs or target selected private copies')
    constraints = request.get('constraints') or {}
    if not isinstance(constraints, dict):
        raise ValueError('constraints must preserve the explicit task constraints')
    allow_build = constraints.get('allow_sdk_build', True)
    if type(allow_build) is not bool:
        raise ValueError('allow_sdk_build must be a boolean when explicitly supplied')
    enabled = mode == 'conservative' or mode == 'auto' and operation in ('assemble', 'optimize')
    previous = request.get('performance_optimization') or {}
    if not isinstance(previous, dict):
        raise ValueError('Preserve the existing performance_optimization record')
    candidate = request.get('candidate') or {}
    if not isinstance(candidate, dict):
        candidate = {}
    action = 'optimize_once_after_assembly' if enabled else 'no_new_optimization'
    if enabled and previous.get('state') in ('building', 'scheduled', 'processing'):
        action = 'read_existing_receipt_or_wait_existing_job'
    if enabled and previous.get('state') == 'complete':
        recorded = previous.get('candidate_sha256')
        current = candidate.get('sha256')
        action = ('reuse_completed_for_current_candidate' if recorded and recorded == current
                  else 'read_completed_record_identity_before_dispatch')
    fresh = enabled and action == 'optimize_once_after_assembly'
    return {
        'enabled': enabled, 'operation': operation, 'mode': mode, 'action': action,
        'when': 'after required assembly/controls, before freezing final candidate',
        'aao': 'reuse installed Trace And Optimize on the private authoring candidate',
        'texture_policy': textures, 'fixed_texture_resolution': None,
        'standalone_sdk_build': fresh and allow_build and request.get('delivery', 'local') == 'local',
        'reuse_identical_upload_build': fresh and allow_build and request.get('delivery') == 'upload_private',
        'baseline_build_required': False, 'build_and_test': False, 'upload': False,
        'install_or_upgrade_dependencies': False, 'model_or_mcp_config_changes': False,
        'affected_checks': ['normal-material default view', 'changed texture closeup at matching camera',
                            'real affected outfit/part controls', 'affected body/pose restoration'],
        'preserve': ['author assets and old candidate', 'face/body/primary hair quality',
                     'expression parameter type/default/saved/sync semantics', 'author menu and visible features'],
        'executed': False, 'visual_verified': False,
    }


def project_path(value):
    project = Path(value).resolve(strict=True)
    if not (project / 'Assets').is_dir() or not (project / 'ProjectSettings').is_dir():
        raise ValueError('Use the exact selected Unity project')
    return project


def evidence_output(project, value):
    output = Path(value).absolute()
    resolved = output.resolve()
    if not resolved.is_relative_to(project / '.avatar-assembly'):
        raise ValueError('Save helper output in the selected project/.avatar-assembly')
    if any(path.is_symlink() for path in (output, *output.parents)):
        raise ValueError('Symlink evidence output refused')
    if output.exists():
        raise ValueError('Existing receipt/output: inspect it rather than overwrite evidence')
    return output


def build_input(project, receipt_path, metrics_path=None):
    receipt_path = Path(receipt_path).resolve(strict=True)
    receipt = read(receipt_path)
    if receipt.get('status') != 'success' or receipt.get('error'):
        raise ValueError('Build is not successful/terminal; read or wait the existing job')
    bundle = Path(receipt['bundle']).resolve(strict=True)
    size = receipt.get('compressedBytes')
    if type(size) is not int or size <= 0 or bundle.stat().st_size != size:
        raise ValueError('Receipt size does not match the actual built bundle')
    source = (project / receipt['source']).resolve(strict=True)
    if not source.is_relative_to(project / 'Assets'):
        raise ValueError('Candidate input must belong to the selected project Assets')
    candidate_sha = sha(source)
    if receipt.get('candidate_sha256') and receipt['candidate_sha256'] != candidate_sha:
        raise ValueError('Build candidate identity has changed; do not bind it to the new candidate')
    bundle_sha = sha(bundle)
    if receipt.get('bundle_sha256') and receipt['bundle_sha256'] != bundle_sha:
        raise ValueError('Built bundle changed since the receipt')
    metrics = receipt.get('metrics')
    metrics_source = {'kind': 'build_receipt', 'path': str(receipt_path)}
    if metrics_path:
        sidecar_path = Path(metrics_path).resolve(strict=True)
        sidecar = read(sidecar_path)
        if sidecar.get('platform') and sidecar['platform'] != receipt.get('platform'):
            raise ValueError('Metrics are for a different target platform')
        if sidecar.get('sdk_version') and receipt.get('sdk_version') and sidecar['sdk_version'] != receipt['sdk_version']:
            raise ValueError('Metrics and build use different SDK versions')
        if sidecar.get('bundle_sha256') and sidecar['bundle_sha256'] != bundle_sha:
            raise ValueError('Metrics are bound to a different built bundle')
        metrics = sidecar['metrics']
        metrics_source = {'kind': 'separate_built_bundle_stats', 'path': str(sidecar_path),
                          'sha256': sha(sidecar_path), 'declared_source': sidecar.get('source'),
                          'bundle_binding_in_original_record': bool(sidecar.get('bundle_sha256'))}
    if not isinstance(metrics, dict):
        raise ValueError('Metrics missing: read the built bundle with the installed SDK; do not invent them')
    return {'receipt': receipt, 'receipt_path': str(receipt_path), 'receipt_sha256': sha(receipt_path),
            'source': str(source), 'current_source_sha256': candidate_sha,
            'candidate_binding_in_original_receipt': bool(receipt.get('candidate_sha256')),
            'bundle': str(bundle), 'bundle_sha256': bundle_sha, 'compressed_bytes': size,
            'metrics': metrics, 'metrics_source': metrics_source}


def numeric_metrics(value, prefix=''):
    result = {}
    for key, item in value.items():
        name = prefix + key
        if isinstance(item, dict):
            result.update(numeric_metrics(item, name + '.'))
        elif type(item) in (int, float):
            if not math.isfinite(item) or item < 0:
                raise ValueError('Invalid SDK metric: ' + name)
            result[name] = item
    return result


def delta(before, after):
    return {'before': before, 'after': after, 'delta': after - before,
            'reduction_percent': 100 * (1 - after / before) if before else None}


def compare(project, before_path, after_path, before_metrics=None, after_metrics=None):
    before = build_input(project, before_path, before_metrics)
    after = build_input(project, after_path, after_metrics)
    platform = before['receipt'].get('platform')
    if not platform or platform != after['receipt'].get('platform'):
        raise ValueError('Never compare or reuse SDK measurements across target platforms')
    old_sdk = before['receipt'].get('sdk_version')
    new_sdk = after['receipt'].get('sdk_version')
    if old_sdk and new_sdk and old_sdk != new_sdk:
        raise ValueError('SDK versions differ; do not present this as an optimization-only delta')
    first, second = numeric_metrics(before['metrics']), numeric_metrics(after['metrics'])
    return {'status': 'MEASURED_COMPARISON', 'platform': platform,
            'sdk_version': new_sdk if new_sdk == old_sdk else None,
            'sdk_version_binding_in_receipts': bool(new_sdk and old_sdk),
            'before': before, 'after': after,
            'compressed_bytes': delta(before['compressed_bytes'], after['compressed_bytes']),
            'metrics': {key: delta(first[key], second[key]) for key in sorted(first.keys() & second.keys())},
            'rank': {'before': before['metrics'].get('rank'), 'after': after['metrics'].get('rank')},
            'visual_verified': False, 'client_tested': False, 'avatar_accepted': False,
            'note': 'Supplied real receipt/file comparison only. Missing original identity/version fields stay unverified. Visual and client conclusions require actual observations.'}


def sdk_stats_command(project, bundle, output):
    bundle = Path(bundle).resolve(strict=True)
    output = evidence_output(project, output)
    template = Path(__file__).parent / 'unity/read_built_stats.cs'
    code = template.read_text(encoding='utf-8')
    for token, path in [('PROJECT', project), ('BUNDLE', bundle), ('OUTPUT', output)]:
        literal = '@"' + str(path).replace('"', '""') + '"'
        code = code.replace('__' + token + '_PATH_LITERAL__', literal)
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    command = sub.add_parser('plan')
    command.add_argument('--input', type=Path, required=True)
    command = sub.add_parser('compare')
    command.add_argument('--project', type=Path, required=True)
    command.add_argument('--before', type=Path, required=True)
    command.add_argument('--after', type=Path, required=True)
    command.add_argument('--before-metrics', type=Path)
    command.add_argument('--after-metrics', type=Path)
    command.add_argument('--output', type=Path, required=True)
    command = sub.add_parser('sdk-stats-command')
    command.add_argument('--project', type=Path, required=True)
    command.add_argument('--bundle', type=Path, required=True)
    command.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == 'plan':
            print(json.dumps(plan(read(args.input)), ensure_ascii=False, indent=2))
        else:
            project = project_path(args.project)
            if args.command == 'sdk-stats-command':
                print(sdk_stats_command(project, args.bundle, args.output))
            else:
                output = evidence_output(project, args.output)
                result = compare(project, args.before, args.after, args.before_metrics, args.after_metrics)
                output.parent.mkdir(parents=True, exist_ok=True)
                with output.open('x', encoding='utf-8') as stream:
                    json.dump(result, stream, ensure_ascii=False, indent=2, allow_nan=False)
                    stream.write('\n')
                print(json.dumps({'status': result['status'], 'output': str(output),
                                  'compressed_bytes': result['compressed_bytes'], 'avatar_accepted': False}, ensure_ascii=False))
        return 0
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(json.dumps({'status': 'HELPER_NOT_APPLIED', 'error': str(error),
                          'unity_modified': False, 'next': 'Read the existing receipt/actual artifact; do not redispatch a build.'}, ensure_ascii=False))
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
