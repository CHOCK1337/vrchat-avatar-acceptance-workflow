using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    // Shared native Editor presentation. No project, model or workflow mutations.
    internal static class WorkbenchTheme
    {
        internal static void Apply(EditorWindow window, string kind)
        {
            var root = window.rootVisualElement;
            root.AddToClassList("aw-root"); root.AddToClassList("aw-curated"); root.AddToClassList(kind);
            string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(window));
            if (string.IsNullOrEmpty(script)) return;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Path.GetDirectoryName(script).Replace('\\', '/') + "/WorkbenchStyles.uss");
            if (sheet && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        }

        internal static VisualElement Icon(string kind)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("aw-line-icon");
            icon.generateVisualContent += context =>
            {
                var p = context.painter2D;
                p.strokeColor = new Color(.77f, .79f, .85f); p.lineWidth = 1.6f;
                void Line(float x, float y, float a, float b) { p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(a, b)); p.Stroke(); }
                void Box(float x, float y, float width, float height) { p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + width, y)); p.LineTo(new Vector2(x + width, y + height)); p.LineTo(new Vector2(x, y + height)); p.ClosePath(); p.Stroke(); }
                if (kind == "model")
                {
                    p.BeginPath(); p.Arc(new Vector2(10, 5), 3, 0, 360); p.Stroke();
                    Line(10, 9, 10, 15); Line(4, 10, 16, 10); Line(10, 15, 5, 19); Line(10, 15, 15, 19);
                }
                else if (kind == "assets") { Box(2, 2, 6, 6); Box(12, 2, 6, 6); Box(2, 12, 6, 6); Box(12, 12, 6, 6); }
                else if (kind == "reply") { Box(2, 3, 16, 12); Line(5, 15, 5, 19); Line(5, 19, 10, 15); Line(6, 7, 14, 7); Line(6, 11, 12, 11); }
                else if (kind == "images") { Box(2, 3, 16, 14); Line(3, 16, 9, 10); Line(9, 10, 13, 14); Line(13, 14, 17, 9); }
                else { Line(3, 5, 17, 5); Line(3, 10, 17, 10); Line(3, 15, 17, 15); Box(6, 3, 3, 4); Box(12, 8, 3, 4); Box(7, 13, 3, 4); }
            };
            return icon;
        }
    }
}
