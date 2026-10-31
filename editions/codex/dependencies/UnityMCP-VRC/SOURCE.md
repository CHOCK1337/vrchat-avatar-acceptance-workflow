# 本机实际 UnityMCP 源码快照

上游：https://github.com/swax/UnityMCP-VRC

Git HEAD：`ede88559404b56789098d76988d89723a13cd658`。

包含本机已有局部修改：`UnityMCPPlugin/Editor/UnityMCPConnection.cs` 和 `unity-mcp-server/package-lock.json`，不是未经修改的上游发行版。复制时原工作树未改变。具体文件哈希由迁移包 MANIFEST.json 记录。

保留原 CC BY-NC 4.0 LICENSE；本迁移不改变许可。仅包含 Editor 插件、Node 服务源码、构建脚本和依赖锁；不含 node_modules、注册表缓存或任何 Unity 工程资产。

2026-10-31 修订：`UnityMCPPlugin/Editor/UnityMCPConnection.cs` 改为在编辑器加载时立即启动 HTTP 监听，原 `delayCall` 启动保留为兜底。原因：Unity 窗口未获得焦点时几乎不刷新，延迟启动会让后台打开的工程一直连不上。
