# Automation Decision Lifecycle v6 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved schema v6 decision lifecycle, repair notification addressing and reply provenance, make failure closure evidence-based, migrate the current TQ-057 incident without changing business data, and safely restore the hourly controller.

**Architecture:** Keep `tools/automation-controller.ps1` as the only model-facing facade. Put atomic state transitions in `tools/automation-controller-state.ps1`, project rendering in `tools/automation-decision-status.ps1`, and workspace classification/evidence in `tools/automation-workspace-guard.ps1`; add one operator-only repair facade for the live v5-to-v6 correction. A task may have one unresolved `pendingDecision` and one active `decisionFlow` containing prior resolved choices until the business commit succeeds.

**Tech Stack:** PowerShell 7, JSON state/evidence, Git, Codex automation update API, Gmail connector, existing controller/guard/finalizer tests.

---

## Execution invariants

- Pause `tzg-hourly-controller` before changing control-plane code or live state. Keep it `PAUSED` through tests, prompt deployment, migration, second-decision publication, and all acceptance checks.
- Never print, commit, log, or copy the private `recipientEmail`, `allowedReplyFrom`, or aliases into project files. Tests use only `example.invalid` values.
- Never pass `"me"` as a Gmail recipient. Notification success means only that the provider accepted the send request and the exact Sent message has the expected recipient hash.
- Do not stage or alter the pre-existing untracked file `docs/superpowers/specs/2026-07-15-jindan-gameplay-reconstruction-handoff.md` or any other path absent from the task's `ExpectedPaths`.
- Keep the first live choice `DEC-20260715-35ACB87E6C10 = B`; correct its provenance from `email` to `manual` through an appended audit correction tied to conversation `019f63c5-f73c-70a0-9773-5592a3e03194`.
- Do not modify TQ-057 spell data, `SpellData`, CSV schemas, Unity assets, or runtime behavior in this repair. The second data-model choice remains pending for the project owner.
- Use `pwsh -NoProfile -ExecutionPolicy Bypass -File ...` for every PowerShell verification. Run Unity and BattleSim only after a future business decision authorizes related code/data changes; they are outside this repair.

## File map

| File | Responsibility |
|---|---|
| `tools/automation-controller-state.ps1` | Schema v6, decision chains, append-only attempts, source evidence, explicit close/recovery transitions, operator-only correction action. |
| `tools/test-automation-controller-state.ps1` | Atomic state and v1-v5 migration regressions. |
| `tools/automation-decision-status.ps1` | Render pending, implementation-pending, and cleared project states. |
| `tools/test-automation-decision-status.ps1` | Rendering, section isolation, redaction, BOM/newline, and prior-choice summary tests. |
| `tools/automation-workspace-guard.ps1` | Classify failure residue and capture schema-2 interruption evidence. |
| `tools/test-automation-workspace-guard.ps1` | Clean, expected-only, outside-path, deletion, and recovery compatibility tests. |
| `tools/automation-controller.ps1` | Private notification preparation, Sent verification, retry, source-specific resolution, chaining, and failure routing. |
| `tools/test-automation-controller.ps1` | Temporary-repository end-to-end protocol regressions. |
| `tools/automation-controller-repair.ps1` | Operator-only dry-run/apply wrapper for guarded live repair. |
| `tools/test-automation-controller-repair.ps1` | Redacted incident fixture, backup, precondition, dry-run, apply, and idempotence tests. |
| `tools/fixtures/automation-controller-v5-chained-decision-stuck.json` | Redacted schema-v5 reproduction of the stuck lifecycle. |
| `tools/check-automation-workflow.ps1` | Static contract and deployed-prompt checks for v6 names and invariants. |
| `开发管理/自动工作流规则.txt` | Stable v6 policy source. |
| `开发管理/自动工作流控制器提示词.txt` | Thin model-facing v6 action sequence. |
| `开发管理/自动工作流状态.txt` | Live project-visible pending decision; changed only by the normal controller publisher. |
| `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.private.json` | Private target/sender configuration; read only. |
| `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json` | Live state migrated by the operator repair tool. |
| `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml` | Deployment record; update only through the automation API. |

### Task 0: Freeze the writer and capture the live baseline

**Files:**

- Read: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`
- Read: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json`
- Read: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.private.json`
- Read: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/memory.md`
- Create outside Git: `%USERPROFILE%/.codex/automation-state/repairs/decision-v6-*`

- [ ] **Step 1: Pause through the automation API**

View automation `tzg-hourly-controller`, preserve its ID, name, recurrence, model, reasoning effort, execution environment, project, destination, and prompt, then call `codex_app__automation_update` with only `status=PAUSED`. Do not edit `automation.toml` directly and do not pause the read-only daily briefing.

- [ ] **Step 2: Prove there is no active writer**

Run:

```powershell
$controllerToml = "$env:USERPROFILE\.codex\automations\tzg-hourly-controller\automation.toml"
$writerProcesses = @(Get-CimInstance Win32_Process | Where-Object {
  $_.ProcessId -ne $PID -and $_.CommandLine -match 'tzg-hourly-controller|automation-controller\.ps1'
})
if (-not (Select-String -Quiet -LiteralPath $controllerToml -Pattern '^status = "PAUSED"$')) { throw 'hourly controller is not PAUSED' }
if ($writerProcesses.Count -ne 0) { $writerProcesses | Select-Object ProcessId, Name, CommandLine; throw 'controller writer process is still active' }
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: no controller process is listed; the checker reports `check-automation-workflow: OK` with the hourly writer paused.

- [ ] **Step 3: Back up and record a comparison manifest**

Run in one PowerShell session:

```powershell
$stamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')
$backupRoot = Join-Path $env:USERPROFILE ".codex\automation-state\repairs\decision-v6-$stamp"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
Copy-Item -LiteralPath "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json" -Destination (Join-Path $backupRoot 'state.before.json')
Copy-Item -LiteralPath "$env:USERPROFILE\.codex\automations\tzg-hourly-controller\memory.md" -Destination (Join-Path $backupRoot 'memory.before.md')
Get-ChildItem "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller-runs" -File -ErrorAction SilentlyContinue |
  Copy-Item -Destination $backupRoot
git rev-parse HEAD | Set-Content -LiteralPath (Join-Path $backupRoot 'git-head.before.txt') -Encoding utf8NoBOM
git status --porcelain=v1 | Set-Content -LiteralPath (Join-Path $backupRoot 'git-status.before.txt') -Encoding utf8NoBOM
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show |
  Set-Content -LiteralPath (Join-Path $backupRoot 'state.show.before.json') -Encoding utf8NoBOM
