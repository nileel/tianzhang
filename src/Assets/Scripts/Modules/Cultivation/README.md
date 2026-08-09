# TianZhang.Cultivation

- 职责：道基、紫府、金丹和修炼状态机。
- 公开入口：当前为空；迁移后公开显式输入、结果和只读状态。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`、`TianZhang.Character`。
- 禁止依赖：Character 可写实现、Feature、Bootstrap、Editor、UI。
- 运行时所有者：迁移后的修炼状态所有者；当前骨架不写状态。
- 数据／配置来源：显式修炼定义、角色快照与操作输入。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后补状态机直接测试。
- 常见修改路由：修炼规则进入本模块，交互流程进入调用 Feature。
