using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    internal sealed class WorkbenchModelSettingsWindow : EditorWindow
    {
        string profileId = "";
        CancellationTokenSource lifetime;
        TextField key;
        [MenuItem("Tools/改模工作台/API 与模型设置")]
        internal static void Open()
        {
            var window = GetWindow<WorkbenchModelSettingsWindow>(true, "API 与模型设置", true);
            window.minSize = new Vector2(570, 540); window.Show();
        }
        void OnDisable() { lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; if (key != null) key.value = ""; }
        void CreateGUI()
        {
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = new CancellationTokenSource();
            var root = rootVisualElement; root.Clear(); WorkbenchTheme.Apply(this, "aw-settings-window");
            var title = new Label("模型与 API"); title.AddToClassList("aw-window-title"); title.style.fontSize = 20; root.Add(title);
            var list = WorkbenchModels.Profiles(); var names = new List<string> { "＋ 新建接口配置" }; names.AddRange(list.Select(x => (string)x["name"] + " · " + (string)x["model"]));
            int index = list.FindIndex(x => (string)x["id"] == profileId) + 1;
            var choices = new PopupField<string>("接口配置", names, index); choices.name = "model-profile-picker"; root.Add(choices);
            choices.RegisterValueChangedCallback(e => { profileId = choices.index == 0 ? "" : (string)list[choices.index - 1]["id"]; CreateGUI(); });
            var p = WorkbenchModels.Find(profileId) ?? new JObject { ["name"] = "我的接口", ["base_url"] = "https://api.deepseek.com", ["protocol"] = "openai", ["model"] = "" };
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1; root.Add(scroll);
            var preset = new PopupField<string>("地址预设", new List<string> { "保持当前 / 自定义", "DeepSeek", "OpenAI 兼容", "Anthropic 兼容", "本机服务" }, 0); scroll.Add(preset);
            var name = new TextField("显示名称") { value = (string)p["name"], name = "profile-name" }; scroll.Add(name);
            var wire = new PopupField<string>("接口协议", new List<string> { "OpenAI 兼容", "Anthropic 兼容" }, (string)p["protocol"] == "anthropic" ? 1 : 0); scroll.Add(wire);
            var endpoint = new TextField("API 根地址") { value = (string)p["base_url"], name = "profile-endpoint" }; scroll.Add(endpoint);
            var model = new TextField("模型名称") { value = (string)p["model"], name = "profile-model" }; scroll.Add(model);
            key = new TextField("API Key") { isPasswordField = true, name = "profile-api-key" }; scroll.Add(key);
            var remember = new Toggle("使用当前 Windows 账号加密记住密钥") { value = !string.IsNullOrEmpty((string)p["protected_key"]) }; scroll.Add(remember);
            var scope = new Label("此接口收到本次原话、目标/候选、选定素材与圈选元数据和现有 Skill。不会上传素材文件，也不会替你操作 Unity。API 的建议可继续交给原改模流程执行。\n密钥只发往填写的端点；不跟随重定向，不写入任务、反馈、截图或公开包。"); scope.style.whiteSpace = WhiteSpace.Normal; scope.style.marginTop = 10; scope.style.marginBottom = 8; scroll.Add(scope);
            var textConsent = new Toggle("允许发送本次需求与选定上下文") { value = (bool?)p["text_consent"] == true }; scroll.Add(textConsent);
            var images = new Toggle("该模型支持识图") { value = (bool?)p["supports_images"] == true }; scroll.Add(images);
            var imageConsent = new Toggle("允许把我冻结的原图发给该接口") { value = (bool?)p["image_consent"] == true }; scroll.Add(imageConsent);
            var skill = new TextField("现有改模 Skill") { value = (string)WorkbenchModels.Read()["skill_path"], name = "workflow-skill-path" }; scroll.Add(skill);
            var skillPicker = new Button(() => { string file = EditorUtility.OpenFilePanel("选择已安装的 assemble-vrchat-avatar/SKILL.md", Path.GetDirectoryName(skill.value), "md"); if (file.Length > 0) skill.value = file; }) { text = "选择已有 Skill 文件" }; scroll.Add(skillPicker);
            var status = new Label("新接口需要填写自己的模型和密钥；列表不会凭名称猜测识图能力。留空密钥只在同一地址、协议和配置下保留原密钥。"); status.style.whiteSpace = WhiteSpace.Normal; status.style.marginTop = 8; scroll.Add(status);
            var found = new PopupField<string>("可用模型", new List<string> { "点击读取模型列表" }, 0); scroll.Add(found);
            JObject Profile() => new JObject { ["id"] = profileId, ["name"] = name.value, ["base_url"] = endpoint.value, ["protocol"] = wire.index == 0 ? "openai" : "anthropic", ["model"] = model.value,
                ["text_consent"] = textConsent.value, ["supports_images"] = images.value, ["image_consent"] = imageConsent.value };
            var fetch = new Button { text = "读取真实模型列表", name = "fetch-api-models" }; scroll.Add(fetch);
            fetch.clicked += async () =>
            {
                var run = lifetime;
                try
                {
                    if (!textConsent.value) throw new InvalidOperationException("先确认将请求发送到此接口。");
                    var query = Profile(); query["base_url"] = WorkbenchModels.Endpoint(endpoint.value);
                    string token = key.value;
                    var old = WorkbenchModels.Find(profileId);
                    if (token.Length == 0 && old != null && (string)old["base_url"] == (string)query["base_url"] && (string)old["protocol"] == (string)query["protocol"]) token = WorkbenchModels.Key(old);
                    if (token.Length == 0 && !new Uri((string)query["base_url"]).IsLoopback) throw new InvalidOperationException("请先填写这个地址的 API Key。");
                    fetch.SetEnabled(false); status.text = "正在向此接口读取模型列表…";
                    var models = await WorkbenchModels.Models(query, token, run.Token);
                    if (run.IsCancellationRequested) return;
                    found.choices = models; found.SetValueWithoutNotify(models[0]); status.text = "已读取接口实际提供的模型。选择一项后保存；识图支持需由你按服务商说明确认。";
                }
                catch (OperationCanceledException) { if (!run.IsCancellationRequested) status.text = "读取超时，请稍后重试或手填模型名称。"; }
                catch (Exception e) { if (!run.IsCancellationRequested) status.text = e.Message; }
                finally { if (!run.IsCancellationRequested) fetch.SetEnabled(true); }
            };
            found.RegisterValueChangedCallback(e => { if (e.newValue != "点击读取模型列表") model.value = e.newValue; });
            preset.RegisterValueChangedCallback(e =>
            {
                switch (preset.index)
                {
                    case 1: endpoint.value = "https://api.deepseek.com"; wire.index = 0; break;
                    case 2: endpoint.value = "https://api.openai.com/v1"; wire.index = 0; break;
                    case 3: endpoint.value = "https://api.anthropic.com"; wire.index = 1; break;
                    case 4: endpoint.value = "http://127.0.0.1:1234/v1"; wire.index = 0; break;
                }
                key.value = ""; model.value = "";
            });
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexShrink = 0; actions.style.marginTop = 10; root.Add(actions);
            void Save(bool use, bool eye)
            {
                try
                {
                    var profile = Profile(); profileId = WorkbenchModels.Save(profile, key.value, remember.value, skill.value);
                    if (use || eye) WorkbenchModels.Select(profileId, eye);
                    key.value = "";
                    foreach (var w in Resources.FindObjectsOfTypeAll<WorkbenchWindow>()) w.RefreshModelChoice();
                    status.text = eye ? "已选择这个识图模型；只有带原图的 API 请求才会调用它。" : use ? "已切换需求模型。不会重发旧请求，也不会修改工程或 Skill。" : "已保存接口，可在工作台“模型”按钮切换。";
                }
                catch (Exception e) { status.text = e.Message; }
            }
            actions.Add(new Button(() => Save(false, false)) { text = "保存配置", name = "save-model-profile" });
            actions.Add(new Button(() => Save(true, false)) { text = "保存并用于需求", name = "use-model-profile" });
            actions.Add(new Button(() => { if (!images.value || !imageConsent.value) status.text = "先确认识图支持与截图发送范围。"; else Save(false, true); }) { text = "保存并用于识图", name = "use-vision-profile" });
        }
    }
}
