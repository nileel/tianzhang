# TianZhang.Foundation

- 职责：项目最底层通用边界；当前没有 C# 实现，保留给真正跨模块的稳定原语。
- 公开入口：当前为空；六角坐标在 Spatial，CTB 在 Combat.Turns。
- 允许依赖：无项目程序集依赖。
- 禁止依赖：任何领域、Feature、Infrastructure、Bootstrap、Editor 或 Unity 场景实现。
- 运行时所有者：无。
- 数据／配置来源：无；未来入口只能接收显式值。
- 直接测试：`AssemblyBoundaryEditorTests` 与程序集边界脚本。
- 常见修改路由：只有被多个低层模块共同需要且不含业务语义的原语才进入；空间、回合和领域类型分别回到其模块。
