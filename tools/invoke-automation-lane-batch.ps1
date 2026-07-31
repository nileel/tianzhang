#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Start', 'Recover')]
  [string]$Action,
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [string]$Model,
  [ValidateRange(1, 86400)]
  [int]$CoordinatorTimeoutSeconds = 3000,
  [ValidateRange(1, 86400)]
  [int]$LeaseSeconds = 3600
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$corePath = Join-Path $PSScriptRoot 'automation-lane-core.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$guardPath = Join-Path $PSScriptRoot 'automation-workspace-guard.ps1'
$taskCheckerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
$aclPath = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $corePath
. $aclPath

function Invoke-BatchLease {
  param(
    [Parameter(Mandatory = $true)][string]$LeaseAction,
    [hashtable]$Parameters = @{},
    [int[]]$AllowedExitCodes = @(0)
  )

  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $leasePath,
    '-Action', $LeaseAction,
    '-StateRoot', $StateRoot
  )
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) {
    $arguments += "-$($entry.Key)"
    $arguments += [string]$entry.Value
  }
  $output = @(& pwsh @arguments 2>$null)
  if ($LASTEXITCODE -notin $AllowedExitCodes -or $output.Count -ne 1) {
    throw "Lease action failed: $LeaseAction"
  }
  $output[0] | ConvertFrom-Json -Depth 100
}

function Save-CurrentBatch {
  param([Parameter(Mandatory = $true)][object]$Batch)

  Write-TzgPrivateJson -Value $Batch -Path $script:batchStatePath
  Set-PrivatePathAcl -Path $script:batchStatePath
  Assert-PrivatePathAcl -Path $script:batchStatePath
  $saved = Invoke-BatchLease -LeaseAction SaveBatch -Parameters @{
    RunId = [string]$Batch.runId
    BatchStatePath = $script:batchStatePath
  }
  if ([string]$saved.status -cne 'BATCH_SAVED') {
    throw "SaveBatch returned $($saved.status)"
  }
}

function Get-BatchInputs {
  param([string]$Root)

  $queuePath = Join-Path $Root '开发管理/当前任务队列.txt'
  $queueRows = @(Read-TzgLaneQueue -Path $queuePath)
  $cards = [ordered]@{}
  foreach ($row in $queueRows) {
    $cardPath = Join-Path $Root ([string]$row.cardPath)
    $card = Read-TzgLaneTaskCard -Path $cardPath
    $cards[[string]$row.taskId] = $card
  }
  [pscustomobject]@{
    queueRows = $queueRows
    cards = $cards
    queueHash = Get-TzgFileSha256 -Path $queuePath
  }
}

