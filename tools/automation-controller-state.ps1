[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Acquire','Renew','Checkpoint','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure','CreateDecision','MarkDecisionNotified','MarkDecisionDeliveryFailed','ResolveDecision','ClearResolvedDecision','Complete','Fail','Show','ResetBlocked')]
  [string]$Action,
  [string]$StatePath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json",
  [string]$ControllerId = 'tzg-hourly-controller',
  [string]$RunId,
  [ValidateSet('recovery','review','maintenance','execute')]
  [string]$TaskKind,
  [string]$TaskId,
  [ValidateSet('codex','deepseek')]
  [string]$TaskExecutor,
  [ValidateSet('identity_checked','queues_loaded','task_selected','mutation_started','verification_completed','commit_completed')]
  [string]$Checkpoint,
  [string]$ExpectedPaths,
  [string]$RecoveryBaselinePath,
  [string]$RecoveryEvidencePath,
  [ValidatePattern('^[0-9a-f]{64}$')]
  [string]$RecoveryEvidenceHash,
  [string]$TaskSummary,
  [string]$DecisionQuestion,
  [string]$DecisionOptions,
  [string]$RecommendedOption,
  [string]$ImpactSummary,
  [string]$DecisionId,
  [string]$OptionKey,
  [ValidateSet('email','manual')]
  [string]$ReplySource,
  [switch]$ManualOverride,
  [string]$NotificationError,
  [switch]$WasRecovery,
  [switch]$QueueAuditCompleted,
  [string]$QueueFingerprint,
  [int]$RunnableCount = -1,
  [switch]$NoCandidate,
  [ValidateSet('deepseek')]
  [string]$WorkerId,
  [string]$WorkerError,
  [ValidateRange(1, 1440)]
  [int]$BackoffMinutes = 180,
  [string]$ErrorMessage,
  [int]$LeaseMinutes = 180,
  [string]$Now
)

$ErrorActionPreference = 'Stop'
$script:ExitBusy = 10
$script:ExitBlocked = 11
$script:ExitOwnerMismatch = 12
$script:ExitInvalidState = 13
$script:ExitLockContention = 14
$script:ExitInvalidArguments = 15

function Exit-WithCode {
  param([string]$Message, [int]$Code)
  [Console]::Error.WriteLine($Message)
  exit $Code
}

function Get-NowValue {
  if ([string]::IsNullOrWhiteSpace($Now)) { return [DateTimeOffset]::UtcNow }
  [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture)
}

function New-State {
  [ordered]@{
    schemaVersion = 4
    controllerId = $ControllerId
    runId = $null
    state = 'IDLE'
    leaseExpiresAt = $null
    taskKind = $null
    taskId = $null
    taskExecutor = $null
    checkpoint = $null
    expectedPaths = @()
    recoveryBaselinePath = $null
    recoveryEvidencePath = $null
    recoveryEvidenceHash = $null
    recoveryCount = 0
    lastQueueAuditAt = $null
    lastQueueFingerprint = $null
    lastNoCandidateFingerprint = $null
    lastRunnableCount = $null
    workerState = [ordered]@{
      deepseek = [ordered]@{
        failureCount = 0
        backoffUntil = $null
        lastError = $null
      }
    }
    lastError = $null
    pendingDecision = $null
  }
}

function Import-State {
  if (-not (Test-Path -LiteralPath $StatePath)) { return (New-State) }
  $raw = [IO.File]::ReadAllText($StatePath)
  $parsed = $raw | ConvertFrom-Json
  if ($parsed.schemaVersion -notin @(1, 2, 3, 4)) { throw "Unsupported schemaVersion: $($parsed.schemaVersion)" }
  $state = New-State
  foreach ($key in @($state.Keys)) {
    $property = $parsed.PSObject.Properties[$key]
    if ($null -ne $property) { $state[$key] = $property.Value }
  }
  $deepseek = [ordered]@{ failureCount = 0; backoffUntil = $null; lastError = $null }
  $workerStateProperty = $parsed.PSObject.Properties['workerState']
  if ($null -ne $workerStateProperty -and $null -ne $workerStateProperty.Value) {
    $deepseekProperty = $workerStateProperty.Value.PSObject.Properties['deepseek']
    if ($null -ne $deepseekProperty -and $null -ne $deepseekProperty.Value) {
      foreach ($key in @($deepseek.Keys)) {
        $property = $deepseekProperty.Value.PSObject.Properties[$key]
        if ($null -ne $property) { $deepseek[$key] = $property.Value }
      }
    }
  }
  $state.workerState = [ordered]@{ deepseek = $deepseek }
  $state.schemaVersion = 4
  $state
}

