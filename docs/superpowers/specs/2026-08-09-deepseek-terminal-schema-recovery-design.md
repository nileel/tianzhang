# DeepSeek 结构化终态最小修复设计

日期：2026-08-09
适用范围：`tools/invoke-deepseek-responsibility.ps1`、对应直接测试、残留 run `66f8a2f3-d8e7-4074-a5a1-e123fe499f4a`

## 1. 问题与根因

任务 `C-HS-YY-JD-01Q` 的 DeepSeek 会话已创建候选提交 `d210976dad911076538d04e968b9966fadf0fe16`，worktree 干净，提交是 base `605288e32746033faefbc2226b1ee15a028357cc` 的直接后继，实际只修改任务授权的四个路径。

DeepSeek 返回的 `completed` 结构化终态遗漏 `changedPaths`。当前 `New-TerminalSchema` 虽声明该属性，却只把 `status` 列为必填，因此结构化输出层接受了不完整对象；随后 `Assert-CandidateEvidence` 要求 `changedPaths` 时以 `deepseek_invalid_terminal` 失败。共享入口把 run 置为 `attention_required`，后续小时轮次只返回 `existing_run`。

## 2. 目标与非目标

### 目标

- 在结构化输出边界按 `status` 强制对应必填字段，使不完整终态不能进入包装器后置校验。
- 保留包装器现有严格证据校验，不从 Git 猜测或补造模型遗漏字段。
- 使用 schema 5 既有精确失败关闭合同收口当前残留 run，并保留候选 worktree 与提交作为证据。

### 非目标

- 不修改任务内容、任务卡、队列、runtime schema、正式集成流程或通知语义。
- 不把普通失败候选转换成 decision checkpoint，不恢复旧模型会话，不直接合并 `d210976…`。
- 不新增重试、兼容字段、自动补值或独立恢复入口。

## 3. 方案

在 `New-TerminalSchema` 中保留现有属性定义与 `additionalProperties=false`，增加按 `status` 区分的条件必填合同：

- `completed`：要求 `identity`、`model`、`candidateCommit`、`expectedTransition`、`changedPaths`、`verified`、`unverified`、`residualRisk`、`result`、`impact`、`verify`、`plain`。
- `needs_decision`：要求 `identity`、`model`、`candidateCommit`、`changedPaths`、`verified`、`unverified`、`residualRisk`、`decisionId`、`question`、`options`、`recommendedOption`、`impactSummary`、`plainSummary`。
- `blocked` 或 `failed`：要求 `detailCode`。

包装器中的 `Assert-CandidateEvidence` 与 `Assert-DecisionCheckpoint` 保持不变，继续核对身份、模型、提交父链、分支、实际路径、报告路径、验证证据和元数据格式。Schema 只负责尽早拒绝缺字段，不替代事实校验。

不采用包装器自动从 Git 补 `changedPaths`，因为那会把模型合同缺失隐藏为成功；不只加强提示词，因为提示词不能确定性阻止同类遗漏。

## 4. 当前 run 处置

代码与测试正式集成后，重新核验以下现场：

- owner=`deepseek`、taskId=`C-HS-YY-JD-01Q`、runId=`66f8a2f3-d8e7-4074-a5a1-e123fe499f4a`；
- state=`attention_required`，原因仍为 `failed/deepseek_invalid_terminal`；
- runtime 中 `candidateCommit`、`candidateResult` 与 canonical 字段仍全部为空；
- worktree、candidate branch、HEAD `d210976…`、base `605288e…` 与 Git 现场一致；
- worktree 干净，候选提交为 base 的唯一直接后继；
- 集成锁空闲，主分支任务仍保持原 `external_execute/deepseek/ready` 事实。

全部匹配时，使用 `CompleteRun` 的既有 `emptyAttentionClose` 合同精确关闭，并传入当前完整 `ExpectedRecoveryReason`；不传 `ExpectedCandidateCommit`。这是因为 runtime 从未登记候选提交，不能伪装成 `candidateAttentionClose`。关闭只解除 owner 占用，不删除磁盘上的候选 worktree 或 branch，也不合并业务变化。关闭前后分别核验这些残留证据仍存在；任一证据变化都停止处置。

关闭后下一次 DeepSeek 小时入口从最新 `master` 创建全新 run。普通失败候选不具备 checkpoint 恢复资格，因此新 run 可能重新实施该任务；本修复不引入自动复用机制。

## 5. 错误处理

- 状态相关字段缺失：由 Claude CLI 的结构化 Schema 直接拒绝，不再进入候选证据阶段。
- 字段存在但内容、提交或路径不符：沿用现有稳定 detailCode 失败关闭，不新增兜底。
- 当前 run 关闭证据不一致：保留现场并报告，不清理、不重跑。
- 实施若需要修改 runtime、共享入口正式集成或任务生命周期，触发停止条件，重新设计。

## 6. 验证

最小充分验证：

1. 扩展 `tools/test-invoke-deepseek-responsibility.ps1`，证明 `completed` 缺少 `changedPaths` 时 Schema 拒绝，完整 `completed` 合同仍可通过；同时覆盖 `needs_decision` 与 `blocked/failed` 的必填分支。
2. 运行 `tools/test-invoke-deepseek-responsibility.ps1`。
3. 运行 `tools/check-automation-workflow.ps1`。
4. 对预期路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。
5. 正式集成并按 `emptyAttentionClose` 关闭残留 run 后重新调用 schema 5 `Show`，确认 `runs.deepseek=null`、Codex run 未被改变、集成锁空闲；另行确认原 worktree、candidate branch 与提交 `d210976…` 仍存在。

不运行 Unity、BattleSim 或数据链检查，因为相关输入不变。

## 7. 实施与提交边界

实施仅允许修改：

- `tools/invoke-deepseek-responsibility.ps1`
- `tools/test-invoke-deepseek-responsibility.ps1`
- 本设计文档

所有修改在独立 worktree 完成。合并前重新核验两个 owner run、集成锁、主分支 HEAD 与路径冲突，通过 `tools/invoke-project-integration.ps1` 持锁 fast-forward。不得纳入主工作区已有未提交改动。
