# 改模结果

**结果：本地已检查 / 本地待处理 / 已上传 / 上传未确认**（选真实的一项，不把它当全场景无缺陷保证。）

- 开工约定：可视化方式/证据预算、体积口径/精确硬上限、必选与可删顺序、不可缩减的 requested affected chest states（非默认时附用户覆盖来源）、第 3 问内冻结的 per-platform performance policy/target or caps/Poor-VeryPoor handling/headroom/allowed actions/protected features、菜单信息架构/可见语言、固定统一测试节奏、交付层级；精确装配清单、`target_platform`、任务偏好、默认组合、解析版本及只读发现的权威当前 `sdk_context` 来自 `assembly_manifest`。
- 本次完成：用户能看见的变化，以及成功/跳过的资源。
- 当前模型：精确 Prefab 路径和候选标识。
- 统一测试：全部合格衣服/插件装完后的唯一候选和内容哈希，本次实际看过的全身/局部、外观、开关与姿势，附当前候选关键图片路径；覆盖胸部的衣服按资源列出每个 `affected_chest_state` 的独立 observation id/check_id、current candidate/revision/SHA、`post_ndmf_processed_candidate` 层、`LOCAL_OK + observed=true`、非空 note 与原图，不能只列 required check。逐项烟雾检查不写成完整验收，旧 revision 图不混入最终通过证据，未运行客户端就直说。
- 菜单：最终紧凑菜单树、每页 Control 数、可见语言、遍历/可达性结果和关键参数链；逐项列出每个已选中且已安装主衣的 `resource_id → provider/author 部件菜单状态与接入策略 → semantic_role: outfit_parts_menu → post-NDMF 可达路径 → 菜单内实际控制/换装结果`，并明确不存在平行 Outfit 选择树与部件树；有多套主衣时再报告 `A → B → A → 默认恢复`。每个部件控制还报告冻结的 return-state policy 与 `A → part → B → A → expected → restore` 结果，明确残留部件、身体遮罩和鞋/脚型检查；失败使用 `OUTFIT_PART_RETURN_STATE_BROKEN`。列出最少补充控制、fallback wrapper 或明确标注的快捷 alias。
- 体积：指标、十进制/MiB、硬上限、显式 completion/upload gate；SDK 指标按 `applies_to_platforms` 逐平台列实测字节数、candidate/revision/SHA、SDK version、Build Report source 与 canonical job build-result 闭合结果；任一平台数值/source 不一致写 `SIZE_RECEIPT_MISMATCH`，缺 receipt 写 `SIZE_UNVERIFIED`，超限写 `SIZE_BUDGET_EXCEEDED`。交付解压指标单列，不伪装成 SDK receipt。简述已自动执行的获准优化及受影响复测。
- 性能：先确认每个平台安装前 baseline 已验证，列 baseline `measured_utc`、PREFLIGHT lock receipt/hash 及独立两类 report source；再按平台列 exact candidate/revision/SHA、与 `assembly_manifest.sdk_context` 一致的 SDK version、晚于 baseline 的 final time、Rank、worst metrics、完整 metric snapshot、inactive objects included、static analysis not FPS、当前 Avatar Performance Report；列 SDK download/uncompressed bytes、当前官方 platform limit/over flags 与 Build Report。同一 source candidate 可跨平台复用，但 baseline/final 之间及 PC/Android 之间的 report source/job/build/evidence/结论不共用；若平台使用不同 override candidate，再列该 override 的 resource/visual/behavior/menu acceptance。给出 baseline→final 的全部非零 metric 与 SDK-size delta、合法已安装资源/已执行授权动作的归因总和、headroom、每个 allowed action 的 APPLIED/CHECKED_NOT_APPLICABLE。明确官方硬 limit 未被合同抬高、只调整 reserve。任何模式的实际 Rank 为 Poor/VeryPoor 都写 `target_met: false`；metric caps 可另列 `caps_met: true`，但不能写成整体性能目标已达。使用 `PERFORMANCE_BASELINE_UNVERIFIED`、`PERFORMANCE_RANK_NOT_MET`、`PERFORMANCE_BUDGET_EXCEEDED`、`PLATFORM_SIZE_LIMIT_EXCEEDED`、`MOBILE_COMPONENT_LIMIT_EXCEEDED`、`HARD_COMPONENT_LIMIT_EXCEEDED`、`PERF_HEADROOM_EXHAUSTED`、`PERFORMANCE_DELTA_UNATTRIBUTED` 或 `PERFORMANCE_UNVERIFIED` 的真实结果。
- 性能构建：每个平台列 canonical job identity、exact platform/candidate/revision/SHA/SDK version、`status: SUCCEEDED`、`dispatch_count: 1`、完成时间和结果证据；超时仅 `poll_same_job`，不把重复 Build 当作多份证据。
- 尚有问题：一个最重要的失败原因；没有则写无已发现的阻断问题。
- 上传：未授权/测试模式未执行，或真实 Avatar ID、Private、终态。任务授权上传则直接执行，不额外问一遍。

技术细节留在 AssemblyTask.json：本件旧/新引用、修正次数、观察状态和必要操作回执。不要生成重复十章报告。需要反馈时自动从记录导出相关片段，不让用户再手填表格。

仅在有本次新增纠正/经验/复用时，末尾加一行：**经验：**已记录 X 次纠正，形成 Y 条限定范围修法 / Z 条待确认记录，或学习暂不可用。没有新内容就省略。不要把经验计数算作改模进展，不把 ACTIVE 解释成整个模型通过。证据和参数留在本地库。
