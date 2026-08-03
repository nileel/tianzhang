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
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $hourlyPath, '-Owner', 'codex', '-Action', 'RunOnce', '-RepositoryRoot', $mainRoot,
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
$hourlyPath = Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'
$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$originalPath = $env:PATH
$originalTrace = $env:TZG_FAKE_CODEX_TRACE
$originalMain = $env:TZG_FAKE_MAIN_ROOT
$originalMaintenance = $env:TZG_FAKE_QUEUE_MAINTENANCE
$taskId = 'TASK-CODEX-HOURLY'

try {
  [IO.Directory]::CreateDirectory((Join-Path $mainRoot 'tools')) | Out-Null
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  foreach ($tool in @(
      'automation-finalize-commit.ps1', 'automation-commit-metadata.ps1', 'check-pending-whitespace.ps1', 'check-task-cards.ps1', 'codex-cli-session.ps1',
      'hourly-automation-lease.ps1', 'invoke-codex-candidate.ps1', 'private-path-acl.ps1', 'select-hourly-task.ps1'
    )) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $mainRoot "tools/$tool")
  }
  Write-Utf8 -Path (Join-Path $mainRoot '.gitignore') -Text ".worktrees/`n"
  Write-Utf8 -Path (Join-Path $mainRoot 'AGENTS.md') -Text '# Codex hourly fixture'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/自动工作流规则.txt') -Text '# workflow rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI协作规则.txt') -Text '# collaboration rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/自动工作流状态.txt') -Text '# fixture automation status'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/任务卡/.gitkeep') -Text ''
  Write-Utf8 -Path (Join-Path $mainRoot 'unrelated.txt') -Text 'base'
  $metadata = [ordered]@{
    schemaVersion = 1; id = $taskId; title = 'Codex hourly fixture'; priority = 'P1'; route = 'codex_execute'; owner = 'codex'
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @(); stateReason = 'fixture'
    expectedPaths = @('开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt', "开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt")
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $taskId · Codex hourly fixture",
    '## 来源与当前边界', '- fixture', '## 必查范围', '- fixture', '## 实施范围', '- archive fixture task',
    '## 禁止项', '- no extra paths', '## 验证', '- task-card checker', '## 完成条件', '- completed archive', '## 停止条件', '- invalid projection'
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
$CliArguments = @($args)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$prompt = [Console]::In.ReadToEnd()
[IO.File]::WriteAllText($env:TZG_FAKE_CODEX_TRACE, $prompt, [Text.UTF8Encoding]::new($false))
$outputIndex = [Array]::IndexOf($CliArguments, '--output-last-message')
$outputPath = $CliArguments[$outputIndex + 1]
if ($prompt.Contains('Route: QueueMaintenance')) {
  if ($env:TZG_FAKE_QUEUE_MAINTENANCE -ceq 'completed') {
    $statusPath = Join-Path ([Environment]::CurrentDirectory) '开发管理/自动工作流状态.txt'
    [IO.File]::AppendAllText($statusPath, "`n- fixture maintenance completed", [Text.UTF8Encoding]::new($false))
    $maintenanceOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -RepositoryRoot ([Environment]::CurrentDirectory) -ExpectedPaths '开发管理/自动工作流状态.txt' -CommitMessage 'chore(queue): fixture maintenance' -RequireAutomationMetadata -AutomationTask QUEUE-MAINTENANCE -AutomationState completed -AutomationResult '问题=队列维护事实待记录；完成=记录 fixture 维护事实' -AutomationImpact '影响=验证维护独立终态；边界=不执行业务任务' -AutomationVerify '验证=任务投影检查通过；后续=继续小时调度' -AutomationPlain '发生=测试维护事实已记录；影响=只验证维护流程；需要=无需处理' 2>&1)
    $maintenanceCommit = if ($maintenanceOutput.Count) { [string]$maintenanceOutput[-1] } else { '' }
    if ($LASTEXITCODE -ne 0 -or $maintenanceCommit -notmatch '^[0-9a-f]{40,64}$') { throw 'fake maintenance commit failed' }
    [IO.File]::WriteAllText($outputPath, ([ordered]@{ status='completed'; identity='Codex'; model='test-codex-model'; candidateCommit=$maintenanceCommit } | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    [Console]::Out.WriteLine(([ordered]@{ type = 'thread.started'; thread_id = [Guid]::NewGuid().ToString() } | ConvertTo-Json -Compress))
    exit 0
  }
  [IO.File]::WriteAllText($outputPath, ([ordered]@{ status='no_candidate'; identity='Codex'; model='test-codex-model' } | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
  [Console]::Out.WriteLine(([ordered]@{ type = 'thread.started'; thread_id = [Guid]::NewGuid().ToString() } | ConvertTo-Json -Compress))
  exit 0
}
$taskId = 'TASK-CODEX-HOURLY'
$cardPath = Join-Path ([Environment]::CurrentDirectory) "开发管理/任务卡/$taskId.txt"
$archivePath = Join-Path ([Environment]::CurrentDirectory) "开发管理/任务归档/$taskId.txt"
$card = [IO.File]::ReadAllText($cardPath).Replace('"dispatchState": "ready"', '"dispatchState": "completed"').Replace('"stateReason": "fixture"', '"stateReason": "fixture completion confirmed"')
[IO.Directory]::CreateDirectory((Split-Path -Parent $archivePath)) | Out-Null
[IO.File]::WriteAllText($archivePath, $card, [Text.UTF8Encoding]::new($false))
[IO.File]::Delete($cardPath)
$queuePath = Join-Path ([Environment]::CurrentDirectory) '开发管理/当前任务队列.txt'
$queue = @([IO.File]::ReadAllLines($queuePath) | Where-Object { $_ -notmatch '^\| TASK-CODEX-HOURLY \|' }) -join "`n"
[IO.File]::WriteAllText($queuePath, $queue, [Text.UTF8Encoding]::new($false))
$backlogPath = Join-Path ([Environment]::CurrentDirectory) '开发管理/任务列表/自动化任务.txt'
$backlog = @([IO.File]::ReadAllLines($backlogPath) | Where-Object { $_ -notmatch '^\| TASK-CODEX-HOURLY \|' }) -join "`n"
[IO.File]::WriteAllText($backlogPath, $backlog, [Text.UTF8Encoding]::new($false))
$finalizerOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -RepositoryRoot ([Environment]::CurrentDirectory) -ExpectedPaths '开发管理/任务列表/自动化任务.txt|开发管理/任务归档/TASK-CODEX-HOURLY.txt|开发管理/任务卡/TASK-CODEX-HOURLY.txt|开发管理/当前任务队列.txt' -CommitMessage 'test: archive Codex hourly fixture' -RequireAutomationMetadata -AutomationTask TASK-CODEX-HOURLY -AutomationState completed -AutomationResult '问题=测试任务等待闭环；完成=归档任务并移出队列' -AutomationImpact '影响=验证 Codex 删除路径集成；边界=不修改真实任务' -AutomationVerify '验证=任务归档投影检查通过；后续=等待固定入口集成' -AutomationPlain '发生=测试任务已完成并归档；影响=只验证自动流程；需要=无需处理' 2>&1)
$commit = if ($finalizerOutput.Count) { [string]$finalizerOutput[-1] } else { '' }
[IO.File]::AppendAllText($env:TZG_FAKE_CODEX_TRACE, "`nFINALIZER=$LASTEXITCODE|$(@($finalizerOutput) -join ' // ')", [Text.UTF8Encoding]::new($false))
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40,64}$') { throw 'fake Codex commit failed' }
[IO.File]::AppendAllText($env:TZG_FAKE_CODEX_TRACE, "`nMESSAGE=$([string](& git show -s --format=%B $commit))", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $env:TZG_FAKE_MAIN_ROOT 'unrelated.txt'), 'advanced independently', [Text.UTF8Encoding]::new($false))
& git -C $env:TZG_FAKE_MAIN_ROOT add -- unrelated.txt
& git -C $env:TZG_FAKE_MAIN_ROOT commit -q -m 'test: concurrent unrelated main change'
if ($LASTEXITCODE -ne 0) { throw 'fake concurrent main commit failed' }
[IO.File]::WriteAllText($outputPath, ([ordered]@{
  status='completed'; identity='Codex'; model='test-codex-model'; candidateCommit=$commit
  expectedTransition='completed'; changedPaths=@('开发管理/任务列表/自动化任务.txt','开发管理/任务归档/TASK-CODEX-HOURLY.txt','开发管理/任务卡/TASK-CODEX-HOURLY.txt','开发管理/当前任务队列.txt')
  verified=@('任务归档投影检查通过'); unverified=@('none'); residualRisk='fixture only'
  result='问题=测试任务等待闭环；完成=归档任务并移出队列'; impact='影响=验证 Codex 删除路径集成；边界=不修改真实任务'
  verify='验证=任务归档投影检查通过；后续=等待固定入口集成'; plain='发生=测试任务已完成并归档；影响=只验证自动流程；需要=无需处理'
} | ConvertTo-Json -Compress -Depth 10), [Text.UTF8Encoding]::new($false))
[Console]::Out.WriteLine(([ordered]@{ type = 'thread.started'; thread_id = [Guid]::NewGuid().ToString() } | ConvertTo-Json -Compress))
'@
  Write-Utf8 -Path (Join-Path $fakeBin 'codex.cmd') -Text "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-codex.ps1`" %*"
  $env:PATH = "$fakeBin;$originalPath"
  $env:TZG_FAKE_CODEX_TRACE = $tracePath
  $env:TZG_FAKE_MAIN_ROOT = $mainRoot
  $env:TZG_FAKE_QUEUE_MAINTENANCE = 'no_candidate'

  $run = Invoke-Hourly
  Assert-Equal $run.ExitCode 0 "Codex RunOnce process failed: $($run.Stderr)"
  if ([string]$run.Json.status -cne 'completed') {
    $active = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action Show -StateRoot $stateRoot)[0] | ConvertFrom-Json -Depth 50
    $candidateMessage = if ($null -ne $active.state.runs.codex) { Invoke-Git -Root ([string]$active.state.runs.codex.worktree) -Arguments @('show','-s','--format=%B','HEAD') } else { 'missing' }
    $postcondition = if ($null -ne $active.state.runs.codex) { @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $mainRoot 'tools/check-task-cards.ps1') -RepositoryRoot ([string]$active.state.runs.codex.worktree) -TaskId $taskId -Postcondition CodexClosedOrNonReady -OutputJson 2>&1) -join ' // ' } else { 'missing' }
    throw "Codex RunOnce did not complete: $($run.Stdout) stderr=$($run.Stderr) candidateMessage=$($candidateMessage | ConvertTo-Json -Compress) postcondition=$postcondition trace=$([IO.File]::ReadAllText($tracePath))"
  }
  Assert-Equal ([string]$run.Json.taskId) $taskId 'Codex RunOnce task mismatch'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('branch', '--show-current')) 'master' 'Main branch changed'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')) ([string]$run.Json.formalHead) 'Main HEAD did not fast-forward to formal head'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('rev-list', '--count', "$initialHead..HEAD")) '2' 'Expected unrelated + Codex candidate commits'
  $subjects = Invoke-Git -Root $mainRoot -Arguments @('log', '--format=%s', '--reverse', "$initialHead..HEAD")
  Assert-True ($subjects -match 'concurrent unrelated main change') 'Concurrent unrelated commit was lost'
  Assert-True ($subjects -match 'archive Codex hourly fixture') 'Codex candidate commit was not integrated'
  Assert-True (-not (Test-Path -LiteralPath (Join-Path $mainRoot "开发管理/任务卡/$taskId.txt"))) 'Integrated task kept the deleted active card'
  $cardAfter = [IO.File]::ReadAllText((Join-Path $mainRoot "开发管理/任务归档/$taskId.txt"))
  Assert-True ($cardAfter -match '"dispatchState": "completed"') 'Integrated task archive was not completed'
  $runtime = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action Show -StateRoot $stateRoot -RepositoryRoot $mainRoot)
  $runtimeJson = $runtime[0] | ConvertFrom-Json -Depth 50
  Assert-True ($null -eq $runtimeJson.state.runs.codex) 'Completed Codex run remained active'
  Assert-True ([int]$runtimeJson.state.schemaVersion -eq 5) 'Runtime did not use schema 5'
  Assert-Equal ([string]$run.Json.cleanup) 'cleaned' 'Successful Codex worktree was not cleaned'
  Assert-True (([IO.File]::ReadAllText($tracePath)) -match '\[TZG_CODEX_CANDIDATE\]') 'Fixed Codex candidate prompt was not used'

  $env:TZG_FAKE_QUEUE_MAINTENANCE = 'completed'
  $maintenance = Invoke-Hourly
  Assert-Equal ([string]$maintenance.Json.status) 'maintenance_completed' "QueueMaintenance used a business completion status: $($maintenance.Stdout) trace=$([IO.File]::ReadAllText($tracePath))"
  Assert-Equal ([string]$maintenance.Json.notification) 'skipped' 'QueueMaintenance sent a normal task notification'
  Assert-Equal ([string]$maintenance.Json.cleanup) 'cleaned' 'QueueMaintenance worktree was not cleaned'

  $env:TZG_FAKE_QUEUE_MAINTENANCE = 'no_candidate'
  $none = Invoke-Hourly
  Assert-Equal ([string]$none.Json.status) 'no_candidate' 'Second Codex run did not skip cleanly'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex hourly flow changed main workspace outside commits'

  Write-Output 'test-invoke-hourly-owner-codex: OK'
} finally {
  $env:PATH = $originalPath; $env:TZG_FAKE_CODEX_TRACE = $originalTrace; $env:TZG_FAKE_MAIN_ROOT = $originalMain; $env:TZG_FAKE_QUEUE_MAINTENANCE = $originalMaintenance
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
