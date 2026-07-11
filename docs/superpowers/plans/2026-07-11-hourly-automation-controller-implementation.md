# Hourly Automation Controller Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the four competing three-hour write automations with one fail-closed hourly Codex controller, local lease/recovery state, dynamic routing, and a read-only daily briefing.

**Architecture:** Reuse the existing WF2 automation id as the sole writer and pause WF1/WF3/WF4. Keep stable workflow policy in a project rule file, current human-facing results in a small project status file, and runtime lease/checkpoint data in an untracked local JSON file managed by a zero-dependency PowerShell tool.

**Tech Stack:** Codex desktop cron automations, PowerShell 5.1-compatible scripts, Git, Markdown/TXT project management files.

---

## File map

- Create `tools/automation-controller-state.ps1`: atomic local lease, checkpoint, completion, failure and manual-reset operations.
- Create `tools/test-automation-controller-state.ps1`: isolated regression coverage for the state tool.
- Create `tools/check-automation-workflow.ps1`: project/config invariants and hardcoded-task detection.
- Create `开发管理/自动工作流规则.txt`: sole durable source for hourly routing, queue audit, recovery and failure policy.
- Replace `开发管理/自动工作流状态.txt`: human-facing result summary only; no Git-backed runtime lock.
- Modify `开发管理/状态与建议维护规则.txt`: add rule/status/runtime-state layering and conditional queue audit.
- Modify `开发管理/AI协作规则.txt`: route scheduled automation to the new rule file and retain paused WF3 authorization boundaries.
- Modify `AGENTS.md` and `CLAUDE.md`: add short-entry routing to the new workflow rule file.
- Update automation `tzg-wf2-codex-execute-1`: rename and reprompt as the hourly controller.
- Pause automations `tzg-wf1-queue-and-review-maintenance`, `tzg-wf3-claude-execute-1`, and `tzg-wf4-codex-execute-2`.
- Update automation `tzg-daily-automation-briefing`: read the new controller evidence and remain read-only.

## Task 1: Freeze the current writers and capture a deployment baseline

**Files:**
- External config: `%USERPROFILE%/.codex/automations/tzg-wf1-queue-and-review-maintenance/automation.toml`
- External config: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml`
- External config: `%USERPROFILE%/.codex/automations/tzg-wf3-claude-execute-1/automation.toml`
- External config: `%USERPROFILE%/.codex/automations/tzg-wf4-codex-execute-2/automation.toml`
- Read: `开发管理/自动工作流状态.txt`

- [ ] **Step 1: Verify the worktree and record the current automation commit baseline**

Run:

```powershell
git status --short --untracked-files=all
git log -5 --oneline --decorate
Get-ChildItem -Recurse -Filter automation.toml "$env:USERPROFILE\.codex\automations" |
  ForEach-Object {
    "### $($_.Directory.Name)"
    Select-String -LiteralPath $_.FullName -Pattern '^(id|name|status|rrule) = '
  }
