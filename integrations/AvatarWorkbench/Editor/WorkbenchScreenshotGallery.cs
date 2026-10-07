using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    internal sealed class WorkbenchScreenshotGallery : EditorWindow
    {
        readonly List<Texture2D> loaded = new List<Texture2D>();
        [SerializeField] string recordJson = "";
        internal static void Open(IEnumerable<JObject> records)
        {
            var window = GetWindow<WorkbenchScreenshotGallery>(false, "截图记录 · 保留原候选", true);
            window.minSize = new Vector2(680, 420); window.recordJson = new JArray(records.Take(100)).ToString(); window.CreateGUI(); window.Show();
        }
        void Release() { foreach (var texture in loaded) if (texture) DestroyImmediate(texture); loaded.Clear(); }
        void OnDisable() { Release(); }
        public void CreateGUI()
        {
            Release(); var root = rootVisualElement; root.Clear(); root.style.paddingLeft = root.style.paddingRight = 12; root.style.paddingTop = root.style.paddingBottom = 10;
            var title = new Label("截图记录：看清画面和它所属的版本"); title.style.fontSize = 20; root.Add(title);
            var note = new Label("这些是已有截图。已回应只表示收到答复，不表示修复通过；打开旧图仍保留原候选身份。"); note.style.whiteSpace = WhiteSpace.Normal; root.Add(note);
            var items = string.IsNullOrEmpty(recordJson) ? new List<JObject>() : JArray.Parse(recordJson).OfType<JObject>().ToList();
            if (items.Count == 0) { root.Add(new Label("还没有截图；先在工作台冻结真实画面，或打开已有截图清单。")); return; }
            var list = new ListView { itemsSource = items, fixedItemHeight = 245, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, selectionType = SelectionType.None, name = "screenshot-records" }; list.style.flexGrow = 1; root.Add(list);
            list.makeItem = () => { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.paddingTop = row.style.paddingBottom = 8; return row; };
            void ReleaseRow(VisualElement row) { if (row.userData is Texture2D old) { loaded.Remove(old); if (old) DestroyImmediate(old); } row.userData = null; }
            list.unbindItem = (row, index) => ReleaseRow(row);
            list.bindItem = (row, index) =>
            {
                ReleaseRow(row); row.Clear(); var record = items[index];
                var image = new Image { scaleMode = ScaleMode.ScaleToFit }; image.style.width = 310; image.style.height = 220; image.style.flexShrink = 0; row.Add(image);
                var details = new VisualElement(); details.style.flexGrow = 1; details.style.minWidth = 0; details.style.paddingLeft = 12; row.Add(details);
                void Label(string text) { var label = new UnityEngine.UIElements.Label(text) { enableRichText = false }; label.style.whiteSpace = WhiteSpace.Normal; details.Add(label); }
                Label(WorkbenchUiRules.Short((string)record["title"] ?? "已有原图", 80)); Label("候选：" + WorkbenchData.Identity(record["candidate"] as JObject));
                Label("截图：" + WorkbenchData.Text(record["capture_kind"], "历史 / 参考图")); Label(WorkbenchData.Text(record["created_at"]));
                try
                {
                    string path = WorkbenchData.Resolve((string)record["path"], WorkbenchData.Project);
                    if (!string.IsNullOrEmpty((string)record["sha256"]) && WorkbenchData.FileHash(path) != (string)record["sha256"]) throw new IOException("原图哈希不符，未显示替代图。");
                    var texture = WorkbenchData.LoadImage(path); loaded.Add(texture); row.userData = texture; image.image = texture;
                    details.Add(new Button(() => { var owner = Resources.FindObjectsOfTypeAll<WorkbenchWindow>().FirstOrDefault(); if (owner) { owner.ShowRecordedImage(record); owner.Focus(); Close(); } else Label("工作台已关闭；重新打开后可继续查看这份原图。"); }) { text = "在工作台查看原图 / 圈选", name = "open-record-" + index });
                }
                catch (Exception e) { Label("无法读取：" + e.Message); }
            };
        }
    }
}
