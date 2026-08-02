#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Invoke-Git { param([string]$Root, [string[]]$Arguments) $o = @(& git -C $Root @Arguments 2>&1); if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' '): $(@($o) -join "`n")" }; (@($o) -join "`n").Trim() }

$testId = [Guid]::NewGuid().ToString('N')
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $tempBase "tzg-codex-candidate-test-$testId"
$mainRoot = Join-Path $testRoot 'repository'
$fakeBin = Join-Path $testRoot 'bin'
$tracePath = Join-Path $testRoot 'codex-trace.txt'
$approvedState = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId"
$wrapperPath = Join-Path $PSScriptRoot 'invoke-codex-candidate.ps1'
$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$originalPath = $env:PATH
$originalTrace = $env:TZG_FAKE_CODEX_TRACE
$taskId = 'TASK-CODEX-CANDIDATE'

try {
  [IO.Directory]::CreateDirectory((Join-Path $mainRoot 'tools')) | Out-Null
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  foreach ($tool in @('automation-finalize-commit.ps1', 'automation-commit-metadata.ps1', 'check-pending-whitespace.ps1', 'check-task-cards.ps1', 'private-path-acl.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $mainRoot "tools/$tool")
  }
  Write-Utf8 -Path (Join-Path $mainRoot '.gitignore') -Text ".worktrees/`n"
  Write-Utf8 -Path (Join-Path $mainRoot 'AGENTS.md') -Text '# Codex candidate fixture'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/自动工作流规则.txt') -Text '# workflow rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI协作规则.txt') -Text '# collaboration rules'
  $metadata = [ordered]@{
    schemaVersion = 1; id = $taskId; title = 'Codex candidate fixture'; priority = 'P1'; route = 'codex_execute'; owner = 'codex'
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @(); stateReason = 'fixture'
    expectedPaths = @('开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt', "开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt")
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $taskId · Codex candidate fixture",
    '## 来源与当前边界', '- fixture', '## 必查范围', '- fixture', '## 实施范围', '- block fixture task',
    '## 禁止项', '- no extra paths', '## 验证', '- task-card checker', '## 完成条件', '- blocked state', '## 停止条件', '- invalid projection'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $mainRoot "开发管理/任务卡/$taskId.txt") -Text $card
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/当前任务队列.txt') -Text (@(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | codex_execute | codex | P1 | automation | implementation | Codex candidate fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/任务列表/自动化任务.txt') -Text (@(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | P1 | codex | 已排队 | — | Codex candidate fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Invoke-Git -Root $mainRoot -Arguments @('init') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.name', 'Codex Candidate Test') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.email', 'codex-candidate@example.invalid') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('add', '--', '.') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('commit', '-m', 'test: initialize Codex candidate fixture') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('branch', '-M', 'master') | Out-Null
  $base = Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')
  $cardText = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes((Join-Path $mainRoot "开发管理/任务卡/$taskId.txt"))).TrimStart([char]0xFEFF)
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($cardText.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
  $claimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $stateRoot -Owner codex -TaskId $taskId -Route codex_execute -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $base -TaskCardDigest $digest)
  $run = ($claimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$run.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$run.candidateBranch, [string]$run.worktree, $base) | Out-Null

  Write-Utf8 -Path (Join-Path $fakeBin 'fake-codex.ps1') -Text @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CliArguments)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$prompt = [Console]::In.ReadToEnd()
