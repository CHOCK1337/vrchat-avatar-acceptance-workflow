# DSH 工作台预览入口

当前工作台提供 `WorkbenchWindow.RunPreviewCommand(string json)`，只包装现有换装、姿势和原图捕获，不保存 Avatar、改参数或替换拟合方法。适用于已绑定的持久化处理后候选。编辑态源仍走原工具，不强行冒充候选。

绑定已有任务用工作台的 public `Open()`、实例 `BindTask(string)` 与 `SetTarget(GameObject)`；之后以 inspect 的实际 binding/source_kind/asset_path 为准。MCP context_select 只选择 MCP 会话上下文，不会替 Unity 窗口绑定任务。任务文件更新会重载预览，建议在一组操作结束后更新，再正常开启下一组预览。

## 先分清两种状态

- **持久化修改**：本会话 `consumer_lease busy`，暂停预览，防止现场同时写入。
- **临时预览/菜单验收**：本会话 `consumer_lease idle`，保留 `active=true` 的排他所有权，再调用预览。整个等待、截图阶段保持 idle。busy 会禁止换装且停止动画 Tick。
- 结束或失败：本会话 `disconnect`。不释放别人租约，不自动丢弃用户冻结反馈。

通过 `unityMCP.execute_editor_command` 执行下面完整模板。必须是顶层 `EditorCommand` 和 `Execute()`；动态程序集不直接引用自定义 Editor 类型。

```csharp
using System;
using System.Linq;
using System.Reflection;
public class EditorCommand {
    public static object Execute() {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("AvatarWorkbench.WorkbenchWindow"))
            .First(t => t != null);
        return type.GetMethod("RunPreviewCommand", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { @"{""action"":""inspect"",""project"":""D:/实际工程""}" });
    }
}
```

只替换 JSON 内容。每次检查嵌套 `executionSuccess`，再检查返回 JSON 的 `ok`。外层 status=success 不代表内部成功。错误会返回具体原因与状态，不必反射私有字段猜名字。

1. **inspect**：传精确 project，读实际 asset_path、dependency_hash、options、consumer、source_kind 和几何来源。确认候选就是本轮对象；不能自动接受意外变更。
   需要查具体衣片时附加 `root_path:"相对于该Avatar的真实路径"`，返回 source_part 与当前预览自身的 native/rendered 部件、网格来源和骨骼共享数。找不到返回 found=false，不能改用场景中第一个同名对象顶替；预览场景可能不在普通 SceneManager 列表内。骨骼共享数只作结构证据，不证明拟合或动态通过。
2. **select**：传 project、session、expected_asset_path、expected_dependency_hash，以及 `pose:"stand"` **或** `outfit:"从 options 读取的精确名称"`。每次一个控件。通过原生 `PreviewPose`/`PreviewOutfit`，不直接置参。
3. 回到工具层，让正常 Editor 更新，再 **capture**：传相同身份与 session，`focus:"全身"` 或 `"鞋子"`（也支持头部、上身）。返回真实 PNG 路径、SHA256、同帧 controls/view、native/rendered 可见网格。原图与收据自动保存在当前 binding 的本地空间，不覆盖旧图。
4. `read_image` 打开返回 PNG。全身图用于腿和脚是否完整；鞋子特写只用于鞋，不证明整条腿。`visual_acceptance` 保持 NOT_EVALUATED，必须自己看图并记观察。

恢复站立可能重置作者菜单，所以顺序为 **选 stand → 等更新 → 选衣服 → 等更新 → capture**。A→B→A 每步截图读实际可见衣片，至少打开目标 B 与返回 A 的图。先做一件，不批量排队修改。

## 已实测的旧版依赖组合

工作台 0.2.12-dsh 已在 Unity 2022.3.22f1、Gesture Manager 3.9.8、VRChat Base/Avatars SDK 3.10.1 上完成原生站立、换装往返、PhysBone 开启/换装注销/恢复、圆盘 Toggle 和 Siroari 作者坐姿进入/退出操作。旧版 GM 使用单 descriptor 构造；SDK 3.10.1 使用 Start 初始化 PhysBone，没有较新版的全局碰撞体形状待更新队列。适配层保留临时场景归属检查，并拒绝未经确认的未知接口组合；不需要修改 SDK 或 GM 源码。

