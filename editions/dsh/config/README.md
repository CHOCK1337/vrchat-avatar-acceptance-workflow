# 配置模板

这些文件只说明字段，不带有效本机路径、账号或 API Key。`${BUNDLE_ROOT}`、`${UNITY_PROJECT}`、`${WORKBENCH_DATA}` 等是文档占位符，DSH 不会自动展开它们。

正常用户运行 Setup 即可；真正可用的配置会生成到 `local/`。Agent 需要手工局部合并时，以本机实际路径代入，保持固定 ID 与服务名。不可把模板整体覆盖 DSH profile。

- `connection.template.json`：工作台 MCP 入口。
- `mcp-servers.template.json`：通用 MCP 客户端字段；DSH 自身使用下面的 overlay。
- `dsh-overlay.template.yml`：三个 DSH 接入项；不含模型、凭据、权限配置。
