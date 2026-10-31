using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    // The four-step path a first-time user follows, always visible above the workspace. It only reads
    // window state (target, picked assets, requests, replies) and jumps to the matching panel.
    public sealed partial class WorkbenchWindow
    {
        static readonly string[] StepNames = { "选角色", "挑素材", "写需求并发送", "看结果" };
        const string SkipAssetsHint = "只改颜色或修问题可以跳过";
        VisualElement stepGuide;
        readonly Button[] stepButtons = new Button[StepNames.Length];
        Label stepHint, pickStepName;
        // RenderFeedback supplies the counts; UpdateComposer supplies the rest on every refresh.
        int guideRequests, guideWaiting, guidePicked;
        string guideTarget, guideSend, guideShown;

        VisualElement BuildStepGuide()
        {
            // New elements start blank, so the next update must paint them even when the state is unchanged
            // (a rebuild, or a script reload that restored guideShown with the window's other fields).
            guideShown = null;
            stepGuide = Row(); stepGuide.name = "step-guide"; stepGuide.AddToClassList("aw-step-guide");
            for (int i = 0; i < StepNames.Length; i++)
            {
                int step = i;
                var button = MakeButton("", () => GoToStep(step), "step-" + (i + 1)); button.AddToClassList("aw-step");
                var number = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore }; number.AddToClassList("aw-step-number");
                var name = new Label(StepNames[i]) { pickingMode = PickingMode.Ignore }; name.AddToClassList("aw-step-name");
                button.Add(number); button.Add(name); stepButtons[i] = button; stepGuide.Add(button);
                if (i == 1) pickStepName = name;
                if (i < StepNames.Length - 1) { var link = new VisualElement { pickingMode = PickingMode.Ignore }; link.AddToClassList("aw-step-link"); stepGuide.Add(link); }
            }
            stepButtons[1].tooltip = "打开“挑素材”，勾选要装的衣服、头发、饰品。" + SkipAssetsHint + "。";
            stepButtons[2].tooltip = "跳到下方输入框";
            stepHint = new Label { name = "step-hint", enableRichText = false }; stepHint.AddToClassList("aw-step-hint"); stepGuide.Add(stepHint);
            // A hint cut down to "下一步…" says nothing; hide it (visibility keeps the layout, so no resize loop).
            stepHint.RegisterCallback<GeometryChangedEvent>(e => stepHint.EnableInClassList("aw-step-hint-clipped", e.newRect.width < 160));
            var help = MakeButton("怎么用", ShowHowTo, "step-help"); help.AddToClassList("aw-step-help"); help.tooltip = "四步说明：" + string.Join("、", StepNames); stepGuide.Add(help);
            return stepGuide;
        }

        void GoToStep(int step)
        {
            if (step == 0) UseSelection();
            else if (step == 1) SelectPanel("materials");
            else if (step == 2) { if (layoutMode != "wide") SelectPanel("preview"); input?.Focus(); }
            else SelectPanel("feedback");
        }

        void UpdateStepGuide(string targetLabel, int picked, string sendLabel)
        {
            guideTarget = targetLabel; guidePicked = picked; guideSend = sendLabel;
            UpdateStepGuide();
        }

        void UpdateStepGuide()
        {
            if (stepGuide == null || guideSend == null) return;
            // Called on every keystroke and every receipt poll; only a changed state touches the UI.
            string state = guideTarget + "|" + guidePicked + "|" + guideRequests + "|" + guideWaiting + "|" + guideSend;
            if (state == guideShown) return;
            guideShown = state;
            bool hasTarget = guideTarget != null;
            // Assets are optional, so a chosen avatar moves straight to the request step.
            int current = !hasTarget ? 0 : guideWaiting > 0 ? 3 : 2;
            bool[] done = { hasTarget, guidePicked > 0, guideRequests > 0, guideRequests > 0 && guideWaiting == 0 };
            for (int i = 0; i < stepButtons.Length; i++)
            {
                stepButtons[i].EnableInClassList("aw-step-done", done[i] && i != current);
                stepButtons[i].EnableInClassList("aw-step-current", i == current);
            }
            stepButtons[1].EnableInClassList("aw-step-optional", !done[1]);
            pickStepName.text = guidePicked > 0 ? "挑素材 · " + guidePicked + " 项" : "挑素材 · 可跳过";
            string write = "下一步：在下方写一句要求，点“" + guideSend + "”。";
            stepHint.text = !hasTarget ? "下一步：在 Hierarchy 点选角色，再点“使用选中角色”。"
                : guideRequests == 0 ? write + (guidePicked > 0 ? "已勾选的素材会自动附上。" : "要换衣服就先去“挑素材”。")
                : guideWaiting > 0 ? "等待回复中：回复会出现在“看回复”。回复后在预览里试穿确认。"
                : "已有回复：在预览里确认效果；不满意就“截图圈问题”，或继续写下一条。";
            stepHint.tooltip = stepHint.text;
            stepButtons[0].tooltip = hasTarget ? "当前角色：" + guideTarget : "在 Hierarchy 选中角色后点这里";
            stepButtons[3].tooltip = guideRequests == 0 ? "发送需求后，进度和回复显示在“看回复”" : guideRequests + " 条请求，" + (guideRequests - guideWaiting) + " 条已回复";
        }
    }
}