$backupRoot
```

Expected: the command prints a unique backup directory. The private configuration is deliberately not copied because it contains the address; only verify locally that its keys include `schemaVersion`, `recipientEmail`, `allowedReplyFrom`, `gmailLabel`, and `aliases`.

- [ ] **Step 4: Record incident preconditions without mutating state**

```powershell
$live = pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show | ConvertFrom-Json
if ($live.schemaVersion -ne 5 -or $live.taskId -ne 'TQ-057' -or $live.checkpoint -ne 'mutation_started') { throw 'live incident shape changed; stop and redesign the migration preconditions' }
if ($live.pendingDecision.decisionId -ne 'DEC-20260715-35ACB87E6C10' -or $live.pendingDecision.resolution.optionKey -ne 'B') { throw 'live decision changed; do not repair a different decision' }
if ($live.pendingDecision.resolution.source -ne 'email') { throw 'the provenance defect is no longer the recorded live defect' }
```

Expected: no output and no state change. If any assertion fails, stop before Task 1 and update the approved design with the new evidence.

### Task 1: Implement atomic schema v6 decision chains

**Files:**

- Modify: `tools/test-automation-controller-state.ps1`
- Modify: `tools/automation-controller-state.ps1:1-528`

- [ ] **Step 1: Add failing chain, attempt, source, and migration tests**

Extend the existing state test with these scenarios, using its `Invoke-StateTool`, `Assert-Code`, and `Read-TestState` helpers:

```powershell
$first = Invoke-StateTool @(
  'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-v6',
  '-TaskKind', 'execute', '-TaskId', 'TQ-057', '-TaskSummary', 'Repair spell data truth',
  '-DecisionQuestion', 'Which document/CSV scope is authoritative?',
  '-DecisionOptions', 'A=Remove documents|B=Land approved documents',
  '-RecommendedOption', 'B', '-ImpactSummary', 'Controls the TQ-057 implementation scope',
  '-Now', '2026-07-15T04:00:00Z'
)
Assert-Code $first 0 'create first v6 decision'
$firstDecision = (Read-TestState).pendingDecision

$attempt = Invoke-StateTool @(
  'RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-v6',
  '-NotificationStatus', 'PROVIDER_ACCEPTED', '-RecipientHash', ('a' * 64),
  '-ProviderMessageId', 'provider-message-1', '-Now', '2026-07-15T04:01:00Z'
)
Assert-Code $attempt 0 'append provider accepted attempt'

$resolved = Invoke-StateTool @(
  'ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-v6',
  '-DecisionId', $firstDecision.decisionId, '-OptionKey', 'B', '-ReplySource', 'manual',
  '-EvidenceThreadId', '019f63c5-f73c-70a0-9773-5592a3e03194', '-ManualOverride',
  '-Now', '2026-07-15T04:02:00Z'
)
Assert-Code $resolved 0 'move first decision into flow'
$afterFirst = Read-TestState
if ($null -ne $afterFirst.pendingDecision -or $afterFirst.decisionFlow.status -ne 'IMPLEMENTATION_PENDING') { throw 'resolved decision remained pending' }
if ($afterFirst.decisionFlow.resolvedDecisions.Count -ne 1 -or $afterFirst.decisionFlow.resolvedDecisions[0].resolution.source -ne 'manual') { throw 'first decision was not retained with manual provenance' }
if ($afterFirst.decisionFlow.resolvedDecisions[0].resolution.evidenceHash -notmatch '^[0-9a-f]{64}$') { throw 'manual evidence was not hashed' }

$second = Invoke-StateTool @(
  'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-v6',
  '-TaskKind', 'execute', '-TaskId', 'TQ-057', '-TaskSummary', 'Repair spell data truth',
  '-DecisionQuestion', 'How should mixed damage be represented?',
  '-DecisionOptions', 'A=Two explicit channels|B=Primary plus optional secondary|C=Defer mixed spells',
  '-RecommendedOption', 'A', '-ImpactSummary', 'Changes CSV and runtime schema',
  '-Now', '2026-07-15T04:03:00Z'
)
Assert-Code $second 0 'create second decision for the same task'

$foreign = Invoke-StateTool @(
  'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-v6',
  '-TaskKind', 'execute', '-TaskId', 'TQ-059', '-TaskSummary', 'Foreign task',
  '-DecisionQuestion', 'Overwrite the active flow?', '-DecisionOptions', 'A=Yes|B=No',
  '-RecommendedOption', 'B', '-ImpactSummary', 'Would cross task ownership'
)
Assert-Code $foreign 15 'reject a different task while the flow is active'
```

Add three notification failures and assert that exactly three immutable attempt entries remain, the third entry retains its actual result, and `pendingDecision.status` becomes `RETRY_EXHAUSTED`. Add invalid-source tests proving email evidence cannot use thread fields and manual evidence cannot use sender/message fields.

Add v5 fixtures in-memory for both an unresolved `NOTIFIED` decision and a `RESOLVED` decision. Assert the unresolved decision becomes `PROVIDER_ACCEPTED` with one legacy attempt; assert the resolved decision moves into `decisionFlow.resolvedDecisions`, clears `pendingDecision`, preserves option/source, and emits schema version 6 on the next mutating action.

- [ ] **Step 2: Run the state test and verify RED**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: the test fails because schema 6 fields/actions and chained decision creation do not yet exist. The failure must be one of the newly added assertions, not a syntax or fixture error.

- [ ] **Step 3: Add schema v6 and explicit transitions**

Change the action contract to include these state-only actions:

```powershell
[ValidateSet(
  'Acquire','Renew','Checkpoint','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure',
  'CreateDecision','RecordDecisionNotification','ResolveDecision','CompleteDecisionFlow',
  'AbortClean','RecordRecoverableInterruption','BlockUnsafe','RepairDecisionFlow',
  'Complete','Show','ResetBlocked'
)]
```

Add parameters with exact validation:

```powershell
[ValidateSet('PROVIDER_ACCEPTED','DELIVERY_FAILED','MISADDRESSED')]
[string]$NotificationStatus,
[ValidatePattern('^[0-9a-f]{64}$')]
[string]$RecipientHash,
[string]$ProviderMessageId,
[string]$EvidenceMessageId,
[string]$EvidenceSender,
[string]$EvidenceThreadId,
[string]$EvidenceTurnId,
[string]$CorrectionReason,
[string]$CorrectionEvidenceThreadId
```

Use this top-level state shape:

```powershell
[ordered]@{
  schemaVersion = 6
  controllerId = $ControllerId
  runId = $null
  runMode = $null
  state = 'IDLE'
  leaseExpiresAt = $null
  taskKind = $null
  taskId = $null
  taskExecutor = $null
  checkpoint = $null
  expectedPaths = @()
  recoveryBaselinePath = $null
  recoveryEvidencePath = $null
  recoveryEvidenceHash = $null
  recoveryCount = 0
  lastQueueAuditAt = $null
  lastQueueFingerprint = $null
  lastNoCandidateFingerprint = $null
  lastRunnableCount = $null
  workerState = [ordered]@{ deepseek = [ordered]@{ failureCount = 0; backoffUntil = $null; lastError = $null } }
  lastError = $null
  pendingDecision = $null
  decisionFlow = $null
  lastCompletedDecisionFlow = $null
  auditCorrections = @()
  lastDecisionCancellation = $null
}
```

`CreateDecision` creates a flow when none exists, permits reuse only when `decisionFlow.taskId` equals `TaskId` and `pendingDecision` is null, sets flow status to `AWAITING_DECISION`, and initializes `notificationAttempts=@()`. It rejects a different task or a second unresolved decision.

`RecordDecisionNotification` appends this redacted record and never overwrites an earlier one:

```powershell
[ordered]@{
  attemptedAt = $nowValue.ToString('o')
  result = $NotificationStatus
  recipientHash = if ($RecipientHash) { $RecipientHash } else { $null }
  providerMessageIdHash = if ($ProviderMessageId) { Get-Sha256Text $ProviderMessageId.Trim() } else { $null }
  errorCategory = if ($NotificationError) { Get-TruncatedText $NotificationError 120 } else { $null }
}
```

Require recipient/message evidence for `PROVIDER_ACCEPTED` and `MISADDRESSED`; allow `DELIVERY_FAILED` without a provider ID. After the third failed/misaddressed attempt, retain the attempt result but set the pending status to `RETRY_EXHAUSTED`. Reject a fourth attempt without changing the file.

`ResolveDecision` validates the option and source-specific evidence, hashes raw evidence before persistence, then atomically appends the complete pending object to `decisionFlow.resolvedDecisions`, adds its `resolution`, sets flow status to `IMPLEMENTATION_PENDING`, and clears `pendingDecision`. Email requires `EvidenceMessageId` plus `EvidenceSender`; manual requires `ManualOverride` plus `EvidenceThreadId` and accepts an optional turn ID. Persist only hashes.

`CompleteDecisionFlow` requires the current state task ID to match the flow, rejects an unresolved `pendingDecision`, copies this bounded summary, and clears the active flow:

```powershell
$state.lastCompletedDecisionFlow = [ordered]@{
  taskKind = [string]$state.decisionFlow.taskKind
  taskId = [string]$state.decisionFlow.taskId
  openedAt = [string]$state.decisionFlow.openedAt
  completedAt = $nowValue.ToString('o')
  decisions = @($state.decisionFlow.resolvedDecisions | ForEach-Object {
    [ordered]@{
      decisionId = [string]$_.decisionId
      optionKey = [string]$_.resolution.optionKey
      source = [string]$_.resolution.source
      resolvedAt = [string]$_.resolution.resolvedAt
      evidenceHash = [string]$_.resolution.evidenceHash
    }
  })
}
$state.decisionFlow = $null
```

Import schema versions 1-5. Convert `NOTIFIED` to `PROVIDER_ACCEPTED`, convert legacy `DELIVERY_FAILED/REPLY_INVALID` to `DELIVERY_FAILED`, and convert a legacy `notification` object to one attempt without inventing a recipient hash. A legacy `RESOLVED` pending object becomes a resolved decision in a flow with a deterministic legacy evidence hash. Preserve the recorded legacy source; Task 6 performs the audited incident correction.

Remove `ClearResolvedDecision`, `MarkDecisionNotified`, `MarkDecisionDeliveryFailed`, and the old state-level ambiguous `Fail` action. `Complete` releases only the current lease/task/recovery fields; it must preserve `pendingDecision`, `decisionFlow`, bounded history, and corrections.

- [ ] **Step 4: Run state tests and verify GREEN**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: `test-automation-controller-state: OK`.

- [ ] **Step 5: Commit the state slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller-state.ps1|tools/test-automation-controller-state.ps1'
git add -- tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
git diff --cached --check
git commit -m "feat(automation): add decision flow schema v6"
```

