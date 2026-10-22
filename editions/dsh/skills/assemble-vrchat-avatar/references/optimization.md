# 成品优化：装完后执行一次，保留效果与原资源

## 何时进入

完整 Avatar 装配/成品交付，以及用户明确要求优化时，默认在装配和必要控制正确后进入保守优化阶段。单件改色、普通部件修复、GUI 或只读任务按原范围执行，不顺带优化全角色。用户明确不优化或禁止 SDK 构建时遵守该限制，不再问技术选型。

QuickTask 可记录 `operation: assemble / optimize / edit`。`optimization.mode` 默认 `auto`，也支持 `conservative / off`；`textures` 默认 `preserve`，明确做选中衣物的质量优先优化时用 `selected_copies`。`constraints.allow_sdk_build: false` 表示当前任务禁止 SDK 构建。这些由 Codex 从用户需求记录，不让工作台用户填技术字段。

`quickflow.py plan` 已包含该阶段。也可单独调用：

```text
python <SKILL>/scripts/optimization.py plan --input <current-request.json>
```

输出是执行建议，不会调用 Unity、修改模型或授权上传。已有 processing/building 状态先读原回执或等原任务；已有完成记录先核对候选身份，不能续作就重建。没有变化且同一候选已优化完成时复用结果。

## 实际执行顺序

1. 保留装配完成、未优化的可编辑候选与当前可用候选。只给本次会改变的材质、贴图、网格建立私人副本，复用未改变资源；不复制工程，不备份 Library/Temp。
2. 沿用本工程安装的 AAO、MA、NDMF。AAO 已安装且适用时，在私人 authoring 候选根使用 `Anatawa12.AvatarOptimizer.TraceAndOptimize`；保留已有作者设置和 MMD/OSC/表情需求，不追加激进删除或手动强并。仅组件类型属于 AAO 公共 API，配置以当前 Inspector/安装版本支持方式为准，不依赖旧版私有字段。AAO 缺失或不适用时记录具体未执行项，不为优化升级 Unity/SDK、改 Packages、MCP 或模型配置。用户明确必须用 AAO 而无法执行时，该项不能写完成。
3. 根据实际使用的 Shader 属性、动画和引用选择高开销衣物贴图；不能把所有非空贴图槽当成实际启用功能，也不统计整工程作为当前成本。需要降采样/压缩时保留分辨率较高的颜色图，优先评估大法线/AO 副本，但不固定所有模型为 2048，更不统一降到 1024/512。脸、眼、身体、主要头发和用户保护区域默认保持质量；保留 Shader、Alpha、sRGB、法线类型、MipMap、Wrap 和各平台实际覆盖设置。贴图改动是有损选择，必须看同镜头近景，不靠 quality=100 宣称无损。
4. 重定向私人材质和本次真实相关动画/控制引用；记录原引用和改动设置以便撤销。不直接改共享付费源素材，不用换 Shader、删除衣服/物理/Contact、冻结用户滑条或取消同步换小分数。优化 expression 参数时保留类型、默认值、保存/同步语义和原菜单；bit 没减少就如实记录。
5. 经过现有原生 MA/NDMF/AAO 管线生成处理后候选。最终构建使用正常保存的临时 additive 场景，不能把 PreviewScene 当 SDK 构建场景。记录实际 authoring 与 processed 身份，防止把已经处理的结果再次作为新 authoring 叠加优化。模型修复数值、袖口网格、体型形态来自当前角色，不复制上一角色的处理。
6. 看最终正常材质默认画面；有贴图变化就与真实前图用同视角、灯光、姿势、尺寸对比。没有前图时记录缺失，不能造一张“优化前”。AAO 影响的实际换装/部件开关、身体遮罩、相应姿势须真实操作一次；脱下→穿回恢复正确。已发现的明显穿模先修当前缺陷再复查，不隐藏 Body、小腿或简化材质出图，不重拍无关内容。
7. 完整成品和明确优化任务做一次最终 SDK 本地 `Build(GameObject)` 测量，或复用同候选、同相关依赖、同平台/SDK 的已有成功结果。禁止构建时记录“未构建”；上传任务可复用同候选的必要上传构建，不另增加构建。复用真实已存在的基线；缺少基线只报最终实测，没有对比值，不为普通任务强制补安装前 baseline。正在运行就等同一任务，不高频轮询/扫描，不因超时重复派发。`Build`、`Build & Test`、客户端验收、上传四种结果分开记录；这里不启动客户端或新增上传许可。
8. 清理本轮临时对象、场景、事件和读取的 AssetBundle，恢复预览。把真实结果写进当前 QuickTask 的 `performance_optimization`，保留旧反馈、草稿、资源、候选和原图身份。只有优化完成且必要受影响画面已实际检查才写 `state: complete`；只构建成功不能证明画面通过。

