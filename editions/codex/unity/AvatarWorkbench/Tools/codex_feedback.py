#!/usr/bin/env python3
"""On-demand consumer for the existing AvatarWorkbench feedback inbox. No server/agent."""
import argparse
import copy
from contextlib import contextmanager
import errno
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import time
import zipfile
import uuid


def stamp():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def location(binding, storage=None):
    root = Path(storage) if storage else Path(os.environ.get("LOCALAPPDATA", str(Path.home() / ".local/share"))) / "AvatarWorkbench"
    # Keep the same logical namespace as Unity Path.GetFullPath. Resolving reparse
    # points can change MSIX AppData paths after the first file is created.
    namespace = hashlib.sha256(os.path.abspath(binding).encode("utf-8")).hexdigest()[:20]
    return root / namespace


def read(path):
    path = Path(path)
    if path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError("记录超过 2 MiB")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", dir=path.parent, prefix=".consumer-", delete=False, encoding="utf-8") as f:
        temp = Path(f.name)
        json.dump(value, f, ensure_ascii=False, indent=2, allow_nan=False)
        f.write("\n")
        f.flush()
        os.fsync(f.fileno())
    try:
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


# Every write under the operation lock lasts milliseconds, so a short wait
# spares the caller a retry and an aged legacy marker has no live owner.
# WorkbenchData.cs uses the same two values.
OPERATION_WAIT_SECONDS = 2.0
LEGACY_LOCK_STALE_SECONDS = 30


def _lock_guard_byte(guard, acquire):
    guard.seek(0)
    if os.name == "nt":
        import msvcrt
        msvcrt.locking(guard.fileno(), msvcrt.LK_NBLCK if acquire else msvcrt.LK_UNLCK, 1)
    else:
        import fcntl
        fcntl.lockf(guard, fcntl.LOCK_EX | fcntl.LOCK_NB if acquire else fcntl.LOCK_UN, 1)


@contextmanager
def binding_guard(space):
    """OS releases the byte-range lock even when its owner exits inside a write."""
    space.mkdir(parents=True, exist_ok=True)
    with (space / ".workbench-operation.guard").open("a+b") as guard:
        deadline = time.monotonic() + OPERATION_WAIT_SECONDS
        while True:
            try:
                _lock_guard_byte(guard, True)
                break
            except OSError as exc:
                if exc.errno not in (errno.EACCES, errno.EAGAIN, errno.EDEADLK):
                    raise
                if time.monotonic() >= deadline:
                    raise ValueError("ANOTHER_OPERATION_ACTIVE: retry after the current write") from exc
                time.sleep(0.05)
        try:
            yield
        finally:
            _lock_guard_byte(guard, False)


def retire_stale_legacy_lock(marker):
    """Rename an old bridge's leftover marker aside. True when the name is free again."""
    try:
        info = marker.lstat()
    except FileNotFoundError:
        return True
    if not stat.S_ISREG(info.st_mode) or info.st_size or time.time() - info.st_mtime < LEGACY_LOCK_STALE_SECONDS:
        return False
    marker.rename(marker.with_name(marker.name + ".recovered-" + uuid.uuid4().hex))
    return True


@contextmanager
def operation_lock(space):
    with binding_guard(space):
        # Old bridges use O_EXCL on this name. A directory keeps them out and
        # distinguishes an interrupted new writer from a live legacy file.
        marker = space / ".dsh-operation.lock"
        deadline = time.monotonic() + OPERATION_WAIT_SECONDS
        while True:
            try:
                marker.mkdir(exist_ok=True)
                break
            except FileExistsError as exc:
                if retire_stale_legacy_lock(marker):
                    continue
                if time.monotonic() >= deadline:
                    raise ValueError("ANOTHER_OPERATION_ACTIVE: " + str(marker) + " is a legacy marker. An empty one is set aside "
                                     "automatically after %d seconds; otherwise close the older hosts and use recover-legacy-lock."
                                     % LEGACY_LOCK_STALE_SECONDS) from exc
                time.sleep(0.05)
        try:
            yield
        finally:
            try:
                marker.rmdir()
            except OSError:
                pass  # A leftover directory never blocks this version; the next operation removes it.


