#!/usr/bin/env python3
"""On-demand MCP bridge for existing Workbench files. Python 3.10+, stdlib only."""
import argparse
import base64
from collections import Counter
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import time
import threading
sys.path.insert(0, str(Path(__file__).resolve().parent))
from workflow_context import WorkflowContext
from inbox_notifications import InboxNotifications


FINISH_NOT_RUN = "未运行收尾检查"


def finish_gate(space, entry, status, note):
    """Replying "addressed" needs a Unity hand-in check newer than the request; its result travels with the reply."""
    if status != "addressed":
        return {}

    def when(value):
        return datetime.fromisoformat(str(value).replace("Z", "+00:00"))

    check = None
    receipt = Path(space) / "finish-check.json"
    if receipt.is_file():
        try:
            check = json.loads(receipt.read_text(encoding="utf-8-sig"))
            if entry.get("created_at") and when(check["checked_at"]) < when(entry["created_at"]):
                check = None
        except (ValueError, KeyError, TypeError):
            check = None
    if check is None:
        if FINISH_NOT_RUN in (note or ""):
            return {"finish_check": {"status": "NOT_RUN", "reason": note}}
        raise ValueError("FINISH_CHECK_REQUIRED: run AvatarWorkbench.WorkbenchFinishCheck.Run in Unity before replying "
                         "(template: Assets/AvatarWorkbench/Tools/finish-check.md in the project, same as the Skill's references/finish-check.md). If Unity cannot be reached, write "
                         "'" + FINISH_NOT_RUN + "：<原因>' in the note.")
    return {"finish_check": {"status": "PASS" if check.get("passed") else "FAIL", "checked_at": check.get("checked_at"),
                             "candidate_prefab": check.get("candidate_prefab"),
                             "errors": check.get("errors", []), "warnings": check.get("warnings", [])}}

WORKFLOW_VERSION = "0.3.2-local5-dsh"

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('feedback', ROOT / 'unity/AvatarWorkbench/Tools/codex_feedback.py')
feedback = importlib.util.module_from_spec(spec)
spec.loader.exec_module(feedback)

def load(path, limit=32 * 1024 * 1024):
    path = Path(path)
    if path.stat().st_size > limit:
        raise ValueError('FILE_TOO_LARGE')
    return json.loads(path.read_text(encoding='utf-8-sig'))

def text_result(value):
    return {'content': [{'type': 'text', 'text': json.dumps(value, ensure_ascii=False, indent=2)}]}

def schema(properties=None, required=None):
    return {'type': 'object', 'properties': properties or {}, 'required': required or [], 'additionalProperties': False}

