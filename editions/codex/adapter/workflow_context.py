"""Session-local workflow routing. No profile/task/index/Unity mutations."""
import hashlib
import json
import os
from pathlib import Path

def read_json(path, limit=4*1024*1024):
    path = Path(path)
    if path.stat().st_size > limit:
        raise ValueError("CONTEXT_FILE_TOO_LARGE")
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError("CONTEXT_OBJECT_REQUIRED")
    return value

def data_root(config, override=None):
    if override is not None:
        return Path(os.path.abspath(override))
    if config.get("storage_root"):
        return Path(os.path.abspath(config["storage_root"]))
    return Path(os.path.abspath(Path(os.environ.get("LOCALAPPDATA", str(Path.home()/".local/share")))/"AvatarWorkbench"))

class WorkflowContext:
    def __init__(self, config, project=None, binding=None, storage=None, selected=False):
        self.config = config
        self.project = Path(project or config["project"]).resolve()
        self.storage = data_root(config, storage)
        self.editor = self.storage/"editor"/hashlib.sha256(str(self.project).encode()).hexdigest()[:20]
        self.window_file = self.editor/"window.json"
        self.source = "explicit_binding" if selected and binding else "saved_window" if selected else "configured"
        # Unity hashes Path.GetFullPath, which retains logical MSIX/junction names.
        self.binding = Path(os.path.abspath(binding)) if binding else self.window_binding() if selected else Path(os.path.abspath(config["binding"]))
        configured_project = Path(config["project"]).resolve()
        self.index = (Path(os.path.abspath(config["catalog_file"]))
                      if not selected and self.project == configured_project and self.storage == data_root(config) and config.get("catalog_file")
                      else self.editor/"sources.json")
        self.context_id = hashlib.sha256(json.dumps(
            [str(self.project), str(self.binding), str(self.storage)], ensure_ascii=False).encode()).hexdigest()[:20]

    def window_binding(self):
        state = read_json(self.window_file) if self.window_file.is_file() else {}
        task = state.get("task_path")
        if task and not isinstance(task, str):
            raise ValueError("INVALID_WINDOW_TASK_PATH")
        value = Path(task) if task else self.editor/"selection.json"
        return Path(os.path.abspath(value if value.is_absolute() else self.project/value))

    @classmethod
    def select(cls, config, project, binding=None, storage=None):
        project = Path(project).resolve()
        if not (project/"ProjectSettings/ProjectVersion.txt").is_file():
            raise ValueError("EXISTING_UNITY_PROJECT_REQUIRED")
        context = cls(config, project, binding, storage, selected=True)
        # Inspecting a superseded task is allowed; status/guard prevent treating it as writable.
        context.status()
        return context

    def status(self):
        issues = []
        if not (self.project/"ProjectSettings/ProjectVersion.txt").is_file():
            issues.append("PROJECT_NOT_FOUND")
        window_binding = self.window_binding()
        if self.source in ("configured", "saved_window") and window_binding != self.binding:
            issues.append("WORKBENCH_BINDING_MISMATCH")
        task = read_json(self.binding) if self.binding.is_file() else None
        status = str((task or {}).get("status") or "")
        if status.upper().startswith(("SUPERSEDED", "CANCELLED", "CANCELED")):
            issues.append("TASK_SUPERSEDED_OR_CANCELLED")
        task_project = (task or {}).get("project_path") or (task or {}).get("project")
        if isinstance(task_project, str) and task_project:
            candidate = Path(task_project)
            if not candidate.is_absolute():
                candidate = self.binding.parent/candidate
            if candidate.resolve() != self.project:
                issues.append("TASK_PROJECT_MISMATCH")
        return {
            "context_id": self.context_id, "context_source": self.source,
            "project": str(self.project), "binding": str(self.binding),
            "storage_root": str(self.storage), "workbench_window": str(self.window_file),
            "workbench_binding": str(window_binding), "task_exists": self.binding.is_file(),
            "task_status": status or None, "task_project": task_project,
            "routing_ready": not issues, "routing_issues": issues,
            "catalog_file": str(self.index), "catalog_available": self.index.is_file(),
            "configuration_rewritten": False,
        }

    def guard_write(self):
        current = self.status()
        if current["routing_issues"]:
            raise ValueError("CONTEXT_NOT_READY: " + ", ".join(current["routing_issues"]))
        return current
