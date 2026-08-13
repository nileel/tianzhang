# TianZhang.Features.Settlement

- 职责：据点进入／离开、功能分派、悬赏板、册界站点交互与最小视图。
- 公开入口：`SettlementController`、`SettlementFeatureDispatcher`、`BountyBoardView`、`CharterSiteController`、`CharterSiteView`。
- 允许依赖：Foundation、Content、Character、World、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor。
- 运行时所有者：SettlementScene 的 controller/view；悬赏、背包与册界长期状态仍在 World。
- 数据／配置来源：`ContentCatalogData` 的据点、悬赏与册界站点定义，以及 Character／World 只读状态。
- 直接测试：`BountyBoardViewTests`、`CharterSiteViewTests`、`FeatureCompositionEditorTests`、`GuanzhongFormalEndToEndTests`、正式场景 PlayMode。
- 常见修改路由：据点交互进入本模块，悬赏与册界状态进入 World。
