# TianZhang.Bootstrap

- 职责：唯一应用组合根、场景接线、应用生命周期与跨场景运行时组合。
- 公开入口：`GameBootstrap`、`GameRuntime` 与四个 `*SceneInstaller`。
- 允许依赖：Domain、Character、Content、Combat、Cultivation、World、Gameplay.Contracts、五个 Feature 与两项 Infrastructure。
- 禁止依赖：业务规则、状态机、UI 格式化、资源 fallback 和第二组合根。
- 运行时所有者：`GameBootstrap` 是唯一 Unity 生命周期根；`GameRuntime` 组合领域 store、应用用例、导航与保存快照。
- 数据／配置来源：四个正式场景 Installer 的显式序列化引用；本地槽位由 Persistence 提供。
- 直接测试：`GameRuntimeTests`、`GameSaveEnvelopeTests`、`SceneArchitectureEditorTests`、`FeatureCompositionEditorTests`。
- 常见修改路由：只修改创建、连接、导航和应用级组合；领域行为回到领域模块，显示回到 Feature。
