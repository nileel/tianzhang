#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Start', 'Resume')]
  [string]$Action,
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$TaskId,
  [Parameter(Mandatory = $true)][string]$RunId,
  [Parameter(Mandatory = $true)][string]$BatchId,
  [Parameter(Mandatory = $true)][string]$LaneId,
  [Parameter(Mandatory = $true)][string]$ResultPath,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [string]$SessionId,
  [string]$DecisionReply,
  [ValidateRange(1, 86400)]
  [int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$corePath = Join-Path $PSScriptRoot 'automation-lane-core.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
. $corePath

function Stop-ExternalLane {
  param([string]$Code)
  $exception = [InvalidOperationException]::new($Code)
  $exception.Data['DetailCode'] = $Code
  throw $exception
}

function Get-ExternalLaneBaseUrl {
  $baseUrl = [string]$env:ANTHROPIC_BASE_URL
  if (-not [string]::IsNullOrWhiteSpace($baseUrl)) {
    return $baseUrl
  }
  $settingsPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.claude\settings.json'
  if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    return $null
  }
  try {
    $settings = [Text.UTF8Encoding]::new($false, $true).GetString(
      [IO.File]::ReadAllBytes($settingsPath)
    ) | ConvertFrom-Json -Depth 100
    [string]$settings.env.ANTHROPIC_BASE_URL
  } catch {
    Stop-ExternalLane 'external_lane_identity_unavailable'
  }
}

function Test-ExternalLaneDeepSeekEndpoint {
  param([string]$BaseUrl)

  $uri = $null
  [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri) -and
    $uri.Scheme -ceq 'http' -and
    $uri.Host -ceq '127.0.0.1' -and
    $uri.Port -eq 15721
}

function Resolve-ExternalLaneResultPath {
  param([string]$Path)

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    Stop-ExternalLane 'external_lane_result_path_invalid'
  }
  $fullPath = [IO.Path]::GetFullPath($Path)
  $root = [IO.Path]::GetFullPath($StateRoot).TrimEnd('\', '/')
  if (-not $fullPath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    Stop-ExternalLane 'external_lane_result_path_invalid'
  }
  $fullPath
}

function Invoke-ExternalLaneShow {
  $output = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action Show `
      -StateRoot $StateRoot 2>$null
  )
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
    Stop-ExternalLane 'external_lane_claim_unavailable'
  }
  $output[0] | ConvertFrom-Json -Depth 100
}

function New-ExternalLanePrompt {
  param([object]$Lane, [string]$Session)

  @(
    '[TZG_AUTOMATION_LANE_WORKER]'
    "BatchId: $BatchId"
    "LaneId: $LaneId"
    "TaskId: $TaskId"
    "RunId: $RunId"
    "Owner: $($Lane.owner)"
    "Identity: $($Lane.identity)"
    "SessionId: $Session"
    "RepositoryRoot: $script:resolvedRepositoryRoot"
    "BaseCommit: $($Lane.baseCommit)"
    "WorkerPaths: $(@($Lane.workerPaths) -join '|')"
    "CoordinatorPaths: $(@($Lane.coordinatorPaths) -join '|')"
    ''
    '身份确认：我是 DeepSeek V4 Flash（经 Claude CLI 调用 DeepSeek API），本轮负责执行型工作，不替 Codex 做审核结论。'
    '这是已认领任务的隔离自动 Worker。只在 RepositoryRoot 当前分支工作，不创建或切换分支/worktree，不重新扫描队列，也不处理另一任务。'
    '完整读取 AGENTS.md、开发管理/自动工作流规则.txt、开发管理/AI协作规则.txt、开发管理/DeepSeek工作提示词.txt、开发管理/当前任务队列.txt 和同一 TaskId 任务卡；再按任务卡读取直接事实源。'
    '候选提交只能包含 WorkerPaths。不得修改、stage 或提交 CoordinatorPaths；不得调用 hourly-automation-lease.ps1、RecordResult、Release、通知、推送、stash、reset、checkout、clean 或并行代理。'
    '完成业务修改与直接验证后，用 tools/automation-finalize-commit.ps1 创建一个不带 Automation 元数据的路径限定候选提交。该提交只是内部候选，不是正式业务提交。'
    '最终只返回 JSON schema 对象。completed 必须提供候选 SHA、实际路径、验证结果、正式元数据事实、transition=codex_review/codex/ready，以及 CoordinatorPaths 内任务卡、队列、backlog 与 AI 合作交接的完整 write/delete 内容。'
    'coordinatorChanges 只作为固定集成器提案，不得先写入 worktree。交接内容必须标记 DeepSeek V4 Flash 修改、待 Codex 独立复审，并包含候选交付的已验证、未验证和残留风险；不得自审。'
    '若需要负责人决定或不能在授权边界内继续，返回 needs_decision、blocked 或 failed 和稳定 detailCode，不增加兼容分支、默认值或重试。'
  ) -join "`n"
}

