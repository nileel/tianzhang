# TianZhang.Bootstrap

- 职责：唯一应用组合根，只创建并连接模块。
- 公开入口：`GameRuntime` 与唯一 Unity 组合根 `GameBootstrap`。
- 允许依赖：Domain、Character、Content、Combat、Cultivation、World、Gameplay.Contracts、五个 Feature 和两项 Infrastructure。
- 禁止依赖：业务规则、状态机、UI 格式化、资源 fallback 和第二组合根。
- 运行时所有者：`GameRuntime` 只组合模块 store、生命周期、保存与导航；算法留在模块用例。
- 数据／配置来源：场景 Installer 的显式序列化引用；存档槽位由 Persistence 提供。
- 直接测试：`GameRuntimeTests`、`ArchitectureBaselineEditorTests`、`AssemblyBoundaryEditorTests`、`FeatureCompositionEditorTests`。
- 常见修改路由：只增加创建与连接；任何业务行为回到对应模块。
