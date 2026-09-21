# 验收：看结果，只看有关结果

## “完成”的三件不同事情
- 内部断言：字段/参数/数值检查符合预期，仅记录为内部 `check_ok`。
- `LOCAL_OK`：本资源规定的本地观察已经在精确候选上完成，正常外观、相关实际控制和代表状态符合目标。
- 上传成功：SDK/远端确认了实际发布动作，不代表 VR/多人或其他未测客户端行为已验证。

不用一个“Full PASS”同时描述三者。普通试跑看到 `LOCAL_OK` 后即可结束，不强制 Build & Test。若实际看见炸模/缺块/浮空，数值正确不能覆盖该失败。

## 首先校准一次预览
确认唯一目标 Avatar，排除场景旧副本、测试模型和重复衣服。核对待观察 candidate 与当前源配置相符；在任务元数据里写明 authoring / provider_preview / processed_clone / Unity_play 的确切层。
经过 MA/NDMF 的最终绑定与状态在实际处理候选上检查，不能拿未经处理的 source 假装最终效果。处理后重新设置本次衣服/身体状态，等待形变和 bounds 更新。
若画面异常疑似预览颜色、相机、重复角色或未应用姿势，先修预览一次，不先改 Mesh。不能用黑图、路径、缩略图、像素非空、截图标题证明观察成立。

展示协议来自已确认的开工卡。未确认展示方式不得开始 Unity 写入；已确认后维护同一份可更新图册或用户选择的等价载体，不为每件衣服各造一套长报告。全部衣服与插件安装烟雾通过后才冻结统一验收候选。

开工卡同时写明最终证据预算。推荐预算按覆盖面生成：默认全身基准视角、每套主衣/发型一张正常材质代表图、仅与该资源有关的风险姿势/局部图、一个紧凑菜单树，以及每个插件一个 Happy Path；缺陷另留必要的全身定位、未缩小局部和前后对照。预算不是少测的理由，而是禁止为同一状态重复拍图。中间诊断、无效截图和旧 revision 不进入最终图册；为未解决缺陷保留原图可以超出预算，但必须标为失败证据。

每份证据绑定候选标识/内容哈希、处理层、provider/preview 版本、状态和检查版本；这也包括 SDK Build Report 与交付包解压实测。Mesh、BlendShape/AAO、材质/纹理、菜单、Animator、参数或 provider 输出变化后，相关证据立即失效；只有能证明不依赖该变化的证据才可复用。最终验收图册不得用旧候选的姿势图替代当前候选，尤其不能在 AAO 或 BlendShape 清理后沿用此前的穿模/动作通过图。

## 每件资源的最小相关检查
| 类型 | 必须观察什么 | 代表状态/画面 | 不附带运行什么 |
|---|---|---|---|
| 原生/转换上衣 | 衣片完整、绑定正确、默认状态、基础开关及联动、轻微穿模 | 正常材质前/斜侧；色块默认、抬手、前倾；胸部控制确实相关才加最小/默认/最大 | 不查所有鞋子、全部插件或组合矩阵 |
| 裤子/裙子 | 腰胯/腿与裙骨跟随、开关、遮罩恢复 | 默认、坐、蹲、单腿抬起；只取暴露风险的角度 | 不测无关胸部 |
| 鞋子 | 鞋完整、脚型/姿势配套、脚底/踝部、开关恢复 | 默认、作者支持的高跟状态、蹲；脚部近景 | 不把所有普通鞋强制加高跟形态 |
| 连体衣 | 全部必要部件、体型联动、互斥/开关、覆盖区 | 默认、抬手、坐、蹲，最有风险的身体状态 | 不做所有姿势×所有胸型 |
| 头饰/刚性附件 | 接触/锚点位置、大小、相对跟随、开关 | 目标部位正面与侧面，骨骼一次典型活动 | 不跑身体 Profile、全身姿势 |
| 功能插件 | 指定入口/自动触发→参数→最终效果、保留已有正常功能 | 本次功能的一个启停或等价 Happy Path | 不重新验证整套衣柜 |
| 仅改材质 | 指定槽位与共享隔离、shader 正常、正常材质外观 | 对应部位正常预览 | 不跑姿势矩阵 |
| 仅改遮罩/部件开关 | 开/关/再开后衣片、身体、丝袜及仍开启层正确 | 本件四个有效状态；数字检查配合实际画面 | 不只看 *_OFF 数值、不测全部参数组合 |

