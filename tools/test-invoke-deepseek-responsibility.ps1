#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Invoke-Git { param([string]$Root, [string[]]$Arguments) $output = @(& git -C $Root @Arguments 2>&1); if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' '): $(@($output) -join "`n")" }; (@($output) -join "`n").Trim() }

function Invoke-Wrapper {
  param([string]$Action, [string]$Root, [string]$TaskId, [string]$RunId)
  $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapperPath, '-Action', $Action, '-RepositoryRoot', $Root, '-StateRoot', $stateRoot, '-ResponsibilityTimeoutSeconds', '30')
  if ($Action -ceq 'Candidate') { $arguments += @('-TaskId', $TaskId, '-RunId', $RunId) }
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'; $startInfo.WorkingDirectory = $Root; $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
  foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
  Assert-True $process.Start() 'Wrapper process did not start'
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult().Trim(); $stderr = $stderrTask.GetAwaiter().GetResult().Trim(); $exitCode = $process.ExitCode; $process.Dispose()
  [pscustomobject]@{ ExitCode = $exitCode; Json = $stdout | ConvertFrom-Json -Depth 50; Stderr = $stderr }
}

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $temporaryBase "tzg-deepseek-wrapper-test-$testId"
$mainRoot = Join-Path $testRoot 'repository'
$fakeBin = Join-Path $testRoot 'bin'
$recordPath = Join-Path $testRoot 'claude-record.json'
$approvedStateRoot = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedStateRoot "tzg-deepseek-wrapper-test-$testId"
$wrapperPath = Join-Path $PSScriptRoot 'invoke-deepseek-responsibility.ps1'
$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$originalPath = $env:PATH
$originalBaseUrl = $env:ANTHROPIC_BASE_URL
$originalRecord = $env:TZG_FAKE_CLAUDE_RECORD
$taskId = 'TASK-DEEPSEEK-CANDIDATE'

