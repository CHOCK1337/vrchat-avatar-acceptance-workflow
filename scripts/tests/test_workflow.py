from __future__ import annotations

import copy
import sys
import unittest
from pathlib import Path


SCRIPTS = Path(__file__).resolve().parents[1]
SKILL_ROOT = SCRIPTS.parent
sys.path.insert(0, str(SCRIPTS))

import task_store  # noqa: E402
import workflow  # noqa: E402


SHA = 'a' * 64
TARGET_SHA = 'b' * 64
BASELINE_SHA = 'c' * 64


def safe_policy() -> dict:
    return {
        'auto_apply': True,
        'copy_scope': 'task_generated_copies_only',
        'preserve_author_sources': True,
        'texture_max_size': {
            'clothing_main_color': 1024,
            'clothing_normal': 1024,
            'mask_ao': 512,
            'reflection_cubemap': 256,
        },
        'protected_quality': ['face', 'eyes', 'skin', 'primary_hair'],
        'aao_cleanup': 'proven_unused_only',
        'auto_continue_after_success': True,
    }


def metric_snapshot(**overrides: int) -> dict:
    metrics = {
        'triangles': 100_000,
        'skinned_mesh_renderers': 10,
        'basic_mesh_renderers': 5,
        'material_slots': 20,
        'animators': 1,
        'bones': 120,
        'lights': 0,
        'texture_memory_bytes': 50_000_000,
        'physbone_components': 4,
        'physbone_transforms': 32,
        'physbone_colliders': 8,
        'physbone_collision_checks': 20,
        'contacts': 4,
        'constraints': 10,
        'constraint_depth': 4,
        'particle_systems': 2,
        'active_particles': 100,
        'mesh_particle_triangles': 0,
        'trail_renderers': 0,
        'line_renderers': 0,
        'raycasts': 0,
        'cloth_components': 0,
        'cloth_vertices': 0,
        'physics_colliders': 0,
        'rigidbodies': 0,
        'audio_sources': 1,
        'expression_parameter_bits': 128,
        'bounds_x_m': 2.0,
        'bounds_y_m': 2.0,
        'bounds_z_m': 2.0,
    }
    metrics.update(overrides)
    return metrics


def performance_measurement(platform: str, candidate: str, revision: str,
                            sha256: str, **metric_overrides: int) -> dict:
    snapshot = metric_snapshot(**metric_overrides)
    is_baseline = 'base' in candidate.lower()
    measured_utc = ('2026-09-20T00:00:00Z' if is_baseline
                    else '2026-09-20T01:00:00Z')
    source_prefix = f'{platform}-baseline' if is_baseline else platform
    bundle_values = ({'sdk_download_bytes': 100_000_000,
                      'sdk_uncompressed_bytes': 250_000_000}
                     if platform == 'PC'
                     else {'sdk_download_bytes': 5_000_000,
                           'sdk_uncompressed_bytes': 20_000_000})
    limits = workflow.PLATFORM_BUNDLE_LIMITS[platform]
    record = {
        'candidate': candidate,
        'candidate_revision': revision,
        'candidate_sha256': sha256,
        'platform': platform,
        'sdk_version': '3.8.2-test',
        'measured_utc': measured_utc,
        'frozen_at_phase': 'PREFLIGHT' if is_baseline else 'FINAL_VALIDATION',
        'rank': 'Good',
        'worst_metrics': ['triangles'],
        'metric_snapshot': snapshot,
        'boolean_snapshot': {
            'particle_trails_enabled': False,
            'particle_collision_enabled': False,
        },
        'report_kind': 'current_sdk_avatar_performance_report',
        'inactive_objects_included': True,
        'rank_is_static_analysis': True,
        'fps_claimed': False,
        'evidence': [{
            'candidate': candidate,
            'candidate_revision': revision,
            'candidate_sha256': sha256,
            'platform': platform,
            'sdk_version': '3.8.2-test',
            'layer': 'current_sdk_avatar_performance_report',
            'source': f'{source_prefix}-avatar-performance-report.json',
            'measured': True,
        }],
        'platform_size': {
            **bundle_values,
            'current_sdk_limits': copy.deepcopy(limits),
            'current_sdk_limit_source': 'current installed VRCSDK platform report',
            'sdk_download_bytes_over_limit': (
                bundle_values['sdk_download_bytes'] > limits['sdk_download_bytes']),
            'sdk_uncompressed_bytes_over_limit': (
                bundle_values['sdk_uncompressed_bytes']
                > limits['sdk_uncompressed_bytes']),
            'evidence': [{
                'candidate': candidate,
                'candidate_revision': revision,
                'candidate_sha256': sha256,
                'platform': platform,
                'sdk_version': '3.8.2-test',
                'layer': 'current_sdk_build_report',
                'source': f'{source_prefix}-sdk-build-report.json',
                'measured': True,
            }],
        },
    }
    if is_baseline:
        performance_sources = [record['evidence'][0]['source']]
        build_sources = [record['platform_size']['evidence'][0]['source']]
        receipt_sha = workflow.baseline_receipt_sha256(
            platform, candidate, revision, sha256, record['sdk_version'],
            measured_utc, performance_sources, build_sources)
        record['baseline_receipt'] = {
            'receipt_id': f'baseline-{receipt_sha}',
            'receipt_sha256': receipt_sha,
            'platform': platform,
            'candidate': candidate,
            'candidate_revision': revision,
            'candidate_sha256': sha256,
            'sdk_version': record['sdk_version'],
            'measured_utc': measured_utc,
            'frozen_at_phase': 'PREFLIGHT',
            'performance_report_sources': performance_sources,
            'build_report_sources': build_sources,
            'locked': True,
            'source': f'{platform}-baseline-lock.json',
        }
    return record


def complete_performance_final(record: dict) -> dict:
    record['allowed_action_results'] = [
        {
            'action': action,
            'status': 'CHECKED_NOT_APPLICABLE',
            'source': 'current generated-candidate inventory',
            'evidence': [f'{action}.json'],
        }
        for action in workflow.SAFE_PERFORMANCE_ACTIONS
    ]
    record['delta_attribution'] = {'entries': [], 'unattributed_deltas': []}
    return record


def rewrite_measurement_sdk(record: dict, sdk_version: str) -> None:
    record['sdk_version'] = sdk_version
    for item in record.get('evidence', []):
        item['sdk_version'] = sdk_version
    for item in record.get('platform_size', {}).get('evidence', []):
        item['sdk_version'] = sdk_version
    receipt = record.get('baseline_receipt')
    if isinstance(receipt, dict):
        performance_sources = [item['source'] for item in record['evidence']]
        build_sources = [
            item['source'] for item in record['platform_size']['evidence']]
        receipt_sha = workflow.baseline_receipt_sha256(
            record['platform'], record['candidate'],
            record['candidate_revision'], record['candidate_sha256'],
            sdk_version, record['measured_utc'],
            performance_sources, build_sources)
        receipt.update({
            'receipt_id': f'baseline-{receipt_sha}',
            'receipt_sha256': receipt_sha,
            'sdk_version': sdk_version,
        })


def performance_job(platform: str, candidate: str, revision: str,
                    sha256: str, sdk_version: str = '3.8.2-test') -> dict:
    return {
        'job_id': workflow.performance_job_id(
            platform, candidate, revision, sha256, sdk_version),
        'platform': platform,
        'candidate': candidate,
        'candidate_revision': revision,
        'candidate_sha256': sha256,
        'sdk_version': sdk_version,
        'status': 'SUCCEEDED',
        'dispatch_count': 1,
        'poll_count': 0,
        'timeout_action': 'poll_same_job',
        'completed_utc': '2026-09-20T00:00:00Z',
        'result_evidence': [
            {
                'kind': 'avatar_performance_report',
                'platform': platform,
                'candidate': candidate,
                'candidate_revision': revision,
                'candidate_sha256': sha256,
                'sdk_version': sdk_version,
                'layer': 'current_sdk_avatar_performance_report',
                'source': f'{platform}-avatar-performance-report.json',
            },
            {
                'kind': 'sdk_build_report',
                'platform': platform,
                'candidate': candidate,
                'candidate_revision': revision,
                'candidate_sha256': sha256,
                'sdk_version': sdk_version,
                'layer': 'current_sdk_build_report',
                'source': f'{platform}-sdk-build-report.json',
            },
        ],
    }


def performance_contract() -> dict:
    baseline = performance_measurement('PC', 'candidate-base', 'r1', BASELINE_SHA)
    final = complete_performance_final(
        performance_measurement('PC', 'candidate-a', 'r2', SHA))
    return {
        'policy_mode': 'measured_best_effort',
        'platforms': ['PC'],
        'target_rank': {'PC': 'Good'},
        'metric_caps': {},
        'rank_handling': {
            'PC': {'Poor': 'block', 'VeryPoor': 'block'},
        },
        'gate_completion': True,
        'gate_upload': True,
        'allowed_actions': list(workflow.SAFE_PERFORMANCE_ACTIONS),
        'protected_features': [
            'outfit_part_controls', 'blendshapes_and_chest_states',
            'material_animations', 'provider_writers', 'physbones',
            'contacts', 'particles', 'audio',
        ],
        'headroom_policy': {
            'gate_completion': True,
            'gate_upload': True,
            'platforms': {
                'PC': {
                    'expression_parameter_bits': {'limit': 256, 'reserve': 16},
                    'physbone_components': {'limit': 256, 'reserve': 16},
                },
            },
        },
        'baseline_measurements': {'PC': baseline},
        'final_measurements': {'PC': final},
        'evidence': ['policy frozen in Start Card question 3'],
    }


def menu_plan() -> dict:
    return {
        'visible_label_language': 'zh-CN',
        'translate_internal_parameters': False,
        'clothing_integration_strategy': 'provider_parts_menu_first',
        'allow_synthetic_universal_clothing_categories': False,
        'allow_parallel_outfit_selector_and_parts_trees': False,
        'default_combination': {'outfit': 'white', 'hair': 'original'},
        'control_budget': {'max_controls_per_page': 8},
        'provider_sources': [
            {'id': 'generated', 'source': 'task_generated_menu'},
            {'id': 'author', 'source': 'author_existing_menu'},
        ],
        'outfit_menu_integrations': [
            {
                'resource_id': 'outfit-white', 'provider_id': 'author',
                'provider_parts_menu_status': 'usable',
                'strategy': 'reuse_provider_parts_menu',
                'menu_entry_control_id': 'white-parts-entry',
                'added_control_ids': [],
                'evidence': ['author parts menu verified'],
            }
        ],
        'pages': [
            {
                'id': 'root',
                'controls': [
                    {
                        'id': 'white-parts-entry', 'label': '白色套装部件',
                        'label_language': 'zh-CN', 'type': 'submenu',
                        'submenu_page_id': 'wardrobe', 'provider_id': 'author',
                        'resource_id': 'outfit-white',
                        'semantic_role': 'outfit_parts_menu',
                    }
                ],
            },
            {
                'id': 'wardrobe',
                'controls': [
                    {
                        'id': 'white', 'label': '上衣',
                        'label_language': 'zh-CN', 'type': 'toggle',
                        'internal_parameter': 'Author_White', 'provider_id': 'author',
                        'resource_id': 'outfit-white',
                        'semantic_role': 'outfit_part_control',
                        'return_state_policy': 'preserve_user_choice',
                        'return_state_policy_source': 'provider',
                        'return_state_policy_evidence': ['author menu Saved semantics'],
                    }
                ],
            },
        ],
    }


def add_provider_parts_menu(plan: dict, resource_id: str, control_id: str,
                            label: str, parameter: str) -> None:
    page_id = f'{resource_id}-parts'
    entry_id = f'{resource_id}-parts-entry'
    plan['pages'][0]['controls'].append({
        'id': entry_id, 'label': f'{label}部件', 'label_language': 'zh-CN',
        'type': 'submenu', 'submenu_page_id': page_id, 'provider_id': 'author',
        'resource_id': resource_id, 'semantic_role': 'outfit_parts_menu',
    })
    plan['pages'].append({
        'id': page_id,
        'controls': [{
            'id': control_id, 'label': label, 'label_language': 'zh-CN',
            'type': 'toggle', 'internal_parameter': parameter,
            'provider_id': 'author', 'resource_id': resource_id,
            'semantic_role': 'outfit_part_control',
            'return_state_policy': 'preserve_user_choice',
            'return_state_policy_source': 'provider',
            'return_state_policy_evidence': ['author menu Saved semantics'],
        }],
    })
    plan['outfit_menu_integrations'].append({
        'resource_id': resource_id, 'provider_id': 'author',
        'provider_parts_menu_status': 'usable',
        'strategy': 'reuse_provider_parts_menu',
        'menu_entry_control_id': entry_id,
        'added_control_ids': [],
        'evidence': ['author parts menu verified'],
    })


