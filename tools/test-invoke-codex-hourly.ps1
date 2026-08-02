#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Invoke-Git { param([string]$Root, [string[]]$Arguments) $output = @(& git -C $Root @Arguments 2>&1); if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' '): $(@($output) -join "`n")" }; (@($output) -join "`n").Trim() }

function Invoke-Hourly {
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'; $startInfo.WorkingDirectory = $mainRoot; $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
  foreach ($argument in @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $hourlyPath, '-RepositoryRoot', $mainRoot,
      '-Model', 'test-codex-model', '-StateRoot', $stateRoot, '-ResponsibilityTimeoutSeconds', '30', '-OutputJson'
    )) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
  Assert-True $process.Start() 'Codex hourly process did not start'
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult().Trim(); $stderr = $stderrTask.GetAwaiter().GetResult().Trim(); $exitCode = $process.ExitCode; $process.Dispose()
  [pscustomobject]@{ ExitCode = $exitCode; Json = $stdout | ConvertFrom-Json -Depth 100; Stdout = $stdout; Stderr = $stderr }
}

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $temporaryBase "tzg-中文-codex-hourly-test-$testId"
$mainRoot = Join-Path $testRoot 'repository'
$fakeBin = Join-Path $testRoot 'bin'
$tracePath = Join-Path $testRoot 'codex-trace.txt'
$approvedStateRoot = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedStateRoot "tzg-codex-hourly-test-$testId"
$hourlyPath = Join-Path $PSScriptRoot 'invoke-codex-hourly.ps1'
$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$originalPath = $env:PATH
$originalTrace = $env:TZG_FAKE_CODEX_TRACE
$originalMain = $env:TZG_FAKE_MAIN_ROOT
$taskId = 'TASK-CODEX-HOURLY'

try {
  [IO.Directory]::CreateDirectory((Join-Path $mainRoot 'tools')) | Out-Null
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  foreach ($tool in @(
      'automation-commit-metadata.ps1', 'check-task-cards.ps1', 'codex-cli-session.ps1',
      'hourly-automation-lease.ps1', 'invoke-codex-candidate.ps1', 'private-path-acl.ps1', 'select-hourly-task.ps1'
    )) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $mainRoot "tools/$tool")
  }
  Write-Utf8 -Path (Join-Path $mainRoot '.gitignore') -Text ".worktrees/`n"
  Write-Utf8 -Path (Join-Path $mainRoot 'AGENTS.md') -Text '# Codex hourly fixture'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/自动工作流规则.txt') -Text '# workflow rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI协作规则.txt') -Text '# collaboration rules'
  Write-Utf8 -Path (Join-Path $mainRoot 'unrelated.txt') -Text 'base'
  $metadata = [ordered]@{
    schemaVersion = 1; id = $taskId; title = 'Codex hourly fixture'; priority = 'P1'; route = 'codex_execute'; owner = 'codex'
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @(); stateReason = 'fixture'
    expectedPaths = @('开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt', "开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt")
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $taskId · Codex hourly fixture",
    '## 来源与当前边界', '- fixture', '## 必查范围', '- fixture', '## 实施范围', '- block fixture task',
    '## 禁止项', '- no extra paths', '## 验证', '- task-card checker', '## 完成条件', '- blocked state', '## 停止条件', '- invalid projection'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $mainRoot "开发管理/任务卡/$taskId.txt") -Text $card
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/当前任务队列.txt') -Text (@(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | codex_execute | codex | P1 | automation | implementation | Codex hourly fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/任务列表/自动化任务.txt') -Text (@(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | P1 | codex | 已排队 | — | Codex hourly fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Invoke-Git -Root $mainRoot -Arguments @('init') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.name', 'Codex Hourly Test') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.email', 'codex-hourly@example.invalid') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('add', '--', '.') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('commit', '-m', 'test: initialize Codex hourly fixture') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('branch', '-M', 'master') | Out-Null
  $initialHead = Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')

  Write-Utf8 -Path (Join-Path $fakeBin 'fake-codex.ps1') -Text @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CliArguments)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$prompt = [Console]::In.ReadToEnd()
