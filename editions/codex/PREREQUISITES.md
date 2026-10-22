# 使用条件

- Windows x64；正常工作的 Codex 本地应用或 CLI。桌面直送需要当前应用提供本地工具管道；CLI 使用手动交接。
- 已有可打开的 Unity 工程。本包的编译验证环境为 Unity 2022.3.22f1。其他版本应先运行 Doctor 再做小范围测试。
- VRChat 改模需要用户自己的 VCC/SDK、Avatar、衣服、Shader、Modular Avatar/NDMF/Gesture Manager/拟合器等实际作者依赖。本包不代购、不抓取付费模型、不自动重配这些依赖。
- 安装需要目标工程和工具包目录可写；运行需要允许 Codex 启动包内 Python/Node 本地进程。项目信任及组织权限按 Codex 自身界面处理，安装器不降低权限设置。
- 包内已有 Python/Node、两个 MCP 和 Unity Newtonsoft JSON；Unity 已有相应依赖时复用。工具安装可离线，Codex 模型调用、账号登录、缺少的作者资源/SDK 安装仍可能需要联网。
