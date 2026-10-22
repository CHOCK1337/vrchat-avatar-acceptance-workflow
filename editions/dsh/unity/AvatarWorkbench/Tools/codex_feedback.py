#!/usr/bin/env python3
"""On-demand consumer for the existing AvatarWorkbench feedback inbox. No server/agent."""
import argparse
import copy
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import zipfile


def stamp():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def location(binding, storage=None):
    root = Path(storage) if storage else Path(os.environ.get("LOCALAPPDATA", str(Path.home() / ".local/share"))) / "AvatarWorkbench"
    namespace = hashlib.sha256(str(Path(binding).resolve()).encode("utf-8")).hexdigest()[:20]
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


def load_entries(inbox, include_addressed=False):
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
        if include_addressed or entry.get("status") != "addressed":
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


def main():
    # MCP/PowerShell pipes read UTF-8; Windows' legacy console code page would corrupt Chinese feedback.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["path", "inbox", "ack", "connect", "busy", "disconnect", "export"])
    parser.add_argument("--task", "--binding", dest="binding", required=True, type=Path)
    parser.add_argument("--storage", type=Path)
    parser.add_argument("--id")
    parser.add_argument("--status", choices=["seen", "processing_feedback", "addressed"])
    parser.add_argument("--note", default="")
    parser.add_argument("--session")
    parser.add_argument("--lease", type=int, default=120)
    parser.add_argument("--value", choices=["true", "false"], default="true")
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    space = location(args.binding, args.storage)
    inbox = space / "feedback"
    if args.command == "path":
        print(str(inbox))
        return
    if args.command in ["inbox", "export"]:
        entries = load_entries(inbox, args.all)
        if args.command == "inbox":
            print(json.dumps(entries, ensure_ascii=False, indent=2))
            return
        if not args.output:
            parser.error("export 需要 --output <私有反馈 ZIP>")
        export_entries(entries, args.output)
        print(str(args.output))
        return
    if not args.session:
        parser.error("消费者操作需要 --session <实际 Codex 会话标识>")
    if args.command == "ack":
        if not args.id or not re.fullmatch(r"[a-f0-9]{32}", args.id) or not args.status:
            parser.error("ack 需要真实 --id 与 --status")
        path = inbox / (args.id + ".json")
        entry = read(path)
        entry.update(status=args.status, reply=args.note, consumer_session=args.session,
                     execution_started=args.status == "processing_feedback", updated_at=stamp())
        write(path, entry)
        print(json.dumps(entry, ensure_ascii=False, indent=2))
        return
    consumer_path = space / "consumer.json"
    consumer = read(consumer_path) if consumer_path.exists() else {}
    if args.command == "busy" and consumer.get("session") != args.session:
        raise ValueError("busy 只能更新已连接的同一会话")
    consumer.update(session=args.session, active=args.command != "disconnect",
                    busy=args.command == "busy" and args.value == "true", updated_at=stamp(),
                    expires_at=(datetime.now(timezone.utc) + timedelta(seconds=max(10, min(args.lease, 3600)))).isoformat(timespec="seconds"))
    write(consumer_path, consumer)
    print(json.dumps(consumer, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as error:
        raise SystemExit("FEEDBACK_ERROR: " + str(error))
