#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Assert-True {
  param(
    [Parameter(Mandatory = $true)]
    [bool]$Condition,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Assert-Equal {
  param(
    [AllowNull()]
    [object]$Actual,
    [AllowNull()]
    [object]$Expected,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if ($Actual -ne $Expected) {
    throw "$Message (expected=$Expected actual=$Actual)"
  }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolPath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
  throw "Expected implementation is missing: $toolPath"
}

$privateAclPath = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $privateAclPath

function Invoke-LeaseTool {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Action,
    [hashtable]$Parameters = @{},
    [int[]]$AllowedExitCodes = @(0)
  )

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $toolPath, '-Action', $Action)) {
    $startInfo.ArgumentList.Add($argument)
  }
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) {
    if ($entry.Value -is [bool]) {
      if ($entry.Value) {
        $startInfo.ArgumentList.Add("-$($entry.Key)")
      }
      continue
    }
    $startInfo.ArgumentList.Add("-$($entry.Key)")
    if ($entry.Value -is [Collections.IEnumerable] -and $entry.Value -isnot [string]) {
      $startInfo.ArgumentList.Add((@($entry.Value) -join '|'))
    } else {
      $startInfo.ArgumentList.Add([string]$entry.Value)
    }
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw "Failed to start lease tool action $Action"
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()

  $lines = @($stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  Assert-Equal -Actual $lines.Count -Expected 1 -Message "$Action must emit exactly one stdout line"
  try {
    $json = $lines[0] | ConvertFrom-Json -Depth 100
  } catch {
    throw "$Action stdout is not JSON: $($lines[0])"
  }
  Assert-True -Condition ($exitCode -in $AllowedExitCodes) -Message "$Action exit code $exitCode was not allowed; stderr=$stderr"

  [pscustomobject]@{
    ExitCode = $exitCode
    Json = $json
    Stdout = $stdout
    Stderr = $stderr
  }
}

function Get-FileSha256 {
  param([Parameter(Mandatory = $true)][string]$Path)

  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path)))
}

function Assert-RejectedWithoutStateChange {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [hashtable]$Parameters,
    [Parameter(Mandatory = $true)]
    [string]$StatePath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedStatus
  )

  $before = Get-FileSha256 -Path $StatePath
  $result = Invoke-LeaseTool -Action $Action -Parameters $Parameters -AllowedExitCodes @(2)
  Assert-Equal -Actual $result.Json.status -Expected $ExpectedStatus -Message "$Action rejection status mismatch"
  $after = Get-FileSha256 -Path $StatePath
  Assert-Equal -Actual $after -Expected $before -Message "$Action rejection changed state bytes"
}

