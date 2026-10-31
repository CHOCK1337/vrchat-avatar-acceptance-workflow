# 升级、搬家与回滚

## 升级

把新 ZIP 解压到独立固定目录。保存并退出 DSH/Unity，运行新目录的 Setup。三个固定 ID 会更新到新路径，模型、权限与未涉及配置保留；工作台已知旧版文件可更新，未知本地修改会阻止覆盖。已有 UnityMCP Editor 插件复用，不重复安装第二份。

成功后重新打开 Unity，运行 Doctor，再开 DSH 新会话。不要只复制新 DLL/CS 而遗漏 manifest、MCP 和 Skill。旧 ZIP、旧工作目录和回滚记录先保留，确认新包可用后再自行清理。

## 更换解压位置/工程

绝对路径是安装时按本机生成的。移动工具目录后运行新位置的 Setup，更新自己管理的 DSH 区块和工作台 Skill 路径。多个工程可以共用工具包，运行时让 Agent `context_select` 切换准确项目；同时持久写入时只允许一名 Agent 操作一个现场。

## 撤销本次安装

每次有实际写入时生成 `local/rollback/<时间戳>/restore.json`，具体路径在 `local/install-receipt.json`。先保存关闭 DSH/Unity，预览撤销：

```powershell
.\runtime\python\python.exe .\tools\rollback.py --receipt ".\local\rollback\<实际时间戳>\restore.json"
```

确认列出的本次记录后，在已授权的撤销范围执行：

```powershell
.\runtime\python\python.exe .\tools\rollback.py --receipt ".\local\rollback\<实际时间戳>\restore.json" --apply
```

工具逐一核对安装后哈希。某文件后来被用户或 DSH 修改时会以 `LATER_EDIT_DETECTED` 停止整个撤销，避免覆盖新改动。没有强制覆盖开关。可由 Agent 针对那个文件人工合并。回滚不删用户素材或 Unity 自动产生的 `.meta`，不递归清空工程。

## 给维护者重新打包

`tools/prepare_runtime.py` 从固定官方 HTTPS URL 下载 Node/Python 并核对官方 SHA256，按锁文件编译 UnityMCP、安装校验依赖；不调用 DSH 模型、不读取账号。运行时来源在 `runtime/PROVENANCE.json`。

```powershell
python .\tools\prepare_runtime.py
python .\pack.py --version AvatarWorkbench_DSH_Full_v0.3.5_Win64_20261101 --full
```

分发器使用允许清单，排除 local、个人 profile、任务/反馈、截图、模型和 Unity 缓存。ZIP 同时生成 manifest 与 `.sha256`，拒绝覆盖已有同名产物。重新测试后再发新版本，不把用户安装后目录直接压缩发布。

运行测试不会自动安装到真实 DSH profile：

```powershell
.\runtime\python\python.exe -m unittest discover -s .\verification -p "test_*.py"
```

用户安装不需要运行开发测试或重建运行环境。
