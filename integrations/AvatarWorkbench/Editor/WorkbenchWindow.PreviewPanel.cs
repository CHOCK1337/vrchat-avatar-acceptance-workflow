using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        VisualElement previewWorkspace, radialPanel, radialHost;
        IMGUIContainer radialCanvas;
        Button radialButton, expandPreviewButton;
        Label cameraHelp;
        WorkbenchRadialPreview radialPreview;
        bool previewExpanded;

        VisualElement BuildPreviewWorkspace()
        {
            previewWorkspace = new VisualElement { name = "preview-workspace" };
            previewWorkspace.AddToClassList("aw-preview-workspace");
            previewWorkspace.Add(BuildPreviewControls());
            var view = new VisualElement { name = "preview-camera-and-menu" }; view.AddToClassList("aw-preview-split");
            canvas = new IMGUIContainer(DrawPreview) { name = "native-preview", focusable = true };
            canvas.AddToClassList("aw-canvas");
            canvas.RegisterCallback<FocusOutEvent>(_ => StopPreviewCamera());
            canvas.RegisterCallback<MouseCaptureOutEvent>(_ => StopPreviewCamera());
            canvas.RegisterCallback<MouseLeaveEvent>(_ => StopPreviewCamera());
            canvas.RegisterCallback<DetachFromPanelEvent>(_ => StopPreviewCamera());
            view.Add(canvas);
            radialPanel = new VisualElement { name = "preview-radial-panel" }; radialPanel.AddToClassList("aw-radial-panel");
            radialHost = new VisualElement { name = "preview-radial-host" }; radialHost.AddToClassList("aw-radial-host");
            radialCanvas = new IMGUIContainer(DrawRadialMenu) { name = "preview-radial-canvas", focusable = true };
            radialCanvas.style.flexGrow = 1; radialCanvas.style.minHeight = 0; radialHost.Add(radialCanvas);
            radialPanel.Add(radialHost); radialPanel.style.display = DisplayStyle.None; view.Add(radialPanel);
            previewWorkspace.Add(view);
            cameraHelp = new Label("右键 + WASD 移动 · Q/E 升降 · Shift 加速 · 左拖旋转 · 滚轮拉近") { name = "preview-camera-help", pickingMode = PickingMode.Ignore };
            cameraHelp.AddToClassList("aw-camera-help"); previewWorkspace.Add(cameraHelp);
            return previewWorkspace;
        }

        void ToggleRadialMenu()
        {
            if (radialPreview != null) { CloseRadialMenu(); UpdateLabels(); return; }
            StopPreviewCamera(); EnsureTrial();
            // The installed menu uses a 300 px wheel. Give it real room instead of
            // shrinking its controls or covering the avatar with it.
            if (canvas.resolvedStyle.height < WorkbenchRadialPreview.MinimumSize || canvas.resolvedStyle.width < 720)
                previewExpanded = true;
            activePanel = "preview"; ApplyLayout(position.width, position.height);
            var next = new WorkbenchRadialPreview(preview.Gesture, this, radialHost);
            if (!next.Ready) { string error = next.Error; next.Dispose(); throw new InvalidOperationException(error); }
            radialPreview = next; radialPanel.style.display = DisplayStyle.Flex;
            radialCanvas.MarkDirtyRepaint(); canvas.MarkDirtyRepaint(); UpdateLabels();
            Toast("圆盘已打开：点击衣服、配件或表情查看效果。只改变临时试穿；点“完成预览”回到需求输入。");
        }

        void DrawRadialMenu()
        {
            if (radialPreview == null) return;
            if (!CameraPreviewReady()) { CloseRadialMenu(); return; }
            try
            {
                if (radialPreview.DrawLayout())
                {
                    preview.SampleGesturePose(); canvas.MarkDirtyRepaint(); UpdateLabels();
                }
                if (!string.IsNullOrEmpty(radialPreview?.Error))
                    EditorGUILayout.HelpBox(radialPreview.Error, MessageType.Info);
            }
            catch (Exception e) { CloseRadialMenu(); Toast("圆盘暂时无法预览：" + (e.InnerException ?? e).Message); }
        }

        void CloseRadialMenu()
        {
            var previous = radialPreview; radialPreview = null;
            try { previous?.Dispose(); }
            finally
            {
                if (radialPanel != null) radialPanel.style.display = DisplayStyle.None;
                if (radialButton != null) radialButton.text = "圆盘菜单";
            }
        }

        void TogglePreviewWorkspace()
        {
            StopPreviewCamera();
            previewExpanded = !previewExpanded; activePanel = "preview";
            if (!previewExpanded) CloseRadialMenu();
            ApplyLayout(position.width, position.height); canvas?.MarkDirtyRepaint();
            if (!previewExpanded) Toast("已回到编辑工作台。下方写一句需求，发送给 Codex。");
        }

        void ApplyPreviewWorkspace()
        {
            if (activePanel != "preview") { previewExpanded = false; StopPreviewCamera(); CloseRadialMenu(); }
            rootVisualElement.EnableInClassList("aw-preview-expanded", previewExpanded);
            var header = rootVisualElement.Q<VisualElement>(className: "aw-header");
            if (header != null) header.style.display = previewExpanded ? DisplayStyle.None : DisplayStyle.Flex;
            if (connectionBox != null) connectionBox.style.display = previewExpanded ? DisplayStyle.None : DisplayStyle.Flex;
            if (composer != null) composer.style.display = previewExpanded ? DisplayStyle.None : DisplayStyle.Flex;
            if (previewExpanded)
            {
                tabBar.style.display = DisplayStyle.None;
                materialPanel.style.display = resultPanel.style.display = DisplayStyle.None;
                previewPanel.style.display = DisplayStyle.Flex; previewPanel.style.marginLeft = previewPanel.style.marginRight = 0;
            }
            if (expandPreviewButton != null) expandPreviewButton.text = previewExpanded ? "完成预览" : "放大预览";
        }
    }
}