```

Expected: Git is clean. Record the last pre-migration commit and the four writer statuses in the execution notes. If Git is dirty, stop without changing automation configuration.

- [ ] **Step 2: Pause all four write automations through the automation API**

For each id below, call `automation_update` in view mode, then update the same complete configuration with only `status` changed to `PAUSED`:

```text
tzg-wf1-queue-and-review-maintenance
tzg-wf2-codex-execute-1
tzg-wf3-claude-execute-1
tzg-wf4-codex-execute-2
```

Preserve name, prompt, schedule, model, reasoning effort, project id, execution environment and destination at this step. Do not pause `tzg-daily-automation-briefing` because it is read-only.

- [ ] **Step 3: Verify that no write automation remains active**

Run:

```powershell
$ids = @(
  'tzg-wf1-queue-and-review-maintenance',
  'tzg-wf2-codex-execute-1',
  'tzg-wf3-claude-execute-1',
  'tzg-wf4-codex-execute-2'
)
foreach ($id in $ids) {
  $path = Join-Path "$env:USERPROFILE\.codex\automations" "$id\automation.toml"
  if (-not (Select-String -Quiet -LiteralPath $path -Pattern '^status = "PAUSED"$')) {
    throw "$id is not paused"
  }
}
```

Expected: command exits 0. This task changes external configuration only, so there is no Git commit.

## Task 2: Build the local lease and recovery state tool with tests

**Files:**
- Create: `tools/test-automation-controller-state.ps1`
- Create: `tools/automation-controller-state.ps1`

- [ ] **Step 1: Write the failing state-tool regression script**

Create `tools/test-automation-controller-state.ps1` with:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-controller-state.ps1'
$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ("tzg-state-test-" + [guid]::NewGuid().ToString('N'))
$statePath = Join-Path $sandbox 'state.json'
$engine = (Get-Process -Id $PID).Path

function Invoke-StateTool {
  param([string[]]$Arguments)
  $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $tool @Arguments 2>&1
  [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)
  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function Read-TestState {
  (Invoke-StateTool @('Show', '-StatePath', $statePath)).Output | ConvertFrom-Json
}

New-Item -ItemType Directory -Path $sandbox | Out-Null
try {
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-1', '-Now', '2026-07-11T00:00:00Z')
  Assert-Code $r 0 'first acquire'
  if ((Read-TestState).state -ne 'RUNNING') { throw 'first acquire did not set RUNNING' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-2', '-Now', '2026-07-11T00:10:00Z')
  Assert-Code $r 10 'active lease rejection'

  $r = Invoke-StateTool @('Renew', '-StatePath', $statePath, '-RunId', 'wrong-run', '-Now', '2026-07-11T00:20:00Z')
  Assert-Code $r 12 'owner mismatch'

  $r = Invoke-StateTool @('Checkpoint', '-StatePath', $statePath, '-RunId', 'run-1', '-TaskKind', 'execute', '-TaskId', 'sample-task', '-Checkpoint', 'mutation_started', '-ExpectedPaths', 'a.txt|b/c.txt', '-Now', '2026-07-11T00:20:00Z')
  Assert-Code $r 0 'checkpoint'
  $state = Read-TestState
  if ($state.taskId -ne 'sample-task' -or $state.expectedPaths.Count -ne 2) { throw 'checkpoint fields were not persisted' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-2', '-Now', '2026-07-11T04:00:00Z')
  Assert-Code $r 0 'expired lease takeover'
  $state = Read-TestState
  if ($state.runId -ne 'run-2' -or $state.taskId -ne 'sample-task') { throw 'takeover did not preserve recovery fields' }

  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-2', '-ErrorMessage', 'initial interruption', '-Now', '2026-07-11T04:01:00Z')
  Assert-Code $r 0 'initial interruption'
  if ((Read-TestState).recoveryCount -ne 0) { throw 'initial interruption consumed a recovery attempt' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-3', '-Now', '2026-07-11T04:02:00Z')
  Assert-Code $r 0 'first recovery acquire'
  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-3', '-WasRecovery', '-ErrorMessage', 'first recovery failed', '-Now', '2026-07-11T04:03:00Z')
  Assert-Code $r 0 'first recovery failure'
  if ((Read-TestState).recoveryCount -ne 1) { throw 'first recovery count was not 1' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-4', '-Now', '2026-07-11T04:04:00Z')
  Assert-Code $r 0 'second recovery acquire'
  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-4', '-WasRecovery', '-ErrorMessage', 'second recovery failed', '-Now', '2026-07-11T04:05:00Z')
  Assert-Code $r 0 'second recovery failure'
  if ((Read-TestState).state -ne 'AUTO-BLOCKED') { throw 'second recovery failure did not block' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-5', '-Now', '2026-07-11T05:00:00Z')
  Assert-Code $r 11 'blocked acquire rejection'

  $r = Invoke-StateTool @('ResetBlocked', '-StatePath', $statePath, '-ErrorMessage', 'manual test reset', '-Now', '2026-07-11T05:01:00Z')
  Assert-Code $r 0 'manual reset'
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-6', '-Now', '2026-07-11T05:02:00Z')
  Assert-Code $r 0 'acquire after reset'
  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-6', '-QueueAuditCompleted', '-Now', '2026-07-11T05:03:00Z')
  Assert-Code $r 0 'complete'
  $state = Read-TestState
  if ($state.state -ne 'IDLE' -or -not $state.lastQueueAuditAt) { throw 'complete did not clear the run or record the audit' }

  $original = '{broken json'
  [System.IO.File]::WriteAllText($statePath, $original)
  $r = Invoke-StateTool @('Show', '-StatePath', $statePath)
  Assert-Code $r 13 'corrupt json'
  if ([System.IO.File]::ReadAllText($statePath) -ne $original) { throw 'corrupt JSON was overwritten' }

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":99}')
  $r = Invoke-StateTool @('Show', '-StatePath', $statePath)
  Assert-Code $r 13 'unsupported schema'

  'test-automation-controller-state: OK'
} finally {
  Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 2: Run the test and verify the RED state**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
```

