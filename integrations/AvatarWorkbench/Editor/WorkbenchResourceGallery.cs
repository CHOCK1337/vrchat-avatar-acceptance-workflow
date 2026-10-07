using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AvatarWorkbench
{
    // Only the supplied resource scope is browsed. No package import or author scripts run here.
    internal sealed class WorkbenchResourceGallery : EditorWindow
    {
        [SerializeField] string snapshot = "", selected = "";
        List<ResourceCard> items = new List<ResourceCard>();
        readonly HashSet<string> picked = new HashSet<string>();
        readonly List<int> rows = new List<int>();
        readonly Dictionary<string, Texture2D> ownImages = new Dictionary<string, Texture2D>();
        readonly List<WorkbenchPreview> previews = new List<WorkbenchPreview>();
        readonly List<ResourceCard> compared = new List<ResourceCard>();
        VisualElement comparison;
        ListView grid;
        Label hint;
        Button apply;
        IVisualElementScheduledItem loading;
        EventCallback<GeometryChangedEvent> resize;
        int columns = 3, attempts;
        bool visible = true;
        WorkbenchWindow Owner => Resources.FindObjectsOfTypeAll<WorkbenchWindow>().FirstOrDefault();
        internal static void Open(IEnumerable<ResourceCard> resources, IEnumerable<string> selectedIds)
        {
            var w = GetWindow<WorkbenchResourceGallery>(false, "本地素材大图与对比", true);
            w.minSize = new Vector2(680, 420); w.snapshot = new JArray(resources.Take(300).Select(x => x.Json())).ToString();
            w.selected = new JArray(selectedIds).ToString(); w.CreateGUI(); w.Show();
        }
        internal static void OpenPreview(IEnumerable<ResourceCard> resources, IEnumerable<string> selectedIds, string id)
        {
            var scope = resources.ToArray(); Open(scope, selectedIds);
            var window = Resources.FindObjectsOfTypeAll<WorkbenchResourceGallery>().FirstOrDefault();
            var item = scope.FirstOrDefault(x => x.id == id); if (window && item != null) window.Compare(item);
        }
        void OnDisable() { Release(); }
        void OnBecameInvisible() { visible = false; loading?.Pause(); }
        void OnBecameVisible() { visible = true; loading?.Resume(); }
        void Release()
        {
            loading?.Pause(); loading = null;
            if (resize != null && grid != null) grid.UnregisterCallback(resize); resize = null;
            foreach (var preview in previews) preview.Dispose(); previews.Clear(); compared.Clear();
            foreach (var image in ownImages.Values) if (image) Object.DestroyImmediate(image); ownImages.Clear();
        }
        public void CreateGUI()
        {
            Release(); items.Clear(); picked.Clear();
            if (!string.IsNullOrEmpty(snapshot)) items.AddRange(JArray.Parse(snapshot).OfType<JObject>().Select(x => new ResourceCard { id = (string)x["id"], name = (string)x["name"], path = (string)x["path"], thumbnail = (string)x["thumbnail"], kind = (string)x["kind"], state = (string)x["state"] }));
            if (!string.IsNullOrEmpty(selected)) picked.UnionWith(JArray.Parse(selected).Values<string>());
            var root = rootVisualElement; root.Clear(); WorkbenchTheme.Apply(this, "aw-gallery-window");
            var title = new Label("素材库"); title.AddToClassList("aw-window-title"); title.style.fontSize = 20; title.style.flexShrink = 0; root.Add(title);
            hint = new Label("卡片只选择和查看。Prefab 可旋转、缩放并对比；unitypackage 未导入时只显示已有封面，不能假装已经穿上角色。"); hint.style.whiteSpace = WhiteSpace.Normal; hint.style.flexShrink = 0; root.Add(hint);
            var filter = new TextField("筛选素材") { name = "gallery-search" }; root.Add(filter);
            grid = new ListView { itemsSource = rows, fixedItemHeight = 262, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, selectionType = SelectionType.None, name = "local-resource-grid" };
            grid.style.flexGrow = 1; grid.style.minHeight = 90; root.Add(grid);
            var scope = items.ToList();
            grid.makeItem = () => { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.paddingTop = row.style.paddingBottom = 4; return row; };
            grid.bindItem = (row, index) =>
            {
                row.Clear();
                for (int col = 0; col < columns; col++)
                {
                    int n = index * columns + col; if (n >= items.Count) break; var item = items[n];
                    var card = new VisualElement(); card.style.flexGrow = 1; card.style.flexBasis = 0; card.style.minWidth = 0; card.style.marginRight = 7; card.AddToClassList("aw-asset-tile"); card.EnableInClassList("aw-asset-picked", picked.Contains(item.id));
                    var img = new Image { name = "resource-cover", scaleMode = ScaleMode.ScaleToFit, image = Cover(item), userData = item }; img.style.height = Mathf.Max(40, grid.fixedItemHeight - 84); img.style.flexShrink = 0; img.AddToClassList("aw-gallery-cover"); card.Add(img);
                    var loader = new Label(img.image ? "" : IsProjectAsset(item.path) && Path.GetExtension(item.path).Equals(".prefab", StringComparison.OrdinalIgnoreCase) ? "◌ 正在读取预览…" : "没有已有封面") { name = "cover-loading", enableRichText = false }; loader.style.fontSize = 10; card.Add(loader);
                    var caption = new Toggle(WorkbenchUiRules.Short(item.name, 25)) { value = picked.Contains(item.id), name = "pick-" + item.id }; caption.AddToClassList("aw-gallery-pick"); caption.tooltip = item.name + "\n" + item.path;
                    caption.RegisterValueChangedCallback(e => { if (e.newValue) picked.Add(item.id); else picked.Remove(item.id); selected = new JArray(picked).ToString(); card.EnableInClassList("aw-asset-picked", e.newValue); UpdateCount(); }); card.Add(caption);
                    var state = new Label(WorkbenchData.StateName(item.state)) { enableRichText = false }; state.style.fontSize = 11; card.Add(state);
                    var action = new Button(() => Compare(item)) { text = "3D 预览 / 对比", name = "preview-" + item.id };
                    action.SetEnabled(Path.GetExtension(item.path ?? "").Equals(".prefab", StringComparison.OrdinalIgnoreCase) && IsProjectAsset(item.path)); card.Add(action); row.Add(card);
                }
                for (int fill = row.childCount; fill < columns; fill++) { var empty = new VisualElement(); empty.AddToClassList("aw-asset-tile"); empty.style.visibility = Visibility.Hidden; row.Add(empty); }
            };
            filter.RegisterValueChangedCallback(e => { items = scope.Where(x => ((x.name ?? "") + " " + x.path).IndexOf(e.newValue ?? "", StringComparison.OrdinalIgnoreCase) >= 0).ToList(); Rebuild(); });
            comparison = new VisualElement { name = "resource-3d-comparison" }; comparison.style.flexDirection = FlexDirection.Row; comparison.style.flexShrink = 0; comparison.style.height = 230; comparison.style.display = DisplayStyle.None; root.Add(comparison);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexShrink = 0; root.Add(actions);
            apply = new Button(() => { var owner = Owner; if (owner == null) { hint.text = "工作台窗口已关闭，选择仍保留；重新打开工作台后再应用。"; return; } owner.SelectResourcesForRequest(picked.ToArray()); hint.text = "已更新本次需求的素材选择；没有安装或修改角色。"; }) { name = "apply-resource-picks" }; apply.AddToClassList("aw-primary"); actions.AddToClassList("aw-gallery-footer"); actions.Add(apply);
            actions.Add(new Button(() => { foreach (var p in previews) p.Dispose(); previews.Clear(); compared.Clear(); comparison.Clear(); comparison.style.display = DisplayStyle.None; }) { text = "收起对比", name = "close-resource-comparison" });
            resize = e => { if (e.target != grid || e.newRect.height <= 0) return; int next = Mathf.Clamp((int)(e.newRect.width / 245), 2, 4); float itemHeight = Mathf.Min(262, Mathf.Max(1, Mathf.Floor(e.newRect.height))); bool changed = next != columns || grid.fixedItemHeight != itemHeight; columns = next; if (changed) { grid.fixedItemHeight = itemHeight; Rebuild(); } };
            grid.RegisterCallback(resize);
            Rebuild(); UpdateCount(); attempts = 0;
            loading = root.schedule.Execute(() =>
            {
                if (!visible || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                bool pending = false; foreach (var image in grid.Query<Image>(name: "resource-cover").ToList())
                {
                    if (image.image || !(image.userData is ResourceCard item)) continue;
                    image.image = Cover(item);
                    bool wait = image.image == null && IsProjectAsset(item.path) && Path.GetExtension(item.path).Equals(".prefab", StringComparison.OrdinalIgnoreCase);
                    pending |= wait;
                    var label = image.parent.Q<Label>("cover-loading"); if (label != null) label.text = image.image ? "" : wait && attempts < 59 ? new[] { "◐", "◓", "◑", "◒" }[attempts % 4] + " 正在读取预览…" : "没有可用封面，可打开真实 3D 预览";
                }
                attempts++; if (!pending || attempts >= 60) loading?.Pause();
            }).Every(300);
        }
        void Rebuild() { rows.Clear(); rows.AddRange(Enumerable.Range(0, (items.Count + columns - 1) / columns)); grid?.Rebuild(); attempts = 0; loading?.Resume(); }
        void UpdateCount() { if (apply != null) apply.text = "用于本次需求（" + picked.Count + " 项）"; }
        static bool IsProjectAsset(string path) => (path ?? "").Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal) || (path ?? "").Replace('\\', '/').StartsWith("Packages/", StringComparison.Ordinal);
        Texture Cover(ResourceCard item)
        {
            if (!string.IsNullOrEmpty(item.thumbnail))
            {
                string path = WorkbenchData.Resolve(item.thumbnail, WorkbenchData.Project);
                if (ownImages.TryGetValue(path, out var cached)) return cached;
                if (File.Exists(path))
                {
                    if (ownImages.Count >= 24) { var visibleImages = grid?.Query<Image>().ToList().Select(x => x.image).ToList() ?? new List<Texture>(); string spare = ownImages.Keys.FirstOrDefault(x => !visibleImages.Contains(ownImages[x])); if (spare != null) { Object.DestroyImmediate(ownImages[spare]); ownImages.Remove(spare); } }
                    if (ownImages.Count < 24) { try { return ownImages[path] = WorkbenchData.LoadImage(path); } catch (Exception) { } }
                }
            }
            if (!IsProjectAsset(item.path)) return null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(item.path); return asset ? AssetPreview.GetAssetPreview(asset) : null;
        }
        void Compare(ResourceCard resource)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || (Owner != null && Owner.PreviewIsBusy)) { hint.text = "Unity 或原改模流程正忙，先保留当前对比画面。"; return; }
            if (compared.Any(x => x.id == resource.id)) return;
            if (previews.Count >= 2) { hint.text = "同时对比最多两件，先结束对比再选择其他素材。"; return; }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(resource.path);
            if (!source || !source.GetComponentsInChildren<Renderer>(true).Any()) { hint.text = "这项没有可显示的模型网格；未将封面或贴图当作模型。"; return; }
            var preview = new WorkbenchPreview();
            try { preview.Load(source); }
            catch (Exception e) { preview.Dispose(); hint.text = "这项不能独立预览：" + e.Message; return; }
            previews.Add(preview); compared.Add(resource);
            var pane = new VisualElement(); pane.style.flexGrow = 1; pane.style.flexBasis = 0; pane.style.minWidth = 0; comparison.Add(pane);
            var name = new Label(resource.name + " · 素材自身，未适配"); name.enableRichText = false; pane.Add(name);
            var canvas = new IMGUIContainer(() => Draw(preview)); canvas.style.flexGrow = 1; canvas.style.minHeight = 0; pane.Add(canvas);
            comparison.style.display = DisplayStyle.Flex; hint.text = "两个独立素材保留原材质；拖动旋转、滚轮缩放，只调整临时相机。不是当前角色的试穿结果。";
        }
        void Draw(WorkbenchPreview preview)
        {
            Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var e = Event.current;
            if (e.type == EventType.Repaint) { var image = preview.Render(rect); if (image) GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit, false); }
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDrag && e.button == 0) { preview.Yaw += e.delta.x * .5f; preview.Pitch = Mathf.Clamp(preview.Pitch + e.delta.y * .35f, -80, 80); preview.Dirty = true; e.Use(); Repaint(); }
            if (e.type == EventType.ScrollWheel) { preview.Distance = Mathf.Clamp(preview.Distance * Mathf.Exp(e.delta.y * .06f), .03f, 50f); preview.Dirty = true; e.Use(); Repaint(); }
        }
    }
}
