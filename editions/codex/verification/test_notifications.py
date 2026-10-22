"""Validate advisory notifications without a model call or live inbox mutation."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "adapter"))
from inbox_notifications import InboxNotifications


class InboxNotificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.watcher = InboxNotifications()

    def snapshot(self, name="A", baseline=(), ready=True):
        folder = self.folder / name
        folder.mkdir(exist_ok=True)
        return {"context_id": name, "project": name, "binding": name + ".json",
                "inbox": folder, "baseline": baseline, "routing_ready": ready}

    def write(self, state, identifier, **values):
        record = {"id": identifier, "status": "received", "model_route": {"type": "codex"}, "message": "Private user text"}
        record.update(values)
        path = state["inbox"] / (identifier + ".json")
        path.write_text(json.dumps(record), encoding="utf-8")
        return path

    def scan(self, state):
        return self.watcher.scan(state, lambda p: json.loads(p.read_text(encoding="utf-8")))

    def test_existing_records_are_not_replayed_after_context_switch(self):
        identifier = "a" * 32
        first = self.snapshot(baseline=[identifier])
        second = self.snapshot("B", baseline=[identifier])
        self.write(first, identifier)
        self.write(second, identifier)
        self.assertEqual([], self.scan(first))
        self.assertEqual([], self.scan(second))

    def test_new_record_before_first_poll_is_still_announced_once(self):
        state = self.snapshot()
        self.write(state, "a" * 32)
        events = self.scan(state)
        self.assertEqual(1, len(events))
        self.assertEqual("A.json", events[0]["binding"])
        self.assertNotIn("Private user text", json.dumps(events))
        self.assertFalse(events[0]["automatic_wakeup"])
        self.assertEqual([], self.scan(state))

    def test_incomplete_file_is_retried_after_writer_finishes(self):
        state = self.snapshot()
        path = state["inbox"] / ("a" * 32 + ".json")
        path.write_text('{"id":', encoding="utf-8")
        self.assertEqual([], self.scan(state))
        self.write(state, "a" * 32)
        self.assertEqual(1, len(self.scan(state)))

    def test_same_identifier_in_other_context_is_not_globally_suppressed(self):
        first = self.snapshot()
        second = self.snapshot("B")
        self.write(first, "a" * 32)
        self.assertEqual("A", self.scan(first)[0]["context_id"])
        self.write(second, "a" * 32)
        self.assertEqual("B", self.scan(second)[0]["context_id"])

    def test_stale_context_waits_for_explicit_ready_context(self):
        state = self.snapshot(ready=False)
        self.write(state, "a" * 32)
        self.assertEqual([], self.scan(state))
        state["routing_ready"] = True
        self.assertEqual(1, len(self.scan(state)))

    def test_other_routes_bad_records_and_replies_do_not_starve_new_request(self):
        state = self.snapshot()
        self.write(state, "a" * 32, model_route=[])
        self.write(state, "b" * 32, model_route={"type": "dsh"})
        self.write(state, "c" * 32, status="addressed")
        self.write(state, "d" * 32)
        self.assertEqual(["d" * 32], [e["feedback_id"] for e in self.scan(state)])


if __name__ == "__main__":
    unittest.main(verbosity=2)
