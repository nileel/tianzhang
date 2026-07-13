# Hourly Automation Controller v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the single hourly controller so it replenishes based on runnable supply, safely works beside unrelated local changes, and serially dispatches Codex or DeepSeek without reintroducing multiple writers.

**Architecture:** Keep `TZG Hourly Controller` as the only scheduled writer. Extend the local state tool for queue fingerprints and DeepSeek backoff, add a deterministic Git workspace guard for path isolation, then make the project rules and controller prompt consume those helpers. The controller remains paused until migration, unit checks, static checks, and a read-only policy dry run pass.

**Tech Stack:** PowerShell 7, Git, Codex desktop cron automation, Node REPL identity metadata, Claude CLI / DeepSeek local proxy, Markdown project rules.

---

## File map and boundaries

- Modify `tools/automation-controller-state.ps1`: schema v3, runnable-supply fingerprints, task executor, and DeepSeek backoff.
- Modify `tools/test-automation-controller-state.ps1`: state migration and new action regression tests.
- Create `tools/automation-workspace-guard.ps1`: snapshot, overlap check, and post-task verification for pre-existing Git changes.
- Create `tools/test-automation-workspace-guard.ps1`: temporary-repository tests for staged, unstaged, untracked, rename, delete, and path-limited commit behavior.
- Modify `tools/check-automation-workflow.ps1`: current controller path plus v2 prompt/rule invariants.
- Modify `开发管理/自动工作流规则.txt`: v2 routing, 2→5 runnable watermarks, dirty-path isolation, self-owned status commits, and serial worker dispatch.
- Modify `开发管理/状态与建议维护规则.txt`: replace total-row maintenance triggers with runnable supply and fingerprint suppression.
- Modify `开发管理/AI协作规则.txt`: authorize controller-owned sequential DeepSeek invocation while keeping WF3 paused and review boundaries intact.
- Modify `开发管理/DeepSeek工作提示词.txt`: controller wrapper owns lease, expected paths, staging, and commit; DeepSeek only edits and hands off.
- Modify `AGENTS.md` and `CLAUDE.md`: route controller-invoked Claude/DeepSeek identity and authorization through the same rules.
- Modify `docs/superpowers/specs/2026-07-11-hourly-automation-controller-design.md`: mark v1 as implemented and superseded for routing by v2 without deleting it.
- Modify `docs/superpowers/specs/2026-07-13-hourly-automation-controller-v2-design.md`: link this implementation plan and record the predecessor relationship.
- Modify `开发管理/自动工作流状态.txt`: first commit the existing controller-owned pending-decision visibility change; update hardening/canary facts only after evidence exists.
- Update `%USERPROFILE%\.codex\automations\tzg-hourly-controller\automation.toml` only through the Codex automation update tool; do not edit the TOML directly.
- Do not relocate or delete existing `docs/superpowers/specs/` or `docs/superpowers/plans/` files. They are durable decision records; daily routing files should link only to currently authoritative specifications.

## Task 1: Preserve the current controller-owned pending-decision result

**Files:**
- Modify: `开发管理/自动工作流状态.txt`
- Read only: `%USERPROFILE%\.codex\automation-state\tzg-hourly-controller.json`
- Read only: `%USERPROFILE%\.codex\automations\tzg-hourly-controller\memory.md`

- [ ] **Step 1: Confirm the controller is paused and capture the dirty baseline**

Run:

```powershell
Get-Content -Raw "$env:USERPROFILE\.codex\automations\tzg-hourly-controller\automation.toml" | Select-String '^status = "PAUSED"$'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show
git status --short --untracked-files=all
git diff -- 开发管理/自动工作流状态.txt
```

Expected: the controller is `PAUSED`; the local state contains one pending decision; the project status diff describes that same decision and no unrelated path is staged.

- [ ] **Step 2: Fail closed if ownership cannot be proven**

Compare the `decisionId`, task ID, question summary, options, recommendation, and notification state in local JSON, automation memory, and the project diff. If they do not describe the same controller result, stop without editing or staging anything and report the mismatched fields.

