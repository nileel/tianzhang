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
  [string]$Model,
  [string]$DecisionReply,
  [ValidateRange(1, 86400)]
  [int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$corePath = Join-Path $PSScriptRoot 'automation-lane-core.ps1'
$runnerPath = Join-Path $PSScriptRoot 'codex-cli-session.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
. $corePath

function Stop-CodexLane {
  param([string]$Code)
  $exception = [InvalidOperationException]::new($Code)
  $exception.Data['DetailCode'] = $Code
  throw $exception
}

function Resolve-PrivateResultPath {
  param([string]$Path, [string]$PrivateRoot)

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    Stop-CodexLane 'codex_lane_result_path_invalid'
  }
  $fullPath = [IO.Path]::GetFullPath($Path)
  $fullRoot = [IO.Path]::GetFullPath($PrivateRoot).TrimEnd('\', '/')
  if (-not $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    Stop-CodexLane 'codex_lane_result_path_invalid'
  }
  $fullPath
}

function Invoke-LeaseShow {
  $output = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action Show `
      -StateRoot $StateRoot 2>$null
  )
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
    Stop-CodexLane 'codex_lane_claim_unavailable'
  }
  $output[0] | ConvertFrom-Json -Depth 100
}

function New-CodexLanePrompt {
  param([object]$Lane)

  $workerPaths = @($Lane.workerPaths) -join '|'
  $coordinatorPaths = @($Lane.coordinatorPaths) -join '|'
  @(
    '[TZG_AUTOMATION_LANE_WORKER]'
    "BatchId: $BatchId"
    "LaneId: $LaneId"
    "TaskId: $TaskId"
    "RunId: $RunId"
    "Identity: $($Lane.identity)"
    "RepositoryRoot: $script:resolvedRepositoryRoot"
    "BaseCommit: $($Lane.baseCommit)"
    "WorkerPaths: $workerPaths"
    "CoordinatorPaths: $coordinatorPaths"
    ''
    "模型核验证明：协调器已核验并以 -Model 传入 $Model；子会话不因缺少父 request metadata 再次阻塞。"
    '这是已认领任务的隔离自动 Worker。只在 RepositoryRoot 当前分支工作，不创建或切换分支/worktree，不读取或处理另一任务。'
    '完整读取 AGENTS.md、开发管理/自动工作流规则.txt、开发管理/AI协作规则.txt、开发管理/当前任务队列.txt 和同一 TaskId 任务卡；再按任务卡路由读取直接事实源。'
    '候选提交只能包含 WorkerPaths。不得修改、stage 或提交 CoordinatorPaths；不得调用 hourly-automation-lease.ps1、RecordResult、Release、通知、推送、stash、reset、checkout 或 clean。'
    '完成业务修改与任务直接验证后，用 tools/automation-finalize-commit.ps1 创建一个不带 Automation 元数据的路径限定候选提交。候选提交不是正式交付。'
    '最终响应必须严格匹配输出 schema。status=completed 时填完整 candidateCommit、实际 changedPaths、逐项 validationResults、九个正式提交事实字段、期望 transition，以及 CoordinatorPaths 内每个机械管理变更的完整 write/delete 内容。'
    'coordinatorChanges 只是交给固定集成器的提案，不得先写入 worktree。Codex 复审仍须独立读取审核入口和匹配证据；DeepSeek 成果不得被视为自审通过。'
    'sessionId 填 null；wrapper 会用 CLI 的真实 session 覆盖。若需要负责人决定或不能在边界内继续，返回 needs_decision、blocked 或 failed，并提供稳定 detailCode，不创建猜测性补丁。'
  ) -join "`n"
}

$terminal = $null
$capturedSessionId = if ($Action -ceq 'Resume') { $SessionId } else { $null }
$resolvedResultPath = $null

try {
  foreach ($path in @($corePath, $runnerPath, $leasePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      Stop-CodexLane 'codex_lane_dependency_missing'
    }
  }
  $resolvedResultPath = Resolve-PrivateResultPath -Path $ResultPath -PrivateRoot $StateRoot
  $script:resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  $gitRoot = (& git -C $script:resolvedRepositoryRoot rev-parse --show-toplevel 2>$null).Trim()
  if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/') -ine $script:resolvedRepositoryRoot) {
    Stop-CodexLane 'codex_lane_repository_invalid'
  }

  $shown = Invoke-LeaseShow
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
    Stop-CodexLane 'codex_lane_claim_mismatch'
  }
  $lane = $laneMatches[0]
  if (
    [string]$lane.owner -cne 'codex' -or
    [string]$lane.identity -cne 'Codex' -or
    [IO.Path]::GetFullPath([string]$lane.worktree).TrimEnd('\', '/') -ine $script:resolvedRepositoryRoot
  ) {
    Stop-CodexLane 'codex_lane_claim_mismatch'
  }
  $beforeHead = (& git -C $script:resolvedRepositoryRoot rev-parse HEAD).Trim()
  $beforeStatus = @(& git -C $script:resolvedRepositoryRoot status --porcelain --untracked-files=all)
  if ($LASTEXITCODE -ne 0 -or $beforeHead -cne [string]$lane.baseCommit -or $beforeStatus.Count -ne 0) {
    Stop-CodexLane 'codex_lane_worktree_not_clean'
  }
  if ($Action -ceq 'Start' -and [string]::IsNullOrWhiteSpace($Model)) {
    Stop-CodexLane 'codex_lane_model_missing'
  }
  if ($Action -ceq 'Resume' -and [string]::IsNullOrWhiteSpace($SessionId)) {
    Stop-CodexLane 'codex_lane_session_missing'
  }

  $resultDirectory = Split-Path -Parent $resolvedResultPath
  [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
  $schemaPath = Join-Path $resultDirectory "$LaneId-output-schema.json"
  $lastMessagePath = Join-Path $resultDirectory "$LaneId-last-message.json"
  [IO.File]::WriteAllText($schemaPath, (New-TzgLaneWorkerTerminalSchema), [Text.UTF8Encoding]::new($false))
  $prompt = if ($Action -ceq 'Start') {
    New-CodexLanePrompt -Lane $lane
  } else {
    if ([string]::IsNullOrWhiteSpace($DecisionReply)) {
      Stop-CodexLane 'codex_lane_decision_reply_missing'
    }
    "[TZG_AUTOMATION_LANE_RESUME batchId=$BatchId laneId=$LaneId taskId=$TaskId]`n$DecisionReply"
  }

  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerPath,
    '-Action', $Action,
    '-RepositoryRoot', $script:resolvedRepositoryRoot,
    '-TaskId', $TaskId,
    '-RunId', $RunId,
    '-OutputSchemaPath', $schemaPath,
    '-OutputLastMessagePath', $lastMessagePath
  )
  if ($Action -ceq 'Start') {
    $arguments += @('-Model', $Model)
  } else {
    $arguments += @('-SessionId', $SessionId)
  }
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.WorkingDirectory = $script:resolvedRepositoryRoot
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in $arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    Stop-CodexLane 'codex_lane_runner_unavailable'
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($prompt + "`n")
  $process.StandardInput.Close()
  $timedOut = -not $process.WaitForExit([int]($ResponsibilityTimeoutSeconds * 1000))
  if ($timedOut) {
    try { $process.Kill($true) } catch { }
    $process.WaitForExit()
  }
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
  $process.Dispose()
  $sessionLines = @(
    $stderr -split '\r?\n' |
      Where-Object { $_.StartsWith('codex_session_id=', [StringComparison]::Ordinal) } |
      ForEach-Object { $_.Substring('codex_session_id='.Length) }
  )
  if ($sessionLines.Count -eq 1) {
    $capturedSessionId = [string]$sessionLines[0]
  }
  if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $lastMessagePath -PathType Leaf)) {
    Stop-CodexLane $(if ($timedOut) { 'codex_lane_timeout' } else { 'codex_lane_runner_failed' })
  }
  $terminal = Read-TzgPrivateJson -Path $lastMessagePath
  $terminal.sessionId = $capturedSessionId
  Assert-TzgLaneWorkerTerminal -Terminal $terminal -Lane $lane -BatchId $BatchId
  if ([string]$terminal.status -ceq 'completed') {
    $null = Test-TzgCandidateCommit -RepositoryRoot $script:resolvedRepositoryRoot -Lane $lane -Terminal $terminal
    $afterHead = (& git -C $script:resolvedRepositoryRoot rev-parse HEAD).Trim()
    $afterStatus = @(& git -C $script:resolvedRepositoryRoot status --porcelain --untracked-files=all)
    if (
      $afterHead -cne [string]$terminal.candidateCommit -or
      $afterStatus.Count -ne 0
    ) {
      Stop-CodexLane 'codex_lane_candidate_not_clean'
    }
  }
} catch {
  $detailCode = if ($_.Exception.Data.Contains('DetailCode')) {
    [string]$_.Exception.Data['DetailCode']
  } else {
    'codex_lane_worker_error'
  }
  $terminal = [pscustomobject][ordered]@{
    status = 'failed'
    batchId = $BatchId
    laneId = $LaneId
    taskId = $TaskId
    identity = 'Codex'
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
