#!/usr/bin/env python3
"""Small decision helpers. No Unity mutation, model call, network, or auto-PASS.

These checks inspect recorded evidence only. They cannot authenticate screenshots
or prove that an agent actually observed an avatar. Use the live Unity workflow.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import re
from datetime import datetime
from pathlib import Path
from typing import Any


SIZE_METRICS = ('deliverable_extracted_bytes', 'sdk_uncompressed_bytes')
RESOURCE_REQUIREMENTS = ('required', 'optional')
PERFORMANCE_PLATFORMS = ('PC', 'Android')
PERFORMANCE_POLICY_MODES = ('measured_best_effort', 'rank_gate', 'metric_caps')
PERFORMANCE_RANKS = ('VeryPoor', 'Poor', 'Medium', 'Good', 'Excellent')
PERFORMANCE_TARGET_RANKS = ('Medium', 'Good', 'Excellent')
PERFORMANCE_RANK_SCORE = {rank: index for index, rank in enumerate(PERFORMANCE_RANKS)}
PERFORMANCE_RANK_HANDLING = ('block', 'accept_with_warning')
PERFORMANCE_METRICS = (
    'triangles', 'skinned_mesh_renderers', 'basic_mesh_renderers',
    'material_slots', 'animators', 'bones', 'lights',
    'texture_memory_bytes', 'physbone_components', 'physbone_transforms',
    'physbone_colliders', 'physbone_collision_checks', 'contacts',
    'constraints', 'constraint_depth', 'particle_systems', 'active_particles',
    'mesh_particle_triangles', 'trail_renderers', 'line_renderers', 'raycasts',
    'cloth_components', 'cloth_vertices', 'physics_colliders', 'rigidbodies',
    'audio_sources', 'expression_parameter_bits',
    'bounds_x_m', 'bounds_y_m', 'bounds_z_m',
)
PERFORMANCE_BOOLEAN_METRICS = ('particle_trails_enabled',
                               'particle_collision_enabled')
PERFORMANCE_PLATFORM_SIZE_METRICS = ('sdk_download_bytes',
                                     'sdk_uncompressed_bytes')
SDK_HARD_METRIC_LIMITS = {
    'expression_parameter_bits': 256,
    'physbone_components': 256,
    'physbone_colliders': 256,
    'contacts': 256,
    'raycasts': 80,
}
MOBILE_HARD_COMPONENT_LIMITS = {
    'physbone_components': 8,
    'physbone_transforms': 64,
    'physbone_colliders': 16,
    'physbone_collision_checks': 64,
    'contacts': 16,
    'constraints': 150,
    'constraint_depth': 50,
}
PLATFORM_BUNDLE_LIMITS = {
    'PC': {'sdk_download_bytes': 200_000_000,
           'sdk_uncompressed_bytes': 500_000_000},
    'Android': {'sdk_download_bytes': 10_000_000,
                'sdk_uncompressed_bytes': 40_000_000},
}
SAFE_PERFORMANCE_ACTIONS = (
    'selected_variants_only',
    'generated_copy_texture_limits',
    'remove_proven_unreferenced_generated_assets',
    'semantic_safe_dedupe',
    'aao_proven_unused_only',
)
SEMANTIC_RETEST_ACTIONS = ('semantic_safe_dedupe', 'aao_proven_unused_only')
PERFORMANCE_ACTION_STATUSES = ('APPLIED', 'CHECKED_NOT_APPLICABLE')
PERFORMANCE_JOB_TERMINAL_STATUS = 'SUCCEEDED'
PERFORMANCE_JOB_TIMEOUT_ACTION = 'poll_same_job'
SAFE_OPTIMIZATION_POLICY = {
    'copy_scope': 'task_generated_copies_only',
    'preserve_author_sources': True,
    'texture_max_size': {
        'clothing_main_color': 1024,
        'clothing_normal': 1024,
        'mask_ao': 512,
        'reflection_cubemap': 256,
    },
    'protected_quality': ('face', 'eyes', 'skin', 'primary_hair'),
    'aao_cleanup': 'proven_unused_only',
    'auto_apply': True,
    'auto_continue_after_success': True,
}
TERMINAL_UPLOAD_STATUSES = ('PRIVATE_UPLOAD_CONFIRMED', 'UPLOAD_CONFIRMED')
DELIVERY_LAYERS = ('local_prefab', 'exported_package', 'sdk_build',
                   'client_runtime', 'private_upload')
CLOTHING_KINDS = ('outfit', 'clothing', 'garment')
BREAST_ADJUSTMENT_METHODS = ('author_native', 'ma_sync', 'validated_profile')
DEFAULT_REQUESTED_CHEST_STATES = ('small', 'default', 'large')
OUTFIT_MENU_ACTIVATION_ROLES = ('outfit_activate', 'outfit_wear')
OUTFIT_PART_CONTROL_ROLES = ('outfit_part_control', 'outfit_part_toggle')
OUTFIT_MENU_ACTION_ROLES = (*OUTFIT_MENU_ACTIVATION_ROLES,
                            *OUTFIT_PART_CONTROL_ROLES)
OUTFIT_PARTS_MENU_ROLE = 'outfit_parts_menu'
OUTFIT_MENU_STRATEGY = 'provider_parts_menu_first'
OUTFIT_PART_RETURN_STATE_POLICIES = ('preserve_user_choice',
                                     'restore_provider_default')
OUTFIT_PART_RETURN_STATE_SOURCES = ('provider', 'user')
NON_OUTFIT_MENU_ROLES = (
    'non_outfit_eye', 'non_outfit_action', 'non_outfit_function',
    'non_outfit_setting', 'non_outfit_body', 'non_outfit_face',
    'non_outfit_hair', 'non_outfit_plugin',
)
OUTFIT_MENU_STATUS_STRATEGY = {
    'usable': 'reuse_provider_parts_menu',
    'incomplete': 'complete_provider_parts_menu',
    'absent': 'minimal_parts_wrapper',
    'unusable': 'minimal_parts_wrapper',
}
CHEST_OBSERVATION_LAYER = 'post_ndmf_processed_candidate'
CHEST_OBSERVATION_OK_STATUS = 'LOCAL_OK'
CHEST_OBSERVATION_FAIL_STATUSES = ('LOCAL_FAIL', 'FAILED', 'REJECTED')
INSTALLING_RESOURCE_STATES = (
    'ADMITTED', 'INSTALLING', 'INSTALLED_UNVERIFIED', 'SMOKE_FAILED',
    'ROLLED_BACK', 'REJECTED', 'SKIPPED',
)
MENU_CONTROL_TYPE_ALIASES = {
    'button': 'button',
    'toggle': 'toggle',
    'submenu': 'submenu',
    'radialpuppet': 'radial_puppet',
    'twoaxispuppet': 'two_axis_puppet',
    'fouraxispuppet': 'four_axis_puppet',
}


def _nonempty(value: Any) -> bool:
    return isinstance(value, str) and bool(value.strip())


def _valid_revision(value: Any) -> bool:
    return (isinstance(value, int) and not isinstance(value, bool) and value > 0) or _nonempty(value)


def _valid_sha256(value: Any) -> bool:
    return _nonempty(value) and re.fullmatch(r'[0-9a-fA-F]{64}', value) is not None


def _candidate_context(task: dict[str, Any]) -> tuple[str, Any, str]:
    return task.get('candidate', ''), task.get('candidate_revision'), task.get('candidate_sha256', '')


def performance_job_id(
    platform: str, candidate: str, candidate_revision: Any,
    candidate_sha256: str, sdk_version: str,
) -> str:
    """Canonical formal-measurement identity; changing any input creates a new job."""
    identity = {
        'platform': platform,
        'candidate': candidate,
        'candidate_revision': candidate_revision,
        'candidate_sha256': candidate_sha256.lower(),
        'sdk_version': sdk_version,
    }
    encoded = json.dumps(identity, sort_keys=True, ensure_ascii=False,
                         separators=(',', ':')).encode('utf-8')
    return f'perf-{hashlib.sha256(encoded).hexdigest()}'


def baseline_receipt_sha256(
    platform: str, candidate: str, candidate_revision: Any,
    candidate_sha256: str, sdk_version: str, measured_utc: str,
    performance_report_sources: list[str], build_report_sources: list[str],
) -> str:
    """Canonical pre-install baseline lock over identity, time and report sources."""
    identity = {
        'platform': platform,
        'candidate': candidate,
        'candidate_revision': candidate_revision,
        'candidate_sha256': candidate_sha256.lower(),
        'sdk_version': sdk_version,
        'measured_utc': measured_utc,
        'frozen_at_phase': 'PREFLIGHT',
        'performance_report_sources': sorted(performance_report_sources),
        'build_report_sources': sorted(build_report_sources),
    }
    encoded = json.dumps(identity, sort_keys=True, ensure_ascii=False,
                         separators=(',', ':')).encode('utf-8')
    return hashlib.sha256(encoded).hexdigest()


def _utc_timestamp(value: Any) -> datetime | None:
    if not _nonempty(value) or not value.endswith('Z'):
        return None
    try:
        parsed = datetime.fromisoformat(value[:-1] + '+00:00')
    except ValueError:
        return None
    return parsed if parsed.utcoffset() is not None else None


def _evidence_is_current(
    evidence: dict[str, Any], candidate: str, candidate_revision: Any, candidate_sha256: str
) -> bool:
    """Evidence is fresh only when it names the frozen candidate revision and hash.

    Old files may remain in the task directory, but they cannot directly satisfy a
    current gate. Unaffected evidence can be retained by recording a new observation
    against the current context after it is reviewed.
    """
    return (
        isinstance(evidence, dict)
        and evidence.get('candidate') == candidate
        and evidence.get('candidate_revision') == candidate_revision
        and isinstance(evidence.get('candidate_sha256'), str)
        and evidence['candidate_sha256'].lower() == candidate_sha256.lower()
    )


def validate_optimization_policy(policy: Any, prefix: str = 'base_optimization') -> list[str]:
    """Return structural/safety errors for the pre-authorized copy-only policy."""
    if not isinstance(policy, dict):
        return [prefix]
    errors: list[str] = []
    if policy.get('copy_scope') != SAFE_OPTIMIZATION_POLICY['copy_scope']:
        errors.append(f'{prefix}.copy_scope')
    if policy.get('preserve_author_sources') is not True:
        errors.append(f'{prefix}.preserve_author_sources')
    limits = policy.get('texture_max_size')
    if not isinstance(limits, dict):
        errors.append(f'{prefix}.texture_max_size')
    else:
        for name, maximum in SAFE_OPTIMIZATION_POLICY['texture_max_size'].items():
            value = limits.get(name)
            if not isinstance(value, int) or isinstance(value, bool) or value != maximum:
                errors.append(f'{prefix}.texture_max_size.{name}')
    protected = policy.get('protected_quality')
    if not isinstance(protected, list) or not set(SAFE_OPTIMIZATION_POLICY['protected_quality']).issubset(protected):
        errors.append(f'{prefix}.protected_quality')
    if policy.get('aao_cleanup') != SAFE_OPTIMIZATION_POLICY['aao_cleanup']:
        errors.append(f'{prefix}.aao_cleanup')
    if policy.get('auto_apply') is not True:
        errors.append(f'{prefix}.auto_apply')
    if policy.get('auto_continue_after_success') is not True:
        errors.append(f'{prefix}.auto_continue_after_success')
    return errors


def validate_content_priority(priority: Any, manifest: Any = None) -> list[str]:
    """Validate required/optional scope and frozen budget/chest handling."""
    if not isinstance(priority, dict) or not priority:
        return ['content_priority']
    errors: list[str] = []
    lists: dict[str, list[str]] = {}
    for name in ('required_resource_ids', 'optional_resource_ids'):
        value = priority.get(name)
        if (not isinstance(value, list)
                or any(not _nonempty(item) for item in value)
                or len(value) != len(set(value))):
            errors.append(f'content_priority.{name}')
        else:
            lists[name] = value
    required = set(lists.get('required_resource_ids', []))
    optional = set(lists.get('optional_resource_ids', []))
    if required.intersection(optional):
        errors.append('content_priority.required_optional_must_be_disjoint')
    if not _nonempty(priority.get('optional_failure_action')):
        errors.append('content_priority.optional_failure_action')
    if not _nonempty(priority.get('extra_over_budget_action')):
        errors.append('content_priority.extra_over_budget_action')
    requested_states = priority.get('requested_affected_chest_states')
    requested_valid = (isinstance(requested_states, list) and bool(requested_states)
                       and all(_nonempty(item) for item in requested_states)
                       and len(requested_states) == len(set(requested_states)))
    if not requested_valid:
        errors.append('content_priority.requested_affected_chest_states')
    chest_states = priority.get('affected_chest_states')
    affected_valid = (isinstance(chest_states, list) and bool(chest_states)
                      and all(_nonempty(item) for item in chest_states)
                      and len(chest_states) == len(set(chest_states)))
    if not affected_valid:
        errors.append('content_priority.affected_chest_states')
    elif requested_valid and chest_states != requested_states:
        errors.append('content_priority.affected_chest_states_must_match_requested')

    override = priority.get('requested_affected_chest_states_override')
    if requested_valid and tuple(requested_states) != DEFAULT_REQUESTED_CHEST_STATES:
        if (not isinstance(override, dict) or override.get('explicit') is not True
                or not _nonempty(override.get('source'))):
            errors.append(
                'content_priority.requested_affected_chest_states_explicit_user_override_required')
    elif override is not None:
        if (not isinstance(override, dict)
                or not isinstance(override.get('explicit'), bool)
                or (override.get('explicit') is True and not _nonempty(override.get('source')))):
            errors.append('content_priority.requested_affected_chest_states_override')

    manifest_items = manifest.get('resources') if isinstance(manifest, dict) else None
    if isinstance(manifest_items, list) and not errors:
        manifest_required = {item.get('id') for item in manifest_items
                             if isinstance(item, dict) and item.get('requirement') == 'required'}
        manifest_optional = {item.get('id') for item in manifest_items
                             if isinstance(item, dict) and item.get('requirement') == 'optional'}
        if required != manifest_required:
            errors.append('content_priority.required_resource_ids_manifest_mismatch')
        if optional != manifest_optional:
            errors.append('content_priority.optional_resource_ids_manifest_mismatch')
    return errors


def validate_delivery_layers(task: dict[str, Any], card: dict[str, Any]) -> list[str]:
    """Validate delivery vocabulary and upload authority consistency."""
    layers = card.get('delivery_layers')
    if (not isinstance(layers, list) or not layers
            or any(layer not in DELIVERY_LAYERS for layer in layers)
            or len(layers) != len(set(layers))):
        return ['delivery_layers']
    errors: list[str] = []
    mode = task.get('mode')
    if mode not in ('workflow_test', 'assemble', 'assemble_upload'):
        errors.append('mode')
    card_authority = card.get('upload_authority')
    if not isinstance(card_authority, dict):
        errors.append('start_contract.upload_authority')
        card_authority = {}
    if not isinstance(card_authority.get('authorized'), bool):
        errors.append('start_contract.upload_authority.authorized')
    elif card_authority['authorized'] is not task.get('upload_authorized'):
        errors.append('start_contract.upload_authority.authorized_mismatch')
    if not isinstance(card_authority.get('private'), bool):
        errors.append('start_contract.upload_authority.private')
    elif card_authority['private'] is not task.get('private'):
        errors.append('start_contract.upload_authority.private_mismatch')

    wants_upload = 'private_upload' in layers
    if wants_upload:
        if mode != 'assemble_upload':
            errors.append('delivery_layers.private_upload_requires_assemble_upload')
        if task.get('upload_authorized') is not True:
            errors.append('delivery_layers.private_upload_requires_authority')
        if task.get('private') is not True:
            errors.append('delivery_layers.private_upload_requires_private')
    elif mode == 'assemble_upload' or task.get('upload_authorized') is True:
        errors.append('delivery_layers.private_upload_missing_for_upload_scope')
    return errors


def _platforms_from_manifest(manifest: Any) -> list[str] | None:
    """Return discovery-recorded target platforms; this is not a sixth question."""
    if not isinstance(manifest, dict):
        return None
    value = manifest.get('target_platform')
    if (not isinstance(value, list) or not value
            or any(platform not in PERFORMANCE_PLATFORMS for platform in value)
            or len(value) != len(set(value))):
        return None
    return value


def _sdk_context_errors(manifest: Any) -> list[str]:
    """Validate the read-only current SDK anchor; contracts cannot self-declare it."""
    prefix = 'assembly_manifest.sdk_context'
    if not isinstance(manifest, dict):
        return [prefix]
    context = manifest.get('sdk_context')
    if not isinstance(context, dict):
        return [prefix]
    errors: list[str] = []
    if not _nonempty(context.get('current_vrcsdk_version')):
        errors.append(f'{prefix}.current_vrcsdk_version')
    if context.get('authoritative') is not True:
        errors.append(f'{prefix}.authoritative')
    if not _nonempty(context.get('source')):
        errors.append(f'{prefix}.source')
    evidence = context.get('evidence')
    if (not isinstance(evidence, list) or not evidence
            or not all(isinstance(item, dict)
                       and _nonempty(item.get('source'))
                       and item.get('observed') is True
                       for item in evidence)):
        errors.append(f'{prefix}.evidence')
    return errors


def _current_vrcsdk_version(manifest: Any) -> str | None:
    if _sdk_context_errors(manifest):
        return None
    return manifest['sdk_context']['current_vrcsdk_version']


def validate_performance_contract(contract: Any, manifest: Any = None) -> list[str]:
    """Validate the frozen platform policy without requiring final measurements at intake."""
    if not isinstance(contract, dict) or not contract:
        return ['performance_contract']
    errors: list[str] = []
    mode = contract.get('policy_mode')
    if mode not in PERFORMANCE_POLICY_MODES:
        errors.append('performance_contract.policy_mode')
    platforms = contract.get('platforms')
    platforms_valid = (isinstance(platforms, list) and bool(platforms)
                       and all(platform in PERFORMANCE_PLATFORMS for platform in platforms)
                       and len(platforms) == len(set(platforms)))
    if not platforms_valid:
        errors.append('performance_contract.platforms')
        platforms = []
    discovered = _platforms_from_manifest(manifest)
    if manifest is not None and discovered is None:
        errors.append('assembly_manifest.target_platform')
    elif platforms_valid and discovered is not None and platforms != discovered:
        errors.append('performance_contract.platforms_manifest_mismatch')
    for gate in ('gate_completion', 'gate_upload'):
        if not isinstance(contract.get(gate), bool):
            errors.append(f'performance_contract.{gate}')

    target_rank = contract.get('target_rank')
    if not isinstance(target_rank, dict):
        errors.append('performance_contract.target_rank')
        target_rank = {}
    elif any(platform not in PERFORMANCE_PLATFORMS or rank not in PERFORMANCE_TARGET_RANKS
             for platform, rank in target_rank.items()):
        errors.append('performance_contract.target_rank')
    metric_caps = contract.get('metric_caps')
    if not isinstance(metric_caps, dict):
        errors.append('performance_contract.metric_caps')
        metric_caps = {}
    else:
        for platform, caps in metric_caps.items():
            if (platform not in PERFORMANCE_PLATFORMS or not isinstance(caps, dict)
                    or not caps or any(metric not in PERFORMANCE_METRICS
                                       or not isinstance(limit, int)
                                       or isinstance(limit, bool) or limit < 0
                                       for metric, limit in caps.items())):
                errors.append(f'performance_contract.metric_caps.{platform}')
    if mode == 'rank_gate' and (not platforms or any(platform not in target_rank
                                                     for platform in platforms)):
        errors.append('performance_contract.target_rank_required_for_each_platform')
    if mode == 'metric_caps' and (not platforms or any(platform not in metric_caps
                                                       for platform in platforms)):
        errors.append('performance_contract.metric_caps_required_for_each_platform')
    if mode == 'measured_best_effort' and platforms:
        for platform in platforms:
            caps = metric_caps.get(platform)
            if platform not in target_rank and not (isinstance(caps, dict) and caps):
                errors.append(
                    f'performance_contract.reporting_target_required.{platform}')

    rank_handling = contract.get('rank_handling')
    if not isinstance(rank_handling, dict):
        errors.append('performance_contract.rank_handling')
    else:
        for platform in platforms:
            handling = rank_handling.get(platform)
            if (not isinstance(handling, dict)
                    or handling.get('Poor') not in PERFORMANCE_RANK_HANDLING
                    or handling.get('VeryPoor') not in PERFORMANCE_RANK_HANDLING):
                errors.append(f'performance_contract.rank_handling.{platform}')

    actions = contract.get('allowed_actions')
    if (not isinstance(actions, list) or not actions
            or len(actions) != len(set(actions))
            or any(action not in SAFE_PERFORMANCE_ACTIONS for action in actions)):
        errors.append('performance_contract.allowed_actions')
    protected = contract.get('protected_features')
    if (not isinstance(protected, list) or not protected
            or any(not _nonempty(item) for item in protected)
            or len(protected) != len(set(protected))):
        errors.append('performance_contract.protected_features')
    headroom = contract.get('headroom_policy')
    if not isinstance(headroom, dict):
        errors.append('performance_contract.headroom_policy')
    else:
        for gate in ('gate_completion', 'gate_upload'):
            if not isinstance(headroom.get(gate), bool):
                errors.append(f'performance_contract.headroom_policy.{gate}')
        headroom_platforms = headroom.get('platforms')
        if not isinstance(headroom_platforms, dict):
            errors.append('performance_contract.headroom_policy.platforms')
        else:
            for platform in platforms:
                platform_policy = headroom_platforms.get(platform)
                if not isinstance(platform_policy, dict):
                    errors.append(
                        f'performance_contract.headroom_policy.platforms.{platform}')
                    continue
                for metric in ('expression_parameter_bits', 'physbone_components'):
                    specification = platform_policy.get(metric)
                    if (not isinstance(specification, dict)
                            or not isinstance(specification.get('limit'), int)
                            or isinstance(specification.get('limit'), bool)
                            or specification.get('limit', 0) <= 0
                            or not isinstance(specification.get('reserve'), int)
                            or isinstance(specification.get('reserve'), bool)
                            or specification.get('reserve', 0) <= 0
                            or specification['reserve'] >= specification['limit']):
                        errors.append(
                            f'performance_contract.headroom_policy.platforms.'
                            f'{platform}.{metric}')
                for metric, specification in platform_policy.items():
                    if (metric not in PERFORMANCE_METRICS
                            or not isinstance(specification, dict)
                            or not isinstance(specification.get('limit'), int)
                            or isinstance(specification.get('limit'), bool)
                            or specification.get('limit', 0) <= 0
                            or not isinstance(specification.get('reserve'), int)
                            or isinstance(specification.get('reserve'), bool)
                            or specification.get('reserve', 0) <= 0
                            or specification['reserve'] >= specification['limit']):
                        errors.append(
                            f'performance_contract.headroom_policy.platforms.'
                            f'{platform}.{metric}')
                    elif isinstance(specification, dict):
                        expected_limit = (
                            MOBILE_HARD_COMPONENT_LIMITS.get(metric)
                            if platform == 'Android' else None)
                        if expected_limit is None:
                            expected_limit = SDK_HARD_METRIC_LIMITS.get(metric)
                        if (expected_limit is not None
                                and specification.get('limit') != expected_limit):
                            errors.append(
                                'performance_contract.headroom_policy.platforms.'
                                f'{platform}.{metric}.limit')
    for name in ('baseline_measurements', 'final_measurements'):
        if not isinstance(contract.get(name), dict):
            errors.append(f'performance_contract.{name}')
    evidence = contract.get('evidence')
    if (not isinstance(evidence, list) or not evidence
            or any(not (_nonempty(item) if isinstance(item, str)
                        else isinstance(item, dict) and bool(item))
                   for item in evidence)):
        errors.append('performance_contract.evidence')
    return errors


def _normalize_control_type(value: Any) -> str | None:
    if not _nonempty(value):
        return None
    token = re.sub(r'[^a-z0-9]', '', value.lower())
    return MENU_CONTROL_TYPE_ALIASES.get(token)


def _canonical_menu_control(control: dict[str, Any]) -> dict[str, Any]:
    """Keep only fields that must survive provider/NDMF processing unchanged."""
    kind = _normalize_control_type(control.get('type'))
    value: dict[str, Any] = {
        'id': control.get('id'),
        'label': control.get('label'),
        'label_language': control.get('label_language'),
        'type': kind,
        'provider_id': control.get('provider_id'),
        'scope': control.get('scope'),
    }
    if kind == 'submenu':
        value['submenu_page_id'] = control.get('submenu_page_id')
    elif kind in ('two_axis_puppet', 'four_axis_puppet'):
        value['puppet_parameters'] = control.get('puppet_parameters')
    else:
        value['internal_parameter'] = control.get('internal_parameter')
    # Resource semantics are optional for generic controls, but when planned
    # they are part of the immutable plan -> post-NDMF comparison.
    if 'resource_id' in control or 'semantic_role' in control:
        value['resource_id'] = control.get('resource_id')
        value['semantic_role'] = control.get('semantic_role')
    if control.get('semantic_role') in OUTFIT_PART_CONTROL_ROLES:
        value['return_state_policy'] = control.get('return_state_policy')
        value['return_state_policy_source'] = control.get('return_state_policy_source')
        value['return_state_policy_evidence'] = control.get('return_state_policy_evidence')
    return value


def _menu_plan_snapshot(plan: dict[str, Any]) -> dict[str, Any]:
    """Return the exact planned pages, controls and reachable control paths."""
    pages = {page['id']: page for page in plan['pages']}
    root = plan.get('root_page_id', 'root')
    counts = {page_id: len(page['controls']) for page_id, page in pages.items()}
    ids = {page_id: [control['id'] for control in page['controls']]
           for page_id, page in pages.items()}
    controls = {page_id: [_canonical_menu_control(control) for control in page['controls']]
                for page_id, page in pages.items()}
    paths: list[str] = []

    def walk(page_id: str, prefix: list[str]) -> None:
        for control in pages[page_id]['controls']:
            control_path = '/'.join((*prefix, control['id']))
            paths.append(control_path)
            if _normalize_control_type(control.get('type')) == 'submenu':
                walk(control['submenu_page_id'], [*prefix, control['id']])

    walk(root, [root])
    return {
        'page_control_counts': counts,
        'observed_control_ids': ids,
        'observed_controls': controls,
        'observed_paths': sorted(paths),
    }


def validate_menu_plan(plan: Any) -> list[str]:
    """Validate a localized provider-aware menu graph before Unity mutation."""
    if not isinstance(plan, dict):
        return ['menu_plan']
    errors: list[str] = []
    language = plan.get('visible_label_language')
    if not _nonempty(language):
        errors.append('menu_plan.visible_label_language')
    if plan.get('translate_internal_parameters') is not False:
        errors.append('menu_plan.translate_internal_parameters_must_be_false')
    if plan.get('clothing_integration_strategy') != OUTFIT_MENU_STRATEGY:
        errors.append('menu_plan.clothing_integration_strategy')
    if plan.get('allow_synthetic_universal_clothing_categories') is not False:
        errors.append('menu_plan.allow_synthetic_universal_clothing_categories_must_be_false')
    if plan.get('allow_parallel_outfit_selector_and_parts_trees') is not False:
        errors.append('menu_plan.allow_parallel_outfit_selector_and_parts_trees_must_be_false')
    budget = plan.get('control_budget')
    cap = budget.get('max_controls_per_page') if isinstance(budget, dict) else None
    if not isinstance(cap, int) or isinstance(cap, bool) or cap < 1 or cap > 8:
        errors.append('menu_plan.control_budget.max_controls_per_page')
        cap = 8

    providers = plan.get('provider_sources')
    provider_ids: set[str] = set()
    if not isinstance(providers, list) or not providers:
        errors.append('menu_plan.provider_sources')
    else:
        for index, provider in enumerate(providers):
            if (not isinstance(provider, dict) or not _nonempty(provider.get('id'))
                    or not _nonempty(provider.get('source'))):
                errors.append(f'menu_plan.provider_sources[{index}]')
                continue
            if provider['id'] in provider_ids:
                errors.append(f'menu_plan.provider_sources[{index}].duplicate_id')
            provider_ids.add(provider['id'])

    pages = plan.get('pages')
    pages_by_id: dict[str, dict[str, Any]] = {}
    controls: list[tuple[int, int, dict[str, Any]]] = []
    if not isinstance(pages, list) or not pages:
        errors.append('menu_plan.pages')
        pages = []
    for page_index, page in enumerate(pages):
        if not isinstance(page, dict) or not _nonempty(page.get('id')):
            errors.append(f'menu_plan.pages[{page_index}].id')
            continue
        if page['id'] in pages_by_id:
            errors.append(f'menu_plan.pages[{page_index}].duplicate_id')
            continue
        pages_by_id[page['id']] = page
        page_controls = page.get('controls')
        if not isinstance(page_controls, list):
            errors.append(f'menu_plan.pages[{page_index}].controls')
            continue
        if not page_controls:
            errors.append(f'menu_plan.pages[{page_index}].empty')
        if len(page_controls) > cap:
            errors.append(f'menu_plan.pages[{page_index}].controls_exceed_{cap}')
        for control_index, control in enumerate(page_controls):
            if not isinstance(control, dict):
                errors.append(f'menu_plan.pages[{page_index}].controls[{control_index}]')
                continue
            controls.append((page_index, control_index, control))

    root_page_id = plan.get('root_page_id', 'root')
    root_page = pages_by_id.get(root_page_id)
    if not isinstance(root_page, dict):
        errors.append('menu_plan.root_page_id')

    categories = plan.get('root_categories', [])
    if not isinstance(categories, list):
        errors.append('menu_plan.root_categories')
        categories = []
    elif len(categories) > cap:
        errors.append(f'menu_plan.root_categories_exceed_{cap}')
    category_ids: set[str] = set()
    for index, category in enumerate(categories):
        prefix = f'menu_plan.root_categories[{index}]'
        if not isinstance(category, dict):
            errors.append(prefix)
            continue
        for key in ('id', 'label', 'label_language', 'page_id'):
            if not _nonempty(category.get(key)):
                errors.append(f'{prefix}.{key}')
        if _nonempty(category.get('id')):
            if category['id'] in category_ids:
                errors.append(f'{prefix}.duplicate_id')
            category_ids.add(category['id'])
        if _nonempty(language) and category.get('label_language') != language:
            errors.append(f'{prefix}.label_language')
        if _nonempty(category.get('page_id')) and category['page_id'] not in pages_by_id:
            errors.append(f'{prefix}.page_id_unknown')

    adjacency: dict[str, list[str]] = {page_id: [] for page_id in pages_by_id}
    control_ids: set[str] = set()
    for page_index, control_index, control in controls:
        prefix = f'menu_plan.pages[{page_index}].controls[{control_index}]'
        for key in ('id', 'label', 'label_language', 'provider_id'):
            if not _nonempty(control.get(key)):
                errors.append(f'{prefix}.{key}')
        if _nonempty(control.get('id')):
            if control['id'] in control_ids:
                errors.append(f'{prefix}.duplicate_id')
            control_ids.add(control['id'])
        if _nonempty(language) and control.get('label_language') != language:
            errors.append(f'{prefix}.label_language')
        if _nonempty(control.get('provider_id')) and control['provider_id'] not in provider_ids:
            errors.append(f'{prefix}.provider_id_unknown')
        has_resource_binding = ('resource_id' in control
                                or (_nonempty(control.get('semantic_role'))
                                    and control.get('semantic_role')
                                    not in NON_OUTFIT_MENU_ROLES))
        if has_resource_binding:
            if not _nonempty(control.get('resource_id')):
                errors.append(f'{prefix}.resource_id')
            if not _nonempty(control.get('semantic_role')):
                errors.append(f'{prefix}.semantic_role')

        kind = _normalize_control_type(control.get('type'))
        if kind is None:
            errors.append(f'{prefix}.type')
            continue
        if kind == 'submenu':
            target = control.get('submenu_page_id')
            if not _nonempty(target) or target not in pages_by_id:
                errors.append(f'{prefix}.submenu_page_id')
            else:
                page = pages[page_index]
                if isinstance(page, dict) and _nonempty(page.get('id')):
                    adjacency.setdefault(page['id'], []).append(target)
        elif kind == 'two_axis_puppet':
            values = control.get('puppet_parameters')
            if not isinstance(values, list) or len(values) != 2 or any(not _nonempty(v) for v in values):
                errors.append(f'{prefix}.puppet_parameters')
        elif kind == 'four_axis_puppet':
            values = control.get('puppet_parameters')
            if not isinstance(values, list) or len(values) != 4 or any(not _nonempty(v) for v in values):
                errors.append(f'{prefix}.puppet_parameters')
        elif not _nonempty(control.get('internal_parameter')):
            errors.append(f'{prefix}.internal_parameter')

        if kind != 'submenu':
            if _nonempty(control.get('resource_id')):
                if not _nonempty(control.get('semantic_role')):
                    errors.append(f'{prefix}.resource_semantic_role')
                if control.get('scope') not in (None, 'resource'):
                    errors.append(f'{prefix}.scope')
            else:
                if control.get('scope') != 'non_outfit':
                    errors.append(f'{prefix}.non_outfit_scope_required')
                if control.get('semantic_role') not in NON_OUTFIT_MENU_ROLES:
                    errors.append(f'{prefix}.non_outfit_semantic_role')

        if control.get('semantic_role') in OUTFIT_PART_CONTROL_ROLES:
            if control.get('return_state_policy') not in OUTFIT_PART_RETURN_STATE_POLICIES:
                errors.append(f'{prefix}.return_state_policy')
            if control.get('return_state_policy_source') not in OUTFIT_PART_RETURN_STATE_SOURCES:
                errors.append(f'{prefix}.return_state_policy_source')
            return_evidence = control.get('return_state_policy_evidence')
            if (not isinstance(return_evidence, list) or not return_evidence
                    or any(not _nonempty(item) for item in return_evidence)):
                errors.append(f'{prefix}.return_state_policy_evidence')

    integrations = plan.get('outfit_menu_integrations')
    if not isinstance(integrations, list):
        errors.append('menu_plan.outfit_menu_integrations')
        integrations = []
    integrations_by_resource: dict[str, dict[str, Any]] = {}
    controls_by_id = {
        control.get('id'): (page_index, control)
        for page_index, _, control in controls if _nonempty(control.get('id'))
    }
    for index, integration in enumerate(integrations):
        prefix = f'menu_plan.outfit_menu_integrations[{index}]'
        if not isinstance(integration, dict):
            errors.append(prefix)
            continue
        for key in ('resource_id', 'provider_id', 'provider_parts_menu_status',
                    'strategy', 'menu_entry_control_id'):
            if not _nonempty(integration.get(key)):
                errors.append(f'{prefix}.{key}')
        resource_id = integration.get('resource_id')
        if _nonempty(resource_id):
            if resource_id in integrations_by_resource:
                errors.append(f'{prefix}.duplicate_resource_id')
            integrations_by_resource[resource_id] = integration
        provider_id = integration.get('provider_id')
        if _nonempty(provider_id) and provider_id not in provider_ids:
            errors.append(f'{prefix}.provider_id_unknown')
        status = integration.get('provider_parts_menu_status')
        strategy = integration.get('strategy')
        if status not in OUTFIT_MENU_STATUS_STRATEGY:
            errors.append(f'{prefix}.provider_parts_menu_status')
        elif strategy != OUTFIT_MENU_STATUS_STRATEGY[status]:
            errors.append(f'{prefix}.strategy')
        evidence = integration.get('evidence')
        if not isinstance(evidence, list) or not evidence:
            errors.append(f'{prefix}.evidence')
        added_ids = integration.get('added_control_ids')
        if not isinstance(added_ids, list) or any(not _nonempty(value) for value in added_ids):
            errors.append(f'{prefix}.added_control_ids')
            added_ids = []
        elif len(added_ids) != len(set(added_ids)):
            errors.append(f'{prefix}.added_control_ids_duplicate')
        if strategy == 'reuse_provider_parts_menu' and added_ids:
            errors.append(f'{prefix}.reuse_must_not_add_controls')
        if strategy in ('complete_provider_parts_menu', 'minimal_parts_wrapper') and not added_ids:
            errors.append(f'{prefix}.missing_minimal_added_control')

        entry_id = integration.get('menu_entry_control_id')
        entry_pair = controls_by_id.get(entry_id)
        if entry_pair is None:
            if _nonempty(entry_id):
                errors.append(f'{prefix}.menu_entry_control_id_unknown')
            continue
        _, entry = entry_pair
        if (_normalize_control_type(entry.get('type')) != 'submenu'
                or entry.get('resource_id') != resource_id
                or entry.get('semantic_role') != OUTFIT_PARTS_MENU_ROLE
                or entry.get('provider_id') != provider_id):
            errors.append(f'{prefix}.menu_entry_control_mismatch')
            continue

        target_page = entry.get('submenu_page_id')
        descendant_pages: set[str] = set()

        def collect_descendants(page_id: str) -> None:
            if page_id in descendant_pages:
                return
            descendant_pages.add(page_id)
            for next_page in adjacency.get(page_id, []):
                collect_descendants(next_page)

        if _nonempty(target_page) and target_page in pages_by_id:
            collect_descendants(target_page)
        descendant_controls = [
            control for page_id in descendant_pages
            for control in pages_by_id[page_id].get('controls', [])
            if isinstance(control, dict)
        ]
        actions = [
            control for control in descendant_controls
            if _normalize_control_type(control.get('type')) != 'submenu'
            and control.get('resource_id') == resource_id
            and control.get('semantic_role') in OUTFIT_MENU_ACTION_ROLES
        ]
        if not actions:
            errors.append(f'{prefix}.no_actionable_control_in_parts_menu')
        descendant_ids = {control.get('id') for control in descendant_controls}
        if any(control_id not in descendant_ids for control_id in added_ids):
            errors.append(f'{prefix}.added_control_outside_parts_menu')
        parallel_activation = any(
            control.get('resource_id') == resource_id
            and control.get('semantic_role') in OUTFIT_MENU_ACTIVATION_ROLES
            and control.get('id') not in descendant_ids
            for _, _, control in controls
        )
        if parallel_activation:
            errors.append(f'menu_plan.redundant_parallel_outfit_parts_tree.{resource_id}')
        provider_parameter_chains = {
            (control.get('provider_id'), control.get('internal_parameter'))
            for control in descendant_controls
            if (_normalize_control_type(control.get('type')) != 'submenu'
                and control.get('resource_id') == resource_id
                and control.get('semantic_role') in OUTFIT_MENU_ACTION_ROLES
                and _nonempty(control.get('provider_id'))
                and _nonempty(control.get('internal_parameter')))
        }
        duplicate_chain_outside = any(
            control.get('id') not in descendant_ids
            and (control.get('provider_id'), control.get('internal_parameter'))
            in provider_parameter_chains
            for _, _, control in controls
        )
        if duplicate_chain_outside:
            errors.append(f'menu_plan.redundant_parallel_outfit_parts_tree.{resource_id}')

    if isinstance(root_page, dict):
        root_targets = {control.get('submenu_page_id') for control in root_page.get('controls', [])
                        if isinstance(control, dict)
                        and _normalize_control_type(control.get('type')) == 'submenu'}
        for index, category in enumerate(categories):
            if isinstance(category, dict) and category.get('page_id') not in root_targets:
                errors.append(f'menu_plan.root_categories[{index}].root_entry_missing')

        reachable: set[str] = set()
        visiting: set[str] = set()
        cycle_pages: set[str] = set()

        def visit(page_id: str) -> None:
            if page_id in visiting:
                cycle_pages.add(page_id)
                return
            if page_id in reachable:
                return
            visiting.add(page_id)
            for target in adjacency.get(page_id, []):
                visit(target)
            visiting.remove(page_id)
            reachable.add(page_id)

        visit(root_page_id)
        for page_id in sorted(cycle_pages):
            errors.append(f'menu_plan.pages.cycle.{page_id}')
        for page_id in sorted(set(pages_by_id) - reachable):
            errors.append(f'menu_plan.pages.unreachable.{page_id}')
    return errors


def _selected_main_outfit_ids(
    task: dict[str, Any], *, installed_only: bool = False
) -> list[str]:
    """Return selected main outfits, excluding explicitly unavailable choices.

    Intake uses every selected required or optional main outfit. Final-result
    checks may additionally require the resource to have reached an installed
    state. A rejected/rolled-back/skipped item is no longer a selected result.
    """
    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    items = manifest.get('resources') if isinstance(manifest, dict) else []
    runtime = {item.get('id'): item for item in task.get('resources', [])
               if isinstance(item, dict) and _nonempty(item.get('id'))}
    result: list[str] = []
    for item in items if isinstance(items, list) else []:
        if not isinstance(item, dict) or not _nonempty(item.get('id')):
            continue
        actual = runtime.get(item['id'], {})
        selected = (item.get('selected', actual.get('selected', True)) is True
                    and actual.get('selected', True) is True)
        main = (item.get('kind') == 'outfit'
                or item.get('role') == 'main_outfit'
                or actual.get('role') == 'main_outfit'
                or item.get('main_outfit') is True
                or actual.get('main_outfit') is True)
        unavailable = actual.get('status') in ('REJECTED', 'ROLLED_BACK', 'SKIPPED')
        installed = actual.get('status') in ('INSTALLED_UNVERIFIED', 'LOCAL_OK')
        if selected and main and not unavailable and (installed or not installed_only):
            result.append(item['id'])
    return result


def _selected_main_outfit_menu_errors(
    task: dict[str, Any], plan: Any, *, installed_only: bool = False
) -> list[str]:
    """Require one provider-traced parts-menu entry per installed main outfit.

    ``validate_menu_plan`` proves every page is reachable from root. This
    task-aware check closes each installed main outfit to its recorded
    author/provider parts-menu integration by stable resource identity.
    """
    outfit_ids = _selected_main_outfit_ids(task, installed_only=installed_only)
    if not outfit_ids:
        return []
    integrations = plan.get('outfit_menu_integrations') if isinstance(plan, dict) else None
    integrated_ids = {
        item.get('resource_id') for item in integrations
        if isinstance(item, dict) and _nonempty(item.get('resource_id'))
    } if isinstance(integrations, list) else set()
    errors: list[str] = []
    for resource_id in outfit_ids:
        if resource_id not in integrated_ids:
            errors.append(f'menu_plan.selected_main_outfit_parts_menu.{resource_id}')
    return errors


def menu_validation_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Gate exact post-NDMF page/control/path parity and observed restore behavior."""
    result = task.get('menu_validation')
    if not isinstance(result, dict):
        return {'status': 'MENU_VALIDATION_MISSING', 'errors': ['menu_validation']}
    card = task.get('start_contract')
    plan = card.get('menu_plan') if isinstance(card, dict) else None
    plan_errors = validate_menu_plan(plan)
    if plan_errors:
        return {'status': 'MENU_VALIDATION_FAILED',
                'errors': [f'menu_validation.plan.{error}' for error in plan_errors]}
    expected = _menu_plan_snapshot(plan)
    candidate, revision, sha256 = _candidate_context(task)
    errors: list[str] = [
        f"menu_validation.plan.{error.removeprefix('menu_plan.')}"
        for error in _selected_main_outfit_menu_errors(task, plan, installed_only=True)
    ]
    if result.get('layer') != 'post_ndmf':
        errors.append('menu_validation.layer')
    if (not candidate or not _valid_revision(revision) or not _valid_sha256(sha256)
            or result.get('candidate') != candidate
            or result.get('candidate_revision') != revision
            or not isinstance(result.get('candidate_sha256'), str)
            or result.get('candidate_sha256', '').lower() != sha256.lower()):
        errors.append('menu_validation.candidate_identity')
    artifact_sha256 = result.get('menu_artifact_sha256')
    if not _valid_sha256(artifact_sha256):
        errors.append('menu_validation.menu_artifact_sha256')
    if result.get('status') != 'LOCAL_OK':
        errors.append('menu_validation.status')

    checks = result.get('checks')
    if not isinstance(checks, dict):
        errors.append('menu_validation.checks')
        checks = {}
    empty_failure_lists = (
        'unexpected_root_injections',
        'redundant_parallel_outfit_parts_paths',
        'cycle_pages',
        'unreachable_pages',
        'empty_pages',
        'unbound_controls',
        'multiple_default_groups',
        'undeclared_parameters',
        'parameter_type_mismatches',
        'mixed_visible_label_languages',
    )
    for name in empty_failure_lists:
        value = checks.get(name)
        if not isinstance(value, list) or value:
            errors.append(f'menu_validation.checks.{name}')
    if checks.get('auto_pagination_detected') is not False:
        errors.append('menu_validation.checks.auto_pagination_detected')

    for name in ('page_control_counts', 'observed_control_ids', 'observed_controls'):
        if checks.get(name) != expected[name]:
            errors.append(f'menu_validation.checks.{name}')
    paths = checks.get('observed_paths')
    if (not isinstance(paths, list) or any(not _nonempty(path) for path in paths)
            or len(paths) != len(set(paths)) or sorted(paths) != expected['observed_paths']):
        errors.append('menu_validation.checks.observed_paths')
    expected_language = plan.get('visible_label_language')
    if checks.get('observed_visible_label_languages') != [expected_language]:
        errors.append('menu_validation.checks.observed_visible_label_languages')

    path_results = checks.get('path_results')
    path_result_map = ({item.get('path'): item.get('outcome') for item in path_results
                        if isinstance(item, dict) and _nonempty(item.get('path'))}
                       if isinstance(path_results, list) else {})
    if (not isinstance(path_results, list)
            or len(path_result_map) != len(path_results)
            or set(path_result_map) != set(expected['observed_paths'])
            or any(outcome != 'OK' for outcome in path_result_map.values())):
        errors.append('menu_validation.checks.path_results')

    planned_non_submenu = {
        control['id']: control
        for controls in expected['observed_controls'].values()
        for control in controls if control['type'] != 'submenu'
    }
    control_results = checks.get('control_results')
    control_result_map = ({item.get('control_id'): item for item in control_results
                           if isinstance(item, dict) and _nonempty(item.get('control_id'))}
                          if isinstance(control_results, list) else {})
    if (not isinstance(control_results, list)
            or len(control_result_map) != len(control_results)
            or set(control_result_map) != set(planned_non_submenu)
            or any(item.get('outcome') != 'OK'
                   or item.get('provider_id') != planned_non_submenu[control_id].get('provider_id')
                   or item.get('resource_id') != planned_non_submenu[control_id].get('resource_id')
                   or item.get('semantic_role') != planned_non_submenu[control_id].get('semantic_role')
                   or item.get('scope') != planned_non_submenu[control_id].get('scope')
                   or item.get('return_state_policy')
                   != planned_non_submenu[control_id].get('return_state_policy')
                   for control_id, item in control_result_map.items())):
        errors.append('menu_validation.checks.control_results')
    for name in ('path_walk_observed', 'control_effects_observed',
                 'default_state_observed', 'provider_trace_complete'):
        if checks.get(name) is not True:
            errors.append(f'menu_validation.checks.{name}')

    evidence = result.get('evidence')
    evidence_items = evidence if isinstance(evidence, list) else []
    all_evidence_current = bool(evidence_items) and _valid_sha256(artifact_sha256) and all(
        isinstance(item, dict)
        and item.get('layer') == 'post_ndmf'
        and isinstance(item.get('menu_artifact_sha256'), str)
        and item['menu_artifact_sha256'].lower() == artifact_sha256.lower()
        and _evidence_is_current(item, candidate, revision, sha256)
        and item.get('observed') is True and item.get('source') and item.get('note')
        for item in evidence_items)
    if not all_evidence_current:
        errors.append('menu_validation.evidence_current_post_ndmf_artifact')

    default_evidence = any(
        isinstance(item, dict) and item.get('check_id') == 'default_state'
        and item.get('outcome') == 'OK'
        for item in evidence_items)
    if not default_evidence:
        errors.append('menu_validation.checks.default_state_evidence')

    planned_part_controls = {
        control_id: control for control_id, control in planned_non_submenu.items()
        if control.get('semantic_role') in OUTFIT_PART_CONTROL_ROLES
    }
    return_results = checks.get('outfit_part_return_state_results')
    return_result_map = ({item.get('control_id'): item for item in return_results
                          if isinstance(item, dict) and _nonempty(item.get('control_id'))}
                         if isinstance(return_results, list) else {})
    if (not isinstance(return_results, list)
            or len(return_result_map) != len(return_results)
            or set(return_result_map) != set(planned_part_controls)):
        errors.append('menu_validation.checks.outfit_part_return_state_results')
    policy_expectations = {
        'preserve_user_choice': 'user_choice_preserved',
        'restore_provider_default': 'provider_default_restored',
    }
    for control_id, planned_control in planned_part_controls.items():
        result_item = return_result_map.get(control_id)
        if not isinstance(result_item, dict):
            errors.append(
                f'menu_validation.checks.OUTFIT_PART_RETURN_STATE_UNVERIFIED.{control_id}')
            continue
        resource_id = planned_control.get('resource_id')
        counterpart = result_item.get('counterpart_outfit_id')
        sequence = result_item.get('sequence')
        expected_sequence = [resource_id, control_id, counterpart, resource_id,
                             'observe_expected', 'restore']
        policy = planned_control.get('return_state_policy')
        state_checks = result_item.get('state_checks')
        state_checks_ok = (isinstance(state_checks, dict)
                           and state_checks.get('part_state_matches_policy') is True
                           and state_checks.get('no_residual_parts') is True
                           and state_checks.get('body_mask_correct') is True
                           and state_checks.get('footwear_and_foot_shape_correct') is True)
        matching_evidence = any(
            isinstance(item, dict)
            and item.get('check_id') == f'outfit_part_return_state:{control_id}'
            and item.get('control_id') == control_id
            and item.get('resource_id') == resource_id
            and item.get('return_state_policy') == policy
            and item.get('sequence') == sequence
            and item.get('outcome') == 'OK'
            for item in evidence_items
        )
        failed = (result_item.get('failure_code') == 'OUTFIT_PART_RETURN_STATE_BROKEN'
                  or result_item.get('outcome') in ('FAILED', 'LOCAL_FAIL'))
        if failed:
            errors.append(
                f'menu_validation.checks.OUTFIT_PART_RETURN_STATE_BROKEN.{control_id}')
            continue
        valid = (
            result_item.get('resource_id') == resource_id
            and result_item.get('provider_id') == planned_control.get('provider_id')
            and result_item.get('return_state_policy') == policy
            and result_item.get('expected_return_state') == policy_expectations.get(policy)
            and _nonempty(counterpart) and counterpart != resource_id
            and sequence == expected_sequence
            and result_item.get('observed') is True
            and result_item.get('outcome') == 'OK'
            and result_item.get('restore_observed') is True
            and state_checks_ok and matching_evidence
        )
        if not valid:
            errors.append(
                f'menu_validation.checks.OUTFIT_PART_RETURN_STATE_UNVERIFIED.{control_id}')

    main_outfits = _selected_main_outfit_ids(task, installed_only=True)
    if len(main_outfits) >= 2:
        sequence = checks.get('restore_sequence')
        valid_sequence = (isinstance(sequence, list) and len(sequence) == 4
                          and sequence[0] in main_outfits and sequence[1] in main_outfits
                          and sequence[0] != sequence[1] and sequence[2] == sequence[0]
                          and sequence[3] == 'default')
        restore_evidence = any(
            isinstance(item, dict) and item.get('check_id') == 'restore_a_b_a_default'
            and item.get('outcome') == 'OK' and item.get('sequence') == sequence
            for item in evidence_items)
        if (checks.get('restore_a_b_a_default_observed') is not True
                or not restore_evidence or not valid_sequence):
            errors.append('menu_validation.checks.restore_a_b_a_default_observed')
    else:
        if (checks.get('restore_a_b_a_default_status') != 'NOT_APPLICABLE'
                or not _nonempty(checks.get('restore_a_b_a_default_reason'))
                or not any(isinstance(item, dict)
                           and item.get('check_id') == 'restore_a_b_a_default'
                           and item.get('outcome') == 'NOT_APPLICABLE'
                           for item in evidence_items)):
            errors.append('menu_validation.checks.restore_a_b_a_default_not_applicable')
    if errors:
        return {'status': 'MENU_VALIDATION_FAILED', 'errors': errors}
    return {'status': 'MENU_LOCAL_OK', 'layer': 'post_ndmf',
            'scope': 'exact planned/final menu paths, controls, defaults and restore'}


