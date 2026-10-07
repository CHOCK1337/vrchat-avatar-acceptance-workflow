using System;

namespace AvatarWorkbench
{
    // Pure presentation rules; no scene, task, account or execution mutations.
    internal static class WorkbenchUiRules
    {
        public const float WideWidth = 1180f;
        public const float WideHeight = 620f;
        public const float MediumWidth = 900f;
        public const float MediumHeight = 560f;

        public static string Layout(float width, float height)
        {
            if (width >= WideWidth && height >= WideHeight) return "wide";
            if (width >= MediumWidth && height >= MediumHeight) return "medium";
            return "compact";
        }
        public static string ReceiptLabel(string status)
        {
            switch (status)
            {
                case "received": return "等待读取";
                case "seen": return "已读";
                case "processing_feedback": return "处理中";
                case "addressed": return "已有回复";
                default: return "状态未记录";
            }
        }
        public static string PhaseLabel(string phase)
        {
            switch ((phase ?? "").ToUpperInvariant())
            {
                case "INTAKE": case "PRECHECK": case "PREFLIGHT": return "正在整理本次需求";
                case "INSTALLING": case "ASSEMBLING": case "PROCESSING": return "正在修改模型";
                case "REVIEW": case "REVIEWING": case "VALIDATING": return "正在查看修改结果";
                case "BLOCKED": case "WAITING_USER": return "有一项问题需要处理";
                case "LOCAL_ACCEPTED_WITH_WARNINGS": return "任务记录：已完成本地步骤，仍有提醒";
                case "LOCAL_ACCEPTED": case "LOCAL_OK": case "COMPLETED": return "任务记录：本地步骤已结束";
                case "UPLOADED": return "任务记录：已上传";
                case "FAILED": return "本轮任务遇到问题";
                default: return "等待任务更新";
            }
        }
        public static string Short(string text, int max = 90)
        {
            text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }
    }
}
