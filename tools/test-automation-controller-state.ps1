$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-controller-state.ps1'
$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ("tzg-state-test-" + [guid]::NewGuid().ToString('N'))
$statePath = Join-Path $sandbox 'state.json'
$engine = (Get-Process -Id $PID).Path

function Invoke-StateTool {
  param([string[]]$Arguments)
  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $tool @Arguments 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)
  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function Remove-AnsiCsi {
  param([string]$Text)
  [regex]::Replace($Text, '\x1B\[[0-?]*[ -/]*[@-~]', '')
}

function Read-TestState {
  (Invoke-StateTool @('Show', '-StatePath', $statePath)).Output | ConvertFrom-Json
}

function New-Schema6PendingFixture {
  param([string]$Status)
  @{
    schemaVersion = 6
    controllerId = 'schema6-validation'
    state = 'IDLE'
    pendingDecision = @{
      decisionId = 'DEC-SCHEMA6-INVALID'
      createdAt = '2026-07-11T05:30:00Z'
      taskKind = 'execute'
      taskId = 'schema6-validation-task'
      taskSummary = 'Validate schema v6 status'
      question = 'Is this pending status valid?'
      options = @(@{ key = 'A'; label = 'Yes' }, @{ key = 'B'; label = 'No' })
      recommendedOption = 'B'
      impactSummary = 'Strict import validation'
      status = $Status
      notificationAttempts = @()
    }
    decisionFlow = @{
      taskKind = 'execute'
      taskId = 'schema6-validation-task'
      openedAt = '2026-07-11T05:30:00Z'
      status = 'AWAITING_DECISION'
      resolvedDecisions = @()
    }
  } | ConvertTo-Json -Depth 7 -Compress
}

