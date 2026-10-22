# Codex 执行补充 — 0.3.2-local5-codex1

先核对源、处理后候选、实际预览身份；只对当前请求的副本操作。安装与协议握手通过不代表服装、腿部或动态已通过验收。

本补充处理宿主、路由和检查能力；不改变 routes/execution/review/optimization 的改模方法。工具加载后应有 9 个 avatarWorkbench 工具，仍使用已有 unityMCP。不要把一个模型名称或工具列表当成质量保证。

## 锁定本次任务，拒绝读错收件箱

1. workbench_status 查看配置上下文及 routing_issues；list_unity_instances 查看实际实例。
2. 用户指定的工程/Avatar 与配置不同时，用 context_select(project=精确工程路径)；它仅改变当前 MCP 进程，读取该工程现有 window.json 的 task_path，无任务则选择同工程 selection.json。不会重写 profile/任务/旧图。
3. 用户明确要求一个已有任务时可传 binding；容器数据根与普通桌面数据根不同时，先在 Unity 读取 WorkbenchData.DataRoot，再用 storage_root 显式选择同一根。不要跨两个数据根复制/消费反馈来掩盖不一致。
4. routing_ready=true 后读取 task_read 和未处理 feedback_read；选择精确 Unity 实例。任务被替代、任务工程不符或已选窗口绑定改变时，写操作会被拒绝；重新检查并选定，不绕过。
5. 切换上下文会清除已读哈希缓存；需要重新读本空间反馈。持久化修改前租约 connect/busy。临时换装/姿势/等待/截图前本会话改 idle，保留 active 排他所有权；busy 会禁止换装和暂停动画。结束 disconnect；断线后核对实际部分结果，不盲重派。

工作台实际试穿先读 [preview-api.md](preview-api.md)，使用已验证的 inspect/select/capture 小入口。它返回候选身份、具体锁定原因、实际可见衣片及原图，仍须打开 PNG 才能记录视觉判断。不反复猜私有 API，不用 clear_logs 修异常。

按本次需求和最后有效纠正更新 QuickTask 的当前要求；旧完整提示词只作来源。历史任务不能自动作为新操作清单。例如用户已经替换某插件，就不能按旧导入列表又装回来。用户要求身体网格替换时，必须识别源 Mesh/骨架和实际候选 Mesh；仅换贴图不能写移植完成。

## 图像输入要实际验证

- 冻结反馈：feedback_read → feedback_image(id)。适配器校验原记录和原图哈希，返回原像素和原候选身份，不自动 ack。
- 当前现场：使用已有 unityMCP take_screenshot 或已验证的真实预览入口。查看实际图像，指出本次相关部位；不用统计替代观察。
- 如果当前 Codex 模型/传输链不提供像素输入，记录 visual_checked=NOT_RUN。说明缺的是当前图像通路，不伪造看图，也不未经用户选择自动换模型。
- 正常中性光照、正确候选、完整构图。编辑态、NDMF 预览、烘焙输出和客户端分别标注；截图存在不证明操控有效。

## MA 互斥组的小检查

只在新增/修改了衣柜、发型互斥或相关菜单时使用。按本次目标和真实参数生成查询：

```powershell
python <本包>/skills/assemble-vrchat-avatar/scripts/emit_ma_probe.py --avatar "<精确Avatar路径>" --exclusive-parameter "<本次服装参数>" --exclusive-parameter "<本次发型参数>" --menu-root "<相对Avatar的菜单根>" --outfit-root "<受影响的衣服根>" --body-renderer "<相关身体Renderer路径>"
```

将 stdout 的完整 C# 传给同一实例的 unityMCP execute_editor_command。不要把 .cs 导入 Assets；不要启动另一个 Unity。可以只传实际受影响的一组参数，省略无关发型/衣柜。

