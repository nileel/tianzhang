# TianZhang.World

- 职责：世界时间、任务、背包、NPC、悬赏、册界与环境状态。
- 公开入口：`WorldClockService`、各子域 store、`InventoryGrantUseCase`、`BountyUseCase`、`CharterUseCase` 与 `CharterCommitService`。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`。
- 禁止依赖：Feature、Bootstrap、Editor、UI 和场景生命周期。
- 运行时所有者：各子域 store；`CharterStore` 的长期变更只经 `CharterCommitService`。
- 数据／配置来源：显式世界定义、稳定 ID 与操作输入。
- 直接测试：`AssemblyBoundaryEditorTests`、`WorldStateStoreTests`、`WorldApplicationUseCaseTests`、`CharterCommitServiceTests`。
- 常见修改路由：世界子域状态进入本模块，场景交互进入对应 Feature。
