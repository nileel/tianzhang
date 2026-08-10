# TianZhang.Cultivation

- 职责：道基、紫府、金丹和修炼状态机。
- 公开入口：`CultivationState`、道基／紫府／守护能力／行动／闭关／金丹锁的独立状态与快照。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`、`TianZhang.Character`。
- 禁止依赖：Character 可写实现、Feature、Bootstrap、Editor、UI。
- 运行时所有者：`CultivationState`；它接受显式输入并只管理修炼状态，不取得 Character 的可写实现。
- 数据／配置来源：显式修炼定义、角色快照与操作输入。
- 直接测试：`AssemblyBoundaryEditorTests`、`CultivationStateTests` 与既有金丹证明测试。
- 常见修改路由：修炼规则进入本模块，交互流程进入调用 Feature。
