# 本发行版的交付和验收要求

交付完整 ZIP、SHA-256、MANIFEST、依赖许可证、安装/检查/回滚工具、用户手册、Agent 手册、操作案例和实际验证记录。源工程、用户 profile、账号、私有反馈、付费模型和 Library/Temp 不进入发行包。

发行检查应使用最终 ZIP 重新解压，实际运行包内 Python/Node，验证安装、重复安装、保留既有配置、拒绝冲突、回滚和两个 MCP 协议。另用隔离 Unity 工程验证编译和准确实例绑定，避免触碰用户当前 Avatar。

Codex 配置可解析、CLI 能列出服务器、MCP 自检通过、Unity 加载成功与桌面会话可直送是不同结果。桌面直送未经实际用户绑定和投递就标 NOT_RUN；工具包本身不提供任何 Avatar 外观保证。参见 [本次验证记录](../verification/DISTRIBUTION_VERIFIED.md)。