Expected: FAIL because `tools/automation-controller-state.ps1` does not exist.

- [ ] **Step 3: Implement the minimal state tool**

Create `tools/automation-controller-state.ps1` with these public parameters and exact behavior:

```powershell
[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Acquire','Renew','Checkpoint','Complete','Fail','Show','ResetBlocked')]
  [string]$Action,
  [string]$StatePath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json",
  [string]$ControllerId = 'tzg-hourly-controller',
  [string]$RunId,
  [ValidateSet('recovery','review','maintenance','execute')]
  [string]$TaskKind,
  [string]$TaskId,
  [ValidateSet('identity_checked','queues_loaded','task_selected','mutation_started','verification_completed','commit_completed')]
  [string]$Checkpoint,
  [string]$ExpectedPaths,
  [switch]$WasRecovery,
  [switch]$QueueAuditCompleted,
  [string]$ErrorMessage,
  [int]$LeaseMinutes = 180,
  [string]$Now
)

$ErrorActionPreference = 'Stop'
$script:ExitBusy = 10
$script:ExitBlocked = 11
$script:ExitOwnerMismatch = 12
$script:ExitInvalidState = 13
$script:ExitLockContention = 14
$script:ExitInvalidArguments = 15

function Get-NowValue {
  if ([string]::IsNullOrWhiteSpace($Now)) { return [DateTimeOffset]::UtcNow }
  [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture)
}

function New-State {
  [ordered]@{
    schemaVersion = 1
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
  }
}

function Import-State {
  if (-not (Test-Path -LiteralPath $StatePath)) { return (New-State) }
  $raw = [IO.File]::ReadAllText($StatePath)
  $parsed = $raw | ConvertFrom-Json
  if ($parsed.schemaVersion -ne 1) { throw "Unsupported schemaVersion: $($parsed.schemaVersion)" }
  $state = New-State
  foreach ($key in @($state.Keys)) {
    $property = $parsed.PSObject.Properties[$key]
    if ($null -ne $property) { $state[$key] = $property.Value }
  }
  $state
}

function Export-State {
  param([System.Collections.IDictionary]$State)
  $directory = Split-Path -Parent $StatePath
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $temporary = Join-Path $directory ('.state-' + [guid]::NewGuid().ToString('N') + '.tmp')
  $encoding = New-Object Text.UTF8Encoding($false)
  [IO.File]::WriteAllText($temporary, ([pscustomobject]$State | ConvertTo-Json -Depth 6), $encoding)
  if (Test-Path -LiteralPath $StatePath) {
    [IO.File]::Replace($temporary, $StatePath, $null)
  } else {
    [IO.File]::Move($temporary, $StatePath)
  }
}

function Require-RunId {
  if ([string]::IsNullOrWhiteSpace($RunId)) { Write-Error 'RunId is required'; exit $script:ExitInvalidArguments }
}

function Require-Owner {
  param([System.Collections.IDictionary]$State)
  Require-RunId
  if ($State.runId -ne $RunId) { Write-Error "RunId does not own the lease"; exit $script:ExitOwnerMismatch }
}

function Set-Lease {
  param([System.Collections.IDictionary]$State, [DateTimeOffset]$At)
  $State.leaseExpiresAt = $At.AddMinutes($LeaseMinutes).ToString('o')
}

$nowValue = Get-NowValue
$directory = Split-Path -Parent $StatePath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$guardPath = "$StatePath.guard"
$guard = $null
try {
  try {
    $guard = [IO.File]::Open($guardPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
  } catch [IO.IOException] {
    Write-Error 'State transaction lock is busy'
    exit $script:ExitLockContention
  }

  try { $state = Import-State } catch {
    Write-Error "Invalid state file: $($_.Exception.Message)"
    exit $script:ExitInvalidState
  }

  switch ($Action) {
    'Show' {
      [pscustomobject]$state | ConvertTo-Json -Depth 6
      exit 0
    }
    'Acquire' {
      Require-RunId
      if ($state.state -eq 'AUTO-BLOCKED') { Write-Error 'Controller is AUTO-BLOCKED'; exit $script:ExitBlocked }
      if ($state.state -eq 'RUNNING' -and $state.leaseExpiresAt) {
        $expires = [DateTimeOffset]::Parse($state.leaseExpiresAt)
        if ($expires -gt $nowValue) { Write-Error 'An active lease already exists'; exit $script:ExitBusy }
      }
      if ($state.state -eq 'IDLE') {
        $state.taskKind = $null; $state.taskId = $null; $state.checkpoint = $null
        $state.expectedPaths = @(); $state.recoveryCount = 0; $state.lastError = $null
      }
      $state.controllerId = $ControllerId; $state.runId = $RunId; $state.state = 'RUNNING'
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Renew' {
      Require-Owner $state
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Checkpoint' {
      Require-Owner $state
      if ($TaskKind) { $state.taskKind = $TaskKind }
      if ($TaskId) { $state.taskId = $TaskId }
      if ($Checkpoint) { $state.checkpoint = $Checkpoint }
      if ($PSBoundParameters.ContainsKey('ExpectedPaths')) {
        $paths = @($ExpectedPaths -split '\|' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $state.expectedPaths = @($paths | ForEach-Object { ([string]$_).Replace('\','/') } | Sort-Object -Unique)
      }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Complete' {
      Require-Owner $state
      if ($QueueAuditCompleted) { $state.lastQueueAuditAt = $nowValue.ToString('o') }
      $state.state = 'IDLE'; $state.runId = $null; $state.leaseExpiresAt = $null
      $state.taskKind = $null; $state.taskId = $null; $state.checkpoint = $null
      $state.expectedPaths = @(); $state.recoveryCount = 0; $state.lastError = $null
      Export-State $state
    }
    'Fail' {
      Require-Owner $state
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Write-Error 'ErrorMessage is required'; exit $script:ExitInvalidArguments }
      $state.lastError = $ErrorMessage
      if ($WasRecovery) { $state.recoveryCount = [int]$state.recoveryCount + 1 }
      if ([int]$state.recoveryCount -ge 2) {
        $state.state = 'AUTO-BLOCKED'; $state.leaseExpiresAt = $null
      } else {
        $state.state = 'RUNNING'; $state.leaseExpiresAt = $nowValue.ToString('o')
      }
      Export-State $state
    }
    'ResetBlocked' {
      if ($state.state -ne 'AUTO-BLOCKED') { Write-Error 'State is not AUTO-BLOCKED'; exit $script:ExitInvalidArguments }
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Write-Error 'A manual reset reason is required'; exit $script:ExitInvalidArguments }
      $state.state = 'IDLE'; $state.runId = $null; $state.leaseExpiresAt = $null
      $state.taskKind = $null; $state.taskId = $null; $state.checkpoint = $null
      $state.expectedPaths = @(); $state.recoveryCount = 0
      $state.lastError = "Manual reset: $ErrorMessage"
      Export-State $state
    }
  }
  [pscustomobject]$state | ConvertTo-Json -Depth 6
} finally {
  if ($null -ne $guard) { $guard.Dispose() }
}
```

