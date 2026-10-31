"""Installation contracts against isolated Chinese/space/& paths; never a real profile."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import tomllib
import unittest

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import install
from check_config import check

class DistributionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='改模 空格 & ')
        self.addCleanup(self.temp.cleanup)
        self.base=Path(self.temp.name).resolve()
        self.root=self.base/'工具包'
        self.root.mkdir()
        # Only the writable package-local pointer is redirected; all payloads are read-only source.
        self.project=self.base/'Unity 中文 & 项目'
        (self.project/'Assets').mkdir(parents=True)
        (self.project/'ProjectSettings').mkdir()
        (self.project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.22f1\n')
        self.profile=self.base/'profile'
        self.profile.mkdir()
        self.storage=self.base/'storage'
        self.args=install.parser().parse_args(['--project',str(self.project),'--codex-home',str(self.profile),
            '--user-skills',str(self.base/'user-skills'),'--storage-root',str(self.storage)])
        self.original_pointer=(ROOT/'local/last-install.json').read_bytes() if (ROOT/'local/last-install.json').exists() else None
    def plan(self):
        files,report=install.plan(self.args)
        # Preserve the production source pointer even when the tests apply a simulated installation.
        files={self.root/'local/last-install.json' if p==ROOT/'local/last-install.json' else p:b for p,b in files.items()}
        files={p:b for p,b in files.items() if not p.is_file() or p.read_bytes()!=b}
        return files,report
    def apply(self):
        files,report=self.plan()
        return install.apply_files(files,report,self.root)
    def put(self,path,data):
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_bytes(data)
    def test_preview_has_no_writes(self):
        before=set(self.base.rglob('*'))
        files,report=self.plan()
        self.assertGreater(len(files),100)
        self.assertEqual('PREVIEW_ONLY',report['status'])
        self.assertEqual(before,set(self.base.rglob('*')))
    def test_apply_and_repeat_are_idempotent(self):
        first=self.apply()
        self.assertEqual('INSTALLED_NOT_UNITY_VERIFIED',first['status'])
        again=self.apply()
        self.assertEqual('UNCHANGED',again['status'])
        self.assertEqual('PASS',check(first['config'])['status'])
    def test_unrelated_toml_comments_permissions_models_preserved(self):
        path=self.project/'.codex/config.toml'
        original=b'# Keep this comment\r\nmodel = "user-choice"\r\napproval_policy = "on-request"\r\n[mcp_servers.myCustom]\r\ncommand = "example"\r\n'
        self.put(path,original)
        self.apply()
        self.assertTrue(path.read_bytes().startswith(original))
        value=tomllib.loads(path.read_text())
        self.assertEqual('user-choice',value['model'])
        self.assertEqual('on-request',value['approval_policy'])
        self.assertEqual(3,len(value['mcp_servers']))
    def test_existing_api_and_dsh_settings_preserved(self):
        editor=self.storage/'editor'/install.sha(str(self.project).encode())[:20]
        path=editor/'model-services.json'
        before={'active':'dsh','profiles':[{'name':'custom','key':'fixture-only'}],'dsh_skill_path':'existing','unknown':True}
        self.put(path,install.encoded(before)); self.apply()
        after=install.read(path)
        for key,value in before.items(): self.assertEqual(value,after[key])
    def test_existing_meta_and_unknown_asset_preserved(self):
        base=self.project/'Assets/AvatarWorkbench'
        self.put(base/'Editor/WorkbenchModels.cs.meta',b'guid: user-preserved-guid\n')
        self.put(base/'User Notes.txt',b'my file')
        self.apply()
        self.assertEqual(b'guid: user-preserved-guid\n',(base/'Editor/WorkbenchModels.cs.meta').read_bytes())
        self.assertEqual(b'my file',(base/'User Notes.txt').read_bytes())
    def test_local_code_edit_refused_without_writes(self):
        path=self.project/'Assets/AvatarWorkbench/Editor/WorkbenchModels.cs'
        self.put(path,b'local custom code')
        with self.assertRaisesRegex(ValueError,'LOCAL_EDIT_CONFLICT'): self.plan()
        self.assertFalse((self.project/'.codex/config.toml').exists())
    def test_malformed_toml_does_not_echo_secret(self):
        self.put(self.project/'.codex/config.toml',b'secret-example=')
        with self.assertRaises(ValueError) as error: self.plan()
        self.assertNotIn('secret-example',str(error.exception))
        self.assertIn('INVALID_TOML',str(error.exception))
    def test_other_global_unity_mcp_kept_and_ours_added_beside_it(self):
        path=self.profile/'config.toml'
        for before in (b'[mcp_servers.unityMCP]\ncommand="unknown"\nargs=[]\n', b'[mcp_servers.unityMCP]\nurl="http://127.0.0.1:8080/mcp"\n'):
            with self.subTest(existing=before):
                self.put(path,before)
                report=self.apply()
                text=(self.project/'.codex/config.toml').read_text()
                self.assertIn('[mcp_servers.unityMCPVRC]',text)
                self.assertNotIn('[mcp_servers.unityMCP]',text)
                self.assertEqual(before,path.read_bytes())
                self.assertIn(self.apply()['status'],('UNCHANGED','INSTALLED_NOT_UNITY_VERIFIED'))
                self.assertEqual('PASS',check(report['config'])['status'])
    def test_conflicting_global_workbench_mcp_refused(self):
        path=self.profile/'config.toml'
        before=b'[mcp_servers.avatarWorkbench]\ncommand="unknown"\nargs=["workbench_mcp.py"]\n'
        self.put(path,before)
        with self.assertRaisesRegex(ValueError,'MCP_CONFLICT'): self.plan()
        self.assertEqual(before,path.read_bytes())
    def test_compatible_global_unity_mcp_reused(self):
        row={'command':str(ROOT/'runtime/node/node.exe'),'args':[str(ROOT/'runtime/unity-mcp/build/index.js')]}
        data='[mcp_servers.existingUnity]\n'+''.join(k+' = '+json.dumps(v)+'\n' for k,v in row.items())
        self.put(self.profile/'config.toml',data.encode())
        report=self.apply()
        self.assertIn('unityMCP',report['reused_mcp'])
        self.assertNotIn('[mcp_servers.unityMCP]',(self.project/'.codex/config.toml').read_text())
        self.assertEqual('PASS',check(report['config'])['status'])
    def test_duplicate_mcp_rejected(self):
        self.put(self.profile/'config.toml',b'[mcp_servers.unityMCP]\ncommand="unknown"\n')
        self.put(self.project/'.codex/config.toml',b'[mcp_servers.unityMCP]\ncommand="unknown"\n')
        with self.assertRaisesRegex(ValueError,'DUPLICATE_MCP'): self.plan()
    def test_existing_skill_preserved_and_selected(self):
        skill=self.args.user_skills/'assemble-vrchat-avatar/SKILL.md'
        content=b'---\nname: assemble-vrchat-avatar\n---\nUser customized workflow; references/optimization.md\n'
        self.put(skill,content)
        self.put(skill.parent/'references/optimization.md',b'custom reference')
        self.put(skill.parent/'scripts/optimization.py',b'# custom helper')
        report=self.apply()
        self.assertEqual('EXISTING_REUSED',report['skill_mode'])
        self.assertEqual(str(skill),report['skill'])
        self.assertFalse((self.project/'.agents/skills/assemble-vrchat-avatar/SKILL.md').exists())
        self.assertEqual(content,skill.read_bytes())
    def test_older_global_skill_gaps_reported_not_patched(self):
        # 2026-10-31: the reused global Skill predated finish-check.md, and nothing said so.
        skill=self.profile/'skills/assemble-vrchat-avatar/SKILL.md'
        content=b'---\nname: assemble-vrchat-avatar\n---\nOlder workflow; references/optimization.md\n'
        self.put(skill,content)
        self.put(skill.parent/'references/optimization.md',b'old reference')
        self.put(skill.parent/'scripts/optimization.py',b'# old helper')
        self.put(skill.parent/'VERSION.json',b'{"version": "0.3.2-local1"}')
        before={p:p.read_bytes() for p in skill.parent.rglob('*') if p.is_file()}
        report=self.apply()
        self.assertEqual('EXISTING_REUSED',report['skill_mode'])
        self.assertEqual('0.3.2-local1',report['skill_version'])
        self.assertIn('references/finish-check.md',report['skill_missing_files'])
        self.assertIn('Assets/AvatarWorkbench/Tools/finish-check.md',report['skill_note'])
        self.assertTrue((self.project/'Assets/AvatarWorkbench/Tools/finish-check.md').is_file())
        self.assertEqual(before,{p:p.read_bytes() for p in skill.parent.rglob('*') if p.is_file()})
    def test_managed_block_user_edit_refused(self):
        self.apply()
        path=self.project/'.codex/config.toml'
        path.write_text(path.read_text().replace('tool_timeout_sec = 300','tool_timeout_sec = 450'),encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'MANAGED_MCP_EDITED'): self.plan()
    def test_doctor_detects_changed_registration(self):
        report=self.apply()
        path=self.project/'.codex/config.toml'
        path.write_text(path.read_text().replace('startup_timeout_sec = 30','enabled = false\nstartup_timeout_sec = 30'),encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'MCP_DISABLED'): check(report['config'])
    def test_invalid_project_refused(self):
        self.args.project=self.base/'not-a-project'
        with self.assertRaisesRegex(ValueError,'UNITY_PROJECT_REQUIRED'): self.plan()
    def test_path_escape_refused(self):
        with self.assertRaisesRegex(ValueError,'UNSAFE_INSTALL_PATH'): install.owned_path(self.project,'../outside')
    def test_rollback_restores_exact_original_bytes(self):
        cfg=self.project/'.codex/config.toml'
        original=b'\xef\xbb\xbf# original\r\nmodel="kept"\r\n'
        self.put(cfg,original)
        report=self.apply()
        result=subprocess.run([sys.executable,str(ROOT/'tools/rollback.py'),'--receipt',report['rollback'],'--apply'],capture_output=True,text=True)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual(original,cfg.read_bytes())
        self.assertFalse((self.project/'Assets/AvatarWorkbench/Editor/WorkbenchModels.cs').exists())
    def test_rollback_refuses_later_edits_without_partial_restore(self):
        report=self.apply()
        cfg=self.project/'.codex/config.toml'
        cfg.write_text(cfg.read_text()+'# user later change\n',encoding='utf-8')
        before=cfg.read_bytes()
        result=subprocess.run([sys.executable,str(ROOT/'tools/rollback.py'),'--receipt',report['rollback'],'--apply'],capture_output=True,text=True)
        self.assertEqual(2,result.returncode)
        self.assertEqual(before,cfg.read_bytes())
        self.assertTrue((self.project/'Assets/AvatarWorkbench/Editor/WorkbenchModels.cs').exists())

if __name__=='__main__': unittest.main(verbosity=2)
