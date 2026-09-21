# VRChat Avatar 性能合同与优化

## 性能不是最后看一眼 Rank
性能与体积在开工卡第 3 问一起冻结，但两者是不同的门。`size_budget` 保存用户任务的精确口径；`performance_contract` 保存平台、策略、目标、允许动作、受保护功能、余量和证据要求。不得用官方上传上限替换更严格的任务门。例如 `SDK Uncompressed Size <= 300,000,000 bytes` 比 PC 官方 500,000,000-byte 上限严格，两者都要分别通过。

`assembly_manifest.target_platform` 来自只读发现，只能列 `PC`、`Android` 或两者。`assembly_manifest.sdk_context` 必须同时保存当前 VRCSDK 精确版本、`authoritative: true`、非空发现 source 和已观察的结构化 evidence；该只读发现才是版本锚，不能信任 performance contract 自报“current”。双平台可以复用同一 source candidate/revision/SHA；强制独立的是每个平台的正式 job、build、SDK 报告、size evidence 和结论，PC 报告不能让 Android 通过。若某平台使用不同的 override candidate，则该平台还必须有绑定该 override candidate/revision/SHA 的资源、视觉、行为与菜单验收。

## 当前官方边界与证据权威
以下 `PLATFORM_BUNDLE_LIMITS` 是 2026-09-20 核查快照，不是会自动永远正确的动态值；安装中的当前 VRCSDK Avatar Performance/Build Report 始终是执行时权威。如果 SDK 报告与此快照不一致，必须 fail closed、刷新规则和合同后再继续，不能选择对自己有利的旧数字。

| 平台 | Download Size | Uncompressed Size |
|---|---:|---:|
| PC | 200,000,000 bytes | 500,000,000 bytes |
| Android | 10,000,000 bytes | 40,000,000 bytes |

Android/Mobile 的硬组件上限：PhysBone Components 8、PhysBone Transforms 64、PhysBone Colliders 16、PhysBone Collision Checks 64、Contacts 16、Constraints 150、Constraint Depth 50。客户端会移除超限组件，因此任何一项超过即 `MOBILE_COMPONENT_LIMIT_EXCEEDED`，不能靠用户接受 VeryPoor 绕过。