- [ ] **Step 3: Validate the existing project-visible result**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理/自动工作流状态.txt
& tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流状态.txt' -Fix
& tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流状态.txt'
git diff --check -- 开发管理/自动工作流状态.txt
```

Expected: every command exits 0.

- [ ] **Step 4: Commit only the controller-owned status result**

```powershell
git add -- 开发管理/自动工作流状态.txt
git diff --cached --check
git diff --cached --name-only
git commit -m "chore(automation): record pending decision"
```

Expected: the cached name list contains only `开发管理/自动工作流状态.txt`; the commit succeeds; `git status --short` is clean before v2 implementation starts.

## Task 2: Add failing state-tool tests for queue supply and worker backoff

**Files:**
- Modify: `tools/test-automation-controller-state.ps1`
- Test: `tools/test-automation-controller-state.ps1`

- [ ] **Step 1: Add schema-v3 and executor assertions after the existing checkpoint test**

Add a checkpoint call with `-TaskExecutor codex`, then assert:

```powershell
$state = Read-TestState
if ($state.schemaVersion -ne 3 -or $state.taskExecutor -ne 'codex') {
  throw 'schema v3 or task executor was not persisted'
}
```

- [ ] **Step 2: Add queue-state behavior tests**

Add this sequence while `run-1` owns the lease:

```powershell
$r = Invoke-StateTool @(
  'RecordQueueState', '-StatePath', $statePath, '-RunId', 'run-1',
  '-QueueFingerprint', 'queue-a', '-RunnableCount', '0', '-NoCandidate',
  '-QueueAuditCompleted', '-Now', '2026-07-11T00:41:00Z'
)
Assert-Code $r 0 'record no-candidate queue state'
$state = Read-TestState
if ($state.lastQueueFingerprint -ne 'queue-a' -or
    $state.lastNoCandidateFingerprint -ne 'queue-a' -or
    $state.lastRunnableCount -ne 0 -or
    -not $state.lastQueueAuditAt) {
  throw 'queue state was not persisted'
}

$r = Invoke-StateTool @(
  'RecordQueueState', '-StatePath', $statePath, '-RunId', 'run-1',
  '-QueueFingerprint', 'queue-b', '-RunnableCount', '3',
  '-Now', '2026-07-11T00:42:00Z'
)
Assert-Code $r 0 'record runnable queue state'
if ($null -ne (Read-TestState).lastNoCandidateFingerprint) {
  throw 'runnable queue state did not clear no-candidate suppression'
}
```

- [ ] **Step 3: Add DeepSeek backoff tests**

```powershell
$r = Invoke-StateTool @(
  'RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1',
  '-WorkerId', 'deepseek', '-WorkerError', 'proxy unavailable',
  '-BackoffMinutes', '180', '-Now', '2026-07-11T00:43:00Z'
)
Assert-Code $r 0 'record DeepSeek backoff'
$worker = (Read-TestState).workerState.deepseek
if ($worker.failureCount -ne 1 -or
    $worker.backoffUntil -ne '2026-07-11T03:43:00.0000000+00:00' -or
    $worker.lastError -ne 'proxy unavailable') {
  throw 'DeepSeek backoff was not persisted'
}