function Write-ConsumeRequestFixture {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$DecisionId
  )

  $fixture = [ordered]@{
    pendingDecision = [ordered]@{
      decisionId = $DecisionId
      allowedOptions = @('A', 'B', 'C')
      allowCustomReply = $true
      createdAt = '2026-07-22T00:00:00.000Z'
      expiresAt = '2026-07-23T00:00:00.000Z'
      cardNonceHash = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
      providerMessageIdHash = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
      providerChatIdHash = 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'
    }
  }
  $json = $fixture | ConvertTo-Json -Depth 10 -Compress
  [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

$automationStateRoot = Join-Path $env:USERPROFILE '.codex\automation-state'
$testId = [Guid]::NewGuid().ToString('N')
$stateRoot = Join-Path $automationStateRoot "tzg-hourly-controller-lease-tests\$testId"
$legacyStateRoot = Join-Path $stateRoot 'legacy-v1'
$legacyV2StateRoot = Join-Path $stateRoot 'legacy-v2'
$bridgeRoot = Join-Path $automationStateRoot "tzg-feishu-decision-bridge\lease-test-$testId"
$statePath = Join-Path $stateRoot 'runtime.json'
$requestPath = Join-Path $bridgeRoot 'decision-request.json'

try {
  [IO.Directory]::CreateDirectory($bridgeRoot) | Out-Null
  Set-PrivatePathAcl -Path $bridgeRoot -Directory
  Assert-PrivatePathAcl -Path $bridgeRoot -Directory
  foreach ($fixturePath in @($requestPath)) {
    [IO.File]::WriteAllText($fixturePath, '{"fixture":true}', [Text.UTF8Encoding]::new($false))
    Set-PrivatePathAcl -Path $fixturePath
    Assert-PrivatePathAcl -Path $fixturePath
  }

  [IO.Directory]::CreateDirectory($legacyStateRoot) | Out-Null
  Set-PrivatePathAcl -Path $legacyStateRoot -Directory
  $legacyStatePath = Join-Path $legacyStateRoot 'runtime.json'
  $legacyState = [ordered]@{
    schemaVersion = 1
    lease = $null
    recovery = $null
    pendingResumes = @()
    blocking = [ordered]@{ fingerprint = $null; count = 0; pauseRequested = $false }
    lastResult = [ordered]@{
      category = 'success'
      taskId = 'legacy-task'
      detailCode = 'legacy-result'
      recordedAt = '2026-07-22T00:00:00.0000000+00:00'
    }
  }
  [IO.File]::WriteAllText($legacyStatePath, ($legacyState | ConvertTo-Json -Compress -Depth 10), [Text.UTF8Encoding]::new($false))
  Set-PrivatePathAcl -Path $legacyStatePath
  $migratedLegacy = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $legacyStateRoot }
  Assert-Equal -Actual $migratedLegacy.Json.state.schemaVersion -Expected 3 -Message 'Legacy runtime was not migrated in memory'
  Assert-True -Condition ($null -eq $migratedLegacy.Json.state.PSObject.Properties['pendingResumes']) -Message 'Legacy migration retained the removed pending resume queue'
  Assert-True -Condition ($null -eq $migratedLegacy.Json.state.lastResult.runId) -Message 'Legacy result migration invented a run id'

  [IO.Directory]::CreateDirectory($legacyV2StateRoot) | Out-Null
  Set-PrivatePathAcl -Path $legacyV2StateRoot -Directory
  $legacyV2StatePath = Join-Path $legacyV2StateRoot 'runtime.json'
  $legacyV2State = [ordered]@{
    schemaVersion = 2
    lease = $null
    recovery = [ordered]@{
      trigger = 'decision'
      runId = 'legacy-v2-run'
      taskId = 'legacy-v2-task'
      owner = 'codex'
      repositoryRoot = $repositoryRoot
      resumeKind = 'codex'
      resumeId = 'legacy-v2-thread'
      decisionId = 'legacy-v2-decision'
      decisionRequestPath = $requestPath
      hasUncommittedChanges = $false
      changedPaths = @()
    }
    pendingResumes = @([ordered]@{
      decisionId = 'legacy-v2-decision'
      replyPath = 'ignored.json'
      queuedAt = '2026-07-24T00:00:00.0000000+00:00'
    })
    blocking = [ordered]@{ fingerprint = $null; count = 0; pauseRequested = $false }
    lastResult = $null
  }
  [IO.File]::WriteAllText($legacyV2StatePath, ($legacyV2State | ConvertTo-Json -Compress -Depth 10), [Text.UTF8Encoding]::new($false))
  Set-PrivatePathAcl -Path $legacyV2StatePath
  $migratedLegacyV2 = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $legacyV2StateRoot }
  Assert-Equal -Actual $migratedLegacyV2.Json.state.schemaVersion -Expected 3 -Message 'Schema v2 runtime was not migrated in memory'
  Assert-True -Condition ($null -eq $migratedLegacyV2.Json.state.PSObject.Properties['pendingResumes']) -Message 'Schema v2 migration retained the removed pending resume queue'
  Assert-True -Condition ($null -eq $migratedLegacyV2.Json.state.recovery.PSObject.Properties['resumeKind']) -Message 'Schema v2 decision migration retained the resume kind'
  Assert-True -Condition ($null -eq $migratedLegacyV2.Json.state.recovery.PSObject.Properties['resumeId']) -Message 'Schema v2 decision migration retained the resume id'
  Assert-Equal -Actual $migratedLegacyV2.Json.state.recovery.decisionId -Expected 'legacy-v2-decision' -Message 'Schema v2 migration lost the decision id'

  $relativeState = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = 'relative-state' } -AllowedExitCodes @(2)
  Assert-Equal -Actual $relativeState.Json.status -Expected 'INVALID_ARGUMENT' -Message 'Relative state root must be rejected'

  $leaseStatusStateRoot = Join-Path $stateRoot 'lease-status'
  $leaseStatusStatePath = Join-Path $leaseStatusStateRoot 'runtime.json'
  $emptyLeaseStatus = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $leaseStatusStateRoot }
  Assert-Equal -Actual $emptyLeaseStatus.Json.leaseStatus -Expected 'none' -Message 'Show did not identify an empty lease'

  $leaseStatusOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $leaseStatusStateRoot
    TaskId = 'task-lease-status'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    LeaseSeconds = 60
  }
  $activeLeaseStatus = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $leaseStatusStateRoot }
  Assert-Equal -Actual $activeLeaseStatus.Json.leaseStatus -Expected 'active' -Message 'Show did not identify an active lease'

  $expiredLeaseState = Get-Content -LiteralPath $leaseStatusStatePath -Raw | ConvertFrom-Json -Depth 100
  $expiredLeaseState.lease.expiresAt = '2000-01-01T00:00:00.0000000+00:00'
  [IO.File]::WriteAllText(
    $leaseStatusStatePath,
    ($expiredLeaseState | ConvertTo-Json -Compress -Depth 100),
    [Text.UTF8Encoding]::new($false)
  )
  $expiredLeaseStatus = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $leaseStatusStateRoot }
  Assert-Equal -Actual $expiredLeaseStatus.Json.leaseStatus -Expected 'expired' -Message 'Show did not identify an expired lease'
  Invoke-LeaseTool -Action Release -Parameters @{
    StateRoot = $leaseStatusStateRoot
    RunId = $leaseStatusOwner.Json.runId
  } | Out-Null

  $invalidRepository = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-invalid-repository'
    Owner = 'codex'
    RepositoryRoot = $bridgeRoot
  } -AllowedExitCodes @(2)
  Assert-Equal -Actual $invalidRepository.Json.status -Expected 'INVALID_ARGUMENT' -Message 'Non-Git repository root must be rejected'
  Assert-True -Condition (-not (Test-Path -LiteralPath $statePath)) -Message 'Invalid acquire must not create runtime state'

  $first = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-first'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    LeaseSeconds = 60
  }
  Assert-Equal -Actual $first.Json.status -Expected 'ACQUIRED' -Message 'First acquire failed'
  Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string]$first.Json.runId)) -Message 'First acquire did not return runId'
  $firstRunId = [string]$first.Json.runId
  Assert-True -Condition (Test-Path -LiteralPath $statePath -PathType Leaf) -Message 'Acquire did not create runtime state'

  $beforeInvalidWaiting = Get-FileSha256 -Path $statePath
  $waitingWithoutRecovery = Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $firstRunId
    Category = 'waiting_decision'
    TaskId = 'task-first'
    DetailCode = 'decision-not-sent'
  } -AllowedExitCodes @(0, 2)
  Assert-Equal -Actual $waitingWithoutRecovery.Json.status -Expected 'RECOVERY_REQUIRED' -Message 'waiting_decision without recovery must fail closed'
  Assert-Equal -Actual (Get-FileSha256 -Path $statePath) -Expected $beforeInvalidWaiting -Message 'Rejected waiting_decision changed state bytes'

  $activeStateHash = Get-FileSha256 -Path $statePath
  $secondWriter = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-second'
    Owner = 'external'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $secondWriter.Json.status -Expected 'BUSY' -Message 'Second writer was not rejected'
  Assert-Equal -Actual (Get-FileSha256 -Path $statePath) -Expected $activeStateHash -Message 'Busy acquire changed state bytes'

  $wrongRunId = [Guid]::NewGuid().ToString()
  Assert-RejectedWithoutStateChange -Action RecordResult -StatePath $statePath -ExpectedStatus 'RUN_ID_MISMATCH' -Parameters @{
    StateRoot = $stateRoot
    RunId = $wrongRunId
    Category = 'failed'
    TaskId = 'task-first'
    DetailCode = 'wrong-run'
  }
  Assert-RejectedWithoutStateChange -Action SaveRecovery -StatePath $statePath -ExpectedStatus 'RUN_ID_MISMATCH' -Parameters @{
    StateRoot = $stateRoot
    RunId = $wrongRunId
    DecisionId = 'decision-wrong-run'
    DecisionRequestPath = $requestPath
    CodexThreadId = 'thread-wrong-run'
  }
  Assert-RejectedWithoutStateChange -Action Release -StatePath $statePath -ExpectedStatus 'RUN_ID_MISMATCH' -Parameters @{
    StateRoot = $stateRoot
    RunId = $wrongRunId
  }

  $releaseFirst = Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $firstRunId }
  Assert-Equal -Actual $releaseFirst.Json.status -Expected 'RELEASED' -Message 'Correct release failed'
  $afterRelease = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-after-release'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $afterRelease.Json.status -Expected 'ACQUIRED' -Message 'Acquire after release failed'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $afterRelease.Json.runId } | Out-Null

  $expiring = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-expiring'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    LeaseSeconds = 1
  }
  Start-Sleep -Milliseconds 1200
  $reclaimed = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-reclaimed'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $reclaimed.Json.status -Expected 'ACQUIRED' -Message 'Expired lease without recovery was not reclaimed'
  Assert-True -Condition ($reclaimed.Json.runId -ne $expiring.Json.runId) -Message 'Reclaimed lease reused old runId'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $reclaimed.Json.runId } | Out-Null

  $recoveryOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-recovery-only'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    LeaseSeconds = 1
  }
  [IO.File]::WriteAllText(
    $requestPath,
    '{"attemptNumber":1,"decision":{"decisionId":"decision-invalid-shape"}}',
    [Text.UTF8Encoding]::new($false)
  )
  Assert-RejectedWithoutStateChange -Action SaveRecovery -StatePath $statePath -ExpectedStatus 'INVALID_ARGUMENT' -Parameters @{
    StateRoot = $stateRoot
    RunId = $recoveryOwner.Json.runId
    DecisionId = 'decision-invalid-shape'
    DecisionRequestPath = $requestPath
  }
  Write-ConsumeRequestFixture -Path $requestPath -DecisionId 'decision-recovery-only'
  Assert-RejectedWithoutStateChange -Action SaveRecovery -StatePath $statePath -ExpectedStatus 'INVALID_ARGUMENT' -Parameters @{
    StateRoot = $stateRoot
    RunId = $recoveryOwner.Json.runId
    DecisionId = 'decision-recovery-only'
    DecisionRequestPath = $requestPath
    CodexThreadId = 'thread-recovery-only'
  }
  $savedRecovery = Invoke-LeaseTool -Action SaveRecovery -Parameters @{
    StateRoot = $stateRoot
    RunId = $recoveryOwner.Json.runId
    DecisionId = 'decision-recovery-only'
    DecisionRequestPath = $requestPath
  }
  Assert-Equal -Actual $savedRecovery.Json.status -Expected 'RECOVERY_SAVED' -Message 'Decision recovery was not saved'
  Assert-Equal -Actual $savedRecovery.Json.recovery.trigger -Expected 'decision' -Message 'Decision recovery trigger mismatch'
  Assert-Equal -Actual $savedRecovery.Json.recovery.runId -Expected $recoveryOwner.Json.runId -Message 'Decision recovery run id mismatch'
  $decisionRecoveryNames = @($savedRecovery.Json.recovery.PSObject.Properties.Name | Sort-Object)
  $expectedDecisionRecoveryNames = @('changedPaths', 'decisionId', 'decisionRequestPath', 'hasUncommittedChanges', 'owner', 'repositoryRoot', 'runId', 'taskId', 'trigger') | Sort-Object
  Assert-Equal -Actual ($decisionRecoveryNames -join ',') -Expected ($expectedDecisionRecoveryNames -join ',') -Message 'Decision recovery schema has unexpected fields'
  Assert-True -Condition ($null -eq $savedRecovery.Json.recovery.PSObject.Properties['resumeKind']) -Message 'Decision recovery retained a resume kind'
  Assert-True -Condition ($null -eq $savedRecovery.Json.recovery.PSObject.Properties['resumeId']) -Message 'Decision recovery retained a resume id'
  Assert-True -Condition (-not [bool]$savedRecovery.Json.recovery.hasUncommittedChanges) -Message 'Decision recovery invented uncommitted changes'
  Assert-Equal -Actual @($savedRecovery.Json.recovery.changedPaths).Count -Expected 0 -Message 'Decision recovery invented changed paths'
  $validWaiting = Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $recoveryOwner.Json.runId
    Category = 'waiting_decision'
    TaskId = 'task-recovery-only'
    DetailCode = 'decision-sent'
  }
  Assert-Equal -Actual $validWaiting.Json.status -Expected 'RECORDED' -Message 'Valid waiting_decision was rejected'
  Assert-Equal -Actual $validWaiting.Json.lastResult.runId -Expected $recoveryOwner.Json.runId -Message 'lastResult did not retain current run id'
  Start-Sleep -Milliseconds 1200
  $recoveryOnly = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-must-not-overwrite-recovery'
    Owner = 'external'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $recoveryOnly.Json.status -Expected 'RECOVERY_ONLY' -Message 'Decision recovery did not block normal acquire'
  Assert-Equal -Actual $recoveryOnly.Json.taskId -Expected 'task-recovery-only' -Message 'Recovery-only response lost original task'
  Invoke-LeaseTool -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $recoveryOwner.Json.runId } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $recoveryOwner.Json.runId } | Out-Null

  $claudeOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-claude-recovery'
    Owner = 'external'
    RepositoryRoot = $repositoryRoot
  }
  Write-ConsumeRequestFixture -Path $requestPath -DecisionId 'decision-claude'
  $claudeRecovery = Invoke-LeaseTool -Action SaveRecovery -Parameters @{
    StateRoot = $stateRoot
    RunId = $claudeOwner.Json.runId
    DecisionId = 'decision-claude'
    DecisionRequestPath = $requestPath
  }
  Assert-True -Condition ($null -eq $claudeRecovery.Json.recovery.PSObject.Properties['resumeKind']) -Message 'External decision recovery retained a resume kind'
  Assert-True -Condition ($null -eq $claudeRecovery.Json.recovery.PSObject.Properties['resumeId']) -Message 'External decision recovery retained a resume id'
  Invoke-LeaseTool -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $claudeOwner.Json.runId } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $claudeOwner.Json.runId } | Out-Null

  $interruptionOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-interruption'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  $interruption = Invoke-LeaseTool -Action SaveInterruption -Parameters @{
    StateRoot = $stateRoot
    RunId = $interruptionOwner.Json.runId
    CodexThreadId = 'thread-interruption'
    HasUncommittedChanges = $true
    ChangedPaths = @('tools/interrupted-one.txt')
  }
  Assert-Equal -Actual $interruption.Json.recovery.trigger -Expected 'interruption' -Message 'Interruption recovery trigger mismatch'
  Assert-Equal -Actual $interruption.Json.recovery.runId -Expected $interruptionOwner.Json.runId -Message 'Interruption recovery run id mismatch'
  $interruptionRecoveryNames = @($interruption.Json.recovery.PSObject.Properties.Name | Sort-Object)
  $expectedInterruptionRecoveryNames = @('changedPaths', 'hasUncommittedChanges', 'owner', 'repositoryRoot', 'resumeId', 'resumeKind', 'runId', 'taskId', 'trigger') | Sort-Object
  Assert-Equal -Actual ($interruptionRecoveryNames -join ',') -Expected ($expectedInterruptionRecoveryNames -join ',') -Message 'Interruption recovery schema has unexpected fields'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $interruptionOwner.Json.runId } | Out-Null
  $otherDuringInterruption = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-other-during-interruption'
    Owner = 'external'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $otherDuringInterruption.Json.status -Expected 'RECOVERY_ONLY' -Message 'Ordinary acquire bypassed interruption recovery'
  $reacquiredInterruption = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-interruption'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $reacquiredInterruption.Json.status -Expected 'RECOVERY_ACQUIRED' -Message 'Original responsibility could not reacquire interruption recovery'
  Assert-Equal -Actual $reacquiredInterruption.Json.resumeId -Expected 'thread-interruption' -Message 'Recovery acquire lost original session id'
  Assert-True -Condition ($reacquiredInterruption.Json.runId -ne $interruptionOwner.Json.runId) -Message 'Recovery acquire reused expired run id'
  Invoke-LeaseTool -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $reacquiredInterruption.Json.runId } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $reacquiredInterruption.Json.runId } | Out-Null

  $resumeOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-resume'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  Write-ConsumeRequestFixture -Path $requestPath -DecisionId 'decision-resume'
  Invoke-LeaseTool -Action SaveRecovery -Parameters @{
    StateRoot = $stateRoot
    RunId = $resumeOwner.Json.runId
    DecisionId = 'decision-resume'
    DecisionRequestPath = $requestPath
  } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $resumeOwner.Json.runId } | Out-Null

  $manualDecisionWithoutId = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-resume'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    ResumeRecovery = $true
  } -AllowedExitCodes @(2)
  Assert-Equal -Actual $manualDecisionWithoutId.Json.status -Expected 'DECISION_ID_REQUIRED' -Message 'Manual decision recovery did not require the exact decision id'
  $manualDecisionWrongId = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-resume'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    ResumeRecovery = $true
    DecisionId = 'decision-wrong'
  } -AllowedExitCodes @(2)
  Assert-Equal -Actual $manualDecisionWrongId.Json.status -Expected 'RECOVERY_MISMATCH' -Message 'Manual decision recovery accepted the wrong decision id'
  $manualDecisionLease = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-resume'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    ResumeRecovery = $true
    DecisionId = 'decision-resume'
  }
  Assert-Equal -Actual $manualDecisionLease.Json.status -Expected 'RECOVERY_ACQUIRED' -Message 'Manual decision recovery could not acquire the original task'
  Assert-Equal -Actual $manualDecisionLease.Json.decisionId -Expected 'decision-resume' -Message 'Manual decision recovery lost the decision id'
  Assert-True -Condition ($null -eq $manualDecisionLease.Json.PSObject.Properties['resumeKind']) -Message 'Decision recovery acquire exposed a resume kind'
  Assert-True -Condition ($null -eq $manualDecisionLease.Json.PSObject.Properties['resumeId']) -Message 'Decision recovery acquire exposed a resume id'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $manualDecisionLease.Json.runId } | Out-Null

  $clearWithRecovery = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $clearWithRecovery.Json.status -Expected 'RECOVERY_PRESENT' -Message 'ClearBlocking ignored recovery'

  $clearOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-resume'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
    ResumeRecovery = $true
    DecisionId = 'decision-resume'
  }
  Invoke-LeaseTool -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $clearOwner.Json.runId } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $clearOwner.Json.runId } | Out-Null

  $blockedOneOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-blocked-one'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  $blockedOne = Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $blockedOneOwner.Json.runId
    Category = 'blocked'
    TaskId = 'task-blocked-one'
    DetailCode = 'all-candidates-blocked'
    BlockingFingerprint = 'fingerprint-a'
  }
  Assert-Equal -Actual $blockedOne.Json.blocking.count -Expected 1 -Message 'First blocked result count mismatch'
  Assert-True -Condition (-not [bool]$blockedOne.Json.blocking.pauseRequested) -Message 'First blocked result requested pause'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $blockedOneOwner.Json.runId } | Out-Null

  $blockedTwoOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-blocked-two'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  $blockedTwo = Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $blockedTwoOwner.Json.runId
    Category = 'blocked'
    TaskId = 'task-blocked-two'
    DetailCode = 'all-candidates-blocked'
    BlockingFingerprint = 'fingerprint-a'
  }
  Assert-Equal -Actual $blockedTwo.Json.blocking.count -Expected 2 -Message 'Second blocked result count mismatch'
  Assert-True -Condition ([bool]$blockedTwo.Json.blocking.pauseRequested) -Message 'Second blocked result did not request pause'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $blockedTwoOwner.Json.runId } | Out-Null

  $suspendedAcquire = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-must-not-start'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $suspendedAcquire.Json.status -Expected 'SUSPENDED' -Message 'Paused runtime allowed a normal Acquire'
  Assert-Equal -Actual $suspendedAcquire.Json.fingerprint -Expected 'fingerprint-a' -Message 'Suspended fingerprint mismatch'
  Assert-Equal -Actual $suspendedAcquire.Json.count -Expected 2 -Message 'Suspended count mismatch'

  $suspendedShow = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $stateRoot }
  Assert-True -Condition ($null -eq $suspendedShow.Json.state.lease) -Message 'Suspended Acquire wrote a lease'

  $clearBlocking = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $clearBlocking.Json.status -Expected 'BLOCKING_CLEARED' -Message 'ClearBlocking did not succeed'
  Assert-True -Condition ($null -eq $clearBlocking.Json.blocking.fingerprint) -Message 'ClearBlocking retained fingerprint'
  Assert-Equal -Actual $clearBlocking.Json.blocking.count -Expected 0 -Message 'ClearBlocking retained count'
  Assert-True -Condition (-not [bool]$clearBlocking.Json.blocking.pauseRequested) -Message 'ClearBlocking retained pause request'

  $postClearAcquire = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-after-clear'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $postClearAcquire.Json.status -Expected 'ACQUIRED' -Message 'Acquire did not resume after ClearBlocking'

  $clearWithLease = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $clearWithLease.Json.status -Expected 'BUSY' -Message 'ClearBlocking ignored an active lease'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $postClearAcquire.Json.runId } | Out-Null

  $successOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-success'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  $success = Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $successOwner.Json.runId
    Category = 'success'
    TaskId = 'task-success'
    DetailCode = 'completed'
  }
  Assert-Equal -Actual $success.Json.blocking.count -Expected 0 -Message 'Success did not reset blocking count'
  Assert-True -Condition (-not [bool]$success.Json.blocking.pauseRequested) -Message 'Success did not clear pause request'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $successOwner.Json.runId } | Out-Null

  $differentOneOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-different-one'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $differentOneOwner.Json.runId
    Category = 'blocked'
    TaskId = 'task-different-one'
    DetailCode = 'blocked-a'
    BlockingFingerprint = 'fingerprint-a'
  } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $differentOneOwner.Json.runId } | Out-Null

  $differentTwoOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-different-two'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  $differentTwo = Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $differentTwoOwner.Json.runId
    Category = 'blocked'
    TaskId = 'task-different-two'
    DetailCode = 'blocked-b'
    BlockingFingerprint = 'fingerprint-b'
  }
  Assert-Equal -Actual $differentTwo.Json.blocking.count -Expected 1 -Message 'Different fingerprint did not reset count'
  Assert-True -Condition (-not [bool]$differentTwo.Json.blocking.pauseRequested) -Message 'Different fingerprint retained pause request'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $differentTwoOwner.Json.runId } | Out-Null

  $refilledOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-refilled'
    Owner = 'queue'
    RepositoryRoot = $repositoryRoot
  }
  $refilled = Invoke-LeaseTool -Action RecordResult -Parameters @{
    StateRoot = $stateRoot
    RunId = $refilledOwner.Json.runId
    Category = 'refilled'
    TaskId = 'task-refilled'
    DetailCode = 'queue-updated'
  }
  Assert-Equal -Actual $refilled.Json.blocking.count -Expected 0 -Message 'Refilled did not reset blocking count'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $refilledOwner.Json.runId } | Out-Null

  $finalShow = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $stateRoot }
  $topLevelNames = @($finalShow.Json.state.PSObject.Properties.Name | Sort-Object)
  $expectedTopLevelNames = @('blocking', 'lastResult', 'lease', 'recovery', 'schemaVersion') | Sort-Object
  Assert-Equal -Actual ($topLevelNames -join ',') -Expected ($expectedTopLevelNames -join ',') -Message 'Runtime state schema has unexpected top-level fields'
  Assert-Equal -Actual $finalShow.Json.state.schemaVersion -Expected 3 -Message 'Runtime schema version mismatch'
  Assert-True -Condition ($null -eq $finalShow.Json.state.lease) -Message 'Final lease was not released'
  Assert-True -Condition ($null -eq $finalShow.Json.state.recovery) -Message 'Final recovery was not cleared'

  $bytes = [IO.File]::ReadAllBytes($statePath)
  $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
  Assert-True -Condition (-not $hasBom) -Message 'Runtime state has UTF-8 BOM'
  $runtimeText = [Text.Encoding]::UTF8.GetString($bytes)
  Assert-True -Condition ($runtimeText -notmatch '"(?i:providerToken|tenantKey|openId|chatId|messageId|eventId|secret)"\s*:') -Message 'Runtime state contains forbidden secret fields'
  Assert-PrivatePathAcl -Path $stateRoot -Directory
  Assert-PrivatePathAcl -Path $statePath
  $temporaryFiles = @(Get-ChildItem -LiteralPath $stateRoot -Force -File | Where-Object { $_.Name -like '*.tmp-*' })
  Assert-Equal -Actual $temporaryFiles.Count -Expected 0 -Message 'Atomic replacement left temporary files behind'

  Write-Output 'test-hourly-automation-lease: OK'
} finally {
  foreach ($cleanupPath in @($stateRoot, $bridgeRoot)) {
    if (-not (Test-Path -LiteralPath $cleanupPath)) {
      continue
    }
    $resolvedCleanup = [IO.Path]::GetFullPath($cleanupPath)
    $resolvedApproved = [IO.Path]::GetFullPath($automationStateRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCleanup.StartsWith($resolvedApproved, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing cleanup outside automation state root: $resolvedCleanup"
    }
    $leaf = Split-Path -Leaf $resolvedCleanup
    if ($leaf -notmatch "^(?:$testId|lease-test-$testId)$") {
      throw "Refusing cleanup of unexpected path: $resolvedCleanup"
    }
    Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
  }
}
