# Unity 正式运行时结构

## 何时读取

修改正式场景入口、运行时状态、导航、保存、Adventure／Combat 流程或 Bootstrap 接线时读取。

## 主要路径

- 正式场景：`src/Assets/Scenes/StartMenuScene.unity`、`WorldScene.unity`、`SettlementScene.unity`、`AdventureScene.unity`
- 组合根：`src/Assets/Scripts/Modules/Bootstrap/`
- Feature：`src/Assets/Scripts/Modules/Features/`
- 跨场景契约：`src/Assets/Scripts/Modules/GameplayContracts/`
- 长期状态：`src/Assets/Scripts/Modules/Character/`、`Cultivation/`、`World/`
- 保存适配：`src/Assets/Scripts/Modules/Infrastructure/Persistence/`

## 正式场景与所有者

| 场景 | Installer | Feature 所有者 |
|---|---|---|
| StartMenu | `StartMenuSceneInstaller` | `StartMenuController`、`CharacterCreationController` |
| World | `WorldSceneInstaller` | `WorldMapController` |
| Settlement | `SettlementSceneInstaller` | `SettlementController`、`SettlementFeatureDispatcher`、册界／悬赏控制器 |
| Adventure | `AdventureSceneInstaller` | `AdventureController`、`EncounterCoordinator`、`CombatPresentation` 组件 |

`StartMenuScene` 序列化唯一 `GameBootstrap`；其余场景不保存第二个 Bootstrap。`GameBootstrap` 只创建并保持 `GameRuntime` 与存档槽位适配器，缺失时失败关闭，不动态补建组合根。

## 状态与用例所有者

- `GameRuntime` 组合 Player、Cultivation、World 子域 store、导航、悬赏、背包授予、册界和 NPC 修炼用例；领域规则仍在各模块。
- `CharacterRuntimeProfile` 拥有角色身份、属性、资源、装载与成长引用。
- `CultivationState` 拥有修炼状态；`WorldClockService`、Quest／Inventory／Npc／Bounty／Charter store 分别拥有世界长期状态。
- `CharterCommitService` 是册界长期提交入口；`GameSaveEnvelope`／`GameSaveSerializer`／`GameSaveSlotStore` 负责 schema 1 快照与原子槽位文件适配。
- `INavigationUseCase`、`NavigationStateSnapshot` 与 `SceneReturnTarget` 承载正式场景进入／返回。

## Adventure 与 Combat 路线

`AdventureSceneInstaller` 显式绑定内容目录、冒险地图、环境档案、单位 Prefab、攻击档案与视图；`AdventureController`／`EncounterCoordinator` 把纯快照交给 Combat。CombatPresentation 只经 `ICombatCommandHandler` 和只读表现 DTO 连接，不拥有战斗规则。

## 验证提示

- `SceneArchitectureEditorTests` 与 `SceneArchitectureValidator.ValidateForBatchMode`
- `FeatureCompositionEditorTests`、`GameRuntimeTests`、`GameSaveEnvelopeTests`
- `GuanzhongFormalEndToEndTests` 与 `GuanzhongBasicAttackPlayModeTests.FormalFeatureSceneChainCreatesFightsClaimsSavesAndLoads`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-playmode-tests.ps1`

## 禁止修改

- 不恢复旧 Exploration／BattleUI Hub、原型场景或旧新双轨。
- 不在 Installer／Bootstrap 内加入业务规则或 fallback 内容。
- 不让 Feature 直接取得兄弟 Feature 实现或第二个长期状态写入者。

## 开放边界

本图只描述当前功能薄切片；渲染管线、3D 角色与正式美术属于独立视觉链。
