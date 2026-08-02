#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Invoke-Git { param([string]$Root, [string[]]$Arguments) $output = @(& git -C $Root @Arguments 2>&1); if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' '): $(@($output) -join "`n")" }; (@($output) -join "`n").Trim() }

function Invoke-Hourly {
  param([string]$Action, [switch]$UseStateRoot)
  $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $hourlyPath, '-Action', $Action, '-RepositoryRoot', $mainRoot, '-ResponsibilityTimeoutSeconds', '30', '-OutputJson')
  if ($UseStateRoot) { $arguments += @('-StateRoot', $stateRoot) }
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'; $startInfo.WorkingDirectory = $mainRoot; $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
  foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
  Assert-True $process.Start() 'Hourly process did not start'
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult().Trim(); $stderr = $stderrTask.GetAwaiter().GetResult().Trim(); $exitCode = $process.ExitCode; $process.Dispose()
  [pscustomobject]@{ ExitCode = $exitCode; Json = $stdout | ConvertFrom-Json -Depth 100; Stdout = $stdout; Stderr = $stderr }
}

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $temporaryBase "tzg-deepseek-hourly-test-$testId"
$mainRoot = Join-Path $testRoot 'repository'
$fakeBin = Join-Path $testRoot 'bin'
$recordPath = Join-Path $testRoot 'claude-record.json'
$approvedStateRoot = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedStateRoot "tzg-deepseek-hourly-test-$testId"
$hourlyPath = Join-Path $PSScriptRoot 'invoke-deepseek-hourly.ps1'
$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$originalPath = $env:PATH
$originalBaseUrl = $env:ANTHROPIC_BASE_URL
$originalRecord = $env:TZG_FAKE_CLAUDE_RECORD
$originalMain = $env:TZG_FAKE_MAIN_ROOT
$taskId = 'TASK-DEEPSEEK-HOURLY'

try {
  [IO.Directory]::CreateDirectory((Join-Path $mainRoot 'tools')) | Out-Null
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  foreach ($tool in @(
      'automation-finalize-commit.ps1', 'automation-commit-metadata.ps1', 'check-pending-whitespace.ps1',
      'check-task-cards.ps1', 'check-pwsh-runtime.ps1', 'hourly-automation-lease.ps1', 'private-path-acl.ps1'
    )) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $mainRoot "tools/$tool")
  }
  Write-Utf8 -Path (Join-Path $mainRoot '.gitignore') -Text ".worktrees/`n"
  Write-Utf8 -Path (Join-Path $mainRoot 'AGENTS.md') -Text '# hourly fixture rules'
  Write-Utf8 -Path (Join-Path $mainRoot 'CLAUDE.md') -Text '# hourly fixture Claude rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/自动工作流规则.txt') -Text '# workflow rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI协作规则.txt') -Text '# collaboration rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/DeepSeek工作提示词.txt') -Text '# DeepSeek prompt'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI合作沟通.txt') -Text (@(
    '# AI合作沟通（✅ 已审核）', '', '> 用途：fixture', '', '## 当前交接队列', '', '> 注：fixture', '', '当前无有效交接条目。'
  ) -join "`n")
  Write-Utf8 -Path (Join-Path $mainRoot 'unrelated.txt') -Text 'base'
  $metadata = [ordered]@{
    schemaVersion = 1; id = $taskId; title = 'Hourly fixture'; priority = 'P1'; route = 'external_execute'; owner = 'deepseek'
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @(); stateReason = 'fixture'
    expectedPaths = @('fixtures/business.txt', '开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt', "开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt")
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $taskId · Hourly fixture",
    '## 来源与当前边界', '- fixture', '## 必查范围', '- fixture', '## 实施范围', '- create fixtures/business.txt',
    '## 禁止项', '- no lifecycle changes in candidate', '## 验证', '- git diff --check', '## 完成条件', '- pending review', '## 停止条件', '- path violation'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $mainRoot "开发管理/任务卡/$taskId.txt") -Text $card
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/当前任务队列.txt') -Text (@(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | external_execute | deepseek | P1 | automation | implementation | Hourly fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/任务列表/自动化任务.txt') -Text (@(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | P1 | deepseek | 已排队 | — | Hourly fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Invoke-Git -Root $mainRoot -Arguments @('init') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.name', 'DeepSeek Hourly Test') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.email', 'deepseek-hourly@example.invalid') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('add', '--', '.') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('commit', '-m', 'test: initialize hourly fixture') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('branch', '-M', 'master') | Out-Null
  $initialHead = Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')

  Write-Utf8 -Path (Join-Path $fakeBin 'fake-claude.ps1') -Text @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CliArguments)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$prompt = [Console]::In.ReadToEnd()
