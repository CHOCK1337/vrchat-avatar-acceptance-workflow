using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    /// <summary>The installed Gesture Manager's actual radial, bound to one temporary module.</summary>
    internal sealed class WorkbenchRadialPreview : IDisposable
    {
        public const float MinimumSize = 320f;
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string Prefix = "BlackStartX.GestureManager.Editor.Modules.Vrc3.";
        readonly WorkbenchGesturePreview gesture;
        readonly ScriptableObject owner;
        readonly VisualElement host;
        readonly VisualElement renderRoot;
        object module, menu;
        VisualElement visual;
        MethodInfo render, openMenu, setButtons, removeMenu, closePuppet;
        FieldInfo menuRect, menuPath, buttons, puppet, pressingButton, registry;
        Array filteredRootButtons;
        Rect lastRect;
        int controlId;
        bool pointerDown, disposed, suspended = true;

        public string Error { get; private set; }
        public bool Ready => !disposed && menu != null && ReferenceEquals(module, gesture.NativeModule) && string.IsNullOrEmpty(Error);
        public bool CapturingPointer => pointerDown || (Ready && puppet.GetValue(menu) != null);

        // host is a dedicated, origin-aligned area containing the caller's full-size
        // IMGUIContainer. Its title and close button should be outside this area.
        public WorkbenchRadialPreview(WorkbenchGesturePreview gesture, ScriptableObject owner, VisualElement host)
        {
            this.gesture = gesture ?? throw new ArgumentNullException(nameof(gesture));
            this.owner = owner ? owner : throw new ArgumentNullException(nameof(owner));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            renderRoot = new VisualElement { name = "AW_NativeRadialRoot", pickingMode = PickingMode.Ignore };
            renderRoot.style.position = Position.Absolute;
            renderRoot.style.left = renderRoot.style.top = 0;
            renderRoot.style.right = renderRoot.style.bottom = 0;
            renderRoot.style.overflow = Overflow.Hidden;
            try
            {
                module = gesture.NativeModule ?? throw new InvalidOperationException("请先为已处理候选开启独立功能预览。");
                var type = WorkbenchGesturePreview.FindType(Prefix + "RadialMenu");
                var parameterType = WorkbenchGesturePreview.FindType(Prefix + "Params.Vrc3Param");
                var expressionType = WorkbenchGesturePreview.FindType("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
                var sliceType = WorkbenchGesturePreview.FindType(Prefix + "RadialSlices.RadialSliceBase");
                if (type == null || parameterType == null || expressionType == null || sliceType == null)
                    throw new InvalidOperationException("已安装的 Gesture Manager 缺少原生圆盘接口。");
                render = RequiredMethod(type, "Render", typeof(VisualElement), typeof(Rect));
                openMenu = RequiredMethod(type, "OpenMenu", expressionType, parameterType, typeof(float));
                setButtons = RequiredMethod(type, "SetButtons", sliceType.MakeArrayType(), typeof(int));
                removeMenu = RequiredMethod(type, "RemoveMenu", typeof(int));
                closePuppet = RequiredMethod(type, "ClosePuppet");
                menuRect = RequiredField(type, "Rect");
                menuPath = RequiredField(type, "_menuPath");
                buttons = RequiredField(type, "_buttons");
                puppet = RequiredField(type, "_puppet");
                pressingButton = RequiredField(type, "PressingButton");
                registry = RequiredField(module.GetType(), "_radialMenus");
                // This is the same overload used by GM's own floating menu. The public
                // one-argument overload inserts an unrelated GUILayout credit label.
                var create = RequiredMethod(module.GetType(), "GetOrCreateRadial", typeof(ScriptableObject), typeof(bool));
                menu = create.Invoke(module, new object[] { owner, true });
                visual = menu as VisualElement ?? throw new InvalidOperationException("原生圆盘不是可嵌入的 UIElements 组件。");
                var authorMenu = RequiredField(type, "_menu").GetValue(menu);
                if (!authorMenu.IsUnityObject()) throw new InvalidOperationException("处理后候选没有原生表情菜单。");
                openMenu.Invoke(menu, new[] { authorMenu, null, (object)0f });
                KeepAuthorRoot();
                MakeUnpickable(visual);
            }
            catch (Exception e)
            {
                Error = "原生圆盘不可用：" + (e.InnerException ?? e).Message;
                DetachAndUnregister();
            }
        }

        static MethodInfo RequiredMethod(Type type, string name, params Type[] args) =>
            type.GetMethod(name, Flags, null, args, null) ?? throw new InvalidOperationException("Gesture Manager 接口版本不兼容：" + name);
        static FieldInfo RequiredField(Type type, string name) =>
            type.GetField(name, Flags) ?? throw new InvalidOperationException("Gesture Manager 接口版本不兼容：" + name);
        static void MakeUnpickable(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (var child in element.Children()) MakeUnpickable(child);
        }

        void KeepAuthorRoot()
        {
            var path = (IList)menuPath.GetValue(menu);
            if (path.Count != 1) return;
            var current = (Array)buttons.GetValue(menu);
            if (ReferenceEquals(current, filteredRootButtons)) return;
            if (current.Length <= 2) throw new InvalidOperationException("角色根菜单没有可用的控制项。");
            // Native SetMenu adds Home/Quick Actions before the author's controls.
            // Omit only these GM tool entries; all retained slices, submenu pages,
            // icons, labels, toggles, buttons and puppets remain native objects.
            if (current.GetValue(0).GetType().Name != "RadialSliceButton" || current.GetValue(1).GetType().Name != "RadialSliceButton")
                throw new InvalidOperationException("Gesture Manager 的根菜单结构与当前适配版本不兼容。");
            filteredRootButtons = Array.CreateInstance(current.GetType().GetElementType(), current.Length - 2);
            Array.Copy(current, 2, filteredRootButtons, 0, filteredRootButtons.Length);
            setButtons.Invoke(menu, new object[] { filteredRootButtons, -1 });
        }

        public bool DrawLayout()
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            return Draw(rect);
        }

        // Call in IMGUI. True means actual temporary parameter values changed and the
        // caller should sample the preview output; GesturePreview already woke its graph.
        public bool Draw(Rect rect)
        {
            if (!Ready || Event.current == null) { renderRoot.RemoveFromHierarchy(); return false; }
            var current = Event.current;
            if (current.type != EventType.Layout && current.type != EventType.Used && rect.width > 1 && rect.height > 1) lastRect = rect;
            if (lastRect.width < MinimumSize || lastRect.height < MinimumSize)
            {
                Suspend();
                if (current.type == EventType.Repaint) GUI.Label(rect, "放大预览区域后可显示原生圆盘（至少 320 × 320）。", EditorStyles.wordWrappedLabel);
                return false;
            }
            controlId = GUIUtility.GetControlID("AW_NativeRadial".GetHashCode(), FocusType.Passive, lastRect);
            bool inside = lastRect.Contains(current.mousePosition);
            bool mouseInput = current.type == EventType.MouseMove || current.type == EventType.MouseDrag || current.type == EventType.MouseDown || current.type == EventType.MouseUp;
            bool useInput = GUI.enabled && mouseInput && (inside || pointerDown);
            if ((current.type == EventType.MouseDown || current.type == EventType.MouseUp || current.type == EventType.MouseDrag) && current.button != 0) useInput = false;
            if ((current.type == EventType.MouseUp || current.type == EventType.MouseDrag) && !pointerDown) useInput = false;
            bool mayChange = useInput && (current.type == EventType.MouseDown || current.type == EventType.MouseUp || puppet.GetValue(menu) != null);
            var before = mayChange ? gesture.CaptureNativeMenuParameters() : null;
            bool changed = false;
            try
            {
                suspended = false;
                if (renderRoot.parent != host) host.Add(renderRoot);
                if (visual.parent != renderRoot) renderRoot.Add(visual);
                // GM Render does not assign this field; its cursor reads this exact rect.
                menuRect.SetValue(menu, lastRect);
                if (useInput && current.type == EventType.MouseDown)
                {
                    pointerDown = true;
                    GUIUtility.hotControl = controlId;
                }
                if (useInput && (current.type == EventType.MouseDown || current.type == EventType.MouseUp) && puppet.GetValue(menu) == null)
                    RenderWithEvent(current, EventType.MouseMove); // Refresh cursor before native click dispatch.
                if (useInput && current.type == EventType.MouseUp && !inside && puppet.GetValue(menu) == null)
                    ReleaseButton(); // Releasing outside must not activate a stale hovered toggle.
                else RenderWithEvent(current, useInput ? current.type : EventType.Layout);
                KeepAuthorRoot();
                MakeUnpickable(visual);
                if (before != null) changed = gesture.CompleteNativeMenuInput(before);
                if (useInput && current.type == EventType.MouseUp) ReleasePointer();
                if (inside && current.isMouse || pointerDown && current.isMouse)
                {
                    current.Use();
                    if (owner is EditorWindow window) window.Repaint();
                }
            }
            catch (Exception e)
            {
                Error = "原生圆盘已暂停：" + (e.InnerException ?? e).Message;
                Suspend();
            }
            return changed;
        }

        void RenderWithEvent(Event current, EventType type)
        {
            // Native HandleExternalInput writes puppet parameters even on Repaint.
            // A scoped Layout event keeps passive rendering side-effect-free without
            // altering GM or suppressing actual mouse input.
            var original = Event.current;
            try
            {
                Event.current = new Event(current) { type = type };
                render.Invoke(menu, new object[] { renderRoot, lastRect });
            }
            finally { Event.current = original; }
        }

        void ReleaseButton()
        {
            var pressed = pressingButton?.GetValue(menu);
            if (pressed != null) RequiredMethod(pressed.GetType(), "OnClickEnd").Invoke(pressed, null);
        }
        void ReleasePointer()
        {
            if (pointerDown && GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
            pointerDown = false;
        }

        // Cleanup writes only the already-owned temporary module. It releases a held
        // button, puppet gate and nested submenu gates, preserving selected outfit and
        // puppet sub-parameter values. No source GameObject or asset is touched.
        public void Suspend()
        {
            ReleasePointer();
            if (suspended) { renderRoot.RemoveFromHierarchy(); return; }
            suspended = true;
            if (menu != null && ReferenceEquals(module, gesture.NativeModule))
            {
                var before = gesture.CaptureNativeMenuParameters();
                var original = Event.current;
                try
                {
                    if (original == null) Event.current = new Event { type = EventType.Layout };
                    ReleaseButton();
                    if (puppet?.GetValue(menu) != null) closePuppet.Invoke(menu, null);
                    if (menuPath?.GetValue(menu) is IList path)
                        while (path.Count > 1) removeMenu.Invoke(menu, new object[] { path.Count - 1 });
                    // RemoveMenu releases gates but does not reopen the parent page.
                    if (menuPath?.GetValue(menu) is IList remainingPath && remainingPath.Count == 1)
                    {
                        RequiredMethod(remainingPath[0].GetType(), "Open").Invoke(remainingPath[0], null);
                        KeepAuthorRoot();
                    }
                    gesture.CompleteNativeMenuInput(before);
                }
                catch (Exception e) { Error = "原生圆盘释放失败：" + (e.InnerException ?? e).Message; }
                finally { Event.current = original; }
            }
            renderRoot.RemoveFromHierarchy();
        }

        void DetachAndUnregister()
        {
            visual?.RemoveFromHierarchy();
            renderRoot.RemoveFromHierarchy();
            if (registry?.GetValue(module) is IDictionary menus && menus.Contains(owner) && ReferenceEquals(menus[owner], menu)) menus.Remove(owner);
            menu = null; visual = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            Suspend();
            DetachAndUnregister();
            disposed = true;
        }
    }
}
