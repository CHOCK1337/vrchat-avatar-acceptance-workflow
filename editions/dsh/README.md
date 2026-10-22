> **下载安装请前往 [Release v0.3.0](https://github.com/CHOCK1337/vrchat-avatar-acceptance-workflow/releases/tag/dsh-v0.3.0)。** 下载 `Full...zip` 完整安装包，完整解压后运行 `Verify-Package.cmd`，再按 `START_HERE.md` 使用 `Setup.cmd`。
> 本仓库保留对应发行版本的源码、Unity 插件、MCP、配置模板、用户文档和 Agent 文档；GitHub 自动生成的 Source code ZIP 不包含便携 Node/Python 和编译后的 MCP 运行环境，不能代替 Full 安装包。

# DSH 改模完整工具包 · Windows x64 · 0.3.0

让已经安装 DSH 的用户，把 Unity 工作台、两个 MCP 和改模 Skill 一起接通。包内提供便携 Node/Python、编译好的 MCP、配置生成器、安装记录、回滚和操作文档。正常安装不需要 npm、pip、Codex 或管理员权限。

**开始：完整解压 → 关闭 Unity/DSH → 双击 `Setup.cmd` → 输入 Unity 工程目录 → 重新打开 Unity → 双击 `Doctor.cmd` → 在 DSH 新建会话。**

DSH 应先用用户自己的账号配置好可调用工具的模型；Unity 工程应已有 VRChat Avatar、合法素材及素材依赖。这个包不包含 DSH 桌面安装程序、模型服务额度、Unity/VRChat SDK、付费模型或作者衣服。它提供完整的改模工具接入，不承诺任意素材都能自动适配。

## 按角色阅读

| 你要做什么 | 入口 |
|---|---|
| 第一次使用 | [用户手册](docs/USER_GUIDE.md) |
| 交给 DSH Agent 操作 | [Agent 手册](docs/AGENT_GUIDE.md)、[可复制启动提示词](examples/FIRST_TASK.md) |
| 学习衣服、头发、道具、插件操作 | [操作案例](docs/EXAMPLES.md) |
| 排查无法连接、换装没变化、腿异常 | [故障排查](docs/TROUBLESHOOTING.md) |
| 配置文件、工具接口和目录 | [MCP 与配置](docs/MCP_AND_CONFIG.md) |
| 升级、搬家、撤销安装 | [维护与回滚](docs/MAINTENANCE.md) |
| 了解本次测了什么 | [发布验证](verification/DISTRIBUTION_VERIFIED.md) |
| 再分发或商用 | [来源与许可](THIRD_PARTY_NOTICES.md) |

## 版本与边界

分发包 0.3.0；Unity Workbench 0.2.12-dsh；Skill 0.3.2-local5-dsh；Workbench MCP 0.2.1（便携启动修正）；Node 24.15.0；Python 3.13.16。UnityMCP 的精确源码与本地修订见 [SOURCE.md](dependencies/UnityMCP-VRC/SOURCE.md)。

Workbench 在 2022.3.22f1 / GM 3.9.8 / SDK 3.10.1 上完成过真实换装、PhysBone 和姿势预览验证。其他版本需按 Doctor 与实际预览结果确认，安装器不强制升级、降级现有工程。

“保存给 DSH”保存需求和反馈；首次接手需要把工作台给出的接手文字复制到 DSH。它不会自动唤醒聊天。工具连接通过、Agent 读过任务、画面正确、本地构建成功、VRChat 客户端表现是不同的结果。

完整包带 UnityMCP-VRC，须遵守其 **CC BY-NC 4.0 非商业许可**；不能把整个 ZIP 宣传为无限制商用的 MIT 包。
