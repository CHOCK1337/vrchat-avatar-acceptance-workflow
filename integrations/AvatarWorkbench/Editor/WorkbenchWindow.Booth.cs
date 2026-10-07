using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        static readonly string[] BoothCategories = { "全部", "衣服", "头发", "妆容贴图", "饰品", "道具", "插件" };
        static readonly string[] BoothCategoryValues = { "", "3D衣装", "3D髪型", "3Dテクスチャ", "3D装飾品", "3D小道具", "ソフトウェア" };
        static readonly string[] BoothSortNames = { "热门", "最新", "价格从低到高", "价格从高到低" };
        static readonly string[] BoothSortValues = { "popularity", "new", "price_asc", "price_desc" };
        readonly List<JObject> boothResults = new List<JObject>();
        readonly List<JObject> boothVisible = new List<JObject>();
        readonly List<List<JObject>> boothGridRows = new List<List<JObject>>();
        readonly Dictionary<string, Texture2D> boothTextures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Task<Texture2D>> boothImageTasks = new Dictionary<string, Task<Texture2D>>();
        readonly HashSet<string> boothFailedImages = new HashSet<string>();
        readonly Dictionary<string, double> boothImageStartedAt = new Dictionary<string, double>();
        readonly Dictionary<VisualElement, BoothCoverWait> boothCoverWaits = new Dictionary<VisualElement, BoothCoverWait>();
        readonly SemaphoreSlim boothThumbnailSlots = new SemaphoreSlim(2, 2);
        IVisualElementScheduledItem boothThumbnailJob;
        IVisualElementScheduledItem boothCoverAnimation;
        ScrollView boothScroll;
        CancellationTokenSource boothCancellation;
        VisualElement localMaterialContent, boothMaterialContent;
        Button localMaterialTab, boothMaterialTab, boothSearchButton, boothCancelButton, boothReferencesButton, boothAddButton, boothRemoveButton, boothLinkButton, boothDetailsButton, boothOpenButton, boothImageButton, boothPreviousButton, boothNextButton, boothSettingsButton;
        VisualElement boothPager;
        TextField boothQueryField;
        IntegerField boothPriceField;
        DropdownField boothCategoryField, boothSortField;
        Label boothStatus, boothEmptyMessage, boothQueryPlaceholder;
        ListView boothList;
        string materialSource = "local", boothQuery = "", boothSelectedId = "", boothStatusText = "输入关键词后搜索；当前未联网。";
        int boothCategory, boothSort, boothMaxPrice, boothEpoch, boothDetailEpoch;
        string boothLastQuery = "";
        JObject boothLastQueryPlan;
        int boothPage = 1, boothLastCategory, boothLastMaxPrice, boothLastSort, boothColumns = 1;
        bool boothUsageAcknowledged, boothShowingReferences, boothSearching, boothNetworkSession, boothLoadingDetails, boothHasNext, boothQueryFailed;
        sealed class BoothCoverWait
        {
            internal string Id, Url;
            internal int Epoch;
        }

        VisualElement BuildBoothMaterials()
        {
            boothThumbnailJob?.Pause(); boothThumbnailJob = null;
            boothCoverAnimation?.Pause(); boothCoverAnimation = null; boothCoverWaits.Clear();
            if (boothScroll != null) boothScroll.verticalScroller.valueChanged -= OnBoothScrolled;
            boothScroll = null;
            var panel = new VisualElement { name = "booth-materials-panel" }; panel.AddToClassList("aw-material-content");
            var controls = new VisualElement(); controls.AddToClassList("aw-booth-controls");
            var queryRow = Row(); queryRow.AddToClassList("aw-booth-query");
            var queryInput = new VisualElement(); queryInput.AddToClassList("aw-booth-query-wrap");
            boothQueryField = new TextField("找素材") { name = "booth-query", value = boothQuery, maxLength = 240, tooltip = "中文需求或商品关键词。默认使用本地词表；更复杂的中文可在搜索设置中启用独立 API。" };
            boothQueryField.AddToClassList("aw-booth-query-input");
            boothQueryField.RegisterValueChangedCallback(e => { boothQuery = e.newValue; if (boothQueryPlaceholder != null) boothQueryPlaceholder.style.display = string.IsNullOrEmpty(boothQuery) ? DisplayStyle.Flex : DisplayStyle.None; QueueSave(); });
            boothQueryField.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return && !boothSearching) { StartBoothSearch(); e.StopPropagation(); } });
            queryInput.Add(boothQueryField);
            boothQueryPlaceholder = new Label("中文需求或商品关键词") { name = "booth-query-placeholder", enableRichText = false, pickingMode = PickingMode.Ignore };
            boothQueryPlaceholder.AddToClassList("aw-booth-query-placeholder"); boothQueryPlaceholder.style.display = string.IsNullOrEmpty(boothQuery) ? DisplayStyle.Flex : DisplayStyle.None; queryInput.Add(boothQueryPlaceholder); queryRow.Add(queryInput);
            boothSearchButton = MakeButton("搜索", StartBoothSearch, "booth-search"); queryRow.Add(boothSearchButton);
            boothCancelButton = MakeButton("取消", () => { StopBoothRequests(true); SetBoothStatus("已取消联网请求；已有结果保留。"); }, "booth-cancel"); queryRow.Add(boothCancelButton); controls.Add(queryRow);
            var filters = Row(); filters.AddToClassList("aw-booth-filters");
            boothCategoryField = new DropdownField(BoothCategories.ToList(), boothCategory) { name = "booth-category", tooltip = "商品类别；类别和标题不是适配验证。" };
            boothCategoryField.RegisterValueChangedCallback(e => { boothCategory = Math.Max(0, Array.IndexOf(BoothCategories, e.newValue)); QueueSave(); }); filters.Add(boothCategoryField);
            boothPriceField = new IntegerField("≤¥") { name = "booth-max-price", value = boothMaxPrice, tooltip = "最高价格（日元）；0 表示不限。" };
            boothPriceField.RegisterValueChangedCallback(e => { boothMaxPrice = Math.Max(0, e.newValue); boothPriceField.SetValueWithoutNotify(boothMaxPrice); QueueSave(); }); filters.Add(boothPriceField);
            boothSortField = new DropdownField(BoothSortNames.ToList(), boothSort) { name = "booth-sort", tooltip = "BOOTH 商品排序；以返回结果的实际排序说明为准。" };
            boothSortField.RegisterValueChangedCallback(e => { boothSort = Math.Max(0, Array.IndexOf(BoothSortNames, e.newValue)); QueueSave(); }); filters.Add(boothSortField); controls.Add(filters); panel.Add(controls);
            var meta = new VisualElement(); meta.AddToClassList("aw-booth-meta");
            var notice = Row(); notice.AddToClassList("aw-booth-notice");
            var shortNotice = new Label("非官方 · 商品仅供参考 · 适配未核实") { enableRichText = false, tooltip = WorkbenchBooth.Notice }; shortNotice.AddToClassList("aw-ellipsis"); shortNotice.style.flexGrow = 1; notice.Add(shortNotice);
            notice.Add(MakeButton("声明", ShowBoothNotice, "booth-notice"));
            boothSettingsButton = MakeButton("搜索设置", WorkbenchBoothSearchSettings.Open, "booth-search-settings"); boothSettingsButton.tooltip = "当前：" + WorkbenchBoothSearchSettings.ModeLabel; notice.Add(boothSettingsButton);
            boothReferencesButton = MakeButton("已选参考", ToggleBoothReferences, "booth-show-references"); notice.Add(boothReferencesButton); meta.Add(notice);
            var statusRow = Row(); statusRow.AddToClassList("aw-booth-status-row");
            boothStatus = new Label(boothStatusText) { name = "booth-status", enableRichText = false }; boothStatus.AddToClassList("aw-booth-status"); boothStatus.AddToClassList("aw-ellipsis"); boothStatus.style.flexGrow = 1; statusRow.Add(boothStatus);
            boothPager = Row(); boothPager.AddToClassList("aw-booth-pager");
            boothPreviousButton = MakeButton("‹ 上一页", () => StartBoothPage(boothPage - 1), "booth-previous-page"); boothPager.Add(boothPreviousButton);
            boothNextButton = MakeButton("下一页 ›", () => StartBoothPage(boothPage + 1), "booth-next-page"); boothPager.Add(boothNextButton); statusRow.Add(boothPager); meta.Add(statusRow); panel.Add(meta);
            // Unity 2022's fixed-height controller creates zero rows when the viewport is
            // shorter than one item. DynamicHeight still realizes the first partial row.
            boothList = new ListView { name = "booth-results", itemsSource = boothGridRows, fixedItemHeight = 190, virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight, selectionType = SelectionType.None };
            boothList.AddToClassList("aw-booth-results"); boothList.makeItem = () => { var row = Row(); row.AddToClassList("aw-booth-grid-row"); return row; }; boothList.bindItem = BindBoothGridRow;
            boothList.unbindItem = (row, _) => { foreach (var card in row.Children()) { card.userData = null; var image = card.Q<Image>("booth-thumb"); if (image != null) image.image = null; } };
            boothList.RegisterCallback<GeometryChangedEvent>(e => { UpdateBoothGridColumns(); QueueVisibleBoothThumbnails(); });
            panel.Add(boothList);
            boothEmptyMessage = new Label { name = "booth-empty", enableRichText = false }; boothEmptyMessage.AddToClassList("aw-booth-empty"); panel.Add(boothEmptyMessage);
            var footer = new VisualElement(); footer.AddToClassList("aw-booth-actions");
            var primary = Row();
            boothOpenButton = MakeButton("查看商品", () => OpenBoothProduct(SelectedBoothProduct()), "booth-open-product"); primary.Add(boothOpenButton);
            boothImageButton = MakeButton("大图预览", () => OpenBoothImage(SelectedBoothProduct()), "booth-image-preview"); primary.Add(boothImageButton);
            boothAddButton = MakeButton("加入本次需求", AddSelectedBoothProduct, "booth-add-reference"); primary.Add(boothAddButton); footer.Add(primary);
            var secondary = Row();
            boothDetailsButton = MakeButton("查看说明", StartBoothDetails, "booth-product-details"); secondary.Add(boothDetailsButton);
            boothLinkButton = MakeButton("关联已下载文件", LinkSelectedBoothProduct, "booth-link-local"); secondary.Add(boothLinkButton);
            boothRemoveButton = MakeButton("移除参考", RemoveSelectedBoothProduct, "booth-remove-reference"); secondary.Add(boothRemoveButton); footer.Add(secondary); panel.Add(footer);
            RefreshBoothReferencesUI(); return panel;
        }

        void SelectMaterialSource(string value, bool save = true)
        {
            materialSource = new[] { "local", "booth", "directory", "baidu" }.Contains(value) ? value : "local";
            if (localMaterialContent == null || boothMaterialContent == null) return;
            localMaterialContent.style.display = materialSource == "local" ? DisplayStyle.Flex : DisplayStyle.None;
            boothMaterialContent.style.display = materialSource == "booth" ? DisplayStyle.Flex : DisplayStyle.None;
            if (directoryContent != null) directoryContent.style.display = materialSource == "directory" ? DisplayStyle.Flex : DisplayStyle.None;
            if (panContent != null) panContent.style.display = materialSource == "baidu" ? DisplayStyle.Flex : DisplayStyle.None;
            materialModeChoice?.SetValueWithoutNotify(materialModeNames[Array.IndexOf(new[] { "local", "directory", "baidu", "booth" }, materialSource)]);
            if (directoryToolbar != null) directoryToolbar.style.display = materialSource == "directory" ? DisplayStyle.Flex : DisplayStyle.None;
            directoryTab?.EnableInClassList("aw-tab-active", materialSource == "directory"); panTab?.EnableInClassList("aw-tab-active", materialSource == "baidu");
            localMaterialTab.EnableInClassList("aw-tab-active", materialSource == "local"); boothMaterialTab.EnableInClassList("aw-tab-active", materialSource == "booth");
            if (materialSource != "booth") StopBoothRequests(true);
            if (save) activePanel = "materials";
            if (save && materialPanel != null && previewPanel != null && resultPanel != null) ApplyLayout(position.width, position.height);
            if (save) QueueSave();
        }

        void UpdateBoothGridColumns()
        {
            if (boothList == null || boothList.resolvedStyle.width <= 0) return;
            int columns = Mathf.Clamp(Mathf.FloorToInt((boothList.resolvedStyle.width - 16f) / 206f), 1, 6);
            if (columns == boothColumns) return;
            boothColumns = columns; RebuildBoothGrid();
        }
        void RebuildBoothGrid()
        {
            boothGridRows.Clear();
            for (int start = 0; start < boothVisible.Count; start += boothColumns) boothGridRows.Add(boothVisible.Skip(start).Take(boothColumns).ToList());
            boothList?.Rebuild(); QueueVisibleBoothThumbnails();
        }
        void BindBoothGridRow(VisualElement row, int index)
        {
            if (index < 0 || index >= boothGridRows.Count) return;
            while (row.childCount < boothColumns) row.Add(MakeBoothCard());
            while (row.childCount > boothColumns) row.RemoveAt(row.childCount - 1);
            for (int column = 0; column < boothColumns; column++)
            {
                var card = row[column]; bool has = column < boothGridRows[index].Count;
                card.style.visibility = has ? Visibility.Visible : Visibility.Hidden;
                if (has) BindBoothCard(card, boothGridRows[index][column]);
                else { card.userData = null; card.Q<Image>("booth-thumb").image = null; }
            }
        }

        VisualElement MakeBoothCard()
        {
            var row = new VisualElement(); row.AddToClassList("aw-booth-card");
            var cover = new VisualElement { tooltip = "点击查看商品大图（作者宣传图）" }; cover.AddToClassList("aw-booth-thumb");
            var image = new Image { name = "booth-thumb", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore }; image.style.width = new StyleLength(new Length(100, LengthUnit.Percent)); image.style.height = new StyleLength(new Length(100, LengthUnit.Percent)); cover.Add(image);
            var coverStatus = new Label("未加载") { name = "booth-cover-status", enableRichText = false, pickingMode = PickingMode.Ignore }; coverStatus.AddToClassList("aw-booth-cover-status"); cover.Add(coverStatus); row.Add(cover);
            cover.RegisterCallback<ClickEvent>(e => { if (e.button == 0) { OpenBoothRowImage(row); e.StopPropagation(); } });
            row.RegisterCallback<ClickEvent>(e => { if (e.button != 0) return; SelectBoothCard(row.userData as string); if (e.clickCount == 2) OpenBoothRowImage(row); e.StopPropagation(); });
            var texts = new VisualElement(); texts.style.flexGrow = 1; texts.style.minWidth = 0;
            foreach (string name in new[] { "booth-name", "booth-price-shop", "booth-product-id", "booth-compatibility" })
            { var label = new Label { name = name, enableRichText = false }; label.AddToClassList("aw-ellipsis"); texts.Add(label); }
            row.Add(texts); return row;
        }

        void BindBoothCard(VisualElement row, JObject product)
        {
            string id = ProductId(product); row.userData = id; row.EnableInClassList("aw-booth-selected", id == boothSelectedId);
            row.Q<Label>("booth-name").text = WorkbenchBoothInfoWindow.ReadableTitle(ProductText(product, "name", "未命名商品"));
            row.Q<Label>("booth-product-id").text = "BOOTH 号：" + id + " · 适配未核实";
            var priceShop = row.Q<Label>("booth-price-shop"); priceShop.text = ProductPrice(product) + " · " + ProductText(product["shop"] as JObject, "name", "店铺以商品页为准"); priceShop.tooltip = priceShop.text;
            row.Q<Label>("booth-compatibility").text = "商品参考 · 适配未核实" + (boothReferences.Any(p => ProductId(p) == id) ? " · 已加入" : "");
            row.tooltip = ProductText(product, "name", "") + "\n" + ProductText(product, "url", "");
            var image = row.Q<Image>("booth-thumb"); image.image = null; image.userData = null;
            string url = ProductText(product, "image", "");
            if (boothTextures.TryGetValue(url, out var texture) && texture) { image.image = texture; SetBoothCoverStatus(row, ""); }
            else SetBoothCoverStatus(row, string.IsNullOrEmpty(url) ? "无封面" : boothFailedImages.Contains(url) ? "封面暂不可用" : "未加载");
            QueueVisibleBoothThumbnails();
        }

        void OnBoothScrolled(float value) { QueueVisibleBoothThumbnails(); }
        void QueueVisibleBoothThumbnails()
        {
            if (disposed || boothSearching || !boothNetworkSession || !windowVisible || materialSource != "booth" || boothList == null || boothList.panel == null) return;
            var scroll = boothList.Q<ScrollView>();
            if (scroll != boothScroll)
            {
                if (boothScroll != null) boothScroll.verticalScroller.valueChanged -= OnBoothScrolled;
                boothScroll = scroll;
                if (boothScroll != null) boothScroll.verticalScroller.valueChanged += OnBoothScrolled;
            }
            // Bind can briefly visit all results before the list has a viewport. One delayed,
            // coalesced callback inspects the laid-out rows; there is no repeating timer.
            if (boothThumbnailJob == null) boothThumbnailJob = boothList.schedule.Execute(LoadVisibleBoothThumbnails);
            boothThumbnailJob.ExecuteLater(25);
        }

        void LoadVisibleBoothThumbnails()
        {
            if (disposed || boothSearching || !boothNetworkSession || !windowVisible || materialSource != "booth" || boothList == null || boothList.panel == null || boothScroll == null || boothList.resolvedStyle.display == DisplayStyle.None) return;
            Rect viewport = boothScroll.contentViewport.worldBound;
            if (viewport.width <= 0 || viewport.height <= 0) return;
            foreach (var row in boothList.Query<VisualElement>(className: "aw-booth-card").ToList())
            {
                Rect bounds = row.worldBound;
                if (row.panel == null || row.resolvedStyle.visibility == Visibility.Hidden || bounds.width <= 0 || bounds.height <= 0 || !viewport.Overlaps(bounds)) continue;
                string id = row.userData as string;
                var product = boothVisible.FirstOrDefault(p => ProductId(p) == id); if (product == null) continue;
                string url = ProductText(product, "image", ""); var image = row.Q<Image>("booth-thumb");
                if (string.IsNullOrEmpty(url)) { SetBoothCoverStatus(row, "无封面"); continue; }
                if (boothTextures.TryGetValue(url, out var texture) && texture) { image.image = texture; SetBoothCoverStatus(row, ""); continue; }
                if (boothFailedImages.Contains(url)) { SetBoothCoverStatus(row, "封面暂不可用"); continue; }
                if ((string)image.userData == url) { TrackBoothCoverLoading(row, id, url, boothEpoch); continue; }
                image.userData = url; SetBoothCoverStatus(row, "加载中"); LoadBoothCardImage(row, id, url, boothEpoch);
            }
        }

        static void SetBoothCoverStatus(VisualElement row, string text)
        {
            var label = row.Q<Label>("booth-cover-status"); label.text = text;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void TrackBoothCoverLoading(VisualElement row, string id, string url, int epoch)
        {
            if (!boothImageTasks.TryGetValue(url, out var task) || task.IsCompleted || disposed || !windowVisible || epoch != boothEpoch) return;
            boothCoverWaits[row] = new BoothCoverWait { Id = id, Url = url, Epoch = epoch };
            if (boothCoverAnimation == null) boothCoverAnimation = boothList.schedule.Execute(AnimateBoothLoadingCovers).Every(150);
            else boothCoverAnimation.Resume();
        }
        void AnimateBoothLoadingCovers()
        {
            if (disposed || !windowVisible || !boothNetworkSession || materialSource != "booth" || boothScroll == null)
            { boothCoverWaits.Clear(); boothCoverAnimation?.Pause(); return; }
            Rect viewport = boothScroll.contentViewport.worldBound;
            // Only active visible image requests are tracked; there is no asset or whole-list scan.
            foreach (var entry in boothCoverWaits.ToArray())
            {
                var row = entry.Key; var wait = entry.Value;
                bool current = wait.Epoch == boothEpoch && row.panel != null && (string)row.userData == wait.Id && row.resolvedStyle.visibility != Visibility.Hidden && viewport.Overlaps(row.worldBound);
                if (!current || !boothImageTasks.TryGetValue(wait.Url, out var pending) || pending.IsCompleted)
                { boothCoverWaits.Remove(row); continue; }
                double elapsed = boothImageStartedAt.TryGetValue(wait.Url, out double started) ? Math.Max(0, EditorApplication.timeSinceStartup - started) : 0;
                SetBoothCoverStatus(row, "加载中" + new string('.', 1 + (int)(elapsed * 4) % 3) + "\n" + elapsed.ToString("0.0") + " 秒");
            }
            if (boothCoverWaits.Count == 0) boothCoverAnimation?.Pause();
        }
        void FinishBoothCoverLoading(VisualElement row, string id, int epoch)
        {
            if (boothCoverWaits.TryGetValue(row, out var wait) && wait.Id == id && wait.Epoch == epoch) boothCoverWaits.Remove(row);
            if (boothCoverWaits.Count == 0) boothCoverAnimation?.Pause();
        }

        async void LoadBoothCardImage(VisualElement row, string id, string url, int epoch)
        {
            try
            {
                if (boothCancellation == null) return;
                if (!boothImageTasks.TryGetValue(url, out var pending))
                { boothImageStartedAt[url] = EditorApplication.timeSinceStartup; pending = LoadBoothTexture(url, epoch, boothCancellation.Token); boothImageTasks[url] = pending; }
                TrackBoothCoverLoading(row, id, url, epoch);
                var texture = await pending;
                if (!disposed && epoch == boothEpoch && windowVisible && row.panel != null && (string)row.userData == id)
                {
                    if (texture) { row.Q<Image>("booth-thumb").image = texture; SetBoothCoverStatus(row, ""); }
                    else { boothFailedImages.Add(url); SetBoothCoverStatus(row, "封面暂不可用"); }
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (!disposed && epoch == boothEpoch)
                {
                    boothFailedImages.Add(url);
                    if (row.panel != null && (string)row.userData == id) SetBoothCoverStatus(row, "封面暂不可用");
                }
            }
            finally { FinishBoothCoverLoading(row, id, epoch); }
        }

        async Task<Texture2D> LoadBoothTexture(string url, int epoch, CancellationToken token)
        {
            await boothThumbnailSlots.WaitAsync(token);
            try
            {
                if (disposed || epoch != boothEpoch) return null;
                var bytes = await WorkbenchBooth.ThumbnailAsync(url, token);
                token.ThrowIfCancellationRequested();
                if (disposed || epoch != boothEpoch || !windowVisible) return null;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "AW_BoothThumbnail", hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    if (!ImageConversion.LoadImage(texture, bytes, true)) { UnityEngine.Object.DestroyImmediate(texture); return null; }
                    if (boothTextures.Count >= 24) { var oldest = boothTextures.First(); if (oldest.Value) UnityEngine.Object.DestroyImmediate(oldest.Value); boothTextures.Remove(oldest.Key); boothImageTasks.Remove(oldest.Key); boothImageStartedAt.Remove(oldest.Key); }
                    boothTextures[url] = texture; return texture;
                }
                catch { if (texture) UnityEngine.Object.DestroyImmediate(texture); throw; }
            }
            finally { boothThumbnailSlots.Release(); }
        }

        void StartBoothSearch()
        {
            if (boothSearching) return;
            boothQuery = (boothQueryField?.value ?? boothQuery).Trim();
            if (string.IsNullOrWhiteSpace(boothQuery)) { SetBoothStatus("请输入中文需求或商品关键词，例如：双马尾、ミルフィ 衣装。"); boothQueryField?.Focus(); return; }
            if (!AcknowledgeBoothUse("同意并搜索")) return;
            RunBoothSearch(1, true);
        }

        void StartBoothPage(int page)
        {
            if (boothSearching || boothShowingReferences || page < 1 || (page > boothPage && !boothHasNext) || string.IsNullOrWhiteSpace(boothLastQuery)) return;
            if (!AcknowledgeBoothUse("同意并搜索")) return;
            RunBoothSearch(page, false);
        }

        async void RunBoothSearch(int requestedPage, bool newQuery)
        {
            string query = newQuery ? boothQuery : boothLastQuery;
            int category = newQuery ? boothCategory : boothLastCategory, price = newQuery ? boothMaxPrice : boothLastMaxPrice, sort = newQuery ? boothSort : boothLastSort;
            StopBoothRequests(true);
            boothCancellation = new CancellationTokenSource(); var token = boothCancellation.Token; int epoch = boothEpoch;
            boothNetworkSession = true; boothSearching = true; boothShowingReferences = false; boothQueryFailed = false;
            SetBoothStatus("正在查询 BOOTH；未上传角色、截图或本地素材。"); RefreshBoothReferencesUI();
            try
            {
                // API configuration may contain a credential. Keep it transient, never in BoothState.
                var translation = newQuery ? WorkbenchBoothSearchSettings.RequestConfiguration() : null;
                var plan = newQuery ? null : boothLastQueryPlan == null ? null : (JObject)boothLastQueryPlan.DeepClone();
                boothSettingsButton.tooltip = "当前：" + WorkbenchBoothSearchSettings.ModeLabel;
                var response = await WorkbenchBooth.SearchAsync(query, BoothCategoryValues[category], price, BoothSortValues[sort], requestedPage, token, translation, plan);
                if (disposed || epoch != boothEpoch || token.IsCancellationRequested) return;
                boothResults.Clear(); boothResults.AddRange((response["items"] as JArray ?? new JArray()).OfType<JObject>().Take(24).Select(item => (JObject)item.DeepClone()));
                boothLastQuery = query; boothLastCategory = category; boothLastMaxPrice = price; boothLastSort = sort;
                boothLastQueryPlan = SafeBoothQueryPlan(response["query_plan"] as JObject);
                boothPage = Math.Max(1, (int?)response["page"] ?? requestedPage); boothHasNext = (bool?)response["has_next"] ?? false;
                boothSelectedId = "";
                string sortNote = ProductText(response, "sort_note", "");
                SetBoothSearchStatus(sortNote);
                RefreshBoothReferencesUI(); QueueSave();
            }
            catch (OperationCanceledException) { if (!disposed && epoch == boothEpoch) SetBoothStatus("已取消查询。"); }
            catch (Exception e) { if (!disposed && epoch == boothEpoch) { boothQueryFailed = true; SetBoothStatus("搜索未完成：" + ShortBoothError(e.Message) + " · 保留上次结果，可打开 BOOTH 网页。"); } }
            finally { if (!disposed && epoch == boothEpoch) { boothSearching = false; RefreshBoothButtons(); QueueVisibleBoothThumbnails(); } }
        }

        void OpenBoothProduct(JObject product)
        {
            string url = ProductText(product, "url", "");
            if (product == null) url = "https://booth.pm/ja/search/" + Uri.EscapeDataString(boothQuery) + "?adult=none";
            if (!IsBoothProductUrl(url)) { Toast("商品链接不是有效的 BOOTH 官方 HTTPS 地址，未打开。"); return; }
            Application.OpenURL(url);
        }

        void OpenBoothRowImage(VisualElement row)
        {
            string id = row.userData as string;
            var product = boothVisible.FirstOrDefault(p => ProductId(p) == id); if (product == null) return;
            SelectBoothCard(id); OpenBoothImage(product);
        }
        void SelectBoothCard(string id)
        {
            if (string.IsNullOrEmpty(id) || !boothVisible.Any(p => ProductId(p) == id)) return;
            boothSelectedId = id;
            foreach (var card in boothList.Query<VisualElement>(className: "aw-booth-card").ToList()) card.EnableInClassList("aw-booth-selected", (string)card.userData == id);
            RefreshBoothButtons(); QueueSave();
        }

        void OpenBoothImage(JObject product)
        {
            if (product == null || !AcknowledgeBoothUse("同意并查看商品图片")) return;
            WorkbenchBoothImageWindow.ShowProduct(product, this, item =>
            {
                if (!this || disposed) throw new InvalidOperationException("工作台已关闭，请重新打开工作台后加入参考。");
                AddBoothReference((JObject)item.DeepClone()); RefreshBoothReferencesUI(); UpdateComposer();
                if (draft != null) Toast("已加入当前商品参考；旧圈选保留原内容。取消旧附件后，新请求才会附上本次选择。");
                return boothReferences.Any(p => ProductId(p) == ProductId(item));
            });
        }

        static bool IsBoothProductUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort && (uri.Host.Equals("booth.pm", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase));
        }

        void StartBoothDetails()
        {
            var product = SelectedBoothProduct(); if (product == null || !AcknowledgeBoothUse("同意并查看")) return;
            if (boothCancellation == null) { boothCancellation = new CancellationTokenSource(); boothNetworkSession = true; }
            RunBoothDetails((JObject)product.DeepClone(), boothEpoch, ++boothDetailEpoch, boothCancellation.Token);
        }

        async void RunBoothDetails(JObject selected, int epoch, int detailEpoch, CancellationToken token)
        {
            string id = ProductId(selected); boothLoadingDetails = true; boothDetailsButton.SetEnabled(false); SetBoothStatus("正在读取所选商品说明；适配仍需核实。");
            try
            {
                var detail = await WorkbenchBooth.DetailAsync(id, token);
                if (disposed || epoch != boothEpoch || detailEpoch != boothDetailEpoch || token.IsCancellationRequested) return;
                foreach (var result in boothResults.Where(p => ProductId(p) == id)) { result["description"] = detail["description"]?.DeepClone(); result["variations"] = detail["variations"]?.DeepClone(); }
                var merged = (JObject)selected.DeepClone(); merged.Merge(detail, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                // Detail pages may return a formatted price rather than the search result's integer.
                if (merged["price"]?.Type != JTokenType.Integer && selected["price"]?.Type == JTokenType.Integer) merged["price"] = selected["price"].DeepClone();
                if (boothReferences.Any(p => ProductId(p) == id)) { AddBoothReference(merged); RefreshBoothReferencesUI(); }
                WorkbenchBoothInfoWindow.ShowProduct(merged); SetBoothStatus("已读取商品说明；作者描述不等于实际适配通过。"); QueueSave();
                if (draft != null && boothReferences.Any(p => ProductId(p) == id)) Toast("已更新当前商品参考说明；旧圈选所附参考保持原样。");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!disposed && epoch == boothEpoch && detailEpoch == boothDetailEpoch) SetBoothStatus("商品说明读取失败：" + ShortBoothError(e.Message) + "；可查看商品网页。"); }
            finally { if (!disposed && epoch == boothEpoch && detailEpoch == boothDetailEpoch) { boothLoadingDetails = false; RefreshBoothButtons(); } }
        }

        bool AcknowledgeBoothUse(string positive)
        {
            if (boothUsageAcknowledged) return true;
            if (!EditorUtility.DisplayDialog("BOOTH 素材搜索 · 使用声明", WorkbenchBooth.Notice + "\n\n" + WorkbenchBooth.Attribution, positive, "暂不联网")) return false;
            boothUsageAcknowledged = true; QueueSave(); return true;
        }

        void ShowBoothNotice() { WorkbenchBoothInfoWindow.ShowNotice(WorkbenchBooth.Notice + "\n\n" + WorkbenchBooth.Attribution); }
        void ToggleBoothReferences()
        {
            boothShowingReferences = !boothShowingReferences;
            if (!boothShowingReferences) SetBoothSearchStatus("");
            RefreshBoothReferencesUI(); QueueSave();
        }
        void AddSelectedBoothProduct()
        {
            var selected = SelectedBoothProduct(); if (selected == null) return;
            AddBoothReference((JObject)selected.DeepClone()); RefreshBoothReferencesUI(); UpdateComposer();
            if (draft != null) Toast("已加入当前选择；旧圈选仍保留原参考。取消旧附件后，新请求才会附上本次选择。");
        }
        void LinkSelectedBoothProduct()
        {
            var selected = SelectedBoothProduct(); if (selected == null) return;
            LinkBoothReference((JObject)selected.DeepClone()); RefreshBoothReferencesUI(); UpdateComposer();
            if (draft != null) Toast("旧圈选保留原目标与参考；本次关联不会偷偷追加到旧图。请取消旧附件后提交新请求。");
        }
        void RemoveSelectedBoothProduct()
        {
            var selected = SelectedBoothProduct(); if (selected == null) return;
            RemoveBoothReference(ProductId(selected)); RefreshBoothReferencesUI(); UpdateComposer();
            if (draft != null) Toast("已移除当前选择；旧圈选所附参考保持不变。");
        }

        void RefreshBoothReferencesUI()
        {
            if (boothList == null) return;
            boothVisible.Clear(); boothVisible.AddRange(boothShowingReferences ? boothReferences : boothResults);
            if (!boothVisible.Any(p => ProductId(p) == boothSelectedId)) boothSelectedId = "";
            RebuildBoothGrid();
            boothReferencesButton.text = boothShowingReferences ? "返回搜索" : "已选参考 (" + boothReferences.Count + ")";
            boothReferencesButton.tooltip = "查看已加入本次请求的商品参考；这里不是已购买或已安装清单。";
            if (boothShowingReferences) SetBoothStatus(boothReferences.Count == 0 ? "尚未加入商品参考。点击返回搜索，选中商品后再加入。" : "本次商品参考 " + boothReferences.Count + " 项 · 不代表已购买、下载或验证适配。");
            RefreshBoothButtons();
        }

        void RefreshBoothButtons()
        {
            if (boothSearchButton == null) return;
            var selected = SelectedBoothProduct(); bool has = selected != null;
            bool added = has && boothReferences.Any(p => ProductId(p) == ProductId(selected));
            boothSearchButton.SetEnabled(!boothSearching); boothSearchButton.text = boothSearching ? "查询中…" : "搜索";
            boothCancelButton.SetEnabled(boothCancellation != null && !boothCancellation.IsCancellationRequested);
            boothAddButton.SetEnabled(has && !added); boothAddButton.text = added ? "已加入参考" : "加入本次需求";
            boothRemoveButton.SetEnabled(added); boothLinkButton.SetEnabled(has); boothDetailsButton.SetEnabled(has && !boothSearching && !boothLoadingDetails);
            boothOpenButton.text = has ? "查看商品" : "打开搜索网页"; boothOpenButton.SetEnabled(has || !string.IsNullOrWhiteSpace(boothQuery));
            boothImageButton.SetEnabled(has);
            boothImageButton.tooltip = "打开可调整大小的原生窗口，查看作者商品图、BOOTH号和价格；不会试穿或安装。";
            boothPager.style.display = boothShowingReferences ? DisplayStyle.None : DisplayStyle.Flex;
            boothPreviousButton.SetEnabled(!boothSearching && boothPage > 1 && !string.IsNullOrWhiteSpace(boothLastQuery));
            boothNextButton.SetEnabled(!boothSearching && boothHasNext && !string.IsNullOrWhiteSpace(boothLastQuery));
            boothPreviousButton.tooltip = boothNextButton.tooltip = "沿用最近成功搜索的关键词、类别、价格和排序；修改筛选后请点击搜索。";
            RefreshBoothEmptyState();
        }

        void RefreshBoothEmptyState()
        {
            if (boothEmptyMessage == null || boothList == null) return;
            bool empty = boothVisible.Count == 0;
            boothList.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            boothEmptyMessage.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            if (!empty) return;
            boothEmptyMessage.text = boothShowingReferences ? "尚未加入商品参考。\n返回搜索，选中商品后点击“加入本次需求”。"
                : boothSearching ? "正在查询 BOOTH…\n可随时取消。"
                : boothQueryFailed ? "本次查询未完成。\n请重试，或点击下方“打开搜索网页”。"
                : !string.IsNullOrWhiteSpace(boothLastQuery) ? "没有找到匹配商品。\n试试其他关键词、类别或搜索设置。"
                : "输入中文需求或商品关键词，点击“搜索”找素材。\n例如：双马尾、ミルフィ 衣装。";
        }

        JObject SelectedBoothProduct() { return boothVisible.FirstOrDefault(p => ProductId(p) == boothSelectedId); }
        static string ProductId(JObject product) { return ProductText(product, "id", ""); }
        static string ProductText(JObject product, string key, string fallback) { var value = product?[key]; return value == null || value.Type == JTokenType.Null ? fallback : value.ToString(); }
        static string ProductPrice(JObject product)
        {
            string price = ProductText(product, "price", "");
            if (string.IsNullOrWhiteSpace(price)) return "价格以商品页为准";
            return price == "0" ? "免费 · JPY 0" : "JPY " + price.Replace("¥", "").Replace("￥", "");
        }
        static string ShortBoothError(string text) { return string.IsNullOrWhiteSpace(text) ? "网络或接口暂不可用" : text.Length <= 220 ? text : text.Substring(0, 220); }
        void SetBoothStatus(string text) { boothStatusText = text; if (boothStatus != null) { boothStatus.text = text; boothStatus.tooltip = text; } }

        static JObject SafeBoothQueryPlan(JObject plan)
        {
            if (plan == null) return null;
            var safe = new JObject();
            foreach (string key in new[] { "version", "original_query", "source", "translated", "note" }) if (plan[key] is JValue) safe[key] = plan[key].DeepClone();
            safe["keywords"] = new JArray((plan["keywords"] as JArray ?? new JArray()).OfType<JValue>().Where(v => v.Type == JTokenType.String).Take(3).Select(v => v.DeepClone()));
            return safe;
        }
        void SetBoothSearchStatus(string extra)
        {
            string original = ProductText(boothLastQueryPlan, "original_query", boothLastQuery);
            string actual = string.Join(" / ", (boothLastQueryPlan?["keywords"] as JArray ?? new JArray()).Values<string>());
            string query = string.IsNullOrWhiteSpace(actual) ? original : original == actual ? actual : original + " → " + actual;
            string source = ProductText(boothLastQueryPlan, "source", "keyword");
            string sourceLabel = source == "api" ? "独立 API 转换" : source == "local" ? "本地词表" : "原词搜索";
            string summary = string.IsNullOrEmpty(original) ? "输入需求后搜索；点击搜索才会联网。" : query + " · 第 " + boothPage + " 页 · " + boothResults.Count + " 项";
            SetBoothStatus(summary);
            if (boothStatus != null) boothStatus.tooltip = summary + "\n实际检索来源：" + sourceLabel + "\n" + ProductText(boothLastQueryPlan, "note", "商品匹配与中文转换不代表适配验证。") + (string.IsNullOrWhiteSpace(extra) ? "" : "\n" + extra);
        }

        JObject BoothState()
        {
            return new JObject { ["material_source"] = materialSource, ["query"] = boothQuery, ["category"] = boothCategory, ["max_price"] = boothMaxPrice, ["sort"] = boothSort, ["selected_id"] = boothSelectedId, ["show_references"] = boothShowingReferences, ["usage_ack"] = boothUsageAcknowledged, ["last_results"] = new JArray(boothResults.Take(24).Select(p => p.DeepClone())), ["page"] = boothPage, ["has_next"] = boothHasNext, ["last_query"] = boothLastQuery, ["last_category"] = boothLastCategory, ["last_max_price"] = boothLastMaxPrice, ["last_sort"] = boothLastSort, ["last_query_plan"] = SafeBoothQueryPlan(boothLastQueryPlan) };
        }
        void RestoreBoothState(JObject state)
        {
            if (state == null) return;
            string mode = ProductText(state, "material_source", "local"); materialSource = new[] { "local", "booth", "directory", "baidu" }.Contains(mode) ? mode : "local";
            boothQuery = ProductText(state, "query", ""); boothSelectedId = ProductText(state, "selected_id", "");
            boothCategory = Mathf.Clamp((int?)state["category"] ?? 0, 0, BoothCategories.Length - 1); boothSort = Mathf.Clamp((int?)state["sort"] ?? 0, 0, BoothSortNames.Length - 1);
            boothMaxPrice = Math.Max(0, (int?)state["max_price"] ?? 0); boothUsageAcknowledged = (bool?)state["usage_ack"] ?? false; boothShowingReferences = (bool?)state["show_references"] ?? false;
            boothPage = Math.Max(1, (int?)state["page"] ?? 1); boothHasNext = (bool?)state["has_next"] ?? false; boothLastQuery = ProductText(state, "last_query", boothQuery);
            boothLastQueryPlan = SafeBoothQueryPlan(state["last_query_plan"] as JObject);
            boothLastCategory = Mathf.Clamp((int?)state["last_category"] ?? boothCategory, 0, BoothCategories.Length - 1); boothLastSort = Mathf.Clamp((int?)state["last_sort"] ?? boothSort, 0, BoothSortNames.Length - 1); boothLastMaxPrice = Math.Max(0, (int?)state["last_max_price"] ?? boothMaxPrice);
            boothResults.Clear(); boothResults.AddRange((state["last_results"] as JArray ?? new JArray()).OfType<JObject>().Take(24).Select(p => (JObject)p.DeepClone()));
            boothStatusText = boothResults.Count > 0 ? "已恢复上次搜索参考，当前未联网；点击搜索更新结果。" : "输入关键词后搜索；当前未联网。";
        }
        void StopBoothRequests(bool releaseTextures = true)
        {
            bool wasPending = boothSearching || boothLoadingDetails;
            boothThumbnailJob?.Pause();
            boothCoverAnimation?.Pause(); boothCoverWaits.Clear();
            if (boothScroll != null) boothScroll.verticalScroller.valueChanged -= OnBoothScrolled;
            boothScroll = null;
            boothEpoch++; boothDetailEpoch++; boothNetworkSession = false; boothSearching = false; boothLoadingDetails = false;
            if (boothCancellation != null) { boothCancellation.Cancel(); boothCancellation.Dispose(); boothCancellation = null; }
            boothImageTasks.Clear(); boothImageStartedAt.Clear(); boothFailedImages.Clear();
            if (releaseTextures)
            {
                foreach (var texture in boothTextures.Values) if (texture) UnityEngine.Object.DestroyImmediate(texture); boothTextures.Clear();
                if (boothList != null)
                    foreach (var row in boothList.Query<VisualElement>(className: "aw-booth-card").ToList())
                    { var image = row.Q<Image>("booth-thumb"); image.image = null; image.userData = null; SetBoothCoverStatus(row, "未加载"); }
            }
            if (wasPending) SetBoothStatus("联网请求已停止；已有结果保留，点击搜索可重新查询。");
            RefreshBoothButtons();
        }
    }

    internal sealed class WorkbenchBoothInfoWindow : EditorWindow
    {
        // Display normalization removes unsupported mathematical alphabet decoration.
        // Saved references retain the exact original product title.
        internal static string ReadableTitle(string value)
        {
            try { return (value ?? "").Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { return value ?? ""; }
        }
        [SerializeField] string contents = "", productUrl = "", heading = "BOOTH 使用声明";
        internal static void ShowNotice(string text)
        {
            var window = CreateInstance<WorkbenchBoothInfoWindow>(); window.contents = text; window.titleContent = new GUIContent("BOOTH 使用声明"); window.minSize = new Vector2(360, 260); window.ShowUtility(); window.BuildContent();
        }
        internal static void ShowProduct(JObject product)
        {
            var window = CreateInstance<WorkbenchBoothInfoWindow>(); window.heading = product["name"]?.ToString() ?? "BOOTH 商品说明"; window.productUrl = product["url"]?.ToString() ?? "";
            window.contents = "商品参考 · 适配未核实\n\n以下为商品页说明，仅供评估；授权、价格和适配版本以卖家页面为准。\n\n" + (product["description"]?.ToString() ?? "未返回商品说明，请查看商品网页。") + "\n\n规格 / 版本：\n" + FormatVariations(product["variations"] as JArray);
            if ((bool?)product["description_truncated"] == true) window.contents += "\n\n说明过长，已截断展示；请在商品网页查看完整原文。";
            window.titleContent = new GUIContent("BOOTH 商品说明"); window.minSize = new Vector2(420, 320); window.ShowUtility(); window.BuildContent();
        }
        static string FormatVariations(JArray variations)
        {
            if (variations == null || variations.Count == 0) return "以商品网页为准。";
            var lines = new List<string>(); int index = 0;
            foreach (var variant in variations.OfType<JObject>().Take(80))
            {
                index++; string name = variant["name"] is JValue ? variant["name"].ToString() : "";
                if (string.IsNullOrWhiteSpace(name)) name = "规格 " + index + "（名称请看商品页）";
                string price = variant["price"] is JValue ? variant["price"].ToString() : "";
                string priceLabel = string.IsNullOrWhiteSpace(price) ? "价格请看商品页" : "JPY " + price.Replace("¥", "").Replace("￥", "").Trim();
                lines.Add("• " + name + " / " + priceLabel);
            }
            return lines.Count == 0 ? "以商品网页为准。" : string.Join("\n", lines);
        }
        void CreateGUI() { BuildContent(); }
        void BuildContent()
        {
            var root = rootVisualElement; root.Clear(); root.style.paddingLeft = root.style.paddingRight = 12; root.style.paddingTop = root.style.paddingBottom = 10;
            var title = new Label(ReadableTitle(heading)) { enableRichText = false, tooltip = heading }; title.style.whiteSpace = WhiteSpace.Normal; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.marginBottom = 8; title.style.maxHeight = 44; title.style.flexShrink = 0; title.style.overflow = Overflow.Hidden; root.Add(title);
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1; scroll.style.minHeight = 0; scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.contentContainer.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            var body = new Label(contents) { enableRichText = false }; body.style.whiteSpace = WhiteSpace.Normal; body.style.flexShrink = 1; body.style.minWidth = 0; scroll.Add(body); root.Add(scroll);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexShrink = 0; actions.style.marginTop = 8;
            if (Uri.TryCreate(productUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && (uri.Host == "booth.pm" || uri.Host.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase)) && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo))
                actions.Add(new Button(() => Application.OpenURL(productUrl)) { text = "查看商品网页" });
            actions.Add(new Button(Close) { text = "关闭" }); root.Add(actions);
        }
    }
}
