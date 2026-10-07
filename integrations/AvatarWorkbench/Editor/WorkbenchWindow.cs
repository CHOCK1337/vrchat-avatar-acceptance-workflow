using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow : EditorWindow
    {
        [SerializeField] GameObject target;
        string taskPath = "", manifestPath = "", sourceKind = "editing_state", message = "", selectedId = "";
        JObject task = new JObject(), stableContext, draft;
        readonly List<ResourceCard> resources = new List<ResourceCard>();
        readonly List<ResourceCard> localResources = new List<ResourceCard>();
        readonly List<JObject> images = new List<JObject>();
        readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
        WorkbenchPreview preview;
        int previewTargetId;
        Texture2D frozen, historical, before;
        JObject historicalInfo, beforeInfo;
        Rect annotation;
        Vector2 dragStart;
        bool drawing, comparing, busy, restoring, disposed;
        string toast = "选择角色后显示真实画面。", sourceNotice = "暂无预览", receiptSignature = "";
        string toastFeedbackId = "";
        DateTime taskTime;
        int historyIndex = -1;
        ObjectField targetField;
        Label projectLabel, candidateLabel, connectionLabel, previewLabel, detailLabel, taskLabel, noticeLabel, emptyResources;
        VisualElement feedbackPanel, previewPanel;
        IMGUIContainer canvas;
        ListView cards;
        TextField input;
        Button refreshButton, freezeButton, compareButton, candidateButton;
        IVisualElementScheduledItem poll, saveJob;
        EventCallback<GeometryChangedEvent> sizeCallback;

        public string BindingPath => string.IsNullOrEmpty(taskPath) ? WorkbenchData.SelectionBinding : taskPath;

        [MenuItem("Tools/改模工作台（Avatar Workbench）")]
        public static void Open() { var w = GetWindow<WorkbenchWindow>(); w.titleContent = new GUIContent("改模工作台"); w.Show(); w.Focus(); }
        [MenuItem("Window/改模工作台（Avatar Workbench）")]
        public static void OpenWindow() => Open();

        void OnEnable()
        {
            disposed = false; minSize = new Vector2(560, 360); wantsMouseMove = true;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += BeforeReload;
            EditorApplication.playModeStateChanged += PreviewPlayModeChanged;
            RestoreState();
            StartSceneSync();
        }
        void OnDisable()
        {
            ReleaseVisualUi();
            SaveState(); disposed = true; poll?.Pause(); saveJob?.Pause();
            StopCodexConnection();
            StopSceneSync();
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            EditorApplication.quitting -= BeforeReload;
            EditorApplication.playModeStateChanged -= PreviewPlayModeChanged;
            if (sizeCallback != null) rootVisualElement.UnregisterCallback(sizeCallback);
            ReleaseGraphics();
        }
        void BeforeReload() { SaveState(); poll?.Pause(); StopSceneSync(); StopCodexConnection(); ReleaseGraphics(); }
        void OnBecameInvisible() { windowVisible = false; CancelSourceRead(); StopPreviewCamera(); CloseRadialMenu(); poll?.Pause(); sceneRefreshJob?.Pause(); StopBoothRequests(true); }
        void OnLostFocus() { StopPreviewCamera(); CloseRadialMenu(); }
        void OnBecameVisible() { windowVisible = true; poll?.Resume(); if (followScene && preview == null) sceneBindingPending = true; if (refreshPending || sceneBindingPending) QueueSceneRefresh(); canvas?.MarkDirtyRepaint(); }
        void ReleaseGraphics()
        {
            StopPreviewCamera(); CloseRadialMenu();
            StopBoothRequests(true);
            preview?.Dispose(); preview = null;
            DestroyTexture(ref frozen); DestroyTexture(ref historical); DestroyTexture(ref before);
            foreach (var texture in thumbnails.Values) if (texture) Object.DestroyImmediate(texture);
            thumbnails.Clear();
        }
        static void DestroyTexture(ref Texture2D texture) { if (texture) Object.DestroyImmediate(texture); texture = null; }

        public void CreateGUI()
        {
            poll?.Pause(); saveJob?.Pause();
            if (sizeCallback != null) rootVisualElement.UnregisterCallback(sizeCallback);
            // Rebuilding only replaces visual controls; no Unity project mutation.
            ReleaseGraphics();
            busy = IsBusy(); BuildInterface();
            TryAction(ReadTask);
            BindSceneIfAvailable();
            TryAction(LoadManifest);
            TryAction(LoadPreview);
            TryAction(RestoreDraftTexture);
            if (target && toast == "选择角色后显示真实画面。")
                Toast(draft != null ? "已恢复上次未提交的圈选。写好需求后保存即可。" : "先看模型，再在下方写一句你想修改的内容。");
            UpdateLabels(); RenderFeedback();
            poll = null; nextLocalPollAt = EditorApplication.timeSinceStartup + .25;
            if (WorkbenchCodex.Link() != null && !sendingCodex) ReconnectCodex();
        }

        static VisualElement Row() { var e = new VisualElement(); e.style.flexDirection = FlexDirection.Row; e.style.alignItems = Align.Center; e.style.flexShrink = 0; e.style.minWidth = 0; return e; }
        static VisualElement Pane(float width) { var e = new VisualElement(); e.AddToClassList("aw-pane"); if (width > 0) { e.style.width = width; e.style.flexShrink = 0; } return e; }
        static Label Heading(string text) { var l = new Label(text) { enableRichText = false }; l.AddToClassList("aw-heading"); return l; }
        static Label Wrapped(string text) { var l = new Label(text) { enableRichText = false }; l.style.whiteSpace = WhiteSpace.Normal; l.style.flexShrink = 0; l.style.minWidth = 0; return l; }
        Button MakeButton(string text, Action action, string name) { return new Button(() => TryAction(action)) { text = text, name = name }; }
        void TryAction(Action action) { try { action(); } catch (Exception e) { Toast("这一步没有完成：" + e.Message); } }
        void Toast(string text) { toast = text; if (text == "已发送给 Codex，等待它实际读取；不用再复制粘贴。" && activeDeliveryReceipt != null) toastFeedbackId = WorkbenchData.Text(activeDeliveryReceipt["feedback_id"]); if (noticeLabel != null) { noticeLabel.text = text; noticeLabel.tooltip = text; } }
        static string KindName(string kind) { switch (kind) { case "base": case "body": case "avatar": return "素体"; case "outfit": return "衣服"; case "hair": return "发型"; case "makeup": return "妆容"; case "accessory": return "饰品"; case "plugin": return "插件"; default: return "素材"; } }

        public void UseSelection()
        {
            var go = Selection.activeGameObject;
            if (!go) throw new InvalidOperationException("请在 Hierarchy 或 Project 中选中角色。");
            var root = go.transform;
            for (var t = go.transform; t; t = t.parent)
                if (t.GetComponents<Component>().Any(c => c && c.GetType().Name == "VRCAvatarDescriptor")) { root = t; break; }
            if (!root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any()) root = go.transform.root;
            SetTarget(root.gameObject);
        }
        public void SetTarget(GameObject go) => SetTargetSource(go, "editing_state");
        void SetTargetSource(GameObject go, string kind)
        {
            if (IsBusy()) { Toast("Codex 正在修改现场，先保留当前画面。稍后刷新目标。"); targetField?.SetValueWithoutNotify(target); return; }
            if (go && !go.GetComponentsInChildren<Renderer>(true).Any()) throw new InvalidOperationException("所选对象没有模型网格，请选择 Avatar 根对象。");
            sceneBindingNotice = "";
            target = go; sourceKind = go && EditorUtility.IsPersistent(go) ? "candidate_prefab" : kind;
            if (kind == "candidate_prefab") followScene = false;
            else
            {
                followScene = true; sceneBindingPending = true;
                if (go && EditorUtility.IsPersistent(go))
                {
                    var linked = SceneTargetFor(go);
                    if (linked) { target = linked; sourceKind = "editing_state"; }
                    else { followScene = false; sourceKind = "candidate_prefab"; }
                }
                else { pendingSelection = go; BindSceneIfAvailable(); }
            }
            targetField?.SetValueWithoutNotify(target); ClearHistory(); LoadPreview(); SaveState(); UpdateLabels();
        }
        void PickTask()
        {
            var menu = new GenericMenu(); string directory = Path.Combine(WorkbenchData.Project, ".avatar-assembly");
            int count = 0;
            if (Directory.Exists(directory))
                foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Where(p => new[] { "quicktask.json", "assemblytask.json", "task.json" }.Contains(Path.GetFileName(p).ToLowerInvariant())).Take(80))
                {
                    try { var data = WorkbenchData.Read(path); string title = WorkbenchData.Text(data["title"] ?? data["avatar"]);
                        if (string.IsNullOrEmpty(title)) title = Path.GetFileNameWithoutExtension(WorkbenchData.Text((data["candidate"] as JObject)?["path"] ?? (data["candidate"] as JObject)?["id"] ?? data["candidate"] ?? data["new_prefab"]));
                        if (string.IsNullOrEmpty(title)) title = "已有改模任务";
                        string selectedPath = path; menu.AddItem(new GUIContent(title.Replace('/', '／') + " · " + Path.GetFileName(Path.GetDirectoryName(path))), path == taskPath, () => TryAction(() => BindTask(selectedPath))); count++; }
                    catch (IOException) { } catch (Newtonsoft.Json.JsonException) { }
                }
            if (count == 0) menu.AddDisabledItem(new GUIContent("当前工程没有已有任务"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("不绑定任务 · 仅提交本次修改请求"), string.IsNullOrEmpty(taskPath), () => TryAction(UnbindTask));
            menu.AddItem(new GUIContent("从其他位置选择已有任务…"), false, () => TryAction(() => { string p = EditorUtility.OpenFilePanel("选择本次改模任务（只读）", WorkbenchData.Project, "json"); if (!string.IsNullOrEmpty(p)) BindTask(p); }));
            menu.ShowAsContext();
        }
        public void UnbindTask()
        {
            taskPath = ""; manifestPath = ""; task = new JObject(); ReadTask(); LoadManifest(); ClearHistory();
            if (target) LoadPreview(); SaveState(); UpdateLabels(); RenderFeedback(); Toast("本次请求不绑定已有任务；目标和选择会自动附上。");
        }
        public void BindTask(string path)
        {
            var data = WorkbenchData.Read(path);
            string project = WorkbenchData.Text(data["project_path"] ?? data["project"]);
            if (!string.IsNullOrEmpty(project) && !string.Equals(Path.GetFullPath(project).TrimEnd('\\', '/'), Path.GetFullPath(WorkbenchData.Project).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("这个任务属于其他 Unity 工程，请选择当前工程的任务。");
            if (data["format"] == null && data["candidate"] == null && data["new_prefab"] == null && data["task_id"] == null)
                throw new InvalidOperationException("这个文件没有任务或候选信息；请选择已有任务记录。");
            taskPath = Path.GetFullPath(path); taskTime = DateTime.MinValue;
            ReadTask(); manifestPath = WorkbenchData.Text(data["screenshot_manifest"] ?? data["image_manifest"]);
            if (!string.IsNullOrEmpty(manifestPath)) manifestPath = WorkbenchData.Resolve(manifestPath, Path.GetDirectoryName(taskPath));
            LoadManifest(); RestoreDraftTexture(); if (followScene) BindSceneIfAvailable(); LoadPreview(); SaveState(); UpdateLabels(); RenderFeedback(); Toast("已绑定已有任务，画面自动跟随当前角色。");
        }
        void ReadTask()
        {
            if (string.IsNullOrEmpty(taskPath)) task = new JObject();
            else { var next = WorkbenchData.Read(taskPath); task = next; taskTime = File.GetLastWriteTimeUtc(taskPath); }
            resources.Clear(); resources.AddRange(WorkbenchData.Resources(task));
            foreach (var local in localResources)
            {
                int existing = resources.FindIndex(x => string.Equals(x.path, local.path, StringComparison.OrdinalIgnoreCase));
                if (existing < 0) resources.Add(local);
                else if (local.productReference != null) resources[existing] = local;
            }
            UpdateResourceList(); UpdateLabels();
        }
        string TaskId => WorkbenchData.Text(task["task_id"], string.IsNullOrEmpty(taskPath) ? "selection-" + Path.GetFileName(Path.GetDirectoryName(WorkbenchData.WindowFile)) : Path.GetFileName(Path.GetDirectoryName(taskPath)));
        string CandidatePath => WorkbenchData.Text((task["candidate"] as JObject)?["path"] ?? (task["candidate"] as JObject)?["asset_path"] ?? (task["candidate"] as JObject)?["id"] ?? task["candidate"] ?? task["new_prefab"]);
        void UseTaskCandidate()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(CandidatePath);
            if (!go) throw new IOException("任务未指向当前工程中可读取的候选 Prefab。");
            SetTargetSource(go, "candidate_prefab");
        }
        void LoadPreview()
        {
            StopPreviewCamera(); CloseRadialMenu();
            if (IsBusy()) { UpdateLabels(); return; }
            if (!target) { preview?.Dispose(); preview = null; previewTargetId = 0; stableContext = null; watchedObjects.Clear(); watchedAssets.Clear(); sourceNotice = sceneBindingNotice; refreshPending = false; ReadPreviewControls(); UpdateLabels(); canvas?.MarkDirtyRepaint(); return; }
            refreshingPreview = true;
            var next = new WorkbenchPreview();
            try
            {
                next.Load(target);
                if (preview != null && previewTargetId == target.GetInstanceID()) next.KeepCameraFrom(preview);
                var nextContext = Context(next);
                WatchPreviewSource();
                preview?.Dispose(); preview = next;
                previewTargetId = target.GetInstanceID();
                sourceKind = EditorUtility.IsPersistent(target) ? "candidate_prefab" : IsPrefabEditingTarget(target) ? "prefab_stage" : "editing_state";
                sourceNotice = !string.IsNullOrEmpty(preview.SourceNotice) ? preview.SourceNotice : sourceKind == "candidate_prefab" ? "磁盘候选 Prefab · 独立于场景，尚未运行 MA/NDMF" : sourceKind == "prefab_stage" ? "同一 Prefab 编辑对象 · 当前编辑网格，非最终构建" : "同一场景角色 · 原始编辑网格，非最终构建";
                if (!string.IsNullOrEmpty(sceneBindingNotice)) sourceNotice = sceneBindingNotice;
                stableContext = nextContext; refreshPending = false; ReadPreviewControls();
            }
            catch { next.Dispose(); throw; }
            finally { refreshingPreview = false; }
            canvas?.MarkDirtyRepaint(); UpdateLabels();
        }
        JObject Context(WorkbenchPreview sourcePreview = null)
        {
            var c = WorkbenchData.Candidate(task); var t = target ? WorkbenchData.Target(target, sourceKind) : new JObject();
            string assetPath = WorkbenchData.Text(t["asset_path"]);
            if (target)
            {
                if (string.IsNullOrEmpty(assetPath) || !string.Equals(assetPath, CandidatePath, StringComparison.Ordinal)) c = new JObject { ["id"] = string.IsNullOrEmpty(assetPath) ? WorkbenchData.Text(t["global_id"]) : assetPath, ["name"] = target.name };
                if (t["asset_sha256"] != null) c["sha256"] = t["asset_sha256"].DeepClone();
                t["scene_dirty"] = target.scene.IsValid() && target.scene.isDirty;
                if (target.scene.IsValid())
                {
                    string sceneFile = string.IsNullOrEmpty(target.scene.path) ? "" : Path.Combine(WorkbenchData.Project, target.scene.path);
                    t["scene_sha256"] = File.Exists(sceneFile) ? WorkbenchData.FileHash(sceneFile) : null;
                    t["scene_unsaved"] = string.IsNullOrEmpty(target.scene.path);
                    if (target.scene.path == WorkbenchData.Text(task["new_scene"])) t["task_candidate_asset_path"] = CandidatePath;
                    c["scene_sha256"] = t["scene_sha256"]?.DeepClone();
                }
            }
            return new JObject { ["task_id"] = TaskId, ["task_path"] = taskPath, ["binding"] = BindingPath, ["project_path"] = WorkbenchData.Project,
                ["task_revision"] = task["task_revision"]?.DeepClone(), ["candidate"] = c, ["target"] = t, ["snapshot_id"] = Guid.NewGuid().ToString("N"), ["resource_id"] = selectedId,
                ["selected_resources"] = new JArray(resources.Where(ResourceRequested).Select(x => x.Json())),
                ["selected_product_references"] = new JArray(boothReferences.Select(x => x.DeepClone())),
                ["selected_source_references"] = SourceReferences(),
                ["known_controls"] = (sourcePreview ?? preview)?.RenderedControls?.DeepClone() ?? new JObject { ["status"] = "unknown", ["preview_output"] = (sourcePreview ?? preview)?.SceneProxyCount > 0 ? "existing_ndmf_scene_preview" : "source_meshes", ["note"] = "可复用已有场景功能预览；未读取开关数值，不推断最终构建。" } };
        }
        void RefreshAll()
        {
            ReadTask(); LoadManifest(); if (followScene) BindSceneIfAvailable(); if (!historical) LoadPreview(); RenderFeedback(); UpdateLabels();
            Toast(IsBusy() ? "Codex 正在修改现场，显示上一份稳定预览。" : "已刷新本次任务与目标；没有运行构建。");
        }

        public void AddDroppedPaths(string[] paths)
        {
            int added = 0, ignored = 0; bool truncated = false;
            foreach (string original in paths ?? Array.Empty<string>())
            {
                string path = original.Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(path))
                {
                    var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { path });
                    foreach (string guid in prefabs.Take(300)) if (AddResource(AssetDatabase.GUIDToAssetPath(guid))) added++;
                    if (prefabs.Length == 0) { if (AssetDatabase.FindAssets("t:MonoScript", new[] { path }).Length > 0 && AddResource(path, "plugin")) added++; else ignored++; }
                    if (prefabs.Length > 300) truncated = true;
                }
                else if (Directory.Exists(path))
                {
                    // Only the chosen folder's top level is read. A pictures-only folder is not an installation resource.
                    string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                    if (full == Path.GetPathRoot(full).TrimEnd('\\', '/')) { ignored++; continue; }
                    bool usable = Directory.EnumerateFiles(full, "*", SearchOption.TopDirectoryOnly).Take(1000).Any(f => new[] { ".prefab", ".unitypackage", ".cs", ".asmdef" }.Contains(Path.GetExtension(f).ToLowerInvariant()) || Path.GetFileName(f) == "package.json");
                    if (usable && AddResource(full, "directory")) added++; else ignored++;
                }
                else if (new[] { ".prefab", ".unitypackage" }.Contains(Path.GetExtension(path).ToLowerInvariant())) { if (AddResource(path)) added++; }
                else ignored++;
            }
            ReadTask(); SaveState(); Toast("已加入 " + added + " 项选择；点击卡片只查看。" + (ignored > 0 ? " 已忽略贴图、色卡、封面或不支持的文件。" : "") + (truncated ? " 目录过大，本次只读取前 300 项，请缩小目录。" : ""));
        }
        bool AddResource(string path, string kind = "other")
        {
            if (localResources.Count >= 300) throw new IOException("本次选择最多 300 项，请缩小素材范围。");
            if (resources.Any(x => x.path == path) || localResources.Any(x => x.path == path)) return false;
            var card = new ResourceCard { id = "ui-" + WorkbenchData.Hash(Encoding.UTF8.GetBytes(path)).Substring(0, 16), name = Path.GetFileNameWithoutExtension(path), path = path, kind = kind, state = "selected", note = "本地选择，尚未交给原工作流处理。" };
            localResources.Add(card); requestedResourceIds?.Add(card.id); return true;
        }
        void RemoveLocalSelection() { localResources.RemoveAll(r => r.id == selectedId); selectedId = ""; ReadTask(); SaveState(); }
        Texture Thumbnail(ResourceCard r)
        {
            if (!string.IsNullOrEmpty(r.thumbnail))
            {
                string path = WorkbenchData.Resolve(r.thumbnail, string.IsNullOrEmpty(taskPath) ? WorkbenchData.Project : Path.GetDirectoryName(taskPath));
                string key = WorkbenchCoverImage.Fingerprint(path);
                if (thumbnails.TryGetValue(key, out var texture)) return texture;
                if (thumbnails.Count >= 32) {
                    var visible = rootVisualElement.Query<Image>().ToList().Select(i => i.image).ToArray();
                    string evict = thumbnails.Keys.FirstOrDefault(k => !thumbnails[k] || !visible.Contains(thumbnails[k]));
                    if (evict == null) return null; Object.DestroyImmediate(thumbnails[evict]); thumbnails.Remove(evict);
                }
                try { texture = WorkbenchCoverImage.Load(path, 600); thumbnails[key] = texture; return texture; } catch (IOException) { thumbnails[key] = null; return null; } catch (UnauthorizedAccessException) { thumbnails[key] = null; return null; }
            }
            return string.IsNullOrEmpty(r.path) ? EditorGUIUtility.IconContent("Prefab Icon").image : AssetDatabase.GetCachedIcon(r.path);
        }

        void PickManifest()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("选择已有 PNG / JPEG 图片…"), false, () => TryAction(() =>
            {
                string path = EditorUtility.OpenFilePanelWithFilters("选择已有原图", WorkbenchData.Project, new[] { "原图", "png,jpg,jpeg" }); if (string.IsNullOrEmpty(path)) return;
                var texture = WorkbenchData.LoadImage(path); Object.DestroyImmediate(texture);
                string file = Path.Combine(Path.GetDirectoryName(WorkbenchData.WindowFile), "selected-image-manifest.json");
                WorkbenchData.Write(file, new JObject { ["items"] = new JArray(new JObject { ["path"] = path, ["title"] = Path.GetFileName(path), ["view"] = "视角未记录",
                    ["state"] = "原图状态未记录", ["capture_kind"] = "unknown", ["candidate"] = new JObject(), ["sha256"] = WorkbenchData.FileHash(path) }) });
                BindImages(file); ShowHistory(0); Toast("显示已有图片；未提供候选身份，按参考/历史图处理。");
            }));
            menu.AddItem(new GUIContent("选择已有截图清单…"), false, () => TryAction(() => { string path = EditorUtility.OpenFilePanel("选择已有原图清单", string.IsNullOrEmpty(taskPath) ? WorkbenchData.Project : Path.GetDirectoryName(taskPath), "json"); if (!string.IsNullOrEmpty(path)) BindImages(path); }));
            menu.ShowAsContext();
        }
        public void BindImages(string path)
        {
            if (!(WorkbenchData.Read(path)["items"] is JArray)) throw new IOException("截图清单需要包含 items。");
            manifestPath = Path.GetFullPath(path); ClearHistory(); LoadManifest(); SaveState();
        }
        void LoadManifest()
        {
            images.Clear();
            if (!string.IsNullOrEmpty(manifestPath))
            {
                var data = WorkbenchData.Read(manifestPath);
                if (!(data["items"] is JArray items)) throw new IOException("请选择包含 items 的已有截图清单。不会根据文件名推断视角或候选。");
                if (items.Count > 80) throw new IOException("本次截图清单最多 80 张，请只列相关原图。");
                foreach (var item in items.OfType<JObject>()) images.Add((JObject)item.DeepClone());
            }
            UpdateLabels();
        }
        void ShowHistory(int index)
        {
            var info = images[index]; string path = WorkbenchData.Resolve(WorkbenchData.Text(info["path"]), Path.GetDirectoryName(manifestPath));
            var texture = WorkbenchData.LoadImage(path);
            string actualHash = WorkbenchData.FileHash(path);
            if (info["sha256"] != null && WorkbenchData.Text(info["sha256"]) != actualHash) { Object.DestroyImmediate(texture); throw new IOException("原图与清单哈希不一致，拒绝将新图当作旧图。"); }
            DestroyTexture(ref historical); historical = texture; historicalInfo = (JObject)info.DeepClone(); historicalInfo["path"] = path; historicalInfo["sha256"] = actualHash; historyIndex = index;
            sourceNotice = "历史原图 · " + WorkbenchData.Identity(info["candidate"] as JObject) + " · " + WorkbenchData.Text(info["capture_kind"], "处理层级未知");
            comparing = false; UpdateLabels(); canvas.MarkDirtyRepaint();
        }
        void ClearHistory() { DestroyTexture(ref historical); historicalInfo = null; historyIndex = -1; comparing = false; }
        void SetBeforeImage()
        {
            if (!historical || historicalInfo == null) { Toast("请先选择一张真实的已有原图，再设为前图。"); return; }
            var next = WorkbenchData.LoadImage(WorkbenchData.Text(historicalInfo["path"])); DestroyTexture(ref before); before = next; beforeInfo = (JObject)historicalInfo.DeepClone();
            SaveState(); UpdateLabels(); Toast("已将这张原图指定为修改前；前图身份保持原记录。");
        }

        void DrawPreview()
        {
            if (canvas == null) return;
            var area = new Rect(0, 0, canvas.contentRect.width, canvas.contentRect.height);
            if (area.width < 2 || area.height < 2) return;
            EditorGUI.DrawRect(area, new Color(.08f, .095f, .13f));
            Texture texture = frozen ? frozen : historical ? historical : preview?.Frame;
            if (!frozen && !historical && !busy && Event.current.type == EventType.Repaint && preview != null)
            {
                try { bool wasEmpty = preview.Frame == null; texture = preview.Render(comparing ? new Rect(0, 0, area.width * .5f - 2, area.height) : area); if (wasEmpty && texture != null) rootVisualElement.schedule.Execute(UpdateLabels); }
                catch (Exception e) { Toast("预览未完成：" + e.Message); }
            }
            if (!texture) { GUI.Label(new Rect(14, 22, area.width - 28, 80), busy ? "Codex 正在修改现场。暂无可复用的稳定画面。" : sceneBindingPending ? "正在读取当前场景角色…" : sceneBindingNotice, EditorStyles.wordWrappedLabel); return; }
            Rect currentArea = area;
            if (comparing && before && !frozen)
            {
                var first = new Rect(0, 0, area.width * .5f - 2, area.height); DrawImage(first, before);
                currentArea = new Rect(area.width * .5f + 2, 0, area.width * .5f - 2, area.height);
                GUI.Label(new Rect(8, 8, first.width - 16, 40), "指定前图（历史）\n" + WorkbenchData.Identity(beforeInfo?["candidate"] as JObject));
            }
            Rect imageRect = DrawImage(currentArea, texture);
            if (historical && !frozen)
                GUI.Label(new Rect(currentArea.x + 8, 8, currentArea.width - 16, 24), "已有截图 · 不是实时模型");
            if (frozen)
            {
                GUI.Label(new Rect(8, 8, area.width - 16, 20), "冻结画面 · 拖动框选 · " + WorkbenchData.Identity(draft?["candidate"] as JObject));
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && imageRect.Contains(e.mousePosition)) { drawing = true; dragStart = Normalize(e.mousePosition, imageRect); annotation = new Rect(dragStart, Vector2.zero); e.Use(); }
                if (drawing && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
                {
                    var end = Normalize(e.mousePosition, imageRect); annotation = Rect.MinMaxRect(Mathf.Min(dragStart.x, end.x), Mathf.Min(dragStart.y, end.y), Mathf.Max(dragStart.x, end.x), Mathf.Max(dragStart.y, end.y));
                    if (e.type == EventType.MouseUp) { drawing = false; SaveState(); } e.Use(); canvas.MarkDirtyRepaint();
                }
                if (annotation.width > 0 && annotation.height > 0)
                {
                    Rect selected = new Rect(imageRect.x + annotation.x * imageRect.width, imageRect.y + annotation.y * imageRect.height, annotation.width * imageRect.width, annotation.height * imageRect.height);
                    EditorGUI.DrawRect(selected, new Color(.25f, .85f, 1, .12f));
                    EditorGUI.DrawRect(new Rect(selected.x, selected.y, selected.width, 2), Color.cyan); EditorGUI.DrawRect(new Rect(selected.x, selected.yMax - 2, selected.width, 2), Color.cyan);
                    EditorGUI.DrawRect(new Rect(selected.x, selected.y, 2, selected.height), Color.cyan); EditorGUI.DrawRect(new Rect(selected.xMax - 2, selected.y, 2, selected.height), Color.cyan);
                }
            }
            else if (!historical && !busy && preview != null)
            {
                HandlePreviewCamera(area);
            }
        }
        static Vector2 Normalize(Vector2 p, Rect r) => new Vector2(Mathf.Clamp01((p.x - r.x) / r.width), Mathf.Clamp01((p.y - r.y) / r.height));
        static Rect DrawImage(Rect area, Texture image)
        {
            float scale = Mathf.Min(area.width / image.width, area.height / image.height); var r = new Rect(area.center.x - image.width * scale * .5f, area.center.y - image.height * scale * .5f, image.width * scale, image.height * scale);
            GUI.DrawTexture(r, image, ScaleMode.StretchToFill, false); return r;
        }

        public void BeginFeedback()
        {
            StopPreviewCamera(); CloseRadialMenu();
            SelectPanel("preview");
            if (draft != null && frozen) { Toast("正在圈选这份冻结画面；其候选身份保持不变。"); return; }
            if (draft != null) { RestoreDraftTexture(); Toast("已恢复尚未提交的冻结画面。提交或保留后可继续预览。"); UpdateLabels(); canvas.MarkDirtyRepaint(); return; }
            JObject context, image;
            if (historical && historicalInfo != null)
            {
                frozen = WorkbenchData.LoadImage(WorkbenchData.Text(historicalInfo["path"]));
                context = Context(); context["candidate"] = historicalInfo["candidate"]?.DeepClone() ?? new JObject();
                context["target"] = historicalInfo["source"]?.DeepClone() ?? historicalInfo["target"]?.DeepClone() ?? new JObject { ["status"] = "unknown", ["note"] = "历史图未提供原目标，不绑定当前角色。" };
                if (historicalInfo["task_id"] != null) { context["task_id"] = historicalInfo["task_id"].DeepClone(); context["image_task_id"] = historicalInfo["task_id"].DeepClone(); }
                foreach (string field in new[] { "binding", "task_path", "task_revision", "snapshot_id" }) if (historicalInfo[field] != null) context[field] = historicalInfo[field].DeepClone();
                image = (JObject)historicalInfo.DeepClone(); image["source_path"] = image["path"]?.DeepClone(); image["source_sha256"] = image["sha256"]?.DeepClone();
                if (historicalInfo["known_controls"] != null) context["known_controls"] = historicalInfo["known_controls"].DeepClone();
            }
            else
            {
                if (preview == null || stableContext == null) throw new InvalidOperationException("暂无真实画面，可直接输入自然语言请求。");
                frozen = preview.Capture(); context = (JObject)stableContext.DeepClone();
                if (preview.RenderedControls != null) context["known_controls"] = preview.RenderedControls.DeepClone();
                image = new JObject { ["view"] = preview.RenderedView?.DeepClone(), ["state"] = preview.RenderedControls != null ? ((bool?)preview.RenderedControls["physics_simulated"] == true ? "Unity 原生动画与 PhysBone 预览；实际帧状态随原图保存" : "Unity 原生动画预览；该帧尚未模拟 PhysBone") : "编辑态快照；功能开关未知", ["capture_kind"] = preview.RenderedControls != null ? "unity_native_gpu_skinning" : WorkbenchData.Text(context["target"]?["preview_kind"], sourceKind),
                    ["candidate"] = context["candidate"]?.DeepClone(), ["source"] = context["target"]?.DeepClone() };
            }
            // Resource selection belongs to this feedback, while target/image belong to the frozen frame.
            context["selected_resources"] = Context()["selected_resources"]?.DeepClone(); context["resource_id"] = selectedId;
            context["selected_product_references"] = new JArray(boothReferences.Select(x => x.DeepClone()));
            context["selected_source_references"] = SourceReferences();
            byte[] bytes = frozen.EncodeToPNG(); string folder = Path.Combine(WorkbenchData.Space(WorkbenchData.Text(context["binding"])), "drafts"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png"); File.WriteAllBytes(file, bytes);
            image["path"] = file; image["sha256"] = WorkbenchData.Hash(bytes); image["width"] = frozen.width; image["height"] = frozen.height;
            context["image"] = image; context["frozen_at"] = WorkbenchData.Now; draft = context; annotation = new Rect(); comparing = false;
            SaveState(); UpdateLabels(); Toast("画面已冻结。拖动圈出问题，下面写一句说明，再保存。"); canvas.MarkDirtyRepaint();
        }
        void LeaveAnnotation() { DestroyTexture(ref frozen); ClearHistory(); UpdateLabels(); canvas.MarkDirtyRepaint(); Toast(draft != null ? "未提交的圈选与原图已保留；“指出问题”可继续。" : "返回模型预览。"); }
        void DiscardDraft() { draft = null; annotation = new Rect(); DestroyTexture(ref frozen); SaveState(); UpdateLabels(); canvas.MarkDirtyRepaint(); Toast("已放弃本条未提交圈选；原图和已保存反馈保留。"); }
        public void SubmitFeedback() { SaveFeedback(); }
        JObject SaveFeedback()
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 5000) throw new InvalidOperationException("请输入 1–5000 字的修改意见。");
            JObject context = draft != null ? (JObject)draft.DeepClone() : Context();
            string binding = WorkbenchData.Text(context["binding"]); string request = Guid.NewGuid().ToString("N");
            string id = WorkbenchData.Hash(Encoding.UTF8.GetBytes(Path.GetFileName(WorkbenchData.Space(binding)) + "|" + request)).Substring(0, 32);
            JObject image = context["image"] as JObject;
            if (image != null && WorkbenchData.FileHash(WorkbenchData.Text(image["path"])) != WorkbenchData.Text(image["sha256"])) throw new IOException("冻结原图已变化，反馈未保存。");
            var entry = new JObject { ["format"] = "avatar-ui-feedback-v1", ["id"] = id, ["request_id"] = request,
                ["created_at"] = WorkbenchData.Now, ["task_id"] = context["task_id"]?.DeepClone(), ["task_revision"] = context["task_revision"]?.DeepClone(),
                ["task_path"] = context["task_path"]?.DeepClone(), ["project_path"] = context["project_path"]?.DeepClone(),
                ["candidate"] = context["candidate"]?.DeepClone(), ["snapshot_id"] = context["snapshot_id"]?.DeepClone(),
                ["target"] = context["target"]?.DeepClone(), ["selected_resources"] = context["selected_resources"]?.DeepClone(),
                ["selected_product_references"] = context["selected_product_references"]?.DeepClone() ?? new JArray(),
                ["selected_source_references"] = context["selected_source_references"]?.DeepClone() ?? new JArray(),
                ["resource_id"] = context["resource_id"]?.DeepClone(), ["image"] = image?.DeepClone(), ["known_controls"] = context["known_controls"]?.DeepClone(),
                ["rect"] = image != null && annotation.width > .001f && annotation.height > .001f ? new JObject { ["x"] = annotation.x, ["y"] = annotation.y, ["w"] = annotation.width, ["h"] = annotation.height } : null,
                ["category"] = image == null ? "change" : "visual", ["message"] = message, ["status"] = "received", ["execution_started"] = false,
                ["model_route"] = WorkbenchModels.Route(),
                ["stale_relative_to_current"] = !WorkbenchData.SameCandidate(context["candidate"] as JObject, Context()["candidate"] as JObject),
                ["notice"] = "图片坐标不是网格坐标。已发送不代表已读；已回应不代表修复通过；没有上传授权。" };
            if (entry["rect"] is JObject rect) entry["pixel_rect"] = new JObject { ["x"] = Mathf.RoundToInt((float)rect["x"] * (int)image["width"]), ["y"] = Mathf.RoundToInt((float)rect["y"] * (int)image["height"]), ["w"] = Mathf.RoundToInt((float)rect["w"] * (int)image["width"]), ["h"] = Mathf.RoundToInt((float)rect["h"] * (int)image["height"]) };
            WorkbenchData.Write(Path.Combine(WorkbenchData.Inbox(binding), id + ".json"), entry);
            draft = null; annotation = new Rect(); DestroyTexture(ref frozen); message = ""; input?.SetValueWithoutNotify("");
            SaveState(); RenderFeedback(); UpdateLabels(); Toast("已保存，等待 Codex 读取" + (binding != BindingPath ? "（原图所属任务的收件箱）" : "") + "。" ); canvas?.MarkDirtyRepaint();
            return entry;
        }

        bool IsBusy() => (bool?)WorkbenchData.Consumer(BindingPath)?["busy"] == true || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode;
        void PollLocalReceipts()
        {
            if (disposed || !windowVisible || rootVisualElement.panel == null) return;
            PollSceneSync();
            bool next = IsBusy(); if (busy != next) { busy = next; UpdateLabels(); canvas?.MarkDirtyRepaint(); }
            TryAction(() =>
            {
                if (!string.IsNullOrEmpty(taskPath) && File.Exists(taskPath) && File.GetLastWriteTimeUtc(taskPath) != taskTime)
                {
                    ReadTask(); QueueSceneRefresh(); Toast("任务有新进展，空闲时自动同步当前角色；圈选原图保留原版本。");
                }
                string signature = WorkbenchData.FeedbackSignature(BindingPath);
                if (signature != receiptSignature)
                {
                    receiptSignature = signature; RenderFeedback();
                    // A read/reply receipt is not a model change. Keep the current trial
                    // pose; actual source edits still arrive through the source watchers.
                    if (preview?.Gesture == null) QueueSceneRefresh();
                }
                UpdateLabels();
            });
        }
        void UpdateLabels()
        {
            if (projectLabel == null) return;
            busy = IsBusy();
            projectLabel.text = "工程：" + WorkbenchData.Project + "\nUnity：" + Application.unityVersion + "\n任务：" + BindingPath + "\n" + McpStatus();
            var consumer = WorkbenchData.Consumer(BindingPath);
            connectionBox.EnableInClassList("aw-connected", codexLink != null);
            if (connectingCodex)
            {
                connectionLabel.text = "正在连接 Codex…"; connectionHelp.text = "核对已绑定的现有会话，不调用模型。";
            }
            else if (codexLink != null)
            {
                connectionLabel.text = "Codex 已连接";
                connectionHelp.text = "下方输入后点“发送给 Codex”，原话、角色、候选和素材自动附上。";
            }
            else if (consumer == null)
            {
                connectionLabel.text = busy ? "Unity 忙碌 · 暂不刷新画面" : "Codex 尚未连接";
                connectionHelp.text = string.IsNullOrEmpty(codexError) ? "首次绑定会话后可在这里直接发送；未连接时请求仍会保存。" : codexError;
            }
            else
            {
                connectionLabel.text = (bool?)consumer["busy"] == true ? "Codex 正在处理 · 先保留当前画面" : busy ? "Unity 忙碌 · 暂不刷新画面" : "Codex 接收会话已登记";
                connectionHelp.text = "请求会在该会话下次读取时处理；收到真实答复后显示在右侧。";
            }
            helpButton.text = WorkbenchCodex.Link() == null ? "连接 Codex" : "重新连接";
            helpButton.SetEnabled(!connectingCodex && !sendingCodex);
            ApplyModelLabels();
            connectionLabel.tooltip = WorkbenchData.Text(codexLink?["title"]) + "\n" + connectionHelp.text + "\n" + McpStatus();
            var shown = (frozen ? draft?["candidate"] : historical ? historicalInfo?["candidate"] : stableContext?["candidate"]) as JObject;
            candidateLabel.text = "画面版本：" + WorkbenchData.Identity(shown ?? WorkbenchData.Candidate(task)) + "\n" + sourceNotice;
            previewLabel.text = frozen ? "已冻结 · 在图上圈选" : historical ? "已有截图" : busy ? "之前的稳定画面" : preview?.Gesture?.PhysicsRunning == true ? "Unity PhysBone · 运行中" : preview?.Gesture?.Moving == true ? "正在切换 · 保留上次画面" : preview?.Gesture != null ? "试穿与姿势 · 临时预览" : "编辑态快照";
            previewLabel.tooltip = candidateLabel.text;
            if (sceneSourceLabel != null)
            {
                bool showingPreviousTarget = preview != null && target && previewTargetId != target.GetInstanceID();
                sceneSourceLabel.text = frozen ? "圈选原图 · 保留原版本" : historical ? "历史截图 · 不跟随现场" : busy ? "正在修改 · 保留上次稳定画面" : showingPreviousTarget ? "旧画面 · " + WorkbenchData.Text(stableContext?["target"]?["name"]) : !target ? sceneBindingNotice : preview?.Gesture != null ? "独立试穿 · " + target.name + " · 非构建" : !EditorUtility.IsPersistent(target) ? (IsPrefabEditingTarget(target) ? "同步 Prefab 编辑模式 · " : "同步场景 · ") + target.name + (preview?.SceneProxyCount > 0 ? " · MA/NDMF 预览" : " · 编辑态") : "磁盘 Prefab · 独立于场景";
                sceneSourceLabel.tooltip = candidateLabel.text;
            }
            compareButton?.SetEnabled(before && !frozen);
            if (compareButton != null) { compareButton.text = comparing ? "退出对比" : "前后对比"; compareButton.tooltip = before ? "与已选择的修改前原图对照" : "没有修改前原图；可从来源菜单打开历史图并设为对照。"; }
            freezeButton?.SetEnabled(frozen || historical || preview?.Frame != null || draft != null);
            if (freezeButton != null) freezeButton.text = frozen ? "正在圈选" : draft != null ? "继续圈选" : "圈出问题";
            bool canMoveCamera = preview != null && !historical && !frozen && !busy;
            cameraViewButton?.SetEnabled(canMoveCamera);
            foreach (string name in new[] { "view-正面", "view-侧面", "view-背面", "focus-全身", "focus-头部", "focus-上身", "focus-鞋子" }) rootVisualElement.Q<Button>(name)?.SetEnabled(canMoveCamera);
            var returnButton = rootVisualElement.Q<Button>("return-preview"); if (returnButton != null) { returnButton.SetEnabled(frozen || historical); returnButton.style.display = frozen || historical ? DisplayStyle.Flex : DisplayStyle.None; }
            UpdatePreviewControls();
            rootVisualElement.Q<Button>("discard-feedback")?.SetEnabled(draft != null);
            refreshButton?.SetEnabled(!busy); candidateButton?.SetEnabled(!string.IsNullOrEmpty(CandidatePath) && !busy);
            targetField?.SetEnabled(!busy); rootVisualElement.Q<Button>("use-selection")?.SetEnabled(!busy);
            var req = task["explicit_requirements"] as JArray;
            string phase = WorkbenchData.Text(task["phase"] ?? task["status"]);
            activityLabel.text = string.IsNullOrEmpty(taskPath) ? "从下方写下第一条需求" : WorkbenchUiRules.PhaseLabel(phase);
            var parts = new List<string>();
            if (req != null && req.Count > 0) parts.Add(string.Join("\n", req.Take(3).Select(x => "• " + WorkbenchData.Text(x))));
            if (req != null && req.Count > 3) parts.Add("另有 " + (req.Count - 3) + " 项要求，见技术详情。");
            string nextAction = WorkbenchData.Text(task["next_action"]);
            if (!string.IsNullOrWhiteSpace(nextAction)) parts.Add("接下来：" + WorkbenchUiRules.Short(nextAction, 160));
            if (task["known_issues"] is JArray issues && issues.Count > 0) parts.Add("需要注意：" + WorkbenchUiRules.Short(WorkbenchData.Text(issues[0]), 160));
            taskLabel.text = parts.Count > 0 ? string.Join("\n\n", parts) : "想安装素材就先选择素材；只想调整外观，可以直接写需求。";
            taskDetails.text = "原任务阶段：" + phase + "\n原任务要求：\n" + (req == null ? "未记录" : string.Join("\n", req.Select(x => WorkbenchData.Text(x))));
            taskLabel.tooltip = string.IsNullOrEmpty(taskPath) ? "" : "显示来自原任务的记录，不等于当前画面已经通过验收。";
            UpdateComposer();
        }
        static string McpStatus()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) { var type = assembly.GetType("UnityMCP.Editor.UnityMCPConnection"); if (type != null) return (bool?)type.GetProperty("IsListening")?.GetValue(null) == true ? "UnityMCP 已监听" : "UnityMCP 未监听"; }
            return "当前工程未发现 UnityMCP";
        }
        void RenderFeedback(List<JObject> entries = null)
        {
            if (feedbackPanel == null) return;
            feedbackPanel.Clear(); entries = entries ?? WorkbenchData.Feedback(BindingPath);
            var sentEntry = string.IsNullOrEmpty(toastFeedbackId) ? null : entries.FirstOrDefault(x => WorkbenchData.Text(x["id"]) == toastFeedbackId);
            bool receiptToast = toast == "已发送给 Codex，等待它实际读取；不用再复制粘贴。" || toast == "Codex 已实际读取，等待后续回应。" ||
                toast == "Codex 已确认正在处理，请查看实际回应与模型。" || toast == "Codex 已回应，可在“需求与回复”查看；回应不代表模型修复通过。";
            if (receiptToast && sentEntry != null)
            {
                string latestStatus = WorkbenchData.Text(sentEntry["status"]);
                if (latestStatus == "addressed") Toast("Codex 已回应，可在“需求与回复”查看；回应不代表模型修复通过。");
                else if (latestStatus == "seen") Toast("Codex 已实际读取，等待后续回应。");
                else if (latestStatus == "processing_feedback") Toast("Codex 已确认正在处理，请查看实际回应与模型。");
            }
            int waiting = entries.Count(x => WorkbenchData.Text(x["status"]) != "addressed");
            feedbackTab.text = "需求与回复" + (waiting > 0 ? " (" + waiting + ")" : "");
            feedbackPanel.Add(Heading("我的请求"));
            if (entries.Count == 0)
            {
                var empty = Wrapped("还没有请求。\n在下方写需求并保存，回复会显示在这里。"); empty.AddToClassList("aw-muted"); feedbackPanel.Add(empty); return;
            }
            foreach (var entry in entries.Take(showAllFeedback ? 30 : 5))
            {
                var card = new VisualElement(); card.AddToClassList("aw-feedback-card");
                var delivery = WorkbenchCodex.Delivery(BindingPath, WorkbenchData.Text(entry["id"]));
                string deliveryState = WorkbenchData.Text(delivery?["state"]), status = WorkbenchData.Text(entry["status"]);
                string stateText = status == "received" && deliveryState == "accepted" ? "已发送 · 等待读取" : status == "received" && deliveryState == "sending" ? "正在发送" : status == "received" && deliveryState == "failed" ? "已保存 · 发送未完成" : status == "received" && deliveryState == "unknown" ? "已保存 · 发送回执未知" : WorkbenchUiRules.ReceiptLabel(status);
                var state = new Label(stateText) { enableRichText = false }; state.AddToClassList("aw-feedback-state"); card.Add(state);
                var text = Wrapped(WorkbenchUiRules.Short(WorkbenchData.Text(entry["message"]), 110)); text.AddToClassList("aw-feedback-text"); card.Add(text);
                string reply = WorkbenchData.Text(entry["reply"]);
                if (!string.IsNullOrEmpty(reply)) { var r = Wrapped(((string)entry["model_route"]?["type"] == "api" ? "API：" : "Codex：") + WorkbenchUiRules.Short(reply, 135)); r.AddToClassList("aw-feedback-reply"); card.Add(r); }
                else { var r = Wrapped("已保存。等 Codex 实际读取后更新状态。"); r.AddToClassList("aw-muted"); card.Add(r); }
                var detail = new Foldout { text = "查看完整内容", value = false };
                detail.Add(Wrapped(WorkbenchData.Text(entry["message"])));
                if (!string.IsNullOrEmpty(reply)) detail.Add(Wrapped("回复：" + reply));
                detail.Add(Wrapped("对应画面：" + WorkbenchData.Identity(entry["candidate"] as JObject)));
                detail.Add(Wrapped("有回复不代表修复通过，请查看实际模型。"));
                if (entry["model_response"] is JObject) { detail.Add(Wrapped("API 仅答复 / 提出步骤；正式模型未由 API 修改。")); detail.Add(MakeButton("继续交给原改模流程（Codex）", () => ContinueApiRequest(BindingPath, entry), "continue-workflow-" + WorkbenchData.Text(entry["id"]))); }
                if (entry["image"] is JObject recorded) detail.Add(MakeButton("查看这条请求的原图", () => ShowRecordedImage(recorded), "request-image-" + WorkbenchData.Text(entry["id"])));
                if ((status == "received" || (status == "seen" && (string)entry["model_route"]?["type"] == "api")) && deliveryState != "accepted" && deliveryState != "sending")
                    card.Add(MakeButton((string)entry["model_route"]?["type"] == "api" ? "重试此 API 请求" : "重新发送给 Codex", () => RetryRequest(BindingPath, entry), "retry-" + WorkbenchData.Text(entry["id"])));
                card.Add(detail); feedbackPanel.Add(card);
            }
            if (entries.Count > 5) feedbackPanel.Add(MakeButton(showAllFeedback ? "只看最近 5 条" : "显示最近 30 条", () => { showAllFeedback = !showAllFeedback; RenderFeedback(); }, "toggle-feedback-history"));
        }
        void CopyInstruction() { CopyInstructionFor(BindingPath); Toast("指令已复制。粘贴到 Codex 后，它才能接手这些请求。"); }
        void CopyInstructionFor(string binding)
        {
            EditorGUIUtility.systemCopyBuffer = "请读取并处理改模工作台中尚未回应的请求。继续使用现有工作流和已连接的 UnityMCP，只读取一次相关收件箱，不修改模型配置或扩大测试范围。\n工程：" + WorkbenchData.Project + "\n绑定路径：" + binding + "\n反馈目录：" + WorkbenchData.Inbox(binding) + "\n在实际安装的 AvatarWorkbench/Tools/codex_feedback.py 执行 inbox --task <以上绑定路径>。实际读取后回写 seen，有答复后回写 addressed；两个状态都不等于修复通过。此条接手指令本身不授予上传权限。";
        }
        void ExportFeedback()
        {
            string path = EditorUtility.SaveFilePanel("导出本任务反馈（含原图路径）", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AvatarWorkbench_Feedback", "json");
            if (string.IsNullOrEmpty(path)) return;
            WorkbenchData.Write(path, new JObject { ["format"] = "avatar-feedback-export-v1", ["binding"] = BindingPath, ["feedback"] = new JArray(WorkbenchData.Feedback(BindingPath)) });
            Toast("已导出反馈记录；随文件共享时请附上对应原图，或使用接入文档的 ZIP 导出命令。");
        }
        void QueueSave() { saveJob?.Pause(); saveJob = rootVisualElement.schedule.Execute(SaveState).StartingIn(400); }
        void SaveState()
        {
            if (restoring) return;
            try
            {
                if (draft != null) { draft["rect"] = new JObject { ["x"] = annotation.x, ["y"] = annotation.y, ["w"] = annotation.width, ["h"] = annotation.height }; }
                var state = new JObject { ["task_path"] = taskPath, ["manifest_path"] = manifestPath, ["message"] = message, ["selected_id"] = selectedId,
                    ["panel_mode"] = activePanel, ["source_kind"] = sourceKind, ["follow_scene"] = followScene, ["target_global_id"] = target ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() : "", ["target_asset_path"] = target ? AssetDatabase.GetAssetPath(target) : "",
                    ["local_resources"] = new JArray(localResources.Select(x => x.Json())), ["selected_resource_ids"] = requestedResourceIds == null ? null : new JArray(requestedResourceIds), ["draft"] = draft?.DeepClone(), ["before"] = beforeInfo?.DeepClone(),
                    ["booth"] = BoothState(), ["booth_references"] = new JArray(boothReferences.Select(x => x.DeepClone())) };
                WorkbenchData.Write(WorkbenchData.WindowFile, state);
                if (string.IsNullOrEmpty(taskPath)) WorkbenchData.Write(WorkbenchData.SelectionBinding, new JObject { ["format"] = "avatar-workbench-selection-v1", ["project_path"] = WorkbenchData.Project, ["task_id"] = TaskId, ["target"] = target ? new JObject { ["name"] = target.name, ["global_id"] = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(), ["asset_path"] = AssetDatabase.GetAssetPath(target), ["preview_kind"] = sourceKind } : new JObject() });
            }
            catch (Exception e) { Toast("界面草稿未保存：" + e.Message); }
        }
        void RestoreState()
        {
            if (!File.Exists(WorkbenchData.WindowFile)) return;
            restoring = true;
            try
            {
                var state = WorkbenchData.Read(WorkbenchData.WindowFile); taskPath = WorkbenchData.Text(state["task_path"]); manifestPath = WorkbenchData.Text(state["manifest_path"]);
                activePanel = WorkbenchData.Text(state["panel_mode"], "preview"); if (!new[] { "preview", "materials", "feedback" }.Contains(activePanel)) activePanel = "preview";
                message = WorkbenchData.Text(state["message"]); selectedId = WorkbenchData.Text(state["selected_id"]); sourceKind = WorkbenchData.Text(state["source_kind"], "editing_state");
                // Older releases saved a prefab source without recording a deliberate mode.
                // Migrate those records to automatic scene following; preserve explicit choices.
                followScene = (bool?)state["follow_scene"] ?? true;
                if (GlobalObjectId.TryParse(WorkbenchData.Text(state["target_global_id"]), out var id)) target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (!target) target = AssetDatabase.LoadAssetAtPath<GameObject>(WorkbenchData.Text(state["target_asset_path"]));
                localResources.Clear(); foreach (var item in (state["local_resources"] as JArray ?? new JArray()).OfType<JObject>()) localResources.Add(new ResourceCard { id = WorkbenchData.Text(item["id"]), name = WorkbenchData.Text(item["name"]), path = WorkbenchData.Text(item["path"]), kind = WorkbenchData.Text(item["kind"]), state = WorkbenchData.Text(item["state"]), note = WorkbenchData.Text(item["note"]), thumbnail = WorkbenchData.Text(item["thumbnail"]), productReference = item["product_reference"] as JObject });
                requestedResourceIds = state["selected_resource_ids"] is JArray choices ? new HashSet<string>(choices.Values<string>()) : null;
                RestoreProductReferences(state["booth_references"] as JArray); RestoreBoothState(state["booth"] as JObject);
                draft = state["draft"] as JObject; beforeInfo = state["before"] as JObject;
                if (draft?["rect"] is JObject r) annotation = new Rect((float?)r["x"] ?? 0, (float?)r["y"] ?? 0, (float?)r["w"] ?? 0, (float?)r["h"] ?? 0);
            }
            catch (Exception e) { toast = "上次界面记录暂不可读：" + e.Message; }
            finally { restoring = false; }
        }
        void RestoreDraftTexture()
        {
            if (draft?["image"] is JObject image && !frozen)
            {
                string path = WorkbenchData.Text(image["path"]); if (WorkbenchData.FileHash(path) != WorkbenchData.Text(image["sha256"])) throw new IOException("草稿原图已变化。保留文字和身份，不替换原图。"); frozen = WorkbenchData.LoadImage(path);
            }
            if (beforeInfo != null && !before) { string path = WorkbenchData.Text(beforeInfo["path"]); if (File.Exists(path) && WorkbenchData.FileHash(path) == WorkbenchData.Text(beforeInfo["sha256"])) before = WorkbenchData.LoadImage(path); }
        }
    }
}