def valid_task(phase: str = 'COMPLETE') -> dict:
    plan = menu_plan()
    menu_snapshot = workflow._menu_plan_snapshot(plan)
    evidence_context = {
        'candidate': 'candidate-a',
        'candidate_revision': 'r2',
        'candidate_sha256': SHA,
    }
    resource = {
        'id': 'outfit-white',
        'source': 'C:/source/outfit',
        'kind': 'outfit',
        'selected': True,
        'requirement': 'required',
        'status': ('ADMITTED' if phase == 'PREFLIGHT'
                   else 'INSTALLED_UNVERIFIED' if phase == 'INSTALLING'
                   else 'LOCAL_OK'),
        'affected_chest_states': ['small', 'default', 'large'],
        'expected_parts': [
            {'id': 'top', 'required_check_id': 'part-top'},
            {'id': 'skirt', 'required_check_id': 'part-skirt'},
            {'id': 'shoes', 'required_check_id': 'part-shoes'},
        ],
        'preflight': {
            'covers_chest': True,
            'breast_adjustment': {
                'status': 'SUPPORTED',
                'method': 'author_native',
                'supported_states': ['small', 'default', 'large'],
                'evidence': [
                    {
                        'source': 'author-prefab', 'version': '1.0',
                        'verified': True,
                    }
                ],
            },
            'performance_impact': {
                'source_inventory': {
                    'source': 'outfit-white-source-inventory.json',
                    'verified': True,
                },
                'estimate': {
                    'added_meshes': 3,
                    'added_material_slots': 5,
                    'added_triangles': 25_000,
                    'added_texture_memory_bytes': 12_000_000,
                    'dynamics': {
                        'physbone_components': 1,
                        'physbone_transforms': 8,
                        'physbone_colliders': 2,
                        'physbone_collision_checks': 6,
                        'contacts': 0,
                        'particles': 0,
                        'audio_sources': 0,
                    },
                },
                'dependencies': ['Modular Avatar'],
                'mitigation': ['selected black variant only', 'generated-copy texture caps'],
                'headroom_after_estimate': {
                    'source': 'cumulative performance budget worksheet',
                    'platforms': {
                        'PC': {
                            'expression_parameter_bits_remaining': 112,
                            'physbone_components_remaining': 251,
                            'meets_frozen_reserve': True,
                        },
                    },
                },
                'admission': {
                    'decision': 'ADMIT',
                    'reason': 'estimated headroom remains and required features are protected',
                    'evidence': 'preflight impact worksheet',
                },
            },
        },
        'required': [
            {
                'id': 'part-top', 'kind': 'visual',
                'affected_chest_states': ['small', 'default', 'large'],
            },
            {'id': 'part-skirt', 'kind': 'visual'},
            {'id': 'part-shoes', 'kind': 'visual'},
            {'id': 'pose-risk', 'kind': 'pose'},
            {'id': 'toggle-restore', 'kind': 'behavior'},
        ],
        'observations': [
            {
                **evidence_context,
                'id': 'outfit-white-chest-small-final',
                'resource_id': 'outfit-white', 'check_id': 'part-top',
                'kind': 'visual', 'status': 'LOCAL_OK',
                'layer': 'post_ndmf_processed_candidate',
                'chest_state': 'small', 'observed': True,
                'evidence': ['current-small.png'], 'note': 'small chest opened and observed',
            },
            {
                **evidence_context,
                'id': 'outfit-white-chest-default-final',
                'resource_id': 'outfit-white', 'check_id': 'part-top',
                'kind': 'visual', 'status': 'LOCAL_OK',
                'layer': 'post_ndmf_processed_candidate',
                'chest_state': 'default', 'observed': True,
                'evidence': ['current-default.png'], 'note': 'default chest opened and observed',
            },
            {
                **evidence_context,
                'id': 'outfit-white-chest-large-final',
                'resource_id': 'outfit-white', 'check_id': 'part-top',
                'kind': 'visual', 'status': 'LOCAL_OK',
                'layer': 'post_ndmf_processed_candidate',
                'chest_state': 'large', 'observed': True,
                'evidence': ['current-large.png'], 'note': 'large chest opened and observed',
            },
            {
                **evidence_context,
                'id': 'outfit-white-part-skirt-final', 'resource_id': 'outfit-white',
                'check_id': 'part-skirt', 'kind': 'visual', 'outcome': 'ok',
                'observed': True, 'evidence': 'current.png', 'note': 'skirt complete',
            },
            {
                **evidence_context,
                'id': 'outfit-white-part-shoes-final', 'resource_id': 'outfit-white',
                'check_id': 'part-shoes', 'kind': 'visual', 'outcome': 'ok',
                'observed': True, 'evidence': 'shoes.png', 'note': 'shoes complete',
            },
            {
                **evidence_context,
                'id': 'outfit-white-pose-risk-final', 'resource_id': 'outfit-white',
                'check_id': 'pose-risk', 'kind': 'pose', 'outcome': 'ok',
                'observed': True, 'evidence': 'pose.png', 'note': 'pose observed',
                'pose_applied': True, 'pose_evidence': 'pose-state.json',
            },
            {
                **evidence_context,
                'id': 'outfit-white-toggle-restore-final', 'resource_id': 'outfit-white',
                'check_id': 'toggle-restore', 'kind': 'behavior', 'outcome': 'ok',
                'observed': True, 'evidence': 'toggle.png', 'note': 'restore observed',
            },
        ],
    }
    return {
        'task_id': 'task-test',
        'task_revision': 1,
        'mode': 'assemble_upload',
        'phase': phase,
        'start_contract': {
            'confirmed': True,
            'visualization': {'mode': 'updatable_gallery'},
            'size_budget': {
                'metric': 'sdk_uncompressed_bytes',
                'hard_limit_bytes': 300_000_000,
                'working_limit_bytes': 270_000_000,
                'gate_completion': True,
                'gate_upload': True,
                'applies_to_platforms': ['PC'],
                'projected_bytes': {
                    'sdk_uncompressed_bytes': {'PC': 260_000_000},
                },
                'actual_bytes': {
                    'sdk_uncompressed_bytes': {'PC': 250_000_000},
                },
                'evidence': [
                    {
                        'metric': 'sdk_uncompressed_bytes', 'measured': True,
                        'platform': 'PC', 'sdk_version': '3.8.2-test',
                        'source': 'PC-sdk-build-report.json',
                        'layer': 'current_sdk_build_report',
                        'bytes': 250_000_000, **evidence_context,
                    }
                ],
            },
            'content_priority': {
                'required_resource_ids': ['outfit-white'],
                'optional_resource_ids': [],
                'optional_failure_action': 'isolate_and_continue',
                'extra_over_budget_action': 'remove_optional_in_frozen_order_or_pause',
                'requested_affected_chest_states': ['small', 'default', 'large'],
                'affected_chest_states': ['small', 'default', 'large'],
                'requested_affected_chest_states_override': {
                    'explicit': False, 'source': None,
                },
            },
            'base_optimization': safe_policy(),
            'performance_contract': performance_contract(),
            'menu_plan': plan,
            'assembly_manifest': {
                'target_avatar': {
                    'id': 'avatar', 'version': '1.0', 'source': 'C:/source/avatar',
                },
                'target_platform': ['PC'],
                'sdk_context': {
                    'current_vrcsdk_version': '3.8.2-test',
                    'authoritative': True,
                    'source': 'resolved Unity package manifest and current SDK panel',
                    'evidence': [{
                        'source': 'resolved-vrcsdk-version.json',
                        'observed': True,
                    }],
                },
                'resources': [
                    {
                        'id': 'outfit-white', 'kind': 'outfit', 'version': '1.0',
                        'source': 'C:/source/outfit', 'requirement': 'required',
                        'selected': True, 'main_outfit': True,
                    }
                ],
            },
            'batch_cadence': 'preflight_all_install_all_smoke_only_then_unified_acceptance',
            'delivery_layers': ['local_prefab', 'sdk_build', 'private_upload'],
            'upload_authority': {'authorized': True, 'private': True},
        },
        'candidate': 'candidate-a',
        'candidate_revision': 'r2',
        'candidate_sha256': SHA,
        'performance_measurement_jobs': {
            'PC': performance_job('PC', 'candidate-a', 'r2', SHA),
        },
        'scene_candidate': 'candidate-a',
        'local_decision_candidate': 'candidate-a',
        'local_decision': 'LOCAL_OK',
        'resources': [resource],
        'base_optimization': {
            'status': 'COMPLETE',
            'policy_applied': safe_policy(),
            'author_sources_unchanged': True,
            **evidence_context,
            'evidence': [
                {'source': 'generated-copy-inventory.json', 'verified': True}
            ],
        },
        'final_validation': {
            **evidence_context,
            'status': 'LOCAL_OK',
            'evidence': [
                {
                    **evidence_context,
                    'id': 'final-default', 'observed': True,
                    'source': 'final-default.png', 'note': 'opened current final view',
                }
            ],
        },
        'menu_validation': {
            **evidence_context,
            'layer': 'post_ndmf',
            'menu_artifact_sha256': TARGET_SHA,
            'status': 'LOCAL_OK',
            'checks': {
                'unexpected_root_injections': [],
                'redundant_parallel_outfit_parts_paths': [],
                'auto_pagination_detected': False,
                **menu_snapshot,
                'cycle_pages': [],
                'unreachable_pages': [],
                'empty_pages': [],
                'unbound_controls': [],
                'multiple_default_groups': [],
                'undeclared_parameters': [],
                'parameter_type_mismatches': [],
                'mixed_visible_label_languages': [],
                'observed_visible_label_languages': ['zh-CN'],
                'path_results': [
                    {'path': path, 'outcome': 'OK'}
                    for path in menu_snapshot['observed_paths']
                ],
                'control_results': [
                    {
                        'control_id': 'white', 'outcome': 'OK', 'provider_id': 'author',
                        'resource_id': 'outfit-white',
                        'semantic_role': 'outfit_part_control',
                        'scope': None,
                        'return_state_policy': 'preserve_user_choice',
                    }
                ],
                'path_walk_observed': True,
                'control_effects_observed': True,
                'default_state_observed': True,
                'restore_a_b_a_default_status': 'NOT_APPLICABLE',
                'restore_a_b_a_default_reason': 'Only one selectable main outfit',
                'provider_trace_complete': True,
                'outfit_part_return_state_results': [
                    {
                        'control_id': 'white', 'resource_id': 'outfit-white',
                        'provider_id': 'author',
                        'return_state_policy': 'preserve_user_choice',
                        'counterpart_outfit_id': 'original-outfit',
                        'sequence': [
                            'outfit-white', 'white', 'original-outfit',
                            'outfit-white', 'observe_expected', 'restore',
                        ],
                        'expected_return_state': 'user_choice_preserved',
                        'observed': True, 'outcome': 'OK',
                        'restore_observed': True,
                        'state_checks': {
                            'part_state_matches_policy': True,
                            'no_residual_parts': True,
                            'body_mask_correct': True,
                            'footwear_and_foot_shape_correct': True,
                        },
                    }
                ],
            },
            'evidence': [
                {
                    **evidence_context,
                    'layer': 'post_ndmf', 'menu_artifact_sha256': TARGET_SHA,
                    'check_id': 'restore_a_b_a_default', 'outcome': 'NOT_APPLICABLE',
                    'observed': True, 'source': 'post-ndmf-menu.json',
                    'note': 'walked paths and observed A-B-A-default restore',
                },
                {
                    **evidence_context,
                    'layer': 'post_ndmf', 'menu_artifact_sha256': TARGET_SHA,
                    'check_id': 'default_state', 'outcome': 'OK',
                    'observed': True, 'source': 'post-ndmf-menu.json',
                    'note': 'observed final default menu state',
                },
                {
                    **evidence_context,
                    'layer': 'post_ndmf', 'menu_artifact_sha256': TARGET_SHA,
                    'check_id': 'outfit_part_return_state:white', 'outcome': 'OK',
                    'control_id': 'white', 'resource_id': 'outfit-white',
                    'return_state_policy': 'preserve_user_choice',
                    'sequence': [
                        'outfit-white', 'white', 'original-outfit',
                        'outfit-white', 'observe_expected', 'restore',
                    ],
                    'observed': True, 'source': 'white-return-state.png',
                    'note': 'returned to white and observed frozen part-state policy',
                }
            ],
        },
        'upload_authorized': True,
        'private': True,
        'cancelled': False,
        'build_succeeded': True,
        'remote_pending': False,
        'upload_result': None,
    }


def add_optional_main_outfit(task: dict, resource_id: str = 'outfit-blue') -> dict:
    resource = copy.deepcopy(task['resources'][0])
    resource.update({
        'id': resource_id,
        'source': f'C:/source/{resource_id}',
        'requirement': 'optional',
        'main_outfit': True,
    })
    for observation in resource['observations']:
        if observation.get('check_id'):
            observation['resource_id'] = resource_id
            observation['id'] = observation['id'].replace('outfit-white', resource_id)
    task['resources'].append(resource)
    task['start_contract']['assembly_manifest']['resources'].append({
        'id': resource_id, 'kind': 'outfit', 'main_outfit': True,
        'version': '1.0', 'source': f'C:/source/{resource_id}',
        'requirement': 'optional', 'selected': True,
    })
    task['start_contract']['content_priority']['optional_resource_ids'].append(resource_id)
    return resource


def add_terminal_upload(task: dict) -> None:
    task['upload_authority'] = {
        'account_id': 'usr_test', 'display_name': '测试模型', 'release': 'private',
        'candidate': task['candidate'], 'candidate_revision': task['candidate_revision'],
        'candidate_sha256': task['candidate_sha256'],
    }
    task['upload_target'] = {
        'prefab': 'Assets/Generated/Upload.prefab', 'prefab_sha256': TARGET_SHA,
        'derived_from_candidate_sha256': task['candidate_sha256'],
        'blueprint_id': 'avtr_test',
    }
    task['upload_remote'] = {
        'status': 'PRIVATE_UPLOAD_CONFIRMED', 'id': 'avtr_test',
        'name': '测试模型', 'authorId': 'usr_test', 'release': 'private',
        'version': 1, 'pendingUpload': False, 'windowsAsset': True,
        'completedUtc': '2026-09-12T00:00:00Z',
    }
    task['upload_outcome'] = 'PRIVATE_UPLOAD_CONFIRMED'


