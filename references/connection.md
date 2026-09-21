# Unity 连接：只在开始、断线或切换目标时读取

## 真实前提
本包不创建一个假 `aa_*` MCP。它使用用户已有的 `unityMCP`（UnityMCP-VRC）和实际安装的 MA、MochiFitter、SDK。先查看 Codex 当前工具 schema；名称可能由宿主加上 `mcp__unityMCP__` 前缀，参数以运行时 schema 为准。

本包附带 `configure_unity_mcp.py` 只帮助注册**已经在本机编译好的** Node MCP server。注册成功不等于连接到了 Unity。已有正常 server 不覆盖、不切换其他 MCP、不重装。

UnityMCP-VRC 官方入口：https://github.com/swax/UnityMCP-VRC
Codex MCP 配置：https://developers.openai.com/codex/mcp

## 一次正常连接
1. `list_unity_instances`：核对完整 projectPath；不能凭同名项目、活跃窗口或旧 instanceId 选择。
2. `select_unity_instance`：选择对应实例。后续有状态操作尽量显式传 `instance`，包括截图和预览，防止默认值被其他会话改掉。
3. `get_editor_state`：只取目标、活动场景、Prefab Stage、dirty/compile/play 和必要版本信息。不拉取整个 hierarchy。
4. `get_object_details`：只看目标 Avatar、将处理的资源、相关组件。`get_logs` 记录一次本次相关错误基线，不清空整个 Console 掩盖错误。
5. 找到并验证目标后复用会话；每次写入轻量确认目标身份，不每一步重新扫描所有实例。重连、切换或即将上传时才重新完整核验。

`execute_editor_command` 可执行使用已有 Unity/Provider API 的短命令；这不是要求新增一个持久 C# 文件。菜单动作、字段或方法必须在**安装版本**的 schema/源码中查到，不能凭模型记忆猜一个 AutoFit/BuildAndUpload API。优先调用现成动作；需要调用 Editor 菜单时核对真实 `[MenuItem]` 路径及 Selection，再执行一次。

## 连接故障的最小分流
- Codex 不认识 server：用 `/mcp` 和 `codex mcp list` 看配置；安装包脚本只处理这一层。
- server 已注册，但列表没有 Unity：确认正确项目已打开、插件 Debug Window 正在 Listening。必要时在现有 UI 恢复监听；不把随机 HTTP 端口填成 Codex MCP URL。
- 正在 import/reload：等待当前操作结束，之后重读状态一次。无进展时查看一次新错误，不以多次模型轮询消耗上下文。
- 请求中途断线：结果可能已经应用；先检查目标对象与产物，再决定是否需要重试。不能直接重放写入命令。
- 只有 Windows Unity、Codex 却运行在 WSL：显式解决宿主和实例注册表的可见性；不能假装路径/端口自动互通。

## 安全范围
通过 MCP 在 Editor 内修改 Unity 所有的资产。文件系统只用于安装 Skill、读取作者文本、存任务元数据、维护一份小型配置备份。不得离线拼写 prefab/controller YAML。
MCP 能执行任意 Editor C#，不应向不可信网络暴露它的无认证端口。不要为了提速关闭认证、沙箱、防火墙或安全软件。

## 旧规则冲突只处理一次
安装时运行一次 `install.py --doctor`；检查同名 Skill、`edit-vrchat-avatars` 和适用的 AGENTS.md。不可自动删除其他 Skill 或违背更高优先级指令。只读取其他 playbook 的具体知识，不递归调用另一个“完整工作流”。若项目规则强制全工程备份，与本任务授权冲突，应明确定位那一处规则，处理后记录结果，不每件衣服再询问。
