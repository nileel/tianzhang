# TianZhang.Features.Settlement

- 职责：据点用例、交互控制器与最小视图。
- 公开入口：当前为空；迁移后通过 Gameplay.Contracts 导航与返回结果。
- 允许依赖：Foundation、Content、Character、World、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor。
- 运行时所有者：据点场景的用例与视图所有者；当前骨架不运行。
- 数据／配置来源：Content 据点定义、Character／World 只读状态。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后复用悬赏和据点测试。
- 常见修改路由：据点交互进入本模块，悬赏与册界状态进入 World。
