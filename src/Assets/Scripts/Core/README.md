# TianZhang.Foundation

- 职责：现有通用六角坐标、空间查询和 CTB 基础类型；迁移前继续作为低层程序集复用。
- 公开入口：`HexCoord`、`SpatialQueryBoard`、`CTBEngine`。
- 允许依赖：无项目程序集依赖。
- 禁止依赖：Domain、Combat、Gameplay、Feature、Bootstrap、Editor。
- 运行时所有者：调用方持有实例；本程序集不拥有场景或 UI 生命周期。
- 数据／配置来源：显式构造参数，不读取 Unity 资源路径。
- 直接测试：`SpatialQueryBoardTests`、`TacticalGridModelTests`、`AssemblyBoundaryEditorTests`。
- 常见修改路由：通用原语留在本程序集；业务状态和表现进入对应模块。