[IO.File]::WriteAllText($env:TZG_FAKE_CODEX_TRACE, $prompt, [Text.UTF8Encoding]::new($false))
$taskId = 'TASK-CODEX-CANDIDATE'
$cardPath = Join-Path ([Environment]::CurrentDirectory) "开发管理/任务卡/$taskId.txt"
$card = [IO.File]::ReadAllText($cardPath)
$card = $card.Replace('"dispatchState": "ready"', '"dispatchState": "blocked"').Replace('"stateReason": "fixture"', '"stateReason": "fixture blocker confirmed"')
[IO.File]::WriteAllText($cardPath, $card, [Text.UTF8Encoding]::new($false))
$queuePath = Join-Path ([Environment]::CurrentDirectory) '开发管理/当前任务队列.txt'
$queue = @([IO.File]::ReadAllLines($queuePath) | Where-Object { $_ -notmatch '^\| TASK-CODEX-CANDIDATE \|' }) -join "`n"
[IO.File]::WriteAllText($queuePath, $queue, [Text.UTF8Encoding]::new($false))
$backlogPath = Join-Path ([Environment]::CurrentDirectory) '开发管理/任务列表/自动化任务.txt'
$backlog = [IO.File]::ReadAllText($backlogPath).Replace('| TASK-CODEX-CANDIDATE | P1 | codex | 已排队 |', '| TASK-CODEX-CANDIDATE | P1 | codex | 阻塞 |')
[IO.File]::WriteAllText($backlogPath, $backlog, [Text.UTF8Encoding]::new($false))
& git add -- $cardPath $queuePath $backlogPath
$message = "test: close Codex candidate fixture`n`nAutomation: tzg-hourly-controller`nTask: TASK-CODEX-CANDIDATE`nState: completed`nResult: 问题=测试任务仍可调度；完成=确认阻塞并移出队列`nImpact: 影响=验证 Codex 候选入口；边界=不修改真实任务`nVerify: 验证=任务投影检查通过；后续=等待固定入口集成`nPlain: 发生=测试任务被标记为暂不可执行；影响=只验证自动流程；需要=无需处理"
& git commit -q -m $message
if ($LASTEXITCODE -ne 0) { throw 'fake Codex commit failed' }
[Console]::Out.WriteLine(([ordered]@{ type = 'thread.started'; thread_id = [Guid]::NewGuid().ToString() } | ConvertTo-Json -Compress))
'@
  Write-Utf8 -Path (Join-Path $fakeBin 'codex.cmd') -Text "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-codex.ps1`" %*"
  $env:PATH = "$fakeBin;$originalPath"
  $env:TZG_FAKE_CODEX_TRACE = $tracePath
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Route Execution -RepositoryRoot ([string]$run.worktree) -TaskId $taskId -RunId ([string]$run.runId) -Model 'test-codex-model' -StateRoot $stateRoot -ResponsibilityTimeoutSeconds 30)
  Assert-Equal $LASTEXITCODE 0 'Codex candidate wrapper process failed'
  Assert-Equal $output.Count 1 'Codex candidate wrapper output count mismatch'
  $candidate = $output[0] | ConvertFrom-Json -Depth 50
  Assert-Equal ([string]$candidate.status) 'completed' "Codex candidate failed: $($candidate | ConvertTo-Json -Compress -Depth 20)"
  Assert-True ([string]$candidate.sessionId -ne '') 'Codex candidate sessionId is missing'
  Assert-Equal ([string]$candidate.candidateResult.expectedTransition) 'blocked' 'Codex candidate transition mismatch'
  Assert-True (@($candidate.candidateResult.changedPaths) -ccontains "开发管理/任务卡/$taskId.txt") 'Codex candidate lost task-card path'
  $trace = [IO.File]::ReadAllText($tracePath)
  Assert-True ($trace -match '\[TZG_CODEX_CANDIDATE\]') 'Codex candidate prompt marker is missing'
  Assert-True ($trace -match 'claim') 'Codex candidate prompt omitted fixed claim boundary'
  Assert-True ($trace -match 'worktree/branch') 'Codex candidate prompt omitted worktree boundary'
  Assert-Equal (Invoke-Git -Root ([string]$run.worktree) -Arguments @('rev-list', '--count', "$base..HEAD")) '1' 'Codex candidate did not create exactly one commit'
  Assert-Equal (Invoke-Git -Root ([string]$run.worktree) -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex candidate worktree is dirty'

  Write-Output 'test-invoke-codex-candidate: OK'
} finally {
  $env:PATH = $originalPath; $env:TZG_FAKE_CODEX_TRACE = $originalTrace
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($tempBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-codex-candidate-test-$testId") { throw "Unsafe Codex candidate test cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedState = [IO.Path]::GetFullPath($stateRoot)
    if (-not $resolvedState.StartsWith($approvedState + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedState) -cne "tzg-codex-candidate-test-$testId") { throw "Unsafe Codex candidate state cleanup: $resolvedState" }
    Remove-Item -LiteralPath $resolvedState -Recurse -Force
  }
}