def validate_assembly_manifest(manifest: Any) -> list[str]:
    """Validate discovery-populated target/resource identities without asking a routine question."""
    if not isinstance(manifest, dict):
        return ['assembly_manifest']
    errors: list[str] = []
    target = manifest.get('target_avatar')
    if not isinstance(target, dict):
        errors.append('assembly_manifest.target_avatar')
    else:
        for key in ('id', 'version', 'source'):
            if not _nonempty(target.get(key)):
                errors.append(f'assembly_manifest.target_avatar.{key}')
    default = manifest.get('default_combination')
    if default is not None and (not isinstance(default, dict) or not default
                                or not _nonempty(default.get('source'))):
        errors.append('assembly_manifest.default_combination')
    if _platforms_from_manifest(manifest) is None:
        errors.append('assembly_manifest.target_platform')
    errors.extend(_sdk_context_errors(manifest))
    resources = manifest.get('resources')
    if not isinstance(resources, list):
        errors.append('assembly_manifest.resources')
    else:
        seen: set[str] = set()
        for index, resource in enumerate(resources):
            prefix = f'assembly_manifest.resources[{index}]'
            if not isinstance(resource, dict):
                errors.append(prefix)
                continue
            for key in ('id', 'kind', 'version', 'source'):
                if not _nonempty(resource.get(key)):
                    errors.append(f'{prefix}.{key}')
            requirement = resource.get('requirement')
            if requirement not in RESOURCE_REQUIREMENTS:
                errors.append(f'{prefix}.requirement')
            if _nonempty(resource.get('id')):
                if resource['id'] in seen:
                    errors.append(f'{prefix}.duplicate_id')
                seen.add(resource['id'])
    return errors


