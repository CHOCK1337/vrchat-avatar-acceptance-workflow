# 交给 Agent 的安装提示词

请读取本包 README.md、docs/USER_GUIDE.md 和 docs/MCP_AND_CONFIG.md。用户已授权把本工具包安装接入指定的现有 DSH/Unity 工程。先用 Setup.ps1 -Project "<用户工程>" 预览；核对没有未知文件冲突后，用同样参数加 -Apply。仅在真实目标或现有冲突不明确时问用户；普通可逆安装无需重复请示。

保持 DSH 的模型、凭据和权限，复用已有 UnityMCP。完整包的 Python/Node/MCP 已准备好，不要重新装 Codex、额外隔离 DSH 或扫描全盘。Unity 正在打开时请保留未保存工作，不强杀进程。

安装完成后让用户打开 Unity，运行 Doctor。没有真实模型任务授权时只检查接入，不修改 Avatar。说明实际通过的层及尚未测试的模型/视觉结果。
