using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        Button outfitButton, poseButton, physicsButton, shakeButton, endTrialButton;
        List<WorkbenchMenuControl> nativeMenu = new List<WorkbenchMenuControl>(), outfitOptions = new List<WorkbenchMenuControl>();
        string controlsUnavailable = "先选择角色。";

        VisualElement BuildPreviewControls()
        {
            var row = Row(); row.AddToClassList("aw-play-tools");
            outfitButton = MakeButton("换衣服 ▾", ShowOutfits, "preview-outfit"); row.Add(outfitButton);
            poseButton = MakeButton("换姿势 ▾", ShowPoses, "preview-pose"); row.Add(poseButton);
            radialButton = MakeButton("圆盘菜单", ToggleRadialMenu, "preview-radial"); row.Add(radialButton);
            row.Add(MakeButton("回到全身", () => { if (!CameraPreviewReady()) return; StopPreviewCamera(); preview.SetFocus("全身"); canvas.MarkDirtyRepaint(); }, "preview-reset-camera"));
            physicsButton = MakeButton("动态预览", () => TryAction(TogglePhysics), "preview-physics"); row.Add(physicsButton);
            shakeButton = MakeButton("晃一晃", () => TryAction(ShakePreview), "preview-shake"); row.Add(shakeButton);
            endTrialButton = MakeButton("还原试穿", EndTrial, "preview-end-trial"); row.Add(endTrialButton);
            expandPreviewButton = MakeButton("放大预览", TogglePreviewWorkspace, "preview-expand"); row.Add(expandPreviewButton);
            return row;
        }
        void ReadPreviewControls()
        {
            controlsUnavailable = WorkbenchGesturePreview.Availability(target);
            nativeMenu = WorkbenchGesturePreview.ReadMenu(target);
            outfitOptions = WorkbenchGesturePreview.Outfits(nativeMenu);
        }
        void UpdatePreviewControls()
        {
            if (outfitButton == null) return;
            bool available = string.IsNullOrEmpty(controlsUnavailable) && target && preview != null && !frozen && !historical && !busy;
            outfitButton.SetEnabled(available && outfitOptions.Count > 0); poseButton.SetEnabled(available);
            physicsButton.SetEnabled(available); shakeButton.SetEnabled(available);
            physicsButton.text = preview?.Gesture?.PhysicsRunning == true ? "暂停动态" : "动态预览";
            endTrialButton.SetEnabled(preview?.Gesture != null && !busy && !frozen && !historical);
            outfitButton.text = preview?.Gesture != null ? "衣服：" + WorkbenchUiRules.Short(preview.Gesture.OutfitName, 16) + " ▾" : "换衣服 ▾";
            poseButton.text = preview?.Gesture != null ? "姿势：" + preview.Gesture.PoseName + " ▾" : "换姿势 ▾";
            if (preview?.Gesture?.Moving == true) { outfitButton.text = "衣服：切换中… ▾"; poseButton.text = "姿势：切换中… ▾"; }
            string reason = frozen ? "圈选原图已经冻结；先返回模型预览。" : historical ? "历史图片不能换装或改变姿势。" : busy ? "Unity 或 Codex 正在操作；保留当前画面。" : controlsUnavailable;
            outfitButton.tooltip = !string.IsNullOrEmpty(reason) ? reason : outfitOptions.Count == 0 ? "未识别到可切换的整套衣服菜单；不直接开关模型物件。" : "切换角色已有的整套穿搭菜单，只作用于临时预览。再次选择当前衣服不会脱掉衣服。";
            poseButton.tooltip = !string.IsNullOrEmpty(reason) ? reason : "现有 Gesture Manager 动画预览：站立、T 姿、伸臂；有对应 GoGoLoco 菜单时还可坐下。开启物理预览后继续求值临时角色的 PhysBone。";
            physicsButton.tooltip = !string.IsNullOrEmpty(reason) ? reason : "在 Unity 内持续运行临时角色的 SDK 原生 PhysBone。窗口隐藏或圈选时停止推进；再次点击暂停。";
            shakeButton.tooltip = !string.IsNullOrEmpty(reason) ? reason : "临时角色轻晃两秒，查看头发、衣物等真实 PhysBone 运动。正式角色与共享材质保持原状态。";
            endTrialButton.tooltip = "结束试穿与姿势预览，返回现场编辑画面；正式角色从未被修改。";
            radialButton?.SetEnabled(available);
            if (radialButton != null) { radialButton.text = radialPreview != null ? "收起圆盘" : "圆盘菜单"; radialButton.tooltip = !string.IsNullOrEmpty(reason) ? reason : "在模型旁操作角色原有的圆盘菜单，查看换装、配件和表情；仅影响临时试穿。"; }
            if (!available && radialPreview != null) CloseRadialMenu();
        }
        void EnsureTrial()
        {
            if (IsBusy() || frozen || historical) throw new InvalidOperationException("请返回空闲的模型画面后再切换。");
            if (!string.IsNullOrEmpty(controlsUnavailable)) throw new InvalidOperationException(controlsUnavailable);
            if (preview?.Gesture != null) return;
            StopPreviewCamera(); CloseRadialMenu();
            var next = new WorkbenchPreview(); refreshingPreview = true;
            try
            {
                next.LoadFunctional(target, nativeMenu);
                if (preview != null) next.KeepCameraFrom(preview);
                var context = Context(next);
                preview?.Dispose(); preview = next; previewTargetId = target.GetInstanceID(); stableContext = context;
                sourceNotice = "Unity 临时动画预览 · 可开启 PhysBone · 非最终构建";
            }
            catch { next.Dispose(); throw; }
            finally { refreshingPreview = false; }
        }
        void ShowOutfits()
        {
            var menu = new GenericDropdownMenu();
            for (int i = 0; i < outfitOptions.Count; i++)
            {
                int index = i; var option = outfitOptions[i];
                menu.AddItem(option.Name, preview?.Gesture?.OutfitName == option.Name, () => TryAction(() => PreviewOutfit(index)));
            }
            menu.DropDown(outfitButton.worldBound, outfitButton, true);
        }
        void ShowPoses()
        {
            var menu = new GenericDropdownMenu();
            foreach (var pair in new[] { new KeyValuePair<string, string>("stand", "站立（恢复）"), new KeyValuePair<string, string>("sit", "坐姿"), new KeyValuePair<string, string>("t", "T 姿"), new KeyValuePair<string, string>("ik", "伸臂（IK）") })
            {
                string value = pair.Key, label = pair.Value;
                if (value == "sit" && !nativeMenu.Any(c => c.Path.IndexOf("GoGo", StringComparison.OrdinalIgnoreCase) >= 0 && c.Type == "Button" && c.Parameter == "VRCEmote" && c.Value == 252)) menu.AddDisabledItem("坐姿（角色没有对应菜单）", false);
                else menu.AddItem(label, preview?.Gesture?.PoseName == (value == "stand" ? "站立" : label), () => TryAction(() => PreviewPose(value)));
            }
            menu.DropDown(poseButton.worldBound, poseButton, true);
        }
        public void PreviewOutfit(int index)
        {
            if (index < 0 || index >= outfitOptions.Count) throw new ArgumentOutOfRangeException(nameof(index));
            EnsureTrial(); preview.Gesture.SelectOutfit(outfitOptions[index]);
            UpdateLabels(); canvas?.MarkDirtyRepaint(); Toast("试穿：" + outfitOptions[index].Name + "。正式角色保持原状态。");
        }
        public void PreviewPose(string pose)
        {
            if (!new[] { "stand", "sit", "t", "ik" }.Contains(pose)) throw new ArgumentException("未知姿势。");
            EnsureTrial(); preview.Gesture.SelectPose(pose);
            UpdateLabels(); canvas?.MarkDirtyRepaint(); Toast("姿势预览：" + preview.Gesture.PoseName + "。正式角色保持原状态。");
        }
        public void EndTrial()
        {
            if (IsBusy() || frozen || historical) return;
            LoadPreview(); Toast("已结束试穿和姿势预览，返回当前角色的编辑画面。");
        }
        public void TogglePhysics()
        {
            EnsureTrial(); preview.Gesture.SetPhysics(!preview.Gesture.PhysicsRunning);
            sourceNotice = preview.Gesture.PhysicsRunning ? "Unity PhysBone 已开启 · 临时角色 · 非最终构建" : "Unity PhysBone 已暂停 · 临时角色 · 非最终构建";
            preview.SampleGesturePose(); UpdateLabels(); canvas?.MarkDirtyRepaint();
            Toast(preview.Gesture.PhysicsRunning ? "Unity PhysBone 预览已开启；可换姿势或点击“晃动一下”。" : "物理预览已暂停，保留当前画面。");
        }
        public void ShakePreview()
        {
            EnsureTrial(); preview.Gesture.Shake(); UpdateLabels(); canvas?.MarkDirtyRepaint();
            Toast("临时角色正在轻晃；原生 PhysBone 持续求值。正式角色未修改。");
        }
        void TickPreviewControls(double now)
        {
            TickPreviewCamera(now);
            if (preview?.Gesture == null) return;
            bool visible = windowVisible && !busy && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode && !historical && !frozen && previewPanel != null && previewPanel.resolvedStyle.display != DisplayStyle.None;
            try
            {
                bool wasMoving = preview.Gesture.Moving;
                if (preview.Gesture.Tick(visible) && (radialPreview != null || preview.Gesture.PhysicsRunning || !preview.Gesture.Moving))
                {
                    preview.SampleGesturePose();
                    string nextNotice = preview.Gesture.PhysicsRunning ? "Unity 原生 PhysBone 运行中 · 临时角色 · 非最终构建" : preview.Gesture.PhysicsSimulated ? "Unity PhysBone 已暂停 · 临时角色 · 非最终构建" : "Unity 临时动画预览 · 可开启 PhysBone · 非最终构建";
                    bool labelsChanged = sourceNotice != nextNotice || wasMoving && !preview.Gesture.Moving;
                    sourceNotice = nextNotice;
                    if (labelsChanged) UpdateLabels();
                    canvas?.MarkDirtyRepaint();
                    radialCanvas?.MarkDirtyRepaint();
                }
            }
            catch (Exception e)
            {
                preview.StopGesture(); controlsUnavailable = "原生动画预览已停止：" + (e.InnerException ?? e).Message;
                Toast(controlsUnavailable); UpdateLabels();
            }
        }
        void PreviewPlayModeChanged(PlayModeStateChange state)
        {
            // Disconnect native hooks and SDK manager references before entering Play Mode.
            // The last rendered texture stays visible while Unity is busy.
            if (state == PlayModeStateChange.ExitingEditMode && preview?.Gesture != null) preview.StopGesture();
            if (state == PlayModeStateChange.EnteredEditMode) QueueSceneRefresh();
        }
    }
}
