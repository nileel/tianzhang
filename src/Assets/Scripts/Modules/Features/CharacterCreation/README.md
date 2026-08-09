# TianZhang.Features.CharacterCreation

- 职责：角色创建用例、控制器与最小视图。
- 公开入口：当前为空；迁移后通过 Gameplay.Contracts 导航。
- 允许依赖：Foundation、Content、Character、Cultivation、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor。
- 运行时所有者：角色创建场景的用例与视图所有者；当前骨架不运行。
- 数据／配置来源：Content 定义与显式玩家输入。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后复用 `CharacterCreationTests`。
- 常见修改路由：只放角色创建流程，不接管角色领域状态。
