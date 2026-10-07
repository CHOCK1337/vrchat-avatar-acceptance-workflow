# 改模工作台 / Avatar Workbench · 0.2.1

Unity Editor 插件：中文任务绑定、真实模型预览、圈选反馈、多接口 / 模型切换、本地素材大图与独立 3D 对比、保留候选身份的截图记录。

0.2.1 重做原生 UI Toolkit 界面：固定左侧导航、图片优先的素材卡片、统一的深色与珊瑚色样式、始终可见的需求输入区。970×456 停靠区域也能选择角色、看模型、浏览素材、读取回复并发送；图库、设置和截图记录使用同一套样式。渲染、菜单、API、搜索及反馈后端沿用原实现。

从 [START_HERE.md](START_HERE.md) 开始。本插件只读用户当前安装的 `assemble-vrchat-avatar/SKILL.md`，沿用 QuickTask 和反馈收件箱，不改仓库原工作流规则、不恢复五问启动流程、不重配 Skill / MCP / 全局模型，不变更 Avatar / Packages / 上传权限。

API 模式读取需求、识图并回写建议；真实 Unity 修改继续交给原 Codex 工作流。没有内置新的后台 Agent、自动工具执行、购买或上传。

Editor-only；本轮实际验证 Windows / Unity 2022.3.22f1。UI Toolkit + PreviewRenderUtility 保留原材质。既有功能预览复用已安装的 Gesture Manager，仅作用于临时对象，缺少兼容接口时明确禁用。

- [CODEX_CONNECT.md](CODEX_CONNECT.md)：真实接入命令、回执和数据边界。
- [VERIFIED.md](VERIFIED.md)：本轮操作证据与未完成项。
- [MIO_REFERENCE.md](MIO_REFERENCE.md)：参考作者、来源与许可说明。
- [BOOTH_NOTICE.md](BOOTH_NOTICE.md)、[上游来源](Editor/BoothCli~/UPSTREAM.md)：原有 BOOTH 搜索声明和 MIT 许可。

安装器和校验清单位于上一层 integrations 目录。ZIP 只包含源码、安装器、文档与许可；实际 Unity 截图另交付给用户，不进入公开仓库。