def _resource_performance_impact_errors(
    resource: dict[str, Any], manifest_item: dict[str, Any], platforms: list[str]
) -> list[str]:
    """Require a comparable source estimate before any selected resource is installed."""
    resource_id = resource.get('id') or manifest_item.get('id') or '<unnamed>'
    prefix = f'resources.{resource_id}.preflight.performance_impact'
    preflight = resource.get('preflight')
    impact = preflight.get('performance_impact') if isinstance(preflight, dict) else None
    if not isinstance(impact, dict):
        return [prefix]
    errors: list[str] = []
    inventory = impact.get('source_inventory')
    if (not isinstance(inventory, dict) or not inventory
            or not _nonempty(inventory.get('source'))
            or inventory.get('verified') is not True):
        errors.append(f'{prefix}.source_inventory')
    estimate = impact.get('estimate')
    scalar_metrics = ('added_meshes', 'added_material_slots', 'added_triangles',
                      'added_texture_memory_bytes')
    if not isinstance(estimate, dict):
        errors.append(f'{prefix}.estimate')
    else:
        for metric in scalar_metrics:
            value = estimate.get(metric)
            if not isinstance(value, int) or isinstance(value, bool) or value < 0:
                errors.append(f'{prefix}.estimate.{metric}')
        dynamics = estimate.get('dynamics')
        dynamic_metrics = ('physbone_components', 'physbone_transforms',
                           'physbone_colliders', 'physbone_collision_checks',
                           'contacts', 'particles', 'audio_sources')
        if not isinstance(dynamics, dict):
            errors.append(f'{prefix}.estimate.dynamics')
        else:
            for metric in dynamic_metrics:
                value = dynamics.get(metric)
                if not isinstance(value, int) or isinstance(value, bool) or value < 0:
                    errors.append(f'{prefix}.estimate.dynamics.{metric}')
    if not isinstance(impact.get('dependencies'), list):
        errors.append(f'{prefix}.dependencies')
    mitigation = impact.get('mitigation')
    if (not isinstance(mitigation, list) or not mitigation
            or any(not _nonempty(item) for item in mitigation)):
        errors.append(f'{prefix}.mitigation')
    admission = impact.get('admission')
    if (not isinstance(admission, dict)
            or admission.get('decision') not in ('ADMIT', 'REJECT')
            or not _nonempty(admission.get('reason'))
            or not _nonempty(admission.get('evidence'))):
        errors.append(f'{prefix}.admission')
    elif (admission.get('decision') == 'REJECT'
          and resource.get('status') not in ('REJECTED', 'ROLLED_BACK', 'SKIPPED')):
        errors.append(f'{prefix}.admission_reject_status_mismatch')
    if isinstance(admission, dict) and admission.get('decision') == 'REJECT':
        requirement = manifest_item.get('requirement', resource.get('requirement'))
        if requirement == 'required':
            errors.append(f'{prefix}.admission_required_rejected')
        elif (requirement == 'optional'
              and (resource.get('status') not in ('REJECTED', 'ROLLED_BACK', 'SKIPPED')
                   or resource.get('isolation_confirmed') is not True)):
            errors.append(f'{prefix}.admission_optional_rejection_not_isolated')
    headroom = impact.get('headroom_after_estimate')
    headroom_results: list[bool] = []
    if (not isinstance(headroom, dict)
            or not _nonempty(headroom.get('source'))
            or not isinstance(headroom.get('platforms'), dict)):
        errors.append(f'{prefix}.headroom_after_estimate')
    else:
        for platform in platforms:
            result = headroom['platforms'].get(platform)
            if (not isinstance(result, dict)
                    or not isinstance(result.get('expression_parameter_bits_remaining'), int)
                    or isinstance(result.get('expression_parameter_bits_remaining'), bool)
                    or result.get('expression_parameter_bits_remaining', -1) < 0
                    or not isinstance(result.get('physbone_components_remaining'), int)
                    or isinstance(result.get('physbone_components_remaining'), bool)
                    or result.get('physbone_components_remaining', -1) < 0
                    or not isinstance(result.get('meets_frozen_reserve'), bool)):
                errors.append(
                    f'{prefix}.headroom_after_estimate.platforms.{platform}')
            else:
                headroom_results.append(result['meets_frozen_reserve'])
    if (isinstance(admission, dict) and admission.get('decision') == 'ADMIT'
            and headroom_results and not all(headroom_results)):
        errors.append(f'{prefix}.admission_exhausts_frozen_headroom')
    return errors


