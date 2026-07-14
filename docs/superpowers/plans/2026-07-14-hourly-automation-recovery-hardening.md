# Hourly Automation Recovery Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace fragile generated commit commands with a fixed helper, add fingerprint-backed cross-run recovery, and safely clear the current `AUTO-BLOCKED` state.

**Architecture:** A focused commit helper owns whitespace, index, and path-limited commit mechanics. The workspace guard owns immutable recovery evidence and exact recovery comparison, while the state tool stores evidence references and lifecycle state. The automation prompt orchestrates these fixed interfaces and remains fail-closed when evidence is absent or mismatched.

**Tech Stack:** PowerShell 7, Git porcelain/plumbing commands, JSON state files, Codex App automation update interface.

---

## File map

- Create `tools/automation-finalize-commit.ps1`: fixed path-limited commit transaction.
- Create `tools/test-automation-finalize-commit.ps1`: temporary-repository tests for Unicode paths and unrelated Git state.
- Modify `tools/automation-workspace-guard.ps1` and its test: capture and check recovery evidence.
- Modify `tools/automation-controller-state.ps1` and its test: schema v4 evidence lifecycle.
- Modify `tools/check-automation-workflow.ps1` and `开发管理/自动工作流规则.txt`: enforce the new contract.
- Update the existing controller through the Codex App automation interface; never edit `automation.toml` directly.
- Reconcile only `开发管理/自动工作流状态.txt`, then reset the user-level blocked state.

### Task 1: Fixed path-limited commit helper

**Files:**
- Create: `tools/automation-finalize-commit.ps1`
- Create: `tools/test-automation-finalize-commit.ps1`

- [ ] **Step 1: Write the failing temporary-repository test**

Create a fixture with these paths:

```powershell
$expected = '目录/决策 状态.txt'
$unrelatedStaged = 'manual-staged.txt'
$unrelatedDirty = 'manual-dirty.txt'
$unrelatedUntracked = 'manual-untracked.txt'
```

Record the unrelated staged blob and dirty/untracked hashes, modify `$expected`, invoke the helper, and assert:

```powershell
if ($result.Code -ne 0) { throw "commit helper failed: $($result.Output)" }
if (@(git -C $repo show --pretty='' --name-only HEAD) -notcontains $expected) { throw 'expected Unicode path was not committed' }
if ((git -C $repo diff --cached --raw -- $unrelatedStaged) -notmatch $stagedBlobBefore) { throw 'unrelated staged blob changed' }
if ((Get-FileHash $dirtyPath).Hash -ne $dirtyHashBefore) { throw 'unrelated dirty file changed' }
if ((Get-FileHash $untrackedPath).Hash -ne $untrackedHashBefore) { throw 'unrelated untracked file changed' }
```

Add a missing-expected-path case that requires a nonzero exit and no new commit.

- [ ] **Step 2: Run the new test and confirm failure**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
```

Expected: FAIL because the helper does not exist.

- [ ] **Step 3: Implement the fixed helper**

Use this public interface:

```powershell
[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Get-Location).Path,
  [Parameter(Mandatory = $true)][string]$ExpectedPaths,
  [Parameter(Mandatory = $true)][string]$CommitMessage
)
```

Normalize the pipe-delimited paths without trimming meaningful filename whitespace; reject absolute paths and empty, `.` or `..` segments. Record index entries before mutation, then use argument arrays:

```powershell
$beforeIndex = @(git -C $RepositoryRoot ls-files --stage -z)
& (Join-Path $RepositoryRoot 'tools/check-pending-whitespace.ps1') -ExpectedPaths ($paths -join '|') -Fix
& (Join-Path $RepositoryRoot 'tools/check-pending-whitespace.ps1') -ExpectedPaths ($paths -join '|')
& git -C $RepositoryRoot add -- @paths
& git -C $RepositoryRoot diff --cached --check
& git -C $RepositoryRoot commit --only -m $CommitMessage -- @paths
& git -C $RepositoryRoot rev-parse HEAD
```

After `git add`, require every expected path to have a staged change and require all path-external index entries to match `$beforeIndex`. Check `$LASTEXITCODE` after each native Git call. Do not mutate controller state or push.

- [ ] **Step 4: Run only the helper test**

Run Step 2 again. Expected: `test-automation-finalize-commit: OK`.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- tools/automation-finalize-commit.ps1 tools/test-automation-finalize-commit.ps1
git diff --cached --check
git commit --only -m "fix(automation): add fixed commit helper" -- tools/automation-finalize-commit.ps1 tools/test-automation-finalize-commit.ps1
```

