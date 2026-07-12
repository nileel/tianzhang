# Automation Decision Requests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the TZG hourly controller request, display, email, and safely consume explicit user decisions while continuing independent work.

**Architecture:** The local PowerShell state tool owns a schema-v2 `pendingDecision` record and an untracked private notification configuration. The controller prompt uses that state plus Gmail tools to send one structured email and accept only a whitelisted, strictly formatted reply; project files show a redacted summary. No email address, authorization detail, or decision response is stored in Git.

**Tech Stack:** PowerShell 7, JSON local state, Codex App automation update API, Gmail connector, Markdown project rules, Git.

---

## File structure

| File | Responsibility |
|---|---|
| `tools/automation-controller-state.ps1` | Atomic schema-v2 state, private notification config, decision CRUD and validation. |
| `tools/test-automation-controller-state.ps1` | Isolated regression tests for both legacy lease handling and new decision behavior. |
| `tools/check-automation-workflow.ps1` | Static guardrails proving the active controller contains the required decision policy without secrets or fixed decision IDs. |
| `开发管理/自动工作流规则.txt` | Stable project policy for decision boundaries, routing, visibility and failure fallback. |
| `开发管理/自动工作流状态.txt` | Git-tracked redacted “待你决策” display section. |
| `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml` | Runtime controller prompt, updated only through `codex_app__automation_update`. |
| `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.private.json` | Untracked recipient, allowed sender and Gmail-label configuration; never create in the repository. |

### Task 1: Upgrade local state to schema v2 with decision operations

**Files:**

- Modify: `tools/automation-controller-state.ps1:1-238`
- Modify: `tools/test-automation-controller-state.ps1:1-106`

- [ ] **Step 1: Add failing decision-state tests before changing the state tool**

  Append this block immediately after the existing `complete` assertion and before the transaction-lock contention test. It uses the existing temporary `$statePath` and first acquires a fresh `run-7` lease, so the current lease/recovery regression sequence remains unchanged.

  ```powershell
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:04:00Z')
  Assert-Code $r 0 'acquire decision lease'

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-7',
    '-TaskKind', 'execute', '-TaskId', 'decision-task', '-TaskSummary', '选择限制模式',
    '-DecisionQuestion', '限制字段应在运行时拦截还是仅标注元数据？',
    '-OptionsJson', '[{"key":"A","label":"运行时拦截"},{"key":"B","label":"仅元数据"}]',
    '-RecommendedOption', 'A', '-ImpactSummary', '影响内容可用性与测试范围',
    '-Now', '2026-07-11T00:45:00Z'
  )
  Assert-Code $r 0 'create decision'
  $decision = (Read-TestState).pendingDecision
  if ($decision.status -ne 'PENDING' -or $decision.taskId -ne 'decision-task' -or $decision.options.Count -ne 2) {
    throw 'create decision did not persist a pending decision'
  }

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-7',
    '-TaskKind', 'execute', '-TaskId', 'second-decision', '-TaskSummary', '重复项',
    '-DecisionQuestion', '不应创建第二项', '-OptionsJson', '[{"key":"A","label":"A"}]',
    '-RecommendedOption', 'A', '-ImpactSummary', 'none', '-Now', '2026-07-11T00:46:00Z'
  )
  Assert-Code $r 15 'second pending decision rejection'

  $r = Invoke-StateTool @('MarkDecisionNotified', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:05:00Z')
  Assert-Code $r 0 'mark decision notified'
  if ((Read-TestState).pendingDecision.status -ne 'NOTIFIED') { throw 'notification status was not persisted' }

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'C', '-ReplySource', 'email', '-Now', '2026-07-11T05:06:00Z')
  Assert-Code $r 15 'unknown option rejection'

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'A', '-ReplySource', 'email', '-Now', '2026-07-11T05:07:00Z')
  Assert-Code $r 0 'resolve decision'
  if ((Read-TestState).pendingDecision.status -ne 'RESOLVED' -or (Read-TestState).pendingDecision.resolution.optionKey -ne 'A') {
    throw 'valid decision resolution was not persisted'
  }
  ```

