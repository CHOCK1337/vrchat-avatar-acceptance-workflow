using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarWorkbench
{
    internal sealed class ResourceCard
    {
        public string id, name, kind, path, state, note, thumbnail;
        public JObject productReference;
        public JObject Json() => new JObject { ["id"] = id, ["name"] = name, ["kind"] = kind,
            ["path"] = path, ["state"] = state, ["note"] = note, ["thumbnail"] = thumbnail,
            ["source_type"] = "local_asset", ["product_reference"] = productReference?.DeepClone() };
    }

    internal static class WorkbenchData
    {
        public static string Project => Directory.GetParent(Application.dataPath).FullName;
        public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvatarWorkbench");
        public static string WindowFile => Path.Combine(DataRoot, "editor", Hash(Encoding.UTF8.GetBytes(Project)).Substring(0, 20), "window.json");
        public static string SelectionBinding => Path.Combine(Path.GetDirectoryName(WindowFile), "selection.json");
        public static string Space(string binding) => Path.Combine(DataRoot, Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(binding))).Substring(0, 20));
        public static string Inbox(string binding) => Path.Combine(Space(binding), "feedback");
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string FileHash(string path) { using (var sha = SHA256.Create()) using (var f = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
        public static string Now => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        public static JObject Read(string path)
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException("记录超过 2 MiB，请选择本次任务的小清单。");
            using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(path, Encoding.UTF8))) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                return JObject.Load(reader);
        }
        public static void Write(string path, JObject value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, value.ToString(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string Text(JToken value, string fallback = "") => value == null || value.Type == JTokenType.Null ? fallback : value.Type == JTokenType.String ? (string)value : value.ToString(Newtonsoft.Json.Formatting.None);
        public static string Resolve(string path, string relativeTo)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile) throw new IOException("只读取本次选定的本地文件。");
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(relativeTo, path));
        }
        public static JObject Candidate(JObject task)
        {
            var value = task["candidate"] ?? task["new_prefab"];
            var c = value is JObject obj ? (JObject)obj.DeepClone() : new JObject();
            if (!(value is JObject) && value != null) c["id"] = Text(value);
            if (c["revision"] == null && task["candidate_revision"] != null) c["revision"] = task["candidate_revision"].DeepClone();
            if (c["sha256"] == null && task["candidate_sha256"] != null) c["sha256"] = task["candidate_sha256"].DeepClone();
            return c;
        }
        public static string Identity(JObject c)
        {
            string id = Text(c?["name"] ?? c?["id"], "未记录候选");
            if (id.StartsWith("GlobalObjectId", StringComparison.Ordinal)) id = "当前编辑快照";
            string sha = Text(c?["sha256"]);
            return Path.GetFileNameWithoutExtension(id) + (c?["revision"] != null ? " · r" + Text(c["revision"]) : "") + (sha.Length >= 10 ? " · " + sha.Substring(0, 10) : "");
        }
        public static List<ResourceCard> Resources(JObject task)
        {
            var list = new List<ResourceCard>();
            foreach (var r in (task["resources"] as JArray ?? new JArray()).OfType<JObject>().Take(300))
                list.Add(new ResourceCard { id = Text(r["id"], "resource-" + list.Count), name = Text(r["name"] ?? r["label"] ?? r["id"], "未命名素材"),
                    kind = Text(r["kind"], "other"), path = Text(r["path"] ?? r["asset_path"] ?? r["source"]),
                    state = Text(r["state"], "未记录"), note = Text(r["note"] ?? r["summary"]), thumbnail = Text(r["thumbnail"]), productReference = r["product_reference"] as JObject });
            return list;
        }
        public static string StateName(string state)
        {
            switch (state) { case "not_started": return "尚未开始"; case "installed": return "已安装（任务记录）";
                case "processing": return "正在处理"; case "excluded": return "已排除"; case "selected": return "待提交安装";
                case "received": return "已保存，等待 Codex 读取"; case "seen": return "Codex 已读";
                case "processing_feedback": return "Codex 处理中"; case "addressed": return "已回应 · 修复未验收";
                case "unknown": return "未记录"; default: return state; }
        }
        // Enumerate metadata only while idle; parse records only when this signature changes.
        public static string FeedbackSignature(string binding)
        {
            string directory = Inbox(binding);
            if (!Directory.Exists(directory)) return "empty";
            return string.Join("|", new DirectoryInfo(directory).EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(300)
                .Select(f => f.Name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks));
        }
        public static List<JObject> Feedback(string binding)
        {
            var result = new List<JObject>(); string dir = Inbox(binding);
            if (!Directory.Exists(dir)) return result;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(300))
                try { result.Add(Read(file)); } catch (IOException) { } catch (Newtonsoft.Json.JsonException) { }
            return result;
        }
        public static JObject Consumer(string binding)
        {
            string path = Path.Combine(Space(binding), "consumer.json");
            if (!File.Exists(path)) return null;
            try { var c = Read(path); if (DateTime.TryParse(Text(c["expires_at"]), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var end) && end > DateTime.UtcNow && (bool?)c["active"] == true) return c; }
            catch (Exception) { }
            return null;
        }
        public static Texture2D LoadImage(string path)
        {
            if (!File.Exists(path)) throw new IOException("原图不存在：" + path);
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new IOException("图片超过 20 MiB。");
            if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new IOException("本 Unity 版本的原生读取支持 PNG/JPEG；WebP 请提供已有 PNG 原图。");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "AW_Image" };
            if (!tex.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.DestroyImmediate(tex); throw new IOException("图片无法解码。"); }
            return tex;
        }
        public static JObject Target(GameObject target, string sourceKind)
        {
            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path)) path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(target);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene == target.scene) path = stage.assetPath;
            var result = new JObject { ["name"] = target.name, ["global_id"] = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                ["asset_path"] = path, ["scene"] = target.scene.path, ["scene_handle"] = target.scene.handle,
                ["instance_id"] = target.GetInstanceID(), ["preview_kind"] = sourceKind };
            // The file digest describes the backing asset; it is not a claim about unsaved scene content.
            if (!string.IsNullOrEmpty(path) && File.Exists(Path.Combine(Project, path))) result["asset_sha256"] = FileHash(Path.Combine(Project, path));
            return result;
        }
        public static bool SameCandidate(JObject a, JObject b) => JToken.DeepEquals(a, b);
    }

    /// <summary>Small public surface callable through the existing UnityMCP execute_editor_command.</summary>
    public static class WorkbenchApi
    {
        // Codex calls this once with its actual desktop endpoint and current thread ID.
        // Binding is not advertised as connected until read_thread verifies the same live session.
        public static object BindCodexDesktop(string pipeAddress, string threadId)
        {
            var link = WorkbenchCodex.Bind(pipeAddress, threadId);
            foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<WorkbenchWindow>()) window.ReconnectCodex();
            return new JObject { ["state"] = "binding_pending", ["thread_id"] = link["thread_id"]?.DeepClone() };
        }
        public static object ReadPending(string binding) => new JArray(WorkbenchData.Feedback(binding).Where(x => WorkbenchData.Text(x["status"]) != "addressed"));
        // Notification only. The existing workflow owns the actual scene/candidate edits.
        public static object NotifyTargetChanged(string binding, string targetGlobalId)
        {
            int queued = 0;
            foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<WorkbenchWindow>())
                if (window.BindingPath == binding && window.FollowsTarget(targetGlobalId)) { window.QueueSceneRefresh(); queued++; }
            return new JObject { ["state"] = "refresh_queued", ["windows"] = queued, ["target_global_id"] = targetGlobalId };
        }
        public static object Acknowledge(string binding, string id, string status, string note, string session)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$")) throw new ArgumentException("反馈 id 无效。");
            if (!new[] { "seen", "processing_feedback", "addressed" }.Contains(status)) throw new ArgumentException("仅支持 seen / processing_feedback / addressed。");
            if (string.IsNullOrWhiteSpace(session)) throw new ArgumentException("请记录实际读取反馈的 Codex 会话。");
            string file = Path.Combine(WorkbenchData.Inbox(binding), id + ".json"); var entry = WorkbenchData.Read(file);
            entry["status"] = status; entry["reply"] = note; entry["updated_at"] = WorkbenchData.Now;
            entry["consumer_session"] = session; entry["execution_started"] = status == "processing_feedback";
            WorkbenchData.Write(file, entry); return entry;
        }
        public static object SetConsumer(string binding, string session, bool active, bool busy, int leaseSeconds = 120)
        {
            if (string.IsNullOrWhiteSpace(session)) throw new ArgumentException("会话不能为空。");
            var c = new JObject { ["active"] = active, ["busy"] = busy, ["session"] = session, ["updated_at"] = WorkbenchData.Now,
                ["expires_at"] = DateTime.UtcNow.AddSeconds(Mathf.Clamp(leaseSeconds, 10, 3600)).ToString("yyyy-MM-ddTHH:mm:ssZ") };
            WorkbenchData.Write(Path.Combine(WorkbenchData.Space(binding), "consumer.json"), c); return c;
        }
    }
}
