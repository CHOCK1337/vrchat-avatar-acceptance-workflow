# Codex 0.1.6 验证记录

日期：2026-11-01。工作台 0.2.16-codex。本次更新工作台界面与字体、收尾检查（含身体遮罩检测）、发送前体型检查、接手指令、安装器对旧版全局 Skill 的提示，以及 UnityMCP 后台监听。

| 层级 | 本次结果 |
|---|---|
| 自动测试 | 47 项通过，在本完整包内用随包 Python 运行；新增收尾检查（`test_finish_gate.py`）和版本一致性（`test_version.py`） |
| 包完整性 | ZIP CRC、全部 manifest 文件哈希与外部 SHA-256 通过 |
| Unity | 工作台离线编译通过；在测试工程中实机检查宽、中、窄三种窗口宽度，以及引导条、空素材库和超长连接标题 |
| Avatar、换装与视觉验收 | **NOT_RUN**。没有修改用户 Avatar |
| 模型推理、SDK Build / 上传 | **NOT_RUN** |

以下是 0.1.0 的原始记录。

## 0.1.0 验证记录（历史）

日期：2026-10-21。对象为本包代码、公开运行环境和新建隔离工程，不是用户的 Avatar。

| 层级 | 本次结果 |
|---|---|
| 自动测试 | 42 项通过：17 项安装/配置/回滚 + 25 项反馈、上下文和通知，含 Windows 物理重定向不改变 Unity 收件箱命名的回归检查 |
| 安装边界 | 中文、空格、`&` 路径；预览不写；重复安装无变化；既有 TOML 注释/模型/权限、API/DSH 设置、`.meta` 与未知文件保留 |
| 冲突与回滚 | 本地改码、重复/不兼容 MCP、失效 TOML、管理区段被改均拒绝；逐字节回滚；后续编辑触发全量拒绝而非部分覆盖 |
| Codex CLI 配置 | 0.163.0-alpha.10.2，隔离 CODEX_HOME，实际 `mcp list --json` 返回两个启用的 stdio 服务；未调用模型 |
| MCP 协议 | 包内 Python/Node 实际启动两个服务；各返回 9 个工具，Workbench 路由与任务读取通过 |
| Unity | 2022.3.22f1，新建空工程，工作台与内嵌 UnityMCP/JSON 编译加载成功；只读执行器确认准确工程、非编译状态、非播放状态 |
| 桌面当前会话绑定和直送 | **NOT_RUN**。未借用用户会话 ID、未发送合成测试消息 |
| Avatar、换装、腿部、动态与视觉验收 | **NOT_RUN**。测试工程无用户模型；历史 DSH 结果不作为本版本新验收 |
| SDK Build / 上传 / VRChat 客户端 | **NOT_RUN** |

发布 ZIP 的逐文件哈希在 `MANIFEST.json`，ZIP SHA-256 位于同名 `.zip.sha256`。最终解压安装、MCP、重复安装和回滚的实际结果另存发行 ZIP 旁的 `.validation.json`，以该文件的状态为准。重现实验时，执行包内 `verification/test_*.py`，只在新建测试工程和隔离配置目录运行。

Doctor 明确区分 `MCP_PROTOCOL_PASSED_UNITY_NOT_CHECKED` 与 `MCP_AND_UNITY_READY_SESSION_NOT_TESTED`；它不会把连接通过写成视觉通过，也不会自动发消息或改 Avatar。
