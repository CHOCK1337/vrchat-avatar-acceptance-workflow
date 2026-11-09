using System;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    internal static class WorkbenchBoothSearchSettings
    {
        static string sessionKey, sessionEndpoint;
        static string FilePath => Path.Combine(Path.GetDirectoryName(WorkbenchData.WindowFile), "booth-search.json");
        internal static bool CanRememberKey => Application.platform == RuntimePlatform.WindowsEditor;
        internal static JObject Read()
        {
            if (!File.Exists(FilePath)) return new JObject { ["mode"] = "local", ["base_url"] = "https://api.deepseek.com", ["model"] = "" };
            try { return WorkbenchData.Read(FilePath); }
            catch (Exception) { throw new IOException("搜索设置无法读取，请打开搜索设置重新保存；原任务与反馈未受影响。"); }
        }
        internal static string ModeLabel
        {
            get { try { string mode = (string)Read()["mode"]; return mode == "api" ? "独立 API" : mode == "keyword" ? "原词搜索" : "中文词转换"; } catch { return "设置待检查"; } }
        }
        static JObject ReadForEdit()
        {
            try { return Read(); } catch (IOException) { return new JObject(); }
        }
        static void WriteConfiguration(JObject data)
        {
            if (File.Exists(FilePath))
            {
                try { Read(); }
                catch (IOException) { File.Copy(FilePath, FilePath + ".unreadable." + Guid.NewGuid().ToString("N")); }
            }
            WorkbenchData.Write(FilePath, data);
        }
        internal static bool IsDeepSeek(string address)
        {
            return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        }
        internal static string ValidateEndpoint(string value)
        {
            if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.IsLoopback)
                throw new ArgumentException("请填写无账号、查询参数的 HTTPS API 地址，例如 https://api.deepseek.com。");
            return uri.AbsoluteUri.TrimEnd('/');
        }
        static byte[] Protect(byte[] bytes, string endpoint, bool decrypt)
        {
            if (!CanRememberKey) throw new InvalidOperationException("当前平台仅支持本次 Unity 会话使用密钥。");
            // Resolve the Windows protection API without adding a Unity package or assembly reference.
            var type = Type.GetType("System.Security.Cryptography.ProtectedData, System.Security", false)
                ?? Type.GetType("System.Security.Cryptography.ProtectedData, System.Security.Cryptography.ProtectedData", false);
            if (type == null) throw new InvalidOperationException("当前 Unity 不提供本机密钥保护，请取消记住密钥后保存。");
            var method = type.GetMethod(decrypt ? "Unprotect" : "Protect", BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new InvalidOperationException("本机密钥保护不可用，请仅在本次 Unity 会话中使用。");
            try
            {
                var scope = Enum.Parse(method.GetParameters()[2].ParameterType, "CurrentUser");
                return (byte[])method.Invoke(null, new object[] { bytes, Encoding.UTF8.GetBytes("AvatarWorkbench.Booth:" + endpoint), scope });
            }
            catch (Exception) { throw new InvalidOperationException("无法使用当前 Windows 账号保护或读取密钥，请重新填写，或取消记住密钥。"); }
        }
        static string KeyFor(JObject data, string endpoint)
        {
            if (sessionEndpoint == endpoint && !string.IsNullOrEmpty(sessionKey)) return sessionKey;
            if ((string)data["key_endpoint"] != endpoint || string.IsNullOrEmpty((string)data["protected_key"])) return "";
            try { return Encoding.UTF8.GetString(Protect(Convert.FromBase64String((string)data["protected_key"]), endpoint, true)); }
            catch (FormatException) { throw new InvalidOperationException("保存的密钥无法读取，请重新填写。"); }
        }
        internal static bool HasKey(JObject data)
        {
            return (sessionEndpoint == (string)data["base_url"] && !string.IsNullOrEmpty(sessionKey)) ||
                ((string)data["key_endpoint"] == (string)data["base_url"] && !string.IsNullOrEmpty((string)data["protected_key"]));
        }
        internal static JObject RequestConfiguration()
        {
            var data = Read(); string mode = (string)data["mode"] ?? "local";
            if (mode != "api") return new JObject { ["mode"] = mode == "keyword" ? "keyword" : "local" };
            if ((bool?)data["text_consent"] != true) throw new InvalidOperationException("请先在搜索设置中确认文本发送范围。");
            string endpoint = ValidateEndpoint((string)data["base_url"]), key = KeyFor(data, endpoint);
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未填写搜索 API Key。请打开搜索设置，或切换到无需 API 的中文词转换。");
            return new JObject { ["mode"] = "api", ["base_url"] = endpoint, ["model"] = (string)data["model"] ?? "", ["api_key"] = key };
        }
        internal static void Save(string mode, string endpoint, string model, string key, bool remember, bool consent)
        {
            if (mode != "local" && mode != "api" && mode != "keyword") throw new ArgumentException("搜索方式无效。");
            endpoint = ValidateEndpoint(endpoint); model = (model ?? "").Trim();
            if (model.Length > 200 || model.Contains("\n")) throw new ArgumentException("模型名称过长或包含换行。");
            if (mode == "api" && !consent) throw new ArgumentException("启用独立 API 前，请确认将搜索需求发送到指定接口。");
            var old = ReadForEdit(); key = (key ?? "").Trim();
            if (key.Length == 0 && (string)old["base_url"] == endpoint)
            {
                try { key = KeyFor(old, endpoint); }
                catch (InvalidOperationException) { if (mode == "api") throw; }
            }
            if (mode == "api" && key.Length == 0) throw new ArgumentException("请填写该接口的 API Key；无需在 Codex 聊天里发送密钥。");
            if (key.Length > 8192 || key.Contains("\n") || key.Contains("\r")) throw new ArgumentException("API Key 格式无效。");
            var saved = new JObject { ["mode"] = mode, ["base_url"] = endpoint, ["model"] = model, ["text_consent"] = consent };
            if (remember && key.Length > 0)
            {
                saved["protected_key"] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(key), endpoint, false));
                saved["key_endpoint"] = endpoint;
            }
            WriteConfiguration(saved);
            sessionKey = key; sessionEndpoint = endpoint;
        }
        internal static void ClearKey()
        {
            var data = ReadForEdit(); data.Remove("protected_key"); data.Remove("key_endpoint");
            data["mode"] = "local"; data["text_consent"] = false;
            WriteConfiguration(data); sessionKey = null; sessionEndpoint = null;
        }
        internal static void Open() => WorkbenchBoothSearchSettingsWindow.ShowSettings();
    }

    internal sealed class WorkbenchBoothSearchSettingsWindow : EditorWindow
    {
        static readonly string[] Modes = { "常用中文词（无需 API）", "智能中文需求（自选 API）", "原词搜索（日文／商品名）" };
        TextField keyField;
        internal static void ShowSettings()
        {
            var window = GetWindow<WorkbenchBoothSearchSettingsWindow>(true, "BOOTH 搜索设置", true);
            window.minSize = new Vector2(530, 540); window.Show();
        }
        void CreateGUI()
        {
            var root = rootVisualElement; root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 18; root.style.paddingTop = root.style.paddingBottom = 14;
            var title = new Label("中文找素材，不必通过 Codex"); title.style.fontSize = 20; title.style.unityFontStyleAndWeight = FontStyle.Bold; root.Add(title);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; root.Add(scroll);
            var status = new Label(); status.style.whiteSpace = WhiteSpace.Normal; status.style.marginTop = 10;
            JObject data;
            try { data = WorkbenchBoothSearchSettings.Read(); }
            catch (Exception e) { data = new JObject(); status.text = e.Message; }
            var mode = new PopupField<string>("搜索方式", new System.Collections.Generic.List<string>(Modes), (string)data["mode"] == "api" ? 1 : (string)data["mode"] == "keyword" ? 2 : 0); scroll.Add(mode);
            var explanation = new Label("常用词转换支持双马尾、短发、裙子等；复杂描述使用你自己的 API 转成日文检索词。模型只规划关键词，商品仍从 BOOTH 实际查询。"); explanation.style.whiteSpace = WhiteSpace.Normal; explanation.style.marginTop = explanation.style.marginBottom = 10; scroll.Add(explanation);
            var provider = new PopupField<string>("接口预设", new System.Collections.Generic.List<string> { "DeepSeek", "自定义兼容接口" }, WorkbenchBoothSearchSettings.IsDeepSeek((string)data["base_url"] ?? "https://api.deepseek.com") ? 0 : 1); scroll.Add(provider);
            var endpoint = new TextField("API 地址") { value = (string)data["base_url"] ?? "https://api.deepseek.com" }; scroll.Add(endpoint);
            var model = new TextField("模型名称（可留空）") { value = (string)data["model"] ?? "" }; model.tooltip = "留空时由上游适配尝试发现可用模型；接口不提供模型列表时填写服务商给出的文本模型名称。"; scroll.Add(model);
            keyField = new TextField("API Key") { isPasswordField = true }; scroll.Add(keyField);
            var keyState = new Label(WorkbenchBoothSearchSettings.HasKey(data) ? "此端点已有密钥；留空可保留。切换地址后需要重新填写。" : "尚未配置密钥。密钥仅用于你指定的搜索接口。"); keyState.style.whiteSpace = WhiteSpace.Normal; scroll.Add(keyState);
            var remember = new Toggle("用当前 Windows 账号加密保存在本机") { value = !string.IsNullOrEmpty((string)data["protected_key"]) }; remember.SetEnabled(WorkbenchBoothSearchSettings.CanRememberKey); scroll.Add(remember);
            var lifetime = new Label("不记住密钥时，仅保留到下一次脚本重载；重编译或进入运行模式后可能需要重新填写。"); lifetime.style.whiteSpace = WhiteSpace.Normal; scroll.Add(lifetime);
            var consent = new Toggle("允许把本次搜索需求发送到上述 API") { value = (bool?)data["text_consent"] ?? false }; scroll.Add(consent);
            var notice = new HelpBox("API 模式仅发送输入的搜索需求及关键词规划提示，不发送 Avatar、截图、本地路径或 Codex 聊天记录。费用和数据处理由所选服务商决定。密钥不会写入工程、任务、反馈或插件包。保存设置不代表接口已验证成功。", HelpBoxMessageType.Info); scroll.Add(notice);
            var doc = new Button(() => Application.OpenURL("https://api-docs.deepseek.com/")) { text = "打开 DeepSeek 官方 API 文档" }; scroll.Add(doc);
            provider.RegisterValueChangedCallback(e => { if (e.newValue == "DeepSeek") { endpoint.value = "https://api.deepseek.com"; model.value = ""; } });
            endpoint.RegisterValueChangedCallback(e => { keyField.SetValueWithoutNotify(""); consent.value = false; keyState.text = "地址已更改；请核对服务商并填写对应密钥。"; });
            root.Add(status);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.marginTop = 12;
            var save = new Button(() => {
                try {
                    WorkbenchBoothSearchSettings.Save(mode.index == 1 ? "api" : mode.index == 2 ? "keyword" : "local", endpoint.value, model.value, keyField.value, remember.value, consent.value);
                    keyField.SetValueWithoutNotify(""); status.text = "已保存。回到 BOOTH 页输入中文并点击搜索；无需 Codex 连接。";
                } catch (Exception e) { status.text = e.Message; }
            }) { text = "保存搜索设置", name = "booth-save-search-settings" }; save.style.flexGrow = 1; actions.Add(save);
            actions.Add(new Button(() => { try { WorkbenchBoothSearchSettings.ClearKey(); keyField.SetValueWithoutNotify(""); mode.index = 0; consent.value = false; remember.value = false; keyState.text = "尚未配置密钥。"; status.text = "本机密钥已清除，已恢复常用中文词转换。"; } catch (Exception e) { status.text = e.Message; } }) { text = "清除密钥" });
            actions.Add(new Button(Close) { text = "返回" }); root.Add(actions);
        }
        void OnDisable() { if (keyField != null) keyField.SetValueWithoutNotify(""); }
    }
}
