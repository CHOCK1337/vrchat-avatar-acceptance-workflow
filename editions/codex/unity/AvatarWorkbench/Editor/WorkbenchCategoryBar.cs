using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    // A local view over already indexed cards. Category changes never read source folders.
    internal sealed class WorkbenchCategoryBar : VisualElement
    {
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        readonly Action<string> choose;
        readonly Button more;
        string selected = WorkbenchCategories.All;
        internal WorkbenchCategoryBar(string id, Action<string> onChoose)
        {
            name = id; choose = onChoose; AddToClassList("aw-category-bar");
            foreach (string category in WorkbenchCategories.Quick)
            {
                string value = category;
                var button = new Button(() => choose(value)) { name = id + "-" + value, enableRichText = false };
                button.AddToClassList("aw-category-chip"); buttons[value] = button; Add(button);
            }
            more = new Button(ShowMore) { name = id + "-more", text = "更多分类 ▾", enableRichText = false, tooltip = "面捕、动作、音效、字体、其他、未分类以及自定义分类。" };
            more.AddToClassList("aw-category-chip"); Add(more);
        }
        internal void Refresh(IEnumerable<string> categories, string value)
        {
            selected = string.IsNullOrEmpty(value) ? WorkbenchCategories.All : value;
            counts.Clear(); counts[WorkbenchCategories.All] = 0;
            foreach (string category in categories) { counts[WorkbenchCategories.All]++; counts[category] = Count(category) + 1; }
            foreach (var entry in buttons)
            {
                entry.Value.text = entry.Key + " " + Count(entry.Key);
                entry.Value.tooltip = entry.Key == "材质" ? "材质、妆容、皮肤等资源包；单张图片不作为安装资源。" : "查看已索引的" + entry.Key + "素材；不扫描、不安装。";
                entry.Value.EnableInClassList("aw-category-selected", entry.Key == selected);
            }
            bool secondary = !buttons.ContainsKey(selected);
            more.text = secondary ? selected + " " + Count(selected) + " ▾" : "更多分类 ▾";
            more.EnableInClassList("aw-category-selected", secondary);
        }
        int Count(string key) => counts.TryGetValue(key, out var count) ? count : 0;
        void ShowMore()
        {
            var menu = new UnityEditor.GenericMenu();
            foreach (string category in WorkbenchCategories.Order.Concat(counts.Keys.OrderBy(x => x, StringComparer.Ordinal)).Append(selected).Distinct().Where(x => !buttons.ContainsKey(x)))
            {
                string value = category; menu.AddItem(new GUIContent(value + "  " + Count(value)), value == selected, () => choose(value));
            }
            menu.DropDown(more.worldBound);
        }
    }
}
