# TianZhang.World

- 职责：世界时间、任务、背包、NPC、悬赏、册界与环境状态。
- 公开入口：当前为空；迁移后按子域公开状态所有者和结果。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`。
- 禁止依赖：Feature、Bootstrap、Editor、UI 和场景生命周期。
- 运行时所有者：迁移后的各子域状态所有者；当前骨架不写状态。
- 数据／配置来源：显式世界定义、稳定 ID 与操作输入。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后复用悬赏、册界与存档测试。
- 常见修改路由：世界子域状态进入本模块，场景交互进入对应 Feature。