### Task 2: Fingerprint-backed workspace recovery

**Files:**
- Modify: `tools/automation-workspace-guard.ps1`
- Modify: `tools/test-automation-workspace-guard.ps1`

- [ ] **Step 1: Add four failing recovery cases**

After changing `task.txt`, capture evidence and check the unchanged residue:

```powershell
$evidence = Join-Path $sandbox 'recovery-evidence.json'
$r = Invoke-Guard @('CaptureRecoveryEvidence', '-RepositoryRoot', $repo, '-BaselinePath', $cleanBaseline, '-EvidencePath', $evidence, '-ExpectedPaths', 'task.txt')
Assert-Code $r 0 'capture recovery evidence'
$r = Invoke-Guard @('CheckRecovery', '-RepositoryRoot', $repo, '-BaselinePath', $cleanBaseline, '-EvidencePath', $evidence, '-ExpectedPaths', 'task.txt')
Assert-Code $r 0 'exact controller residue recovers'
```

Then independently assert: changing `task.txt` after capture returns exit 22 and `recovery_expected_changed`; changing `human.txt` returns exit 21 and `baseline_changed`; tampering evidence returns `recovery_evidence_invalid`.

- [ ] **Step 2: Run only the guard test and confirm failure**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
```

Expected: FAIL because the two actions are absent.

- [ ] **Step 3: Implement evidence capture and checking**

Extend the interface:

```powershell
[ValidateSet('Snapshot', 'Check', 'Verify', 'CaptureRecoveryEvidence', 'CheckRecovery')]
[string]$Action,
[string]$EvidencePath
```

Add exit code 22 for expected-path mismatch. Write this evidence atomically:

```powershell
[ordered]@{
  schemaVersion = 1
  repositoryRoot = $repository
  baselinePayloadHash = [string]$baseline.payloadHash
  head = [string]$fresh.head
  expectedPaths = @($expected)
  expectedEntries = @(Sort-WorkspaceEntries $expectedEntries)
  payloadHash = $null
}
```

`CaptureRecoveryEvidence` validates the original baseline, rejects changes outside `expectedPaths`, records all current entries overlapping expected paths, computes a canonical hash, and writes atomically. `CheckRecovery` validates repository, hashes, HEAD and expected path identity; rejects outside changes via `Get-ChangedWorkspacePaths $baseline $fresh $expected`; and compares the complete current expected-entry fingerprint with evidence. It returns structured reasons and never rewrites evidence.

- [ ] **Step 4: Run only the guard test**

Run Step 2 again. Expected: `test-automation-workspace-guard: OK`.

- [ ] **Step 5: Commit Task 2**

```powershell
git add -- tools/automation-workspace-guard.ps1 tools/test-automation-workspace-guard.ps1
git diff --cached --check
git commit --only -m "fix(automation): prove recovery path ownership" -- tools/automation-workspace-guard.ps1 tools/test-automation-workspace-guard.ps1
```

### Task 3: Controller state evidence lifecycle

**Files:**
- Modify: `tools/automation-controller-state.ps1`
- Modify: `tools/test-automation-controller-state.ps1`

- [ ] **Step 1: Add failing schema and lifecycle assertions**

Use a verification checkpoint containing:

```powershell
'-RecoveryBaselinePath', 'C:\state\baseline.json',
'-RecoveryEvidencePath', 'C:\state\evidence.json',
'-RecoveryEvidenceHash', ('a' * 64)
```

Assert schema v4 and exact persistence. `Fail` must preserve all values. Separate `Complete` and `ResetBlocked` fixtures must clear them. Schema v1-v3 fixtures must migrate missing fields to null.

- [ ] **Step 2: Run only the state test and confirm failure**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: FAIL because schema v4 and evidence parameters are absent.

- [ ] **Step 3: Implement schema v4 and lifecycle rules**

Add parameters:

```powershell
[string]$RecoveryBaselinePath,
[string]$RecoveryEvidencePath,
[ValidatePattern('^[0-9a-f]{64}$')][string]$RecoveryEvidenceHash
```

Add null-initialized state fields `recoveryBaselinePath`, `recoveryEvidencePath`, and `recoveryEvidenceHash`. Accept schema 1 through 4 and always export 4. `Checkpoint` changes a field only when its parameter is present; `Fail` preserves fields; `Complete`, fresh `IDLE` acquisition, and `ResetBlocked` clear them together.

- [ ] **Step 4: Run only the state test**

Run Step 2 again. Expected: `test-automation-controller-state: OK`.

- [ ] **Step 5: Commit Task 3**

```powershell
git add -- tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
git diff --cached --check
git commit --only -m "fix(automation): persist recovery evidence" -- tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
```

### Task 4: Rules, prompt, and static contract

**Files:**
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `tools/check-automation-workflow.ps1`
- Update externally: existing `tzg-hourly-controller` record

- [ ] **Step 1: Add failing static requirements**

Require both rules and prompt to contain:

```powershell
Require-Match $source.Path 'automation-finalize-commit\.ps1' "$($source.Label) does not use the fixed commit helper"
Require-Match $source.Path 'CaptureRecoveryEvidence' "$($source.Label) does not capture recovery evidence"
Require-Match $source.Path 'CheckRecovery' "$($source.Label) does not use the recovery guard"
```

Reject a recovery instruction that uses ordinary candidate `Check` before ownership evidence is evaluated.

- [ ] **Step 2: Run the static check once and confirm failure**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
```

