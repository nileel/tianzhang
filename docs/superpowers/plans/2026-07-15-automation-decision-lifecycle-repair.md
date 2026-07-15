# Automation Decision Lifecycle Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair the hourly controller's complete pending-decision lifecycle so project visibility, local state, notification evidence, reply recovery, Git finalization, and current blocked-state cleanup remain consistent.

**Architecture:** Keep `tools/automation-controller.ps1` as the only model-facing deterministic facade. Add a focused project-status publisher, extend the private state tool with receipt hashing and audited operator cancellation, and make decision creation/reply consumption re-enter the same workspace-guard and finalizer pipeline as ordinary tasks.

**Tech Stack:** PowerShell 7, JSON state, Git, Codex automation update API, existing workspace guard/finalizer tests.

---

## File structure

| File | Responsibility |
|---|---|
| `tools/automation-controller-state.ps1` | Private state schema, notification receipt hash, internal rollback, audited operator cancellation. |
| `tools/test-automation-controller-state.ps1` | Isolated state-transition regressions. |
| `tools/automation-decision-status.ps1` | Atomic rendering of the tracked `## 当前待决策` section only. |
| `tools/test-automation-decision-status.ps1` | BOM, newline, redaction, section-boundary, publish and clear tests. |
| `tools/automation-controller.ps1` | Model-facing decision preparation, publication, notification and reply-resume protocol. |
| `tools/test-automation-controller.ps1` | Real temporary-Git-repository lifecycle tests. |
| `tools/check-automation-workflow.ps1` | Static guarantees for the deployed decision contract. |
| `开发管理/自动工作流控制器提示词.txt` | Versioned thin runtime prompt. |
| `开发管理/自动工作流规则.txt` | Stable project policy for the repaired protocol. |
| `开发管理/自动工作流状态.txt` | Durable redacted control-plane result. |
| `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml` | Deployed prompt, updated only through the Codex automation API. |
| `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json` | Current blocked runtime state repaired after verification. |

### Task 1: Extend private state safely

**Files:**

- Modify: `tools/test-automation-controller-state.ps1`
- Modify: `tools/automation-controller-state.ps1:1-459`

- [ ] **Step 1: Add failing receipt and cancellation tests**

After the existing `MarkDecisionNotified` coverage, add assertions with the existing `Invoke-StateTool`, `Assert-Code`, and `Read-TestState` helpers:

```powershell
$missingReceipt = Invoke-StateTool @(
  'MarkDecisionNotified', '-StatePath', $statePath, '-RunId', 'run-7',
  '-Now', '2026-07-11T05:05:00Z'
)
Assert-Code $missingReceipt 15 'notification receipt required'

$receipt = 'gmail-message-18f00abc123'
$notified = Invoke-StateTool @(
  'MarkDecisionNotified', '-StatePath', $statePath, '-RunId', 'run-7',
  '-NotificationReceipt', $receipt, '-Now', '2026-07-11T05:05:00Z'
)
Assert-Code $notified 0 'mark decision notified with receipt'
$receiptHash = (Read-TestState).pendingDecision.notification.receiptHash
if ($receiptHash -notmatch '^[0-9a-f]{64}$' -or $receiptHash -eq $receipt) {
  throw 'notification receipt was not stored as a SHA-256 hash'
}

$cancelWithoutOverride = Invoke-StateTool @(
  'CancelDecision', '-StatePath', $statePath, '-RunId', 'run-7',
  '-DecisionId', $decision.decisionId, '-CancellationReason', 'duplicate decision'
)
Assert-Code $cancelWithoutOverride 15 'operator cancellation requires override'

$cancelled = Invoke-StateTool @(
  'CancelDecision', '-StatePath', $statePath, '-RunId', 'run-7',
  '-DecisionId', $decision.decisionId, '-CancellationReason', 'duplicate decision',
  '-ManualOverride', '-Now', '2026-07-11T05:06:00Z'
)
Assert-Code $cancelled 0 'operator cancellation'
$cancelState = Read-TestState
if ($null -ne $cancelState.pendingDecision -or
    $cancelState.lastDecisionCancellation.decisionId -ne $decision.decisionId -or
    $cancelState.lastDecisionCancellation.source -ne 'manual') {
  throw 'operator cancellation did not preserve a redacted audit record'
}
```

