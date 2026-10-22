# 第三方说明

本包部分流程设计与 playbook 改写借鉴 XiaoboooOvO/VRChatEditorSkill。
Source: https://github.com/XiaoboooOvO/VRChatEditorSkill
核心验收工作流不捆绑其第二个 Skill 入口，也不包含 Modular Avatar、MochiFitter、AvatarAssembler Core 或任何付费模型/衣服。新增的 `editions/dsh` 与 `editions/codex` 分别保留完整工作台分发源码、UnityMCP-VRC 和其他必要公开依赖；对应许可见下文。
下面保留上游完整许可。用户素材和其他第三方组件各自受其原许可约束。

MIT License

Copyright (c) 2026 XiaoboooOvO

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## DSH / Codex 图形工作台分发

- [DSH 版来源、特别鸣谢与许可](editions/dsh/THIRD_PARTY_NOTICES.md)
- [Codex 版来源、特别鸣谢与许可](editions/codex/THIRD_PARTY_NOTICES.md)

两个版本各自保留 `licenses/`、`DEPENDENCY_INVENTORY.json` 和 UnityMCP-VRC 的源码来源说明。UnityMCP-VRC 使用 CC BY-NC 4.0 非商业许可；Unity Newtonsoft JSON 等依赖保留对应条款。仓库根目录的 MIT 许可不覆盖这些第三方许可，也不为任何用户素材授予权利。
