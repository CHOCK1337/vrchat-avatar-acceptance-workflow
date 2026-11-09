using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        HashSet<string> requestedResourceIds;
        internal bool PreviewIsBusy => IsBusy();
        bool ResourceRequested(ResourceCard resource) => requestedResourceIds != null ? requestedResourceIds.Contains(resource.id) : resource.id == selectedId || localResources.Any(x => x.id == resource.id);
        void OpenResourceGallery() => WorkbenchResourceGallery.Open(resources, resources.Where(ResourceRequested).Select(x => x.id));
        public void SelectResourcesForRequest(string[] ids)
        {
            requestedResourceIds = new HashSet<string>((ids ?? Array.Empty<string>()).Where(x => resources.Any(r => r.id == x)));
            SaveState(); UpdateComposer(); Toast(draft == null ? "已选择本次需求的 " + requestedResourceIds.Count + " 项素材；没有立即安装。" : "已更新后续需求；当前圈选仍保留冻结时的素材。");
        }
        void OpenScreenshotGallery()
        {
            var records = images.Select(x => { var copy = (JObject)x.DeepClone(); copy["path"] = WorkbenchData.Resolve((string)copy["path"], System.IO.Path.GetDirectoryName(manifestPath)); return copy; }).ToList();
            foreach (var feedback in WorkbenchData.Feedback(BindingPath))
            {
                if (!(feedback["image"] is JObject source)) continue;
                var item = (JObject)source.DeepClone();
                item["candidate"] = feedback["candidate"]?.DeepClone(); item["target"] = feedback["target"]?.DeepClone(); item["known_controls"] = feedback["known_controls"]?.DeepClone();
                item["title"] = feedback["message"]?.DeepClone(); item["created_at"] = feedback["created_at"]?.DeepClone(); item["feedback_status"] = feedback["status"]?.DeepClone();
                item["task_id"] = feedback["task_id"]?.DeepClone(); item["task_path"] = feedback["task_path"]?.DeepClone(); item["snapshot_id"] = feedback["snapshot_id"]?.DeepClone();
                item["binding"] = BindingPath;
                if (!records.Any(x => (string)x["path"] == (string)item["path"] && (string)x["sha256"] == (string)item["sha256"])) records.Add(item);
            }
            if (draft?["image"] is JObject frozenImage)
            {
                var item = (JObject)frozenImage.DeepClone(); item["candidate"] = draft["candidate"]?.DeepClone(); item["target"] = draft["target"]?.DeepClone(); item["title"] = "尚未提交的圈选原图"; item["binding"] = draft["binding"]?.DeepClone(); records.Insert(0, item);
            }
            WorkbenchScreenshotGallery.Open(records);
        }
        internal void ShowRecordedImage(JObject image)
        {
            var item = (JObject)image.DeepClone();
            SelectPanel("preview");
            images.Add(item); ShowHistory(images.Count - 1); // The existing reader validates the original image hash.
        }
    }
}
