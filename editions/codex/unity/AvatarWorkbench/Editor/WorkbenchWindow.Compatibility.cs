using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        // Catalog base tags describe the product, not the files on this disk: a folder named
        // "EKU_MILFY" may hold only the MILFY package. Package file names are what hosts install.
        sealed class BodyMismatch { public string name, path; public string[] found; }
        static readonly string[] WearableCategories = { "衣服", "头发", "配饰" };
        VisualElement compatStrip;
        Label compatLabel;
        Toggle compatToggle;
        bool allowOtherBody;
        string compatKey = "", compatBase = "";
        List<BodyMismatch> compatMismatches = new List<BodyMismatch>();

        void BuildCompatibilityStrip(VisualElement parent)
        {
            compatStrip = Row(); compatStrip.AddToClassList("aw-draft-strip"); compatStrip.style.display = DisplayStyle.None;
            compatLabel = new Label { enableRichText = false }; compatLabel.AddToClassList("aw-ellipsis"); compatLabel.style.flexGrow = 1; compatStrip.Add(compatLabel);
            compatToggle = new Toggle("仍然安装（可能不贴合）") { name = "allow-other-body", value = allowOtherBody };
            compatToggle.RegisterValueChangedCallback(e => allowOtherBody = e.newValue);
            compatStrip.Add(compatToggle); parent.Add(compatStrip);
        }

        void UpdateCompatibilityStrip()
        {
            if (compatStrip == null) return;
            var requested = resources.Where(ResourceRequested).ToList();
            string targetBase = RequestBase(requested);
            string key = targetBase + "|" + string.Join("|", requested.Select(r => r.path));
            if (key != compatKey)
            {
                compatKey = key; compatBase = targetBase;
                compatMismatches = string.IsNullOrEmpty(targetBase) ? new List<BodyMismatch>()
                    : requested.Where(r => WearableCategories.Contains(WorkbenchCategories.Of(r))).Select(r => Mismatch(r, targetBase)).Where(m => m != null).ToList();
            }
            bool show = compatMismatches.Count > 0 && draft == null;
            compatStrip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            compatLabel.text = "本地只有其他体型的版本：" + string.Join("、", compatMismatches.Select(m => m.name + "（" + string.Join("/", m.found) + "）"));
            compatLabel.tooltip = "目标体型是 " + compatBase + "，但这些素材的文件里只有其他体型的版本。\n不勾选：AI 会跳过它们，并在回复里说明。\n勾选：AI 会照样安装，回复里会提醒贴合风险。";
            compatToggle.SetValueWithoutNotify(allowOtherBody);
        }

        string RequestBase(List<ResourceCard> requested)
        {
            if (target)
            {
                string fromAvatar = WorkbenchFinishCheck.InferBase(target);
                if (!string.IsNullOrEmpty(fromAvatar)) return fromAvatar;
            }
            foreach (var body in requested.Where(r => WorkbenchCategories.Of(r) == "素体"))
            {
                string hit = WorkbenchFinishCheck.KnownBases.FirstOrDefault(b => WorkbenchFinishCheck.Mentions(body.name + " " + body.path, b));
                if (hit != null) return hit;
            }
            return "";
        }

        static BodyMismatch Mismatch(ResourceCard resource, string targetBase)
        {
            string path = resource.path ?? "";
            if (!Path.IsPathRooted(path)) path = Path.Combine(WorkbenchData.Project, path);
            IEnumerable<string> names;
            try
            {
                names = Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(IsPackage).Take(300).Select(Path.GetFileNameWithoutExtension).ToList()
                    : File.Exists(path) ? new[] { Path.GetFileNameWithoutExtension(path) } : Array.Empty<string>();
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            var list = names.ToList();
            if (list.Count == 0 || list.Any(n => WorkbenchFinishCheck.Mentions(n, targetBase))) return null;
            var others = WorkbenchFinishCheck.KnownBases.Where(b => !b.Equals(targetBase, StringComparison.OrdinalIgnoreCase) && list.Any(n => WorkbenchFinishCheck.Mentions(n, b))).ToArray();
            return others.Length == 0 ? null : new BodyMismatch { name = resource.name, path = resource.path, found = others };
        }
        static bool IsPackage(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".unitypackage" || ext == ".prefab" || ext == ".zip" || ext == ".7z" || ext == ".rar";
        }

        /// <summary>Recorded on the request so every host applies the user's choice the same way.</summary>
        JObject CompatibilityRecord()
        {
            if (compatMismatches.Count == 0) return null;
            return new JObject { ["base"] = compatBase, ["decision"] = allowOtherBody ? "install_anyway" : "skip",
                ["mismatches"] = new JArray(compatMismatches.Select(m => new JObject { ["name"] = m.name, ["path"] = m.path, ["local_versions"] = new JArray(m.found) })),
                ["note"] = allowOtherBody ? "用户允许使用其他体型的版本；回复里要说明贴合风险。" : "用户没有允许使用其他体型的版本；不要安装这些素材，回复里说明已跳过。" };
        }
    }
}
