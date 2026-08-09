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
| 程序集与模块边界 | 目标程序集旁的 `README.md`、`tools/check-unity-assembly-boundaries.ps1` |
| 当前正式运行时与场景流 | `src/Assets/Scripts/Game/` 及命中的 Adventure／World／Settlement 所有者与直接测试 |
| Combat | `src/Assets/Scripts/Combat/README.md` 与目标符号、直接测试 |
| Content 与导入 | `src/Assets/DataConfig/README.txt`、`src/Assets/Scripts/Editor/README.md` 与目标 schema／导入器测试 |
| Bootstrap | `src/Assets/Scripts/Modules/Bootstrap/README.md` |

现行 Player 代码仍由 Foundation、Domain、Combat、Gameplay 承载；`Modules/` 保存批准的目标程序集边界。实际行为以目标符号和直接测试为准。

## 验证入口

- 程序集边界：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`
- 快速编译：`dotnet build src/TianZhang.EditModeTests.csproj`
- Unity EditMode：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`
- 正式场景壳：`SceneBuilder.ValidateSceneArchitectureShellsForBatchMode`

## 不可触碰区域

- 不手工维护 Unity 生成的 `.csproj`、`.sln`、`Library/` 或 `Temp/`。
- 不把 `ExplorationScene.unity` 当成第二个正式 Adventure 入口。
- 改动场景、Prefab、asset 或脚本路径前先证明序列化 GUID 与运行时所有者。

## 最后核验日期

2026-08-09
