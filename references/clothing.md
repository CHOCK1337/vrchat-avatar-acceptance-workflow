# 衣服：标准安装与受限适配

## 先解决路由，不先审计整个身体
阅读资源实际 Prefab、作者适配列表、目标模型版本、依赖及作者提供的安装组件。名称/目录只提供线索；SkinnedMeshRenderer 不是“衣服”的充分条件。
记录预期可见部件及真正需要的身体控制。原生适配优先复用作者配置；没有配置时使用 MA Setup Outfit/Merge Armature。发现已有正确 Merge Armature，不重复 Setup Outfit 制造第二套组件。

只读准入时建立 `expected_parts`：主衣、内衬、袖/裙片、鞋、袜、装饰、必要材质槽和作者有意开口分别列出。每个语义部件带稳定 ID 与 `required_check_id`，并在该资源的统一验收 `required` 列表中落到实际 visual/structure 检查；不是要求每个 Renderer 都拍一张图，而是防止作者预期袖、袜、鞋或装饰从记录里静默消失。不能因对象同挂一根节点就默认全部开启，也不能因 Renderer 存在就认定显示完整。准入同时估算本件及依赖对已确认体积指标的贡献；在生成副本导入时就执行开工卡允许的贴图上限、压缩和去重，不能等全部装完才第一次优化。

MA 官方参考：https://modular-avatar.nadena.dev/docs/tutorials/clothing

## 非对应衣服只走 Profile 生态
确认来源与目标身份、Profile 双向/单向能力、方向、版本、作者要求的 body states。检查安装的 MochiFitter 是否真的可调用；存在文件夹或产品名称不代表调用接口可用。
官方说明入口：https://yamirin.booth.pm/items/7657840

源/目标 Profile 齐全且兼容 → 在生成副本转换 → 采用转换产物安装 → 查看最终处理候选。
任一 Profile 缺失/不兼容 → 标为 `UNSUPPORTED_PROFILE` 并隔离，不改走 GenericFit、盲试其他角色 Profile 或要求用户提供一张“继续冒险声明”。
无头 API 未确认时，先读安装版的官方入口/源码；有当前宿主支持的可调用动作才用。不能凭空反射猜方法。仅 GUI 可操作而当前工具无法调用时，明确 `PROVIDER_NOT_CALLABLE`，不要伪称转换成功。纯 Unity 流程不能默默引入 Blender。

## 身体状态不是前置无限审查
作者目标版本或转换 Profile 提供胸部、腰臀、脚型等支持时，进入候选验证；不要求先证明所有不存在的状态。仅验证用户保持启用且会影响本件衣服的控制。
无效/缺失的必要联动必须报告，不能冻结用户的滑条或称作全范围已支持。已有受验证的局部修复/形变工具可使用；本任务不新增通用胸部/权重算法来绕过能力缺口。
比例缩放仅是初始化，不是适配证据。一个支持 Profile 不意味着衣服的所有动画、Contact、PhysBone 自动正确。

### 胸部支持是衣服硬准入门
目标 Avatar 有用户会启用的胸部大小控制，且本件衣服覆盖、约束或会随该区域形变时，必须在导入前找到作者提供的对应 BlendShape/MA Shape Changer 或 Sync、或该源/目标版本已验证 Profile 的明确映射，并能覆盖不可缩减的 `requested_affected_chest_states`（默认小/默认/大，只有用户明确覆盖才不同）。资源自己的 affected states、preflight supported states、required checks 与最终 observations 都必须闭环该集合。只有同名字段、比例缩放、LLM 推测或“可以手调”都不算支持。

缺失时直接标记 `UNSUPPORTED_BREAST_ADJUSTMENT` 并拒绝本件，不导入、不安装、不现场新造形态键，也不靠隐藏身体绕过。完全不涉及胸部的鞋、裤、头饰等标 `NOT_APPLICABLE`，不得误拒绝。

胸部验收分两层，不能互相代替：

