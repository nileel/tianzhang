# TianZhang.Infrastructure.UnityContent

- 职责：ScriptableObject、Resources 和 Unity 资产加载适配。
- 公开入口：`EnvironmentProfileAsset`、`SpatialQueryBoardFactory`、`SpatialQuerySnapshot`。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`。
- 禁止依赖：Feature 实现、Bootstrap、Editor 和领域写入者。
- 运行时所有者：无；调用 Feature 持有显式资产引用和生成的纯查询快照。
- 数据／配置来源：场景显式引用的 `EnvironmentProfileAsset`、`TacticalGridModel` 与稳定内容 ID；不按字符串扫描 Resources。
- 直接测试：`EnvironmentProfileDataTests`、`SpatialQueryBoardTests`、`TacticalGridModelTests`、`AssemblyBoundaryEditorTests`。
- 常见修改路由：ScriptableObject 到纯定义／Spatial 的适配进入本模块；schema 与目录契约留在 Content，加载调用留在 Feature／Installer。
