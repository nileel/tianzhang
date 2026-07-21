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

$automationStateRoot = Join-Path $env:USERPROFILE '.codex\automation-state'
$testId = [Guid]::NewGuid().ToString('N')
$stateRoot = Join-Path $automationStateRoot "tzg-hourly-controller-lease-tests\$testId"
$bridgeRoot = Join-Path $automationStateRoot "tzg-feishu-decision-bridge\lease-test-$testId"
$statePath = Join-Path $stateRoot 'runtime.json'
$requestPath = Join-Path $bridgeRoot 'decision-request.json'
$replyOnePath = Join-Path $bridgeRoot 'reply-one.json'
$replyTwoPath = Join-Path $bridgeRoot 'reply-two.json'

try {
  [IO.Directory]::CreateDirectory($bridgeRoot) | Out-Null
  Set-PrivatePathAcl -Path $bridgeRoot -Directory
  Assert-PrivatePathAcl -Path $bridgeRoot -Directory
  foreach ($fixturePath in @($requestPath, $replyOnePath, $replyTwoPath)) {
    [IO.File]::WriteAllText($fixturePath, '{"fixture":true}', [Text.UTF8Encoding]::new($false))
    Set-PrivatePathAcl -Path $fixturePath
    Assert-PrivatePathAcl -Path $fixturePath
  }

  $relativeState = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = 'relative-state' } -AllowedExitCodes @(2)
  Assert-Equal -Actual $relativeState.Json.status -Expected 'INVALID_ARGUMENT' -Message 'Relative state root must be rejected'

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
  Assert-RejectedWithoutStateChange -Action SaveRecovery -StatePath $statePath -ExpectedStatus 'INVALID_ARGUMENT' -Parameters @{
    StateRoot = $stateRoot
    RunId = $recoveryOwner.Json.runId
    DecisionId = 'decision-recovery-only'
    DecisionRequestPath = $requestPath
    CodexThreadId = 'thread-recovery-only'
    ClaudeSessionId = 'session-recovery-only'
  }
  $savedRecovery = Invoke-LeaseTool -Action SaveRecovery -Parameters @{
    StateRoot = $stateRoot
    RunId = $recoveryOwner.Json.runId
    DecisionId = 'decision-recovery-only'
    DecisionRequestPath = $requestPath
    CodexThreadId = 'thread-recovery-only'
    HasUncommittedChanges = $true
    ChangedPaths = @('tools/recovery-one.txt', 'tools/recovery-two.txt')
  }
  Assert-Equal -Actual $savedRecovery.Json.status -Expected 'RECOVERY_SAVED' -Message 'Codex recovery was not saved'
  Start-Sleep -Milliseconds 1200
  $recoveryOnly = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-must-not-overwrite-recovery'
    Owner = 'external'
    RepositoryRoot = $repositoryRoot
  }
  Assert-Equal -Actual $recoveryOnly.Json.status -Expected 'RECOVERY_ONLY' -Message 'Uncommitted recovery did not block normal acquire'
  Assert-Equal -Actual $recoveryOnly.Json.taskId -Expected 'task-recovery-only' -Message 'Recovery-only response lost original task'
  Invoke-LeaseTool -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $recoveryOwner.Json.runId } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $recoveryOwner.Json.runId } | Out-Null

  $claudeOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-claude-recovery'
    Owner = 'external'
    RepositoryRoot = $repositoryRoot
  }
  $claudeRecovery = Invoke-LeaseTool -Action SaveRecovery -Parameters @{
    StateRoot = $stateRoot
    RunId = $claudeOwner.Json.runId
    DecisionId = 'decision-claude'
    DecisionRequestPath = $requestPath
    ClaudeSessionId = 'session-claude'
  }
  Assert-Equal -Actual $claudeRecovery.Json.recovery.resumeKind -Expected 'claude' -Message 'Claude recovery kind mismatch'
  Assert-Equal -Actual $claudeRecovery.Json.recovery.resumeId -Expected 'session-claude' -Message 'Claude recovery id mismatch'
  Invoke-LeaseTool -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $claudeOwner.Json.runId } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $claudeOwner.Json.runId } | Out-Null

  $resumeOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-resume'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  Invoke-LeaseTool -Action SaveRecovery -Parameters @{
    StateRoot = $stateRoot
    RunId = $resumeOwner.Json.runId
    DecisionId = 'decision-resume'
    DecisionRequestPath = $requestPath
    CodexThreadId = 'thread-resume'
  } | Out-Null
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $resumeOwner.Json.runId } | Out-Null

  $clearWithRecovery = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $clearWithRecovery.Json.status -Expected 'RECOVERY_PRESENT' -Message 'ClearBlocking ignored recovery'

  $busyOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-busy'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
  }
  $queued = Invoke-LeaseTool -Action QueueResume -Parameters @{
    StateRoot = $stateRoot
    DecisionId = 'decision-resume'
    ReplyPath = $replyOnePath
  }
  Assert-Equal -Actual $queued.Json.status -Expected 'QUEUED' -Message 'Reply was not queued while lease was busy'
  $queuedAgain = Invoke-LeaseTool -Action QueueResume -Parameters @{
    StateRoot = $stateRoot
    DecisionId = 'decision-resume'
    ReplyPath = $replyOnePath
  }
  Assert-Equal -Actual $queuedAgain.Json.status -Expected 'QUEUED' -Message 'Duplicate queued reply returned wrong status'
  Assert-True -Condition ([bool]$queuedAgain.Json.duplicate) -Message 'Duplicate queued reply was not identified'
  $showQueued = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual @($showQueued.Json.state.pendingResumes).Count -Expected 1 -Message 'Duplicate reply was queued twice'
  $releaseBusy = Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $busyOwner.Json.runId }
  Assert-True -Condition ([bool]$releaseBusy.Json.readyResume) -Message 'Release did not report ready resume'

  $clearWithPending = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $clearWithPending.Json.status -Expected 'PENDING_RESUMES' -Message 'ClearBlocking ignored pending resumes'

  $taken = Invoke-LeaseTool -Action TakeResume -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $taken.Json.status -Expected 'DISPATCH' -Message 'TakeResume did not dispatch queued reply'
  Assert-Equal -Actual $taken.Json.taskId -Expected 'task-resume' -Message 'TakeResume acquired wrong task'
  Assert-Equal -Actual $taken.Json.replyPath -Expected ([IO.Path]::GetFullPath($replyOnePath)) -Message 'TakeResume returned wrong reply path'
  $takeAgain = Invoke-LeaseTool -Action TakeResume -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $takeAgain.Json.status -Expected 'BUSY' -Message 'TakeResume dispatched more than once'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $taken.Json.runId } | Out-Null

  $directDispatch = Invoke-LeaseTool -Action QueueResume -Parameters @{
    StateRoot = $stateRoot
    DecisionId = 'decision-resume'
    ReplyPath = $replyTwoPath
  }
  Assert-Equal -Actual $directDispatch.Json.status -Expected 'DISPATCH' -Message 'QueueResume without lease did not dispatch'
  Assert-Equal -Actual $directDispatch.Json.taskId -Expected 'task-resume' -Message 'Direct dispatch acquired wrong task'
  Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $directDispatch.Json.runId } | Out-Null

  $clearOwner = Invoke-LeaseTool -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-resume'
    Owner = 'codex'
    RepositoryRoot = $repositoryRoot
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
  $expectedTopLevelNames = @('blocking', 'lastResult', 'lease', 'pendingResumes', 'recovery', 'schemaVersion') | Sort-Object
  Assert-Equal -Actual ($topLevelNames -join ',') -Expected ($expectedTopLevelNames -join ',') -Message 'Runtime state schema has unexpected top-level fields'
  Assert-Equal -Actual $finalShow.Json.state.schemaVersion -Expected 1 -Message 'Runtime schema version mismatch'
  Assert-True -Condition ($null -eq $finalShow.Json.state.lease) -Message 'Final lease was not released'
  Assert-True -Condition ($null -eq $finalShow.Json.state.recovery) -Message 'Final recovery was not cleared'
  Assert-Equal -Actual @($finalShow.Json.state.pendingResumes).Count -Expected 0 -Message 'Final pending resume queue was not empty'

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