- [ ] **Step 2: Run the tests and confirm the missing action fails**

  Run:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
  ```

  Expected: a nonzero exit with a `ValidateSet` or unknown-action error for `CreateDecision`.

- [ ] **Step 3: Implement schema migration and the decision data contract**

  Replace the top-level `ValidateSet` with the following full action list and add the decision parameters directly after `$ExpectedPaths`:

  ```powershell
  [ValidateSet('Acquire','Renew','Checkpoint','CreateDecision','MarkDecisionNotified','MarkDecisionDeliveryFailed','ResolveDecision','ClearResolvedDecision','Complete','Fail','Show','ResetBlocked')]
  [string]$Action,
  # existing parameters remain in their current order
  [string]$ExpectedPaths,
  [string]$TaskSummary,
  [string]$DecisionQuestion,
  [string]$OptionsJson,
  [string]$RecommendedOption,
  [string]$ImpactSummary,
  [string]$DecisionId,
  [string]$OptionKey,
  [ValidateSet('email')]
  [string]$ReplySource,
  [string]$NotificationError,
  ```

  Replace `New-State` with a schema-v2 shape that preserves all existing fields and adds a nullable decision:

  ```powershell
  function New-State {
    [ordered]@{
      schemaVersion = 2
      controllerId = $ControllerId
      runId = $null
      state = 'IDLE'
      leaseExpiresAt = $null
      taskKind = $null
      taskId = $null
      checkpoint = $null
      expectedPaths = @()
      recoveryCount = 0
      lastQueueAuditAt = $null
      lastError = $null
      pendingDecision = $null
    }
  }
  ```

  In `Import-State`, accept `schemaVersion` 1 and 2. For v1, copy its existing fields into `New-State`, leave `pendingDecision` null, and return v2 in memory. Reject every other schema version. Preserve the existing atomic `Export-State` replacement behavior.

  Add these helpers before the `switch`:

  ```powershell
  function Require-PendingDecision {
    param([System.Collections.IDictionary]$State)
    if ($null -eq $State.pendingDecision) { Exit-WithCode 'No pending decision exists' $script:ExitInvalidArguments }
  }

  function Get-Options {
    param([string]$Json)
    try { $options = @($Json | ConvertFrom-Json) } catch { Exit-WithCode 'OptionsJson must be a JSON array' $script:ExitInvalidArguments }
    if ($options.Count -lt 2 -or @($options | Where-Object { [string]::IsNullOrWhiteSpace($_.key) -or [string]::IsNullOrWhiteSpace($_.label) }).Count -gt 0) {
      Exit-WithCode 'OptionsJson requires at least two keyed options' $script:ExitInvalidArguments
    }
    if (@($options.key | Sort-Object -Unique).Count -ne $options.Count) { Exit-WithCode 'Option keys must be unique' $script:ExitInvalidArguments }
    @($options | ForEach-Object { [ordered]@{ key = [string]$_.key; label = [string]$_.label } })
  }
  ```

  Add switch cases with these invariants: `CreateDecision` requires the owner, all task/question/impact inputs, no existing decision, two or more options, and a recommendation in the option keys; it assigns `DEC-<UTC yyyyMMdd>-<12 hex characters>` and `PENDING`. `MarkDecisionNotified` changes only `PENDING` or `DELIVERY_FAILED` to `NOTIFIED`. `MarkDecisionDeliveryFailed` changes an unresolved decision to `DELIVERY_FAILED` and stores only a 240-character error category. `ResolveDecision` requires exact ID, `NOTIFIED`/`DELIVERY_FAILED`/`REPLY_INVALID`, a known key, and `ReplySource=email`; it writes `RESOLVED` plus `{ optionKey, source, resolvedAt }`. `ClearResolvedDecision` requires `RESOLVED` and sets the object to null. Every mutating action renews the lease with `Set-Lease` before export.

  Keep `Complete` unchanged except it must not alter `pendingDecision`.

- [ ] **Step 4: Extend tests for migration, completion and invalid-response safety**

  Add these cases after the resolve assertion:

  ```powershell
  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:08:00Z')
  Assert-Code $r 0 'complete with resolved decision'
  if ((Read-TestState).pendingDecision.status -ne 'RESOLVED') { throw 'complete cleared a resolved decision' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-8', '-Now', '2026-07-11T05:09:00Z')
  Assert-Code $r 0 'acquire to clear resolved decision'
  $r = Invoke-StateTool @('ClearResolvedDecision', '-StatePath', $statePath, '-RunId', 'run-8', '-Now', '2026-07-11T05:10:00Z')
  Assert-Code $r 0 'clear resolved decision'
  if ($null -ne (Read-TestState).pendingDecision) { throw 'resolved decision was not cleared' }

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":1,"state":"IDLE","controllerId":"legacy","lastQueueAuditAt":null}')
  $legacy = Read-TestState
  if ($legacy.schemaVersion -ne 2 -or $null -ne $legacy.pendingDecision -or $legacy.state -ne 'IDLE') { throw 'schema v1 was not migrated safely' }
  ```

  Move the existing takeover and recovery scenario to run after a fresh `Acquire` so it continues to test the original lease behavior without relying on a deleted decision.

- [ ] **Step 5: Run the focused test suite and commit**

  Run:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
  git diff --check
  ```

  Expected: `test-automation-controller-state: OK` and no whitespace findings.

  Commit:

  ```powershell
  git add tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
  git commit -m "feat: track automation decision requests"
  ```

### Task 2: Define redacted policy and project visibility

**Files:**

- Modify: `开发管理/自动工作流规则.txt:14-46`
- Modify: `开发管理/自动工作流状态.txt:5-29`

- [ ] **Step 1: Add decision policy to the stable rules source**

  Insert a `## 待你决策` section between `动态选题` and `队列维护触发` with this policy text:

  ```markdown
  ## 待你决策

  - 当多个方案会改变架构、运行时行为、数据语义、对外体验、任务范围、冻结边界或主责，且事实源不能唯一推出结论时，控制器必须创建待决策项；删除真实资产、保存数据迁移、外部发送及覆盖有歧义人工改动也必须请求决定。
  - 待决策项不是失败关闭：控制器完成当前安全检查后释放租约，跳过该项及其依赖项，并继续选择其他主责匹配、依赖满足且不依赖该决定的候选。
  - 每次仅保留一个未解决决策。它必须同时写入本机状态、`自动工作流状态.txt` 的“待你决策”区、自动化任务标题/消息，并尝试发送邮件；收件地址、回信地址和搜索条件只保存在本机私有配置。
  - 只接受允许发件人对同一决策编号的严格回复 `DEC-…：选 A` 或 `DEC-…: 选择 A`。未知发件人、编号不符、多个选项或模糊回复均不得推进任务。
  - 邮件发送或读取失败时保留待决策状态并受限重试；不得自动选择、失败关闭、AUTO-BLOCKED 或制造空提交。有效回复恢复原任务后才清除该项。
  ```

- [ ] **Step 2: Add a redacted visible status section**

  Insert this immediately before `## 最近有效结果` in `开发管理/自动工作流状态.txt`:

  ```markdown
  ## 待你决策

  当前无待决策项。

  > 自动控制器出现必须由负责人选择的事项时，在此显示决策编号、事项、选项、推荐项、创建时间、通知状态和严格回复格式；不得写入邮箱地址、搜索条件、令牌或邮件正文。
  ```

- [ ] **Step 3: Validate the policy files and commit**

  Run:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理
  git diff --check
  ```

  Expected: `check-review-text: OK` and no whitespace findings.

  Commit:

  ```powershell
  git add 开发管理/自动工作流规则.txt 开发管理/自动工作流状态.txt
  git commit -m "docs: define automation decision visibility"
  ```

### Task 3: Make decision policy mechanically verifiable

**Files:**

- Modify: `tools/check-automation-workflow.ps1:12-85`

- [ ] **Step 1: Add failing static checks for the required controller clauses**

  After the existing title checks, add these exact guards:

  ```powershell
  $decisionBoundaryPattern = '待决策与邮件回执|CreateDecision|禁止自行决定'
  $decisionVisibilityPattern = '自动工作流状态\.txt|TZG｜待决策|需要决策'
  $decisionFallbackPattern = 'MarkDecisionDeliveryFailed|不得让控制器作出默认选择|继续正常动态路由'
  $emailLiteralPattern = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'

  Require-Match $controller $decisionBoundaryPattern 'controller lacks a decision-request boundary'
  Require-Match $controller $decisionVisibilityPattern 'controller lacks decision visibility instructions'
  Require-Match $controller $decisionFallbackPattern 'controller lacks decision delivery fallback instructions'
  Reject-Match $controller $emailLiteralPattern 'controller prompt contains an email address'
  ```

- [ ] **Step 2: Run the workflow checker before the controller prompt update**

  Run:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
  ```

  Expected: `check-automation-workflow: FAILED` with the three missing decision-policy messages. The email-address check must not fail.

- [ ] **Step 3: Leave the checker uncommitted until the controller update makes it pass**

  Do not commit this intentional failing state. Continue directly to Task 4, then return to this task’s verification and commit step.

### Task 4: Encode controller routing and Gmail protocol

**Files:**

- Modify: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml` through `codex_app__automation_update`
- Create (untracked): `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.private.json`

- [ ] **Step 1: Add a private local notification configuration without touching the repository**

  Create the directory and write a UTF-8 JSON file only under `%USERPROFILE%/.codex/automation-state/`. Its exact schema is:

  ```json
  {
    "schemaVersion": 1,
    "recipientEmail": "<configured outside Git>",
    "allowedReplyFrom": "<configured outside Git>",
    "gmailLabel": "TZG/Automation-Decisions"
  }
  ```

  Populate both address fields from the user-provided recipient at execution time, never from the plan, commit, project markdown, or automation prompt. Restrict filesystem ACLs to the current Windows user. Confirm with `git status --short --untracked-files=all` that this file is outside the repository.

- [ ] **Step 2: Read the full existing automation definition before update**

  Call `codex_app__automation_update` with `{ "id": "tzg-wf2-codex-execute-1", "mode": "view" }`. Preserve its existing name, schedule, active status, model, reasoning effort, project target, destination and execution environment.

- [ ] **Step 3: Append the exact decision-routing contract to the controller prompt**

  Append the following numbered block after its existing result section, renumbering only the prompt instructions if needed; do not add an address or a hardcoded `DEC-` value:

  ```text
  待决策与邮件回执：
  - 在读取队列后调用 automation-controller-state.ps1 Show。若 pendingDecision 未解决，先用 Gmail 搜索允许发件人、标签和同一 decisionId 的未消费回复。只接受主题或正文含相同编号、且正文严格为 `DEC-…：选 A`、`DEC-…: 选 A` 或 `DEC-…: 选择 A` 的最新单选回复；其他邮件调用 MarkDecisionDeliveryFailed 仅记录“invalid_reply”，不推断选择。
  - 有效回复前，排除该任务及其依赖项；继续正常动态路由其余无依赖候选。合法回复必须先调用 ResolveDecision，再优先恢复原任务；原任务完成或将该选择写入其归档后调用 ClearResolvedDecision。
  - 执行中遇到项目事实不能唯一推出、架构/数据语义/可见行为会变化、范围或冻结边界会突破、不可逆操作或事实源冲突时，禁止自行决定。调用 CreateDecision，传入事实背景、至少两个选项、推荐项和影响；更新项目状态的“待你决策”区，标题改为 `TZG｜待决策：<事项>`，并使用 Gmail 发邮件。
  - 邮件标题为 `【天章游戏开发｜需要决策】<decisionId>｜<事项>`。正文包含背景、选项、推荐项及理由、影响、严格回复示例、状态文件入口和“未回复前不会自行决定”。发送成功后调用 MarkDecisionNotified；发送或读取失败调用 MarkDecisionDeliveryFailed，保留状态并在后续轮次受限重试。
  - 邮件与回信能力失败、标题更新失败或状态文件更新失败不得让控制器作出默认选择、调用 Fail、进入 AUTO-BLOCKED 或创建空提交；最终消息必须报告决策编号、可见入口、邮件状态和本轮独立任务是否继续。
  ```

- [ ] **Step 4: Update the automation through the app API and inspect the persisted TOML**

  Call `codex_app__automation_update` in update mode using the full preserved definition and amended prompt. Then read `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml` and verify all three conditions:

  ```powershell
  Select-String -LiteralPath "$env:USERPROFILE\.codex\automations\tzg-wf2-codex-execute-1\automation.toml" -Pattern '待决策与邮件回执','CreateDecision','MarkDecisionNotified','继续正常动态路由'
  git status --short --untracked-files=all
  ```

  Expected: all four decision-policy strings are present, and no recipient address is present in tracked or untracked project paths.

- [ ] **Step 5: Return to Task 3 and make its checker pass, then commit it**

  Run:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
  git diff --check
  ```

  Expected: `check-automation-workflow: OK` and no whitespace findings.

  Commit:

  ```powershell
  git add tools/check-automation-workflow.ps1
  git commit -m "test: guard automation decision protocol"
  ```

### Task 5: End-to-end dry run and final verification

**Files:**

- Modify if needed: `开发管理/自动工作流状态.txt`
- Modify if needed: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml` through `codex_app__automation_update`

- [ ] **Step 1: Exercise the state protocol in an isolated temporary state file**

  Use `tools/automation-controller-state.ps1` with a `%TEMP%` state path to acquire a lease, create a two-option decision, mark it notified, resolve option `A`, complete the work unit, and clear the resolved decision. Use the same input schema as Task 1. Verify `Show` never prints a `recipientEmail` or `allowedReplyFrom` property.

- [ ] **Step 2: Verify Gmail capability without sending production mail**

  Use the Gmail connector’s draft-creation operation to create a draft addressed from the private configuration, with the exact title and body protocol from Task 4. Inspect the resulting draft metadata, then delete the draft. Do not use `send_email` during this dry run and do not include a real decision number from production state.

- [ ] **Step 3: Verify tracked files, local configuration boundary, and controller policy**

  Run:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
  powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
  powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,tools,docs/superpowers/specs,docs/superpowers/plans
  git diff --check
  git status --short --untracked-files=all
  ```

  Expected: both automation tests report `OK`; text check reports `OK`; no whitespace errors; only intended tracked project files are changed; and the private JSON remains outside the repository.

- [ ] **Step 4: Update project state only if the dry run exposed a durable outcome, then commit**

  If the dry run leaves no active decision and no persistent project result, do not modify `开发管理/自动工作流状态.txt`. Otherwise record only the redacted outcome under `最近控制器加固`, then run the same text and whitespace checks.

  Commit only tracked project files from this task:

  ```powershell
  git add 开发管理/自动工作流状态.txt
  git commit -m "docs: record decision workflow verification"
  ```

  If no tracked state file changed, do not create an empty commit.

## Plan self-review

- Spec coverage: Task 1 implements the persistent decision record and validated resolution; Task 2 covers decision boundary and Git-visible status; Task 3 prevents decision-protocol prompt regressions; Task 4 covers title, message, Gmail send/read and independent routing; Task 5 validates redaction, dry-run behavior and the full verification suite.
- Placeholder scan: the only angle-bracket strings are runtime protocol values and the external, non-versioned recipient configuration; no implementation work is deferred or unspecified.
- Type consistency: the plan uses one `pendingDecision` object; one option-key format; the same `CreateDecision`, `MarkDecisionNotified`, `MarkDecisionDeliveryFailed`, `ResolveDecision`, and `ClearResolvedDecision` action names; and one `DEC-<UTC date>-<random suffix>` identifier format throughout.
