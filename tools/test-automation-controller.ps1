$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-controller.ps1'
$stateTool = Join-Path $root 'tools\automation-controller-state.ps1'
$engine = (Get-Process -Id $PID).Path
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-controller-v3-test-' + [guid]::NewGuid().ToString('N'))
$repo = Join-Path $sandbox 'repo'
$statePath = Join-Path $sandbox 'state.json'
$runRoot = Join-Path $sandbox 'runs'
$safeToRemove = $false

function Invoke-Controller {
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

function Invoke-State {
  param([string[]]$Arguments)

  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $stateTool @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "state helper failed: $($output -join "`n")" }
    ($output -join "`n") | ConvertFrom-Json
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Invoke-Git {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

  $output = & git -C $repo @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join "`n")" }
  @($output)
}

function Invoke-GitAt {
  param([string]$Repository, [string[]]$Arguments)

  $output = & git -C $Repository @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join "`n")" }
  @($output)
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)

  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function Read-State {
  Invoke-State @('Show', '-StatePath', $statePath)
}

function Write-Utf8 {
  param([string]$Path, [string]$Value)

  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
  [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Path $repo, $runRoot -Force | Out-Null
$resolvedRepo = (Resolve-Path -LiteralPath $repo).Path
$tempPrefix = (Resolve-Path -LiteralPath $tempRoot).Path.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedRepo.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing fixture outside temp root: $resolvedRepo"
}
$safeToRemove = $true

try {
  Invoke-Git init | Out-Null
  Invoke-Git config user.name 'Controller V3 Test' | Out-Null
  Invoke-Git config user.email 'controller-v3@example.invalid' | Out-Null
  Write-Utf8 (Join-Path $repo 'base.txt') "base`n"
  Write-Utf8 (Join-Path $repo 'human.txt') "human base`n"
  Write-Utf8 (Join-Path $repo 'task.txt') "task base`n"
  Write-Utf8 (Join-Path $repo 'second-task.txt') "second base`n"
  Invoke-Git add -- base.txt human.txt task.txt second-task.txt | Out-Null
  Invoke-Git commit -m 'test: base' | Out-Null
  Write-Utf8 (Join-Path $repo 'human.txt') "human dirty`n"

  $runId = '11111111-1111-4111-8111-111111111111'
  $start = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )
  Assert-Code $start 0 'fresh start'
  $startJson = $start.Output | ConvertFrom-Json
  if (-not $startJson.ok -or $startJson.action -ne 'select_candidate' -or
      $startJson.branchKind -ne 'selection' -or $startJson.nextCommand -ne 'RegisterCandidate' -or
      -not (Test-Path -LiteralPath $startJson.baselinePath)) {
    throw "fresh start protocol mismatch: $($start.Output)"
  }
  $state = Read-State
  if ($state.state -ne 'RUNNING' -or $state.runId -ne $runId -or $state.checkpoint -ne 'identity_checked') {
    throw 'fresh start did not persist the identity checkpoint'
  }

  $contract = Invoke-Controller @('Contract')
  Assert-Code $contract 0 'protocol contract'
  $mapping = ($contract.Output | ConvertFrom-Json).taskKindMapping
  if ($mapping.execution -ne 'execute' -or $mapping.review -ne 'review' -or
      $mapping.maintenance -ne 'maintenance' -or $mapping.recovery -ne 'recovery') {
    throw 'TaskKind mapping is not canonical'
  }

  $conflict = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-WorkType', 'execution',
    '-TaskId', 'conflict-task', '-Executor', 'codex', '-ExpectedPaths', 'human.txt'
  )
  Assert-Code $conflict 20 'candidate conflict'
  $conflictJson = $conflict.Output | ConvertFrom-Json
  if ($conflictJson.failurePolicy -ne 'skip_candidate' -or $conflictJson.errorCode -ne 'candidate_conflict' -or (Read-State).checkpoint -ne 'identity_checked') {
    throw "candidate conflict was not safely skippable: $($conflict.Output)"
  }

  $registered = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-WorkType', 'execution',
    '-TaskId', 'task-1', '-Executor', 'codex', '-ExpectedPaths', 'task.txt|second-task.txt'
  )
  Assert-Code $registered 0 'register execution candidate'
  $registeredJson = $registered.Output | ConvertFrom-Json
  $state = Read-State
  if ($registeredJson.action -ne 'implement_task' -or $registeredJson.nextCommand -ne 'BeginMutation' -or
      $state.taskKind -ne 'execute' -or $state.taskId -ne 'task-1' -or $state.taskExecutor -ne 'codex' -or
      $state.checkpoint -ne 'task_selected' -or @($state.expectedPaths).Count -ne 2) {
    throw "candidate registration did not persist the canonical work unit: $($registered.Output)"
  }

  $invalidStatePath = Join-Path $sandbox 'invalid-state.json'
  $invalidRunRoot = Join-Path $sandbox 'invalid-runs'
  $invalidRunId = '22222222-2222-4222-8222-222222222222'
  $invalidStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $invalidStatePath,
    '-RunRoot', $invalidRunRoot, '-RunId', $invalidRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )
  Assert-Code $invalidStart 0 'invalid-candidate fixture start'
  $invalid = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $invalidStatePath,
    '-RunRoot', $invalidRunRoot, '-RunId', $invalidRunId, '-WorkType', 'execution',
    '-TaskId', 'invalid-task', '-Executor', 'codex', '-ExpectedPaths', '../escape.txt'
  )
  if ($invalid.Code -eq 0) { throw 'unsafe expected path was accepted' }
  $invalidJson = $invalid.Output | ConvertFrom-Json
  $invalidState = Invoke-State @('Show', '-StatePath', $invalidStatePath)
  if ($invalidJson.failurePolicy -ne 'close_empty_run' -or $invalidJson.errorCode -ne 'invalid_arguments' -or
      $invalidState.state -ne 'IDLE' -or $null -ne $invalidState.runId) {
    throw "pre-task failure did not close the empty run: $($invalid.Output)"
  }

  $begin = Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-Now', '2026-07-15T00:05:00Z'
  )
  Assert-Code $begin 0 'begin mutation'
  if ((Read-State).checkpoint -ne 'mutation_started') { throw 'BeginMutation did not persist its checkpoint' }
  Write-Utf8 (Join-Path $repo 'task.txt') "controller residue`n"

  $failed = Invoke-Controller @(
    'Fail', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId,
    '-ErrorMessage', 'simulated interruption', '-Now', '2026-07-15T00:10:00Z'
  )
  Assert-Code $failed 0 'mutation failure'
  $failedState = Read-State
  if (-not $failedState.recoveryBaselinePath -or -not $failedState.recoveryEvidencePath -or
      $failedState.recoveryEvidenceHash -notmatch '^[0-9a-f]{64}$' -or
      -not (Test-Path -LiteralPath $failedState.recoveryEvidencePath)) {
    throw "mutation failure did not capture recovery evidence: $($failed.Output)"
  }

  $recoveryRunId = '33333333-3333-4333-8333-333333333333'
  $recovery = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $recoveryRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T04:00:00Z'
  )
  Assert-Code $recovery 0 'exact residue recovery'
  $recoveryJson = $recovery.Output | ConvertFrom-Json
  if ($recoveryJson.action -ne 'resume_task' -or $recoveryJson.branchKind -ne 'recovery' -or
      $recoveryJson.taskId -ne 'task-1' -or $recoveryJson.executor -ne 'codex' -or
      @($recoveryJson.expectedPaths).Count -ne 2 -or $recoveryJson.nextCommand -ne 'Finish') {
    throw "exact controller residue did not enter recovery: $($recovery.Output)"
  }

  $pauseRecovery = Invoke-Controller @(
    'Fail', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $recoveryRunId,
    '-ErrorMessage', 'pause recovery for changed-path test', '-Now', '2026-07-15T04:01:00Z'
  )
  Assert-Code $pauseRecovery 0 'pause first recovery'
  Write-Utf8 (Join-Path $repo 'task.txt') "controller residue changed after evidence`n"
  $changedRecoveryRunId = '44444444-4444-4444-8444-444444444444'
  $changedRecovery = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $changedRecoveryRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T08:00:00Z'
  )
  Assert-Code $changedRecovery 22 'changed expected recovery rejection'
  $changedRecoveryJson = $changedRecovery.Output | ConvertFrom-Json
  if ($changedRecoveryJson.errorCode -ne 'recovery_expected_changed' -or
      $changedRecoveryJson.failurePolicy -ne 'auto_blocked' -or (Read-State).state -ne 'AUTO-BLOCKED') {
    throw "changed expected path did not fail closed: $($changedRecovery.Output)"
  }

  $baselineStatePath = Join-Path $sandbox 'baseline-state.json'
  $baselineRunRoot = Join-Path $sandbox 'baseline-runs'
  $baselineRunId = '55555555-5555-4555-8555-555555555555'
  $baseBefore = [IO.File]::ReadAllText((Join-Path $repo 'base.txt'))
  $baselineStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $baselineStatePath,
    '-RunRoot', $baselineRunRoot, '-RunId', $baselineRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )
  Assert-Code $baselineStart 0 'baseline-change fixture start'
  Write-Utf8 (Join-Path $repo 'base.txt') "changed outside expected paths`n"
  $baselineChanged = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $baselineStatePath,
    '-RunRoot', $baselineRunRoot, '-RunId', $baselineRunId, '-WorkType', 'execution',
    '-TaskId', 'baseline-task', '-Executor', 'codex', '-ExpectedPaths', 'second-task.txt'
  )
  Assert-Code $baselineChanged 21 'baseline changed rejection'
  $baselineChangedJson = $baselineChanged.Output | ConvertFrom-Json
  $baselineState = Invoke-State @('Show', '-StatePath', $baselineStatePath)
  if ($baselineChangedJson.errorCode -ne 'baseline_changed' -or $baselineChangedJson.failurePolicy -ne 'stop_read_only' -or
      $baselineState.state -ne 'IDLE' -or $null -ne $baselineState.runId) {
    throw "baseline change did not close the empty run: $($baselineChanged.Output)"
  }
  [IO.File]::WriteAllText((Join-Path $repo 'base.txt'), $baseBefore, [Text.UTF8Encoding]::new($false))

  $decisionStatePath = Join-Path $sandbox 'decision-state.json'
  $decisionRunRoot = Join-Path $sandbox 'decision-runs'
  $decisionRunId = '66666666-6666-4666-8666-666666666666'
  $decisionStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )
  Assert-Code $decisionStart 0 'decision fixture start'
  $decisionCandidate = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId, '-WorkType', 'maintenance',
    '-TaskId', 'decision-task', '-Executor', 'codex', '-ExpectedPaths', '开发管理/自动工作流状态.txt'
  )
  Assert-Code $decisionCandidate 0 'decision candidate registration'
  $decisionRegisteredState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  if (@($decisionRegisteredState.expectedPaths) -notcontains '开发管理/自动工作流状态.txt') {
    throw "decision status path was not persisted exactly: $($decisionRegisteredState.expectedPaths | ConvertTo-Json -Compress)"
  }
  $created = Invoke-Controller @(
    'CreateDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-TaskSummary', '选择控制器模式', '-DecisionQuestion', '采用哪一种模式？',
    '-DecisionOptions', 'A=模式甲|B=模式乙', '-RecommendedOption', 'A',
    '-ImpactSummary', '影响后续运行行为', '-Now', '2026-07-15T00:05:00Z'
  )
  Assert-Code $created 0 'create pending decision'
  $decision = (Invoke-State @('Show', '-StatePath', $decisionStatePath)).pendingDecision
  if ($decision.status -ne 'PENDING' -or $decision.taskId -ne 'decision-task' -or $decision.taskKind -ne 'maintenance') {
    throw "CreateDecision did not use the registered work unit: $($created.Output)"
  }
  $notified = Invoke-Controller @(
    'MarkDecisionNotified', '-StatePath', $decisionStatePath, '-RunRoot', $decisionRunRoot,
    '-RunId', $decisionRunId, '-Now', '2026-07-15T00:06:00Z'
  )
  Assert-Code $notified 0 'mark decision notified'

  $invalidReply = Invoke-Controller @(
    'ResolveDecisionReply', '-StatePath', $decisionStatePath, '-RunRoot', $decisionRunRoot,
    '-RunId', $decisionRunId, '-ReplyText', '我建议选 A', '-Now', '2026-07-15T00:07:00Z'
  )
  if ($invalidReply.Code -eq 0 -or ($invalidReply.Output | ConvertFrom-Json).errorCode -ne 'invalid_reply') {
    throw "fuzzy decision reply was accepted: $($invalidReply.Output)"
  }
  $strictReply = Invoke-Controller @(
    'ResolveDecisionReply', '-StatePath', $decisionStatePath, '-RunRoot', $decisionRunRoot,
    '-RunId', $decisionRunId, '-ReplyText', "$($decision.decisionId)：选择 A", '-Now', '2026-07-15T00:08:00Z'
  )
  Assert-Code $strictReply 0 'strict decision reply'
  $resolvedDecision = (Invoke-State @('Show', '-StatePath', $decisionStatePath)).pendingDecision
  if ($resolvedDecision.status -ne 'RESOLVED' -or $resolvedDecision.resolution.optionKey -ne 'A' -or $resolvedDecision.resolution.source -ne 'email') {
    throw "strict decision reply did not resolve the pending decision: $($strictReply.Output)"
  }

  $workerStatePath = Join-Path $sandbox 'worker-state.json'
  $workerRunRoot = Join-Path $sandbox 'worker-runs'
  $workerRunId = '77777777-7777-4777-8777-777777777777'
  $workerStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $workerStatePath,
    '-RunRoot', $workerRunRoot, '-RunId', $workerRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )
  Assert-Code $workerStart 0 'worker fixture start'
  $workerFailed = Invoke-Controller @(
    'RecordWorkerFailure', '-StatePath', $workerStatePath, '-RunRoot', $workerRunRoot,
    '-RunId', $workerRunId, '-WorkerError', 'proxy unavailable', '-BackoffMinutes', '180',
    '-Now', '2026-07-15T00:01:00Z'
  )
  Assert-Code $workerFailed 0 'record worker failure'
  $recordedWorkerState = Invoke-State @('Show', '-StatePath', $workerStatePath)
  if ($recordedWorkerState.workerState.deepseek.failureCount -ne 1 -or
      ([DateTimeOffset]$recordedWorkerState.workerState.deepseek.backoffUntil).ToUniversalTime().ToString('o') -ne '2026-07-15T03:01:00.0000000+00:00') {
    throw "DeepSeek backoff was not persisted: $($recordedWorkerState.workerState.deepseek | ConvertTo-Json -Compress)"
  }
  $backoffCandidate = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $workerStatePath,
    '-RunRoot', $workerRunRoot, '-RunId', $workerRunId, '-WorkType', 'execution',
    '-TaskId', 'deepseek-task', '-Executor', 'deepseek', '-ExpectedPaths', 'second-task.txt',
    '-Now', '2026-07-15T00:02:00Z'
  )
  if ($backoffCandidate.Code -eq 0 -or ($backoffCandidate.Output | ConvertFrom-Json).errorCode -ne 'worker_backoff' -or
      (Invoke-State @('Show', '-StatePath', $workerStatePath)).checkpoint -ne 'identity_checked') {
    throw "DeepSeek backoff did not exclude its candidate: $($backoffCandidate.Output)"
  }
  $workerCleared = Invoke-Controller @(
    'ClearWorkerFailure', '-StatePath', $workerStatePath, '-RunRoot', $workerRunRoot,
    '-RunId', $workerRunId, '-Now', '2026-07-15T00:03:00Z'
  )
  Assert-Code $workerCleared 0 'clear worker failure'
  $deepseekCandidate = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $workerStatePath,
    '-RunRoot', $workerRunRoot, '-RunId', $workerRunId, '-WorkType', 'execution',
    '-TaskId', 'deepseek-task', '-Executor', 'deepseek', '-ExpectedPaths', 'second-task.txt',
    '-Now', '2026-07-15T00:04:00Z'
  )
  Assert-Code $deepseekCandidate 0 'register DeepSeek after clearing backoff'
  $deepseekJson = $deepseekCandidate.Output | ConvertFrom-Json
  if ($deepseekJson.executor -ne 'deepseek' -or $deepseekJson.requiredSources -notcontains '开发管理/DeepSeek工作提示词.txt') {
    throw "DeepSeek branch sources were not loaded on demand: $($deepseekCandidate.Output)"
  }

  $finishRepo = Join-Path $sandbox 'finish-repo'
  New-Item -ItemType Directory -Path $finishRepo -Force | Out-Null
  Invoke-GitAt $finishRepo @('init') | Out-Null
  Invoke-GitAt $finishRepo @('config', 'user.name', 'Controller Finish Test') | Out-Null
  Invoke-GitAt $finishRepo @('config', 'user.email', 'controller-finish@example.invalid') | Out-Null
  Write-Utf8 (Join-Path $finishRepo 'a.txt') "a base`n"
  Write-Utf8 (Join-Path $finishRepo 'b.txt') "b base  `n"
  Write-Utf8 (Join-Path $finishRepo 'manual-dirty.txt') "dirty base`n"
  Write-Utf8 (Join-Path $finishRepo 'manual-staged.txt') "staged base`n"
  Invoke-GitAt $finishRepo @('add', '--', 'a.txt', 'b.txt', 'manual-dirty.txt', 'manual-staged.txt') | Out-Null
  Invoke-GitAt $finishRepo @('commit', '-m', 'test: finish base') | Out-Null
  Write-Utf8 (Join-Path $finishRepo 'manual-dirty.txt') "manual dirty`n"
  Write-Utf8 (Join-Path $finishRepo 'manual-staged.txt') "manual staged`n"
  Invoke-GitAt $finishRepo @('add', '--', 'manual-staged.txt') | Out-Null
  $manualDirtyHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $finishRepo 'manual-dirty.txt')).Hash
  $manualStagedBlob = ((Invoke-GitAt $finishRepo @('rev-parse', ':manual-staged.txt')) -join '').Trim()
  $cleanAllowedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $finishRepo 'b.txt')).Hash

  $finishStatePath = Join-Path $sandbox 'finish-state.json'
  $finishRunRoot = Join-Path $sandbox 'finish-runs'
  $finishRunId = '88888888-8888-4888-8888-888888888888'
  Assert-Code (Invoke-Controller @(
    'Start', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $finishRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )) 0 'finish fixture start'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $finishRunId, '-WorkType', 'execution',
    '-TaskId', 'finish-multi', '-Executor', 'codex', '-ExpectedPaths', 'a.txt|b.txt',
    '-Now', '2026-07-15T00:01:00Z'
  )) 0 'finish candidate registration'
  Assert-Code (Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $finishRunId, '-Now', '2026-07-15T00:02:00Z'
  )) 0 'finish begin mutation'
  Write-Utf8 (Join-Path $finishRepo 'a.txt') "a changed`n"
  $finish = Invoke-Controller @(
    'Finish', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $finishRunId,
    '-CommitMessage', 'test: v3 multi finish', '-Now', '2026-07-15T00:03:00Z'
  )
  Assert-Code $finish 0 'finish multi allowed subset'
  $finishJson = $finish.Output | ConvertFrom-Json
  $finishState = Invoke-State @('Show', '-StatePath', $finishStatePath)
  $finishPaths = @(Invoke-GitAt $finishRepo @('show', '--format=', '--name-only', 'HEAD') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($finishJson.action -ne 'completed' -or $finishJson.commit -notmatch '^[0-9a-f]{40,64}$' -or
      $finishState.state -ne 'IDLE' -or $finishPaths.Count -ne 1 -or $finishPaths[0] -ne 'a.txt') {
    throw "multi-path Finish did not commit only the changed subset: $($finish.Output)"
  }
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $finishRepo 'b.txt')).Hash -ne $cleanAllowedHash -or
      (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $finishRepo 'manual-dirty.txt')).Hash -ne $manualDirtyHash -or
      ((Invoke-GitAt $finishRepo @('rev-parse', ':manual-staged.txt')) -join '').Trim() -ne $manualStagedBlob) {
    throw 'Finish changed a clean allowed path or unrelated human baseline'
  }

  $singleRunId = '99999999-9999-4999-8999-999999999999'
  Assert-Code (Invoke-Controller @(
    'Start', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $singleRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T04:00:00Z'
  )) 0 'single finish start'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $singleRunId, '-WorkType', 'execution',
    '-TaskId', 'finish-single', '-Executor', 'codex', '-ExpectedPaths', 'b.txt',
    '-Now', '2026-07-15T04:01:00Z'
  )) 0 'single finish candidate'
  Assert-Code (Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $singleRunId, '-Now', '2026-07-15T04:02:00Z'
  )) 0 'single finish begin'
  Write-Utf8 (Join-Path $finishRepo 'b.txt') "b changed`n"
  $singleFinish = Invoke-Controller @(
    'Finish', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $singleRunId,
    '-CommitMessage', 'test: v3 single finish', '-Now', '2026-07-15T04:03:00Z'
  )
  Assert-Code $singleFinish 0 'single path finish'
  $singlePaths = @(Invoke-GitAt $finishRepo @('show', '--format=', '--name-only', 'HEAD') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($singlePaths.Count -ne 1 -or $singlePaths[0] -ne 'b.txt') { throw 'single Finish committed an unexpected path' }

  $noChangeStatePath = Join-Path $sandbox 'no-change-state.json'
  $noChangeRunRoot = Join-Path $sandbox 'no-change-runs'
  $noChangeRunId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
  Assert-Code (Invoke-Controller @(
    'Start', '-RepositoryRoot', $finishRepo, '-StatePath', $noChangeStatePath,
    '-RunRoot', $noChangeRunRoot, '-RunId', $noChangeRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )) 0 'no-change start'
  $noChange = Invoke-Controller @(
    'CompleteNoChange', '-RepositoryRoot', $finishRepo, '-StatePath', $noChangeStatePath,
    '-RunRoot', $noChangeRunRoot, '-RunId', $noChangeRunId, '-Now', '2026-07-15T00:01:00Z'
  )
  Assert-Code $noChange 0 'complete no change'
  if ((Invoke-State @('Show', '-StatePath', $noChangeStatePath)).state -ne 'IDLE') { throw 'CompleteNoChange did not release the lease' }

  'test-automation-controller: OK'
} finally {
  if ($safeToRemove) {
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
  }
}
