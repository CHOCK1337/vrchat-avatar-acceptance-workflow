# 表情菜单规划、汉化与验收

先把最终菜单当作一棵用户信息架构来设计，再让衣服和插件接入。包自带菜单能被合并不代表合并后的入口直观。

## 官方边界与本工作流建议

- VRChat 官方规定单个 Expressions Menu 最多 8 个 Control，并支持子菜单继续嵌套；Button、Toggle、Sub-Menu 和 Puppet 的运行语义见 [Expressions Menu and Controls](https://creators.vrchat.com/avatars/expression-menu-and-controls/)。`8` 是硬上限。
- 同步的自定义 Expression Parameters 总预算为 256 bits；Bool 占 1 bit，Int/Float 各占 8 bits，另有较大的自定义参数总数上限。菜单规划要复用作者参数并在最终参数资产上核对类型与 Saved/Synced，不为汉化或分类复制一套参数。见 [Animator Parameters](https://creators.vrchat.com/avatars/animator-parameters/)。
- 本工作流推荐根层最多使用 6 个入口，为后续功能和恢复入口保留位置；常用动作尽量从根菜单两次点击内到达，通常避免超过三层子菜单。`6`、两次点击和三层都是可用性建议，不是 VRChat 官方限制。
- Modular Avatar Menu Installer 默认可把内容加到顶层，也可指定 `Install To`；菜单满时会自动分页。见 [Menu Installer](https://modular-avatar.nadena.dev/docs/reference/menu-installer)、[Menu Install Target](https://modular-avatar.nadena.dev/docs/reference/menu-install-target) 和 [Menu Item](https://modular-avatar.nadena.dev/docs/reference/menu-item)。Menu Item 未绑定时不会生效；同一参数有多个 `Is Default` 时结果未定义。自动分页只能避免构建溢出，不能代替信息架构规划或正式交付菜单。
- VRChat 的作者自定义 Control 只有一个可见名称字段，官方菜单文档没有提供按客户端语言自动切换该文字的机制。因此本工作流把“汉化”定义为生成集成层中的简体中文可见标签，不宣称会随客户端语言自动切换；多语言交付需要分别维护明确版本或按目标平台规则设计。
- VRChat Marketplace 要求英语作为 Avatar 属性、贴图和 Expressions Menu 文字的主要语言，允许附加非英语翻译。见 [Marketplace Content Guidelines](https://creators.vrchat.com/economy/guidelines/)。这条交付规则只在 Marketplace 商品场景适用，不能扩大成普通 Private Avatar 的英语强制要求。

## 写入前先做菜单清单

从 Avatar Descriptor 的根菜单开始，同时盘点 VRCExpressionsMenu、MA Menu Installer/Menu Item/Menu Group、其他 provider 菜单源及其目标位置。写入前的计划本身必须是一棵完整可达图：根页存在、所有页非空、每页 1–8 个 Control、所有子菜单目标存在、无循环/孤页/重复 Control ID；非 Sub-Menu 控制还要有符合其类型的参数绑定。每个拟保留控制至少记录：

- 当前可见标签、Control 类型、来源和作者语义；
- 对每个主衣的主要部件菜单入口显式记录 `resource_id` 与 `semantic_role: outfit_parts_menu`；作者已有的整套激活或部件控制继续记录 `outfit_activate` / `outfit_part_control`（旧记录可为 `outfit_part_toggle`），但不要求人为补一枚整套激活控制；
- 每个非 Sub-Menu 控制必须是 `resource_id + semantic_role` 的资源控制，或显式 `scope: non_outfit` 并使用对应眼睛/动作/功能/设置等非换装语义；不能靠省略字段藏起平行换装控制。每个 `outfit_part_control` 还要冻结 `return_state_policy: preserve_user_choice|restore_provider_default`、`return_state_policy_source: provider|user` 和非空依据；
- 子菜单目标，或参数名/类型/值；
- 参数的 Animator/Reactive/Provider 消费者及最终可见效果；
- 默认、Saved、Synced 语义和与其他控制的互斥关系；
- 计划目标路径，以及是原菜单安全链接还是任务生成 wrapper；
- 在 `outfit_menu_integrations` 中记录该衣服的 `provider_parts_menu_status`、`strategy`、provider 和判定证据。状态/策略配对只允许 `usable → reuse_provider_parts_menu`、`incomplete → complete_provider_parts_menu`、`absent|unusable → minimal_parts_wrapper`。

请求已经给出 Avatar、资源、默认状态或版本时直接写入 `assembly_manifest`，不把白色系、默认组合或“最新版”当作菜单质量问题重复询问。版本只读发现不一致且会影响结果时，才提出一个事实性问题。

## 衣服默认：作者部件菜单优先

衣服不套用固定的通用分类骨架。作者/provider 已有可用部件菜单时，把它作为这套衣服的唯一主要入口，安全链接到用户在开工卡确认的安装目标，并保持作者的开关、默认、互斥和恢复语义。例如：

```text
约定安装目标
├─ 套装 A 部件（作者菜单安全链接，outfit_parts_menu）
│  ├─ 作者原有上衣/外套/下装/袜/鞋/饰品控制
│  └─ 作者原有整套开关（仅当原本就有）
└─ 套装 B 部件（作者菜单安全链接）
```

不要同时建立“Outfits/穿哪套”的选择树和另一棵“部件/衣片”树，让用户先去一处换装、再退回多层到另一处调部件。若作者部件菜单本身已经拥有激活/部件语义，就直接保留，不生成强制 `穿着本套`。同一 provider + internal parameter 控制链出现在主要部件菜单外也按平行树拒绝，即使外部控制自称非换装。作者菜单存在但只缺一个明确、安全的必要控制时，优先在该套作者菜单内补这一个；作者菜单缺失或无法安全复用时，才创建只容纳必要控制的最小部件 wrapper。不得据 Renderer 列表机械造开关。

每个已选中且实际安装的主衣资源必须有一个可达的主要部件菜单入口；入口显式携带该资源的 `resource_id` 和 `semantic_role: outfit_parts_menu`，不能只凭标签猜关联。该菜单内至少要有一个实际可操作、可观察效果的作者或最小补充控制。若拆分会破坏 provider、参数或默认语义，就完整保留作者子菜单。不要为追求整齐复制 Animator、参数或控制器。

衣服以外的动作、功能、设置仍可按当前内容建立清楚入口，但只建立有内容的分类，不生成空页，也不把 `衣柜 / 发型与外观 / 动作 / 功能与插件 / 设置` 当作每个项目必须生成的固定根结构。

## 私人头像的简体中文可见标签

本工作流对 Private Avatar 默认将用户能看到的导航和功能标签汉化为简体中文；用户明确指定其他语言时以用户要求为准。

- 翻译安全 wrapper 的导航与功能含义，例如 `Parts → 部件`、`Settings → 设置`。作者菜单本体的可见文字只有在生成副本且不改变语义时才汉化；不为了统一文案额外生成 `穿着本套`。
- 品牌名、角色名和插件专名保持原名，可补充用途，例如 `GoGoLoco（动作）`，不要杜撰中文品牌名。
- 不翻译参数名、Animator 参数/层/状态、AnimationClip 路径、BlendShape、Provider 标识、脚本字段、对象绑定路径或内部资源文件名。
- 只修改任务生成的 VRCExpressionsMenu 副本、MA Menu Item wrapper 或安全链接标签；不原地改 `Packages`、vendor Prefab 或作者菜单资产。
- 如果同一作者菜单被其他 Avatar/Prefab 共享，必须 Copy-on-Write；记录旧/新引用。汉化后原控制参数、类型、值、默认和 Saved/Synced 语义保持不变。
- Marketplace 交付改用英语主导并可附简体中文，不套用 Private Avatar 的中文默认。

## 安全接入

按规划树为各 Menu Installer 明确设置目标，优先用受支持的 `Install To` / Menu Install Target 把作者部件菜单直接放到约定位置，不依赖默认顶层安装。一个 wrapper 只负责在作者菜单不可用时承载最少导航/控制；行为继续由原作者参数/provider 驱动。若 provider 无法安全重定向，只能整体链接、使用其受支持的安装目标，或隔离该资源，不能直接改 vendor 输出或复制 Writer。

根菜单和每个子菜单都为后续生成输出留出可见余量。需要分页时给页名稳定语义，并确保常用控制不因自动分页被推到不可预期位置。重复入口只有在确有快捷导航价值且指向同一安全控制链时保留，并在菜单清单标注为 alias。

## 最终候选上的菜单验收

只在冻结后的处理候选上验收，不以源菜单资产或 provider 成功日志代替最终结果：

1. 从 Avatar Descriptor 的最终根菜单遍历整个菜单图；每页 `0 < controls <= 8`，所有预期入口可达，子菜单引用非空，不存在循环、无意义空页、非预期重复入口、未绑定 Menu Item 或孤立菜单。明确标注的快捷 alias 不算错误；非规划内的 provider 自动 `More/Next` 分页不算通过。
2. 核对根菜单不被资源入口平铺占满，约定入口、名称、顺序和可见语言确实出现在处理结果；vendor 原件保持未改。不得出现计划外的平行 Outfit 选择树与部件树，也不得强迫用户在二者之间反复返回。
3. 对每个可操作 Control 验证 `菜单 → 实际参数/重映射 → 最终 Animator/Reactive/Provider → 目标对象、材质或形态`，同时核对参数已声明、类型、唯一默认、Saved/Synced 语义。逐一联结“已选中且已安装的主衣资源 → 计划中的 `resource_id + outfit_parts_menu` 入口 → post-NDMF 实际路径 → 菜单内作者/最小补充控制 → 可见换装或部件结果”；post-NDMF 结果本身也必须保存相同的资源身份、语义和 provider 来源。直接 SetActive 不算菜单验证。
4. 有两套及以上可切换主衣时，互斥至少跑 `A → B → A → 默认恢复`，观察没有叠穿、残留部件、错误身体遮罩或错误鞋型。另对每个 `outfit_part_control` 按已冻结策略跑一次 `A → part toggle → B（可为原版）→ A → 观察预期 → restore`；结果逐控制记录 policy、counterpart、sequence、实际部件状态、残留、身体遮罩、鞋/脚型与当前证据。策略可以是保留用户选择或恢复作者默认，不能一律强制重置。这个序列只证明恢复行为，不能替代上一步逐资源可达激活控制映射。没有这类 A/B 状态的插件、头饰或单衣任务，记录 `NOT_APPLICABLE`、理由和当前候选证据，不制造假通过布尔值。独立 Toggle 至少跑开、关、再开；插件跑一个与本次功能相关的 Happy Path。只复测共享依赖实际受影响的链，不做无关全排列。
5. 留一份紧凑菜单树证据，包含最终候选标识/哈希、处理层、每页 Control 数、每个计划 Control ID/最终路径、入口类型、目标路径、关键参数和遍历结果。最终页集合、每页数量与控制路径必须和开工时冻结的计划逐项闭环；单个“菜单看过了”布尔值不能替代这张对照。截图只保留代表页与异常/修复前后对照；历史候选的菜单图不得混入最终通过证据。

任一预期入口不可达、菜单循环、空引用、每页超过 8、汉化 wrapper 改变行为语义、参数链断裂或恢复状态错误，都阻断 `LOCAL_OK` 与上传。统一使用以下可检索失败码：

- `MENU_UNPLANNED_ROOT_INSTALL`：资源未按规划直接注入根层；
- `MENU_PAGE_OVERFLOW`：任一页超过 8 个 Control；
- `MENU_AUTO_PAGINATION`：最终结果依赖非规划内自动 More/Next 分页；
- `MENU_PARAMETER_UNDECLARED` / `MENU_PARAMETER_TYPE_MISMATCH`：控制参数缺失或类型不一致；
- `MENU_MULTIPLE_DEFAULTS`：同一选择存在多个默认；
- `MENU_UNBOUND_ITEM`：预期 Menu Item 未绑定，最终不可达；
- `MENU_OUTFIT_PARTS_MENU_MISSING`：任一已选中且已安装主衣缺少带 `resource_id + outfit_parts_menu` 的计划或 post-NDMF 可达主要入口，或入口下没有实际可操作控制；
- `MENU_REDUNDANT_PARALLEL_OUTFIT_PARTS_TREE`：同一衣服的整套选择控制位于其主要部件菜单分支之外，形成平行选择树和返回式操作；
- `OUTFIT_PART_RETURN_STATE_UNVERIFIED`：部件控制缺冻结的返回策略、完整跨换装序列或当前证据；
- `OUTFIT_PART_RETURN_STATE_BROKEN`：跨换装返回后部件状态不符策略，或出现残留衣片、错误身体遮罩、错误鞋型/脚型；
- `MENU_MIXED_LANGUAGE`：可见标签不符合已确认语言策略，不包括保留的品牌专名；
- `MENU_FINAL_TREE_UNVERIFIED`：尚未在冻结处理候选上完成整树遍历。