$r = Invoke-StateTool @(
  'ClearWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1',
  '-WorkerId', 'deepseek', '-Now', '2026-07-11T00:44:00Z'
)
Assert-Code $r 0 'clear DeepSeek backoff'
$worker = (Read-TestState).workerState.deepseek
if ($worker.failureCount -ne 0 -or $null -ne $worker.backoffUntil -or $null -ne $worker.lastError) {
  throw 'DeepSeek backoff was not cleared'
}
```

- [ ] **Step 4: Extend migration assertions**

Keep the existing schema-v1 fixture and add a schema-v2 fixture. Both must import as schema 3 with a null executor, null queue fingerprints, and an initialized `workerState.deepseek` object.

- [ ] **Step 5: Run the test and verify it fails for missing v3 behavior**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: nonzero exit caused by an unsupported action, missing `TaskExecutor`, or schema still being 2. Do not weaken the new assertions.

## Task 3: Implement state schema v3 and new actions

**Files:**
- Modify: `tools/automation-controller-state.ps1`
- Test: `tools/test-automation-controller-state.ps1`

- [ ] **Step 1: Extend parameters and actions**

Add these actions and parameters:

```powershell
[ValidateSet(
  'Acquire','Renew','Checkpoint','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure',
  'CreateDecision','MarkDecisionNotified','MarkDecisionDeliveryFailed','ResolveDecision',
  'ClearResolvedDecision','Complete','Fail','Show','ResetBlocked'
)]
[string]$Action,
[ValidateSet('codex','deepseek')]
[string]$TaskExecutor,
[string]$QueueFingerprint,
[int]$RunnableCount = -1,
[switch]$NoCandidate,
[ValidateSet('deepseek')]
[string]$WorkerId,
[string]$WorkerError,
[ValidateRange(1,1440)]
[int]$BackoffMinutes = 180
```

- [ ] **Step 2: Replace the state constructor with schema v3 fields**

The normalized state must contain:

```powershell
[ordered]@{
  schemaVersion = 3
  controllerId = $ControllerId
  runId = $null
  state = 'IDLE'
  leaseExpiresAt = $null
  taskKind = $null
  taskId = $null
  taskExecutor = $null
  checkpoint = $null
  expectedPaths = @()
  recoveryCount = 0
  lastQueueAuditAt = $null
  lastQueueFingerprint = $null
  lastNoCandidateFingerprint = $null
  lastRunnableCount = $null
  workerState = [ordered]@{
    deepseek = [ordered]@{
      failureCount = 0
      backoffUntil = $null
      lastError = $null
    }
  }
  lastError = $null
  pendingDecision = $null
}
```

`Import-State` must accept schemas 1, 2, and 3, copy known top-level fields, normalize nested `workerState.deepseek`, and export schema 3 without discarding an existing pending decision.

- [ ] **Step 3: Persist the selected executor in Checkpoint**

Inside `Checkpoint`, add:

```powershell
if ($TaskExecutor) { $state.taskExecutor = $TaskExecutor }
```

Clear `taskExecutor` in the same places that currently clear `taskKind` and `taskId`: fresh IDLE acquisition, `Complete`, and `ResetBlocked`. Preserve it during expired-lease recovery.

- [ ] **Step 4: Implement RecordQueueState**

Require lease ownership, nonblank `QueueFingerprint`, and `RunnableCount -ge 0`. Set `lastQueueFingerprint` and `lastRunnableCount`; set `lastNoCandidateFingerprint` only when `-NoCandidate` is present, otherwise clear it. When `-QueueAuditCompleted` is present, set `lastQueueAuditAt`. Renew and atomically export the state.

- [ ] **Step 5: Implement DeepSeek backoff actions**

`RecordWorkerFailure` must require lease ownership, `WorkerId=deepseek`, and a nonblank `WorkerError`; trim the error to 240 characters, increment `failureCount`, and set `backoffUntil` from `Now + BackoffMinutes`. `ClearWorkerFailure` must reset the three DeepSeek fields. Both actions renew the lease and atomically export.

- [ ] **Step 6: Run state tests**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: `automation-controller-state tests: OK` and exit 0.

- [ ] **Step 7: Commit the state-tool slice**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller-state.ps1|tools/test-automation-controller-state.ps1' -Fix
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller-state.ps1|tools/test-automation-controller-state.ps1'
git add -- tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
git diff --cached --check
git commit -m "feat(automation): track runnable supply and worker backoff"
```

## Task 4: Add failing Git workspace-guard tests

**Files:**
- Create: `tools/test-automation-workspace-guard.ps1`
- Test: `tools/test-automation-workspace-guard.ps1`

- [ ] **Step 1: Build an isolated Git fixture**

The test must create a temporary repository, configure a local test identity, commit `human.txt`, `staged.txt`, `task.txt`, `renamed.txt`, and `deleted.txt`, then create these pre-existing changes:

