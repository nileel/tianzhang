# TianZhang.Infrastructure.UnityContent

- 职责：ScriptableObject、Resources 和 Unity 资产加载适配。
- 公开入口：当前为空；迁移后提供 Content 目录所需加载实现。
- 允许依赖：`TianZhang.Foundation`、`TianZhang.Content`。
- 禁止依赖：Feature 实现、Bootstrap、Editor 和领域写入者。
- 运行时所有者：Unity 内容加载适配器；不拥有领域状态。
- 数据／配置来源：Unity 资产引用和经验证的稳定内容 ID。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后补资源解析测试。
- 常见修改路由：Unity 加载进入本模块，schema 与目录契约留在 Content。
