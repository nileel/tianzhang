# TianZhang.Content

- 职责：不可变配置 schema、目录和稳定数据契约。
- 公开入口：当前为空；迁移后公开只读定义和目录查询。
- 允许依赖：`TianZhang.Foundation`。
- 禁止依赖：Editor 导入流程、运行时状态、Feature、Bootstrap。
- 运行时所有者：无；只保存定义与只读投影。
- 数据／配置来源：经 Infrastructure／Editor 验证后提供的显式内容定义。
- 直接测试：`AssemblyBoundaryEditorTests`；迁移后补 schema 与目录测试。
- 常见修改路由：数据契约进入本模块，Unity 加载进入 Infrastructure.UnityContent。
