# Automation and Manual Worktree Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 隔离自动化运行期间的手动写入，并保证固定调用器在提交形状异常时保存现场或释放租约。

**Architecture:** 自动化继续在主工作区运行，手动写入使用现有 Git worktree。固定调用器复用现有 `SaveInterruption`、`RecordResult` 和 `Release`，只调整异常分支顺序并补齐 `blocked` 收尾。

**Tech Stack:** PowerShell 7、Git、现有 hourly automation lease runtime。

---

## 文件范围

- Modify: `tools/test-invoke-codex-responsibility.ps1` — 增加并发提交回归场景。
- Modify: `tools/invoke-codex-responsibility.ps1` — 调整异常分支顺序并补齐租约收尾。
- Modify: `AGENTS.md` — 增加一条手动写入 worktree 规则。
- No change: workspace guard、lease schema、finalizer、自动化配置。

### Task 1: 用回归测试锁定两种提交形状异常

**Files:**

- Modify: `tools/test-invoke-codex-responsibility.ps1:198-227`
- Modify: `tools/test-invoke-codex-responsibility.ps1:263-305`
- Test: `tools/test-invoke-codex-responsibility.ps1`

- [ ] **Step 1: 在 fake Codex switch 中加入两个场景**

在 `child-failed-with-change` 后加入：

```powershell
  'unverified-commit-with-change' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'manual-with-residue.txt'), 'manual commit', [Text.UTF8Encoding]::new($false))
    & git add manual-with-residue.txt
    & git commit -q -m 'test: unrelated manual commit with residue'
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'orphan-after-commit.txt'), 'preserve me', [Text.UTF8Encoding]::new($false))
    $global:LASTEXITCODE = 9
    exit 9
  }
  'unverified-commit-only' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'manual-only.txt'), 'manual commit', [Text.UTF8Encoding]::new($false))
    & git add manual-only.txt
    & git commit -q -m 'test: unrelated manual commit only'
  }
```

- [ ] **Step 2: 加入“无关提交 + 任务残留”断言**

放在现有 interruption recovery 清理完成之后：

```powershell
  Reset-GitFixture
  $commitWithResidueRun = Acquire-TestLease -TaskId 'task-commit-with-residue'
  $commitWithResidue = Invoke-Responsibility -Case 'unverified-commit-with-change' -TaskId 'task-commit-with-residue' -RunId $commitWithResidueRun
  Assert-True -Condition ($commitWithResidue.ExitCode -ne 0) -Message 'Commit-with-residue invocation unexpectedly succeeded'
  Assert-Equal -Actual $commitWithResidue.Json.status -Expected 'interrupted' -Message 'Commit-with-residue status mismatch'
  Assert-True -Condition (Test-Path -LiteralPath (Join-Path $gitRoot 'orphan-after-commit.txt')) -Message 'Commit-with-residue invocation removed task residue'
  $commitWithResidueState = Assert-LeaseReleased
  Assert-Equal -Actual $commitWithResidueState.state.recovery.trigger -Expected 'interruption' -Message 'Commit-with-residue recovery trigger mismatch'
  Assert-Equal -Actual $commitWithResidueState.state.recovery.resumeId -Expected $sessionId -Message 'Commit-with-residue recovery lost session id'
  Assert-True -Condition ('orphan-after-commit.txt' -in @($commitWithResidueState.state.recovery.changedPaths)) -Message 'Commit-with-residue recovery lost changed path'

  $commitWithResidueRecoveryLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-commit-with-residue'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $commitWithResidueRecoveryLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Commit-with-residue recovery could not be reacquired'
  Invoke-LeaseJson -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $commitWithResidueRecoveryLease.runId } | Out-Null
  Invoke-LeaseJson -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $commitWithResidueRecoveryLease.runId } | Out-Null
```

- [ ] **Step 3: 加入“只有无关提交”断言**

紧接上一个场景加入：

