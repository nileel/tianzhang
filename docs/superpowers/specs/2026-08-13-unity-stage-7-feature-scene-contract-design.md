# Unity 阶段 7 Feature、正式场景与角色入口冻结设计

> 状态：设计内容与书面修订已批准（2026-08-13）
> 适用任务：`U-ARCH-REBUILD-01F2`、`U-ARCH-REBUILD-01G`
> 上位设计：`docs/superpowers/specs/2026-08-09-unity-modular-architecture-rebuild-design.md`

## 一、目的与结论

本设计只冻结阶段 7 开始前仍不唯一的实施契约、所有者迁移、场景组合、冒险节点扩展缝、存档角色入口和字面量路径，不实施源码迁移。

结论固定为：

1. `GameBootstrap` 是唯一跨场景长期应用总管；只有 `StartMenuScene` 序列化它。
2. 四个正式场景分别有一个 Bootstrap 程序集内的场景接线员；接线员只取得依赖并注入本场景 Feature，不保存业务状态。
3. Feature 不引用 Bootstrap，也不引用兄弟 Feature；跨场景导航和 Adventure／CombatPresentation 双向通信只走契约。
4. 角色创建不选择门派。新角色以未加入门派状态进入世界；以后从世界、城市或门派入口加入门派。
5. 开始菜单同时提供新建角色与已有角色存档入口。
6. 当前仓库只有 schema 1 JSON 序列化与原子恢复，没有本地存档槽。先建立独立前置 `U-ARCH-REBUILD-01F2`，完成本地存档槽后再解锁 `01G`。
7. Adventure 使用数据驱动节点和处理器注册表；首轮只实现当前薄切片需要的探索、遭遇和返回，后续节点不修改地图加载与输入主干。
8. 正式场景生成不再顺带生成或覆盖地块、单位和原型资源。

## 二、实时事实与根因

### 2.1 当前运行时与场景

- 五个目标 Feature 目录目前只有 asmdef 与 README，没有运行时代码。
- `WorldSceneController`、`SettlementSceneController`、`AdventureSceneController`、`ExplorationController`、`BattleUIManager` 和开始菜单控制仍在旧 `TianZhang.Gameplay` 路径。
- 四个正式场景仍序列化 `GameManager + SceneFlowManager`。
- `GameBootstrap.RequireRuntime()` 会查找或动态创建 Bootstrap，正式场景没有显式且唯一的注入入口。
- `SceneBuilder.cs` 同时承担正式场景、验证、Hybrid 原型、地块图片、Tile asset、单位 Prefab 和 UI 生成。
- Build Settings 已只有 `StartMenuScene`、`WorldScene`、`SettlementScene`、`AdventureScene` 四个正式入口；这一条件保持不变。

### 2.2 当前保存能力

- `GameSaveEnvelope` 与 `GameSaveSerializer` 已能捕获、序列化和恢复 schema 1。
- `GameRuntime.RestoreSave` 已先构建完整恢复结果，再原子替换当前状态。
- 当前没有本地目录、槽位 ID、槽位枚举、原子文件替换或损坏槽位读取结果，因此开始菜单无法列出已有角色。
- schema 1 的角色记录不含门派归属；删除创建时门派选择不需要保存格式兼容分支。

### 2.3 当前 Adventure 内容

- `guanzhong_wild` 已被据点入口与悬赏稳定 ID 引用，但当前 `ContentCatalogData` 没有 Adventure 地图定义。
- 当前正式 Adventure 的地形、单位、遭遇与 UI 由场景生成器和 `ExplorationController` 硬连接，新增资源点、事件或入口必须修改 Hub。
- 因此扩展根因不是缺少更多条件分支，而是缺少 Adventure 节点数据和节点处理器边界。

## 三、前置切片 `U-ARCH-REBUILD-01F2`

### 3.1 职责

`01F2` 只补本地 schema 1 存档槽适配器，不修改 Feature、Bootstrap、场景、UI、导航、保存信封或领域规则。

新增 `GameSaveSlotStore`，构造时显式接收存档目录。它提供：

- 按稳定槽位 ID 列出槽位摘要；摘要包含槽位 ID、角色 ID、角色显示名、最后写入时间、是否可读和稳定失败原因。
- 将 `GameSaveEnvelope` 写入指定槽位。
- 从指定槽位读取并反序列化 `GameSaveEnvelope`。
- 槽位 ID 只接受 ASCII 字母、数字、`_`、`-`，长度 1～64；任何路径分隔符、相对路径或越界路径均拒绝。
- 写入使用同目录临时文件与原子替换；替换失败时保留原槽位，尽力清理本次临时文件。
- 损坏 JSON、非 schema 1、缺少角色 payload 或 IO 失败返回稳定失败结果；不得覆盖当前 `GameRuntime`。
- 枚举按槽位 ID 的 ordinal 顺序返回，避免文件系统顺序影响 UI 和测试。

