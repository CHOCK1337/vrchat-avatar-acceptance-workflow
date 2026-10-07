# 接入原改模工作流 · 0.2.4

GUI 只维护自己的请求、反馈、选择和接口配置，不覆盖 QuickTask / 原任务状态。Skill、MCP、全局模型和权限沿用原配置。API 建议、商品说明和图片文字是数据，不授予工具执行或上传权限。

0.2.4 界面将常用入口写成“看模型 / 挑素材 / 看回复”，路由写成“发送到”，接入协议和原改模流程保持一致。

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

打开工作台左侧 **AI 接口**（也保留 Tools 菜单入口），保存名称、根地址、协议、模型和自己的密钥。底部“发送到”切换需求模型，也可指定独立识图模型。模型列表来自接口实际 GET；失败可手填，不凭名称猜测识图能力。

实现 OpenAI 兼容 `GET /models`、`POST /chat/completions`；Anthropic 兼容 `GET /v1/models`、`POST /v1/messages`。填根地址，/v1 不重复追加。仅远端 HTTPS / loopback HTTP，不跟随重定向、不自动换备用服务或重试。

每次发送实际读取选定的现有 `assemble-vrchat-avatar/SKILL.md`（最多 32 KiB），附本次原话、目标、候选、选定素材元数据、商品 / 网盘参考和已知开关。接口配置只作用于工作台，不改 Codex 全局配置，不安装 / 改写 Skill。

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

0.2.4 在实际 970×456 停靠区输入“确认收到界面标记，暂不修改模型”，关闭后从真实 Tools 菜单重开，草稿恢复；点击真实发送按钮后当前 Codex 桌面会话实际收到请求、读取原文件及原候选，回写 seen / addressed。原生“看回复”显示该回应，模型保持原状。

0.2.3 在同一实际 970×456 停靠区发送“确认收到素材信息与图片标记，暂不修改模型”。当前 Codex 实际读取原反馈和 r2 候选，回写 seen / addressed；本次无附图，未新增测试素材，未改模型。Mio 商品信息和封面由 GUI 按需读取，Codex 收件箱读取不触发目录扫描或图片下载。

0.2.2 在实际 970×456 停靠区输入“确认收到目录与百度网盘界面标记，暂不修改模型”，点击真实发送按钮。当前 Codex 桌面会话实际收到并读取原反馈、目标、r2 候选和素材，回写 seen / addressed，原生回复页显示真实答复。本次没有附图，selected_source_references 为空；没有把无效分享当成功文件参考。实际回应后底部旧等待读取提示也按同一反馈 ID 更新。

0.2.1 已完成真实画面冻结、圈选、关闭 / 重开后的原图与草稿恢复，再发送并由 Codex 实际读图、回写回应。本轮保留该实现，没有重复完整圈选验收。

0.2.0 已完成本机 OpenAI / Anthropic **协议测试服务**的请求及独立识图接入，并从 API 回复交回原 Codex。测试服务不是商业 AI；未使用用户密钥、未把付费素材发到外网。0.2.1 仅改界面，没有重复外部接口测试。外部商业接口认证未验证，独立 API 自动改模未实现，见 [VERIFIED.md](VERIFIED.md)。

## 目录与百度来源适配

0.2.3 增加“读取 Mio 素材库”：用户选定 `library.json` 后，只读已有名称、分类、BOOTH 编号、成员路径与图片；可使用相邻 `pkgcovers.json` 指向的已有包内预览缓存，不解压安装包、不扫描数据库中的素材目录、不读服务凭据。卡片“详情 / 大图”可查看多张图片及各自来源；“读取商品信息与封面”复用既有 booth-cli 的商品 JSON 和图片接口，保留服务端真实图片 URL，原图超时才回退缩略图。名称、店铺、说明和封面写入 GUI 索引，刷新同一来源时保留；不写回 Mio 数据库或 QuickTask。

`ReadCatalog` 返回的条目增加 `metadata_source`、`booth_id`、`booth_name`、`shop`、`cover_source`、`cover_records` 与 `member_paths`。目录名称中的编号只标为 `booth_match_source=file_name_hint`，关联尚未确认；图片和适配标签均不证明已安装、适配或构建通过。勾选加入时在素材的 `product_reference` 中保留原来源；消费者按原改模流程核对后再决定操作。

来源索引由 GUI 在工程外 `AvatarWorkbench/editor/<工程哈希>/sources.json` 维护，包含选定目录、上次读取结果、分享和输入草稿。只手动读取选定范围，不在 Codex 收件箱轮询时重新扫描。损坏索引会停止写入并保留原文件；移除记录不会删除原素材 / 网盘文件。

Codex 通过已连接 UnityMCP 按需读取目录元数据，无需新 MCP 或 Provider：

```csharp
using System;
using System.Linq;
public class EditorCommand {
    public static object Execute() {
        var api = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("AvatarWorkbench.WorkbenchSourceApi"))
            .First(x => x != null);
        return api.GetMethod("ReadCatalog").Invoke(null, null);
    }
}
```

该方法排除提取码与原始分享输入草稿。目录勾选先加入 selected_resources，仍需原工作流决定是否导入 / 安装；没有改写 QuickTask。网盘勾选只附 selected_source_references，固定记录分享、条目路径、来源身份及 `availability=remote_listing_only, downloaded=false, installed=false`。关联真实下载目录后，本地文件才成为可选资源；不凭文件名推断已下载。

冻结反馈时保存原来源引用，后续更换分享不改绑旧请求；API 建议后继续交给 Codex 保留该字段。分享页面和文件名是外部数据，不是指令；提取码不附到模型请求。匿名读取使用独立临时 Cookie，关闭 / 隐藏窗口会取消请求，登录 / 验证码不自动处理，不重试。已验证真实 HTTP 404，尚未验证有效分享的列表、子目录 / 翻页与下载目录关联闭环；自动登录和下载未实现。