function Export-State {
  param([System.Collections.IDictionary]$State)
  $directory = Split-Path -Parent $StatePath
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $temporary = Join-Path $directory ('.state-' + [guid]::NewGuid().ToString('N') + '.tmp')
  $encoding = New-Object Text.UTF8Encoding($false)
  [IO.File]::WriteAllText($temporary, ([pscustomobject]$State | ConvertTo-Json -Depth 6), $encoding)
  if (Test-Path -LiteralPath $StatePath) {
    $backup = "$StatePath.backup"
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    [IO.File]::Replace($temporary, $StatePath, $backup, $true)
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
  } else {
    [IO.File]::Move($temporary, $StatePath)
  }
}

function Require-RunId {
  if ([string]::IsNullOrWhiteSpace($RunId)) { Exit-WithCode 'RunId is required' $script:ExitInvalidArguments }
}

function Require-Owner {
  param([System.Collections.IDictionary]$State)
  Require-RunId
  if ($State.runId -ne $RunId) { Exit-WithCode 'RunId does not own the lease' $script:ExitOwnerMismatch }
}

function Set-Lease {
  param([System.Collections.IDictionary]$State, [DateTimeOffset]$At)
  $State.leaseExpiresAt = $At.AddMinutes($LeaseMinutes).ToString('o')
}

function Require-PendingDecision {
  param([System.Collections.IDictionary]$State)
  if ($null -eq $State.pendingDecision) { Exit-WithCode 'No pending decision exists' $script:ExitInvalidArguments }
}

function Get-DecisionOptions {
  param([string]$Value)
  $entries = @($Value -split '\|')
  if ($entries.Count -lt 2) { Exit-WithCode 'DecisionOptions requires at least two keyed options' $script:ExitInvalidArguments }
  $options = @()
  foreach ($entry in $entries) {
    $separator = $entry.IndexOf('=')
    if ($separator -le 0 -or $separator -eq $entry.Length - 1) {
      Exit-WithCode 'DecisionOptions entries must use key=label format' $script:ExitInvalidArguments
    }
    $key = $entry.Substring(0, $separator).Trim()
    $label = $entry.Substring($separator + 1).Trim()
    if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($label)) {
      Exit-WithCode 'DecisionOptions entries require non-empty key and label values' $script:ExitInvalidArguments
    }
    $options += [ordered]@{ key = $key; label = $label }
  }
  $keys = @($options | ForEach-Object { [string]$_.key })
  if (@($keys | Sort-Object -Unique).Count -ne $keys.Count) { Exit-WithCode 'Option keys must be unique' $script:ExitInvalidArguments }
  $options
}

function Require-DecisionInput {
  param([string]$Value, [string]$Name)
  if ([string]::IsNullOrWhiteSpace($Value)) { Exit-WithCode "$Name is required" $script:ExitInvalidArguments }
}

function Get-NotificationAttemptCount {
  param($Notification)
  if ($null -eq $Notification -or $null -eq $Notification.attempts) { return 0 }
  [int]$Notification.attempts
}