紧密相关的小型结果 DTO 与稳定失败原因可与 `GameSaveSlotStore` 同文件，避免为无独立生命周期的结构增加文件。

### 3.2 明确不做

- 不读取或迁移旧 schema。
- 不做云存档、多账号、删除／回收站、缩略图、自动备份、冲突合并或加密。
- 不直接调用 `GameRuntime.RestoreSave`；恢复原子性仍由现有 GameRuntime 所有。
- 不使用 `PlayerPrefs`、Resources、静默 fallback 或内存假槽位。

### 3.3 字面量修改路径

新建：

- `src/Assets/Scripts/Modules/Infrastructure/Persistence/GameSaveSlotStore.cs`
- `src/Assets/Scripts/Modules/Infrastructure/Persistence/GameSaveSlotStore.cs.meta`
- `src/Assets/Tests/EditMode/GameSaveSlotStoreTests.cs`
- `src/Assets/Tests/EditMode/GameSaveSlotStoreTests.cs.meta`

修改：

- `src/Assets/Scripts/Modules/Infrastructure/Persistence/README.md`
- `开发管理/任务列表/场景与Unity任务.txt`
- `开发管理/当前任务队列.txt`
- `开发管理/任务卡/U-ARCH-REBUILD-01F2.txt`
- `开发管理/任务归档/U-ARCH-REBUILD-01F2.txt`
- `开发管理/任务卡/U-ARCH-REBUILD-01G.txt`

除测试夹具显式临时目录外，不允许修改或生成项目内存档文件。

### 3.4 验证

