# 排错

| 现象或错误 | 处理 |
|---|---|
| `UNITY_PROJECT_REQUIRED` | 选择包含 Assets/ProjectSettings 的工程根，不是素材目录 |
| `UNITY_IS_OPEN` | 保存并关闭该 Unity；安装器不会代你结束进程 |
| `LOCAL_EDIT_CONFLICT` | 报告位置存在本地改动。先保留并合并，不强制覆盖 |
| `MCP_CONFLICT` / `DUPLICATE_MCP` | 检查报告中的既有注册，选择维护或明确迁移；不要重复添加或删除无关服务 |
| `DUPLICATE_SKILL` | 用 `--skill` 选择已有 Skill；已有重名自动加载仍需用户整理 |
| `INVALID_TOML` | 原配置语法错误。修正后再安装；不贴包含密钥的整份配置 |
| `MANAGED_MCP_EDITED` | 管理区段在安装后被修改。核对安装收据和本地修改，合并后再安装，不绕过哈希 |
| Doctor `NOT_READY` | 打开它列出的准确 Unity 工程，等编译，查看 UnityMCP Debug Window 和 Console 的实际错误 |
| Doctor MCP 通过，Codex 无工具 | 在 Codex 打开同工程并信任，重新加载/开启工程聊天；核对 profile/插件/组织策略的实际工具来源 |
| 当前 Codex 未连接 | 在目标聊天用首次接入提示词；确认桌面本地管道和实际会话 ID。CLI 采用手动交接 |
| `unknown` 投递 | 先看绑定聊天是否已收到，再决定重试，避免重复任务 |
| `WORKBENCH_BINDING_MISMATCH` | 工作台切换了任务；Agent 重新 context_select 后再读取，不改旧任务来消除提示 |
| `PREVIEW_LEASE_BUSY` | 自己的持久化写入结束后切 idle，保留 active 所有权；不解除别人的锁 |
| `CANDIDATE_CHANGED` | 核对本轮候选来源与预期，不默默接受新对象 |
| 回滚报 `LATER_EDIT_DETECTED` | 安装后有编辑。查看报告并保留改动；回滚不会部分覆盖后续文件 |

反馈问题时提供错误码、Doctor 中的阶段状态、Unity/插件版本和工程选择是否一致即可。安装回滚备份、API 设置、登录配置、任务图片都是本地私有资料，不要直接随发行包分享。

## 旧版操作锁

旧版桥接进程异常退出后会留下一个空的 `.dsh-operation.lock`，旧版因此持续报告 `ANOTHER_OPERATION_ACTIVE` 或 `ANOTHER_DSH_OPERATION_ACTIVE`。本版会自动处理：这个空文件存在超过 30 秒后，下一次操作（包括在工作台点“我来操作”）把它改名留存，然后照常继续。旧版一次写入只占用几毫秒，所以放了 30 秒的空锁不会有活着的持有者。

只有两种情况需要手动处理：旧版宿主仍在运行并不断重建这个文件（先关闭它），或者这个文件不是空的（工作台不会自动动它）。关闭正在访问该绑定的旧 Codex、DSH 写入进程后，在包目录运行以下命令。传入报错绑定的完整路径，自定义存储位置时同时传 `--storage`。

```powershell
.\runtime\python\python.exe .\unity\AvatarWorkbench\Tools\codex_feedback.py recover-legacy-lock --binding "本次任务的完整路径" --legacy-writers-stopped
```

命令将该绑定的旧空锁改名留存。租约、反馈和候选继续保留。新版进程异常退出后，下一个正常操作会自动接续。

`ANOTHER_CONSUMER_ACTIVE` 表示另一个会话的租约还没到期，报错里写着到期时间。那个会话已经停掉时不必等：在工作台点“我来操作”再点“交回 AI”，旧租约就释放了。租约记录损坏或缺少到期时间时按没有租约处理，不会一直挡住后续操作。
