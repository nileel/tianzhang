# TianZhang.Gameplay.Contracts

- 职责：跨场景命令、事件、导航接口和只读 DTO。
- 公开入口：`INavigationUseCase`、`NavigationStateSnapshot`、`SceneReturnTarget`、场景名常量、`ICombatCommandHandler` 与 `ICombatPresentationSink`。
- 允许依赖：`TianZhang.Foundation`。
- 禁止依赖：实现工具、领域写入者、Feature 实现、Bootstrap、Editor。
- 运行时所有者：无；所有权留在发布者和处理者。
- 数据／配置来源：显式命令、事件和只读投影。
- 直接测试：`NavigationContractsTests`、`AssemblyBoundaryEditorTests`、`FeatureCompositionEditorTests`。
- 常见修改路由：只有真实跨模块通信才进入本程序集。
