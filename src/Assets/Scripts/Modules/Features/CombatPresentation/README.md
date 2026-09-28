# TianZhang.Features.CombatPresentation

- 职责：战斗输入适配、HUD 状态展示与载体专属的棋子表现。
- 公开入口：`CombatCommandInput`、`CombatHudPresenter`、`CombatHudView`、`CombatActionBarView`、`CombatLogView`、`Static3DCombatUnitPresentationProfileSet` 与正式唯一 `Static3DCombatUnitPresentationProvider`；`BattleAnimationSpriteCombatUnitPresentationAdapter` 只在 `Assets/Tests/Scenes/CombatPiece2DExperimentScene.unity` 消费棋子生命周期／事件合同。
- 允许依赖：Foundation、Gameplay.Contracts 与 Unity UI。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor 和 Character 可写实现。
- 运行时所有者：AdventureScene 只序列化一个静态3D provider，它通过 `ICombatUnitPresentationPort` 接收已提交的生成、六事件、移除和清理投影；`CombatPiece2DExperimentScene` 是非 BuildSettings 的测试入口，不含 Adventure Installer 或 Bootstrap；两者均不拥有 CombatSession 或长期状态。
- 数据／配置来源：HUD 使用 `ICombatPresentationSink` 的只读 DTO；静态3D集合只保存稳定 profileId 到已批准 Prefab 及轴／尺度／接地／六向 QA 元数据，不回写 Content 身份目录；棋子提供器将消费 `ICombatUnitPresentationPort` 的生命周期和已提交结果投影；玩家输入仍经 `ICombatCommandHandler`。
- 直接测试：`FeatureCompositionEditorTests`、`GuanzhongBasicAttackPlayModeTests`、`CombatPiece2DExperimentEditorTests`、`CombatPiece2DExperimentPlayModeTests`。
- 常见修改路由：显示进入本模块；棋子表现不引用 Combat 实现，伤害、AI 与 CTB 留在 Combat。
