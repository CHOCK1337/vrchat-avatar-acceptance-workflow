using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarWorkbench
{
    // Native read-only product preview. Remote metadata never starts installation or Avatar editing.
    internal sealed class WorkbenchCatalogDetailWindow : EditorWindow
    {
        JObject item; Action changed; Texture2D picture; string status = "", error = ""; int index; Vector2 scroll;
        [SerializeField] string savedItem;
        CancellationTokenSource cancellation; Task<FetchResult> pending;
        sealed class FetchResult { internal JObject Metadata; internal BoothProductImage Image; internal string CacheDirectory; }
        internal static void Open(JObject entry, Action updated)
        {
            var window = CreateInstance<WorkbenchCatalogDetailWindow>(); window.item = entry; window.changed = updated;
            window.titleContent = new GUIContent("素材详情 / 商品图片"); window.minSize = new Vector2(570, 420); window.Show(); window.position = new Rect(400, 150, 820, 600); window.LoadPicture();
        }
        string[] Covers => (item?["covers"] as JArray ?? new JArray()).Values<string>().Take(12).ToArray();
        void OnEnable()
        {
            if (string.IsNullOrEmpty(savedItem)) return;
            try { item = JObject.Parse(savedItem); LoadPicture(); status = "脚本已重载；重新从素材卡片打开后可保存商品信息更新。"; } catch { item = null; }
        }
        void LoadPicture()
        {
            if (picture) DestroyImmediate(picture); picture = null; error = "";
            var paths = Covers; if (paths.Length == 0) { error = "没有已有商品图片。可以读取已明确关联的 BOOTH 商品封面。"; Repaint(); return; }
            index = Math.Min(index, paths.Length - 1);
            try { picture = WorkbenchCoverImage.Load(paths[index], 1600); } catch (Exception e) { error = e.Message; }
            Repaint();
        }
        void OnGUI()
        {
            if (item == null) return;
            GUILayout.Label(WorkbenchData.Text(item["name"]), EditorStyles.boldLabel);
            GUILayout.Label((Covers.Length > 0 ? WorkbenchCatalogMetadata.PictureLabel(item, Covers[Math.Min(index, Covers.Length - 1)]) : "没有已有图片") + " · 参考图片，不是当前模型构建结果", EditorStyles.miniLabel);
            Rect imageRect = new Rect(12, 47, Math.Max(180, position.width * .62f - 18), Math.Max(80, position.height - 123));
            EditorGUI.DrawRect(imageRect, new Color(.075f, .085f, .10f));
            if (picture) GUI.DrawTexture(imageRect, picture, ScaleMode.ScaleToFit, true); else GUI.Label(imageRect, error, EditorStyles.wordWrappedLabel);
            float right = imageRect.xMax + 12;
            GUILayout.BeginArea(new Rect(right, 47, Math.Max(180, position.width - right - 12), Math.Max(80, position.height - 123)));
            scroll = GUILayout.BeginScrollView(scroll, false, true);
            Fact("分类", WorkbenchData.Text(item["category"], "尚未记录"));
            Fact("适配标签", string.Join(" / ", (item["bases"] as JArray ?? new JArray()).Values<string>()) + "（记录提示，尚未验证）");
            Fact("信息来源", WorkbenchData.Text(item["metadata_source"]) == "mio_library" ? "Mio 本地素材库缓存" : WorkbenchData.Text(item["metadata_source"]) == "booth_item" ? "BOOTH 商品 JSON" : "本地文件名 / 说明");
            Fact("BOOTH", WorkbenchData.Text(item["booth_id"], "尚未关联"));
            if (!string.IsNullOrEmpty(WorkbenchData.Text(item["booth_name"]))) Fact("商品页名称", WorkbenchData.Text(item["booth_name"]));
            if (!string.IsNullOrEmpty(WorkbenchData.Text(item["shop"]))) Fact("店铺", WorkbenchData.Text(item["shop"]));
            if (picture) Fact("图片尺寸", picture.width + " × " + picture.height + "（当前显示尺寸）");
            if (!string.IsNullOrEmpty(WorkbenchData.Text(item["package_preview_entry"]))) Fact("包内预览对应资源", WorkbenchData.Text(item["package_preview_entry"]));
            Fact("素材位置", WorkbenchData.Text(item["full_path"]));
            if (Covers.Length > 0) Fact("图片位置", Covers[Math.Min(index, Covers.Length - 1)]);
            if (item["metadata_note"] != null) Fact("说明", WorkbenchData.Text(item["metadata_note"]));
            if (item["description"] != null) Fact("商品 / 素材说明", WorkbenchData.Text(item["description"]));
            GUILayout.EndScrollView(); GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(12, position.height - 68, position.width - 24, 64));
            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(Covers.Length < 2)) {
                if (GUILayout.Button("上一张", GUILayout.Width(75))) { index = (index + Covers.Length - 1) % Covers.Length; LoadPicture(); }
                GUILayout.Label(Covers.Length == 0 ? "无图片" : (index + 1) + " / " + Covers.Length, GUILayout.Width(55));
                if (GUILayout.Button("下一张", GUILayout.Width(75))) { index = (index + 1) % Covers.Length; LoadPicture(); }
            }
            GUILayout.FlexibleSpace();
            bool linked = Regex.IsMatch(WorkbenchData.Text(item["booth_id"]), @"^[0-9]{5,12}$");
            using (new EditorGUI.DisabledScope(!linked || pending != null || changed == null)) {
                if (GUILayout.Button("读取商品信息与封面")) StartFetch();
                if (GUILayout.Button("打开商品页", GUILayout.Width(100))) Application.OpenURL("https://booth.pm/ja/items/" + WorkbenchData.Text(item["booth_id"]));
            }
            if (pending != null && GUILayout.Button("取消", GUILayout.Width(60))) cancellation.Cancel();
            GUILayout.EndHorizontal(); GUILayout.Label(status, EditorStyles.wordWrappedMiniLabel); GUILayout.EndArea();
        }
        static void Fact(string label, string value) { GUILayout.Label(label, EditorStyles.boldLabel); GUILayout.Label(value, EditorStyles.wordWrappedLabel); GUILayout.Space(8); }
        void StartFetch()
        {
            cancellation = new CancellationTokenSource(); string id = (string)item["booth_id"];
            string cache = Path.Combine(Path.GetDirectoryName(WorkbenchData.WindowFile), "product-covers");
            // Capture Unity-dependent paths on the editor thread; HTTP and file IO stay in the existing adapter.
            pending = Fetch(id, cache, cancellation.Token); status = "正在读取实际商品 JSON 和封面 · 原图超时后回退缩略图";
        }
        static async Task<FetchResult> Fetch(string id, string cache, CancellationToken token)
        {
            var metadata = await WorkbenchBooth.DetailAsync(id, token);
            var image = await WorkbenchBooth.ProductImageAsync(id, WorkbenchData.Text(metadata["thumbnail"]), token);
            return new FetchResult { Metadata = metadata, Image = image, CacheDirectory = cache };
        }
        void Update()
        {
            if (pending == null || !pending.IsCompleted) return; var completed = pending; pending = null;
            try
            {
                var result = completed.GetAwaiter().GetResult(); byte[] bytes = result.Image.Data; WorkbenchCoverImage.Validate(bytes);
                Directory.CreateDirectory(result.CacheDirectory);
                string path = Path.Combine(result.CacheDirectory, WorkbenchData.Hash(bytes) + (bytes[0] == 137 ? ".png" : ".jpg"));
                if (!File.Exists(path)) { string temporary = path + ".part-" + Guid.NewGuid().ToString("N"); try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path); } finally { if (File.Exists(temporary)) File.Delete(temporary); } }
                var data = result.Metadata;
                item["booth_name"] = WorkbenchCatalogMetadata.Clip(data["name"]); item["product_name"] = item["booth_name"]; item["booth_category"] = WorkbenchCatalogMetadata.Clip(data["category"]);
                if (string.IsNullOrEmpty(WorkbenchData.Text(item["name"]))) item["name"] = item["booth_name"];
                if (string.IsNullOrEmpty(WorkbenchData.Text(item["category"]))) item["category"] = item["booth_category"];
                item["description"] = WorkbenchCatalogMetadata.Clip(data["description"], 1800); item["shop"] = WorkbenchCatalogMetadata.Clip(data["shop"]?["name"]);
                item["tags"] = data["tags"]?.DeepClone(); item["metadata_source"] = "booth_item"; item["metadata_read_at"] = WorkbenchData.Now;
                var records = WorkbenchCatalogMetadata.PictureRecords(item).OfType<JObject>().Where(x => WorkbenchData.Text(x["path"]) != path).ToArray();
                string imageSource = result.Image.Source == "original" ? "booth_original" : "booth_thumbnail";
                item["cover_records"] = new JArray(new[] { new JObject { ["path"] = path, ["source"] = imageSource } }.Concat(records).Take(12));
                var covers = new JArray(new[] { path }.Concat(Covers).Distinct().Take(12)); item["covers"] = covers; item["thumbnail"] = path;
                item["cover_source"] = result.Image.Source == "original" ? "booth_original" : "booth_thumbnail"; item["cover_note"] = result.Image.Note;
                status = result.Image.Note; index = 0; LoadPicture(); changed?.Invoke();
            }
            catch (OperationCanceledException) { status = "读取已取消；原图片和信息保留。"; }
            catch (Exception e) { status = "读取未完成：" + e.Message + "；原图片和信息保留。"; }
            finally { cancellation?.Dispose(); cancellation = null; Repaint(); }
        }
        void OnDisable()
        {
            if (item != null) savedItem = item.ToString(Newtonsoft.Json.Formatting.None);
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            if (pending != null) pending.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); pending = null;
            if (picture) DestroyImmediate(picture); picture = null; changed = null;
        }
    }
}
