# TianZhang.Editor

- 职责：内容导入、正式场景构建、编辑器诊断与批处理验证。
- 公开入口：`DataConfigImporter`、`SceneBuilder` 及其菜单／批处理入口。
- 允许依赖：现行 Foundation、Domain、Combat、Gameplay 和 Unity Editor API。
- 禁止依赖：被任何 Player 程序集引用，或承载运行时业务状态。
- 运行时所有者：Unity Editor；不进入 Player。
- 数据／配置来源：`Assets/DataConfig/`、编辑器选择和显式批处理参数。
- 直接测试：`SceneArchitectureEditorTests`、`ContentCatalogDataTests`、`AssemblyBoundaryEditorTests`。
- 常见修改路由：导入器、场景工具和诊断分别保持在专用编辑器类型。