def _selected_resource_performance_impact_errors(task: dict[str, Any]) -> list[str]:
    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    manifest_items = manifest.get('resources') if isinstance(manifest, dict) else None
    resources = task.get('resources')
    if not isinstance(manifest_items, list) or not isinstance(resources, list):
        return ['preflight.performance_impact.manifest_or_resources']
    runtime_by_id = {item.get('id'): item for item in resources
                     if isinstance(item, dict) and _nonempty(item.get('id'))}
    errors: list[str] = []
    platforms = _platforms_from_manifest(manifest) or []
    for manifest_item in manifest_items:
        if (not isinstance(manifest_item, dict)
                or manifest_item.get('selected', True) is not True):
            continue
        resource_id = manifest_item.get('id')
        runtime = runtime_by_id.get(resource_id)
        if not isinstance(runtime, dict):
            errors.append(f'resources.{resource_id}.performance_impact_runtime_record_missing')
            continue
        errors.extend(_resource_performance_impact_errors(
            runtime, manifest_item, platforms))
    return errors


def route(resource: dict[str, Any]) -> dict[str, str]:
    kind = resource.get('kind', 'unknown')
    if kind == 'plugin':
        return {'route': 'AUTHOR_PROVIDER' if resource.get('provider_ready') else 'WAIT_PROVIDER'}
    if kind == 'rigid_prop':
        return {'route': 'ANCHOR'}
    if kind not in (*CLOTHING_KINDS, 'soft_wearable'):
        return {'route': 'NEEDS_CLASSIFICATION'}
    # A model's self-reported confidence is never sufficient evidence.
    if resource.get('identity_evidence') not in ('author', 'verified_structure', 'user'):
        return {'route': 'NEEDS_SOURCE'}
    if resource.get('native') is True:
        if resource.get('author_install_ready'):
            return {'route': 'AUTHOR_PROVIDER'}
        return {'route': 'MA_SETUP_OUTFIT' if resource.get('ma_ready') else 'WAIT_PROVIDER'}
    if resource.get('native') is not False:
        return {'route': 'NEEDS_SOURCE'}
    if not (resource.get('source_profile') and resource.get('target_profile') and resource.get('profile_compatible')):
        return {'route': 'UNSUPPORTED_PROFILE'}
    return {'route': 'MOCHIFITTER' if resource.get('mochifitter_ready') else 'WAIT_PROVIDER'}


def evidence_key(e: dict[str, Any]) -> str:
    """Changing state/pose or preview invalidates evidence even if files don't."""
    keys = ('candidate', 'state', 'view', 'preview', 'checker')
    if any(not isinstance(e.get(k), str) or not e[k].strip() for k in keys):
        raise ValueError('Evidence key needs nonempty candidate,revision,sha256,state,view,preview,checker')
    if not _valid_revision(e.get('candidate_revision')):
        raise ValueError('Evidence key candidate_revision must be nonempty')
    if not _valid_sha256(e['candidate_sha256']):
        raise ValueError('Evidence key candidate_sha256 must be a SHA-256 hex digest')
    identity_keys = ('candidate', 'candidate_revision', 'candidate_sha256', *keys[1:])
    identity = {k: e[k] for k in identity_keys}
    identity['candidate_sha256'] = identity['candidate_sha256'].lower()
    encoded = json.dumps(identity, sort_keys=True, ensure_ascii=False).encode('utf-8')
    return hashlib.sha256(encoded).hexdigest()


def should_observe(previous_keys: list[str], context: dict[str, Any]) -> bool:
    return evidence_key(context) not in previous_keys


def _required_visual_states(check: dict[str, Any]) -> list[str]:
    """Return explicit visual states named by one required check."""
    states: list[str] = []
    if _nonempty(check.get('chest_state')):
        states.append(check['chest_state'])
    for field in ('affected_chest_states', 'chest_states'):
        values = check.get(field)
        if isinstance(values, list):
            for state in values:
                if _nonempty(state) and state not in states:
                    states.append(state)
    return states


def _nonempty_evidence_list(value: Any) -> bool:
    """Chest-state results use an evidence collection, not a truthy scalar."""
    return (isinstance(value, list) and bool(value)
            and all((_nonempty(item) if isinstance(item, str)
                     else isinstance(item, dict) and bool(item))
                    for item in value))


def _observation_identity_errors(task: dict[str, Any]) -> list[str]:
    """Require stable task-global observation IDs at final acceptance."""
    errors: list[str] = []
    seen: dict[str, str] = {}
    resources = task.get('resources')
    if not isinstance(resources, list):
        return ['resources']
    for resource_index, resource in enumerate(resources):
        if not isinstance(resource, dict):
            continue
        resource_id = resource.get('id') or f'<resource-{resource_index}>'
        observations = resource.get('observations')
        if not isinstance(observations, list):
            errors.append(f'resources.{resource_id}.observations')
            continue
        for observation_index, observation in enumerate(observations):
            prefix = f'resources.{resource_id}.observations[{observation_index}]'
            if not isinstance(observation, dict) or not _nonempty(observation.get('id')):
                errors.append(f'{prefix}.id')
                continue
            observation_id = observation['id']
            if observation_id in seen:
                errors.append(f'observations.duplicate_id.{observation_id}')
            else:
                seen[observation_id] = prefix
            if ('resource_id' in observation
                    and observation.get('resource_id') != resource.get('id')):
                errors.append(f'{prefix}.resource_id_mismatch')
    return errors


def assess_resource(
    item: dict[str, Any],
    candidate: str,
    candidate_revision: Any = None,
    candidate_sha256: str = '',
) -> dict[str, Any]:
    """Assess frozen required checks; do not equate numeric tests with visuals.

    Required checks must be fixed from acceptance.md before editing. A caller
    can lie in JSON; this is not an independent Unity/vision validator.
    """
    required = item.get('required', [])
    if not candidate or not required:
        return {'status': 'NEEDS_OBSERVATION', 'missing': ['candidate_or_required_checks']}
    ids = [r.get('id') for r in required]
    if len(set(ids)) != len(ids) or any(not i for i in ids):
        raise ValueError('Required check IDs must be unique and nonempty')
    enforce_freshness = candidate_revision is not None or bool(candidate_sha256)
    if enforce_freshness and (not _valid_revision(candidate_revision) or not _valid_sha256(candidate_sha256)):
        return {'status': 'NEEDS_OBSERVATION', 'missing': ['candidate_revision_or_sha256']}
    missing, failed, stale = [], [], []
    missing_states: dict[str, list[str]] = {}
    failed_states: dict[str, list[str]] = {}
    stale_states: dict[str, list[str]] = {}

    def add_once(values: list[str], value: str) -> None:
        if value not in values:
            values.append(value)

    def credible(observations: list[dict[str, Any]]) -> list[dict[str, Any]]:
        return [o for o in observations if o.get('outcome') == 'ok'
                and o.get('observed') is True and o.get('evidence') and o.get('note')]

    all_observations = [observation for observation in item.get('observations', [])
                        if isinstance(observation, dict)] \
        if isinstance(item.get('observations'), list) else []
    for req in required:
        if req.get('kind') not in ('structure', 'visual', 'behavior', 'pose'):
            raise ValueError('Unsupported check kind')
        required_states = (_required_visual_states(req)
                           if req.get('kind') == 'visual' else [])
        if required_states:
            # A state list on required-check metadata is only the plan. Each
            # state needs its own independently identified result linked by
            # check_id. Chest-state records intentionally use the strict
            # defaults/example schema; generic observations remain compatible.
            for state in required_states:
                state_observations = [o for o in all_observations
                                      if o.get('check_id') == req['id']
                                      and o.get('kind') == 'visual'
                                      and o.get('chest_state') == state]

                def structurally_valid(observation: dict[str, Any]) -> bool:
                    return (observation.get('resource_id') == item.get('id')
                            and _nonempty(observation.get('id'))
                            and observation.get('layer') == CHEST_OBSERVATION_LAYER
                            and observation.get('observed') is True
                            and _nonempty_evidence_list(observation.get('evidence'))
                            and _nonempty(observation.get('note')))

                structured = [o for o in state_observations if structurally_valid(o)]
                current = [o for o in structured
                           if _valid_revision(candidate_revision)
                           and _valid_sha256(candidate_sha256)
                           and _evidence_is_current(
                               o, candidate, candidate_revision, candidate_sha256)]
                if structured and not current:
                    add_once(stale, req['id'])
                    stale_states.setdefault(req['id'], []).append(state)
                if any(o.get('status') in CHEST_OBSERVATION_FAIL_STATUSES for o in current):
                    add_once(failed, req['id'])
                    failed_states.setdefault(req['id'], []).append(state)
                elif not any(o.get('status') == CHEST_OBSERVATION_OK_STATUS for o in current):
                    add_once(missing, req['id'])
                    missing_states.setdefault(req['id'], []).append(state)
            continue
        observations = [o for o in all_observations
                        if o.get('check_id', o.get('id')) == req['id']
                        and o.get('candidate') == candidate
                        and o.get('kind') == req['kind']]
        if enforce_freshness:
            current = [o for o in observations
                       if _evidence_is_current(o, candidate, candidate_revision, candidate_sha256)]
            if observations and not current:
                stale.append(req['id'])
            observations = current
        if any(o.get('outcome') == 'failed' for o in observations):
            failed.append(req['id']); continue
        credible_observations = credible(observations)
        if req['kind'] == 'pose':
            credible_observations = [o for o in credible_observations
                                     if o.get('pose_applied') is True
                                     and o.get('pose_evidence')]
        if not credible_observations:
            missing.append(req['id'])
    status = 'LOCAL_FAIL' if failed else 'NEEDS_OBSERVATION' if missing else 'LOCAL_OK'
    return {'status': status, 'failed': failed, 'missing': missing, 'stale': stale,
            'failed_states': failed_states, 'missing_states': missing_states,
            'stale_states': stale_states,
            'scope': 'recorded local checks only; no client/runtime guarantee'}


def next_failure_action(corrections_used: int, cause_known: bool) -> str:
    if corrections_used < 0:
        raise ValueError('corrections_used must be nonnegative')
    if not cause_known:
        return 'PAUSE_WHOLE_TASK'
    return 'ONE_TARGETED_CORRECTION' if corrections_used == 0 else 'ROLLBACK_AND_SKIP'


def intake_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Check the five-question contract before Unity writes; read-only discovery is allowed.

    The assembly manifest is populated by discovery and is not a routine sixth
    question. Menu information architecture is the fourth Start Card item.
    """
    card = task.get('start_contract')
    if not isinstance(card, dict):
        return {'status': 'ASK_START_CARD', 'missing': ['start_contract']}
    missing: list[str] = []
    if not isinstance(card.get('visualization'), dict) or not card['visualization'].get('mode'):
        missing.append('visualization')
    budget = card.get('size_budget')
    if not isinstance(budget, dict):
        missing.append('size_budget')
    else:
        metric = budget.get('metric')
        if metric not in (*SIZE_METRICS, 'both'):
            missing.append('size_budget.metric')
        for gate in ('gate_completion', 'gate_upload'):
            if not isinstance(budget.get(gate), bool):
                missing.append(f'size_budget.{gate}')
        if not isinstance(budget.get('hard_limit_bytes'), int) or budget['hard_limit_bytes'] <= 0:
            missing.append('size_budget.hard_limit_bytes')
        if not isinstance(budget.get('working_limit_bytes'), int) or budget['working_limit_bytes'] <= 0:
            missing.append('size_budget.working_limit_bytes')
        elif (isinstance(budget.get('hard_limit_bytes'), int)
              and budget['working_limit_bytes'] > budget['hard_limit_bytes']):
            missing.append('size_budget.working_limit_must_not_exceed_hard_limit')
        if metric in ('sdk_uncompressed_bytes', 'both'):
            applies = budget.get('applies_to_platforms')
            discovered = _platforms_from_manifest(card.get('assembly_manifest'))
            if (not isinstance(applies, list) or not applies
                    or len(applies) != len(set(applies))
                    or any(platform not in PERFORMANCE_PLATFORMS
                           for platform in applies)):
                missing.append('size_budget.applies_to_platforms')
            elif discovered is not None and applies != discovered:
                missing.append('size_budget.applies_to_platforms_manifest_mismatch')
    manifest = card.get('assembly_manifest')
    missing.extend(validate_content_priority(card.get('content_priority'), manifest))
    missing.extend(validate_optimization_policy(card.get('base_optimization')))
    missing.extend(validate_performance_contract(
        card.get('performance_contract'), manifest))
    menu_plan = card.get('menu_plan')
    missing.extend(validate_menu_plan(menu_plan))
    missing.extend(_selected_main_outfit_menu_errors(task, menu_plan))
    discovery_missing = validate_assembly_manifest(manifest)
    if card.get('batch_cadence') != 'preflight_all_install_all_smoke_only_then_unified_acceptance':
        missing.append('batch_cadence')
    missing.extend(validate_delivery_layers(task, card))
    if card.get('confirmed') is not True:
        missing.append('confirmation')
    if missing:
        return {'status': 'ASK_START_CARD', 'missing': missing,
                'question_count': 5}
    if discovery_missing:
        return {'status': 'DISCOVERY_INCOMPLETE', 'missing': discovery_missing,
                'routine_start_question': False,
                'next': 'READ_ONLY_DISCOVERY_OR_ONE_MINIMAL_FACT_QUESTION'}
    return {'status': 'READY_FOR_UNITY_WRITE', 'question_count': 5}


def size_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Assess current metric receipts; SDK bytes close to the same platform job/report."""
    card = task.get('start_contract')
    budget = card.get('size_budget') if isinstance(card, dict) else None
    if not isinstance(budget, dict):
        return {'status': 'SIZE_UNVERIFIED', 'missing': ['size_budget']}
    if any(not isinstance(budget.get(gate), bool)
           for gate in ('gate_completion', 'gate_upload')):
        return {'status': 'SIZE_UNVERIFIED',
                'missing': ['size_budget.gate_completion_or_gate_upload']}
    metric = budget.get('metric')
    required = list(SIZE_METRICS) if metric == 'both' else [metric]
    if any(m not in SIZE_METRICS for m in required):
        return {'status': 'SIZE_UNVERIFIED', 'missing': ['size_budget.metric']}
    hard = budget.get('hard_limit_bytes')
    working = budget.get('working_limit_bytes')
    actual = budget.get('actual_bytes', {})
    projected = budget.get('projected_bytes', {})
    evidence = budget.get('evidence', [])
    if (not isinstance(hard, int) or isinstance(hard, bool) or hard <= 0
            or not isinstance(working, int) or isinstance(working, bool)
            or working <= 0 or working > hard):
        return {'status': 'SIZE_UNVERIFIED', 'missing': ['byte_limits']}
    if not isinstance(actual, dict) or not isinstance(evidence, list):
        return {'status': 'SIZE_UNVERIFIED', 'missing': ['actual_bytes_or_evidence']}

    missing: list[str] = []
    mismatches: list[str] = []
    validated: dict[str, Any] = {}
    candidate, revision, sha256 = _candidate_context(task)

    if 'deliverable_extracted_bytes' in required:
        value = actual.get('deliverable_extracted_bytes')
        valid_receipt = (
            isinstance(value, int) and not isinstance(value, bool) and value >= 0
            and any(
                isinstance(item, dict)
                and item.get('metric') == 'deliverable_extracted_bytes'
                and item.get('measured') is True
                and _nonempty(item.get('source'))
                and item.get('layer') == 'deliverable_extraction'
                and item.get('bytes') == value
                and _valid_revision(revision) and _valid_sha256(sha256)
                and _evidence_is_current(item, candidate, revision, sha256)
                for item in evidence))
        if valid_receipt:
            validated['deliverable_extracted_bytes'] = value
        else:
            missing.append('deliverable_extracted_bytes')

    if 'sdk_uncompressed_bytes' in required:
        manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
        sdk_context_errors = _sdk_context_errors(manifest)
        current_sdk_version = _current_vrcsdk_version(manifest)
        if sdk_context_errors or current_sdk_version is None:
            return {'status': 'SIZE_UNVERIFIED',
                    'missing': sdk_context_errors or [
                        'assembly_manifest.sdk_context.current_vrcsdk_version']}
        platforms = budget.get('applies_to_platforms')
        contract = card.get('performance_contract') if isinstance(card, dict) else None
        contract_platforms = contract.get('platforms') if isinstance(contract, dict) else None
        finals = contract.get('final_measurements') if isinstance(contract, dict) else None
        jobs = task.get('performance_measurement_jobs')
        values = actual.get('sdk_uncompressed_bytes')
        if (not isinstance(platforms, list) or not platforms
                or platforms != contract_platforms):
            missing.append('sdk_uncompressed_bytes.applies_to_platforms')
        elif not isinstance(values, dict) or set(values) != set(platforms):
            missing.append('sdk_uncompressed_bytes.per_platform_actual')
        else:
            sdk_validated: dict[str, int] = {}
            sdk_sources_by_platform: dict[str, set[str]] = {}
            for platform in platforms:
                value = values.get(platform)
                context = _platform_candidate_context(task, platform, platforms)
                final = finals.get(platform) if isinstance(finals, dict) else None
                job = jobs.get(platform) if isinstance(jobs, dict) else None
                if (not isinstance(value, int) or isinstance(value, bool) or value < 0
                        or context is None or not isinstance(final, dict)
                        or not isinstance(job, dict)):
                    missing.append(f'sdk_uncompressed_bytes.{platform}.receipt')
                    continue
                platform_candidate, platform_revision, platform_sha = context
                sdk_version = final.get('sdk_version')
                bundle = final.get('platform_size')
                final_value = (bundle.get('sdk_uncompressed_bytes')
                               if isinstance(bundle, dict) else None)
                final_identity_matches = (
                    final.get('platform') == platform
                    and final.get('candidate') == platform_candidate
                    and final.get('candidate_revision') == platform_revision
                    and isinstance(final.get('candidate_sha256'), str)
                    and final.get('candidate_sha256', '').lower()
                    == platform_sha.lower())
                if (sdk_version != current_sdk_version
                        or not final_identity_matches or value != final_value):
                    mismatches.append(
                        f'sdk_uncompressed_bytes.{platform}.'
                        'final_value_identity_or_sdk_version')
                    continue
                receipt_sources = {
                    item.get('source') for item in evidence
                    if isinstance(item, dict)
                    and item.get('metric') == 'sdk_uncompressed_bytes'
                    and item.get('platform') == platform
                    and item.get('measured') is True
                    and item.get('bytes') == value
                    and item.get('sdk_version') == current_sdk_version
                    and item.get('layer') == 'current_sdk_build_report'
                    and _nonempty(item.get('source'))
                    and _evidence_is_current(
                        item, platform_candidate, platform_revision, platform_sha)
                }
                final_sources = {
                    item.get('source') for item in (
                        bundle.get('evidence', []) if isinstance(bundle, dict) else [])
                    if isinstance(item, dict)
                    and item.get('platform') == platform
                    and item.get('measured') is True
                    and item.get('sdk_version') == current_sdk_version
                    and item.get('layer') == 'current_sdk_build_report'
                    and _nonempty(item.get('source'))
                    and _evidence_is_current(
                        item, platform_candidate, platform_revision, platform_sha)
                }
                job_sources = {
                    item.get('source') for item in job.get('result_evidence', [])
                    if isinstance(item, dict)
                    and item.get('kind') == 'sdk_build_report'
                    and item.get('platform') == platform
                    and item.get('sdk_version') == current_sdk_version
                    and item.get('layer') == 'current_sdk_build_report'
                    and _nonempty(item.get('source'))
                    and _evidence_is_current(
                        item, platform_candidate, platform_revision, platform_sha)
                }
                expected_job_id = performance_job_id(
                    platform, platform_candidate, platform_revision,
                    platform_sha, current_sdk_version) if (
                        _nonempty(platform_candidate)
                        and _valid_revision(platform_revision)
                        and _valid_sha256(platform_sha)
                        and _nonempty(current_sdk_version)) else None
                job_valid = (
                    job.get('job_id') == expected_job_id
                    and job.get('sdk_version') == current_sdk_version
                    and job.get('status') == PERFORMANCE_JOB_TERMINAL_STATUS
                    and job.get('dispatch_count') == 1)
                if (not receipt_sources or receipt_sources != final_sources
                        or receipt_sources != job_sources or not job_valid):
                    mismatches.append(
                        f'sdk_uncompressed_bytes.{platform}.report_job_source_closure')
                    continue
                sdk_validated[platform] = value
                sdk_sources_by_platform[platform] = receipt_sources
            for index, platform in enumerate(platforms):
                for other in platforms[index + 1:]:
                    if (sdk_sources_by_platform.get(platform, set())
                            & sdk_sources_by_platform.get(other, set())):
                        mismatches.append(
                            'sdk_uncompressed_bytes.'
                            f'cross_platform_source_reuse.{platform}.{other}')
            if len(sdk_validated) == len(platforms):
                validated['sdk_uncompressed_bytes'] = sdk_validated

    if mismatches:
        return {'status': 'SIZE_RECEIPT_MISMATCH', 'errors': mismatches}
    if not missing and all(metric_name in validated for metric_name in required):
        exceeded: dict[str, Any] = {}
        for metric_name, value in validated.items():
            if isinstance(value, dict):
                over = {platform: count for platform, count in value.items()
                        if count > hard}
                if over:
                    exceeded[metric_name] = over
            elif value > hard:
                exceeded[metric_name] = value
        if exceeded:
            return {'status': 'SIZE_BUDGET_EXCEEDED',
                    'actual_bytes': exceeded, 'hard_limit_bytes': hard}
        return {'status': 'SIZE_OK', 'actual_bytes': validated,
                'hard_limit_bytes': hard}

    warned: dict[str, Any] = {}
    if isinstance(projected, dict):
        for metric_name in required:
            value = projected.get(metric_name)
            if isinstance(value, dict):
                over = {platform: count for platform, count in value.items()
                        if isinstance(count, int) and not isinstance(count, bool)
                        and count > working}
                if over:
                    warned[metric_name] = over
            elif isinstance(value, int) and not isinstance(value, bool) and value > working:
                warned[metric_name] = value
    if warned:
        return {'status': 'OPTIMIZE_BEFORE_NEXT_BATCH', 'projected_bytes': warned,
                'working_limit_bytes': working, 'missing_actual': missing,
                'scope': 'extra budget action after the always-on base generated-copy optimization'}
    return {'status': 'SIZE_UNVERIFIED', 'missing_actual': missing,
            'note': 'projection is not a final measurement'}