1. **Preflight 支持映射证据：**作者说明、原生 BlendShape/MA Sync/Shape Changer 或已验证 Profile 映射，只决定能否准入。
2. **最终逐状态视觉观测：**在冻结的 post-NDMF 处理候选上，对每个 `affected_chest_state` 实际切换并观察衣片完整性、跟随、穿模/尖刺和覆盖恢复。每条胸型 observation 使用自己的唯一 `id`，再用 `check_id` 关联 required check；必须显式带 `resource_id`、`chest_state`、当前 `candidate`、`candidate_revision`、`candidate_sha256`、`layer: post_ndmf_processed_candidate`、`status`、`observed`、`evidence` 和 `note`。

在 required check 上列一个 `chest_states: [small, default, large]` 只是验收计划，不是已经观测。胸型 observation 只有同时满足当前 candidate/revision/SHA、精确 post-NDMF 层、`status: LOCAL_OK`、`observed: true`、非空 `evidence` 和非空 `note` 才完成；pending 记录必须是 `status: NEEDS_OBSERVATION`、`observed: false`、`evidence: []`、`note: null`。缺任一当前状态实拍时记 `CHEST_STATE_VISUAL_UNVERIFIED`，资源保持 `INSTALLED_UNVERIFIED`；实拍发现穿模、毛刺、漂浮或缺件仍使用 `VISIBLE_GEOMETRY_DEFECT` / `INCOMPLETE_VISIBILITY`。通用旧 observation 可继续用于非胸型检查，但不能满足这套胸型门禁。

## 轻微穿模修法
先确定是几何穿出，还是骨骼/权重、材质深度、透明排序、遮罩恢复、重复层造成的假象。
- 只有轻微、局部、已定位的几何问题，才调用已安装且验证过的局部外扩工具。
- 仅移动受影响衣服区域，周边平滑衰减；不全局放大 Transform，不改骨骼和 bindpose 掩盖错误，不把扣子/鞋底当软布。
- 沿法线外推前需确认法线方向、坐标空间和局部尺度。距离受工具限制与可见结果约束，不写死所有 Avatar 通用的“0.8 mm 就足够”。
- 若主要在动作后出现，先看权重/骨架映射；若只在特定 BlendShape 出现，先看真实联动。不要继续把衣服越推越大。
- 一次局部修复事务中可组合必要的外扩和覆盖区遮罩，但仍只有一次定向复测预算；不得给每个子操作重新发一轮预算。
- 无局部工具时明确能力缺口，不写临时大脚本、不循环做几何审计。

## 身体遮罩是第二级
先复用作者准确的 hide shapes。没有现成形态键时，只有已验证的覆盖/可逆遮罩执行器可创建新遮罩；不能临时猜点、塌缩整片网格。
遮罩不得盖住开口、露肤或透明衣料；生成/缩小 Body 也不得成为掩盖骨架错误的手段。
多层覆盖时使用“仍有任何有效衣片需要遮挡该区域”的合成结果。关闭一件不应覆盖另一件仍然开启的需要。身体与丝袜分别评估，不机械复制同一区域索引。
脱衣、关闭部件、切换套装后都要恢复**无覆盖基线**；作者基线不一定数值为 0。确认为 0 时才写 0。见 behavior-and-materials.md。

## 提交当前资源前
确认生成候选实际引用转换后的 Mesh；没有源衣服/失败版本的重复 Renderer。逐项只做灾难性烟雾检查：缺引用、错根节点、重复 Renderer/Writer、编译失败、明显炸模/脱体。通过后记 `INSTALLED_UNVERIFIED` 并继续；不得在这里做一轮完整截图、姿势、胸型和菜单验收。每件选中衣服的冻结验收清单至少含 visual、pose、behavior 三类；覆盖胸部时还要覆盖开工卡冻结的全部胸型状态，并在统一验收时形成逐状态当前候选 observation。它们全部在衣服和插件装完后按 acceptance.md 对唯一最终候选统一执行，不是逐件重复测试。后续一件失败，不能把前面正常资源一起回滚成旧的全部成果。
