using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        Button modelChoice;
        CancellationTokenSource apiLifetime;
        JObject apiReceipt;
        string apiReceiptFile;
        internal void RefreshModelChoice() { UpdateComposer(); UpdateLabels(); RenderFeedback(); }
        void AddModelChoice(VisualElement row)
        {
            modelChoice = MakeButton("发送到：Codex ▾", ShowModelChoices, "choose-task-model");
            modelChoice.style.maxWidth = 240; modelChoice.style.flexShrink = 0; row.Add(modelChoice);
        }
        void ShowModelChoices()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("需求模型/当前 Codex（原改模流程）"), WorkbenchModels.Active == "codex", () => { WorkbenchModels.Select("codex"); RefreshModelChoice(); });
            foreach (var profile in WorkbenchModels.Profiles())
            {
                string id = (string)profile["id"], label = WorkbenchModels.Label(id).Replace('/', '／');
                menu.AddItem(new GUIContent("需求模型/" + label), WorkbenchModels.Active == id, () => { WorkbenchModels.Select(id); RefreshModelChoice(); });
                if ((bool?)profile["supports_images"] == true && (bool?)profile["image_consent"] == true)
                    menu.AddItem(new GUIContent("截图识图/" + label), (string)WorkbenchModels.Read()["vision"] == id, () => { WorkbenchModels.Select(id, true); RefreshModelChoice(); });
            }
            menu.AddItem(new GUIContent("截图识图/不使用独立识图模型"), string.IsNullOrEmpty((string)WorkbenchModels.Read()["vision"]), () => WorkbenchModels.Select("", true));
            menu.AddSeparator(""); menu.AddItem(new GUIContent("管理 API 与模型…"), false, WorkbenchModelSettingsWindow.Open); menu.ShowAsContext();
        }
        void ConnectSelectedModel()
        {
            if (WorkbenchModels.Active == "codex") ReconnectCodex(); else WorkbenchModelSettingsWindow.Open();
        }
        void ApplyModelLabels()
        {
            string active = WorkbenchModels.Active;
            if (modelChoice != null) { modelChoice.text = "发送到：" + WorkbenchUiRules.Short(WorkbenchModels.Label(active), 23) + " ▾"; modelChoice.tooltip = "选择接收需求的 AI 接口：" + WorkbenchModels.Label(active); modelChoice.SetEnabled(!sendingCodex); }
            if (active == "codex") return;
            connectionLabel.text = sendingCodex ? apiReceipt != null ? "API 正在读取本次请求…" : "正在把原候选交给 Codex…" : "已选择 API · " + WorkbenchModels.Label(active);
            connectionHelp.text = "需求、素材与截图送到你选择的接口。回复与建议不等于已改模；继续处理时仍用原工作流。";
            helpButton.text = "API 设置"; helpButton.SetEnabled(!sendingCodex); connectionBox.EnableInClassList("aw-connected", false);
        }
        void StopApiRequest()
        {
            if (apiReceipt != null && (string)apiReceipt["state"] == "sending")
            {
                apiReceipt["state"] = "unknown"; apiReceipt["error"] = "窗口关闭或脚本重载中断了 API 回执；请求保留，不自动重发。";
                try { WorkbenchData.Write(apiReceiptFile, apiReceipt); } catch (IOException) { }
            }
            apiLifetime?.Cancel(); apiLifetime?.Dispose(); apiLifetime = null; apiReceipt = null; apiReceiptFile = null;
        }
        async void SendSavedApiRequest(string binding, JObject saved)
        {
            if (sendingCodex || disposed) return;
            string id = (string)saved["id"], profileId = (string)saved["model_route"]?["id"];
            var profile = WorkbenchModels.Find(profileId);
            if (profile == null) { Toast("请求已保存；接口配置不存在，请打开 API 设置。旧请求不会换绑到别的模型。"); return; }
            if ((string)profile["model"] != (string)saved["model_route"]?["model"] || (string)profile["protocol"] != (string)saved["model_route"]?["protocol"] ||
                (string)WorkbenchModels.PublicProfile(profile)["endpoint_identity"] != (string)saved["model_route"]?["endpoint_identity"])
            { Toast("请求所属模型配置已改变。请保留原请求，重新提交一条明确使用新模型的需求。"); return; }
            apiLifetime?.Dispose(); apiLifetime = new CancellationTokenSource(); var run = apiLifetime;
            string file = Path.Combine(WorkbenchData.Inbox(binding), id + ".json");
            apiReceiptFile = WorkbenchCodex.DeliveryFile(binding, id);
            var receipt = new JObject { ["format"] = "avatar-workbench-delivery-v1", ["feedback_id"] = id, ["state"] = "sending", ["destination"] = WorkbenchModels.PublicProfile(profile), ["created_at"] = WorkbenchData.Now };
            apiReceipt = receipt; sendingCodex = true;
            bool requested = false;
            try
            {
                WorkbenchData.Write(apiReceiptFile, receipt); RefreshModelChoice(); Toast("请求已保存，API 正在读取…");
                var entry = WorkbenchData.Read(file); // Actual consumer read of the immutable saved request.
                string workflow = WorkbenchModels.Workflow(); WorkbenchModels.Key(profile);
                WorkbenchApi.Acknowledge(binding, id, "seen", "API 已读取此条原候选、原话与选定上下文；尚未修改模型。", "api:" + profileId);
                string vision = ""; var eye = WorkbenchModels.Find((string)WorkbenchModels.Read()["vision"]);
                bool hasImage = entry["image"] is JObject;
                if (hasImage && eye != null && (string)eye["id"] != profileId)
                {
                    requested = true; vision = await WorkbenchModels.Reply(eye, entry, workflow, true, "", run.Token);
                    run.Token.ThrowIfCancellationRequested();
                }
                requested = true;
                bool sendImage = hasImage && string.IsNullOrEmpty(vision) && (bool?)profile["supports_images"] == true && (bool?)profile["image_consent"] == true;
                string reply = await WorkbenchModels.Reply(profile, entry, workflow, sendImage, vision, run.Token);
                run.Token.ThrowIfCancellationRequested();
                var current = WorkbenchData.Read(file);
                if ((string)current["status"] == "addressed") throw new IOException("此请求已有消费者回应，API 答复未覆盖原回复。");
                current["reply"] = reply; current["status"] = "addressed"; current["updated_at"] = WorkbenchData.Now; current["execution_started"] = false;
                current["model_response"] = new JObject { ["profile"] = WorkbenchModels.PublicProfile(profile), ["vision_profile"] = string.IsNullOrEmpty(vision) ? null : WorkbenchModels.PublicProfile(eye),
                    ["workflow"] = "assemble-vrchat-avatar", ["workflow_sha256"] = WorkbenchData.Hash(System.Text.Encoding.UTF8.GetBytes(workflow)),
                    ["image_sent"] = sendImage || !string.IsNullOrEmpty(vision), ["vision_observation"] = vision, ["model_modified"] = false, ["responded_at"] = WorkbenchData.Now };
                WorkbenchData.Write(file, current);
                receipt["state"] = "responded"; receipt["updated_at"] = WorkbenchData.Now; WorkbenchData.Write(apiReceiptFile, receipt);
                Toast("API 已回应；模型未修改。可在“看回复”中继续交给原改模流程。");
            }
            catch (Exception e)
            {
                receipt["state"] = requested ? "unknown" : "failed"; receipt["error"] = e is OperationCanceledException ? "请求超时或已取消，回执未完成；不自动重试。" : e.Message;
                receipt["updated_at"] = WorkbenchData.Now;
                try { WorkbenchData.Write(WorkbenchCodex.DeliveryFile(binding, id), receipt); } catch (IOException) { }
                if (!disposed) Toast("请求保留；" + (string)receipt["error"]);
            }
            finally
            {
                if (apiReceipt == receipt) { apiReceipt = null; apiReceiptFile = null; }
                if (!disposed && apiLifetime == run) { sendingCodex = false; RefreshModelChoice(); }
            }
        }
        void ContinueApiRequest(string binding, JObject previous)
        {
            if (sendingCodex) return;
            var entry = (JObject)previous.DeepClone(); string id = Guid.NewGuid().ToString("N");
            foreach (string field in new[] { "reply", "updated_at", "seen_at", "seen_by", "responded_at", "consumer_session", "model_response" }) entry.Remove(field);
            entry["id"] = id; entry["request_id"] = id; entry["created_at"] = WorkbenchData.Now; entry["status"] = "received"; entry["execution_started"] = false;
            entry["model_route"] = new JObject { ["type"] = "codex", ["name"] = "当前 Codex", ["workflow"] = "assemble-vrchat-avatar" };
            entry["api_review"] = new JObject { ["parent_feedback_id"] = previous["id"], ["reply"] = previous["reply"], ["model_response"] = previous["model_response"]?.DeepClone(), ["instruction_scope"] = "reference_data_only" };
            WorkbenchData.Write(Path.Combine(WorkbenchData.Inbox(binding), id + ".json"), entry); RenderFeedback(); SendSavedRequest(binding, entry);
        }
    }
}