def _platform_candidate_context(
    task: dict[str, Any], platform: str, platforms: list[str]
) -> tuple[str, Any, str] | None:
    candidates = task.get('platform_candidates')
    if isinstance(candidates, dict) and isinstance(candidates.get(platform), dict):
        entry = candidates[platform]
        return (entry.get('candidate', ''), entry.get('candidate_revision'),
                entry.get('candidate_sha256', ''))
    if len(platforms) == 1:
        return _candidate_context(task)
    return None


def _performance_measurement_errors(
    record: Any, platform: str,
    expected_context: tuple[str, Any, str] | None = None,
    require_platform_size: bool = False,
    expected_sdk_version: str | None = None,
) -> list[str]:
    prefix = f'performance_measurements.{platform}'
    if not isinstance(record, dict):
        return [prefix]
    errors: list[str] = []
    candidate = record.get('candidate')
    revision = record.get('candidate_revision')
    sha256 = record.get('candidate_sha256')
    if (not _nonempty(candidate) or not _valid_revision(revision)
            or not _valid_sha256(sha256)):
        errors.append(f'{prefix}.candidate_identity')
    if record.get('platform') != platform:
        errors.append(f'{prefix}.platform')
    if expected_context is not None:
        expected_candidate, expected_revision, expected_sha = expected_context
        if (candidate != expected_candidate or revision != expected_revision
                or not isinstance(sha256, str)
                or not isinstance(expected_sha, str)
                or sha256.lower() != expected_sha.lower()):
            errors.append(f'{prefix}.old_or_wrong_candidate')
    if not _nonempty(record.get('sdk_version')):
        errors.append(f'{prefix}.sdk_version')
    elif (expected_sdk_version is not None
          and record.get('sdk_version') != expected_sdk_version):
        errors.append(f'{prefix}.sdk_version_not_current')
    if record.get('rank') not in PERFORMANCE_RANKS:
        errors.append(f'{prefix}.rank')
    if record.get('report_kind') != 'current_sdk_avatar_performance_report':
        errors.append(f'{prefix}.report_kind')
    if record.get('inactive_objects_included') is not True:
        errors.append(f'{prefix}.inactive_objects_included')
    if record.get('rank_is_static_analysis') is not True:
        errors.append(f'{prefix}.rank_is_static_analysis')
    if record.get('fps_claimed') is not False:
        errors.append(f'{prefix}.fps_claimed')
    snapshot = record.get('metric_snapshot')
    if not isinstance(snapshot, dict):
        errors.append(f'{prefix}.metric_snapshot')
        snapshot = {}
    else:
        for metric in PERFORMANCE_METRICS:
            value = snapshot.get(metric)
            if (not isinstance(value, (int, float)) or isinstance(value, bool)
                    or value < 0):
                errors.append(f'{prefix}.metric_snapshot.{metric}')
        if any('fps' in str(metric).lower() for metric in snapshot):
            errors.append(f'{prefix}.metric_snapshot.no_fps_claim')
    boolean_snapshot = record.get('boolean_snapshot')
    if (not isinstance(boolean_snapshot, dict)
            or any(not isinstance(boolean_snapshot.get(metric), bool)
                   for metric in PERFORMANCE_BOOLEAN_METRICS)):
        errors.append(f'{prefix}.boolean_snapshot')
        boolean_snapshot = {}
    worst = record.get('worst_metrics')
    if (not isinstance(worst, list) or not worst
            or any(metric not in snapshot and metric not in boolean_snapshot
                   for metric in worst)):
        errors.append(f'{prefix}.worst_metrics')
    evidence = record.get('evidence')
    current_evidence = (isinstance(evidence, list) and any(
        isinstance(item, dict) and item.get('measured') is True
        and _nonempty(item.get('source'))
        and item.get('platform') == platform
        and item.get('layer') == 'current_sdk_avatar_performance_report'
        and item.get('sdk_version') == record.get('sdk_version')
        and _valid_sha256(sha256)
        and _evidence_is_current(item, candidate, revision, sha256)
        for item in evidence))
    if not current_evidence:
        errors.append(f'{prefix}.current_evidence')
    if require_platform_size:
        bundle = record.get('platform_size')
        limits = PLATFORM_BUNDLE_LIMITS[platform]
        if not isinstance(bundle, dict):
            errors.append(f'{prefix}.platform_size')
        else:
            for metric, official_limit in limits.items():
                value = bundle.get(metric)
                if not isinstance(value, int) or isinstance(value, bool) or value < 0:
                    errors.append(f'{prefix}.platform_size.{metric}')
                recorded_limit = bundle.get('current_sdk_limits', {}).get(metric) \
                    if isinstance(bundle.get('current_sdk_limits'), dict) else None
                if recorded_limit != official_limit:
                    errors.append(f'{prefix}.platform_size.current_sdk_limits.{metric}')
                over_field = f'{metric}_over_limit'
                if (not isinstance(bundle.get(over_field), bool)
                        or isinstance(value, int)
                        and bundle.get(over_field) != (value > official_limit)):
                    errors.append(f'{prefix}.platform_size.{over_field}')
            if not _nonempty(bundle.get('current_sdk_limit_source')):
                errors.append(f'{prefix}.platform_size.current_sdk_limit_source')
            size_evidence = bundle.get('evidence')
            current_size_evidence = (isinstance(size_evidence, list) and any(
                isinstance(item, dict) and item.get('measured') is True
                and _nonempty(item.get('source'))
                and item.get('platform') == platform
                and item.get('layer') == 'current_sdk_build_report'
                and item.get('sdk_version') == record.get('sdk_version')
                and _valid_sha256(sha256)
                and _evidence_is_current(item, candidate, revision, sha256)
                for item in size_evidence))
            if not current_size_evidence:
                errors.append(f'{prefix}.platform_size.current_sdk_build_report_evidence')
    return errors


def _measurement_report_sources(record: Any) -> tuple[set[str], set[str]]:
    if not isinstance(record, dict):
        return set(), set()
    performance_sources = {
        item.get('source') for item in record.get('evidence', [])
        if isinstance(item, dict)
        and item.get('layer') == 'current_sdk_avatar_performance_report'
        and _nonempty(item.get('source'))
    }
    bundle = record.get('platform_size')
    build_sources = {
        item.get('source') for item in (
            bundle.get('evidence', []) if isinstance(bundle, dict) else [])
        if isinstance(item, dict)
        and item.get('layer') == 'current_sdk_build_report'
        and _nonempty(item.get('source'))
    }
    return performance_sources, build_sources


def _baseline_receipt_errors(
    record: Any, platform: str, expected_sdk_version: str | None,
) -> list[str]:
    prefix = f'performance_baseline_receipts.{platform}'
    if not isinstance(record, dict):
        return [prefix]
    errors: list[str] = []
    measured_utc = record.get('measured_utc')
    if _utc_timestamp(measured_utc) is None:
        errors.append(f'{prefix}.measured_utc')
    if record.get('frozen_at_phase') != 'PREFLIGHT':
        errors.append(f'{prefix}.frozen_at_phase')
    receipt = record.get('baseline_receipt')
    if not isinstance(receipt, dict):
        return [*errors, prefix]
    performance_sources, build_sources = _measurement_report_sources(record)
    candidate = record.get('candidate')
    revision = record.get('candidate_revision')
    sha256 = record.get('candidate_sha256')
    sdk_version = record.get('sdk_version')
    expected_fields = {
        'platform': platform,
        'candidate': candidate,
        'candidate_revision': revision,
        'candidate_sha256': sha256,
        'sdk_version': expected_sdk_version,
        'measured_utc': measured_utc,
        'frozen_at_phase': 'PREFLIGHT',
        'performance_report_sources': sorted(performance_sources),
        'build_report_sources': sorted(build_sources),
    }
    for field, expected in expected_fields.items():
        actual = receipt.get(field)
        if field == 'candidate_sha256':
            if (not isinstance(actual, str) or not isinstance(expected, str)
                    or actual.lower() != expected.lower()):
                errors.append(f'{prefix}.{field}')
        elif actual != expected:
            errors.append(f'{prefix}.{field}')
    if (not performance_sources or not build_sources
            or not _valid_sha256(sha256) or not _nonempty(candidate)
            or not _valid_revision(revision) or not _nonempty(sdk_version)
            or sdk_version != expected_sdk_version
            or _utc_timestamp(measured_utc) is None):
        errors.append(f'{prefix}.identity_inputs')
    else:
        expected_sha = baseline_receipt_sha256(
            platform, candidate, revision, sha256, sdk_version, measured_utc,
            sorted(performance_sources), sorted(build_sources))
        if receipt.get('receipt_sha256') != expected_sha:
            errors.append(f'{prefix}.receipt_sha256')
        if receipt.get('receipt_id') != f'baseline-{expected_sha}':
            errors.append(f'{prefix}.receipt_id')
    if receipt.get('locked') is not True or not _nonempty(receipt.get('source')):
        errors.append(f'{prefix}.lock_evidence')
    if ('job_id' in receipt or 'result_evidence' in receipt
            or 'performance_measurement_job_id' in record):
        errors.append(f'{prefix}.must_not_reference_final_job')
    return errors


def _baseline_final_lifecycle_errors(
    task: dict[str, Any], baseline: Any, final: Any, platform: str,
) -> list[str]:
    prefix = f'performance_lifecycle.{platform}'
    if not isinstance(baseline, dict) or not isinstance(final, dict):
        return [prefix]
    errors: list[str] = []
    baseline_time = _utc_timestamp(baseline.get('measured_utc'))
    final_time = _utc_timestamp(final.get('measured_utc'))
    if final_time is None:
        errors.append(f'{prefix}.final_measured_utc')
    elif baseline_time is None or final_time <= baseline_time:
        errors.append(f'{prefix}.final_not_after_baseline')
    if final.get('frozen_at_phase') != 'FINAL_VALIDATION':
        errors.append(f'{prefix}.final_frozen_at_phase')

    baseline_performance, baseline_build = _measurement_report_sources(baseline)
    final_performance, final_build = _measurement_report_sources(final)
    if baseline_performance & final_performance:
        errors.append(f'{prefix}.performance_report_source_reuse')
    if baseline_build & final_build:
        errors.append(f'{prefix}.build_report_source_reuse')
    job = (task.get('performance_measurement_jobs', {}).get(platform)
           if isinstance(task.get('performance_measurement_jobs'), dict) else None)
    job_sources = {
        item.get('source') for item in (
            job.get('result_evidence', []) if isinstance(job, dict) else [])
        if isinstance(item, dict) and _nonempty(item.get('source'))
    }
    if (baseline_performance | baseline_build) & job_sources:
        errors.append(f'{prefix}.baseline_reuses_final_job_result_source')

    baseline_identity = (
        baseline.get('candidate'), baseline.get('candidate_revision'),
        str(baseline.get('candidate_sha256', '')).lower())
    final_identity = (
        final.get('candidate'), final.get('candidate_revision'),
        str(final.get('candidate_sha256', '')).lower())
    installed_ids = sorted({
        resource.get('id') for resource in task.get('resources', [])
        if isinstance(resource, dict) and _nonempty(resource.get('id'))
        and resource.get('selected', True) is True
        and resource.get('status') in ('INSTALLED_UNVERIFIED', 'LOCAL_OK')
    })
    if baseline_identity == final_identity and installed_ids:
        proof = final.get('zero_impact_proof')
        if (not isinstance(proof, dict) or proof.get('confirmed') is not True
                or proof.get('resource_ids') != installed_ids
                or not _nonempty(proof.get('source'))
                or not _nonempty_evidence_list(proof.get('evidence'))):
            errors.append(f'{prefix}.same_identity_requires_zero_impact_proof')
    return errors


def _performance_measurement_job_errors(
    task: dict[str, Any], platform: str, final_record: dict[str, Any],
    expected_context: tuple[str, Any, str], platforms: list[str],
    expected_sdk_version: str,
) -> list[str]:
    """Require one successfully completed formal job for the exact final measurement."""
    prefix = f'performance_measurement_jobs.{platform}'
    jobs = task.get('performance_measurement_jobs')
    if not isinstance(jobs, dict):
        return ['performance_measurement_jobs']
    errors: list[str] = []
    if set(jobs) != set(platforms):
        errors.append('performance_measurement_jobs.platform_set')
    job = jobs.get(platform)
    if not isinstance(job, dict):
        return [*errors, prefix]
    candidate, revision, sha256 = expected_context
    sdk_version = expected_sdk_version
    if final_record.get('sdk_version') != expected_sdk_version:
        errors.append(f'{prefix}.final_sdk_version_not_current')
    expected = {
        'platform': platform,
        'candidate': candidate,
        'candidate_revision': revision,
        'candidate_sha256': sha256,
        'sdk_version': sdk_version,
    }
    for field, value in expected.items():
        actual = job.get(field)
        if field == 'candidate_sha256':
            if (not isinstance(actual, str) or not isinstance(value, str)
                    or actual.lower() != value.lower()):
                errors.append(f'{prefix}.{field}')
        elif actual != value:
            errors.append(f'{prefix}.{field}')
    if (_nonempty(candidate) and _valid_revision(revision)
            and _valid_sha256(sha256) and _nonempty(sdk_version)):
        expected_job_id = performance_job_id(
            platform, candidate, revision, sha256, sdk_version)
        if job.get('job_id') != expected_job_id:
            errors.append(f'{prefix}.job_id')
    else:
        errors.append(f'{prefix}.job_id_inputs')
    dispatch_count = job.get('dispatch_count')
    if (not isinstance(dispatch_count, int) or isinstance(dispatch_count, bool)
            or dispatch_count != 1):
        errors.append(f'{prefix}.dispatch_count_must_equal_1')
    poll_count = job.get('poll_count')
    if (not isinstance(poll_count, int) or isinstance(poll_count, bool)
            or poll_count < 0):
        errors.append(f'{prefix}.poll_count')
    if job.get('timeout_action') != PERFORMANCE_JOB_TIMEOUT_ACTION:
        errors.append(f'{prefix}.timeout_action')
    if job.get('status') != PERFORMANCE_JOB_TERMINAL_STATUS:
        errors.append(f'{prefix}.status_terminal_succeeded_required')
    if not _nonempty(job.get('completed_utc')):
        errors.append(f'{prefix}.completed_utc')
    result_evidence = job.get('result_evidence')
    expected_layers = {
        'avatar_performance_report': 'current_sdk_avatar_performance_report',
        'sdk_build_report': 'current_sdk_build_report',
    }
    expected_sources = {
        'avatar_performance_report': {
            (item.get('source'), item.get('layer'))
            for item in final_record.get('evidence', [])
            if isinstance(item, dict) and _nonempty(item.get('source'))
        },
        'sdk_build_report': {
            (item.get('source'), item.get('layer'))
            for item in final_record.get('platform_size', {}).get('evidence', [])
            if isinstance(item, dict) and _nonempty(item.get('source'))
        },
    }
    actual_sources = {kind: set() for kind in expected_layers}
    if not isinstance(result_evidence, list) or not result_evidence:
        errors.append(f'{prefix}.result_evidence')
    else:
        seen_results: set[tuple[str, str, str]] = set()
        for index, item in enumerate(result_evidence):
            item_prefix = f'{prefix}.result_evidence[{index}]'
            if not isinstance(item, dict):
                errors.append(item_prefix)
                continue
            kind = item.get('kind')
            source = item.get('source')
            layer = item.get('layer')
            if kind not in expected_layers:
                errors.append(f'{item_prefix}.kind')
                continue
            if (not _nonempty(source) or layer != expected_layers[kind]
                    or item.get('platform') != platform
                    or item.get('sdk_version') != sdk_version
                    or not _evidence_is_current(
                        item, candidate, revision, sha256)):
                errors.append(f'{item_prefix}.identity_or_source')
                continue
            key = (kind, source, layer)
            if key in seen_results:
                errors.append(f'{item_prefix}.duplicate')
            seen_results.add(key)
            actual_sources[kind].add((source, layer))
        for kind in expected_layers:
            if actual_sources[kind] != expected_sources[kind]:
                errors.append(f'{prefix}.result_evidence_source_closure.{kind}')
    return errors


