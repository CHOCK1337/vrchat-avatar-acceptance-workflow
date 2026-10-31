# 来源与许可

- 改模工作流：本包 `skills/assemble-vrchat-avatar/LICENSE`，MIT；保留其 THIRD_PARTY_NOTICES 和 XiaoboooOvO/VRChatEditorSkill 署名。
- Avatar Workbench：`unity/AvatarWorkbench/LICENSE`，MIT；素材图库和分类交互参考 **CokoIya/MioVRC_AssetManager**。保留 `MIO_REFERENCE.md`，元数据继续标注 `mio_library` / `mio_index`，本迁移未复制 Mio 的应用实现或数据库。该来源说明保留其核对日期，不声称是最新许可证审计。
- BOOTH 搜索：内含 wuhutakeoffyoo/booth-cli 本地适配、精确来源和 MIT 声明；见 `unity/AvatarWorkbench/Editor/BoothCli~/UPSTREAM.md`、LICENSE、BOOTH_NOTICE.md。代码许可不包含商品和封面授权。
- DeepSeek Harness：[官方仓库](https://github.com/deepseek-ai/deepseek-harness)，MIT，许可副本 `licenses/DeepSeek-Harness-LICENSE.txt`。运行时由 npm 安装，源码包不捆绑 node_modules。
- UnityMCP-VRC：[swax/UnityMCP-VRC](https://github.com/swax/UnityMCP-VRC)，**CC BY-NC 4.0**。本包依赖源码保留原 LICENSE 与署名，不能把它当 MIT 或据此宣称整包可不受限制地商用。新写的 DSH 适配器另用 MIT，不改变这个依赖的原许可。
- Unity、VRChat SDK、MA/NDMF、AAO、Shader、MochiFitter/作者工具保持各自许可，不随本包打包其已安装内容。

无官方合作或背书。用户的付费模型、照片、图像和私有配置不在本公开迁移 ZIP 中。

## 0.3.0 完整包增加的运行环境

- Node.js 24.15.0：官方 Windows x64 压缩包，保留 runtime/node/LICENSE、README 和所含第三方许可；[官方校验值](https://nodejs.org/download/release/v24.15.0/SHASUMS256.txt)。不改系统 PATH。
- CPython 3.13.16：官方 embeddable x64 包，保留 runtime/python/LICENSE.txt；只调整 python313._pth 以加载本包脚本；[官方版本清单与哈希](https://www.python.org/ftp/python/3.13.16/windows-3.13.16.json)。
- MCP SDK 1.29.0、js-yaml 4.3.2 及生产传递依赖：按锁文件安装，原包 LICENSE/NOTICE 保存在对应 node_modules 子目录；清单见 DEPENDENCY_INVENTORY.json。
- 完整包不包含 DSH 桌面程序或现有用户 profile；以上“运行时由 npm 安装”的历史段落描述旧源码包，0.3.0 起，支持运行环境已经离线带齐。

维护者应使用原始干净 ZIP 再分发。安装后的 local/rollback 可能含用户自己的配置备份，不得打包给别人。UnityMCP-VRC 仍为 CC BY-NC 4.0，新增 MIT 代码不会消除其非商业限制。
