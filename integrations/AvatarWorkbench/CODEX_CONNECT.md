# 接入原改模工作流 · 0.2.1

GUI 只维护自己的请求、反馈、选择和接口配置，不覆盖 QuickTask / 原任务状态。Skill、MCP、全局模型和权限沿用原配置。API 建议、商品说明和图片文字是数据，不授予工具执行或上传权限。

## 当前 Codex 直接发送

复用 Windows Codex 桌面应用原生本机管道：先 `codex_app.read_thread` 核对 local / Codex 会话，再 `send_message_to_thread`。不另开 Agent 或 codex exec。连接 / 发送时核对；接口失效则保留请求并显示未连接。

首次绑定由负责该工程的当前 Codex 读取自身环境 `CODEX_APP_TOOLS_PIPE_PATH`、`CODEX_THREAD_ID`，通过已配置 UnityMCP 调用以下方法；用户无需手填：

```csharp
using System;
using System.Linq;
public class EditorCommand {
    public static object Execute() {
        var api = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("AvatarWorkbench.WorkbenchApi"))
            .First(x => x != null);
        return api.GetMethod("BindCodexDesktop").Invoke(null, new object[] {
            @"<当前环境的真实本机管道>", "<当前真实会话 ID>"
        });
    }
}
```

`execute_editor_command` 必须明确指定已核对的 Unity 实例和非空 comment。通过反射访问 Editor 程序集，无需修改 MCP。实际会话验证成功才显示“可直接发送”。

发送先保存原反馈，再单独保存 `delivery/<id>.json`。桌面应用接受后标为 accepted，仅表示已发送；Codex 实际读文件才写 seen，实际回应才写 addressed。回执未知不自动重发；没有消费者时可复制接手指令或导出。

## API 与多模型

打开工作台左侧 **模型设置**（也保留 Tools 菜单入口），保存名称、根地址、协议、模型和自己的密钥。底部“模型”切换需求模型，也可指定独立识图模型。模型列表来自接口实际 GET；失败可手填，不凭名称猜测识图能力。

实现 OpenAI 兼容 `GET /models`、`POST /chat/completions`；Anthropic 兼容 `GET /v1/models`、`POST /v1/messages`。填根地址，/v1 不重复追加。仅远端 HTTPS / loopback HTTP，不跟随重定向、不自动换备用服务或重试。

每次发送实际读取选定的现有 `assemble-vrchat-avatar/SKILL.md`（最多 32 KiB），附本次原话、目标、候选、选定素材元数据、商品参考和已知开关。接口配置只作用于工作台，不改 Codex 全局配置，不安装 / 改写 Skill。

**API 只能答复和提出步骤，不执行 Unity 工具。** 允许识图时按冻结原图哈希读取 PNG / JPEG（最多 4 MiB）；可以先让独立识图模型描述，再交需求模型。未允许原图发送则只发文本上下文，不上传 Prefab / 材质 / 贴图文件。

回复写回原收件箱，记录 `model_response.workflow_sha256`、模型身份、image_sent，且 `model_modified=false`、`execution_started=false`。回复内“继续交给原改模流程”创建新反馈，保留原 target / candidate / image / rect / 原话，以 `api_review.parent_feedback_id` 引用建议；原反馈不改写，后续改模仍用当前安装的原 Skill。

请求固定 model_route 与端点身份哈希；改地址 / 协议 / 模型不会把旧请求转给新端点。旧图和素材不跟随后续操作改绑。配置在工程外的 `AvatarWorkbench/editor/<工程哈希>/model-services.json`；密钥仅会话保存，或使用 Windows CurrentUser DPAPI 加密，绑定接口 ID + 地址 + 协议。换地址不复用旧密钥。

任务、反馈和导出只含公开模型身份，不含密钥、加密密钥或 API 根地址。超时 / 关闭 / 重载保留请求，标回执未知，不自动重发。HTTP 错误不回显可能带密钥的服务端响应体。

## 实际读取与回应命令

使用现有 Tools/codex_feedback.py（Python 3.10+，标准库）。绑定路径从“更多 → 复制 Codex 接入指令”获取；未选任务时使用 selection.json。

```powershell
$bridge = '<工程>\Assets\AvatarWorkbench\Tools\codex_feedback.py'
$taskBinding = '<实际绑定路径>'
$sessionLabel = $env:CODEX_THREAD_ID

# 开始、续作或相关资源处理结束时只读一次
python $bridge inbox --task $taskBinding
# 实际读到原候选、原图及原话后才写已读
python $bridge ack --task $taskBinding --id '<真实id>' --status seen --session $sessionLabel --note '已读取原图与原候选。'
# 实际回应后才写 addressed；不代表修复通过
python $bridge ack --task $taskBinding --id '<真实id>' --status addressed --session $sessionLabel --note '<实际回应与未完成项>'
# 携带不变原图的私有导出，不放入公开插件包
python $bridge export --task $taskBinding --all --output '<私有目录>\feedback.zip'
```

开始实际改模前可 `connect --session <真实会话> --lease 300` 声明消费者，再 `busy --value true --lease 900`；结束 `busy --value false` / disconnect。租约到期显示无消费者。只有真实模型处理才用 processing_feedback；本次确认收到标记只回应。

UnityMCP 同样可反射调用 `WorkbenchApi.ReadPending(binding)`、`WorkbenchApi.Acknowledge(binding,id,status,note,session)`。现场处理结束可 `WorkbenchApi.NotifyTargetChanged(binding,targetGlobalId)` 通知一次；GUI 保留稳定预览，不抢写。回执检查只读当前绑定文件元数据，不高频唤醒 Codex 或扫描工程。

## 本轮接入结果

0.2.1 已在 Unity 2022.3.22f1 的新界面冻结真实画面、圈选并输入“确认收到界面标记，暂不修改模型”。关闭并重开窗口后，草稿、原图、框选、候选和所选素材恢复；通过真实发送按钮交给当前 Codex 桌面会话。当前会话实际收到、读取原反馈与图片，再回写 seen / addressed。Unity 的“需求与回复”展示了真实回应，970×456 停靠区仍可输入和发送。本次确认不修改模型。

0.2.0 已完成本机 OpenAI / Anthropic **协议测试服务**的请求及独立识图接入，并从 API 回复交回原 Codex。测试服务不是商业 AI；未使用用户密钥、未把付费素材发到外网。0.2.1 仅改界面，没有重复外部接口测试。外部商业接口认证未验证，独立 API 自动改模未实现，见 [VERIFIED.md](VERIFIED.md)。