$terminal = $null
$capturedSessionId = if ($Action -ceq 'Resume') { $SessionId } else { $null }
$resolvedResultPath = $null

try {
  foreach ($path in @($corePath, $leasePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      Stop-ExternalLane 'external_lane_dependency_missing'
    }
  }
  $resolvedResultPath = Resolve-ExternalLaneResultPath -Path $ResultPath
  $script:resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  $gitRoot = (& git -C $script:resolvedRepositoryRoot rev-parse --show-toplevel 2>$null).Trim()
  if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/') -ine $script:resolvedRepositoryRoot) {
    Stop-ExternalLane 'external_lane_repository_invalid'
  }
  $shown = Invoke-ExternalLaneShow
  $batch = $shown.state.batch
  $laneMatches = @($batch.lanes | Where-Object {
    [string]$_.laneId -ceq $LaneId -and
    [string]$_.taskClaim.taskId -ceq $TaskId
  })
  if (
    [string]$shown.status -cne 'OK' -or
    [string]$shown.leaseStatus -cne 'active' -or
    $null -eq $batch -or
    [string]$batch.batchId -cne $BatchId -or
    [string]$batch.runId -cne $RunId -or
    $laneMatches.Count -ne 1
  ) {
    Stop-ExternalLane 'external_lane_claim_mismatch'
  }
  $lane = $laneMatches[0]
  if (
    [string]$lane.owner -cne 'deepseek' -or
    [string]$lane.identity -cne 'DeepSeek V4 Flash' -or
    [IO.Path]::GetFullPath([string]$lane.worktree).TrimEnd('\', '/') -ine $script:resolvedRepositoryRoot
  ) {
    Stop-ExternalLane 'external_lane_claim_mismatch'
  }
  $baseUrl = Get-ExternalLaneBaseUrl
  if (-not (Test-ExternalLaneDeepSeekEndpoint -BaseUrl $baseUrl)) {
    Stop-ExternalLane 'external_lane_identity_unavailable'
  }
  $claude = @(Get-Command 'claude.cmd' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)
  if ($claude.Count -ne 1) {
    Stop-ExternalLane 'external_lane_cli_unavailable'
  }
  $beforeHead = (& git -C $script:resolvedRepositoryRoot rev-parse HEAD).Trim()
  $beforeStatus = @(& git -C $script:resolvedRepositoryRoot status --porcelain --untracked-files=all)
  if ($LASTEXITCODE -ne 0 -or $beforeHead -cne [string]$lane.baseCommit -or $beforeStatus.Count -ne 0) {
    Stop-ExternalLane 'external_lane_worktree_not_clean'
  }
  if ($Action -ceq 'Resume' -and [string]::IsNullOrWhiteSpace($SessionId)) {
    Stop-ExternalLane 'external_lane_session_missing'
  }

  $resultDirectory = Split-Path -Parent $resolvedResultPath
  [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
  $sessionToUse = if ($Action -ceq 'Start') { [Guid]::NewGuid().ToString() } else { $SessionId }
  $capturedSessionId = $sessionToUse
  $inputText = if ($Action -ceq 'Start') {
    New-ExternalLanePrompt -Lane $lane -Session $sessionToUse
  } else {
    if ([string]::IsNullOrWhiteSpace($DecisionReply)) {
      Stop-ExternalLane 'external_lane_decision_reply_missing'
    }
    "[TZG_AUTOMATION_LANE_RESUME batchId=$BatchId laneId=$LaneId taskId=$TaskId]`n$DecisionReply"
  }
  $allowedTools = @(
    'Read'
    'Edit'
    'Write'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 *)'
    'Bash(git diff --check)'
  ) -join ','

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $claude[0].Source
  $startInfo.WorkingDirectory = $script:resolvedRepositoryRoot
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  if ($Action -ceq 'Start') {
    $startInfo.ArgumentList.Add('--session-id')
  } else {
    $startInfo.ArgumentList.Add('--resume')
  }
  $startInfo.ArgumentList.Add($sessionToUse)
  foreach ($argument in @(
      '--print',
      '--output-format', 'json',
      '--json-schema', (New-TzgLaneWorkerTerminalSchema),
      '--permission-mode', 'dontAsk',
      '--allowedTools', $allowedTools
    )) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    Stop-ExternalLane 'external_lane_cli_unavailable'
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($inputText + "`n")
  $process.StandardInput.Close()
  $timedOut = -not $process.WaitForExit([int]($ResponsibilityTimeoutSeconds * 1000))
  if ($timedOut) {
    try { $process.Kill($true) } catch { }
    $process.WaitForExit()
  }
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $null = $stderrTask.GetAwaiter().GetResult()
  $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
  $process.Dispose()
  if ($exitCode -ne 0) {
    Stop-ExternalLane $(if ($timedOut) { 'external_lane_timeout' } else { 'external_lane_cli_failed' })
  }
  try {
    $envelope = $stdout.Trim() | ConvertFrom-Json -Depth 100
  } catch {
    Stop-ExternalLane 'external_lane_terminal_invalid'
  }
  if (
    [string]$envelope.type -cne 'result' -or
    [string]$envelope.subtype -cne 'success' -or
    [bool]$envelope.is_error -or
    [string]$envelope.session_id -cne $sessionToUse -or
    $null -eq $envelope.structured_output
  ) {
    Stop-ExternalLane 'external_lane_terminal_invalid'
  }
  $terminal = $envelope.structured_output
  $terminal.sessionId = $capturedSessionId
  Assert-TzgLaneWorkerTerminal -Terminal $terminal -Lane $lane -BatchId $BatchId
  if ([string]$terminal.status -ceq 'completed') {
    if (
      [string]$terminal.transition.route -cne 'codex_review' -or
      [string]$terminal.transition.owner -cne 'codex' -or
      [string]$terminal.transition.dispatchState -cne 'ready'
    ) {
      Stop-ExternalLane 'external_lane_transition_invalid'
    }
    $null = Test-TzgCandidateCommit -RepositoryRoot $script:resolvedRepositoryRoot -Lane $lane -Terminal $terminal
    $afterHead = (& git -C $script:resolvedRepositoryRoot rev-parse HEAD).Trim()
    $afterStatus = @(& git -C $script:resolvedRepositoryRoot status --porcelain --untracked-files=all)
    if ($afterHead -cne [string]$terminal.candidateCommit -or $afterStatus.Count -ne 0) {
      Stop-ExternalLane 'external_lane_candidate_not_clean'
    }
  }
} catch {
  $detailCode = if ($_.Exception.Data.Contains('DetailCode')) {
    [string]$_.Exception.Data['DetailCode']
  } else {
    'external_lane_worker_error'
  }
  $terminal = [pscustomobject][ordered]@{
    status = 'failed'
    batchId = $BatchId
    laneId = $LaneId
    taskId = $TaskId
    identity = 'DeepSeek V4 Flash'
    sessionId = $capturedSessionId
    detailCode = $detailCode
  }
} finally {
  if ($null -ne $terminal -and -not [string]::IsNullOrWhiteSpace($resolvedResultPath)) {
    Write-TzgPrivateJson -Value $terminal -Path $resolvedResultPath
  }
}

[Console]::Out.WriteLine(($terminal | ConvertTo-Json -Compress -Depth 100))
exit $(if ([string]$terminal.status -cin @('completed', 'needs_decision', 'blocked')) { 0 } else { 1 })
