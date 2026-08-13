# TianZhang.Features.WorldMap

- 职责：世界地图选择、导航用例与最小视图。
- 公开入口：`WorldMapController`、`WorldMapView`、`WorldNodeDefinition`。
- 允许依赖：Foundation、Content、World、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor。
- 运行时所有者：WorldScene 的 `WorldMapController`／`WorldMapView`；长期世界状态仍在 World。
- 数据／配置来源：Content 世界节点定义与 World 只读状态。
- 直接测试：`FeatureCompositionEditorTests`、`SceneArchitectureEditorTests`、`GuanzhongFormalEndToEndTests`。
- 常见修改路由：地图交互进入本模块，世界状态进入 World。
