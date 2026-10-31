"""Keep the workbench version identical in code, package, payload manifest and packer."""
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


class WorkbenchVersionTests(unittest.TestCase):
    def test_single_workbench_version(self):
        package = json.loads((ROOT / "unity/AvatarWorkbench/package.json").read_text(encoding="utf-8"))["version"]
        manifest = json.loads((ROOT / "unity/AVATAR_WORKBENCH_MANIFEST.json").read_text(encoding="utf-8"))["version"]
        code = re.search(r'PluginVersion = "([^"]+)"',
                         (ROOT / "unity/AvatarWorkbench/Editor/WorkbenchWindow.Workflow.cs").read_text(encoding="utf-8")).group(1)
        packer = re.search(r"'workbench': '([^']+)'", (ROOT / "pack.py").read_text(encoding="utf-8")).group(1)
        expected = package + "-codex"
        self.assertEqual({"manifest": manifest, "code": code, "pack": packer},
                         {"manifest": expected, "code": expected, "pack": expected})


if __name__ == "__main__":
    unittest.main()
