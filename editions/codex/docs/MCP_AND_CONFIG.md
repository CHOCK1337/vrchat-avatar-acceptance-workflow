# MCP 与配置

## 工程级接入

本发行版使用 Codex 项目级 `.codex/config.toml` 和 `.agents/skills`。项目配置需被 Codex 信任后才加载；安装器不写信任或权限。官方说明见 [配置优先级](https://learn.chatgpt.com/docs/config-file/config-basic)、[MCP](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)、[Skill](https://learn.chatgpt.com/docs/build-skills)。本包不安装插件市场条目。

`Setup.cmd` 调用包内 Python，逐项预检后写入。它保留配置文件中无关内容与注释，只管理明确标记的 MCP 区段。缺少依赖时内嵌 UnityMCP/JSON 包；已有版本保留，由 Doctor 检查能否实际运行。

| 位置 | 内容 |
|---|---|
| `<工程>/.codex/config.toml` | `avatarWorkbench` 与 `unityMCP` 的 stdio 配置 |
| `<工程>/.agents/skills/assemble-vrchat-avatar` | 无既有 Skill 时安装的独立工作流 |
| `<工程>/.avatar-workbench-codex/connection.json` | 准确工程、绑定、Skill、运行时与有效 MCP 来源 |
| 同目录 `installation.json` | 安装器管理区段摘要 |
| 同目录 `install-receipt.json`、`rollback/` | 私有安装收据与旧文件备份 |
| 同目录 `doctor-result.json` | 最近一次连接检查，各阶段独立状态 |
| `<包>/local/last-install.json` | 本机最近安装工程的快捷入口；发行 ZIP 不包含 |
| `%LOCALAPPDATA%/AvatarWorkbench/editor/<工程哈希>` | 工作台窗口、AI 设置、桌面会话绑定；由现有插件使用 |

两个 MCP 都启动本地子进程，无需用户安装系统 Python/Node。`avatarWorkbench` 默认 9 个工具；`unityMCP` 使用其真实工具目录。注册名可能复用旧名称，Agent 应使用当前会话实际暴露的工具。

## 现有配置的处理

全局 `$CODEX_HOME/config.toml`（未设置时用户 `.codex/config.toml`）只读检查，不修改。检查工程及已识别 Git 仓库范围的配置。同名或同实现 MCP 如果重复或不兼容则停止；兼容的 UnityMCP 编译入口可复用。自定义 profile、组织策略或插件来源仍须以当前 Codex 会话实际工具为准，安装器不覆盖这些层。

既有项目/仓库/用户 Skill 保留并复用；多个候选时用 `--skill` 明确选择，不自动覆盖。选择会影响工作台和 Agent 的实际读取路径，但不会删除其他已注册 Skill；若 Codex 同时发现同名技能，仍需用户整理重名。自动复用校验名称、大小和工作台所需优化引用/辅助脚本，不认证定制工作流质量；Doctor 再检查 Unity 实际读取结果。

现有模型服务设置保留 `active`、API profiles、DSH Skill 和未知字段，仅更新当前 Codex Skill 路径。没有旧设置才默认当前 Codex。

## 命令行

在工具包目录执行，路径始终加引号：

```powershell
# 只看计划
.\runtime\python\python.exe .\tools\install.py --project 'C:\VRCProjects\MyAvatar'
# 安装
.\Setup.ps1 -Project 'C:\VRCProjects\MyAvatar'
# 复用指定 Skill
.\Setup.ps1 -Project 'C:\VRCProjects\MyAvatar' -Skill 'C:\MySkills\assemble-vrchat-avatar\SKILL.md'
# 检查指定工程
.\Doctor.cmd --config 'C:\VRCProjects\MyAvatar\.avatar-workbench-codex\connection.json'
# 不打开 Unity，只检查 MCP 协议
.\Doctor.cmd --config 'C:\VRCProjects\MyAvatar\.avatar-workbench-codex\connection.json' --no-unity
# 回滚前只看计划
.\Rollback.ps1 -Project 'C:\VRCProjects\MyAvatar' -Preview
```

测试/特殊宿主可给 install.py 传 `--codex-home`、`--user-skills`、`--storage-root`、`--binding`。存储根必须与该 Unity 工作台实际 DataRoot 相同；只改适配器路径会读错收件箱。

多工程各跑一次 Setup，各用自己的 connection.json 和 Codex 工程聊天。Doctor/Rollback 无参数选最近安装的工程；前面的工程请显式指定。共享包的路径保持固定，不要分发已安装的 `local/`、工程私有状态、回滚备份或账号配置。