```powershell
[IO.File]::WriteAllText((Join-Path $repo 'human.txt'), "human edit`n")
[IO.File]::WriteAllText((Join-Path $repo 'staged.txt'), "staged edit`n")
git -C $repo add -- staged.txt
[IO.File]::WriteAllText((Join-Path $repo 'untracked.txt'), "untracked edit`n")
git -C $repo mv -- renamed.txt renamed-by-human.txt
$resolvedRepo = [IO.Path]::GetFullPath($repo)
$resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $resolvedRepo.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing to delete outside the temporary test root: $resolvedRepo"
}
Remove-Item -LiteralPath (Join-Path $repo 'deleted.txt')
```

- [ ] **Step 2: Assert the future command interface**

The test must call:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File $guard Snapshot -RepositoryRoot $repo -BaselinePath $baseline
pwsh -NoProfile -ExecutionPolicy Bypass -File $guard Check -RepositoryRoot $repo -BaselinePath $baseline -ExpectedPaths 'task.txt'
pwsh -NoProfile -ExecutionPolicy Bypass -File $guard Check -RepositoryRoot $repo -BaselinePath $baseline -ExpectedPaths 'human.txt'
```

Expected exit codes: Snapshot `0`, disjoint Check `0`, overlapping Check `20`.

- [ ] **Step 3: Test path-limited commit isolation**

Modify `task.txt`, add it, and run:

```powershell
git -C $repo add -- task.txt
git -C $repo commit --only -m 'test: task-only commit' -- task.txt
pwsh -NoProfile -ExecutionPolicy Bypass -File $guard Verify -RepositoryRoot $repo -BaselinePath $baseline -ExpectedPaths 'task.txt'
```

Expected: Verify exits 0; `staged.txt` remains staged; `human.txt`, `untracked.txt`, rename, and delete remain present exactly as before; the commit contains only `task.txt`.

- [ ] **Step 4: Test tampering detection and path validation**

After changing `human.txt` again, Verify must exit `21`. Check must reject absolute paths, `..`, empty segments, and parent/child overlap with exit `15`. A candidate directory such as `src/Assets` must conflict with a dirty descendant such as `src/Assets/Data/example.asset`.

- [ ] **Step 5: Run the test and verify it fails because the guard does not exist**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
```

Expected: nonzero exit identifying the missing `tools/automation-workspace-guard.ps1`.

## Task 5: Implement the Git workspace guard

**Files:**
- Create: `tools/automation-workspace-guard.ps1`
- Test: `tools/test-automation-workspace-guard.ps1`

- [ ] **Step 1: Define the public interface and exit codes**

Use:

```powershell
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Snapshot','Check','Verify')]
  [string]$Action,
  [string]$RepositoryRoot = (Get-Location).Path,
  [Parameter(Mandatory = $true)]
  [string]$BaselinePath,
  [string]$ExpectedPaths
)

$script:ExitInvalidArguments = 15
$script:ExitConflict = 20
$script:ExitBaselineChanged = 21
```

- [ ] **Step 2: Normalize paths and reject unsafe pathspecs**

Normalize separators to `/`, trim leading `./`, reject rooted paths, empty values, `.` and `..` segments, and deduplicate with ordinal-ignore-case comparison. Two paths overlap when they are equal or either is the other's directory prefix followed by `/`.

- [ ] **Step 3: Capture a porcelain-v2 baseline**

Run `git status --porcelain=v2 -z --untracked-files=all`, parse ordinary, rename/copy, unmerged, and untracked records, and record both sides of a rename. For each path store:

```powershell
[ordered]@{
  path = $normalizedPath
  kind = $kind
  indexStatus = $indexStatus
  worktreeStatus = $worktreeStatus
  indexBlob = $indexBlobOrNull
  worktreeHash = $worktreeHashOrNull
}
```

Use the index blob ID from porcelain output for staged content. Hash an existing worktree file with `git hash-object --no-filters -- $relativePath`; use null for deletion. Serialize a sorted, UTF-8-without-BOM JSON document containing `schemaVersion=1`, repository root, HEAD, and entries.

- [ ] **Step 4: Implement candidate overlap checks**

`Check` loads the baseline and exits 20 when any expected path overlaps any baseline entry. On success, emit compact JSON containing `safe=true`, normalized expected paths, and an empty conflict list. On conflict, emit `safe=false` and the exact conflicting paths before exiting 20.

- [ ] **Step 5: Implement post-task verification**

`Verify` takes a fresh snapshot, removes entries that overlap `ExpectedPaths`, and requires the remaining entries to exactly match the original baseline entries in path, kind, index status, worktree status, index blob, and worktree hash. It must also reject a changed repository root. A changed HEAD is allowed only when `git diff-tree --no-commit-id --name-only -r HEAD` contains no path outside `ExpectedPaths`.

- [ ] **Step 6: Run workspace-guard tests**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
```

