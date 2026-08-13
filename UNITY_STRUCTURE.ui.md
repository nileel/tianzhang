# Unity 正式 UI 与表现结构

## 何时读取

修改开始菜单、角色创建、World、Settlement、Adventure HUD、Combat HUD、TMP、按钮、输入或场景视图时读取。

## 主要路径

- CharacterCreation UI：`src/Assets/Scripts/Modules/Features/CharacterCreation/`
- World UI：`src/Assets/Scripts/Modules/Features/WorldMap/`
- Settlement UI：`src/Assets/Scripts/Modules/Features/Settlement/`
- Adventure UI：`src/Assets/Scripts/Modules/Features/Adventure/`
- Combat HUD：`src/Assets/Scripts/Modules/Features/CombatPresentation/`
- 视图生成：`src/Assets/Scripts/Editor/*SceneBuilder.cs`
- 正式场景：`src/Assets/Scenes/`

## 场景 UI 所有者

| 场景 | 控制／输入 | 视图／表现 |
|---|---|---|
| StartMenu | `StartMenuController`、`CharacterCreationController` | `StartMenuView`、`CharacterCreationView` |
| World | `WorldMapController` | `WorldMapView` |
| Settlement | `SettlementController`、`SettlementFeatureDispatcher`、`CharterSiteController` | `SettlementView`、`BountyBoardView`、`CharterSiteView` |
| Adventure | `AdventureController`、`AdventureInputController`、`EncounterCoordinator` | `AdventureHudPresenter`、`AdventureUnitSpawner` |
| Adventure 内战斗 | `CombatCommandInput` | `CombatHudPresenter`、`CombatHudView`、`CombatActionBarView`、`CombatLogView` |

每个 `*SceneInstaller` 只校验序列化引用并连接 controller/view。跨 Feature 战斗命令和表现数据经 `TianZhang.Gameplay.Contracts`；Combat 内核不引用 UI。

## 验证提示

- `SceneArchitectureEditorTests`、`FeatureCompositionEditorTests`
- `GuanzhongFormalEndToEndTests` 与正式薄切片 PlayMode
- 修改可见布局时同时检查场景序列化值、运行时 writer、Canvas／相机与实际 Game View。

## 禁止修改

- 不恢复 `BattleUIManager`、CharacterPresentation／Hybrid 原型或兄弟 Feature 直接调用。
- 不把布局、文本格式或输入处理放进 Combat／World／Character 等领域模块。
- 不用文档或 Editor 预览代替正式场景 PlayMode 证据。

## 开放边界

当前 UI 是功能薄切片；统一 URP 技术视觉、正式美术与 3D 角色不由本图定义。
