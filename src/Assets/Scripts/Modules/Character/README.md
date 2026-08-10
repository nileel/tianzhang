# TianZhang.Character

- 职责：角色身份、基础属性、资源、装载和只读快照。
- 公开入口：`CharacterRuntimeProfile`、六个单职责状态分件与 `CharacterStateSnapshot`。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`、`TianZhang.Spatial`。
- 禁止依赖：Cultivation 流程、Combat 实现、Feature、Bootstrap、Editor、UI。
- 运行时所有者：`CharacterRuntimeProfile`；它只组合身份、属性、资源、装载和成长引用，不持有 CTB、格位、冷却、场景或修炼状态。
- 数据／配置来源：显式角色定义与用例输入。
- 直接测试：`AssemblyBoundaryEditorTests`、`CharacterStateTests`。
- 常见修改路由：角色固有状态进入本模块，修炼与战斗由各自模块组合。
