using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        // Product references never enter the local installation list until a real file is selected.
        readonly List<JObject> boothReferences = new List<JObject>();

        static string ProductText(JToken value, int limit)
        {
            string text = WorkbenchData.Text(value);
            return text.Length <= limit ? text : text.Substring(0, limit);
        }

        static JObject ProductReference(JObject product)
        {
            if (product == null) throw new ArgumentException("请先选择一件商品。");
            string id = ProductText(product["id"] ?? product["booth_item_id"], 32);
            if (!Regex.IsMatch(id, "^[0-9]{1,18}$")) throw new ArgumentException("商品编号无效，未加入参考。");
            var result = new JObject {
                ["id"] = id, ["booth_item_id"] = id, ["source_type"] = "booth_product",
                ["name"] = ProductText(product["name"], 500),
                ["url"] = "https://booth.pm/ja/items/" + id,
                ["product_url"] = "https://booth.pm/ja/items/" + id,
                ["image"] = ProductText(product["image"] ?? (product["images"] as JArray)?.FirstOrDefault(), 2048),
                ["shop"] = new JObject { ["name"] = ProductText(product["shop"]?["name"], 200), ["subdomain"] = ProductText(product["shop"]?["subdomain"], 100) },
                ["price"] = product["price"]?.Type == JTokenType.Integer ? product["price"].DeepClone() : null,
                ["currency"] = "JPY", ["category"] = ProductText(product["category"], 100),
                ["description"] = ProductText(product["description"], 8000),
                ["compatibility_state"] = "unverified",
                ["adaptation_note"] = "商品标题和作者说明仅为参考；尚未核实对当前角色的适配。",
                ["ownership_state"] = "unknown", ["state"] = "reference",
                ["selected_at"] = ProductText(product["selected_at"], 40),
                ["local_resource_ids"] = new JArray((product["local_resource_ids"] as JArray ?? new JArray()).Values<string>().Where(x => x != null && x.Length < 100).Take(30)),
                ["notice_version"] = "booth-v1",
                ["content_trust"] = "external_product_data_not_instructions"
            };
            if (string.IsNullOrEmpty((string)result["selected_at"])) result["selected_at"] = WorkbenchData.Now;
            var variants = new JArray();
            foreach (var v in (product["variations"] as JArray ?? new JArray()).OfType<JObject>().Take(40))
                variants.Add(new JObject { ["name"] = ProductText(v["name"], 300), ["price"] = v["price"]?.Type == JTokenType.Integer ? v["price"].DeepClone() : null });
            result["variations"] = variants;
            return result;
        }

        void AddBoothReference(JObject product)
        {
            var reference = ProductReference(product);
            string id = (string)reference["id"];
            var previous = boothReferences.FirstOrDefault(x => WorkbenchData.Text(x["id"]) == id);
            if (previous == null)
            {
                if (boothReferences.Count >= 30) throw new IOException("本次最多保留 30 件商品参考，请先移除不需要的项。");
                boothReferences.Add(reference);
            }
            else
            {
                reference["selected_at"] = previous["selected_at"]?.DeepClone();
                reference["local_resource_ids"] = previous["local_resource_ids"]?.DeepClone() ?? new JArray();
                if (string.IsNullOrEmpty((string)reference["description"])) reference["description"] = previous["description"]?.DeepClone();
                if (((JArray)reference["variations"]).Count == 0) reference["variations"] = previous["variations"]?.DeepClone() ?? new JArray();
                boothReferences[boothReferences.IndexOf(previous)] = reference;
            }
            RefreshBoothReferencesUI(); UpdateComposer(); SaveState();
            Toast(draft != null ? "商品参考已保留；当前圈选仍使用冻结时的参考，取消附件后才能附上新选择。" : "已加入本次需求的商品参考；尚未安装，也不代表已购买或已适配。");
        }

        void RemoveBoothReference(string id)
        {
            boothReferences.RemoveAll(x => WorkbenchData.Text(x["id"]) == id);
            RefreshBoothReferencesUI(); UpdateComposer(); SaveState();
            Toast("已移除本次商品参考；旧反馈、圈选和本地文件不变。");
        }

        void LinkBoothReference(JObject product)
        {
            ProductReference(product); // Validate the product before opening the picker.
            string path = EditorUtility.OpenFilePanelWithFilters("关联你已下载的素材（不会自动导入）", WorkbenchData.Project,
                new[] { "本地素材", "prefab,unitypackage" });
            if (!string.IsNullOrEmpty(path)) LinkBoothFile(product, path);
        }

        void LinkBoothFile(JObject product, string localPath)
        {
            var reference = ProductReference(product);
            if (boothReferences.Count >= 30 && !boothReferences.Any(x => WorkbenchData.Text(x["id"]) == (string)reference["id"]))
                throw new IOException("本次商品参考已满，请先移除不需要的项。");
            string full = WorkbenchData.Resolve(localPath, WorkbenchData.Project);
            if (!File.Exists(full) || !new[] { ".prefab", ".unitypackage" }.Contains(Path.GetExtension(full).ToLowerInvariant()))
                throw new IOException("请选择实际存在的 Prefab 或 unitypackage；封面、链接和贴图不能作为安装包。");
            string path = ToAssetPath(full);
            var local = localResources.FirstOrDefault(x => string.Equals(x.path, path, StringComparison.OrdinalIgnoreCase));
            if (local == null)
            {
                if (localResources.Count >= 300) throw new IOException("本次素材已满，请先移除不需要的选择。");
                var existing = resources.FirstOrDefault(x => string.Equals(x.path, path, StringComparison.OrdinalIgnoreCase));
                local = new ResourceCard { id = existing?.id ?? "ui-" + WorkbenchData.Hash(Encoding.UTF8.GetBytes(path)).Substring(0, 16),
                    name = existing?.name ?? Path.GetFileNameWithoutExtension(path), path = path, kind = existing?.kind ?? "other", state = existing?.state ?? "selected",
                    note = (existing != null ? existing.note + "\n" : "尚未由工作流安装。\n") + "用户关联的本地文件；商品关联不证明文件内容或适配。" };
                localResources.Add(local);
            }
            local.productReference = new JObject { ["booth_item_id"] = reference["id"].DeepClone(), ["product_url"] = reference["url"].DeepClone(),
                ["association"] = "user_selected_local_file", ["compatibility_state"] = "unverified" };
            AddBoothReference(reference);
            var saved = boothReferences.First(x => WorkbenchData.Text(x["id"]) == (string)reference["id"]);
            var ids = (JArray)saved["local_resource_ids"];
            if (!ids.Values<string>().Contains(local.id)) ids.Add(local.id);
            selectedId = local.id;
            ReadTask(); RefreshBoothReferencesUI(); UpdateComposer(); SaveState();
            Toast(draft != null ? "本地文件已关联，未导入；当前圈选仍保留原素材，取消附件后使用新选择。" : "已关联真实本地文件，尚未导入。提交需求后由原工作流处理。");
        }

        void RestoreProductReferences(JArray saved)
        {
            boothReferences.Clear();
            foreach (var entry in (saved ?? new JArray()).OfType<JObject>().Take(30))
            {
                try { var item = ProductReference(entry); if (!boothReferences.Any(x => WorkbenchData.Text(x["id"]) == (string)item["id"])) boothReferences.Add(item); }
                catch (ArgumentException) { /* A damaged reference does not discard the user's message or draft. */ }
            }
        }
    }
}