新增资源的“完整部件”来自源素材和目标状态；有意隐藏的作者功能不算丢失。有意露肤、孔洞、透明衣料不算穿模。配件之间允许用户有意叠穿，不机械设全互斥。

覆盖胸部的衣服必须同时通过“准入支持映射”和“最终逐状态视觉观察”两层。作者/MA/Profile 映射证据不能证明最终外观；`required` 中声明 `chest_states` 也只是计划。每个 `affected_chest_state` 都要有一条独立 visual observation：唯一 `id` 通过 `check_id` 关联 required check，并显式记录 `resource_id`、`chest_state`、当前 `candidate`、`candidate_revision`、`candidate_sha256`、`layer: post_ndmf_processed_candidate`、`status`、`observed`、原图 `evidence` 和观察 `note`。完成态必须为 `status: LOCAL_OK`、`observed: true`，且 evidence/note 非空；pending 示例固定为 `NEEDS_OBSERVATION`、false、空 evidence、null note。缺一项、仍绑定旧候选或只是通用旧 observation 即 `CHEST_STATE_VISUAL_UNVERIFIED`，不能结算 `LOCAL_OK`。

统一验收先看默认全身正/侧/背/左右斜侧，再看各主衣、发型和会改变外观的插件状态；风险位置同时给全身定位图和未缩小局部图。核对 `expected_parts` 中每个应显示部件、材质槽、层级激活、Renderer enabled、bounds/culling 和实际屏幕结果。任一应显示部件缺失、半截消失或被错误遮罩都失败，不能用“对象存在”补全视觉证据。

## 色块穿模检查
临时副本中身体/内层与当前衣服设为明显不同的纯色，背景用高对比；保留深度关系，关闭不相关重复服装。正常材料帧保留用来排查透明/深度效果。
只在应被不透明衣片覆盖的区域判断不应出现的身体颜色。多视角定位疑似区域，必要时看衣服独立/身体独立一次；不能用任何青色像素都失败的阈值。
确认轻微几何穿出才交局部修复。明显尖刺、脱体、错误骨架不是外扩问题。无遮盖区域不生成 mask，不能隐藏身体来减少检测色块。

在开工卡约定的状态与代表姿势内，已知穿模、毛刺/尖刺、漂浮、脚不在鞋内、错误脚型或缺失部件一律阻断 `LOCAL_OK`。所谓彻底解决指这些已冻结状态和视角中没有已知残留，不代表未经测试的任意动作。一次定向修复和复测仍失败时隔离该资源，不能把“改善了”写成通过。

统一使用三个可检索拒绝码：覆盖胸部却缺少所需作者调节能力为 `UNSUPPORTED_BREAST_ADJUSTMENT`；应显示部件缺失、被错误禁用/裁切/遮罩为 `INCOMPLETE_VISIBILITY`；穿模、尖刺、漂浮、脱体或错误鞋脚形态为 `VISIBLE_GEOMETRY_DEFECT`。拒绝码必须附候选、状态、视角和原图证据，不能只写一个标签。

## 姿势不是文件名
`pose_applied` 必须来自实际预览姿态观察/关键骨骼或 muscle 状态改变，坐姿图要真的坐下。给 T Pose 图改名 seated 视为该检查未执行。
只组合已知有关的风险：胸型三个点可先在默认姿势查；只有发现联动风险时再测一个最坏姿势，不对所有滑条全排列。UI滑条支持范围不是无限任意动作保证。

