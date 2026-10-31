# 改模工作台：DSH 与 Codex

两个工作台版本共用原改模思路，分别提供各自宿主的安装和接入。核心验收工作流的原有 `WORKFLOW.md`、`references/` 与 `scripts/` 保留。

| 版本 | 用户文档 | Agent 文档 | 操作案例 | Release |
|---|---|---|---|---|
| DSH 0.3.5 | [用户手册](dsh/docs/USER_GUIDE.md) | [Agent 手册](dsh/docs/AGENT_GUIDE.md) | [案例](dsh/docs/EXAMPLES.md) | [下载安装包](https://github.com/CHOCK1337/vrchat-avatar-acceptance-workflow/releases/tag/dsh-v0.3.5) |
| Codex 0.1.6 | [用户手册](codex/docs/USER_GUIDE.md) | [Agent 手册](codex/docs/AGENT_GUIDE.md) | [案例](codex/docs/EXAMPLES.md) | [下载安装包](https://github.com/CHOCK1337/vrchat-avatar-acceptance-workflow/releases/tag/codex-v0.1.6) |

## 安装

从对应 Release 下载 Full ZIP 及 `.sha256` 文件，完整解压到固定可写目录。运行 `Verify-Package.cmd` 后，保存并关闭 Unity 和所用客户端，执行 `Setup.cmd`，输入自己的 Unity 工程目录。重开 Unity 后按包内文档运行 `Doctor.cmd`。

Full ZIP 自带便携 Python/Node、已编译 MCP、Unity 插件、配置生成器、用户文档、Agent 文档与案例。Git 仓库只跟踪公开分发源码和依赖说明；GitHub 的 Source code ZIP 不包含便携运行环境。Unity、DSH/Codex 客户端和账号、VRChat SDK、Avatar、衣服和素材依赖按用户实际工程准备。

DSH 的保存操作写入任务收件箱，需要手动将接管指令粘贴到 DSH 会话；不会自动唤醒聊天。Codex 可绑定真实桌面会话，不能直送时可复制接管文本。安装、工具连接、模型理解、真实换装和最终客户端验收分别记录。

## 版本与来源

`editions/dsh` 对应完整包 0.3.5，`editions/codex` 对应完整包 0.1.6。源码从已核验的完整包提取，下载链接与发布记录适配到本仓库。完整包内的用户/Agent 文档保持随包版本；DSH 界面内仍可能显示原有 `0.2.9-dsh` 标识。

各版的 `release/VERIFICATION_20261022.md` 保留发布前验证范围：DSH 33 项、Codex 42 项隔离测试通过。安装与连接测试不代表 Avatar 已修复。本次合入不修改核心判定算法，不执行 Avatar 修改、构建或上传。

公开仓库和本次公开 Release 不附用户截图、商业模型、衣服源文件、账号信息、个人安装配置或任务/反馈数据。

## 许可

项目自有代码与核心工作流采用 MIT。完整工作台随附 UnityMCP-VRC 等第三方组件，需遵守对应非商业许可等条件。请阅读各版 `THIRD_PARTY_NOTICES.md`、`licenses/` 和仓库根目录说明。
