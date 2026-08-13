# TianZhang.Domain

- 职责：既有金丹道证、位格、知识投影与快照纯规则边界。
- 公开入口：`JindanProofCoordinator`、`JindanPositionRegistry`、`DaoProofLedger`、`JindanProofSnapshot`。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`、`TianZhang.Spatial`。
- 禁止依赖：Character／World 可写状态、Combat、Feature、Bootstrap、Editor、Scene 或 UI。
- 运行时所有者：调用方分别持有 ledger、attempt、registry 与 coordinator；本程序集不拥有场景生命周期。
- 数据／配置来源：显式道证档案、行为事件、稳定 ID 与保存快照；源码经 `Scripts/Cultivation/TianZhang.Domain.asmref` 归属本程序集。
- 直接测试：`JindanProofAcceptanceTests`、`JindanProofCoordinatorTests`、`JindanPositionRegistryTests`、`JindanProofSnapshotTests`。
- 常见修改路由：仅修改既有金丹道证纯规则；通用修炼状态进入 `TianZhang.Cultivation`，内容 schema 进入 `TianZhang.Content`。