function New-BatchLane {
  param(
    [Parameter(Mandatory = $true)][object]$Selection,
    [Parameter(Mandatory = $true)][string]$BatchId,
    [Parameter(Mandatory = $true)][string]$BaseCommit,
    [Parameter(Mandatory = $true)][string]$BatchDirectory
  )

  $laneId = [string]$Selection.lane.laneId
  $taskId = [string]$Selection.taskId
  $safeTaskId = $taskId -replace '[^A-Za-z0-9._-]', '-'
  $worktree = Join-Path $BatchDirectory "worktrees\$laneId"
  $branch = "automation/$BatchId/$laneId/$safeTaskId"
  [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
  $output = @(& git -C $script:resolvedRepositoryRoot worktree add -b $branch $worktree $BaseCommit 2>&1)
  if ($LASTEXITCODE -ne 0) {
    throw "Unable to create lane worktree: $(@($output) -join ' ')"
  }
  $resultPath = Join-Path $BatchDirectory "results\$laneId.json"
  [pscustomobject][ordered]@{
    laneId = $laneId
    owner = [string]$Selection.lane.owner
    identity = [string]$Selection.lane.identity
    acceptedRoutes = @($Selection.lane.acceptedRoutes)
    invoker = [string]$Selection.lane.invoker
    taskClaim = [pscustomobject][ordered]@{
      taskId = $taskId
      route = [string]$Selection.route
      owner = [string]$Selection.owner
      dispatchState = 'ready'
      cardHash = [string]$Selection.cardHash
      queueRowHash = [string]$Selection.queueRowHash
    }
    worktree = [IO.Path]::GetFullPath($worktree)
    branch = $branch
    baseCommit = $BaseCommit
    workerPaths = @($Selection.workerPaths)
    coordinatorPaths = @($Selection.coordinatorPaths)
    factPaths = @($Selection.factPaths)
    processOrSession = [pscustomobject][ordered]@{
      state = 'pending'
      processId = $null
      sessionId = $null
      resultPath = [IO.Path]::GetFullPath($resultPath)
      startedAt = $null
      completedAt = $null
    }
    workerTerminal = $null
    integrationState = 'pending'
    queueIndex = [int]$Selection.queueIndex
  }
}

function Start-BatchLaneWorker {
  param(
    [Parameter(Mandatory = $true)][object]$Batch,
    [Parameter(Mandatory = $true)][object]$Lane
  )

  $invokerPath = Join-Path $script:resolvedRepositoryRoot ([string]$Lane.invoker)
  if (-not (Test-Path -LiteralPath $invokerPath -PathType Leaf)) {
    throw "Lane invoker is missing: $($Lane.invoker)"
  }
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $invokerPath,
    '-Action', 'Start',
    '-RepositoryRoot', [string]$Lane.worktree,
    '-TaskId', [string]$Lane.taskClaim.taskId,
    '-RunId', [string]$Batch.runId,
    '-BatchId', [string]$Batch.batchId,
    '-LaneId', [string]$Lane.laneId,
    '-ResultPath', [string]$Lane.processOrSession.resultPath,
    '-StateRoot', $StateRoot
  )
  if ([string]$Lane.owner -ceq 'codex') {
    if ([string]::IsNullOrWhiteSpace($Model)) {
      throw 'Model is required for the Codex lane'
    }
    $arguments += @('-Model', $Model)
  }
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.WorkingDirectory = [string]$Lane.worktree
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in $arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw "Unable to start lane worker: $($Lane.laneId)"
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $Lane.processOrSession.state = 'running'
  $Lane.processOrSession.processId = $process.Id
  $Lane.processOrSession.startedAt = [DateTimeOffset]::UtcNow.ToString('o')
  [pscustomobject]@{
    process = $process
    stdoutTask = $stdoutTask
    stderrTask = $stderrTask
  }
}

function Complete-FinishedLaneProcesses {
  param(
    [Parameter(Mandatory = $true)][object]$Batch,
    [Parameter(Mandatory = $true)][hashtable]$Processes
  )

  $changed = $false
  foreach ($lane in $Batch.lanes) {
    if ($null -ne $lane.workerTerminal) {
      continue
    }
    $resultPath = [string]$lane.processOrSession.resultPath
    if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
      try {
        $terminal = Read-TzgPrivateJson -Path $resultPath
        Assert-TzgLaneWorkerTerminal -Terminal $terminal -Lane $lane -BatchId ([string]$Batch.batchId)
        $lane.workerTerminal = $terminal
        $lane.processOrSession.sessionId = $terminal.sessionId
        $lane.processOrSession.state = 'terminal'
        $lane.processOrSession.completedAt = [DateTimeOffset]::UtcNow.ToString('o')
        $changed = $true
      } catch {
        $lane.workerTerminal = [pscustomobject][ordered]@{
          status = 'failed'
          batchId = [string]$Batch.batchId
          laneId = [string]$lane.laneId
          taskId = [string]$lane.taskClaim.taskId
          identity = [string]$lane.identity
          sessionId = $null
          detailCode = 'lane_result_invalid'
        }
        $lane.processOrSession.state = 'terminal'
        $lane.processOrSession.completedAt = [DateTimeOffset]::UtcNow.ToString('o')
        $changed = $true
      }
      continue
    }
    $processAlive = $false
    if ($Processes.ContainsKey([string]$lane.laneId)) {
      $processAlive = -not $Processes[[string]$lane.laneId].process.HasExited
    } else {
      $processId = [int]$lane.processOrSession.processId
      $startedAt = [DateTimeOffset]::MinValue
      $process = if ($processId -gt 0) {
        Get-Process -Id $processId -ErrorAction SilentlyContinue
      } else {
        $null
      }
      if (
        $null -ne $process -and
        [string]$process.ProcessName -ceq 'pwsh' -and
        [DateTimeOffset]::TryParse([string]$lane.processOrSession.startedAt, [ref]$startedAt)
      ) {
        $actualStart = [DateTimeOffset]$process.StartTime
        $processAlive = [Math]::Abs(($actualStart - $startedAt).TotalSeconds) -le 30
      }
    }
    if (-not $processAlive) {
      $lane.workerTerminal = [pscustomobject][ordered]@{
        status = 'failed'
        batchId = [string]$Batch.batchId
        laneId = [string]$lane.laneId
        taskId = [string]$lane.taskClaim.taskId
        identity = [string]$lane.identity
        sessionId = [string]$lane.processOrSession.sessionId
        detailCode = 'lane_process_lost'
      }
      $lane.processOrSession.state = 'terminal'
      $lane.processOrSession.completedAt = [DateTimeOffset]::UtcNow.ToString('o')
      $changed = $true
    }
  }
  $changed
}