- [ ] **Step 4: Run the regression and text checks**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths tools/automation-controller-state.ps1,tools/test-automation-controller-state.ps1
git diff --check
```

Expected: state regression prints `test-automation-controller-state: OK`; text check exits 0; Git diff check exits 0.

- [ ] **Step 5: Commit the tested state tool**

```powershell
git add tools/automation-controller-state.ps1 tools/test-automation-controller-state.ps1
git commit -m "feat: add automation controller lease state"
```

## Task 3: Add the durable workflow rules and migrate project status

**Files:**
- Create: `开发管理/自动工作流规则.txt`
- Create: `tools/check-automation-workflow.ps1`
- Modify: `开发管理/自动工作流状态.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Write the failing workflow-invariant checker**

Create `tools/check-automation-workflow.ps1` with:

```powershell
param([switch]$ExpectControllerActive)
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$findings = New-Object System.Collections.Generic.List[string]

function Require-Match([string]$Path, [string]$Pattern, [string]$Message) {
  if (-not (Select-String -Quiet -LiteralPath $Path -Pattern $Pattern)) { $findings.Add($Message) }
}
function Reject-Match([string]$Path, [string]$Pattern, [string]$Message) {
  if (Select-String -Quiet -LiteralPath $Path -Pattern $Pattern) { $findings.Add($Message) }
}

$rules = Join-Path $root '开发管理\自动工作流规则.txt'
$status = Join-Path $root '开发管理\自动工作流状态.txt'
if (-not (Test-Path -LiteralPath $rules)) { $findings.Add('missing workflow rules') }
if (Test-Path -LiteralPath $status) {
  Reject-Match $status '全局锁|锁持有者|WF1-QUEUE-MAINTENANCE|WF2-CODEX-ONE|WF3-CLAUDE-ONE|WF4-CODEX-TWO' 'project status still contains runtime lock or legacy workflow table'
}
foreach ($entry in @('AGENTS.md','CLAUDE.md','开发管理\状态与建议维护规则.txt','开发管理\AI协作规则.txt')) {
  Require-Match (Join-Path $root $entry) '自动工作流规则\.txt' "$entry does not route to workflow rules"
}

$automationRoot = Join-Path $env:USERPROFILE '.codex\automations'
$controller = Join-Path $automationRoot 'tzg-wf2-codex-execute-1\automation.toml'
$paused = @(
  'tzg-wf1-queue-and-review-maintenance',
  'tzg-wf3-claude-execute-1',
  'tzg-wf4-codex-execute-2'
)
Require-Match $controller '^name = "TZG Hourly Controller"$' 'controller has not been renamed'
Reject-Match $controller 'TQ-[0-9]+|HANDOFF-[0-9]+' 'controller prompt contains a hardcoded task id'
foreach ($id in $paused) {
  Require-Match (Join-Path $automationRoot "$id\automation.toml") '^status = "PAUSED"$' "$id is not paused"
}
$expectedStatus = if ($ExpectControllerActive) { 'ACTIVE' } else { 'PAUSED' }
Require-Match $controller "^status = `"$expectedStatus`"$" "controller status is not $expectedStatus"
$daily = Join-Path $automationRoot 'tzg-daily-automation-briefing\automation.toml'
Require-Match $daily '^status = "ACTIVE"$' 'daily briefing is not active'
Require-Match $daily '只读|read-only' 'daily briefing does not declare its read-only boundary'
$activeWriters = @(
  Get-ChildItem -Directory $automationRoot -Filter 'tzg-*' |
    Where-Object { $_.Name -ne 'tzg-daily-automation-briefing' } |
    Where-Object { Select-String -Quiet -LiteralPath (Join-Path $_.FullName 'automation.toml') -Pattern '^status = "ACTIVE"$' }
)
$expectedWriterCount = if ($ExpectControllerActive) { 1 } else { 0 }
if ($activeWriters.Count -ne $expectedWriterCount) {
  $findings.Add("expected $expectedWriterCount active writer(s), found $($activeWriters.Count)")
}