Expected: only the two state files are committed; the unrelated handoff file remains untracked.

### Task 2: Render pending and implementation-pending states

**Files:**

- Modify: `tools/test-automation-decision-status.ps1`
- Modify: `tools/automation-decision-status.ps1:1-128`

- [ ] **Step 1: Add failing three-mode publisher tests**

Replace old `Publish` calls in the test with `PublishPending` and add a payload containing one resolved decision plus a second pending decision. Assert the rendered section contains the second decision ID/question and the compact prior line `已登记选择：第一项=B（manual）`, but does not contain the old question as the current question.

Add `PublishImplementationPending` coverage with a flow that has one resolved decision and no pending decision:

```powershell
$flowPayload = [ordered]@{
  pendingDecision = $null
  decisionFlow = [ordered]@{
    taskId = 'TQ-057'
    status = 'IMPLEMENTATION_PENDING'
    resolvedDecisions = @([ordered]@{
      decisionId = 'DEC-20260715-35ACB87E6C10'
      resolution = [ordered]@{ optionKey = 'B'; source = 'manual'; resolvedAt = '2026-07-15T04:02:00Z'; evidenceHash = ('b' * 64) }
    })
  }
}
$flowBase64 = [Convert]::ToBase64String([Text.UTF8Encoding]::new($false).GetBytes(($flowPayload | ConvertTo-Json -Depth 8 -Compress)))
$published = Invoke-Publisher @('PublishImplementationPending', '-StatusPath', $statusPath, '-DecisionStateJsonBase64', $flowBase64)
Assert-Code $published 0 'publish implementation pending'
```

Assert the result states that B is registered and implementation is pending, contains no strict reply instruction, and contains no email-shaped string or evidence hash. Retain existing exact-section, duplicate-heading, BOM, newline, atomic-write, and `Clear` tests.

- [ ] **Step 2: Run and verify RED**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
```

Expected: failure because `PublishPending`, `PublishImplementationPending`, and `DecisionStateJsonBase64` are not implemented.

- [ ] **Step 3: Implement the three publisher modes**

Use this action/parameter contract:

```powershell
[ValidateSet('PublishPending','PublishImplementationPending','Clear')]
[string]$Action,
[string]$DecisionStateJsonBase64
```

For `PublishPending`, require `pendingDecision`, allow flow status `AWAITING_DECISION`, render only the latest question/options/recommendation, and append redacted prior summaries from `decisionFlow.resolvedDecisions`. Render notification states as:

```powershell
$labels = @{
  PENDING = '尚未尝试发送'
  PROVIDER_ACCEPTED = '发送请求已被提供方接受（不代表已收件）'
  DELIVERY_FAILED = '发送失败，可重试'
  MISADDRESSED = 'Sent 目标不一致，未完成通知'
  RETRY_EXHAUSTED = '已达三次尝试上限，等待人工处理'
}
```

For `PublishImplementationPending`, require no pending decision and flow status `IMPLEMENTATION_PENDING`; render the task ID and all resolved decision option/source summaries without a reply template. `Clear` keeps the existing `当前无待决策项。` body. Reject any decoded email address and any raw evidence/provider field before writing.

- [ ] **Step 4: Run and verify GREEN**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
```

Expected: `test-automation-decision-status: OK`.