## 材质、绑定、菜单缺一不可
- 骨骼字段存在只证明引用；处理后手脚/衣服不脱体、动作跟随才是对应观察。
- 数值矩阵正确只证明驱动逻辑；显示缺块/透明仍失败。
- 直接改 `activeSelf` 不证明菜单；必须驱动真实输入链。
- 菜单按 [menu-planning-and-localization.md](menu-planning-and-localization.md) 从最终根节点完整遍历；空引用、循环、不可达预期入口、无意义空页、非预期重复或单页超过 8 个 Control 均失败。
- 每个已选中且已安装主衣都要从计划和 post-NDMF 结果中找到带相同 `resource_id`、`semantic_role: outfit_parts_menu` 的可达作者/provider 部件菜单入口，并观察其内部控制确实产生对应换装/部件效果。作者已有整套控制时验证原控制；没有时不强制生成 `穿着本套`。整套选择控制若存在，必须位于该部件菜单分支内，不能另建平行 Outfit 树。标签、默认状态或 restore_sequence 自报不能替代这条映射。
- 主衣菜单实际运行 `A → B → A → 默认恢复`；它验证互斥与恢复，不替代逐资源激活入口验收。
- 每个 `outfit_part_control` 依据冻结的 provider/user `return_state_policy` 运行 `A → part toggle → B → A → 观察预期 → restore`；逐项确认用户选择保留或作者默认恢复、无残留部件、身体遮罩正确、鞋与脚型正确。缺当前证据为 `OUTFIT_PART_RETURN_STATE_UNVERIFIED`，任一状态不符为 `OUTFIT_PART_RETURN_STATE_BROKEN`；二者都阻断 `LOCAL_OK`，但不要求所有部件×所有衣服全排列。
- SDK 构建成功不证明视觉；普通本地预览不证明远端同步/网络 Contact。

## 性能与体积也绑定当前结果
`assembly_manifest.sdk_context` 的只读发现版本是唯一 current SDK 锚。性能 baseline 必须在进入安装前按平台取得该版本的 Avatar Performance/Build Report，并保存 `measured_utc`、`frozen_at_phase: PREFLIGHT` 和 canonical hashed receipt；缺失、旧 hash/SDK、无锁 receipt 或跨平台复用 source 为 `PERFORMANCE_BASELINE_UNVERIFIED`。final measurement 也必须来自同一当前安装 VRCSDK，并与每个平台的 exact candidate/revision/SHA 一致，时间晚于 baseline，且两类 report source 与 baseline 不相交；每条 Rank/Build evidence 显式包含 platform，至少记录 SDK version、Rank、worst metrics、完整 metric snapshot、禁用对象计入、静态分析非 FPS，以及当前 Avatar Performance Report。每个平台还必须有身份绑定 platform + exact candidate/revision/SHA + SDK version 的 canonical job，`status: SUCCEEDED`、`dispatch_count: 1`；结构化 result evidence 与该平台 final performance/build source 完全闭合且不引用 baseline source。超时只 poll 同一 job。旧候选报告、另一平台 source、Build & Test 截图、重复构建、复制 final 充当 baseline 或手工数字不能通过；SDK 变化后全部重测。

最终 snapshot 覆盖 mesh/renderers/materials/animators/bones/lights、texture memory、PhysBone/Contacts/Constraints、particles/trails/collision/raycast、cloth/physics/audio、expression parameter bits 和 bounds。`platform_size` 另外保存 SDK download/uncompressed bytes、当前 SDK limit 来源/over flags 和 Build Report。PC 官方 200,000,000/500,000,000 bytes、Android 10,000,000/40,000,000 bytes 是独立平台门；任务自定义 300,000,000-byte uncompressed gate 更严格，不能二选一。

