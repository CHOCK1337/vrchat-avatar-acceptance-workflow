# 给 DSH / Agent 的操作手册

## 入口与优先级

先读 [AGENTS.md](../AGENTS.md)、[改模 Skill](../skills/assemble-vrchat-avatar/SKILL.md)、[DeepSeek 规则](../DEEPSEEK_RULES.md)。当前用户请求及最后有效纠正优先于案例、历史交接和示例任务。文档里的工程路径、实例 ID、参数和值只是示例，不是本机事实。包安装请求不构成任意 Avatar 修改或上传授权。

需要部署时按 [用户手册](USER_GUIDE.md) 和 [配置说明](MCP_AND_CONFIG.md) 使用现有安装器。默认 preview，检查具体改动后在已经授权的安装范围内 apply；无须对普通可逆安装再问一次。不得整体覆盖 DSH profile、迁移凭据、改模型/权限或删除已有 MCP 来消除冲突。

## 每个新任务的起点

1. `avatarWorkbench.workbench_status`，读取配置与路由状态。
2. `unityMCP.list_unity_instances`，匹配用户工程完整路径，记录精确 `instanceId`。
3. 必要时 `avatarWorkbench.context_select({project})`；让本会话读取该工程保存的工作台绑定。显式任务可传 `binding`。确认 `routing_ready`，再 `task_read` / `feedback_read`。
4. 用 Unity MCP 只读核对准确 Avatar、源/处理后候选/预览的身份、未保存状态和编译状态。`SUPERSEDED` / `CANCELLED` 任务只读历史，不复活。
5. 本机没有目标工程、Avatar 或作者依赖时，明确缺什么；不悄悄换成另一个角色或体型。

DSH 中工具名通常显示为 `mcp__avatarWorkbench__workbench_status`、`mcp__unityMCP__list_unity_instances`。以宿主实际列出的 schema 为准，不猜参数。

## 从安装到可见结果

- 为本会话取得唯一 consumer lease。持久改动时 `connect → busy`，临时试穿、等物理、截图时 `idle`（仍保持 active 排他所有权），结束/失败 `disconnect`。不得释放别人的租约。
- 选定资源集中预检：真实网格/材质、作者体型、依赖、控制器、遮挡与恢复。只读已有目录索引；不要反复扫描整盘。
- 在私有候选上串行装配。第一次使用预览链路，先完成一件真实衣服的可见切换，再扩大批次。保留原素材、共享材质、GUID、作者菜单和用户指定部件独立控制。
- 预览调用只用 [已验证 API 与反射模板](../skills/assemble-vrchat-avatar/references/dsh-preview-api.md)。每次 Unity 调用带 `comment` 和精确实例，检查外层错误及内层 `executionSuccess`、`ok`。
- 实际操作菜单/控件 A→B→A，等 `settled` 后核对状态并打开返回的原始 PNG。直接置参或 SetActive 只能记“配置诊断”，不能记 `control_tested=PASS`。
- 图片工具返回了数据，不代表当前模型已理解图片。无法看图时 `visual_checked=NOT_RUN`，告诉用户需要哪张图/哪项检查；不要未经选择切模型或编造视觉结论。
- 每批更新 QuickTask 的候选、资源状态、当前动作和证据路径；批次结束再读一次反馈。

## 对常见失败的处理

| 首个分歧 | 接下来查什么 |
|---|---|
| 菜单变了，衣服没变 | 源根是否有衣片；NDMF 输出是否包含；预览是否绑定正确候选；实际菜单是否控制对应消费者 |
| 腿细/消失/脱衣不恢复 | 原装 FX、Shape Changer、BlendShape、遮罩、烘焙输出；每项遮挡的退出恢复 |
| 拼头后发根错位 | 作者头部替换工具施加的实际坐标映射；新增头发是否使用相同映射 |
| 袖子僵硬/姿势参数变而不动 | PhysBone 运行状态、Action 权重、SDK 回调与预览时钟；不能删骨骼或清空动作来掩盖 |
| 插件关闭仍有效 | 已合并 Animator/材质消费者是否真的受门控；关闭空对象通常不够 |
| SDK 构建失败 | 读取实际失败对象、版本与异常。修 PB 启用域或私有贴图/动画换材质引用；不绕过 SDK 检查 |

沿源配置→作者控制→处理输出→当前预览找到首处分歧，小范围修正。不要发明新 fitter、截图框架或巨大 Builder。复杂修改复用已验证脚本；案例里的 4.9 cm、256 PB 等数字不能移植为通用目标。

## 交付记什么

`installed`、`source_checked`、`provider_checked`、`control_tested`、`visual_checked`、`sdk_build`、客户端/上传分别记录。不能用其中一项代替另一项。实际完全装配时沿 Skill 做一次保守优化与授权范围内的最终 SDK 本地 Build，显式“不构建”优先。没有授权不上传，不 Build & Test。

输出可编辑源和最终候选路径、验证过的可见结果、剩余问题、构建/性能边界。任务仅在必需内容实际完成后写 COMPLETE；回复或参数数量不构成成功。示例见 [操作案例](EXAMPLES.md) 和 [完成报告模板](../examples/RESULT_TEMPLATE.md)。
