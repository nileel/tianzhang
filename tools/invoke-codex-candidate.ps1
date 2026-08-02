#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidateSet('Execution', 'Review', 'QueueMaintenance')][string]$Route,
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$TaskId,
  [Parameter(Mandatory = $true)][string]$RunId,
  [Parameter(Mandatory = $true)][string]$Model,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [ValidateRange(1, 86400)][int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$runnerPath = Join-Path $PSScriptRoot 'codex-cli-session.ps1'
$checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
$metadataPath = Join-Path $PSScriptRoot 'automation-commit-metadata.ps1'
. $metadataPath

function Stop-Candidate { param([string]$DetailCode) $e = [InvalidOperationException]::new($DetailCode); $e.Data['DetailCode'] = $DetailCode; throw $e }
function Normalize-FullPath { param([string]$Path) [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }

function Invoke-GitText {
  param([string[]]$Arguments, [string]$DetailCode = 'codex_git_failed')
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'; $startInfo.WorkingDirectory = $script:root; $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false); $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @('-C', $script:root) + $Arguments) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
  if (-not $process.Start()) { Stop-Candidate $DetailCode }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult(); $null = $stderrTask.GetAwaiter().GetResult(); $exitCode = $process.ExitCode; $process.Dispose()
  if ($exitCode -ne 0) { Stop-Candidate $DetailCode }
  $stdout.TrimEnd()
}

function Invoke-JsonTool {
  param([string]$Path, [string[]]$Arguments, [string]$DetailCode)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>$null)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { Stop-Candidate $DetailCode }
  try { $output[0] | ConvertFrom-Json -Depth 100 } catch { Stop-Candidate $DetailCode }
}

function Get-NormalizedTextDigest {
  param([string]$Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n"))
  )).ToLowerInvariant()
}

function Read-TaskMetadata {
  $path = Join-Path $script:root "开发管理/任务卡/$TaskId.txt"
  $bytes = [IO.File]::ReadAllBytes($path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
  $match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
  if (-not $match.Success) { Stop-Candidate 'codex_task_invalid' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-Candidate 'codex_task_invalid' }
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n"))
  )).ToLowerInvariant()
  [pscustomobject]@{ Metadata = $metadata; Digest = $digest }
}

function New-Prompt {
  param([object]$Run)
  $routeInstruction = switch ($Route) {
    'Execution' { '按纯 1 执行指定 codex_execute 任务。' }
    'Review' { '按审核入口与纯 2 复审指定 codex_review 任务。' }
    'QueueMaintenance' { '按状态与建议维护规则只做空队列维护；本轮不执行新增业务任务。' }
  }
  @(
    '[TZG_CODEX_CANDIDATE]'
    "模型核验证明：外层 automation 已核验并通过 -Model 传入 $Model；子会话不因缺少父 request metadata 再次阻塞。"
    "TaskId: $TaskId"
    "RunId: $RunId"
    "Route: $Route"
    "RepositoryRoot: $script:root"
    "CandidateBranch: $($Run.candidateBranch)"
    $routeInstruction
    '先完整读取 AGENTS.md、开发管理/自动工作流规则.txt、开发管理/AI协作规则.txt 和对应业务入口。'
    '固定入口已经选择并 claim 本任务。只处理给定 TaskId；不得重新扫描或选择另一任务，不得调用 runtime。'
    '只在给定 automation worktree 和当前 candidate branch 实施、验证与提交；不得创建、切换或删除 worktree/branch，不得修改主工作区。'
    '端到端完成任务生命周期投影，并且只创建一个路径限定 candidate 提交。该提交仍使用 automation-finalize-commit.ps1 -RequireAutomationMetadata、AutomationState=completed 与现有九字段合同。'
    'candidate 暂不进入 master；固定入口会在最新 master 上核验并生成 canonical。不得自行 fast-forward、merge、push、stash、reset、checkout 或 clean。'
    '不得调用 RecordResult、ReleaseIntegration、CompleteRun 或管理 automation。需要决定、存在路径外修改或无法形成可核验提交时保留现场并如实结束。'
  ) -join "`n"
}

