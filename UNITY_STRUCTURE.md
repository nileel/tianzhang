# Unity 结构事实图

> TQ-064 于 2026-07-12 基于当前工作树扫描生成。此文件记录当前路径与边界，不替代运行时行为测试或数据语义结论。

## 扫描范围与入口

- 已扫描：`src/Assets/`、`src/ProjectSettings/`、全部 `.unity`、`.asmdef`、`Assets/Scripts/**/*.cs` 与 `Assets/Tests/EditMode/**/*.cs`。
- 正式 Build Settings：`src/ProjectSettings/EditorBuildSettings.asset` 当前启用 `StartMenuScene`、`WorldScene`、`SettlementScene` 与 `AdventureScene`。
- 验证入口：`tools/run-unity-editmode-tests.ps1` 运行 EditMode；`src/Assets/Scripts/Editor/SceneBuilder.cs` 提供 `ValidateSceneArchitectureShellsForBatchMode`；`.NET` 编译入口为 `src/Assembly-CSharp.csproj` 与 `src/TianZhang.EditModeTests.csproj`。

## 场景与正式运行时链

| 场景 / 路径 | 当前职责 | 运行时所有者与证据 |
|---|---|---|
| `src/Assets/Scenes/StartMenuScene.unity` | 新游戏入口 | `GameManager`、`SceneFlowManager.PrepareNewGame` 与 `GameSession.BeginNewGame`。 |
| `src/Assets/Scenes/WorldScene.unity` | 世界节点选择 | `WorldSceneController` 写入当前世界节点，并经 `SceneFlowManager` 进入据点或冒险。 |
| `src/Assets/Scenes/SettlementScene.unity` | 据点入口 | `SettlementSceneController` 管理据点上下文并可进入冒险。 |
| `src/Assets/Scenes/AdventureScene.unity` | 唯一正式冒险入口 | `AdventureSceneController`；`SceneFlowManager.PrepareAdventureEntry` 返回该场景，`ReturnToSource` 走返回上下文。 |
| `src/Assets/Scenes/ExplorationScene.unity` | 旧可玩原型 | 不在 Build Settings。`ExplorationController` 仍持有六角网格、敌人和 `TacticalCombatController` 接入；不得作为第二个正式入口。 |

`GameSession` 是跨场景会话对象；`SceneFlowManager` 是场景跳转与进入/返回上下文的协调者。正式冒险链的限定依据见 `开发管理/运行时所有者记录-TQ-060.txt`，本次扫描确认其引用路径和 Build Settings 仍存在。

## Runtime 边界

| 路径 | 当前职责 | 关键所有者 / 依赖方向 |
|---|---|---|
| `src/Assets/Scripts/Game/` | 会话、流转、角色创建、UI 入口 | `GameSession`、`SceneFlowManager`、`GameManager`、`BattleUIManager`；目前仍是跨特性聚合区。 |
| `src/Assets/Scripts/Adventure/`、`World/`、`Settlement/` | 正式场景控制器 | 通过 `GameSession` 与 `SceneFlowManager` 共享进入和返回上下文。 |
| `src/Assets/Scripts/Map/`、`Grid/`、`Tilemap/` | 旧探索原型、格位模型与渲染 | `ExplorationController` 连接 `TacticalGridModel`、渲染和遭遇；迁移边界由 TQ-065 处理。 |
| `src/Assets/Scripts/Combat/`、`Core/`、`Entity/` | 单场战斗、CTB、角色领域数据 | `TacticalCombatController` 保留单场战斗所有权；`Character` 是实体侧领域对象。 |
| `src/Assets/Scripts/Cultivation/` | 修炼与功法成长运行时模型 | 依赖实体与内容数据，不应承接 UI/场景职责。 |
| `src/Assets/Scripts/Editor/` | 编辑器构建、数据导入与校验 | `SceneBuilder`、`DataConfigImporter` 等只允许 Editor assembly 引用 Runtime。 |

## Content 与序列化边界

- `src/Assets/Data/`：现有角色、功法、术法和神通 ScriptableObject 资产；运行时数据类型分布于 `Entity/CharacterData.cs`、`Combat/SpellData.cs`、`Combat/DivineSkillData.cs`、`Cultivation/GongFaGrowthData.cs`。
- `src/Assets/DataConfig/`：CSV 与 `README.txt` 的导入源；`Editor/DataConfigImporter.cs` 是编辑器侧入口。
- `src/Assets/Resources/`：Unity Resources 载入边界；具体资源应保持与 CSV/asset 链路检查一致。
- `src/Assets/Scenes/`、`src/Assets/Tests/` 与脚本相邻 `.meta` 均属于 Unity 序列化边界；后续重构不得改变 GUID、场景或预制体引用。

内容资产存在不等于字段运行时语义有效；`realmReq`、`elementReq` 与 `affiliation` 的可观察口径仍由 TQ-059 / G3 负责。

## Assembly 图

```text
TianZhang.Runtime
  └─ Assets/Scripts（当前所有 runtime 特性仍共享此 assembly）

TianZhang.Editor (Editor only)
  └─ references TianZhang.Runtime

TianZhang.EditModeTests (Editor only, UNITY_INCLUDE_TESTS)
  └─ references TianZhang.Runtime + TianZhang.Editor + NUnit
```

- 定义文件：`src/Assets/Scripts/TianZhang.Runtime.asmdef`、`src/Assets/Scripts/Editor/TianZhang.Editor.asmdef`、`src/Assets/Tests/EditMode/TianZhang.EditModeTests.asmdef`。
- 当前没有项目派生的 feature-level asmdef；TQ-067 负责在保持序列化兼容的前提下建立边界。

## 验证入口

| 入口 | 覆盖面 |
|---|---|
| `dotnet build src/Assembly-CSharp.csproj` | Runtime 脚本编译。 |
| `dotnet build src/TianZhang.EditModeTests.csproj` | EditMode 测试程序集编译。 |
| `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1` | Unity EditMode 全套；运行器会恢复追踪的 `Assets/**` 与 `ProjectSettings/**`。 |
| `SceneBuilder.ValidateSceneArchitectureShellsForBatchMode` | 正式场景壳与架构批处理校验。 |
| `src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs` | 场景流转、正式冒险入口和构建壳回归。 |

## Open gaps 与后续责任

1. `ExplorationController` / `SceneBuilder` 的旧原型职责尚未从正式链清晰分离：TQ-065。
2. `BattleUIManager` 与 `Character` 的 UI / 领域职责仍需拆分：TQ-066。
3. Runtime 仍是单一项目 asmdef；需在不破坏序列化的前提下建立 feature 边界：TQ-067。
4. `GameSession` 的世界时间与起点状态尚未成为已验证的最小一致模型：TQ-068。
5. `ExplorationScene` 仍保留为不可达旧原型；在正式 Adventure 接入其最小玩法前不得删除或重新启用。

## 可复扫命令

```powershell
Get-ChildItem src/Assets -Recurse -File -Include *.unity,*.asmdef,*.asmref
rg --files src/Assets/Scripts -g '*.cs'
rg -n --glob '*.cs' 'LoadScene|PrepareAdventureEntry|PrepareReturnToPreviousScene|BeginNewGame|GameSession' src/Assets/Scripts
Get-Content src/ProjectSettings/EditorBuildSettings.asset
```
