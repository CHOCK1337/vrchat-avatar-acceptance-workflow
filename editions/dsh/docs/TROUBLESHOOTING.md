# 故障排查

先保留当前错误和 `local/doctor-result.json`。不要发送 Key、`.credentials.yaml`、完整 DSH profile 或聊天数据库。

| 现象/错误 | 原因与处理 |
|---|---|
| `OFFLINE_RUNTIME_MISSING` / 找不到 Python | ZIP 没完整解压，或安全软件隔离了文件。重新核对原 ZIP 的 SHA256 和完整性；不要只拷贝几个脚本 |
| `DSH_PROFILE_NOT_FOUND` | DSH 尚未首次启动，或用了不同 DSH_HOME/profile。先完成 DSH 自身设置，再指定实际目录 |
| `UNITY_PROJECT_REQUIRED` | 选到 Assets、模型目录或不存在的工程。选择含 Assets 和 ProjectSettings 的根目录 |
| `UNITY_IS_OPEN` | 先保存并关闭目标 Unity 工程，再 Apply。不会替用户强制关进程或丢弃未保存内容 |
| `LOCAL_EDIT_CONFLICT` | 已有工作台代码不是识别到的旧版本。按报出的文件做局部合并，不用强制覆盖整目录 |
| `EXISTING_UNMANAGED_ID` / `EXISTING_MCP_NAMESPACE` | 已有另一条同 ID 或同服务名配置。让 Agent 查看那一项和本包模板并局部合并，保留账号/权限；不能同时注册两个 unityMCP |
| `PROFILE_PARSE_FAILED` | 原 YAML 语法异常，安装未写入。用本地编辑器查看错误；工具不回显可能带 Key 的配置片段 |
| DSH 新会话没有工具 | 完全退出后重新打开 DSH；检查三个固定 ID 是否启用、运行路径是否仍存在。更新本包路径后重跑 Setup。不要靠放开全部权限解决 |
| MCP 能列工具，Unity 项目 `NOT_READY` | 打开准确工程，等编译完成；UnityMCP → Debug Window 应 Listening。检查 Console 首个错误。没开 Unity 是正常未就绪，不是模型失败 |
| `TASK_SUPERSEDED_OR_CANCELLED` / `WORKBENCH_BINDING_MISMATCH` | 缓存的任务已换绑。Agent 用 context_select 选择当前工程，再读 task/feedback；不改全局配置去迎合旧任务 |
| 工作台无“动态预览”/姿势不动 | 核对 Workbench 版本、Gesture Manager 和 SDK；0.2.12 修复了本机 GM 3.9.8 / SDK 3.10.1 的兼容问题。其他版本先取实际异常，不能假定同样兼容 |
| 预览控件被锁、动画暂停 | 查看当前 lease。自己的持久改动结束后转 idle；不释放别的 Agent 的锁。租约不是“已完成改模”的标记 |
| 菜单有新衣，画面没变化 | 先确认真实衣片和当前预览候选；看源→NDMF→预览首处分歧，不能靠增加菜单项凑数 |
| 腿细/缺腿、脱衣不恢复 | 检查 Shape Changer、原装 FX、BlendShape、遮罩与烘焙顶点；需要实际完整腿脚图，不能一次清零所有身体形态键 |
| 保存给 DSH 后无回复 | 保存不是自动唤醒。复制工作台接手文字到 DSH，再让它读同一任务/反馈 |
| `pwsh` 无法执行 | 原生 DSH 的 Shell 环境问题。包的启动器使用 Windows PowerShell，MCP 用 exe 绝对路径；Agent 可用已正常工作的读写工具和 MCP，不能假装命令执行过 |
| 模型说“看不到图片” | 先确认反馈原图工具是否返回像素和当前模型的能力。确实不可读则标视觉未测，请用户查看或选择支持图像的模型，不编造结论 |
| SDK 构建成功但 VeryPoor | 多套衣服、隐藏网格和 PB 仍计入性能成本；让用户选择要保留的内容后再做优化，不擅自删掉必选素材 |

诊断路径：文件完整性 → 安装/配置 → 两个 MCP → 准确 Unity 工程 → 工作台加载 → 真实素材/候选 → 实际控件 → 看图 → 最终构建。只解决当前失败层，不重复无关的大范围安装或审计。
