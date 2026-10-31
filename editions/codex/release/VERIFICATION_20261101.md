# Codex 发布前复核 · 2026-11-01

- 对应完整包 `AvatarWorkbench_Codex_Full_v0.1.6_Win64_20261101.zip` 的 ZIP CRC、全部 manifest 文件哈希及外部 SHA-256 通过。
- 把 ZIP 完整解压到新目录，运行 `tools/verify_package.py` 结果为 PASS，并用随包 Python 执行 **47 项隔离回归测试，全部通过**。
- Windows 命令：`runtime\python\python.exe -X utf8 -B -m unittest discover -s verification -p "test_*.py"`。
- 完整包由本目录源码加固定版本的便携运行时打包；本目录的 `release/` 发布记录和 Git 元数据不进入 ZIP。发行文件清单保存在 `FULL_PACKAGE_MANIFEST.json`。
- 对 ZIP 内全部文本做密钥与本机个人路径模式扫描，无命中；不含 `local/`、凭据文件、Unity 缓存、私人任务与反馈或商业 Avatar 素材。
- 本轮不进行模型推理、Avatar 修改、SDK 构建或上传。上一版的复核记录 `VERIFICATION_20261022.md` 保持原样。
