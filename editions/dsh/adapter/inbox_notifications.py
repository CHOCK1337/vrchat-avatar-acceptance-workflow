"""Advisory inbox events scoped to an explicitly selected Workbench context."""
import re


class InboxNotifications:
    def __init__(self):
        self.context_id = None
        self.seen = set()

    def scan(self, snapshot, read):
        if not snapshot["routing_ready"]:
            return []
        if self.context_id != snapshot["context_id"]:
            self.context_id = snapshot["context_id"]
            self.seen = set(snapshot["baseline"])
        inbox = snapshot["inbox"]
        if not inbox.is_dir():
            return []
        events = []
        for path in sorted(inbox.glob("*.json")):
            identifier = path.stem
            if identifier in self.seen or not re.fullmatch(r"[a-f0-9]{32}", identifier):
                continue
            try:
                entry = read(path)
            except (OSError, ValueError):
                # A writer may still be completing a file. Retry on the next scan.
                continue
            if not isinstance(entry, dict) or entry.get("id") != identifier:
                continue
            self.seen.add(identifier)
            route = entry.get("model_route")
            if entry.get("status") != "received" or not isinstance(route, dict) or route.get("type") != "dsh":
                continue
            events.append({
                "kind": "workbench_request_available",
                "feedback_id": identifier,
                "context_id": self.context_id,
                "project": snapshot["project"],
                "binding": snapshot["binding"],
                "requires_read": True,
                "automatic_wakeup": False,
            })
        return events
