# 参考项目与声明

交互与接口结构参考 [CokoIya / MioVRC_AssetManager](https://github.com/CokoIya/MioVRC_AssetManager)：需求 / 识图模型分别选择、自定义 API、素材图片浏览和截图记录。核对快照 `0b85646a60761460728087d65411ae8177ebaab0`，日期 2026-10-07。感谢作者公开项目供研究。

0.2.1 还参考其固定侧栏、深色与珊瑚色层级、图片优先的素材浏览，将交互重新实现为 Unity 原生 UI Toolkit。没有复制其样式文件、组件、图标、Logo 或截图；工作台图标与界面代码为独立实现。

本插件的 Unity 界面、适配、模型接口和图库为独立实现，没有复制 Mio 的 Go / 前端代码、打包其应用或引入其自动改模引擎。实际改模继续使用用户现有 assemble-vrchat-avatar 工作流。

该快照根目录未发现主项目 LICENSE，GitHub 主 license 字段为空。README 中 MIT 条目属于 go-webview2、UnitySkills 等第三方组件，不视为整个 Mio 的授权。后续若添加主许可，按届时文件另行核对。

Avatar Workbench 自身沿用本目录 MIT License。既有 BOOTH 后端来源、局部修改及 MIT 许可在 `Editor/BoothCli~/UPSTREAM.md` / LICENSE。代码许可不授予商品、用户模型、付费素材或截图的再发布权。

本插件与 Mio 作者、BOOTH / pixiv、VRChat 和 API 服务商无官方合作关系。API 请求送至用户配置的端点，费用和数据处理按服务条款；保存配置不证明认证、适配或模型修改成功。
