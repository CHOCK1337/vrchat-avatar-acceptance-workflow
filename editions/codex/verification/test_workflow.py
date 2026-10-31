#!/usr/bin/env python3
"""Regression tests using isolated files only; no live tasks, Unity writes or model calls."""
import base64
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"adapter"))
from workbench_mcp import Bridge, feedback

def value(result):
    return json.loads(result["content"][0]["text"])

def write(path, data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(data,ensure_ascii=False),encoding="utf-8")
    return path

class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.base=Path(self.temp.name)
        self.storage=self.base/"storage"
        self.projects=[]
        self.tasks=[]
        self.windows=[]
        for label in ("A","B"):
            project=self.base/("project-"+label)
            (project/"ProjectSettings").mkdir(parents=True)
            (project/"ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.22f1",encoding="utf-8")
            task=write(self.base/("task-"+label+".json"),{"task_id":label,"project_path":str(project),"status":"ACTIVE","unknown_user_field":{"preserve":True}})
            editor=self.storage/"editor"/hashlib.sha256(str(project.resolve()).encode()).hexdigest()[:20]
            window=write(editor/"window.json",{"task_path":str(task)})
            write(editor/"sources.json",{"directories":[{"id":label,"items":[{"id":label+"-item","name":label,"category":"衣服","bases":["Eku"],"metadata_source":"fixture"}]}]})
            self.projects.append(project);self.tasks.append(task);self.windows.append(window)
        self.old=write(self.base/"old.json",{"status":"SUPERSEDED_BY_USER_SHADER_CHANGE","project_path":str(self.projects[0])})
        self.config=write(self.base/"connection.json",{"project":str(self.projects[0]),"binding":str(self.old),"storage_root":str(self.storage)})
        self.config_bytes=self.config.read_bytes()
        self.bridge=Bridge(self.config)
        self.id="ab"*16
        # A real 1x1 PNG, used only to verify byte-preserving transport.
        self.png=base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jCTYAAAAASUVORK5CYII=")
        self.image=self.base/"frozen.png";self.image.write_bytes(self.png)
        self.candidate={"id":"original-candidate","sha256":"unchanged","revision":3}
        for task in self.tasks:
            write(feedback.location(task,self.storage)/"feedback"/(self.id+".json"),{
                "id":self.id,"candidate":self.candidate,"message":"原始用户反馈","status":"saved",
                "image":{"path":str(self.image),"sha256":hashlib.sha256(self.png).hexdigest()},
                "rect":{"x":0.1,"y":0.2,"w":0.2,"h":0.3}})
    def tearDown(self):
        self.assertEqual(self.config_bytes,self.config.read_bytes())
        for task in self.tasks:
            self.assertTrue(json.loads(task.read_text(encoding="utf-8"))["unknown_user_field"]["preserve"])
        self.temp.cleanup()
    def select(self, i=0, **args):
        return value(self.bridge.call("context_select",dict(project=str(self.projects[i]),**args)))
    def read(self):
        return value(self.bridge.call("feedback_read",{"id":self.id}))
    def ack(self,status="seen",session="fixture-session"):
        return self.bridge.call("feedback_ack",{"id":self.id,"session":session,"status":status,"note":"fixture reply"})
    def lease(self,action,session="fixture-session"):
        return self.bridge.call("consumer_lease",{"session":session,"action":action,"seconds":60})

    def test_stale_default_is_readable_but_not_writable(self):
        old_bytes=self.old.read_bytes()
        status=value(self.bridge.call("workbench_status",{}))
        self.assertFalse(status["routing_ready"])
        self.assertIn("TASK_SUPERSEDED_OR_CANCELLED",status["routing_issues"])
        with self.assertRaisesRegex(ValueError,"CONTEXT_NOT_READY"):self.lease("busy")
        self.assertEqual(old_bytes,self.old.read_bytes())

    def test_logical_binding_namespace_matches_unity_despite_physical_redirection(self):
        binding=self.tasks[0]
        logical=os.path.abspath(binding)
        physical=self.base/'MSIX-redirected/task-A.json'
        original=os.path.realpath
        def redirected(path, **kwargs):
            return str(physical) if os.path.abspath(path)==logical else original(path,**kwargs)
        with mock.patch('os.path.realpath',side_effect=redirected):
            self.select(0)
            self.assertEqual(logical,str(self.bridge.binding))
            self.assertEqual(self.storage/hashlib.sha256(logical.encode()).hexdigest()[:20],feedback.location(binding,self.storage))

    def test_select_follows_exact_project_window_and_index(self):
        selected=self.select(1)
        self.assertEqual(str(self.tasks[1].resolve()),selected["binding"])
        self.assertTrue(selected["routing_ready"])
        result=value(self.bridge.call("catalog_search",{}))
        self.assertEqual("B",result["items"][0]["source_id"])
        self.assertFalse(result["scanned_now"])

    def test_selected_context_does_not_keep_foreign_configured_catalog(self):
        cfg=json.loads(self.config.read_text(encoding="utf-8"))
        cfg["catalog_file"]=str(self.base/"old-container-sources.json")
        write(self.base/"old-container-sources.json",{"directories":[{"id":"stale","items":[{"id":"old"}]}]})
        # An isolated config copy; the original connection must remain unchanged.
        other_config=write(self.base/"catalog-config.json",cfg)
        self.bridge=Bridge(other_config)
        self.select()
        catalog=value(self.bridge.call("catalog_search",{}))
        self.assertEqual("A",catalog["items"][0]["source_id"])

    def test_empty_window_uses_same_project_selection(self):
        write(self.windows[1],{"task_path":""})
        selected=self.select(1)
        self.assertEqual(str(self.windows[1].parent/"selection.json"),selected["binding"])
        self.assertNotEqual(str(self.old),selected["binding"])

    def test_wrong_project_task_blocks_writes(self):
        status=self.select(0,binding=str(self.tasks[1]))
        self.assertIn("TASK_PROJECT_MISMATCH",status["routing_issues"])
        with self.assertRaisesRegex(ValueError,"CONTEXT_NOT_READY"):self.lease("connect")

    def test_explicit_binding_does_not_rewrite_window(self):
        before=self.windows[0].read_bytes()
        alternative=write(self.base/"intentional.json",{"project_path":str(self.projects[0]),"status":"ACTIVE"})
        selected=self.select(0,binding=str(alternative))
        self.assertTrue(selected["routing_ready"])
        self.assertEqual(before,self.windows[0].read_bytes())

    def test_window_drift_blocks_ack_and_allows_disconnect(self):
        self.select();self.read();self.lease("connect");self.lease("busy")
        entry=self.bridge.feedback_path(self.id);before=entry.read_bytes()
        write(self.windows[0],{"task_path":str(self.old)})
        with self.assertRaisesRegex(ValueError,"CONTEXT_NOT_READY"):self.ack()
        self.assertEqual(before,entry.read_bytes())
        result=value(self.lease("disconnect"))
        self.assertFalse(result["active"])

    def test_task_superseded_after_selection_blocks_ack(self):
        self.select();self.read()
        task=json.loads(self.tasks[0].read_text(encoding="utf-8"));task["status"]="SUPERSEDED"
        write(self.tasks[0],task)
        with self.assertRaisesRegex(ValueError,"TASK_SUPERSEDED"):self.ack()

    def test_context_switch_requires_reread_even_for_same_feedback_id(self):
        self.select();self.read();self.select(1)
        with self.assertRaisesRegex(ValueError,"READ_FEEDBACK_FIRST"):self.ack()
        self.read()
        self.assertEqual(self.candidate,value(self.ack())["candidate"])

    def test_frozen_image_requires_read(self):
        self.select()
        with self.assertRaisesRegex(ValueError,"READ_FEEDBACK_FIRST"):
            self.bridge.call("feedback_image",{"id":self.id})

    def test_frozen_image_returns_original_pixels_and_identity(self):
        self.select();self.read()
        result=self.bridge.call("feedback_image",{"id":self.id})
        metadata=json.loads(result["content"][0]["text"])
        self.assertEqual(self.png,base64.b64decode(result["content"][1]["data"]))
        self.assertEqual(self.candidate,metadata["candidate"])
        self.assertEqual("NOT_EVALUATED_BY_BRIDGE",metadata["visual_acceptance"])
        self.assertEqual("saved",feedback.read(self.bridge.feedback_path(self.id))["status"])

    def test_changed_image_is_rejected_without_ack(self):
        self.select();self.read();self.image.write_bytes(self.png+b"changed")
        with self.assertRaisesRegex(ValueError,"HASH_MISMATCH"):
            self.bridge.call("feedback_image",{"id":self.id})
        self.assertEqual("saved",feedback.read(self.bridge.feedback_path(self.id))["status"])

    def test_changed_receipt_invalidates_image_and_ack(self):
        self.select();self.read()
        path=self.bridge.feedback_path(self.id);entry=feedback.read(path);entry["message"]="new user correction";write(path,entry)
        with self.assertRaisesRegex(ValueError,"CHANGED_SINCE_READ"):
            self.bridge.call("feedback_image",{"id":self.id})
        with self.assertRaisesRegex(ValueError,"CHANGED_SINCE_READ"):self.ack()

    def test_foreign_lease_still_rejected(self):
        self.select();self.lease("connect")
        with self.assertRaisesRegex(ValueError,"ANOTHER_CONSUMER_ACTIVE"):self.lease("busy","foreign-session")

    def test_switch_while_busy_requires_release(self):
        self.select();self.lease("connect");self.lease("busy")
        with self.assertRaisesRegex(ValueError,"RELEASE_CURRENT_BUSY"):self.select(1)
        self.lease("disconnect")
        self.assertTrue(self.select(1)["routing_ready"])

    def test_real_same_process_ack_preserves_candidate(self):
        self.select();self.read()
        self.assertEqual(self.candidate,value(self.ack())["candidate"])
        with self.assertRaisesRegex(ValueError,"FINISH_CHECK_REQUIRED"):self.ack("addressed")
        # The Unity hand-in check writes this receipt next to the inbox before a reply.
        (self.bridge.space/"finish-check.json").write_text(json.dumps({"checked_at":"2999-01-01T00:00:00Z","passed":True,"errors":[],"warnings":[]}),encoding="utf-8")
        addressed=value(self.ack("addressed"))
        self.assertEqual(self.candidate,addressed["candidate"])
        self.assertEqual("PASS",addressed["finish_check"]["status"])
        self.assertNotIn("visual_checked",addressed)

    def test_explicit_storage_root_uses_its_own_namespace(self):
        other=self.base/"other-storage"
        selected=self.select(1,storage_root=str(other))
        self.assertEqual(str(other.resolve()),selected["storage_root"])
        self.assertTrue(str(self.bridge.space).startswith(str(other)))
        self.assertFalse(other.exists())

    def test_invalid_project_selection_preserves_previous_context(self):
        self.select();previous=self.bridge.context.context_id
        with self.assertRaisesRegex(ValueError,"EXISTING_UNITY_PROJECT"):self.bridge.call("context_select",{"project":str(self.base/"not-project")})
        self.assertEqual(previous,self.bridge.context.context_id)

if __name__=="__main__":
    unittest.main(verbosity=2)