Add a separate fixture for `RollbackDecision` that creates a fresh pending decision, invokes rollback with the matching ID and reason `decision_status_publish_failed`, and asserts `source=controller_rollback`. This action is tested directly but is never added to the model-facing controller contract.

- [ ] **Step 2: Run the state tests and verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: FAIL because `NotificationReceipt`, `CancelDecision`, `CancellationReason`, and `RollbackDecision` are not accepted.

- [ ] **Step 3: Implement schema v5 and new transitions**

Expand the action `ValidateSet`, then insert `NotificationReceipt` and `CancellationReason` immediately after `NotificationError` and before `WasRecovery`:

```powershell
[ValidateSet('Acquire','Renew','Checkpoint','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure','CreateDecision','MarkDecisionNotified','MarkDecisionDeliveryFailed','ResolveDecision','ClearResolvedDecision','CancelDecision','RollbackDecision','Complete','Fail','Show','ResetBlocked')]
[string]$Action,
[string]$NotificationReceipt,
[string]$CancellationReason,
```

Set `schemaVersion = 5` and add this state property:

```powershell
lastDecisionCancellation = $null
```

Accept schema versions 1 through 5 in `Import-State`, then normalize to 5. Add a helper that returns lowercase SHA-256 without persisting its source:

```powershell
function Get-Sha256Text {
  param([string]$Value)
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
  try { ([Security.Cryptography.SHA256]::HashData($bytes) | ForEach-Object { $_.ToString('x2') }) -join '' }
  finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}
```

Require `NotificationReceipt` in `MarkDecisionNotified` and write:

```powershell
$state.pendingDecision.notification = [ordered]@{
  status = 'NOTIFIED'
  attemptedAt = $nowValue.ToString('o')
  attempts = $attempts
  error = $null
  receiptHash = Get-Sha256Text $NotificationReceipt.Trim()
}
```

Keep `receiptHash = $null` in delivery-failure notification records. Implement both cancellation actions through one helper:

```powershell
function Clear-DecisionWithAudit {
  param(
    [System.Collections.IDictionary]$State,
    [string]$ExpectedDecisionId,
    [string]$Reason,
    [string]$Source,
    [DateTimeOffset]$At
  )
  Require-PendingDecision $State
  Require-DecisionInput $ExpectedDecisionId 'DecisionId'
  Require-DecisionInput $Reason 'CancellationReason'
  if ($State.pendingDecision.decisionId -cne $ExpectedDecisionId) {
    Exit-WithCode 'DecisionId does not match the pending decision' $script:ExitInvalidArguments
  }
  $summary = $Reason.Trim()
  if ($summary.Length -gt 240) { $summary = $summary.Substring(0, 240) }
  $State.lastDecisionCancellation = [ordered]@{
    decisionId = [string]$State.pendingDecision.decisionId
    taskId = [string]$State.pendingDecision.taskId
    cancelledAt = $At.ToString('o')
    source = $Source
    reason = $summary
  }
  $State.pendingDecision = $null
}
```

`CancelDecision` requires owner, `-ManualOverride`, and an unresolved decision. `RollbackDecision` requires owner and a `PENDING` decision. Both renew the lease and export atomically.

- [ ] **Step 4: Run state tests and verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
git diff --check -- tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
```

Expected: `test-automation-controller-state: OK` and no whitespace findings.

- [ ] **Step 5: Commit the state slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller-state.ps1|tools/test-automation-controller-state.ps1'
git add -- tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
git diff --cached --check
git commit -m "fix(automation): harden decision state evidence"
```

### Task 2: Add the deterministic project-status publisher

**Files:**

- Create: `tools/test-automation-decision-status.ps1`
- Create: `tools/automation-decision-status.ps1`

- [ ] **Step 1: Write the failing publisher test**

Create a test that writes a BOM + CRLF fixture containing one `## 当前待决策` section and unchanged content before and after it. Encode this decision object as base64 JSON:

```powershell
$decision = [ordered]@{
  decisionId = 'DEC-20260715-ABCDEF123456'
  createdAt = '2026-07-15T03:20:19+08:00'
  taskId = 'TQ-057'
  taskSummary = '清理现存数据矛盾'
  question = '采用哪一条已批准口径？'
  options = @(
    [ordered]@{ key = 'A'; label = '补齐数据链' },
    [ordered]@{ key = 'B'; label = '登记精确豁免' }
  )
  recommendedOption = 'A'
  status = 'PENDING'
  notification = $null
}
$json = [pscustomobject]$decision | ConvertTo-Json -Depth 6 -Compress
$base64 = [Convert]::ToBase64String([Text.UTF8Encoding]::new($false).GetBytes($json))
```

Invoke `Publish`, then assert: the BOM is preserved; every newline remains CRLF; the decision ID, options, recommendation, status and strict reply example are present; neighboring sections are byte-for-byte unchanged; no email-like string is present. Invoke `Clear` and assert the section contains only `当前无待决策项。`. Add invalid JSON, missing heading and duplicate-heading cases that must fail without changing the original hash.

- [ ] **Step 2: Run the publisher test and verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
```

Expected: FAIL because `tools/automation-decision-status.ps1` does not exist.

- [ ] **Step 3: Implement the publisher**

Create `tools/automation-decision-status.ps1` with this public contract:

```powershell
[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Publish','Clear')]
  [string]$Action,
  [Parameter(Mandatory = $true)]
  [string]$StatusPath,
  [string]$DecisionJsonBase64
)
```

The implementation must:

1. read raw bytes and detect a UTF-8 BOM;
2. detect `\r\n` versus `\n` from the original content;
3. split into lines and require exactly one `## 当前待决策` heading;
4. locate the next `## ` heading without changing either heading;
5. validate the complete decision schema for `Publish`;
6. generate the redacted section below with no trailing spaces;
7. write a same-directory temporary file using the original BOM/newline policy and atomically replace the target.

The generated `Publish` body is:

```powershell
$body = [Collections.Generic.List[string]]::new()
$body.Add('')
$body.Add("- 决策编号：``$([string]$decision.decisionId)``")
$body.Add("- 关联任务：``$([string]$decision.taskId)`` — $([string]$decision.taskSummary)")
$body.Add("- 问题：$([string]$decision.question)")
foreach ($option in @($decision.options)) {
  $body.Add("- 选项 $([string]$option.key)：$([string]$option.label)")
}
$body.Add("- 推荐项：$([string]$decision.recommendedOption)")
$body.Add("- 创建时间：$([string]$decision.createdAt)")
$body.Add("- 通知状态：$([string]$decision.status)")
$body.Add("- 严格回复：``$([string]$decision.decisionId)：选 $([string]$decision.recommendedOption)``（也可选择其他单一选项）")
$body.Add('')
```

For `Clear`, use `@('', '当前无待决策项。', '')`.

- [ ] **Step 4: Run publisher tests and verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
git diff --check -- tools/automation-decision-status.ps1 tools/test-automation-decision-status.ps1
```

Expected: `test-automation-decision-status: OK` and no whitespace findings.

- [ ] **Step 5: Commit the publisher slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-decision-status.ps1|tools/test-automation-decision-status.ps1'
git add -- tools/automation-decision-status.ps1 tools/test-automation-decision-status.ps1
git diff --cached --check
git commit -m "feat(automation): publish decision status deterministically"
```

### Task 3: Repair the model-facing controller lifecycle

**Files:**

- Modify: `tools/test-automation-controller.ps1:1-616`
- Modify: `tools/automation-controller.ps1:1-1155`

- [ ] **Step 1: Add the failing end-to-end controller regression**

Extend the temporary Git fixture with a tracked BOM UTF-8 `开发管理/自动工作流状态.txt` containing the current empty section before the initial commit. Replace the current decision block with this sequence:

```powershell
# Start, InspectCandidate, RegisterCandidate and BeginMutation use the existing helpers.
$unprepared = Invoke-Controller @(
  'CreateDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
  '-TaskSummary', '选择控制器模式', '-DecisionQuestion', '采用哪一种模式？',
  '-DecisionOptions', 'A=模式甲|B=模式乙', '-RecommendedOption', 'A',
  '-ImpactSummary', '影响后续运行行为'
)
if ($unprepared.Code -eq 0 -or ($unprepared.Output | ConvertFrom-Json).errorCode -ne 'decision_context_not_prepared') {
  throw 'CreateDecision did not require prepared project context'
}

$prepared = Invoke-Controller @(
  'PrepareDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId
)
Assert-Code $prepared 0 'prepare decision'
$preparedJson = $prepared.Output | ConvertFrom-Json
if ($preparedJson.requiredSources -notcontains '开发管理/自动工作流状态.txt' -or
    $preparedJson.command.requiredParameters -notcontains 'DecisionOptions') {
  throw 'PrepareDecision did not expose context and command schema'
}

$created = Invoke-Controller @(
  'CreateDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
  '-TaskSummary', '选择控制器模式', '-DecisionQuestion', '采用哪一种模式？',
  '-DecisionOptions', 'A=模式甲|B=模式乙', '-RecommendedOption', 'A',
  '-ImpactSummary', '影响后续运行行为'
)
Assert-Code $created 0 'create and publish pending decision'
if ((Get-Content -Raw -LiteralPath (Join-Path $repo '开发管理/自动工作流状态.txt')) -notmatch 'DEC-[0-9]{8}-[A-Z0-9]+') {
  throw 'CreateDecision did not publish the project-visible status'
}

$missingReceipt = Invoke-Controller @(
  'MarkDecisionNotified', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId
)
if ($missingReceipt.Code -eq 0 -or ($missingReceipt.Output | ConvertFrom-Json).errorCode -ne 'notification_receipt_missing') {
  throw 'controller accepted notification success without provider evidence'
}

$notified = Invoke-Controller @(
  'MarkDecisionNotified', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
  '-NotificationReceipt', 'gmail-message-18f00abc123'
)
Assert-Code $notified 0 'mark notified and republish status'

$finished = Invoke-Controller @(
  'Finish', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
  '-CommitMessage', 'test: publish decision lifecycle'
)
Assert-Code $finished 0 'finish pending decision'
$afterDecision = Invoke-State @('Show', '-StatePath', $decisionStatePath)
if ($afterDecision.state -ne 'IDLE' -or $afterDecision.pendingDecision.status -ne 'NOTIFIED' -or
    $afterDecision.recoveryEvidencePath) {
  throw 'decision finish left recovery residue or lost the pending decision'
}
```

Add a second run that calls `ResolveDecisionReply` with the exact decision ID. Assert its result has `action=inspect_candidate`, `taskId=decision-task`, and `nextCommand=InspectCandidate`. Reinspect and reregister the original task with `task.txt|开发管理/自动工作流状态.txt`, begin mutation, change `task.txt`, and finish. Assert the commit contains both paths, the visible section is clear, and `pendingDecision` is null.

- [ ] **Step 2: Run the controller test and verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

Expected: FAIL on the missing `PrepareDecision` action or the absent project publication.

- [ ] **Step 3: Implement controller contracts and helpers**

Add `PrepareDecision` to the action ValidateSet and add:

```powershell
[string]$NotificationReceipt,
```

Define:

```powershell
$script:DecisionStatusTool = Join-Path $PSScriptRoot 'automation-decision-status.ps1'
$script:DecisionStatusRelativePath = '开发管理/自动工作流状态.txt'
```

Add helpers to compute a lowercase file SHA-256, invoke the publisher with base64 JSON, and return the complete decision command schema. `PrepareDecision` must verify `mutation_started`, registered status path, and a readable tracked status file; it stores `decisionContextHash` in the session without changing `session.phase`.

`CreateDecision` must reject a missing or changed `decisionContextHash`, call the state action, then publish the new decision. If publish fails, call private `RollbackDecision`, call state `Complete`, remove run artifacts, and return `decision_status_publish_failed` with `failurePolicy=stop_read_only`.

Update `MarkDecisionNotified` to reject empty `NotificationReceipt`, pass it to the state tool, republish the returned decision, and return `Finish`. Update `MarkDecisionDeliveryFailed` the same way without a receipt.

At the start of `Finish`, if the registered status path belongs to the pending decision's task:

```powershell
if ($null -ne $state.pendingDecision -and
    [string]$state.pendingDecision.taskId -ceq [string]$state.taskId -and
    $paths -contains $script:DecisionStatusRelativePath) {
  if ([string]$state.pendingDecision.status -eq 'RESOLVED') {
    Invoke-DecisionStatusPublisher 'Clear' $null
  } else {
    Invoke-DecisionStatusPublisher 'Publish' $state.pendingDecision
  }
}
```