def add_android_performance(task: dict, use_override_candidate: bool = True) -> tuple[str, str, str]:
    contract = task['start_contract']['performance_contract']
    contract['platforms'] = ['PC', 'Android']
    contract['target_rank']['Android'] = 'Good'
    contract['rank_handling']['Android'] = {
        'Poor': 'block', 'VeryPoor': 'block',
    }
    contract['headroom_policy']['platforms']['Android'] = {
        'expression_parameter_bits': {'limit': 256, 'reserve': 16},
        'physbone_components': {'limit': 8, 'reserve': 1},
    }
    task['start_contract']['assembly_manifest']['target_platform'] = ['PC', 'Android']
    context = (('android-final', 'r2', TARGET_SHA) if use_override_candidate
               else ('candidate-a', 'r2', SHA))
    android_candidate, android_revision, android_sha = context
    contract['baseline_measurements']['Android'] = performance_measurement(
        'Android', 'android-base', 'r1', 'd' * 64)
    contract['final_measurements']['Android'] = complete_performance_final(
        performance_measurement(
            'Android', android_candidate, android_revision, android_sha))
    task['platform_candidates'] = {
        'PC': {'candidate': 'candidate-a', 'candidate_revision': 'r2',
               'candidate_sha256': SHA},
        'Android': {'candidate': android_candidate,
                    'candidate_revision': android_revision,
                    'candidate_sha256': android_sha},
    }
    task['performance_measurement_jobs']['Android'] = performance_job(
        'Android', android_candidate, android_revision, android_sha)
    budget = task['start_contract']['size_budget']
    budget['applies_to_platforms'] = ['PC', 'Android']
    budget['projected_bytes']['sdk_uncompressed_bytes']['Android'] = 20_000_000
    budget['actual_bytes']['sdk_uncompressed_bytes']['Android'] = 20_000_000
    budget['evidence'].append({
        'metric': 'sdk_uncompressed_bytes', 'measured': True,
        'platform': 'Android', 'sdk_version': '3.8.2-test',
        'source': 'Android-sdk-build-report.json',
        'layer': 'current_sdk_build_report', 'bytes': 20_000_000,
        'candidate': android_candidate,
        'candidate_revision': android_revision,
        'candidate_sha256': android_sha,
    })
    task['resources'][0]['preflight']['performance_impact'][
        'headroom_after_estimate']['platforms']['Android'] = {
            'expression_parameter_bits_remaining': 112,
            'physbone_components_remaining': 3,
            'meets_frozen_reserve': True,
        }
    return context


def bind_android_override_semantics(
    task: dict, context: tuple[str, str, str],
) -> None:
    candidate, revision, sha256 = context
    final_record = copy.deepcopy(task['final_validation'])
    final_record.update({
        'candidate': candidate,
        'candidate_revision': revision,
        'candidate_sha256': sha256,
        'local_decision': 'LOCAL_OK',
    })
    for evidence in final_record['evidence']:
        evidence.update({
            'candidate': candidate,
            'candidate_revision': revision,
            'candidate_sha256': sha256,
            'source': f"Android-{evidence['source']}",
        })
    task['platform_final_validations'] = {'Android': final_record}

    menu_record = copy.deepcopy(task['menu_validation'])
    android_menu_sha = 'e' * 64
    menu_record.update({
        'candidate': candidate,
        'candidate_revision': revision,
        'candidate_sha256': sha256,
        'menu_artifact_sha256': android_menu_sha,
    })
    for evidence in menu_record['evidence']:
        evidence.update({
            'candidate': candidate,
            'candidate_revision': revision,
            'candidate_sha256': sha256,
            'menu_artifact_sha256': android_menu_sha,
            'source': f"Android-{evidence['source']}",
        })
    task['platform_menu_validations'] = {'Android': menu_record}

    for resource in task['resources']:
        additions = []
        for observation in resource['observations']:
            android_observation = copy.deepcopy(observation)
            android_observation.update({
                'id': f"{observation['id']}-android",
                'candidate': candidate,
                'candidate_revision': revision,
                'candidate_sha256': sha256,
            })
            additions.append(android_observation)
        resource['observations'].extend(additions)


def add_return_state_result(task: dict, resource_id: str, control_id: str,
                            counterpart: str = 'original-outfit') -> None:
    sequence = [resource_id, control_id, counterpart, resource_id,
                'observe_expected', 'restore']
    task['menu_validation']['checks']['outfit_part_return_state_results'].append({
        'control_id': control_id, 'resource_id': resource_id,
        'provider_id': 'author',
        'return_state_policy': 'preserve_user_choice',
        'counterpart_outfit_id': counterpart, 'sequence': sequence,
        'expected_return_state': 'user_choice_preserved',
        'observed': True, 'outcome': 'OK', 'restore_observed': True,
        'state_checks': {
            'part_state_matches_policy': True, 'no_residual_parts': True,
            'body_mask_correct': True,
            'footwear_and_foot_shape_correct': True,
        },
    })
    task['menu_validation']['evidence'].append({
        'candidate': task['candidate'],
        'candidate_revision': task['candidate_revision'],
        'candidate_sha256': task['candidate_sha256'],
        'layer': 'post_ndmf', 'menu_artifact_sha256': TARGET_SHA,
        'check_id': f'outfit_part_return_state:{control_id}', 'outcome': 'OK',
        'control_id': control_id, 'resource_id': resource_id,
        'return_state_policy': 'preserve_user_choice', 'sequence': sequence,
        'observed': True, 'source': f'{control_id}-return-state.png',
        'note': 'cross-outfit return state observed against frozen policy',
    })


class IntakeAndMenuTests(unittest.TestCase):
    def test_valid_intake_uses_menu_as_question_four(self) -> None:
        result = workflow.intake_decision(valid_task())
        self.assertEqual(result['status'], 'READY_FOR_UNITY_WRITE')
        self.assertEqual(result['question_count'], 5)

    def test_manifest_gap_is_discovery_not_routine_start_question(self) -> None:
        task = valid_task()
        task['start_contract']['assembly_manifest']['target_avatar']['version'] = ''
        result = workflow.intake_decision(task)
        self.assertEqual(result['status'], 'DISCOVERY_INCOMPLETE')
        self.assertFalse(result['routine_start_question'])

    def test_missing_current_sdk_anchor_is_discovery_incomplete(self) -> None:
        task = valid_task()
        task['start_contract']['assembly_manifest'].pop('sdk_context')
        result = workflow.intake_decision(task)
        self.assertEqual(result['status'], 'DISCOVERY_INCOMPLETE')
        self.assertIn('assembly_manifest.sdk_context', result['missing'])

    def test_empty_content_priority_cannot_leave_intake(self) -> None:
        task = valid_task()
        task['start_contract']['content_priority'] = {}
        result = workflow.intake_decision(task)
        self.assertEqual(result['status'], 'ASK_START_CARD')
        self.assertIn('content_priority', result['missing'])

    def test_content_priority_lists_must_be_disjoint_and_chest_states_nonempty(self) -> None:
        task = valid_task()
        priority = task['start_contract']['content_priority']
        priority['optional_resource_ids'] = ['outfit-white']
        priority['affected_chest_states'] = []
        errors = workflow.validate_content_priority(
            priority, task['start_contract']['assembly_manifest'])
        self.assertIn('content_priority.required_optional_must_be_disjoint', errors)
        self.assertIn('content_priority.affected_chest_states', errors)

    def test_requested_chest_scope_cannot_shrink_or_change_without_user_override(self) -> None:
        task = valid_task()
        priority = task['start_contract']['content_priority']
        priority['affected_chest_states'] = ['default', 'large']
        errors = workflow.validate_content_priority(
            priority, task['start_contract']['assembly_manifest'])
        self.assertIn(
            'content_priority.affected_chest_states_must_match_requested', errors)

        priority['requested_affected_chest_states'] = ['default', 'large']
        errors = workflow.validate_content_priority(
            priority, task['start_contract']['assembly_manifest'])
        self.assertIn(
            'content_priority.requested_affected_chest_states_explicit_user_override_required',
            errors,
        )
        priority['requested_affected_chest_states_override'] = {
            'explicit': True, 'source': 'user start-card reply',
        }
        self.assertEqual(workflow.validate_content_priority(
            priority, task['start_contract']['assembly_manifest']), [])

    def test_delivery_layers_are_closed_and_upload_authority_is_explicit(self) -> None:
        task = valid_task()
        task['start_contract']['delivery_layers'].append('mystery_layer')
        self.assertIn('delivery_layers', workflow.intake_decision(task)['missing'])
        task = valid_task()
        task['start_contract'].pop('upload_authority')
        result = workflow.intake_decision(task)
        self.assertIn('start_contract.upload_authority', result['missing'])

    def test_private_upload_layer_requires_matching_mode_authority_and_visibility(self) -> None:
        task = valid_task()
        task['mode'] = 'assemble'
        task['upload_authorized'] = False
        task['start_contract']['upload_authority']['authorized'] = False
        errors = workflow.validate_delivery_layers(task, task['start_contract'])
        self.assertIn('delivery_layers.private_upload_requires_assemble_upload', errors)
        self.assertIn('delivery_layers.private_upload_requires_authority', errors)

    def test_menu_rejects_translation_and_more_than_eight_controls(self) -> None:
        plan = menu_plan()
        plan['translate_internal_parameters'] = True
        plan['pages'][1]['controls'] = [
            {
                'id': f'control-{index}', 'label': f'控件{index}',
                'label_language': 'zh-CN', 'provider_id': 'generated',
            }
            for index in range(9)
        ]
        errors = workflow.validate_menu_plan(plan)
        self.assertIn('menu_plan.translate_internal_parameters_must_be_false', errors)
        self.assertIn('menu_plan.pages[1].controls_exceed_8', errors)

    def test_menu_plan_rejects_bad_types_missing_parameters_empty_unreachable_and_cycles(self) -> None:
        plan = menu_plan()
        plan['pages'][1]['controls'][0]['type'] = 'mystery'
        plan['pages'].append({'id': 'empty-orphan', 'controls': []})
        errors = workflow.validate_menu_plan(plan)
        self.assertIn('menu_plan.pages[1].controls[0].type', errors)
        self.assertIn('menu_plan.pages[2].empty', errors)
        self.assertIn('menu_plan.pages.unreachable.empty-orphan', errors)

        plan = menu_plan()
        plan['pages'][1]['controls'].append({
            'id': 'back', 'label': '返回', 'label_language': 'zh-CN',
            'type': 'submenu', 'submenu_page_id': 'root', 'provider_id': 'generated',
        })
        errors = workflow.validate_menu_plan(plan)
        self.assertTrue(any(error.startswith('menu_plan.pages.cycle.') for error in errors))

        plan = menu_plan()
        plan['pages'][1]['controls'][0].pop('internal_parameter')
        self.assertIn(
            'menu_plan.pages[1].controls[0].internal_parameter',
            workflow.validate_menu_plan(plan),
        )

    def test_default_combination_is_not_a_hard_menu_plan_field(self) -> None:
        plan = menu_plan()
        plan.pop('default_combination')
        self.assertEqual(workflow.validate_menu_plan(plan), [])

    def test_provider_parts_menu_is_valid_without_generated_wear_control_or_root_category(self) -> None:
        plan = menu_plan()
        self.assertNotIn('root_categories', plan)
        self.assertFalse(any(
            control.get('semantic_role') == 'outfit_activate'
            for page in plan['pages'] for control in page['controls']))
        self.assertEqual(workflow.validate_menu_plan(plan), [])

    def test_menu_rejects_parallel_outfit_selector_outside_provider_parts_menu(self) -> None:
        plan = menu_plan()
        plan['pages'][0]['controls'].append({
            'id': 'parallel-outfit-selector', 'label': '穿着本套',
            'label_language': 'zh-CN', 'type': 'toggle',
            'internal_parameter': 'Author_White', 'provider_id': 'author',
            'resource_id': 'outfit-white', 'semantic_role': 'outfit_activate',
        })
        self.assertIn(
            'menu_plan.redundant_parallel_outfit_parts_tree.outfit-white',
            workflow.validate_menu_plan(plan),
        )

    def test_usable_provider_parts_menu_cannot_claim_generated_behavior_controls(self) -> None:
        plan = menu_plan()
        plan['outfit_menu_integrations'][0]['added_control_ids'] = ['white']
        self.assertIn(
            'menu_plan.outfit_menu_integrations[0].reuse_must_not_add_controls',
            workflow.validate_menu_plan(plan),
        )

    def test_intake_closes_optional_selected_main_outfit_to_parts_menu_before_install(self) -> None:
        task = valid_task()
        optional = add_optional_main_outfit(task)
        optional['status'] = 'ADMITTED'
        result = workflow.intake_decision(task)
        self.assertEqual(result['status'], 'ASK_START_CARD')
        self.assertIn(
            'menu_plan.selected_main_outfit_parts_menu.outfit-blue', result['missing'])
        optional.update({'status': 'REJECTED', 'isolation_confirmed': True})
        self.assertEqual(
            workflow.intake_decision(task)['status'], 'READY_FOR_UNITY_WRITE')

    def test_non_submenu_control_needs_resource_binding_or_explicit_nonoutfit_role(self) -> None:
        plan = menu_plan()
        plan['pages'][0]['controls'].append({
            'id': 'eye-mode', 'label': '眼睛', 'label_language': 'zh-CN',
            'type': 'toggle', 'internal_parameter': 'EyeMode',
            'provider_id': 'generated',
        })
        errors = workflow.validate_menu_plan(plan)
        self.assertIn(
            'menu_plan.pages[0].controls[1].non_outfit_scope_required', errors)
        self.assertIn(
            'menu_plan.pages[0].controls[1].non_outfit_semantic_role', errors)
        plan['pages'][0]['controls'][1].update({
            'scope': 'non_outfit', 'semantic_role': 'non_outfit_eye',
        })
        self.assertEqual(workflow.validate_menu_plan(plan), [])

    def test_parallel_provider_parameter_chain_cannot_hide_as_nonoutfit(self) -> None:
        plan = menu_plan()
        plan['pages'][0]['controls'].append({
            'id': 'hidden-parallel-outfit', 'label': '功能',
            'label_language': 'zh-CN', 'type': 'toggle',
            'internal_parameter': 'Author_White', 'provider_id': 'author',
            'scope': 'non_outfit', 'semantic_role': 'non_outfit_function',
        })
        self.assertIn(
            'menu_plan.redundant_parallel_outfit_parts_tree.outfit-white',
            workflow.validate_menu_plan(plan),
        )

    def test_outfit_part_control_freezes_return_policy_source_and_evidence(self) -> None:
        fields = ('return_state_policy', 'return_state_policy_source',
                  'return_state_policy_evidence')
        for field in fields:
            with self.subTest(field=field):
                plan = menu_plan()
                plan['pages'][1]['controls'][0].pop(field)
                self.assertTrue(any(error.endswith(f'.{field}')
                                    for error in workflow.validate_menu_plan(plan)))


