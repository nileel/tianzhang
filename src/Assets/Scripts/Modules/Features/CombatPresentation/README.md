# TianZhang.Features.CombatPresentation

- 职责：战斗输入适配、状态展示与最小战斗视图。
- 公开入口：`CombatCommandInput`、`CombatHudPresenter`、`CombatHudView`、`CombatActionBarView`、`CombatLogView`。
- 允许依赖：Foundation、Combat、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor 和 Character 可写实现。
- 运行时所有者：AdventureScene 中的表现组件；不拥有 CombatSession 或长期状态。
- 数据／配置来源：`ICombatPresentationSink` 的只读 DTO 与 `ICombatCommandHandler` 的显式玩家命令。
- 直接测试：`FeatureCompositionEditorTests`、`GuanzhongBasicAttackPlayModeTests`。
- 常见修改路由：显示进入本模块，伤害、AI 与 CTB 留在 Combat。
