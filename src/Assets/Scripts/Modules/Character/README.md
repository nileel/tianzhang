# TianZhang.Character

- 职责：角色身份、基础属性、资源、装载和只读快照。
- 公开入口：当前为空；迁移后公开角色状态所有者和只读快照。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`、`TianZhang.Spatial`。
- 禁止依赖：Cultivation 流程、Combat 实现、Feature、Bootstrap、Editor、UI。
- 运行时所有者：迁移后的角色状态所有者；当前骨架不写状态。
- 数据／配置来源：显式角色定义与用例输入。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后补角色状态直接测试。
- 常见修改路由：角色固有状态进入本模块，修炼与战斗由各自模块组合。
