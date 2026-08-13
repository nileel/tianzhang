# TianZhang.Features.Adventure

- 职责：Adventure 进入／返回、地图与单位装载、节点分派、遭遇协调、正式结算与最小 HUD。
- 公开入口：`AdventureController`、`AdventureSession`、`EncounterCoordinator`、`AdventureMapLoader`、`AdventureUnitSpawner`、`CombatEntryAdapter`。
- 允许依赖：Foundation、Content、Character、World、Combat、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor。
- 运行时所有者：AdventureScene 的 `AdventureController`／`AdventureSession`；CombatSession 由遭遇期间创建，长期结果经 World 用例提交。
- 数据／配置来源：`AdventureMapData`、`EnvironmentProfileAsset`、角色／世界快照、攻击档案、单位 Prefab 与显式进入上下文。
- 直接测试：`AdventureNodeDispatchTests`、`CharterEnvironmentProjectionTests`、`TacticalGridModelTests`、`GuanzhongFormalEndToEndTests`、`GuanzhongBasicAttackPlayModeTests`。
- 常见修改路由：冒险流程进入本模块，战斗规则留在 Combat。