New-Item -ItemType Directory -Path $sandbox | Out-Null
try {
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-1', '-Now', '2026-07-11T00:00:00Z')
  Assert-Code $r 0 'first acquire'
  $firstAcquire = Read-TestState
  if ($firstAcquire.state -ne 'RUNNING' -or $firstAcquire.runMode -ne 'fresh') { throw 'first acquire did not set RUNNING fresh mode' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-2', '-Now', '2026-07-11T00:10:00Z')
  Assert-Code $r 10 'active lease rejection'

  $r = Invoke-StateTool @('Renew', '-StatePath', $statePath, '-RunId', 'wrong-run', '-Now', '2026-07-11T00:20:00Z')
  Assert-Code $r 12 'owner mismatch'

  $r = Invoke-StateTool @('Renew', '-StatePath', $statePath, '-RunId', 'run-1', '-Now', '2026-07-11T00:30:00Z')
  Assert-Code $r 0 'owner renew'
  if ((Read-TestState).leaseExpiresAt -ne '2026-07-11T03:30:00.0000000+00:00') { throw 'renew did not extend the lease by three hours' }

  $r = Invoke-StateTool @('Checkpoint', '-StatePath', $statePath, '-RunId', 'run-1', '-TaskKind', 'execute', '-TaskId', 'sample-task', '-TaskExecutor', 'codex', '-Checkpoint', 'verification_completed', '-ExpectedPaths', 'a.txt|b/c.txt', '-RecoveryBaselinePath', 'C:\state\baseline.json', '-RecoveryEvidencePath', 'C:\state\evidence.json', '-RecoveryEvidenceHash', ('a' * 64), '-Now', '2026-07-11T00:40:00Z')
  Assert-Code $r 0 'checkpoint'
  $state = Read-TestState
  if ($state.taskId -ne 'sample-task' -or $state.expectedPaths.Count -ne 2) { throw 'checkpoint fields were not persisted' }
  if ($state.schemaVersion -ne 6 -or $state.taskExecutor -ne 'codex' -or $state.recoveryBaselinePath -ne 'C:\state\baseline.json' -or $state.recoveryEvidencePath -ne 'C:\state\evidence.json' -or $state.recoveryEvidenceHash -ne ('a' * 64)) { throw 'checkpoint did not persist schema v6 recovery evidence fields' }
  if ($state.runMode -ne 'fresh' -or $null -ne $state.decisionFlow -or $null -ne $state.lastCompletedDecisionFlow -or $state.auditCorrections.Count -ne 0) { throw 'new schema v6 top-level fields were not initialized safely' }

  $r = Invoke-StateTool @('Checkpoint', '-StatePath', $statePath, '-RunId', 'run-1', '-TaskExecutor', 'deepseek', '-Now', '2026-07-11T00:40:30Z')
  Assert-Code $r 0 'checkpoint DeepSeek executor'
  if ((Read-TestState).taskExecutor -ne 'deepseek') { throw 'checkpoint did not persist the DeepSeek task executor' }

  $r = Invoke-StateTool @('RecordQueueState', '-StatePath', $statePath, '-RunId', 'wrong-run', '-QueueFingerprint', 'queue-a', '-RunnableCount', '0', '-Now', '2026-07-11T00:40:40Z')
  Assert-Code $r 12 'queue state owner mismatch'

  $r = Invoke-StateTool @('RecordQueueState', '-StatePath', $statePath, '-RunId', 'run-1', '-QueueFingerprint', ' ', '-RunnableCount', '0', '-Now', '2026-07-11T00:40:45Z')
  Assert-Code $r 15 'empty queue fingerprint rejection'

  $r = Invoke-StateTool @('RecordQueueState', '-StatePath', $statePath, '-RunId', 'run-1', '-QueueFingerprint', 'queue-a', '-RunnableCount', '-1', '-Now', '2026-07-11T00:40:50Z')
  Assert-Code $r 15 'negative runnable count rejection'

  $r = Invoke-StateTool @('RecordQueueState', '-StatePath', $statePath, '-RunId', 'run-1', '-QueueFingerprint', 'queue-a', '-RunnableCount', '0', '-NoCandidate', '-QueueAuditCompleted', '-Now', '2026-07-11T00:41:00Z')
  Assert-Code $r 0 'record empty runnable supply'
  $state = Read-TestState
  if ($state.lastQueueFingerprint -ne 'queue-a' -or $state.lastNoCandidateFingerprint -ne 'queue-a' -or $state.lastRunnableCount -ne 0 -or -not $state.lastQueueAuditAt -or $state.leaseExpiresAt -ne '2026-07-11T03:41:00.0000000+00:00') {
    throw 'empty runnable supply was not persisted'
  }

  $r = Invoke-StateTool @('RecordQueueState', '-StatePath', $statePath, '-RunId', 'run-1', '-QueueFingerprint', 'queue-b', '-RunnableCount', '3', '-Now', '2026-07-11T00:42:00Z')
  Assert-Code $r 0 'record available runnable supply'
  $state = Read-TestState
  if ($state.lastQueueFingerprint -ne 'queue-b' -or $state.lastRunnableCount -ne 3 -or $null -ne $state.lastNoCandidateFingerprint) {
    throw 'available runnable supply did not clear the no-candidate fingerprint'
  }

  $r = Invoke-StateTool @('RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'wrong-run', '-WorkerId', 'deepseek', '-WorkerError', 'proxy unavailable', '-Now', '2026-07-11T00:42:10Z')
  Assert-Code $r 12 'worker failure owner mismatch'

  $r = Invoke-StateTool @('RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1', '-WorkerId', 'deepseek', '-WorkerError', ' ', '-Now', '2026-07-11T00:42:20Z')
  Assert-Code $r 15 'empty worker error rejection'

  $r = Invoke-StateTool @('RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1', '-WorkerId', 'deepseek', '-WorkerError', 'proxy unavailable', '-BackoffMinutes', '180', '-Now', '2026-07-11T00:43:00Z')
  Assert-Code $r 0 'record DeepSeek worker failure'
  $state = Read-TestState
  $deepseek = $state.workerState.deepseek
  if ($deepseek.failureCount -ne 1 -or $deepseek.backoffUntil -ne '2026-07-11T03:43:00.0000000+00:00' -or $deepseek.lastError -ne 'proxy unavailable' -or $state.leaseExpiresAt -ne '2026-07-11T03:43:00.0000000+00:00') {
    throw 'DeepSeek worker backoff was not persisted'
  }

  $expectedWorkerError = ('x' * 240) -join ''
  $longWorkerError = $expectedWorkerError + 'tail'
  $r = Invoke-StateTool @('RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1', '-WorkerId', 'deepseek', '-WorkerError', $longWorkerError, '-BackoffMinutes', '1', '-Now', '2026-07-11T00:44:00Z')
  Assert-Code $r 0 'record repeated worker failure with minimum backoff'
  $state = Read-TestState
  $deepseek = $state.workerState.deepseek
  if ($deepseek.failureCount -ne 2 -or $deepseek.backoffUntil -ne '2026-07-11T00:45:00.0000000+00:00' -or $deepseek.lastError.Length -ne 240 -or $deepseek.lastError -ne $expectedWorkerError -or $state.leaseExpiresAt -ne '2026-07-11T03:44:00.0000000+00:00') {
    throw 'repeated DeepSeek worker failure did not apply truncation or minimum backoff'
  }

  $r = Invoke-StateTool @('RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1', '-WorkerId', 'deepseek', '-WorkerError', 'maximum backoff', '-BackoffMinutes', '1440', '-Now', '2026-07-11T00:45:00Z')
  Assert-Code $r 0 'record maximum worker backoff'
  $state = Read-TestState
  $deepseek = $state.workerState.deepseek
  if ($deepseek.failureCount -ne 3 -or $deepseek.backoffUntil -ne '2026-07-12T00:45:00.0000000+00:00' -or $state.leaseExpiresAt -ne '2026-07-11T03:45:00.0000000+00:00') {
    throw 'maximum DeepSeek worker backoff was not persisted'
  }

  $r = Invoke-StateTool @('RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1', '-WorkerId', 'deepseek', '-WorkerError', 'invalid minimum', '-BackoffMinutes', '0', '-Now', '2026-07-11T00:45:10Z')
  Assert-Code $r 1 'BackoffMinutes 0 parameter binding rejection'
  $bindingError = Remove-AnsiCsi $r.Output
  if ($bindingError -notmatch 'BackoffMinutes' -or $bindingError -notmatch '(?<!\d)0(?!\d)' -or $bindingError -notmatch '(?i)(ValidateRange|range|范围|最小|小于)') {
    throw "BackoffMinutes 0 rejection did not include stable range evidence: $($r.Output)"
  }

  $r = Invoke-StateTool @('RecordWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1', '-WorkerId', 'deepseek', '-WorkerError', 'invalid maximum', '-BackoffMinutes', '1441', '-Now', '2026-07-11T00:45:20Z')
  Assert-Code $r 1 'BackoffMinutes 1441 parameter binding rejection'
  $bindingError = Remove-AnsiCsi $r.Output
  if ($bindingError -notmatch 'BackoffMinutes' -or $bindingError -notmatch '(?<!\d)1441(?!\d)' -or $bindingError -notmatch '(?i)(ValidateRange|range|范围|最大|大于)') {
    throw "BackoffMinutes 1441 rejection did not include stable range evidence: $($r.Output)"
  }

  $r = Invoke-StateTool @('ClearWorkerFailure', '-StatePath', $statePath, '-RunId', 'wrong-run', '-WorkerId', 'deepseek', '-Now', '2026-07-11T00:45:30Z')
  Assert-Code $r 12 'clear worker failure owner mismatch'

  $r = Invoke-StateTool @('ClearWorkerFailure', '-StatePath', $statePath, '-RunId', 'run-1', '-WorkerId', 'deepseek', '-Now', '2026-07-11T00:46:00Z')
  Assert-Code $r 0 'clear DeepSeek worker failure'
  $state = Read-TestState
  $deepseek = $state.workerState.deepseek
  if ($deepseek.failureCount -ne 0 -or $null -ne $deepseek.backoffUntil -or $null -ne $deepseek.lastError -or $state.leaseExpiresAt -ne '2026-07-11T03:46:00.0000000+00:00') {
    throw 'DeepSeek worker backoff was not cleared'
  }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-2', '-Now', '2026-07-11T04:00:00Z')
  Assert-Code $r 13 'stale running state rejection'
  if ($r.Output -notmatch 'stale_running_state') { throw "stale RUNNING rejection omitted stable error code: $($r.Output)" }
  $staleState = Read-TestState
  if ($staleState.runId -ne 'run-1' -or $staleState.state -ne 'RUNNING') { throw 'stale RUNNING rejection changed ownership' }

  $r = Invoke-StateTool @('RecordRecoverableInterruption', '-StatePath', $statePath, '-RunId', 'run-1', '-ErrorMessage', 'initial interruption', '-Now', '2026-07-11T04:01:00Z')
  Assert-Code $r 0 'initial interruption'
  $state = Read-TestState
  if ($state.state -ne 'RECOVERABLE' -or $null -ne $state.runId -or $null -ne $state.leaseExpiresAt -or $state.recoveryCount -ne 0) { throw 'initial interruption did not become unowned RECOVERABLE state' }
  if ($state.recoveryBaselinePath -ne 'C:\state\baseline.json' -or $state.recoveryEvidencePath -ne 'C:\state\evidence.json' -or $state.recoveryEvidenceHash -ne ('a' * 64)) { throw 'initial interruption did not preserve recovery evidence' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-2', '-Now', '2026-07-11T04:02:00Z')
  Assert-Code $r 0 'first recovery acquire'
  $recoveryAcquire = Read-TestState
  if ($recoveryAcquire.runMode -ne 'recovery' -or $recoveryAcquire.state -ne 'RUNNING') { throw 'RECOVERABLE acquire did not enter recovery mode' }
  $r = Invoke-StateTool @('RecordRecoverableInterruption', '-StatePath', $statePath, '-RunId', 'run-2', '-WasRecovery', '-ErrorMessage', 'first recovery failed', '-Now', '2026-07-11T04:03:00Z')
  Assert-Code $r 0 'first recovery failure'
  if ((Read-TestState).recoveryCount -ne 1) { throw 'first recovery count was not 1' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-3', '-Now', '2026-07-11T04:04:00Z')
  Assert-Code $r 0 'second recovery acquire'
  $r = Invoke-StateTool @('RecordRecoverableInterruption', '-StatePath', $statePath, '-RunId', 'run-3', '-WasRecovery', '-ErrorMessage', 'second recovery failed', '-Now', '2026-07-11T04:05:00Z')
  Assert-Code $r 0 'second recovery failure'
  $state = Read-TestState
  if ($state.state -ne 'AUTO-BLOCKED') { throw 'second recovery failure did not block' }
  if ($state.recoveryEvidenceHash -ne ('a' * 64)) { throw 'AUTO-BLOCKED did not preserve recovery evidence' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-5', '-Now', '2026-07-11T05:00:00Z')
  Assert-Code $r 11 'blocked acquire rejection'

  $r = Invoke-StateTool @('ResetBlocked', '-StatePath', $statePath, '-ErrorMessage', 'manual test reset', '-Now', '2026-07-11T05:01:00Z')
  Assert-Code $r 0 'manual reset'
  $state = Read-TestState
  if ($null -ne $state.taskExecutor -or $null -ne $state.recoveryBaselinePath -or $null -ne $state.recoveryEvidencePath -or $null -ne $state.recoveryEvidenceHash) { throw 'manual reset did not clear task and recovery evidence fields' }
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-6', '-Now', '2026-07-11T05:02:00Z')
  Assert-Code $r 0 'acquire after reset'
  $r = Invoke-StateTool @('Checkpoint', '-StatePath', $statePath, '-RunId', 'run-6', '-TaskExecutor', 'codex', '-RecoveryBaselinePath', 'C:\state\complete-baseline.json', '-RecoveryEvidencePath', 'C:\state\complete-evidence.json', '-RecoveryEvidenceHash', ('b' * 64), '-Now', '2026-07-11T05:02:30Z')
  Assert-Code $r 0 'checkpoint executor before complete'
  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-6', '-QueueAuditCompleted', '-Now', '2026-07-11T05:03:00Z')
  Assert-Code $r 0 'complete'
  $state = Read-TestState
  if ($state.state -ne 'IDLE' -or -not $state.lastQueueAuditAt -or $null -ne $state.taskExecutor -or $null -ne $state.recoveryBaselinePath -or $null -ne $state.recoveryEvidencePath -or $null -ne $state.recoveryEvidenceHash) { throw 'complete did not clear the run and recovery evidence or record the audit' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:04:00Z')
  Assert-Code $r 0 'acquire decision lease'
  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-7',
    '-TaskKind', 'execute', '-TaskId', 'decision-task', '-TaskSummary', 'Choose restriction mode',
    '-DecisionQuestion', 'Use runtime enforcement or metadata only?',
    '-DecisionOptions', 'A=Runtime enforcement|B=Metadata only',
    '-RecommendedOption', 'A', '-ImpactSummary', 'Affects content availability and test scope',
    '-Now', '2026-07-11T05:05:00Z'
  )
  Assert-Code $r 0 'create decision'
  $decisionState = Read-TestState
  $decision = $decisionState.pendingDecision
  if ($decisionState.schemaVersion -ne 6 -or $decision.status -ne 'PENDING' -or $decision.taskId -ne 'decision-task' -or $decision.options.Count -ne 2 -or $decision.notificationAttempts.Count -ne 0) {
    throw 'create decision did not persist a schema v6 pending decision'
  }
  if ($decisionState.decisionFlow.taskId -ne 'decision-task' -or $decisionState.decisionFlow.taskKind -ne 'execute' -or $decisionState.decisionFlow.status -ne 'AWAITING_DECISION' -or $decisionState.decisionFlow.resolvedDecisions.Count -ne 0) {
    throw 'create decision did not open a matching decision flow'
  }

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-7',
    '-TaskKind', 'execute', '-TaskId', 'second-decision', '-TaskSummary', 'Duplicate request',
    '-DecisionQuestion', 'A second request must be rejected', '-DecisionOptions', 'A=A|B=B',
    '-RecommendedOption', 'A', '-ImpactSummary', 'none', '-Now', '2026-07-11T05:06:00Z'
  )
  Assert-Code $r 15 'second pending decision rejection'

  $r = Invoke-StateTool @('RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-7', '-NotificationStatus', 'PROVIDER_ACCEPTED', '-Now', '2026-07-11T05:07:00Z')
  Assert-Code $r 15 'accepted notification evidence required'
  if ((Read-TestState).pendingDecision.status -ne 'PENDING') { throw 'missing accepted notification evidence changed the decision state' }

  $providerMessageId = 'gmail-message-18f00abc123'
  $recipientHash = 'c' * 64
  $r = Invoke-StateTool @(
    'RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-7',
    '-NotificationStatus', 'PROVIDER_ACCEPTED', '-RecipientHash', $recipientHash,
    '-ProviderMessageId', $providerMessageId, '-Now', '2026-07-11T05:07:10Z'
  )
  Assert-Code $r 0 'record provider accepted notification'
  $notifiedDecision = (Read-TestState).pendingDecision
  if ($notifiedDecision.status -ne 'PROVIDER_ACCEPTED' -or $notifiedDecision.notificationAttempts.Count -ne 1) { throw 'provider accepted notification status was not appended' }
  $acceptedAttempt = $notifiedDecision.notificationAttempts[0]
  if ($acceptedAttempt.result -ne 'PROVIDER_ACCEPTED' -or $acceptedAttempt.recipientHash -ne $recipientHash -or $acceptedAttempt.providerMessageIdHash -notmatch '^[0-9a-f]{64}$' -or $acceptedAttempt.providerMessageIdHash -eq $providerMessageId -or $null -ne $acceptedAttempt.errorCategory) {
    throw 'provider accepted attempt did not preserve redacted provenance'
  }
  if ([IO.File]::ReadAllText($statePath).Contains($providerMessageId, [StringComparison]::Ordinal)) { throw 'provider message ID leaked into the state file' }

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'A', '-ReplySource', 'email', '-Now', '2026-07-11T05:07:30Z')
  Assert-Code $r 15 'email resolution evidence required'

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'C', '-ReplySource', 'email', '-EvidenceMessageId', 'reply-message-invalid-option', '-EvidenceSender', 'owner@example.invalid', '-Now', '2026-07-11T05:08:00Z')
  Assert-Code $r 15 'unknown option rejection'

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'A', '-ReplySource', 'email', '-EvidenceMessageId', 'reply-message-001', '-EvidenceSender', 'owner@example.invalid', '-EvidenceThreadId', 'forbidden-thread', '-Now', '2026-07-11T05:08:30Z')
  Assert-Code $r 15 'email evidence field isolation'

  $emailMessageId = 'reply-message-001'
  $emailSender = 'owner@example.invalid'
  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'A', '-ReplySource', 'email', '-EvidenceMessageId', $emailMessageId, '-EvidenceSender', $emailSender, '-Now', '2026-07-11T05:09:00Z')
  Assert-Code $r 0 'email resolve first decision'
  $resolvedState = Read-TestState
  if ($null -ne $resolvedState.pendingDecision -or $resolvedState.decisionFlow.status -ne 'IMPLEMENTATION_PENDING' -or $resolvedState.decisionFlow.resolvedDecisions.Count -ne 1) {
    throw 'first resolution was not moved atomically into the decision flow'
  }
  $emailResolved = $resolvedState.decisionFlow.resolvedDecisions[0]
  if ($emailResolved.decisionId -ne $decision.decisionId -or $emailResolved.notificationAttempts.Count -ne 1 -or $emailResolved.resolution.optionKey -ne 'A' -or $emailResolved.resolution.source -ne 'email' -or $emailResolved.resolution.evidenceHash -notmatch '^[0-9a-f]{64}$' -or $emailResolved.resolution.messageIdHash -notmatch '^[0-9a-f]{64}$' -or $emailResolved.resolution.senderHash -notmatch '^[0-9a-f]{64}$' -or $null -ne $emailResolved.resolution.threadIdHash -or $null -ne $emailResolved.resolution.turnIdHash) {
    throw 'email resolution provenance was not isolated and hashed'
  }
  $rawAfterEmail = [IO.File]::ReadAllText($statePath)
  if ($rawAfterEmail.Contains($emailMessageId, [StringComparison]::Ordinal) -or $rawAfterEmail.Contains($emailSender, [StringComparison]::Ordinal)) { throw 'raw email resolution evidence leaked into the state file' }

  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:10:00Z')
  Assert-Code $r 0 'complete while decision flow is active'
  $stateAfterComplete = Read-TestState
  if ($null -ne $stateAfterComplete.pendingDecision -or $stateAfterComplete.decisionFlow.status -ne 'IMPLEMENTATION_PENDING' -or $stateAfterComplete.decisionFlow.resolvedDecisions.Count -ne 1) {
    throw 'complete cleared active decision flow history'
  }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-8', '-Now', '2026-07-11T05:11:00Z')
  Assert-Code $r 0 'acquire to continue decision flow'

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-8',
    '-TaskKind', 'execute', '-TaskId', 'other-task', '-TaskSummary', 'Wrong task',
    '-DecisionQuestion', 'Should another task join this flow?', '-DecisionOptions', 'A=Yes|B=No',
    '-RecommendedOption', 'B', '-ImpactSummary', 'Must be rejected', '-Now', '2026-07-11T05:11:30Z'
  )
  Assert-Code $r 15 'cross-task decision flow rejection'

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-8',
    '-TaskKind', 'execute', '-TaskId', 'decision-task', '-TaskSummary', 'Choose multiplier model',
    '-DecisionQuestion', 'Use one multiplier or separate physical and soul multipliers?',
    '-DecisionOptions', 'A=One multiplier|B=Separate multipliers', '-RecommendedOption', 'B',
    '-ImpactSummary', 'Affects data schema and combat behavior', '-Now', '2026-07-11T05:12:00Z'
  )
  Assert-Code $r 0 'create same-task second decision'
  $secondDecision = (Read-TestState).pendingDecision
  if ((Read-TestState).decisionFlow.status -ne 'AWAITING_DECISION' -or (Read-TestState).decisionFlow.resolvedDecisions.Count -ne 1) {
    throw 'same-task second decision did not preserve the first resolution'
  }

  $beforeRawNotificationError = [IO.File]::ReadAllBytes($statePath)
  $r = Invoke-StateTool @('RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-8', '-NotificationStatus', 'DELIVERY_FAILED', '-NotificationError', 'owner@example.invalid provider said delivery failed', '-Now', '2026-07-11T05:12:15Z')
  Assert-Code $r 15 'raw notification error rejection'
  $afterRawNotificationError = [IO.File]::ReadAllBytes($statePath)
  if ([Convert]::ToBase64String($beforeRawNotificationError) -cne [Convert]::ToBase64String($afterRawNotificationError)) { throw 'rejected raw notification error changed state file bytes' }

  $r = Invoke-StateTool @('RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-8', '-NotificationStatus', 'DELIVERY_FAILED', '-NotificationError', 'provider_timeout', '-Now', '2026-07-11T05:12:30Z')
  Assert-Code $r 0 'record first failed attempt'
  $r = Invoke-StateTool @('RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-8', '-NotificationStatus', 'MISADDRESSED', '-RecipientHash', ('d' * 64), '-ProviderMessageId', 'provider-message-wrong-target', '-NotificationError', 'recipient_hash_mismatch', '-Now', '2026-07-11T05:13:00Z')
  Assert-Code $r 0 'record second misaddressed attempt'
  $r = Invoke-StateTool @('RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-8', '-NotificationStatus', 'DELIVERY_FAILED', '-NotificationError', 'provider_timeout', '-Now', '2026-07-11T05:13:30Z')
  Assert-Code $r 0 'record third failed attempt'
  $retryState = Read-TestState
  if ($retryState.pendingDecision.status -ne 'RETRY_EXHAUSTED' -or $retryState.pendingDecision.notificationAttempts.Count -ne 3 -or $retryState.pendingDecision.notificationAttempts[1].result -ne 'MISADDRESSED' -or $retryState.pendingDecision.notificationAttempts[2].result -ne 'DELIVERY_FAILED') {
    throw 'three failed attempts did not exhaust retries while preserving actual results'
  }
  $beforeFourthAttempt = [IO.File]::ReadAllBytes($statePath)
  $r = Invoke-StateTool @('RecordDecisionNotification', '-StatePath', $statePath, '-RunId', 'run-8', '-NotificationStatus', 'DELIVERY_FAILED', '-NotificationError', 'fourth_attempt', '-Now', '2026-07-11T05:14:00Z')
  Assert-Code $r 15 'fourth notification attempt rejection'
  $afterFourthAttempt = [IO.File]::ReadAllBytes($statePath)
  if ([Convert]::ToBase64String($beforeFourthAttempt) -cne [Convert]::ToBase64String($afterFourthAttempt)) { throw 'rejected fourth attempt changed state file bytes' }

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-8', '-DecisionId', $secondDecision.decisionId, '-OptionKey', 'B', '-ReplySource', 'manual', '-ManualOverride', '-EvidenceThreadId', 'thread-019f63c5', '-EvidenceMessageId', 'forbidden-message', '-Now', '2026-07-11T05:14:15Z')
  Assert-Code $r 15 'manual evidence field isolation'
  $manualThread = 'thread-019f63c5'
  $manualTurn = 'turn-approval-2'
  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-8', '-DecisionId', $secondDecision.decisionId, '-OptionKey', 'B', '-ReplySource', 'manual', '-ManualOverride', '-EvidenceThreadId', $manualThread, '-EvidenceTurnId', $manualTurn, '-Now', '2026-07-11T05:14:30Z')
  Assert-Code $r 0 'manual resolve second decision'
  $manualState = Read-TestState
  if ($null -ne $manualState.pendingDecision -or $manualState.decisionFlow.resolvedDecisions.Count -ne 2 -or $manualState.decisionFlow.status -ne 'IMPLEMENTATION_PENDING') { throw 'manual resolution did not complete the second decision atomically' }
  $manualResolution = $manualState.decisionFlow.resolvedDecisions[1].resolution
  if ($manualResolution.source -ne 'manual' -or $manualResolution.optionKey -ne 'B' -or $manualResolution.threadIdHash -notmatch '^[0-9a-f]{64}$' -or $manualResolution.turnIdHash -notmatch '^[0-9a-f]{64}$' -or $null -ne $manualResolution.messageIdHash -or $null -ne $manualResolution.senderHash) {
    throw 'manual resolution provenance was not isolated and hashed'
  }
  $rawAfterManual = [IO.File]::ReadAllText($statePath)
  if ($rawAfterManual.Contains($manualThread, [StringComparison]::Ordinal) -or $rawAfterManual.Contains($manualTurn, [StringComparison]::Ordinal)) { throw 'raw manual resolution evidence leaked into the state file' }

  $r = Invoke-StateTool @('Checkpoint', '-StatePath', $statePath, '-RunId', 'run-8', '-TaskKind', 'execute', '-TaskId', 'decision-task', '-Now', '2026-07-11T05:14:45Z')
  Assert-Code $r 0 'checkpoint decision task before flow completion'
  $r = Invoke-StateTool @('CompleteDecisionFlow', '-StatePath', $statePath, '-RunId', 'run-8', '-TaskId', 'decision-task', '-Now', '2026-07-11T05:15:00Z')
  Assert-Code $r 0 'complete decision flow'
  $completedFlowState = Read-TestState
  if ($null -ne $completedFlowState.decisionFlow -or $null -ne $completedFlowState.pendingDecision -or $completedFlowState.lastCompletedDecisionFlow.taskId -ne 'decision-task' -or $completedFlowState.lastCompletedDecisionFlow.decisionCount -ne 2 -or $completedFlowState.lastCompletedDecisionFlow.resolvedDecisions.Count -ne 2) {
    throw 'completed decision flow was not summarized and cleared'
  }
  $historyJson = $completedFlowState.lastCompletedDecisionFlow | ConvertTo-Json -Depth 8 -Compress
  if ($historyJson -match '(?i)(recipient|providerMessage|question|impactSummary|options|messageIdHash|senderHash|threadIdHash|turnIdHash)') { throw 'completed flow summary retained sensitive or unbounded decision details' }

  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-8', '-Now', '2026-07-11T05:15:30Z')
  Assert-Code $r 0 'complete decision flow run'

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'pending-complete-run', '-Now', '2026-07-11T05:16:00Z')
  Assert-Code $r 0 'acquire pending-complete fixture'
  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'pending-complete-run',
    '-TaskKind', 'execute', '-TaskId', 'pending-complete-task', '-TaskSummary', 'Preserve pending decision',
    '-DecisionQuestion', 'Should Complete retain this decision?', '-DecisionOptions', 'A=Yes|B=No',
    '-RecommendedOption', 'A', '-ImpactSummary', 'Tests run cleanup isolation', '-Now', '2026-07-11T05:16:30Z'
  )
  Assert-Code $r 0 'create pending-complete fixture'
  $pendingBeforeComplete = (Read-TestState).pendingDecision.decisionId
  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'pending-complete-run', '-Now', '2026-07-11T05:17:00Z')
  Assert-Code $r 0 'complete with unresolved decision'
  $pendingAfterComplete = Read-TestState
  if ($pendingAfterComplete.pendingDecision.decisionId -ne $pendingBeforeComplete -or $pendingAfterComplete.decisionFlow.status -ne 'AWAITING_DECISION') {
    throw 'complete cleared an unresolved pending decision or its flow'
  }

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":3,"state":"IDLE","controllerId":"fresh-idle","taskExecutor":"deepseek"}')
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'fresh-run', '-Now', '2026-07-11T05:13:00Z')
  Assert-Code $r 0 'fresh IDLE acquire'
  if ($null -ne (Read-TestState).taskExecutor) { throw 'fresh IDLE acquire did not clear the task executor' }
  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'fresh-run', '-Now', '2026-07-11T05:14:00Z')
  Assert-Code $r 0 'complete fresh IDLE acquire'

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'preflight-run', '-Now', '2026-07-11T06:00:00Z')
  Assert-Code $r 0 'preflight acquire'
  $r = Invoke-StateTool @(
    'Checkpoint', '-StatePath', $statePath, '-RunId', 'preflight-run',
    '-TaskKind', 'execute', '-TaskId', 'preflight-task', '-TaskExecutor', 'codex',
    '-Checkpoint', 'mutation_started', '-ExpectedPaths', 'preflight.txt',
    '-RecoveryBaselinePath', 'C:\state\preflight-baseline.json',
    '-RecoveryEvidencePath', 'C:\state\preflight-evidence.json',
    '-RecoveryEvidenceHash', ('c' * 64), '-Now', '2026-07-11T06:00:15Z'
  )
  Assert-Code $r 0 'preflight checkpoint'
  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'preflight-run',
    '-TaskKind', 'execute', '-TaskId', 'preflight-task', '-TaskSummary', 'Preserve decision on clean abort',
    '-DecisionQuestion', 'Should the decision survive?', '-DecisionOptions', 'A=Yes|B=No',
    '-RecommendedOption', 'A', '-ImpactSummary', 'Failure cleanup must not erase decisions',
    '-Now', '2026-07-11T06:00:30Z'
  )
  Assert-Code $r 0 'preflight decision fixture'
  $preflightDecisionId = (Read-TestState).pendingDecision.decisionId
  $r = Invoke-StateTool @('AbortClean', '-StatePath', $statePath, '-RunId', 'preflight-run', '-ErrorMessage', 'task_selected rejected invalid TaskKind', '-Now', '2026-07-11T06:01:00Z')
  Assert-Code $r 0 'preflight failure cleanup'
  $state = Read-TestState
  if ($state.state -ne 'IDLE' -or $null -ne $state.runId -or $null -ne $state.runMode -or $null -ne $state.leaseExpiresAt -or
      $null -ne $state.taskKind -or $null -ne $state.taskId -or $null -ne $state.taskExecutor -or $null -ne $state.checkpoint -or
      @($state.expectedPaths).Count -ne 0 -or $null -ne $state.recoveryBaselinePath -or $null -ne $state.recoveryEvidencePath -or
      $null -ne $state.recoveryEvidenceHash -or $state.recoveryCount -ne 0 -or
      $state.pendingDecision.decisionId -ne $preflightDecisionId -or $state.decisionFlow.status -ne 'AWAITING_DECISION' -or
      $state.lastError -ne 'task_selected rejected invalid TaskKind') {
    throw 'AbortClean did not clear run/recovery fields while preserving decision state and diagnostic'
  }

  $completeRecoveryFixture = [ordered]@{
    schemaVersion = 6
    controllerId = 'recovery-validation'
    runId = 'recovery-validation-run'
    runMode = 'fresh'
    state = 'RUNNING'
    leaseExpiresAt = '2026-07-11T09:30:00Z'
    taskKind = 'execute'
    taskId = 'recovery-validation-task'
    taskExecutor = 'codex'
    checkpoint = 'mutation_started'
    expectedPaths = @('task.txt')
    recoveryBaselinePath = 'C:\state\validation-baseline.json'
    recoveryEvidencePath = 'C:\state\validation-evidence.json'
    recoveryEvidenceHash = ('d' * 64)
    recoveryCount = 0
  }
  foreach ($missingField in @('taskKind','taskId','expectedPaths','recoveryBaselinePath','recoveryEvidencePath','recoveryEvidenceHash')) {
    $fixture = [ordered]@{}
    foreach ($entry in $completeRecoveryFixture.GetEnumerator()) { $fixture[$entry.Key] = $entry.Value }
    $fixture[$missingField] = if ($missingField -eq 'expectedPaths') { @() } else { $null }
    [IO.File]::WriteAllText($statePath, ($fixture | ConvertTo-Json -Depth 5 -Compress))
    $r = Invoke-StateTool @(
      'RecordRecoverableInterruption', '-StatePath', $statePath, '-RunId', 'recovery-validation-run',
      '-ErrorMessage', "missing $missingField", '-Now', '2026-07-11T06:30:00Z'
    )
    Assert-Code $r 15 "missing $missingField recovery invariant rejection"
    if ((Read-TestState).state -ne 'RUNNING') { throw "missing $missingField recovery rejection mutated state" }
  }
  $invalidHashFixture = [ordered]@{}
  foreach ($entry in $completeRecoveryFixture.GetEnumerator()) { $invalidHashFixture[$entry.Key] = $entry.Value }
  $invalidHashFixture.recoveryEvidenceHash = 'not-a-sha256'
  [IO.File]::WriteAllText($statePath, ($invalidHashFixture | ConvertTo-Json -Depth 5 -Compress))
  $r = Invoke-StateTool @(
    'RecordRecoverableInterruption', '-StatePath', $statePath, '-RunId', 'recovery-validation-run',
    '-ErrorMessage', 'invalid evidence hash', '-Now', '2026-07-11T06:31:00Z'
  )
  Assert-Code $r 15 'invalid recovery evidence hash rejection'

  $unsafeFixture = [ordered]@{}
  foreach ($entry in $completeRecoveryFixture.GetEnumerator()) { $unsafeFixture[$entry.Key] = $entry.Value }
  $unsafeFixture.recoveryBaselinePath = $null
  $unsafeFixture.recoveryEvidencePath = $null
  $unsafeFixture.recoveryEvidenceHash = $null
  [IO.File]::WriteAllText($statePath, ($unsafeFixture | ConvertTo-Json -Depth 5 -Compress))
  $r = Invoke-StateTool @(
    'BlockUnsafe', '-StatePath', $statePath, '-RunId', 'recovery-validation-run',
    '-ErrorMessage', 'outside expected paths changed', '-Now', '2026-07-11T06:32:00Z'
  )
  Assert-Code $r 0 'unsafe interruption block'
  $unsafeState = Read-TestState
  if ($unsafeState.state -ne 'AUTO-BLOCKED' -or $null -ne $unsafeState.runId -or $null -ne $unsafeState.leaseExpiresAt -or
      $unsafeState.lastError -ne 'outside expected paths changed' -or $null -ne $unsafeState.recoveryEvidencePath -or
      $null -ne $unsafeState.recoveryEvidenceHash) {
    throw 'BlockUnsafe did not release ownership or fabricated recovery evidence'
  }

  foreach ($invalidPendingStatus in @('RESOLVED', 'UNKNOWN_STATUS')) {
    $schema6Invalid = New-Schema6PendingFixture $invalidPendingStatus
    [IO.File]::WriteAllText($statePath, $schema6Invalid)
    $beforeInvalidSchema6 = [IO.File]::ReadAllBytes($statePath)
    $r = Invoke-StateTool @('Show', '-StatePath', $statePath)
    Assert-Code $r 13 "schema v6 pending status $invalidPendingStatus rejection"
    $afterInvalidSchema6 = [IO.File]::ReadAllBytes($statePath)
    if ([Convert]::ToBase64String($beforeInvalidSchema6) -cne [Convert]::ToBase64String($afterInvalidSchema6)) {
      throw "rejected schema v6 pending status $invalidPendingStatus changed state file bytes"
    }
  }

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":1,"state":"IDLE","controllerId":"legacy","lastQueueAuditAt":null}')
  $legacy = Read-TestState
  if ($legacy.schemaVersion -ne 6 -or $null -ne $legacy.taskExecutor -or $null -ne $legacy.recoveryBaselinePath -or $null -ne $legacy.recoveryEvidencePath -or $null -ne $legacy.recoveryEvidenceHash -or $null -ne $legacy.lastQueueFingerprint -or $null -ne $legacy.lastNoCandidateFingerprint -or $null -ne $legacy.lastRunnableCount -or $legacy.workerState.deepseek.failureCount -ne 0 -or $null -ne $legacy.workerState.deepseek.backoffUntil -or $null -ne $legacy.workerState.deepseek.lastError -or $null -ne $legacy.pendingDecision -or $null -ne $legacy.decisionFlow -or $null -ne $legacy.lastDecisionCancellation -or $legacy.auditCorrections.Count -ne 0 -or $legacy.state -ne 'IDLE') {
    throw 'schema v1 was not migrated safely'
  }

  $v2Fixture = @{
    schemaVersion = 2
    state = 'RUNNING'
    controllerId = 'v2-controller'
    runId = 'v2-run'
    leaseExpiresAt = '2026-07-11T08:00:00Z'
    taskKind = 'execute'
    taskId = 'v2-recovery-task'
    checkpoint = 'mutation_started'
    expectedPaths = @('a.txt', 'b/c.txt')
    recoveryCount = 1
    lastQueueAuditAt = '2026-07-10T23:00:00Z'
    lastError = 'recoverable interruption'
    pendingDecision = @{
      decisionId = 'DEC-V2'
      createdAt = '2026-07-10T23:30:00Z'
      taskKind = 'execute'
      taskId = 'v2-decision-task'
      taskSummary = 'Choose v2 recovery mode'
      question = 'Resume mutation or inspect first?'
      options = @(
        @{ key = 'A'; label = 'Resume mutation' },
        @{ key = 'B'; label = 'Inspect first' }
      )
      recommendedOption = 'B'
      impactSummary = 'Changes recovery latency and mutation risk'
      status = 'DELIVERY_FAILED'
      notification = @{
        status = 'DELIVERY_FAILED'
        attemptedAt = '2026-07-10T23:31:00Z'
        attempts = 2
        error = 'smtp_unavailable'
      }
      resolution = $null
    }
  } | ConvertTo-Json -Depth 6 -Compress
  [System.IO.File]::WriteAllText($statePath, $v2Fixture)
  $r = Invoke-StateTool @('Renew', '-StatePath', $statePath, '-RunId', 'v2-run', '-Now', '2026-07-11T06:00:00Z')
  Assert-Code $r 0 'renew migrated schema v2 state'
  $v2 = Read-TestState
  if ($v2.schemaVersion -ne 6 -or $null -ne $v2.taskExecutor -or $null -ne $v2.recoveryBaselinePath -or $null -ne $v2.recoveryEvidencePath -or $null -ne $v2.recoveryEvidenceHash -or $null -ne $v2.lastQueueFingerprint -or $null -ne $v2.lastNoCandidateFingerprint -or $null -ne $v2.lastRunnableCount -or $v2.workerState.deepseek.failureCount -ne 0 -or $null -ne $v2.workerState.deepseek.backoffUntil -or $null -ne $v2.workerState.deepseek.lastError -or $null -ne $v2.lastDecisionCancellation) {
    throw 'schema v2 was not migrated safely'
  }
  if ($v2.runId -ne 'v2-run' -or $v2.state -ne 'RUNNING' -or $v2.leaseExpiresAt -ne '2026-07-11T09:00:00.0000000+00:00' -or $v2.taskKind -ne 'execute' -or $v2.taskId -ne 'v2-recovery-task' -or $v2.checkpoint -ne 'mutation_started' -or $v2.expectedPaths.Count -ne 2 -or $v2.expectedPaths[0] -ne 'a.txt' -or $v2.expectedPaths[1] -ne 'b/c.txt' -or $v2.recoveryCount -ne 1 -or $v2.lastError -ne 'recoverable interruption') {
    throw 'schema v2 recovery fields were not preserved after write-back'
  }
  $v2Decision = $v2.pendingDecision
  $v2CreatedAt = ([DateTimeOffset]$v2Decision.createdAt).ToUniversalTime().ToString('o')
  if ($v2Decision.decisionId -ne 'DEC-V2' -or $v2CreatedAt -ne '2026-07-10T23:30:00.0000000+00:00' -or $v2Decision.taskKind -ne 'execute' -or $v2Decision.taskId -ne 'v2-decision-task' -or $v2Decision.taskSummary -ne 'Choose v2 recovery mode' -or $v2Decision.question -ne 'Resume mutation or inspect first?' -or $v2Decision.recommendedOption -ne 'B' -or $v2Decision.impactSummary -ne 'Changes recovery latency and mutation risk' -or $v2Decision.status -ne 'DELIVERY_FAILED') {
    throw "schema v2 pending decision fields were not preserved after write-back: $($v2Decision | ConvertTo-Json -Depth 8 -Compress)"
  }
  if ($v2Decision.options.Count -ne 2 -or $v2Decision.options[0].key -ne 'A' -or $v2Decision.options[0].label -ne 'Resume mutation' -or $v2Decision.options[1].key -ne 'B' -or $v2Decision.options[1].label -ne 'Inspect first') {
    throw 'schema v2 pending decision options were not preserved after write-back'
  }
  $v2AttemptedAt = ([DateTimeOffset]$v2Decision.notificationAttempts[0].attemptedAt).ToUniversalTime().ToString('o')
  if ($v2Decision.notificationAttempts.Count -ne 1 -or $v2Decision.notificationAttempts[0].result -ne 'DELIVERY_FAILED' -or $v2AttemptedAt -ne '2026-07-10T23:31:00.0000000+00:00' -or $v2Decision.notificationAttempts[0].errorCategory -ne 'smtp_unavailable') {
    throw 'schema v2 pending decision notification was not converted to a legacy attempt'
  }
  if ($v2.decisionFlow.taskId -ne 'v2-decision-task' -or $v2.decisionFlow.status -ne 'AWAITING_DECISION' -or $v2.decisionFlow.resolvedDecisions.Count -ne 0) {
    throw 'schema v2 pending decision did not open a migrated decision flow'
  }

  $v5UnresolvedFixture = @{
    schemaVersion = 5
    controllerId = 'v5-controller'
    state = 'RUNNING'
    runId = 'v5-run'
    leaseExpiresAt = '2026-07-11T10:00:00Z'
    taskKind = 'execute'
    taskId = 'v5-unresolved-task'
    checkpoint = 'mutation_started'
    expectedPaths = @('legacy.txt')
    recoveryCount = 0
    pendingDecision = @{
      decisionId = 'DEC-V5-UNRESOLVED'
      createdAt = '2026-07-11T06:10:00Z'
      taskKind = 'execute'
      taskId = 'v5-unresolved-task'
      taskSummary = 'Legacy unresolved decision'
      question = 'Keep the legacy choice?'
      options = @(@{ key = 'A'; label = 'Keep' }, @{ key = 'B'; label = 'Replace' })
      recommendedOption = 'A'
      impactSummary = 'Migration coverage'
      status = 'NOTIFIED'
      notification = @{
        status = 'NOTIFIED'
        attemptedAt = '2026-07-11T06:11:00Z'
        attempts = 2
        error = $null
        receiptHash = ('e' * 64)
      }
      resolution = $null
    }
  } | ConvertTo-Json -Depth 7 -Compress
  [IO.File]::WriteAllText($statePath, $v5UnresolvedFixture)
  $r = Invoke-StateTool @('Renew', '-StatePath', $statePath, '-RunId', 'v5-run', '-Now', '2026-07-11T06:20:00Z')
  Assert-Code $r 0 'write back migrated v5 unresolved decision'
  $v5Unresolved = Read-TestState
  if ($v5Unresolved.schemaVersion -ne 6 -or $v5Unresolved.pendingDecision.status -ne 'PROVIDER_ACCEPTED' -or $v5Unresolved.pendingDecision.notificationAttempts.Count -ne 1 -or $v5Unresolved.decisionFlow.status -ne 'AWAITING_DECISION') {
    throw 'v5 unresolved decision status or flow was not migrated to schema v6'
  }
  $v5LegacyAttempt = $v5Unresolved.pendingDecision.notificationAttempts[0]
  if ($v5LegacyAttempt.result -ne 'PROVIDER_ACCEPTED' -or $null -ne $v5LegacyAttempt.recipientHash -or $v5LegacyAttempt.providerMessageIdHash -ne ('e' * 64)) {
    throw 'v5 unresolved notification did not become one redacted legacy attempt'
  }

  $v5ResolvedFixture = @{
    schemaVersion = 5
    controllerId = 'v5-controller'
    state = 'IDLE'
    pendingDecision = @{
      decisionId = 'DEC-V5-RESOLVED'
      createdAt = '2026-07-11T06:30:00Z'
      taskKind = 'execute'
      taskId = 'v5-resolved-task'
      taskSummary = 'Legacy resolved decision'
      question = 'Which path was selected?'
      options = @(@{ key = 'A'; label = 'Old path' }, @{ key = 'B'; label = 'Approved path' })
      recommendedOption = 'B'
      impactSummary = 'Migration coverage'
      status = 'RESOLVED'
      notification = @{
        status = 'NOTIFIED'
        attemptedAt = '2026-07-11T06:31:00Z'
        attempts = 1
        error = $null
        receiptHash = ('f' * 64)
      }
      resolution = @{
        optionKey = 'B'
        source = 'manual'
        resolvedAt = '2026-07-11T06:32:00Z'
      }
    }
  } | ConvertTo-Json -Depth 7 -Compress
  [IO.File]::WriteAllText($statePath, $v5ResolvedFixture)
  $v5ResolvedBeforeWrite = Read-TestState
  if ($null -ne $v5ResolvedBeforeWrite.pendingDecision -or $v5ResolvedBeforeWrite.decisionFlow.status -ne 'IMPLEMENTATION_PENDING' -or $v5ResolvedBeforeWrite.decisionFlow.resolvedDecisions.Count -ne 1) {
    throw 'v5 resolved pending decision was not moved into the migrated flow'
  }
  $migratedResolution = $v5ResolvedBeforeWrite.decisionFlow.resolvedDecisions[0].resolution
  if ($migratedResolution.optionKey -ne 'B' -or $migratedResolution.source -ne 'manual' -or $migratedResolution.evidenceHash -notmatch '^[0-9a-f]{64}$') {
    throw 'v5 resolved decision did not retain option/source with deterministic evidence'
  }
  $legacyEvidenceHash = $migratedResolution.evidenceHash
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'v5-resolved-run', '-Now', '2026-07-11T06:40:00Z')
  Assert-Code $r 0 'write back migrated v5 resolved decision'
  $v5ResolvedAfterWrite = Read-TestState
  if ($v5ResolvedAfterWrite.schemaVersion -ne 6 -or $v5ResolvedAfterWrite.decisionFlow.resolvedDecisions[0].resolution.evidenceHash -ne $legacyEvidenceHash) {
    throw 'v5 resolved migration evidence was not deterministic across write-back'
  }

  $guard = [IO.File]::Open("$statePath.guard", [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
  try {
    $r = Invoke-StateTool @('Show', '-StatePath', $statePath)
    Assert-Code $r 14 'transaction lock contention'
  } finally {
    $guard.Dispose()
  }

  $original = '{broken json'
  [System.IO.File]::WriteAllText($statePath, $original)
  $r = Invoke-StateTool @('Show', '-StatePath', $statePath)
  Assert-Code $r 13 'corrupt json'
  if ([System.IO.File]::ReadAllText($statePath) -ne $original) { throw 'corrupt JSON was overwritten' }

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":99}')
  $r = Invoke-StateTool @('Show', '-StatePath', $statePath)
  Assert-Code $r 13 'unsupported schema'

  'test-automation-controller-state: OK'
} finally {
  Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
