using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    // Native read-only product preview. Remote metadata never starts installation or Avatar editing.
    internal sealed class WorkbenchCatalogDetailWindow : EditorWindow
    {
        JObject item; Action changed; Texture2D picture; string status = "", error = ""; int index;
        Image hero; Label productTitle, subtitle, pictureSource, pictureCount, imageError, statusLabel;
        VisualElement facts; Button previousButton, nextButton, fetchButton, openButton, cancelButton;
        IVisualElementScheduledItem loading; int loadingFrame;
        [SerializeField] string savedItem;
        CancellationTokenSource cancellation; Task<FetchResult> pending;
        sealed class FetchResult { internal JObject Metadata; internal BoothProductImage Image; internal string CacheDirectory; }
        internal static void Open(JObject entry, Action updated)
        {
            var window = CreateInstance<WorkbenchCatalogDetailWindow>(); window.item = entry; window.changed = updated;
            window.titleContent = new GUIContent("素材大图"); window.minSize = new Vector2(620, 420); window.Show(); window.position = new Rect(400, 130, 930, 640); window.BuildView(); window.LoadPicture();
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
            var paths = Covers; if (paths.Length == 0) { error = "没有已有商品图片。可以读取已明确关联的 BOOTH 商品封面。"; RefreshView(); Repaint(); return; }
            index = Math.Min(index, paths.Length - 1);
            try { picture = WorkbenchCoverImage.Load(paths[index], 1600); } catch (Exception e) { error = e.Message; }
            RefreshView(); Repaint();
        }
        public void CreateGUI() { BuildView(); }
        void BuildView()
        {
            loading?.Pause(); var root = rootVisualElement; root.Clear(); WorkbenchTheme.Apply(this, "aw-product-window");
            var heading = new VisualElement(); heading.AddToClassList("aw-product-heading");
            productTitle = new Label { enableRichText = false }; productTitle.AddToClassList("aw-product-title"); heading.Add(productTitle);
            subtitle = new Label { enableRichText = false }; subtitle.AddToClassList("aw-product-subtitle"); heading.Add(subtitle); root.Add(heading);
            var body = new VisualElement(); body.AddToClassList("aw-product-body");
            var gallery = new VisualElement(); gallery.AddToClassList("aw-product-gallery");
            var frame = new VisualElement(); frame.AddToClassList("aw-product-image-frame");
            hero = new Image { scaleMode = ScaleMode.ScaleToFit }; hero.AddToClassList("aw-product-image"); frame.Add(hero);
            imageError = new Label { enableRichText = false }; imageError.AddToClassList("aw-product-image-error"); frame.Add(imageError); gallery.Add(frame);
            var paging = new VisualElement(); paging.AddToClassList("aw-product-paging");
            previousButton = new Button(() => ChangePicture(-1)) { text = "上一张" }; paging.Add(previousButton);
            pictureCount = new Label(); pictureCount.AddToClassList("aw-product-picture-count"); paging.Add(pictureCount);
            nextButton = new Button(() => ChangePicture(1)) { text = "下一张" }; paging.Add(nextButton);
            pictureSource = new Label { enableRichText = false }; pictureSource.AddToClassList("aw-product-picture-source"); paging.Add(pictureSource); gallery.Add(paging); body.Add(gallery);
            var info = new ScrollView(ScrollViewMode.Vertical); info.AddToClassList("aw-product-info"); facts = new VisualElement(); info.Add(facts); body.Add(info); root.Add(body);
            var actions = new VisualElement(); actions.AddToClassList("aw-product-actions");
            statusLabel = new Label { enableRichText = false }; statusLabel.AddToClassList("aw-product-status"); actions.Add(statusLabel);
            cancelButton = new Button(() => cancellation?.Cancel()) { text = "取消读取" }; actions.Add(cancelButton);
            openButton = new Button(() => Application.OpenURL("https://booth.pm/ja/items/" + WorkbenchData.Text(item?["booth_id"]))) { text = "打开商品页" }; actions.Add(openButton);
            fetchButton = new Button(StartFetch) { text = "从 BOOTH 更新信息与图片" }; fetchButton.AddToClassList("aw-accent-outline"); actions.Add(fetchButton); root.Add(actions);
            loading = root.schedule.Execute(() => { if (pending != null && statusLabel != null) statusLabel.text = "正在读取 BOOTH" + new[] { "", ".", "..", "..." }[loadingFrame++ % 4]; }).Every(350); loading.Pause();
            RefreshView();
        }
        void ChangePicture(int step)
        {
            if (Covers.Length < 2) return; index = (index + step + Covers.Length) % Covers.Length; LoadPicture();
        }
        void Fact(VisualElement parent, string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var group = new VisualElement(); group.AddToClassList("aw-product-fact");
            var title = new Label(label); title.AddToClassList("aw-product-fact-title"); group.Add(title);
            var text = new Label(value) { enableRichText = false }; text.AddToClassList("aw-product-fact-value"); group.Add(text); parent.Add(group);
        }
        void RefreshView()
        {
            if (hero == null || item == null) return;
            string name = WorkbenchData.Text(item["name"]), official = WorkbenchData.Text(item["booth_name"]);
            productTitle.text = string.IsNullOrEmpty(official) ? name : official; productTitle.tooltip = productTitle.text;
            subtitle.text = (!string.IsNullOrEmpty(official) && name != official ? name + " · " : "") + "商品参考图片 · 当前角色的效果请在工作台查看";
            hero.image = picture; imageError.text = error; imageError.style.display = picture ? DisplayStyle.None : DisplayStyle.Flex;
            var covers = Covers; pictureCount.text = covers.Length == 0 ? "无图片" : (index + 1) + " / " + covers.Length;
            pictureSource.text = covers.Length > 0 ? WorkbenchCatalogMetadata.PictureLabel(item, covers[Math.Min(index, covers.Length - 1)]) : "暂无已有图片";
            previousButton.SetEnabled(covers.Length > 1); nextButton.SetEnabled(covers.Length > 1);
            facts.Clear();
            Fact(facts, "分类", WorkbenchData.Text(item["category"], "尚未记录"));
            Fact(facts, "店铺", WorkbenchData.Text(item["shop"]));
            string bases = string.Join(" / ", (item["bases"] as JArray ?? new JArray()).Values<string>());
            Fact(facts, "适配提示", string.IsNullOrEmpty(bases) ? "尚未记录适配角色" : bases + "\n来源中的标签，尚未验证适配");
            Fact(facts, "说明", WorkbenchData.Text(item["description"] ?? item["metadata_note"]));
            var detail = new Foldout { text = "来源与文件信息", value = false }; facts.Add(detail);
            Fact(detail, "信息来源", WorkbenchData.Text(item["metadata_source"]) == "mio_library" ? "Mio 本地素材库缓存" : WorkbenchData.Text(item["metadata_source"]) == "booth_item" ? "BOOTH 商品 JSON" : "本地文件名 / 说明");
            Fact(detail, "BOOTH 商品编号", WorkbenchData.Text(item["booth_id"], "尚未关联"));
            if (picture) Fact(detail, "当前图片尺寸", picture.width + " × " + picture.height);
            Fact(detail, "包内预览对应资源", WorkbenchData.Text(item["package_preview_entry"]));
            Fact(detail, "素材位置", WorkbenchData.Text(item["full_path"]));
            if (covers.Length > 0) Fact(detail, "图片位置", covers[Math.Min(index, covers.Length - 1)]);
            bool linked = Regex.IsMatch(WorkbenchData.Text(item["booth_id"]), @"^[0-9]{5,12}$");
            fetchButton.SetEnabled(linked && pending == null && changed != null); openButton.SetEnabled(linked);
            fetchButton.tooltip = linked ? "读取已关联的真实商品；原图超时后使用缩略图。不会购买、下载素材包或安装。" : "素材尚未关联明确的 BOOTH 商品编号。";
            cancelButton.style.display = pending == null ? DisplayStyle.None : DisplayStyle.Flex;
            statusLabel.text = string.IsNullOrEmpty(status) ? "只查看图片与信息" : status; statusLabel.tooltip = statusLabel.text;
            if (pending == null) loading?.Pause(); else loading?.Resume();
        }
        void StartFetch()
        {
            cancellation = new CancellationTokenSource(); string id = (string)item["booth_id"];
            string cache = Path.Combine(Path.GetDirectoryName(WorkbenchData.WindowFile), "product-covers");
            // Capture Unity-dependent paths on the editor thread; HTTP and file IO stay in the existing adapter.
            pending = Fetch(id, cache, cancellation.Token); status = "正在读取实际商品 JSON 和封面 · 原图超时后回退缩略图"; RefreshView();
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
            finally { cancellation?.Dispose(); cancellation = null; RefreshView(); Repaint(); }
        }
        void OnDisable()
        {
            loading?.Pause(); loading = null;
            if (item != null) savedItem = item.ToString(Newtonsoft.Json.Formatting.None);
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            if (pending != null) pending.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); pending = null;
            if (hero != null) hero.image = null;
            if (picture) DestroyImmediate(picture); picture = null; changed = null;
        }
    }
}
