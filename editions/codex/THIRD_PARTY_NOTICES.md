# 第三方来源与许可

- 本发行版的新安装器、适配器改动与文档：根目录 LICENSE。保留各组件自己的许可证；整包许可不能概括为 MIT。
- 改模工作流：skills/assemble-vrchat-avatar/LICENSE（MIT）和该目录 THIRD_PARTY_NOTICES；保留 XiaoboooOvO/VRChatEditorSkill 署名。
- Avatar Workbench：unity/AvatarWorkbench/LICENSE（MIT）。素材索引交互参考 CokoIya/MioVRC_AssetManager，保留 MIO_REFERENCE.md；未捆绑其私人数据库。
- BOOTH 搜索：wuhutakeoffyoo/booth-cli 的本地适配，保留 Editor/BoothCli~/LICENSE、UPSTREAM.md 和 BOOTH_NOTICE.md。代码许可不包含商品或封面的授权。
- UnityMCP-VRC：swax/UnityMCP-VRC，CC BY-NC 4.0；完整原许可与署名在 dependencies/UnityMCP-VRC。新增 MIT 代码不改变该依赖的非商业限制。
- Unity Newtonsoft JSON 3.2.1：dependencies/com.unity.nuget.newtonsoft-json，保留 LICENSE.md（Unity Companion License）、Third Party Notices.md、原 package.json 与 DLL。该依赖供 Unity 工程使用。
- Node.js 24.15.0：runtime/node/LICENSE 和随附第三方许可；来源和官方压缩包哈希见 runtime/PROVENANCE.json。
- CPython 3.13.16 embeddable：runtime/python/LICENSE.txt；为本包脚本加载调整 python313._pth，不改系统安装或 PATH。
- MCP SDK 1.29.0、js-yaml 4.3.2 和随附 npm 生产依赖：保留各 node_modules 中原始许可证；版本和来源汇总见 DEPENDENCY_INVENTORY.json。setup 的 YAML 运行库保留用于来源追溯，Codex 安装器本身使用 Python 标准库 TOML。
- DSH 发行基础的来源许可保存在 licenses/DeepSeek-Harness-LICENSE.txt；本包不包含 DSH 或 Codex 桌面程序。

本包不包含 Unity 编辑器、VRChat SDK、MA/NDMF、AAO、Gesture Manager、Shader、拟合器、付费模型、用户图片、账号或私人 profile。由用户已有工程按各自许可提供。

没有官方合作或背书。再分发使用干净发行 ZIP；不得包含安装后 local/、工程 .avatar-workbench-codex、反馈和回滚备份。
