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

New-Item -ItemType Directory -Path $sandbox | Out-Null
try {
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-1', '-Now', '2026-07-11T00:00:00Z')
  Assert-Code $r 0 'first acquire'
  if ((Read-TestState).state -ne 'RUNNING') { throw 'first acquire did not set RUNNING' }

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
  if ($state.schemaVersion -ne 5 -or $state.taskExecutor -ne 'codex' -or $state.recoveryBaselinePath -ne 'C:\state\baseline.json' -or $state.recoveryEvidencePath -ne 'C:\state\evidence.json' -or $state.recoveryEvidenceHash -ne ('a' * 64)) { throw 'checkpoint did not persist schema v5 recovery evidence fields' }

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
  Assert-Code $r 0 'expired lease takeover'
  $state = Read-TestState
  if ($state.runId -ne 'run-2' -or $state.taskId -ne 'sample-task' -or $state.taskExecutor -ne 'deepseek') { throw 'takeover did not preserve recovery fields' }

  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-2', '-ErrorMessage', 'initial interruption', '-Now', '2026-07-11T04:01:00Z')
  Assert-Code $r 0 'initial interruption'
  $state = Read-TestState
  if ($state.recoveryCount -ne 0) { throw 'initial interruption consumed a recovery attempt' }
  if ($state.recoveryBaselinePath -ne 'C:\state\baseline.json' -or $state.recoveryEvidencePath -ne 'C:\state\evidence.json' -or $state.recoveryEvidenceHash -ne ('a' * 64)) { throw 'initial interruption did not preserve recovery evidence' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-3', '-Now', '2026-07-11T04:02:00Z')
  Assert-Code $r 0 'first recovery acquire'
  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-3', '-WasRecovery', '-ErrorMessage', 'first recovery failed', '-Now', '2026-07-11T04:03:00Z')
  Assert-Code $r 0 'first recovery failure'
  if ((Read-TestState).recoveryCount -ne 1) { throw 'first recovery count was not 1' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-4', '-Now', '2026-07-11T04:04:00Z')
  Assert-Code $r 0 'second recovery acquire'
  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-4', '-WasRecovery', '-ErrorMessage', 'second recovery failed', '-Now', '2026-07-11T04:05:00Z')
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
  $decision = (Read-TestState).pendingDecision
  if ($decision.status -ne 'PENDING' -or $decision.taskId -ne 'decision-task' -or $decision.options.Count -ne 2) {
    throw 'create decision did not persist a pending decision'
  }

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-7',
    '-TaskKind', 'execute', '-TaskId', 'second-decision', '-TaskSummary', 'Duplicate request',
    '-DecisionQuestion', 'A second request must be rejected', '-DecisionOptions', 'A=A|B=B',
    '-RecommendedOption', 'A', '-ImpactSummary', 'none', '-Now', '2026-07-11T05:06:00Z'
  )
  Assert-Code $r 15 'second pending decision rejection'

  $r = Invoke-StateTool @('MarkDecisionNotified', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:07:00Z')
  Assert-Code $r 15 'notification receipt required'
  if ((Read-TestState).pendingDecision.status -ne 'PENDING') { throw 'missing notification receipt changed the decision state' }

  $receipt = 'gmail-message-18f00abc123'
  $r = Invoke-StateTool @('MarkDecisionNotified', '-StatePath', $statePath, '-RunId', 'run-7', '-NotificationReceipt', $receipt, '-Now', '2026-07-11T05:07:00Z')
  Assert-Code $r 0 'mark decision notified with receipt'
  $notifiedDecision = (Read-TestState).pendingDecision
  if ($notifiedDecision.status -ne 'NOTIFIED') { throw 'notification status was not persisted' }
  if ($notifiedDecision.notification.attempts -ne 1) { throw 'first notification attempt was not recorded' }
  if ($notifiedDecision.notification.receiptHash -notmatch '^[0-9a-f]{64}$' -or $notifiedDecision.notification.receiptHash -eq $receipt) {
    throw 'notification receipt was not stored as a SHA-256 hash'
  }
  if ([IO.File]::ReadAllText($statePath).Contains($receipt, [StringComparison]::Ordinal)) {
    throw 'notification receipt leaked into the state file'
  }

  $r = Invoke-StateTool @('MarkDecisionDeliveryFailed', '-StatePath', $statePath, '-RunId', 'run-7', '-NotificationError', 'smtp_unavailable', '-Now', '2026-07-11T05:07:30Z')
  Assert-Code $r 0 'mark decision delivery failed'
  if ((Read-TestState).pendingDecision.status -ne 'DELIVERY_FAILED' -or (Read-TestState).pendingDecision.notification.attempts -ne 2 -or $null -ne (Read-TestState).pendingDecision.notification.receiptHash) {
    throw 'delivery failure did not retain retry count'
  }

  $r = Invoke-StateTool @('MarkDecisionNotified', '-StatePath', $statePath, '-RunId', 'run-7', '-NotificationReceipt', 'gmail-message-18f00def456', '-Now', '2026-07-11T05:07:45Z')
  Assert-Code $r 0 'retry decision notification'
  if ((Read-TestState).pendingDecision.notification.attempts -ne 3) { throw 'retry notification did not advance attempt count' }

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'C', '-ReplySource', 'email', '-Now', '2026-07-11T05:08:00Z')
  Assert-Code $r 15 'unknown option rejection'

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'A', '-ReplySource', 'manual', '-ManualOverride', '-Now', '2026-07-11T05:09:00Z')
  Assert-Code $r 0 'manual resolve decision'
  if ((Read-TestState).pendingDecision.status -ne 'RESOLVED' -or (Read-TestState).pendingDecision.resolution.optionKey -ne 'A' -or (Read-TestState).pendingDecision.resolution.source -ne 'manual') {
    throw 'valid decision resolution was not persisted'
  }

  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:10:00Z')
  Assert-Code $r 0 'complete with resolved decision'
  if ((Read-TestState).pendingDecision.status -ne 'RESOLVED') { throw 'complete cleared a resolved decision' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-8', '-Now', '2026-07-11T05:11:00Z')
  Assert-Code $r 0 'acquire to clear resolved decision'
  $r = Invoke-StateTool @('ClearResolvedDecision', '-StatePath', $statePath, '-RunId', 'run-8', '-Now', '2026-07-11T05:12:00Z')
  Assert-Code $r 0 'clear resolved decision'
  if ($null -ne (Read-TestState).pendingDecision) { throw 'resolved decision was not cleared' }

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-8',
    '-TaskKind', 'execute', '-TaskId', 'rollback-task', '-TaskSummary', 'Rollback fixture',
    '-DecisionQuestion', 'Should a failed publication retain local state?',
    '-DecisionOptions', 'A=Retain|B=Rollback', '-RecommendedOption', 'B',
    '-ImpactSummary', 'Tests controller rollback after project publication failure',
    '-Now', '2026-07-11T05:12:30Z'
  )
  Assert-Code $r 0 'create rollback fixture'
  $rollbackDecision = (Read-TestState).pendingDecision
  $r = Invoke-StateTool @(
    'RollbackDecision', '-StatePath', $statePath, '-RunId', 'run-8',
    '-DecisionId', $rollbackDecision.decisionId,
    '-CancellationReason', 'decision_status_publish_failed',
    '-Now', '2026-07-11T05:13:00Z'
  )
  Assert-Code $r 0 'rollback unpublished decision'
  $rollbackState = Read-TestState
  if ($null -ne $rollbackState.pendingDecision -or
      $rollbackState.lastDecisionCancellation.decisionId -ne $rollbackDecision.decisionId -or
      $rollbackState.lastDecisionCancellation.source -ne 'controller_rollback') {
    throw 'controller rollback did not preserve a redacted audit record'
  }

  $r = Invoke-StateTool @(
    'CreateDecision', '-StatePath', $statePath, '-RunId', 'run-8',
    '-TaskKind', 'execute', '-TaskId', 'cancel-task', '-TaskSummary', 'Cancel fixture',
    '-DecisionQuestion', 'Should an operator cancel a duplicate decision?',
    '-DecisionOptions', 'A=Keep|B=Cancel', '-RecommendedOption', 'B',
    '-ImpactSummary', 'Tests explicit operator repair',
    '-Now', '2026-07-11T05:13:30Z'
  )
  Assert-Code $r 0 'create operator cancellation fixture'
  $cancelDecision = (Read-TestState).pendingDecision
  $r = Invoke-StateTool @(
    'CancelDecision', '-StatePath', $statePath, '-RunId', 'run-8',
    '-DecisionId', $cancelDecision.decisionId, '-CancellationReason', 'duplicate decision',
    '-Now', '2026-07-11T05:14:00Z'
  )
  Assert-Code $r 15 'operator cancellation requires override'
  $r = Invoke-StateTool @(
    'CancelDecision', '-StatePath', $statePath, '-RunId', 'run-8',
    '-DecisionId', $cancelDecision.decisionId, '-CancellationReason', 'duplicate decision',
    '-ManualOverride', '-Now', '2026-07-11T05:14:30Z'
  )
  Assert-Code $r 0 'operator cancellation'
  $cancelState = Read-TestState
  if ($null -ne $cancelState.pendingDecision -or
      $cancelState.lastDecisionCancellation.decisionId -ne $cancelDecision.decisionId -or
      $cancelState.lastDecisionCancellation.source -ne 'manual' -or
      $null -ne $cancelDecision.resolution) {
    throw 'operator cancellation did not preserve a redacted audit record'
  }

  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-8', '-Now', '2026-07-11T05:15:00Z')
  Assert-Code $r 0 'complete cancellation fixture'

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":3,"state":"IDLE","controllerId":"fresh-idle","taskExecutor":"deepseek"}')
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'fresh-run', '-Now', '2026-07-11T05:13:00Z')
  Assert-Code $r 0 'fresh IDLE acquire'
  if ($null -ne (Read-TestState).taskExecutor) { throw 'fresh IDLE acquire did not clear the task executor' }
  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'fresh-run', '-Now', '2026-07-11T05:14:00Z')
  Assert-Code $r 0 'complete fresh IDLE acquire'

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'preflight-run', '-Now', '2026-07-11T06:00:00Z')
  Assert-Code $r 0 'preflight acquire'
  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'preflight-run', '-ErrorMessage', 'task_selected rejected invalid TaskKind', '-Now', '2026-07-11T06:01:00Z')
  Assert-Code $r 0 'preflight failure cleanup'
  $state = Read-TestState
  if ($state.state -ne 'IDLE' -or $null -ne $state.runId -or $null -ne $state.leaseExpiresAt -or $state.recoveryCount -ne 0 -or $state.lastError -ne 'task_selected rejected invalid TaskKind') {
    throw 'preflight failure did not release the empty run while preserving its diagnostic'
  }

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":1,"state":"IDLE","controllerId":"legacy","lastQueueAuditAt":null}')
  $legacy = Read-TestState
  if ($legacy.schemaVersion -ne 5 -or $null -ne $legacy.taskExecutor -or $null -ne $legacy.recoveryBaselinePath -or $null -ne $legacy.recoveryEvidencePath -or $null -ne $legacy.recoveryEvidenceHash -or $null -ne $legacy.lastQueueFingerprint -or $null -ne $legacy.lastNoCandidateFingerprint -or $null -ne $legacy.lastRunnableCount -or $legacy.workerState.deepseek.failureCount -ne 0 -or $null -ne $legacy.workerState.deepseek.backoffUntil -or $null -ne $legacy.workerState.deepseek.lastError -or $null -ne $legacy.pendingDecision -or $null -ne $legacy.lastDecisionCancellation -or $legacy.state -ne 'IDLE') {
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
  if ($v2.schemaVersion -ne 5 -or $null -ne $v2.taskExecutor -or $null -ne $v2.recoveryBaselinePath -or $null -ne $v2.recoveryEvidencePath -or $null -ne $v2.recoveryEvidenceHash -or $null -ne $v2.lastQueueFingerprint -or $null -ne $v2.lastNoCandidateFingerprint -or $null -ne $v2.lastRunnableCount -or $v2.workerState.deepseek.failureCount -ne 0 -or $null -ne $v2.workerState.deepseek.backoffUntil -or $null -ne $v2.workerState.deepseek.lastError -or $null -ne $v2.lastDecisionCancellation) {
    throw 'schema v2 was not migrated safely'
  }
  if ($v2.runId -ne 'v2-run' -or $v2.state -ne 'RUNNING' -or $v2.leaseExpiresAt -ne '2026-07-11T09:00:00.0000000+00:00' -or $v2.taskKind -ne 'execute' -or $v2.taskId -ne 'v2-recovery-task' -or $v2.checkpoint -ne 'mutation_started' -or $v2.expectedPaths.Count -ne 2 -or $v2.expectedPaths[0] -ne 'a.txt' -or $v2.expectedPaths[1] -ne 'b/c.txt' -or $v2.recoveryCount -ne 1 -or $v2.lastError -ne 'recoverable interruption') {
    throw 'schema v2 recovery fields were not preserved after write-back'
  }
  $v2Decision = $v2.pendingDecision
  $v2CreatedAt = ([DateTimeOffset]$v2Decision.createdAt).ToUniversalTime().ToString('o')
  if ($v2Decision.decisionId -ne 'DEC-V2' -or $v2CreatedAt -ne '2026-07-10T23:30:00.0000000+00:00' -or $v2Decision.taskKind -ne 'execute' -or $v2Decision.taskId -ne 'v2-decision-task' -or $v2Decision.taskSummary -ne 'Choose v2 recovery mode' -or $v2Decision.question -ne 'Resume mutation or inspect first?' -or $v2Decision.recommendedOption -ne 'B' -or $v2Decision.impactSummary -ne 'Changes recovery latency and mutation risk' -or $v2Decision.status -ne 'DELIVERY_FAILED') {
    throw 'schema v2 pending decision fields were not preserved after write-back'
  }
  if ($v2Decision.options.Count -ne 2 -or $v2Decision.options[0].key -ne 'A' -or $v2Decision.options[0].label -ne 'Resume mutation' -or $v2Decision.options[1].key -ne 'B' -or $v2Decision.options[1].label -ne 'Inspect first') {
    throw 'schema v2 pending decision options were not preserved after write-back'
  }
  $v2AttemptedAt = ([DateTimeOffset]$v2Decision.notification.attemptedAt).ToUniversalTime().ToString('o')
  if ($v2Decision.notification.status -ne 'DELIVERY_FAILED' -or $v2AttemptedAt -ne '2026-07-10T23:31:00.0000000+00:00' -or $v2Decision.notification.attempts -ne 2 -or $v2Decision.notification.error -ne 'smtp_unavailable' -or $null -ne $v2Decision.resolution) {
    throw 'schema v2 pending decision notification or resolution was not preserved after write-back'
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
