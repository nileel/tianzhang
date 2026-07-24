#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Start', 'Resume')]
  [string]$Action,
  [Parameter(Mandatory = $true)]
  [ValidateSet('Execution', 'Review', 'QueueMaintenance', 'Recovery')]
  [string]$Route,
  [Parameter(Mandatory = $true)]
  [string]$RepositoryRoot,
  [Parameter(Mandatory = $true)]
  [string]$TaskId,
  [Parameter(Mandatory = $true)]
  [string]$RunId,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [string]$SessionId,
  [string]$Model,
  [string]$DecisionId,
  [ValidateSet('A', 'B', 'C')]
  [string]$DecisionOption,
  [switch]$ReadDecisionReplyFromStdin
)

$ErrorActionPreference = 'Stop'
$runnerPath = Join-Path $PSScriptRoot 'codex-cli-session.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$taskCardCheckerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
$result = $null
$resultExitCode = 1
$capturedSessionId = $null
$verifiedCommitSha = $null
$decisionReply = $null

function Assert-StableArgument {
  param([AllowNull()][string]$Value, [string]$Name, [int]$MaximumLength = 512)

  if ([string]::IsNullOrWhiteSpace($Value)) {
    throw "$Name is required"
  }
  if ($Value.Length -gt $MaximumLength -or $Value -match '[\x00-\x1F\x7F]') {
    throw "$Name is invalid"
  }
}

function Invoke-GitText {
  param([string[]]$Arguments)

  $output = & git -C $script:resolvedRepositoryRoot @Arguments 2>$null
  if ($LASTEXITCODE -ne 0) {
    throw "Git command failed: git $($Arguments -join ' ')"
  }
  (@($output) -join "`n").TrimEnd()
}

function Get-WorkspaceSnapshot {
  $snapshot = @{}
  $statusText = Invoke-GitText -Arguments @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')
  $lines = @($statusText -split '\r?\n')
  foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) {
      continue
    }
    $status = $line.Substring(0, 2)
    $path = $line.Substring(3)
    $arrowIndex = $path.LastIndexOf(' -> ', [StringComparison]::Ordinal)
    if ($arrowIndex -ge 0) {
      $path = $path.Substring($arrowIndex + 4)
    }
    $normalized = $path.Replace('\', '/')
    $fullPath = Join-Path $script:resolvedRepositoryRoot $normalized
    $contentHash = if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
      [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($fullPath)))
    } else {
      '<missing>'
    }
    $snapshot[$normalized] = "$status|$contentHash"
  }
  $snapshot
}

function Get-NewChangedPaths {
  param([hashtable]$Before, [hashtable]$After)

  $changed = [Collections.Generic.List[string]]::new()
  $allPaths = @($Before.Keys) + @($After.Keys) | Sort-Object -Unique
  foreach ($path in $allPaths) {
    if (
      -not $Before.ContainsKey($path) -or
      -not $After.ContainsKey($path) -or
      [string]$Before[$path] -cne [string]$After[$path]
    ) {
      $changed.Add([string]$path)
    }
  }
  @($changed)
}

function Invoke-LeaseAction {
  param([string]$LeaseAction, [hashtable]$Parameters = @{}, [int[]]$AllowedExitCodes = @(0))

  $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $leasePath, '-Action', $LeaseAction)
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) {
    if ($entry.Value -is [bool]) {
      if ($entry.Value) { $arguments += "-$($entry.Key)" }
      continue
    }
    $arguments += "-$($entry.Key)"
    $arguments += if ($entry.Value -is [Collections.IEnumerable] -and $entry.Value -isnot [string]) {
      @($entry.Value) -join '|'
    } else {
      [string]$entry.Value
    }
  }
  $output = & pwsh @arguments
  $exitCode = $LASTEXITCODE
  if ($exitCode -notin $AllowedExitCodes) {
    throw "Lease action $LeaseAction failed with exit code $exitCode"
  }
  $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  if ($lines.Count -ne 1) {
    throw "Lease action $LeaseAction emitted an invalid response"
  }
  $lines[0] | ConvertFrom-Json -Depth 100
}

