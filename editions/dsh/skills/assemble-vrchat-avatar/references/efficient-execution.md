# Low-I/O 执行适配（本地试用 v1）

**只改变信息搬运和重复等待，不改变现有准入、胸部状态、菜单、体积、性能、授权或验收规则。** 本次保留原有判定，在 intake/final 追加已确认选择和复核记录检查；task_store 与学习实现不变；主入口的批量安装→冻结→一次统一验收保持不变。此文件不能把 unknown 改成通过。

## 正常任务：三个接入点

### 1. 开始/续作：读小摘要，不反复输出整个 AssemblyTask

```powershell
python <工作流目录>/scripts/context_packet.py resume .avatar-assembly/<task-id>/AssemblyTask.json
python <工作流目录>/scripts/context_packet.py field .avatar-assembly/<task-id>/AssemblyTask.json /start_contract/size_budget
python <工作流目录>/scripts/context_packet.py field .avatar-assembly/<task-id>/AssemblyTask.json /resources/0
```

路径要替换成本次真实路径；基础 Low-I/O 工具 Python 3.10+；自动 TOML 配置需要 Python 3.11+，仅标准库。摘要保留全量文件地址、内容哈希和被省略字段的指针。**摘要不是验收器、不是完整权限合同**。第一次准备写入、合同改变或暂停恢复时，读相应原合同字段；确认过且哈希未变，不再让用户重答起始卡。实际修改仍通过已有入口，不能直接覆盖旧状态或自造 `LOCAL_BUGFIX_COMPLETE` 来绕过状态机。

`start_card` 和 `current_candidate` 不是 `start_contract` 与正式候选字段的自动别名。发现旧格式只标 `LEGACY_OR_INCOMPLETE`，利用现存事实进行一次明确映射，缺失观察维持缺失，不能自动补 confirmed / PASS / receipt。父会话只在阶段切换运行原判定器一次：`intake → preflight → batch-ready → final`；上传前按既有授权再检查 upload。不要在每个对象后跑全部四项。`workflow.py` 进程返回0不代表决定可继续，必须读取其 status / blockers。

当前需求、平台、尺寸口径和大小上限冻结在起始卡。后续更改视为 scope delta，不能默默把400MB改300MB、解压大小换成下载大小、同步参数改成本地。只让受影响项失效，不重开整个历史任务。

### 2. 工具运行：小回执 / 明确失败 / 固定等待

大型扫描在 Unity/执行端把完整数据写到任务目录，返回 `path + hash + count + changed IDs + errors`；模型只读取需要的 JSON Pointer。**必须在工具返回给模型前投影**；把已经打印的5MB日志再总结一遍不能省已消耗的输入。

现有保存的工具回执可这样检查：

```powershell
python <工作流目录>/scripts/context_packet.py receipt .avatar-assembly/<task-id>/receipts/op-001.json
```

外层 MCP `status=success` 只说明运输/封装成功。内层 `executionSuccess=false`、非零 exit code、非空 errors 都优先视为失败。截断结果不能据此宣布成功。**回执成功也不是视觉成功**。

动态 C# 查询不再每次重新探测整套包：按 Unity / SDK / Provider 版本缓存已经成功的最小查询模板和字段。明确静态依赖不存在或字段类型错误，先读取精确 API/组件结构，不用同一巨型命令换几个名称重试。发生运行异常的 mutation 不等同于零改动：先核对文件哈希和生成物再决定回滚/重试，不盲目再做一次。

构建/导入采用已有 job_id，只有实际已有后台工作者写回 JSON 的路线才能使用：

```powershell
python <工作流目录>/scripts/job_wait.py .avatar-assembly/<task-id>/jobs/final-PC.json --expected .avatar-assembly/<task-id>/jobs/final-PC.identity.json --timeout 120 --interval 8
```

expected 文件写入调用方已知的 job_id、candidate、candidate_revision、candidate_sha256、platform、sdk_version，不能从轮询结果中临时决定预期对象。`revision` 仅是已知旧回执字段的读取别名，若两种字段冲突会拒绝。嵌套 job 可用 `--pointer /performance_measurement_jobs/PC`。