Expected: FAIL with the three new contract findings.

- [ ] **Step 3: Update project rules**

Replace generated commit mechanics with:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -ExpectedPaths '<pipe-delimited paths>' -CommitMessage '<message>'
```

Require `CaptureRecoveryEvidence` before the `verification_completed` checkpoint and `CheckRecovery` for recovery tasks. Ordinary `Check` remains only for new candidates.

- [ ] **Step 4: Update the existing automation through the app interface**

Preserve id, name, schedule, model, reasoning, environment, project, and active status. Replace only the recovery and commit paragraphs. Do not edit TOML or create a duplicate.

- [ ] **Step 5: Run the static check once after both sources change**

Run Step 2 again. Expected: `check-automation-workflow: OK`.

- [ ] **Step 6: Commit the project-side contract**

```powershell
git add -- tools/check-automation-workflow.ps1 '开发管理/自动工作流规则.txt'
git diff --cached --check
git commit --only -m "fix(automation): route commits and recovery through fixed tools" -- tools/check-automation-workflow.ps1 '开发管理/自动工作流规则.txt'
```

### Task 5: Reconcile the interrupted result and clear the block

**Files:**
- Commit existing intended change only: `开发管理/自动工作流状态.txt`
- Preserve untouched: `docs/superpowers/plans/2026-07-14-daily-automation-briefing-content-plan.md`
- Update user-level state through: `tools/automation-controller-state.ps1`

- [ ] **Step 1: Confirm the diff still matches the resolved decision**

Require `pendingDecision.status = RESOLVED`, option `A`, removal of the pending section, and addition of only the matching decision result. Stop if any fact differs.

- [ ] **Step 2: Commit only the status path with the new helper**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 `
  -ExpectedPaths '开发管理/自动工作流状态.txt' `
  -CommitMessage 'chore(automation): record realm decision'
```

Expected: one status-only commit; the daily-briefing plan remains untracked.

- [ ] **Step 3: Reset the blocked controller once**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 ResetBlocked `
  -ErrorMessage 'fixed commit helper and fingerprint-backed recovery; reconciled interrupted decision record'
```

Expected: state `IDLE`, null task/checkpoint/paths/evidence, recovery count 0. The resolved pending decision remains for the next normal round.

- [ ] **Step 4: Perform one final read-only state check**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show
git status --short
```

Expected: controller `IDLE`; only the pre-existing untracked daily-briefing plan remains. Do not trigger a business run or repeat unchanged component tests.

- [ ] **Step 5: Report completion**

Report implementation commits, the status reconciliation commit, four targeted checks, final controller state, and the untouched untracked plan. Do not push unless requested.
