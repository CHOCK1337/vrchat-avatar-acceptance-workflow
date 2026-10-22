# 小工具只解决具体重复工作

Python 3.10+，标准库，无额外安装依赖。无需每个步骤调用工具。模型必须实际读原图/实际工具结果，本文件与脚本都不能代做视觉认证。

## 检查建议：只针对本次修改
`python <SKILL>/scripts/quickflow.py plan --input <request.json>`
最简 request：
```json
{"task_id":"t1","delivery":"local","resources":[{"id":"hair","kind":"material"}],"constraints":{}}
```
kind：outfit/accessory/plugin/material/behavior。衣服可填 region upper/lower/feet/full、真实启用body_controls、用户required_states、受影响parts。明确要求可写 `constraints.sdk_uncompressed_bytes`、`sdk_download_bytes`、`rank`、`metrics`，其他约束原样返回；不能根据它没解析就忽略。
`requested_method` 与资源 `suggested_route` 冲突会提示不要静默换法。所有输出是建议，不是执行证明/授权/完成。不要为填这个输入重扫全工程。

完整成品用 `operation: assemble`，明确优化用 `operation: optimize`，普通编辑默认 `edit`。plan 包含最终优化阶段；`optimization.mode: off` 和 `constraints.allow_sdk_build: false` 会保留用户限制。已有优化记录先读身份/回执，不重复派发。实际执行、SDK 输出读取与对比入口见 [optimization.md](optimization.md)，无需另一个任务 JSON 或新的 Agent。

## 避免重复观察
`python <SKILL>/scripts/quickflow.py reuse --input <probe.json> --history <observations.json>`
probe键：project、target、check、dependency_digest、state、view、pipeline_digest、check_version。dependency_digest 必须来自本检查完整相关依赖（包含body、menu/FX、材质与Provider需要的部分），不能只拿衣服名字代替；无法取得就不要缓存，做一次必要观察。
历史按时间旧到新，单项含 key、outcome observed_ok/observed_problem、observer、note、evidence[{path,sha256}]。key由`observation_key()`计算。原图丢失/改变或相关状态/管线变更就重新观察；相同失败不重复拍。用户再次指出问题时加`--user-reported-issue`。
结果 REUSE_SCOPED_RECORD 只表示记录和文件可复用，不能认证截图内容。旧图保持旧候选标签；经确定不变依赖复用时说明支持的是哪些局部结果，最终新默认画面仍须看。

## 回执很长/外层假成功
`python <SKILL>/scripts/quickflow.py receipt --input <actual-receipt.json>`
只在真实工具把大量结果保存在本地时使用。输入本次命令的回执，不把包含旧失败的整个历史日志作为当前成功/失败判据。输出 FAILED/PENDING/SUCCEEDED/UNKNOWN 说的是执行回执；不是模型通过。已经进模型上下文的日志不能靠再跑摘要追回Token。

## 少量续作记录（不做完整合同校验）
`python <SKILL>/scripts/quickflow.py save --task <PROJECT>/.avatar-assembly/<id>/QuickTask.json --patch <delta.json> --expected 0`
首次至少包含真实 task_id 与 project_path。后续expected用当前task_revision，顶层合并保留未改字段。候选和约束只因真实用户指令或实际结果改变，不靠摘要重写。
`python <SKILL>/scripts/quickflow.py summary --input <QuickTask.json>`
记录更新仅在自然边界，不在每个工具调用后跑。旧 AssemblyTask 不接受直接输入或覆写；read-only读取已有用户要求，再单独建立小任务引用原路径，见包根MIGRATION。
辅助工具失败返回HELPER_NOT_APPLIED，不修改Unity。继续用原始少量回执/短记录即可，切勿现场开发一个新验证器。显式要求和实际已知问题仍然有效。
