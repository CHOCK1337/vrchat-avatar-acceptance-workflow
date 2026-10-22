using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AvatarWorkbench
{
    // Independent classification adapter. Only index facts are read; no directory or AssetDatabase scan.
    internal static class WorkbenchCategories
    {
        internal const string All = "全部", Unclassified = "未分类";
        internal const string MioUrl = "https://github.com/CokoIya/MioVRC_AssetManager";
        internal static readonly string[] Order = { "素体", "衣服", "头发", "配饰", "道具", "材质", "面捕", "插件", "动作", "音效", "字体", "其他", Unclassified };
        internal static readonly string[] Quick = { All, "素体", "衣服", "头发", "配饰", "道具", "材质", "插件" };

        internal static string Normalize(string raw)
        {
            string value = (raw ?? "").Trim();
            switch (value.ToLowerInvariant())
            {
                case "": case "未分类": case "unknown": return Unclassified;
                case "avatar": case "base": case "avatar base": case "3dキャラクター": case "3d character": case "素体": return "素体";
                case "clothes": case "clothing": case "outfit": case "衣装": case "服装": case "3d衣装": case "3d clothing": case "衣服": return "衣服";
                case "hair": case "hair style": case "发型": case "髪型": case "頭髪": case "头发": return "头发";
                case "accessory": case "accessories": case "饰品": case "装饰": case "3d装飾品": case "3d accessory": case "配饰": return "配饰";
                case "prop": case "props": case "gimmick": case "3d小道具": case "3d prop": case "道具": return "道具";
                case "material": case "texture": case "makeup": case "skin": case "妆容": case "皮肤": case "贴图": case "纹理": case "3dテクスチャ": case "3d texture": case "材质": return "材质";
                case "face tracking": case "facetracking": case "面捕": return "面捕";
                case "plugin": case "tool": case "tools": case "工具": case "插件": return "插件";
                case "animation": case "pose": case "姿势": case "表情": case "动作": return "动作";
                case "audio": case "sound": case "音声": case "音效": return "音效";
                case "font": case "字体": return "字体";
                case "other": case "others": case "その他": case "其他": return "其他";
                default: return value.Length > 48 ? value.Substring(0, 48) : value; // Keep custom categories visible.
            }
        }

        internal static string Of(JObject item)
        {
            string declared = WorkbenchData.Text(item?["category"]);
            return string.IsNullOrWhiteSpace(declared) ? Infer(WorkbenchData.Text(item?["name"]), WorkbenchData.Text(item?["kind"])) : Normalize(declared);
        }
        internal static string Of(ResourceCard item) => Of(new JObject { ["category"] = item.productReference?["category"]?.DeepClone(), ["name"] = item.name, ["kind"] = item.kind });
        internal static IEnumerable<string> Bases(JObject item) => (item?["bases"] as JArray ?? new JArray()).OfType<JValue>()
            .Where(x => x.Type == JTokenType.String).Select(x => ((string)x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
        internal static bool MatchesBase(JObject item, string baseName) => string.IsNullOrEmpty(baseName) || Bases(item).Contains(baseName, StringComparer.OrdinalIgnoreCase);
        static string Infer(string name, string kind)
        {
            if (kind == "plugin") return "插件";
            // Use explicit name hints only. An avatar compatibility label never makes clothing a base body.
            foreach (var rule in new[] {
                Tuple.Create("面捕", @"面捕|face[ _-]?tracking"), Tuple.Create("头发", @"发型|髪型|ヘア|\bhair\b"),
                Tuple.Create("衣服", @"衣服|衣装|服装|ワンピース|\b(dress|outfit|clothing)\b"),
                Tuple.Create("材质", @"妆容|贴图|纹理|メイク|\b(makeup|texture)\b"),
                Tuple.Create("配饰", @"配饰|饰品|アクセサリー|\baccessor(?:y|ies)\b"),
                Tuple.Create("道具", @"道具|ギミック|\b(prop|gimmick)\b"), Tuple.Create("插件", @"插件|\bplugin\b"),
                Tuple.Create("动作", @"动作|姿势|\b(animation|pose)\b"), Tuple.Create("音效", @"音效|効果音|\bsfx\b"),
                Tuple.Create("素体", @"素体|オリジナル\s*3dモデル|\bavatar[ _-]?base\b") })
                if (Regex.IsMatch(name ?? "", rule.Item2, RegexOptions.IgnoreCase)) return rule.Item1;
            return Unclassified;
        }
        internal static bool IsMio(JObject item) => WorkbenchData.Text(item?["origin_source"]) == "mio_library" || WorkbenchData.Text(item?["metadata_source"]) == "mio_library" || WorkbenchData.Text(item?["id"]).StartsWith("mio-", StringComparison.Ordinal);
        internal static string Origin(JObject item) => IsMio(item) ? "来源：Mio 素材库" : "来源：本地素材";
        internal static string Origin(ResourceCard item) => IsMio(item.productReference) || (item.id ?? "").StartsWith("mio-", StringComparison.Ordinal) ? "来源：Mio 素材库" : "来源：本地素材";
        internal static string Explanation(JObject item)
        {
            string source = WorkbenchData.Text(item?["category_source"]);
            if (source == "mio_user") return "Mio 中的手动分类";
            if (source == "mio_booth_cache") return "Mio 中已有的 BOOTH 分类缓存";
            if (IsMio(item)) return "Mio 素材库记录中的分类";
            return string.IsNullOrWhiteSpace(WorkbenchData.Text(item?["category"])) ? Of(item) == Unclassified ? "没有明确分类；保留在未分类中" : "依据素材名称的线索，尚未确认" : "素材已有的分类记录";
        }
        internal static string MioCategory(JObject user, JObject asset, JObject product, out string source)
        {
            string manual = WorkbenchData.Text(user["category"]), indexed = WorkbenchData.Text(asset["category"]), cached = WorkbenchData.Text(product?["category"]);
            if (!string.IsNullOrWhiteSpace(manual)) { source = "mio_user"; return manual; }
            // Cached BOOTH categories are broad; retain the more specific local categories.
            bool specific = new[] { "头发", "面捕", "插件", "动作", "音效", "字体" }.Contains(Normalize(indexed));
            if (!specific && !string.IsNullOrWhiteSpace(cached) && Order.Contains(Normalize(cached)) && (Normalize(cached) != "其他" || Normalize(indexed) == "其他" || string.IsNullOrWhiteSpace(indexed))) { source = "mio_booth_cache"; return cached; }
            source = "mio_index"; return indexed;
        }
    }
}
