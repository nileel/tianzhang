# TianZhang.Combat

- 职责：现有攻击解析、伤害、AI、CTB 战斗会话与结果。
- 公开入口：`CombatResolver`、`DamageCalculator`、`TacticalCombatController`、`AttackProfileData`。
- 允许依赖：`TianZhang.Foundation`、现行 `TianZhang.Domain`。
- 禁止依赖：Gameplay、Feature、Bootstrap、Editor 和 UI 实现。
- 运行时所有者：`TacticalCombatController` 当前拥有单场战斗会话。
- 数据／配置来源：显式战斗输入及现行攻击／术法／神通数据对象。
- 直接测试：`AttackProfileDataTests`、`SpellDamageMultiplierTests`、`GuanzhongFormalEndToEndTests`。
- 常见修改路由：战斗规则留在 Combat；跨场景和显示进入契约或 Feature。
