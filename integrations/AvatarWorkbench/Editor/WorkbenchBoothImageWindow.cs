using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    /// <summary>Read-only product artwork viewer. It owns one decoded cover and no avatar objects.</summary>
    internal sealed class WorkbenchBoothImageWindow : EditorWindow
    {
        [SerializeField] string productJson = "";
        JObject product;
        UnityEngine.Object sourceWorkbench;
        Func<JObject, bool> addReference;
        Texture2D productTexture;
        CancellationTokenSource request;
        IVisualElementScheduledItem loadingAnimation;
        Image artwork;
        Label imagePlaceholder, status, imageSource;
        Button loadButton, cancelButton, addButton;
        int generation;
        double loadingStartedAt;
        bool disposed, visible = true, loading;

        internal static void ShowProduct(JObject value, UnityEngine.Object owner, Func<JObject, bool> onAdd)
        {
            if (value == null) return;
            var window = GetWindow<WorkbenchBoothImageWindow>(true, "BOOTH 商品图片", true);
            bool firstProduct = string.IsNullOrEmpty(window.productJson);
            window.CancelAndRelease();
            window.product = (JObject)value.DeepClone(); window.productJson = window.product.ToString(Newtonsoft.Json.Formatting.None);
            window.sourceWorkbench = owner; window.addReference = onAdd;
            window.disposed = false; window.visible = true; window.titleContent = new GUIContent("BOOTH 商品图片");
            if (firstProduct) { var bounds = window.position; bounds.width = 760; bounds.height = 780; window.position = bounds; }
            window.BuildInterface(); window.ShowUtility(); window.Focus(); window.BeginLoad();
        }

        void OnEnable()
        {
            disposed = false; minSize = new Vector2(400, 410);
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += BeforeReload;
            if (!string.IsNullOrEmpty(productJson))
                try { product = JObject.Parse(productJson); } catch { product = null; }
        }

        void OnDisable()
        {
            disposed = true; CancelAndRelease(); addReference = null; sourceWorkbench = null;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            EditorApplication.quitting -= BeforeReload;
        }

        void BeforeReload() { CancelAndRelease(); addReference = null; sourceWorkbench = null; }
        void OnBecameInvisible() { visible = false; CancelAndRelease(); SetStatus("图片已暂停并释放；重新显示后点击“加载商品图片”。"); }
        void OnBecameVisible() { visible = true; if (!loading && !productTexture) SetStatus("点击“加载商品图片”查看所选商品。不会自动安装或试穿。"); }
        void CreateGUI() { BuildInterface(); }

        void BuildInterface()
        {
            loadingAnimation?.Pause(); loadingAnimation = null;
            var root = rootVisualElement; root.Clear(); root.name = "booth-image-window";
            root.style.flexDirection = FlexDirection.Column; root.style.flexGrow = 1; root.style.minHeight = 0; root.style.minWidth = 0;
            root.style.paddingLeft = root.style.paddingRight = 12; root.style.paddingTop = root.style.paddingBottom = 10;
            root.style.backgroundColor = (Color)new Color32(20, 27, 36, 255); root.style.color = (Color)new Color32(233, 240, 246, 255);

            var kind = Text("商品图片 / 作者宣传图", "booth-image-kind"); kind.style.fontSize = 12; kind.style.color = (Color)new Color32(145, 217, 219, 255); root.Add(kind);
            var title = Text(WorkbenchBoothInfoWindow.ReadableTitle(Value(product, "name", "未选择商品")), "booth-image-title");
            title.tooltip = Value(product, "name", ""); title.style.fontSize = 16; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal; title.style.maxHeight = 46; title.style.overflow = Overflow.Hidden; title.style.marginTop = 5; title.style.marginBottom = 6; root.Add(title);
            var identity = Row();
            var id = Text("BOOTH 号：" + Value(product, "id", "未提供"), "booth-image-product-id"); id.style.flexGrow = 1; id.style.fontSize = 13; identity.Add(id);
            var copy = Button("复制 BOOTH 号", () => { GUIUtility.systemCopyBuffer = Value(product, "id", ""); SetStatus("已复制此商品的 BOOTH 号。"); }, "booth-image-copy-id");
            copy.SetEnabled(!string.IsNullOrEmpty(Value(product, "id", ""))); identity.Add(copy); root.Add(identity);
            var price = Text(Price(product) + "    作者 / 店铺：" + Value(product?["shop"] as JObject, "name", "以商品页为准"), "booth-image-price-author");
            price.style.fontSize = 12; price.style.whiteSpace = WhiteSpace.Normal; price.style.maxHeight = 36; price.style.overflow = Overflow.Hidden; price.style.marginTop = 5; price.tooltip = price.text; root.Add(price);
            var notice = Text("宣传图仅供挑选；不代表已购买、已安装或已验证适配。价格和授权以卖家网页为准。", "booth-image-notice");
            notice.style.whiteSpace = WhiteSpace.Normal; notice.style.fontSize = 11; notice.style.marginTop = 7; notice.style.marginBottom = 8; notice.style.color = (Color)new Color32(174, 191, 208, 255); root.Add(notice);

            var imageArea = new VisualElement { name = "booth-image-area" }; imageArea.style.flexGrow = 1; imageArea.style.flexShrink = 1; imageArea.style.flexBasis = 0;
            imageArea.style.minHeight = 80; imageArea.style.minWidth = 0; imageArea.style.overflow = Overflow.Hidden; imageArea.style.backgroundColor = (Color)new Color32(9, 14, 21, 255);
            artwork = new Image { name = "booth-product-artwork", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, image = productTexture };
            artwork.style.width = new StyleLength(new Length(100, LengthUnit.Percent)); artwork.style.height = new StyleLength(new Length(100, LengthUnit.Percent)); imageArea.Add(artwork);
            imagePlaceholder = Text(productTexture ? "" : "商品图片尚未加载", "booth-image-placeholder"); imagePlaceholder.style.position = Position.Absolute;
            imagePlaceholder.style.left = imagePlaceholder.style.right = imagePlaceholder.style.top = imagePlaceholder.style.bottom = 8;
            imagePlaceholder.style.whiteSpace = WhiteSpace.Normal; imagePlaceholder.style.unityTextAlign = TextAnchor.MiddleCenter; imagePlaceholder.style.color = (Color)new Color32(170, 190, 212, 255);
            imagePlaceholder.style.display = productTexture ? DisplayStyle.None : DisplayStyle.Flex; imageArea.Add(imagePlaceholder); root.Add(imageArea);
            imageSource = Text("优先加载 BOOTH 商品原图；原图超时或不可用时回退压缩缩略图。", "booth-image-source");
            imageSource.style.fontSize = 11; imageSource.style.whiteSpace = WhiteSpace.Normal; imageSource.style.marginTop = 5; imageSource.style.maxHeight = 32; imageSource.style.overflow = Overflow.Hidden; root.Add(imageSource);

            var actions = Row(); actions.style.marginTop = 8;
            var web = Button("去 BOOTH 查看 / 购买", OpenProductPage, "booth-image-open-product"); web.style.flexGrow = 1; web.style.flexBasis = 0; web.SetEnabled(IsProductUrl(Value(product, "url", ""))); actions.Add(web);
            addButton = Button("加入本次参考", AddToRequest, "booth-image-add-reference"); addButton.style.flexGrow = 1; addButton.style.flexBasis = 0;
            addButton.SetEnabled(sourceWorkbench && addReference != null); addButton.tooltip = "只加入当前工作台的商品参考；不会购买、下载或改动旧圈选。"; actions.Add(addButton); root.Add(actions);
            var requestActions = Row(); requestActions.style.marginTop = 5;
            loadButton = Button("加载商品图片", BeginLoad, "booth-image-load"); loadButton.style.flexGrow = 1; requestActions.Add(loadButton);
            cancelButton = Button("取消加载", () => { CancelAndRelease(); SetStatus("已取消加载；可点击“加载商品图片”重试。"); SetPlaceholder("图片未加载"); }, "booth-image-cancel"); requestActions.Add(cancelButton);
            requestActions.Add(Button("关闭", Close, "booth-image-close")); root.Add(requestActions);
            status = Text("点击“加载商品图片”查看。", "booth-image-status"); status.style.fontSize = 11; status.style.marginTop = 6; status.style.whiteSpace = WhiteSpace.Normal; status.style.maxHeight = 32; status.style.overflow = Overflow.Hidden; root.Add(status);
            UpdateButtons();
            if (loading) StartLoadingAnimation();
        }

        async void BeginLoad()
        {
            if (disposed || !visible || loading) return;
            string url = Value(product, "image", ""), itemId = Value(product, "id", "");
            if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(itemId)) { SetStatus("商品未提供有效编号或封面；请去 BOOTH 查看原始商品页面。"); SetPlaceholder("未提供商品图片"); return; }
            CancelAndRelease();
            var cancellation = new CancellationTokenSource(); cancellation.CancelAfter(TimeSpan.FromSeconds(35)); request = cancellation; var token = cancellation.Token;
            int currentGeneration = generation; loading = true; loadingStartedAt = EditorApplication.timeSinceStartup; UpdateButtons();
            SetStatus("正在加载作者商品图片，可取消。原图最多等待 12 秒，必要时再尝试缩略图。");
            imageSource.text = "优先读取 BOOTH 商品原图；无法及时取得时使用压缩缩略图。";
            StartLoadingAnimation();
            try
            {
                var image = await WorkbenchBooth.ProductImageAsync(itemId, url, token);
                token.ThrowIfCancellationRequested();
                if (disposed || !visible || currentGeneration != generation) return;
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "AW_BoothProductImage", hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    if (image == null || image.Data == null || !ImageConversion.LoadImage(decoded, image.Data, true)) throw new InvalidOperationException("返回的图片无法解码，请在 BOOTH 商品页查看。");
                    productTexture = decoded; artwork.image = productTexture; imagePlaceholder.style.display = DisplayStyle.None;
                    bool original = string.Equals(image.Source, "original", StringComparison.Ordinal);
                    bool originalTimedOut = !original && (image.Note ?? "").IndexOf("超时", StringComparison.Ordinal) >= 0;
                    imageSource.text = (original ? "来源：BOOTH 商品原图" : originalTimedOut ? "原图超时，已回退压缩缩略图" : "已回退：压缩缩略图") + " · " + decoded.width + " × " + decoded.height + " 像素";
                    imageSource.tooltip = imageSource.text + "\n" + (image.Note ?? "") + (original ? "" : "\n缩略图放大不会增加原图细节。");
                    string elapsed = Math.Max(0, image.ElapsedSeconds).ToString("0.0");
                    SetStatus((original ? "已取得原图" : "已显示压缩缩略图") + " · " + elapsed + " 秒。" + (image.Note ?? ""));
                }
                catch { if (decoded) DestroyImmediate(decoded); throw; }
            }
            catch (OperationCanceledException)
            {
                if (!disposed && currentGeneration == generation) { SetStatus("商品图片加载超时，未取得图片。可重试或去 BOOTH 查看。"); SetPlaceholder("图片暂未取得"); }
            }
            catch (Exception exception)
            {
                if (!disposed && currentGeneration == generation)
                { string detail = exception.Message ?? "网络暂不可用"; SetStatus("图片加载失败：" + (detail.Length > 180 ? detail.Substring(0, 180) : detail)); SetPlaceholder("图片暂不可用\n可重试或查看 BOOTH 商品网页"); }
            }
            finally
            {
                if (!disposed && currentGeneration == generation)
                { loading = false; loadingAnimation?.Pause(); if (ReferenceEquals(request, cancellation)) request = null; UpdateButtons(); }
                cancellation.Dispose();
            }
        }

        void AddToRequest()
        {
            if (!sourceWorkbench || addReference == null)
            { addButton?.SetEnabled(false); SetStatus("工作台连接已关闭或重载；请从工作台重新打开此商品后加入参考。"); return; }
            try
            {
                bool saved = addReference((JObject)product.DeepClone());
                SetStatus(saved ? "已加入当前工作台的商品参考；旧圈选内容保持原样。" : "此商品未加入参考，请查看工作台提示。");
            }
            catch (Exception exception) { SetStatus("加入参考未完成：" + exception.Message); }
        }

        void OpenProductPage()
        {
            string url = Value(product, "url", "");
            if (!IsProductUrl(url)) { SetStatus("未提供有效 BOOTH 商品链接，未打开网页。"); return; }
            Application.OpenURL(url); SetStatus("已交给浏览器打开商品页；购买、授权确认和下载由你在 BOOTH 完成。");
        }

        void CancelAndRelease()
        {
            generation++; loading = false; loadingAnimation?.Pause();
            if (request != null) { request.Cancel(); request.Dispose(); request = null; }
            if (artwork != null) artwork.image = null;
            if (productTexture) DestroyImmediate(productTexture); productTexture = null;
            if (imageSource != null) imageSource.text = "来源：BOOTH 官方商品封面；当前未保留图片。";
            if (imagePlaceholder != null) SetPlaceholder("商品图片尚未加载");
            UpdateButtons();
        }
        void StartLoadingAnimation()
        {
            if (disposed || !visible || !loading || imagePlaceholder == null) return;
            UpdateLoadingAnimation();
            if (loadingAnimation == null) loadingAnimation = rootVisualElement.schedule.Execute(UpdateLoadingAnimation).Every(150);
            else loadingAnimation.Resume();
        }
        void UpdateLoadingAnimation()
        {
            if (disposed || !visible || !loading || imagePlaceholder == null) { loadingAnimation?.Pause(); return; }
            double elapsed = Math.Max(0, EditorApplication.timeSinceStartup - loadingStartedAt);
            string dots = new string('.', 1 + (int)(elapsed * 4) % 3);
            SetPlaceholder("正在加载商品图片" + dots + "\n已等待 " + elapsed.ToString("0.0") + " 秒\n可随时取消");
        }
        void UpdateButtons() { loadButton?.SetEnabled(!loading && product != null); if (loadButton != null) loadButton.text = productTexture ? "重新加载图片" : "加载商品图片"; cancelButton?.SetEnabled(loading); }
        void SetStatus(string text) { if (status != null) { status.text = text; status.tooltip = text; } }
        void SetPlaceholder(string text) { if (imagePlaceholder != null) { imagePlaceholder.text = text; imagePlaceholder.style.display = DisplayStyle.Flex; } }

        static Label Text(string value, string name) { var label = new Label(value) { name = name, enableRichText = false }; label.style.flexShrink = 0; label.style.minWidth = 0; return label; }
        static VisualElement Row() { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.flexShrink = 0; row.style.minWidth = 0; return row; }
        static Button Button(string caption, Action action, string name)
        {
            var button = new Button(action) { text = caption, name = name }; button.style.minHeight = 27; button.style.minWidth = 0; button.style.flexShrink = 1;
            button.style.marginRight = 5; button.style.paddingLeft = button.style.paddingRight = 8; return button;
        }
        static string Value(JObject item, string key, string fallback) { return item?[key] is JValue value && value.Type != JTokenType.Null ? value.ToString() : fallback; }
        static string Price(JObject item)
        {
            string value = Value(item, "price", "").Replace("¥", "").Replace("￥", "").Trim();
            return string.IsNullOrWhiteSpace(value) ? "价格以商品页为准" : value == "0" ? "免费 · JPY 0" : "价格：JPY " + value;
        }
        static bool IsProductUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
                && (uri.Host.Equals("booth.pm", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase));
        }
    }
}