def recover_legacy_lock(space):
    with binding_guard(space):
        marker = space / ".dsh-operation.lock"
        if not marker.exists():
            return None
        if marker.is_symlink() or not marker.is_file() or marker.stat().st_size:
            raise ValueError("EXPECTED_EMPTY_LEGACY_LOCK: " + str(marker))
        saved = marker.with_name(marker.name + ".recovered-" + uuid.uuid4().hex)
        marker.rename(saved)
        return saved


def lease_expiry(lease):
    """None when the record names no usable end time; nobody can honour or renew such a lease."""
    try:
        expiry = datetime.fromisoformat(str(lease.get("expires_at") or "").replace("Z", "+00:00"))
    except ValueError:
        return None
    return expiry if expiry.tzinfo else expiry.replace(tzinfo=timezone.utc)


def guard_consumer(space, session):
    if not session.strip() or len(session) > 200:
        raise ValueError("ACTUAL_SESSION_REQUIRED")
    path = space / "consumer.json"
    try:
        current = read(path) if path.is_file() else {}
    except ValueError:
        current = {}  # Unity reads a damaged record as "no lease" too; the next write replaces it.
    if not isinstance(current, dict):
        current = {}
    if current.get("active") and current.get("session") != session:
        expiry = lease_expiry(current)
        if expiry and expiry > datetime.now(timezone.utc):
            raise ValueError("ANOTHER_CONSUMER_ACTIVE: %s until %s. If that session has stopped, the user can release it in the "
                             "Workbench: 我来操作, then 交回 AI." % (current.get("session"), expiry.isoformat(timespec="seconds")))
    return current


def user_editing(space):
    return (space / "user-editing.json").is_file()


def save_lease(space, current, session, action, seconds):
    if action not in ("connect", "busy", "idle", "disconnect"):
        raise ValueError("INVALID_ACTION")
    if user_editing(space):
        if action in ("connect", "busy"):
            raise ValueError("USER_EDITING: use 交回 AI in the Workbench before starting another write")
        action = "disconnect"
    current.update(session=session, active=action != "disconnect", busy=action == "busy", updated_at=stamp(),
                   expires_at=(datetime.now(timezone.utc) + timedelta(seconds=max(10, min(3600, seconds)))).isoformat(timespec="seconds"))
    write(space / "consumer.json", current)
    return current


ACK_STATUSES = ("seen", "processing_feedback", "waiting_user", "addressed")


def save_ack(space, path, entry, status, note, session, current):
    if status not in ACK_STATUSES:
        raise ValueError("INVALID_STATUS")
    if status == "processing_feedback" and user_editing(space):
        raise ValueError("USER_EDITING: wait for the user to return control")
    gate = finish_gate(space, entry, status, note)
    if status == "waiting_user":
        if not note.strip():
            raise ValueError("HANDOFF_NOTE_REQUIRED: name the candidate, next manual step and how to resume")
        # Release first. If the receipt write fails the user can still edit and
        # the same acknowledgement can be retried without changing ownership.
        if current.get("session") == session and current.get("active"):
            current.update(active=False, busy=False, updated_at=stamp())
            write(space / "consumer.json", current)
    entry.update(status=status, reply=note, consumer_session=session,
                 execution_started=status == "processing_feedback", updated_at=stamp(), **gate)
    write(path, entry)
    return entry


def load_entries(inbox, include_addressed=False, include_waiting_user=False):
    """Keep a single malformed receipt from hiding all valid user feedback."""
    entries = []
    for path in sorted(inbox.glob("*.json"), key=lambda p: p.stat().st_mtime, reverse=True)[:300]:
        try:
            entry = read(path)
            if not isinstance(entry, dict):
                raise ValueError("feedback is not an object")
        except (OSError, ValueError) as error:
            print("FEEDBACK_SKIPPED: " + path.name + ": " + str(error), file=sys.stderr)
            continue
        status = entry.get("status")
        if include_addressed or status not in ("addressed", "waiting_user") or (include_waiting_user and status == "waiting_user"):
            entries.append(entry)
    return entries