function Get-RouteInstruction {
  switch ($Route) {
    'Execution' {
      '按 开发管理/AI协作规则.txt 的纯 1 入口执行，但只处理本次指定 TaskId 和其独立任务卡。'
    }
    'Review' {
      '按 开发管理/审核入口.txt 与纯 2 入口复审，但只处理本次指定 TaskId 和其独立任务卡。'
    }
    'QueueMaintenance' {
      '按 开发管理/状态与建议维护规则.txt 维护空队列或状态事件；本轮不执行新增业务任务。'
    }
    'Recovery' {
      if (-not [string]::IsNullOrWhiteSpace($DecisionId)) {
        '读取 开发管理/自动工作流恢复规则.txt；这是带决定回复的新责任方会话，处理同一 TaskId，先核对 durable recovery 与决定，再继续工作。'
      } else {
        '读取 开发管理/自动工作流恢复规则.txt；这是中断恢复，恢复原责任方的同一 TaskId，先核对现有改动与 recovery，再继续未完成工作。'
      }
    }
  }
}

function New-ResponsibilityPrompt {
  $actionInstruction = if ($Action -ceq 'Resume') {
    '这是原 CLI session 的续跑，不创建新责任方。'
  } else {
    '这是新的 CLI-native 责任方会话。'
  }
  $lines = @()
  if (-not [string]::IsNullOrWhiteSpace($DecisionId)) {
    $lines += "[TZG_DECISION_REPLY runId=$RunId]"
    $lines += $script:decisionReply
  }
  $lines += @(
    "模型核验证明：控制器已核验并以 -Model 传入 $Model；子会话不因缺少父 request metadata 再次阻塞。"
    "TaskId: $TaskId"
    "RunId: $RunId"
    "Route: $Route"
    "RepositoryRoot: $script:resolvedRepositoryRoot"
    $actionInstruction
    (Get-RouteInstruction)
    '先完整读取仓库 AGENTS.md、开发管理/自动工作流规则.txt 和上述入口。'
    '本自动化责任方由单写入租约隔离，必须直接在上述 RepositoryRoot 的当前分支工作；不得创建或切换 linked worktree、任务分支，不得调用 using-git-worktrees 或 git worktree add。'
    '责任方端到端实施、最小充分验证并使用 automation-finalize-commit.ps1 创建路径限定提交。'
    'Execution、Review、QueueMaintenance 责任方仅在实际到达新的用户决定事件时，才读取 开发管理/自动工作流恢复规则.txt 的“创建决定恢复”一节；未到达决定事件时不得读取该文件。'
    '不得自行调用 RecordResult 或 Release；固定调用器会根据 Git 与 runtime 核验结果后统一关闭本轮。'
  )
  $lines -join "`n"
}

function Invoke-SessionRunner {
  param([string]$Prompt)

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerPath,
      '-Action', $Action,
      '-RepositoryRoot', $script:resolvedRepositoryRoot,
      '-TaskId', $TaskId,
      '-RunId', $RunId
    )) {
    $startInfo.ArgumentList.Add($argument)
  }
  if ($Action -ceq 'Resume') {
    $startInfo.ArgumentList.Add('-SessionId')
    $startInfo.ArgumentList.Add($SessionId)
  } elseif (-not [string]::IsNullOrWhiteSpace($Model)) {
    $startInfo.ArgumentList.Add('-Model')
    $startInfo.ArgumentList.Add($Model)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw 'Failed to start codex CLI session runner'
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($Prompt)
  $process.StandardInput.Close()
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()

  foreach ($line in @($stderr -split '\r?\n')) {
    if ($line -cin @('session_started', 'running')) {
      [Console]::Error.WriteLine($line)
    }
  }
  $lines = @($stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($lines.Count -ne 1) {
    throw 'CLI session runner emitted an invalid summary'
  }
  $summary = $lines[0] | ConvertFrom-Json
  [pscustomobject]@{ ExitCode = $exitCode; Summary = $summary }
}

function Get-CommitMetadata {
  param([string]$CommitSha)

  $body = Invoke-GitText -Arguments @('show', '-s', '--format=%B', $CommitSha)
  $fields = [ordered]@{}
  foreach ($name in @('Automation', 'Task', 'State', 'Result', 'Impact', 'Verify')) {
    $matches = [regex]::Matches($body, "(?m)^$([regex]::Escape($name)):\s*(?<value>.+?)\s*$")
    if ($matches.Count -ne 1) {
      return $null
    }
    $fields[$name] = $matches[0].Groups['value'].Value
  }
  [pscustomobject]$fields
}

function Test-TaskCardCloseout {
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $taskCardCheckerPath,
    '-RepositoryRoot', $script:resolvedRepositoryRoot
  )
  if ($Route -cin @('Execution', 'Review')) {
    $arguments += @('-TaskId', $TaskId, '-Postcondition', 'CodexClosedOrNonReady')
  }
  $null = & pwsh @arguments 2>&1
  $LASTEXITCODE -eq 0
}

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