Expected: `automation-workspace-guard tests: OK` and exit 0.

- [ ] **Step 7: Commit the workspace guard**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-workspace-guard.ps1|tools/test-automation-workspace-guard.ps1' -Fix
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-workspace-guard.ps1|tools/test-automation-workspace-guard.ps1'
git add -- tools/automation-workspace-guard.ps1 tools/test-automation-workspace-guard.ps1
git diff --cached --check
git commit -m "feat(automation): isolate unrelated workspace changes"
```

## Task 6: Add failing static policy checks for controller v2

**Files:**
- Modify: `tools/check-automation-workflow.ps1`
- Test: `tools/check-automation-workflow.ps1`

- [ ] **Step 1: Point the checker at the real controller directory**

Replace:

```powershell
$controller = Join-Path $automationRoot 'tzg-wf2-codex-execute-1\automation.toml'
```

with:

```powershell
$controller = Join-Path $automationRoot 'tzg-hourly-controller\automation.toml'
```

- [ ] **Step 2: Require v2 rule and prompt invariants**

Add `Require-Match` checks for these meanings in both the authoritative rule file and controller prompt:

```text
低水位：2
高水位：5
automation-workspace-guard.ps1
RecordQueueState
RecordWorkerFailure
DeepSeek工作提示词.txt
git commit --only
控制器自有状态变更必须提交或保留恢复指针
```

Add `Reject-Match` checks for the old global behavior:

```text
若无恢复指针但工作区不干净，调用 Complete 释放本轮租约后只读退出
当前队列少于 5 条
```

- [ ] **Step 3: Require single-writer DeepSeek boundaries**

The checker must verify that WF3 remains paused, the controller prompt forbids parallel agents, DeepSeek cannot commit directly, and only the controller owns staging/commit.

- [ ] **Step 4: Run the checker against the paused old configuration**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: nonzero exit listing missing v2 markers. This is the red test; do not loosen it to pass the old prompt.

## Task 7: Update project rules and specification lifecycle markers

**Files:**
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `开发管理/DeepSeek工作提示词.txt`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-07-11-hourly-automation-controller-design.md`
- Modify: `docs/superpowers/specs/2026-07-13-hourly-automation-controller-v2-design.md`
- Modify: `tools/check-automation-workflow.ps1`

- [ ] **Step 1: Mark the specification relationship without deleting history**

Add to the v1 header:

```markdown
> 生命周期：已实施；当前调度优化由 `docs/superpowers/specs/2026-07-13-hourly-automation-controller-v2-design.md` 继承。本文保留初始单控制器取舍与部署证据，不再作为 v2 队列/脏工作区/执行器路由规则源。
```

Add to the v2 header:

```markdown
> 前身：`docs/superpowers/specs/2026-07-11-hourly-automation-controller-design.md`
> 实施计划：`docs/superpowers/plans/2026-07-13-hourly-automation-controller-v2-implementation.md`
```

- [ ] **Step 2: Replace total-row maintenance with runnable watermarks**

In both workflow rules and status-maintenance rules, define a runnable task using status, dependencies, reviewed DeepSeek prerequisites, configured executor, pending decision, content gates, complete expected paths, and workspace overlap. Define low water 2, high water 5, completion-time replenishment, independent maintenance only when no safe candidate or queue facts are invalid, and fingerprint suppression for unchanged empty backlog.

- [ ] **Step 3: Replace global dirty exit with path isolation**

Require `automation-workspace-guard.ps1 Snapshot` before candidate selection, `Check` before `task_selected`, and `Verify` before and after path-limited commit. Explicitly forbid stash/reset/checkout/clean and require trying the next candidate after one path conflict.

- [ ] **Step 4: Define controller-owned status commits**

State that a pending-decision visibility update is a valid result: register the status path before mutation, validate it, commit it with a controller-status message, and preserve a recovery pointer on interruption. Explicitly forbid `IDLE + no recovery pointer + controller-owned dirty file`.

