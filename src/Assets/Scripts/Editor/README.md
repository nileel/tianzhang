# TianZhang.Editor

- 职责：内容导入、正式场景构建、编辑器诊断与批处理验证。
- 公开入口：`ContentImportCoordinator`、六个领域 importer、四个正式场景 Builder、`SceneArchitectureValidator` 与 `Diagnostics/UnityRuntimeProbe`。
- 允许依赖：现行领域、Feature、Infrastructure、Bootstrap 与 Unity Editor API。
- 禁止依赖：被任何 Player 程序集引用、恢复宽泛 Gameplay、承载运行时业务状态或跨领域半提交。
- 运行时所有者：Unity Editor；不进入 Player。
- 数据／配置来源：`Assets/DataConfig/`、编辑器选择和显式批处理参数。
- 直接测试：`ContentImporterArchitectureTests`、各领域数据测试、`SceneArchitectureEditorTests`、`AssemblyBoundaryEditorTests` 与 `tools/test-get-unity-runtime-snapshot.ps1`。
- 常见修改路由：导入器、场景工具和诊断分别保持在专用编辑器类型。
