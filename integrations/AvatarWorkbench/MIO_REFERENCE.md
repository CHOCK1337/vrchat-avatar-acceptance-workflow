# 参考项目与声明

0.2.3 还核对 `internal/library/pkgcover.go`：Mio 会缓存安装包内原有 `preview.png`。本插件只读取用户已有 `pkgcovers.json` 与固定缓存目录中的 PNG/JPEG，明确标为包内 Unity 缩略图，不把它写成商品原图；不运行解压或导入。多图浏览分别记录来源，旧图片没有来源类型时如实说明。

交互与接口结构参考 [CokoIya / MioVRC_AssetManager](https://github.com/CokoIya/MioVRC_AssetManager)：需求 / 识图模型分别选择、自定义 API、素材图片浏览和截图记录。核对快照 `0b85646a60761460728087d65411ae8177ebaab0`，日期 2026-10-07。感谢作者公开项目供研究。

0.2.1 还参考其固定侧栏、深色与珊瑚色层级、图片优先的素材浏览，将交互重新实现为 Unity 原生 UI Toolkit。没有复制其样式文件、组件、图标、Logo 或截图；工作台图标与界面代码为独立实现。

0.2.2 参考其素材根目录管理、分享文本 / 提取码输入、网盘列表与本地目录关联方式，独立实现原生“读取目录 / 百度网盘”页面。目录图片来自用户选定范围的已有封面或 Unity AssetPreview。百度适配只提供匿名分享列表与手动下载后关联入口，未移植其账号登录、下载器或数据模型，不声称完整功能等同。

本插件的 Unity 界面、适配、模型接口和图库为独立实现，没有复制 Mio 的 Go / 前端代码、打包其应用或引入其自动改模引擎。实际改模继续使用用户现有 assemble-vrchat-avatar 工作流。

0.2.3 核对其 `internal/library/scan.go`、`views.go`、`thumbs.go` 与 `internal/booth/booth.go` 的实际信息来源：本地文件 / 说明、同名图片、BOOTH 商品 JSON 和封面缓存，各有来源优先级。新增对用户选择的 Mio `library.json` 的只读适配，提取商品名称、分类、适配标签、成员路径和已有 PNG/JPEG 图片，按明确的 BOOTH ID 合并记录；不读取服务凭据、不启动 Mio 下载器、不扫描数据库列出的目录。工作台自己的目录读取只在所选范围关联说明中的商品 URL 与已有图片，跳过纹理、色卡、二维码和广告候选。详情可按需调用既有 booth-cli 读取已明确关联的商品 JSON 和真实图片；缓存 / 标签均不证明已购、适配或构建通过。

该快照根目录未发现主项目 LICENSE，GitHub 主 license 字段为空。README 中 MIT 条目属于 go-webview2、UnitySkills 等第三方组件，不视为整个 Mio 的授权。后续若添加主许可，按届时文件另行核对。

Avatar Workbench 自身沿用本目录 MIT License。既有 BOOTH 后端来源、局部修改及 MIT 许可在 `Editor/BoothCli~/UPSTREAM.md` / LICENSE。代码许可不授予商品、用户模型、付费素材或截图的再发布权。

本插件与 Mio 作者、BOOTH / pixiv、VRChat 和 API 服务商无官方合作关系。API 请求送至用户配置的端点，费用和数据处理按服务条款；保存配置不证明认证、适配或模型修改成功。
