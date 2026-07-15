[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Acquire','Renew','Checkpoint','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure','CreateDecision','RecordDecisionNotification','ResolveDecision','CompleteDecisionFlow','AbortClean','RecordRecoverableInterruption','BlockUnsafe','RepairDecisionFlow','CancelDecision','RollbackDecision','Complete','Show','ResetBlocked')]
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
  [ValidateSet('PROVIDER_ACCEPTED','DELIVERY_FAILED','MISADDRESSED')]
  [string]$NotificationStatus,
  [ValidatePattern('^[0-9a-f]{64}$')]
  [string]$RecipientHash,
  [string]$ProviderMessageId,
  [string]$NotificationError,
  [string]$EvidenceMessageId,
  [string]$EvidenceSender,
  [string]$EvidenceThreadId,
  [string]$EvidenceTurnId,
  [string]$CorrectionReason,
  [string]$CorrectionEvidenceThreadId,
  [string]$CancellationReason,
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
    schemaVersion = 6
    controllerId = $ControllerId
    runId = $null
    runMode = $null
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
    decisionFlow = $null
    lastCompletedDecisionFlow = $null
    auditCorrections = @()
    lastDecisionCancellation = $null
  }
}

function Import-State {
  if (-not (Test-Path -LiteralPath $StatePath)) { return (New-State) }
  $raw = [IO.File]::ReadAllText($StatePath)
  $parsed = $raw | ConvertFrom-Json
  if ($parsed.schemaVersion -notin @(1, 2, 3, 4, 5, 6)) { throw "Unsupported schemaVersion: $($parsed.schemaVersion)" }
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
  Convert-DecisionStateToV6 $state $parsed ([int]$parsed.schemaVersion)
  $state.schemaVersion = 6
  $state
}