function Invoke-Runner {
  param([string]$Prompt)
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'; $startInfo.WorkingDirectory = $script:root; $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true; $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
  $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerPath, '-Action', 'Start', '-RepositoryRoot', $script:root, '-TaskId', $TaskId, '-RunId', $RunId, '-Model', $Model)) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
  if (-not $process.Start()) { Stop-Candidate 'codex_runner_unavailable' }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($Prompt); $process.StandardInput.Close()
  $timedOut = -not $process.WaitForExit([int]($ResponsibilityTimeoutSeconds * 1000))
  if ($timedOut) { try { $process.Kill($true) } catch [InvalidOperationException] { if (-not $process.HasExited) { throw } }; $process.WaitForExit() }
  $stdout = $stdoutTask.GetAwaiter().GetResult(); $null = $stderrTask.GetAwaiter().GetResult(); $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }; $process.Dispose()
  if ($timedOut) { Stop-Candidate 'codex_responsibility_timeout' }
  $lines = @($stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($exitCode -ne 0 -or $lines.Count -ne 1) { Stop-Candidate 'codex_runner_failed' }
  try { $lines[0] | ConvertFrom-Json -Depth 20 } catch { Stop-Candidate 'codex_runner_failed' }
}

function Get-ChangedPaths {
  param([string]$Range)
  @((Invoke-GitText @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', $Range)) -split '\r?\n' |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
}

function Test-QueueMaintenancePath {
  param([string]$Path)
  $Path -match '^开发管理/(?:当前任务队列\.txt|任务卡/[^/]+\.txt|任务列表/[^/]+\.txt|设计-当前状态\.txt|设计-下一步建议\.txt|开发-下一步建议\.txt|自动工作流状态\.txt)$'
}

$result = $null
try {
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { Stop-Candidate 'codex_repository_invalid' }
  $script:root = Normalize-FullPath (Resolve-Path -LiteralPath $RepositoryRoot).Path
  if (-not (Test-Path -LiteralPath (Join-Path $script:root '.git'))) { Stop-Candidate 'codex_repository_invalid' }
  foreach ($value in @($TaskId, $RunId, $Model)) { if ([string]::IsNullOrWhiteSpace($value) -or $value -match '[\x00-\x1F\x7F]') { Stop-Candidate 'codex_arguments_invalid' } }
  $shown = Invoke-JsonTool -Path $runtimePath -Arguments @('-Action', 'Show', '-StateRoot', $StateRoot) -DetailCode 'codex_runtime_mismatch'
  $run = $shown.state.runs.codex
  if (
    [string]$shown.status -cne 'OK' -or $null -eq $run -or [string]$run.runId -cne $RunId -or [string]$run.taskId -cne $TaskId -or
    [string]$run.state -cne 'developing' -or (Normalize-FullPath ([string]$run.worktree)) -cne $script:root
  ) { Stop-Candidate 'codex_runtime_mismatch' }
  $expectedRoute = switch ($Route) { 'Execution' { 'codex_execute' }; 'Review' { 'codex_review' }; 'QueueMaintenance' { 'queue_maintenance' } }
  if ([string]$run.route -cne $expectedRoute) { Stop-Candidate 'codex_route_mismatch' }
  if ((Invoke-GitText @('branch', '--show-current')) -cne [string]$run.candidateBranch -or (Invoke-GitText @('rev-parse', 'HEAD')) -cne [string]$run.baseCommit) { Stop-Candidate 'codex_worktree_mismatch' }
  if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Candidate 'codex_worktree_dirty' }

  $expectedPaths = @()
  if ($Route -cne 'QueueMaintenance') {
    $task = Read-TaskMetadata
    if ([string]$task.Digest -cne [string]$run.taskCardDigest) { Stop-Candidate 'codex_task_changed' }
    $metadata = $task.Metadata
    if ([string]$metadata.id -cne $TaskId -or [string]$metadata.route -cne $expectedRoute -or [string]$metadata.owner -cne 'codex' -or [string]$metadata.dispatchState -cne 'ready') { Stop-Candidate 'codex_task_not_ready' }
    $expectedPaths = @($metadata.expectedPaths | ForEach-Object { [string]$_ })
    $postcondition = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $script:root, '-TaskId', $TaskId, '-Postcondition', 'CodexDispatchReady', '-ExpectedRoute', $expectedRoute, '-OutputJson') -DetailCode 'codex_task_not_ready'
    if ([string]$postcondition.status -cne 'ok') { Stop-Candidate 'codex_task_not_ready' }
  } else {
    $queuePath = Join-Path $script:root '开发管理\当前任务队列.txt'
    if ((Get-NormalizedTextDigest -Path $queuePath) -cne [string]$run.taskCardDigest) { Stop-Candidate 'codex_queue_changed' }
  }

  $beforeHead = [string]$run.baseCommit
  $runner = Invoke-Runner -Prompt (New-Prompt -Run $run)
  $sessionId = [string]$runner.sessionId
  if ([string]$runner.status -cne 'ok' -or [string]::IsNullOrWhiteSpace($sessionId)) { Stop-Candidate 'codex_runner_failed' }
  $afterHead = Invoke-GitText @('rev-parse', 'HEAD')
  $status = Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')
  if ($afterHead -ceq $beforeHead) {
    if ($Route -ceq 'QueueMaintenance' -and [string]::IsNullOrWhiteSpace($status)) {
      $result = [ordered]@{ status = 'no_candidate'; taskId = $TaskId; runId = $RunId; sessionId = $sessionId; detailCode = 'no_runnable_candidate' }
    } else {
      $result = [ordered]@{ status = 'failed'; taskId = $TaskId; runId = $RunId; sessionId = $sessionId; detailCode = if ([string]::IsNullOrWhiteSpace($status)) { 'codex_candidate_missing' } else { 'codex_uncommitted_changes' } }
    }
  } else {
    if (-not [string]::IsNullOrWhiteSpace($status)) { Stop-Candidate 'codex_worktree_dirty' }
    if ((Invoke-GitText @('rev-list', '--count', "$beforeHead..$afterHead")) -cne '1' -or (Invoke-GitText @('rev-parse', "$afterHead^")) -cne $beforeHead) { Stop-Candidate 'codex_candidate_invalid' }
    try {
      $body = Invoke-GitText @('show', '-s', '--format=%B', $afterHead)
      $commitMetadata = ConvertFrom-TzgAutomationCommitMessage -Message $body -ExpectedTask $TaskId -ExpectedState 'completed'
    } catch { Stop-Candidate 'codex_candidate_metadata_invalid' }
    $changedPaths = Get-ChangedPaths -Range "$beforeHead..$afterHead"
    if ($changedPaths.Count -eq 0) { Stop-Candidate 'codex_candidate_empty' }
    if ($Route -cne 'QueueMaintenance') {
      foreach ($path in $changedPaths) { if ($expectedPaths -cnotcontains $path) { Stop-Candidate 'codex_candidate_path_violation' } }
      $closed = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $script:root, '-TaskId', $TaskId, '-Postcondition', 'CodexClosedOrNonReady', '-OutputJson') -DetailCode 'codex_candidate_postcondition_failed'
      $expectedTransition = [string]$closed.taskState
    } else {
      foreach ($path in $changedPaths) { if (-not (Test-QueueMaintenancePath -Path $path)) { Stop-Candidate 'codex_candidate_path_violation' } }
      $maintenance = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $script:root, '-OutputJson') -DetailCode 'codex_candidate_postcondition_failed'
      $expectedTransition = "queue_ready_count=$([int]$maintenance.readyCount)"
    }
    $candidateResult = [ordered]@{
      category = 'completed'; expectedTransition = $expectedTransition; changedPaths = $changedPaths
      verified = @([string]$commitMetadata.Verification); unverified = @([string]$commitMetadata.Next)
      residualRisk = [string]$commitMetadata.Next; result = [string]$commitMetadata.ResultText
      impact = [string]$commitMetadata.ImpactText; verify = [string]$commitMetadata.VerifyText; plain = [string]$commitMetadata.PlainText
    }
    $result = [ordered]@{ status = 'completed'; taskId = $TaskId; runId = $RunId; sessionId = $sessionId; candidateCommit = $afterHead; candidateResult = $candidateResult }
  }
} catch {
  $detailCode = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'codex_candidate_wrapper_error' }
  $result = [ordered]@{ status = 'failed'; taskId = $TaskId; runId = $RunId; detailCode = $detailCode }
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 30))