try {
  [IO.Directory]::CreateDirectory((Join-Path $mainRoot 'tools')) | Out-Null
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  foreach ($tool in @('automation-finalize-commit.ps1', 'automation-commit-metadata.ps1', 'check-pending-whitespace.ps1', 'check-task-cards.ps1', 'private-path-acl.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $mainRoot "tools/$tool")
  }
  Write-Utf8 -Path (Join-Path $mainRoot 'AGENTS.md') -Text '# wrapper fixture rules'
  Write-Utf8 -Path (Join-Path $mainRoot 'CLAUDE.md') -Text '# wrapper fixture Claude rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/自动工作流规则.txt') -Text '# workflow rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI协作规则.txt') -Text '# collaboration rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/DeepSeek工作提示词.txt') -Text '# DeepSeek prompt'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI合作沟通.txt') -Text '# communication'
  $metadata = [ordered]@{
    schemaVersion = 1; id = $taskId; title = 'Candidate fixture'; priority = 'P1'; route = 'external_execute'; owner = 'deepseek'
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @(); stateReason = 'fixture'
    expectedPaths = @('fixtures/business.txt', '开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt', "开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt")
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $taskId · Candidate fixture",
    '## 来源与当前边界', '- fixture', '## 必查范围', '- fixture', '## 实施范围', '- create fixtures/business.txt',
    '## 禁止项', '- no lifecycle changes', '## 验证', '- git diff --check', '## 完成条件', '- candidate commit', '## 停止条件', '- path violation'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $mainRoot "开发管理/任务卡/$taskId.txt") -Text $card
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/当前任务队列.txt') -Text (@(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | external_execute | deepseek | P1 | automation | implementation | Candidate fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/任务列表/自动化任务.txt') -Text (@(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | P1 | deepseek | 已排队 | — | Candidate fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Invoke-Git -Root $mainRoot -Arguments @('init') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.name', 'DeepSeek Wrapper Test') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.email', 'deepseek-wrapper@example.invalid') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('add', '--', '.') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('commit', '-m', 'test: initialize DeepSeek wrapper fixture') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('branch', '-M', 'master') | Out-Null
  $baseCommit = Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')
  $cardText = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes((Join-Path $mainRoot "开发管理/任务卡/$taskId.txt"))).TrimStart([char]0xFEFF)
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($cardText.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
  $claimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $stateRoot -Owner deepseek -TaskId $taskId -Route external_execute -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $baseCommit -TaskCardDigest $digest)
  Assert-Equal $LASTEXITCODE 0 'Fixture claim failed'
  $run = ($claimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$run.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$run.candidateBranch, [string]$run.worktree, $baseCommit) | Out-Null

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
  [IO.File]::WriteAllText((Join-Path ([Environment]::CurrentDirectory) 'fixtures/business.txt'), 'candidate', [Text.UTF8Encoding]::new($false))
  & git add -- fixtures/business.txt
  & git commit -q -m 'candidate(TASK-DEEPSEEK-CANDIDATE): DeepSeek implementation'
  if ($LASTEXITCODE -ne 0) { throw 'fake candidate commit failed' }
  $commit = [string](& git rev-parse HEAD)
  $terminal = [ordered]@{
    status = 'completed'; identity = 'DeepSeek V4 Flash'; model = 'deepseek-v4-flash'; candidateCommit = $commit
    expectedTransition = 'codex_review/codex/ready'; changedPaths = @('fixtures/business.txt'); verified = @('git diff --check passed')
    unverified = @('none'); residualRisk = 'fixture only'; result = '问题=缺少候选；完成=创建候选'
    impact = '影响=验证候选合同；边界=不修改生产任务'; verify = '验证=Git 检查通过；后续=等待固定入口集成'
    plain = '发生=测试生成了候选；影响=只验证自动化；需要=无需处理'
  }
}
[Console]::Out.WriteLine(([ordered]@{ type = 'result'; subtype = 'success'; is_error = $false; session_id = $sessionId; structured_output = $terminal } | ConvertTo-Json -Compress -Depth 20))
'@
  Write-Utf8 -Path (Join-Path $fakeBin 'claude.cmd') -Text "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-claude.ps1`" %*"
  $env:PATH = "$fakeBin;$originalPath"
  $env:ANTHROPIC_BASE_URL = 'http://127.0.0.1:15721/claude-desktop'
  $env:TZG_FAKE_CLAUDE_RECORD = $recordPath

  $candidate = Invoke-Wrapper -Action Candidate -Root ([string]$run.worktree) -TaskId $taskId -RunId ([string]$run.runId)
  Assert-Equal $candidate.ExitCode 0 "Candidate wrapper process failed: $($candidate.Stderr)"
  Assert-Equal ([string]$candidate.Json.status) 'completed' "Candidate wrapper status mismatch: $($candidate.Json | ConvertTo-Json -Compress -Depth 20); stderr=$($candidate.Stderr)"
  Assert-Equal ([string]$candidate.Json.identity) 'DeepSeek V4 Flash' 'Candidate identity mismatch'
  Assert-Equal ([string]$candidate.Json.model) 'deepseek-v4-flash' 'Candidate model mismatch'
  Assert-True ([string]$candidate.Json.candidateCommit -cmatch '^[0-9a-f]{40}$') 'Candidate SHA is invalid'
  Assert-Equal ([string]$candidate.Json.candidateResult.changedPaths[0]) 'fixtures/business.txt' 'Candidate changed paths mismatch'
  $record = Get-Content -Raw -LiteralPath $recordPath | ConvertFrom-Json -Depth 20
  $arguments = @($record.arguments | ForEach-Object { [string]$_ })
  Assert-Equal ([string]$arguments[[Array]::IndexOf($arguments, '--model') + 1]) 'deepseek-v4-flash' 'Wrapper did not pin the model'
  Assert-True ([string]$record.prompt -match 'fixed Windows entry already selected') 'Candidate prompt omitted fixed-entry boundary'
  Assert-True ([string]$record.prompt -match 'Do not modify the task card') 'Candidate prompt omitted lifecycle exclusion'
  Assert-True ([string]$record.prompt -match 'result="问题=\.\.\.；完成=\.\.\."') 'Candidate prompt omitted the exact finalizer metadata form'
  $allowedTools = [string]$arguments[[Array]::IndexOf($arguments, '--allowedTools') + 1]
  Assert-True (-not $allowedTools.Contains('Bash(*)', [StringComparison]::Ordinal)) 'Wrapper allowed wildcard Bash'

  $canary = Invoke-Wrapper -Action Canary -Root $mainRoot -TaskId '' -RunId ''
  Assert-Equal ([string]$canary.Json.status) 'verified' 'Canary did not verify'
  Assert-Equal ([string]$canary.Json.providerEndpointCategory) 'local_deepseek_gateway' 'Canary endpoint category mismatch'
  Assert-Equal ([int]$canary.Json.pwshMajor) 7 'Canary did not verify PowerShell 7'

  $env:ANTHROPIC_BASE_URL = 'https://api.anthropic.com'
  $identityFailure = Invoke-Wrapper -Action Canary -Root $mainRoot -TaskId '' -RunId ''
  Assert-Equal ([string]$identityFailure.Json.status) 'failed' 'Wrong endpoint did not fail'
  Assert-Equal ([string]$identityFailure.Json.detailCode) 'deepseek_identity_unavailable' 'Wrong endpoint failure code mismatch'

  Write-Output 'test-invoke-deepseek-responsibility: OK'
} finally {
  $env:PATH = $originalPath; $env:ANTHROPIC_BASE_URL = $originalBaseUrl; $env:TZG_FAKE_CLAUDE_RECORD = $originalRecord
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($temporaryBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-deepseek-wrapper-test-$testId") { throw "Unsafe wrapper-test cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedState = [IO.Path]::GetFullPath($stateRoot)
    if (-not $resolvedState.StartsWith($approvedStateRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedState) -cne "tzg-deepseek-wrapper-test-$testId") { throw "Unsafe wrapper-state cleanup: $resolvedState" }
    Remove-Item -LiteralPath $resolvedState -Recurse -Force
  }
}