所有平台另有不可由任务合同放宽的 SDK 硬限：synced expression parameter bits 256、VRCPhysBone 256、VRCPhysBoneCollider 256、VRCContact 256、VRCRaycast 80。最终 snapshot 先使用 Android 更严格的对应限制，再检查这些全平台硬限；超过返回 `HARD_COMPONENT_LIMIT_EXCEEDED`。合同里 `headroom_policy.*.limit` 必须等于该平台当前官方硬限，用户只可调整大于 0 且小于 limit 的 `reserve`。依据是官方 [Animator Parameters](https://creators.vrchat.com/avatars/animator-parameters/)、[SDK 3.2.2 release](https://creators.vrchat.com/releases/release-3-2-2/) 和 [Performance Ranks 的 VRCRaycast 脚注](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)；这些同样属于 2026-09-20 快照，当前安装 SDK 若显示不同则 fail closed 并刷新规则。

Performance Rank 统计禁用的 GameObject/Component。它是静态复杂度分析，不等于 FPS、VR 帧率或特定设备体验。可声明的 target Rank 只限 `Excellent`、`Good`、`Medium`；目标推荐 `Good`，`Medium` 可接受。`Poor`/`VeryPoor` 只能通过冻结的 `rank_handling` 选择 `block` 或 `accept_with_warning`，不能成为可宣称达标的目标。任何 policy mode 的实际 Rank 为 Poor/VeryPoor 时都必须写 `target_met: false`；`metric_caps` 可以另报逐平台 `caps_met: true` 并按冻结 handling 交付，但不能把“caps 合同通过”泛化为“整体性能目标已达到”。

Build & Test 不执行正式上传 size limits，也不能替代当前 SDK Avatar Performance 报告或 SDK Build Report。最终平台 size 记录必须来自 `current_sdk_build_report`，Rank/指标来自 `current_sdk_avatar_performance_report`。这两类 evidence item 都必须显式记录 platform、candidate/revision/SHA 和 SDK version；同一 report source 默认不得跨平台复用。

## 第 3 问要冻结什么
合同至少包含：

- `policy_mode`: `measured_best_effort`、`rank_gate` 或 `metric_caps`；
- `platforms`，以及每个平台至少一个只取 `Excellent/Good/Medium` 的 `target_rank` 或非空 `metric_caps`；`measured_best_effort` 不能因 caps 达标把实际 Poor/VeryPoor 算成 `target_met: true`；
- `rank_handling` 中 Poor/VeryPoor 的明确处理；
- `gate_completion`、`gate_upload`；
- `allowed_actions` 与 `protected_features`；
- `headroom_policy`，至少覆盖 expression parameter bits 与 PhysBone Components；
- `baseline_measurements`、`final_measurements`、`evidence`；进入 `INSTALLING` 前，baseline 已按每个平台完成当前 SDK Avatar Performance/Build Report，并保存 `measured_utc`、`frozen_at_phase: PREFLIGHT` 与覆盖身份、SDK、时间、两类 report source 的 canonical SHA-256 receipt；final 可等批量安装完成后再生成；
- PC/Android 官方 bundle limits 与当前 SDK 来源。

默认 PC 余量建议把 expression parameter bits 和 PhysBone Components 都按固定 limit 256、reserve 16 规划；Android PhysBone Components 按固定 limit 8、reserve 至少 1。`limit` 不是用户偏好或可覆盖字段；任务只能明确调整 reserve，但不能为 0，也不能把 253/256、256/256 或“AAO 后刚好压线”称作有余量。触发合同余量门时返回 `PERF_HEADROOM_EXHAUSTED`。

## 每件资源的导入前记录
每个选中资源（含插件、附件和之后会被拒绝/隔离的候选）都写 `preflight.performance_impact`：

- 已验证 `source_inventory`，只统计选中的 Avatar/色款/平台和必需依赖；
- `estimate`: added meshes、material slots、triangles、texture memory；
- dynamics: PhysBone components/transforms/colliders/checks、Contacts、particles、audio；
- dependencies、mitigation、admission；
- `headroom_after_estimate`：每个平台加入后的 expression bits/PhysBone 剩余量、是否满足冻结 reserve 和累计预算来源；`ADMIT` 不得与 `meets_frozen_reserve: false` 同时出现。`admission: REJECT` 的 required 资源立即阻断安装；optional 资源只有已标为拒绝/回滚/跳过且 `isolation_confirmed: true` 时才可继续，并必须从 `install_resource_ids` 排除。

估算用于准入与排序，不是最终测量。不要逐件触发完整 SDK 性能构建；逐件只做灾难性烟雾检查。所有合格资源装完后，按平台统一测量该平台的精确最终上下文；两个平台可指向同一 source candidate，但测量任务与报告不能共用。预测会耗尽余量时应在导入前拒绝、隔离或按已冻结策略优化，不能先全部灌入再等正式 SDK 报错。

## 自动优化顺序
只执行合同允许的动作，顺序如下：

1. 只导入/引用选中变体、平台、材质与必需依赖；
2. 对任务生成副本执行已确认纹理上限，作者源只读；
3. 移除已证明未被最终候选引用的任务生成资产；
4. 只做语义安全的重复材质、Renderer、Mesh 处理；
5. AAO 只处理最终候选已证实未用的数据。

PhysBone、Contact、粒子、音频、菜单、衣服部件或其他功能的删除/合并不属于 silent safe action。自动合并 PhysBone/组件曾可能破坏现有动画或行为，因此只能作为用户明确批准的功能取舍，并重新验证行为。

任何 Mesh/Renderer/Material 合并都必须先证明：

- 部件开关和 A→part→B→A 返回策略仍可表达；
- BlendShape 和 small/default/large 胸围联动未丢失；
- 材质动画及独立材质消费者未被合并；
- provider writer/Animator 目标仍唯一且完整。

这类修改以及 AAO 会使相关视觉、菜单和行为证据失效。修改后必须在当前 revision 上重跑受影响证据，不能沿用旧图。

## 最终统一测量
每个平台最终 measurement 必须绑定：exact candidate、revision、SHA-256、platform、SDK version、Rank、worst metrics、完整 metric snapshot、inactive objects included、static-analysis-not-FPS 标记、当前报告证据。同一 source candidate 可用于多个平台，但每个平台仍有独立 job/build/report/evidence；只有平台 override candidate 与主候选不同时，才额外要求该 override 的独立语义验收。完整快照至少包含：

- triangles、skinned/basic mesh renderers、material slots、animators、bones、lights；
- texture memory；
- PhysBone components/transforms/colliders/collision checks、Contacts；
- constraints/depth；
- particle systems、active particles、mesh particle triangles、trails/collision、trail/line renderers、raycasts；
- cloth components/vertices、physics colliders/rigidbodies、audio sources；
- expression parameter bits、bounds X/Y/Z；
- `platform_size` 的 SDK download/uncompressed bytes、当前 SDK limits、over flags 与 Build Report 证据。

所有非零 baseline→final 变化都要归因，包含完整 snapshot 指标和 `platform_size.sdk_download_bytes/sdk_uncompressed_bytes`，也包含资源带来的正增长与优化带来的负变化。同一 metric 可以由多条 entry 贡献，条目 `delta` 合计必须恰好等于实际差值，并附 source/evidence。每个平台 baseline 与 final 的 Performance Report source、Build Report source、final job-result source 必须不相交，final `measured_utc` 必须晚于 baseline；不能复制 final 当 baseline 或仅把 baseline source 改成 final source来制造零差值。若 baseline/final 身份哈希完全相同但期间已有选中资源实际安装，必须有逐资源显式 zero-impact 证明。`source_kind: resource` 只能指向 manifest 中 selected 且当前实际安装/LOCAL_OK 的资源；`optimization_action` 只能指向合同允许且该平台最终结果为 `APPLIED` 的动作。未知 metric、实际零变化的 phantom entry、被拒/隔离资源或未执行动作都返回 `PERFORMANCE_DELTA_UNATTRIBUTED`。这能防止误删必选衣服、菜单或功能后把“变小”写成优化成功；资源完整性、菜单语义和视觉门必须先通过，性能门才有意义。

正式测量使用 canonical job identity：`platform + candidate + candidate revision + candidate SHA + SDK version`。SDK version 必须等于只读发现的 `assembly_manifest.sdk_context.current_vrcsdk_version`；baseline、final、两类 evidence、job/result evidence 与 task size SDK receipt 全部同锚，SDK 版本变化立即使旧闭环失效。每个平台在 `performance_measurement_jobs` 中恰有一条匹配记录；首次只 dispatch 一次，`dispatch_count` 必须等于 1，超时后以 `timeout_action: poll_same_job` 轮询同一个 job，不重复触发 Build。`result_evidence` 不是文件名字符串，而是结构化记录：kind、platform、candidate/revision/SHA、SDK version、layer、source；其 performance/build source 集合必须分别与该平台 final measurement 的两类 evidence 完全闭合。只有候选或 SDK 版本真正改变才创建新 job。completion/upload 只接受终态 `SUCCEEDED`、非空完成时间和闭合的结果证据；缺失、身份错配、跨平台 source 复用、重复派发或非终态统一为 `PERFORMANCE_UNVERIFIED`。Build/报告失败应修根因后定向重试，不能连续重复六次同一构建。

## 结算
- `rank_gate` 未达目标：`PERFORMANCE_RANK_NOT_MET`；
- `metric_caps` 超限：`PERFORMANCE_BUDGET_EXCEEDED`；
- 官方平台 bundle limit 超限：`PLATFORM_SIZE_LIMIT_EXCEEDED`；
- Android 硬组件超限：`MOBILE_COMPONENT_LIMIT_EXCEEDED`；
- 全平台 SDK 硬项超限：`HARD_COMPONENT_LIMIT_EXCEEDED`；
- 合同余量耗尽：`PERF_HEADROOM_EXHAUSTED`；
- 差值无法完整归因：`PERFORMANCE_DELTA_UNATTRIBUTED`；
- 当前候选/SDK/完整快照/证据缺失：`PERFORMANCE_UNVERIFIED`。

`measured_best_effort` 仍必须测量、执行/检查全部允许动作、报告真实 Rank 和未达目标状态。它只改变硬目标处理，不允许跳过证据或伪造“已优化到 Good”。