try {
  foreach ($path in @($runnerPath, $leasePath, $taskCardCheckerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      throw "Required tool is missing: $path"
    }
  }
  Assert-StableArgument -Value $TaskId -Name 'TaskId'
  Assert-StableArgument -Value $RunId -Name 'RunId'
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) {
    throw 'RepositoryRoot must be absolute'
  }
  $script:resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
  if (-not (Test-Path -LiteralPath (Join-Path $script:resolvedRepositoryRoot '.git'))) {
    throw 'RepositoryRoot must be a Git root'
  }
  if ($Action -ceq 'Resume') {
    Assert-StableArgument -Value $SessionId -Name 'SessionId'
  } else {
    Assert-StableArgument -Value $Model -Name 'Model'
    if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
      throw 'SessionId is not valid for a new session'
    }
  }
  $hasDecisionId = -not [string]::IsNullOrWhiteSpace($DecisionId)
  $hasDecisionOption = -not [string]::IsNullOrWhiteSpace($DecisionOption)
  if ($hasDecisionOption -and $ReadDecisionReplyFromStdin) {
    throw 'DecisionOption and ReadDecisionReplyFromStdin are mutually exclusive'
  }
  $hasDecisionReply = $hasDecisionOption -or [bool]$ReadDecisionReplyFromStdin
  if ($hasDecisionId -ne $hasDecisionReply) {
    throw 'DecisionId and one decision reply source must be provided together'
  }
  if ($hasDecisionId) {
    if ($Action -cne 'Start' -or $Route -cne 'Recovery') {
      throw 'Decision reply is only valid for a fresh recovery session'
    }
    Assert-StableArgument -Value $DecisionId -Name 'DecisionId'
    $resumeState = Invoke-LeaseAction -LeaseAction Show -Parameters @{ StateRoot = $StateRoot }
    $resumeRecovery = $resumeState.state.recovery
    $resumeLease = $resumeState.state.lease
    $validDecisionResume =
      $null -ne $resumeRecovery -and
      [string]$resumeRecovery.trigger -ceq 'decision' -and
      [string]$resumeRecovery.taskId -ceq $TaskId -and
      [string]$resumeRecovery.decisionId -ceq $DecisionId -and
      $null -ne $resumeLease -and
      [string]$resumeLease.runId -ceq $RunId -and
      [string]$resumeLease.taskId -ceq $TaskId -and
      [string]$resumeLease.repositoryRoot -ieq $script:resolvedRepositoryRoot
    if (-not $validDecisionResume) {
      throw 'Decision recovery does not match the active lease'
    }
    $script:decisionReply = if ($ReadDecisionReplyFromStdin) {
      $stdinReader = [IO.StreamReader]::new(
        [Console]::OpenStandardInput(),
        [Text.UTF8Encoding]::new($false),
        $true
      )
      try {
        $stdinReader.ReadToEnd()
      } finally {
        $stdinReader.Dispose()
      }
    } else {
      $DecisionOption
    }
    if (
      [string]::IsNullOrWhiteSpace($script:decisionReply) -or
      $script:decisionReply.Length -gt 4000 -or
      $script:decisionReply -match '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]'
    ) {
      throw 'Decision reply is invalid'
    }
  }

  $beforeHead = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
  $beforeWorkspace = Get-WorkspaceSnapshot
  $runner = Invoke-SessionRunner -Prompt (New-ResponsibilityPrompt)
  $capturedSessionId = [string]$runner.Summary.sessionId
  if ([string]::IsNullOrWhiteSpace($capturedSessionId)) {
    $capturedSessionId = $null
  }

  $afterHead = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
  $afterWorkspace = Get-WorkspaceSnapshot
  $newChangedPaths = @(Get-NewChangedPaths -Before $beforeWorkspace -After $afterWorkspace)
  $newCommits = if ($beforeHead -ceq $afterHead) {
    @()
  } else {
    @(Invoke-GitText -Arguments @('rev-list', '--reverse', "$beforeHead..$afterHead") -split '\r?\n' | Where-Object { $_ -match '^[0-9a-f]{40}$' })
  }
  $matchingCommits = [Collections.Generic.List[string]]::new()
  foreach ($commitSha in $newCommits) {
    $metadata = Get-CommitMetadata -CommitSha $commitSha
    if (
      $null -ne $metadata -and
      [string]$metadata.Automation -ceq 'tzg-hourly-controller' -and
      [string]$metadata.Task -ceq $TaskId -and
      [string]$metadata.State -ceq 'completed'
    ) {
      $matchingCommits.Add($commitSha)
    }
  }

  $shown = Invoke-LeaseAction -LeaseAction Show -Parameters @{ StateRoot = $StateRoot }
  $recovery = $shown.state.recovery
  $hasMatchingDecisionRecovery =
    $null -ne $recovery -and
    [string]$recovery.trigger -ceq 'decision' -and
    [string]$recovery.taskId -ceq $TaskId -and
    [string]$recovery.runId -ceq $RunId

  if ($hasMatchingDecisionRecovery) {
    Close-Run -Category 'waiting_decision' -DetailCode 'decision_recovery_saved'
    $result = [ordered]@{
      status = 'waiting_decision'; category = 'waiting_decision'; taskId = $TaskId; runId = $RunId
      sessionId = $capturedSessionId; commitSha = $null
    }
    $resultExitCode = 0
  } elseif (
    $newCommits.Count -eq 1 -and
    $matchingCommits.Count -eq 1 -and
    $newChangedPaths.Count -eq 0 -and
    (Test-TaskCardCloseout)
  ) {
    $verifiedCommitSha = $matchingCommits[0]
    if ($null -ne $recovery -and [string]$recovery.taskId -ceq $TaskId) {
      $cleared = Invoke-LeaseAction -LeaseAction ClearRecovery -Parameters @{ StateRoot = $StateRoot; RunId = $RunId }
      if ([string]$cleared.status -cne 'RECOVERY_CLEARED') {
        throw "ClearRecovery returned $($cleared.status)"
      }
    }
    $category = if ($TaskId -ceq 'QUEUE-MAINTENANCE') { 'refilled' } else { 'success' }
    Close-Run -Category $category -DetailCode "commit_$($verifiedCommitSha.Substring(0, 12))"
    $result = [ordered]@{
      status = 'completed'; category = $category; taskId = $TaskId; runId = $RunId
      sessionId = $capturedSessionId; commitSha = $verifiedCommitSha
    }
    $resultExitCode = 0
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
  } else {
    Close-Run -Category 'failed' -DetailCode 'no_verified_outcome'
    $result = [ordered]@{
      status = 'failed'; category = 'failed'; taskId = $TaskId; runId = $RunId
      sessionId = $capturedSessionId; commitSha = $null
    }
    $resultExitCode = 1
  }
} catch {
  $result = [ordered]@{
    status = 'failed'; category = 'failed'; taskId = $TaskId; runId = $RunId
    sessionId = $capturedSessionId; commitSha = $verifiedCommitSha; detailCode = 'invoker_error'
  }
  $resultExitCode = 1
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 10))
exit $resultExitCode