class ResourceAndEvidenceTests(unittest.TestCase):
    def test_evidence_key_changes_with_revision_and_hash(self) -> None:
        context = {
            'candidate': 'candidate-a', 'candidate_revision': 'r1',
            'candidate_sha256': SHA, 'state': 'default', 'view': 'front',
            'preview': 'processed-clone', 'checker': 'visual-v1',
        }
        first = workflow.evidence_key(context)
        context['candidate_revision'] = 'r2'
        context['candidate_sha256'] = TARGET_SHA
        self.assertNotEqual(first, workflow.evidence_key(context))

    def test_required_rejected_blocks_batch(self) -> None:
        task = valid_task()
        task['resources'][0]['status'] = 'REJECTED'
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'REQUIRED_RESOURCE_UNAVAILABLE')
        self.assertEqual(workflow.final_decision(task)['status'], 'REQUIRED_RESOURCE_UNAVAILABLE')

    def test_duplicate_observation_id_blocks_final_even_across_resources(self) -> None:
        task = valid_task()
        duplicate = copy.deepcopy(task['resources'][0])
        duplicate.update({
            'id': 'outfit-duplicate', 'source': 'C:/source/outfit-duplicate',
            'requirement': 'optional', 'main_outfit': False, 'kind': 'clothing',
        })
        for observation in duplicate['observations']:
            observation['resource_id'] = 'outfit-duplicate'
        task['resources'].append(duplicate)
        task['start_contract']['assembly_manifest']['resources'].append({
            'id': 'outfit-duplicate', 'kind': 'clothing', 'main_outfit': False,
            'version': '1.0', 'source': 'C:/source/outfit-duplicate',
            'requirement': 'optional', 'selected': True,
        })
        task['start_contract']['content_priority']['optional_resource_ids'].append(
            'outfit-duplicate')
        result = workflow.final_decision(task)
        self.assertEqual(result['status'], 'OBSERVATION_IDENTITY_INVALID')
        self.assertTrue(any(error.startswith('observations.duplicate_id.')
                            for error in result['errors']))

    def test_required_manifest_resource_cannot_be_silently_dropped(self) -> None:
        task = valid_task()
        task['resources'] = [{
            'id': 'other', 'selected': True, 'requirement': 'optional',
            'status': 'REJECTED', 'isolation_confirmed': True,
        }]
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'REQUIRED_RESOURCE_UNAVAILABLE')
        self.assertEqual(result['resources'], ['outfit-white'])

    def test_optional_rejected_can_continue_only_after_isolation(self) -> None:
        task = valid_task()
        optional = {
            'id': 'hair-alt', 'kind': 'soft_wearable',
            'selected': True, 'requirement': 'optional',
            'status': 'REJECTED', 'isolation_confirmed': True,
            'preflight': {
                'performance_impact': copy.deepcopy(
                    task['resources'][0]['preflight']['performance_impact'])
            },
        }
        task['resources'].append(optional)
        task['start_contract']['assembly_manifest']['resources'].append({
            'id': 'hair-alt', 'kind': 'soft_wearable', 'version': '1.0',
            'source': 'C:/source/hair', 'requirement': 'optional',
        })
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'READY_FOR_FINAL_VALIDATION')
        self.assertEqual(result['isolated_optional_count'], 1)
        optional['isolation_confirmed'] = False
        self.assertEqual(workflow.batch_decision(task)['status'], 'BATCH_NOT_READY')

    def test_manifest_optional_selected_false_still_needs_skipped_isolated_record(self) -> None:
        task = valid_task()
        task['start_contract']['assembly_manifest']['resources'].append({
            'id': 'optional-prop', 'kind': 'rigid_prop', 'version': '1.0',
            'source': 'C:/source/prop', 'requirement': 'optional', 'selected': False,
        })
        task['start_contract']['content_priority']['optional_resource_ids'].append('optional-prop')
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'BATCH_NOT_READY')
        task['resources'].append({
            'id': 'optional-prop', 'kind': 'rigid_prop', 'requirement': 'optional',
            'selected': False, 'status': 'SKIPPED', 'isolation_confirmed': True,
        })
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'READY_FOR_FINAL_VALIDATION')
        self.assertEqual(result['isolated_optional_count'], 1)

    def test_stale_observation_does_not_satisfy_current_candidate(self) -> None:
        resource = valid_task()['resources'][0]
        resource['observations'][0]['candidate_revision'] = 'r1'
        result = workflow.assess_resource(resource, 'candidate-a', 'r2', SHA)
        self.assertEqual(result['status'], 'NEEDS_OBSERVATION')
        self.assertEqual(result['stale'], ['part-top'])

    def test_chest_states_need_explicit_current_visual_observation_per_state(self) -> None:
        task = valid_task()
        resource = task['resources'][0]
        top_observations = [copy.deepcopy(observation)
                            for observation in resource['observations']
                            if observation.get('check_id') == 'part-top']
        unmarked = copy.deepcopy(top_observations[0])
        unmarked.pop('chest_state')
        resource['observations'] = [observation for observation in resource['observations']
                                    if observation.get('check_id') != 'part-top'] + [unmarked]
        assessed = workflow.assess_resource(resource, 'candidate-a', 'r2', SHA)
        self.assertEqual(assessed['status'], 'NEEDS_OBSERVATION')
        self.assertEqual(
            assessed['missing_states']['part-top'], ['small', 'default', 'large'])
        final = workflow.final_decision(task)
        self.assertEqual(final['status'], 'RESOURCE_VALIDATION_NOT_OK')
        self.assertEqual(
            final['resources']['outfit-white']['missing_states']['part-top'],
            ['small', 'default', 'large'],
        )

        resource['observations'] = [observation for observation in resource['observations']
                                    if observation.get('check_id') != 'part-top'] + top_observations
        top_observations[-1]['candidate_revision'] = 'r1'
        assessed = workflow.assess_resource(resource, 'candidate-a', 'r2', SHA)
        self.assertEqual(assessed['status'], 'NEEDS_OBSERVATION')
        self.assertEqual(assessed['stale_states']['part-top'], ['large'])

    def test_example_shaped_chest_observations_pass_strict_schema(self) -> None:
        resource = valid_task()['resources'][0]
        chest = [observation for observation in resource['observations']
                 if observation.get('check_id') == 'part-top']
        self.assertEqual(len(chest), 3)
        self.assertTrue(all(observation['id'] != observation['check_id']
                            and observation['resource_id'] == resource['id']
                            and observation['layer'] == 'post_ndmf_processed_candidate'
                            and observation['status'] == 'LOCAL_OK'
                            and isinstance(observation['evidence'], list)
                            and observation['evidence']
                            for observation in chest))
        self.assertEqual(
            workflow.assess_resource(resource, 'candidate-a', 'r2', SHA)['status'],
            'LOCAL_OK',
        )

    def test_chest_observation_missing_strict_fields_cannot_pass(self) -> None:
        required_fields = (
            'id', 'resource_id', 'check_id', 'chest_state', 'candidate',
            'candidate_revision', 'candidate_sha256', 'layer', 'status',
            'observed', 'evidence', 'note',
        )
        for field in required_fields:
            with self.subTest(field=field):
                resource = valid_task()['resources'][0]
                observation = next(
                    item for item in resource['observations']
                    if item.get('check_id') == 'part-top'
                    and item.get('chest_state') == 'small')
                observation.pop(field)
                assessed = workflow.assess_resource(resource, 'candidate-a', 'r2', SHA)
                self.assertEqual(assessed['status'], 'NEEDS_OBSERVATION')
                self.assertIn('small', assessed['missing_states']['part-top'])

    def test_final_reassesses_resource_instead_of_trusting_local_ok_label(self) -> None:
        task = valid_task()
        task['resources'][0]['observations'] = []
        result = workflow.final_decision(task)
        self.assertEqual(result['status'], 'RESOURCE_VALIDATION_NOT_OK')

    def test_final_blocks_post_ndmf_menu_defects(self) -> None:
        task = valid_task()
        task['menu_validation']['checks']['unexpected_root_injections'] = ['Injected']
        task['menu_validation']['checks']['page_control_counts']['wardrobe'] = 9
        result = workflow.final_decision(task)
        self.assertEqual(result['status'], 'MENU_VALIDATION_FAILED')
        errors = result['detail']['errors']
        self.assertIn('menu_validation.checks.unexpected_root_injections', errors)
        self.assertIn('menu_validation.checks.page_control_counts', errors)

    def test_outfit_part_return_state_missing_or_broken_blocks_menu(self) -> None:
        task = valid_task()
        task['menu_validation']['checks']['outfit_part_return_state_results'] = []
        result = workflow.menu_validation_decision(task)
        self.assertIn(
            'menu_validation.checks.OUTFIT_PART_RETURN_STATE_UNVERIFIED.white',
            result['errors'])

        task = valid_task()
        return_result = task['menu_validation']['checks'][
            'outfit_part_return_state_results'][0]
        return_result.update({
            'outcome': 'FAILED',
            'failure_code': 'OUTFIT_PART_RETURN_STATE_BROKEN',
        })
        result = workflow.menu_validation_decision(task)
        self.assertIn(
            'menu_validation.checks.OUTFIT_PART_RETURN_STATE_BROKEN.white',
            result['errors'])

    def test_restore_provider_default_is_valid_when_frozen_and_observed(self) -> None:
        task = valid_task()
        control = task['start_contract']['menu_plan']['pages'][1]['controls'][0]
        control['return_state_policy'] = 'restore_provider_default'
        control['return_state_policy_source'] = 'user'
        control['return_state_policy_evidence'] = ['user froze reset-on-return behavior']
        task['menu_validation']['checks'].update(
            workflow._menu_plan_snapshot(task['start_contract']['menu_plan']))
        task['menu_validation']['checks']['control_results'][0][
            'return_state_policy'] = 'restore_provider_default'
        result_item = task['menu_validation']['checks'][
            'outfit_part_return_state_results'][0]
        result_item['return_state_policy'] = 'restore_provider_default'
        result_item['expected_return_state'] = 'provider_default_restored'
        evidence = next(item for item in task['menu_validation']['evidence']
                        if item.get('check_id') == 'outfit_part_return_state:white')
        evidence['return_state_policy'] = 'restore_provider_default'
        self.assertEqual(
            workflow.menu_validation_decision(task)['status'], 'MENU_LOCAL_OK')

    def test_final_menu_requires_all_planned_pages_paths_controls_and_localized_labels(self) -> None:
        task = valid_task()
        checks = task['menu_validation']['checks']
        checks['page_control_counts'].pop('wardrobe')
        checks['observed_paths'].pop()
        checks['path_results'].pop()
        checks['control_results'][0]['provider_id'] = 'generated'
        checks['control_results'][0].pop('resource_id')
        checks['observed_controls']['wardrobe'][0]['label'] = 'White Outfit'
        checks['observed_controls']['wardrobe'][0].pop('semantic_role')
        result = workflow.menu_validation_decision(task)
        self.assertEqual(result['status'], 'MENU_VALIDATION_FAILED')
        errors = result['errors']
        self.assertIn('menu_validation.checks.page_control_counts', errors)
        self.assertIn('menu_validation.checks.observed_paths', errors)
        self.assertIn('menu_validation.checks.path_results', errors)
        self.assertIn('menu_validation.checks.control_results', errors)
        self.assertIn('menu_validation.checks.observed_controls', errors)

    def test_menu_evidence_requires_post_ndmf_layer_and_artifact_hash(self) -> None:
        task = valid_task()
        evidence = task['menu_validation']['evidence'][0]
        evidence.pop('layer')
        evidence['menu_artifact_sha256'] = SHA
        result = workflow.menu_validation_decision(task)
        self.assertIn(
            'menu_validation.evidence_current_post_ndmf_artifact', result['errors'])

    def test_single_main_outfit_requires_explicit_restore_not_applicable(self) -> None:
        task = valid_task()
        checks = task['menu_validation']['checks']
        checks.pop('restore_a_b_a_default_status')
        checks.pop('restore_a_b_a_default_reason')
        checks['restore_a_b_a_default_observed'] = True
        result = workflow.menu_validation_decision(task)
        self.assertIn(
            'menu_validation.checks.restore_a_b_a_default_not_applicable', result['errors'])

    def test_restore_sequence_cannot_replace_per_outfit_reachable_menu_control(self) -> None:
        task = valid_task()
        second = copy.deepcopy(task['resources'][0])
        second.update({'id': 'outfit-black', 'source': 'C:/source/outfit-black'})
        task['resources'].append(second)
        task['start_contract']['assembly_manifest']['resources'].append({
            'id': 'outfit-black', 'kind': 'outfit', 'version': '1.0',
            'source': 'C:/source/outfit-black', 'requirement': 'required',
        })
        task['start_contract']['content_priority']['required_resource_ids'].append('outfit-black')
        checks = task['menu_validation']['checks']
        checks.pop('restore_a_b_a_default_status')
        checks.pop('restore_a_b_a_default_reason')
        checks['restore_a_b_a_default_observed'] = True
        checks['restore_sequence'] = ['outfit-white', 'outfit-black', 'outfit-white', 'default']
        task['menu_validation']['evidence'][0].update({
            'outcome': 'OK', 'sequence': checks['restore_sequence'],
        })

        menu = workflow.menu_validation_decision(task)
        self.assertEqual(menu['status'], 'MENU_VALIDATION_FAILED')
        self.assertIn(
            'menu_validation.plan.selected_main_outfit_parts_menu.outfit-black',
            menu['errors'],
        )
        batch = workflow.batch_decision(task)
        self.assertEqual(batch['status'], 'MENU_PLAN_INCOMPLETE')
        self.assertIn(
            'menu_plan.selected_main_outfit_parts_menu.outfit-black', batch['errors'])

    def test_optional_installed_main_outfit_needs_menu_mapping_unless_isolated(self) -> None:
        task = valid_task()
        optional = add_optional_main_outfit(task)
        optional['status'] = 'INSTALLED_UNVERIFIED'

        batch = workflow.batch_decision(task)
        self.assertEqual(batch['status'], 'MENU_PLAN_INCOMPLETE')
        self.assertIn(
            'menu_plan.selected_main_outfit_parts_menu.outfit-blue', batch['errors'])
        menu = workflow.menu_validation_decision(task)
        self.assertIn(
            'menu_validation.plan.selected_main_outfit_parts_menu.outfit-blue',
            menu['errors'],
        )

        optional.update({'status': 'REJECTED', 'isolation_confirmed': True})
        self.assertEqual(
            workflow.batch_decision(task)['status'], 'READY_FOR_FINAL_VALIDATION')
        self.assertEqual(workflow.menu_validation_decision(task)['status'], 'MENU_LOCAL_OK')

    def test_optional_installed_main_outfit_complete_mapping_passes(self) -> None:
        task = valid_task()
        add_optional_main_outfit(task)
        plan = task['start_contract']['menu_plan']
        add_provider_parts_menu(plan, 'outfit-blue', 'blue', '蓝色套装', 'Author_Blue')
        checks = task['menu_validation']['checks']
        snapshot = workflow._menu_plan_snapshot(plan)
        checks.update(snapshot)
        checks['path_results'] = [
            {'path': path, 'outcome': 'OK'} for path in snapshot['observed_paths']
        ]
        checks['control_results'].append({
            'control_id': 'blue', 'outcome': 'OK', 'provider_id': 'author',
            'resource_id': 'outfit-blue', 'semantic_role': 'outfit_part_control',
            'scope': None, 'return_state_policy': 'preserve_user_choice',
        })
        add_return_state_result(task, 'outfit-blue', 'blue', 'outfit-white')
        checks.pop('restore_a_b_a_default_status')
        checks.pop('restore_a_b_a_default_reason')
        checks['restore_a_b_a_default_observed'] = True
        checks['restore_sequence'] = ['outfit-white', 'outfit-blue', 'outfit-white', 'default']
        task['menu_validation']['evidence'][0].update({
            'outcome': 'OK', 'sequence': checks['restore_sequence'],
        })

        self.assertEqual(
            workflow.batch_decision(task)['status'], 'READY_FOR_FINAL_VALIDATION')
        self.assertEqual(workflow.menu_validation_decision(task)['status'], 'MENU_LOCAL_OK')
        self.assertEqual(workflow.final_decision(task)['status'], 'LOCAL_OK')

    def test_two_main_outfits_require_observed_ok_a_b_a_default_sequence(self) -> None:
        task = valid_task()
        second = copy.deepcopy(task['resources'][0])
        second['id'] = 'outfit-black'
        second['source'] = 'C:/source/outfit-black'
        task['resources'].append(second)
        task['start_contract']['assembly_manifest']['resources'].append({
            'id': 'outfit-black', 'kind': 'outfit', 'version': '1.0',
            'source': 'C:/source/outfit-black', 'requirement': 'required',
        })
        task['start_contract']['content_priority']['required_resource_ids'].append('outfit-black')
        plan = task['start_contract']['menu_plan']
        add_provider_parts_menu(plan, 'outfit-black', 'black', '黑色套装', 'Author_Black')
        checks = task['menu_validation']['checks']
        snapshot = workflow._menu_plan_snapshot(plan)
        checks.update(snapshot)
        checks['path_results'] = [
            {'path': path, 'outcome': 'OK'} for path in snapshot['observed_paths']
        ]
        checks['control_results'].append({
            'control_id': 'black', 'outcome': 'OK', 'provider_id': 'author',
            'resource_id': 'outfit-black', 'semantic_role': 'outfit_part_control',
            'scope': None, 'return_state_policy': 'preserve_user_choice',
        })
        add_return_state_result(task, 'outfit-black', 'black', 'outfit-white')
        checks.pop('restore_a_b_a_default_status')
        checks.pop('restore_a_b_a_default_reason')
        checks['restore_a_b_a_default_observed'] = True
        checks['restore_sequence'] = ['outfit-white', 'outfit-black', 'outfit-white', 'default']
        task['menu_validation']['evidence'][0]['outcome'] = 'OK'
        task['menu_validation']['evidence'][0]['sequence'] = checks['restore_sequence']
        self.assertEqual(workflow.menu_validation_decision(task)['status'], 'MENU_LOCAL_OK')
        task['menu_validation']['evidence'][0]['outcome'] = 'failed'
        self.assertIn(
            'menu_validation.checks.restore_a_b_a_default_observed',
            workflow.menu_validation_decision(task)['errors'],
        )

    def test_final_blocks_stale_post_ndmf_menu_evidence(self) -> None:
        task = valid_task()
        task['menu_validation']['evidence'][0]['candidate_revision'] = 'r1'
        result = workflow.final_decision(task)
        self.assertEqual(result['status'], 'MENU_VALIDATION_FAILED')
        self.assertIn('menu_validation.evidence_current_post_ndmf_artifact', result['detail']['errors'])

    def test_final_accepts_fresh_per_resource_and_batch_evidence(self) -> None:
        self.assertEqual(workflow.final_decision(valid_task())['status'], 'LOCAL_OK')


