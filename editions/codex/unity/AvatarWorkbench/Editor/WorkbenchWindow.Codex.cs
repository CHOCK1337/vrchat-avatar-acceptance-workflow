using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        JObject codexLink;
        CancellationTokenSource codexLifetime;
        bool connectingCodex, sendingCodex;
        string codexError = "";
        string activeDeliveryFile;
        JObject activeDeliveryReceipt;

        public async void ReconnectCodex()
        {
            if (sendingCodex || connectingCodex || disposed) return;
            var stored = WorkbenchCodex.Link();
            if (stored == null)
            {
                string pipe = Environment.GetEnvironmentVariable("CODEX_APP_TOOLS_PIPE_PATH"), thread = Environment.GetEnvironmentVariable("CODEX_THREAD_ID");
                if (WorkbenchCodex.ValidPipe(pipe) && Guid.TryParse(thread, out _)) stored = WorkbenchCodex.Bind(pipe, thread);
            }
            if (stored == null)
            {
                codexLink = null;
                Toast("首次接入：在你要继续的 Codex 会话里说“把改模工作台连接到这个会话”。绑定后可在这里直接发送。"); UpdateLabels(); return;
            }
            codexLifetime?.Cancel(); codexLifetime?.Dispose(); codexLifetime = new CancellationTokenSource();
            var lifetime = codexLifetime; connectingCodex = true; codexLink = null; codexError = ""; UpdateLabels();
            try
            {
                var verified = await WorkbenchCodex.ValidateAsync(stored, lifetime.Token);
                if (disposed || lifetime.IsCancellationRequested) return;
                WorkbenchCodex.SaveLink(verified); codexLink = verified; Toast("已连接“" + WorkbenchData.Text(verified["title"]) + "”，写好需求后可直接发送。");
            }
            catch (Exception e) { if (!disposed && !lifetime.IsCancellationRequested) { codexError = e.Message; Toast(e.Message); } }
            finally { if (!disposed && codexLifetime == lifetime) { connectingCodex = false; UpdateLabels(); } }
        }
        void StopCodexConnection()
        {
            StopApiRequest();
            // A reload can discard async continuations. Persist uncertainty before releasing the pipe.
            if (activeDeliveryReceipt != null && WorkbenchData.Text(activeDeliveryReceipt["state"]) == "sending")
            {
                activeDeliveryReceipt["state"] = "unknown";
                activeDeliveryReceipt["error"] = "窗口关闭或脚本重载中断了发送回执，先查看 Codex 是否收到。";
                activeDeliveryReceipt["updated_at"] = WorkbenchData.Now;
                try { WorkbenchData.Write(activeDeliveryFile, activeDeliveryReceipt); } catch (IOException) { }
            }
            codexLifetime?.Cancel(); codexLifetime?.Dispose(); codexLifetime = null;
            codexLink = null; connectingCodex = false; sendingCodex = false;
        }
        async void SendSavedRequest(string binding, JObject entry)
        {
            if (sendingCodex || disposed) return;
            string routeType = (string)entry["model_route"]?["type"] ?? "";
            if (routeType == "api") { SendSavedApiRequest(binding, entry); return; }
            if (routeType == WorkbenchModels.DshHost) { SaveDshRequest(binding, entry); return; }
            // Only an explicit Codex route (or a pre-route legacy record) may reach the Codex pipe.
            if (routeType.Length > 0 && routeType != WorkbenchModels.CodexHost)
            {
                Toast("这条请求的执行宿主是“" + WorkbenchModels.RouteName(entry["model_route"] as JObject) + "”，当前工作台不能发送它；请求已保留，不会改发给 Codex。");
                return;
            }
            var configured = codexLink ?? WorkbenchCodex.Link();
            if (configured == null) { Toast("已保存，尚未绑定 Codex 会话。点“连接 Codex”完成一次接入后即可直接发送。"); return; }
            codexLifetime ??= new CancellationTokenSource();
            var lifetime = codexLifetime;
            string deliveryFile = WorkbenchCodex.DeliveryFile(binding, WorkbenchData.Text(entry["id"]));
            var receipt = new JObject { ["format"] = "avatar-workbench-delivery-v1", ["feedback_id"] = entry["id"]?.DeepClone(),
                ["thread_id"] = configured["thread_id"]?.DeepClone(), ["state"] = "sending", ["created_at"] = WorkbenchData.Now };
            bool dispatchStarted = false; sendingCodex = true; codexError = "";
            activeDeliveryFile = deliveryFile; activeDeliveryReceipt = receipt;
            try
            {
                WorkbenchData.Write(deliveryFile, receipt); UpdateLabels(); RenderFeedback(); Toast("请求已保存，正在发送给 Codex…");
                // Recheck the exact existing session before each dispatch; never start or resume a second agent.
                var verified = await WorkbenchCodex.ValidateAsync(configured, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                WorkbenchCodex.SaveLink(verified); codexLink = verified;
                dispatchStarted = true;
                var accepted = await WorkbenchCodex.SendAsync(verified, WorkbenchCodex.Prompt(binding, entry), lifetime.Token);
                receipt["state"] = "accepted"; receipt["accepted_at"] = WorkbenchData.Now; receipt["desktop_receipt"] = accepted;
                WorkbenchData.Write(deliveryFile, receipt);
                if (!disposed) ToastReceipt("已发送给 Codex，等待它实际读取；不用再复制粘贴。", WorkbenchData.Text(entry["id"]));
            }
            catch (Exception e)
            {
                receipt["state"] = dispatchStarted && !(e is WorkbenchCodex.CodexRejectedException) ? "unknown" : "failed";
                receipt["error"] = e.Message; receipt["updated_at"] = WorkbenchData.Now;
                try { WorkbenchData.Write(deliveryFile, receipt); } catch (IOException) { }
                if (!disposed)
                {
                    codexLink = null; codexError = e.Message;
                    Toast(WorkbenchData.Text(receipt["state"]) == "unknown" ? "请求已保留，但发送回执未知。先查看 Codex；确认未收到后可重新发送。" : "请求已保留，发送未完成。打开 Codex 后可重新发送。");
                }
            }
            finally
            {
                if (activeDeliveryReceipt == receipt) { activeDeliveryFile = null; activeDeliveryReceipt = null; }
                if (!disposed && codexLifetime == lifetime) { sendingCodex = false; UpdateLabels(); RenderFeedback(); }
            }
        }
        // This integration does not implement a DSH push channel: the workbench only writes the request into the same task inbox
        // and leaves a waiting_dsh receipt. A notification log is not an automatic wake-up; nothing
        // is started, resumed or woken here.
        void SaveDshRequest(string binding, JObject entry)
        {
            string id = WorkbenchData.Text(entry["id"]);
            string deliveryFile = WorkbenchCodex.DeliveryFile(binding, id);
            var receipt = new JObject { ["format"] = "avatar-workbench-delivery-v1", ["feedback_id"] = entry["id"]?.DeepClone(),
                ["route"] = entry["model_route"]?.DeepClone(), ["state"] = "waiting_dsh", ["created_at"] = WorkbenchData.Now,
                ["pushed"] = false, ["automatic_wakeup"] = false,
                ["note"] = "DSH 没有推送接口：请求已保存到同一任务收件箱，由 DSH 读取后把回复写回同一条反馈。本机读取，不自动唤醒，不起第二个客户端；通知日志不等于自动唤醒。" };
            try { WorkbenchData.Write(deliveryFile, receipt); }
            catch (Exception e) { Toast("请求已保存，但 DSH 回执未写入：" + e.Message + " 可稍后重试写入回执。"); UpdateLabels(); RenderFeedback(); return; }
            UpdateLabels(); RenderFeedback();
            ToastReceipt("已保存给 DSH：请求在收件箱等待 DSH 读取，不会自动唤醒；DSH 回复会写回同一条。", id);
        }
        void RetryRequest(string binding, JObject entry)
        {
            var delivery = WorkbenchCodex.Delivery(binding, WorkbenchData.Text(entry["id"]));
            string routeType = (string)entry["model_route"]?["type"] ?? "";
            bool api = routeType == "api", dsh = routeType == WorkbenchModels.DshHost;
            string deliveryState = WorkbenchData.Text(delivery?["state"]), status = WorkbenchData.Text(entry["status"]);
            // Re-saving is only for a missing/failed receipt. Never push a DSH request back to unread
            // and never rewrite the route of an already delivered or already read request.
            if (dsh && (deliveryState == "waiting_dsh" || status != "received")) return;
            if (deliveryState == "accepted" || (status != "received" && !(api && status == "seen"))) return;
            if (deliveryState == "unknown" && !UnityEditor.EditorUtility.DisplayDialog("发送回执未知", api ? "API 可能已经处理这条请求，但回执未保存。再次请求可能再次计费，请确认后重试。" : dsh ? "DSH 可能已经读取这条请求，但回执未保存。请先确认收件箱状态，再决定是否重新保存。" : "Codex 可能已经收到这条请求。请先查看会话，确认未收到后再重试，以免重复执行。", "确认后重试", "先保留原请求")) return;
            SendSavedRequest(binding, entry);
        }
    }
}