After a successful post-commit Verify, clear a matching `RESOLVED` decision before `Complete`. If clearing local state fails, preserve the commit-completed recovery checkpoint and return a stable failure.

Change `ResolveDecisionReply` to save the resolved task ID in the session and return `InspectCandidate`. During `RegisterCandidate`, if the current pending decision is `RESOLVED` and matches the inspected task, require the project status path in `ExpectedPaths`.

Update `Contract` with exact templates and metadata:

```powershell
PrepareDecision = 'PrepareDecision -RepositoryRoot $RepositoryRoot -RunId $runId'
CreateDecision = 'CreateDecision -RepositoryRoot $RepositoryRoot -RunId $runId -TaskSummary $taskSummary -DecisionQuestion $question -DecisionOptions $options -RecommendedOption $key -ImpactSummary $impact'
MarkDecisionNotified = 'MarkDecisionNotified -RepositoryRoot $RepositoryRoot -RunId $runId -NotificationReceipt $providerMessageId'
MarkDecisionDeliveryFailed = 'MarkDecisionDeliveryFailed -RepositoryRoot $RepositoryRoot -RunId $runId -NotificationError $errorCategory'
ResolveDecisionReply = 'ResolveDecisionReply -RepositoryRoot $RepositoryRoot -RunId $runId -ReplyText $strictReply'
```

- [ ] **Step 4: Run controller and related tests and verify GREEN**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
git diff --check -- tools/automation-controller.ps1 tools/test-automation-controller.ps1
```

Expected: all three tests report `OK`; no whitespace findings.

- [ ] **Step 5: Commit the controller slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller.ps1|tools/test-automation-controller.ps1'
git add -- tools/automation-controller.ps1 tools/test-automation-controller.ps1
git diff --cached --check
git commit -m "fix(automation): close decision lifecycle"
```

### Task 4: Update policy, prompt and deployment guardrails

**Files:**

- Modify: `tools/check-automation-workflow.ps1:63-274`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流状态.txt`
- Update through API: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`

- [ ] **Step 1: Add failing static checks**

Require both the versioned and deployed prompt to contain `PrepareDecision`, `NotificationReceipt`, `CreateDecision` and `ResolveDecisionReply`, and require the rules to contain the exact invariant that a valid reply returns to `InspectCandidate` rather than `Finish`.

```powershell
foreach ($entry in @('PrepareDecision','NotificationReceipt','CreateDecision','ResolveDecisionReply')) {
  Require-Match $controller [regex]::Escape($entry) "controller prompt lacks repaired decision contract: $entry"
}
Require-Match $rules '有效回复.*InspectCandidate.*不得直接.*Finish' 'rules lack decision reply re-registration'
```

- [ ] **Step 2: Run the checker and verify RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
```

Expected: FAIL for missing prepared-context, receipt, or reply re-registration clauses.

- [ ] **Step 3: Update rules and thin prompt**

Update the stable rules to state:

- `PrepareDecision` must expose `自动工作流状态.txt` before creation;
- the controller, not the model, writes the visible decision section;
- notification success requires a provider receipt, otherwise use delivery failure;
- valid replies return to `InspectCandidate`, reregister complete business paths plus the status path, and clear the decision only after a successful commit;
- publication failure rolls back without recovery residue or an empty commit;
- audited `CancelDecision -ManualOverride` is only for human repair of invalid/duplicate decisions.

Rewrite the prompt's decision sentence into this exact operational sequence while staying under the existing 3000-character and 10-step limits:

```text
发现需负责人选择时，状态路径必须已在 expectedPaths；先调用 PrepareDecision 并读取其 requiredSources，确认既有决定不能解决后才按返回的完整参数契约调用 CreateDecision。入口会写项目可见状态；随后实际发送邮件，成功时把连接器消息标识作为 NotificationReceipt 调用 MarkDecisionNotified，失败调用 MarkDecisionDeliveryFailed，最后按 nextCommand Finish。不得无投递证据标成功。有效回复调用 ResolveDecisionReply 后必须按返回的原 taskId 重新 InspectCandidate、登记业务路径和状态路径、BeginMutation、实施并 Finish；不得从 identity_checked 直接 Finish。
```

Append the durable result to `自动工作流状态.txt` only after all tests pass; keep `当前无待决策项。` unchanged.

- [ ] **Step 4: Deploy with the automation API**

Call the automation view operation for `tzg-hourly-controller`, preserve name, schedule, status, model, reasoning effort, destination, project and execution environment, and update only the prompt using the full versioned source. Do not edit `automation.toml` directly.

- [ ] **Step 5: Verify policy and deployed prompt**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,tools,docs/superpowers/specs,docs/superpowers/plans
git diff --check
```