function Send-LaneOutcomeNotification {
  param(
    [Parameter(Mandatory = $true)][object]$Batch,
    [Parameter(Mandatory = $true)][object]$Lane,
    [string]$CommitSha,
    [string]$DetailCode
  )

  if ($Lane.workerTerminal.PSObject.Properties.Name -contains 'notificationStatus') {
    return
  }
  $notificationPath = Join-Path $script:resolvedRepositoryRoot 'tools/send-feishu-notification.ps1'
  if (-not (Test-Path -LiteralPath $notificationPath -PathType Leaf)) {
    $Lane.workerTerminal | Add-Member -NotePropertyName notificationStatus -NotePropertyValue 'not_configured' -Force
    return
  }
  $status = if (-not [string]::IsNullOrWhiteSpace($CommitSha)) {
    if ([string]$Lane.taskClaim.route -ceq 'external_execute') { 'pending_review' } else { 'completed' }
  } else {
    switch ([string]$Lane.workerTerminal.status) {
      'needs_decision' { 'waiting_decision' }
      'blocked' { 'blocked' }
      default { 'failed' }
    }
  }
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $notificationPath,
    '-Kind', 'TaskOutcome',
    '-RepositoryRoot', $script:resolvedRepositoryRoot,
    '-TaskId', [string]$Lane.taskClaim.taskId,
    '-Status', $status,
    '-RunId', [string]$Batch.runId
  )
  if (-not [string]::IsNullOrWhiteSpace($CommitSha)) {
    $arguments += @('-CommitSha', $CommitSha)
  } else {
    $stableDetail = if ([string]::IsNullOrWhiteSpace($DetailCode)) { 'lane_failed' } else { $DetailCode }
    $arguments += @('-DetailCode', $stableDetail)
  }
  $notificationStatus = 'delivery_failed'
  try {
    $output = @(& pwsh @arguments 2>$null)
    if ($LASTEXITCODE -eq 0 -and $output.Count -gt 0) {
      $notification = $output[-1] | ConvertFrom-Json -Depth 10
      if ([string]$notification.result -cmatch '\A[A-Z_]+\z') {
        $notificationStatus = [string]$notification.result
      }
    }
  } catch {
    $notificationStatus = 'delivery_failed'
  }
  $Lane.workerTerminal | Add-Member -NotePropertyName notificationStatus -NotePropertyValue $notificationStatus -Force
}