[IO.File]::WriteAllText($env:TZG_FAKE_CODEX_TRACE, $prompt, [Text.UTF8Encoding]::new($false))
if ($prompt.Contains('Route: QueueMaintenance')) {
  [Console]::Out.WriteLine(([ordered]@{ type = 'thread.started'; thread_id = [Guid]::NewGuid().ToString() } | ConvertTo-Json -Compress))
  exit 0
}
$taskId = 'TASK-CODEX-HOURLY'
$cardPath = Join-Path ([Environment]::CurrentDirectory) "开发管理/任务卡/$taskId.txt"
$card = [IO.File]::ReadAllText($cardPath).Replace('"dispatchState": "ready"', '"dispatchState": "blocked"').Replace('"stateReason": "fixture"', '"stateReason": "fixture blocker confirmed"')
[IO.File]::WriteAllText($cardPath, $card, [Text.UTF8Encoding]::new($false))
$queuePath = Join-Path ([Environment]::CurrentDirectory) '开发管理/当前任务队列.txt'
$queue = @([IO.File]::ReadAllLines($queuePath) | Where-Object { $_ -notmatch '^\| TASK-CODEX-HOURLY \|' }) -join "`n"
[IO.File]::WriteAllText($queuePath, $queue, [Text.UTF8Encoding]::new($false))
$backlogPath = Join-Path ([Environment]::CurrentDirectory) '开发管理/任务列表/自动化任务.txt'
$backlog = [IO.File]::ReadAllText($backlogPath).Replace('| TASK-CODEX-HOURLY | P1 | codex | 已排队 |', '| TASK-CODEX-HOURLY | P1 | codex | 阻塞 |')
[IO.File]::WriteAllText($backlogPath, $backlog, [Text.UTF8Encoding]::new($false))
& git add -- $cardPath $queuePath $backlogPath
$message = "test: close Codex hourly fixture`n`nAutomation: tzg-hourly-controller`nTask: TASK-CODEX-HOURLY`nState: completed`nResult: 问题=测试任务仍可调度；完成=确认阻塞并移出队列`nImpact: 影响=验证 Codex 小时入口；边界=不修改真实任务`nVerify: 验证=任务投影检查通过；后续=等待固定入口集成`nPlain: 发生=测试任务被标记为暂不可执行；影响=只验证自动流程；需要=无需处理"
& git commit -q -m $message
if ($LASTEXITCODE -ne 0) { throw 'fake Codex commit failed' }
[IO.File]::WriteAllText((Join-Path $env:TZG_FAKE_MAIN_ROOT 'unrelated.txt'), 'advanced independently', [Text.UTF8Encoding]::new($false))
& git -C $env:TZG_FAKE_MAIN_ROOT add -- unrelated.txt
& git -C $env:TZG_FAKE_MAIN_ROOT commit -q -m 'test: concurrent unrelated main change'
if ($LASTEXITCODE -ne 0) { throw 'fake concurrent main commit failed' }
[Console]::Out.WriteLine(([ordered]@{ type = 'thread.started'; thread_id = [Guid]::NewGuid().ToString() } | ConvertTo-Json -Compress))
'@
  Write-Utf8 -Path (Join-Path $fakeBin 'codex.cmd') -Text "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-codex.ps1`" %*"
  $env:PATH = "$fakeBin;$originalPath"
  $env:TZG_FAKE_CODEX_TRACE = $tracePath
  $env:TZG_FAKE_MAIN_ROOT = $mainRoot

  $run = Invoke-Hourly
  Assert-Equal $run.ExitCode 0 "Codex RunOnce process failed: $($run.Stderr)"
  Assert-Equal ([string]$run.Json.status) 'completed' "Codex RunOnce did not complete: $($run.Stdout) stderr=$($run.Stderr)"
  Assert-Equal ([string]$run.Json.taskId) $taskId 'Codex RunOnce task mismatch'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('branch', '--show-current')) 'master' 'Main branch changed'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')) ([string]$run.Json.canonicalHead) 'Main HEAD did not fast-forward to canonical head'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('rev-list', '--count', "$initialHead..HEAD")) '2' 'Expected unrelated + Codex candidate commits'
  $subjects = Invoke-Git -Root $mainRoot -Arguments @('log', '--format=%s', '--reverse', "$initialHead..HEAD")
  Assert-True ($subjects -match 'concurrent unrelated main change') 'Concurrent unrelated commit was lost'
  Assert-True ($subjects -match 'close Codex hourly fixture') 'Codex candidate commit was not integrated'
  $cardAfter = [IO.File]::ReadAllText((Join-Path $mainRoot "开发管理/任务卡/$taskId.txt"))
  Assert-True ($cardAfter -match '"dispatchState": "blocked"') 'Integrated task was not blocked'
  $runtime = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action Show -StateRoot $stateRoot)
  $runtimeJson = $runtime[0] | ConvertFrom-Json -Depth 50
  Assert-True ($null -eq $runtimeJson.state.runs.codex) 'Completed Codex run remained active'
  Assert-True ($null -eq $runtimeJson.state.integrationLease) 'Completed integration lease remained active'
  Assert-True (([IO.File]::ReadAllText($tracePath)) -match '\[TZG_CODEX_CANDIDATE\]') 'Fixed Codex candidate prompt was not used'

  $none = Invoke-Hourly
  Assert-Equal ([string]$none.Json.status) 'no_candidate' 'Second Codex run did not skip cleanly'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex hourly flow changed main workspace outside commits'

  Write-Output 'test-invoke-codex-hourly: OK'
} finally {
  $env:PATH = $originalPath; $env:TZG_FAKE_CODEX_TRACE = $originalTrace; $env:TZG_FAKE_MAIN_ROOT = $originalMain
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($temporaryBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-中文-codex-hourly-test-$testId") { throw "Unsafe Codex hourly test cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedState = [IO.Path]::GetFullPath($stateRoot)
    if (-not $resolvedState.StartsWith($approvedStateRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedState) -cne "tzg-codex-hourly-test-$testId") { throw "Unsafe Codex hourly state cleanup: $resolvedState" }
    Remove-Item -LiteralPath $resolvedState -Recurse -Force
  }
}