def _cross_platform_evidence_source_errors(
    records: dict[str, Any], platforms: list[str],
    jobs: Any = None, prefix: str = 'performance_evidence',
) -> list[str]:
    """Fail closed when one report file is claimed by multiple platforms."""
    sources: dict[str, set[str]] = {}
    for platform in platforms:
        platform_sources: set[str] = set()
        record = records.get(platform) if isinstance(records, dict) else None
        if isinstance(record, dict):
            for item in record.get('evidence', []):
                if isinstance(item, dict) and _nonempty(item.get('source')):
                    platform_sources.add(item['source'])
            bundle = record.get('platform_size')
            if isinstance(bundle, dict):
                for item in bundle.get('evidence', []):
                    if isinstance(item, dict) and _nonempty(item.get('source')):
                        platform_sources.add(item['source'])
        job = jobs.get(platform) if isinstance(jobs, dict) else None
        if isinstance(job, dict):
            for item in job.get('result_evidence', []):
                if isinstance(item, dict) and _nonempty(item.get('source')):
                    platform_sources.add(item['source'])
        sources[platform] = platform_sources
    errors: list[str] = []
    for index, platform in enumerate(platforms):
        for other in platforms[index + 1:]:
            if sources.get(platform, set()) & sources.get(other, set()):
                errors.append(
                    f'{prefix}.cross_platform_source_reuse.{platform}.{other}')
    return errors


def _performance_action_errors(
    final_record: dict[str, Any], contract: dict[str, Any], platform: str
) -> list[str]:
    prefix = f'performance_measurements.{platform}.allowed_action_results'
    allowed = contract.get('allowed_actions', [])
    results = final_record.get('allowed_action_results')
    if not isinstance(results, list):
        return [prefix]
    by_action: dict[str, dict[str, Any]] = {}
    errors: list[str] = []
    for index, result in enumerate(results):
        if not isinstance(result, dict) or result.get('action') in by_action:
            errors.append(f'{prefix}[{index}]')
            continue
        action = result.get('action')
        by_action[action] = result
        if action not in allowed:
            errors.append(f'{prefix}[{index}].not_authorized')
        if result.get('status') not in PERFORMANCE_ACTION_STATUSES:
            errors.append(f'{prefix}[{index}].status')
        if not _nonempty(result.get('source')) or not _nonempty_evidence_list(result.get('evidence')):
            errors.append(f'{prefix}[{index}].evidence')
        if action in SEMANTIC_RETEST_ACTIONS and result.get('status') == 'APPLIED':
            safety = result.get('semantic_safety')
            required = ('outfit_part_controls', 'blendshapes_and_chest_states',
                        'material_animations', 'provider_writers')
            if not isinstance(safety, dict) or any(safety.get(key) is not True for key in required):
                errors.append(f'{prefix}[{index}].semantic_safety')
            if (result.get('affected_evidence_invalidated_and_retested') is not True
                    or not _nonempty_evidence_list(result.get('retest_evidence'))):
                errors.append(f'{prefix}[{index}].affected_retest')
    for action in allowed:
        if action not in by_action:
            errors.append(f'{prefix}.{action}')
    return errors


def _delta_attribution_errors(
    task: dict[str, Any], contract: dict[str, Any],
    baseline: dict[str, Any], final: dict[str, Any], platform: str
) -> list[str]:
    prefix = f'performance_measurements.{platform}.delta_attribution'
    attribution = final.get('delta_attribution')
    if not isinstance(attribution, dict):
        return [prefix]
    entries = attribution.get('entries')
    unattributed = attribution.get('unattributed_deltas')
    if not isinstance(entries, list) or not isinstance(unattributed, list):
        return [prefix]
    errors: list[str] = []
    if unattributed:
        errors.append(f'{prefix}.unattributed_deltas')
    baseline_metrics = baseline.get('metric_snapshot', {})
    final_metrics = final.get('metric_snapshot', {})
    deltas = {metric: final_metrics[metric] - baseline_metrics[metric]
              for metric in PERFORMANCE_METRICS
              if isinstance(baseline_metrics.get(metric), (int, float))
              and isinstance(final_metrics.get(metric), (int, float))
              and final_metrics[metric] != baseline_metrics[metric]}
    baseline_size = baseline.get('platform_size', {})
    final_size = final.get('platform_size', {})
    deltas.update({
        metric: final_size[metric] - baseline_size[metric]
        for metric in PERFORMANCE_PLATFORM_SIZE_METRICS
        if isinstance(baseline_size.get(metric), int)
        and not isinstance(baseline_size.get(metric), bool)
        and isinstance(final_size.get(metric), int)
        and not isinstance(final_size.get(metric), bool)
        and final_size[metric] != baseline_size[metric]
    })
    baseline_booleans = baseline.get('boolean_snapshot', {})
    final_booleans = final.get('boolean_snapshot', {})
    boolean_deltas = {
        metric: (baseline_booleans.get(metric), final_booleans.get(metric))
        for metric in PERFORMANCE_BOOLEAN_METRICS
        if baseline_booleans.get(metric) != final_booleans.get(metric)
    }

    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    manifest_items = manifest.get('resources') if isinstance(manifest, dict) else []
    selected_manifest_ids = {
        item.get('id') for item in manifest_items
        if isinstance(item, dict) and item.get('selected', True) is True
        and _nonempty(item.get('id'))
    }
    eligible_resource_ids = {
        resource.get('id') for resource in task.get('resources', [])
        if isinstance(resource, dict)
        and resource.get('selected', True) is True
        and resource.get('id') in selected_manifest_ids
        and resource.get('status') in ('INSTALLED_UNVERIFIED', 'LOCAL_OK')
    }
    allowed_actions = set(contract.get('allowed_actions', []))
    applied_actions = {
        result.get('action') for result in final.get('allowed_action_results', [])
        if isinstance(result, dict) and result.get('status') == 'APPLIED'
    }
    for index, entry in enumerate(entries):
        entry_prefix = f'{prefix}.entries[{index}]'
        if not isinstance(entry, dict):
            errors.append(entry_prefix)
            continue
        metric = entry.get('metric')
        if metric not in deltas and metric not in boolean_deltas:
            errors.append(f'{entry_prefix}.phantom_or_unknown_metric')
        source_kind = entry.get('source_kind')
        source_id = entry.get('source_id')
        if source_kind == 'resource':
            if source_id not in eligible_resource_ids:
                errors.append(f'{entry_prefix}.source_resource_not_eligible')
        elif source_kind == 'optimization_action':
            if source_id not in allowed_actions:
                errors.append(f'{entry_prefix}.source_action_not_authorized')
            elif source_id not in applied_actions:
                errors.append(f'{entry_prefix}.source_action_not_applied')
        else:
            errors.append(f'{entry_prefix}.source_kind')
        if not _nonempty_evidence_list(entry.get('evidence')):
            errors.append(f'{entry_prefix}.evidence')
        if metric in deltas and (not isinstance(entry.get('delta'), (int, float))
                                 or isinstance(entry.get('delta'), bool)):
            errors.append(f'{entry_prefix}.delta')
        if metric in boolean_deltas:
            expected_from, expected_to = boolean_deltas[metric]
            if (entry.get('from') != expected_from
                    or entry.get('to') != expected_to):
                errors.append(f'{entry_prefix}.boolean_transition')

    for metric, delta in deltas.items():
        matches = [entry for entry in entries
                   if isinstance(entry, dict) and entry.get('metric') == metric
                   and isinstance(entry.get('delta'), (int, float))
                   and not isinstance(entry.get('delta'), bool)
                   and entry.get('source_kind') in ('resource', 'optimization_action')
                   and _nonempty(entry.get('source_id'))
                   and _nonempty_evidence_list(entry.get('evidence'))]
        if not matches or sum(entry['delta'] for entry in matches) != delta:
            errors.append(f'{prefix}.unattributed_delta.{metric}')
    for metric, (expected_from, expected_to) in boolean_deltas.items():
        matches = [entry for entry in entries
                   if isinstance(entry, dict) and entry.get('metric') == metric
                   and entry.get('from') == expected_from
                   and entry.get('to') == expected_to
                   and entry.get('source_kind') in ('resource', 'optimization_action')
                   and _nonempty(entry.get('source_id'))
                   and _nonempty_evidence_list(entry.get('evidence'))]
        if not matches:
            errors.append(f'{prefix}.unattributed_delta.{metric}')
    return errors


def performance_decision(task: dict[str, Any], gate: str = 'completion') -> dict[str, Any]:
    """Evaluate current SDK performance evidence; Rank is static and never an FPS claim."""
    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    contract = card.get('performance_contract') if isinstance(card, dict) else None
    contract_errors = validate_performance_contract(contract, manifest)
    if contract_errors:
        return {'status': 'PERFORMANCE_CONTRACT_INVALID', 'errors': contract_errors}
    sdk_context_errors = _sdk_context_errors(manifest)
    current_sdk_version = _current_vrcsdk_version(manifest)
    if sdk_context_errors or current_sdk_version is None:
        return {'status': 'PERFORMANCE_UNVERIFIED',
                'errors': sdk_context_errors or [
                    'assembly_manifest.sdk_context.current_vrcsdk_version']}
    assert isinstance(contract, dict)
    platforms = contract['platforms']
    baseline_by_platform = contract['baseline_measurements']
    final_by_platform = contract['final_measurements']
    contexts: dict[str, tuple[str, Any, str]] = {}
    errors: list[str] = []
    for platform in platforms:
        context = _platform_candidate_context(task, platform, platforms)
        if context is None:
            errors.append(f'platform_candidates.{platform}')
            continue
        candidate, revision, sha256 = context
        if not _nonempty(candidate) or not _valid_revision(revision) or not _valid_sha256(sha256):
            errors.append(f'platform_candidates.{platform}.identity')
            continue
        contexts[platform] = context
        baseline = baseline_by_platform.get(platform)
        final = final_by_platform.get(platform)
        errors.extend(_performance_measurement_errors(
            baseline, platform, require_platform_size=True,
            expected_sdk_version=current_sdk_version))
        errors.extend(_baseline_receipt_errors(
            baseline, platform, current_sdk_version))
        errors.extend(_performance_measurement_errors(
            final, platform, context, require_platform_size=True,
            expected_sdk_version=current_sdk_version))
        if isinstance(final, dict):
            errors.extend(_performance_measurement_job_errors(
                task, platform, final, context, platforms,
                current_sdk_version))
            errors.extend(_performance_action_errors(final, contract, platform))
        if isinstance(baseline, dict) and isinstance(final, dict):
            errors.extend(_baseline_final_lifecycle_errors(
                task, baseline, final, platform))
            errors.extend(_delta_attribution_errors(
                task, contract, baseline, final, platform))
    errors.extend(_cross_platform_evidence_source_errors(
        baseline_by_platform, platforms,
        prefix='performance_baseline_evidence'))
    errors.extend(_cross_platform_evidence_source_errors(
        final_by_platform, platforms, task.get('performance_measurement_jobs')))
    if errors:
        status = ('PERFORMANCE_DELTA_UNATTRIBUTED'
                  if any('.delta_attribution' in error for error in errors)
                  else 'PERFORMANCE_UNVERIFIED')
        return {'status': status, 'errors': errors}

    platform_size_exceeded: dict[str, dict[str, Any]] = {}
    for platform in platforms:
        bundle = final_by_platform[platform]['platform_size']
        limits = PLATFORM_BUNDLE_LIMITS[platform]
        exceeded = {metric: {'actual': bundle[metric], 'limit': limit}
                    for metric, limit in limits.items()
                    if bundle[metric] > limit}
        if exceeded:
            platform_size_exceeded[platform] = exceeded
    if platform_size_exceeded:
        return {'status': 'PLATFORM_SIZE_LIMIT_EXCEEDED',
                'platforms': platform_size_exceeded,
                'note': 'official platform gate; independent from task size_budget'}

    mobile_exceeded: dict[str, dict[str, Any]] = {}
    if 'Android' in platforms:
        snapshot = final_by_platform['Android']['metric_snapshot']
        mobile_exceeded = {
            metric: {'actual': snapshot[metric], 'limit': limit}
            for metric, limit in MOBILE_HARD_COMPONENT_LIMITS.items()
            if snapshot[metric] > limit
        }
    if mobile_exceeded:
        return {'status': 'MOBILE_COMPONENT_LIMIT_EXCEEDED',
                'platform': 'Android', 'metrics': mobile_exceeded}

    hard_exceeded: dict[str, dict[str, Any]] = {}
    for platform in platforms:
        snapshot = final_by_platform[platform]['metric_snapshot']
        exceeded = {
            metric: {'actual': snapshot[metric], 'limit': limit}
            for metric, limit in SDK_HARD_METRIC_LIMITS.items()
            if snapshot[metric] > limit
        }
        if exceeded:
            hard_exceeded[platform] = exceeded
    if hard_exceeded:
        return {'status': 'HARD_COMPONENT_LIMIT_EXCEEDED',
                'platforms': hard_exceeded,
                'note': 'current SDK hard limits cannot be raised by contract'}

    headroom = contract['headroom_policy']
    headroom_gate = 'gate_upload' if gate == 'upload' else 'gate_completion'
    if headroom.get(headroom_gate) is True:
        exhausted: dict[str, dict[str, Any]] = {}
        for platform in platforms:
            snapshot = final_by_platform[platform]['metric_snapshot']
            policy = headroom['platforms'][platform]
            platform_exhausted = {}
            for metric, specification in policy.items():
                threshold = specification['limit'] - specification['reserve']
                if snapshot[metric] > threshold:
                    platform_exhausted[metric] = {
                        'actual': snapshot[metric],
                        'limit': specification['limit'],
                        'reserve': specification['reserve'],
                        'maximum_with_reserve': threshold,
                    }
            if platform_exhausted:
                exhausted[platform] = platform_exhausted
        if exhausted:
            return {'status': 'PERF_HEADROOM_EXHAUSTED',
                    'platforms': exhausted, 'gate': headroom_gate}

    gate_field = 'gate_upload' if gate == 'upload' else 'gate_completion'
    enforce_policy = contract.get(gate_field) is True
    mode = contract['policy_mode']
    target_met: dict[str, bool] = {}
    caps_met: dict[str, bool] = {}
    warnings: list[str] = []
    rank_failures: dict[str, dict[str, str]] = {}
    cap_failures: dict[str, dict[str, dict[str, Any]]] = {}
    for platform in platforms:
        final = final_by_platform[platform]
        rank = final['rank']
        target = contract.get('target_rank', {}).get(platform)
        reporting_caps = contract.get('metric_caps', {}).get(platform)
        if isinstance(reporting_caps, dict) and reporting_caps:
            caps_met[platform] = all(
                final['metric_snapshot'][metric] <= cap
                for metric, cap in reporting_caps.items())
        if target is not None:
            target_met[platform] = (
                PERFORMANCE_RANK_SCORE[rank] >= PERFORMANCE_RANK_SCORE[target])
        elif isinstance(reporting_caps, dict) and reporting_caps:
            target_met[platform] = caps_met[platform]
        else:
            target_met[platform] = False
        if rank in ('Poor', 'VeryPoor'):
            target_met[platform] = False
        handling = contract['rank_handling'][platform]
        if rank in ('Poor', 'VeryPoor'):
            if enforce_policy and handling[rank] == 'block':
                rank_failures[platform] = {'actual': rank, 'required': target or 'Medium'}
            else:
                warnings.append(f'{platform}:{rank}:accepted_with_warning')
        if mode == 'rank_gate' and enforce_policy and not target_met[platform]:
            rank_failures[platform] = {'actual': rank, 'required': target}
        if mode == 'metric_caps':
            caps = contract['metric_caps'][platform]
            exceeded = {metric: {'actual': final['metric_snapshot'][metric], 'cap': cap}
                        for metric, cap in caps.items()
                        if final['metric_snapshot'][metric] > cap}
            if exceeded and enforce_policy:
                cap_failures[platform] = exceeded
            elif exceeded:
                warnings.append(f'{platform}:metric_caps_not_met_but_not_gated')
    if rank_failures:
        return {'status': 'PERFORMANCE_RANK_NOT_MET', 'platforms': rank_failures,
                'gate': gate_field}
    if cap_failures:
        return {'status': 'PERFORMANCE_BUDGET_EXCEEDED', 'platforms': cap_failures,
                'gate': gate_field}
    if any(not met for met in target_met.values()):
        warnings.append('recommended_target_not_met_do_not_claim_performance_goal_achieved')
    return {
        'status': 'PERFORMANCE_OK',
        'policy_mode': mode,
        'target_met': target_met,
        'caps_met': caps_met,
        'warnings': warnings,
        'rank_is_static_analysis_not_fps': True,
        'platforms': {
            platform: {
                'candidate': final_by_platform[platform]['candidate'],
                'candidate_revision': final_by_platform[platform]['candidate_revision'],
                'candidate_sha256': final_by_platform[platform]['candidate_sha256'],
                'sdk_version': final_by_platform[platform]['sdk_version'],
                'rank': final_by_platform[platform]['rank'],
                'worst_metrics': final_by_platform[platform]['worst_metrics'],
            } for platform in platforms
        },
    }


def _extra_optimization_errors(task: dict[str, Any]) -> list[str]:
    """An extra lossy pass is outside the pre-authorized base caps."""
    record = task.get('extra_optimization')
    if not isinstance(record, dict) or record.get('status') in (None, 'NOT_REQUESTED', 'NOT_REQUIRED'):
        return []
    errors: list[str] = []
    if record.get('status') != 'COMPLETE':
        errors.append('extra_optimization.status')
    if record.get('user_authorized') is not True or not _nonempty(record.get('authorization_source')):
        errors.append('extra_optimization.user_authority')
    policy = record.get('policy')
    if not isinstance(policy, dict) or not isinstance(policy.get('changes'), list) or not policy['changes']:
        errors.append('extra_optimization.policy')
    evidence = record.get('evidence')
    if not isinstance(evidence, list) or not any(
            isinstance(item, dict) and item.get('verified') is True and _nonempty(item.get('source'))
            for item in evidence):
        errors.append('extra_optimization.evidence')
    candidate, revision, sha256 = _candidate_context(task)
    if candidate and (record.get('candidate') != candidate
                      or record.get('candidate_revision') != revision
                      or not isinstance(record.get('candidate_sha256'), str)
                      or record.get('candidate_sha256', '').lower() != sha256.lower()):
        errors.append('extra_optimization.candidate_identity')
    return errors


def _has_selected_clothing(task: dict[str, Any]) -> bool:
    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    items = manifest.get('resources') if isinstance(manifest, dict) else None
    if isinstance(items, list):
        return any(isinstance(item, dict) and item.get('kind') in CLOTHING_KINDS
                   and item.get('selected', True) is True for item in items)
    return any(isinstance(item, dict) and item.get('kind') in CLOTHING_KINDS
               and item.get('selected', True) is True for item in task.get('resources', []))


