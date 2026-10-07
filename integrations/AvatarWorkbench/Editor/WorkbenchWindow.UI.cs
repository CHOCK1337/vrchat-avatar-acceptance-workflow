using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        VisualElement bodyArea, materialPanel, resultPanel, tabBar, connectionBox, composer;
        Label connectionHelp, placeholder, requestContext, draftLabel, resourceCount, activityLabel, roleGuide, taskDetails;
        VisualElement draftStrip;
        TextField search;
        readonly List<ResourceCard> visibleResources = new List<ResourceCard>();
        Button submitButton, materialTab, previewTab, feedbackTab, helpButton;
        string activePanel = "preview", layoutMode = "", resourceFilter = "";
        bool showAllFeedback, windowVisible = true;

        void BuildInterface()
        {
            ReleaseVisualUi();
            var root = rootVisualElement;
            if (sizeCallback != null) root.UnregisterCallback(sizeCallback);
            root.Clear(); root.name = "avatar-workbench-root";
            WorkbenchTheme.Apply(this, "aw-main-window");
            var header = Row(); header.AddToClassList("aw-header");
            var brand = Row(); brand.AddToClassList("aw-brand");
            var mark = new Label("AW"); mark.AddToClassList("aw-brand-mark"); brand.Add(mark);
            var brandText = new VisualElement(); var title = new Label("改模工作台"); title.AddToClassList("aw-title"); brandText.Add(title);
            var english = new Label("AVATAR WORKBENCH"); english.AddToClassList("aw-brand-caption"); brandText.Add(english); brand.Add(brandText); header.Add(brand);
            targetField = new ObjectField("当前角色") { objectType = typeof(GameObject), allowSceneObjects = true, value = target };
            targetField.AddToClassList("aw-target"); targetField.RegisterValueChangedCallback(e => { if (!restoring) SetTarget(e.newValue as GameObject); }); header.Add(targetField);
            header.Add(MakeButton("用选中角色", UseSelection, "use-selection"));
            refreshButton = MakeButton("刷新", RefreshAll, "refresh"); refreshButton.tooltip = "重新读取当前角色和任务，不构建或安装素材。"; header.Add(refreshButton);
            header.Add(MakeButton("更多 ▾", ShowMore, "more-menu")); root.Add(header);
            var shell = Row(); shell.AddToClassList("aw-shell"); shell.style.alignItems = Align.Stretch;
            navigationRail = BuildNavigation(); shell.Add(navigationRail);
            workspaceColumn = new VisualElement(); workspaceColumn.AddToClassList("aw-workspace-column"); shell.Add(workspaceColumn);
            bodyArea = Row(); bodyArea.AddToClassList("aw-body"); bodyArea.style.alignItems = Align.Stretch;
            materialPanel = BuildMaterials(); bodyArea.Add(materialPanel);
            previewPanel = BuildPreviewPanel(); bodyArea.Add(previewPanel);
            resultPanel = BuildResultPanel(); bodyArea.Add(resultPanel); workspaceColumn.Add(bodyArea);
            BuildComposer(workspaceColumn); root.Add(shell);
            sizeCallback = e => { if (e.target == root) ApplyLayout(e.newRect.width, e.newRect.height); };
            root.RegisterCallback(sizeCallback); ApplyLayout(position.width, position.height);
        }

        VisualElement BuildMaterials()
        {
            var panel = Pane(0); panel.name = "materials-panel";
            var modes = Row(); modes.AddToClassList("aw-material-modes");
            localMaterialTab = MakeButton("本次素材", () => SelectMaterialSource("local"), "materials-local-tab");
            directoryTab = MakeButton("读取目录", () => SelectMaterialSource("directory"), "materials-directory-tab");
            panTab = MakeButton("百度网盘", () => SelectMaterialSource("baidu"), "materials-baidu-tab");
            boothMaterialTab = MakeButton("BOOTH 找素材", () => SelectMaterialSource("booth"), "materials-booth-tab");
            modes.Add(localMaterialTab); modes.Add(directoryTab); modes.Add(panTab); modes.Add(boothMaterialTab); panel.Add(modes);
            localMaterialContent = BuildLocalMaterials(); localMaterialContent.AddToClassList("aw-material-content"); panel.Add(localMaterialContent);
            directoryContent = BuildDirectorySources(); panel.Add(directoryContent);
            panContent = BuildBaiduSources(); panel.Add(panContent);
            boothMaterialContent = BuildBoothMaterials(); panel.Add(boothMaterialContent);
            SelectMaterialSource(materialSource, false);
            return panel;
        }

        VisualElement BuildLocalMaterials() => BuildResourceLibrary();

        VisualElement BuildPreviewPanel()
        {
            var panel = Pane(0); panel.name = "preview-panel";
            var heading = Row(); heading.AddToClassList("aw-preview-heading");
            previewLabel = new Label { enableRichText = false }; previewLabel.AddToClassList("aw-source-badge"); heading.Add(previewLabel);
            sceneSourceLabel = new Label { name = "scene-sync-source", enableRichText = false }; sceneSourceLabel.AddToClassList("aw-scene-source"); sceneSourceLabel.AddToClassList("aw-ellipsis"); sceneSourceLabel.style.flexGrow = 1; heading.Add(sceneSourceLabel);
            freezeButton = MakeButton("圈出问题", BeginFeedback, "freeze-feedback"); freezeButton.AddToClassList("aw-accent-outline"); heading.Add(freezeButton);
            heading.Add(MakeButton("返回实时画面", LeaveAnnotation, "return-preview"));
            compareButton = MakeButton("前后对比", () => { comparing = !comparing; UpdateLabels(); canvas.MarkDirtyRepaint(); }, "compare"); heading.Add(compareButton);
            heading.Add(MakeButton("来源 ▾", ShowSources, "source-menu")); panel.Add(heading);
            panel.Add(BuildPreviewWorkspace());
            roleGuide = Wrapped("拖动旋转、滚轮缩放；右键加 WASD 移动相机。试穿仅操作临时角色。"); roleGuide.style.display = DisplayStyle.None; panel.Add(roleGuide);
            return panel;
        }

        VisualElement BuildResultPanel()
        {
            var panel = Pane(0); panel.name = "feedback-panel";
            panel.Add(Heading("需求与回复"));
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("aw-inner-scroll");
            activityLabel = Heading("等待修改需求"); activityLabel.style.whiteSpace = WhiteSpace.Normal; scroll.Add(activityLabel);
            taskLabel = Wrapped(""); taskLabel.AddToClassList("aw-task-summary");
            var taskSummary = new Foldout { text = "本次任务记录", value = false }; taskSummary.Add(taskLabel);
            feedbackPanel = new VisualElement(); feedbackPanel.style.marginTop = 10; scroll.Add(feedbackPanel);
            var detail = new Foldout { text = "技术详情", value = false, name = "technical-details" };
            projectLabel = Wrapped(""); candidateLabel = Wrapped("");
            detail.Add(projectLabel); detail.Add(candidateLabel);
            taskDetails = Wrapped(""); detail.Add(taskDetails);
            detail.Add(Wrapped("画面会注明正在查看现场角色、保存的候选还是临时试穿。换装和圆盘菜单运行在临时角色上；动态预览使用现有 SDK 的 PhysBone。已有回复只表示 Codex 给出了答复，不表示模型已修好。"));
            detail.Add(MakeButton("选择已有任务", PickTask, "choose-task"));
            candidateButton = MakeButton("查看任务保存的角色", UseTaskCandidate, "task-candidate"); detail.Add(candidateButton);
            detail.Add(MakeButton("导出反馈记录", ExportFeedback, "export-feedback"));
            detail.Add(MakeButton("复制接入信息", CopyInstruction, "copy-instruction")); scroll.Add(detail);
            scroll.Add(taskSummary); panel.Add(scroll); return panel;
        }

        void BuildComposer(VisualElement root)
        {
            composer = new VisualElement { name = "request-composer" }; composer.AddToClassList("aw-composer");
            var title = Row(); title.AddToClassList("aw-composer-title"); title.Add(Heading("写下修改需求"));
            requestContext = new Label { enableRichText = false }; requestContext.AddToClassList("aw-ellipsis"); requestContext.AddToClassList("aw-muted"); requestContext.style.flexGrow = 1; title.Add(requestContext); AddModelChoice(title); composer.Add(title);
            draftStrip = Row(); draftStrip.AddToClassList("aw-draft-strip");
            draftLabel = new Label { enableRichText = false }; draftLabel.AddToClassList("aw-ellipsis"); draftLabel.style.flexGrow = 1; draftStrip.Add(draftLabel);
            draftStrip.Add(MakeButton("查看圈选", BeginFeedback, "view-draft"));
            draftStrip.Add(MakeButton("取消附件", DiscardDraft, "discard-feedback")); composer.Add(draftStrip);
            var line = Row(); line.AddToClassList("aw-input-row"); line.style.alignItems = Align.Stretch;
            var inputArea = new VisualElement(); inputArea.AddToClassList("aw-input-area");
            input = new TextField { multiline = true, value = message, maxLength = 5000, name = "feedback-message", tooltip = "例如：把头发改成黑色；或把左侧已选的饰品装到头上。" }; input.AddToClassList("aw-input");
            input.RegisterValueChangedCallback(e => { message = e.newValue; UpdateComposer(); QueueSave(); });
            input.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return && (e.ctrlKey || e.commandKey) && submitButton != null && submitButton.enabledSelf) { TryAction(SaveAndHandoff); e.StopPropagation(); } });
            inputArea.Add(input);
            placeholder = Wrapped("例如：把头发改成黑色，保留原来的发饰。");
            placeholder.name = "request-placeholder"; placeholder.pickingMode = PickingMode.Ignore; placeholder.AddToClassList("aw-placeholder"); inputArea.Add(placeholder);
            line.Add(inputArea);
            submitButton = MakeButton("发送给 Codex", SaveAndHandoff, "submit-feedback"); submitButton.AddToClassList("aw-primary"); submitButton.AddToClassList("aw-submit"); line.Add(submitButton); composer.Add(line);
            noticeLabel = new Label(toast) { enableRichText = false }; noticeLabel.AddToClassList("aw-notice"); noticeLabel.AddToClassList("aw-ellipsis"); composer.Add(noticeLabel);
            root.Add(composer);
        }

        void ApplyLayout(float width, float height)
        {
            if (bodyArea == null || width < 1 || height < 1) return;
            bool wide = width >= 1420 && height >= 620;
            layoutMode = wide ? "wide" : "compact";
            rootVisualElement.EnableInClassList("aw-wide", wide);
            rootVisualElement.EnableInClassList("aw-compact", !wide);
            rootVisualElement.EnableInClassList("aw-medium", false);
            rootVisualElement.EnableInClassList("aw-narrow", width < 620);
            rootVisualElement.EnableInClassList("aw-icon-rail", width < 760);
            rootVisualElement.EnableInClassList("aw-short-window", height < 520);
            rootVisualElement.EnableInClassList("aw-compact-landscape", false);
            targetField.style.display = width < 620 ? DisplayStyle.None : DisplayStyle.Flex;
            placeholder.text = width < 620 ? "写一句修改需求…" : "例如：把头发改成黑色，保留原来的发饰。";
            bool boothFocus = materialSource != "local" && activePanel == "materials";
            rootVisualElement.EnableInClassList("aw-booth-full", boothFocus);
            rootVisualElement.EnableInClassList("aw-booth-compact-grid", false);
            tabBar.style.display = DisplayStyle.Flex;
            bool showMaterials = wide || activePanel == "materials";
            bool showResults = !boothFocus && (wide || activePanel == "feedback");
            materialPanel.style.display = showMaterials ? DisplayStyle.Flex : DisplayStyle.None;
            resultPanel.style.display = showResults ? DisplayStyle.Flex : DisplayStyle.None;
            previewPanel.style.display = !boothFocus && (wide || activePanel == "preview") ? DisplayStyle.Flex : DisplayStyle.None;
            materialPanel.style.width = wide && !boothFocus ? new StyleLength(236) : new StyleLength(StyleKeyword.Auto);
            resultPanel.style.width = wide ? new StyleLength(292) : new StyleLength(StyleKeyword.Auto);
            materialPanel.style.flexGrow = wide && !boothFocus ? 0 : 1; resultPanel.style.flexGrow = wide ? 0 : 1; previewPanel.style.flexGrow = 1;
            materialPanel.style.marginRight = wide && !boothFocus ? 10 : 0; resultPanel.style.marginLeft = wide ? 10 : 0;
            previewTab.EnableInClassList("aw-tab-active", activePanel == "preview"); materialTab.EnableInClassList("aw-tab-active", activePanel == "materials"); feedbackTab.EnableInClassList("aw-tab-active", activePanel == "feedback");
            ApplyPreviewWorkspace(); if (!showMaterials) StopBoothRequests(true); else libraryLoading?.Resume();
            canvas?.MarkDirtyRepaint();
        }

        void SelectPanel(string name) { activePanel = name; ApplyLayout(position.width, position.height); QueueSave(); }

        void UpdateResourceList()
        {
            visibleResources.Clear(); visibleResources.AddRange(resources.Where(r => string.IsNullOrWhiteSpace(resourceFilter) || (r.name ?? "").IndexOf(resourceFilter, StringComparison.OrdinalIgnoreCase) >= 0 || (r.path ?? "").IndexOf(resourceFilter, StringComparison.OrdinalIgnoreCase) >= 0));
            if (cards != null)
            {
                RebuildLibrary();
                cards.style.display = visibleResources.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (resourceCount != null) resourceCount.text = resources.Count == 0 ? "尚未选择" : "本次 " + resources.Count + " 项 · 本地新增 " + localResources.Count + " 项";
            if (materialTab != null) materialTab.text = "素材库" + (resources.Count == 0 ? "" : " (" + resources.Count + ")");
            if (emptyResources != null) { emptyResources.style.display = visibleResources.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None; emptyResources.text = resources.Count == 0 ? "还没有素材。\n也可直接在下方写修改需求。" : "没有匹配项，试试清空筛选词。"; }
            UpdateSelectedResource(); UpdateComposer();
        }
        void UpdateSelectedResource()
        {
            var r = resources.FirstOrDefault(x => x.id == selectedId);
            if (detailLabel != null) detailLabel.text = r == null ? "点击素材卡片可查看来源。" : r.name + "\n" + WorkbenchData.StateName(r.state) + "\n" + r.path + "\n" + r.note;
            rootVisualElement.Q<Button>("remove-resource")?.SetEnabled(localResources.Any(x => x.id == selectedId));
            rootVisualElement.Q<Button>("describe-selection")?.SetEnabled(localResources.Count > 0 || r != null || boothReferences.Count > 0);
        }
        void UpdateComposer()
        {
            if (submitButton == null) return;
            string label = draft != null ? WorkbenchData.Identity(draft["candidate"] as JObject) : target ? target.name : "尚未选角色";
            int selectedCount = draft != null ? (draft["selected_resources"] as JArray)?.Count ?? 0 : resources.Count(ResourceRequested);
            int productCount = draft != null ? (draft["selected_product_references"] as JArray)?.Count ?? 0 : boothReferences.Count;
            requestContext.text = label + (selectedCount > 0 ? " · 已附 " + selectedCount + " 项素材" : "") + (productCount > 0 ? " · 商品参考 " + productCount + " 项" : ""); requestContext.tooltip = requestContext.text;
            if (draft != null) requestContext.tooltip += "\n此条保留圈选时的资源与目标；取消附件后使用当前选择。";
            placeholder.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.Flex : DisplayStyle.None;
            bool attached = draft != null;
            draftStrip.style.display = attached ? DisplayStyle.Flex : DisplayStyle.None;
            draftLabel.text = attached ? "已附圈选原图 · " + WorkbenchData.Identity(draft["candidate"] as JObject) : "";
            draftLabel.tooltip = attached ? "此条请求继续关联被冻结的原图与角色版本。即使切换角色也不会自动改绑；取消附件后可提交新的文字请求。" : "";
            submitButton.text = sendingCodex ? "读取 / 发送中…" : WorkbenchModels.Active == "codex" ? "发送给 Codex" : "发送给 API";
            submitButton.tooltip = WorkbenchModels.Active == "codex" ? "将本次需求交给当前 Codex，沿用原改模流程" : "发送到 " + WorkbenchModels.Label(WorkbenchModels.Active);
            submitButton.SetEnabled(!sendingCodex && !connectingCodex && !string.IsNullOrWhiteSpace(message) && message.Length <= 5000 && (target || draft != null || !string.IsNullOrEmpty(taskPath)));
            submitButton.tooltip += "。先保存原始请求，Ctrl+Enter 发送；API 回复只代表建议。";
        }
        void SaveAndHandoff()
        {
            string binding = draft != null ? WorkbenchData.Text(draft["binding"]) : BindingPath;
            JObject entry = SaveFeedback();
            SendSavedRequest(binding, entry);
            input?.Focus();
        }
        void PickResource()
        {
            string p = EditorUtility.OpenFilePanelWithFilters("选择衣服、发型或饰品文件", WorkbenchData.Project, new[] { "可安装素材", "prefab,unitypackage" });
            if (!string.IsNullOrEmpty(p)) AddDroppedPaths(new[] { ToAssetPath(p) });
        }
        void PickResourceFolder()
        {
            string p = EditorUtility.OpenFolderPanel("选择本次素材目录", WorkbenchData.Project, "");
            if (!string.IsNullOrEmpty(p)) AddDroppedPaths(new[] { ToAssetPath(p) });
        }
        static string ToAssetPath(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/'); string root = WorkbenchData.Project.Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        }
        void DescribeSelection()
        {
            if (!string.IsNullOrWhiteSpace(message)) { Toast("所选素材会自动随请求附上；已保留你正在写的内容。"); input.Focus(); return; }
            bool local = localResources.Count > 0 || resources.Any(x => x.id == selectedId);
            message = local ? "请把本次已选本地素材安装到当前角色，保留其他未涉及的外观和功能。" : "请评估本次商品参考与当前角色的适配情况，说明需要准备的本地素材。商品参考不代表我已购买或下载，请勿直接安装。";
            if (local && boothReferences.Count > 0) message += " 同时评估附带的商品参考；尚未关联本地文件的商品只供参考。";
            input.SetValueWithoutNotify(message); UpdateComposer(); QueueSave(); input.Focus();
        }
        void ShowSources()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("跟随场景角色（自动同步）"), !historical && followScene && target && !EditorUtility.IsPersistent(target), () => TryAction(UseSceneTarget));
            if (!string.IsNullOrEmpty(CandidatePath)) menu.AddItem(new GUIContent("任务中保存的角色"), !historical && sourceKind == "candidate_prefab", () => TryAction(UseTaskCandidate));
            else menu.AddDisabledItem(new GUIContent("任务中保存的角色（尚未绑定）"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("打开已有截图…"), false, () => TryAction(PickManifest));
            for (int i = 0; i < images.Count; i++) { int index = i; menu.AddItem(new GUIContent("已有截图/" + (i + 1) + " " + WorkbenchUiRules.Short(WorkbenchData.Text(images[i]["title"], "原图"), 48).Replace('/', '／')), i == historyIndex, () => TryAction(() => ShowHistory(index))); }
            if (historical) menu.AddItem(new GUIContent("把这张原图设为修改前对照"), false, () => TryAction(SetBeforeImage));
            menu.ShowAsContext();
        }
        void ShowMore()
        {
            var menu = new GenericMenu();
            if (docked) menu.AddItem(new GUIContent(maximized ? "恢复停靠布局" : "最大化停靠区"), false, () => maximized = !maximized);
            else menu.AddDisabledItem(new GUIContent("最大化停靠区（浮动窗口可拖动边框放大）"));
            menu.AddItem(new GUIContent("选择已有任务…"), false, () => TryAction(PickTask));
            menu.AddItem(new GUIContent("API 与模型设置…"), false, WorkbenchModelSettingsWindow.Open);
            menu.AddItem(new GUIContent("本地素材大图 / 多素材对比…"), false, OpenResourceGallery);
            menu.AddItem(new GUIContent("截图记录…"), false, OpenScreenshotGallery);
            menu.AddItem(new GUIContent("切换画面来源 / 已有截图…"), false, () => TryAction(ShowSources));
            menu.AddItem(new GUIContent("跟随场景角色（自动同步）"), followScene && target && !EditorUtility.IsPersistent(target), () => TryAction(UseSceneTarget));
            menu.AddItem(new GUIContent("复制 Codex 接入指令"), false, () => TryAction(CopyInstruction));
            menu.AddItem(new GUIContent("导出本任务反馈记录…"), false, () => TryAction(ExportFeedback));
            menu.AddItem(new GUIContent("查看进度与技术详情"), false, () => { SelectPanel("feedback"); rootVisualElement.Q<Foldout>("technical-details").value = true; });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("操作说明"), false, () => EditorUtility.DisplayDialog("改模工作台怎么用", "1. 选择当前角色，需要安装素材时再添加素材。\n2. 转动模型看效果；有问题就先圈选，再写一句话。\n3. 点击保存。未接入 Codex 时会同时复制指令，粘贴到 Codex 即可接手。\n\n此窗口只展示与记录，不会自动改模、构建或上传。", "知道了"));
            menu.ShowAsContext();
        }
    }
}