if ($findings.Count -gt 0) {
  'check-automation-workflow: FAILED'
  $findings | Sort-Object
  exit 1
}
'check-automation-workflow: OK'
```

- [ ] **Step 2: Run the checker and verify it fails before the migration**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: FAIL for the missing rule file, legacy status fields, missing routes, and old controller name.

- [ ] **Step 3: Create the single workflow rule source**

Create `开发管理/自动工作流规则.txt` with these sections and operational statements copied without weakening from the approved design spec:

```text
# 自动工作流规则（✅ 已审核）

> 2026-07-11 Codex：按已批准设计，将四条三小时写入工作流收束为一个每小时串行控制器。设计依据：docs/superpowers/specs/2026-07-11-hourly-automation-controller-design.md。

## 自动化拓扑
- `tzg-wf2-codex-execute-1` 原地改造为唯一写入型 `TZG Hourly Controller`，每小时第 15 分钟触发。
- WF1、WF3、WF4 暂停但保留历史配置与 memory；不得与控制器同时启用。
- 每日简报只读，不修改项目、不 stage、不提交。

## 每轮顺序
1. 通过 Node REPL 确认实际模型身份；失败则只读退出。
2. 用 `tools/automation-controller-state.ps1` 获取三小时本机租约。
3. 检查恢复状态和完整 Git 工作区；来源不明脏改只读退出。
4. 单轮只选择一个分支：恢复中断任务 → 最高风险待复审 → 必要队列维护 → 一个 Codex 待处理任务 → 安静退出。
5. 按检查点续期；验证通过才提交；结束时释放租约。

## 动态选题
- 禁止在自动化提示或本规则中硬编码具体 TQ、HANDOFF 或审核清单编号。
- 复审走 `审核入口.txt`；执行走纯 `1` 的 Codex 路由。
- 内容冻结、主责边界和 G1/G2/G3 依赖均为硬约束。
- 每轮最多处理一个复审对象、一次维护或一个最小任务切片。