- [ ] **Step 5: Commit the publisher slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-decision-status.ps1|tools/test-automation-decision-status.ps1'
git add -- tools/automation-decision-status.ps1 tools/test-automation-decision-status.ps1
git diff --cached --check
git commit -m "feat(automation): publish chained decision state"
```

### Task 3: Classify interruption residue and capture schema-2 evidence

**Files:**

- Modify: `tools/test-automation-workspace-guard.ps1`
- Modify: `tools/automation-workspace-guard.ps1:1-700`

- [ ] **Step 1: Add failing interruption tests**

Add `CaptureInterruptionEvidence` tests for:

1. unchanged worktree relative to the baseline returns `classification=clean`, does not create evidence, and lists no changed paths;
2. one modified expected file returns `classification=recoverable`, creates schema-2 evidence, and records that file in `expectedChangedPaths`;
3. one deleted expected file also returns `recoverable` even though `expectedEntries` is empty;
4. an expected change plus `intruder.txt` returns `classification=unsafe`, does not create evidence, and lists `intruder.txt` in `conflictingPaths`;
5. a HEAD change returns `unsafe`;
6. `CheckRecovery` accepts unchanged schema-1 evidence and the new schema-2 evidence, but rejects a later edit to the expected residue.

Use a new assertion helper:

```powershell
function Assert-Classification {
  param($Result, [int]$ExpectedCode, [string]$ExpectedClass, [string]$Label)
  Assert-Code $Result $ExpectedCode $Label
  $json = $Result.Output | ConvertFrom-Json
  if ($json.classification -cne $ExpectedClass) { throw "$Label expected $ExpectedClass but got $($json.classification)" }
  $json
}
```

- [ ] **Step 2: Run and verify RED**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
```

Expected: failure because `CaptureInterruptionEvidence` is not an allowed action.

- [ ] **Step 3: Implement classification and evidence schema 2**

Add the action:

```powershell
[ValidateSet('Snapshot','Check','Verify','CaptureRecoveryEvidence','CaptureInterruptionEvidence','CheckRecovery')]
```

Add a helper that compares all baseline/fresh entries and partitions actual changes by overlap with expected paths. `CaptureInterruptionEvidence` must return this JSON contract:

```powershell
[pscustomobject][ordered]@{
  safe = ($classification -ne 'unsafe')
  classification = $classification
  expectedPaths = @($expected)
  changedExpectedPaths = @($changedExpected)
  conflictingPaths = @($changedOutside)
  reason = $reason
  evidenceHash = $evidenceHash
} | ConvertTo-Json -Compress
```

For expected-only changes, write evidence with this exact shape:

```powershell
[pscustomobject][ordered]@{
  schemaVersion = 2
  purpose = 'interruption'
  repositoryRoot = $repository
  baselinePayloadHash = [string]$baseline.payloadHash
  head = [string]$fresh.head
  expectedPaths = @($expected)
  expectedChangedPaths = @($changedExpected | Sort-Object)
  expectedEntries = @(Sort-WorkspaceEntries $expectedEntries)
  payloadHash = $null
}
```

Allow `expectedEntries` to be empty for deletion-only residue, but require `expectedChangedPaths` to be non-empty. Extend the evidence hash canonicalization and reader to support schema 1 and 2. For schema 2, `CheckRecovery` compares the current expected fingerprints and recomputed changed-path set with both recorded arrays. Preserve schema-1 behavior for finalizer evidence.

- [ ] **Step 4: Run and verify GREEN**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
```

Expected: `test-automation-workspace-guard: OK`.

- [ ] **Step 5: Commit the guard slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-workspace-guard.ps1|tools/test-automation-workspace-guard.ps1'
git add -- tools/automation-workspace-guard.ps1 tools/test-automation-workspace-guard.ps1
git diff --cached --check
git commit -m "fix(automation): classify interruption residue"
```

### Task 4: Add verified notification and source-specific reply actions

**Files:**

- Modify: `tools/test-automation-controller.ps1`
- Modify: `tools/automation-controller.ps1:1-1357`

- [ ] **Step 1: Add failing private-config and notification tests**

Create a temporary private config inside the existing test sandbox:

```powershell
$privatePath = Join-Path $sandbox 'controller.private.json'
Write-Utf8 $privatePath (@{
  schemaVersion = 1
  recipientEmail = 'owner@example.invalid'
  allowedReplyFrom = 'owner@example.invalid'
  gmailLabel = 'TZG_DECISIONS'
  aliases = @('owner.alias@example.invalid')
} | ConvertTo-Json -Depth 4)
```

Add tests that prove:

- `PrepareDecisionNotification` returns the configured test target, stores only its SHA-256 in the session, and rejects configs whose target is `me` or blank;
- `MarkDecisionSubmitted -ObservedRecipient owner@example.invalid` records `PROVIDER_ACCEPTED` and a hashed provider ID;
- a different observed recipient records `MISADDRESSED` and never returns a success message claiming notification;
- three actual failed/misaddressed send attempts produce `RETRY_EXHAUSTED`, and `RetryDecisionNotification` does not create a fourth;
- no controller JSON, state JSON, status file, or session file contains the test address after the action that consumes it (the transient prepare response is the only allowed occurrence);
- the controller contract no longer exposes `MarkDecisionNotified` or `ResolveDecisionReply`.

- [ ] **Step 2: Add failing reply provenance and read-only-invalid tests**

After publishing a pending decision and completing that status-only run, start a new run. Capture the state file bytes before each invalid call. Test empty text, fuzzy text, wrong decision ID, multiple options, unknown sender, and missing provider message ID through `ResolveDecisionEmailReply`; assert exit 15, unchanged bytes, unchanged attempt count, and unchanged notification status.

Then test exact email and manual paths separately:

```powershell
$emailResolved = Invoke-Controller @(
  'ResolveDecisionEmailReply', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $emailRunId, '-PrivateConfigPath', $privatePath,
  '-ReplyText', "$($decision.decisionId)：选择 A", '-ReplyMessageId', 'reply-provider-1',
  '-ReplyFrom', 'owner.alias@example.invalid', '-Now', '2026-07-15T05:00:00Z'
)
Assert-Code $emailResolved 0 'resolve verified email reply'
```

For a fresh fixture, call:

```powershell
$manualResolved = Invoke-Controller @(
  'ResolveDecisionManual', '-RepositoryRoot', $repo, '-StatePath', $manualStatePath,
  '-RunRoot', $manualRunRoot, '-RunId', $manualRunId,
  '-DecisionId', $manualDecision.decisionId, '-OptionKey', 'B',
  '-CurrentThreadId', '019f63c5-f73c-70a0-9773-5592a3e03194',
  '-CurrentTurnId', 'turn-manual-test', '-ManualOverride', '-Now', '2026-07-15T05:10:00Z'
)
Assert-Code $manualResolved 0 'resolve manual conversation choice'
```

Assert the first resolution source is `email` with sender/message hashes and no thread hashes; the second is `manual` with thread/turn hashes and no sender/message hashes. Both must clear `pendingDecision`, retain the choice in `decisionFlow`, and return the original task ID with `nextCommand=InspectCandidate`.

- [ ] **Step 3: Run and verify RED**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

Expected: failure on the first new action or state-shape assertion.

- [ ] **Step 4: Implement exact facade actions**

Change the model-facing action list to:

```powershell
[ValidateSet(
  'Contract','Start','InspectCandidate','RegisterCandidate','BeginMutation','Renew','Finish',
  'CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure',
  'PrepareDecision','CreateDecision','PrepareDecisionNotification','MarkDecisionSubmitted',
  'RetryDecisionNotification','MarkDecisionDeliveryFailed',
  'ResolveDecisionEmailReply','ResolveDecisionManual'
)]
```

