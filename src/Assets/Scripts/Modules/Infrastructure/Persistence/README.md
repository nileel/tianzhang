# TianZhang.Infrastructure.Persistence

- 职责：领域快照的 schema 1 序列化和原子恢复；不读取旧格式。
- 公开入口：`GameSaveEnvelope`、`GameSaveSerializer`、`GameSaveSlotStore`。
- 允许依赖：Foundation、Content、Character、Cultivation、World、Gameplay.Contracts。
- 禁止依赖：Feature 实现、Bootstrap、Editor 和 UI。
- 运行时所有者：持久化适配器；领域模块仍拥有状态语义。
- 数据／配置来源：领域快照、schema 版本，以及调用方显式提供的本地存档目录和稳定槽位 ID。
- 本地槽位：只保存 schema 1；槽位 ID 只允许 ASCII 字母、数字、`_`、`-`，写入通过同目录临时文件原子提交，损坏槽位以稳定失败结果保留在枚举中。
- 直接测试：`GameSaveEnvelopeTests`、`GameSaveSlotStoreTests`、`AssemblyBoundaryEditorTests`。
- 常见修改路由：格式与迁移进入本模块，领域校验留在领域模块。