def export_entries(entries, output):
    """Create a portable archive atomically; never rewrite source receipts."""
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix=".feedback-export-", suffix=".zip", dir=output.parent)
    os.close(fd)
    temp = Path(name)
    try:
        exported = copy.deepcopy(entries)
        with zipfile.ZipFile(temp, "w", zipfile.ZIP_DEFLATED) as archive:
            for entry in exported:
                image = entry.get("image")
                if not image:
                    continue
                identifier = str(entry.get("id", ""))
                if not re.fullmatch(r"[a-f0-9]{32}", identifier):
                    raise ValueError("反馈 id 无效，无法导出图片")
                path = Path(image.get("path", ""))
                if not path.is_file():
                    raise ValueError("反馈原图不存在，未生成不完整导出")
                if path.suffix.lower() not in {".png", ".jpg", ".jpeg"} or path.stat().st_size > 20 * 1024 * 1024:
                    raise ValueError("反馈原图格式或大小不支持")
                image_bytes = path.read_bytes()
                if hashlib.sha256(image_bytes).hexdigest() != image.get("sha256"):
                    raise ValueError("反馈原图已变化，拒绝导出为同一张图")
                archive_path = "images/" + identifier + path.suffix.lower()
                archive.writestr(archive_path, image_bytes)
                image["original_path"] = str(path)
                image["path"] = archive_path
            archive.writestr("feedback.json", json.dumps(exported, ensure_ascii=False, indent=2, allow_nan=False))
            archive.writestr("README.txt", "Private feedback export. Image paths in feedback.json are relative to this folder. No model, Unity project or upload authorization is included.\n")
        os.replace(temp, output)
    finally:
        temp.unlink(missing_ok=True)


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


def main():
    # MCP/PowerShell pipes read UTF-8; Windows' legacy console code page would corrupt Chinese feedback.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["path", "inbox", "ack", "connect", "busy", "disconnect", "export", "recover-legacy-lock"])
    parser.add_argument("--task", "--binding", dest="binding", required=True, type=Path)
    parser.add_argument("--storage", type=Path)
    parser.add_argument("--id")
    parser.add_argument("--status", choices=ACK_STATUSES)
    parser.add_argument("--note", default="")
    parser.add_argument("--session")
    parser.add_argument("--lease", type=int, default=120)
    parser.add_argument("--value", choices=["true", "false"], default="true")
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--legacy-writers-stopped", action="store_true", help="Confirm all older writers on this binding have exited")
    args = parser.parse_args()
    space = location(args.binding, args.storage)
    inbox = space / "feedback"
    if args.command == "path":
        print(str(inbox))
        return
    if args.command in ["inbox", "export"]:
        entries = load_entries(inbox, args.all, include_waiting_user=args.command == "export")
        if args.command == "inbox":
            print(json.dumps(entries, ensure_ascii=False, indent=2))
            return
        if not args.output:
            parser.error("export 需要 --output <私有反馈 ZIP>")
        export_entries(entries, args.output)
        print(str(args.output))
        return
    if args.command == "recover-legacy-lock":
        if not args.legacy_writers_stopped:
            parser.error("Close both older hosts on this binding first, then pass --legacy-writers-stopped")
        print(recover_legacy_lock(space) or "NO_LEGACY_LOCK")
        return
    if not args.session:
        parser.error("消费者操作需要 --session <实际 Codex 会话标识>")
    with operation_lock(space):
        consumer = guard_consumer(space, args.session)
        if args.command == "ack":
            if not args.id or not re.fullmatch(r"[a-f0-9]{32}", args.id) or not args.status:
                parser.error("ack 需要真实 --id 与 --status")
            path = inbox / (args.id + ".json")
            entry = save_ack(space, path, read(path), args.status, args.note, args.session, consumer)
            print(json.dumps(entry, ensure_ascii=False, indent=2))
            return
        if args.command == "busy" and consumer.get("session") != args.session:
            raise ValueError("busy 只能更新已连接的同一会话")
        action = "idle" if args.command == "busy" and args.value == "false" else args.command
        save_lease(space, consumer, args.session, action, args.lease)
        print(json.dumps(consumer, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as error:
        raise SystemExit("FEEDBACK_ERROR: " + str(error))
