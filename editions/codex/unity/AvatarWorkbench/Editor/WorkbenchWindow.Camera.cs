using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        readonly HashSet<KeyCode> previewCameraKeys = new HashSet<KeyCode>();
        bool previewCameraActive, previewCameraFast;
        int previewCameraControl;
        double previewCameraTime;
        WorkbenchPreview previewCameraOwner;

        bool CameraPreviewReady() => !disposed && windowVisible && preview != null && !busy && !frozen && !historical
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode
            && canvas != null && canvas.panel != null && previewPanel != null && previewPanel.resolvedStyle.display != DisplayStyle.None;

        bool PreviewTextFieldFocused()
        {
            if (EditorGUIUtility.editingTextField) return true;
            var focused = rootVisualElement.focusController?.focusedElement as VisualElement;
            for (var element = focused; element != null; element = element.parent)
                if (element is TextField) return true;
            return false;
        }

        static bool IsPreviewMovementKey(KeyCode key) => key == KeyCode.W || key == KeyCode.S || key == KeyCode.A
            || key == KeyCode.D || key == KeyCode.Q || key == KeyCode.E;

        void HandlePreviewCamera(Rect area)
        {
            var evt = Event.current;
            int control = GUIUtility.GetControlID("AvatarWorkbenchPreviewCamera".GetHashCode(), FocusType.Keyboard, area);
            if (!CameraPreviewReady()) { StopPreviewCamera(); return; }

            // rawType also observes a release consumed by another control. No cursor lock means
            // crossing the preview boundary ends capture, preventing a released key from sticking.
            if (previewCameraActive && (evt.rawType == EventType.MouseUp && evt.button == 1
                || evt.type == EventType.MouseLeaveWindow || evt.isMouse && !area.Contains(evt.mousePosition)))
            {
                bool released = evt.rawType == EventType.MouseUp && evt.button == 1;
                StopPreviewCamera();
                if (released && evt.type != EventType.Used) evt.Use();
                return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 1 && area.Contains(evt.mousePosition))
            {
                StopPreviewCamera();
                canvas.Focus(); GUI.FocusControl(null); EditorGUIUtility.editingTextField = false;
                previewCameraActive = true; previewCameraFast = evt.shift; previewCameraOwner = preview;
                previewCameraTime = EditorApplication.timeSinceStartup; previewCameraControl = control;
                GUIUtility.hotControl = control; GUIUtility.keyboardControl = control;
                canvas.CaptureMouse(); evt.Use(); canvas.MarkDirtyRepaint();
                return;
            }
            if (previewCameraActive)
            {
                if (PreviewTextFieldFocused() || previewCameraOwner != preview) { StopPreviewCamera(); return; }
                if (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp)
                {
                    if (evt.keyCode == KeyCode.Escape) { StopPreviewCamera(); evt.Use(); return; }
                    if (evt.alt || evt.control || evt.command) { StopPreviewCamera(); return; }
                    previewCameraFast = evt.shift;
                    if (IsPreviewMovementKey(evt.keyCode))
                    {
                        if (evt.type == EventType.KeyDown) previewCameraKeys.Add(evt.keyCode);
                        else previewCameraKeys.Remove(evt.keyCode);
                        evt.Use(); return;
                    }
                    if (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift)
                    {
                        previewCameraFast = evt.type == EventType.KeyDown; evt.Use(); return;
                    }
                }
                if (evt.type == EventType.MouseDrag && evt.button == 1)
                {
                    previewCameraFast = evt.shift;
                    if (evt.delta.sqrMagnitude > 0) { preview.FlyLook(evt.delta); canvas.MarkDirtyRepaint(); }
                    evt.Use(); return;
                }
            }
            if (evt.type == EventType.ContextClick && area.Contains(evt.mousePosition)) { evt.Use(); return; }
            if (evt.type == EventType.MouseDrag && area.Contains(evt.mousePosition) && (evt.button == 0 || evt.button == 2))
            {
                if (evt.button == 0) preview.Orbit(evt.delta); else preview.Pan(evt.delta);
                evt.Use(); canvas.MarkDirtyRepaint();
            }
            if (evt.type == EventType.ScrollWheel && area.Contains(evt.mousePosition))
            {
                preview.Zoom(evt.delta.y); evt.Use(); canvas.MarkDirtyRepaint();
            }
            if (evt.type == EventType.Repaint && previewCameraActive)
                GUI.Label(new Rect(area.x + 10, area.yMax - 28, Mathf.Max(0, area.width - 20), 22), "自由相机 · WASD 移动 / Q E 升降 / Shift 加速 / 松开右键退出", EditorStyles.helpBox);
        }

        void TickPreviewCamera(double now)
        {
            if (!previewCameraActive) return;
            if (!CameraPreviewReady() || focusedWindow != this || PreviewTextFieldFocused() || previewCameraOwner != preview)
            {
                StopPreviewCamera(); return;
            }
            float dt = Mathf.Clamp((float)(now - previewCameraTime), 0, .05f);
            previewCameraTime = now;
            var direction = new Vector3(
                (previewCameraKeys.Contains(KeyCode.D) ? 1 : 0) - (previewCameraKeys.Contains(KeyCode.A) ? 1 : 0),
                (previewCameraKeys.Contains(KeyCode.E) ? 1 : 0) - (previewCameraKeys.Contains(KeyCode.Q) ? 1 : 0),
                (previewCameraKeys.Contains(KeyCode.W) ? 1 : 0) - (previewCameraKeys.Contains(KeyCode.S) ? 1 : 0));
            if (direction.sqrMagnitude == 0 || dt <= 0) return;
            preview.MoveCamera(direction.normalized * (preview.CameraMoveSpeed * (previewCameraFast ? 3f : 1f) * dt));
            canvas.MarkDirtyRepaint();
        }

        void StopPreviewCamera()
        {
            bool wasActive = previewCameraActive;
            int control = previewCameraControl;
            previewCameraActive = false; previewCameraFast = false; previewCameraControl = 0;
            previewCameraTime = 0; previewCameraOwner = null; previewCameraKeys.Clear();
            if (control != 0 && GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            if (control != 0 && GUIUtility.keyboardControl == control) GUIUtility.keyboardControl = 0;
            if (wasActive && canvas != null)
            {
                if (canvas.HasMouseCapture()) canvas.ReleaseMouse();
                canvas.MarkDirtyRepaint();
            }
        }

        public void MovePreviewCameraForward(float direction)
        {
            if (!CameraPreviewReady() || direction == 0) return;
            StopPreviewCamera();
            preview.MoveCamera(Vector3.forward * (Mathf.Sign(direction) * preview.CameraMoveSpeed * .2f));
            canvas.MarkDirtyRepaint();
        }
    }
}