Expected: both checks report `OK`, deployed prompt equals the versioned source, and Git reports no whitespace errors.

- [ ] **Step 6: Commit tracked policy and prompt changes**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/check-automation-workflow.ps1|开发管理/自动工作流控制器提示词.txt|开发管理/自动工作流规则.txt|开发管理/自动工作流状态.txt'
git add -- tools/check-automation-workflow.ps1 开发管理/自动工作流控制器提示词.txt 开发管理/自动工作流规则.txt 开发管理/自动工作流状态.txt
git diff --cached --check
git commit -m "docs(automation): deploy repaired decision protocol"
```

### Task 5: Full verification and live-state repair

**Files:**

- Modify outside Git: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json`
- Modify outside Git: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/memory.md`

- [ ] **Step 1: Run the shared-control-plane regression suite**

Run once after all related files stabilize:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,tools,docs/superpowers/specs,docs/superpowers/plans
git diff --check
git status --short
```

Expected: every script reports `OK`; Git has no unstaged or staged project changes.

- [ ] **Step 2: Exercise an isolated end-to-end decision run**

Use a temporary state path, run root and Git repository. Execute creation with `PrepareDecision`, publish, delivery failure, `Finish`, fresh `Start`, strict reply resolution, original-task reinspection, business mutation and final `Finish`. Assert both commits contain only registered paths, no recovery pointer remains, and no production email is sent.

- [ ] **Step 3: Verify and repair the current live state**

Read the state and require all of these preconditions before mutation:

```powershell
$live = pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show | ConvertFrom-Json
if ($live.state -ne 'AUTO-BLOCKED' -or
    $live.pendingDecision.decisionId -ne 'DEC-20260714-87F6870C80C3' -or
    $live.pendingDecision.taskId -ne 'TQ-057') {
  throw 'Live controller state changed; stop instead of repairing a different state.'
}
```

Then run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 ResetBlocked -ErrorMessage 'Decision publication lifecycle repaired and regression-tested'
$repairRunId = [guid]::NewGuid().Guid
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Acquire -RunId $repairRunId
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 CancelDecision -RunId $repairRunId -DecisionId 'DEC-20260714-87F6870C80C3' -CancellationReason 'Duplicate of recorded TQ-057 decisions; no email was sent' -ManualOverride
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Complete -RunId $repairRunId
```

Read `Show` again and require `state=IDLE`, null `pendingDecision`, null recovery paths, `recoveryCount=0`, and a matching `lastDecisionCancellation`.

- [ ] **Step 4: Record the local repair audit**

Append one concise entry to the automation memory using `apply_patch`. Include the fixed commit IDs, cancelled decision ID, final `IDLE` state, tests run and the fact that no production mail was sent. Do not include private addresses or search criteria.

- [ ] **Step 5: Final verification before completion claim**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
git status --short
git log -5 --oneline
```

Expected: workflow checker is `OK`, project worktree is clean, deployed prompt matches source, and the final live controller state is clean `IDLE`.

## Plan self-review

- Spec coverage: Tasks 1-3 implement receipt evidence, deterministic project publication, precreation context exposure, valid reply re-registration, and audited repair. Task 4 updates stable policy and deployed prompt. Task 5 verifies shared control infrastructure and repairs the live state.
- Completeness scan: every action, parameter, file and verification command is explicit.
- Type consistency: decision options use `A=label|B=label`; `NotificationReceipt` is accepted by both facade and state tool; `CancelDecision` is operator-only; `RollbackDecision` is internal; resolved tasks return to `InspectCandidate` and require the status path.
- Scope: no task changes Gmail private configuration, worker ownership, queue policy, workspace guard semantics or automatic push behavior.
