"""Isolated installation regressions. No live DSH profile or avatar writes."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

SOURCE=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('distribution_install',SOURCE/'tools/install.py')
installer=importlib.util.module_from_spec(spec);spec.loader.exec_module(installer)

class DistributionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='aw-distribution-')
        base=Path(self.temp.name)/'中文 空格 & 新机器'
        self.root=base/'tool'
        self.project=base/'Avatar project'
        self.home=base/'user/.dsh'
        self.storage=base/'storage'
        for name in ('unity','dependencies/UnityMCP-VRC/UnityMCPPlugin','runtime/setup','tools'):
            shutil.copytree(SOURCE/name,self.root/name,ignore=shutil.ignore_patterns('__pycache__'))
        (self.project/'Assets').mkdir(parents=True)
        (self.project/'ProjectSettings').mkdir()
        (self.project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.22f1')
        self.patch=self.home/'profiles/desktop/cordis.patch.yml'
        self.patch.parent.mkdir(parents=True)
        self.original=b'\xef\xbb\xbf# retain this exact CRLF comment\r\n- id: agent-default-model\r\n  config:\r\n    model: my-model\r\n- id: user-policy\r\n  config:\r\n    mode: workspace-write\r\n'
        self.patch.write_bytes(self.original)
        self.credential=self.home/'.credentials.yaml'
        self.credential.write_text('test-fixture-only-do-not-copy')
        self.args=installer.parser().parse_args(['--project',str(self.project),'--dsh-home',str(self.home),
          '--storage-root',str(self.storage),'--node',str(SOURCE/'runtime/node/node.exe'),
          '--unity-mcp',str(SOURCE/'runtime/unity-mcp/build/index.js')])
    def tearDown(self):
        self.assertEqual(self.credential.read_text(),'test-fixture-only-do-not-copy')
        self.temp.cleanup()
    def plan(self): return installer.plan(self.args,self.root)
    def install(self):
        files,report=self.plan()
        return installer.apply_files(files,report,self.root)
    def test_preview_is_read_only_and_generates_portable_paths(self):
        files,report=self.plan()
        self.assertEqual(self.patch.read_bytes(),self.original)
        self.assertFalse((self.root/'local').exists())
        cfg=json.loads(files[self.root/'local/connection.json'])
        self.assertEqual(cfg['package_root'],str(self.root))
        self.assertEqual(report['unity_mcp'],'EMBEDDED_PACKAGE_WILL_BE_INSTALLED')
        self.assertFalse(any('.credentials' in str(p) for p in files))
    def test_apply_preserves_unowned_profile_bytes_and_is_idempotent(self):
        first=self.install()
        self.assertEqual(first['status'],'INSTALLED_NOT_UNITY_VERIFIED')
        self.assertTrue(self.patch.read_bytes().startswith(self.original))
        files,report=self.plan()
        self.assertEqual(files,{})
        self.assertEqual(report['unity_mcp'],'EXISTING_REUSED')
    def test_preserves_unknown_unity_files_existing_guid_and_model_profiles(self):
        target=self.project/'Assets/AvatarWorkbench'
        target.mkdir()
        (target/'user-note.txt').write_text('keep me')
        (target/'Editor.meta').write_text('guid: user-guid')
        editor=self.storage/'editor'/installer.sha(str(self.project).encode())[:20]
        editor.mkdir(parents=True)
        settings=editor/'model-services.json'
        settings.write_text(json.dumps({'active':'codex','profiles':[{'id':'own','name':'mine'}],'extra':42}))
        self.install()
        self.assertEqual((target/'user-note.txt').read_text(),'keep me')
        self.assertEqual((target/'Editor.meta').read_text(),'guid: user-guid')
        saved=json.loads(settings.read_text(encoding='utf-8'))
        self.assertEqual(saved['active'],'codex');self.assertEqual(saved['extra'],42)
        self.assertEqual(saved['profiles'],[{'id':'own','name':'mine'}])
    def test_local_code_conflict_does_not_modify_profile(self):
        target=self.project/'Assets/AvatarWorkbench/Editor/WorkbenchWindow.cs'
        target.parent.mkdir(parents=True);target.write_text('// user code')
        with self.assertRaisesRegex(ValueError,'LOCAL_EDIT_CONFLICT'): self.plan()
        self.assertEqual(self.patch.read_bytes(),self.original)
        self.assertFalse((self.root/'local').exists())
    def test_duplicate_mcp_namespace_stops_without_private_yaml_output(self):
        duplicate=b'\n- id: external-unity\n  name: "@deepseek-ai/dsh-mcp-client"\n  config:\n    serverName: unityMCP\n'
        self.patch.write_bytes(self.original+duplicate)
        with self.assertRaisesRegex(ValueError,'EXISTING_MCP_NAMESPACE'): self.plan()
        self.assertEqual(self.patch.read_bytes(),self.original+duplicate)
    def test_malformed_yaml_diagnostic_redacts_content(self):
        self.patch.write_text('- private-key: [PRIVATE_TEST_VALUE\n')
        with self.assertRaises(ValueError) as caught: self.plan()
        self.assertNotIn('PRIVATE_TEST_VALUE',str(caught.exception))
    def test_rollback_restores_exact_original_profile(self):
        result=self.install()
        command=[sys.executable,str(self.root/'tools/rollback.py'),'--receipt',str(Path(result['rollback'])/'restore.json')]
        p=subprocess.run(command+['--apply'],capture_output=True,text=True,encoding='utf-8')
        self.assertEqual(p.returncode,0,p.stderr)
        self.assertEqual(self.patch.read_bytes(),self.original)
        self.assertFalse((self.root/'local/connection.json').exists())
    def test_rollback_rejects_later_edits_before_any_write(self):
        result=self.install()
        self.patch.write_bytes(self.patch.read_bytes()+b'# later change\n')
        current=self.patch.read_bytes()
        p=subprocess.run([sys.executable,str(self.root/'tools/rollback.py'),'--receipt',str(Path(result['rollback'])/'restore.json'),'--apply'],capture_output=True,text=True,encoding='utf-8')
        self.assertEqual(p.returncode,2)
        self.assertIn('LATER_EDIT_DETECTED',p.stderr)
        self.assertEqual(self.patch.read_bytes(),current)
        self.assertTrue((self.root/'local/connection.json').exists())
    def test_invalid_profile_path_rejected(self):
        self.args.profile='../other'
        with self.assertRaisesRegex(ValueError,'INVALID_DSH_PROFILE_NAME'): self.plan()

if __name__=='__main__': unittest.main()
