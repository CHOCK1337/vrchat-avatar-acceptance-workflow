# MCP、配置与目录

## 随包组件

| 路径 | 内容 |
|---|---|
| `unity/AvatarWorkbench` | Unity Editor 工作台 0.2.12-dsh；安装至工程 `Assets/AvatarWorkbench` |
| `dependencies/UnityMCP-VRC` | 固定快照的 UnityMCP Editor 插件、Node 源码与许可 |
| `runtime/unity-mcp/build/index.js` | 已编译 Unity MCP；生产依赖已带入，不在用户机器运行 npm install |
| `adapter/workbench_mcp.py` | 工作台上下文、目录、任务、冻结图、反馈与单写入租约；标准库 Python |
| `runtime/node/node.exe` | 便携 Node 24.15.0 |
| `runtime/python/python.exe` | 便携 Python 3.13.16，隔离路径，不使用用户 site-packages |
| `runtime/setup` | 配置校验用 js-yaml 4.3.2 |
| `skills/assemble-vrchat-avatar` | DSH 改模工作流、参考文档、真实预览 API、任务助手 |
| `tools` | 安装、诊断、校验、回滚、维护者运行时构建脚本 |

## 安装生成的配置

安装器按解压后的真实绝对路径生成 `local/connection.json`、`local/dsh-overlay.yml`、`local/mcp-servers.json`。发布 ZIP 中没有这些个人文件；[config 目录](../config/README.md) 只有标明占位符的模板。

默认 DSH profile 是 `%USERPROFILE%\.dsh\profiles\desktop\cordis.patch.yml`；设置了 `DSH_HOME` 时优先使用它。特殊安装位置用 `Setup.ps1 -DshHome "实际目录" -Profile "实际profile"`。

仅写入两个标记之间的三个固定接入 ID：

```text
# BEGIN AVATAR_WORKBENCH_DSH_V1
avatar-workbench-skill       → @deepseek-ai/dsh-skill-filesystem
avatar-workbench-mcp         → @deepseek-ai/dsh-mcp-client / avatarWorkbench
avatar-workbench-unity-mcp   → @deepseek-ai/dsh-mcp-client / unityMCP
# END AVATAR_WORKBENCH_DSH_V1
```

原 profile 的其他字节、模型选择、权限和凭据引用保持原样。未标记的重复 ID/同名服务会拒绝自动安装，按故障文档合并已有项。`!<tag:yaml.org,2002:js>` 在校验时只解析成数据，不执行。不要将整个模板覆盖个人配置。

另外只写所选工程的工作台 `model-services.json` 中 `dsh_skill_path`。首次创建该文件设置 `active=dsh`，已存在则保留用户原宿主与 API 配置。`local/rollback` 可能保存个人配置副本，**不要把安装后的 local 目录转发给别人**；再分发用原始干净 ZIP。

## 两个 MCP 的职责

| 服务 | 工具 |
|---|---|
| avatarWorkbench | context_select、workbench_status、catalog_search、catalog_cover、task_read、feedback_read、feedback_image、feedback_ack、consumer_lease |
| unityMCP | list_unity_instances、select_unity_instance、get_editor_state、execute_editor_command、get_logs、clear_logs、take_screenshot、get_object_details、get_command_page |

两者均为本地 stdio 进程，由 DSH 启停。Unity 侧是本机动态端口，实例记录位于用户本地 UnityMCP 目录；不用填写固定 8080 或 59119 端口。多工程时使用实例 ID 与完整路径匹配，不按相同角色名猜。

工作台的 `context_select`、已读反馈缓存属于当前 MCP 长连接。分开执行几个一次性 CLI 不能冒充同一会话。`feedback_image` 需先在同进程 `feedback_read`；ack 还需记录未变化且有真实会话 ID。`addressed` 是答复状态，不是外观验收。

## 诊断边界

`Doctor.cmd` 会启动真实 MCP 进程、列工具、读路由、寻找所选 Unity 工程并只读检查工作台程序集。不会改场景或调用付费模型。结果保存在 `local/doctor-result.json`。

`CONNECTION_READY` 不表示 DSH 的当前模型已经调用过 MCP；最后仍需在新 DSH 会话按 FIRST_TASK 让它真实读取。新账号、新机器的模型调用和 Avatar 适配不能由离线工具包预先保证。