Add `PrivateConfigPath` with the production default, plus `ProviderMessageId`, `PriorProviderMessageId`, `ObservedRecipient`, `ReplyMessageId`, `ReplyFrom`, `CurrentThreadId`, and `CurrentTurnId` parameters. Remove the old facade-only `NotificationReceipt` parameter. Read private JSON with strict UTF-8 and validate one email-shaped `recipientEmail`, one email-shaped `allowedReplyFrom`, zero or more email-shaped aliases, and `recipientEmail -cne 'me'`.

`PrepareDecisionNotification` requires an active pending decision, returns `decisionId`, subject, body, and transient `recipientEmail`, and persists only these session fields:

```powershell
$session.notificationContext = [ordered]@{
  decisionId = [string]$state.pendingDecision.decisionId
  recipientHash = Get-Sha256Text $private.recipientEmail.Trim().ToLowerInvariant()
  preparedAt = $nowValue.ToString('o')
  attemptNumber = @($state.pendingDecision.notificationAttempts).Count + 1
}
```

`MarkDecisionSubmitted` requires `ProviderMessageId` and the exact Sent `ObservedRecipient`. Hash the normalized observed target and compare it with the prepared hash. Call state `RecordDecisionNotification` with `PROVIDER_ACCEPTED` on equality or `MISADDRESSED` on inequality. Never return wording equivalent to “已收到”.

`RetryDecisionNotification` requires the current decision ID. When a prior Sent result is supplied, validate `PriorProviderMessageId` and `ObservedRecipient`, record `MISADDRESSED` only for an actual mismatch, then prepare the configured target for the next send. If three attempts already exist, return `retry_exhausted` without state mutation. `MarkDecisionDeliveryFailed` records one actual connector failure using the currently prepared target hash and a truncated error category.

`ResolveDecisionEmailReply` validates the strict reply regex, current decision ID, exactly one option, provider message ID, and a normalized sender in `allowedReplyFrom + aliases`. Invalid input returns before invoking the state tool. Valid input calls state `ResolveDecision` with `ReplySource=email`, `EvidenceMessageId`, and `EvidenceSender`.

`ResolveDecisionManual` requires `ManualOverride`, exact decision ID, one valid option, and `CurrentThreadId`; it passes only thread/optional turn evidence with `ReplySource=manual`. It never rewrites text into an email reply and never reads Gmail configuration.

- [ ] **Step 5: Implement chained Start/Create/Finish routing**

`Start` routing order after a fresh lease and baseline is:

1. explicit recovery lease handled by Task 5;
2. non-null `pendingDecision` returns `inspect_pending_decision` with next actions `ResolveDecisionEmailReply`, `ResolveDecisionManual`, and `RetryDecisionNotification`;
3. `decisionFlow.status=IMPLEMENTATION_PENDING` and no pending decision returns `resume_decision_task`, original task ID, and `nextCommand=InspectCandidate`;
4. otherwise normal selection.

When `CreateDecision` is called for the same active flow, allow it after normal `InspectCandidate → RegisterCandidate → BeginMutation → PrepareDecision`. Publish with `PublishPending`, including earlier resolved summaries.

During `Finish`:

- publish pending state when `pendingDecision` exists;
- publish implementation-pending when the flow is resolved but business work has not been committed;
- clear the status section as part of the successful business commit when all decisions are resolved and this run implements the flow task;
- call state `CompleteDecisionFlow` only after the commit and post-commit guard verification succeed;
- then call state `Complete` to release the lease.

Do not clear a flow during a status-only decision publication commit.

- [ ] **Step 6: Run focused controller tests and verify GREEN**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
```

Expected: all three scripts report `OK`.

- [ ] **Step 7: Commit the notification/reply slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller.ps1|tools/test-automation-controller.ps1'
git add -- tools/automation-controller.ps1 tools/test-automation-controller.ps1
git diff --cached --check
git commit -m "fix(automation): verify decision notification provenance"
```

### Task 5: Route failure closure through clean/recoverable/unsafe states

**Files:**

- Modify: `tools/test-automation-controller-state.ps1`
- Modify: `tools/test-automation-controller.ps1`
- Modify: `tools/automation-controller-state.ps1`
- Modify: `tools/automation-controller.ps1`

- [ ] **Step 1: Add failing state invariants**

Test these exact transitions:

- `AbortClean` from an owned run releases `runId`, lease, task/checkpoint, expected paths, and recovery fields; returns `IDLE`; preserves pending/flow; and resets `recoveryCount=0`.
- `RecordRecoverableInterruption` rejects missing baseline/evidence/hash/task/expected paths; with complete data it sets `RECOVERABLE`, releases the lease, and preserves decision state.
- `Acquire` from `RECOVERABLE` sets `RUNNING` with `runMode=recovery`; `Acquire` from `IDLE` sets `runMode=fresh`.
- a schema-6 expired `RUNNING` state is not silently treated as recovery; `Acquire` rejects it as `stale_running_state` so an operator must classify it.
- `RecordRecoverableInterruption -WasRecovery` increments the count; the second failed recovery produces `AUTO-BLOCKED`.
- `BlockUnsafe` produces `AUTO-BLOCKED`, releases the lease, and records the reason without inventing evidence.

- [ ] **Step 2: Add failing controller failure tests**

In temporary Git repositories, run `Start → InspectCandidate → RegisterCandidate → BeginMutation`, then:

1. make no file change and call facade `Fail`; assert `failurePolicy=close_clean`, state `IDLE`, no evidence file, and preserved decision flow;
2. modify only `task.txt`; assert `failurePolicy=preserve_recovery`, state `RECOVERABLE`, schema-2 evidence exists, and its hash matches state;
3. modify `task.txt` plus `intruder.txt`; assert `failurePolicy=auto_blocked`, state `AUTO-BLOCKED`, and `intruder.txt` is reported;
4. start the recoverable fixture and assert only `state=RECOVERABLE` enters `CheckRecovery`;
5. feed an incomplete schema-6 recoverable fixture and assert it stops once without incrementing an endless stale-running loop.

- [ ] **Step 3: Run and verify RED**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

Expected: failures on missing explicit transition actions and old `hasRecovery` inference.

- [ ] **Step 4: Implement state transitions and recovery leases**

`AbortClean` uses one helper to clear all run/task/recovery fields while preserving decision fields. `RecordRecoverableInterruption` validates a 64-character evidence hash, requires `taskId`, `taskKind`, non-empty expected paths, baseline and evidence, and sets `state=RECOVERABLE`. When `WasRecovery` raises the count to two, set `AUTO-BLOCKED` instead. `BlockUnsafe` sets `AUTO-BLOCKED` and clears only lease ownership.

`Acquire` accepts only `IDLE` and `RECOVERABLE` (plus rejection for a live lease), records `runMode`, and never infers recovery from arbitrary non-null task fields. Keep `AUTO-BLOCKED` rejection. A stale `RUNNING` without an active lease exits 13 with `stale_running_state`.

- [ ] **Step 5: Implement facade failure classification**

Replace `Stop-RegisteredWork`/`Fail` preserve heuristics with one classifier:

```powershell
$classification = Invoke-GuardTool @(
  'CaptureInterruptionEvidence',
  '-BaselinePath', [string]$session.baselinePath,
  '-EvidencePath', [string]$session.evidencePath,
  '-ExpectedPaths', (@($state.expectedPaths) -join '|')
)
```