function Invoke-ReadyLaneIntegrations {
  param([Parameter(Mandatory = $true)][object]$Batch)

  $changed = $false
  $knownCommits = [Collections.Generic.List[string]]::new()
  foreach ($lane in @($Batch.lanes | Sort-Object queueIndex)) {
    if ($null -ne $lane.workerTerminal -and $lane.workerTerminal.PSObject.Properties.Name -contains 'canonicalCommits') {
      foreach ($commit in @($lane.workerTerminal.canonicalCommits)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$commit)) { $knownCommits.Add([string]$commit) }
      }
    }
    if ([string]$lane.integrationState -cin @('integrated', 'held_conflict', 'stale_selection', 'failed')) {
      continue
    }
    if ($null -eq $lane.workerTerminal) {
      break
    }
    if ([string]$lane.workerTerminal.status -cne 'completed') {
      $lane.integrationState = 'failed'
      Send-LaneOutcomeNotification `
        -Batch $Batch `
        -Lane $lane `
        -DetailCode ([string]$lane.workerTerminal.detailCode)
      $changed = $true
      continue
    }
    $lane.integrationState = 'integrating'
    Save-CurrentBatch -Batch $Batch
    try {
      $result = Invoke-TzgLaneCanonicalIntegration `
        -RepositoryRoot $script:resolvedRepositoryRoot `
        -Batch $Batch `
        -Lane $lane `
        -Terminal $lane.workerTerminal `
        -PrivateDirectory (Split-Path -Parent ([string]$lane.processOrSession.resultPath)) `
        -KnownBatchCommits @($knownCommits)
      $lane.integrationState = [string]$result.state
      if ([string]$result.state -ceq 'integrated') {
        $commits = @($result.businessCommit, $result.handoffCommit | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        $lane.workerTerminal | Add-Member -NotePropertyName canonicalCommits -NotePropertyValue $commits -Force
        $lane.workerTerminal | Add-Member -NotePropertyName taskState -NotePropertyValue ([string]$result.taskState) -Force
        foreach ($commit in $commits) { $knownCommits.Add([string]$commit) }
        Send-LaneOutcomeNotification `
          -Batch $Batch `
          -Lane $lane `
          -CommitSha ([string]$result.businessCommit)
      } else {
        $lane.workerTerminal | Add-Member -NotePropertyName conflictingPaths -NotePropertyValue @($result.conflictingPaths) -Force
        Send-LaneOutcomeNotification -Batch $Batch -Lane $lane -DetailCode ([string]$result.state)
      }
    } catch {
      $lane.integrationState = 'failed'
      $lane.workerTerminal | Add-Member -NotePropertyName integrationDetailCode -NotePropertyValue 'integration_failed' -Force
      Send-LaneOutcomeNotification -Batch $Batch -Lane $lane -DetailCode 'integration_failed'
    }
    $changed = $true
    Save-CurrentBatch -Batch $Batch
  }
  $changed
}

function Close-BatchIfTerminal {
  param([Parameter(Mandatory = $true)][object]$Batch)

  $allTerminal = @($Batch.lanes | Where-Object {
    $null -eq $_.workerTerminal -or
    [string]$_.integrationState -cnotin @('integrated', 'held_conflict', 'stale_selection', 'failed')
  }).Count -eq 0
  if (-not $allTerminal) {
    return $false
  }
  $Batch.status = 'closed'
  Save-CurrentBatch -Batch $Batch
  $true
}

function Remove-SafeLaneWorktrees {
  param([Parameter(Mandatory = $true)][object]$Batch)

  foreach ($lane in $Batch.lanes) {
    if (-not (Test-TzgLaneCleanupAllowed -Lane $lane)) {
      continue
    }
    $worktree = [string]$lane.worktree
    $status = @(& git -C $worktree status --porcelain --untracked-files=all 2>$null)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
      continue
    }
    $output = @(& git -C $script:resolvedRepositoryRoot worktree remove $worktree 2>&1)
    if ($LASTEXITCODE -ne 0) {
      continue
    }
    $null = @(& git -C $script:resolvedRepositoryRoot branch -D ([string]$lane.branch) 2>$null)
  }
}

