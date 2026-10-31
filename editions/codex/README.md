> **下载安装请前往 [Release v0.1.0](https://github.com/CHOCK1337/vrchat-avatar-acceptance-workflow/releases/tag/codex-v0.1.0)。** 下载 `Full...zip` 完整安装包，完整解压后运行 `Verify-Package.cmd`，再按 `START_HERE.md` 使用 `Setup.cmd`。
> 本仓库保留对应发行版本的源码、Unity 插件、MCP、配置模板、用户文档和 Agent 文档；GitHub 自动生成的 Source code ZIP 不包含便携 Node/Python 和编译后的 MCP 运行环境，不能代替 Full 安装包。

# Avatar Workbench · Codex 完整包 0.1.0

Windows x64 的 Unity 改模工作台接入包。双击 `Setup.cmd`，选择自己的 Unity 工程；安装器会部署工作台、两个 MCP 和改模 Skill，并生成该工程的 Codex 配置。

先读 [开始使用](START_HERE.md)。本包沿用 DSH 版的工作台与预览实现，新增 Codex 专用安装、配置、检查和回滚入口。它是本地集成 ZIP，不是 Codex 插件商店安装项。

包含 Unity 插件 0.2.16-codex、工作流 0.3.2-local5-codex1、Workbench MCP、UnityMCP Editor/服务端、Node 24.15.0、Python 3.13.16、Unity Newtonsoft JSON 3.2.1、源代码、许可证、校验清单和文档。安装工具本身不需要 npm/pip 或在线下载。Unity、Codex、账号、VRChat SDK、模型、衣服、Shader 和作者要求的适配插件由使用者准备。

| 入口 | 用途 |
|---|---|
| `Setup.cmd` | 校验包并安装到选定工程 |
| `Doctor.cmd` | 检查配置、MCP 握手和准确的 Unity 工程 |
| `Verify-Package.cmd` | 校验发行文件的 SHA-256 |
| `Rollback.cmd` | 撤回最近一次安装写入；遇到后续修改会停止 |
| [用户手册](docs/USER_GUIDE.md) | 从安装到首次改模、反馈、回滚 |
| [Agent 手册](docs/AGENT_GUIDE.md) | 真实会话绑定、工具调用和验收规则 |
| [配置说明](docs/MCP_AND_CONFIG.md) | 文件位置、复用、冲突、多工程 |
| [操作案例](docs/EXAMPLES.md) | 换装、腿部、截图反馈、切换工程 |
| [检查记录](verification/DISTRIBUTION_VERIFIED.md) | 实际验证范围和未测项 |

安装和连接检查通过不等于 Avatar 外观通过。首次使用仍需在自己的工程里确认候选、实际换装、腿部全貌和相关动态。当前 Codex 桌面会话的直送需要用户绑定；不支持管道时可用工作台真实交接文本粘贴到同一聊天继续。