- 新槽位写入／读取往返保持 envelope 内容。
- 同槽位覆盖成功后只读到新内容。
- 模拟替换失败时原文件逐字节不变。
- 非法槽位 ID 与路径穿越全部拒绝。
- 损坏 JSON、错误 schema、缺少玩家 payload 显示为不可读，且不阻止其他合法槽位枚举／读取。
- `GameSaveSlotStoreTests` 使用独立临时目录并在测试结束清理。
- `dotnet build src/TianZhang.EditModeTests.csproj`
- Unity EditMode 目标测试或全套 EditMode。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -TaskId U-ARCH-REBUILD-01F2 -Postcondition CodexDispatchReady -ExpectedRoute codex_execute -OutputJson`（制卡时）
- 完成归档后以 `CodexClosedOrNonReady` 核验，并重新判断 `01G`。

## 四、阶段 7 总体结构

### 4.1 应用总管与场景接线员

`GameBootstrap`：

- 只在 `StartMenuScene` 序列化一份并跨场景保留。
- 创建和持有唯一 `GameRuntime`。
- 不再自动创建第二个 Bootstrap；缺少总管时明确失败。
- 不按场景名承担 UI 或 Feature 分支。

四个场景接线员全部位于 `TianZhang.Bootstrap`：

- `StartMenuSceneInstaller`
- `WorldSceneInstaller`
- `SettlementSceneInstaller`
- `AdventureSceneInstaller`

每个场景只序列化一个对应接线员。接线员在启动时验证必需的总管、Feature 控制器、视图、目录和资源引用；任一缺失时禁用本场景交互并报告稳定错误，不动态补建组件。

### 4.2 注入合同

- `INavigationUseCase` 继续作为 WorldMap、Settlement、Adventure 的跨场景状态与目标合同。
- CharacterCreation Feature 定义局部 `IPlayerEntryHost`；`StartMenuSceneInstaller` 实现该合同并把自身注入 `StartMenuController`。Installer 负责创建新档、枚举／读取 schema 1 槽位并调用 GameRuntime；Controller 只持有和调用已注入合同，Feature 不认识 Bootstrap 或 Persistence 实现。
- `ICombatCommandHandler` 由新建的 Adventure `EncounterCoordinator` 实现，并注入 CombatPresentation。
- `Gameplay.Contracts` 新增纯 DTO 的 `ICombatPresentationSink`；Adventure 的 `EncounterCoordinator` 只向它发布战斗 HUD 快照、日志、回合和可用命令状态。
- `CombatHudPresenter` 实现 `ICombatPresentationSink`，是战斗 HUD 的唯一投影所有者；`AdventureHudPresenter` 只投影探索地图、节点选择与非战斗状态，不接收或重复显示战斗快照。
- `AdventureSceneInstaller` 同时看到 Adventure 与 CombatPresentation，实现命令与投影的双向接线；两个 Feature 不互相引用。
- Settlement 直接接收经 Bootstrap 创建的 `BountyUseCase`、`CharterUseCase` 与 `INavigationUseCase`，不接收 `GameRuntime`。
- Adventure 只接收当前角色只读投影、目录、导航、悬赏／奖励所需用例和 CombatPresentation 合同，不接收 `GameRuntime`。

## 五、角色入口与门派边界

### 5.1 开始菜单状态

StartMenu 的状态步骤分别验证，不合并成一个布尔值：

```text
菜单显示
-> 选择“新建角色”或某个已有槽位
-> 新建面板／槽位确认打开
-> 创建结果完成／槽位读取成功
-> GameRuntime 新建或原子恢复
-> 按导航状态进入目标正式场景
```

没有存档时显示新建角色入口和空列表；损坏槽位仍显示但不可进入，并给出稳定原因。

### 5.2 新角色

- `CharacterCreationController` 只处理角色身份、属性、灵根、出身及已批准创建字段。
- 旧 `SectSelectionManager` 不整体搬迁；其中真正属于角色创建的计算调用现有 `CharacterCreationCatalog`、`CharacterCreationRules` 与 `CharacterCreationManager`。
- 创建结果不含门派选择或门派归属。
- `StartMenuSceneInstaller` 收到完成结果后建立新 `GameRuntime`、写入指定新槽位并进入 World。
- 存档写入失败时不进入世界，留在创建完成界面并允许用户明确重试；不在后台无限重试。

### 5.3 已有角色

- `StartMenuSceneInstaller` 作为 `IPlayerEntryHost` 实现被注入；`StartMenuController` 只通过该已注入合同列出槽位摘要，不主动查找 Installer、Bootstrap 或存档实现。
- 用户选择可读槽位后，Installer 读取 envelope，并调用现有 `GameRuntime.RestoreSave(envelope, catalog)`。
- 恢复成功后按 `NavigationStateSnapshot` 进入 World、Settlement 或 Adventure；StartMenu 不是保存的游戏内返回目标。
- 恢复失败时留在菜单，原 GameRuntime 不产生半状态。

### 5.4 门派

- 创建界面不显示门派列表、门派功法预设或“选择门派”文案。
- 新角色默认没有门派归属；当前 schema 1 没有门派字段，不新增伪字段。
- 未来门派加入由 Adventure／World／Settlement 中的门派入口与独立用例实现；`01G` 只保留导航和节点扩展缝，不实现门派加入业务。

## 六、Feature 所有者迁移

本章所有“移动”统一执行同一 GUID 策略：源 `.cs` 与同名 `.meta` 在同一变更中一起移动，目标复用原 GUID，以维持场景、Prefab、asset 和测试引用。所有“删除”都必须先取得被删脚本的 `.meta` GUID，并在验收中证明 `.unity`、`.prefab`、`.asset`、`.controller`、`.anim` 无残留引用；不能只凭 C# 搜索判断安全。

### 6.1 CharacterCreation

移动：

- `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationCatalog.cs` -> `src/Assets/Scripts/Modules/Features/CharacterCreation/CharacterCreationCatalog.cs`
- `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationManager.cs` -> `src/Assets/Scripts/Modules/Features/CharacterCreation/CharacterCreationManager.cs`
- `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationModels.cs` -> `src/Assets/Scripts/Modules/Features/CharacterCreation/CharacterCreationModels.cs`
- `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationRules.cs` -> `src/Assets/Scripts/Modules/Features/CharacterCreation/CharacterCreationRules.cs`

原 `src/Assets/Scripts/Game/CharacterCreation.meta` 在目录清空后删除。

新建：

- `StartMenuController.cs`／`.meta`
- `StartMenuView.cs`／`.meta`
- `CharacterCreationController.cs`／`.meta`
- `CharacterCreationView.cs`／`.meta`
- `IPlayerEntryHost.cs`／`.meta`

以上路径均位于 `src/Assets/Scripts/Modules/Features/CharacterCreation/`。

删除而不保留兼容类：

- `src/Assets/Scripts/Game/SectSelectionManager.cs`／`.meta`
- `src/Assets/Scripts/Editor/SectSelectionSetup.cs`／`.meta`

### 6.2 WorldMap

移动：

- `src/Assets/Scripts/World/WorldSceneController.cs`／`.meta` -> `src/Assets/Scripts/Modules/Features/WorldMap/WorldMapController.cs`／`.meta`
- `src/Assets/Scripts/World/WorldNodeDefinition.cs`／`.meta` -> `src/Assets/Scripts/Modules/Features/WorldMap/WorldNodeDefinition.cs`／`.meta`

新建：

- `src/Assets/Scripts/Modules/Features/WorldMap/WorldMapView.cs`／`.meta`

`WorldMapController` 负责节点选择和导航命令；`WorldMapView` 只负责最小按钮与文本投影。

### 6.3 Settlement

移动到 `src/Assets/Scripts/Modules/Features/Settlement/`：

- `SettlementSceneController.cs` -> `SettlementController.cs`
- `SettlementSceneView.cs` -> `SettlementView.cs`
- `SettlementFeatureDispatcher.cs`
- `BountyBoardView.cs`
- `CharterSiteController.cs`
- `CharterSiteView.cs`

`SettlementController` 只通过注入的导航与 World 用例修改状态；Dispatcher 继续按稳定功能 ID 分发，不使用 placeholder 成功日志。

`src/Assets/Scripts/Settlement/SettlementDefinition.cs` 不迁入 Feature：实时引用检查表明 `SettlementFeatureDispatcher` 使用的是 `TianZhang.Content.SettlementFeatureData`，不引用 `SettlementDefinition`；该旧类型当前也没有 C# 或 Unity 序列化使用证据。它保留在旧 `TianZhang.Gameplay` 路径，作为 `01H` 的显式删除候选，不因名字相近扩大 `01G` 迁移范围。

### 6.4 册界规则协作者

`CharterSiteController` 仍直接依赖以下三份既有纯规则协作者。它们在 `01G` 中移入 `TianZhang.World` 模块，保持命名空间、类型签名、行为与 GUID 不变：

- `src/Assets/Scripts/World/CharterConflictRules.cs`／`.meta` -> `src/Assets/Scripts/Modules/World/CharterConflictRules.cs`／`.meta`
- `src/Assets/Scripts/World/CharterRuleRuntime.cs`／`.meta` -> `src/Assets/Scripts/Modules/World/CharterRuleRuntime.cs`／`.meta`
- `src/Assets/Scripts/World/CharterSiteInteractionRuntime.cs`／`.meta` -> `src/Assets/Scripts/Modules/World/CharterSiteInteractionRuntime.cs`／`.meta`

这三份文件只负责现有规则冲突、调用评估和据点交互准备，不新增长期状态所有者。现有 `CharterUseCase.CommitEvaluatedState` 只提交已经评估完成的状态，不能替代这条评估链，因此 `01G` 不删除它们，也不把它们错误改写成 `CharterUseCase` 的兼容分支。

### 6.5 Adventure

移动：

- `src/Assets/Scripts/Adventure/AdventureSceneController.cs` -> `src/Assets/Scripts/Modules/Features/Adventure/AdventureController.cs`
- `src/Assets/Scripts/Adventure/CharterEnvironmentProjection.cs` -> `src/Assets/Scripts/Modules/Features/Adventure/CharterEnvironmentProjection.cs`
- `src/Assets/Scripts/Adventure/FormalEncounterResult.cs` -> `src/Assets/Scripts/Modules/Features/Adventure/FormalEncounterResult.cs`

新建于 `src/Assets/Scripts/Modules/Features/Adventure/`，每项均包含 `.cs.meta`：

- `AdventureSession.cs`
- `AdventureMapLoader.cs`
- `AdventureUnitSpawner.cs`
- `AdventureInputController.cs`
- `AdventureNodeDispatcher.cs`
- `IAdventureNodeHandler.cs`
- `EncounterNodeHandler.cs`
- `ReturnNodeHandler.cs`
- `EncounterCoordinator.cs`
- `CombatEntryAdapter.cs`
- `AdventureHudPresenter.cs`

`AdventureUnitSpawner` 的输入来源冻结为：玩家单位来自 Installer 注入的当前角色只读投影；敌人单位由 encounter 节点的 `contentId` 通过 `ContentCatalogData.TryGetEnemy` 解析；出生六角坐标来自节点数据；可视 Prefab 来自场景中显式序列化的 `UnitMarker.prefab` 引用。未知敌人 ID、重复／非法坐标或缺失 Prefab 在 Adventure 会话启用前失败关闭；不得硬编码石甲兽 ID、使用 `Resources.Load` 兜底或运行时生成替代单位。

`AdventureHudPresenter` 只消费 Adventure 会话的地图、节点和探索状态；战斗开始后全部战斗 HUD 快照只经 `ICombatPresentationSink` 交给 `CombatHudPresenter`，两者不互相消费，也不共同写同一 HUD 元素。

旧 `ExplorationController` 不搬迁，拆分完成后删除：

- `src/Assets/Scripts/Map/ExplorationController.cs`／`.meta`
- `src/Assets/Scripts/Map/TianZhang.Gameplay.asmref`／`.meta`
- `src/Assets/Scripts/Map.meta`

### 6.6 CombatPresentation

新建于 `src/Assets/Scripts/Modules/Features/CombatPresentation/`，每项均包含 `.cs.meta`：

- `CombatHudView.cs`
- `CombatHudPresenter.cs`
- `CombatCommandInput.cs`
- `CombatLogView.cs`
- `CombatActionBarView.cs`

完成切换后删除 `src/Assets/Scripts/Game/BattleUIManager.cs`／`.meta`，不保留同名门面。

### 6.7 Bootstrap 与旧场景流

修改：

- `src/Assets/Scripts/Modules/Bootstrap/GameBootstrap.cs`
- `src/Assets/Scripts/Modules/Bootstrap/README.md`
- `src/Assets/Scripts/Modules/Bootstrap/TianZhang.Bootstrap.asmdef`

新建于 `src/Assets/Scripts/Modules/Bootstrap/`，每项均包含 `.cs.meta`：

- `StartMenuSceneInstaller.cs`
- `WorldSceneInstaller.cs`
- `SettlementSceneInstaller.cs`
- `AdventureSceneInstaller.cs`

删除：

- `src/Assets/Scripts/Game/GameManager.cs`／`.meta`
- `src/Assets/Scripts/Game/SceneFlowManager.cs`／`.meta`

`GameRuntime.cs` 不新增 Feature、UI、场景加载或存档槽职责；若实现发现必须修改其业务表面，停止并重审所有者。

## 七、Adventure 数据与扩展缝

### 7.1 数据定义

新增 `TianZhang.Content.AdventureMapData`：

- `adventureId`
- `displayNameKey`
- `contentScope`
- `AdventureNodeData[] nodes`

每个节点固定字段：

- `nodeId`
- `nodeTypeId`
- 六角坐标 `q`、`r`
- `contentId`

`nodeTypeId` 使用稳定字符串，不使用封闭 enum。首轮正式值只有：

- `adventure_node_start`
- `adventure_node_encounter`
- `adventure_node_return`

`guanzhong_wild` 至少包含唯一 start、石甲兽 encounter 和 return。节点 ID 在同一 Adventure 内唯一，坐标不得重复，`contentId` 必须由对应 handler 验证。

### 7.2 处理器注册

`AdventureNodeDispatcher` 接收显式处理器列表并按 `nodeTypeId` 建表：

- 重复注册、空 ID 或未知节点类型在加载阶段失败关闭。
- `AdventureMapLoader` 只校验地图结构并产生节点运行投影，不包含节点类型 switch。
- `AdventureInputController` 只把选中节点交给 Dispatcher。
- `EncounterNodeHandler` 调用 `EncounterCoordinator`。
- `ReturnNodeHandler` 调用导航返回。
- 未来资源点、事件、门派入口、城市入口、副本入口只新增数据与 handler；不得修改 Loader、Input 或 Session 主干。
- 城市、门派和副本入口通过导航／应用合同，不引用其他 Feature 实现。

### 7.3 内容链路径

新建并包含 `.meta`：

- `src/Assets/Scripts/Modules/Content/AdventureMapData.cs`
- `src/Assets/DataConfig/Adventures.csv`
- `src/Assets/DataConfig/Adventures.csv.meta`
- `src/Assets/Data/Adventures.meta`
- `src/Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset`
- `src/Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset.meta`
- `src/Assets/Scripts/Editor/AdventureContentImporter.cs`
- `src/Assets/Scripts/Editor/AdventureContentImporter.cs.meta`
- `src/Assets/Tests/EditMode/AdventureContentImporterTests.cs`
- `src/Assets/Tests/EditMode/AdventureContentImporterTests.cs.meta`
- `src/Assets/Tests/EditMode/AdventureNodeDispatchTests.cs`
- `src/Assets/Tests/EditMode/AdventureNodeDispatchTests.cs.meta`

修改：

- `src/Assets/Scripts/Modules/Content/ContentCatalogData.cs`
- `src/Assets/Scripts/Editor/WorldContentImporter.cs`
- `src/Assets/Scripts/Editor/ContentImportCoordinator.cs`（只登记 ImportAll 调用，不塞入 Adventure 解析逻辑）
- `src/Assets/Data/ContentCatalog/ContentCatalog.asset`
- `src/Assets/Tests/EditMode/ContentCatalogDataTests.cs`

`AdventureContentImporter` 自己读取并校验 `Adventures.csv`，通过现有 `CsvTableReader`／`ImportDiagnostics`／`AssetCommitter` 提交；SceneBuilder 不创建 Adventure 数据 asset。

## 八、SceneBuilder 拆分与场景持久化

删除 `src/Assets/Scripts/Editor/SceneBuilder.cs`／`.meta`，新建以下 Editor 文件及 `.meta`：

- `SceneBuildSupport.cs`
- `StartMenuSceneBuilder.cs`
- `WorldSceneBuilder.cs`
- `SettlementSceneBuilder.cs`
- `AdventureSceneBuilder.cs`
- `BuildSettingsRegistrar.cs`
- `SceneArchitectureValidator.cs`
- `HybridTacticalPrototypeBuilder.cs`

职责：

- 四个正式 builder 只生成自己的场景壳、最小 UI、接线员和显式资源引用。
- `SceneBuildSupport` 只放相机、EventSystem、Canvas 和序列化命名等无业务辅助。
- `BuildSettingsRegistrar` 只登记四个正式场景。
- `SceneArchitectureValidator` 只打开、检查并关闭场景，不重写文件。
- Hybrid builder 独立维护原型场景，正式 builder 和正式场景均不引用 Hybrid 类型或资源。
- `AdventureSceneBuilder` 只加载已提交的 AdventureMap、Tile、Sprite、Prefab、ContentCatalog、EnvironmentProfile 与 AttackProfile；缺失时失败，不生成或覆盖它们。

正式场景修改路径：

- `src/Assets/Scenes/StartMenuScene.unity`／`.meta`
- `src/Assets/Scenes/WorldScene.unity`／`.meta`
- `src/Assets/Scenes/SettlementScene.unity`／`.meta`
- `src/Assets/Scenes/AdventureScene.unity`／`.meta`
- `src/ProjectSettings/EditorBuildSettings.asset`

删除旧正式竞争入口：

- `src/Assets/Scenes/ExplorationScene.unity`
- `src/Assets/Scenes/ExplorationScene.unity.meta`

只读必需资源，禁止在本任务修改：

- `src/Assets/Resources/Tiles/AdventureGround.png`／`.meta`
- `src/Assets/Resources/Tiles/AdventureGround.asset`／`.meta`
- `src/Assets/Resources/Tiles/AdventureMoveHighlight.png`／`.meta`
- `src/Assets/Resources/Tiles/AdventureMoveHighlight.asset`／`.meta`
- `src/Assets/Resources/Tiles/AdventureAttackHighlight.png`／`.meta`
- `src/Assets/Resources/Tiles/AdventureAttackHighlight.asset`／`.meta`
- `src/Assets/Resources/Tiles/AdventureSelected.png`／`.meta`
- `src/Assets/Resources/Tiles/AdventureSelected.asset`／`.meta`
- `src/Assets/Resources/UnitMarker.png`／`.meta`
- `src/Assets/Resources/UnitMarker.prefab`／`.meta`
- `src/Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset`／`.meta`
- `src/Assets/Data/AttackProfiles/AttackProfile_basic_unarmed.asset`／`.meta`

`CharacterPresentationPrototypeBuilder.cs` 只更新对已删除正式类型的负向验证，不把原型接到正式 Feature。

## 九、程序集与测试路径

### 9.1 程序集

修改：

- 五个 `src/Assets/Scripts/Modules/Features/*/TianZhang.Features.*.asmdef`
- `src/Assets/Scripts/Modules/GameplayContracts/TianZhang.Gameplay.Contracts.asmdef`
- `src/Assets/Scripts/Modules/GameplayContracts/README.md`
- `src/Assets/Scripts/Modules/GameplayContracts/ICombatCommandHandler.cs`（只有合同注释／签名确需同步时）
- `src/Assets/Scripts/Editor/TianZhang.Editor.asmdef`
- `src/Assets/Tests/EditMode/TianZhang.EditModeTests.asmdef`
- `src/Assets/Tests/PlayMode/TianZhang.PlayModeTests.asmdef`
- `src/Assets/Tests/EditMode/AssemblyBoundaryEditorTests.cs`
- `src/Assets/Tests/EditMode/ArchitectureBaselineEditorTests.cs`
- `tools/check-unity-assembly-boundaries.ps1`（同步 01G 已落地的 Feature UI／Spatial 引用，并只对运行时程序集保留“仅 Bootstrap 可组合多个 Feature”的闸门；Editor 场景构建程序集可引用多个 Feature，但不得进入 Player）
- `UNITY_STRUCTURE.md`（同步正式 Feature、逐场景 Installer、拆分后的场景构建与验证入口）

新建并包含 `.meta`：

- `src/Assets/Scripts/Modules/GameplayContracts/CombatPresentationContracts.cs`
- `src/Assets/Scripts/Modules/GameplayContracts/CombatPresentationContracts.cs.meta`
- `src/Assets/Tests/EditMode/FeatureCompositionEditorTests.cs`
- `src/Assets/Tests/EditMode/FeatureCompositionEditorTests.cs.meta`

固定边界：

- Feature 不引用兄弟 Feature 或 Bootstrap。
- Gameplay.Contracts 只含合同和只读 DTO，不含实现工具或运行时所有者。
- Bootstrap 是唯一可以同时引用多个 Feature 实现的程序集。
- 旧 `TianZhang.Gameplay` 与其剩余 asmref 的最终删除仍由 `01H` 处理；`01G` 不为迁移代码新增对它的引用。

### 9.2 既有直接测试更新

以下测试因类型移动、场景所有者或端到端路径变化纳入 `01G` 精确路径；对应 `.meta` 保持不变：

- `src/Assets/Tests/EditMode/BountyBoardViewTests.cs`
- `src/Assets/Tests/EditMode/CharterEnvironmentProjectionTests.cs`
- `src/Assets/Tests/EditMode/CharterSiteViewTests.cs`
- `src/Assets/Tests/EditMode/CharacterCreationTests.cs`
- `src/Assets/Tests/EditMode/CharacterPresentationViewTests.cs`
- `src/Assets/Tests/EditMode/GuanzhongFormalEndToEndTests.cs`
- `src/Assets/Tests/EditMode/GuanzhongFormalUiTextTests.cs`
- `src/Assets/Tests/EditMode/HybridTacticalRendererTests.cs`
- `src/Assets/Tests/EditMode/NavigationContractsTests.cs`
- `src/Assets/Tests/EditMode/SceneArchitectureEditorTests.cs`
- `src/Assets/Tests/EditMode/TacticalGridModelTests.cs`
- `src/Assets/Tests/PlayMode/CharterVerticalSlicePlayModeTests.cs`
- `src/Assets/Tests/PlayMode/GuanzhongBasicAttackPlayModeTests.cs`
- `src/Assets/Tests/PlayMode/GuanzhongFormalUiTextPlayModeTests.cs`

允许把现有 PlayMode 文件改名为职责更准确的名字，但必须同轮移动原 `.meta`，并在任务卡中用实际源／目标字面量替换；不得复制第二套测试入口。

`CharterConflictRulesTests.cs`、`CharterRuleRuntimeTests.cs`、`CharterSiteInteractionRuntimeTests.cs` 与 `CharterSiteDataTests.cs` 作为册界迁移的直接回归集运行，但因类型命名空间、签名和测试程序集对 `TianZhang.World` 的引用均保持不变，预期不修改这些测试源码；只有实时编译证据证明必须修改时，才先回到任务路径审查，不预先制造无意义改动。

## 十、端到端验收

### 10.1 新建角色路径

```text
StartMenu 显示
-> 选择新建角色
-> 完成角色字段
-> 角色无门派归属
-> GameRuntime.BeginNewGame
-> schema 1 槽位写入成功
-> WorldScene
```

断言创建界面和场景中不存在门派选择文案、门派按钮或门派预设状态写入。

### 10.2 已有角色路径

```text
StartMenu 列出槽位
-> 选择可读角色
-> 读取 envelope
-> GameRuntime 原子恢复
-> 按 NavigationStateSnapshot 进入 World／Settlement／Adventure
```

损坏槽位、错误 schema、恢复引用无效时留在 StartMenu；原运行状态不被部分覆盖。

### 10.3 正式薄切片

```text
新建角色或读取已有角色
-> 世界
-> 据点
-> 接取悬赏
-> guanzhong_wild Adventure
-> 遭遇石甲兽
-> CombatPresentation 接收 HUD 快照
-> 玩家命令经 ICombatCommandHandler 执行
-> 战斗结算
-> 悬赏／掉落结果只消费一次
-> 返回原据点
-> 保存
-> 返回菜单并再次读取
-> 关键状态一致
```

### 10.4 Adventure 扩展证明

- 正式 `guanzhong_wild` 的 start、encounter、return 全部由数据创建。
- encounter 节点的敌人由 `contentId -> ContentCatalogData.TryGetEnemy` 解析，玩家由只读角色投影生成，坐标来自节点，Prefab 来自场景显式引用；不存在硬编码敌人或 Resources／运行时生成 fallback。
- 未知节点类型在加载阶段拒绝，地图不进入半初始化。
- 测试注册一个仅用于测试的节点 handler；在不修改 `AdventureMapLoader`、`AdventureInputController` 和 `AdventureSession` 的情况下完成选择与处理，证明未来资源点／事件／入口的扩展缝真实存在。

### 10.5 场景与结构

- 四个正式场景每个恰有一个对应 SceneInstaller。
- 只有 StartMenu 序列化一个 `GameBootstrap`；其余场景不序列化 Bootstrap。
- 四个正式场景没有 `GameManager`、`SceneFlowManager`、`ExplorationController`、`BattleUIManager` 或缺失脚本 GUID。
- 对本任务删除的每个脚本，删除前记录其 `.meta` GUID；删除后在 `.unity`、`.prefab`、`.asset`、`.controller`、`.anim` 中逐一检索为零，且 Unity 场景验证无 Missing Script。
- Build Settings 只启用四个正式场景且顺序固定。
- 重建两次正式场景后，场景与 ContentCatalog／AdventureMap／Environment／AttackProfile 引用保持一致。
- Validator 运行不改写任何正式 `.unity` 文件。
- Hybrid 与 Character Presentation Prototype 不在 Build Settings，也不被正式场景或正式 Feature 引用。

## 十一、验证命令

按相关输入变化运行最小充分集合：

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`
2. `dotnet build src/TianZhang.EditModeTests.csproj`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`
4. Unity PlayMode：三个既有正式 PlayMode 集合及角色入口／完整薄切片新增断言。
5. `SceneArchitectureValidator.ValidateForBatchMode`，并比较运行前后四个场景哈希。
6. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1`
7. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
8. 暂存前：把对应任务卡 `expectedPaths` 中本轮实际变化的路径逐项传给 `tools/check-pending-whitespace.ps1 -Paths`。
9. 暂存后：`git diff --cached --check`
10. 任务状态：`tools/check-task-cards.ps1` 对 `01F2`／`01G` 使用对应 postcondition。

结构检查同时报告：

- 新手写 `.cs` 是否超过 500 行。
- 旧 `ExplorationController`／`BattleUIManager` Hub 是否删除。
- 所有移动脚本是否复用原 `.meta` GUID，所有删除脚本 GUID 是否无序列化残留。
- Feature 兄弟引用、Feature -> Bootstrap、非 Bootstrap 多 Feature 引用是否为零。
- 新增 Adventure 节点是否通过数据和 handler 扩展，而非向 Loader／Input 增加类型分支。

## 十二、状态转换

本设计写入本身不改变任务状态。后续经用户明确要求实施管理更新时：

1. 当前 `01G.blockedBy=[]` 不是已存在的前置完成声明；当前阻塞事实仍由 `dispatchState`／`stateReason` 表达。在正式创建 `01F2` 前不得单独写入一个尚不存在的 blocker ID。
2. 同一次管理状态变更中原子完成：新建 `U-ARCH-REBUILD-01F2`（`priority=P1`、`route=codex_execute`、`owner=codex`、`stage=migration`、`dispatchState=ready`）、把 `U-ARCH-REBUILD-01G.blockedBy` 设置为 `["U-ARCH-REBUILD-01F2"]` 并更新状态原因、同步 backlog，最后按最高顺序把 `01F2` 写入空队列。不得让任务卡、backlog 与队列停留在互相矛盾的中间状态。
3. 同轮运行 `check-task-cards.ps1`，同时证明 `01F2` 可派发、`01G` 的 blocker 已存在且仍不可派发、队列投影一致。
4. `01F2` 完成归档后，实时复核本设计字面量与当前仓库；无新冲突时移除 blocker，把完整 `expectedPaths` 写入 `01G`，同步 backlog，并按 P1 顺序入队。
5. `01G` 实施过程中若实时发现新的必改路径，先判断是否属于本文已冻结职责；不属于时停止，不以兼容层或追加 Hub 扩大范围。

## 十三、停止条件

出现任一情况立即停止：

- 需要在创建角色时恢复门派选择或写入未批准的门派字段。
- 需要兼容旧 schema、建立云存档、多账号或第二保存所有者。
- Feature 必须引用兄弟 Feature 或 Bootstrap 才能工作。
- 正式场景必须依赖 Hybrid／Character Presentation Prototype。
- Adventure 新节点必须修改 Loader、Input 或 Session 的节点类型分支才能接入。
- 需要改变 Combat／BattleSim 数值、悬赏规则、册界规则或掉落语义。
- 需要保留 `GameManager`、`SceneFlowManager`、`ExplorationController`、`BattleUIManager` 兼容门面或双运行。
- 场景重建必须生成／覆盖已有正式资源，或无法保持目标 `.meta` GUID 与显式引用。
- 单次实施开始连续跨越本文未列出的领域边界或需要叠加 fallback、重试层和额外全局状态。