function Complete-BatchCloseout {
  param([Parameter(Mandatory = $true)][object]$Batch)

  Remove-SafeLaneWorktrees -Batch $Batch
  $hasPreservedEvidence = @($Batch.lanes | Where-Object {
    [string]$_.integrationState -cin @('held_conflict', 'stale_selection') -or
    (Test-Path -LiteralPath ([string]$_.worktree) -PathType Container) -or
    (
      [string]$_.integrationState -ceq 'failed' -and
      (
        $null -eq $_.workerTerminal -or
        [string]$_.workerTerminal.status -cin @('completed', 'needs_decision')
      )
    )
  }).Count -gt 0
  $integratedCount = @($Batch.lanes | Where-Object { [string]$_.integrationState -ceq 'integrated' }).Count
  $category = if ($integratedCount -gt 0) { 'success' } else { 'failed' }
  $detailCode = if ($hasPreservedEvidence) { 'batch_evidence_preserved' } else { "batch_integrated_$integratedCount" }
  $recorded = Invoke-BatchLease -LeaseAction RecordResult -Parameters @{
    RunId = [string]$Batch.runId
    TaskId = 'AUTOMATION-BATCH'
    Category = $category
    DetailCode = $detailCode
  }
  if ([string]$recorded.status -cne 'RECORDED') {
    throw 'Unable to record batch result'
  }
  if (-not $hasPreservedEvidence) {
    $cleared = Invoke-BatchLease -LeaseAction ClearBatch -Parameters @{ RunId = [string]$Batch.runId }
    if ([string]$cleared.status -cne 'BATCH_CLEARED') {
      throw 'Unable to clear closed batch'
    }
  }
  $released = Invoke-BatchLease -LeaseAction Release -Parameters @{ RunId = [string]$Batch.runId }
  if ([string]$released.status -cne 'RELEASED') {
    throw 'Unable to release coordinator lease'
  }
}

$script:resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
$script:batchStatePath = $null
$processes = @{}
$result = $null
$batch = $null
$stage = 'initialize'

