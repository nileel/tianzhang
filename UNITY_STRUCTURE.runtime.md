# Unity 正式运行时结构

> 审核完成；修改方：DeepSeek V4 Pro 0813；审核方：Codex；变更范围：保存链 schema 版本与所有者校正。

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
- `CharacterRuntimeProfile` 拥有角色身份、属性、资源、装载、成长引用与显式战斗修饰（`CharacterCombatModifiers`）。
- `CultivationState` 拥有修炼状态；`WorldClockService`、Quest／Inventory／Npc／Bounty／Charter store 分别拥有世界长期状态。
- `CultivationState` 对完整道基／紫府根状态只作玩家投影；`CultivationRecord` 与 NPC 的 `FoundationPurpleMansionSaveData` 以同一 schema 2 负载保存阶段编码、承载、圆满历史、五府、行动和结丹锁。
- `CharterUseCase.CommitEvaluatedState` 是册界长期提交入口（现行 `GameRuntime.Charters` 与正式 `CharterSiteController.SubmitFormal` 调用链）；`GameSaveEnvelope`／`GameSaveSerializer`／`GameSaveSlotStore` 负责 schema 3 快照和原子槽位文件适配。旧 schema 1／2 的玩家道基负载以 `FPM_LEGACY_STAGE_SCHEMA_INCOMPATIBLE` 拒绝，恢复在替换任一 live owner 前完成验证。
- `INavigationUseCase`、`NavigationStateSnapshot` 与 `SceneReturnTarget` 承载正式场景进入／返回。

## Adventure 与 Combat 路线

`AdventureSceneInstaller` 显式绑定内容目录、冒险地图、环境档案、单位 Prefab、攻击档案与视图；`AdventureController`／`EncounterCoordinator` 把纯快照交给 Combat。CombatPresentation 只经 `ICombatCommandHandler` 和只读表现 DTO 连接，不拥有战斗规则。

- `EnvironmentProfiles.csv.battlefieldCells` 是关中 6×6 正式战场的唯一手工地图输入；`WorldContentImporter` 确定性生成 170 条有向边并写入 asset。`CombatEntryAdapter` 逐项投影 `TacticalTileData`，`SpatialQueryBoardFactory` 消费同 asset 的边，`AdventureSceneBuilder.BuildGuanzhongBattlefield` 逐项保存 36 个显示地块，三者不再各自推断地图。
- `GuanzhongTerrainAssetBuilder` 仅在 Editor 生成批准地块/石阶的共享材质与Prefab。36 格均共享 `Assets/Art/Environments/Guanzhong/Tile.prefab`；四个 `blocksGroundMove` 格在 tile 下实例化 `Stair.prefab` 作为可见障碍，逻辑仍由 `TacticalTileData` 的不可地面进入／不挡视线语义拥有。保留 `HexToWorld` 的 x=q+.5r、z=.8660254r+1，地块根Y=.34、单位缩放；Prefab 内保留原FBX层级与已测刚体/正等比规范化，天然格心三角面不等于草冠 bounds.max.y。`VisualBaselineBoard`／`BattleVisualComparisonPanel` 仍是序列化比较 fixture，默认 inactive，不是正式格位或规则输入。Marker与固定相机保持既有所有者；初始地形接入不代表整屏或最终美术验收。
- `AdventureSceneBuilder.Build` 仅在Adventure关闭旧`VisualBackdrop`平面，避免Y=-.04的平面挡住延伸至Y≈-1.543的岩壁；共享`BeginScene`和其他场景不变。苻渊同地形图片由可选PlayMode取证临时克隆原模型子树，取证后销毁并恢复Marker/相机，不形成正式角色引用或第二条表现链。
- `AdventureCameraController` 由 Builder 显式配置正式 Camera 与 `GuanzhongBattlefield`，Adventure 局部正交 size=4。Input System 的 WASD／方向键按实际帧时长平移，中键以棋盘平面射线差值拖动；视线焦点按棋盘实际 renderer bounds 的 XZ 钳制，保持相机 Y 与朝向。UI 指针／输入框取得输入时停止相机操作；禁用、失焦清除拖动状态，不写战斗或跨场景状态。

- 关中移动的只读投影由 EncounterCoordinator 从同一 Session.SpatialQuery.FindReachable 与 ValidateCommand(Move) 生成：CombatMovementSnapshot 保存余额/阶段和原始路径/成本。CombatMovementView 仅从显式保存的正式格位、当前 Camera 射线和 EventSystem 做选格/高光/路径显示，本地选择直到确认才经 ICombatCommandHandler.RequestMove 提交。
- Static3DCombatUnitPresentationProvider 以固定4格/秒消费起点加完整相邻路径，根节点逐段转向/插值，保留body与底座几何；独立 ICombatPresentationActivity 把移动、动作恢复和反馈寿命的只读busy交给Coordinator。玩家直接命令、回合交接和胜负清理均等待同一演出；实验Sprite port不增加必实现成员。Installer与AdventureSceneBuilder.RebuildCombatMovement显式接线，不新增全局状态服务。

## 非正式 2D 合同实验

`src/Assets/Tests/Scenes/CombatPiece2DExperimentScene.unity` 不在 BuildSettings。`CombatPiece2DExperimentSceneBuilder` 只持久化实验相机、`BattleAnimationSpriteCombatUnitPresentationAdapter` 与既有 `FuYuan_BattleAnimationSprite.prefab` 引用；adapter 将 `ICombatUnitPresentationPort` 的准备、生成、事件、移除和清理投影为 2D pilot，不写 Combat、场景路由或保存状态。缺少实验 profile／Prefab／关键组件时在该入口失败关闭；不得接入 `AdventureScene`、`GameBootstrap` 或静态 3D provider。

## 验证提示

- `SceneArchitectureEditorTests` 与 `SceneArchitectureValidator.ValidateForBatchMode`
- `FeatureCompositionEditorTests`、`GameRuntimeTests`、`GameSaveEnvelopeTests`
- `GuanzhongFormalEndToEndTests` 与 `GuanzhongBasicAttackPlayModeTests.FormalFeatureSceneChainCreatesFightsClaimsSavesAndLoads`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-playmode-tests.ps1`
- `CombatPiece2DExperimentEditorTests` 验证 Builder 重建、场景重开与四个正式 BuildSettings 场景；`CombatPiece2DExperimentPlayModeTests` 用同一冻结 Prefab 输入覆盖六向、六事件和生命周期合同。

## 禁止修改

- 不恢复旧 Exploration／BattleUI Hub、原型场景或旧新双轨。
- 不在 Installer／Bootstrap 内加入业务规则或 fallback 内容。
- 不让 Feature 直接取得兄弟 Feature 实现或第二个长期状态写入者。

## 开放边界

本图只描述当前功能薄切片；渲染管线、3D 角色与正式美术属于独立视觉链。
