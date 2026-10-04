# 自动化 checkpoint 恢复 kind 合同修复设计

日期：2026-08-25

状态：已批准，待实施

## 问题与根因

普通 decision checkpoint 回复被接受后，共享入口会把 checkpoint 以 `cherry-pick --no-commit` 重放到新 run 的 owner worktree，并写入私有 resume context。当前生产者没有写入 `kind`，而 Codex candidate wrapper 在 `Set-StrictMode -Version Latest` 下直接读取 `$context.kind`。缺失属性触发 `PropertyNotFoundException`，最终被外层捕获器降级为 `codex_candidate_wrapper_error`，模型会话尚未启动便进入 `attention_required`。

现存失败 run 为 `c2a4ee66-9a27-4c78-8e4a-96c09ce17f3e`。其 HEAD 等于 base commit，candidate／canonical 证据均为空；worktree 中五个已暂存路径与原 checkpoint 的 `changedPaths` 精确一致。

## 目标与边界

本修复只完成三个结果：

1. 普通 checkpoint 和维护型决策使用显式、互斥的 resume context `kind`。
2. candidate wrapper 在访问分类型字段前验证 `kind`，缺失或未知值稳定返回 `codex_resume_context_invalid`。
3. 自动化测试覆盖普通 checkpoint 从上下文写入、checkpoint 重放到 candidate wrapper 接受该上下文的完整路径。

不新增兼容分支、重试层、恢复状态、第二 runtime 或后台任务；不修改任务业务内容、Unity、通知协议或现存私有 runtime 文件。

## 字段合同与数据流

普通 checkpoint 的 resume context 固定写入：

```text
schemaVersion = 1
kind = decision_checkpoint
taskId
decisionId
replyKind
replyValue
source
evidenceHash
checkpointCommit
checkpointChangedPaths
```

维护型决策继续使用既有 `kind = queue_maintenance` 合同。

`Read-ResumeContext` 先通过 `PSObject.Properties.Name` 检查 `kind` 是否存在，再只接受 `decision_checkpoint` 和 `queue_maintenance`。随后按 kind 检查既有必需字段与 route 约束；任何缺失、未知 kind 或类型／route 不匹配都经 `Stop-Candidate 'codex_resume_context_invalid'` 停止，不进入 runner。

普通 checkpoint 的 prompt、checkpoint 路径核验和候选提交合同保持不变。维护型决策分支行为保持不变。

## 修改所有者与允许路径

- `tools/invoke-hourly-owner.ps1`：`Apply-CheckpointToNewRun` 是普通 resume context 的唯一生产者，只增加 `kind = 'decision_checkpoint'`。
- `tools/invoke-codex-candidate.ps1`：`Read-ResumeContext` 是 resume context 的唯一入口，在属性访问前冻结 kind 合同；后续分支使用已验证 kind。
- 直接相关自动化测试：补普通 checkpoint 恢复链路和缺失／未知 kind 的失败断言。实施时优先扩展现有 `tools/test-invoke-codex-candidate.ps1` 与 checkpoint owner 测试；只有现有测试夹具无法表达生产者到 consumer 的链路时才新增一个聚焦测试文件。

禁止修改任务卡、队列、自动化规则、通知脚本、runtime schema、Unity 文件和失败 run worktree。

## 验证

最小充分验证必须证明：

1. 普通 checkpoint 生产出的 context 含 `kind = decision_checkpoint`。
2. checkpoint 重放路径和 task digest 核验仍通过。
3. candidate wrapper 接受合法普通 context 并到达 runner；不再返回 `codex_candidate_wrapper_error`。
4. 缺失或未知 kind 稳定返回 `codex_resume_context_invalid`。
5. `queue_maintenance` 既有测试保持通过。
6. 变更路径通过 `tools/check-pending-whitespace.ps1`、`git diff --check`、相关 PowerShell 测试和 `tools/check-automation-workflow.ps1`。

本修复不涉及 Unity 运行时或画面，不运行 PlayMode、Game view 或数值验证。

## 现存 run 的后续人工处置

代码修复集成并通过上述验证后，重新执行 schema 5 `Show`，再次核对 owner、runId、taskId、base、digest、branch、HEAD、五个重放路径、进程、candidate／canonical 空值和集成锁。

证据仍精确匹配时，使用 `CompleteRun` 的 `failed + attention_required` 精确关闭合同，并传入原 `recoveryReason`；关闭只释放 owner 占用，保留当前失败 worktree、原 checkpoint worktree、branch 和 resume context，不发送成功通知、不删除证据、不自动触发新一轮。若任一证据变化、出现 candidate／canonical 结果、路径不再精确匹配或锁异常，则停止处置并保留现场。

关闭失败 run 不等于完成 `D-CHAR-STATIC3D-MOTION-PIPELINE-01`。该业务任务的 checkpoint 内容和用户回复继续作为保留证据，后续业务收口不得冒充本次基础设施修复，也不得由定时器静默重试。

## 完成条件

- 字段生产者与 consumer 使用同一显式 kind 合同。
- 合法普通 checkpoint 的端到端回归测试通过；缺失／未知 kind 的稳定失败测试通过。
- 维护型决策回归无变化。
- 修复以独立 worktree 的路径限定提交集成到最新 `master`。
- 代码验证通过后，现存 run 按上述证据门完成精确关闭或因证据变化明确停止；不删除失败证据。