$sessionIndex = [Array]::IndexOf($CliArguments, '--session-id')
$sessionId = $CliArguments[$sessionIndex + 1]
[IO.File]::WriteAllText($env:TZG_FAKE_CLAUDE_RECORD, ([ordered]@{ arguments = $CliArguments; prompt = $prompt; cwd = [Environment]::CurrentDirectory } | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
if ($prompt.Contains('[TZG_DEEPSEEK_WINDOWS_CANARY]')) {
  $terminal = [ordered]@{ status = 'verified'; identity = 'DeepSeek V4 Flash'; model = 'deepseek-v4-flash' }
} else {
  [IO.Directory]::CreateDirectory((Join-Path ([Environment]::CurrentDirectory) 'fixtures')) | Out-Null
  [IO.File]::WriteAllText((Join-Path ([Environment]::CurrentDirectory) 'fixtures/business.txt'), 'hourly candidate', [Text.UTF8Encoding]::new($false))
  & git add -- fixtures/business.txt
  & git commit -q -m 'candidate(TASK-DEEPSEEK-HOURLY): DeepSeek implementation'
  if ($LASTEXITCODE -ne 0) { throw 'fake candidate commit failed' }
  $commit = [string](& git rev-parse HEAD)
  [IO.File]::WriteAllText((Join-Path $env:TZG_FAKE_MAIN_ROOT 'unrelated.txt'), 'advanced independently', [Text.UTF8Encoding]::new($false))
  & git -C $env:TZG_FAKE_MAIN_ROOT add -- unrelated.txt
  & git -C $env:TZG_FAKE_MAIN_ROOT commit -q -m 'test: concurrent unrelated main change'
  if ($LASTEXITCODE -ne 0) { throw 'fake concurrent main commit failed' }
  $terminal = [ordered]@{
    status = 'completed'; identity = 'DeepSeek V4 Flash'; model = 'deepseek-v4-flash'; candidateCommit = $commit
    expectedTransition = 'codex_review/codex/ready'; changedPaths = @('fixtures/business.txt'); verified = @('git diff --check passed')
    unverified = @('none'); residualRisk = 'fixture only'; result = '问题=缺少小时候选；完成=创建小时候选'
    impact = '影响=验证独立 DeepSeek 入口；边界=不修改真实项目'; verify = '验证=候选 Git 检查通过；后续=等待 Codex 复审'
    plain = '发生=自动任务生成了测试结果；影响=只验证新工作流；需要=无需处理'
  }
}
[Console]::Out.WriteLine(([ordered]@{ type = 'result'; subtype = 'success'; is_error = $false; session_id = $sessionId; structured_output = $terminal } | ConvertTo-Json -Compress -Depth 20))
'@
  Write-Utf8 -Path (Join-Path $fakeBin 'claude.cmd') -Text "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-claude.ps1`" %*"
  $env:PATH = "$fakeBin;$originalPath"
  $env:ANTHROPIC_BASE_URL = 'http://127.0.0.1:15721/claude-desktop'
  $env:TZG_FAKE_CLAUDE_RECORD = $recordPath
  $env:TZG_FAKE_MAIN_ROOT = $mainRoot

  $run = Invoke-Hourly -Action RunOnce -UseStateRoot
  Assert-Equal $run.ExitCode 0 "RunOnce process failed: $($run.Stderr)"
  Assert-Equal ([string]$run.Json.status) 'completed' "RunOnce did not complete: $($run.Stdout) stderr=$($run.Stderr)"
  Assert-Equal ([string]$run.Json.category) 'success' 'RunOnce category mismatch'
  Assert-Equal ([string]$run.Json.taskId) $taskId 'RunOnce task mismatch'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('branch', '--show-current')) 'master' 'Main branch changed'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')) ([string]$run.Json.canonicalHead) 'Main HEAD did not fast-forward to canonical head'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('rev-list', '--count', "$initialHead..HEAD")) '3' 'Expected unrelated + business + handoff commits'
  $subjects = Invoke-Git -Root $mainRoot -Arguments @('log', '--format=%s', '--reverse', "$initialHead..HEAD")
  Assert-True ($subjects -match 'concurrent unrelated main change') 'Concurrent unrelated commit was lost'
  Assert-True ($subjects -match "feat\($taskId\): complete DeepSeek task") 'Business commit was not integrated'
  Assert-True ($subjects -match "handoff\($taskId\): register DeepSeek result") 'Handoff commit was not integrated'
  $cardAfter = [IO.File]::ReadAllText((Join-Path $mainRoot "开发管理/任务卡/$taskId.txt"))
  Assert-True ($cardAfter -match '"route": "codex_review"') 'Integrated task was not routed to review'
  Assert-True (([IO.File]::ReadAllText((Join-Path $mainRoot '开发管理/AI合作沟通.txt'))) -match '⚠️ 未审核') 'Integrated handoff was not recorded'
  Assert-True (Test-Path -LiteralPath (Join-Path $mainRoot 'fixtures/business.txt') -PathType Leaf) 'Business file was not integrated'
  $runtime = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action Show -StateRoot $stateRoot)
  $runtimeJson = $runtime[0] | ConvertFrom-Json -Depth 50
  Assert-True ($null -eq $runtimeJson.state.runs.deepseek) 'Completed run remained active'
  Assert-True ($null -eq $runtimeJson.state.integrationLease) 'Completed integration lease remained active'

  $none = Invoke-Hourly -Action RunOnce -UseStateRoot
  Assert-Equal ([string]$none.Json.status) 'no_candidate' 'Second run did not skip cleanly'

  $canary = Invoke-Hourly -Action Canary
  Assert-Equal $canary.ExitCode 0 "Canary process failed: $($canary.Stderr)"
  Assert-Equal ([string]$canary.Json.status) 'verified' "Canary did not verify: $($canary.Stdout)"
  Assert-Equal ([string]$canary.Json.model) 'deepseek-v4-flash' 'Canary model mismatch'
  Assert-Equal ([string]$canary.Json.privateState) 'isolated' 'Canary private state was not isolated'
  Assert-True (-not (Test-Path -LiteralPath ([string]$canary.Json.worktree))) 'Successful canary worktree was not cleaned'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Canary changed main workspace'

  Write-Output 'test-invoke-deepseek-hourly: OK'
} finally {
  $env:PATH = $originalPath; $env:ANTHROPIC_BASE_URL = $originalBaseUrl; $env:TZG_FAKE_CLAUDE_RECORD = $originalRecord; $env:TZG_FAKE_MAIN_ROOT = $originalMain
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($temporaryBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-deepseek-hourly-test-$testId") { throw "Unsafe hourly-test cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedState = [IO.Path]::GetFullPath($stateRoot)
    if (-not $resolvedState.StartsWith($approvedStateRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedState) -cne "tzg-deepseek-hourly-test-$testId") { throw "Unsafe hourly-state cleanup: $resolvedState" }
    Remove-Item -LiteralPath $resolvedState -Recurse -Force
  }
}