function Export-State {
  param([System.Collections.IDictionary]$State)
  $directory = Split-Path -Parent $StatePath
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $temporary = Join-Path $directory ('.state-' + [guid]::NewGuid().ToString('N') + '.tmp')
  $encoding = New-Object Text.UTF8Encoding($false)
  [IO.File]::WriteAllText($temporary, ([pscustomobject]$State | ConvertTo-Json -Depth 10), $encoding)
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

function Get-Sha256Text {
  param([string]$Value)
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
  try {
    ([Security.Cryptography.SHA256]::HashData($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
  } finally {
    [Array]::Clear($bytes, 0, $bytes.Length)
  }
}

function Get-ObjectValue {
  param($Object, [string]$Name, $Default = $null)
  if ($null -eq $Object) { return $Default }
  if ($Object -is [System.Collections.IDictionary]) {
    if ($Object.Contains($Name)) { return $Object[$Name] }
    return $Default
  }
  $property = $Object.PSObject.Properties[$Name]
  if ($null -eq $property) { return $Default }
  $property.Value
}

function Convert-StateTimestamp {
  param($Value)
  if ($null -eq $Value) { return $null }
  if ($Value -is [DateTimeOffset]) { return $Value.ToString('o') }
  if ($Value -is [DateTime]) { return ([DateTimeOffset]$Value).ToString('o') }
  [string]$Value
}

function Convert-DecisionStatusToV6 {
  param([string]$Status)
  switch ($Status) {
    'NOTIFIED' { 'PROVIDER_ACCEPTED' }
    'PROVIDER_ACCEPTED' { 'PROVIDER_ACCEPTED' }
    'DELIVERY_FAILED' { 'DELIVERY_FAILED' }
    'REPLY_INVALID' { 'DELIVERY_FAILED' }
    'MISADDRESSED' { 'MISADDRESSED' }
    'RETRY_EXHAUSTED' { 'RETRY_EXHAUSTED' }
    'RESOLVED' { 'PROVIDER_ACCEPTED' }
    default { 'PENDING' }
  }
}

function Convert-NotificationAttemptsToV6 {
  param($Decision, [int]$SourceSchema)
  $attempts = @()
  $existingAttempts = Get-ObjectValue $Decision 'notificationAttempts'
  if ($SourceSchema -eq 6 -and $null -ne $existingAttempts) {
    foreach ($attempt in @($existingAttempts)) {
      $attempts += [ordered]@{
        attemptedAt = Convert-StateTimestamp (Get-ObjectValue $attempt 'attemptedAt')
        result = Convert-DecisionStatusToV6 ([string](Get-ObjectValue $attempt 'result'))
        recipientHash = Get-ObjectValue $attempt 'recipientHash'
        providerMessageIdHash = Get-ObjectValue $attempt 'providerMessageIdHash'
        errorCategory = Get-ObjectValue $attempt 'errorCategory'
      }
    }
    return @($attempts)
  }
  $legacyNotification = Get-ObjectValue $Decision 'notification'
  if ($null -eq $legacyNotification) { return @() }
  $legacyStatus = [string](Get-ObjectValue $legacyNotification 'status' (Get-ObjectValue $Decision 'status'))
  $attempts += [ordered]@{
    attemptedAt = Convert-StateTimestamp (Get-ObjectValue $legacyNotification 'attemptedAt' (Get-ObjectValue $Decision 'createdAt'))
    result = Convert-DecisionStatusToV6 $legacyStatus
    recipientHash = $null
    providerMessageIdHash = Get-ObjectValue $legacyNotification 'receiptHash'
    errorCategory = Get-ObjectValue $legacyNotification 'error'
  }
  @($attempts)
}

function Convert-PendingDecisionToV6 {
  param($Decision, [int]$SourceSchema)
  if ($null -eq $Decision) { return $null }
  $sourceStatus = [string](Get-ObjectValue $Decision 'status')
  $status = if ($SourceSchema -eq 6) { $sourceStatus } else { Convert-DecisionStatusToV6 $sourceStatus }
  if (@('PENDING','PROVIDER_ACCEPTED','DELIVERY_FAILED','MISADDRESSED','RETRY_EXHAUSTED') -cnotcontains $status) {
    throw "Unsupported pending decision status: $status"
  }
  [ordered]@{
    decisionId = [string](Get-ObjectValue $Decision 'decisionId')
    createdAt = Convert-StateTimestamp (Get-ObjectValue $Decision 'createdAt')
    taskKind = [string](Get-ObjectValue $Decision 'taskKind')
    taskId = [string](Get-ObjectValue $Decision 'taskId')
    taskSummary = [string](Get-ObjectValue $Decision 'taskSummary')
    question = [string](Get-ObjectValue $Decision 'question')
    options = @((Get-ObjectValue $Decision 'options'))
    recommendedOption = [string](Get-ObjectValue $Decision 'recommendedOption')
    impactSummary = [string](Get-ObjectValue $Decision 'impactSummary')
    status = $status
    notificationAttempts = @(Convert-NotificationAttemptsToV6 $Decision $SourceSchema)
  }
}

function New-DecisionFlowValue {
  param($Decision, [string]$Status, [object[]]$ResolvedDecisions)
  [ordered]@{
    taskKind = [string](Get-ObjectValue $Decision 'taskKind')
    taskId = [string](Get-ObjectValue $Decision 'taskId')
    openedAt = Convert-StateTimestamp (Get-ObjectValue $Decision 'createdAt')
    status = $Status
    resolvedDecisions = @($ResolvedDecisions)
  }
}

function Convert-DecisionStateToV6 {
  param([System.Collections.IDictionary]$State, $Parsed, [int]$SourceSchema)
  $State.auditCorrections = @($State.auditCorrections)
  if ($SourceSchema -eq 6) {
    if ($null -ne $State.pendingDecision) {
      $State.pendingDecision = Convert-PendingDecisionToV6 $State.pendingDecision 6
    }
    if ($null -ne $State.decisionFlow) {
      $flow = $State.decisionFlow
      $State.decisionFlow = [ordered]@{
        taskKind = [string](Get-ObjectValue $flow 'taskKind')
        taskId = [string](Get-ObjectValue $flow 'taskId')
        openedAt = Convert-StateTimestamp (Get-ObjectValue $flow 'openedAt')
        status = [string](Get-ObjectValue $flow 'status')
        resolvedDecisions = @((Get-ObjectValue $flow 'resolvedDecisions'))
      }
    }
    if ($null -ne $State.pendingDecision -and $null -eq $State.decisionFlow) {
      throw 'schema v6 pendingDecision requires decisionFlow'
    }
    return
  }

  $legacyDecision = Get-ObjectValue $Parsed 'pendingDecision'
  if ($null -eq $legacyDecision) {
    $State.pendingDecision = $null
    $State.decisionFlow = $null
    return
  }
  $legacyStatus = [string](Get-ObjectValue $legacyDecision 'status')
  $normalized = Convert-PendingDecisionToV6 $legacyDecision $SourceSchema
  if ($legacyStatus -ne 'RESOLVED') {
    $State.pendingDecision = $normalized
    $State.decisionFlow = New-DecisionFlowValue $legacyDecision 'AWAITING_DECISION' @()
    return
  }

  $legacyResolution = Get-ObjectValue $legacyDecision 'resolution'
  $option = [string](Get-ObjectValue $legacyResolution 'optionKey')
  $source = [string](Get-ObjectValue $legacyResolution 'source')
  $resolvedAt = Convert-StateTimestamp (Get-ObjectValue $legacyResolution 'resolvedAt' (Get-ObjectValue $legacyDecision 'createdAt'))
  $normalized['resolution'] = [ordered]@{
    optionKey = $option
    source = $source
    resolvedAt = $resolvedAt
    evidenceHash = Get-Sha256Text "legacy|$([string]$normalized.decisionId)|$option|$source"
  }
  $State.pendingDecision = $null
  $State.decisionFlow = New-DecisionFlowValue $legacyDecision 'IMPLEMENTATION_PENDING' @($normalized)
}

function Clear-RunAndRecovery {
  param([System.Collections.IDictionary]$State)
  $State.state = 'IDLE'
  $State.runId = $null
  $State.runMode = $null
  $State.leaseExpiresAt = $null
  $State.taskKind = $null
  $State.taskId = $null
  $State.taskExecutor = $null
  $State.checkpoint = $null
  $State.expectedPaths = @()
  $State.recoveryBaselinePath = $null
  $State.recoveryEvidencePath = $null
  $State.recoveryEvidenceHash = $null
  $State.recoveryCount = 0
}

function Require-RecoveryInvariant {
  param([System.Collections.IDictionary]$State)

  $taskKindValue = [string]$State['taskKind']
  $taskIdValue = [string]$State['taskId']
  $hasExpectedPath = $false
  foreach ($expectedPathValue in @($State['expectedPaths'])) {
    if (-not [string]::IsNullOrWhiteSpace([string]$expectedPathValue)) {
      $hasExpectedPath = $true
      break
    }
  }
  $baselinePathValue = [string]$State['recoveryBaselinePath']
  $evidencePathValue = [string]$State['recoveryEvidencePath']
  $evidenceHashValue = [string]$State['recoveryEvidenceHash']
  if ([string]::IsNullOrWhiteSpace($taskKindValue) -or
      [string]::IsNullOrWhiteSpace($taskIdValue) -or
      -not $hasExpectedPath -or
      [string]::IsNullOrWhiteSpace($baselinePathValue) -or
      [string]::IsNullOrWhiteSpace($evidencePathValue) -or
      $evidenceHashValue -notmatch '^[0-9a-f]{64}$') {
    Exit-WithCode 'recovery_state_incomplete' $script:ExitInvalidArguments
  }
}

function Clear-DecisionWithAudit {
  param(
    [System.Collections.IDictionary]$State,
    [string]$ExpectedDecisionId,
    [string]$Reason,
    [string]$Source,
    [DateTimeOffset]$At
  )
  Require-PendingDecision $State
  Require-DecisionInput $ExpectedDecisionId 'DecisionId'
  Require-DecisionInput $Reason 'CancellationReason'
  if ($State.pendingDecision.decisionId -cne $ExpectedDecisionId) {
    Exit-WithCode 'DecisionId does not match the pending decision' $script:ExitInvalidArguments
  }
  $summary = $Reason.Trim()
  if ($summary.Length -gt 240) { $summary = $summary.Substring(0, 240) }
  $State.lastDecisionCancellation = [ordered]@{
    decisionId = [string]$State.pendingDecision.decisionId
    taskId = [string]$State.pendingDecision.taskId
    cancelledAt = $At.ToString('o')
    source = $Source
    reason = $summary
  }
  $State.pendingDecision = $null
  if ($null -ne $State.decisionFlow) {
    if (@($State.decisionFlow.resolvedDecisions).Count -eq 0) {
      $State.decisionFlow = $null
    } else {
      $State.decisionFlow.status = 'IMPLEMENTATION_PENDING'
    }
  }
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
      [pscustomobject]$state | ConvertTo-Json -Depth 10
      exit 0
    }
    'Acquire' {
      Require-RunId
      if ($state.state -eq 'AUTO-BLOCKED') { Exit-WithCode 'Controller is AUTO-BLOCKED' $script:ExitBlocked }
      if ($state.leaseExpiresAt) {
        $expires = [DateTimeOffset]::Parse($state.leaseExpiresAt)
        if ($expires -gt $nowValue) { Exit-WithCode 'An active lease already exists' $script:ExitBusy }
      }
      if ($state.state -eq 'RUNNING') { Exit-WithCode 'stale_running_state' $script:ExitInvalidState }
      if ($state.state -notin @('IDLE','RECOVERABLE')) {
        Exit-WithCode "State is not acquirable: $($state.state)" $script:ExitInvalidState
      }
      $isRecoveryAcquire = $state.state -eq 'RECOVERABLE'
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
      $state.runMode = if ($isRecoveryAcquire) { 'recovery' } else { 'fresh' }
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
      if ($null -eq $state.decisionFlow) {
        $state.decisionFlow = [ordered]@{
          taskKind = $TaskKind
          taskId = $TaskId
          openedAt = $nowValue.ToString('o')
          status = 'AWAITING_DECISION'
          resolvedDecisions = @()
        }
      } elseif ([string]$state.decisionFlow.taskId -cne $TaskId) {
        Exit-WithCode 'A decision flow already belongs to another task' $script:ExitInvalidArguments
      } elseif ([string]$state.decisionFlow.taskKind -cne $TaskKind) {
        Exit-WithCode 'TaskKind does not match the existing decision flow' $script:ExitInvalidArguments
      } else {
        $state.decisionFlow.status = 'AWAITING_DECISION'
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
        notificationAttempts = @()
      }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'RecordDecisionNotification' {
      Require-Owner $state
      Require-PendingDecision $state
      Require-DecisionInput $NotificationStatus 'NotificationStatus'
      $attemptCount = @($state.pendingDecision.notificationAttempts).Count
      if ($attemptCount -ge 3 -or $state.pendingDecision.status -eq 'RETRY_EXHAUSTED') {
        Exit-WithCode 'Decision notification retry limit has been reached' $script:ExitInvalidArguments
      }
      if ($state.pendingDecision.status -eq 'PROVIDER_ACCEPTED') {
        Exit-WithCode 'An accepted notification cannot be retried' $script:ExitInvalidArguments
      }
      if ($NotificationStatus -in @('PROVIDER_ACCEPTED','MISADDRESSED')) {
        Require-DecisionInput $RecipientHash 'RecipientHash'
        Require-DecisionInput $ProviderMessageId 'ProviderMessageId'
      }
      if ($NotificationStatus -in @('DELIVERY_FAILED','MISADDRESSED')) {
        Require-DecisionInput $NotificationError 'NotificationError'
      }
      $errorCategory = $null
      if (-not [string]::IsNullOrWhiteSpace($NotificationError)) {
        if ($NotificationError -cnotmatch '^[a-z][a-z0-9_]{0,119}$') {
          Exit-WithCode 'NotificationError must be a symbolic error category' $script:ExitInvalidArguments
        }
        $errorCategory = $NotificationError
      }
      $attempt = [ordered]@{
        attemptedAt = $nowValue.ToString('o')
        result = $NotificationStatus
        recipientHash = if ([string]::IsNullOrWhiteSpace($RecipientHash)) { $null } else { $RecipientHash }
        providerMessageIdHash = if ([string]::IsNullOrWhiteSpace($ProviderMessageId)) { $null } else { Get-Sha256Text $ProviderMessageId.Trim() }
        errorCategory = $errorCategory
      }
      $state.pendingDecision.notificationAttempts = @($state.pendingDecision.notificationAttempts) + $attempt
      if ($NotificationStatus -eq 'PROVIDER_ACCEPTED') {
        $state.pendingDecision.status = 'PROVIDER_ACCEPTED'
      } elseif (@($state.pendingDecision.notificationAttempts).Count -ge 3) {
        $state.pendingDecision.status = 'RETRY_EXHAUSTED'
      } else {
        $state.pendingDecision.status = $NotificationStatus
      }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'ResolveDecision' {
      Require-Owner $state
      Require-PendingDecision $state
      Require-DecisionInput $DecisionId 'DecisionId'
      Require-DecisionInput $OptionKey 'OptionKey'
      Require-DecisionInput $ReplySource 'ReplySource'
      if ($state.pendingDecision.decisionId -cne $DecisionId) { Exit-WithCode 'DecisionId does not match the pending decision' $script:ExitInvalidArguments }
      if (@($state.pendingDecision.options | Where-Object { $_.key -eq $OptionKey }).Count -ne 1) { Exit-WithCode 'OptionKey is not valid for the pending decision' $script:ExitInvalidArguments }
      if ($null -eq $state.decisionFlow -or [string]$state.decisionFlow.taskId -cne [string]$state.pendingDecision.taskId) {
        Exit-WithCode 'Pending decision does not belong to an active decision flow' $script:ExitInvalidState
      }
      $resolution = [ordered]@{
        optionKey = $OptionKey
        source = $ReplySource
        resolvedAt = $nowValue.ToString('o')
        evidenceHash = $null
      }
      if ($ReplySource -eq 'email') {
        Require-DecisionInput $EvidenceMessageId 'EvidenceMessageId'
        Require-DecisionInput $EvidenceSender 'EvidenceSender'
        if ($PSBoundParameters.ContainsKey('EvidenceThreadId') -or $PSBoundParameters.ContainsKey('EvidenceTurnId')) {
          Exit-WithCode 'Email resolution cannot contain manual thread evidence' $script:ExitInvalidArguments
        }
        $messageIdHash = Get-Sha256Text $EvidenceMessageId.Trim()
        $senderHash = Get-Sha256Text $EvidenceSender.Trim()
        $resolution['messageIdHash'] = $messageIdHash
        $resolution['senderHash'] = $senderHash
        $resolution['evidenceHash'] = Get-Sha256Text "email|$messageIdHash|$senderHash"
      } else {
        if (-not $ManualOverride) { Exit-WithCode 'Manual decision resolution requires -ManualOverride' $script:ExitInvalidArguments }
        Require-DecisionInput $EvidenceThreadId 'EvidenceThreadId'
        if ($PSBoundParameters.ContainsKey('EvidenceMessageId') -or $PSBoundParameters.ContainsKey('EvidenceSender')) {
          Exit-WithCode 'Manual resolution cannot contain email evidence' $script:ExitInvalidArguments
        }
        $threadIdHash = Get-Sha256Text $EvidenceThreadId.Trim()
        $turnIdHash = if ([string]::IsNullOrWhiteSpace($EvidenceTurnId)) { $null } else { Get-Sha256Text $EvidenceTurnId.Trim() }
        $resolution['threadIdHash'] = $threadIdHash
        $resolution['turnIdHash'] = $turnIdHash
        $resolution['evidenceHash'] = Get-Sha256Text "manual|$threadIdHash|$turnIdHash"
      }
      $resolvedDecision = [ordered]@{
        decisionId = [string]$state.pendingDecision.decisionId
        createdAt = [string]$state.pendingDecision.createdAt
        taskKind = [string]$state.pendingDecision.taskKind
        taskId = [string]$state.pendingDecision.taskId
        taskSummary = [string]$state.pendingDecision.taskSummary
        question = [string]$state.pendingDecision.question
        options = @($state.pendingDecision.options)
        recommendedOption = [string]$state.pendingDecision.recommendedOption
        impactSummary = [string]$state.pendingDecision.impactSummary
        status = [string]$state.pendingDecision.status
        notificationAttempts = @($state.pendingDecision.notificationAttempts)
        resolution = $resolution
      }
      $state.decisionFlow.resolvedDecisions = @($state.decisionFlow.resolvedDecisions) + $resolvedDecision
      $state.decisionFlow.status = 'IMPLEMENTATION_PENDING'
      $state.pendingDecision = $null
      Set-Lease $state $nowValue
      Export-State $state
    }
    'CompleteDecisionFlow' {
      Require-Owner $state
      Require-DecisionInput $TaskId 'TaskId'
      if ($null -eq $state.decisionFlow) { Exit-WithCode 'No active decision flow exists' $script:ExitInvalidArguments }
      if ($null -ne $state.pendingDecision) { Exit-WithCode 'Pending decision must be resolved before completing its flow' $script:ExitInvalidArguments }
      if ([string]$state.taskId -cne $TaskId -or [string]$state.decisionFlow.taskId -cne $TaskId) {
        Exit-WithCode 'TaskId does not match the current decision flow task' $script:ExitInvalidArguments
      }
      $allResolved = @($state.decisionFlow.resolvedDecisions)
      if ($allResolved.Count -eq 0) { Exit-WithCode 'Decision flow has no resolved decisions' $script:ExitInvalidArguments }
      $summaryItems = @()
      $startIndex = [Math]::Max(0, $allResolved.Count - 20)
      for ($index = $startIndex; $index -lt $allResolved.Count; $index++) {
        $entry = $allResolved[$index]
        $summaryItems += [ordered]@{
          decisionId = [string]$entry.decisionId
          optionKey = [string]$entry.resolution.optionKey
          source = [string]$entry.resolution.source
          resolvedAt = [string]$entry.resolution.resolvedAt
          evidenceHash = [string]$entry.resolution.evidenceHash
        }
      }
      $state.lastCompletedDecisionFlow = [ordered]@{
        taskKind = [string]$state.decisionFlow.taskKind
        taskId = [string]$state.decisionFlow.taskId
        openedAt = [string]$state.decisionFlow.openedAt
        completedAt = $nowValue.ToString('o')
        decisionCount = $allResolved.Count
        resolvedDecisions = @($summaryItems)
      }
      $state.decisionFlow = $null
      Set-Lease $state $nowValue
      Export-State $state
    }
    'CancelDecision' {
      Require-Owner $state
      Require-PendingDecision $state
      if (-not $ManualOverride) { Exit-WithCode 'Manual decision cancellation requires -ManualOverride' $script:ExitInvalidArguments }
      if ($state.pendingDecision.status -eq 'RESOLVED') { Exit-WithCode 'Resolved decisions cannot be cancelled' $script:ExitInvalidArguments }
      Clear-DecisionWithAudit $state $DecisionId $CancellationReason 'manual' $nowValue
      Set-Lease $state $nowValue
      Export-State $state
    }
    'RollbackDecision' {
      Require-Owner $state
      Require-PendingDecision $state
      if ($state.pendingDecision.status -ne 'PENDING') { Exit-WithCode 'Only a pending unpublished decision can be rolled back' $script:ExitInvalidArguments }
      Clear-DecisionWithAudit $state $DecisionId $CancellationReason 'controller_rollback' $nowValue
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Complete' {
      Require-Owner $state
      if ($QueueAuditCompleted) { $state.lastQueueAuditAt = $nowValue.ToString('o') }
      Clear-RunAndRecovery $state
      $state.lastError = $null
      Export-State $state
    }
    'AbortClean' {
      Require-Owner $state
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'ErrorMessage is required' $script:ExitInvalidArguments }
      $state.lastError = $ErrorMessage
      Clear-RunAndRecovery $state
      Export-State $state
    }
    'RecordRecoverableInterruption' {
      Require-Owner $state
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'ErrorMessage is required' $script:ExitInvalidArguments }
      if ($PSBoundParameters.ContainsKey('RecoveryBaselinePath')) { $state.recoveryBaselinePath = $RecoveryBaselinePath }
      if ($PSBoundParameters.ContainsKey('RecoveryEvidencePath')) { $state.recoveryEvidencePath = $RecoveryEvidencePath }
      if ($PSBoundParameters.ContainsKey('RecoveryEvidenceHash')) { $state.recoveryEvidenceHash = $RecoveryEvidenceHash }
      Require-RecoveryInvariant $state
      $state.lastError = $ErrorMessage
      if ($WasRecovery) { $state.recoveryCount = [int]$state.recoveryCount + 1 }
      if ([int]$state.recoveryCount -ge 2) {
        $state.state = 'AUTO-BLOCKED'
      } else {
        $state.state = 'RECOVERABLE'
      }
      $state.runId = $null
      $state.runMode = $null
      $state.leaseExpiresAt = $null
      Export-State $state
    }
    'BlockUnsafe' {
      Require-Owner $state
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'ErrorMessage is required' $script:ExitInvalidArguments }
      $state.state = 'AUTO-BLOCKED'
      $state.runId = $null
      $state.runMode = $null
      $state.leaseExpiresAt = $null
      $state.lastError = $ErrorMessage
      Export-State $state
    }
    'RepairDecisionFlow' {
      Exit-WithCode 'RepairDecisionFlow requires the operator repair tool' $script:ExitInvalidState
    }
    'ResetBlocked' {
      if ($state.state -ne 'AUTO-BLOCKED') { Exit-WithCode 'State is not AUTO-BLOCKED' $script:ExitInvalidArguments }
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'A manual reset reason is required' $script:ExitInvalidArguments }
      Clear-RunAndRecovery $state
      $state.lastError = "Manual reset: $ErrorMessage"
      Export-State $state
    }
  }
  [pscustomobject]$state | ConvertTo-Json -Depth 10
} finally {
  if ($null -ne $guard) { $guard.Dispose() }
}