Route `clean → AbortClean`, `recoverable → RecordRecoverableInterruption` with returned evidence hash and `WasRecovery` when applicable, and `unsafe → BlockUnsafe`. Pre-registration failures with no expected paths call `AbortClean` directly because there is no authorized mutation scope.

In `Start`, branch on `acquiredState.runMode -eq 'recovery'` only. Require the complete evidence invariant before `CheckRecovery`; if it is violated, call `BlockUnsafe` once rather than returning a renewable stale `RUNNING` state. A successful recovery retains the original baseline and expected paths.

- [ ] **Step 6: Run the shared failure/recovery tests**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
```

Expected: all four scripts report `OK`; no test leaves a background process.

- [ ] **Step 7: Commit the failure/recovery slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller-state.ps1|tools/test-automation-controller-state.ps1|tools/automation-controller.ps1|tools/test-automation-controller.ps1'
git add -- tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1 tools/automation-controller.ps1 tools/test-automation-controller.ps1
git diff --cached --check
git commit -m "fix(automation): close failures with workspace evidence"
```

### Task 6: Build the operator-only v5 incident repair

**Files:**

- Create: `tools/automation-controller-repair.ps1`
- Create: `tools/test-automation-controller-repair.ps1`
- Create: `tools/fixtures/automation-controller-v5-chained-decision-stuck.json`
- Modify: `tools/automation-controller-state.ps1`
- Modify: `tools/test-automation-controller-state.ps1`

- [ ] **Step 1: Add a redacted reproduction fixture**

The fixture uses schema 5, state `RUNNING`, task `TQ-057`, checkpoint `mutation_started`, expired lease, `recoveryCount=1`, no evidence, and decision `DEC-20260715-35ACB87E6C10` resolved to B with the incorrect source `email`. Use `C:\redacted\baseline.json` and `example.invalid` nowhere; the fixture contains no address, message ID, private path, or email body.

- [ ] **Step 2: Add failing dry-run/apply tests**

The repair test creates a temporary repository/baseline, copies the fixture, substitutes only its temporary baseline/run paths in memory, and runs:

```powershell
$dry = Invoke-Repair @(
  'DryRun', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-MemoryPath', $memoryPath,
  '-IncidentDecisionId', 'DEC-20260715-35ACB87E6C10', '-SelectedOption', 'B',
  '-EvidenceThreadId', '019f63c5-f73c-70a0-9773-5592a3e03194'
)
Assert-Code $dry 0 'repair dry-run'
```

Assert dry-run leaves byte-identical state/session/memory and returns a redacted projected v6 summary. Then apply with `-ManualOverride` and assert:

- backup copies of state, matching session, and memory exist;
- state is schema 6 and `IDLE`, with null lease/run/task/recovery fields and `recoveryCount=0`;
- the B decision is in `decisionFlow.resolvedDecisions`, source is `manual`, and pending is null;
- `auditCorrections` contains old `email`, new `manual`, the conversation ID hash, timestamp, and a bounded reason;
- no raw address/provider message ID is introduced;
- a second apply is rejected as `already_repaired` without modifying files;
- any expected-path or outside-path change from the original baseline blocks apply;
- apply without `ManualOverride` is rejected.

- [ ] **Step 3: Run and verify RED**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-repair.ps1
```

Expected: failure because the repair script does not exist.

- [ ] **Step 4: Implement the operator-only state correction**

Add state action `RepairDecisionFlow`, but do not expose it through `automation-controller.ps1 Contract`. Require ownerless operator invocation plus `ManualOverride`, exact decision ID/option, correction reason, and evidence thread ID. It operates on the in-memory schema-6 projection of the v5 state, finds exactly one matching resolved decision, appends:

```powershell
[ordered]@{
  decisionId = $DecisionId
  field = 'resolution.source'
  oldValue = 'email'
  newValue = 'manual'
  correctedAt = $nowValue.ToString('o')
  evidenceHash = Get-Sha256Text $CorrectionEvidenceThreadId.Trim()
  reason = Get-TruncatedText $CorrectionReason 240
}
```

Then clear run/task/lease/recovery fields with the same clean-reset helper, set flow status `IMPLEMENTATION_PENDING`, and export once under the state transaction lock. Reject if the decision is absent, option differs, source is not the known old value, or state already contains the same correction.

- [ ] **Step 5: Implement the guarded repair wrapper**

Use this contract:

```powershell
[ValidateSet('DryRun','Apply')]
[string]$Action,
[string]$RepositoryRoot = 'D:\天章游戏开发',
[string]$StatePath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json",
[string]$RunRoot = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller-runs",
[string]$MemoryPath = "$env:USERPROFILE\.codex\automations\tzg-hourly-controller\memory.md",
[string]$IncidentDecisionId,
[string]$SelectedOption,
[string]$EvidenceThreadId,
[switch]$ManualOverride
```

The wrapper validates the exact v5 incident shape, locates the session from `runId`, calls `CaptureInterruptionEvidence` against the recorded baseline/expected paths, and proceeds only when classification is `clean`. `DryRun` copies the state to a temporary file, invokes `RepairDecisionFlow` on the copy, prints a redacted before/after summary, then removes the copy. `Apply` first copies state/session/memory to a timestamped directory beside the state file, invokes the operator state action, rereads and validates the v6 invariants, and prints only hashes/IDs/statuses.

- [ ] **Step 6: Run repair and state tests GREEN**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-repair.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: both scripts report `OK`.

- [ ] **Step 7: Commit the repair slice**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller-repair.ps1|tools/test-automation-controller-repair.ps1|tools/fixtures/automation-controller-v5-chained-decision-stuck.json|tools/automation-controller-state.ps1|tools/test-automation-controller-state.ps1'
git add -- tools/automation-controller-repair.ps1 tools/test-automation-controller-repair.ps1 tools/fixtures/automation-controller-v5-chained-decision-stuck.json tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
git diff --cached --check
git commit -m "feat(automation): add guarded decision state repair"
```

### Task 7: Synchronize rules, thin prompt, and deployment checks

**Files:**