## 队列维护触发
仅在队列少于 5 条、含完成未归档项、依赖状态错误、主责不匹配、冻结内容入队，或距上次成功校验超过 12 小时时维护。无项目内容变化时只更新本机校验时间和 memory，不提交时间戳。

## 锁与恢复
- 运行状态位于 `%USERPROFILE%\.codex\automation-state\tzg-hourly-controller.json`，不得写入 Git 锁。
- 租约三小时，在 task_selected、mutation_started、verification_completed、commit_completed 等检查点续期。
- 首次中断不计恢复次数，随后最多恢复两次；第二次恢复仍失败转 `AUTO-BLOCKED`。
- 只有 Git 变更路径全部属于已记录 expectedPaths 时才能恢复；额外路径一律阻塞。
- `ResetBlocked` 只能由人工在处理原因后明确调用。

## 提交与失败关闭
- 只 stage 本轮相关文件，不覆盖、回退或提交人工改动，不自动推送远端。
- 身份未知、状态损坏、主责/依赖不明、恢复路径不符或验证证据无效时失败关闭。
- 无任务、有效锁和人工脏工作区只进入自动化 memory，不改项目状态、不制造提交。
- `开发管理/自动工作流状态.txt` 只在产生有效结果、队列路由变化或 `AUTO-BLOCKED` 时更新。
```

- [ ] **Step 4: Replace the project status with a result-only snapshot**

Replace `开发管理/自动工作流状态.txt` with:

```text
# 自动工作流状态（✅ 已审核）

> 2026-07-11 Codex：运行锁、检查点和恢复次数已迁移到本机 JSON；本文件只保存项目负责人需要看到的有效结果与阻塞，不记录空转轮次。

## 使用规则
1. 调度、选题、锁与恢复规则见 `开发管理/自动工作流规则.txt`。
2. 本文件不替代任务队列、审核入口、未通过审核清单或 AI 合作沟通。
3. 无任务、有效锁、身份确认失败或人工脏工作区不更新本文件。

## 最近有效结果
| 字段 | 值 |
|------|----|
| 迁移前最后结果 | 已保留在旧自动化 memory 与 Git 历史，不复制为滚动状态 |
| 单控制器部署 | 配置迁移中；首次真实小时轮次尚未验收 |
| 最近队列校验 | 新控制器尚未完成首次队列校验 |

## 当前阻塞
当前无 `AUTO-BLOCKED` 项。

## 运行证据
- 逐轮轨迹：Codex 自动化 `memory.md`。
- 业务成果：Git 提交、任务归档和任务卡验证结果。
- 本机租约：`tools/automation-controller-state.ps1 Show`。
```

- [ ] **Step 5: Add short routing entries to management and agent files**

Apply these exact semantic edits:

```text
AGENTS.md / CLAUDE.md:
- Add a management-file table row for `开发管理/自动工作流规则.txt` describing it as the hourly controller's sole scheduling/lock/recovery source.
- Add a view rule: scheduled automation changes or diagnosis must read this rule first, then the result status and routed task/review source.

开发管理/状态与建议维护规则.txt:
- Add separate rows for 自动工作流规则.txt (stable policy) and 自动工作流状态.txt (result snapshot).
- State that queue audit runs only on the six conditions in the rule file; time-only audits do not create commits.

开发管理/AI协作规则.txt:
- Add a scheduled-controller section pointing to 自动工作流规则.txt.
- Preserve WF3 authorization text but mark its timer paused.
- Change WF3 clean skips to automation memory only; only AUTO-BLOCKED or a real handoff changes project management files.
```

Do not duplicate the full rule body into these four files.

- [ ] **Step 6: Run documentation checks**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,tools/check-automation-workflow.ps1
git diff --check
```

Expected: both commands exit 0. If the workflow-invariant checker is rerun here, it still fails only because the paused controller has not yet been renamed/reprompted.

- [ ] **Step 7: Commit project-side workflow policy and checks**

```powershell
git add AGENTS.md CLAUDE.md tools/check-automation-workflow.ps1 开发管理/自动工作流规则.txt 开发管理/自动工作流状态.txt 开发管理/状态与建议维护规则.txt 开发管理/AI协作规则.txt
git commit -m "docs: centralize hourly automation workflow rules"
```

## Task 4: Reconfigure the paused automations