STRING = {'type': 'string'}
TOOLS = [
    ('context_select', 'Select the explicitly requested Unity project and its saved Workbench binding for THIS MCP process only. Optional explicit binding/storage root. Never rewrites profiles, tasks or feedback; re-read after switching.', schema({'project': STRING, 'binding': STRING, 'storage_root': STRING}, ['project'])),
    ('feedback_image', 'Return the original frozen PNG/JPEG pixels of already-read feedback, verifying its recorded SHA-256 and unchanged receipt. Does not acknowledge or approve the image.', schema({'id': STRING}, ['id'])),
    ('workbench_status', 'Read exact local binding, workflow paths and availability. Does not discover Unity or read secrets.', schema()),
    ('catalog_search', 'Search the EXISTING saved index only. Category/base filters, counts, paging; never scan source directories. Records from different sources are not deduplicated.', schema({'query': STRING, 'category': STRING, 'base': STRING, 'source_id': STRING, 'offset': {'type': 'integer', 'minimum': 0}, 'limit': {'type': 'integer', 'minimum': 1, 'maximum': 60}})),
    ('catalog_cover', 'Read an existing indexed local cover by item id. Returns pixels only if the file is an image. Explicit request only; this sends image content to the calling model.', schema({'source_id': STRING, 'item_id': STRING}, ['source_id', 'item_id'])),
    ('task_read', 'Read current QuickTask or legacy task without converting or changing it.', schema()),
    ('feedback_read', 'Read original frozen feedback once on task start/resume/resource completion. Reading does NOT acknowledge. Preserve its target/candidate/image identity.', schema({'id': STRING, 'include_addressed': {'type': 'boolean'}})),
    ('feedback_ack', 'Acknowledge only feedback read through this process; requires unchanged content and a real DSH session id. addressed means replied, NOT avatar accepted.', schema({'id': STRING, 'session': STRING, 'status': {'type': 'string', 'enum': ['seen', 'processing_feedback', 'addressed']}, 'note': STRING}, ['id', 'session', 'status', 'note'])),
    ('consumer_lease', 'Connect/renew/busy/release the existing consumer lease. Reject another active consumer. Use busy for persistent Avatar writes. BEFORE temporary preview/menu controls set YOUR lease idle: active ownership remains, while busy would block controls and pause animation. Keep idle during settling/capture, disconnect at completion. No automatic polling or wakeup.', schema({'session': STRING, 'action': {'type': 'string', 'enum': ['connect', 'busy', 'idle', 'disconnect']}, 'seconds': {'type': 'integer', 'minimum': 10, 'maximum': 3600}}, ['session', 'action'])),
]

