#requires -Version 7.0

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
$privateConfigPath = Join-Path $sandbox 'private-config.json'
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

function New-FailureFixture {
  param([string]$Name, [string]$RunId)

  $fixtureRepo = Join-Path $sandbox "$Name-repo"
  $fixtureStatePath = Join-Path $sandbox "$Name-state.json"
  $fixtureRunRoot = Join-Path $sandbox "$Name-runs"
  $taskId = "$Name-task"
  New-Item -ItemType Directory -Path $fixtureRepo -Force | Out-Null
  Invoke-GitAt $fixtureRepo @('init') | Out-Null
  Invoke-GitAt $fixtureRepo @('config', 'user.name', 'Controller Failure Test') | Out-Null
  Invoke-GitAt $fixtureRepo @('config', 'user.email', 'controller-failure@example.invalid') | Out-Null
  Write-Utf8 (Join-Path $fixtureRepo 'task.txt') "task base`n"
  Write-Utf8 (Join-Path $fixtureRepo 'intruder.txt') "intruder base`n"
  Write-QueueFixture $fixtureRepo @("| $taskId | P0 | Codex / ChatGPT5.5 | 工具 | 待处理 | failure fixture |")
  Invoke-GitAt $fixtureRepo @('add', '--', 'task.txt', 'intruder.txt', '开发管理/当前任务队列.txt') | Out-Null
  Invoke-GitAt $fixtureRepo @('commit', '-m', 'test: failure base') | Out-Null

  Assert-Code (Invoke-Controller @(
    'Start', '-RepositoryRoot', $fixtureRepo, '-StatePath', $fixtureStatePath,
    '-RunRoot', $fixtureRunRoot, '-RunId', $RunId, '-ActualModel', 'gpt-test'
  )) 0 "$Name start"
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $fixtureRepo, '-StatePath', $fixtureStatePath,
    '-RunRoot', $fixtureRunRoot, '-RunId', $RunId, '-TaskId', $taskId
  )) 0 "$Name inspect"
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $fixtureRepo, '-StatePath', $fixtureStatePath,
    '-RunRoot', $fixtureRunRoot, '-RunId', $RunId, '-ExpectedPaths', 'task.txt'
  )) 0 "$Name register"
  Assert-Code (Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $fixtureRepo, '-StatePath', $fixtureStatePath,
    '-RunRoot', $fixtureRunRoot, '-RunId', $RunId
  )) 0 "$Name begin mutation"

  [pscustomobject]@{
    Repository = $fixtureRepo
    StatePath = $fixtureStatePath
    RunRoot = $fixtureRunRoot
    RunId = $RunId
    TaskId = $taskId
  }
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
  Write-Utf8 $privateConfigPath (@{
    schemaVersion = 1
    recipientEmail = 'owner@example.invalid'
    allowedReplyFrom = 'owner@example.invalid'
    gmailLabel = 'TZG_DECISIONS'
    aliases = @('owner.alias@example.invalid')
  } | ConvertTo-Json -Depth 3)
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
  $expectedActions = @(
    'Contract','Start','InspectCandidate','RegisterCandidate','BeginMutation','Renew','Finish',
    'CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure',
    'PrepareDecision','CreateDecision','SendDecisionNotification','ConsumeDecisionReply','ResolveDecisionManual'
  )
  if ((@($contractJson.actions) -join '|') -cne ($expectedActions -join '|')) {
    throw "controller facade actions are not exact: $(@($contractJson.actions) -join '|')"
  }
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
      $contractJson.commandTemplates.SendDecisionNotification -notmatch 'SendDecisionNotification -RepositoryRoot .* -RunId' -or
      $contractJson.commandTemplates.ConsumeDecisionReply -notmatch 'ConsumeDecisionReply -RepositoryRoot .* -RunId' -or
      $null -ne $contractJson.commandTemplates.PrepareDecisionNotification -or
      $null -ne $contractJson.commandTemplates.MarkDecisionSubmitted -or
      $null -ne $contractJson.commandTemplates.RetryDecisionNotification -or
      $null -ne $contractJson.commandTemplates.MarkDecisionDeliveryFailed -or
      $null -ne $contractJson.commandTemplates.ResolveDecisionEmailReply -or
      $null -ne $contractJson.commandTemplates.MarkDecisionNotified -or
      $null -ne $contractJson.commandTemplates.ResolveDecisionReply -or
      @($contractJson.decisionParameters.required) -cnotcontains 'DecisionOptions' -or
      $contractJson.decisionParameters.optionFormat -ne 'A=label|B=label|C=label' -or
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
      $preparedDecisionJson.command.optionFormat -ne 'A=label|B=label|C=label' -or
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
  $createdJson = $created.Output | ConvertFrom-Json
  if ($decision.status -ne 'PENDING' -or $decision.taskId -ne 'decision-task' -or $decision.taskKind -ne 'execute' -or
      $createdJson.nextCommand -ne 'SendDecisionNotification' -or
      @($createdJson.nextCommands) -cnotcontains 'SendDecisionNotification' -or
      $statusText -notmatch [regex]::Escape($decision.decisionId) -or $statusText -notmatch '通知状态：等待发送飞书卡片') {
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
  $preparedNotification = Invoke-Controller @(
    'PrepareDecisionNotification', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-PrivateConfigPath', $privateConfigPath, '-Now', '2026-07-15T00:05:30Z'
  )
  Assert-Code $preparedNotification 0 'prepare decision notification'
  $preparedNotificationJson = $preparedNotification.Output | ConvertFrom-Json
  $decisionSessionPath = Join-Path $decisionRunRoot "$decisionRunId.json"
  $decisionSession = [IO.File]::ReadAllText($decisionSessionPath) | ConvertFrom-Json
  $contextNames = @($decisionSession.notificationContext.PSObject.Properties.Name | Sort-Object)
  if ($preparedNotificationJson.legacyOnly -ne $true -or
      $preparedNotificationJson.notification.recipientEmail -cne 'owner@example.invalid' -or
      $preparedNotificationJson.notification.gmailLabel -cne 'TZG_DECISIONS' -or
      ([regex]::Matches($preparedNotification.Output, 'owner@example\.invalid')).Count -ne 1 -or
      ($contextNames -join '|') -cne ((@('attemptNumber','decisionId','normalizedTargetHash','preparedAt') | Sort-Object) -join '|') -or
      $decisionSession.notificationContext.normalizedTargetHash -notmatch '^[0-9a-f]{64}$' -or
      ([IO.File]::ReadAllText($decisionSessionPath)) -match 'owner(?:\.alias)?@example\.invalid') {
    throw "prepared notification did not isolate the transient target: $($preparedNotification.Output)"
  }
  $submitted = Invoke-Controller @(
    'MarkDecisionSubmitted', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId,
    '-ProviderMessageId', 'gmail-message-18f00abc123', '-ObservedRecipient', 'owner@example.invalid',
    '-Now', '2026-07-15T00:06:00Z'
  )
  Assert-Code $submitted 0 'mark provider accepted decision'
  $submittedState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  $statusText = [IO.File]::ReadAllText((Join-Path $repo '开发管理\自动工作流状态.txt'))
  $sensitiveRuntimeText = @(
    $submitted.Output,
    [IO.File]::ReadAllText($decisionStatePath),
    [IO.File]::ReadAllText($decisionSessionPath),
    $statusText
  ) -join "`n"
  if ($submittedState.pendingDecision.status -ne 'PROVIDER_ACCEPTED' -or
      @($submittedState.pendingDecision.notificationAttempts).Count -ne 1 -or
      $submittedState.pendingDecision.notificationAttempts[0].providerMessageIdHash -notmatch '^[0-9a-f]{64}$' -or
      $statusText -notmatch '提供方接受' -or $sensitiveRuntimeText -match 'owner(?:\.alias)?@example\.invalid') {
    throw "provider-accepted notification was not stored as hash-only evidence: $($submitted.Output)"
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
      $decisionPublishedState.state -ne 'IDLE' -or $decisionPublishedState.pendingDecision.status -ne 'PROVIDER_ACCEPTED' -or
      $decisionPublishedState.decisionFlow.status -ne 'AWAITING_DECISION' -or
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
      $decisionReplyStartJson.nextCommand -ne 'SendDecisionNotification' -or
      @($decisionReplyStartJson.nextCommands) -notcontains 'SendDecisionNotification' -or
      @($decisionReplyStartJson.nextCommands) -notcontains 'ResolveDecisionManual' -or
      @($decisionReplyStartJson.nextCommands) -contains 'ResolveDecisionEmailReply' -or
      @($decisionReplyStartJson.nextCommands) -contains 'RetryDecisionNotification') {
    throw "pending decision was not surfaced on the next run: $($decisionReplyStart.Output)"
  }

  $invalidEmailReplies = @(
    @('-ReplyText', '', '-ReplyMessageId', 'reply-empty', '-ReplyFrom', 'owner@example.invalid'),
    @('-ReplyText', '我建议选 A', '-ReplyMessageId', 'reply-fuzzy', '-ReplyFrom', 'owner@example.invalid'),
    @('-ReplyText', 'DEC-20260715-WRONG：选择 A', '-ReplyMessageId', 'reply-wrong-id', '-ReplyFrom', 'owner@example.invalid'),
    @('-ReplyText', "$($decision.decisionId)：选择 A、B", '-ReplyMessageId', 'reply-multiple', '-ReplyFrom', 'owner@example.invalid'),
    @('-ReplyText', "$($decision.decisionId)：选择 A", '-ReplyMessageId', 'reply-unknown', '-ReplyFrom', 'intruder@example.invalid'),
    @('-ReplyText', "$($decision.decisionId)：选择 A", '-ReplyMessageId', '', '-ReplyFrom', 'owner@example.invalid')
  )
  foreach ($invalidArguments in $invalidEmailReplies) {
    $stateBytesBefore = [IO.File]::ReadAllBytes($decisionStatePath)
    $invalidReply = Invoke-Controller (@(
      'ResolveDecisionEmailReply', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
      '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId,
      '-PrivateConfigPath', $privateConfigPath, '-Now', '2026-07-15T01:01:00Z'
    ) + $invalidArguments)
    if ($invalidReply.Code -ne 15 -or
        -not [Linq.Enumerable]::SequenceEqual([byte[]]$stateBytesBefore, [byte[]][IO.File]::ReadAllBytes($decisionStatePath))) {
      throw "invalid email reply changed state or returned the wrong code: $($invalidReply.Output)"
    }
  }
  $strictReply = Invoke-Controller @(
    'ResolveDecisionEmailReply', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionReplyRunId,
    '-ReplyText', "$($decision.decisionId)：选择 A", '-ReplyMessageId', 'reply-message-001',
    '-ReplyFrom', 'owner.alias@example.invalid', '-PrivateConfigPath', $privateConfigPath,
    '-Now', '2026-07-15T01:02:00Z'
  )
  Assert-Code $strictReply 0 'strict decision reply'
  $strictReplyJson = $strictReply.Output | ConvertFrom-Json
  $emailResolvedState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  $emailResolution = $emailResolvedState.decisionFlow.resolvedDecisions[0].resolution
  if ($null -ne $emailResolvedState.pendingDecision -or $emailResolvedState.decisionFlow.status -ne 'IMPLEMENTATION_PENDING' -or
      $emailResolution.optionKey -ne 'A' -or $emailResolution.source -ne 'email' -or
      $emailResolution.messageIdHash -notmatch '^[0-9a-f]{64}$' -or $emailResolution.senderHash -notmatch '^[0-9a-f]{64}$' -or
      $null -ne $emailResolution.threadIdHash -or $null -ne $emailResolution.turnIdHash -or
      $strictReplyJson.action -ne 'inspect_candidate' -or
      $strictReplyJson.taskId -ne 'decision-task' -or $strictReplyJson.nextCommand -ne 'InspectCandidate') {
    throw "strict decision reply did not resolve the pending decision: $($strictReply.Output)"
  }
  [void](Invoke-State @('Complete', '-StatePath', $decisionStatePath, '-RunId', $decisionReplyRunId, '-Now', '2026-07-15T01:02:30Z'))
  $decisionResumeRunId = '66666666-6666-4666-8666-666666666668'
  $decisionResumeStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T01:03:00Z'
  )
  Assert-Code $decisionResumeStart 0 'resolved decision flow resume start'
  $decisionResumeJson = $decisionResumeStart.Output | ConvertFrom-Json
  if ($decisionResumeJson.action -ne 'resume_decision_task' -or $decisionResumeJson.taskId -ne 'decision-task' -or
      $decisionResumeJson.nextCommand -ne 'InspectCandidate') {
    throw "resolved decision flow was not resumed by Start: $($decisionResumeStart.Output)"
  }
  $wrongDecisionTask = Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId, '-TaskId', 'TQ-057'
  )
  if ($wrongDecisionTask.Code -eq 0 -or
      ($wrongDecisionTask.Output | ConvertFrom-Json).errorCode -ne 'decision_task_mismatch' -or
      ($wrongDecisionTask.Output | ConvertFrom-Json).taskId -ne 'decision-task') {
    throw "resolved decision recovery accepted a different task: $($wrongDecisionTask.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId, '-TaskId', 'decision-task'
  )) 0 'inspect resolved decision candidate'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId,
    '-ExpectedPaths', 'task.txt|开发管理/自动工作流状态.txt'
  )) 0 'register resolved decision candidate'
  Assert-Code (Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId, '-Now', '2026-07-15T01:04:00Z'
  )) 0 'begin resolved decision implementation'
  Assert-Code (Invoke-Controller @(
    'PrepareDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId
  )) 0 'prepare chained decision context'
  $chainedCreated = Invoke-Controller @(
    'CreateDecision', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId,
    '-TaskSummary', '确认第二阶段模式', '-DecisionQuestion', '第二阶段采用哪种模式？',
    '-DecisionOptions', 'A=继续甲|B=切换乙', '-RecommendedOption', 'B',
    '-ImpactSummary', '影响最终实现', '-Now', '2026-07-15T01:05:00Z'
  )
  Assert-Code $chainedCreated 0 'create chained pending decision'
  $chainedState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  $chainedDecision = $chainedState.pendingDecision
  $statusText = [IO.File]::ReadAllText((Join-Path $repo '开发管理\自动工作流状态.txt'))
  if (@($chainedState.decisionFlow.resolvedDecisions).Count -ne 1 -or
      $statusText -notmatch [regex]::Escape($decision.decisionId) -or
      $statusText -notmatch [regex]::Escape($chainedDecision.decisionId)) {
    throw 'chained decision status omitted prior resolved summaries'
  }
  Assert-Code (Invoke-Controller @(
    'PrepareDecisionNotification', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId,
    '-PrivateConfigPath', $privateConfigPath, '-Now', '2026-07-15T01:05:30Z'
  )) 0 'prepare chained notification'
  Assert-Code (Invoke-Controller @(
    'MarkDecisionSubmitted', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId,
    '-ProviderMessageId', 'gmail-message-chain-002', '-ObservedRecipient', 'owner@example.invalid',
    '-Now', '2026-07-15T01:06:00Z'
  )) 0 'submit chained notification'
  $chainedPublicationFinish = Invoke-Controller @(
    'Finish', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $decisionResumeRunId,
    '-CommitMessage', 'test: publish chained decision', '-Now', '2026-07-15T01:06:30Z'
  )
  Assert-Code $chainedPublicationFinish 0 'finish chained status-only publication'
  $statusOnlyPaths = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  $statusOnlyState = Invoke-State @('Show', '-StatePath', $decisionStatePath)
  if ($statusOnlyPaths.Count -ne 1 -or $statusOnlyPaths[0] -ne '开发管理/自动工作流状态.txt' -or
      $null -eq $statusOnlyState.pendingDecision -or $null -eq $statusOnlyState.decisionFlow) {
    throw 'status-only decision publication cleared the active flow'
  }

  $chainedReplyRunId = '66666666-6666-4666-8666-666666666669'
  Assert-Code (Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $chainedReplyRunId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T02:00:00Z'
  )) 0 'chained decision reply start'
  Assert-Code (Invoke-Controller @(
    'ResolveDecisionEmailReply', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $chainedReplyRunId,
    '-ReplyText', "$($chainedDecision.decisionId)：选择 B", '-ReplyMessageId', 'reply-message-002',
    '-ReplyFrom', 'owner@example.invalid', '-PrivateConfigPath', $privateConfigPath,
    '-Now', '2026-07-15T02:01:00Z'
  )) 0 'resolve chained decision'
  Assert-Code (Invoke-Controller @(
    'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $chainedReplyRunId, '-TaskId', 'decision-task'
  )) 0 'inspect final decision implementation'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $chainedReplyRunId,
    '-ExpectedPaths', 'task.txt|开发管理/自动工作流状态.txt'
  )) 0 'register final decision implementation'
  Assert-Code (Invoke-Controller @(
    'BeginMutation', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $chainedReplyRunId, '-Now', '2026-07-15T02:02:00Z'
  )) 0 'begin final decision implementation'
  Write-Utf8 (Join-Path $repo 'task.txt') "decision implemented`n"
  $decisionResolutionFinish = Invoke-Controller @(
    'Finish', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
    '-RunRoot', $decisionRunRoot, '-RunId', $chainedReplyRunId,
    '-CommitMessage', 'test: implement resolved decision', '-Now', '2026-07-15T02:03:00Z'
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
      $null -ne $decisionResolvedState.decisionFlow -or
      $decisionResolvedState.lastCompletedDecisionFlow.decisionCount -ne 2 -or
      $statusText -notmatch '当前无待决策项。') {
    throw "resolved decision lifecycle did not commit and clear atomically: $($decisionResolutionFinish.Output)"
  }

  $manualStatePath = Join-Path $sandbox 'manual-state.json'
  $manualRunRoot = Join-Path $sandbox 'manual-runs'
  $manualCreateRunId = '12121212-1212-4212-8212-121212121212'
  Assert-Code (Invoke-Controller @('Start','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualCreateRunId,'-ActualModel','gpt-test','-Now','2026-07-15T03:00:00Z')) 0 'manual fixture start'
  Assert-Code (Invoke-Controller @('InspectCandidate','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualCreateRunId,'-TaskId','decision-task')) 0 'manual fixture inspect'
  Assert-Code (Invoke-Controller @('RegisterCandidate','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualCreateRunId,'-ExpectedPaths','开发管理/自动工作流状态.txt')) 0 'manual fixture register'
  Assert-Code (Invoke-Controller @('BeginMutation','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualCreateRunId)) 0 'manual fixture begin'
  Assert-Code (Invoke-Controller @('PrepareDecision','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualCreateRunId)) 0 'manual fixture prepare'
  Assert-Code (Invoke-Controller @(
    'CreateDecision','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualCreateRunId,
    '-TaskSummary','人工覆盖测试','-DecisionQuestion','人工选择？','-DecisionOptions','A=甲|B=乙','-RecommendedOption','B','-ImpactSummary','测试人工证据','-Now','2026-07-15T03:01:00Z'
  )) 0 'manual fixture create'
  $manualDecision = (Invoke-State @('Show','-StatePath',$manualStatePath)).pendingDecision
  Assert-Code (Invoke-Controller @('Finish','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualCreateRunId,'-CommitMessage','test: publish manual decision','-Now','2026-07-15T03:02:00Z')) 0 'manual fixture publish'
  $manualReplyRunId = '13131313-1313-4313-8313-131313131313'
  Assert-Code (Invoke-Controller @('Start','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualReplyRunId,'-ActualModel','gpt-test','-Now','2026-07-15T03:03:00Z')) 0 'manual reply start'
  $manualResolved = Invoke-Controller @(
    'ResolveDecisionManual','-RepositoryRoot',$repo,'-StatePath',$manualStatePath,'-RunRoot',$manualRunRoot,'-RunId',$manualReplyRunId,
    '-ReplyText',"$($manualDecision.decisionId)：选择 B",'-CurrentThreadId','019f63c5-f73c-70a0-9773-5592a3e03194',
    '-CurrentTurnId','turn-manual-001','-ManualOverride','-Now','2026-07-15T03:04:00Z'
  )
  Assert-Code $manualResolved 0 'manual decision resolution'
  $manualResolvedState = Invoke-State @('Show','-StatePath',$manualStatePath)
  $manualResolution = $manualResolvedState.decisionFlow.resolvedDecisions[0].resolution
  if ($manualResolved.Output -match 'owner(?:\.alias)?@example\.invalid' -or $manualResolution.optionKey -ne 'B' -or
      $manualResolution.source -ne 'manual' -or $manualResolution.threadIdHash -notmatch '^[0-9a-f]{64}$' -or
      $manualResolution.turnIdHash -notmatch '^[0-9a-f]{64}$' -or $null -ne $manualResolution.senderHash -or
      $null -ne $manualResolution.messageIdHash -or $null -ne $manualResolvedState.pendingDecision -or
      ($manualResolved.Output | ConvertFrom-Json).taskId -ne 'decision-task') {
    throw "manual resolution mixed evidence domains: $($manualResolved.Output)"
  }

  $retryStatePath = Join-Path $sandbox 'retry-state.json'
  $retryRunRoot = Join-Path $sandbox 'retry-runs'
  $retryRunId = '14141414-1414-4414-8414-141414141414'
  Assert-Code (Invoke-Controller @('Start','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,'-ActualModel','gpt-test','-Now','2026-07-15T04:00:00Z')) 0 'retry fixture start'
  Assert-Code (Invoke-Controller @('InspectCandidate','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,'-TaskId','decision-task')) 0 'retry fixture inspect'
  Assert-Code (Invoke-Controller @('RegisterCandidate','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,'-ExpectedPaths','开发管理/自动工作流状态.txt')) 0 'retry fixture register'
  Assert-Code (Invoke-Controller @('BeginMutation','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId)) 0 'retry fixture begin'
  Assert-Code (Invoke-Controller @('PrepareDecision','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId)) 0 'retry fixture prepare'
  Assert-Code (Invoke-Controller @(
    'CreateDecision','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,
    '-TaskSummary','通知重试','-DecisionQuestion','选择？','-DecisionOptions','A=甲|B=乙','-RecommendedOption','A','-ImpactSummary','测试重试上限','-Now','2026-07-15T04:01:00Z'
  )) 0 'retry fixture create'
  $retryDecision = (Invoke-State @('Show','-StatePath',$retryStatePath)).pendingDecision
  $meConfigPath = Join-Path $sandbox 'me-private.json'
  $blankConfigPath = Join-Path $sandbox 'blank-private.json'
  Write-Utf8 $meConfigPath (@{schemaVersion=1;recipientEmail='me';allowedReplyFrom='owner@example.invalid';gmailLabel='TZG_DECISIONS';aliases=@()} | ConvertTo-Json)
  Write-Utf8 $blankConfigPath (@{schemaVersion=1;recipientEmail='';allowedReplyFrom='owner@example.invalid';gmailLabel='TZG_DECISIONS';aliases=@()} | ConvertTo-Json)
  foreach ($badConfig in @($meConfigPath,$blankConfigPath)) {
    $badPrepare = Invoke-Controller @('PrepareDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,'-PrivateConfigPath',$badConfig)
    if ($badPrepare.Code -ne 15) { throw "invalid private target was accepted: $($badPrepare.Output)" }
  }
  Assert-Code (Invoke-Controller @('PrepareDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,'-PrivateConfigPath',$privateConfigPath,'-Now','2026-07-15T04:02:00Z')) 0 'prepare retry attempt one'
  $misaddressed = Invoke-Controller @(
    'MarkDecisionSubmitted','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,
    '-ProviderMessageId','wrong-target-message-1','-ObservedRecipient','wrong@example.invalid','-Now','2026-07-15T04:03:00Z'
  )
  Assert-Code $misaddressed 0 'record misaddressed submitted decision'
  if ((Invoke-State @('Show','-StatePath',$retryStatePath)).pendingDecision.status -ne 'MISADDRESSED' -or
      $misaddressed.Output -match 'notified|received|通知成功|已收到') {
    throw "misaddressed submission claimed notification success: $($misaddressed.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'RetryDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,
    '-DecisionId',$retryDecision.decisionId,'-PrivateConfigPath',$privateConfigPath,'-Now','2026-07-15T04:04:00Z'
  )) 0 'prepare retry attempt two'
  Assert-Code (Invoke-Controller @(
    'RetryDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,
    '-DecisionId',$retryDecision.decisionId,'-PriorProviderMessageId','wrong-target-message-2','-ObservedRecipient','wrong2@example.invalid',
    '-PrivateConfigPath',$privateConfigPath,'-Now','2026-07-15T04:05:00Z'
  )) 0 'record prior mismatch and prepare attempt three'
  $longError = 'connector_' + ('x' * 200)
  Assert-Code (Invoke-Controller @(
    'MarkDecisionDeliveryFailed','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,
    '-NotificationError',$longError,'-Now','2026-07-15T04:06:00Z'
  )) 0 'record third real delivery failure'
  $exhaustedState = Invoke-State @('Show','-StatePath',$retryStatePath)
  $attemptCountBeforeRetry = @($exhaustedState.pendingDecision.notificationAttempts).Count
  $exhaustedRetry = Invoke-Controller @(
    'RetryDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$retryStatePath,'-RunRoot',$retryRunRoot,'-RunId',$retryRunId,
    '-DecisionId',$retryDecision.decisionId,'-PrivateConfigPath',$privateConfigPath,'-Now','2026-07-15T04:07:00Z'
  )
  $exhaustedAfter = Invoke-State @('Show','-StatePath',$retryStatePath)
  if ($exhaustedRetry.Code -ne 0 -or ($exhaustedRetry.Output | ConvertFrom-Json).errorCode -ne 'retry_exhausted' -or
      $exhaustedAfter.pendingDecision.status -ne 'RETRY_EXHAUSTED' -or $attemptCountBeforeRetry -ne 3 -or
      @($exhaustedAfter.pendingDecision.notificationAttempts).Count -ne 3 -or
      $exhaustedAfter.pendingDecision.notificationAttempts[2].errorCategory.Length -gt 120) {
    throw "retry exhaustion created a fourth attempt: $($exhaustedRetry.Output)"
  }

  $incidentRepo = Join-Path $sandbox 'decision-incident-repo'
  $incidentStatePath = Join-Path $sandbox 'decision-incident-state.json'
  $incidentRunRoot = Join-Path $sandbox 'decision-incident-runs'
  New-Item -ItemType Directory -Path $incidentRepo, $incidentRunRoot -Force | Out-Null
  Invoke-GitAt $incidentRepo @('init') | Out-Null
  Invoke-GitAt $incidentRepo @('config', 'user.name', 'Decision Incident Test') | Out-Null
  Invoke-GitAt $incidentRepo @('config', 'user.email', 'decision-incident@example.invalid') | Out-Null
  Invoke-GitAt $incidentRepo @('config', 'core.quotePath', 'false') | Out-Null
  Write-Utf8 (Join-Path $incidentRepo 'task.txt') "TQ-057 base`n"
  Write-Utf8Bom (Join-Path $incidentRepo '开发管理\自动工作流状态.txt') @"
# 自动工作流状态（事故时间线测试）

## 当前待决策

当前无待决策项。

## 最近有效结果

| 字段 | 值 |
|------|----|
| 测试 | 保持不变 |
"@
  Write-QueueFixture $incidentRepo @(
    '| TQ-057 | P0 | Codex / ChatGPT5.5 | G3 数据 | 待处理 | D-TRUST-02：清理现存数据矛盾 |'
  )
  Invoke-GitAt $incidentRepo @('add', '--', 'task.txt', '开发管理/当前任务队列.txt', '开发管理/自动工作流状态.txt') | Out-Null
  Invoke-GitAt $incidentRepo @('commit', '-m', 'test: incident base') | Out-Null

  $incidentCreateRunId = '61616161-6161-4161-8161-616161616161'
  Assert-Code (Invoke-Controller @(
    'Start','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-ActualModel','gpt-test','-Now','2026-07-15T05:00:00Z'
  )) 0 'incident first decision start'
  Assert-Code (Invoke-Controller @(
    'InspectCandidate','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-TaskId','TQ-057'
  )) 0 'incident first decision inspect'
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-ExpectedPaths','开发管理/自动工作流状态.txt'
  )) 0 'incident first decision register'
  Assert-Code (Invoke-Controller @(
    'BeginMutation','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-Now','2026-07-15T05:01:00Z'
  )) 0 'incident first decision begin'
  Assert-Code (Invoke-Controller @(
    'PrepareDecision','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId
  )) 0 'incident first decision prepare'
  Assert-Code (Invoke-Controller @(
    'CreateDecision','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-TaskSummary','TQ-057 事故选择','-DecisionQuestion','采用哪个修复方案？',
    '-DecisionOptions','A=保留旧状态|B=修复决策链','-RecommendedOption','B','-ImpactSummary','影响 TQ-057 后续实现',
    '-Now','2026-07-15T05:02:00Z'
  )) 0 'incident first decision create'
  $incidentFirstDecision = (Invoke-State @('Show','-StatePath',$incidentStatePath)).pendingDecision
  Assert-Code (Invoke-Controller @(
    'PrepareDecisionNotification','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-PrivateConfigPath',$privateConfigPath,'-Now','2026-07-15T05:03:00Z'
  )) 0 'incident wrong target prepare'
  $incidentWrongTarget = Invoke-Controller @(
    'MarkDecisionSubmitted','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-ProviderMessageId','incident-wrong-message','-ObservedRecipient','wrong@example.invalid',
    '-Now','2026-07-15T05:04:00Z'
  )
  Assert-Code $incidentWrongTarget 0 'incident wrong target submitted'
  $incidentMisaddressedState = Invoke-State @('Show','-StatePath',$incidentStatePath)
  $incidentFirstAttempts = @($incidentMisaddressedState.pendingDecision.notificationAttempts) | ConvertTo-Json -Depth 5 -Compress
  if ($incidentMisaddressedState.pendingDecision.status -ne 'MISADDRESSED' -or
      @($incidentMisaddressedState.pendingDecision.notificationAttempts).Count -ne 1 -or
      $incidentWrongTarget.Output -match 'notified|received|通知成功|已收到') {
    throw "incident wrong Sent target was not recorded exactly: $($incidentWrongTarget.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'Finish','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentCreateRunId,'-CommitMessage','test: publish incident decision','-Now','2026-07-15T05:05:00Z'
  )) 0 'incident first decision publish'

  $incidentResolveRunId = '62626262-6262-4262-8262-626262626262'
  $incidentPendingStart = Invoke-Controller @(
    'Start','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-ActualModel','gpt-test','-Now','2026-07-15T06:00:00Z'
  )
  Assert-Code $incidentPendingStart 0 'incident pending decision start'
  $incidentStateBytesBeforeInvalid = [IO.File]::ReadAllBytes($incidentStatePath)
  $incidentStatusPath = Join-Path $incidentRepo '开发管理\自动工作流状态.txt'
  $incidentStatusBytesBeforeInvalid = [IO.File]::ReadAllBytes($incidentStatusPath)
  $incidentNoReceipt = Invoke-Controller @(
    'ResolveDecisionManual','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-ReplyText','用户报告没有收到邮件','-CurrentThreadId','019f63c5-f73c-70a0-9773-5592a3e03194',
    '-CurrentTurnId','turn-incident-report','-ManualOverride','-Now','2026-07-15T06:01:00Z'
  )
  if ($incidentNoReceipt.Code -ne 15 -or
      -not [Linq.Enumerable]::SequenceEqual([byte[]]$incidentStateBytesBeforeInvalid, [byte[]][IO.File]::ReadAllBytes($incidentStatePath)) -or
      -not [Linq.Enumerable]::SequenceEqual([byte[]]$incidentStatusBytesBeforeInvalid, [byte[]][IO.File]::ReadAllBytes($incidentStatusPath))) {
    throw "incident no-receipt report mutated state or status: $($incidentNoReceipt.Output)"
  }
  $incidentManual = Invoke-Controller @(
    'ResolveDecisionManual','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-ReplyText',"$($incidentFirstDecision.decisionId)：选择 B",
    '-CurrentThreadId','019f63c5-f73c-70a0-9773-5592a3e03194','-CurrentTurnId','turn-incident-approval',
    '-ManualOverride','-Now','2026-07-15T06:02:00Z'
  )
  Assert-Code $incidentManual 0 'incident correct manual B resolution'
  $incidentAfterManual = Invoke-State @('Show','-StatePath',$incidentStatePath)
  $incidentFirstResolved = $incidentAfterManual.decisionFlow.resolvedDecisions[0]
  if ($incidentFirstResolved.resolution.optionKey -ne 'B' -or $incidentFirstResolved.resolution.source -ne 'manual' -or
      (@($incidentFirstResolved.notificationAttempts) | ConvertTo-Json -Depth 5 -Compress) -cne $incidentFirstAttempts -or
      @($incidentFirstResolved.notificationAttempts).Count -ne 1) {
    throw 'incident manual resolution changed the append-only notification attempt history'
  }

  $incidentReinspect = Invoke-Controller @(
    'InspectCandidate','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-TaskId','TQ-057'
  )
  Assert-Code $incidentReinspect 0 'incident same TQ-057 reinspection'
  $incidentReinspectJson = $incidentReinspect.Output | ConvertFrom-Json
  if ($incidentReinspectJson.taskId -ne 'TQ-057' -or (Invoke-State @('Show','-StatePath',$incidentStatePath)).decisionFlow.taskId -ne 'TQ-057') {
    throw "incident reinspection left the original TQ-057 flow: $($incidentReinspect.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'RegisterCandidate','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-ExpectedPaths','开发管理/自动工作流状态.txt'
  )) 0 'incident second decision register'
  Assert-Code (Invoke-Controller @(
    'BeginMutation','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-Now','2026-07-15T06:03:00Z'
  )) 0 'incident second decision begin'
  Assert-Code (Invoke-Controller @(
    'PrepareDecision','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId
  )) 0 'incident second decision prepare'
  Assert-Code (Invoke-Controller @(
    'CreateDecision','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-TaskSummary','TQ-057 第二选择','-DecisionQuestion','继续采用哪个实现？',
    '-DecisionOptions','A=路径甲|B=路径乙','-RecommendedOption','B','-ImpactSummary','影响同一任务的第二阶段',
    '-Now','2026-07-15T06:04:00Z'
  )) 0 'incident second decision create'
  $incidentSecondDecision = (Invoke-State @('Show','-StatePath',$incidentStatePath)).pendingDecision
  Assert-Code (Invoke-Controller @(
    'Finish','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentResolveRunId,'-CommitMessage','test: publish incident second decision','-Now','2026-07-15T06:05:00Z'
  )) 0 'incident second decision publish'

  $incidentFailRunId = '63636363-6363-4363-8363-636363636363'
  $incidentBeforeCleanFailure = Invoke-Controller @(
    'Start','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentFailRunId,'-ActualModel','gpt-test','-Now','2026-07-15T07:00:00Z'
  )
  Assert-Code $incidentBeforeCleanFailure 0 'incident pre-failure start'
  $incidentCleanFailure = Invoke-Controller @(
    'Fail','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentFailRunId,'-ErrorMessage','incident stopped before business file changes'
  )
  Assert-Code $incidentCleanFailure 0 'incident clean failure close'
  $incidentCleanFailureJson = $incidentCleanFailure.Output | ConvertFrom-Json
  $incidentClosedState = Invoke-State @('Show','-StatePath',$incidentStatePath)
  if ($incidentCleanFailureJson.failurePolicy -ne 'close_clean' -or $incidentClosedState.state -ne 'IDLE' -or
      $incidentClosedState.pendingDecision.decisionId -ne $incidentSecondDecision.decisionId -or
      $incidentCleanFailure.Output -match 'recovery_state_incomplete') {
    throw "incident clean failure did not close IDLE with only the second decision pending: $($incidentCleanFailure.Output)"
  }

  $incidentNextRunId = '64646464-6464-4464-8464-646464646464'
  $incidentNextStart = Invoke-Controller @(
    'Start','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentNextRunId,'-ActualModel','gpt-test','-Now','2026-07-15T08:00:00Z'
  )
  Assert-Code $incidentNextStart 0 'incident next start'
  $incidentNextStartJson = $incidentNextStart.Output | ConvertFrom-Json
  if ($incidentNextStartJson.action -ne 'inspect_pending_decision' -or
      $incidentNextStartJson.pendingDecision.decisionId -ne $incidentSecondDecision.decisionId -or
      $incidentNextStart.Output -match [regex]::Escape($incidentFirstDecision.decisionId) -or
      $incidentNextStart.Output -match 'recovery_state_incomplete') {
    throw "incident next Start did not present only the second pending decision: $($incidentNextStart.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'CompleteNoChange','-RepositoryRoot',$incidentRepo,'-StatePath',$incidentStatePath,'-RunRoot',$incidentRunRoot,
    '-RunId',$incidentNextRunId,'-Now','2026-07-15T08:01:00Z'
  )) 0 'incident final clean close'
  $incidentFinalState = Invoke-State @('Show','-StatePath',$incidentStatePath)
  if ($incidentFinalState.state -ne 'IDLE' -or $incidentFinalState.pendingDecision.decisionId -ne $incidentSecondDecision.decisionId -or
      $incidentFinalState.decisionFlow.resolvedDecisions[0].resolution.source -ne 'manual' -or
      (@($incidentFinalState.decisionFlow.resolvedDecisions[0].notificationAttempts) | ConvertTo-Json -Depth 5 -Compress) -cne $incidentFirstAttempts) {
    throw 'incident timeline did not close IDLE with the original manual evidence and append-only attempts intact'
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

  $cleanFailure = New-FailureFixture 'fail-clean' '10101010-1010-4010-8010-101010101010'
  $decisionState = Invoke-State @(
    'CreateDecision', '-StatePath', $cleanFailure.StatePath, '-RunId', $cleanFailure.RunId,
    '-TaskKind', 'execute', '-TaskId', $cleanFailure.TaskId, '-TaskSummary', 'Keep decision through clean failure',
    '-DecisionQuestion', 'Should failure cleanup preserve this?', '-DecisionOptions', 'A=Yes|B=No',
    '-RecommendedOption', 'A', '-ImpactSummary', 'Decision state is independent of run ownership'
  )
  $cleanDecisionId = [string]$decisionState.pendingDecision.decisionId
  $cleanFail = Invoke-Controller @(
    'Fail', '-RepositoryRoot', $cleanFailure.Repository, '-StatePath', $cleanFailure.StatePath,
    '-RunRoot', $cleanFailure.RunRoot, '-RunId', $cleanFailure.RunId, '-ErrorMessage', 'clean failure fixture'
  )
  Assert-Code $cleanFail 0 'clean failure close'
  $cleanFailJson = $cleanFail.Output | ConvertFrom-Json
  $cleanFailState = Invoke-State @('Show', '-StatePath', $cleanFailure.StatePath)
  $cleanEvidencePath = Join-Path $cleanFailure.RunRoot "$($cleanFailure.RunId).recovery.json"
  if ($cleanFailJson.failurePolicy -ne 'close_clean' -or $cleanFailState.state -ne 'IDLE' -or
      $null -ne $cleanFailState.runId -or (Test-Path -LiteralPath $cleanEvidencePath) -or
      $cleanFailState.pendingDecision.decisionId -ne $cleanDecisionId -or $cleanFailState.decisionFlow.status -ne 'AWAITING_DECISION') {
    throw "clean failure was not closed without recovery evidence or lost decision state: $($cleanFail.Output)"
  }

  $recoverableFailure = New-FailureFixture 'fail-recoverable' '20202020-2020-4020-8020-202020202020'
  Write-Utf8 (Join-Path $recoverableFailure.Repository 'task.txt') "task interrupted`n"
  $recoverableFail = Invoke-Controller @(
    'Fail', '-RepositoryRoot', $recoverableFailure.Repository, '-StatePath', $recoverableFailure.StatePath,
    '-RunRoot', $recoverableFailure.RunRoot, '-RunId', $recoverableFailure.RunId, '-ErrorMessage', 'recoverable failure fixture'
  )
  Assert-Code $recoverableFail 0 'recoverable failure close'
  $recoverableJson = $recoverableFail.Output | ConvertFrom-Json
  $recoverableState = Invoke-State @('Show', '-StatePath', $recoverableFailure.StatePath)
  if ($recoverableJson.failurePolicy -ne 'preserve_recovery' -or $recoverableState.state -ne 'RECOVERABLE' -or
      $recoverableState.recoveryEvidenceHash -notmatch '^[0-9a-f]{64}$' -or
      -not (Test-Path -LiteralPath $recoverableState.recoveryEvidencePath)) {
    throw "recoverable failure did not persist evidence: $($recoverableFail.Output)"
  }
  $recoverableEvidence = [IO.File]::ReadAllText([string]$recoverableState.recoveryEvidencePath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
  if ($recoverableEvidence.schemaVersion -ne 2 -or $recoverableEvidence.payloadHash -cne $recoverableState.recoveryEvidenceHash) {
    throw 'recoverable failure evidence schema/hash did not match state'
  }
  $originalRecoveryBaseline = [string]$recoverableState.recoveryBaselinePath
  $originalRecoveryPaths = @($recoverableState.expectedPaths) -join '|'

  $recoveryRunId = '21212121-2121-4121-8121-212121212121'
  $resume = Invoke-Controller @(
    'Start', '-RepositoryRoot', $recoverableFailure.Repository, '-StatePath', $recoverableFailure.StatePath,
    '-RunRoot', $recoverableFailure.RunRoot, '-RunId', $recoveryRunId, '-ActualModel', 'gpt-test'
  )
  Assert-Code $resume 0 'recoverable fixture resume'
  $resumeJson = $resume.Output | ConvertFrom-Json
  $resumedState = Invoke-State @('Show', '-StatePath', $recoverableFailure.StatePath)
  if ($resumeJson.action -ne 'resume_task' -or $resumeJson.branchKind -ne 'recovery' -or
      $resumedState.runMode -ne 'recovery' -or $resumedState.state -ne 'RUNNING' -or
      [string]$resumeJson.baselinePath -cne $originalRecoveryBaseline -or
      (@($resumedState.expectedPaths) -join '|') -cne $originalRecoveryPaths) {
    throw "only RECOVERABLE state did not resume with original evidence: $($resume.Output)"
  }

  $unsafeFailure = New-FailureFixture 'fail-unsafe' '30303030-3030-4030-8030-303030303030'
  Write-Utf8 (Join-Path $unsafeFailure.Repository 'task.txt') "task interrupted`n"
  Write-Utf8 (Join-Path $unsafeFailure.Repository 'intruder.txt') "intruder changed`n"
  $unsafeFail = Invoke-Controller @(
    'Fail', '-RepositoryRoot', $unsafeFailure.Repository, '-StatePath', $unsafeFailure.StatePath,
    '-RunRoot', $unsafeFailure.RunRoot, '-RunId', $unsafeFailure.RunId, '-ErrorMessage', 'unsafe failure fixture'
  )
  Assert-Code $unsafeFail 0 'unsafe failure close'
  $unsafeFailJson = $unsafeFail.Output | ConvertFrom-Json
  $unsafeFailState = Invoke-State @('Show', '-StatePath', $unsafeFailure.StatePath)
  if ($unsafeFailJson.failurePolicy -ne 'auto_blocked' -or $unsafeFailState.state -ne 'AUTO-BLOCKED' -or
      @($unsafeFailJson.conflictingPaths) -notcontains 'intruder.txt' -or
      $null -ne $unsafeFailState.recoveryEvidencePath -or $null -ne $unsafeFailState.recoveryEvidenceHash) {
    throw "unsafe failure was not blocked with conflicting path evidence: $($unsafeFail.Output)"
  }

  $staleStatePath = Join-Path $sandbox 'stale-running-state.json'
  $staleRunRoot = Join-Path $sandbox 'stale-running-runs'
  $staleFixture = [ordered]@{
    schemaVersion = 6
    controllerId = 'tzg-hourly-controller'
    runId = '40404040-4040-4040-8040-404040404040'
    runMode = 'recovery'
    state = 'RUNNING'
    leaseExpiresAt = $null
    taskKind = 'execute'
    taskId = $recoverableFailure.TaskId
    taskExecutor = 'codex'
    checkpoint = 'mutation_started'
    expectedPaths = @('task.txt')
    recoveryBaselinePath = $originalRecoveryBaseline
    recoveryEvidencePath = [string]$recoverableState.recoveryEvidencePath
    recoveryEvidenceHash = [string]$recoverableState.recoveryEvidenceHash
    recoveryCount = 0
  }
  Write-Utf8 $staleStatePath ($staleFixture | ConvertTo-Json -Depth 5)
  $staleStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $recoverableFailure.Repository, '-StatePath', $staleStatePath,
    '-RunRoot', $staleRunRoot, '-RunId', '41414141-4141-4141-8141-414141414141', '-ActualModel', 'gpt-test'
  )
  Assert-Code $staleStart 13 'stale RUNNING start rejection'
  if ($staleStart.Output -notmatch 'stale_running_state' -or (Invoke-State @('Show', '-StatePath', $staleStatePath)).state -ne 'RUNNING') {
    throw "stale RUNNING state entered recovery check or was mutated: $($staleStart.Output)"
  }

  $incompleteStatePath = Join-Path $sandbox 'incomplete-recovery-state.json'
  $incompleteRunRoot = Join-Path $sandbox 'incomplete-recovery-runs'
  $incompleteFixture = [ordered]@{
    schemaVersion = 6
    controllerId = 'tzg-hourly-controller'
    runId = $null
    runMode = $null
    state = 'RECOVERABLE'
    leaseExpiresAt = $null
    taskKind = 'execute'
    taskId = $recoverableFailure.TaskId
    taskExecutor = 'codex'
    checkpoint = 'mutation_started'
    expectedPaths = @('task.txt')
    recoveryBaselinePath = $originalRecoveryBaseline
    recoveryEvidencePath = $null
    recoveryEvidenceHash = $null
    recoveryCount = 0
  }
  Write-Utf8 $incompleteStatePath ($incompleteFixture | ConvertTo-Json -Depth 5)
  $incompleteRunId = '50505050-5050-4050-8050-505050505050'
  $incompleteStart = Invoke-Controller @(
    'Start', '-RepositoryRoot', $recoverableFailure.Repository, '-StatePath', $incompleteStatePath,
    '-RunRoot', $incompleteRunRoot, '-RunId', $incompleteRunId, '-ActualModel', 'gpt-test'
  )
  Assert-Code $incompleteStart 1 'incomplete RECOVERABLE stop'
  $incompleteAfter = Invoke-State @('Show', '-StatePath', $incompleteStatePath)
  if ($incompleteAfter.state -ne 'AUTO-BLOCKED' -or $incompleteAfter.recoveryCount -ne 0 -or
      $incompleteStart.Output -notmatch 'recovery_state_incomplete') {
    throw "incomplete RECOVERABLE state was not blocked exactly once: $($incompleteStart.Output)"
  }
  $incompleteAgain = Invoke-Controller @(
    'Start', '-RepositoryRoot', $recoverableFailure.Repository, '-StatePath', $incompleteStatePath,
    '-RunRoot', $incompleteRunRoot, '-RunId', '51515151-5151-4151-8151-515151515151', '-ActualModel', 'gpt-test'
  )
  Assert-Code $incompleteAgain 11 'incomplete recovery second start blocked'
  if ((Invoke-State @('Show', '-StatePath', $incompleteStatePath)).recoveryCount -ne 0) {
    throw 'incomplete recovery entered a repeated stale-running loop'
  }

  $feishuFixtureRoot = Join-Path $sandbox 'feishu-controller-fixture'
  $feishuSeedStatePath = Join-Path $feishuFixtureRoot 'seed-state.json'
  $feishuAcceptedStatePath = Join-Path $feishuFixtureRoot 'accepted-state.json'
  $feishuUnknownStatePath = Join-Path $feishuFixtureRoot 'unknown-state.json'
  $feishuAcceptedRunRoot = Join-Path $feishuFixtureRoot 'accepted-runs'
  $feishuUnknownRunRoot = Join-Path $feishuFixtureRoot 'unknown-runs'
  $feishuAcceptedBridgeRoot = Join-Path $feishuFixtureRoot 'accepted-bridge'
  $feishuUnknownBridgeRoot = Join-Path $feishuFixtureRoot 'unknown-bridge'
  $feishuConfigPath = Join-Path $feishuFixtureRoot 'feishu-private.json'
  $fakeSenderScript = Join-Path $feishuFixtureRoot 'fake-sender.mjs'
  $fakeConsumerScript = Join-Path $feishuFixtureRoot 'fake-consumer.mjs'
  $fakeSenderResultPath = Join-Path $feishuFixtureRoot 'sender-result.json'
  $fakeConsumerResultPath = Join-Path $feishuFixtureRoot 'consumer-result.json'
  $fakeSenderTracePath = Join-Path $feishuFixtureRoot 'sender-trace.jsonl'
  $fakeConsumerTracePath = Join-Path $feishuFixtureRoot 'consumer-trace.jsonl'
  New-Item -ItemType Directory -Path $feishuFixtureRoot -Force | Out-Null
  Write-Utf8 $feishuConfigPath (@{
    schemaVersion = 1
    appId = 'cli_fake_app'
    appSecret = 'controller-secret-must-not-leak'
  } | ConvertTo-Json)

  $senderResultLiteral = $fakeSenderResultPath | ConvertTo-Json -Compress
  $senderTraceLiteral = $fakeSenderTracePath | ConvertTo-Json -Compress
  Write-Utf8 $fakeSenderScript @"
import { appendFileSync, readFileSync } from 'node:fs';
const resultPath = $senderResultLiteral;
const tracePath = $senderTraceLiteral;
const requestPath = process.argv[3];
const request = JSON.parse(readFileSync(requestPath, 'utf8'));
appendFileSync(tracePath, JSON.stringify({ argv: process.argv.slice(2), request }) + '\n');
const output = JSON.parse(readFileSync(resultPath, 'utf8'));
process.stdout.write(JSON.stringify(output) + '\n');
process.exitCode = ({ PROVIDER_ACCEPTED: 0, CHANNEL_UNAVAILABLE: 20, DELIVERY_FAILED: 21, INVALID_INPUT: 22, PROVIDER_OUTCOME_UNKNOWN: 23 })[output.result] ?? 22;
"@
  $consumerResultLiteral = $fakeConsumerResultPath | ConvertTo-Json -Compress
  $consumerTraceLiteral = $fakeConsumerTracePath | ConvertTo-Json -Compress
  Write-Utf8 $fakeConsumerScript @"
import { appendFileSync, readFileSync } from 'node:fs';
const resultPath = $consumerResultLiteral;
const tracePath = $consumerTraceLiteral;
const requestPath = process.argv[3];
const request = JSON.parse(readFileSync(requestPath, 'utf8'));
appendFileSync(tracePath, JSON.stringify({ argv: process.argv.slice(2), request }) + '\n');
const output = JSON.parse(readFileSync(resultPath, 'utf8'));
process.stdout.write(JSON.stringify(output) + '\n');
process.exitCode = output.result === 'INVALID_INPUT' ? 22 : 0;
"@

  $seedRunId = '71717171-7171-4171-8171-717171717171'
  [void](Invoke-State @('Acquire','-StatePath',$feishuSeedStatePath,'-RunId',$seedRunId,'-Now','2026-07-16T01:00:00Z'))
  [void](Invoke-State @(
    'CreateDecision','-StatePath',$feishuSeedStatePath,'-RunId',$seedRunId,
    '-TaskKind','execute','-TaskId','decision-task','-TaskSummary','飞书控制器测试',
    '-DecisionQuestion','采用哪个飞书方案？','-DecisionOptions','A=方案甲|B=方案乙|C=方案丙',
    '-RecommendedOption','A','-ImpactSummary','验证飞书发送与回复消费','-Now','2026-07-16T01:01:00Z'
  ))
  [void](Invoke-State @('Complete','-StatePath',$feishuSeedStatePath,'-RunId',$seedRunId,'-Now','2026-07-16T01:02:00Z'))
  Copy-Item -LiteralPath $feishuSeedStatePath -Destination $feishuAcceptedStatePath
  Copy-Item -LiteralPath $feishuSeedStatePath -Destination $feishuUnknownStatePath
  $feishuDecisionId = [string](Invoke-State @('Show','-StatePath',$feishuSeedStatePath)).pendingDecision.decisionId

  $hashA = 'a' * 64
  $hashB = 'b' * 64
  $hashC = 'c' * 64
  $hashD = 'd' * 64
  $hashE = 'e' * 64
  $hashF = 'f' * 64
  $hashOne = '1' * 64
  $hashTwo = '2' * 64
  Write-Utf8 $fakeSenderResultPath (@{
    result = 'PROVIDER_OUTCOME_UNKNOWN'
    targetHash = $hashA
    cardNonceHash = $hashC
    intentKeyHash = $hashTwo
  } | ConvertTo-Json -Compress)
  $unknownRunId = '72727272-7272-4272-8272-727272727272'
  $unknownStart = Invoke-Controller @(
    'Start','-RepositoryRoot',$repo,'-StatePath',$feishuUnknownStatePath,'-RunRoot',$feishuUnknownRunRoot,
    '-RunId',$unknownRunId,'-ActualModel','gpt-test','-Now','2026-07-16T02:00:00Z'
  )
  Assert-Code $unknownStart 0 'Feishu unknown fixture start'
  if (($unknownStart.Output | ConvertFrom-Json).nextCommand -ne 'SendDecisionNotification') {
    throw "unattempted Feishu decision did not route to sender: $($unknownStart.Output)"
  }
  $unknownSendArguments = @(
    'SendDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$feishuUnknownStatePath,
    '-RunRoot',$feishuUnknownRunRoot,'-RunId',$unknownRunId,'-FeishuConfigPath',$feishuConfigPath,
    '-FeishuBridgeRoot',$feishuUnknownBridgeRoot,'-NodeExecutable','node',
    '-FeishuSenderScript',$fakeSenderScript,'-Now','2026-07-16T02:01:00Z'
  )
  $unknownSend = Invoke-Controller $unknownSendArguments
  Assert-Code $unknownSend 0 'Feishu provider outcome unknown'
  $unknownState = Invoke-State @('Show','-StatePath',$feishuUnknownStatePath)
  if (($unknownSend.Output | ConvertFrom-Json).deliveryResult -ne 'PROVIDER_OUTCOME_UNKNOWN' -or
      ($unknownSend.Output | ConvertFrom-Json).nextCommand -ne 'CompleteNoChange' -or
      @($unknownState.pendingDecision.notificationAttempts).Count -ne 1 -or
      $unknownState.pendingDecision.notificationAttempts[0].provider -ne 'feishu' -or
      $unknownState.pendingDecision.notificationAttempts[0].result -ne 'PROVIDER_OUTCOME_UNKNOWN') {
    throw "unknown provider outcome was not fail-closed: $($unknownSend.Output)"
  }
  $senderTraceCount = @(Get-Content -LiteralPath $fakeSenderTracePath).Count
  $forbiddenResend = Invoke-Controller $unknownSendArguments
  if ($forbiddenResend.Code -ne 15 -or @(Get-Content -LiteralPath $fakeSenderTracePath).Count -ne $senderTraceCount) {
    throw "unknown provider outcome allowed a new sender invocation: $($forbiddenResend.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'CompleteNoChange','-RepositoryRoot',$repo,'-StatePath',$feishuUnknownStatePath,
    '-RunRoot',$feishuUnknownRunRoot,'-RunId',$unknownRunId,'-Now','2026-07-16T02:02:00Z'
  )) 0 'close Feishu unknown run'
  $unknownFollowupRunId = '73737373-7373-4373-8373-737373737373'
  $unknownFollowup = Invoke-Controller @(
    'Start','-RepositoryRoot',$repo,'-StatePath',$feishuUnknownStatePath,'-RunRoot',$feishuUnknownRunRoot,
    '-RunId',$unknownFollowupRunId,'-ActualModel','gpt-test','-Now','2026-07-16T02:03:00Z'
  )
  Assert-Code $unknownFollowup 0 'Feishu unknown follow-up start'
  if (($unknownFollowup.Output | ConvertFrom-Json).nextCommand -ne 'CompleteNoChange' -or
      @($unknownFollowup.Output | ConvertFrom-Json).nextCommands -contains 'SendDecisionNotification') {
    throw "unknown provider outcome re-entered automatic send routing: $($unknownFollowup.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'CompleteNoChange','-RepositoryRoot',$repo,'-StatePath',$feishuUnknownStatePath,
    '-RunRoot',$feishuUnknownRunRoot,'-RunId',$unknownFollowupRunId
  )) 0 'close Feishu unknown follow-up'

  Write-Utf8 $fakeSenderResultPath '{"result":"CHANNEL_UNAVAILABLE"}'
  $unavailableRunId = '70707070-7070-4070-8070-707070707070'
  Assert-Code (Invoke-Controller @(
    'Start','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,'-RunRoot',$feishuAcceptedRunRoot,
    '-RunId',$unavailableRunId,'-ActualModel','gpt-test','-Now','2026-07-16T02:30:00Z'
  )) 0 'Feishu unavailable fixture start'
  $unavailableSend = Invoke-Controller @(
    'SendDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$unavailableRunId,'-FeishuConfigPath',$feishuConfigPath,
    '-FeishuBridgeRoot',$feishuAcceptedBridgeRoot,'-NodeExecutable','node',
    '-FeishuSenderScript',$fakeSenderScript,'-Now','2026-07-16T02:31:00Z'
  )
  Assert-Code $unavailableSend 0 'Feishu channel unavailable'
  $unavailableState = Invoke-State @('Show','-StatePath',$feishuAcceptedStatePath)
  if (($unavailableSend.Output | ConvertFrom-Json).deliveryResult -ne 'CHANNEL_UNAVAILABLE' -or
      ($unavailableSend.Output | ConvertFrom-Json).nextCommand -ne 'CompleteNoChange' -or
      @($unavailableState.pendingDecision.notificationAttempts).Count -ne 0 -or
      (Test-Path -LiteralPath (Join-Path $feishuAcceptedBridgeRoot 'pending-bindings.json'))) {
    throw "CHANNEL_UNAVAILABLE consumed a send attempt or created a binding: $($unavailableSend.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'CompleteNoChange','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$unavailableRunId
  )) 0 'close Feishu unavailable run'

  Write-Utf8 $fakeSenderResultPath (@{
    result = 'PROVIDER_ACCEPTED'
    targetHash = $hashA
    providerMessageIdHash = $hashB
    cardNonceHash = $hashC
  } | ConvertTo-Json -Compress)
  $acceptedSendRunId = '74747474-7474-4474-8474-747474747474'
  $acceptedStart = Invoke-Controller @(
    'Start','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,'-RunRoot',$feishuAcceptedRunRoot,
    '-RunId',$acceptedSendRunId,'-ActualModel','gpt-test','-Now','2026-07-16T03:00:00Z'
  )
  Assert-Code $acceptedStart 0 'Feishu accepted fixture start'
  $traceBeforeForbidden = @(Get-Content -LiteralPath $fakeSenderTracePath).Count
  $forbiddenEvidence = Invoke-Controller @(
    'SendDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$acceptedSendRunId,'-DecisionId','DEC-20260716-GUESSED',
    '-FeishuConfigPath',$feishuConfigPath,'-FeishuBridgeRoot',$feishuAcceptedBridgeRoot,
    '-NodeExecutable','node','-FeishuSenderScript',$fakeSenderScript,'-Now','2026-07-16T03:01:00Z'
  )
  if ($forbiddenEvidence.Code -ne 15 -or @(Get-Content -LiteralPath $fakeSenderTracePath).Count -ne $traceBeforeForbidden) {
    throw "model-supplied Feishu evidence reached the sender: $($forbiddenEvidence.Output)"
  }
  $acceptedSend = Invoke-Controller @(
    'SendDecisionNotification','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$acceptedSendRunId,'-FeishuConfigPath',$feishuConfigPath,
    '-FeishuBridgeRoot',$feishuAcceptedBridgeRoot,'-NodeExecutable','node',
    '-FeishuSenderScript',$fakeSenderScript,'-Now','2026-07-16T03:02:00Z'
  )
  Assert-Code $acceptedSend 0 'Feishu accepted send'
  $acceptedState = Invoke-State @('Show','-StatePath',$feishuAcceptedStatePath)
  $bindingPath = Join-Path $feishuAcceptedBridgeRoot 'pending-bindings.json'
  $binding = [IO.File]::ReadAllText($bindingPath) | ConvertFrom-Json
  $senderTrace = (Get-Content -LiteralPath $fakeSenderTracePath | Select-Object -Last 1) | ConvertFrom-Json
  $requestFiles = @(Get-ChildItem -LiteralPath (Join-Path $feishuAcceptedRunRoot '.feishu-requests') -File -ErrorAction SilentlyContinue)
  if (($acceptedSend.Output | ConvertFrom-Json).deliveryResult -ne 'PROVIDER_ACCEPTED' -or
      @($acceptedState.pendingDecision.notificationAttempts).Count -ne 1 -or
      $acceptedState.pendingDecision.notificationAttempts[0].provider -ne 'feishu' -or
      $acceptedState.pendingDecision.notificationAttempts[0].providerMessageIdHash -ne $hashB -or
      $binding.decisionId -ne $feishuDecisionId -or (@($binding.allowedOptions) -join '|') -ne 'A|B|C' -or
      $binding.cardNonceHash -ne $hashC -or $binding.providerMessageIdHash -ne $hashB -or
      $senderTrace.request.decision.decisionId -ne $feishuDecisionId -or
      @($senderTrace.request.PSObject.Properties.Name).Count -ne 2 -or
      ($senderTrace.argv -join '|') -match 'controller-secret-must-not-leak|feishu-private\.json' -or
      $acceptedSend.Output -match 'controller-secret-must-not-leak' -or $requestFiles.Count -ne 0) {
    throw "accepted Feishu send did not preserve the sanitized boundary: $($acceptedSend.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'CompleteNoChange','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$acceptedSendRunId
  )) 0 'close Feishu accepted send run'

  $consumeRunId = '75757575-7575-4575-8575-757575757575'
  $consumeStart = Invoke-Controller @(
    'Start','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,'-RunRoot',$feishuAcceptedRunRoot,
    '-RunId',$consumeRunId,'-ActualModel','gpt-test','-Now','2026-07-16T04:00:00Z'
  )
  Assert-Code $consumeStart 0 'Feishu consume start'
  $consumeStartJson = $consumeStart.Output | ConvertFrom-Json
  if ($consumeStartJson.nextCommand -ne 'ConsumeDecisionReply' -or
      @($consumeStartJson.nextCommands) -contains 'ResolveDecisionEmailReply' -or
      @($consumeStartJson.nextCommands) -contains 'RetryDecisionNotification') {
    throw "accepted Feishu decision exposed a legacy route: $($consumeStart.Output)"
  }
  Write-Utf8 $fakeConsumerResultPath '{"result":"INVALID_INPUT"}'
  $consumeArguments = @(
    'ConsumeDecisionReply','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$consumeRunId,'-FeishuConfigPath',$feishuConfigPath,
    '-FeishuBridgeRoot',$feishuAcceptedBridgeRoot,'-NodeExecutable','node',
    '-FeishuConsumerScript',$fakeConsumerScript,'-Now','2026-07-16T04:01:00Z'
  )
  $stateBeforeInvalidConsume = [IO.File]::ReadAllBytes($feishuAcceptedStatePath)
  $invalidConsume = Invoke-Controller $consumeArguments
  if ($invalidConsume.Code -ne 15 -or
      -not [Linq.Enumerable]::SequenceEqual([byte[]]$stateBeforeInvalidConsume, [byte[]][IO.File]::ReadAllBytes($feishuAcceptedStatePath))) {
    throw "invalid Feishu consumer result changed pending state: $($invalidConsume.Output)"
  }
  Write-Utf8 $fakeConsumerResultPath '{"result":"NO_REPLY"}'
  $noReply = Invoke-Controller $consumeArguments
  Assert-Code $noReply 0 'Feishu no reply'
  if (($noReply.Output | ConvertFrom-Json).nextCommand -ne 'CompleteNoChange' -or
      (Invoke-State @('Show','-StatePath',$feishuAcceptedStatePath)).pendingDecision.decisionId -ne $feishuDecisionId) {
    throw "Feishu NO_REPLY did not preserve the pending decision: $($noReply.Output)"
  }
  Assert-Code (Invoke-Controller @(
    'CompleteNoChange','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$consumeRunId
  )) 0 'close Feishu no-reply run'

  $resolveRunId = '76767676-7676-4676-8676-767676767676'
  Assert-Code (Invoke-Controller @(
    'Start','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,'-RunRoot',$feishuAcceptedRunRoot,
    '-RunId',$resolveRunId,'-ActualModel','gpt-test','-Now','2026-07-16T05:00:00Z'
  )) 0 'Feishu reply resolution start'
  $resolveArguments = @(
    'ConsumeDecisionReply','-RepositoryRoot',$repo,'-StatePath',$feishuAcceptedStatePath,
    '-RunRoot',$feishuAcceptedRunRoot,'-RunId',$resolveRunId,'-FeishuConfigPath',$feishuConfigPath,
    '-FeishuBridgeRoot',$feishuAcceptedBridgeRoot,'-NodeExecutable','node',
    '-FeishuConsumerScript',$fakeConsumerScript,'-Now','2026-07-16T05:01:00Z'
  )
  Write-Utf8 $fakeConsumerResultPath (@{
    result='REPLY_ACCEPTED';optionKey='A';source='feishu_card';providerMessageIdHash=('9' * 64)
    providerEventIdHash=$hashD;operatorOpenIdHash=$hashE;tenantKeyHash=$hashF;cardNonceHash=$hashC;evidenceHash=$hashOne
  } | ConvertTo-Json -Compress)
  $mismatchedReply = Invoke-Controller $resolveArguments
  if ($mismatchedReply.Code -ne 15 -or
      (Invoke-State @('Show','-StatePath',$feishuAcceptedStatePath)).pendingDecision.decisionId -ne $feishuDecisionId) {
    throw "mismatched Feishu reply resolved the decision: $($mismatchedReply.Output)"
  }
  Write-Utf8 $fakeConsumerResultPath (@{
    result='REPLY_ACCEPTED';optionKey='A';source='feishu_card';providerMessageIdHash=$hashB
    providerEventIdHash=$hashD;operatorOpenIdHash=$hashE;tenantKeyHash=$hashF;cardNonceHash=$hashC;evidenceHash=$hashOne
  } | ConvertTo-Json -Compress)
  $resolvedReply = Invoke-Controller $resolveArguments
  Assert-Code $resolvedReply 0 'valid Feishu reply resolution'
  $resolvedReplyJson = $resolvedReply.Output | ConvertFrom-Json
  $resolvedFeishuState = Invoke-State @('Show','-StatePath',$feishuAcceptedStatePath)
  $feishuResolution = $resolvedFeishuState.decisionFlow.resolvedDecisions[0].resolution
  $consumerTrace = (Get-Content -LiteralPath $fakeConsumerTracePath | Select-Object -Last 1) | ConvertFrom-Json
  if ($resolvedReplyJson.nextCommand -ne 'InspectCandidate' -or $resolvedReplyJson.taskId -ne 'decision-task' -or
      $null -ne $resolvedFeishuState.pendingDecision -or $feishuResolution.source -ne 'feishu_card' -or
      $feishuResolution.optionKey -ne 'A' -or $feishuResolution.providerMessageIdHash -ne $hashB -or
      $feishuResolution.operatorOpenIdHash -ne $hashE -or $feishuResolution.evidenceHash -ne $hashOne -or
      $consumerTrace.request.pendingDecision.decisionId -ne $feishuDecisionId -or
      ($consumerTrace.argv -join '|') -match 'controller-secret-must-not-leak|feishu-private\.json' -or
      $resolvedReply.Output -match 'controller-secret-must-not-leak') {
    throw "valid Feishu reply did not resolve with hash-only evidence: $($resolvedReply.Output)"
  }
  [void](Invoke-State @('Complete','-StatePath',$feishuAcceptedStatePath,'-RunId',$resolveRunId,'-Now','2026-07-16T05:02:00Z'))

  'test-automation-controller: OK'
} finally {
  if ($safeToRemove) {
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
  }
}
