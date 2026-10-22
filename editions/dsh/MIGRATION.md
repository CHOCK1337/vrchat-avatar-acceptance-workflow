# 0.2.2 DSH 原生工作台接入 — 2026-10-15

Unity GUI 更新到 0.2.8-dsh，DSH Skill 为 0.3.2-local4-dsh，适配器为 0.2.1。补齐 DSH 需求路由、独立 Skill 路径保存、优化入口、回复署名、不可变请求重试和 API 续作；修复通知跨工程重放与未完成 JSON 丢通知。

本轮使用官方 DSH 0.2.0-rc.2 的 CLI 启动真实模型会话完成源码修改，并实际消费 Unity 新提交的接入/冻结图请求。当前 Unity 已编译、显示两条真实 DSH 回复；原版→Mira→原版控制入口回切已测试。图像通路可用，样本外观仍有缺陷并明确记 FAIL；未进行 Avatar 修复、SDK 构建或上传。完整范围见 verification/WORKFLOW_VERIFIED.md。

已有电脑需更新 Unity 插件，在“API 与模型设置”独立保存 DSH 包内 Skill 路径并选择当前 DSH。保存请求不会自动唤醒模型。原 desktop 模型配置、全局 Codex Skill、场景文件与 Avatar Prefab 哈希保持不变。插件与配置回滚证据保留在本机 local/rollback 和工程 .avatar-workbench-update，不进入公开包。

以下均为历史记录，旧的“没有 DSH 按钮”“本轮无模型调用/Unity 实例”等结论不适用于 0.2.2。

---

# 0.2.1 DSH 执行规则更新 — 2026-10-14

DSH Skill 更新为 0.3.2-local3-dsh。新增 DEEPSEEK_RULES.md，补充真实衣片、预览身份、身体遮挡恢复与实际输入/完整图像验收；只读 probe 增加可选服装根/身体 Renderer 范围。工具协议仍为 0.2.0、9 个工具不变。保留适配器已有通知线程，只更新 workflow_version；不重装或覆盖当前 Unity GUI，不改当前模型、场景、任务反馈、DSH profile 或全局 Skill。原位回滚在 local/rollback/rules-*。

本次已执行和未执行的验证见 verification/WORKFLOW_VERIFIED.md。以下保留历史，不能据此推断当前在线状态。

---

# 0.2.0 DSH 工作流局部升级 — 2026-10-13

DSH 专用 Skill 从 0.3.2-local1 更新为 0.3.2-local2-dsh；不修改全局 Skill。新增 session-local context_select、带哈希的反馈原图工具、定向 MA 源配置查询和分层验收指引。保留原 GUI、作者菜单、装配方法、AAO、索引、反馈、上传边界和模型配置。源码回滚副本在 local/rollback/workflow-*，不进公开包。

本版本的实际验证以 verification/WORKFLOW_VERIFIED.md 为准。以下为旧迁移历史，包含当时的模型/Unity 状态；不能据此判断当前在线状态。

---

# 迁移内容与不变部分

迁移来源是本机实际生效的 assemble-vrchat-avatar 0.3.2-local1，含 2026-10-08 加入的成品保守优化。Unity 源码采用 Avatar Workbench 0.2.7 当前局部合并状态，`.meta` 原样保留。没有拿旧仓库根目录 0.3.0-rc1 的严格发布流程替换当前 Skill。

DSH 接管自然语言任务、Skill 阅读、原有 MCP 调度、索引筛选、反馈消费及实际证据的记录。改模算法、作者菜单、MA/NDMF、AAO 的业务规则保持原样。文件名 `codex_feedback.py` 为兼容沿用，它本身是宿主无关的收件箱读写工具。

本包不是完整 Unity 工程，不带 Avatar、付费衣服、个人截图、Mio 数据库、真实任务或 Key。索引只保留对本机文件的引用，不存在全库上传或跨电脑自动搬运。换电脑需要用户把合法素材放到新位置并重新选择/关联目录。

source Skill 下的旧开发安装回执/缓存不在公开包中；保留运行所需入口、引用、模板和脚本，包根 MANIFEST.json 记录实际哈希。全局 Skill 的源文件没有修改。agents/openai.yaml 是原 Skill 的宿主元数据，DSH 不靠它选择模型；DSH 使用自己的当前模型设置。

旧任务原样只读，不能自动转换覆盖；工作流按既有 QuickTask 合并助手维护本次记录。GUI、DSH 适配器只处理各自请求与反馈，避免并行整文件重写任务。MCP 租约协作避免同时写 Unity，但不能强行阻止用户手工操作或绕过租约的其他程序。

实际模型调用受当前服务商 Global 区域限制；因此本轮没有 DSH 模型读回成功证据。协议读写闭环和 DSH 官方宿主工具加载分别有真实记录，不能互相冒充。当前无 Unity 实例，未重做原工作台的窗口验收或模型构建。
