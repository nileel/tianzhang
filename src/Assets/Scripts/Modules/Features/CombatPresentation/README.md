# TianZhang.Features.CombatPresentation

- 职责：战斗输入适配、HUD 状态展示与载体专属的棋子表现。
- 公开入口：`CombatCommandInput`、`CombatHudPresenter`、`CombatHudView`、`CombatActionBarView`、`CombatLogView`，以及隔离比较控制器。
- 允许依赖：Foundation、Gameplay.Contracts 与 Unity UI。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor 和 Character 可写实现。
- 运行时所有者：AdventureScene 中的表现组件；不拥有 CombatSession 或长期状态。
- 数据／配置来源：HUD 使用 `ICombatPresentationSink` 的只读 DTO；棋子提供器将消费 `ICombatUnitPresentationPort` 的生命周期和已提交结果投影；玩家输入仍经 `ICombatCommandHandler`。
- 直接测试：`FeatureCompositionEditorTests`、`GuanzhongBasicAttackPlayModeTests`。
- 常见修改路由：显示进入本模块；棋子表现不引用 Combat 实现，伤害、AI 与 CTB 留在 Combat。
