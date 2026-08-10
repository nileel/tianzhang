# TianZhang.Spatial

- 职责：唯一六角坐标、格位、范围、视线与路径查询边界。
- 公开入口：`HexCoord`、`HexGrid`、`TacticalGridModel` 与 `SpatialQueryBoard` 的确定性空间值和查询结果。
- 允许依赖：`TianZhang.Foundation`。
- 禁止依赖：领域状态、Feature、Bootstrap、Editor 和表现。
- 运行时所有者：无；由调用模块持有查询输入与结果。
- 数据／配置来源：显式坐标、格位和阻挡输入。
- 直接测试：`HexCoordTests`、`HexGridTests`、`SpatialQueryBoardTests`、`TacticalGridModelTests` 与 `AssemblyBoundaryEditorTests`。
- 常见修改路由：通用空间算法进入本模块，场景渲染进入对应 Feature。