- Modify: `tools/check-automation-workflow.ps1:1-278`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Update through API: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`

- [ ] **Step 1: Add failing static contract checks**

Require both source and deployed prompt to contain all new action names and reject the retired names:

```powershell
foreach ($entry in @(
  'PrepareDecisionNotification','MarkDecisionSubmitted','RetryDecisionNotification',
  'ResolveDecisionEmailReply','ResolveDecisionManual'
)) {
  Require-Match $promptSource.Path ([regex]::Escape($entry)) "$($promptSource.Label) lacks v6 decision action: $entry"
}
foreach ($retired in @('MarkDecisionNotified','ResolveDecisionReply')) {
  Reject-Match $promptSource.Path ([regex]::Escape($retired)) "$($promptSource.Label) still exposes retired action: $retired"
}
```

Require the rules to state these phrases semantically: `PROVIDER_ACCEPTED 不代表收件`, `无效回复只读`, `人工选择不得记为 email`, `同一任务允许连续决策`, `RECOVERABLE 必须有 interruption evidence`, and `Fail 不得留下过期 RUNNING`.

- [ ] **Step 2: Run and verify RED while paused**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: failure on missing v6 prompt/rule clauses; it must not fail because the writer is paused.

- [ ] **Step 3: Update the stable rules and thin prompt**

Replace v5 decision/recovery language with the approved v6 state machine. The prompt must direct the model through this exact sequence while remaining below 3000 characters and 10 numbered steps:

```text
待决策：同一任务仅允许一个 pendingDecision，但已选择项保留在 decisionFlow，恢复原任务后可创建下一项。创建后调用 PrepareDecisionNotification，只向返回的私有配置目标发送，禁止 to="me"；Gmail 成功后读取精确 Sent 的 To，再调用 MarkDecisionSubmitted。未收到走 RetryDecisionNotification，最多三次。提供方接受不代表收件。邮件回复必须以全邮箱真实结果调用 ResolveDecisionEmailReply；聊天明确选择只能调用 ResolveDecisionManual -ManualOverride，二者不得互相伪造。无效输入只读。失败必须调用 Fail，由入口按 clean/RECOVERABLE/unsafe 分类，不得留下无 evidence 的过期 RUNNING。
```

Keep candidate ownership, DeepSeek boundaries, status path registration, finalizer, and no-push rules unchanged.

- [ ] **Step 4: Deploy the source prompt while still PAUSED**

View `tzg-hourly-controller` through the automation API. Call `codex_app__automation_update` with the complete existing fields, replace only the prompt from `开发管理/自动工作流控制器提示词.txt`, and keep `status=PAUSED`. Do not create another automation and do not directly edit the TOML.

- [ ] **Step 5: Verify and commit policy files**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,tools,docs/superpowers/specs,docs/superpowers/plans
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/check-automation-workflow.ps1|开发管理/自动工作流规则.txt|开发管理/自动工作流控制器提示词.txt'
git add -- tools/check-automation-workflow.ps1 开发管理/自动工作流规则.txt 开发管理/自动工作流控制器提示词.txt
git diff --cached --check
git commit -m "docs(automation): define decision lifecycle v6"
```

Expected: both checkers report `OK`; the deployment matches source and remains paused; only the three named tracked files are committed.

### Task 8: Run the complete isolated control-plane regression

**Files:**

- Verify only: all Task 1-7 files

- [ ] **Step 1: Run the full relevant suite once after stabilization**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-repair.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,tools,docs/superpowers/specs,docs/superpowers/plans
git diff --check
```

Expected: every script reports `OK`; Git reports no whitespace error.

- [ ] **Step 2: Add and run the complete incident timeline regression**

In `tools/test-automation-controller.ps1`, create one end-to-end scenario that reproduces: wrong Sent target → user reports no receipt → correct manual B resolution → same TQ-057 flow reinspection → second decision creation → clean failure before any business file changes → next Start surfaces only the second pending decision. Assert the first choice remains manual, attempts are append-only, invalid input never changes bytes, state closes to `IDLE`, and no `recovery_state_incomplete` result appears.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

Expected: `test-automation-controller: OK`.

- [ ] **Step 3: Commit the final timeline regression if Step 2 changed the test**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/test-automation-controller.ps1'
git add -- tools/test-automation-controller.ps1
git diff --cached --check
git commit -m "test(automation): cover chained decision incident"
```

If the timeline was already added verbatim in Task 4 or Task 5 and `git diff --quiet -- tools/test-automation-controller.ps1` is true, do not create an empty commit.

- [ ] **Step 4: Verify project scope before live mutation**

```powershell
git status --short
git log -7 --oneline
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller.ps1 Contract | ConvertFrom-Json | Select-Object -ExpandProperty actions
```

Expected: only pre-existing user changes are listed; recent commits match Tasks 1-7/8; the contract lists v6 actions and no retired reply/notified action.

### Task 9: Dry-run and apply the live v5-to-v6 repair

**Files:**

- Modify outside Git: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json`
- Backup outside Git: matching run session and automation memory

- [ ] **Step 1: Recheck pause and live preconditions**

```powershell
$controllerToml = "$env:USERPROFILE\.codex\automations\tzg-hourly-controller\automation.toml"
if (-not (Select-String -Quiet -LiteralPath $controllerToml -Pattern '^status = "PAUSED"$')) { throw 'controller must remain paused during repair' }
$live = Get-Content -Raw "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json" | ConvertFrom-Json
if ($live.schemaVersion -ne 5 -or $live.taskId -ne 'TQ-057' -or $live.checkpoint -ne 'mutation_started') { throw 'live state no longer matches the approved incident' }
if ($live.pendingDecision.decisionId -ne 'DEC-20260715-35ACB87E6C10' -or $live.pendingDecision.resolution.optionKey -ne 'B' -or $live.pendingDecision.resolution.source -ne 'email') { throw 'live decision precondition changed' }
```

- [ ] **Step 2: Dry-run against the actual state**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-repair.ps1 DryRun `
  -RepositoryRoot 'D:\天章游戏开发' `
  -IncidentDecisionId 'DEC-20260715-35ACB87E6C10' `
  -SelectedOption 'B' `
  -EvidenceThreadId '019f63c5-f73c-70a0-9773-5592a3e03194'
```

Expected: projected schema 6, `IDLE`, flow status `IMPLEMENTATION_PENDING`, B/manual, no pending decision, no recovery residue, and `workspaceClassification=clean`. No live file changes.

- [ ] **Step 3: Apply once with explicit operator override**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-repair.ps1 Apply `
  -RepositoryRoot 'D:\天章游戏开发' `
  -IncidentDecisionId 'DEC-20260715-35ACB87E6C10' `
  -SelectedOption 'B' `
  -EvidenceThreadId '019f63c5-f73c-70a0-9773-5592a3e03194' `
  -ManualOverride
```

Expected: the script prints its backup directory and a redacted success summary only.

- [ ] **Step 4: Verify post-repair invariants**

```powershell
$state = pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show | ConvertFrom-Json
if ($state.schemaVersion -ne 6 -or $state.state -ne 'IDLE' -or $state.recoveryCount -ne 0) { throw 'live state was not reset to schema 6 IDLE' }
if ($null -ne $state.runId -or $null -ne $state.leaseExpiresAt -or $null -ne $state.recoveryEvidencePath) { throw 'live repair left lease or recovery residue' }
if ($null -ne $state.pendingDecision -or $state.decisionFlow.taskId -ne 'TQ-057' -or $state.decisionFlow.status -ne 'IMPLEMENTATION_PENDING') { throw 'live flow is not ready to resume TQ-057' }
$first = @($state.decisionFlow.resolvedDecisions | Where-Object decisionId -eq 'DEC-20260715-35ACB87E6C10')
if ($first.Count -ne 1 -or $first[0].resolution.optionKey -ne 'B' -or $first[0].resolution.source -ne 'manual') { throw 'first decision was not preserved and corrected' }
if (@($state.auditCorrections).Count -lt 1) { throw 'source correction audit is missing' }
git diff --name-only
git diff --cached --name-only
```

