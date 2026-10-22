# Codex 发布前复核 · 2026-10-22

- 对应完整包 `AvatarWorkbench_Codex_Full_v0.1.0_Win64_20261021.zip` 的 ZIP CRC、全部 manifest 文件哈希及外部 SHA-256 通过。
- 在对应完整包的解压目录内，用随包 Python 执行 **42 项隔离回归测试，全部通过**。
- Windows 命令：`runtime\python\python.exe -X utf8 -B -m unittest discover -s verification -p "test_*.py"`。
- `-X utf8` 明确测试文件的默认编码，避免中文 Windows 上默认 GBK 导致测试夹具读取失败。
- 仓库不跟踪便携运行时和 node_modules；需要运行安装器或上述完整测试时，请使用 Release 的 Full ZIP。
- 源码从已核验的发行 ZIP 提取；新增 README 下载入口、Git 元数据和发布记录。原始发行文件清单保存在 `FULL_PACKAGE_MANIFEST.json`。
- 私人配置、凭据、会话、任务、反馈数据、商业 Avatar 与衣服源文件未包含在分发代码中；文本密钥模式扫描无命中。
- 本轮不进行模型推理、Avatar 修改、SDK 构建或上传。历史验证记录保持原有日期与范围。