try {
  foreach ($path in @($corePath, $leasePath, $guardPath, $taskCheckerPath, $aclPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      throw "Required coordinator component is missing: $path"
    }
  }
  $gitRoot = (& git -C $script:resolvedRepositoryRoot rev-parse --show-toplevel 2>$null).Trim()
  if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/') -ine $script:resolvedRepositoryRoot) {
    throw 'RepositoryRoot must be the main Git root'
  }
  $shown = Invoke-BatchLease -LeaseAction Show
  if ([bool]$shown.state.blocking.pauseRequested) {
    $result = [ordered]@{ status = 'suspended'; batchId = $null; lanes = @() }
    throw 'logical suspension'
  }

  if ($Action -ceq 'Start') {
    $stage = 'validate_start'
    if ($null -ne $shown.state.batch -or [string]$shown.leaseStatus -ceq 'active' -or $null -ne $shown.state.recovery) {
      $result = [ordered]@{ status = 'occupied'; batchId = $shown.state.batch.batchId; lanes = @() }
      throw 'control plane occupied'
    }
    $stage = 'task_contract'
    $checkerOutput = @(
      & pwsh -NoProfile -ExecutionPolicy Bypass -File $taskCheckerPath `
        -RepositoryRoot $script:resolvedRepositoryRoot `
        -OutputJson 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
      throw "Task-card contract failed: $(@($checkerOutput) -join ' ')"
    }
    $stage = 'read_inputs'
    $inputs = Get-BatchInputs -Root $script:resolvedRepositoryRoot
    $stage = 'read_configuration'
    $configuration = Get-TzgAutomationLaneConfiguration
    $stage = 'read_manual_paths'
    $manualPaths = @(Get-TzgManualWorkspacePaths -RepositoryRoot $script:resolvedRepositoryRoot)
    $stage = 'select'
    $selection = @(Select-TzgAutomationLaneBatch `
      -QueueRows $inputs.queueRows `
      -Cards $inputs.cards `
      -Lanes $configuration.lanes `
      -MaxConcurrent ([int]$configuration.maxConcurrent) `
      -ManualPaths $manualPaths)
    if ($selection.Count -eq 0) {
      $result = [ordered]@{ status = 'no_safe_candidate'; batchId = $null; lanes = @() }
      throw 'no safe candidate'
    }
    $stage = 'acquire'
    $acquired = Invoke-BatchLease -LeaseAction Acquire -Parameters @{
      TaskId = 'AUTOMATION-BATCH'
      Owner = 'coordinator'
      RepositoryRoot = $script:resolvedRepositoryRoot
      LeaseSeconds = $LeaseSeconds
    }
    if ([string]$acquired.status -cne 'ACQUIRED') {
      throw "Coordinator lease was not acquired: $($acquired.status)"
    }
    $stage = 'snapshot'
    $runId = [string]$acquired.runId
    $batchId = [Guid]::NewGuid().ToString()
    $batchDirectory = Join-Path $StateRoot "batches\$batchId"
    [IO.Directory]::CreateDirectory($batchDirectory) | Out-Null
    Set-PrivatePathAcl -Path $batchDirectory -Directory
    Assert-PrivatePathAcl -Path $batchDirectory -Directory
    $script:batchStatePath = Join-Path $batchDirectory 'batch.json'
    $baselinePath = Join-Path $batchDirectory 'manual-baseline.json'
    $null = @(
      & pwsh -NoProfile -ExecutionPolicy Bypass -File $guardPath Snapshot `
        -RepositoryRoot $script:resolvedRepositoryRoot `
        -BaselinePath $baselinePath 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
      throw 'Unable to capture batch manual baseline'
    }
    Set-PrivatePathAcl -Path $baselinePath
    Assert-PrivatePathAcl -Path $baselinePath
    $baseCommit = (& git -C $script:resolvedRepositoryRoot rev-parse HEAD).Trim()

    $freshInputs = Get-BatchInputs -Root $script:resolvedRepositoryRoot
    $freshManualPaths = @(Get-TzgManualWorkspacePaths -RepositoryRoot $script:resolvedRepositoryRoot)
    $freshSelection = @(Select-TzgAutomationLaneBatch `
      -QueueRows $freshInputs.queueRows `
      -Cards $freshInputs.cards `
      -Lanes $configuration.lanes `
      -MaxConcurrent ([int]$configuration.maxConcurrent) `
      -ManualPaths $freshManualPaths)
    $selectionFingerprint = @($selection | ForEach-Object { "$($_.queueIndex):$($_.taskId):$($_.lane.laneId)" }) -join '|'
    $freshFingerprint = @($freshSelection | ForEach-Object { "$($_.queueIndex):$($_.taskId):$($_.lane.laneId)" }) -join '|'
    if ($selectionFingerprint -cne $freshFingerprint) {
      throw 'Selection changed while acquiring coordinator lease'
    }

    $stage = 'create_lanes'
    $lanes = [Collections.Generic.List[object]]::new()
    foreach ($selected in $freshSelection) {
      $lanes.Add((New-BatchLane `
        -Selection $selected `
        -BatchId $batchId `
        -BaseCommit $baseCommit `
        -BatchDirectory $batchDirectory))
    }
    $batch = [pscustomobject][ordered]@{
      schemaVersion = 1
      batchId = $batchId
      runId = $runId
      repositoryRoot = $script:resolvedRepositoryRoot
      status = 'open'
      baseCommit = $baseCommit
      queueHash = [string]$freshInputs.queueHash
      manualBaselinePath = [IO.Path]::GetFullPath($baselinePath)
      startedAt = [DateTimeOffset]::UtcNow.ToString('o')
      maxConcurrent = [int]$configuration.maxConcurrent
      lanes = @($lanes)
    }
    $stage = 'start_workers'
    Save-CurrentBatch -Batch $batch
    foreach ($lane in $batch.lanes) {
      $processes[[string]$lane.laneId] = Start-BatchLaneWorker -Batch $batch -Lane $lane
    }
    Save-CurrentBatch -Batch $batch
  } else {
    $stage = 'recover'
    if ($null -eq $shown.state.batch) {
      $result = [ordered]@{ status = 'no_batch_to_recover'; batchId = $null; lanes = @() }
      throw 'no batch'
    }
    $batch = $shown.state.batch
    $script:batchStatePath = Join-Path $StateRoot "batches\$($batch.batchId)\batch.json"
    if ([string]$batch.status -ceq 'closed') {
      $result = [ordered]@{
        status = 'preserved_batch'
        batchId = [string]$batch.batchId
        lanes = @($batch.lanes | ForEach-Object { [ordered]@{ laneId = $_.laneId; taskId = $_.taskClaim.taskId; integrationState = $_.integrationState } })
      }
      throw 'closed batch evidence is preserved'
    }
    if ([string]$shown.leaseStatus -cne 'active') {
      $resumed = Invoke-BatchLease -LeaseAction ResumeBatch -Parameters @{
        BatchId = [string]$batch.batchId
        LeaseSeconds = $LeaseSeconds
      }
      if ([string]$resumed.status -cne 'BATCH_RESUMED') {
        throw "Unable to resume coordinator lease: $($resumed.status)"
      }
    }
  }

  $stage = 'coordinate'
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds($CoordinatorTimeoutSeconds)
  while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $workerChanged = Complete-FinishedLaneProcesses -Batch $batch -Processes $processes
    if ($workerChanged) {
      Save-CurrentBatch -Batch $batch
    }
    $null = Invoke-ReadyLaneIntegrations -Batch $batch
    if (Close-BatchIfTerminal -Batch $batch) {
      Complete-BatchCloseout -Batch $batch
      $result = [ordered]@{
        status = 'completed'
        batchId = [string]$batch.batchId
        lanes = @($batch.lanes | Sort-Object queueIndex | ForEach-Object {
          [ordered]@{
            laneId = $_.laneId
            taskId = $_.taskClaim.taskId
            route = $_.taskClaim.route
            workerStatus = $_.workerTerminal.status
            integrationState = $_.integrationState
            sessionId = $_.workerTerminal.sessionId
            conflictingPaths = if ($_.workerTerminal.PSObject.Properties.Name -contains 'conflictingPaths') {
              [string[]]@($_.workerTerminal.conflictingPaths)
            } else {
              [string[]]@()
            }
            canonicalCommits = if ($_.workerTerminal.PSObject.Properties.Name -contains 'canonicalCommits') {
              [string[]]@($_.workerTerminal.canonicalCommits)
            } else {
              [string[]]@()
            }
            integrationDetailCode = if ($_.workerTerminal.PSObject.Properties.Name -contains 'integrationDetailCode') {
              [string]$_.workerTerminal.integrationDetailCode
            } else {
              $null
            }
            detailCode = if ($_.workerTerminal.PSObject.Properties.Name -contains 'detailCode') {
              [string]$_.workerTerminal.detailCode
            } else {
              $null
            }
            notificationStatus = if ($_.workerTerminal.PSObject.Properties.Name -contains 'notificationStatus') {
              [string]$_.workerTerminal.notificationStatus
            } else {
              $null
            }
            decision = if ([string]$_.workerTerminal.status -ceq 'needs_decision') {
              [ordered]@{
                decisionId = $_.workerTerminal.decisionId
                question = $_.workerTerminal.question
                options = [string[]]@($_.workerTerminal.options)
              }
            } else {
              $null
            }
          }
        })
      }
      break
    }
    Start-Sleep -Milliseconds 250
  }
  if ($null -eq $result) {
    $result = [ordered]@{
      status = 'running'
      batchId = [string]$batch.batchId
      lanes = @($batch.lanes | ForEach-Object {
        [ordered]@{
          laneId = $_.laneId
          taskId = $_.taskClaim.taskId
          route = $_.taskClaim.route
          workerStatus = if ($null -eq $_.workerTerminal) { 'running' } else { $_.workerTerminal.status }
          integrationState = $_.integrationState
          sessionId = $_.processOrSession.sessionId
        }
      })
    }
  }
} catch {
  if ($null -eq $result) {
    $result = [ordered]@{
      status = 'failed'
      batchId = if ($null -ne $batch) { [string]$batch.batchId } else { $null }
      detailCode = "batch_${stage}_failed"
      lanes = @()
    }
  }
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 100))
exit $(if ([string]$result.status -cin @('completed', 'running', 'preserved_batch', 'no_safe_candidate', 'no_batch_to_recover', 'suspended', 'occupied')) { 0 } else { 1 })