def optimization_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Drive the already-authorized safe optimization without another user prompt."""
    card = task.get('start_contract')
    policy = card.get('base_optimization') if isinstance(card, dict) else None
    policy_errors = validate_optimization_policy(policy)
    if policy_errors:
        return {'status': 'OPTIMIZATION_POLICY_UNSAFE', 'errors': policy_errors,
                'next': 'RETURN_TO_INTAKE'}
    record = task.get('base_optimization')
    if not isinstance(record, dict) or record.get('status') in (None, 'PLANNED', 'PENDING'):
        return {'status': 'SAFE_OPTIMIZATION_READY', 'next': 'APPLY_SAFE_OPTIMIZATION',
                'requires_user_confirmation': False}
    status = record.get('status')
    if status == 'IN_PROGRESS':
        return {'status': 'SAFE_OPTIMIZATION_IN_PROGRESS', 'next': 'CONTINUE_SAFE_OPTIMIZATION',
                'requires_user_confirmation': False}
    if status == 'FAILED':
        return {'status': 'OPTIMIZATION_BLOCKED', 'next': 'ROLLBACK_OR_APPLY_FROZEN_CONTENT_PRIORITY'}
    if status not in ('COMPLETE', 'NOT_REQUIRED'):
        return {'status': 'OPTIMIZATION_STATE_INVALID', 'next': 'RECORD_REAL_OPTIMIZATION_STATE'}
    if status == 'NOT_REQUIRED' and _has_selected_clothing(task):
        return {'status': 'BASE_OPTIMIZATION_COMPLETION_REQUIRED',
                'next': 'APPLY_SAFE_OPTIMIZATION',
                'requires_user_confirmation': False}
    if status == 'COMPLETE':
        errors = validate_optimization_policy(record.get('policy_applied'), 'base_optimization.policy_applied')
        if errors:
            return {'status': 'OPTIMIZATION_EVIDENCE_INVALID', 'errors': errors}
        if record.get('author_sources_unchanged') is not True:
            return {'status': 'OPTIMIZATION_EVIDENCE_INVALID',
                    'errors': ['base_optimization.author_sources_unchanged']}
    elif not _nonempty(record.get('reason')):
        return {'status': 'OPTIMIZATION_EVIDENCE_INVALID',
                'errors': ['base_optimization.reason']}
    evidence = record.get('evidence')
    credible_evidence = (isinstance(evidence, list) and any(
        isinstance(item, dict) and _nonempty(item.get('source'))
        and item.get('verified') is True for item in evidence))
    if not credible_evidence:
        return {'status': 'OPTIMIZATION_EVIDENCE_INVALID',
                'errors': ['base_optimization.evidence']}

    candidate, revision, sha256 = _candidate_context(task)
    if candidate:
        if (record.get('candidate') != candidate
                or record.get('candidate_revision') != revision
                or not isinstance(record.get('candidate_sha256'), str)
                or not _valid_sha256(sha256)
                or record['candidate_sha256'].lower() != sha256.lower()):
            return {'status': 'OPTIMIZATION_EVIDENCE_STALE',
                    'next': 'REAPPLY_OR_RECHECK_CURRENT_CANDIDATE'}
    size = size_decision(task)
    if size['status'] == 'OPTIMIZE_BEFORE_NEXT_BATCH':
        priority = card.get('content_priority', {})
        frozen_action = priority.get('extra_over_budget_action')
        extra = task.get('extra_optimization')
        extra_authorized = (isinstance(extra, dict)
                            and extra.get('user_authorized') is True
                            and _nonempty(extra.get('authorization_source'))
                            and isinstance(extra.get('policy'), dict)
                            and isinstance(extra['policy'].get('changes'), list)
                            and bool(extra['policy']['changes']))
        return {
            'status': 'EXTRA_BUDGET_ACTION_OR_MEASUREMENT_REQUIRED',
            'next': 'APPLY_AUTHORIZED_EXTRA_BUDGET_ACTION_OR_MEASURE',
            'frozen_action': frozen_action,
            'extra_policy_authorized': extra_authorized,
            'if_extra_policy_not_authorized': frozen_action or 'PAUSE_FOR_USER',
            'requires_user_confirmation': not extra_authorized and not _nonempty(frozen_action),
            'size_detail': size,
        }
    extra_errors = _extra_optimization_errors(task)
    if extra_errors:
        return {'status': 'EXTRA_OPTIMIZATION_NOT_AUTHORIZED_OR_INVALID',
                'errors': extra_errors,
                'next': 'ROLLBACK_EXTRA_OPTIMIZATION_OR_GET_EXPLICIT_AUTHORITY'}

    phase = task.get('phase')
    if phase == 'INTAKE':
        next_phase = 'PREFLIGHT'
    elif phase == 'PREFLIGHT':
        preflight = preflight_decision(task)
        if preflight.get('status') != 'READY_FOR_INSTALLATION':
            return preflight
        next_phase = 'INSTALLING'
    elif phase == 'INSTALLING':
        next_phase = ('FINAL_VALIDATION'
                      if batch_decision(task).get('status') == 'READY_FOR_FINAL_VALIDATION'
                      else 'INSTALLING')
    elif phase in ('FINAL_VALIDATION', 'COMPLETE'):
        next_phase = phase
    else:
        next_phase = 'PREFLIGHT'
    return {'status': 'SAFE_OPTIMIZATION_COMPLETE' if status == 'COMPLETE' else 'OPTIMIZATION_NOT_REQUIRED',
            'next': next_phase, 'requires_user_confirmation': False}


def _clothing_preflight_errors(
    resource: dict[str, Any], manifest_item: dict[str, Any], affected_chest_states: Any
) -> list[str]:
    """Validate expected parts and the fail-closed chest admission record."""
    resource_id = resource.get('id') or manifest_item.get('id') or '<unnamed>'
    prefix = f'resources.{resource_id}'
    errors: list[str] = []
    parts = resource.get('expected_parts')
    required_checks = resource.get('required')
    check_by_id = ({check.get('id'): check for check in required_checks
                    if isinstance(check, dict) and _nonempty(check.get('id'))}
                   if isinstance(required_checks, list) else {})
    valid_parts = (isinstance(parts, list) and bool(parts)
                   and all(isinstance(part, dict) and _nonempty(part.get('id'))
                           and _nonempty(part.get('required_check_id'))
                           and part['required_check_id'] in check_by_id
                           and part.get('verification', 'visual') in ('visual', 'structure')
                           and check_by_id[part['required_check_id']].get('kind')
                           == part.get('verification', 'visual')
                           and (part.get('verification', 'visual') != 'structure'
                                or part.get('visible') is False)
                           for part in parts))
    if (not valid_parts
            or len({part.get('id') for part in parts if isinstance(part, dict)}) != len(parts)
            or len({part.get('required_check_id') for part in parts
                    if isinstance(part, dict)}) != len(parts)):
        errors.append(f'{prefix}.expected_parts')
    check_kinds = {check.get('kind') for check in required_checks
                   if isinstance(check, dict)} if isinstance(required_checks, list) else set()
    missing_kinds = {'visual', 'pose', 'behavior'} - check_kinds
    if missing_kinds:
        errors.append(f'{prefix}.required_checks_missing_{"_".join(sorted(missing_kinds))}')
    preflight = resource.get('preflight')
    if not isinstance(preflight, dict):
        return [*errors, f'{prefix}.preflight']
    covers_chest = preflight.get('covers_chest')
    if not isinstance(covers_chest, bool):
        errors.append(f'{prefix}.preflight.covers_chest')
        return errors
    adjustment = preflight.get('breast_adjustment')
    if not isinstance(adjustment, dict):
        errors.append(f'{prefix}.preflight.breast_adjustment')
        return errors
    status = adjustment.get('status')
    if covers_chest:
        if not isinstance(affected_chest_states, list) or not affected_chest_states:
            errors.append('content_priority.affected_chest_states_for_covering_clothing')
        required_states = affected_chest_states if isinstance(affected_chest_states, list) else []
        resource_states = resource.get('affected_chest_states')
        if resource_states != required_states:
            errors.append(f'{prefix}.affected_chest_states_must_match_requested')
        state_check_ids: dict[str, set[str]] = {}
        state_metadata_valid = True
        for check in required_checks if isinstance(required_checks, list) else []:
            if not isinstance(check, dict) or check.get('kind') != 'visual':
                continue
            if 'chest_state' in check and not _nonempty(check.get('chest_state')):
                state_metadata_valid = False
            for field in ('affected_chest_states', 'chest_states'):
                values = check.get(field)
                if (field in check
                        and (not isinstance(values, list) or not values
                             or any(not _nonempty(state) for state in values)
                             or len(values) != len(set(values)))):
                    state_metadata_valid = False
            if _nonempty(check.get('id')):
                for state in _required_visual_states(check):
                    state_check_ids.setdefault(state, set()).add(check['id'])
        if (not state_metadata_valid
                or any(not state_check_ids.get(state) for state in required_states)):
            errors.append(f'{prefix}.required_checks_chest_states')
        if status == 'SUPPORTED':
            if adjustment.get('method') not in BREAST_ADJUSTMENT_METHODS:
                errors.append(f'{prefix}.preflight.breast_adjustment.method')
            supported_states = adjustment.get('supported_states')
            if (not isinstance(supported_states, list)
                    or any(not _nonempty(state) for state in supported_states)
                    or len(supported_states) != len(set(supported_states))
                    or not set(required_states).issubset(supported_states)):
                errors.append(f'{prefix}.preflight.breast_adjustment.supported_states')
            adjustment_evidence = adjustment.get('evidence')
            credible = (isinstance(adjustment_evidence, list) and bool(adjustment_evidence)
                        and all(isinstance(item, dict)
                                and item.get('verified') is True
                                and _nonempty(item.get('source'))
                                and (_nonempty(item.get('version_or_mapping'))
                                     or _nonempty(item.get('version'))
                                     or _nonempty(item.get('mapping')))
                                for item in adjustment_evidence))
            if not credible:
                errors.append(f'{prefix}.preflight.breast_adjustment.evidence')
        elif status == 'UNSUPPORTED':
            if (resource.get('status') != 'REJECTED'
                    or resource.get('rejection_code') != 'UNSUPPORTED_BREAST_ADJUSTMENT'):
                errors.append(f'{prefix}.preflight.unsupported_must_be_rejected')
        else:
            errors.append(f'{prefix}.preflight.breast_adjustment.status')
    elif status != 'NOT_APPLICABLE':
        errors.append(f'{prefix}.preflight.breast_adjustment.must_be_not_applicable')
    return errors


def _selected_clothing_preflight_errors(task: dict[str, Any]) -> list[str]:
    """Close every selected clothing manifest item to a complete runtime preflight."""
    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    manifest_items = manifest.get('resources') if isinstance(manifest, dict) else None
    resources = task.get('resources')
    if not isinstance(manifest_items, list) or not isinstance(resources, list):
        return ['preflight.manifest_or_resources']
    runtime_by_id = {item.get('id'): item for item in resources
                     if isinstance(item, dict) and _nonempty(item.get('id'))}
    affected = (card.get('content_priority', {}).get('requested_affected_chest_states')
                if isinstance(card, dict) else None)
    errors: list[str] = []
    complete_states = {
        'ADMITTED', 'INSTALLED_UNVERIFIED', 'SMOKE_FAILED', 'ROLLED_BACK',
        'REJECTED', 'SKIPPED', 'LOCAL_OK',
    }
    for manifest_item in manifest_items:
        if (not isinstance(manifest_item, dict)
                or manifest_item.get('kind') not in CLOTHING_KINDS
                or manifest_item.get('selected', True) is not True):
            continue
        resource_id = manifest_item.get('id')
        runtime = runtime_by_id.get(resource_id)
        if not isinstance(runtime, dict):
            errors.append(f'resources.{resource_id}.preflight_runtime_record_missing')
            continue
        if (runtime.get('selected', True) is not True
                and runtime.get('status') not in ('REJECTED', 'ROLLED_BACK', 'SKIPPED')):
            errors.append(f'resources.{resource_id}.selected_scope_mismatch')
            continue
        errors.extend(_clothing_preflight_errors(runtime, manifest_item, affected))
        if runtime.get('status') not in complete_states:
            errors.append(f'resources.{resource_id}.preflight_status_not_complete')
    return errors


def installing_resource_status_errors(task: dict[str, Any]) -> list[str]:
    """Reject premature acceptance and unknown resource states during installation."""
    if task.get('phase') != 'INSTALLING':
        return []
    errors: list[str] = []
    for index, resource in enumerate(task.get('resources', [])):
        if not isinstance(resource, dict):
            errors.append(f'resources[{index}]')
            continue
        resource_id = resource.get('id') or resource.get('source') or f'<resource-{index}>'
        status = resource.get('status')
        if status == 'LOCAL_OK':
            errors.append(f'resources.{resource_id}.LOCAL_OK_before_unified_validation')
        elif status not in INSTALLING_RESOURCE_STATES:
            errors.append(f'resources.{resource_id}.invalid_installing_status')
    return errors


def _performance_baseline_preflight_errors(task: dict[str, Any]) -> list[str]:
    """Require formal per-platform baseline reports before any installation."""
    card = task.get('start_contract')
    contract = card.get('performance_contract') if isinstance(card, dict) else None
    if not isinstance(contract, dict):
        return ['performance_contract']
    platforms = contract.get('platforms')
    baselines = contract.get('baseline_measurements')
    if not isinstance(platforms, list) or not isinstance(baselines, dict):
        return ['performance_contract.baseline_measurements']
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    errors: list[str] = _sdk_context_errors(manifest)
    current_sdk_version = _current_vrcsdk_version(manifest)
    if set(baselines) != set(platforms):
        errors.append('performance_contract.baseline_measurements.platform_set')
    for platform in platforms:
        record = baselines.get(platform)
        context = None
        if isinstance(record, dict):
            context = (record.get('candidate', ''),
                       record.get('candidate_revision'),
                       record.get('candidate_sha256', ''))
        errors.extend(_performance_measurement_errors(
            record, platform, context, require_platform_size=True,
            expected_sdk_version=current_sdk_version))
        errors.extend(_baseline_receipt_errors(
            record, platform, current_sdk_version))
    errors.extend(_cross_platform_evidence_source_errors(
        baselines, platforms, prefix='performance_baseline_evidence'))
    return errors


def preflight_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Fail closed before PREFLIGHT can advance to Unity installation."""
    if intake_decision(task).get('status') != 'READY_FOR_UNITY_WRITE':
        return {'status': 'INTAKE_NOT_READY'}
    errors = _selected_clothing_preflight_errors(task)
    errors.extend(_selected_resource_performance_impact_errors(task))
    baseline_errors = _performance_baseline_preflight_errors(task)
    errors.extend(baseline_errors)
    if errors:
        result = {'status': 'PREFLIGHT_NOT_READY', 'errors': errors,
                'next': 'COMPLETE_OR_REJECT_EACH_SELECTED_CLOTHING_PREFLIGHT'}
        if baseline_errors:
            result['performance_status'] = 'PERFORMANCE_BASELINE_UNVERIFIED'
        return result
    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    manifest_items = manifest.get('resources') if isinstance(manifest, dict) else []
    resources = task.get('resources') if isinstance(task.get('resources'), list) else []
    runtime_by_id = {resource.get('id'): resource for resource in resources
                     if isinstance(resource, dict) and _nonempty(resource.get('id'))}
    install_resource_ids: list[str] = []
    isolated_optional_resource_ids: list[str] = []
    for manifest_item in manifest_items:
        if (not isinstance(manifest_item, dict)
                or manifest_item.get('selected', True) is not True):
            continue
        resource_id = manifest_item.get('id')
        resource = runtime_by_id.get(resource_id)
        impact = (resource.get('preflight', {}).get('performance_impact')
                  if isinstance(resource, dict) else None)
        admission = impact.get('admission') if isinstance(impact, dict) else None
        if isinstance(admission, dict) and admission.get('decision') == 'REJECT':
            isolated_optional_resource_ids.append(resource_id)
        else:
            install_resource_ids.append(resource_id)
    return {'status': 'READY_FOR_INSTALLATION',
            'install_resource_ids': install_resource_ids,
            'isolated_optional_resource_ids': isolated_optional_resource_ids}


