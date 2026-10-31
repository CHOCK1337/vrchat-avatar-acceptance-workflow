using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AvatarWorkbench
{
    internal static class WorkbenchWorkflow
    {
        // Must equal package.json "version" + "-codex" and the payload manifest; verification/test_version.py checks it.
        internal const string PluginVersion = "0.2.16-codex";

        // Read only the selected installed Skill of the CURRENT execution host. No installation,
        // configuration or project scan. DSH never falls back to the global Codex Skill.
        internal static JObject Describe() => Describe(WorkbenchModels.ExecutionHost);
        internal static JObject Describe(string host)
        {
            bool dsh = host == WorkbenchModels.DshHost;
            var result = new JObject { ["plugin_version"] = PluginVersion, ["execution_host"] = host, ["ready"] = false };
            try
            {
                string path = dsh ? WorkbenchModels.DshSkillPath : (string)WorkbenchModels.Read()["skill_path"];
                if (string.IsNullOrWhiteSpace(path)) path = dsh ? "" : WorkbenchModels.DefaultSkill;
                if (dsh && string.IsNullOrWhiteSpace(path)) throw new IOException("尚未指定 DSH 自己的改模 Skill。" + WorkbenchModels.DshSkillHint);
                path = Path.GetFullPath(path);
                if (!File.Exists(path) || new FileInfo(path).Length > 32768) throw new IOException(dsh
                    ? "DSH 包内的改模工作流暂不可读；当前路径：" + path + "。" + WorkbenchModels.DshSkillHint
                    : "原改模工作流暂不可读。请检查 AI 接口页中的现有 Skill 路径。");
                string text = File.ReadAllText(path, Encoding.UTF8);
                string root = Path.GetDirectoryName(path);
                string reference = Path.Combine(root, "references", "optimization.md");
                string helper = Path.Combine(root, "scripts", "optimization.py");
                string versionFile = Path.Combine(root, "VERSION.json");
                result["path"] = path;
                result["sha256"] = WorkbenchData.FileHash(path);
                result["version"] = File.Exists(versionFile) ? WorkbenchData.Read(versionFile)["version"]?.DeepClone() : null;
                result["optimization_reference"] = reference;
                if (!text.Contains("assemble-vrchat-avatar") || !text.Contains("optimization.md") || !File.Exists(reference) || !File.Exists(helper))
                    throw new IOException(dsh ? "DSH 包内选中的工作流还没有成品优化步骤；不会切换到 Codex 的流程或冒充已接入。" : "当前选中的工作流还没有成品优化步骤；不会切换到另一套流程或冒充已接入。");
                result["optimization_sha256"] = WorkbenchData.FileHash(reference);
                result["helper_sha256"] = WorkbenchData.FileHash(helper);
                result["ready"] = true;
            }
            catch (Exception e) { result["error"] = e.Message; }
            return result;
        }

        internal static JObject Request(JObject context, string original, bool measure, bool selectedTextures, bool checkOnly)
        {
            if (context?["target"] == null || string.IsNullOrWhiteSpace((string)context["target"]["global_id"]))
                throw new InvalidOperationException("请先选择要处理的完整角色。");
            if (string.IsNullOrWhiteSpace(original) || original.Length > 5000) throw new ArgumentException("请输入 1–5000 字的需求。");
            var skill = Describe();
            if ((bool?)skill["ready"] != true) throw new IOException((string)skill["error"] ?? "原改模工作流尚未接入。");
            string request = Guid.NewGuid().ToString("N");
            string binding = (string)context["binding"];
            var entry = (JObject)context.DeepClone();
            entry.Remove("binding");
            entry["format"] = "avatar-ui-feedback-v1";
            entry["id"] = WorkbenchData.Hash(Encoding.UTF8.GetBytes(Path.GetFileName(WorkbenchData.Space(binding)) + "|" + request)).Substring(0, 32);
            entry["request_id"] = request;
            entry["created_at"] = WorkbenchData.Now;
            entry["category"] = checkOnly ? "workflow_connection_check" : "optimization";
            entry["message"] = original;
            entry["status"] = "received";
            entry["execution_started"] = false;
            // This is a new text request, never a rebind of an existing frozen screenshot/draft.
            entry["image"] = null; entry["rect"] = null; entry["pixel_rect"] = null;
            // Follows the currently selected execution host instead of assuming Codex.
            entry["model_route"] = WorkbenchModels.HostRoute(WorkbenchModels.ExecutionHost);
            entry["workflow_request"] = new JObject {
                ["format"] = "avatar-workbench-workflow-request-v1", ["operation"] = checkOnly ? "check_connection" : "optimize",
                ["check_only"] = checkOnly, ["execution_host"] = WorkbenchModels.ExecutionHost, ["installed_skill"] = skill,
                ["optimization"] = new JObject { ["mode"] = checkOnly ? "off" : "conservative", ["texture_policy"] = selectedTextures && !checkOnly ? "selected_copies" : "preserve" },
                ["constraints"] = new JObject { ["allow_sdk_build"] = measure && !checkOnly, ["allow_build_and_test"] = false, ["allow_upload"] = false },
                ["submitted_from"] = "unity_tools", ["executed"] = false };
            entry["notice"] = "请求已保存不代表优化已执行。只检查接入时不得修改模型或构建；已回应不代表模型验收通过。";
            return entry;
        }
    }

    public sealed partial class WorkbenchWindow
    {
        internal GameObject WorkflowTarget => target;
        internal string WorkflowConnection
        {
            get
            {
                if (WorkbenchModels.ExecutionHost == WorkbenchModels.DshHost)
                    return "执行宿主：当前 DSH · 请求写入同一任务收件箱，不自动唤醒；DSH 读取后把回复写回同一条。";
                return codexLink != null ? "执行宿主：当前 Codex · 已验证连接：" + WorkbenchData.Text(codexLink["title"]) : "执行宿主：当前 Codex · 发送时验证绑定的会话；未连接时仍保留请求。";
            }
        }

        internal JObject SubmitWorkflowRequest(GameObject chosen, string original, bool measure, bool selectedTextures, bool checkOnly)
        {
            if (IsBusy() || sendingCodex || connectingCodex) throw new InvalidOperationException("Unity 或执行宿主正在处理现场，请保留需求，空闲后再发送。");
            if (!chosen) throw new InvalidOperationException("请先选择完整角色。");
            if (chosen != target) SetTarget(chosen);
            var context = Context();
            var entry = WorkbenchWorkflow.Request(context, original, measure, selectedTextures, checkOnly);
            string binding = (string)context["binding"];
            WorkbenchData.Write(Path.Combine(WorkbenchData.Inbox(binding), (string)entry["id"] + ".json"), entry);
            // The normal composer, frozen image and its original candidate remain intact.
            RenderFeedback(); UpdateLabels();
            SendSavedRequest(binding, entry);
            return entry;
        }
    }
}
