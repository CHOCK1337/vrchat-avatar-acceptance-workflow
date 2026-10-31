"""A reply cannot be marked addressed without a Unity hand-in check newer than the request."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "adapter"))


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


WRITERS = {
    "codex_feedback": load("codex_feedback_gate", ROOT / "unity/AvatarWorkbench/Tools/codex_feedback.py"),
    "workbench_mcp": load("workbench_mcp_gate", ROOT / "adapter/workbench_mcp.py"),
}
REQUEST = {"id": "a" * 32, "created_at": "2026-10-31T01:20:00Z", "status": "processing_feedback"}


class FinishGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.space = Path(self.temp.name)

    def receipt(self, checked_at, errors=()):
        (self.space / "finish-check.json").write_text(json.dumps(
            {"checked_at": checked_at, "passed": not errors, "errors": list(errors), "warnings": []}), encoding="utf-8")

    def test_every_writer_applies_the_same_gate(self):
        for name, module in WRITERS.items():
            with self.subTest(writer=name):
                (self.space / "finish-check.json").unlink(missing_ok=True)
                self.assertEqual(module.finish_gate(self.space, REQUEST, "seen", ""), {})
                with self.assertRaisesRegex(ValueError, "FINISH_CHECK_REQUIRED"):
                    module.finish_gate(self.space, REQUEST, "addressed", "已完成")
                skipped = module.finish_gate(self.space, REQUEST, "addressed", "未运行收尾检查：Unity 没有打开")
                self.assertEqual(skipped["finish_check"]["status"], "NOT_RUN")
                self.receipt("2026-10-31T01:19:59Z")
                with self.assertRaisesRegex(ValueError, "FINISH_CHECK_REQUIRED"):
                    module.finish_gate(self.space, REQUEST, "addressed", "已完成")
                self.receipt("2026-10-31T02:00:00+00:00", errors=[{"code": "MULTIPLE_ACTIVE_AVATARS"}])
                failed = module.finish_gate(self.space, REQUEST, "addressed", "已完成")["finish_check"]
                self.assertEqual((failed["status"], failed["errors"][0]["code"]), ("FAIL", "MULTIPLE_ACTIVE_AVATARS"))

    def test_template_shipped_with_workbench_matches_skill(self):
        # The handoff points at the workbench copy, which exists even when an older Skill was reused.
        shipped = ROOT / "unity/AvatarWorkbench/Tools/finish-check.md"
        self.assertEqual(shipped.read_bytes(), (ROOT / "skills/assemble-vrchat-avatar/references/finish-check.md").read_bytes())
        self.assertIn("BODY_MASK_KEPT", shipped.read_text(encoding="utf-8"))
        guid = (ROOT / "unity/AvatarWorkbench/Tools/finish-check.md.meta").read_text(encoding="utf-8").split("guid: ")[1].split()[0]
        self.assertIn('GuideGuid = "' + guid + '"', (ROOT / "unity/AvatarWorkbench/Editor/WorkbenchFinishCheck.cs").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
