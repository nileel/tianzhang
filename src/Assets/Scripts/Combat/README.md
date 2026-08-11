# TianZhang.Combat

- 职责：现有攻击解析、伤害、AI、CTB 战斗会话与结果。
- 现行生产入口：`CombatResolver`、`DamageCalculator`、`TacticalCombatController`、`AttackProfileData`。阶段 5 的 01E1 不切换它们；旧 Controller 仍是唯一生产可达路径。
- 纯内核：`CombatSession`、`CombatantSnapshot`、`CombatAttackProfile`、`CombatCommandService`、`CombatActionResolver` 与 `CombatResultBuilder` 只接受稳定 ID、快照、攻击档案投影和 `ICombatSpatialQuery`。它们不持有 Character、Scene、MonoBehaviour、UI 或日志对象。
- CTB：`Turns/TianZhang.Combat.Turns.asmdef` 只包含稳定 ID 的纯 `CTBEngine`；根 Combat 的 `CombatTurnScheduler` 是其会话内包装，不注册生产调用方。
- 允许依赖：`TianZhang.Foundation`、现行 `TianZhang.Domain`、`TianZhang.Content`、`TianZhang.Spatial` 与纯 Turns 程序集。
- 禁止依赖：Gameplay、Feature、Bootstrap、Editor 和 UI 实现。
- 直接测试：`CombatRuntimeKernelTests` 覆盖 1v1／2v2、CTB、伤害、范围、拒绝原因、结果投影及纯度；既有 `AttackProfileDataTests`、`SpellDamageMultiplierTests`、`GuanzhongFormalEndToEndTests` 继续覆盖旧生产路径。
- 常见修改路由：战斗规则留在 Combat；跨场景和显示进入契约或 Feature。