class Bridge:
    def __init__(self, config):
        self.state_lock = threading.RLock()
        self.config = load(config)
        self.context = WorkflowContext(self.config)
        self.activate_context()

    def activate_context(self):
        self.binding = self.context.binding
        self.project = self.context.project
        self.space = feedback.location(self.binding, self.context.storage)
        self.index = self.context.index
        self.read_hashes = {}
        self.notification_baseline = {p.stem for p in (self.space / 'feedback').glob('*.json')}

    def notification_snapshot(self):
        with self.state_lock:
            return {'context_id': self.context.context_id, 'project': str(self.project),
                    'binding': str(self.binding), 'inbox': self.space / 'feedback',
                    'baseline': frozenset(self.notification_baseline),
                    'routing_ready': self.context.status()['routing_ready']}

    def feedback_path(self, identifier):
        if not re.fullmatch(r'[a-f0-9]{32}', identifier):
            raise ValueError('INVALID_FEEDBACK_ID')
        return self.space / 'feedback' / (identifier + '.json')

    def records(self):
        if not self.index or not self.index.is_file():
            return []
        data = load(self.index)
        return [(d, i) for d in data.get('directories', []) for i in d.get('items', []) if isinstance(i, dict)]

    def guard_consumer(self, session):
        if not session.strip() or len(session) > 200:
            raise ValueError('ACTUAL_DSH_SESSION_REQUIRED')
        path = self.space / 'consumer.json'
        current = feedback.read(path) if path.exists() else {}
        if current.get('active') and current.get('session') != session:
            expiry = datetime.fromisoformat(current.get('expires_at', '').replace('Z', '+00:00'))
            if expiry > datetime.now(timezone.utc):
                raise ValueError('ANOTHER_CONSUMER_ACTIVE: ' + str(current.get('session')))
        return current

    def call(self, name, args):
        with self.state_lock:
            return self.call_locked(name, args)

    def call_locked(self, name, args):
        if name == 'context_select':
            next_context = WorkflowContext.select(self.config, args['project'], args.get('binding'), args.get('storage_root'))
            lease_file = self.space / 'consumer.json'
            if lease_file.is_file():
                lease = feedback.read(lease_file)
                if lease.get('active') and lease.get('busy'):
                    expiry = datetime.fromisoformat(lease.get('expires_at', '').replace('Z', '+00:00'))
                    if expiry > datetime.now(timezone.utc):
                        raise ValueError('RELEASE_CURRENT_BUSY_LEASE_BEFORE_CONTEXT_SWITCH')
            self.context = next_context
            self.activate_context()
            return text_result(dict(self.context.status(), read_cache_cleared=True))
        if name == 'workbench_status':
            return text_result(dict(self.context.status(),
                feedback_directory=str(self.space / 'feedback'),
                workflow=str(ROOT / 'skills/assemble-vrchat-avatar/SKILL.md'), workflow_version=WORKFLOW_VERSION,
                quickflow=str(ROOT / 'skills/assemble-vrchat-avatar/scripts/quickflow.py'),
                optimization=str(ROOT / 'skills/assemble-vrchat-avatar/scripts/optimization.py'),
                ma_probe=str(ROOT / 'skills/assemble-vrchat-avatar/scripts/emit_ma_probe.py'),
                unity_state='Call mcp__unityMCP__list_unity_instances and match this exact project. Not checked here.',
                inbox_notifications='advisory_only', automatic_wakeup=False, upload_authorized=False, build_and_test_authorized=False))
        if name == 'task_read':
            return text_result({'binding': str(self.binding), 'task': load(self.binding, 4 * 1024 * 1024) if self.binding.is_file() else None})
        if name == 'catalog_search':
            rows = self.records()
            source = args.get('source_id', '')
            scoped = [(d, i) for d, i in rows if not source or d.get('id') == source]
            cats = Counter(str(i.get('category') or i.get('kind') or '其他') for _, i in scoped)
            bases = Counter(b for _, i in scoped for b in i.get('bases', []) if isinstance(b, str))
            out = []
            fields = ['id', 'name', 'product_name', 'category', 'kind', 'bases', 'tags', 'bytes', 'state', 'full_path', 'path', 'thumbnail', 'covers', 'booth_id', 'booth_url', 'shop', 'metadata_source', 'category_source', 'origin_source', 'compatibility_status', 'member_paths']
            for d, i in scoped:
                if args.get('category') and args['category'] != str(i.get('category') or i.get('kind') or '其他'):
                    continue
                if args.get('base') and args['base'].casefold() not in [str(b).casefold() for b in i.get('bases', [])]:
                    continue
                query = args.get('query', '').strip().casefold()
                hay = json.dumps({k: i.get(k) for k in ['name', 'product_name', 'tags', 'bases', 'shop', 'full_path', 'booth_id']}, ensure_ascii=False).casefold()
                if query and not all(word in hay for word in query.split()):
                    continue
                out.append(dict({k: i[k] for k in fields if k in i}, source_id=d.get('id'), indexed_at=d.get('read_at')))
            offset = max(0, int(args.get('offset', 0)))
            limit = max(1, min(60, int(args.get('limit', 20))))
            return text_result({'index_exists': bool(self.index and self.index.is_file()), 'scanned_now': False, 'source_counts': dict(Counter(d.get('id', '') for d, _ in rows)), 'scope_categories': dict(cats), 'scope_bases': dict(bases), 'total_matches': len(out), 'offset': offset, 'items': out[offset:offset+limit]})
        if name == 'catalog_cover':
            item = next((i for d, i in self.records() if d.get('id') == args['source_id'] and i.get('id') == args['item_id']), None)
            if not item:
                raise ValueError('INDEXED_ITEM_NOT_FOUND')
            values = [item.get('thumbnail')] + list(item.get('covers', []))
            for value in values:
                if not isinstance(value, str) or not value or value.startswith(('http:', 'https:')):
                    continue
                path = Path(value)
                if not path.is_absolute():
                    path = self.project / path
                if not path.is_file() or path.stat().st_size > 8 * 1024 * 1024:
                    continue
                data = path.read_bytes()
                mime = 'image/png' if data.startswith(b'\x89PNG\r\n\x1a\n') else 'image/jpeg' if data.startswith(b'\xff\xd8\xff') else None
                if mime:
                    return {'content': [{'type': 'text', 'text': json.dumps({'item_id': args['item_id'], 'sha256': hashlib.sha256(data).hexdigest()}, ensure_ascii=False)}, {'type': 'image', 'mimeType': mime, 'data': base64.b64encode(data).decode('ascii')}]}
            raise ValueError('NO_EXISTING_LOCAL_PNG_JPEG_COVER: no download or rescan performed')
        if name == 'feedback_read':
            identifier = args.get('id')
            entries = [feedback.read(self.feedback_path(identifier))] if identifier else feedback.load_entries(self.space / 'feedback', bool(args.get('include_addressed')))
            for entry in entries:
                path = self.feedback_path(str(entry.get('id', '')))
                self.read_hashes[entry['id']] = hashlib.sha256(path.read_bytes()).hexdigest()
            return text_result({'binding': str(self.binding), 'entries': entries, 'automatically_acknowledged': False})
        if name == 'feedback_image':
            path = self.feedback_path(args['id'])
            actual = hashlib.sha256(path.read_bytes()).hexdigest()
            if self.read_hashes.get(args['id']) != actual:
                raise ValueError('READ_FEEDBACK_FIRST_OR_CHANGED_SINCE_READ')
            entry = feedback.read(path)
            image = entry.get('image')
            if not isinstance(image, dict) or not image.get('path'):
                raise ValueError('NO_FROZEN_FEEDBACK_IMAGE')
            image_path = Path(image['path'])
            if not image_path.is_absolute():
                raise ValueError('FROZEN_IMAGE_ABSOLUTE_PATH_REQUIRED')
            if not image_path.is_file() or image_path.stat().st_size > 8 * 1024 * 1024:
                raise ValueError('FROZEN_IMAGE_MISSING_OR_OVER_8_MIB: use the original local image viewer; do not silently resize')
            data = image_path.read_bytes()
            digest = hashlib.sha256(data).hexdigest()
            if not image.get('sha256') or digest != image['sha256'].lower():
                raise ValueError('FROZEN_IMAGE_HASH_MISMATCH')
            mime = 'image/png' if data.startswith(b'\x89PNG\r\n\x1a\n') else 'image/jpeg' if data.startswith(b'\xff\xd8\xff') else None
            if not mime:
                raise ValueError('FROZEN_IMAGE_PNG_JPEG_REQUIRED')
            metadata = {'id': args['id'], 'context_id': self.context.context_id,
                'candidate': entry.get('candidate'), 'target': entry.get('target'),
                'image': image, 'rect': entry.get('rect'), 'sha256_verified': digest,
                'visual_acceptance': 'NOT_EVALUATED_BY_BRIDGE'}
            return {'content': [{'type': 'text', 'text': json.dumps(metadata, ensure_ascii=False)},
                {'type': 'image', 'mimeType': mime, 'data': base64.b64encode(data).decode('ascii')}]}
        if name in ('feedback_ack', 'consumer_lease'):
            # A disconnect remains available after drift so the old lease can always be released.
            if name != 'consumer_lease' or args.get('action') != 'disconnect':
                self.context.guard_write()

            # Exclusive short operation lock prevents two DSH bridge processes claiming the same lease.
            self.space.mkdir(parents=True, exist_ok=True)
            lock = self.space / '.dsh-operation.lock'
            try:
                fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except FileExistsError:
                raise ValueError('ANOTHER_DSH_OPERATION_ACTIVE: do not bypass the lock')
            try:
                os.close(fd)
                current = self.guard_consumer(args['session'])
                if name == 'feedback_ack':
                    path = self.feedback_path(args['id'])
                    actual = hashlib.sha256(path.read_bytes()).hexdigest()
                    if self.read_hashes.get(args['id']) != actual:
                        raise ValueError('READ_FEEDBACK_FIRST_OR_CHANGED_SINCE_READ')
                    if args['status'] not in ('seen', 'processing_feedback', 'addressed'):
                        raise ValueError('INVALID_STATUS')
                    entry = feedback.read(path)
                    gate = finish_gate(self.space, entry, args['status'], args['note'])
                    entry.update(status=args['status'], reply=args['note'], consumer_session=args['session'], execution_started=args['status'] == 'processing_feedback', updated_at=feedback.stamp(), **gate)
                    feedback.write(path, entry)
                    self.read_hashes[args['id']] = hashlib.sha256(path.read_bytes()).hexdigest()
                    return text_result(entry)
                action = args['action']
                if action not in ('connect', 'busy', 'idle', 'disconnect'):
                    raise ValueError('INVALID_ACTION')
                from datetime import timedelta
                current.update(session=args['session'], active=action != 'disconnect', busy=action == 'busy', updated_at=feedback.stamp(), expires_at=(datetime.now(timezone.utc) + timedelta(seconds=max(10, min(3600, int(args.get('seconds', 300)))))).isoformat(timespec='seconds'))
                feedback.write(self.space / 'consumer.json', current)
                return text_result(current)
            finally:
                lock.unlink(missing_ok=True)
        raise ValueError('UNKNOWN_TOOL')

