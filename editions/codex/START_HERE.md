# 开始使用 Codex 版

1. 将 ZIP **完整解压到固定位置**，例如 `C:\Tools\AvatarWorkbench-Codex`。不要直接在压缩软件里运行，也不要只复制 Unity 子目录。
2. 准备已经能正常打开的 Unity 2022.3 工程。保存并关闭这个工程的 Unity 编辑器。
3. 双击 `Setup.cmd`，粘贴包含 `Assets` 和 `ProjectSettings` 的工程文件夹。成功显示 `INSTALLED_NOT_UNITY_VERIFIED`；重复安装无变化则显示 `UNCHANGED`。
4. 打开同一 Unity 工程，等编译完成。菜单 `Tools > 改模工作台（Avatar Workbench）` 打开插件。已有设置如果选的是 DSH/API，在工作台“AI 与模型设置”改选“当前 Codex”。
5. 双击 `Doctor.cmd`。`MCP_AND_UNITY_READY_SESSION_NOT_TESTED` 表示配置、两个 MCP 和 Unity 插件加载通过；还需要完成下一步。
6. 在 Codex 桌面应用打开**这个 Unity 工程文件夹**作为本地工作区。按应用提示信任自己的工程，开启该工程聊天。将 [首次接入提示词](examples/FIRST_TASK.md) 粘贴进去，让 Agent 读取本工程配置、确认工具并绑定当前会话。
7. 在工作台选择明确的 Avatar/候选，先做一件小改动。例如“给候选换上这件适配衣服，保留原版与部件开关”。在工作台点击发送，或复制真实交接文本到刚才的聊天。

不需要填另一个模型 API Key 来使用当前 Codex。首次登录 Codex、项目信任、资源导入和当前聊天绑定仍由用户完成；安装器不会猜测会话，也不会自动开第二个 Agent。

新用户先看 [用户手册](docs/USER_GUIDE.md)。失败先看 [排错](docs/TROUBLESHOOTING.md)。安装后保留整个工具包原位置；移动后需在新位置重新执行 Setup，并让 Codex 重新加载工程配置。
