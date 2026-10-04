# DeepSeek 小时入口最小纠偏设计

> 日期：2026-08-02
> 状态：已批准并实施
> 修订对象：`2026-08-01-independent-codex-deepseek-hourly-worktree-design`

## 目标与边界

本次只纠正 2026-08-02 故障处理中加入的过度恢复逻辑，不改变已经集成的业务提交、任务生命周期、双 owner runtime、worktree 或 canonical／integration 流程。

永久保留两项根因修复：

1. DeepSeek 固定入口按 owner 与规范化 `StateRoot` 持有单实例互斥；并发入口在读取 runtime 前返回 `occupied/deepseek_entry_running`。
2. DeepSeek candidate 提示词明确要求 finalizer 的四个精确单行格式，并继续由既有责任方校验拒绝不合规结果。

保留 `a588b3d` 的人工收尾能力：只有 runtime 未记录 candidate／canonical、没有 `integrationLease`，且调用方精确匹配原 `recoveryReason` 时，才允许把 `attention_required` run 以失败类别关闭。该动作不恢复、覆盖或重跑业务。

## 删除内容

- 删除 `hourly-automation-lease.ps1` 中 `attention_required → candidate_ready` 的特殊转换。
- 删除允许以同一 candidate SHA 覆盖 `candidateResult` 的恢复分支。
- 删除 runtime 中仅为上述恢复入口增加的 finalizer-ready 重复校验；正常责任方和正式 finalizer 已各自执行合同校验。
- 删除对应的事故专用测试、临时状态根和恢复规则说明。

不删除入口互斥测试、精确提示词测试、正常状态转换测试或人工失败关闭测试。

## 简化后的数据流与失败处理

正常自动路径仍为：

```text
developing → candidate_ready → canonical_ready → integrated → CompleteRun
```

`attention_required` 对自动入口保持终态：自动轮次只报告并退出，不返回正常运行状态、不重跑 DeepSeek、不覆盖候选结果。普通管理上下文只能在上述严格空证据条件下关闭 run；存在 candidate／canonical 或集成租约时必须保留现场并另行判断，不把事故恢复写入通用状态机。

并发入口由进程级互斥直接拒绝。元数据格式错误由 `invoke-deepseek-responsibility.ps1` 在写入 runtime 前拒绝；正式提交仍由 `automation-finalize-commit.ps1` 做最终合同校验。

## 修改范围

预计只修改：

- `tools/hourly-automation-lease.ps1`
- `tools/test-hourly-automation-lease.ps1`
- `开发管理/自动工作流恢复规则.txt`

如果删除逻辑后无需调整其他文件，不扩大范围。`invoke-deepseek-hourly.ps1`、`invoke-deepseek-responsibility.ps1`、业务文件、任务卡、队列和 runtime 状态均不修改。

## 验证与完成条件

- `test-hourly-automation-lease.ps1` 通过，并证明 `attention_required → candidate_ready` 被拒绝、严格空证据 attention run 仍可人工关闭。
- `test-invoke-deepseek-hourly.ps1` 通过，证明入口互斥仍生效。
- `test-invoke-deepseek-responsibility.ps1` 通过，证明精确元数据提示词与既有校验仍生效。
- `check-pwsh-runtime.ps1`、`check-automation-workflow.ps1`、`check-review-text.ps1`、目标路径空白检查和 `git diff --cached --check` 通过。
- diff 不包含业务路径、任务状态或新的 runtime 字段／动作／状态。

## 停止条件

如果删除特殊恢复后必须新增另一状态、动作、兼容分支、重试或持久化字段才能通过现有正常路径测试，立即停止并重新确认根因，不继续叠加补丁。
