using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        JObject sourceStore;
        VisualElement directoryContent, panContent;
        Button directoryTab, panTab, scanDirectoryButton, stopSourceButton, addCatalogButton, panReadButton, panPreviousButton, panNextButton, panParentButton, panCancelButton, panManageButton;
        TextField directoryPathField, catalogFilter, panShareField, panCodeField, panNameField;
        Toggle sourceRecursive;
        Label sourceStatus, catalogEmpty, catalogCount, panStatus, panEmpty, panLocation, panReferenceCount, directoryEmpty, shareEmpty;
        ListView directoryList, catalogList, shareList, panList;
        readonly List<JObject> sourceDirectories = new List<JObject>(), catalogItems = new List<JObject>(), sourceShares = new List<JObject>(), panFiles = new List<JObject>();
        readonly List<int> catalogRows = new List<int>();
        readonly HashSet<string> catalogPicks = new HashSet<string>(), panPicks = new HashSet<string>();
        IVisualElementScheduledItem sourceWork, sourceCovers, sourceSave;
        CancellationTokenSource sourceCancellation;
        Task<JObject> sourceTask;
        WorkbenchSources.Progress directoryProgress;
        WorkbenchBaiduShare baiduSession;
        int sourceColumns = 2, sourceEpoch, sourceCoverAttempts;
        bool sourceNetwork;
        string sourceLoadError = "";
        string selectedDirectoryId = "", selectedShareId = "", sourceFilterText = "";
        EventCallback<GeometryChangedEvent> catalogResize;

        void LoadSourceStore()
        {
            if (sourceStore != null) return;
            try { sourceStore = WorkbenchSources.Load(); }
            catch (Exception e) { sourceLoadError = "来源记录暂不可读，原文件保留。请修复索引后重开窗口：" + e.Message; sourceStore = new JObject { ["directories"] = new JArray(), ["shares"] = new JArray(), ["references"] = new JArray() }; Toast(sourceLoadError); }
            selectedDirectoryId = WorkbenchData.Text(sourceStore["selected_directory"]); selectedShareId = WorkbenchData.Text(sourceStore["selected_share"]);
            catalogPicks.UnionWith((sourceStore["catalog_picks"] as JArray ?? new JArray()).Values<string>());
        }
        bool SaveSources()
        {
            if (sourceStore == null || !string.IsNullOrEmpty(sourceLoadError)) return false;
            sourceStore["selected_directory"] = selectedDirectoryId; sourceStore["selected_share"] = selectedShareId;
            sourceStore["catalog_picks"] = new JArray(catalogPicks);
            if (directoryPathField != null) sourceStore["directory_draft"] = directoryPathField.value;
            if (panShareField != null) sourceStore["share_draft"] = panShareField.value;
            if (panCodeField != null) sourceStore["code_draft"] = panCodeField.value;
            if (panNameField != null) sourceStore["name_draft"] = panNameField.value;
            try { WorkbenchSources.Save(sourceStore); return true; } catch (Exception e) { Toast("来源记录未保存：" + e.Message); return false; }
        }
        void QueueSourceSave() { sourceSave?.Pause(); sourceSave = rootVisualElement.schedule.Execute(() => SaveSources()).StartingIn(400); }
        JObject CurrentDirectory => sourceDirectories.FirstOrDefault(d => WorkbenchData.Text(d["id"]) == selectedDirectoryId);
        JObject CurrentShare => sourceShares.FirstOrDefault(d => WorkbenchData.Text(d["id"]) == selectedShareId);
        static string SourceType(string kind) => kind == "prefab" ? "预制体" : kind == "package" ? "Unity 安装包" : kind == "archive" ? "压缩包" : kind == "folder" ? "商品素材目录" : "插件目录";
        static string SizeLabel(long bytes) => bytes >= 1024 * 1024 * 1024 ? (bytes / (1024d * 1024 * 1024)).ToString("0.0") + " GB" : bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.0") + " MB" : bytes >= 1024 ? (bytes / 1024d).ToString("0.0") + " KB" : bytes + " B";
        static Label SourceLabel(string text, string style = "") { var label = new Label(text) { enableRichText = false }; if (!string.IsNullOrEmpty(style)) label.AddToClassList(style); return label; }

        VisualElement BuildDirectorySources()
        {
            LoadSourceStore(); var panel = new VisualElement { name = "directory-sources-panel" }; panel.AddToClassList("aw-source-content");
            var toolbar = Row(); toolbar.AddToClassList("aw-source-toolbar");
            directoryPathField = new TextField("目录") { name = "source-directory-path", value = WorkbenchData.Text(sourceStore["directory_draft"]), tooltip = "粘贴素材目录路径，或点击选择目录；不会读取整个磁盘。" }; directoryPathField.style.flexGrow = 1;
            directoryPathField.RegisterValueChangedCallback(_ => QueueSourceSave()); toolbar.Add(directoryPathField);
            toolbar.Add(MakeButton("选择目录", () => { string path = EditorUtility.OpenFolderPanel("选择素材目录", WorkbenchData.Project, ""); if (!string.IsNullOrEmpty(path)) { directoryPathField.SetValueWithoutNotify(path); RegisterDirectory(path); } }, "browse-source-directory"));
            toolbar.Add(MakeButton("添加", () => RegisterDirectory(directoryPathField.value), "add-source-directory")); panel.Add(toolbar);
            toolbar.Add(MakeButton("读取 Mio 素材库", PickMioLibrary, "read-mio-library"));
            var split = Row(); split.AddToClassList("aw-source-split"); split.style.alignItems = Align.Stretch;
            var sidebar = new VisualElement(); sidebar.AddToClassList("aw-source-sidebar"); sidebar.Add(SourceLabel("素材目录", "aw-source-section"));
            directoryList = new ListView { itemsSource = sourceDirectories, fixedItemHeight = 57, selectionType = SelectionType.None, name = "source-directory-list" };
            directoryList.style.flexGrow = 1; directoryList.style.minHeight = 0;
            directoryList.makeItem = () => new VisualElement();
            directoryList.bindItem = (container, index) => {
                container.Clear(); var item = sourceDirectories[index]; string id = WorkbenchData.Text(item["id"]), path = WorkbenchData.Text(item["path"]);
                var button = MakeButton("", () => SelectDirectory(id), "directory-" + id); button.AddToClassList("aw-source-record"); button.EnableInClassList("aw-source-current", id == selectedDirectoryId); button.tooltip = path;
                button.Add(SourceLabel(WorkbenchData.Text(item["source_kind"]) == "mio_library" ? "Mio 素材库" : Path.GetFileName(path), "aw-source-record-name")); button.Add(SourceLabel(item["read_at"] == null ? "尚未读取" : (item["items"] as JArray)?.Count + " 项 · 上次读取", "aw-muted")); container.Add(button);
            }; sidebar.Add(directoryList); directoryEmpty = SourceLabel("尚未添加目录", "aw-source-empty"); sidebar.Add(directoryEmpty);
            sidebar.Add(MakeButton("移除目录记录", RemoveDirectoryRecord, "remove-directory-record")); split.Add(sidebar);
            var results = new VisualElement(); results.AddToClassList("aw-source-results");
            var filterRow = Row(); filterRow.AddToClassList("aw-source-toolbar");
            catalogFilter = new TextField("筛选") { name = "catalog-filter", value = sourceFilterText }; catalogFilter.style.flexGrow = 1; catalogFilter.RegisterValueChangedCallback(e => { sourceFilterText = e.newValue; RefreshCatalog(); }); filterRow.Add(catalogFilter);
            sourceRecursive = new Toggle("含子目录") { value = true, name = "catalog-recursive" }; filterRow.Add(sourceRecursive);
            scanDirectoryButton = MakeButton("读取目录", StartDirectoryRead, "scan-source-directory"); scanDirectoryButton.AddToClassList("aw-primary"); filterRow.Add(scanDirectoryButton); results.Add(filterRow);
            catalogList = new ListView { itemsSource = catalogRows, fixedItemHeight = 190, selectionType = SelectionType.None, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, name = "source-catalog-grid" }; catalogList.style.flexGrow = 1; catalogList.style.minHeight = 0;
            catalogList.makeItem = () => { var row = Row(); row.AddToClassList("aw-tile-row"); return row; };
            catalogList.bindItem = BindCatalogRow; results.Add(catalogList);
            catalogEmpty = SourceLabel("添加一个素材目录，再点击读取。\n封面、贴图和色卡只作图片，不收录为安装资源。", "aw-source-empty"); results.Add(catalogEmpty); split.Add(results); panel.Add(split);
            var footer = Row(); footer.AddToClassList("aw-source-footer");
            catalogCount = SourceLabel("", "aw-muted"); catalogCount.style.flexGrow = 1; footer.Add(catalogCount);
            stopSourceButton = MakeButton("取消读取", CancelSourceRead, "cancel-source-read"); stopSourceButton.style.display = DisplayStyle.None; footer.Add(stopSourceButton);
            addCatalogButton = MakeButton("勾选后加入本次素材", AddCatalogPicks, "apply-catalog-picks"); addCatalogButton.AddToClassList("aw-primary"); footer.Add(addCatalogButton); panel.Add(footer);
            sourceStatus = SourceLabel("只读取选定目录 · 不自动导入、解压或安装", "aw-source-status"); panel.Add(sourceStatus);
            catalogResize = e => { if (e.target != catalogList || e.newRect.height <= 0) return; int columns = Mathf.Clamp((int)(e.newRect.width / (e.newRect.height < 180 ? 290 : 200)), 1, 5); float height = Mathf.Min(290, Mathf.Max(96, Mathf.Floor(e.newRect.height))); bool changed = columns != sourceColumns || catalogList.fixedItemHeight != height; sourceColumns = columns; if (changed) { catalogList.fixedItemHeight = height; RebuildCatalogRows(); } };
            catalogList.RegisterCallback(catalogResize);
            RefreshDirectoryRecords(); RefreshCatalog();
            int currentIndex = sourceDirectories.FindIndex(d => WorkbenchData.Text(d["id"]) == selectedDirectoryId);
            if (currentIndex >= 0) directoryList.schedule.Execute(() => directoryList.ScrollToItem(currentIndex)).StartingIn(80);
            if (WorkbenchData.Text(CurrentDirectory?["source_kind"]) == "mio_library") sourceStatus.text = "Mio 素材库 · 显示上次读取的名称与图片记录 · 点击读取可刷新";
            sourceCovers = panel.schedule.Execute(() => {
                if (!windowVisible || activePanel != "materials" || materialSource != "directory" || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                bool pending = false;
                foreach (var image in catalogList.Query<Image>("catalog-cover").ToList()) { if (image.image || !(image.userData is ResourceCard item)) continue; image.image = LibraryCover(item); pending |= image.image == null; var label = image.parent.Q<Label>("catalog-cover-state"); if (label != null) label.text = image.image ? "" : sourceCoverAttempts >= 29 ? "没有可读封面 · 点详情查看" : "正在读取预览" + new[] { "", ".", "..", "..." }[sourceCoverAttempts % 4]; }
                sourceCoverAttempts++; if (!pending || sourceCoverAttempts >= 30) sourceCovers.Pause();
            }).Every(350);
            if (!string.IsNullOrEmpty(sourceLoadError)) { panel.SetEnabled(false); catalogEmpty.text = sourceLoadError; catalogEmpty.tooltip = sourceLoadError; sourceStatus.text = "来源记录读取失败，已停止写入，原文件保留。"; }
            return panel;
        }
        bool RegisterDirectory(string path, string shareId = "")
        {
            bool registered = false;
            TryAction(() => {
                if (!string.IsNullOrEmpty(sourceLoadError)) throw new IOException(sourceLoadError);
                LoadSourceStore(); path = WorkbenchSources.DirectoryPath(path); var array = (JArray)sourceStore["directories"];
                var record = array.OfType<JObject>().FirstOrDefault(r => string.Equals(WorkbenchData.Text(r["path"]), path, StringComparison.OrdinalIgnoreCase));
                if (record == null) { if (array.Count >= 16) throw new IOException("最多保留 16 个本次素材目录，请移除不用的目录记录。"); record = new JObject { ["id"] = "dir-" + WorkbenchData.Hash(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())).Substring(0, 16), ["path"] = path, ["items"] = new JArray() }; array.Add(record); }
                if (!string.IsNullOrEmpty(shareId)) record["share_id"] = shareId;
                selectedDirectoryId = (string)record["id"]; directoryPathField?.SetValueWithoutNotify(path); RefreshDirectoryRecords(); RefreshCatalog(); if (!SaveSources()) { sourceStatus.text = "目录尚未保存，请查看下方提示；原记录保留。"; return; }
                sourceStatus.text = "目录已登记，点击读取后只查看该范围。";
                registered = true;
            });
            return registered;
        }
        void RefreshDirectoryRecords() { sourceDirectories.Clear(); sourceDirectories.AddRange(((JArray)sourceStore["directories"]).OfType<JObject>()); directoryList?.Rebuild(); if (directoryList != null) directoryList.style.display = sourceDirectories.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None; if (directoryEmpty != null) directoryEmpty.style.display = sourceDirectories.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None; scanDirectoryButton?.SetEnabled(CurrentDirectory != null && sourceTask == null); }
        void SelectDirectory(string id) { if (sourceTask != null) return; selectedDirectoryId = id; directoryPathField?.SetValueWithoutNotify(WorkbenchData.Text(CurrentDirectory?["path"])); RefreshDirectoryRecords(); RefreshCatalog(); SaveSources(); }
        void RemoveDirectoryRecord() { if (sourceTask != null || CurrentDirectory == null) return; CurrentDirectory.Remove(); selectedDirectoryId = ""; RefreshDirectoryRecords(); RefreshCatalog(); SaveSources(); sourceStatus.text = "已移除目录记录，磁盘文件保留。"; }
        void RefreshCatalog()
        {
            catalogItems.Clear(); catalogItems.AddRange((CurrentDirectory?["items"] as JArray ?? new JArray()).OfType<JObject>().Where(x => ((string)x["name"] + " " + (string)x["path"] + " " + (string)x["category"] + " " + x["bases"] + " " + x["tags"] + " " + (string)x["booth_id"]).IndexOf(sourceFilterText ?? "", StringComparison.OrdinalIgnoreCase) >= 0).OrderByDescending(x => !string.IsNullOrEmpty(WorkbenchData.Text(x["thumbnail"]))));
            RebuildCatalogRows(); catalogEmpty.style.display = catalogItems.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None; catalogList.style.display = catalogItems.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            catalogEmpty.text = CurrentDirectory == null ? "添加素材目录，再点击读取。\n封面、贴图和色卡不会被收录为安装资源。" : CurrentDirectory["read_at"] == null ? "已选目录，尚未读取。点击上方“读取目录”。" : "没有匹配的安装资源。可清空筛选或选择更具体的目录。";
            UpdateCatalogCount();
        }
        void RebuildCatalogRows() { catalogRows.Clear(); catalogRows.AddRange(Enumerable.Range(0, (catalogItems.Count + sourceColumns - 1) / sourceColumns)); catalogList?.Rebuild(); sourceCoverAttempts = 0; sourceCovers?.Resume(); }
        ResourceCard CatalogResource(JObject item) => new ResourceCard { id = WorkbenchData.Text(item["id"]), name = WorkbenchData.Text(item["name"]), path = WorkbenchData.Text(item["path"]), kind = WorkbenchData.Text(item["kind"]), thumbnail = WorkbenchData.Text(item["thumbnail"]), state = "selected", note = "本地素材，尚未交给原工作流处理。", productReference = new JObject {
            ["metadata_source"] = item["metadata_source"]?.DeepClone(), ["source_key"] = item["source_key"]?.DeepClone(), ["booth_id"] = item["booth_id"]?.DeepClone(), ["booth_url"] = item["booth_url"]?.DeepClone(),
            ["category"] = item["category"]?.DeepClone(), ["bases"] = item["bases"]?.DeepClone(), ["cover_source"] = item["cover_source"]?.DeepClone(), ["member_paths"] = item["member_paths"]?.DeepClone(), ["booth_match_source"] = item["booth_match_source"]?.DeepClone(), ["compatibility_status"] = "unverified" } };
        void BindCatalogRow(VisualElement row, int index)
        {
            row.Clear();
            for (int column = 0; column < sourceColumns; column++)
            {
                int n = index * sourceColumns + column; if (n >= catalogItems.Count) break; var item = catalogItems[n]; var resource = CatalogResource(item);
                bool compact = catalogList.fixedItemHeight < 180;
                var tile = new VisualElement(); tile.AddToClassList("aw-asset-tile"); tile.EnableInClassList("aw-asset-picked", catalogPicks.Contains(resource.id));
                var cover = new VisualElement(); cover.AddToClassList("aw-tile-cover"); cover.style.height = Mathf.Max(12, catalogList.fixedItemHeight - (compact ? 12 : 87));
                if (compact) { tile.style.flexDirection = FlexDirection.Row; cover.style.width = Mathf.Min(112, catalogList.fixedItemHeight - 12); }
                var image = new Image { image = LibraryCover(resource), userData = resource, name = "catalog-cover", scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("aw-tile-image"); cover.Add(image);
                cover.RegisterCallback<MouseDownEvent>(e => { if (e.button == 0 && !(e.target is Toggle) && (e.target as VisualElement)?.GetFirstAncestorOfType<Toggle>() == null) ShowCatalogDetails(item); });
                var label = SourceLabel(image.image ? "" : "正在读取预览…", "aw-tile-loading"); label.name = "catalog-cover-state"; cover.Add(label);
                var pick = new Toggle { value = catalogPicks.Contains(resource.id), tooltip = "选择 " + resource.name }; pick.AddToClassList("aw-tile-pick"); pick.RegisterValueChangedCallback(e => { if (e.newValue) catalogPicks.Add(resource.id); else catalogPicks.Remove(resource.id); tile.EnableInClassList("aw-asset-picked", e.newValue); UpdateCatalogCount(); QueueSourceSave(); }); cover.Add(pick); tile.Add(cover);
                var info = compact ? new VisualElement() : tile; if (compact) { info.style.flexGrow = 1; info.style.flexShrink = 1; info.style.minWidth = 0; tile.Add(info); }
                var title = SourceLabel(resource.name, "aw-tile-title"); title.tooltip = resource.path; info.Add(title);
                var facts = SourceLabel(WorkbenchData.Text(item["category"], SourceType(resource.kind)) + " · " + WorkbenchCatalogMetadata.CoverLabel(item), "aw-muted"); facts.tooltip = WorkbenchCatalogMetadata.CoverLabel(item) + "\n" + WorkbenchData.Text(item["thumbnail"]); facts.style.fontSize = 10; facts.style.height = 17; facts.style.overflow = Overflow.Hidden; facts.style.whiteSpace = WhiteSpace.NoWrap; info.Add(facts);
                var footer = Row(); footer.AddToClassList("aw-tile-footer"); var type = SourceLabel(SizeLabel((long?)item["bytes"] ?? 0), "aw-muted"); type.style.flexGrow = 1; if (!compact || !IsLibraryPrefab(resource)) footer.Add(type);
                footer.Add(MakeButton("详情 / 大图", () => ShowCatalogDetails(item), "catalog-detail-" + resource.id));
                if (IsLibraryPrefab(resource)) footer.Add(MakeButton(compact ? "3D" : "3D 预览", () => WorkbenchResourceGallery.OpenPreview(new[] { resource }, Array.Empty<string>(), resource.id), "catalog-preview-" + resource.id));
                info.Add(footer); row.Add(tile);
            }
            for (int fill = row.childCount; fill < sourceColumns; fill++) { var empty = new VisualElement(); empty.AddToClassList("aw-asset-tile"); empty.style.visibility = Visibility.Hidden; row.Add(empty); }
        }
        void UpdateCatalogCount() { int count = catalogItems.Count(x => catalogPicks.Contains(WorkbenchData.Text(x["id"]))); catalogCount.text = "显示 " + catalogItems.Count + " 项 · 已勾选 " + count + " 项"; addCatalogButton.SetEnabled(count > 0 && sourceTask == null); }
        void StartDirectoryRead()
        {
            if (WorkbenchData.Text(CurrentDirectory?["source_kind"]) == "mio_library") { StartMioRead(CurrentDirectory); return; }
            if (sourceTask != null || CurrentDirectory == null) return;
            var record = CurrentDirectory; string path = WorkbenchData.Text(record["path"]), project = WorkbenchData.Project; bool recursive = sourceRecursive.value;
            directoryProgress = new WorkbenchSources.Progress(); sourceNetwork = false; sourceCancellation = new CancellationTokenSource(); var token = sourceCancellation.Token;
            sourceTask = Task.Run(() => WorkbenchSources.Scan(path, project, recursive, token, directoryProgress), token);
            BeginSourceWork(result => { WorkbenchCatalogMetadata.PreserveFetched(record["items"] as JArray, result["items"] as JArray); record["items"] = result["items"].DeepClone(); record["read_at"] = result["read_at"]; record["recursive"] = recursive; record["limited"] = result["limited"]; RefreshDirectoryRecords(); RefreshCatalog(); SaveSources(); sourceStatus.text = directoryProgress.Label + ((bool?)result["limited"] == true ? " · 达到范围上限，请选择更小目录" : " · 读取完成") + ((int?)result["skipped"] > 0 ? " · 已跳过无法访问项和链接" : ""); });
        }
        void ShowCatalogDetails(JObject item)
        {
            WorkbenchCatalogDetailWindow.Open(item, () => { if (disposed) return; RefreshCatalog(); SaveSources(); });
        }
        void PickMioLibrary()
        {
            if (sourceTask != null) return;
            string file = EditorUtility.OpenFilePanel("选择 Mio 素材库 library.json（只读）", "", "json");
            if (string.IsNullOrEmpty(file)) return;
            TryAction(() => {
                var records = (JArray)sourceStore["directories"]; string id = "mio-db-" + WorkbenchData.Hash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToLowerInvariant())).Substring(0, 16);
                var record = records.OfType<JObject>().FirstOrDefault(x => WorkbenchData.Text(x["id"]) == id);
                if (record == null) { if (records.Count >= 16) throw new IOException("最多保留 16 个素材来源。"); record = new JObject { ["id"] = id, ["path"] = file, ["source_kind"] = "mio_library", ["items"] = new JArray() }; records.Add(record); }
                selectedDirectoryId = id; directoryPathField.SetValueWithoutNotify(file); RefreshDirectoryRecords(); directoryList.ScrollToItem(sourceDirectories.IndexOf(record)); RefreshCatalog(); StartMioRead(record);
            });
        }
        void StartMioRead(JObject record)
        {
            if (sourceTask != null || record == null || !string.IsNullOrEmpty(sourceLoadError)) return;
            string path = WorkbenchData.Text(record["path"]), project = WorkbenchData.Project;
            sourceCancellation = new CancellationTokenSource(); var token = sourceCancellation.Token; directoryProgress = new WorkbenchSources.Progress(); sourceNetwork = false;
            sourceTask = Task.Run(() => WorkbenchCatalogMetadata.ReadMio(path, project, token, directoryProgress), token);
            BeginSourceWork(result => { WorkbenchCatalogMetadata.PreserveFetched(record["items"] as JArray, result["items"] as JArray); foreach (var field in result.Properties()) record[field.Name] = field.Value.DeepClone(); RefreshCatalog();
                sourceStatus.text = SaveSources() ? "已只读载入 Mio：" + result["raw_records"] + " 条原记录 → " + ((JArray)result["items"]).Count + " 张本地商品卡片" + ((bool?)result["limited"] == true ? " · 达到读取上限" : "") + " · 素材与图片留在原位置" : "索引尚未保存，原记录保留。"; });
        }
        void AddCatalogPicks()
        {
            TryAction(() => {
                if (!string.IsNullOrEmpty(sourceLoadError)) throw new IOException(sourceLoadError);
                var chosen = catalogItems.Where(x => catalogPicks.Contains(WorkbenchData.Text(x["id"]))).ToArray();
                foreach (var item in chosen)
                {
                    string full = WorkbenchData.Text(item["full_path"]);
                    if (!File.Exists(full) && !Directory.Exists(full)) throw new IOException("素材已移动，请重新读取目录：" + WorkbenchData.Text(item["name"]));
                }
                var pending = chosen.Select(CatalogResource).Where(card => !resources.Any(r => string.Equals(r.path, card.path, StringComparison.OrdinalIgnoreCase)) && !localResources.Any(r => string.Equals(r.path, card.path, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (localResources.Count + pending.Length > 300) throw new IOException("本次素材最多 300 项，请缩小选择。");
                foreach (var card in pending) { localResources.Add(card); requestedResourceIds?.Add(card.id); }
                ReadTask(); SaveState(); SaveSources(); SelectMaterialSource("local"); Toast("已将 " + pending.Length + " 项加入本次素材；尚未安装或修改角色。");
            });
        }

        VisualElement BuildBaiduSources()
        {
            LoadSourceStore(); var panel = new VisualElement { name = "baidu-sources-panel" }; panel.AddToClassList("aw-source-content");
            var paste = Row(); paste.AddToClassList("aw-share-paste");
            panShareField = new TextField("分享文本") { name = "baidu-share-text", multiline = true, maxLength = 8192, value = WorkbenchData.Text(sourceStore["share_draft"]), tooltip = "粘贴百度网盘链接或带提取码的完整分享文本。" }; panShareField.style.flexGrow = 1; panShareField.RegisterValueChangedCallback(_ => QueueSourceSave()); paste.Add(panShareField);
            paste.Add(MakeButton("添加分享", RegisterBaiduShare, "add-baidu-share")); panel.Add(paste);
            var details = Row(); details.AddToClassList("aw-source-toolbar");
            panCodeField = new TextField("提取码") { name = "baidu-share-code", maxLength = 4, value = WorkbenchData.Text(sourceStore["code_draft"]) }; panCodeField.style.width = 125; panCodeField.style.flexGrow = 0; panCodeField.style.flexShrink = 0; panCodeField.style.flexBasis = StyleKeyword.Auto; panCodeField.RegisterValueChangedCallback(_ => QueueSourceSave()); details.Add(panCodeField);
            panNameField = new TextField("名称") { name = "baidu-share-name", maxLength = 160, value = WorkbenchData.Text(sourceStore["name_draft"]), tooltip = "可选，为这份分享起一个容易认的名字。" }; panNameField.style.flexGrow = 1; panNameField.RegisterValueChangedCallback(_ => QueueSourceSave()); details.Add(panNameField);
            panReadButton = MakeButton("读取文件列表", StartBaiduRead, "read-baidu-share"); panReadButton.AddToClassList("aw-primary"); details.Add(panReadButton); panel.Add(details);
            panManageButton = MakeButton("管理分享 ▾", ShowBaiduShareActions, "manage-baidu-share"); details.Add(panManageButton);
            var split = Row(); split.AddToClassList("aw-source-split"); split.style.alignItems = Align.Stretch;
            var sidebar = new VisualElement(); sidebar.AddToClassList("aw-source-sidebar"); sidebar.Add(SourceLabel("我的分享", "aw-source-section"));
            shareList = new ListView { itemsSource = sourceShares, fixedItemHeight = 55, selectionType = SelectionType.None, name = "baidu-share-list" }; shareList.style.flexGrow = 1; shareList.style.minHeight = 0;
            shareList.makeItem = () => new VisualElement(); shareList.bindItem = (row, i) => { row.Clear(); var share = sourceShares[i]; string id = WorkbenchData.Text(share["id"]); var button = MakeButton("", () => SelectBaiduShare(id), "share-" + id); button.AddToClassList("aw-source-record"); button.EnableInClassList("aw-source-current", id == selectedShareId); button.Add(SourceLabel(WorkbenchData.Text(share["name"], "百度分享"), "aw-source-record-name")); button.Add(SourceLabel(share["listing"] == null ? "尚未读取" : "已有文件记录", "aw-muted")); row.Add(button); }; sidebar.Add(shareList); shareEmpty = SourceLabel("尚未添加分享", "aw-source-empty"); sidebar.Add(shareEmpty);
            split.Add(sidebar);
            var results = new VisualElement(); results.AddToClassList("aw-source-results");
            var location = Row(); location.AddToClassList("aw-source-toolbar"); panParentButton = MakeButton("上一级", BaiduParent, "baidu-parent"); location.Add(panParentButton); panLocation = SourceLabel("文件列表", "aw-source-section"); panLocation.style.flexGrow = 1; location.Add(panLocation);
            panPreviousButton = MakeButton("上一页", () => ReadBaiduFolder(CurrentShare?["listing"] as JObject, -1), "baidu-previous-page"); location.Add(panPreviousButton);
            panNextButton = MakeButton("下一页", () => ReadBaiduFolder(CurrentShare?["listing"] as JObject, 1), "baidu-next-page"); location.Add(panNextButton); results.Add(location);
            panList = new ListView { itemsSource = panFiles, fixedItemHeight = 40, selectionType = SelectionType.None, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, name = "baidu-files" }; panList.style.flexGrow = 1; panList.style.minHeight = 0;
            panList.makeItem = () => Row(); panList.bindItem = BindBaiduFile; results.Add(panList);
            panEmpty = SourceLabel("粘贴分享链接并添加，\n再读取百度网盘的真实文件列表。", "aw-source-empty"); results.Add(panEmpty); split.Add(results); panel.Add(split);
            var footer = Row(); footer.AddToClassList("aw-source-footer"); panReferenceCount = SourceLabel("", "aw-muted"); panReferenceCount.style.flexGrow = 1; footer.Add(panReferenceCount);
            footer.Add(MakeButton("关联下载目录", LinkBaiduDirectory, "link-baidu-directory")); footer.Add(MakeButton("加入本次参考", AddBaiduReferences, "apply-baidu-references"));
            footer.Add(MakeButton("清空参考", () => { ((JArray)sourceStore["references"]).Clear(); SaveSources(); RefreshBaiduFiles(); UpdateComposer(); }, "clear-baidu-references"));
            panCancelButton = MakeButton("取消读取", CancelSourceRead, "cancel-baidu-read"); panCancelButton.SetEnabled(false); footer.Add(panCancelButton); panel.Add(footer);
            panStatus = SourceLabel("分享文件与下载状态分开记录；下载请在百度网盘完成，再关联本地目录。", "aw-source-status"); panel.Add(panStatus);
            RefreshShareRecords(); RefreshBaiduFiles();
            if (!string.IsNullOrEmpty(sourceLoadError)) { panel.SetEnabled(false); panEmpty.text = sourceLoadError; panEmpty.tooltip = sourceLoadError; panStatus.text = "来源记录读取失败，已停止写入，原文件保留。"; }
            return panel;
        }
        void RegisterBaiduShare()
        {
            TryAction(() => {
                if (!string.IsNullOrEmpty(sourceLoadError)) throw new IOException(sourceLoadError);
                if (sourceTask != null) return; var parsed = WorkbenchBaiduShare.Parse(panShareField.value, panCodeField.value); var array = (JArray)sourceStore["shares"];
                var share = array.OfType<JObject>().FirstOrDefault(x => WorkbenchData.Text(x["id"]) == (string)parsed["id"]);
                if (share == null) { if (array.Count >= 32) throw new IOException("本次最多记录 32 份分享。"); share = parsed; array.Add(share); }
                else { bool changed = WorkbenchData.Text(share["code"]) != WorkbenchData.Text(parsed["code"]); share["code"] = parsed["code"]; if (changed) share.Remove("listing"); }
                share["name"] = string.IsNullOrWhiteSpace(panNameField.value) ? WorkbenchData.Text(share["name"], "百度分享 · " + WorkbenchData.Text(parsed["short_id"]).Substring(Math.Max(0, WorkbenchData.Text(parsed["short_id"]).Length - 6))) : panNameField.value.Trim();
                selectedShareId = (string)share["id"]; panCodeField.SetValueWithoutNotify(WorkbenchData.Text(share["code"])); panPicks.Clear(); baiduSession?.Dispose(); baiduSession = null;
                RefreshShareRecords(); RefreshBaiduFiles(); panStatus.text = SaveSources() ? "分享已保存，尚未联网读取。点击“读取文件列表”。" : "分享尚未保存，请查看下方提示；原记录保留。";
            });
        }
        void RefreshShareRecords() { sourceShares.Clear(); sourceShares.AddRange(((JArray)sourceStore["shares"]).OfType<JObject>()); shareList?.Rebuild(); if (shareList != null) shareList.style.display = sourceShares.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None; if (shareEmpty != null) shareEmpty.style.display = sourceShares.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None; panReadButton?.SetEnabled(CurrentShare != null && sourceTask == null); panManageButton?.SetEnabled(CurrentShare != null && sourceTask == null); }
        void ShowBaiduShareActions()
        {
            if (CurrentShare == null || sourceTask != null) return;
            var share = CurrentShare; var menu = new GenericMenu();
            menu.AddItem(new GUIContent("在百度网盘打开分享"), false, () => Application.OpenURL(WorkbenchData.Text(share["url"])));
            menu.AddItem(new GUIContent("复制提取码"), false, () => EditorGUIUtility.systemCopyBuffer = WorkbenchData.Text(share["code"]));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("移除这份分享记录（保留网盘文件）"), false, () => { if (sourceTask != null) return; share.Remove(); selectedShareId = ""; panPicks.Clear(); baiduSession?.Dispose(); baiduSession = null; RefreshShareRecords(); RefreshBaiduFiles(); SaveSources(); panStatus.text = "已移除分享记录，原网盘文件及已附参考保留。"; });
            menu.ShowAsContext();
        }
        void SelectBaiduShare(string id)
        {
            if (sourceTask != null) return; selectedShareId = id; baiduSession?.Dispose(); baiduSession = null; panPicks.Clear();
            panShareField.SetValueWithoutNotify(WorkbenchData.Text(CurrentShare?["url"])); panCodeField.SetValueWithoutNotify(WorkbenchData.Text(CurrentShare?["code"])); panNameField.SetValueWithoutNotify(WorkbenchData.Text(CurrentShare?["name"]));
            RefreshShareRecords(); RefreshBaiduFiles(); SaveSources(); panStatus.text = !string.IsNullOrEmpty(WorkbenchData.Text(CurrentShare?["read_error"])) ? WorkbenchData.Text(CurrentShare["read_error"]) : CurrentShare?["listing"] == null ? "尚未读取分享。" : "显示上次读取的文件记录；点击读取才会联网刷新。";
        }
        void StartBaiduRead()
        {
            if (sourceTask != null || CurrentShare == null) return;
            var share = CurrentShare; var parsed = WorkbenchBaiduShare.Parse(WorkbenchData.Text(share["url"]), panCodeField.value); share["code"] = parsed["code"]; if (!SaveSources()) return; baiduSession?.Dispose(); baiduSession = new WorkbenchBaiduShare(); sourceCancellation = new CancellationTokenSource(); sourceNetwork = true;
            sourceTask = baiduSession.ReadRoot((JObject)share.DeepClone(), sourceCancellation.Token); BeginSourceWork(result => SaveBaiduListing(share, result));
        }
        void SaveBaiduListing(JObject share, JObject result) { share["listing"] = result; share.Remove("read_error"); panPicks.Clear(); RefreshShareRecords(); RefreshBaiduFiles(); bool saved = SaveSources(); panStatus.text = "已实际读取 " + panFiles.Count + " 个文件 / 文件夹 · " + (saved ? "只列出本页，尚未下载。" : "索引未保存，请查看下方提示；尚未下载。"); }
        void ReadBaiduFolder(JObject listing, int delta, string path = null)
        {
            if (sourceTask != null || CurrentShare == null || baiduSession == null) { panStatus.text = "请先点击读取文件列表，建立本次访问会话。"; return; }
            var share = CurrentShare; string location = path ?? WorkbenchData.Text(listing?["path"]); int page = path == null ? Math.Max(1, ((int?)listing?["page"] ?? 1) + delta) : 1;
            sourceCancellation = new CancellationTokenSource(); sourceNetwork = true; sourceTask = baiduSession.ReadFolder(location, page, sourceCancellation.Token); BeginSourceWork(result => SaveBaiduListing(share, result));
        }
        void BaiduParent() { string path = WorkbenchData.Text(CurrentShare?["listing"]?["path"]); int at = path.LastIndexOf('/'); ReadBaiduFolder(null, 0, at > 0 ? path.Substring(0, at) : ""); }
        void RefreshBaiduFiles()
        {
            panFiles.Clear(); var listing = CurrentShare?["listing"] as JObject; panFiles.AddRange((listing?["files"] as JArray ?? new JArray()).OfType<JObject>()); panList.Rebuild();
            panEmpty.style.display = panFiles.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None; panList.style.display = panFiles.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            string error = WorkbenchData.Text(CurrentShare?["read_error"]);
            panEmpty.text = CurrentShare == null ? "粘贴分享链接并添加，\n再读取百度网盘的真实文件列表。" : listing == null ? string.IsNullOrEmpty(error) ? "分享已登记，尚未读取。\n文件、下载和安装状态不会凭链接猜测。" : "未能读取文件列表\n" + error : "本页没有文件。";
            panLocation.text = string.IsNullOrEmpty(WorkbenchData.Text(listing?["path"])) ? "分享文件 · 第 " + ((int?)listing?["page"] ?? 1) + " 页" : Path.GetFileName(WorkbenchData.Text(listing["path"])) + " · 第 " + ((int?)listing["page"] ?? 1) + " 页";
            bool folders = sourceTask == null && baiduSession?.CanReadFolders == true;
            panPreviousButton.SetEnabled(folders && (int?)listing?["page"] > 1); panNextButton.SetEnabled(folders && (bool?)listing?["has_next"] == true); panParentButton.SetEnabled(folders && !string.IsNullOrEmpty(WorkbenchData.Text(listing?["path"])));
            panReferenceCount.text = "已勾选 " + panPicks.Count + " 项 · 本次参考 " + ((JArray)sourceStore["references"]).Count + " 项";
        }
        void BindBaiduFile(VisualElement row, int index)
        {
            row.Clear(); var file = panFiles[index]; string path = WorkbenchData.Text(file["remote_path"]), name = WorkbenchData.Text(file["name"]); bool folder = (bool?)file["directory"] == true;
            row.AddToClassList("aw-cloud-file-row"); var pick = new Toggle { value = panPicks.Contains(path), tooltip = string.IsNullOrEmpty(path) ? "返回数据没有文件路径，无法附加参考。" : "选择 " + name }; pick.SetEnabled(!string.IsNullOrEmpty(path)); pick.style.width = 22; pick.style.flexShrink = 0; pick.RegisterValueChangedCallback(e => { if (e.newValue) panPicks.Add(path); else panPicks.Remove(path); panReferenceCount.text = "已勾选 " + panPicks.Count + " 项 · 本次参考 " + ((JArray)sourceStore["references"]).Count + " 项"; }); row.Add(pick);
            var label = SourceLabel((folder ? "目录 · " : "文件 · ") + name, "aw-cloud-file-name"); label.tooltip = path; label.style.flexGrow = 1; row.Add(label); row.Add(SourceLabel(folder ? "" : SizeLabel((long?)file["bytes"] ?? 0), "aw-muted"));
            if (folder) { var open = MakeButton("进入", () => ReadBaiduFolder(null, 0, path), "baidu-open-folder"); open.SetEnabled(!string.IsNullOrEmpty(path) && baiduSession?.CanReadFolders == true && sourceTask == null); open.tooltip = "需要本次分享返回真实目录路径与分享身份；历史记录需先重新读取。"; row.Add(open); }
        }
        void AddBaiduReferences()
        {
            if (CurrentShare == null || panPicks.Count == 0) { panStatus.text = "先读取文件列表，再勾选要交给原改模流程参考的条目。"; return; }
            var references = (JArray)sourceStore["references"];
            string shareUrl = WorkbenchData.Text(CurrentShare["url"]);
            var pending = panFiles.Where(x => !string.IsNullOrEmpty(WorkbenchData.Text(x["remote_path"])) && panPicks.Contains(WorkbenchData.Text(x["remote_path"])) && !references.OfType<JObject>().Any(r => WorkbenchData.Text(r["share_url"]) == shareUrl && WorkbenchData.Text(r["remote_path"]) == WorkbenchData.Text(x["remote_path"]))).ToArray();
            if (pending.Length == 0) { panStatus.text = "勾选条目已在本次参考中，没有重复添加。"; return; }
            if (references.Count + pending.Length > 32) { panStatus.text = "本次最多附 32 项网盘参考，请缩小选择；本次未添加。"; return; }
            foreach (var file in pending)
            {
                references.Add(new JObject { ["source_type"] = "baidu_share", ["share_record_id"] = CurrentShare["id"], ["share_url"] = shareUrl, ["name"] = file["name"], ["remote_path"] = file["remote_path"], ["directory"] = file["directory"], ["availability"] = "remote_listing_only", ["downloaded"] = false, ["installed"] = false });
            }
            bool saved = SaveSources(); RefreshBaiduFiles(); UpdateComposer(); if (saved) Toast("网盘参考已加入下次请求；不是已下载或已安装素材，提取码不会发给外部模型。"); else panStatus.text = "本次参考尚未保存，关闭前请解决下方提示；原记录保留。";
        }
        void LinkBaiduDirectory()
        {
            if (CurrentShare == null) { panStatus.text = "请先添加并选中一份分享。"; return; }
            string path = EditorUtility.OpenFolderPanel("选择这份分享已下载的本地目录", WorkbenchData.Project, ""); if (string.IsNullOrEmpty(path)) return;
            if (!RegisterDirectory(path, selectedShareId)) return; CurrentShare["local_directory"] = path; SaveSources(); SelectMaterialSource("directory"); StartDirectoryRead();
        }
        JArray SourceReferences() { LoadSourceStore(); return (JArray)sourceStore["references"].DeepClone(); }

        void BeginSourceWork(Action<JObject> complete)
        {
            int epoch = ++sourceEpoch; var task = sourceTask; bool network = sourceNetwork;
            scanDirectoryButton?.SetEnabled(false); panReadButton?.SetEnabled(false); panCancelButton?.SetEnabled(network); stopSourceButton.style.display = network ? DisplayStyle.None : DisplayStyle.Flex;
            sourceWork?.Pause(); sourceWork = rootVisualElement.schedule.Execute(() => {
                if (disposed || epoch != sourceEpoch) return;
                if (!task.IsCompleted) { if (network) panStatus.text = "正在读取百度分享… · 可取消，不会自动重试"; else sourceStatus.text = directoryProgress.Label + " · 正在读取"; return; }
                sourceWork.Pause(); sourceTask = null; sourceCancellation?.Dispose(); sourceCancellation = null; panCancelButton?.SetEnabled(false); stopSourceButton.style.display = DisplayStyle.None;
                try { complete(task.GetAwaiter().GetResult()); }
                catch (OperationCanceledException) { if (network) { baiduSession?.Dispose(); baiduSession = null; panStatus.text = "读取已取消或超时；上次成功记录保留。"; } else sourceStatus.text = "本次目录读取已取消；上次成功索引保留。"; }
                catch (Exception e) { string error = e is System.Net.Http.HttpRequestException ? "网络连接失败，请检查网络后手动读取。" : e is Newtonsoft.Json.JsonException ? "百度网盘返回格式无法读取，没有记录为读取成功。" : e.Message; if (network) { baiduSession?.Dispose(); baiduSession = null; panStatus.text = error; if (CurrentShare != null) CurrentShare["read_error"] = error; } else sourceStatus.text = error; SaveSources(); }
                RefreshDirectoryRecords(); RefreshShareRecords(); RefreshBaiduFiles(); UpdateCatalogCount();
            }).Every(150);
        }
        void CancelSourceRead() { sourceCancellation?.Cancel(); }
        void ReleaseSourceUi()
        {
            SaveSources(); sourceSave?.Pause(); sourceSave = null; sourceEpoch++; sourceCancellation?.Cancel(); sourceCancellation?.Dispose(); sourceCancellation = null;
            if (sourceTask != null) sourceTask.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); sourceTask = null;
            sourceWork?.Pause(); sourceWork = null; sourceCovers?.Pause(); sourceCovers = null;
            if (catalogList != null && catalogResize != null) catalogList.UnregisterCallback(catalogResize); catalogResize = null; baiduSession?.Dispose(); baiduSession = null;
        }
    }
}
