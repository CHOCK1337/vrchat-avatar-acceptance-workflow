# DeepSeek 在 DSH 中改模的执行规则（核心页）
规则内容：0.3.2-local4-dsh（2026-10-15，实测修订 2026-10-18）· 2026-10-31 拆为核心页 + 按需细则，规则本身未改。

本页在开工和续作时读一次。细则按下面的表格在遇到对应情况时再读，不要一次预读全部。沿用已安装的 UnityMCP-VRC、作者方案和现有 Skill；规则不能补出不存在的网格，也不能赋予模型未实测的视觉能力。

## 开工
1. 按 AGENTS.md 的顺序核对工程、Avatar、工作台任务和反馈。当前需求和用户最后一次有效纠正优先于旧交接、案例和示例。
2. 记一次三个身份：可编辑源 Avatar、处理后候选（路径和实际版本）、工作台当前预览的对象及来源。三者对不上时先校正现有工作台目标，再动手。
3. 在当前私有候选上修改，保留源资产、共享材质、作者功能和回退所需的旧值。普通可逆修改不用逐项再问。
4. 第一次使用某条预览/换装链路时，先让一件真实衣服在正确候选上显示，并能从菜单切过去再切回默认；通过后再串行处理同批其他素材，最后集中检查。
5. 请求里有 `compatibility` 字段时，按其中的 `decision` 处理只有其他体型版本的素材：`skip` 不装并在回复里说明，`install_anyway` 照装并说明贴合风险。没有这个字段却发现只有其他体型版本时，停下来请用户在工作台选择（[finish-check.md](skills/assemble-vrchat-avatar/references/finish-check.md)）。

## 怎样才算做到
- **衣服装上了**：候选里出现了带可用 Mesh 的真实衣片。辅助、菜单、Shape Changer Prefab，相同骨名，MA Merge Armature 成功，商品封面，都不能证明。
- **换装能用**：通过工作台真实菜单或[已验证的预览入口](skills/assemble-vrchat-avatar/references/dsh-preview-api.md)操作 A→B→A，看到衣片变化并恢复默认。直接写参数、SetActive、改默认值后重烘焙只算诊断。
- **外观正常**：打开同一候选的原图，构图覆盖要验收的部位；腿部要看到完整小腿、脚踝和脚。截图文件存在、组件数量、NDMF 或 SDK 成功都不能证明外观。
- **修好了**：沿“源配置 → 作者控制 → 处理输出 → 当前预览”找到第一处分歧，只改导致它的最小范围，再复查受影响的状态。没有新证据不重复同一轮失败操作。

## 不用这些捷径
清零全部身体形态键、全局关闭 Animator、移除作者必要遮挡、删物理或骨骼、换身体/算法/模型、增加并行写入者，都不能用来让结果“看起来通过”。缺少必要能力或素材时，如实记下具体缺口，保持未完成。

## 收尾
回复 `addressed` 之前，在 Unity 运行收尾检查（[finish-check.md](skills/assemble-vrchat-avatar/references/finish-check.md)）。删除或停用测试副本、保存场景和修改版本、补齐 MA 引用后再跑一次；仍有问题就在回复里写明。没有检查，回复工具会拒绝；检查结果会自动显示在用户的工作台上。

## 遇到这些情况再读细则
| 情况 | 读这一页 |
|---|---|
| 确认目标与候选身份、批次顺序、修正预算、交接记录与当前请求冲突 | [identity-and-scope.md](skills/assemble-vrchat-avatar/references/dsh-playbooks/identity-and-scope.md) |
| 腿变细或缺失、脱衣不恢复、形态键联动、骨骼 null、跨素体拼头后头发错位 | [body-shapes-occlusion.md](skills/assemble-vrchat-avatar/references/dsh-playbooks/body-shapes-occlusion.md) |
| 衣柜/菜单/插件开关验收、互斥组覆盖作者开关、原生圆盘旋钮 | [controls-and-menus.md](skills/assemble-vrchat-avatar/references/dsh-playbooks/controls-and-menus.md) |
| 看图与构图、几何测量、PhysBone 动态、姿势参数变了画面不动、模型读不了图 | [images-physics-pose.md](skills/assemble-vrchat-avatar/references/dsh-playbooks/images-physics-pose.md) |
| 成品优化、SDK 构建与硬限制、PhysBone 合并、AAO、动态脚本连续出错 | [build-optimize-physbone.md](skills/assemble-vrchat-avatar/references/dsh-playbooks/build-optimize-physbone.md) |
| 调用工作台试穿、换姿势、截图 | [dsh-preview-api.md](skills/assemble-vrchat-avatar/references/dsh-preview-api.md) |
| 收尾检查的调用模板、问题代码和体型决定 | [finish-check.md](skills/assemble-vrchat-avatar/references/finish-check.md) |
| 路由不对、多个数据根、MA 互斥衣柜/发型 probe | [dsh-execution.md](skills/assemble-vrchat-avatar/references/dsh-execution.md) |

## 记录与交付
在当前任务中增量记录下列事实即可，不重写用户字段，不另造大型验收系统：

| 记录 | 什么时候能写 PASS |
|---|---|
| installed | 请求的实际资源和必要衣片进入候选 |
| source_checked | 受影响的引用、默认值、互斥覆盖、形态目标已检查 |
| provider_checked | 同一候选经过实际 MA/NDMF/相关作者处理并核对输出 |
| control_tested | 通过真实入口操作并观察了相关状态与回切 |
| visual_checked | 实际读取同一候选的合适原图，相关外观没有未解决缺陷 |
| sdk_build | 任务需要/允许的本地 SDK 构建确实成功；普通小改不强制构建 |

每层独立填写 PASS、FAIL、NOT_RUN 或 NOT_APPLICABLE（说明原因）。NOT_RUN 不能包装成整体通过。反馈 seen/processing_feedback/addressed 分别只代表已读/处理中/已答复，不能代替修复验收。
交付用短句写清：修改了什么、看到了什么、操作了什么、候选在哪里、还缺什么。用户要的换装或明显腿部问题未通过时保持未完成。构建、上传和客户端效果各按实际证据记录；规则本身不新增上传权限。

## 直接交给 DSH 的启动提示词
> 读取本包 AGENTS.md、skills/assemble-vrchat-avatar/SKILL.md 和 DEEPSEEK_RULES.md，按当前任务继续执行。先确认准确工程、源 Avatar、处理后候选及工作台预览来源；先完成一件真实衣服的显示和菜单回切，再扩大批次。腿部先追身体遮挡和恢复链，不盲加同名形态联动。实际操作相关控件并打开覆盖问题部位的原图后才能宣布修复。复用现有工程与工具，保留作者功能和当前修改；必要能力缺失则如实标未测。不要只输出方案，不构建或上传，除非我当前任务明确要求并授权。

本提示词中的“不构建或上传”适合当前修复任务；若用户明确要求成品优化/本地构建或已授权上传，应按当前授权和原 Skill 执行，不拿旧提示词撤销它。

2026-10-15 现场实测补充：[DSH 改模执行卡](DSH_TASK_CARD.md) 提供每轮的精简执行要求，包括实际网格来源、部分写入后的恢复、工作台可见状态、姿势重置和真实图像判读。它不改变原改模方法，也不把规则说明当成自动验收。
