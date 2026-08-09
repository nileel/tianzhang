# TianZhang.Features.Adventure

- 职责：Adventure 进入、遭遇用例、返回与最小视图。
- 公开入口：当前为空；迁移后通过 Gameplay.Contracts 接收上下文并返回结果。
- 允许依赖：Foundation、Content、Character、World、Combat、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor。
- 运行时所有者：Adventure 场景的用例与视图所有者；当前骨架不运行。
- 数据／配置来源：Content 冒险定义、领域快照和显式进入上下文。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后复用正式关中端到端测试。
- 常见修改路由：冒险流程进入本模块，战斗规则留在 Combat。