查询检查源配置并返回精确对象路径：
- Menu Installer 有资产或同对象的 MenuSource（Menu Item/Menu Group）。menuToAppend 为空不自动等于错误；先查真正失败对象。
- 本次互斥组有默认项或明确参数默认值；多默认、无对应选项的默认值单独报告。
- 同组每个 Object Toggle 覆盖组内完整目标集合；后来增加的服装/发型也要纳入原版与其他选项。
- Children 子菜单有正确孩子；空同名节点和错误的兄弟关系需要按实际菜单意图判断。
- 指定 --outfit-root 时报告真实 Renderer、可用网格、Prefab 来源及该范围的 MA Shape Changer 目标；OUTFIT_HAS_NO_MESH 要追配套衣片/真实生成输出，不能把辅助 Prefab 当服装。只传服装根，不把灯光/菜单插件当服装。
- 指定 --body-renderer 时报告准确身体 Mesh 及非零形态值；不按名称猜体型/遮挡语义，不把非零值本身当错误。
- 形态目标未解析、目标不属当前 Avatar、形态不在目标 Mesh 或字段版本不支持均单独报告。相对路径解析不调用会改变组件缓存的 Get()。
- 未传新增参数时保留原有默认/互斥检查；查询不做拟合、不烘焙、不更改状态，不能判断动画恢复和可见外观。

probe 的 issue 是定向调查线索：TOGGLE_CONSUMER_NOT_FOUND 可能由 Animator/其他 provider 合法接管，需查真实消费者。预先存在的错误不能直接归因于本次改动。unsupported 必须查已安装版本，不能当零项通过。

修正源配置后，在任务允许的现有预览/NDMF 流程中核对默认外观与合并菜单。对本次新增/受影响的每套衣服或发型实际选择一次，再回默认；测 A→B→A，部件有状态依赖时补部件关→换装→回切。保留作者部件控制，不把整套互斥误当独立部件功能。不得用直接设置参数替代最终菜单可达性验证。禁止构建时停在允许的证据层并说明未验证项。

## 最小验收记录

在当前 QuickTask 中增量记录，不覆盖用户字段，不另造大型审计工程：

```json
{
  "validation": {
    "candidate": {"id": "实际候选", "revision": "实际版本"},
    "preview": {"source_avatar": "实际源", "processed_candidate": "实际处理后候选", "displayed_origin": "预览所用来源"},
    "installed": "PASS",
    "source_checked": {"status": "PASS或FAIL", "receipt": "实际查询文件"},
    "provider_checked": {"status": "NOT_RUN", "layer": null},
    "control_tested": {"status": "NOT_RUN", "operations": []},
    "visual_checked": {"status": "NOT_RUN", "image": null, "observations": []},
    "sdk_build": {"status": "NOT_RUN"}
  }
}
```

状态可用 PASS/FAIL/NOT_RUN/NOT_APPLICABLE；不适用需写理由。只有对应层真的运行并观察过才改 PASS。记录操作路径、状态与实际看到的结果；模型自述、脚本检查和 addressed 不能认证视觉通过。没有请求客户端/网络验证就如实写未测，不扩大测试。重用同候选、同相关依赖、同状态和同视角的真实证据；修共享默认/控制链后重新看一次相关默认画面。

## 当前 Codex 会话绑定

只在用户要求绑定本会话时操作。读取当前宿主提供的真实 `CODEX_APP_TOOLS_PIPE_PATH` 和 `CODEX_THREAD_ID`，先匹配 `list_unity_instances` 的精确工程，再通过 UnityMCP 反射调用 public `AvatarWorkbench.WorkbenchApi.BindCodexDesktop(pipe, threadId)`。C# 字符串使用 JSON 序列化转义；不得把用户文本拼成代码。该入口返回 `binding_pending`，不是已连接；等待 Editor 正常更新，再从同工程 `codex-link.json` 的 verified_at 和工作台实际连接状态核对。不得借用别的会话 ID、伪造管道或修改 Codex 全局配置。

只有用户点击工作台“发送”提交的需求才能送至授权绑定的会话。绑定本身不发送测试消息。管道不可用时，把工作台真实交接文本粘贴到当前聊天继续。CLI 可以消费 MCP 和手动交接，但不承诺桌面管道直送。

收到需求后 `feedback_read` 并打开 `feedback_image`；读取成功才可 ack seen。`consumer_lease` 的 session 必须是真实当前会话标识。需要改资源时 busy，临时预览与截图时 idle，结束 disconnect；不解除别人租约。仅有通知或 accepted 不能 ack addressed。处理结果须明确可见结果、未测层级和候选身份。
