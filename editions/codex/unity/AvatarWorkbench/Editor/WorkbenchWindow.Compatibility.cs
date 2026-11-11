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
        static readonly List<string> FitLabels = new List<string> { "按本次需求", "跳过这些素材", "仍然安装（可能不贴合）", "我来手动适配" };
        static readonly string[] FitDecisions = { "from_request", "skip", "install_anyway", "manual_fit" };
        PopupField<string> fitChoice;
        string compatKey;
        JObject liveCompatibility;

        void BuildCompatibilityStrip(VisualElement parent)
        {
            compatStrip = Row(); compatStrip.AddToClassList("aw-draft-strip"); compatStrip.style.display = DisplayStyle.None;
            compatLabel = new Label { enableRichText = false }; compatLabel.AddToClassList("aw-ellipsis"); compatLabel.style.flexGrow = 1; compatStrip.Add(compatLabel);
            fitChoice = new PopupField<string>(FitLabels, 0) { name = "other-body-route" };
            fitChoice.RegisterValueChangedCallback(e => {
                var record = draft != null ? draft["compatibility"] as JObject : CompatibilityRecord();
                int index = FitLabels.IndexOf(e.newValue);
                if (record == null || index < 0) return;
                record["decision"] = FitDecisions[index];
                record["note"] = e.newValue;
                if (draft != null) QueueSave();
            });
            compatStrip.Add(fitChoice); parent.Add(compatStrip);
        }

        void UpdateCompatibilityStrip()
        {
            if (compatStrip == null) return;
            var record = draft != null ? draft["compatibility"] as JObject : CompatibilityRecord();
            compatStrip.style.display = record != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (record == null) return;
            compatLabel.text = "其他体型的素材：" + string.Join("、", (record["mismatches"] as JArray ?? new JArray()).OfType<JObject>().Select(m => (string)m["name"]));
            compatLabel.tooltip = "目标体型：" + (string)record["base"] + "。可以在需求里指定现有适配方法，或选择自己处理。";
            int index = Array.IndexOf(FitDecisions, (string)record["decision"]);
            fitChoice.SetValueWithoutNotify(FitLabels[Math.Max(0, index)]);
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
            var requested = resources.Where(ResourceRequested).ToList();
            string targetBase = RequestBase(requested);
            string key = BindingPath + "|" + (target ? target.GetInstanceID() : 0) + "|" + targetBase + "|" + string.Join("|", requested.Select(r => r.id + ":" + r.path));
            if (key == compatKey) return liveCompatibility;
            compatKey = key;
            var mismatches = string.IsNullOrEmpty(targetBase) ? new List<BodyMismatch>()
                : requested.Where(r => WearableCategories.Contains(WorkbenchCategories.Of(r))).Select(r => Mismatch(r, targetBase)).Where(m => m != null).ToList();
            liveCompatibility = mismatches.Count == 0 ? null : new JObject {
                ["base"] = targetBase, ["decision"] = "from_request",
                ["mismatches"] = new JArray(mismatches.Select(m => new JObject { ["name"] = m.name, ["path"] = m.path, ["local_versions"] = new JArray(m.found) })) };
            return liveCompatibility;
        }
    }
}
