# VRChat Avatar Acceptance Workflow

一个面向 VRChat / Unity 改模任务的可复用工作流。重点不是“生成很多报告”，而是把衣服、插件、菜单、胸部体型、穿模修复、性能优化和交付证据约束到同一个可验收流程里。

当前版本：`0.3.0-rc1`

## 解决的问题

- 在导入前一次性预检全部衣服、插件、依赖、菜单入口和性能影响。
- 对覆盖胸部的衣服强制检查 `small / default / large` 三种状态；缺少可信适配证据时直接拒绝进入安装。
- 默认优先复用作者/插件自带的部件菜单，避免根菜单堆满衣服按钮或出现平行的重复菜单树。
- 所有合格部件安装完成后，再做一次统一的视觉、姿势、菜单、行为、尺寸和性能验收。
- 区分静态检查、Unity 已解析状态、NDMF 构建、SDK 构建、VRChat 客户端实测和远端上传，避免把低层级证据误报成最终完成。
- 在不破坏作者源文件的前提下，把纹理、材质槽、未使用对象和平台预算纳入性能优化。
- 对锯齿/毛刺、爆炸网格、穿模、部件残留、错误鞋型/脚型和衣服切换回退进行明确的失败判定。

## 仓库内容

- [`WORKFLOW.md`](WORKFLOW.md)：完整执行规范和状态机。
- [`references/workflow.md`](references/workflow.md)：任务数据、阶段和统一批量验收规则。
- [`references/clothing.md`](references/clothing.md)：MA → MochiFitter → 手工适配的衣服路由。
- [`references/menu-planning-and-localization.md`](references/menu-planning-and-localization.md)：菜单层级、部件菜单优先和本地化规则。
- [`references/performance-optimization.md`](references/performance-optimization.md)：PC / Android 分平台性能契约和优化边界。
- [`references/acceptance.md`](references/acceptance.md)：视觉证据、穿模和最终验收标准。
- [`scripts/workflow.py`](scripts/workflow.py)：对已记录证据进行 fail-closed 判定的命令行工具。
- [`templates/AssemblyTask.example.json`](templates/AssemblyTask.example.json)：任务记录模板。

## 推荐执行顺序

1. 用五问启动卡一次性冻结：可视化方式、体积限制、内容/胸部/性能目标、菜单结构、交付与上传权限。
2. 只读盘点 Unity 工程、Avatar、平台、SDK、全部候选衣服和插件。
3. 对所有资源统一做预检；不合格资源先拒绝，不导入后再碰运气。
4. 依次安装已通过预检的资源，每件只做灾难性冒烟检查。
5. 冻结一个精确候选版本，统一测试全部衣服、部件、胸部状态、姿势、菜单回退、尺寸和性能。
6. 发现问题时回到最后一个已接受候选，只修复可见缺陷；同一方法两次无改善就切换方法。
7. 分层记录本地验收、SDK 构建、客户端实测和上传结果；任何一层都不能替代下一层。

详细规则见 [`WORKFLOW.md`](WORKFLOW.md)。

## 本地验证

需要 Python 3.10+，不依赖第三方 Python 包。

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s scripts/tests -p 'test_*.py' -v
python scripts/workflow.py -h
```

判定工具只验证记录结构和证据闭包，不会修改 Unity、不会调用模型、不会联网，也不会自动给出视觉 `PASS`。最终画面仍需由实际观察者在精确候选版本上验收。

## 数据与版权边界

仓库不包含模型、衣服、纹理、图片、视频、Unity 工程、账号凭据或付费素材。作者源文件应保持只读；修改只落到任务生成的副本或明确的 revision 中。

第三方设计来源和许可见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。本仓库自身采用 MIT License。
