# 改模工作台：DSH 与 Codex

两个工作台版本共用原改模思路，分别提供各自宿主的安装和接入。核心验收工作流的原有 `WORKFLOW.md`、`references/` 与 `scripts/` 保留。

| 版本 | 用户文档 | Agent 文档 | 操作案例 | Release |
|---|---|---|---|---|
| DSH 0.3.6 | [用户手册](dsh/docs/USER_GUIDE.md) | [Agent 手册](dsh/docs/AGENT_GUIDE.md) | [案例](dsh/docs/EXAMPLES.md) | [下载安装包](https://github.com/CHOCK1337/vrchat-avatar-acceptance-workflow/releases/tag/dsh-v0.3.6) |
| Codex 0.1.7 | [用户手册](codex/docs/USER_GUIDE.md) | [Agent 手册](codex/docs/AGENT_GUIDE.md) | [案例](codex/docs/EXAMPLES.md) | [下载安装包](https://github.com/CHOCK1337/vrchat-avatar-acceptance-workflow/releases/tag/codex-v0.1.7) |

## 安装

从对应 Release 下载 Full ZIP 及 `.sha256` 文件，完整解压到固定可写目录。运行 `Verify-Package.cmd` 后，保存并关闭 Unity 和所用客户端，执行 `Setup.cmd`，输入自己的 Unity 工程目录。重开 Unity 后按包内文档运行 `Doctor.cmd`。

Full ZIP 自带便携 Python/Node、已编译 MCP、Unity 插件、配置生成器、用户文档、Agent 文档与案例。Git 仓库只跟踪公开分发源码和依赖说明；GitHub 的 Source code ZIP 不包含便携运行环境。Unity、DSH/Codex 客户端和账号、VRChat SDK、Avatar、衣服和素材依赖按用户实际工程准备。

DSH 的保存操作写入任务收件箱，需要手动将接管指令粘贴到 DSH 会话；不会自动唤醒聊天。Codex 可绑定真实桌面会话，不能直送时可复制接管文本。安装、工具连接、模型理解、真实换装和最终客户端验收分别记录。

## 版本与来源

`editions/dsh` 对应完整包 0.3.6，`editions/codex` 对应完整包 0.1.7。对应的 Unity 工作台版本分别为 `0.2.16-dsh` 和 `0.2.17-codex`。

手动接管的发布验证记录见 [Codex](codex/release/VERIFICATION_20261111.md) 和 [DSH](dsh/release/VERIFICATION_20261111.md)。旧版本的验证记录保留在各自的 `release/` 目录。

公开仓库和本次公开 Release 不附用户截图、商业模型、衣服源文件、账号信息、个人安装配置或任务/反馈数据。

## 许可

项目自有代码与核心工作流采用 MIT。完整工作台随附 UnityMCP-VRC 等第三方组件，需遵守对应非商业许可等条件。请阅读各版 `THIRD_PARTY_NOTICES.md`、`licenses/` 和仓库根目录说明。
