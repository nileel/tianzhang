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

function Write-Utf8Bom {
  param([string]$Path, [string]$Value)

  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
  [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($true))
}

function Write-QueueFixture {
  param([string]$Repository, [string[]]$Rows)

  $queuePath = Join-Path $Repository '开发管理\当前任务队列.txt'
  $tableRows = $Rows -join "`n"
  Write-Utf8 $queuePath @"
# 当前任务队列（测试）

## 队列表头

| ID | 优先级 | 主责 | 类型 | 状态 | 任务 |
|----|--------|------|------|------|------|
$tableRows

## 任务卡片
"@
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
  Invoke-Git config core.quotePath false | Out-Null
  Write-Utf8 (Join-Path $repo 'base.txt') "base`n"
  Write-Utf8 (Join-Path $repo 'human.txt') "human base`n"
  Write-Utf8 (Join-Path $repo 'task.txt') "task base`n"
  Write-Utf8 (Join-Path $repo 'second-task.txt') "second base`n"
  Write-Utf8Bom (Join-Path $repo '开发管理\自动工作流状态.txt') @"
# 自动工作流状态（测试）

## 当前待决策

当前无待决策项。

## 最近有效结果

| 字段 | 值 |
|------|----|
| 测试 | 保持不变 |
"@
  Write-QueueFixture $repo @(
    '| conflict-task | P0 | Codex / ChatGPT5.5 | G3 数据 | 待处理 | 冲突候选 |',
    '| TQ-057 | P0 | Codex / ChatGPT5.5 | G3 数据 | 待处理 | D-TRUST-02：清理现存数据矛盾 |',
    '| invalid-task | P1 | Codex / gpt-5.5 | 工具 | 待处理 | 非法路径候选 |',
    '| mismatch-task | P1 | Codex / gpt-5.5 | 工具 | 待处理 | 身份不一致候选 |',
    '| baseline-task | P1 | Codex / gpt-5.5 | 工具 | 待处理 | 基线变化候选 |',
    '| decision-task | P1 | Codex / gpt-5.5 | 工具 | 待处理 | 决策候选 |',
    '| deepseek-task | P1 | DeepSeek V4 Pro | 工具 | 待处理 | DeepSeek 候选 |',
    '| blocked-task | P1 | Codex / gpt-5.5 | 工具 | 阻塞（TQ-057） | 阻塞候选 |',
    '| unmapped-task | P1 | External Agent | 工具 | 待处理 | 未映射主责候选 |'
  )
  Invoke-Git add -- base.txt human.txt task.txt second-task.txt '开发管理/当前任务队列.txt' '开发管理/自动工作流状态.txt' | Out-Null
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
      $startJson.branchKind -ne 'selection' -or $startJson.nextCommand -ne 'InspectCandidate' -or
      -not (Test-Path -LiteralPath $startJson.baselinePath)) {
    throw "fresh start protocol mismatch: $($start.Output)"
  }
  $state = Read-State
  if ($state.state -ne 'RUNNING' -or $state.runId -ne $runId -or $state.checkpoint -ne 'identity_checked') {
    throw 'fresh start did not persist the identity checkpoint'
  }

  $contract = Invoke-Controller @('Contract')
  Assert-Code $contract 0 'protocol contract'
  $contractJson = $contract.Output | ConvertFrom-Json
  $mapping = $contractJson.taskKindMapping
  if ($mapping.execution -ne 'execute' -or $mapping.review -ne 'review' -or
      $mapping.maintenance -ne 'maintenance' -or $mapping.recovery -ne 'recovery') {
    throw 'TaskKind mapping is not canonical'
  }
  if ($contractJson.actions -notcontains 'InspectCandidate' -or
      $contractJson.actions -notcontains 'PrepareDecision' -or
      $contractJson.commandTemplates.Start -notmatch 'Start -RepositoryRoot .* -RunId .* -ActualModel' -or
      $contractJson.commandTemplates.InspectCandidate -notmatch 'InspectCandidate -RepositoryRoot .* -RunId .* -TaskId' -or
      $contractJson.commandTemplates.InspectCandidate -match 'WorkType|Executor' -or
      $contractJson.commandTemplates.PrepareDecision -notmatch 'PrepareDecision -RepositoryRoot .* -RunId' -or
      $contractJson.commandTemplates.MarkDecisionNotified -notmatch 'NotificationReceipt' -or
      @($contractJson.decisionParameters.required) -cnotcontains 'DecisionOptions' -or
      $contractJson.decisionParameters.optionFormat -ne 'A=label|B=label' -or
      $contractJson.candidateResolvers.execution.source -ne '开发管理/当前任务队列.txt' -or
      @($contractJson.candidateResolvers.execution.semanticInputs) -cnotcontains 'TaskId' -or
      $contractJson.candidateResolvers.review.mode -ne 'separate_resolver_required' -or
      $contractJson.candidateResolvers.maintenance.mode -ne 'separate_resolver_required') {
    throw "protocol contract does not expose the exact candidate discovery entry: $($contract.Output)"
  }

  $uninspected = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-ExpectedPaths', 'task.txt'
  )
  if ($uninspected.Code -eq 0 -or ($uninspected.Output | ConvertFrom-Json).errorCode -ne 'invalid_phase' -or
      ($uninspected.Output | ConvertFrom-Json).nextCommand -ne 'InspectCandidate' -or
      (Read-State).checkpoint -ne 'identity_checked') {
    throw "candidate registration succeeded without inspection: $($uninspected.Output)"
  }

  $blockedCandidate = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-TaskId', 'blocked-task'
  )
  if ($blockedCandidate.Code -eq 0 -or
      ($blockedCandidate.Output | ConvertFrom-Json).errorCode -ne 'candidate_not_runnable' -or
      ($blockedCandidate.Output | ConvertFrom-Json).nextCommand -ne 'InspectCandidate' -or
      (Read-State).checkpoint -ne 'identity_checked') {
    throw "blocked queue task was accepted for inspection: $($blockedCandidate.Output)"
  }

  $unmappedCandidate = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-TaskId', 'unmapped-task'
  )
  if ($unmappedCandidate.Code -eq 0 -or
      ($unmappedCandidate.Output | ConvertFrom-Json).errorCode -ne 'candidate_executor_unmapped' -or
      ($unmappedCandidate.Output | ConvertFrom-Json).nextCommand -ne 'InspectCandidate' -or
      (Read-State).checkpoint -ne 'identity_checked') {
    throw "unmapped queue owner was accepted for inspection: $($unmappedCandidate.Output)"
  }

  $inspectedConflict = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-TaskId', 'conflict-task'
  )
  Assert-Code $inspectedConflict 0 'inspect conflict candidate'
  $inspectJson = $inspectedConflict.Output | ConvertFrom-Json
  if ($inspectJson.action -ne 'inspect_candidate' -or $inspectJson.nextCommand -ne 'RegisterCandidate' -or
      $inspectJson.requiredSources -notcontains '开发管理/AI协作规则.txt' -or
      -not $inspectJson.discoveryPolicy.readOnlyProjectDiscovery) {
    throw "candidate inspection contract mismatch: $($inspectedConflict.Output)"
  }

  $conflict = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-ExpectedPaths', 'human.txt'
  )
  Assert-Code $conflict 20 'candidate conflict'
  $conflictJson = $conflict.Output | ConvertFrom-Json
  if ($conflictJson.nextCommand -ne 'InspectCandidate' -or $conflictJson.failurePolicy -ne 'skip_candidate' -or
      $conflictJson.errorCode -ne 'candidate_conflict' -or (Read-State).checkpoint -ne 'identity_checked') {
    throw "candidate conflict was not safely routed back to inspection: $($conflict.Output)"
  }

  $inspected = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-TaskId', 'TQ-057'
  )
  Assert-Code $inspected 0 'inspect execution candidate'
  $inspectedJson = $inspected.Output | ConvertFrom-Json
  if ($inspectedJson.branchKind -ne 'execution' -or $inspectedJson.executor -ne 'codex') {
    throw "realistic queue owner was not mapped deterministically: $($inspected.Output)"
  }
  $registered = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId, '-ExpectedPaths', 'task.txt|second-task.txt'
  )
  Assert-Code $registered 0 'register execution candidate'
  $registeredJson = $registered.Output | ConvertFrom-Json
  $state = Read-State
  if ($registeredJson.action -ne 'implement_task' -or $registeredJson.nextCommand -ne 'BeginMutation' -or
      $state.taskKind -ne 'execute' -or $state.taskId -ne 'TQ-057' -or $state.taskExecutor -ne 'codex' -or
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
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $invalidStatePath,
    '-RunRoot', $invalidRunRoot, '-RunId', $invalidRunId, '-TaskId', 'invalid-task'
  )) 0 'inspect invalid-path candidate'
  $invalid = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $invalidStatePath,
    '-RunRoot', $invalidRunRoot, '-RunId', $invalidRunId, '-ExpectedPaths', '../escape.txt'
  )
  if ($invalid.Code -eq 0) { throw 'unsafe expected path was accepted' }
  $invalidJson = $invalid.Output | ConvertFrom-Json
  $invalidState = Invoke-State @('Show', '-StatePath', $invalidStatePath)
  if ($invalidJson.failurePolicy -ne 'close_empty_run' -or $invalidJson.errorCode -ne 'invalid_arguments' -or
      $invalidState.state -ne 'IDLE' -or $null -ne $invalidState.runId) {
    throw "pre-task failure did not close the empty run: $($invalid.Output)"
  }

  $mismatchStatePath = Join-Path $sandbox 'mismatch-state.json'
  $mismatchRunRoot = Join-Path $sandbox 'mismatch-runs'
  $mismatchRunId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
  Assert-Code (Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $mismatchStatePath,
    '-RunRoot', $mismatchRunRoot, '-RunId', $mismatchRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )) 0 'mismatch start'
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $mismatchStatePath,
    '-RunRoot', $mismatchRunRoot, '-RunId', $mismatchRunId, '-TaskId', 'mismatch-task'
  )) 0 'inspect mismatch candidate'
  $mismatch = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $mismatchStatePath,
    '-RunRoot', $mismatchRunRoot, '-RunId', $mismatchRunId,
    '-TaskId', 'different-task', '-ExpectedPaths', 'task.txt'
  )
  if ($mismatch.Code -eq 0 -or ($mismatch.Output | ConvertFrom-Json).errorCode -ne 'candidate_identity_mismatch' -or
      (Invoke-State @('Show', '-StatePath', $mismatchStatePath)).state -ne 'IDLE') {
    throw "candidate identity mismatch did not close the empty run: $($mismatch.Output)"
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
      $recoveryJson.taskId -ne 'TQ-057' -or $recoveryJson.executor -ne 'codex' -or
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
  Write-Utf8 (Join-Path $repo 'task.txt') "task base`n"

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
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $baselineStatePath,
    '-RunRoot', $baselineRunRoot, '-RunId', $baselineRunId, '-TaskId', 'baseline-task'
  )) 0 'inspect baseline candidate'
  Write-Utf8 (Join-Path $repo 'base.txt') "changed outside expected paths`n"
  $baselineChanged = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $baselineStatePath,
    '-RunRoot', $baselineRunRoot, '-RunId', $baselineRunId, '-ExpectedPaths', 'second-task.txt'
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
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId, '-TaskId', 'decision-task'
  )) 0 'inspect decision candidate'
  $decisionCandidate = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId, '-ExpectedPaths', '开发管理/自动工作流状态.txt'
  )
  Assert-Code $decisionCandidate 0 'decision candidate registration'
  $decisionCandidateJson = $decisionCandidate.Output | ConvertFrom-Json
  $decisionRegisteredState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  if (@($decisionRegisteredState.expectedPaths) -notcontains '开发管理/自动工作流状态.txt' -or
      @($decisionCandidateJson.requiredSources) -notcontains '开发管理/自动工作流状态.txt') {
    throw "decision status path was not persisted exactly: $($decisionRegisteredState.expectedPaths | ConvertTo-Json -Compress)"
  }
  Assert-Code (Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId, '-Now', '2026-07-15T00:04:00Z'
  )) 0 'decision begin mutation'
  $unpreparedDecision = Invoke-Controller @(
    'CreateDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-TaskSummary', '选择控制器模式', '-DecisionQuestion', '采用哪一种模式？',
    '-DecisionOptions', 'A=模式甲|B=模式乙', '-RecommendedOption', 'A',
    '-ImpactSummary', '影响后续运行行为', '-Now', '2026-07-15T00:05:00Z'
  )
  if ($unpreparedDecision.Code -eq 0 -or
      ($unpreparedDecision.Output | ConvertFrom-Json).errorCode -ne 'decision_context_not_prepared' -or
      ($unpreparedDecision.Output | ConvertFrom-Json).nextCommand -ne 'PrepareDecision') {
    throw "CreateDecision accepted guessed context: $($unpreparedDecision.Output)"
  }
  $preparedDecision = Invoke-Controller @(
    'PrepareDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId
  )
  Assert-Code $preparedDecision 0 'prepare decision context'
  $preparedDecisionJson = $preparedDecision.Output | ConvertFrom-Json
  if ($preparedDecisionJson.action -ne 'inspect_decision_context' -or
      @($preparedDecisionJson.requiredSources) -notcontains '开发管理/自动工作流状态.txt' -or
      @($preparedDecisionJson.command.requiredParameters) -cnotcontains 'DecisionOptions' -or
      $preparedDecisionJson.command.optionFormat -ne 'A=label|B=label' -or
      $preparedDecisionJson.nextCommand -ne 'CreateDecision') {
    throw "PrepareDecision did not expose the exact decision contract: $($preparedDecision.Output)"
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
  $statusText = [IO.File]::ReadAllText((Join-Path $repo '开发管理\自动工作流状态.txt'))
  if ($decision.status -ne 'PENDING' -or $decision.taskId -ne 'decision-task' -or $decision.taskKind -ne 'execute' -or
      $statusText -notmatch [regex]::Escape($decision.decisionId) -or $statusText -notmatch '通知状态：PENDING') {
    throw "CreateDecision did not use the registered work unit: $($created.Output)"
  }

  $overrideStatePath = Join-Path $sandbox 'override-state.json'
  $overrideRunRoot = Join-Path $sandbox 'override-runs'
  $overrideRunId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'
  Assert-Code (Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $overrideStatePath,
    '-RunRoot', $overrideRunRoot, '-RunId', $overrideRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )) 0 'selector override start'
  $selectorOverride = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $overrideStatePath,
    '-RunRoot', $overrideRunRoot, '-RunId', $overrideRunId, '-WorkType', 'review',
    '-TaskId', 'TQ-057', '-Executor', 'codex'
  )
  if ($selectorOverride.Code -eq 0 -or
      ($selectorOverride.Output | ConvertFrom-Json).errorCode -ne 'candidate_selector_override_forbidden' -or
      (Invoke-State @('Show', '-StatePath', $overrideStatePath)).state -ne 'IDLE') {
    throw "execution inspection accepted model-supplied protocol selectors: $($selectorOverride.Output)"
  }
  $missingReceipt = Invoke-Controller @(
    'MarkDecisionNotified', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId, '-Now', '2026-07-15T00:06:00Z'
  )
  if ($missingReceipt.Code -eq 0 -or
      ($missingReceipt.Output | ConvertFrom-Json).errorCode -ne 'notification_receipt_missing' -or
      (Invoke-State @('Show', '-StatePath', $decisionStatePath)).pendingDecision.status -ne 'PENDING') {
    throw "decision notification succeeded without provider evidence: $($missingReceipt.Output)"
  }
  $notified = Invoke-Controller @(
    'MarkDecisionNotified', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-NotificationReceipt', 'gmail-message-18f00abc123', '-Now', '2026-07-15T00:06:00Z'
  )
  Assert-Code $notified 0 'mark decision notified'
  $notifiedState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  $statusText = [IO.File]::ReadAllText((Join-Path $repo '开发管理\自动工作流状态.txt'))
  if ($notifiedState.pendingDecision.status -ne 'NOTIFIED' -or
      $notifiedState.pendingDecision.notification.receiptHash -notmatch '^[0-9a-f]{64}$' -or
      $statusText -notmatch '通知状态：NOTIFIED') {
    throw "notified decision was not published with evidence: $($notified.Output)"
  }
  $prematureReply = Invoke-Controller @(
    'ResolveDecisionReply', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-ReplyText', "$($decision.decisionId)：选择 A", '-Now', '2026-07-15T00:06:15Z'
  )
  if ($prematureReply.Code -eq 0 -or
      ($prematureReply.Output | ConvertFrom-Json).errorCode -ne 'invalid_phase' -or
      (Invoke-State @('Show', '-StatePath', $decisionStatePath)).pendingDecision.status -ne 'NOTIFIED') {
    throw "decision reply bypassed the publication Finish boundary: $($prematureReply.Output)"
  }
  $decisionFinish = Invoke-Controller @(
    'Finish', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-CommitMessage', 'test: publish pending decision', '-Now', '2026-07-15T00:06:30Z'
  )
  Assert-Code $decisionFinish 0 'finish pending decision publication'
  $decisionPublicationPaths = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  $decisionPublishedState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  if ($decisionPublicationPaths.Count -ne 1 -or $decisionPublicationPaths[0] -ne '开发管理/自动工作流状态.txt' -or
      $decisionPublishedState.state -ne 'IDLE' -or $decisionPublishedState.pendingDecision.status -ne 'NOTIFIED' -or
      $null -ne $decisionPublishedState.recoveryBaselinePath -or $null -ne $decisionPublishedState.recoveryEvidencePath -or
      $decisionPublishedState.recoveryCount -ne 0) {
    throw "pending decision publication did not close cleanly: $($decisionFinish.Output)"
  }

  $decisionReplyRunId = '66666666-6666-4666-8666-666666666667'
  $decisionReplyStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T01:00:00Z'
  )
  Assert-Code $decisionReplyStart 0 'pending decision reply start'
  $decisionReplyStartJson = $decisionReplyStart.Output | ConvertFrom-Json
  if ($decisionReplyStartJson.action -ne 'inspect_pending_decision' -or
      $decisionReplyStartJson.pendingDecision.decisionId -ne $decision.decisionId -or
      $decisionReplyStartJson.nextCommand -ne 'ResolveDecisionReply') {
    throw "pending decision was not surfaced on the next run: $($decisionReplyStart.Output)"
  }

  $invalidReply = Invoke-Controller @(
    'ResolveDecisionReply', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId,
    '-ReplyText', '我建议选 A', '-Now', '2026-07-15T01:01:00Z'
  )
  if ($invalidReply.Code -eq 0 -or ($invalidReply.Output | ConvertFrom-Json).errorCode -ne 'invalid_reply') {
    throw "fuzzy decision reply was accepted: $($invalidReply.Output)"
  }
  $strictReply = Invoke-Controller @(
    'ResolveDecisionReply', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId,
    '-ReplyText', "$($decision.decisionId)：选择 A", '-Now', '2026-07-15T01:02:00Z'
  )
  Assert-Code $strictReply 0 'strict decision reply'
  $strictReplyJson = $strictReply.Output | ConvertFrom-Json
  $resolvedDecision = (Invoke-State @('Show', '-StatePath', $decisionStatePath)).pendingDecision
  if ($resolvedDecision.status -ne 'RESOLVED' -or $resolvedDecision.resolution.optionKey -ne 'A' -or
      $resolvedDecision.resolution.source -ne 'email' -or $strictReplyJson.action -ne 'inspect_candidate' -or
      $strictReplyJson.taskId -ne 'decision-task' -or $strictReplyJson.nextCommand -ne 'InspectCandidate') {
    throw "strict decision reply did not resolve the pending decision: $($strictReply.Output)"
  }
  $wrongDecisionTask = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId, '-TaskId', 'TQ-057'
  )
  if ($wrongDecisionTask.Code -eq 0 -or
      ($wrongDecisionTask.Output | ConvertFrom-Json).errorCode -ne 'decision_task_mismatch' -or
      ($wrongDecisionTask.Output | ConvertFrom-Json).taskId -ne 'decision-task') {
    throw "resolved decision recovery accepted a different task: $($wrongDecisionTask.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId, '-TaskId', 'decision-task'
  )) 0 'inspect resolved decision candidate'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId,
    '-ExpectedPaths', 'task.txt|开发管理/自动工作流状态.txt'
  )) 0 'register resolved decision candidate'
  Assert-Code (Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId, '-Now', '2026-07-15T01:03:00Z'
  )) 0 'begin resolved decision implementation'
  Write-Utf8 (Join-Path $repo 'task.txt') "decision implemented`n"
  $decisionResolutionFinish = Invoke-Controller @(
    'Finish', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId,
    '-CommitMessage', 'test: implement resolved decision', '-Now', '2026-07-15T01:04:00Z'
  )
  Assert-Code $decisionResolutionFinish 0 'finish resolved decision implementation'
  $decisionResolutionPaths = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  $decisionResolvedState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  $statusText = [IO.File]::ReadAllText((Join-Path $repo '开发管理\自动工作流状态.txt'))
  $actualDecisionResolutionPaths = @($decisionResolutionPaths | Sort-Object) -join '|'
  $expectedDecisionResolutionPaths = @('task.txt', '开发管理/自动工作流状态.txt') | Sort-Object
  $expectedDecisionResolutionPaths = $expectedDecisionResolutionPaths -join '|'
  if ($actualDecisionResolutionPaths -cne $expectedDecisionResolutionPaths -or
      $decisionResolvedState.state -ne 'IDLE' -or $null -ne $decisionResolvedState.pendingDecision -or
      $statusText -notmatch '当前无待决策项。') {
    throw "resolved decision lifecycle did not commit and clear atomically: $($decisionResolutionFinish.Output)"
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
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $workerStatePath,
    '-RunRoot', $workerRunRoot, '-RunId', $workerRunId, '-TaskId', 'deepseek-task',
    '-Now', '2026-07-15T00:02:00Z'
  )
  if ($backoffCandidate.Code -eq 0 -or ($backoffCandidate.Output | ConvertFrom-Json).errorCode -ne 'worker_backoff' -or
      ($backoffCandidate.Output | ConvertFrom-Json).nextCommand -ne 'InspectCandidate' -or
      (Invoke-State @('Show', '-StatePath', $workerStatePath)).checkpoint -ne 'identity_checked') {
    throw "DeepSeek backoff did not exclude inspection: $($backoffCandidate.Output)"
  }
  $workerCleared = Invoke-Controller @(
    'ClearWorkerFailure', '-StatePath', $workerStatePath, '-RunRoot', $workerRunRoot,
    '-RunId', $workerRunId, '-Now', '2026-07-15T00:03:00Z'
  )
  Assert-Code $workerCleared 0 'clear worker failure'
  $deepseekInspection = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $workerStatePath,
    '-RunRoot', $workerRunRoot, '-RunId', $workerRunId, '-TaskId', 'deepseek-task',
    '-Now', '2026-07-15T00:04:00Z'
  )
  Assert-Code $deepseekInspection 0 'inspect DeepSeek after clearing backoff'
  $deepseekInspectJson = $deepseekInspection.Output | ConvertFrom-Json
  if ($deepseekInspectJson.requiredSources -notcontains '开发管理/DeepSeek工作提示词.txt') {
    throw "DeepSeek branch sources were not loaded for inspection: $($deepseekInspection.Output)"
  }
  $deepseekCandidate = Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $workerStatePath,
    '-RunRoot', $workerRunRoot, '-RunId', $workerRunId, '-ExpectedPaths', 'second-task.txt',
    '-Now', '2026-07-15T00:05:00Z'
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
  Write-QueueFixture $finishRepo @(
    '| finish-multi | P0 | Codex / ChatGPT5.5 | 工具 | 待处理 | 多路径完成候选 |',
    '| finish-single | P0 | Codex / ChatGPT5.5 | 工具 | 待处理 | 单路径完成候选 |'
  )
  Invoke-GitAt $finishRepo @('add', '--', 'a.txt', 'b.txt', 'manual-dirty.txt', 'manual-staged.txt', '开发管理/当前任务队列.txt') | Out-Null
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
    'InspectCandidate', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $finishRunId, '-TaskId', 'finish-multi',
    '-Now', '2026-07-15T00:01:00Z'
  )) 0 'inspect multi-path finish candidate'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $finishRunId, '-ExpectedPaths', 'a.txt|b.txt',
    '-Now', '2026-07-15T00:01:30Z'
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
    'InspectCandidate', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $singleRunId, '-TaskId', 'finish-single',
    '-Now', '2026-07-15T04:01:00Z'
  )) 0 'inspect single finish candidate'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
    '-RunRoot', $finishRunRoot, '-RunId', $singleRunId, '-ExpectedPaths', 'b.txt',
    '-Now', '2026-07-15T04:01:30Z'
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