**Files:**
- External config: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml`
- External config: `%USERPROFILE%/.codex/automations/tzg-daily-automation-briefing/automation.toml`

- [ ] **Step 1: Replace the paused WF2 configuration with the hourly controller**

Use `automation_update` with id `tzg-wf2-codex-execute-1`, preserving project id, local execution environment, model and high reasoning effort. Set:

```text
name: TZG Hourly Controller
status: PAUSED
schedule: every hour at minute 15 in Asia/Hong_Kong
```

Replace the prompt with:

```text
你是 D:\天章游戏开发 项目唯一拥有写权限的每小时自动工作流控制器（TZG Hourly Controller）。每轮最多恢复或完成一个工作单元。不得并行派发代理，不得自动推送远端。

入口与身份：
1. 先完整读取 AGENTS.md 和 开发管理/自动工作流规则.txt；再按规则读取状态、队列、审核或任务事实源。
2. 使用 Node REPL 读取 nodeRepl.requestMeta['x-codex-turn-metadata'].model。失败时准确报告并只读退出，不修改项目或本机控制器状态。

租约与工作区：
3. 生成唯一 runId，调用 tools/automation-controller-state.ps1 Acquire 获取租约。退出码 10 表示有效租约占用，安静退出；11 表示 AUTO-BLOCKED，报告人工入口后退出；其他非零均失败关闭。
4. 调用 Show 读取恢复指针，并运行 git status --short --untracked-files=all。若无恢复指针但工作区不干净，调用 Complete 释放本轮租约后只读退出。
5. 若有恢复指针，仅当所有 Git 变更路径都属于 expectedPaths 时恢复；存在额外路径时调用 Fail 记录准确原因。若本轮是恢复轮，必须带 WasRecovery。

动态路由：
6. 调用 Checkpoint 记录 queues_loaded。严格按以下优先级只选择一个分支：现有恢复任务；审核入口中最高风险真实待复审对象；满足规则条件的队列维护；当前队列中依赖满足、状态待处理、Codex 主责的最高优先级任务；无候选则 Complete 后安静退出。
7. 禁止在提示解释中发明或沿用固定任务编号。候选只能来自当前文件事实。

执行与验证：
8. 选定后用 Checkpoint 写 task_selected、taskKind、taskId，并以竖线分隔、项目相对路径传递完整 expectedPaths；修改前写 mutation_started。Windows 文件名不允许竖线，因此该分隔符不会与合法项目路径冲突。
9. 按任务卡和项目规则执行。数值结论运行 BattleSim；Unity/C# 运行对应 build/test；文档与管理文件运行文本检查；所有任务运行 git diff --check。
10. 验证失败时不得提交完成状态；调用 Fail 写明失败命令、退出码和最小人工入口。恢复轮带 WasRecovery。
11. 验证通过后写 verification_completed，只 stage 本轮相关文件并提交；不得 stage、覆盖或回退任何其他改动。提交后写 commit_completed，再调用 Complete。若本轮完成队列审计，Complete 带 QueueAuditCompleted。

结果：
12. 最终回复简洁记录实际模型、分支类型、对象、提交、验证和残留风险。无任务、锁占用和人工脏工作区只留在自动化 memory，不修改 开发管理/自动工作流状态.txt，不制造空提交。
```

- [ ] **Step 2: Update the daily briefing while keeping it active and read-only**

Use `automation_update` on `tzg-daily-automation-briefing`, preserving its daily 01:00 schedule, local project, model and medium reasoning effort. Keep `status: ACTIVE`. Replace the prompt with:

```text
你是 D:\天章游戏开发 项目的每日自动化简报。只读汇总上一自然日（Asia/Hong_Kong）内 TZG Hourly Controller 的可核验成果，不修改文件、不执行任务、不 stage、不提交。

先读取 AGENTS.md、开发管理/自动工作流规则.txt、开发管理/自动工作流状态.txt、开发管理/当前任务队列.txt；按需读取审核与交接入口。再读取控制器 automation.toml 与 memory.md、本机状态工具 Show 输出、上一自然日 git log 和当前 git status。

只把 memory、Git 提交、任务归档或项目状态能够共同支持的事项列为自动化成果。状态只保留摘要时要明确可确认范围；其他提交单列为“未能确认是否由自动化产生”。

输出 300–600 字中文 Markdown，包含日期和总体判断、控制器完成/跳过/中断/阻塞概览、提交短哈希与验证证据、当前风险，以及今天最值得关注的 1–3 个队列任务。没有成果时直接说明“未发现可确认的自动化推进”。
```

- [ ] **Step 3: Verify paused topology and prompt invariants**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: `check-automation-workflow: OK` with the controller still paused and WF1/WF3/WF4 paused.

This task changes external configuration only, so there is no Git commit.

## Task 5: Enable the sole writer and perform deployment checks

**Files:**
- External config: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/automation.toml`
- Read: all project workflow files and tools created above

