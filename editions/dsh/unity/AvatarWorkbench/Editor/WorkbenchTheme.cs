using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    // Shared native Editor presentation. No project, model or workflow mutations.
    internal static class WorkbenchTheme
    {
        static readonly CustomStyleProperty<Color> IconColor = new CustomStyleProperty<Color>("--aw-icon-color");
        // The editor font (Inter) has no CJK glyphs, so every Chinese character fell back to whichever
        // system font had it first and one sentence mixed two weights. One OS family that covers Latin and
        // CJK replaces that; without any of these families the editor default stays.
        static readonly string[] UiFamilies = { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Source Han Sans SC" };
        const string UiFontName = "AvatarWorkbench UI Font";
        static FontAsset uiFont;

        // WorkbenchStyles.uss.meta; found by GUID so windows declared in another class's file get it too.
        const string StylesGuid = "bf6f18ffe252ffc45b584076ad56acf6";

        internal static void Apply(EditorWindow window, string kind)
        {
            var root = window.rootVisualElement;
            root.AddToClassList("aw-root"); root.AddToClassList("aw-curated"); root.AddToClassList("aw-studio"); root.AddToClassList(kind);
            ApplyFont(root);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(StylesGuid));
            if (!sheet)
            {
                string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(window));
                if (string.IsNullOrEmpty(script)) return;
                sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Path.GetDirectoryName(script).Replace('\\', '/') + "/WorkbenchStyles.uss");
            }
            if (sheet && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        }

        // Windows with their own look (BOOTH image and product info) take only the font.
        internal static void ApplyFont(VisualElement root)
        {
            var font = UiFont();
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
        }

        static FontAsset UiFont()
        {
            if (uiFont) return uiFont;
            // A domain reload clears the static but not the in-memory asset; reuse it instead of building another atlas.
            uiFont = Resources.FindObjectsOfTypeAll<FontAsset>().FirstOrDefault(f => f.name == UiFontName);
            if (uiFont) return uiFont;
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (string family in UiFamilies.Where(installed.Contains))
            {
                var regular = FontAsset.CreateFontAsset(family, "Regular", 90);
                if (regular == null) continue;
                regular.name = UiFontName; regular.hideFlags = HideFlags.DontSave;
                // Slot 7 is bold: use the family's real Bold face rather than a synthesised one.
                var bold = installed.Contains(family + " Bold") ? FontAsset.CreateFontAsset(family, "Bold", 90) : null;
                if (bold != null) { bold.name = UiFontName + " Bold"; bold.hideFlags = HideFlags.DontSave; regular.fontWeightTable[7].regularTypeface = bold; }
                return uiFont = regular;
            }
            return null;
        }

        internal static VisualElement Icon(string kind)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("aw-line-icon");
            Color color = new Color32(0xb9, 0xc0, 0xca, 0xff); // same as the USS default --aw-icon-color
            icon.RegisterCallback<CustomStyleResolvedEvent>(e => { if (e.customStyle.TryGetValue(IconColor, out var c)) { color = c; icon.MarkDirtyRepaint(); } });
            icon.generateVisualContent += context =>
            {
                var p = context.painter2D;
                p.strokeColor = color; p.lineWidth = 1.6f;
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
