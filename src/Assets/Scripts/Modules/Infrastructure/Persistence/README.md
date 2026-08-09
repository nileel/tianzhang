# TianZhang.Infrastructure.Persistence

- 职责：领域快照的序列化、版本迁移和持久化实现。
- 公开入口：当前为空；迁移后实现 Gameplay.Contracts 中的持久化端口。
- 允许依赖：Foundation、Content、Character、Cultivation、World、Gameplay.Contracts。
- 禁止依赖：Feature 实现、Bootstrap、Editor 和 UI。
- 运行时所有者：持久化适配器；领域模块仍拥有状态语义。
- 数据／配置来源：领域快照、schema 版本和显式存取请求。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后复用 `SessionStateSnapshotTests`。
- 常见修改路由：格式与迁移进入本模块，领域校验留在领域模块。