def serve(bridge):
    _out = threading.Lock()

    def emit(payload, ensure_ascii=True):
        """Serialize every stdout write so the request loop and the watcher never interleave."""
        with _out:
            sys.stdout.write(json.dumps(payload, ensure_ascii=ensure_ascii) + "\n")
            sys.stdout.flush()

    def watch_inbox():
        """Low-frequency inbox watcher: notify the host about NEW dsh-routed requests only.

        Notifications are advisory; the host decides whether the model ever sees them.
        Existing records are treated as already known, so a restart never re-announces old work.
        """
        watcher = InboxNotifications()
        while True:
            try:
                for event in watcher.scan(bridge.notification_snapshot(), load):
                    emit({'jsonrpc': '2.0', 'method': 'notifications/message',
                          'params': {'level': 'info', 'logger': 'avatar-workbench', 'data': event}},
                         ensure_ascii=False)
            except Exception:
                pass
            time.sleep(3)

    threading.Thread(target=watch_inbox, daemon=True).start()

    for line in sys.stdin.buffer:
        request = None
        try:
            if len(line) > 4 * 1024 * 1024:
                raise ValueError('REQUEST_TOO_LARGE')
            request = json.loads(line)
            if 'id' not in request:
                continue
            method = request.get('method')
            if method == 'initialize':
                result = {'protocolVersion': '2024-11-05', 'capabilities': {'tools': {}}, 'serverInfo': {'name': 'avatar-workbench', 'version': '0.2.1'}}
            elif method == 'ping':
                result = {}
            elif method == 'tools/list':
                result = {'tools': [{'name': n, 'description': d, 'inputSchema': s} for n, d, s in TOOLS]}
            elif method == 'tools/call':
                try:
                    result = bridge.call(request['params']['name'], request['params'].get('arguments') or {})
                except Exception as error:
                    result = dict(text_result({'error': str(error)}), isError=True)
            else:
                emit({'jsonrpc': '2.0', 'id': request['id'], 'error': {'code': -32601, 'message': 'Method not supported'}})
                continue
            emit({'jsonrpc': '2.0', 'id': request['id'], 'result': result}, ensure_ascii=False)
        except Exception as error:
            emit({'jsonrpc': '2.0', 'id': request.get('id') if isinstance(request, dict) else None, 'error': {'code': -32603, 'message': str(error)}})

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True, type=Path)
    parser.add_argument('--call', choices=[t[0] for t in TOOLS])
    parser.add_argument('--args', default='{}')
    options = parser.parse_args()
    bridge = Bridge(options.config)
    if options.call:
        print(json.dumps(bridge.call(options.call, json.loads(options.args)), ensure_ascii=False, indent=2))
    else:
        serve(bridge)