- [ ] **Step 5: Define sequential DeepSeek delegation**

Keep WF3 paused. Authorize the controller to invoke Claude CLI only after selecting an eligible DeepSeek task. Require the child to read the DeepSeek prompt, make only expected-path edits, mark outputs unreviewed, and write a handoff. The child must not stage or commit; the controller validates and commits. A failed preflight records backoff and continues to a Codex candidate before any mutation.

- [ ] **Step 6: Run project text checks**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,docs/superpowers,tools
git diff --check
```

Expected: exit 0. The automation static checker still fails because the paused controller prompt has not yet been updated.

## Task 8: Replace the paused controller prompt through the automation API

**External configuration:**
- Update: automation ID `tzg-hourly-controller`
- Keep: name, hourly schedule, local project target, model, reasoning effort, and `PAUSED` status

- [ ] **Step 1: View and preserve the complete automation record**

Use the Codex automation update tool in view mode for `tzg-hourly-controller`. Record the current `name`, `rrule`, `model`, `reasoningEffort`, `executionEnvironment`, `projectId`, and `destination`; do not infer them from an older plan.

- [ ] **Step 2: Build the replacement prompt with these ordered sections**

The full prompt must preserve identity, lease, title, decision email, verification, and result-reporting behavior, while replacing routing and workspace sections with:

```text
工作区基线：获取租约后调用 automation-workspace-guard.ps1 Snapshot。人工脏改不是全局退出条件；候选必须先推导完整 expectedPaths，再调用 Check。冲突候选跳过并继续选择；全部冲突才安静退出。禁止 stash、reset、checkout 或 clean。

可执行库存：只统计待处理、依赖完成、DeepSeek 前置已复审、主责映射到配置执行器、无待决策、满足冻结/闸门、expectedPaths 完整且与基线不冲突的任务。低水位 2，高水位 5。存在安全执行候选时不得让补位抢占本轮；任务收尾时才补位。无安全候选或队列事实错误时才独立维护。用 RecordQueueState 保存指纹、可执行数量和无候选抑制。

执行器：Codex 候选直接执行。DeepSeek 候选只有选中后才检查 Claude CLI 与代理身份；读取 DeepSeek工作提示词.txt 和 AI协作规则.txt。预检失败且未修改项目时调用 RecordWorkerFailure 并继续 Codex 候选。DeepSeek 只修改 expectedPaths、标未审核并写交接，不 stage、不提交；控制器负责验证和提交。成功预检调用 ClearWorkerFailure。

提交隔离：验证完成后只对 expectedPaths 执行 git add，并用 git commit --only -m "$commitMessage" -- @expectedPaths 提交。提交前后调用 workspace guard Verify，确认原 staged、unstaged、untracked、rename、delete 基线完全不变。

控制器自有状态：创建待决策或有效阻塞摘要前登记状态路径和检查点；写入后验证并提交。中断则保留恢复指针。不得 Complete 为 IDLE 后留下无恢复指针的控制器自有脏文件。
```

The prompt must not contain a concrete task, handoff, decision, email address, historical thread ID, or user-local secret.

- [ ] **Step 3: Update the existing automation while keeping it paused**

Call the automation update tool with the same automation ID and full preserved fields, the replacement prompt, and `status=PAUSED`. Do not create a second controller.

- [ ] **Step 4: Run all paused-controller checks**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,docs/superpowers,tools
git diff --check
```

Expected: both unit suites report `OK`; the workflow checker reports `OK` with zero active writers; text and Git checks exit 0.

- [ ] **Step 5: Commit project-side v2 rules and checks**

Run the required whitespace precheck over exactly the modified project paths, stage only those paths, run `git diff --cached --check`, inspect the staged name list, then commit:

```powershell
git commit -m "feat(automation): route runnable work across serial workers"
```

The user-level automation TOML is intentionally not part of the Git commit.

## Task 9: Perform a read-only policy dry run

**Files:**
- Read only: current queue, review entry, backlogs, local controller state, Git status
- No project modification

- [ ] **Step 1: Compute current runnable supply without selecting a mutation**

