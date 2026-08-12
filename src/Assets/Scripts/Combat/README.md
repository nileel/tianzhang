# TianZhang.Combat

- 职责：现有攻击解析、伤害、AI、CTB 战斗会话与结果。
- 正式生产入口：Gameplay 的 `ExplorationController` 只以 `CombatSession`、`CombatCommandService`、`CombatLegalActionService` 和 `CombatResultBuilder` 组合战斗。UI 经 `TianZhang.Gameplay.Contracts.ICombatCommandHandler` 传递稳定 ID、槽位／档案 ID 与坐标整数；Combat 不引用该契约程序集。
- 纯内核：`CombatSession`、`CombatantSnapshot`、`CombatAttackProfile`、`CombatCommandService`、`CombatActionResolver`、`CombatLegalActionService` 与 `CombatResultBuilder` 只接受稳定 ID、快照、攻击档案投影和 `ICombatSpatialQuery`。它们不持有 Character、Scene、MonoBehaviour、UI 或日志对象。
- 功法战斗数值：`CombatantSnapshot` 以 `GongFaId`、`RealmMultiplier`、当前生命与印记层数保存纯战斗状态；纯规则按现行生产语义计算玄感神魂加值、含弘／载物防御、守一／符胆／雷劫上限与雷劫倍率。受伤叠雷及行动后印记变化只发生在快照内，不回写 Character。
- 命令契约：基础攻击、术法、神通、防御、等待、移动、换法均由 `CombatSession.ValidateCommand` 作无副作用验证；`CombatCommandService` 只在成功后消费一次 CTB。移动和换法的 CTB 惩罚为 0；换入术法冷却固定为 60 tick。
- 空间边界：`ICombatSpatialQuery` 只提供范围、可达格和规范移动路径／代价。它接收会话占用快照；Combat 成功移动时只更新 `CombatantSnapshot.Position`，不写 Grid、Tilemap 或 SpatialQueryBoard。
- CTB：`Turns/TianZhang.Combat.Turns.asmdef` 只包含稳定 ID 的纯 `CTBEngine`；根 Combat 的 `CombatTurnScheduler` 是其会话内包装，不注册生产调用方。
- 允许依赖：`TianZhang.Foundation`、现行 `TianZhang.Domain`、`TianZhang.Content`、`TianZhang.Spatial` 与纯 Turns 程序集。
- 禁止依赖：Gameplay、Feature、Bootstrap、Editor 和 UI 实现。
- AI：正式 `LegalActionAI` 只从 `CombatLegalActionService` 的合法命令集合选择行动；旧 `SimpleAI`、`TacticalCombatController`、旧 `CombatResolver` 与 Core CTB 仅为 01E3 的待删除遗留，不能由正式链调用。
- 直接测试：`CombatRuntimeKernelTests` 覆盖 1v1／2v2、CTB、伤害、范围、拒绝原因、移动、换法、七类合法行动、结果投影及纯度；`GuanzhongBasicAttackPlayModeTests` 覆盖正式 Gameplay 入口。
- 常见修改路由：战斗规则留在 Combat；跨场景和显示进入契约或 Feature。
