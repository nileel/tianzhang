# TianZhang.Features.CombatPresentation

- 职责：战斗输入适配、状态展示与最小战斗视图。
- 公开入口：当前为空；迁移后通过 Gameplay.Contracts 接收场景上下文。
- 允许依赖：Foundation、Combat、Gameplay.Contracts。
- 禁止依赖：兄弟 Feature、Bootstrap、Editor 和 Character 可写实现。
- 运行时所有者：战斗表现对象；当前骨架不运行。
- 数据／配置来源：Combat 只读结果和显式玩家命令。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后补表现适配直接测试。
- 常见修改路由：显示进入本模块，伤害、AI 与 CTB 留在 Combat。