Expected: assertions pass and Git has no new change from the local state repair.

### Task 10: Publish the real second decision through the repaired normal path

**Files:**

- Modify: `开发管理/自动工作流状态.txt`
- Modify outside Git: live state, run session, and automation memory
- Send externally: one Gmail decision message to the private configured target

- [ ] **Step 1: Start a controlled paused run and re-register TQ-057**

Confirm the actual model through the required Node REPL metadata check. Generate a UUID and call `Start`; expect `action=resume_decision_task`, `taskId=TQ-057`, and `nextCommand=InspectCandidate`. Call `InspectCandidate -TaskId TQ-057`, read its required sources including `开发管理/开发-技术经验.txt`, the task card, the 11 approved spell docs, `Spells.csv`, `SpellData.cs`, `DataConfigImporter.cs`, and `CombatResolver.cs`.

Register the complete discovered potential business paths plus `开发管理/自动工作流状态.txt`, then call `BeginMutation` and `PrepareDecision`. Do not edit any business path.

- [ ] **Step 2: Create the second decision with exact content**

Call `CreateDecision` for TQ-057 with:

```text
TaskSummary: TQ-057：11 个古修术法的双倍率落库口径
DecisionQuestion: 扶摇、演卦同时具有物理与神魂倍率，但当前 Spells.csv、SpellData 与 CombatResolver 只有单一 damageMultiplier。应采用哪种数据表示？
DecisionOptions: A=为所有攻击术法增加明确的 physicalDamageMultiplier 与 soulDamageMultiplier，并迁移现有单通道数据|B=保留 damageMultiplier 作为主通道，新增可选 secondaryDamageMultiplier 并由 type 推断通道|C=本轮只落库其余 9 个术法，扶摇与演卦延期到独立运行时任务
RecommendedOption: A
ImpactSummary: A 语义最明确但需要 CSV、导入器、SpellData、CombatResolver 与既有资产迁移；B 改动较小但保留通道推断；C 不改运行时但 TQ-057 暂不能完整清零文档/数据差异。
```

Expected: first B/manual remains in `decisionFlow.resolvedDecisions`; the new decision is the sole `pendingDecision`; project status renders the new question and a compact first-choice summary.

- [ ] **Step 3: Prepare, send, and verify the exact Sent target**

Call `PrepareDecisionNotification`. Use only its transient `recipientEmail` as the Gmail `to` value; assert it is not `me`. After the connector accepts the send, open/read the exact Sent message by returned provider ID and obtain the actual `To`. Call `MarkDecisionSubmitted` with that provider ID and observed target.

If Gmail send fails, call `MarkDecisionDeliveryFailed` with the connector error category. If the observed target differs, allow `MISADDRESSED`, do not claim notification, and use `RetryDecisionNotification` before a corrected send. Never disclose the address in chat, project status, memory, or Git.

Expected success state: pending status `PROVIDER_ACCEPTED`, one new attempt with matching recipient hash and provider message hash, no raw address.

- [ ] **Step 4: Finish the status-only run**

Call `Finish` with commit message:

```text
chore(automation): publish TQ-057 multiplier decision
```

Expected commit paths exactly:

```text
开发管理/自动工作流状态.txt
```

State after Finish must be `IDLE`, `recoveryCount=0`, first B/manual in the flow, second decision pending, and no recovery fields. The 11 spell docs, CSV, runtime code, assets, and task card remain byte-identical.

- [ ] **Step 5: Record a redacted local audit entry**

Append one automation memory entry containing the repair commits, backup directory basename, first decision ID and corrected source, second decision ID/status, status-only commit, and test suite result. Do not include recipient/sender values, Gmail search text, raw provider IDs, or message body.

### Task 11: Final acceptance and controlled re-enable

**Files:**

- Update through API: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`
- Verify only: project and live state

- [ ] **Step 1: Run final relevant validation while paused**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-decision-status.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-repair.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,tools,docs/superpowers/specs,docs/superpowers/plans
git diff --check
```

Expected: all tests/checkers report `OK` with the controller still paused.

- [ ] **Step 2: Verify live acceptance state and scope**

```powershell
$state = pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show | ConvertFrom-Json
if ($state.state -ne 'IDLE' -or $state.recoveryCount -ne 0 -or $null -ne $state.runId -or $null -ne $state.recoveryEvidencePath) { throw 'controller is not cleanly idle' }
if ($state.decisionFlow.resolvedDecisions[0].resolution.optionKey -ne 'B' -or $state.decisionFlow.resolvedDecisions[0].resolution.source -ne 'manual') { throw 'first decision acceptance failed' }
if ($null -eq $state.pendingDecision -or $state.pendingDecision.taskId -ne 'TQ-057' -or $state.pendingDecision.status -ne 'PROVIDER_ACCEPTED') { throw 'second decision is not the only notified pending decision' }
git show --format= --name-only HEAD
git status --short
```

Expected: the latest live commit contains only the status file. `git status --short` matches the pre-repair user-change baseline, including the unrelated untracked handoff file and no new controller residue.

- [ ] **Step 3: Re-enable the unique writer through the API**

View `tzg-hourly-controller`, preserve all fields from Task 7, and call `codex_app__automation_update` with only `status=ACTIVE`. WF1, WF3, and WF4 remain paused; the daily briefing remains active. Do not trigger an immediate business run and do not create a duplicate automation.

- [ ] **Step 4: Verify active topology and prompt identity**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
git status --short
git log -9 --oneline
```

Expected: checker `OK`, exactly one active write controller, source/deployed prompt equality, live state still `IDLE` with the second decision pending, and no unplanned project changes.

## Plan self-review

- Spec coverage: Tasks 1-2 implement single unresolved decision plus retained same-task flow; Task 4 implements correct addressing, append-only attempts, retry limits, and real email/manual provenance; Tasks 3/5 implement clean/recoverable/unsafe failure closure; Task 6 implements guarded v5 migration and audit correction; Tasks 9-11 repair and validate the live incident.
- Interface consistency: state actions are `RecordDecisionNotification`, `ResolveDecision`, `CompleteDecisionFlow`, `AbortClean`, `RecordRecoverableInterruption`, `BlockUnsafe`, and operator-only `RepairDecisionFlow`; model-facing actions are `PrepareDecisionNotification`, `MarkDecisionSubmitted`, `RetryDecisionNotification`, `MarkDecisionDeliveryFailed`, `ResolveDecisionEmailReply`, and `ResolveDecisionManual`.
- State consistency: `pendingDecision` never stores `RESOLVED`; `decisionFlow` belongs to one task; `IDLE` has no lease/recovery pointer; only `RECOVERABLE` is recoverable; resolved history clears only after a successful business commit.
- Privacy: project files and bounded state keep hashes/summary only; the raw configured target is transiently returned for the connector call and is never copied to Git, memory, status, tests, or final output.
- Scope: the repair changes control-plane code, policy, one project status section, and user-level runtime state. It does not implement or choose the TQ-057 dual-multiplier business model.
- Verification proportionality: the shared control plane receives its full relevant PowerShell regression suite and a live migration dry-run; Unity/BattleSim are skipped because no gameplay or numeric implementation is authorized.
