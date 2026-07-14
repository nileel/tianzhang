# 小时自动化写前失败收尾设计

> 2026-07-14 Codex：针对控制器 v2 上线后真实运行暴露的 `TaskKind` 参数映射与写前失败状态残留问题。本规格只加固写前边界；恢复证据和路径限定提交继续使用现有实现。

## 问题与根因

1. 控制器提示只要求在 `task_selected` 写入 `taskKind`，没有固定业务分支到脚本枚举的映射。模型把“执行任务”翻译成 `execution`，而 `automation-controller-state.ps1` 只接受 `execute`，导致选题登记在任何项目修改前失败。
2. `Fail` 对所有失败统一保留过期的 `RUNNING`。当失败发生在 `task_selected` 成功之前，状态中没有任务、预期路径或恢复证据，下一轮没有实际对象可恢复，却仍显示为运行中断。
3. 现有部署检查只验证提示包含 `task_selected`，没有验证四种合法 `TaskKind` 值及其映射；状态测试也只覆盖已有恢复对象的失败。

## 目标

- 普通执行、复审、维护和恢复分别固定使用 `execute`、`review`、`maintenance`、`recovery`。
- 在没有任务身份、预期路径和恢复证据时，`Fail` 视为写前失败：释放租约、回到 `IDLE`，保留 `lastError` 供诊断。
- 一旦已登记任务或恢复证据，继续沿用现有恢复计数、过期租约和 `AUTO-BLOCKED` 规则，不降低写入隔离强度。
- 部署检查必须拒绝缺少固定映射或把 `execution` 当成 `TaskKind` 的控制器提示。

## 设计

### 状态机

`Fail` 先判断是否存在可恢复工作。满足以下任一条件即继续走现有恢复路径：存在 `taskKind`、`taskId`、非空 `expectedPaths`、`recoveryBaselinePath`、`recoveryEvidencePath` 或 `recoveryEvidenceHash`。

若上述字段全部为空，则本轮尚未形成可恢复工作单元。`Fail` 将状态设为 `IDLE`，清除 `runId`、`leaseExpiresAt`、执行器和检查点等瞬时字段，保持 `recoveryCount = 0`，并保留截断后的 `lastError`。它不得创建恢复指针或增加恢复次数。

### 控制器提示与静态检查

控制器提示在选题登记步骤显式列出唯一映射：

- 执行：`execute`
- 复审：`review`
- 维护：`maintenance`
- 恢复：`recovery`

并要求写前 `Checkpoint` 失败时调用 `Fail`。`check-automation-workflow.ps1` 同时检查四个映射和禁止 `TaskKind` 使用 `execution`，使部署配置无法再次遗漏该契约。

### 当前残留状态

控制器保持暂停。代码部署后，以当前 `runId` 对现有“无任务、无预期路径、无恢复证据”的失败状态再次调用 `Fail`，由新语义原子收敛到 `IDLE`。不得用 `ResetBlocked`，也不得修改业务文件。

## 验证

1. 状态测试先复现写前 `Fail` 留下 `RUNNING`，再验证修复后变为 `IDLE`、租约释放、错误保留。
2. 保留已有“任务已登记后的 Fail 可恢复”和“两次恢复失败进入 AUTO-BLOCKED”测试，防止安全语义退化。
3. 工作流检查先证明旧提示因缺少固定映射失败，再验证项目规则、部署提示与检查器一致。
4. 运行现有状态、workspace guard、提交 helper 和工作流检查；不运行与控制面无关的 BattleSim 或 Unity 测试。

## 非目标

- 不接受 `execution` 作为兼容别名。
- 不新增状态 schema、第二控制器或通用编排 wrapper。
- 不改变恢复证据对 HEAD、路径外改动和目标文件指纹的严格校验。
- 不重新实现今天已经通过测试的多路径提交和恢复证据功能。
