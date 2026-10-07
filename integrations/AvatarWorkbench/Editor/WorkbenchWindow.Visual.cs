using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        VisualElement navigationRail, workspaceColumn, libraryRoot;
        Button cameraViewButton;
        readonly List<int> libraryRows = new List<int>();
        readonly Dictionary<string, Object> libraryAssets = new Dictionary<string, Object>();
        IVisualElementScheduledItem libraryLoading;
        EventCallback<GeometryChangedEvent> libraryResize;
        int libraryColumns = 3, libraryAttempts;

        Button Navigation(string text, string icon, Action action, string name)
        {
            var button = MakeButton(text, action, name);
            button.AddToClassList("aw-nav-item"); button.tooltip = text;
            button.Add(WorkbenchTheme.Icon(icon)); return button;
        }

        VisualElement BuildNavigation()
        {
            var rail = new VisualElement { name = "workspace-navigation" }; rail.AddToClassList("aw-navigation");
            var caption = new Label("工作区"); caption.AddToClassList("aw-nav-caption"); rail.Add(caption);
            tabBar = new VisualElement(); tabBar.AddToClassList("aw-nav-primary");
            previewTab = Navigation("模型预览", "model", () => SelectPanel("preview"), "tab-preview");
            materialTab = Navigation("素材库", "assets", () => SelectPanel("materials"), "tab-materials");
            feedbackTab = Navigation("需求与回复", "reply", () => SelectPanel("feedback"), "tab-feedback");
            tabBar.Add(previewTab); tabBar.Add(materialTab); tabBar.Add(feedbackTab); rail.Add(tabBar);
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; rail.Add(spacer);
            rail.Add(Navigation("截图记录", "images", OpenScreenshotGallery, "open-screenshot-gallery"));
            rail.Add(Navigation("模型设置", "settings", WorkbenchModelSettingsWindow.Open, "open-model-settings"));
            var project = new Label(Path.GetFileName(WorkbenchData.Project.TrimEnd('\\', '/'))) { tooltip = WorkbenchData.Project, enableRichText = false };
            project.AddToClassList("aw-project-caption"); project.AddToClassList("aw-ellipsis"); rail.Add(project);
            connectionBox = new VisualElement(); connectionBox.AddToClassList("aw-connection");
            connectionLabel = new Label { enableRichText = false }; connectionLabel.AddToClassList("aw-connection-title"); connectionBox.Add(connectionLabel);
            connectionHelp = Wrapped(""); connectionHelp.AddToClassList("aw-connection-help"); connectionBox.Add(connectionHelp);
            helpButton = MakeButton("连接 Codex", ConnectSelectedModel, "copy-connect"); connectionBox.Add(helpButton); rail.Add(connectionBox);
            return rail;
        }

        void ShowCameraViews()
        {
            var menu = new GenericMenu();
            foreach (string direction in new[] { "正面", "侧面", "背面" })
            {
                string value = direction;
                menu.AddItem(new GUIContent("视角/" + value), false, () => { if (frozen || historical || busy) return; preview?.Orient(value); canvas.MarkDirtyRepaint(); });
            }
            foreach (string part in new[] { "全身", "头部", "上身", "鞋子" })
            {
                string value = part;
                menu.AddItem(new GUIContent("聚焦/" + value), false, () => { if (frozen || historical || busy) return; preview?.SetFocus(value); canvas.MarkDirtyRepaint(); });
            }
            menu.AddSeparator("");
            if (shakeButton != null && shakeButton.enabledSelf) menu.AddItem(new GUIContent("动态/晃一晃"), false, () => TryAction(ShakePreview));
            else menu.AddDisabledItem(new GUIContent("动态/晃一晃（先开启动态预览）"));
            if (endTrialButton != null && endTrialButton.enabledSelf) menu.AddItem(new GUIContent("还原临时试穿"), false, () => TryAction(EndTrial));
            else menu.AddDisabledItem(new GUIContent("还原临时试穿（尚未试穿）"));
            menu.DropDown(cameraViewButton.worldBound);
        }

        VisualElement BuildResourceLibrary()
        {
            libraryRoot = new VisualElement { name = "local-materials-panel" }; libraryRoot.AddToClassList("aw-library");
            var header = Row(); header.AddToClassList("aw-library-heading"); header.Add(Heading("本次素材"));
            resourceCount = new Label { enableRichText = false }; resourceCount.AddToClassList("aw-muted"); resourceCount.style.flexGrow = 1; header.Add(resourceCount);
            header.Add(MakeButton("添加素材 ▾", () => { var menu = new GenericMenu(); menu.AddItem(new GUIContent("选择 Prefab / 安装包…"), false, () => TryAction(PickResource)); menu.AddItem(new GUIContent("读取素材目录…"), false, () => SelectMaterialSource("directory")); menu.AddItem(new GUIContent("添加百度网盘分享…"), false, () => SelectMaterialSource("baidu")); menu.ShowAsContext(); }, "add-resource"));
            header.Add(MakeButton("双素材预览", OpenResourceGallery, "open-resource-gallery")); libraryRoot.Add(header);
            search = new TextField("筛选") { name = "resource-search", value = resourceFilter }; search.AddToClassList("aw-library-search"); search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(e => { resourceFilter = e.newValue; UpdateResourceList(); }); header.Insert(2, search); resourceCount.style.display = DisplayStyle.None;
            libraryRoot.RegisterCallback<DragUpdatedEvent>(e => { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation(); });
            libraryRoot.RegisterCallback<DragPerformEvent>(e => { DragAndDrop.AcceptDrag(); AddDroppedPaths(DragAndDrop.paths); e.StopPropagation(); });
            cards = new ListView { itemsSource = libraryRows, fixedItemHeight = 246, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, selectionType = SelectionType.None, name = "resource-list" };
            cards.AddToClassList("aw-library-grid"); cards.makeItem = () => { var row = Row(); row.AddToClassList("aw-tile-row"); return row; };
            cards.bindItem = (row, index) =>
            {
                row.Clear();
                for (int column = 0; column < libraryColumns; column++)
                {
                    int n = index * libraryColumns + column; if (n >= visibleResources.Count) break;
                    var resource = visibleResources[n]; var card = new VisualElement(); card.AddToClassList("aw-asset-tile"); card.EnableInClassList("aw-asset-picked", ResourceRequested(resource));
                    var cover = new VisualElement(); cover.AddToClassList("aw-tile-cover"); cover.style.height = Mathf.Max(12, cards.fixedItemHeight - 69);
                    var image = new Image { image = LibraryCover(resource), scaleMode = ScaleMode.ScaleToFit, userData = resource, name = "library-cover" }; image.AddToClassList("aw-tile-image"); cover.Add(image);
                    var loading = new Label(image.image ? "" : "正在读取预览…") { name = "library-cover-state", pickingMode = PickingMode.Ignore }; loading.AddToClassList("aw-tile-loading"); cover.Add(loading);
                    var pick = new Toggle { value = ResourceRequested(resource), tooltip = "加入本次需求：" + resource.name, name = "pick-" + resource.id }; pick.AddToClassList("aw-tile-pick");
                    pick.RegisterValueChangedCallback(e => { var ids = new HashSet<string>(resources.Where(ResourceRequested).Select(x => x.id)); if (e.newValue) ids.Add(resource.id); else ids.Remove(resource.id); card.EnableInClassList("aw-asset-picked", e.newValue); SelectResourcesForRequest(ids.ToArray()); }); cover.Add(pick);
                    var kind = new Label(KindName(resource.kind)); kind.AddToClassList("aw-tile-kind"); cover.Add(kind);
                    image.RegisterCallback<ClickEvent>(_ => { selectedId = resource.id; UpdateSelectedResource(); UpdateComposer(); QueueSave(); }); card.Add(cover);
                    var title = new Label(resource.name) { enableRichText = false, tooltip = resource.name }; title.AddToClassList("aw-tile-title"); card.Add(title);
                    var footer = Row(); footer.AddToClassList("aw-tile-footer"); var state = new Label(WorkbenchData.StateName(resource.state)) { tooltip = resource.path, enableRichText = false }; state.AddToClassList("aw-muted"); state.style.flexGrow = 1; footer.Add(state);
                    var show = MakeButton("3D 预览", () => WorkbenchResourceGallery.OpenPreview(resources, resources.Where(ResourceRequested).Select(x => x.id), resource.id), "preview-" + resource.id);
                    show.SetEnabled(IsLibraryPrefab(resource)); show.AddToClassList("aw-tile-action"); footer.Add(show); card.Add(footer); row.Add(card);
                }
                for (int fill = row.childCount; fill < libraryColumns; fill++) { var empty = new VisualElement(); empty.AddToClassList("aw-asset-tile"); empty.style.visibility = Visibility.Hidden; row.Add(empty); }
            };
            libraryRoot.Add(cards);
            emptyResources = Wrapped("把本次 Prefab 或素材目录拖到这里。\n也可以直接输入修改需求。"); emptyResources.AddToClassList("aw-empty"); libraryRoot.Add(emptyResources);
            var note = new Label("勾选的素材随请求附上 · 点击只查看 · 不会立即安装"); note.AddToClassList("aw-library-note"); libraryRoot.Add(note);
            detailLabel = Wrapped(""); detailLabel.style.display = DisplayStyle.None; libraryRoot.Add(detailLabel);
            libraryResize = e => { if (e.target != cards) return; if (e.newRect.height <= 0) return; int next = Mathf.Clamp((int)(e.newRect.width / 220), 1, 5); float itemHeight = Mathf.Min(position.height < 520 ? 210 : 264, Mathf.Max(1, Mathf.Floor(e.newRect.height))); bool changed = next != libraryColumns || cards.fixedItemHeight != itemHeight; libraryColumns = next; if (changed) { cards.fixedItemHeight = itemHeight; RebuildLibrary(); } };
            cards.RegisterCallback(libraryResize);
            libraryLoading = libraryRoot.schedule.Execute(() =>
            {
                if (!windowVisible || materialPanel.resolvedStyle.display == DisplayStyle.None || materialSource != "local" || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                bool pending = false;
                foreach (var image in cards.Query<Image>("library-cover").ToList())
                {
                    if (image.image || !(image.userData is ResourceCard resource)) continue;
                    image.image = LibraryCover(resource); pending |= image.image == null;
                    var label = image.parent.Q<Label>("library-cover-state");
                    if (label != null) label.text = image.image ? "" : libraryAttempts < 59 ? "正在读取预览" + new[] { "", ".", "..", "..." }[libraryAttempts % 4] : "暂无已有封面";
                }
                libraryAttempts++; if (!pending || libraryAttempts >= 60) libraryLoading.Pause();
            }).Every(300);
            return libraryRoot;
        }

        static bool IsLibraryPrefab(ResourceCard resource) => IsLibraryAsset(resource.path) && Path.GetExtension(resource.path ?? "").Equals(".prefab", StringComparison.OrdinalIgnoreCase);
        static bool IsLibraryAsset(string path) => (path ?? "").StartsWith("Assets/", StringComparison.Ordinal) || (path ?? "").StartsWith("Packages/", StringComparison.Ordinal);
        Texture LibraryCover(ResourceCard resource)
        {
            if (!string.IsNullOrEmpty(resource.thumbnail)) return Thumbnail(resource);
            if (!IsLibraryAsset(resource.path)) return null;
            if (!libraryAssets.TryGetValue(resource.id, out var asset)) libraryAssets[resource.id] = asset = AssetDatabase.LoadAssetAtPath<Object>(resource.path);
            return asset ? AssetPreview.GetAssetPreview(asset) : null;
        }
        void RebuildLibrary()
        {
            libraryRows.Clear(); libraryRows.AddRange(Enumerable.Range(0, (visibleResources.Count + libraryColumns - 1) / libraryColumns)); cards?.Rebuild(); libraryAttempts = 0; libraryLoading?.Resume();
        }
        void ReleaseVisualUi()
        {
            ReleaseSourceUi();
            libraryLoading?.Pause(); libraryLoading = null;
            if (cards != null && libraryResize != null) cards.UnregisterCallback(libraryResize);
            libraryResize = null; libraryAssets.Clear();
        }
    }
}