AAO 的默认追踪与高级删除说明见 [作者文档](https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/trace-and-optimize/)。高级零面积多边形删除可能影响 Shader/缩放动画，保守阶段不新增启用。Crunch 的磁盘压缩与 GPU 纹理内存是不同指标，见 [Unity 贴图格式说明](https://docs.unity3d.com/2022.3/Documentation/Manual/class-TextureImporterOverride.html)。包体下降不能冒充 GPU 内存下降。

## 构建统计的实际接口

沿用本工程 `VRC.SDK3A.Editor.IVRCSdkAvatarBuilderApi.Build(GameObject)` 和 SDK 构建事件。`TryGetBuilder` 依赖 SDK 控制面板打开时，打开已有面板即可；是否可构建以安装版本真实结果为准，不绕过登录/权限。回执记录成功终态、准确候选路径/哈希、平台、SDK 版本、实际 .vrca 路径/字节数；失败/进行中都不写完成。

SDK 回调没有统计时，复用本包 `scripts/unity/read_built_stats.cs`：用 Unity `AssetBundle.LoadFromFile` 读取成功构建的本地包，`LoadAsset<GameObject>` 后调用已安装 SDK `AvatarPerformance.CalculatePerformanceStats` 和参数 `CalcTotalCost`；finally `Unload(true)`。它读取实际输出，不需要第二次构建。动态 MCP 未直接引用 SDK 自定义程序集时，通过已安装程序集反射找到类型。

生成当前工程的 Editor 命令：

```text
python <SKILL>/scripts/optimization.py sdk-stats-command --project <PROJECT> --bundle <actual.vrca> --output <PROJECT>/.avatar-assembly/<TASK>/built-stats.json
```

把返回的 C# 交给当前已连接 Unity MCP `execute_editor_command`；核对内部 `executionSuccess`/异常，不以外层 success 判断。该命令不会启动构建、上传、客户端或修改源模型。模板绑定当前工程，拒绝覆盖已有统计，保留 bundle 哈希和平台/SDK信息。此模板复用已验证的 Windows PC SDK 读取方法，只用于 StandaloneWindows/StandaloneWindows64；Android 必须用对应平台的真实 SDK 统计，不套用 PC 字段。不同 Unity/SDK 版本接口不可用时说明缺失，不把旧字段或上次数字填进来。

用成功回执对比实际文件：

```text
python <SKILL>/scripts/optimization.py compare --project <PROJECT> --before <baseline-build.json> --after <final-build.json> --after-metrics <built-stats.json> --output <PROJECT>/.avatar-assembly/<TASK>/optimization-comparison.json
```

回调已含 metrics 时省略 `--after-metrics`；原基线也有侧车统计时用 `--before-metrics`。没有基线就直接报告真实最终回执，不调用 compare。脚本核对成功状态、实际包字节数、已有哈希、平台和提供的 SDK 版本，并计算有实际数据的变化；旧回执缺少候选/SDK绑定字段保持未知，不能重写旧回执补成“当时已验证”。脚本不认证图片、用户授权或 Avatar通过。

记录最少字段：`state`、`candidate_sha256`、实际 `aao_version`、候选/构建/统计/对比/画面文件路径、做了什么、未做什么及剩余限制。报告真实包体、SDK纹理内存、材质/网格/骨骼/PhysBone变化、参数bit与Rank；允许某项不变或增加。没有测客户端FPS、长时间物理和网络就写未测；Very Poor 不是降到别的等级，也不自动扩大用户未要求的目标。
