# TianZhang.Combat

- 职责：纯攻击解析、AI、CTB 战斗会话与结果。
- 公开入口：`CombatSession`、`CombatCommandService`、`CombatLegalActionService`、`CombatResultBuilder`、`CombatantSnapshot` 与 `ICombatSpatialQuery`。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Spatial` 与纯 Turns 程序集。
- 禁止依赖：Character 实现、World、Gameplay.Contracts、Feature、Bootstrap、Editor、Scene 或 UI。
- 运行时所有者：`CombatSession` 只拥有战斗快照与 CTB 会话；正式 Adventure 通过适配器创建并消费结果。
- 数据／配置来源：显式稳定 ID、角色战斗快照、攻击档案投影与 `ICombatSpatialQuery`。
- 直接测试：`CombatRuntimeKernelTests`、`TacticalGridModelTests`、`GuanzhongBasicAttackPlayModeTests`。
- 常见修改路由：伤害、AI、命令合法性与战斗结果进入本模块；场景流程进入 Adventure，显示／输入进入 CombatPresentation。
