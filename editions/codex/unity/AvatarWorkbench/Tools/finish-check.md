# 收尾检查与回复闸门

回复 `addressed` 之前必须在 Unity 跑一次收尾检查。回复工具（MCP `feedback_ack`、`codex_feedback.py ack`）会核对检查时间晚于这条请求；没有检查就拒绝，并返回 `FINISH_CHECK_REQUIRED`。检查结果会自动附到请求上，用户在工作台卡片上直接看到，所以回复里不要写与检查结果矛盾的结论。

本文件随插件装在工程的 `Assets/AvatarWorkbench/Tools/finish-check.md`，内容与 Skill 里的 `references/finish-check.md` 相同；复用的旧 Skill 里没有时读工程里这份。

## 调用

通过 `execute_editor_command` 执行，只替换 JSON。`candidate_prefab` 填本次交付的修改版本（处理后或编辑版均可）；场景里正在编辑的角色会一并检查。`base` 可省略，会从身体网格推断。

```csharp
using System;
using System.Linq;
using System.Reflection;
public class EditorCommand {
    public static object Execute() {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("AvatarWorkbench.WorkbenchFinishCheck"))
            .First(t => t != null);
        return type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { @"{""project"":""D:/实际工程"",""candidate_prefab"":""Assets/实际路径.prefab""}" });
    }
}
```

检查只读：不保存、不修改场景或资产。修好问题后再跑一次，用最新结果回复。

## 结果怎么处理

| 代码 | 级别 | 含义 | 处理 |
|---|---|---|---|
| `MULTIPLE_ACTIVE_AVATARS` | 问题 | 场景里同时有多个激活的角色，常见是测试用的处理后副本 | 删除测试副本，或停用并标成 EditorOnly，然后保存场景 |
| `UNSAVED_SCENE` | 问题/提醒 | 有角色的场景没保存（问题）；其他场景有未保存改动（提醒） | 保存交付所在的场景；临时场景不需要保留时说明即可 |
| `CANDIDATE_MISSING` | 问题 | 指定的修改版本不存在 | 核对路径，确认已经保存 Prefab |
| `NO_AVATAR` | 问题 | 场景和指定修改版本里都没有角色 | 传入正确的 `candidate_prefab` |
| `MISSING_SCRIPT` | 问题 | 组件脚本丢失 | 补装缺的依赖，或移除确认无用的空组件 |
| `MA_MERGE_ANIMATOR_EMPTY` | 问题 | Merge Animator 没有指定控制器（NDMF MA-1300），功能不会生效 | 按作者说明指定控制器；素材缺必要文件时如实写缺口 |
| `MA_TOGGLE_TARGET_EMPTY` | 问题 | MA 开关有空目标 | 补上目标或删除空行 |
| `EMPTY_SUBMENU` | 提醒 | 子菜单下面没有项目 | 确认是否故意留空 |
| `OTHER_BODY_MESH` | 提醒 | 网格来自其他体型的素材 | 对照请求里的 `compatibility.decision`；允许安装时在回复里说明贴合风险，否则移除 |
| `BODY_MASK_KEPT` | 问题 | 某个菜单状态（多半是新加的衣柜项）隐藏了原装衣物，但身体上给那件衣物准备的遮罩形态键还在生效，换装后会缺腿、缺腰或变形 | 在这个状态里把列出的形态键复位：给这套衣服的菜单项加 MA Shape Changer（Set，值 0）；新衣服自己需要遮罩时按新衣服的范围另设。改完在工作台实际切换这个状态和原装，看腿脚 |
| `BODY_MASK_UNCHECKED` | 提醒 | 没能推算换装后的身体遮罩 | 在工作台逐个切换衣服看腿脚，回复里写明 |

`BODY_MASK_KEPT` 是按菜单默认值和每个开关静态推算的：衣物和遮罩的对应关系取自原作者自己的开关动画（脱掉这件时把哪些形态键归零）和 MA Shape Changer。它能指出“哪个状态、哪些形态键”，最终是否贴合仍以工作台实际切换的图为准。

还有“问题”时，可以在回复里写明哪些没修好以及原因，再标 `addressed`；不要把未修好的写成完成。

## 请求里的体型决定

请求的 `compatibility` 字段来自工作台发送前的检查：所选素材在本地只有其他体型的版本。

- `decision = "skip"`：不要安装这些素材，回复里说明已跳过及原因，不要停下来再问一次。
- `decision = "install_anyway"`：照常安装，回复里说明贴合风险，收尾检查的 `OTHER_BODY_MESH` 提醒属于预期。
- 没有这个字段但安装时发现只有其他体型版本：停下，向用户说明并请用户在工作台重新选择。

## Unity 不可用时

确实无法连接 Unity（未打开、工程不对）时，回复备注里写“未运行收尾检查：<原因>”，回复工具会放行，并在工作台上显示“收尾检查：未运行”。
