using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed class WorkbenchOptimizationWindow : EditorWindow
    {
        [SerializeField] GameObject target;
        [SerializeField] string note = "在保留当前外观和全部原有开关的前提下，优化这个角色的占用。";
        [SerializeField] bool measure = true, selectedTextures;
        Label workflow, connection, status, hostNote;
        Button send, check;
        string DraftPath => Path.Combine(Path.GetDirectoryName(WorkbenchData.WindowFile), "optimization-draft.json");

        [MenuItem("Tools/改模工作台 · 优化性能", false, 2101)]
        public static void Open()
        {
            var window = GetWindow<WorkbenchOptimizationWindow>();
            window.titleContent = new GUIContent("成品优化");
            window.Show(); window.Focus();
        }

        void OnEnable()
        {
            minSize = new Vector2(560, 410);
            if (File.Exists(DraftPath))
                try { var draft = WorkbenchData.Read(DraftPath); note = WorkbenchData.Text(draft["message"], note); measure = (bool?)draft["allow_sdk_build"] ?? true; selectedTextures = (bool?)draft["selected_textures"] ?? false; }
                catch (Exception) { /* Keep serialized draft; never overwrite an unreadable file. */ }
            if (!target) target = Resources.FindObjectsOfTypeAll<WorkbenchWindow>().FirstOrDefault()?.WorkflowTarget;
            if (!target) target = SelectedAvatar();
        }

        static GameObject SelectedAvatar()
        {
            var selected = Selection.activeGameObject;
            for (var t = selected ? selected.transform : null; t; t = t.parent)
                if (t.GetComponents<Component>().Any(c => c && c.GetType().Name == "VRCAvatarDescriptor")) return t.gameObject;
            return selected && selected.GetComponentsInChildren<Renderer>(true).Any() ? selected : null;
        }

        void OnDisable() => SaveDraft();
        void SaveDraft()
        {
            try
            {
                if (File.Exists(DraftPath)) WorkbenchData.Read(DraftPath); // Preserve unreadable existing drafts.
                WorkbenchData.Write(DraftPath, new JObject { ["message"] = note, ["allow_sdk_build"] = measure, ["selected_textures"] = selectedTextures });
            }
            catch (Exception e) { if (status != null) status.text = "优化草稿未保存：" + e.Message; }
        }

        Label Text(string value)
        {
            var label = new Label(value) { enableRichText = false };
            label.style.whiteSpace = WhiteSpace.Normal; label.style.flexShrink = 0; label.style.marginBottom = 8;
            return label;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); WorkbenchTheme.Apply(this, "aw-optimization-window");
            root.style.paddingLeft = root.style.paddingRight = 16;
            root.style.paddingTop = root.style.paddingBottom = 12;
            var heading = Text("成品优化"); heading.AddToClassList("aw-window-title"); root.Add(heading);
            hostNote = Text(""); hostNote.name = "optimization-host"; root.Add(hostNote);
            var picker = new ObjectField("目标角色") { objectType = typeof(GameObject), allowSceneObjects = true, value = target, name = "optimization-target" };
            picker.RegisterValueChangedCallback(e => { target = e.newValue as GameObject; RefreshInfo(); }); root.Add(picker);
            var selection = new Button(() => { target = SelectedAvatar(); picker.SetValueWithoutNotify(target); RefreshInfo(); }) { text = "使用选中角色", name = "optimization-use-selection" }; root.Add(selection);
            workflow = Text(""); workflow.name = "optimization-workflow"; root.Add(workflow);
            connection = Text(""); root.Add(connection);
            var build = new Toggle("允许最终 SDK 本地测量") { value = measure, tooltip = "只测量最终候选的实际占用，不启动客户端或上传。" };
            build.RegisterValueChangedCallback(e => { measure = e.newValue; SaveDraft(); }); root.Add(build);
            var textures = new Toggle("按画质检查选定贴图副本") { value = selectedTextures, tooltip = "默认保留贴图尺寸。启用后仍保护脸、身体和主要头发，不统一降低全部贴图。" };
            textures.RegisterValueChangedCallback(e => { selectedTextures = e.newValue; SaveDraft(); }); root.Add(textures);
            var input = new TextField("补充要求") { multiline = true, maxLength = 5000, value = note, name = "optimization-note" };
            input.style.minHeight = 62; input.style.flexGrow = 1;
            input.RegisterValueChangedCallback(e => { note = e.newValue; SaveDraft(); RefreshInfo(); }); root.Add(input);
            root.Add(Text("发送后在“看回复”查看读取和处理状态。请求和优化结果分别记录。"));
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexShrink = 0;
            check = new Button(() => Submit(true)) { text = "只检查接入，不改模型", name = "optimization-check" }; check.style.flexGrow = 1; actions.Add(check);
            send = new Button(() => Submit(false)) { text = "发送优化请求", name = "optimization-send" }; send.AddToClassList("aw-primary"); send.style.flexGrow = 1; actions.Add(send); root.Add(actions);
            var refresh = new Button(RefreshInfo) { text = "刷新接入状态", name = "optimization-refresh" }; root.Add(refresh);
            status = Text(""); status.name = "optimization-status"; root.Add(status); RefreshInfo();
        }

        void RefreshInfo()
        {
            if (workflow == null) return;
            bool dsh = WorkbenchModels.ExecutionHost == WorkbenchModels.DshHost;
            var info = WorkbenchWorkflow.Describe();
            bool ready = (bool?)info["ready"] == true;
            if (hostNote != null) hostNote.text = dsh
                ? "保留外观与原有开关，交给当前 DSH 执行：只写入同一任务收件箱，不自动唤醒，由 DSH 用自己包内的 assemble-vrchat-avatar Skill 处理。"
                : "保留外观与原有开关，交给当前 Codex 的 assemble-vrchat-avatar 改模流程执行。";
            workflow.text = "工作台 " + WorkbenchWorkflow.PluginVersion + " · 执行宿主 " + (dsh ? "当前 DSH" : "当前 Codex")
                + " · 原改模工作流 " + WorkbenchData.Text(info["version"], "版本未知") + "\n" + (ready ? "成品优化步骤已接入" : WorkbenchData.Text(info["error"], "尚未接入"));
            workflow.tooltip = WorkbenchData.Text(info["path"]);
            connection.text = Resources.FindObjectsOfTypeAll<WorkbenchWindow>().FirstOrDefault()?.WorkflowConnection
                ?? (dsh ? "请求发到当前 DSH 的同一任务收件箱；不自动唤醒，由 DSH 读取。" : "请求发到当前工作台绑定的 Codex；未连接时保存并提示接入。");
            check?.SetEnabled(ready && target);
            send?.SetEnabled(ready && target && !string.IsNullOrWhiteSpace(note));
        }

        void Submit(bool checkOnly)
        {
            try
            {
                SaveDraft();
                WorkbenchWindow.Open();
                var workbench = Resources.FindObjectsOfTypeAll<WorkbenchWindow>().First();
                string original = checkOnly ? "确认收到优化入口标记，暂不修改模型。只核对 Tools、工作台版本和原改模工作流接入；禁止构建、改模或上传。" : note;
                var entry = workbench.SubmitWorkflowRequest(target, original, measure, selectedTextures, checkOnly);
                status.text = WorkbenchModels.ExecutionHost == WorkbenchModels.DshHost
                    ? "已保存到 DSH 收件箱，等待 DSH 读取（不自动唤醒）。发送情况到工作台“看回复”查看。"
                    : "已保存，等待 Codex 读取。发送情况到工作台“看回复”查看。";
                status.tooltip = (string)entry["id"];
            }
            catch (Exception e) { status.text = "尚未提交：" + e.Message; }
            RefreshInfo();
        }
    }
}
