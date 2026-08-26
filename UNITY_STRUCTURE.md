# Unity 结构索引

## 项目与 Unity 版本

- Unity 项目根：`src/`
- Unity：`6000.3.18f1`
- 脚本根：`src/Assets/Scripts/`

## 正式场景入口

`src/ProjectSettings/EditorBuildSettings.asset` 当前启用：

1. `Assets/Scenes/StartMenuScene.unity`
2. `Assets/Scenes/WorldScene.unity`
3. `Assets/Scenes/SettlementScene.unity`
4. `Assets/Scenes/AdventureScene.unity`

## 聚焦结构路由

| 关注面 | 先读 |
|---|---|
| 程序集、asmdef／asmref、模块依赖 | `UNITY_STRUCTURE.assemblies.md` |
| 正式运行时、场景流、状态与保存 | `UNITY_STRUCTURE.runtime.md` |
| CSV、ScriptableObject、目录与导入链 | `UNITY_STRUCTURE.content.md` |
| 正式 UI、HUD 与场景视图 | `UNITY_STRUCTURE.ui.md` |

普通任务随后只读目标程序集旁的 `README.md`、目标逻辑单元与直接测试；发现真实跨模块依赖时再扩展。

## 验证入口

- 程序集边界：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`
- 条件快速编译（仅当前 worktree 已有生成投影时）：`dotnet build src/TianZhang.EditModeTests.csproj`
- 权威 Unity EditMode：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`
- Unity PlayMode：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-playmode-tests.ps1`
- 正式场景：`TianZhang.Editor.SceneArchitectureValidator.ValidateForBatchMode`

## 不可触碰区域

- 不手工维护 Unity 生成的 `.csproj`、`.sln`、`Library/` 或 `Temp/`。
- 不恢复已删除的旧场景、宽泛 `TianZhang.Gameplay` 或第二个正式 Adventure 入口。
- 改动场景、Prefab、asset 或脚本路径前先证明序列化 GUID 与运行时所有者。

## 最后核验日期

2026-08-27