```powershell
  Reset-GitFixture
  $commitOnlyRun = Acquire-TestLease -TaskId 'task-commit-only'
  $commitOnly = Invoke-Responsibility -Case 'unverified-commit-only' -TaskId 'task-commit-only' -RunId $commitOnlyRun
  Assert-True -Condition ($commitOnly.ExitCode -ne 0) -Message 'Commit-only invocation unexpectedly succeeded'
  Assert-Equal -Actual $commitOnly.Json.status -Expected 'blocked' -Message 'Commit-only status mismatch'
  Assert-Equal -Actual $commitOnly.Json.detailCode -Expected 'unverified_commit_shape' -Message 'Commit-only detail code mismatch'
  $commitOnlyState = Assert-LeaseReleased
  Assert-Equal -Actual $commitOnlyState.state.lastResult.category -Expected 'blocked' -Message 'Commit-only result category mismatch'
  Assert-Equal -Actual $commitOnlyState.state.lastResult.detailCode -Expected 'unverified_commit_shape' -Message 'Commit-only recorded detail mismatch'
  Assert-True -Condition ($null -eq $commitOnlyState.state.recovery) -Message 'Commit-only invocation invented recovery'
```

- [ ] **Step 4: 运行测试并确认先失败**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
```

Expected: FAIL at `Commit-with-residue status mismatch`; current implementation returns `blocked` before inspecting the uncommitted path。

### Task 2: 最小修改固定调用器并通过全部测试

**Files:**

- Modify: `tools/invoke-codex-responsibility.ps1:232-249`
- Modify: `tools/invoke-codex-responsibility.ps1:351-410`
- Test: `tools/test-invoke-codex-responsibility.ps1`

- [ ] **Step 1: 让 `Close-Run` 只在 blocked 时传现有 fingerprint 参数**

替换 `Close-Run` 的参数构造部分：

```powershell
function Close-Run {
  param(
    [string]$Category,
    [string]$DetailCode,
    [string]$BlockingFingerprint
  )

  $recordParameters = @{
    StateRoot = $StateRoot
    RunId = $RunId
    Category = $Category
    TaskId = $TaskId
    DetailCode = $DetailCode
  }
  if ($Category -ceq 'blocked') {
    $recordParameters.BlockingFingerprint = $BlockingFingerprint
  }
  $recorded = Invoke-LeaseAction -LeaseAction RecordResult -Parameters $recordParameters
  if ([string]$recorded.status -cne 'RECORDED') {
    throw "RecordResult returned $($recorded.status)"
  }
  $released = Invoke-LeaseAction -LeaseAction Release -Parameters @{ StateRoot = $StateRoot; RunId = $RunId }
  if ([string]$released.status -cne 'RELEASED') {
    throw "Release returned $($released.status)"
  }
}
```

- [ ] **Step 2: 把新增未提交路径分支移到无法归属的新提交之前**

保留 decision 和成功分支不变，把后续分支改为：

```powershell
  } elseif ($newChangedPaths.Count -gt 0) {
    if ($null -eq $capturedSessionId) {
      $result = [ordered]@{
        status = 'blocked'; category = 'blocked'; taskId = $TaskId; runId = $RunId
        sessionId = $null; commitSha = $null; detailCode = 'changed_without_session'
      }
      $resultExitCode = 2
    } else {
      $saved = Invoke-LeaseAction -LeaseAction SaveInterruption -Parameters @{
        StateRoot = $StateRoot
        RunId = $RunId
        CodexThreadId = $capturedSessionId
        HasUncommittedChanges = $true
        ChangedPaths = $newChangedPaths
      }
      if ([string]$saved.status -cne 'RECOVERY_SAVED') {
        throw "SaveInterruption returned $($saved.status)"
      }
      Close-Run -Category 'failed' -DetailCode 'interruption_recovery_saved'
      $result = [ordered]@{
        status = 'interrupted'; category = 'failed'; taskId = $TaskId; runId = $RunId
        sessionId = $capturedSessionId; commitSha = $null
      }
      $resultExitCode = 1
    }
  } elseif ($newCommits.Count -gt 0) {
    Close-Run `
      -Category 'blocked' `
      -DetailCode 'unverified_commit_shape' `
      -BlockingFingerprint "unverified_commit_shape:$TaskId"
    $result = [ordered]@{
      status = 'blocked'; category = 'blocked'; taskId = $TaskId; runId = $RunId
      sessionId = $capturedSessionId; commitSha = $null; detailCode = 'unverified_commit_shape'
    }
    $resultExitCode = 2
```

不要改动 `changed_without_session` 的保留租约行为，也不要修改 catch 分支。

- [ ] **Step 3: 运行全部调用器测试**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
```

Expected: `test-invoke-codex-responsibility: OK`

- [ ] **Step 4: 检查并提交代码与测试**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -Paths tools/invoke-codex-responsibility.ps1,tools/test-invoke-codex-responsibility.ps1
git add -- tools/invoke-codex-responsibility.ps1 tools/test-invoke-codex-responsibility.ps1
git diff --cached --check
git commit -m "fix: close automation runs after concurrent commits"
```

Expected: whitespace 和 staged diff 检查均通过，只提交上述两个文件。

### Task 3: 记录手动写入隔离规则

**Files:**

- Modify: `AGENTS.md:64-72`
- Test: `tools/check-review-text.ps1`

- [ ] **Step 1: 在“修改与验证”中增加一条规则**

在 PowerShell 7 规则之后加入：

```markdown
- 手动 Codex 对话准备写入项目文件且自动化 `Show` 返回非空 lease 时，使用 `.worktrees/` 隔离工作；只在 `lease=null` 且待合并路径不冲突时合并回主工作区。只读对话不需要 worktree。
```

- [ ] **Step 2: 运行直接检查**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -Paths AGENTS.md
```

Expected: 两个检查都返回 OK。

- [ ] **Step 3: 暂存、检查并提交**

Run:

```powershell
git add -- AGENTS.md
git diff --cached --check
git commit -m "docs: isolate manual writes during automation"
```

Expected: 只提交 `AGENTS.md`。

### Task 4: 恢复 N-AI-01A 现场并合并修复

**Files:**

- Runtime only: `%USERPROFILE%\.codex\automation-state\tzg-hourly-controller-runtime`
- Existing task paths remain owned by session `019f8e45-8de8-7522-a455-cda2d21e4aa4`

- [ ] **Step 1: 在主工作区核对现场仍精确匹配**

Run:

```powershell
$mainRoot = 'D:\天章游戏开发'
$worktreeRoot = 'D:\天章游戏开发\.worktrees\automation-manual-worktree-fix'
$leaseTool = Join-Path $mainRoot 'tools\hourly-automation-lease.ps1'
$stateRoot = Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'
$oldRunId = 'bb10b771-1717-4f18-9cbc-7a99f643c176'
$sessionId = '019f8e45-8de8-7522-a455-cda2d21e4aa4'
$changedPaths = @(
  '开发管理/任务列表/数值与战斗任务.txt'
  '开发管理/当前任务队列.txt'
  '开发管理/战棋AI与Boss模板现行所有者及决策输入.txt'
)
$shown = pwsh -NoProfile -ExecutionPolicy Bypass -File $leaseTool -Action Show -StateRoot $stateRoot | ConvertFrom-Json -Depth 100
if ($shown.state.lease.runId -cne $oldRunId -or $null -ne $shown.state.recovery) {
  throw 'N-AI-01A runtime no longer matches the approved recovery input'
}
$actualPaths = @(git -C $mainRoot -c core.quotepath=false status --porcelain=v1 | ForEach-Object { $_.Substring(3).Replace('\', '/') } | Sort-Object)
if (Compare-Object ($changedPaths | Sort-Object) $actualPaths) {
  throw 'Main workspace paths no longer match the approved recovery input'
}
```

Expected: 无输出、无异常；不修改任何业务文件。

- [ ] **Step 2: 使用现有接口保存 interruption 并关闭旧 run**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File $leaseTool `
  -Action SaveInterruption -StateRoot $stateRoot -RunId $oldRunId `
  -CodexThreadId $sessionId -HasUncommittedChanges `
  -ChangedPaths ($changedPaths -join '|')
pwsh -NoProfile -ExecutionPolicy Bypass -File $leaseTool `
  -Action RecordResult -StateRoot $stateRoot -RunId $oldRunId `
  -TaskId 'N-AI-01A' -Category failed -DetailCode interruption_recovery_saved
pwsh -NoProfile -ExecutionPolicy Bypass -File $leaseTool `
  -Action Release -StateRoot $stateRoot -RunId $oldRunId
```

Expected: 依次返回 `RECOVERY_SAVED`、`RECORDED`、`RELEASED`。

- [ ] **Step 3: 立即由修复后的固定调用器恢复原 session**

Run:

```powershell
$acquired = pwsh -NoProfile -ExecutionPolicy Bypass -File $leaseTool `
  -Action Acquire -StateRoot $stateRoot -TaskId 'N-AI-01A' `
  -Owner 'Codex / gpt-5.6-terra' -RepositoryRoot $mainRoot `
  -ResumeRecovery | ConvertFrom-Json -Depth 100
if ($acquired.status -cne 'RECOVERY_ACQUIRED') {
  throw "Recovery acquire failed: $($acquired.status)"
}
$resumeOutput = & pwsh -NoProfile -ExecutionPolicy Bypass `
  -File (Join-Path $worktreeRoot 'tools\invoke-codex-responsibility.ps1') `
  -Action Resume -Route Recovery -RepositoryRoot $mainRoot `
  -TaskId 'N-AI-01A' -RunId $acquired.runId -StateRoot $stateRoot `
  -SessionId $sessionId
$resumeExit = $LASTEXITCODE
$resumeResult = @($resumeOutput)[-1] | ConvertFrom-Json -Depth 100
if ($resumeExit -ne 0 -or $resumeResult.status -cne 'completed') {
  throw "N-AI-01A did not complete: exit=$resumeExit status=$($resumeResult.status)"
}
```

Expected: 原 session 返回 `completed`，由固定调用器释放新租约并清除 recovery。若返回等待决定或新的 interruption，停止，不合并分支，保留既有 recovery 并报告实际状态。

- [ ] **Step 4: 核对自动化为空闲且主工作区干净**

Run:

```powershell
$finalState = pwsh -NoProfile -ExecutionPolicy Bypass -File $leaseTool -Action Show -StateRoot $stateRoot | ConvertFrom-Json -Depth 100
if ($null -ne $finalState.state.lease -or $null -ne $finalState.state.recovery) {
  throw 'Automation is not idle after N-AI-01A recovery'
}
if (@(git -C $mainRoot status --short).Count -ne 0) {
  throw 'Main workspace is not clean after N-AI-01A recovery'
}
```

Expected: 无输出、无异常。

- [ ] **Step 5: 合并隔离分支并核对结果**

Run:

```powershell
git -C $mainRoot merge --no-ff codex/automation-manual-worktree-fix -m "merge: isolate manual development from automation"
git -C $mainRoot status --short
git -C $mainRoot log --graph --oneline --decorate -8
```

Expected: merge 成功，`git status --short` 无输出；提交图同时包含 N-AI-01A 业务提交、修复分支和 merge 提交。

## 完成条件

- 两个新增回归场景与全部既有调用器场景通过。
- `AGENTS.md` 只增加一条隔离规则。
- 当前 N-AI-01A 原 session 完成，runtime 中 `lease=null`、`recovery=null`。
- 修复分支合并回主分支，主工作区干净。
- 未修改 workspace guard、lease schema、finalizer 或自动化配置。
