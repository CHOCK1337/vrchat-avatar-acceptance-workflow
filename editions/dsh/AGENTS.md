# Avatar Workbench 的 DSH 执行入口 — 0.3.2-local5-dsh

分发包 0.3.0：首次安装/配置先读 `docs/USER_GUIDE.md`、`docs/MCP_AND_CONFIG.md`；实际改模只需先读本文件、`skills/assemble-vrchat-avatar/SKILL.md` 和 `DEEPSEEK_RULES.md`（核心页）；`docs/AGENT_GUIDE.md` 和 `references/` 下的细则遇到对应情况再读，不预读全部。只做工具安装、打包或文档时不执行下面的 Avatar 修改流程，不把接入授权扩成模型修改。

沿用原改模方法、作者功能和用户约束；本版本只补 DSH 路由和可验证的检查，不换成完整审计或另一套改模算法。

开始/续作先 `mcp__avatarWorkbench__workbench_status`，再 `mcp__unityMCP__list_unity_instances`。用户这次指定的工程/目标优先；工程或任务不符时，调用 `context_select(project, binding?, storage_root?)` 选择本会话上下文，不编辑全局配置凑匹配。省略 binding 时读取该工程工作台保存的 task_path/selection.json。核对 routing_ready，再读一次 task_read 和 feedback_read。SUPERSEDED/CANCELLED 任务只作历史，不恢复其操作。

当前需求 > 用户最后有效纠正 > 旧提示词/交接记录。记录本次目标、保留项、替代项、默认外观和候选身份。无需重新询问已明确的选择；只有冲突影响执行时合并问一次。工作流更新不等于授权修改 Avatar。

Unity 只用现有 swax/UnityMCP-VRC，匹配完整工程路径和精确 Avatar。记录模式、未保存状态和编译状态；每次操作带 comment，检查嵌套 executionSuccess。单一现场写入者；持久化修改前 consumer_lease connect/busy；临时换装、姿势、等待动画与截图前把本会话租约设为 idle（保留 active 排他所有权），busy 会禁止控件并暂停预览。结束/失败 disconnect，切上下文前释放旧租约。工作台操作使用 [已验证入口](skills/assemble-vrchat-avatar/references/dsh-preview-api.md)，不再猜私有字段；自定义 Editor 程序集通过模板反射调用。

索引只用 catalog_search 的 source_id/category/base/query 分页，不重扫磁盘。封面用 catalog_cover；封面不证明适配。冻结反馈图用 feedback_read 后的 feedback_image：返回带原始哈希/候选身份的真实像素。实际打开图片后才能描述它；工具返回图片不等于模型已经理解。确实无法读图时标记视觉未执行，说明当前宿主/模型限制，不盲称所有图片不可用。

批次结束时检查受影响控制和默认外观。MA 互斥衣柜/发型使用本包只读 probe，明确本次参数和菜单根；检查默认值、互斥覆盖、菜单来源及空子菜单。按真实报错对象定位，不按报错数量猜根因。该查询不能替代 NDMF 输出、真实菜单操作或看图；不为每件素材跑完整构建。

改模的判定标准和分层记录以 `DEEPSEEK_RULES.md` 核心页为准（首件可见增量、真实衣片、真实控件 A→B→A、覆盖完整腿脚的原图、installed/source_checked/provider_checked/control_tested/visual_checked 分开记录）。贴图替换、身体网格替换、跨体型适配分别记录；缺关键功能、明显叠衣或实际控制未测时保持未完成。

回复 addressed 前先在 Unity 跑收尾检查（`skills/assemble-vrchat-avatar/references/finish-check.md`），回复工具会核对；请求带 `compatibility` 时按其 `decision` 处理。反馈先读再 ack：seen=已读，processing_feedback=开始处理，addressed=已答复，均不等于视觉修复通过。保留旧图、原话、候选哈希和区域。资源批次结束再读一次，不高频轮询，不为本次升级改写旧记录。

完整成品/显式优化沿用原 Skill 一次保守 AAO 和授权范围内的最终 SDK 本地测量。小改不扩成完整审计。明确“不构建”优先；不 Build & Test、不上传、不修改上传权限、不 commit/push。保留当前模型、DSH 权限、全局 Skill、现有 GUI 和 Codex 接入，不静默切换模型或增加审核代理。