class PreflightAndSizeTests(unittest.TestCase):
    def test_prefight_cannot_advance_to_installing_without_complete_mapping(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        task['resources'][0]['preflight']['breast_adjustment']['evidence'] = []
        result = workflow.optimization_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith('.breast_adjustment.evidence')
                            for error in result['errors']))

        task['phase'] = 'INSTALLING'
        with self.assertRaisesRegex(ValueError, 'complete preflight'):
            task_store.validate_workflow(task)

    def test_installing_cannot_claim_local_ok_before_unified_acceptance(self) -> None:
        task = valid_task(phase='INSTALLING')
        task['resources'][0]['status'] = 'LOCAL_OK'
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'RESOURCE_STATUS_PHASE_INVALID')
        with self.assertRaisesRegex(ValueError, 'LOCAL_OK is final-only'):
            task_store.validate_workflow(task)

    def test_resource_and_required_states_cannot_shrink_requested_chest_scope(self) -> None:
        task = valid_task()
        task['resources'][0]['affected_chest_states'] = ['default']
        result = workflow.batch_decision(task)
        self.assertTrue(any(error.endswith(
            '.affected_chest_states_must_match_requested') for error in result['errors']))

        task = valid_task()
        task['resources'][0]['required'][0]['affected_chest_states'] = ['default']
        result = workflow.batch_decision(task)
        self.assertTrue(any(error.endswith('.required_checks_chest_states')
                            for error in result['errors']))

    def test_clothing_cannot_enter_batch_without_expected_parts_and_chest_preflight(self) -> None:
        task = valid_task()
        task['resources'][0].pop('expected_parts')
        task['resources'][0].pop('preflight')
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith('.expected_parts') for error in result['errors']))
        self.assertTrue(any(error.endswith('.preflight') for error in result['errors']))

    def test_supported_breast_adjustment_needs_all_states_and_verified_source(self) -> None:
        task = valid_task()
        adjustment = task['resources'][0]['preflight']['breast_adjustment']
        adjustment['supported_states'] = ['default']
        adjustment['evidence'] = [{'source': 'author-prefab', 'verified': True}]
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith('.supported_states') for error in result['errors']))
        self.assertTrue(any(error.endswith('.evidence') for error in result['errors']))

    def test_clothing_expected_parts_and_risk_checks_must_close_to_required_checks(self) -> None:
        task = valid_task()
        resource = task['resources'][0]
        resource['expected_parts'][0]['required_check_id'] = 'missing-check'
        resource['required'] = [
            check for check in resource['required'] if check['kind'] != 'behavior'
        ]
        for check in resource['required']:
            check.pop('chest_states', None)
            check.pop('affected_chest_states', None)
        result = workflow.batch_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith('.expected_parts') for error in result['errors']))
        self.assertTrue(any('required_checks_missing_behavior' in error for error in result['errors']))
        self.assertTrue(any(error.endswith('.required_checks_chest_states') for error in result['errors']))

    def test_visible_expected_part_cannot_use_structure_only_check(self) -> None:
        task = valid_task()
        resource = task['resources'][0]
        resource['required'][2]['kind'] = 'structure'
        next(observation for observation in resource['observations']
             if observation.get('check_id') == 'part-shoes')['kind'] = 'structure'
        result = workflow.batch_decision(task)
        self.assertTrue(any(error.endswith('.expected_parts') for error in result['errors']))
        resource['expected_parts'][2]['verification'] = 'structure'
        resource['expected_parts'][2]['visible'] = False
        self.assertEqual(workflow.batch_decision(task)['status'], 'READY_FOR_FINAL_VALIDATION')

    def test_non_covering_clothing_requires_explicit_not_applicable(self) -> None:
        task = valid_task()
        preflight = task['resources'][0]['preflight']
        preflight['covers_chest'] = False
        self.assertEqual(workflow.batch_decision(task)['status'], 'PREFLIGHT_NOT_READY')
        preflight['breast_adjustment'] = {'status': 'NOT_APPLICABLE'}
        self.assertEqual(workflow.batch_decision(task)['status'], 'READY_FOR_FINAL_VALIDATION')

    def test_unsupported_optional_clothing_may_continue_only_as_rejected_isolated(self) -> None:
        task = valid_task()
        optional = {
            'id': 'optional-coat', 'kind': 'clothing', 'selected': True,
            'requirement': 'optional', 'status': 'REJECTED',
            'rejection_code': 'UNSUPPORTED_BREAST_ADJUSTMENT',
            'isolation_confirmed': True,
            'affected_chest_states': ['small', 'default', 'large'],
            'expected_parts': [
                {'id': 'coat', 'required_check_id': 'coat-visible'},
            ],
            'required': [
                {
                    'id': 'coat-visible', 'kind': 'visual',
                    'chest_states': ['small', 'default', 'large'],
                },
                {'id': 'coat-pose', 'kind': 'pose'},
                {'id': 'coat-restore', 'kind': 'behavior'},
            ],
            'preflight': {
                'covers_chest': True,
                'breast_adjustment': {'status': 'UNSUPPORTED'},
                'performance_impact': copy.deepcopy(
                    task['resources'][0]['preflight']['performance_impact']),
            },
        }
        task['resources'].append(optional)
        task['start_contract']['assembly_manifest']['resources'].append({
            'id': 'optional-coat', 'kind': 'clothing', 'version': '1.0',
            'source': 'C:/source/coat', 'requirement': 'optional', 'selected': True,
        })
        task['start_contract']['content_priority']['optional_resource_ids'].append('optional-coat')
        self.assertEqual(workflow.batch_decision(task)['status'], 'READY_FOR_FINAL_VALIDATION')
        optional['status'] = 'LOCAL_OK'
        self.assertEqual(workflow.batch_decision(task)['status'], 'PREFLIGHT_NOT_READY')

    def test_size_evidence_must_match_current_candidate_layer_and_byte_value(self) -> None:
        task = valid_task()
        self.assertEqual(workflow.size_decision(task)['status'], 'SIZE_OK')
        evidence = task['start_contract']['size_budget']['evidence'][0]
        evidence['candidate_revision'] = 'r1'
        self.assertEqual(workflow.size_decision(task)['status'], 'SIZE_RECEIPT_MISMATCH')
        evidence['candidate_revision'] = 'r2'
        evidence['bytes'] = 249_999_999
        self.assertEqual(workflow.size_decision(task)['status'], 'SIZE_RECEIPT_MISMATCH')
        evidence['bytes'] = 250_000_000
        evidence['layer'] = 'old_build_report'
        self.assertEqual(workflow.size_decision(task)['status'], 'SIZE_RECEIPT_MISMATCH')

    def test_sdk_size_receipt_must_equal_final_platform_build_report(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['platform_size']['sdk_uncompressed_bytes'] = 400_000_000
        final['platform_size']['sdk_uncompressed_bytes_over_limit'] = False
        self.assertEqual(
            workflow.size_decision(task)['status'], 'SIZE_RECEIPT_MISMATCH')
        self.assertEqual(
            workflow.final_decision(task)['status'], 'SIZE_RECEIPT_MISMATCH')

    def test_matching_sdk_size_receipt_passes(self) -> None:
        task = valid_task()
        result = workflow.size_decision(task)
        self.assertEqual(result['status'], 'SIZE_OK')
        self.assertEqual(
            result['actual_bytes']['sdk_uncompressed_bytes']['PC'],
            250_000_000,
        )

    def test_dual_platform_size_gate_requires_each_platform_receipt(self) -> None:
        task = valid_task()
        add_android_performance(task, use_override_candidate=False)
        task['start_contract']['size_budget']['actual_bytes'][
            'sdk_uncompressed_bytes'].pop('Android')
        self.assertEqual(workflow.size_decision(task)['status'], 'SIZE_UNVERIFIED')

    def test_dual_platform_size_receipts_cannot_reuse_one_build_source(self) -> None:
        task = valid_task()
        add_android_performance(task, use_override_candidate=False)
        shared_source = 'PC-sdk-build-report.json'
        next(item for item in task['start_contract']['size_budget']['evidence']
             if item.get('platform') == 'Android')['source'] = shared_source
        android_final = task['start_contract']['performance_contract'][
            'final_measurements']['Android']
        android_final['platform_size']['evidence'][0]['source'] = shared_source
        next(item for item in task['performance_measurement_jobs']['Android'][
            'result_evidence'] if item.get('kind') == 'sdk_build_report')[
                'source'] = shared_source
        result = workflow.size_decision(task)
        self.assertEqual(result['status'], 'SIZE_RECEIPT_MISMATCH')
        self.assertTrue(any('cross_platform_source_reuse' in error
                            for error in result['errors']))

    def test_deliverable_extracted_metric_does_not_require_sdk_platform_receipt(self) -> None:
        task = valid_task()
        budget = task['start_contract']['size_budget']
        budget['metric'] = 'deliverable_extracted_bytes'
        budget.pop('applies_to_platforms')
        budget['projected_bytes'] = {'deliverable_extracted_bytes': 200_000_000}
        budget['actual_bytes'] = {'deliverable_extracted_bytes': 200_000_000}
        budget['evidence'] = [{
            'metric': 'deliverable_extracted_bytes', 'measured': True,
            'source': 'clean-extraction-inventory.json',
            'layer': 'deliverable_extraction', 'bytes': 200_000_000,
            'candidate': task['candidate'],
            'candidate_revision': task['candidate_revision'],
            'candidate_sha256': task['candidate_sha256'],
        }]
        self.assertEqual(workflow.size_decision(task)['status'], 'SIZE_OK')

    def test_size_gate_booleans_are_required_and_explicit_false_is_honored(self) -> None:
        for gate, value in (('gate_completion', None), ('gate_upload', 'false')):
            with self.subTest(gate=gate):
                task = valid_task()
                if value is None:
                    task['start_contract']['size_budget'].pop(gate)
                else:
                    task['start_contract']['size_budget'][gate] = value
                result = workflow.intake_decision(task)
                self.assertEqual(result['status'], 'ASK_START_CARD')
                self.assertIn(f'size_budget.{gate}', result['missing'])

        task = valid_task()
        budget = task['start_contract']['size_budget']
        budget['gate_completion'] = False
        budget['gate_upload'] = True
        budget['actual_bytes']['sdk_uncompressed_bytes']['PC'] = 249_000_000
        self.assertEqual(workflow.final_decision(task)['status'], 'LOCAL_OK')
        add_terminal_upload(task)
        terminal = workflow.upload_terminal_decision(task)
        self.assertEqual(terminal['status'], 'UPLOAD_TERMINAL_INCONSISTENT')
        self.assertIn(
            'upload_size_gate.SIZE_RECEIPT_MISMATCH', terminal['errors'])
        budget['gate_upload'] = False
        self.assertEqual(
            workflow.upload_terminal_decision(task)['status'],
            'UPLOAD_CONFIRMED',
        )

    def test_performance_baseline_is_required_before_installation(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        task['start_contract']['performance_contract']['final_measurements'] = {}
        self.assertEqual(
            workflow.preflight_decision(task)['status'],
            'READY_FOR_INSTALLATION',
        )
        task['start_contract']['performance_contract']['baseline_measurements'] = {}
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertEqual(
            result['performance_status'], 'PERFORMANCE_BASELINE_UNVERIFIED')

    def test_dual_platform_preflight_needs_both_current_baselines(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        add_android_performance(task, use_override_candidate=False)
        task['start_contract']['performance_contract']['baseline_measurements'].pop(
            'Android')
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertEqual(
            result['performance_status'], 'PERFORMANCE_BASELINE_UNVERIFIED')

    def test_stale_baseline_hash_or_sdk_evidence_blocks_preflight(self) -> None:
        for field, value in (('candidate_sha256', TARGET_SHA),
                             ('sdk_version', '3.7.0-old')):
            with self.subTest(field=field):
                task = valid_task(phase='PREFLIGHT')
                baseline = task['start_contract']['performance_contract'][
                    'baseline_measurements']['PC']
                baseline['evidence'][0][field] = value
                result = workflow.preflight_decision(task)
                self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
                self.assertEqual(
                    result['performance_status'],
                    'PERFORMANCE_BASELINE_UNVERIFIED',
                )

    def test_dual_platform_baselines_cannot_reuse_report_sources(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        add_android_performance(task, use_override_candidate=False)
        baselines = task['start_contract']['performance_contract'][
            'baseline_measurements']
        baselines['Android']['evidence'][0]['source'] = (
            baselines['PC']['evidence'][0]['source'])
        baselines['Android']['platform_size']['evidence'][0]['source'] = (
            baselines['PC']['platform_size']['evidence'][0]['source'])
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertEqual(
            result['performance_status'], 'PERFORMANCE_BASELINE_UNVERIFIED')
        self.assertTrue(any('cross_platform_source_reuse' in error
                            for error in result['errors']))

    def test_every_selected_resource_needs_preflight_performance_impact(self) -> None:
        task = valid_task()
        task['resources'][0]['preflight'].pop('performance_impact')
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith('.performance_impact')
                            for error in result['errors']))

    def test_resource_cannot_be_admitted_when_estimate_exhausts_headroom(self) -> None:
        task = valid_task()
        impact = task['resources'][0]['preflight']['performance_impact']
        impact['headroom_after_estimate']['platforms']['PC'][
            'meets_frozen_reserve'] = False
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith(
            '.admission_exhausts_frozen_headroom') for error in result['errors']))

    def test_rejected_performance_impact_cannot_remain_admitted(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        impact = task['resources'][0]['preflight']['performance_impact']
        impact['admission'] = {
            'decision': 'REJECT',
            'reason': 'estimated dynamics exceed frozen reserve',
            'evidence': 'preflight impact worksheet',
        }
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith(
            '.admission_reject_status_mismatch') for error in result['errors']))

    def test_required_resource_rejected_by_performance_cannot_enter_installation(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        resource = task['resources'][0]
        resource['status'] = 'REJECTED'
        resource['isolation_confirmed'] = True
        resource['preflight']['performance_impact']['admission'] = {
            'decision': 'REJECT',
            'reason': 'required resource exceeds frozen performance contract',
            'evidence': 'preflight impact worksheet',
        }
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertTrue(any(error.endswith(
            '.admission_required_rejected') for error in result['errors']))

    def test_isolated_optional_performance_rejection_is_excluded_from_install_set(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        optional = add_optional_main_outfit(task, 'outfit-optional-heavy')
        add_provider_parts_menu(
            task['start_contract']['menu_plan'], 'outfit-optional-heavy',
            'optional-heavy-part', '可选部件', 'Author_OptionalHeavy',
        )
        optional['status'] = 'REJECTED'
        optional['isolation_confirmed'] = True
        optional['preflight']['performance_impact']['admission'] = {
            'decision': 'REJECT',
            'reason': 'optional resource exceeds frozen performance contract',
            'evidence': 'preflight impact worksheet',
        }
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'READY_FOR_INSTALLATION')
        self.assertEqual(result['install_resource_ids'], ['outfit-white'])
        self.assertEqual(
            result['isolated_optional_resource_ids'],
            ['outfit-optional-heavy'],
        )


class PerformanceContractTests(unittest.TestCase):
    def test_missing_or_empty_performance_contract_cannot_leave_intake(self) -> None:
        task = valid_task()
        task['start_contract'].pop('performance_contract')
        result = workflow.intake_decision(task)
        self.assertEqual(result['status'], 'ASK_START_CARD')
        self.assertIn('performance_contract', result['missing'])
        task['start_contract']['performance_contract'] = {}
        result = workflow.intake_decision(task)
        self.assertEqual(result['status'], 'ASK_START_CARD')
        self.assertIn('performance_contract', result['missing'])
        task = valid_task()
        task['start_contract']['performance_contract']['evidence'] = []
        result = workflow.intake_decision(task)
        self.assertIn('performance_contract.evidence', result['missing'])

    def test_best_effort_requires_a_reporting_target_for_every_platform(self) -> None:
        task = valid_task()
        add_android_performance(task, use_override_candidate=False)
        contract = task['start_contract']['performance_contract']
        contract['target_rank'].pop('Android')
        result = workflow.intake_decision(task)
        self.assertEqual(result['status'], 'ASK_START_CARD')
        self.assertIn(
            'performance_contract.reporting_target_required.Android',
            result['missing'],
        )

    def test_poor_and_verypoor_cannot_be_declared_target_ranks(self) -> None:
        for rank in ('Poor', 'VeryPoor'):
            with self.subTest(rank=rank):
                task = valid_task()
                task['start_contract']['performance_contract'][
                    'target_rank']['PC'] = rank
                result = workflow.intake_decision(task)
                self.assertEqual(result['status'], 'ASK_START_CARD')
                self.assertIn(
                    'performance_contract.target_rank', result['missing'])

    def test_official_headroom_limits_cannot_be_inflated_by_contract(self) -> None:
        for metric in ('expression_parameter_bits', 'physbone_components'):
            with self.subTest(metric=metric):
                task = valid_task()
                task['start_contract']['performance_contract'][
                    'headroom_policy']['platforms']['PC'][metric]['limit'] = 1000
                result = workflow.intake_decision(task)
                self.assertEqual(result['status'], 'ASK_START_CARD')
                self.assertIn(
                    'performance_contract.headroom_policy.platforms.'
                    f'PC.{metric}.limit',
                    result['missing'],
                )

    def test_best_effort_caps_do_not_mark_poor_or_verypoor_as_target_met(self) -> None:
        for rank in ('Poor', 'VeryPoor'):
            with self.subTest(rank=rank):
                task = valid_task()
                contract = task['start_contract']['performance_contract']
                contract['target_rank'] = {}
                contract['metric_caps'] = {'PC': {'triangles': 200_000}}
                contract['rank_handling']['PC'][rank] = 'accept_with_warning'
                contract['final_measurements']['PC']['rank'] = rank
                result = workflow.performance_decision(task)
                self.assertEqual(result['status'], 'PERFORMANCE_OK')
                self.assertFalse(result['target_met']['PC'])
                self.assertIn(
                    'recommended_target_not_met_do_not_claim_performance_goal_achieved',
                    result['warnings'],
                )

    def test_metric_caps_can_pass_without_claiming_poor_or_verypoor_target(self) -> None:
        for rank in ('Poor', 'VeryPoor'):
            with self.subTest(rank=rank):
                task = valid_task()
                contract = task['start_contract']['performance_contract']
                contract['policy_mode'] = 'metric_caps'
                contract['target_rank'] = {}
                contract['metric_caps'] = {'PC': {'triangles': 200_000}}
                contract['rank_handling']['PC'][rank] = 'accept_with_warning'
                contract['final_measurements']['PC']['rank'] = rank
                result = workflow.performance_decision(task)
                self.assertEqual(result['status'], 'PERFORMANCE_OK')
                self.assertTrue(result['caps_met']['PC'])
                self.assertFalse(result['target_met']['PC'])
                self.assertIn(
                    'recommended_target_not_met_do_not_claim_performance_goal_achieved',
                    result['warnings'],
                )

    def test_empty_final_measurement_cannot_pass_final(self) -> None:
        task = valid_task()
        task['start_contract']['performance_contract']['final_measurements'] = {}
        self.assertEqual(
            workflow.performance_decision(task)['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertNotEqual(workflow.final_decision(task)['status'], 'LOCAL_OK')

    def test_formal_measurement_job_is_required_for_current_platform(self) -> None:
        task = valid_task()
        task.pop('performance_measurement_jobs')
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertIn('performance_measurement_jobs', result['errors'])

    def test_formal_measurement_job_identity_fields_and_job_id_must_match(self) -> None:
        cases = {
            'platform': ('Android', 'performance_measurement_jobs.PC.platform'),
            'candidate': ('candidate-old', 'performance_measurement_jobs.PC.candidate'),
            'candidate_revision': ('r1', 'performance_measurement_jobs.PC.candidate_revision'),
            'candidate_sha256': (TARGET_SHA, 'performance_measurement_jobs.PC.candidate_sha256'),
            'sdk_version': ('3.7.0-old', 'performance_measurement_jobs.PC.sdk_version'),
            'job_id': ('perf-wrong', 'performance_measurement_jobs.PC.job_id'),
        }
        for field, (wrong_value, expected_error) in cases.items():
            with self.subTest(field=field):
                task = valid_task()
                task['performance_measurement_jobs']['PC'][field] = wrong_value
                result = workflow.performance_decision(task)
                self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
                self.assertIn(expected_error, result['errors'])

    def test_formal_measurement_job_cannot_be_redispatched(self) -> None:
        task = valid_task()
        task['performance_measurement_jobs']['PC']['dispatch_count'] = 6
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertIn(
            'performance_measurement_jobs.PC.dispatch_count_must_equal_1',
            result['errors'],
        )

    def test_formal_measurement_job_must_be_terminal_succeeded(self) -> None:
        task = valid_task()
        task['performance_measurement_jobs']['PC']['status'] = 'TIMED_OUT_WAITING'
        task['performance_measurement_jobs']['PC']['poll_count'] = 3
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertIn(
            'performance_measurement_jobs.PC.status_terminal_succeeded_required',
            result['errors'],
        )

    def test_exact_terminal_measurement_job_passes(self) -> None:
        task = valid_task()
        self.assertEqual(workflow.performance_decision(task)['status'], 'PERFORMANCE_OK')

    def test_self_consistent_old_sdk_baseline_fails_preflight(self) -> None:
        task = valid_task('PREFLIGHT')
        baseline = task['start_contract']['performance_contract'][
            'baseline_measurements']['PC']
        rewrite_measurement_sdk(baseline, '3.7.0-old')
        result = workflow.preflight_decision(task)
        self.assertEqual(result['status'], 'PREFLIGHT_NOT_READY')
        self.assertEqual(
            result['performance_status'], 'PERFORMANCE_BASELINE_UNVERIFIED')
        self.assertIn(
            'performance_measurements.PC.sdk_version_not_current',
            result['errors'],
        )

    def test_baseline_and_final_sdk_versions_must_match_current_anchor(self) -> None:
        task = valid_task()
        baseline = task['start_contract']['performance_contract'][
            'baseline_measurements']['PC']
        rewrite_measurement_sdk(baseline, '3.7.0-old')
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertIn(
            'performance_measurements.PC.sdk_version_not_current',
            result['errors'],
        )

    def test_all_old_self_consistent_sdk_records_cannot_pass_final(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        rewrite_measurement_sdk(contract['baseline_measurements']['PC'], '3.7.0-old')
        rewrite_measurement_sdk(contract['final_measurements']['PC'], '3.7.0-old')
        task['performance_measurement_jobs']['PC'] = performance_job(
            'PC', 'candidate-a', 'r2', SHA, '3.7.0-old')
        task['start_contract']['size_budget']['evidence'][0][
            'sdk_version'] = '3.7.0-old'
        self.assertEqual(
            workflow.performance_decision(task)['status'],
            'PERFORMANCE_UNVERIFIED',
        )
        self.assertEqual(
            workflow.size_decision(task)['status'],
            'SIZE_RECEIPT_MISMATCH',
        )
        self.assertNotEqual(workflow.final_decision(task)['status'], 'LOCAL_OK')

    def test_current_sdk_anchor_closes_measurement_job_and_size(self) -> None:
        task = valid_task()
        self.assertEqual(workflow.intake_decision(task)['status'],
                         'READY_FOR_UNITY_WRITE')
        self.assertEqual(workflow.performance_decision(task)['status'],
                         'PERFORMANCE_OK')
        self.assertEqual(workflow.size_decision(task)['status'], 'SIZE_OK')
        self.assertEqual(workflow.final_decision(task)['status'], 'LOCAL_OK')

    def test_baseline_and_final_report_sources_must_be_disjoint(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        final = contract['final_measurements']['PC']
        baseline = performance_measurement('PC', 'candidate-base', 'r1', BASELINE_SHA)
        baseline['evidence'][0]['source'] = final['evidence'][0]['source']
        baseline['platform_size']['evidence'][0]['source'] = (
            final['platform_size']['evidence'][0]['source'])
        performance_sources = [baseline['evidence'][0]['source']]
        build_sources = [baseline['platform_size']['evidence'][0]['source']]
        receipt_sha = workflow.baseline_receipt_sha256(
            'PC', baseline['candidate'], baseline['candidate_revision'],
            baseline['candidate_sha256'], baseline['sdk_version'],
            baseline['measured_utc'], performance_sources, build_sources)
        baseline['baseline_receipt'].update({
            'receipt_id': f'baseline-{receipt_sha}',
            'receipt_sha256': receipt_sha,
            'performance_report_sources': performance_sources,
            'build_report_sources': build_sources,
        })
        contract['baseline_measurements']['PC'] = baseline
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertIn(
            'performance_lifecycle.PC.performance_report_source_reuse',
            result['errors'],
        )
        self.assertIn(
            'performance_lifecycle.PC.build_report_source_reuse',
            result['errors'],
        )

    def test_copying_final_as_baseline_cannot_zero_out_delta(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        final = contract['final_measurements']['PC']
        baseline = copy.deepcopy(final)
        baseline.pop('allowed_action_results')
        baseline.pop('delta_attribution')
        baseline['measured_utc'] = '2026-09-20T00:00:00Z'
        baseline['frozen_at_phase'] = 'PREFLIGHT'
        performance_sources = [baseline['evidence'][0]['source']]
        build_sources = [baseline['platform_size']['evidence'][0]['source']]
        receipt_sha = workflow.baseline_receipt_sha256(
            'PC', baseline['candidate'], baseline['candidate_revision'],
            baseline['candidate_sha256'], baseline['sdk_version'],
            baseline['measured_utc'], performance_sources, build_sources)
        baseline['baseline_receipt'] = {
            'receipt_id': f'baseline-{receipt_sha}',
            'receipt_sha256': receipt_sha,
            'platform': 'PC',
            'candidate': baseline['candidate'],
            'candidate_revision': baseline['candidate_revision'],
            'candidate_sha256': baseline['candidate_sha256'],
            'sdk_version': baseline['sdk_version'],
            'measured_utc': baseline['measured_utc'],
            'frozen_at_phase': 'PREFLIGHT',
            'performance_report_sources': performance_sources,
            'build_report_sources': build_sources,
            'locked': True,
            'source': 'PC-baseline-lock.json',
        }
        contract['baseline_measurements']['PC'] = baseline
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertTrue(any(
            error.startswith('performance_lifecycle.PC.')
            for error in result['errors']))

    def test_old_candidate_performance_evidence_fails(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['evidence'][0]['candidate_revision'] = 'r1'
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertTrue(any(error.endswith('.current_evidence')
                            for error in result['errors']))

    def test_boolean_particle_flag_can_be_a_worst_metric(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['worst_metrics'] = ['particle_trails_enabled']
        self.assertEqual(workflow.performance_decision(task)['status'], 'PERFORMANCE_OK')

    def test_rank_gate_fails_when_current_rank_is_below_target(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        contract['policy_mode'] = 'rank_gate'
        contract['final_measurements']['PC']['rank'] = 'Medium'
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_RANK_NOT_MET')

    def test_metric_cap_gate_fails_on_current_snapshot(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        contract['policy_mode'] = 'metric_caps'
        contract['metric_caps'] = {'PC': {'triangles': 90_000}}
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_BUDGET_EXCEEDED')
        self.assertEqual(result['platforms']['PC']['triangles']['actual'], 100_000)

    def test_best_effort_measured_verypoor_passes_without_faking_target(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        contract['rank_handling']['PC']['VeryPoor'] = 'accept_with_warning'
        contract['final_measurements']['PC']['rank'] = 'VeryPoor'
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_OK')
        self.assertFalse(result['target_met']['PC'])
        self.assertIn(
            'recommended_target_not_met_do_not_claim_performance_goal_achieved',
            result['warnings'],
        )

    def test_mobile_hard_component_limit_fails_closed(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        contract['platforms'] = ['Android']
        contract['target_rank'] = {'Android': 'Good'}
        contract['rank_handling'] = {
            'Android': {'Poor': 'block', 'VeryPoor': 'block'},
        }
        contract['headroom_policy']['platforms'] = {
            'Android': {
                'expression_parameter_bits': {'limit': 256, 'reserve': 16},
                'physbone_components': {'limit': 8, 'reserve': 1},
            },
        }
        baseline = performance_measurement(
            'Android', 'android-base', 'r1', BASELINE_SHA,
            physbone_components=9)
        final = complete_performance_final(performance_measurement(
            'Android', 'android-final', 'r2', TARGET_SHA,
            physbone_components=9))
        contract['baseline_measurements'] = {'Android': baseline}
        contract['final_measurements'] = {'Android': final}
        task['start_contract']['assembly_manifest']['target_platform'] = ['Android']
        task['candidate'] = task['scene_candidate'] = task['local_decision_candidate'] = 'android-final'
        task['candidate_sha256'] = TARGET_SHA
        task['performance_measurement_jobs'] = {
            'Android': performance_job('Android', 'android-final', 'r2', TARGET_SHA),
        }
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'MOBILE_COMPONENT_LIMIT_EXCEEDED')
        self.assertEqual(result['metrics']['physbone_components']['limit'], 8)

    def test_sdk_hard_metrics_fail_even_when_headroom_gate_is_disabled(self) -> None:
        cases = {
            'expression_parameter_bits': 500,
            'physbone_components': 300,
            'physbone_colliders': 257,
            'contacts': 257,
            'raycasts': 81,
        }
        for metric, actual in cases.items():
            with self.subTest(metric=metric):
                task = valid_task()
                contract = task['start_contract']['performance_contract']
                contract['headroom_policy']['gate_completion'] = False
                baseline = contract['baseline_measurements']['PC'][
                    'metric_snapshot'][metric]
                final = contract['final_measurements']['PC']
                final['metric_snapshot'][metric] = actual
                final['delta_attribution']['entries'] = [{
                    'metric': metric,
                    'delta': actual - baseline,
                    'source_kind': 'resource',
                    'source_id': 'outfit-white',
                    'evidence': [f'{metric}-delta.json'],
                }]
                result = workflow.performance_decision(task)
                self.assertEqual(result['status'], 'HARD_COMPONENT_LIMIT_EXCEEDED')
                self.assertEqual(
                    result['platforms']['PC'][metric]['limit'],
                    workflow.SDK_HARD_METRIC_LIMITS[metric],
                )

    def test_unattributed_delta_blocks_completion(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['metric_snapshot']['triangles'] += 1
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_DELTA_UNATTRIBUTED')
        self.assertTrue(any(error.endswith('.unattributed_delta.triangles')
                            for error in result['errors']))

    def test_delta_attribution_sums_multiple_sources_and_covers_reductions(self) -> None:
        task = valid_task()
        add_optional_main_outfit(task, 'hair-alt')
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['metric_snapshot']['triangles'] += 30
        final['metric_snapshot']['texture_memory_bytes'] -= 10
        final['delta_attribution']['entries'] = [
            {'metric': 'triangles', 'delta': 10, 'source_kind': 'resource',
             'source_id': 'outfit-white', 'evidence': ['mesh-inventory.json']},
            {'metric': 'triangles', 'delta': 20, 'source_kind': 'resource',
             'source_id': 'hair-alt', 'evidence': ['mesh-inventory.json']},
            {'metric': 'texture_memory_bytes', 'delta': -10,
             'source_kind': 'optimization_action',
             'source_id': 'generated_copy_texture_limits',
             'evidence': ['texture-delta.json']},
        ]
        next(result for result in final['allowed_action_results']
             if result['action'] == 'generated_copy_texture_limits')[
                 'status'] = 'APPLIED'
        self.assertEqual(workflow.performance_decision(task)['status'], 'PERFORMANCE_OK')
        final['delta_attribution']['entries'][1]['delta'] = 19
        self.assertEqual(
            workflow.performance_decision(task)['status'],
            'PERFORMANCE_DELTA_UNATTRIBUTED',
        )

    def test_headroom_gate_rejects_exact_or_near_limit(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['metric_snapshot']['expression_parameter_bits'] = 253
        final['delta_attribution']['entries'] = [{
            'metric': 'expression_parameter_bits', 'delta': 125,
            'source_kind': 'resource', 'source_id': 'outfit-white',
            'evidence': ['parameter-delta.json'],
        }]
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERF_HEADROOM_EXHAUSTED')
        self.assertEqual(
            result['platforms']['PC']['expression_parameter_bits'][
                'maximum_with_reserve'], 240)

    def test_official_platform_bundle_limit_is_independent_hard_gate(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        bundle = final['platform_size']
        bundle['sdk_uncompressed_bytes'] = 500_000_001
        bundle['sdk_uncompressed_bytes_over_limit'] = True
        final['delta_attribution']['entries'] = [{
            'metric': 'sdk_uncompressed_bytes',
            'delta': 250_000_001,
            'source_kind': 'resource',
            'source_id': 'outfit-white',
            'evidence': ['PC-sdk-build-size-delta.json'],
        }]
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PLATFORM_SIZE_LIMIT_EXCEEDED')
        self.assertEqual(
            result['platforms']['PC']['sdk_uncompressed_bytes']['limit'],
            500_000_000,
        )

    def test_platform_size_deltas_require_full_positive_and_negative_attribution(self) -> None:
        task = valid_task()
        add_optional_main_outfit(task, 'hair-alt')
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        bundle = final['platform_size']
        bundle['sdk_download_bytes'] += 100
        bundle['sdk_uncompressed_bytes'] -= 100
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_DELTA_UNATTRIBUTED')
        self.assertTrue(any(error.endswith(
            '.unattributed_delta.sdk_download_bytes') for error in result['errors']))
        self.assertTrue(any(error.endswith(
            '.unattributed_delta.sdk_uncompressed_bytes') for error in result['errors']))
        final['delta_attribution']['entries'] = [
            {
                'metric': 'sdk_download_bytes', 'delta': 40,
                'source_kind': 'resource', 'source_id': 'outfit-white',
                'evidence': ['download-delta-outfit.json'],
            },
            {
                'metric': 'sdk_download_bytes', 'delta': 60,
                'source_kind': 'resource', 'source_id': 'hair-alt',
                'evidence': ['download-delta-hair.json'],
            },
            {
                'metric': 'sdk_uncompressed_bytes', 'delta': -100,
                'source_kind': 'optimization_action',
                'source_id': 'generated_copy_texture_limits',
                'evidence': ['uncompressed-delta-optimization.json'],
            },
        ]
        next(result for result in final['allowed_action_results']
             if result['action'] == 'generated_copy_texture_limits')[
                 'status'] = 'APPLIED'
        self.assertEqual(workflow.performance_decision(task)['status'], 'PERFORMANCE_OK')

    def test_build_and_test_cannot_substitute_for_current_sdk_build_report(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['platform_size']['evidence'][0]['layer'] = 'build_and_test'
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertTrue(any(error.endswith(
            '.platform_size.current_sdk_build_report_evidence')
            for error in result['errors']))

    def test_cross_platform_requires_independent_current_measurements(self) -> None:
        task = valid_task()
        contract = task['start_contract']['performance_contract']
        contract['platforms'] = ['PC', 'Android']
        contract['target_rank']['Android'] = 'Good'
        contract['rank_handling']['Android'] = {
            'Poor': 'block', 'VeryPoor': 'block',
        }
        contract['headroom_policy']['platforms']['Android'] = {
            'expression_parameter_bits': {'limit': 256, 'reserve': 16},
            'physbone_components': {'limit': 8, 'reserve': 1},
        }
        task['start_contract']['assembly_manifest']['target_platform'] = ['PC', 'Android']
        task['platform_candidates'] = {
            'PC': {'candidate': 'candidate-a', 'candidate_revision': 'r2',
                   'candidate_sha256': SHA},
            'Android': {'candidate': 'android-final', 'candidate_revision': 'r2',
                        'candidate_sha256': TARGET_SHA},
        }
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        contract['baseline_measurements']['Android'] = performance_measurement(
            'Android', 'android-base', 'r1', 'd' * 64)
        contract['final_measurements']['Android'] = complete_performance_final(
            performance_measurement('Android', 'android-final', 'r2', TARGET_SHA))
        task['performance_measurement_jobs']['Android'] = performance_job(
            'Android', 'android-final', 'r2', TARGET_SHA)
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_OK')

    def test_cross_platform_same_source_candidate_reuses_semantics_but_not_jobs(self) -> None:
        task = valid_task()
        add_android_performance(task, use_override_candidate=False)
        decision = workflow.performance_decision(task)
        self.assertEqual(decision['status'], 'PERFORMANCE_OK')
        self.assertNotEqual(
            task['performance_measurement_jobs']['PC']['job_id'],
            task['performance_measurement_jobs']['Android']['job_id'],
        )
        self.assertEqual(workflow.final_decision(task)['status'], 'LOCAL_OK')

    def test_cross_platform_cannot_reuse_pc_report_sources_for_android(self) -> None:
        task = valid_task()
        add_android_performance(task, use_override_candidate=False)
        contract = task['start_contract']['performance_contract']
        pc_final = contract['final_measurements']['PC']
        android_final = contract['final_measurements']['Android']
        android_final['evidence'] = copy.deepcopy(pc_final['evidence'])
        android_final['platform_size']['evidence'] = copy.deepcopy(
            pc_final['platform_size']['evidence'])
        task['performance_measurement_jobs']['Android']['result_evidence'] = (
            copy.deepcopy(
                task['performance_measurement_jobs']['PC']['result_evidence']))
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertTrue(any(
            'cross_platform_source_reuse' in error
            for error in result['errors']))
        self.assertTrue(any(
            error.endswith('.current_evidence')
            or error.endswith('.current_sdk_build_report_evidence')
            for error in result['errors']))

    def test_platform_override_requires_platform_bound_semantic_acceptance(self) -> None:
        task = valid_task()
        add_android_performance(task, use_override_candidate=True)
        result = workflow.final_decision(task)
        self.assertEqual(result['status'], 'PLATFORM_SEMANTIC_VALIDATION_FAILED')
        self.assertIn(
            'platform_final_validations.Android',
            result['detail']['errors'],
        )

    def test_platform_override_passes_with_platform_bound_semantic_acceptance(self) -> None:
        task = valid_task()
        android_context = add_android_performance(
            task, use_override_candidate=True)
        bind_android_override_semantics(task, android_context)
        result = workflow.final_decision(task)
        self.assertEqual(result['status'], 'LOCAL_OK')

    def test_delta_resource_source_must_be_selected_and_installed(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['metric_snapshot']['triangles'] += 1
        final['delta_attribution']['entries'] = [{
            'metric': 'triangles', 'delta': 1,
            'source_kind': 'resource', 'source_id': 'ghost-resource',
            'evidence': ['ghost-delta.json'],
        }]
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_DELTA_UNATTRIBUTED')
        self.assertTrue(any(error.endswith('.source_resource_not_eligible')
                            for error in result['errors']))

    def test_delta_optimization_action_must_be_authorized_and_applied(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['metric_snapshot']['texture_memory_bytes'] -= 1
        final['delta_attribution']['entries'] = [{
            'metric': 'texture_memory_bytes', 'delta': -1,
            'source_kind': 'optimization_action',
            'source_id': 'silently_delete_physbones',
            'evidence': ['unauthorized-action.json'],
        }]
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_DELTA_UNATTRIBUTED')
        self.assertTrue(any(error.endswith('.source_action_not_authorized')
                            for error in result['errors']))

    def test_phantom_delta_entry_for_unchanged_metric_is_rejected(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        final['delta_attribution']['entries'] = [{
            'metric': 'triangles', 'delta': 1,
            'source_kind': 'resource', 'source_id': 'outfit-white',
            'evidence': ['phantom-delta.json'],
        }]
        result = workflow.performance_decision(task)
        self.assertEqual(result['status'], 'PERFORMANCE_DELTA_UNATTRIBUTED')
        self.assertTrue(any(error.endswith('.phantom_or_unknown_metric')
                            for error in result['errors']))

    def test_semantic_dedupe_requires_invalidation_and_retest(self) -> None:
        task = valid_task()
        final = task['start_contract']['performance_contract']['final_measurements']['PC']
        result = next(item for item in final['allowed_action_results']
                      if item['action'] == 'semantic_safe_dedupe')
        result['status'] = 'APPLIED'
        decision = workflow.performance_decision(task)
        self.assertEqual(decision['status'], 'PERFORMANCE_UNVERIFIED')
        self.assertTrue(any(error.endswith('.semantic_safety')
                            for error in decision['errors']))


class OptimizationAndUploadTests(unittest.TestCase):
    def test_base_optimization_cannot_silently_lower_texture_limits(self) -> None:
        policy = safe_policy()
        policy['texture_max_size']['clothing_main_color'] = 512
        self.assertIn(
            'base_optimization.texture_max_size.clothing_main_color',
            workflow.validate_optimization_policy(policy),
        )

    def test_base_optimization_auto_applies_without_confirmation(self) -> None:
        task = valid_task(phase='PREFLIGHT')
        task.pop('base_optimization')
        result = workflow.optimization_decision(task)
        self.assertEqual(result['next'], 'APPLY_SAFE_OPTIMIZATION')
        self.assertFalse(result['requires_user_confirmation'])

    def test_selected_clothing_cannot_mark_base_optimization_not_required(self) -> None:
        task = valid_task(phase='INSTALLING')
        task['base_optimization'] = {
            'status': 'NOT_REQUIRED', 'reason': 'claimed no changes',
            'candidate': task['candidate'],
            'candidate_revision': task['candidate_revision'],
            'candidate_sha256': task['candidate_sha256'],
            'evidence': [{'source': 'claim.json', 'verified': True}],
        }
        result = workflow.optimization_decision(task)
        self.assertEqual(result['status'], 'BASE_OPTIMIZATION_COMPLETION_REQUIRED')
        self.assertEqual(result['next'], 'APPLY_SAFE_OPTIMIZATION')

    def test_completed_base_optimization_auto_continues_to_final_validation(self) -> None:
        task = valid_task(phase='INSTALLING')
        result = workflow.optimization_decision(task)
        self.assertEqual(result['status'], 'SAFE_OPTIMIZATION_COMPLETE')
        self.assertEqual(result['next'], 'FINAL_VALIDATION')
        self.assertFalse(result['requires_user_confirmation'])

    def test_over_working_projection_without_actual_cannot_advance_to_final(self) -> None:
        task = valid_task(phase='INSTALLING')
        budget = task['start_contract']['size_budget']
        budget['projected_bytes']['sdk_uncompressed_bytes'] = 280_000_000
        budget['actual_bytes'] = {}
        budget['evidence'] = []
        result = workflow.optimization_decision(task)
        self.assertEqual(result['status'], 'EXTRA_BUDGET_ACTION_OR_MEASUREMENT_REQUIRED')
        self.assertEqual(result['next'], 'APPLY_AUTHORIZED_EXTRA_BUDGET_ACTION_OR_MEASURE')
        self.assertEqual(
            result['frozen_action'], 'remove_optional_in_frozen_order_or_pause')

    def test_unapproved_extra_lossy_optimization_is_not_hidden_in_base_policy(self) -> None:
        task = valid_task(phase='INSTALLING')
        task['extra_optimization'] = {
            'status': 'COMPLETE', 'user_authorized': False,
            'authorization_source': None,
            'policy': {'changes': ['lower clothing to 512']},
            'candidate': task['candidate'],
            'candidate_revision': task['candidate_revision'],
            'candidate_sha256': task['candidate_sha256'],
            'evidence': [{'source': 'extra.json', 'verified': True}],
        }
        result = workflow.optimization_decision(task)
        self.assertEqual(result['status'], 'EXTRA_OPTIMIZATION_NOT_AUTHORIZED_OR_INVALID')
        self.assertIn('extra_optimization.user_authority', result['errors'])

    def test_task_store_cannot_enter_final_validation_before_base_optimization(self) -> None:
        task = valid_task(phase='FINAL_VALIDATION')
        task.pop('base_optimization')
        with self.assertRaisesRegex(ValueError, 'Base generated-copy optimization'):
            task_store.validate_workflow(task)

    def test_claimed_upload_with_false_authority_is_rejected(self) -> None:
        task = valid_task()
        add_terminal_upload(task)
        task['upload_authorized'] = False
        result = workflow.upload_decision(task)
        self.assertEqual(result['next'], 'UPLOAD_TERMINAL_INCONSISTENT')
        self.assertIn('upload_authorized', result['detail']['errors'])

    def test_valid_terminal_upload_is_complete(self) -> None:
        task = valid_task()
        add_terminal_upload(task)
        result = workflow.upload_decision(task)
        self.assertEqual(result['next'], 'UPLOAD_COMPLETE')
        self.assertEqual(result['terminal']['avatar_id'], 'avtr_test')
        task_store.validate_workflow(task)


class PackageConsistencyTests(unittest.TestCase):
    def test_rc1_defaults_and_example_match_the_live_validators(self) -> None:
        import json

        defaults = json.loads((SKILL_ROOT / 'defaults.json').read_text(encoding='utf-8-sig'))
        example = json.loads(
            (SKILL_ROOT / 'templates' / 'AssemblyTask.example.json').read_text(encoding='utf-8-sig'))
        card = example['start_contract']
        self.assertEqual(defaults['version'], '0.3.0-rc1')
        self.assertTrue(card['questions_version'].startswith('rc1-'))
        self.assertEqual(defaults['start_contract']['default_size_metric'], 'sdk_uncompressed_bytes')
        self.assertEqual(workflow.validate_optimization_policy(card['base_optimization']), [])
        self.assertEqual(workflow.validate_menu_plan(card['menu_plan']), [])
        self.assertEqual(workflow.validate_assembly_manifest(card['assembly_manifest']), [])
        self.assertEqual(workflow.validate_performance_contract(
            card['performance_contract'], card['assembly_manifest']), [])
        self.assertEqual(
            workflow.validate_content_priority(card['content_priority'], card['assembly_manifest']), [])
        self.assertEqual(workflow.validate_delivery_layers(example, card), [])
        manifest_ids = {item['id'] for item in card['assembly_manifest']['resources']}
        runtime_ids = {item['id'] for item in example['resources']}
        self.assertEqual(runtime_ids, manifest_ids)


if __name__ == '__main__':
    unittest.main()