$nowValue = Get-NowValue
$directory = Split-Path -Parent $StatePath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$guardPath = "$StatePath.guard"
$guard = $null
try {
  try {
    $guard = [IO.File]::Open($guardPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
  } catch [IO.IOException] {
    Exit-WithCode 'State transaction lock is busy' $script:ExitLockContention
  }

  try { $state = Import-State } catch {
    Exit-WithCode "Invalid state file: $($_.Exception.Message)" $script:ExitInvalidState
  }

  switch ($Action) {
    'Show' {
      [pscustomobject]$state | ConvertTo-Json -Depth 6
      exit 0
    }
    'Acquire' {
      Require-RunId
      if ($state.state -eq 'AUTO-BLOCKED') { Exit-WithCode 'Controller is AUTO-BLOCKED' $script:ExitBlocked }
      if ($state.state -eq 'RUNNING' -and $state.leaseExpiresAt) {
        $expires = [DateTimeOffset]::Parse($state.leaseExpiresAt)
        if ($expires -gt $nowValue) { Exit-WithCode 'An active lease already exists' $script:ExitBusy }
      }
      if ($state.state -eq 'IDLE') {
        $state.taskKind = $null
        $state.taskId = $null
        $state.taskExecutor = $null
        $state.checkpoint = $null
        $state.expectedPaths = @()
        $state.recoveryBaselinePath = $null
        $state.recoveryEvidencePath = $null
        $state.recoveryEvidenceHash = $null
        $state.recoveryCount = 0
        $state.lastError = $null
      }
      $state.controllerId = $ControllerId
      $state.runId = $RunId
      $state.state = 'RUNNING'
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Renew' {
      Require-Owner $state
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Checkpoint' {
      Require-Owner $state
      if ($TaskKind) { $state.taskKind = $TaskKind }
      if ($TaskId) { $state.taskId = $TaskId }
      if ($PSBoundParameters.ContainsKey('TaskExecutor')) { $state.taskExecutor = $TaskExecutor }
      if ($Checkpoint) { $state.checkpoint = $Checkpoint }
      if ($PSBoundParameters.ContainsKey('ExpectedPaths')) {
        $paths = @($ExpectedPaths -split '\|' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $state.expectedPaths = @($paths | ForEach-Object { ([string]$_).Replace('\','/') } | Sort-Object -Unique)
      }
      if ($PSBoundParameters.ContainsKey('RecoveryBaselinePath')) {
        if ([string]::IsNullOrWhiteSpace($RecoveryBaselinePath)) { Exit-WithCode 'RecoveryBaselinePath must not be empty' $script:ExitInvalidArguments }
        $state.recoveryBaselinePath = $RecoveryBaselinePath
      }
      $hasEvidencePath = $PSBoundParameters.ContainsKey('RecoveryEvidencePath')
      $hasEvidenceHash = $PSBoundParameters.ContainsKey('RecoveryEvidenceHash')
      if ($hasEvidencePath -xor $hasEvidenceHash) { Exit-WithCode 'RecoveryEvidencePath and RecoveryEvidenceHash must be provided together' $script:ExitInvalidArguments }
      if ($hasEvidencePath) {
        if ([string]::IsNullOrWhiteSpace($RecoveryEvidencePath)) { Exit-WithCode 'RecoveryEvidencePath must not be empty' $script:ExitInvalidArguments }
        if ([string]::IsNullOrWhiteSpace([string]$state.recoveryBaselinePath)) { Exit-WithCode 'RecoveryBaselinePath must be set before recovery evidence' $script:ExitInvalidArguments }
        $state.recoveryEvidencePath = $RecoveryEvidencePath
        $state.recoveryEvidenceHash = $RecoveryEvidenceHash
      }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'RecordQueueState' {
      Require-Owner $state
      if ([string]::IsNullOrWhiteSpace($QueueFingerprint)) { Exit-WithCode 'QueueFingerprint is required' $script:ExitInvalidArguments }
      if ($RunnableCount -lt 0) { Exit-WithCode 'RunnableCount must be at least zero' $script:ExitInvalidArguments }
      $state.lastQueueFingerprint = $QueueFingerprint
      $state.lastRunnableCount = $RunnableCount
      $state.lastNoCandidateFingerprint = if ($NoCandidate) { $QueueFingerprint } else { $null }
      if ($QueueAuditCompleted) { $state.lastQueueAuditAt = $nowValue.ToString('o') }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'RecordWorkerFailure' {
      Require-Owner $state
      if ($WorkerId -ne 'deepseek') { Exit-WithCode 'WorkerId is required' $script:ExitInvalidArguments }
      if ([string]::IsNullOrWhiteSpace($WorkerError)) { Exit-WithCode 'WorkerError is required' $script:ExitInvalidArguments }
      $errorSummary = $WorkerError.Trim()
      if ($errorSummary.Length -gt 240) { $errorSummary = $errorSummary.Substring(0, 240) }
      $state.workerState.deepseek.failureCount = [int]$state.workerState.deepseek.failureCount + 1
      $state.workerState.deepseek.backoffUntil = $nowValue.AddMinutes($BackoffMinutes).ToString('o')
      $state.workerState.deepseek.lastError = $errorSummary
      Set-Lease $state $nowValue
      Export-State $state
    }
    'ClearWorkerFailure' {
      Require-Owner $state
      if ($WorkerId -ne 'deepseek') { Exit-WithCode 'WorkerId is required' $script:ExitInvalidArguments }
      $state.workerState.deepseek.failureCount = 0
      $state.workerState.deepseek.backoffUntil = $null
      $state.workerState.deepseek.lastError = $null
      Set-Lease $state $nowValue
      Export-State $state
    }
    'CreateDecision' {
      Require-Owner $state
      if ($null -ne $state.pendingDecision) { Exit-WithCode 'A pending decision already exists' $script:ExitInvalidArguments }
      Require-DecisionInput $TaskKind 'TaskKind'
      Require-DecisionInput $TaskId 'TaskId'
      Require-DecisionInput $TaskSummary 'TaskSummary'
      Require-DecisionInput $DecisionQuestion 'DecisionQuestion'
      Require-DecisionInput $DecisionOptions 'DecisionOptions'
      Require-DecisionInput $RecommendedOption 'RecommendedOption'
      Require-DecisionInput $ImpactSummary 'ImpactSummary'
      $options = Get-DecisionOptions $DecisionOptions
      if (@($options | Where-Object { $_.key -eq $RecommendedOption }).Count -ne 1) {
        Exit-WithCode 'RecommendedOption must match exactly one option key' $script:ExitInvalidArguments
      }
      $state.pendingDecision = [ordered]@{
        decisionId = "DEC-$($nowValue.UtcDateTime.ToString('yyyyMMdd'))-$(([guid]::NewGuid().ToString('N').Substring(0, 12)).ToUpperInvariant())"
        createdAt = $nowValue.ToString('o')
        taskKind = $TaskKind
        taskId = $TaskId
        taskSummary = $TaskSummary
        question = $DecisionQuestion
        options = $options
        recommendedOption = $RecommendedOption
        impactSummary = $ImpactSummary
        status = 'PENDING'
        notification = $null
        resolution = $null
      }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'MarkDecisionNotified' {
      Require-Owner $state
      Require-PendingDecision $state
      if ($state.pendingDecision.status -notin @('PENDING', 'DELIVERY_FAILED')) { Exit-WithCode 'Decision cannot be marked notified in its current status' $script:ExitInvalidArguments }
      $state.pendingDecision.status = 'NOTIFIED'
      $attempts = (Get-NotificationAttemptCount $state.pendingDecision.notification) + 1
      $state.pendingDecision.notification = [ordered]@{ status = 'NOTIFIED'; attemptedAt = $nowValue.ToString('o'); attempts = $attempts; error = $null }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'MarkDecisionDeliveryFailed' {
      Require-Owner $state
      Require-PendingDecision $state
      Require-DecisionInput $NotificationError 'NotificationError'
      if ($state.pendingDecision.status -eq 'RESOLVED') { Exit-WithCode 'Resolved decision cannot be marked failed' $script:ExitInvalidArguments }
      $errorSummary = $NotificationError.Trim()
      if ($errorSummary.Length -gt 240) { $errorSummary = $errorSummary.Substring(0, 240) }
      $status = if ($errorSummary -eq 'invalid_reply') { 'REPLY_INVALID' } else { 'DELIVERY_FAILED' }
      $state.pendingDecision.status = $status
      $attempts = (Get-NotificationAttemptCount $state.pendingDecision.notification) + 1
      $state.pendingDecision.notification = [ordered]@{ status = $status; attemptedAt = $nowValue.ToString('o'); attempts = $attempts; error = $errorSummary }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'ResolveDecision' {
      Require-Owner $state
      Require-PendingDecision $state
      Require-DecisionInput $DecisionId 'DecisionId'
      Require-DecisionInput $OptionKey 'OptionKey'
      Require-DecisionInput $ReplySource 'ReplySource'
      if ($ReplySource -eq 'manual' -and -not $ManualOverride) { Exit-WithCode 'Manual decision resolution requires -ManualOverride' $script:ExitInvalidArguments }
      if ($state.pendingDecision.decisionId -ne $DecisionId) { Exit-WithCode 'DecisionId does not match the pending decision' $script:ExitInvalidArguments }
      if ($state.pendingDecision.status -notin @('NOTIFIED', 'DELIVERY_FAILED', 'REPLY_INVALID')) { Exit-WithCode 'Decision cannot be resolved in its current status' $script:ExitInvalidArguments }
      if (@($state.pendingDecision.options | Where-Object { $_.key -eq $OptionKey }).Count -ne 1) { Exit-WithCode 'OptionKey is not valid for the pending decision' $script:ExitInvalidArguments }
      $state.pendingDecision.status = 'RESOLVED'
      $state.pendingDecision.resolution = [ordered]@{ optionKey = $OptionKey; source = $ReplySource; resolvedAt = $nowValue.ToString('o') }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'ClearResolvedDecision' {
      Require-Owner $state
      Require-PendingDecision $state
      if ($state.pendingDecision.status -ne 'RESOLVED') { Exit-WithCode 'Only a resolved decision can be cleared' $script:ExitInvalidArguments }
      $state.pendingDecision = $null
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Complete' {
      Require-Owner $state
      if ($QueueAuditCompleted) { $state.lastQueueAuditAt = $nowValue.ToString('o') }
      $state.state = 'IDLE'
      $state.runId = $null
      $state.leaseExpiresAt = $null
      $state.taskKind = $null
      $state.taskId = $null
      $state.taskExecutor = $null
      $state.checkpoint = $null
      $state.expectedPaths = @()
      $state.recoveryBaselinePath = $null
      $state.recoveryEvidencePath = $null
      $state.recoveryEvidenceHash = $null
      $state.recoveryCount = 0
      $state.lastError = $null
      Export-State $state
    }
    'Fail' {
      Require-Owner $state
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'ErrorMessage is required' $script:ExitInvalidArguments }
      $state.lastError = $ErrorMessage
      if ($WasRecovery) { $state.recoveryCount = [int]$state.recoveryCount + 1 }
      if ([int]$state.recoveryCount -ge 2) {
        $state.state = 'AUTO-BLOCKED'
        $state.leaseExpiresAt = $null
      } else {
        $state.state = 'RUNNING'
        $state.leaseExpiresAt = $nowValue.ToString('o')
      }
      Export-State $state
    }
    'ResetBlocked' {
      if ($state.state -ne 'AUTO-BLOCKED') { Exit-WithCode 'State is not AUTO-BLOCKED' $script:ExitInvalidArguments }
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'A manual reset reason is required' $script:ExitInvalidArguments }
      $state.state = 'IDLE'
      $state.runId = $null
      $state.leaseExpiresAt = $null
      $state.taskKind = $null
      $state.taskId = $null
      $state.taskExecutor = $null
      $state.checkpoint = $null
      $state.expectedPaths = @()
      $state.recoveryBaselinePath = $null
      $state.recoveryEvidencePath = $null
      $state.recoveryEvidenceHash = $null
      $state.recoveryCount = 0
      $state.lastError = "Manual reset: $ErrorMessage"
      Export-State $state
    }
  }
  [pscustomobject]$state | ConvertTo-Json -Depth 6
} finally {
  if ($null -ne $guard) { $guard.Dispose() }
}
