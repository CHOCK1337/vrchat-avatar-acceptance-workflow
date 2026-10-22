using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    // Independent native view of saved index facts. Filtering never reads source files.
    internal sealed class WorkbenchLibraryFilters : VisualElement
    {
        readonly ScrollView categoryList, baseList;
        readonly Dictionary<string, Button> categories = new Dictionary<string, Button>();
        readonly Dictionary<string, Button> bases = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        readonly Action<string> chooseCategory, chooseBase;
        readonly Button clear;
        string categorySignature = "", baseSignature = "";

        internal WorkbenchLibraryFilters(Action<string> onCategory, Action<string> onBase, Action onClear)
        {
            name = "catalog-filter-sidebar"; AddToClassList("aw-filter-sidebar");
            chooseCategory = onCategory; chooseBase = onBase;
            var heading = new VisualElement(); heading.AddToClassList("aw-filter-heading");
            heading.Add(Text("分类", "aw-filter-heading-title"));
            clear = new Button(onClear) { text = "清除", name = "clear-catalog-filters", tooltip = "清除分类、适配素体和搜索词；保留已勾选素材。" };
            clear.AddToClassList("aw-filter-clear"); heading.Add(clear); Add(heading);
            categoryList = new ScrollView(ScrollViewMode.Vertical) { name = "catalog-category-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            categoryList.AddToClassList("aw-filter-categories"); Add(categoryList);
            var baseHeading = Text("适配素体", "aw-filter-base-heading");
            baseHeading.tooltip = "来自当前来源的已有适配标签；数量按索引卡片计算，尚未经适配验收。"; Add(baseHeading);
            baseList = new ScrollView(ScrollViewMode.Vertical) { name = "catalog-base-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            baseList.AddToClassList("aw-filter-bases"); baseList.contentContainer.AddToClassList("aw-filter-base-chips"); Add(baseList);
        }

        internal void Refresh(IEnumerable<JObject> index, string category, string baseName, bool hasSearch)
        {
            var items = index.ToArray();
            var categoryCounts = items.GroupBy(WorkbenchCategories.Of).ToDictionary(x => x.Key, x => x.Count());
            categoryCounts[WorkbenchCategories.All] = items.Length;
            var categoryNames = new[] { WorkbenchCategories.All }.Concat(WorkbenchCategories.Order)
                .Concat(categoryCounts.Keys.OrderBy(x => x, StringComparer.Ordinal)).Append(category).Where(x => !string.IsNullOrEmpty(x))
                .Where(x => x == WorkbenchCategories.All || x == category || Count(categoryCounts, x) > 0).Distinct().ToArray();
            string nextCategories = string.Join("\n", categoryNames.Select(x => x + "\t" + Count(categoryCounts, x)));
            if (nextCategories != categorySignature)
            {
                categorySignature = nextCategories; categories.Clear(); categoryList.Clear();
                foreach (string value in categoryNames)
                {
                    string chosen = value;
                    var button = new Button(() => chooseCategory(chosen)) { name = "filter-category-" + value, tooltip = value + " · 当前来源 " + Count(categoryCounts, value) + " 项。仅筛选已有索引，不安装。" };
                    button.AddToClassList("aw-filter-category");
                    var dot = new VisualElement { pickingMode = PickingMode.Ignore }; dot.AddToClassList("aw-filter-dot"); dot.style.backgroundColor = Tint(value);
                    if (value == WorkbenchCategories.All) dot.style.visibility = Visibility.Hidden;
                    button.Add(dot); button.Add(Text(value, "aw-filter-category-name")); button.Add(Text(Count(categoryCounts, value).ToString(), "aw-filter-category-count"));
                    categories.Add(value, button); categoryList.Add(button);
                }
            }
            foreach (var pair in categories) pair.Value.EnableInClassList("aw-filter-selected", pair.Key == category);

            var baseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
                foreach (string value in WorkbenchCategories.Bases(item)) baseCounts[value] = Count(baseCounts, value) + 1;
            var baseNames = baseCounts.Keys.OrderByDescending(x => baseCounts[x]).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrEmpty(baseName) && !baseCounts.ContainsKey(baseName)) baseNames.Insert(0, baseName);
            string nextBases = items.Length + "\n" + string.Join("\n", baseNames.Select(x => x + "\t" + Count(baseCounts, x)));
            if (nextBases != baseSignature)
            {
                baseSignature = nextBases; bases.Clear(); baseList.Clear();
                AddBase("", "不限", items.Length);
                foreach (string value in baseNames) AddBase(value, value, Count(baseCounts, value));
                if (baseNames.Count == 0) baseList.Add(Text("此来源暂无适配标签", "aw-filter-no-bases"));
            }
            foreach (var pair in bases) pair.Value.EnableInClassList("aw-filter-base-selected", string.Equals(pair.Key, baseName ?? "", StringComparison.OrdinalIgnoreCase));
            clear.SetEnabled(category != WorkbenchCategories.All || !string.IsNullOrEmpty(baseName) || hasSearch);
        }

        void AddBase(string value, string label, int count)
        {
            var button = new Button(() => chooseBase(value)) { text = label + " " + count, name = "filter-base-" + (value == "" ? "all" : value), enableRichText = false,
                tooltip = value == "" ? "不限制适配素体，保留分类与搜索词。" : "按已有 " + label + " 标签筛选 · " + count + " 项；不证明适配已通过。" };
            button.AddToClassList("aw-filter-base-chip"); bases[value] = button; baseList.Add(button);
        }
        static int Count(Dictionary<string, int> counts, string name) => counts.TryGetValue(name, out var count) ? count : 0;
        static Label Text(string text, string style) { var label = new Label(text) { enableRichText = false, pickingMode = PickingMode.Ignore }; label.AddToClassList(style); return label; }
        static Color Tint(string category)
        {
            string hex;
            switch (category)
            {
                case "素体": hex = "#c58aff"; break; case "衣服": hex = "#ff8cab"; break;
                case "头发": hex = "#ffc46b"; break; case "配饰": hex = "#82d2ff"; break;
                case "道具": hex = "#b1db76"; break; case "材质": hex = "#ffae83"; break;
                case "面捕": hex = "#6edac4"; break; case "插件": hex = "#9995ff"; break;
                case "动作": hex = "#ecdc77"; break; case "音效": hex = "#91c0ff"; break;
                default: hex = "#9ba8bb"; break;
            }
            ColorUtility.TryParseHtmlString(hex, out var color); return color;
        }
    }
}