def batch_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Require every selected resource to finish install or safe isolation first."""
    resources = task.get('resources')
    if not isinstance(resources, list) or not resources:
        return {'status': 'BATCH_NOT_READY', 'missing': ['resources']}
    card = task.get('start_contract')
    manifest = card.get('assembly_manifest') if isinstance(card, dict) else None
    manifest_items = manifest.get('resources') if isinstance(manifest, dict) else None
    isolated_unselected: list[str] = []
    if isinstance(manifest_items, list):
        manifest_by_id = {r.get('id'): r for r in manifest_items
                          if isinstance(r, dict) and _nonempty(r.get('id'))}
        runtime_by_id = {r.get('id'): r for r in resources
                         if isinstance(r, dict) and _nonempty(r.get('id'))}
        missing_required = [rid for rid, item in manifest_by_id.items()
                            if item.get('requirement') == 'required'
                            and (rid not in runtime_by_id
                                 or runtime_by_id[rid].get('selected', True) is not True)]
        if missing_required:
            return {'status': 'REQUIRED_RESOURCE_UNAVAILABLE', 'resources': missing_required,
                    'next': 'BLOCK_OR_GET_EXPLICIT_SCOPE_CHANGE'}
        missing_records = [rid for rid in manifest_by_id if rid not in runtime_by_id]
        extra_records = [rid for rid in runtime_by_id if rid not in manifest_by_id]
        requirement_mismatch = [rid for rid, resource in runtime_by_id.items()
                                if rid in manifest_by_id
                                and resource.get('requirement', 'required') != manifest_by_id[rid].get('requirement')]
        kind_mismatch = [rid for rid, resource in runtime_by_id.items()
                         if rid in manifest_by_id and resource.get('kind') != manifest_by_id[rid].get('kind')]
        if missing_records or extra_records or requirement_mismatch or kind_mismatch:
            return {'status': 'BATCH_NOT_READY', 'manifest_mismatch': {
                'missing_records': missing_records,
                'extra_records': extra_records,
                'requirement_mismatch': requirement_mismatch,
                'kind_mismatch': kind_mismatch,
            }}
        unavailable_states = {'REJECTED', 'ROLLED_BACK', 'SKIPPED'}
        optional_unselected_invalid: list[str] = []
        for resource_id, manifest_item in manifest_by_id.items():
            if manifest_item.get('requirement') != 'optional':
                continue
            runtime = runtime_by_id.get(resource_id)
            manifest_selected = manifest_item.get('selected', True) is True
            runtime_selected = runtime.get('selected', True) is True if runtime else False
            if not manifest_selected or not runtime_selected:
                if (not runtime or runtime.get('status') not in unavailable_states
                        or runtime.get('isolation_confirmed') is not True):
                    optional_unselected_invalid.append(resource_id)
                else:
                    isolated_unselected.append(resource_id)
        if optional_unselected_invalid:
            return {'status': 'BATCH_NOT_READY',
                    'pending_optional_isolation': optional_unselected_invalid}

        preflight_errors = _selected_clothing_preflight_errors(task)
        preflight_errors.extend(_selected_resource_performance_impact_errors(task))
        if preflight_errors:
            return {'status': 'PREFLIGHT_NOT_READY', 'errors': preflight_errors}
        relevant = [runtime_by_id[rid] for rid, item in manifest_by_id.items()
                    if item.get('selected', True) is True and rid in runtime_by_id]
    else:
        relevant = [r for r in resources if r.get('selected', True) is True]
    if not relevant:
        return {'status': 'BATCH_NOT_READY', 'missing': ['selected_resources']}
    invalid_requirements = [r.get('id') or r.get('source') or '<unnamed>' for r in relevant
                            if r.get('requirement', 'required') not in RESOURCE_REQUIREMENTS]
    if invalid_requirements:
        return {'status': 'BATCH_NOT_READY', 'invalid_requirement': invalid_requirements}
    phase_status_errors = installing_resource_status_errors(task)
    if phase_status_errors:
        return {'status': 'RESOURCE_STATUS_PHASE_INVALID',
                'errors': phase_status_errors}
    ready_states = {'INSTALLED_UNVERIFIED', 'LOCAL_OK', 'REJECTED', 'ROLLED_BACK', 'SKIPPED'}
    pending = [r.get('id') or r.get('source') or '<unnamed>' for r in relevant
               if r.get('status') not in ready_states]
    unavailable_states = {'REJECTED', 'ROLLED_BACK', 'SKIPPED'}
    required_unavailable = [r.get('id') or r.get('source') or '<unnamed>' for r in relevant
                            if r.get('requirement', 'required') == 'required'
                            and r.get('status') in unavailable_states]
    if required_unavailable:
        return {'status': 'REQUIRED_RESOURCE_UNAVAILABLE',
                'resources': required_unavailable,
                'next': 'BLOCK_OR_GET_EXPLICIT_SCOPE_CHANGE'}
    optional_not_isolated = [r.get('id') or r.get('source') or '<unnamed>' for r in relevant
                             if r.get('requirement') == 'optional'
                             and r.get('status') in unavailable_states
                             and r.get('isolation_confirmed') is not True]
    if optional_not_isolated:
        return {'status': 'BATCH_NOT_READY', 'pending_isolation': optional_not_isolated}
    installed = [r for r in relevant if r.get('status') in ('INSTALLED_UNVERIFIED', 'LOCAL_OK')]
    if pending or not installed:
        return {'status': 'BATCH_NOT_READY', 'pending': pending,
                'missing': [] if installed else ['installed_resource']}
    menu_binding_errors = _selected_main_outfit_menu_errors(
        task, card.get('menu_plan') if isinstance(card, dict) else None)
    if menu_binding_errors:
        return {'status': 'MENU_PLAN_INCOMPLETE', 'errors': menu_binding_errors,
                'next': 'FIX_MENU_PLAN_BEFORE_FINAL_VALIDATION'}
    isolated_ids = {r.get('id') or r.get('source') or '<unnamed>' for r in relevant
                    if r.get('requirement') == 'optional'
                    and r.get('status') in unavailable_states}
    isolated_ids.update(isolated_unselected)
    return {'status': 'READY_FOR_FINAL_VALIDATION', 'resource_count': len(relevant),
            'installed_count': len(installed),
            'isolated_optional_count': len(isolated_ids)}


def _final_validation_record_errors(
    record: Any, context: tuple[str, Any, str], prefix: str,
) -> list[str]:
    candidate, revision, sha256 = context
    if not isinstance(record, dict):
        return [prefix]
    errors: list[str] = []
    if (record.get('candidate') != candidate
            or record.get('candidate_revision') != revision
            or not isinstance(record.get('candidate_sha256'), str)
            or record.get('candidate_sha256', '').lower() != sha256.lower()):
        errors.append(f'{prefix}.candidate_identity')
    if record.get('status') != 'LOCAL_OK':
        errors.append(f'{prefix}.status')
    evidence = record.get('evidence')
    fresh = (isinstance(evidence, list) and any(
        _evidence_is_current(item, candidate, revision, sha256)
        and item.get('observed') is True
        and _nonempty(item.get('source')) and _nonempty(item.get('note'))
        for item in evidence if isinstance(item, dict)))
    if not fresh:
        errors.append(f'{prefix}.current_evidence')
    return errors


def _platform_override_semantic_decision(
    task: dict[str, Any], primary_context: tuple[str, Any, str]
) -> dict[str, Any]:
    """Different per-platform override candidates need their own semantic acceptance."""
    card = task.get('start_contract')
    contract = card.get('performance_contract') if isinstance(card, dict) else None
    platforms = contract.get('platforms') if isinstance(contract, dict) else None
    if not isinstance(platforms, list) or len(platforms) <= 1:
        return {'status': 'PLATFORM_SEMANTICS_OK', 'override_count': 0}
    contexts = {platform: _platform_candidate_context(task, platform, platforms)
                for platform in platforms}
    if primary_context not in contexts.values():
        return {'status': 'PLATFORM_SEMANTIC_VALIDATION_FAILED',
                'errors': ['top_level_candidate_not_in_platform_candidates']}
    override_platforms = [platform for platform, context in contexts.items()
                          if context is not None and context != primary_context]
    if not override_platforms:
        return {'status': 'PLATFORM_SEMANTICS_OK', 'override_count': 0,
                'note': 'same source candidate semantics reused; jobs/reports remain per-platform'}
    final_records = task.get('platform_final_validations')
    menu_records = task.get('platform_menu_validations')
    errors: list[str] = []
    resource_failures: dict[str, Any] = {}
    menu_failures: dict[str, Any] = {}
    for platform in override_platforms:
        context = contexts[platform]
        assert context is not None
        record = final_records.get(platform) if isinstance(final_records, dict) else None
        errors.extend(_final_validation_record_errors(
            record, context, f'platform_final_validations.{platform}'))
        if not isinstance(record, dict) or record.get('local_decision') != 'LOCAL_OK':
            errors.append(f'platform_final_validations.{platform}.local_decision')

        platform_menu = menu_records.get(platform) if isinstance(menu_records, dict) else None
        platform_task = dict(task)
        platform_task['candidate'], platform_task['candidate_revision'], \
            platform_task['candidate_sha256'] = context
        platform_task['menu_validation'] = platform_menu
        menu = menu_validation_decision(platform_task)
        if menu.get('status') != 'MENU_LOCAL_OK':
            menu_failures[platform] = menu

        candidate, revision, sha256 = context
        failures: dict[str, Any] = {}
        for index, resource in enumerate(task.get('resources', [])):
            if not isinstance(resource, dict) or resource.get('selected', True) is not True:
                continue
            if (resource.get('requirement') == 'optional'
                    and resource.get('status') in ('REJECTED', 'ROLLED_BACK', 'SKIPPED')):
                continue
            resource_id = resource.get('id') or resource.get('source') or f'<resource-{index}>'
            assessed = assess_resource(resource, candidate, revision, sha256)
            if assessed.get('status') != 'LOCAL_OK':
                failures[resource_id] = assessed
        if failures:
            resource_failures[platform] = failures
    if errors or menu_failures or resource_failures:
        return {'status': 'PLATFORM_SEMANTIC_VALIDATION_FAILED',
                'errors': errors, 'menus': menu_failures,
                'resources': resource_failures}
    return {'status': 'PLATFORM_SEMANTICS_OK',
            'override_count': len(override_platforms),
            'platforms': override_platforms}


def final_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Check recorded final-batch state; cannot authenticate images or Unity observations."""
    if intake_decision(task)['status'] != 'READY_FOR_UNITY_WRITE':
        return {'status': 'MIGRATION_OR_INTAKE_REQUIRED'}
    batch = batch_decision(task)
    if batch['status'] != 'READY_FOR_FINAL_VALIDATION':
        return {'status': batch['status'], 'detail': batch}
    observation_errors = _observation_identity_errors(task)
    if observation_errors:
        return {'status': 'OBSERVATION_IDENTITY_INVALID',
                'errors': observation_errors}
    candidate, candidate_revision, candidate_sha256 = _candidate_context(task)
    final = task.get('final_validation')
    if (not candidate or task.get('scene_candidate') != candidate
            or task.get('local_decision_candidate') != candidate):
        return {'status': 'CANDIDATE_MISMATCH'}
    if not _valid_revision(candidate_revision) or not _valid_sha256(candidate_sha256):
        return {'status': 'CANDIDATE_IDENTITY_INCOMPLETE',
                'missing': ['candidate_revision', 'candidate_sha256']}
    if (not isinstance(final, dict) or final.get('candidate') != candidate
            or final.get('candidate_revision') != candidate_revision
            or not isinstance(final.get('candidate_sha256'), str)
            or final['candidate_sha256'].lower() != candidate_sha256.lower()):
        return {'status': 'FINAL_VALIDATION_MISSING'}
    if final.get('status') != 'LOCAL_OK' or not isinstance(final.get('evidence'), list):
        return {'status': 'FINAL_VALIDATION_NOT_OK'}
    fresh_final_evidence = [e for e in final['evidence']
                            if _evidence_is_current(e, candidate, candidate_revision, candidate_sha256)
                            and e.get('observed') is True and e.get('source') and e.get('note')]
    if not fresh_final_evidence:
        return {'status': 'FINAL_EVIDENCE_STALE_OR_MISSING'}

    menu = menu_validation_decision(task)
    if menu['status'] != 'MENU_LOCAL_OK':
        return {'status': menu['status'], 'detail': menu}

    resource_results: dict[str, Any] = {}
    for index, resource in enumerate(task.get('resources', [])):
        if resource.get('selected', True) is not True:
            continue
        if resource.get('requirement') == 'optional' and resource.get('status') in ('REJECTED', 'ROLLED_BACK', 'SKIPPED'):
            continue
        resource_id = resource.get('id') or resource.get('source') or f'<resource-{index}>'
        assessed = assess_resource(resource, candidate, candidate_revision, candidate_sha256)
        if assessed['status'] != 'LOCAL_OK':
            resource_results[resource_id] = assessed
    if resource_results:
        return {'status': 'RESOURCE_VALIDATION_NOT_OK', 'resources': resource_results}

    platform_semantics = _platform_override_semantic_decision(
        task, (candidate, candidate_revision, candidate_sha256))
    if platform_semantics['status'] != 'PLATFORM_SEMANTICS_OK':
        return {'status': platform_semantics['status'],
                'detail': platform_semantics}

    unresolved = [r.get('id') or r.get('source') or '<unnamed>'
                  for r in task.get('resources', [])
                  if r.get('status') == 'INSTALLED_UNVERIFIED']
    if unresolved:
        return {'status': 'INSTALLED_UNVERIFIED_REMAINS', 'resources': unresolved}
    if task.get('local_decision') != 'LOCAL_OK':
        return {'status': 'LOCAL_DECISION_NOT_OK'}
    optimization = optimization_decision(task)
    if optimization['status'] not in ('SAFE_OPTIMIZATION_COMPLETE', 'OPTIMIZATION_NOT_REQUIRED'):
        return {'status': optimization['status'], 'detail': optimization}
    budget = task['start_contract'].get('size_budget', {})
    if budget.get('gate_completion', True) is True:
        size = size_decision(task)
        if size['status'] != 'SIZE_OK':
            return {'status': size['status'], 'detail': size}
    performance = performance_decision(task, 'completion')
    if performance['status'] != 'PERFORMANCE_OK':
        return {'status': performance['status'], 'detail': performance}
    return {'status': 'LOCAL_OK', 'scope': 'recorded unified checks only'}


def _upload_terminal_claimed(task: dict[str, Any]) -> bool:
    remote = task.get('upload_remote')
    result = task.get('upload_result')
    outcome = task.get('upload_outcome')
    return (
        isinstance(remote, dict) and remote.get('status') in TERMINAL_UPLOAD_STATUSES
        or isinstance(result, dict) and result.get('status') in TERMINAL_UPLOAD_STATUSES
        or isinstance(outcome, str) and 'UPLOAD_CONFIRMED' in outcome
    )


def upload_terminal_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Validate a claimed remote terminal state against frozen authority and target."""
    if not _upload_terminal_claimed(task):
        return {'status': 'NO_TERMINAL_UPLOAD_RESULT'}
    errors: list[str] = []
    if task.get('upload_authorized') is not True:
        errors.append('upload_authorized')
    if task.get('private') is not True:
        errors.append('private')
    if task.get('remote_pending') is not False:
        errors.append('remote_pending')
    if task.get('build_succeeded') is not True:
        errors.append('build_succeeded')

    authority = task.get('upload_authority')
    if not isinstance(authority, dict):
        errors.append('upload_authority')
        authority = {}
    for key in ('account_id', 'display_name', 'candidate'):
        if not _nonempty(authority.get(key)):
            errors.append(f'upload_authority.{key}')
    if authority.get('release') != 'private':
        errors.append('upload_authority.release')
    candidate, revision, sha256 = _candidate_context(task)
    if (authority.get('candidate') != candidate
            or authority.get('candidate_revision') != revision
            or not isinstance(authority.get('candidate_sha256'), str)
            or not _valid_sha256(sha256)
            or authority.get('candidate_sha256', '').lower() != sha256.lower()):
        errors.append('upload_authority.candidate_identity')

    target = task.get('upload_target')
    if not isinstance(target, dict):
        errors.append('upload_target')
        target = {}
    for key in ('prefab', 'blueprint_id'):
        if not _nonempty(target.get(key)):
            errors.append(f'upload_target.{key}')
    if not _valid_sha256(target.get('prefab_sha256')):
        errors.append('upload_target.prefab_sha256')
    if (not isinstance(target.get('derived_from_candidate_sha256'), str)
            or target.get('derived_from_candidate_sha256', '').lower() != sha256.lower()):
        errors.append('upload_target.derived_from_candidate_sha256')

    remote = task.get('upload_remote')
    if not isinstance(remote, dict):
        errors.append('upload_remote')
        remote = {}
    if remote.get('status') not in TERMINAL_UPLOAD_STATUSES:
        errors.append('upload_remote.status')
    if remote.get('release') != 'private':
        errors.append('upload_remote.release')
    if remote.get('pendingUpload') is not False:
        errors.append('upload_remote.pendingUpload')
    if remote.get('windowsAsset') is not True:
        errors.append('upload_remote.windowsAsset')
    if not isinstance(remote.get('version'), int) or isinstance(remote.get('version'), bool) or remote.get('version', 0) <= 0:
        errors.append('upload_remote.version')
    if not _nonempty(remote.get('completedUtc')):
        errors.append('upload_remote.completedUtc')
    if remote.get('id') != target.get('blueprint_id'):
        errors.append('upload_remote.id_mismatch')
    if remote.get('name') != authority.get('display_name'):
        errors.append('upload_remote.name_mismatch')
    if remote.get('authorId') != authority.get('account_id'):
        errors.append('upload_remote.author_mismatch')
    local = final_decision(task)
    if local.get('status') != 'LOCAL_OK':
        errors.append(f'local_final.{local.get("status", "UNKNOWN")}')
    card = task.get('start_contract')
    size_budget = card.get('size_budget') if isinstance(card, dict) else None
    if isinstance(size_budget, dict) and size_budget.get('gate_upload') is True:
        size = size_decision(task)
        if size.get('status') != 'SIZE_OK':
            errors.append(f'upload_size_gate.{size.get("status", "UNKNOWN")}')
    performance_contract = (card.get('performance_contract')
                            if isinstance(card, dict) else None)
    if (isinstance(performance_contract, dict)
            and performance_contract.get('gate_upload') is True):
        performance = performance_decision(task, 'upload')
        if performance.get('status') != 'PERFORMANCE_OK':
            errors.append(
                f'upload_performance_gate.{performance.get("status", "UNKNOWN")}')
    if errors:
        return {'status': 'UPLOAD_TERMINAL_INCONSISTENT', 'errors': errors}
    return {'status': 'UPLOAD_CONFIRMED', 'avatar_id': remote['id'],
            'name': remote['name'], 'author_id': remote['authorId'],
            'version': remote['version'], 'release': 'private'}


def upload_decision(task: dict[str, Any]) -> dict[str, Any]:
    """Returns the next action, never performs a build or a network request."""
    if task.get('cancelled'):
        return {'next': 'STOP_CANCELLED'}
    if task.get('remote_pending'):
        return {'next': 'RECONCILE_REMOTE_ONLY'}
    if _upload_terminal_claimed(task):
        terminal = upload_terminal_decision(task)
        if terminal['status'] == 'UPLOAD_CONFIRMED':
            return {'next': 'UPLOAD_COMPLETE', 'terminal': terminal}
        return {'next': 'UPLOAD_TERMINAL_INCONSISTENT', 'detail': terminal}
    if task.get('mode') == 'workflow_test':
        return {'next': 'TEST_FINISHED_NO_UPLOAD'}
    if not task.get('upload_authorized'):
        return {'next': 'NO_UPLOAD_AUTHORITY'}
    final = final_decision(task)['status']
    if final != 'LOCAL_OK':
        return {'next': final}
    cid = task.get('candidate')
    if not cid or task.get('scene_candidate') != cid or task.get('local_decision_candidate') != cid:
        return {'next': 'CANDIDATE_MISMATCH'}
    if task.get('local_decision') != 'LOCAL_OK':
        return {'next': 'NOT_READY_LOCAL'}
    if task.get('global_issue') and not task.get('user_accepts_global_issue'):
        return {'next': 'PAUSE_FOR_GLOBAL_DECISION'}
    if task.get('blocking_sdk_error') or task.get('blueprint_permission_error'):
        return {'next': 'TECHNICALLY_BLOCKED'}
    card = task.get('start_contract')
    budget = card.get('size_budget') if isinstance(card, dict) else None
    if isinstance(budget, dict) and budget.get('gate_upload') is True:
        size = size_decision(task)['status']
        if size == 'SIZE_BUDGET_EXCEEDED':
            return {'next': 'SIZE_BUDGET_EXCEEDED'}
        if size != 'SIZE_OK':
            return {'next': 'SIZE_UNVERIFIED'}
    performance_contract = card.get('performance_contract') if isinstance(card, dict) else None
    if (isinstance(performance_contract, dict)
            and performance_contract.get('gate_upload') is True):
        performance = performance_decision(task, 'upload')
        if performance.get('status') != 'PERFORMANCE_OK':
            return {'next': performance.get('status', 'PERFORMANCE_UNVERIFIED'),
                    'detail': performance}
    if not task.get('private'):
        return {'next': 'PRIVATE_REQUIRED'}
    if not task.get('build_succeeded'):
        return {'next': 'BUILD_FOR_UPLOAD'}
    return {'next': 'UPLOAD_PRIVATE'}


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('action', choices=['route', 'assess', 'upload', 'upload-terminal',
                                      'check-key', 'intake', 'menu', 'menu-result', 'size',
                                      'performance', 'preflight', 'optimization',
                                      'batch-ready', 'final'])
    p.add_argument('input', type=Path)
    p.add_argument('--candidate')
    p.add_argument('--candidate-revision')
    p.add_argument('--candidate-sha256')
    a = p.parse_args()
    try:
        data = json.loads(a.input.read_text(encoding='utf-8-sig'))
        if a.action == 'route': result = route(data)
        elif a.action == 'assess': result = assess_resource(
            data, a.candidate or data.get('candidate', ''),
            a.candidate_revision or data.get('candidate_revision'),
            a.candidate_sha256 or data.get('candidate_sha256', ''))
        elif a.action == 'upload': result = upload_decision(data)
        elif a.action == 'upload-terminal': result = upload_terminal_decision(data)
        elif a.action == 'intake': result = intake_decision(data)
        elif a.action == 'preflight': result = preflight_decision(data)
        elif a.action == 'menu':
            errors = validate_menu_plan(data)
            result = {'status': 'MENU_PLAN_OK' if not errors else 'MENU_PLAN_INVALID',
                      'errors': errors}
        elif a.action == 'menu-result': result = menu_validation_decision(data)
        elif a.action == 'size': result = size_decision(data)
        elif a.action == 'performance': result = performance_decision(data)
        elif a.action == 'optimization': result = optimization_decision(data)
        elif a.action == 'batch-ready': result = batch_decision(data)
        elif a.action == 'final': result = final_decision(data)
        else: result = {'key': evidence_key(data)}
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, TypeError, KeyError) as ex:
        print(json.dumps({'error': str(ex)}, ensure_ascii=False)); return 2

if __name__ == '__main__':
    raise SystemExit(main())
