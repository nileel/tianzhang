# TianZhang.Combat.Turns

- 职责：只以稳定单位 ID 表达的纯 CTB 排序与时间推进。
- 公开入口：`CTBEngine`。
- 允许依赖：无项目程序集依赖。
- 禁止依赖：Combat 会话、Character、Spatial、Feature、Bootstrap、Editor 或 UnityEngine。
- 运行时所有者：调用方持有 `CTBEngine`；根 Combat 的 `CombatTurnScheduler` 负责会话包装。
- 数据／配置来源：显式单位 ID、速度、当前 tick 与行动成本。
- 直接测试：`CombatRuntimeKernelTests`、`AssemblyBoundaryEditorTests`。
- 常见修改路由：CTB 排序与推进进入本模块；行动合法性、伤害和表现不进入。