0.2.12 给工作台临时 Animator 的行为副本补齐缺少的原生 GM 回调，并为 GM 3.9.8 的混合计时提供实际手动图时间，解决 Edit Mode 的 Time.time 不推进和零时长混合 NaN。State 中 native_behaviour_hooks / native_layer_clock 可辅助诊断；仍须验证实际动作与退出，不能用这些字段替代视觉验收。持续动作测试要在重绑后重新确认 physics_running=true；普通菜单只短时唤醒图，作者较长过渡可能尚未结束。capture 会关闭圆盘，再次输入先重新打开并读当前页。

当前工作台物理按钮的现有 public 入口是 `TogglePhysics()`。使用前先确保自己的 active idle 租约、准确候选和已启动的临时功能预览。启用后等待正常 Editor 更新，读取真实 physics_simulated/physics_running/physbone_chains 并查看原图。物理关闭的绑定静止态不能代替裙摆或发梢的自然态验收。不同版本仍以当前现场核查为准，不沿用旧版本号。

### 原生 RadialPuppet 的 GUI 上下文

2026-10-18 已实测 LightController 的 Light limit 原生旋钮。普通 UnityMCP EditorUpdate 命令里直接调用 OpenPuppet 会因 `Event.current` 为空在 `RadialMenu.MousePosition` 报错；这不说明插件损坏。正确测试是在现有 `WorkbenchWindow.radialCanvas`（`IMGUIContainer`）的真实 `onGUIHandler` 中，用一次性包装在 Repaint 内执行原生切片、`RadialCursor.Update`、`puppet.Update` 和 `ClosePuppet`；输入时使用作用域内的 MouseDrag 事件。保留原回调，finally恢复回调/Event.current，并设置未获得GUI回调的超时清理，不留常驻测试处理器。继续校验自己的active idle租约和准确候选hash，并用CaptureNativeMenuParameters/CompleteNativeMenuInput包围输入。

已验证Light limit .25使画面变暗；插件关闭恢复原亮度；关闭态从旧菜单调至.75仍保持342条实际PropertyBlock基线。该记录不代表所有旋钮类型已穷举，也不允许直接SetSubValue/SetParam冒充原生输入。可参考本次工作目录 `local/assembly-20261018/parent-light-gui-once.cs` 与 `PARENT_LIGHT_RESULT.md`，复用前替换为当前真实候选身份和自己的会话。

## 具体错误怎么处理

- PREVIEW_LEASE_BUSY：本会话改 idle 再操作，不能写内部 busy=false 绕过租约。
- PREVIEW_SETTLING：正常 Editor 更新后再 capture；不要同步循环 Tick 或 Thread.Sleep 锁 Unity 主线程。
- CANDIDATE_CHANGED：核对本轮预期与实际来源后更新身份；不要默默接受错误目标。
- FROZEN_OR_HISTORY：保留反馈，回到模型状态后继续；不自动清空用户草稿。
- source_geometry 有零网格衣柜项，或菜单切换后对应 visible=0：属于待调查项，不宣布已穿衣服。
- 连续两次相同阶段工具错误：停下读具体错误和已验证入口；不要发散扫描私有 API。`Transform.GetHierarchyPath` 不是这里的 Unity 标准 API，路径使用 `AnimationUtility.CalculateTransformPath(transform, root)`。
- 不用 `clear_logs` 修复异常，保留证据。当前宿主若仍报已知 pwsh ACL 错误，不重试、不改权限、不借 Unity 文件 IO 绕过；使用已工作的原生文件读写工具。

这里的 capture 调用已有 Unity 渲染与 Capture，仅输出预览产物；不是任意文件读写/外壳替代入口。源文件/作者资产的实际修改继续使用已有作者工具和精确副本。
