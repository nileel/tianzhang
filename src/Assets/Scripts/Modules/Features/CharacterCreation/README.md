# TianZhang.Features.CharacterCreation

- 职责：开始菜单槽位选择、角色创建用例、点购规则、控制器与最小视图。
- 公开入口：`StartMenuController`、`CharacterCreationController`、`CharacterCreationManager`、`CharacterCreationRules`、`IPlayerEntryHost`。
- 允许依赖：Foundation、Content、Character、Cultivation、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor。
- 运行时所有者：StartMenu 场景中的 controller/view；玩家长期状态由 Bootstrap 创建的 Character／Cultivation owner 接管。
- 数据／配置来源：`ContentCatalogData`、显式 `CharacterCreationPointBuyConfig` 与玩家输入。
- 直接测试：`CharacterCreationRuleTests`、`FeatureCompositionEditorTests`、`SceneArchitectureEditorTests`、正式场景 PlayMode。
- 常见修改路由：槽位进入与角色创建流程进入本模块；角色固有状态进入 Character，保存进入 Persistence／Bootstrap。
