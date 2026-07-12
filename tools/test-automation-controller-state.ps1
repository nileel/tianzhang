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

  $r = Invoke-StateTool @('Checkpoint', '-StatePath', $statePath, '-RunId', 'run-1', '-TaskKind', 'execute', '-TaskId', 'sample-task', '-Checkpoint', 'mutation_started', '-ExpectedPaths', 'a.txt|b/c.txt', '-Now', '2026-07-11T00:40:00Z')
  Assert-Code $r 0 'checkpoint'
  $state = Read-TestState
  if ($state.taskId -ne 'sample-task' -or $state.expectedPaths.Count -ne 2) { throw 'checkpoint fields were not persisted' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-2', '-Now', '2026-07-11T04:00:00Z')
  Assert-Code $r 0 'expired lease takeover'
  $state = Read-TestState
  if ($state.runId -ne 'run-2' -or $state.taskId -ne 'sample-task') { throw 'takeover did not preserve recovery fields' }

  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-2', '-ErrorMessage', 'initial interruption', '-Now', '2026-07-11T04:01:00Z')
  Assert-Code $r 0 'initial interruption'
  if ((Read-TestState).recoveryCount -ne 0) { throw 'initial interruption consumed a recovery attempt' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-3', '-Now', '2026-07-11T04:02:00Z')
  Assert-Code $r 0 'first recovery acquire'
  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-3', '-WasRecovery', '-ErrorMessage', 'first recovery failed', '-Now', '2026-07-11T04:03:00Z')
  Assert-Code $r 0 'first recovery failure'
  if ((Read-TestState).recoveryCount -ne 1) { throw 'first recovery count was not 1' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-4', '-Now', '2026-07-11T04:04:00Z')
  Assert-Code $r 0 'second recovery acquire'
  $r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'run-4', '-WasRecovery', '-ErrorMessage', 'second recovery failed', '-Now', '2026-07-11T04:05:00Z')
  Assert-Code $r 0 'second recovery failure'
  if ((Read-TestState).state -ne 'AUTO-BLOCKED') { throw 'second recovery failure did not block' }

  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-5', '-Now', '2026-07-11T05:00:00Z')
  Assert-Code $r 11 'blocked acquire rejection'

  $r = Invoke-StateTool @('ResetBlocked', '-StatePath', $statePath, '-ErrorMessage', 'manual test reset', '-Now', '2026-07-11T05:01:00Z')
  Assert-Code $r 0 'manual reset'
  $r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'run-6', '-Now', '2026-07-11T05:02:00Z')
  Assert-Code $r 0 'acquire after reset'
  $r = Invoke-StateTool @('Complete', '-StatePath', $statePath, '-RunId', 'run-6', '-QueueAuditCompleted', '-Now', '2026-07-11T05:03:00Z')
  Assert-Code $r 0 'complete'
  $state = Read-TestState
  if ($state.state -ne 'IDLE' -or -not $state.lastQueueAuditAt) { throw 'complete did not clear the run or record the audit' }

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
  Assert-Code $r 0 'mark decision notified'
  if ((Read-TestState).pendingDecision.status -ne 'NOTIFIED') { throw 'notification status was not persisted' }
  if ((Read-TestState).pendingDecision.notification.attempts -ne 1) { throw 'first notification attempt was not recorded' }

  $r = Invoke-StateTool @('MarkDecisionDeliveryFailed', '-StatePath', $statePath, '-RunId', 'run-7', '-NotificationError', 'smtp_unavailable', '-Now', '2026-07-11T05:07:30Z')
  Assert-Code $r 0 'mark decision delivery failed'
  if ((Read-TestState).pendingDecision.status -ne 'DELIVERY_FAILED' -or (Read-TestState).pendingDecision.notification.attempts -ne 2) {
    throw 'delivery failure did not retain retry count'
  }

  $r = Invoke-StateTool @('MarkDecisionNotified', '-StatePath', $statePath, '-RunId', 'run-7', '-Now', '2026-07-11T05:07:45Z')
  Assert-Code $r 0 'retry decision notification'
  if ((Read-TestState).pendingDecision.notification.attempts -ne 3) { throw 'retry notification did not advance attempt count' }

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'C', '-ReplySource', 'email', '-Now', '2026-07-11T05:08:00Z')
  Assert-Code $r 15 'unknown option rejection'

  $r = Invoke-StateTool @('ResolveDecision', '-StatePath', $statePath, '-RunId', 'run-7', '-DecisionId', $decision.decisionId, '-OptionKey', 'A', '-ReplySource', 'email', '-Now', '2026-07-11T05:09:00Z')
  Assert-Code $r 0 'resolve decision'
  if ((Read-TestState).pendingDecision.status -ne 'RESOLVED' -or (Read-TestState).pendingDecision.resolution.optionKey -ne 'A') {
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

  [System.IO.File]::WriteAllText($statePath, '{"schemaVersion":1,"state":"IDLE","controllerId":"legacy","lastQueueAuditAt":null}')
  $legacy = Read-TestState
  if ($legacy.schemaVersion -ne 2 -or $null -ne $legacy.pendingDecision -or $legacy.state -ne 'IDLE') { throw 'schema v1 was not migrated safely' }

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