双平台可复用同一 source candidate/revision/SHA，但必须分别有 platform job、build、测量、报告和证据。若某平台使用不同 override candidate，该平台的 resource/visual/behavior/menu acceptance 也必须绑定 override candidate/revision/SHA；不能用主 PC 候选的语义证据代过。Android 超过 PhysBone Components 8、Transforms 64、Colliders 16、Collision Checks 64、Contacts 16、Constraints 150 或 Constraint Depth 50 时为 `MOBILE_COMPONENT_LIMIT_EXCEEDED`。所有平台 synced expression bits 256、VRCPhysBone/VRCPhysBoneCollider/VRCContact 各 256、VRCRaycast 80 是不可被合同放宽的 SDK 硬限，超出为 `HARD_COMPONENT_LIMIT_EXCEEDED`；Android 对应项仍取更严格限制。合同只能调整正数 reserve，不能抬高 limit。合同余量不足为 `PERF_HEADROOM_EXHAUSTED`；不得把 253/256 或恰好 256/256 当健康通过。

baseline→final 每个非零变化（增长或优化下降）必须由一条或多条资源/动作记录完整归因，包括完整性能 snapshot 与 SDK download/uncompressed bytes；条目 delta 合计等于实测差值且都有 evidence。资源 source 必须是 selected 且已安装/LOCAL_OK，动作 source 必须获准且实际 `APPLIED`；未知或零变化 metric 的 phantom entry 也失败，否则返回 `PERFORMANCE_DELTA_UNATTRIBUTED`。语义安全合并或 AAO 后，相关 visual/menu/behavior evidence 失效并重测。PhysBone/Contact/粒子/音频/功能删减没有用户明确授权就失败。

最终结算顺序是 resource/menu/visual/behavior → optimization → task size → platform performance。这样 size/Rank 通过不能掩盖必选衣服或菜单被误删。target Rank 只允许 Excellent/Good/Medium；Poor/VeryPoor 仅按冻结的 `rank_handling` 处理，不能作为目标。所有模式都必须完成当前测量和允许动作；实际 Rank 为 Poor/VeryPoor 时，即使 metric caps 通过并单列 `caps_met: true`，整体仍必须 `target_met: false`，不得写“已达到性能目标”。详见 [performance-optimization.md](performance-optimization.md)。

## 谁来验收
同一主会话可以完成观察阶段：冻结当前候选，停止修改，实际打开图像并对照预期。独立 reviewer 可选，不依赖特定模型、不强制再派一个 max agent。未独立观察就别写“独立验收”。
工具缺少必要能力时记录准确缺项，不加一个只检查 JSON 的脚本来冒充。要继续生成普通可用版本，可隔离失败资源；确实无法判断用户结果才暂停整项任务。

## 何时重测、何时不测
每次检查必须有当前问题、通过后的动作、失败后的动作。记录 candidate/hash + state/pose + view + preview/provider version + check-version。
这些完全相同且证据有效 → 复用；未读过的画面/实际换姿势/修正截图管线/输入或相关设置变更 → 可重新观察。修复本件只复测失败及受影响项，不强制拍全部衣服。
全批次完成再看一次最终默认外观和有共享状态依赖的主要切换，保证没有混装/残留；已接受且未受影响的细节不从头重测。

最终报告只引用当前冻结候选的接受证据和仍然有效的失败/限制证据。旧 revision 可留在诊断目录，但必须从最终图册和 `LOCAL_OK` 依据中剔除；候选身份或依赖关系无法证明时宁可重拍最小相关检查，不把旧图升级为新结果。

用户在图册指出缺陷后，与该缺陷相关的 `LOCAL_OK` 和所有依赖它的上传就绪状态立即失效。只从最近未受影响且有证据的候选继续；已知坏版本不能因换了文件名、换了图册文字或上传成功而成为新基线。

统一验收结束时，把被本次证据真实覆盖的 `INSTALLED_UNVERIFIED` 资源逐项结算为 `LOCAL_OK`、带拒绝码的 `REJECTED` 或已清理的 `ROLLED_BACK`。只要还有一个已安装资源停留在 `INSTALLED_UNVERIFIED`，任务就不能完成或上传。