脚本只在本地进程等待、最后输出一次，不启动 SDK、不重派、不取消 SDK。`WAIT_WINDOW_ELAPSED` 是等待窗口结束，不是构建失败；下一次接同一进程或同一 job。MCP/终端已返回长任务句柄就优先等待该句柄，不另开 job。receipt 不更新且 SDK 空闲时，检查一次真实工作者是否启动；不无限等待假 job，不因为超时换 job_id。取消文件只停止等待，用户取消上传还要由原唯一写者停止实际操作。

### 3. 统一验收：先按用户选择，重用原图

```powershell
python <工作流目录>/scripts/preview_html.py .avatar-assembly/<task-id>/review/manifest.json --task .avatar-assembly/<task-id>/AssemblyTask.json --source-root .avatar-assembly/<task-id>/captures --out .avatar-assembly/<task-id>/review/r2
```

必须先有明确 HTML 同意；不同意则以上命令加 `--index-only`，不产生 HTML 或重复图片。详见 html-review.md。

manifest 示例在 `examples/lowio/review-manifest.example.json`。每张已有图片必须绑定：资源、具体状态/视角、candidate+revision+SHA+platform+SDK、图片 SHA、采集方式和观察状态。路径在 source-root 下，不打包作者原始素材。

- `actual_control`：经最终菜单/Animator/实际控制链操作得到的画面；仍需真实观察，无图册自动判定。
- `diagnostic_direct`：手工 SetActive、SetBlendShapeWeight、禁用 Animator 后摆出的画面；只用于外观定位，不证明开关或最终 FX 正常。
- `reference`：旧版、商品或参考图，不进入当前验收。
- `NOT_REVIEWED`：尚未观察；生成图册、图片存在、数值符合预期都不能改成 OBSERVED_OK。

候选身份不同自动分到历史/参考；哈希不符列为无效，不偷换 r1 图成 r2。图册使用相对文件，直接打开 HTML，点击图片打开原分辨率；不依赖 file://跨盘绝对路径或另写服务器。无需重新截图来生成 HTML。

所有入选内容先批量串行安装，只做灾难性 smoke。冻结候选后完成原定统一验收，包含每件需要的胸部small/default/large、必要菜单/衣片恢复和真实性能/尺寸。新插件改变某条链就重新收集那条链的证据；**本补丁不自动把旧 SHA 的验收带到新 SHA**。其他已做过的测试是否可复用，要由已有的依赖/证据机制明确证明，不能由模型口头宣布。

衣服/遮罩变更优先观察受影响的默认身体、对应开关和恢复动作，尽早发现明显腿变细/缺块；随后仍完成合同约定的统一验收，不把此默认检查当最终全状态通过。

## 控制模型和总结开销

- 单模型或协作由开工卡确认。单模型保持当前型号；协作只调用已确认角色/档位，不自动升档，不固定 Luna/max。
- 原生/Profile明确路线不用额外专家；范围小且独立的不确定判断可以给一个限定专家；已分配的文件任务可在 job/work 真实修改，主控核对后合入。其他任务返回决定、证据指针和缺失能力；不转发整段历史、不现场研发新算法。
- 复杂所有权冲突、未知脚本、重复失败才升级到更强模型。换模型不重置重试预算、不重复扫整个项目。
- 维护固定工具schema和说明；新增动态数据放后面。不要每回合为了缩短字数重写历史上下文，实际缓存命中需usage验证。
- 学习保留纠正当时的增量记录；一次自然任务/故障闭环边界合并一次。没有 pending 不调总结模型，不为学习截图、测试或扫描。
- 会话异常中断后先读 packet+当前 job+最新真实候选，不从长聊天重建计划。

## 非正常装配的一次性开发测量

`session_meter.py` 仅用于维护者离线对导出做统计，不是新的验收步骤。字符数、编译错误文本出现次数与嵌套工具名出现次数均是代理指标，不是计费 Token 或独立失败次数。

```powershell
python scripts/session_meter.py <session>/operations/operations.jsonl --metadata <session>/export-metadata.json --out metrics.json
```

同一干净输入、同一资源集合、同一合同/模型/版本，分别跑原版与改进版；比较真实完成范围、视觉缺陷、用户纠正次数、有效工具调用、active耗时与实际usage。实际usage不可获得则留空，不能把字符除4充当价格。不要在用户任务里为了做对照额外跑一遍全流程。