- [ ] **Step 1: Run the complete pre-enable verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,tools
git diff --check
git status --short --untracked-files=all
```

Expected: all checks pass and Git is clean. If any check fails, keep the controller paused.

- [ ] **Step 2: Enable only the hourly controller**

View `tzg-wf2-codex-execute-1`, then call `automation_update` with its complete current fields and only `status` changed from `PAUSED` to `ACTIVE`.

- [ ] **Step 3: Verify the active topology**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
```

Expected: `check-automation-workflow: OK`. Exactly one write automation is active; WF1/WF3/WF4 remain paused; the daily briefing remains active and read-only.

- [ ] **Step 4: Record the external configuration evidence**

Run:

```powershell
Get-ChildItem -Recurse -Filter automation.toml "$env:USERPROFILE\.codex\automations" |
  Where-Object { $_.Directory.Name -like 'tzg-*' } |
  ForEach-Object {
    "### $($_.Directory.Name)"
    Select-String -LiteralPath $_.FullName -Pattern '^(name|status|rrule|model|reasoning_effort) = '
  }
```

Expected: controller active hourly, three legacy writers paused, daily briefing active. No Git commit is required because config files live outside the repository.

## Task 6: Observe and verify the first real hourly run

**Files:**
- Read: `%USERPROFILE%/.codex/automations/tzg-wf2-codex-execute-1/memory.md`
- Read: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json`
- Read: `开发管理/自动工作流状态.txt`
- Read: `开发管理/当前任务队列.txt`

- [ ] **Step 1: Capture the pre-run evidence**

Run before the next minute-15 trigger:

```powershell
$memory = "$env:USERPROFILE\.codex\automations\tzg-wf2-codex-execute-1\memory.md"
Get-Item -LiteralPath $memory | Select-Object LastWriteTime,Length
git log -1 --oneline
git status --short --untracked-files=all
```

Expected: Git is clean. Record the memory timestamp and HEAD.

- [ ] **Step 2: After the scheduled run, inspect controller evidence**

Run:

```powershell
Get-Content -Raw "$env:USERPROFILE\.codex\automations\tzg-wf2-codex-execute-1\memory.md"
powershell -ExecutionPolicy Bypass -File tools/automation-controller-state.ps1 Show
git log -5 --oneline --decorate
git status --short --untracked-files=all
```

Expected: memory has a new run record. The controller selected at most one branch. State is `IDLE` after success/clean skip or `AUTO-BLOCKED` with an actionable reason. There are no unexplained worktree changes.

- [ ] **Step 3: Apply the acceptance decision**

If the run succeeds or cleanly skips, verify its result against Git/task evidence and mark the deployment accepted in `开发管理/自动工作流状态.txt` only if the controller did not already do so. If this creates a project change, run text and diff checks and commit:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理/自动工作流状态.txt
git diff --check
git add 开发管理/自动工作流状态.txt
git commit -m "docs: record hourly controller acceptance"
```

If the run leaves unknown changes, invalid state, or a non-actionable failure, immediately pause the controller through `automation_update`; do not reset state or revert files. Preserve memory, JSON and Git evidence, then diagnose under the systematic-debugging workflow.

## Task 7: Final verification and handoff

**Files:**
- Read: all changed repository files
- Read: five TZG automation configurations

- [ ] **Step 1: Run the final verification suite**

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
powershell -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理,tools
git diff --check
git status --short --untracked-files=all
```

Expected: all checks pass and the worktree is clean.

- [ ] **Step 2: Verify commit scope and operational evidence**

```powershell
git log --oneline --decorate -8
git show --stat --oneline HEAD
```

Expected: repository commits contain only the state tool/tests, workflow rules/routes/status, checker, and optional first-run acceptance update. Automation config evidence shows one active writer and one active read-only briefing.

- [ ] **Step 3: Report completion**

Report:

```text
- Active topology: one hourly writer + one daily read-only briefing
- Paused: WF1, WF3, WF4
- State-tool regression result
- Workflow/config invariant result
- First real run result and evidence
- Commits created
- Any remaining AUTO-BLOCKED item or none
```

Do not claim full completion until the first real run satisfies Task 6.