Apply the new rules to every current queue row and record, in the task output only: status, dependency result, pending-decision exclusion, configured executor, expected-path confidence, dirty-path overlap, and final runnable result.

- [ ] **Step 2: Verify expected behavior against the approved examples**

The dry run must demonstrate:

```text
total=4 and runnable=4 -> execute, no independent maintenance
total=10 and runnable=0 -> maintenance or unchanged-fingerprint quiet exit
DeepSeek unavailable/backoff -> DeepSeek rows do not satisfy the 2→5 supply target
one dirty-path conflict -> skip only that candidate
pending decision -> exclude the task and every transitive dependent, not unrelated work
```

- [ ] **Step 3: Leave the repository and local lease unchanged**

Run `git status --short` and `automation-controller-state.ps1 Show` before and after. Expected: identical Git state; controller remains `PAUSED` and `IDLE` with the same pending-decision content.

## Task 10: Activate the controller and verify static deployment

**External configuration:**
- Update: automation ID `tzg-hourly-controller`

- [ ] **Step 1: Re-read the automation before activation**

View the automation record again and verify that the prompt hash/content still matches the paused version tested in Task 8.

- [ ] **Step 2: Activate the same automation**

Use the automation update tool with the full existing fields and `status=ACTIVE`. Do not change the schedule or create a duplicate.

- [ ] **Step 3: Verify topology**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
```

Expected: `check-automation-workflow: OK`, exactly one active writer, WF1/WF3/WF4 paused, and the daily briefing active/read-only.

## Task 11: Observe canaries without overstating completion

**Evidence:**
- Read: controller `memory.md`, local state `Show`, Git log/status, task archive or handoff created by each run
- Modify after evidence: `开发管理/自动工作流状态.txt`

- [ ] **Step 1: Observe the first normal Codex canary**

Wait for a scheduled run with a legal Codex candidate. Verify one branch only, path-limited commit, expected validation, lease returned to IDLE, and no unrelated changes. Record the commit and evidence.

- [ ] **Step 2: Observe an unrelated-dirty-workspace canary**

Only with an explicitly controlled, harmless untracked sentinel outside the selected task paths, verify the controller executes a disjoint task, leaves the sentinel byte-identical and uncommitted, and removes no user state. Delete the sentinel only after comparing its hash; do not use stash or clean.

- [ ] **Step 3: Observe a real DeepSeek-to-Codex canary**

Wait for a genuine eligible DeepSeek task; do not fabricate a business task. Verify on-demand preflight, DeepSeek-only edit scope, unreviewed markers, handoff, controller-owned commit, next-round Codex review, and downstream dependency remaining blocked until review passes.

If no genuine DeepSeek candidate exists, report “DeepSeek canary pending” and do not claim full v2 acceptance. Codex-only operation may remain active if the prior checks and canaries are clean.

- [ ] **Step 4: Record only verified results**

Update `开发管理/自动工作流状态.txt` with the implementation commit and completed canaries. Do not record a pending canary as passed. Validate and commit the state update separately:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理/自动工作流状态.txt
& tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流状态.txt' -Fix
& tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流状态.txt'
git add -- 开发管理/自动工作流状态.txt
git diff --cached --check
git commit -m "docs(automation): record controller v2 canaries"
```

## Task 12: Final verification and handoff

**Files:**
- Verify all project and user-level artifacts touched above

- [ ] **Step 1: Run the complete regression set**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,docs/superpowers,tools
git diff --check
git status --short
```

Expected: all scripts exit 0 and the worktree is clean. If the DeepSeek canary is still pending, say so explicitly even when all static and Codex checks pass.

- [ ] **Step 2: Verify specification lifecycle and discoverability**

Run:

```powershell
rg -n "2026-07-13-hourly-automation-controller-v2-design|2026-07-13-hourly-automation-controller-v2-implementation" docs/superpowers 开发管理
```

Expected: v1 points to v2; v2 points to this plan; the authoritative workflow rules point to v2 where historical rationale is needed. No old specification is deleted.

- [ ] **Step 3: Report the exact completion level**

Report implementation commits, project checks, automation topology, each canary result, the current decision status, and any DeepSeek canary/backoff limitation. Do not equate “configuration updated” with “three canaries passed.”
