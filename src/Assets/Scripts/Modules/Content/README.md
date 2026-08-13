# TianZhang.Content

- 职责：不可变配置 schema、目录和稳定数据契约。
- 公开入口：`ContentCatalogData`、`AdventureMapData`、`EnvironmentProfileDefinition`、据点／敌人／物品／悬赏／册界定义与攻击／功法／术法／神通等 ScriptableObject schema。
- 允许依赖：`TianZhang.Foundation`。
- 禁止依赖：Editor 导入流程、运行时状态、Feature、Bootstrap。
- 运行时所有者：无；`ContentCatalogData` 只提供按稳定 ID 的只读查询。
- 数据／配置来源：`Assets/DataConfig/` 经领域 importer 验证生成的 `Assets/Data/` 与显式场景资产引用。
- 直接测试：`ContentCatalogDataTests`、各 schema／catalog 测试、`ContentImporterArchitectureTests`、`AssemblyBoundaryEditorTests`。
- 常见修改路由：schema 与只读目录进入本模块；CSV 读取／提交进入 Editor，Unity 资产适配进入 Infrastructure.UnityContent。
