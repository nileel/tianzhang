#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Invoke-Git { param([string]$Root, [string[]]$Arguments) $o = @(& git -C $Root @Arguments 2>&1); if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' '): $(@($o) -join "`n")" }; (@($o) -join "`n").Trim() }
function Get-ExpectedDirectDecisionId {
  param([string]$TaskId, [object]$Run, [string]$CheckpointCommit)
  $startedAt = [DateTimeOffset]::Parse([string]$Run.startedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
  $material = "$TaskId$([char]0)$([string]$Run.runId)$([char]0)$CheckpointCommit"
  $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($material)))
  "DEC-$($startedAt.ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture))-CD$($hash.Substring(0, 12))"
}

$testId = [Guid]::NewGuid().ToString('N')
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $tempBase "tzg-codex-candidate-test-$testId"
$mainRoot = Join-Path $testRoot 'repository'
$fakeBin = Join-Path $testRoot 'bin'
$tracePath = Join-Path $testRoot 'codex-trace.txt'
$approvedState = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId"
$qmStateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId-qm"
$failureStateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId-failure"
$pathMismatchStateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId-path-mismatch"
$reviewStateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId-review"
$wrapperPath = Join-Path $PSScriptRoot 'invoke-codex-candidate.ps1'
$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$originalPath = $env:PATH
$originalTrace = $env:TZG_FAKE_CODEX_TRACE
$originalMismatch = $env:TZG_FAKE_CODEX_MISMATCH
$originalDirtyFailure = $env:TZG_FAKE_CODEX_DIRTY_FAILURE
$originalRenameCompressed = $env:TZG_FAKE_CODEX_RENAME_COMPRESSED
$originalTerminalMode = $env:TZG_FAKE_CODEX_TERMINAL_MODE
$taskId = 'TASK-CODEX-CANDIDATE'
$scenarioStateRoots = [Collections.Generic.List[string]]::new()
. (Join-Path $PSScriptRoot 'private-path-acl.ps1')

$ownerSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'))
$applyCheckpoint = [regex]::Match($ownerSource, '(?ms)function Apply-CheckpointToNewRun \{.*?(?=\r?\nfunction Remove-ConsumedCheckpointWorktree)')
Assert-True ($applyCheckpoint.Success -and $applyCheckpoint.Value -cmatch "schemaVersion = 1; kind = 'decision_checkpoint'") 'Normal checkpoint resume context lost its explicit kind'

function Invoke-TerminalScenario {
  param([string]$Mode)
  $scenarioStateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId-$Mode"
  $scenarioStateRoots.Add($scenarioStateRoot)
  $claimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $scenarioStateRoot -Owner codex -TaskId $taskId -Route codex_execute -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $base -TaskCardDigest $digest)
  $scenarioRun = ($claimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$scenarioRun.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$scenarioRun.candidateBranch, [string]$scenarioRun.worktree, $base) | Out-Null
  $env:TZG_FAKE_CODEX_TERMINAL_MODE = $Mode
  try {
    $scenarioOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Candidate -Route Execution -RepositoryRoot ([string]$scenarioRun.worktree) -TaskId $taskId -RunId ([string]$scenarioRun.runId) -Model 'test-codex-model' -StateRoot $scenarioStateRoot -ResponsibilityTimeoutSeconds 30)
  } finally {
    $env:TZG_FAKE_CODEX_TERMINAL_MODE = $null
  }
  Assert-Equal $scenarioOutput.Count 1 "Codex terminal scenario output count mismatch: $Mode"
  [pscustomobject]@{ Terminal = ($scenarioOutput[0] | ConvertFrom-Json -Depth 50); Run = $scenarioRun }
}

function Invoke-ResumeContextScenario {
  param([ValidateSet('valid', 'missing', 'unknown')][string]$Mode)
  $scenarioStateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId-resume-$Mode"
  $scenarioStateRoots.Add($scenarioStateRoot)
  $claimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $scenarioStateRoot -Owner codex -TaskId $taskId -Route codex_execute -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $base -TaskCardDigest $digest)
  $scenarioRun = ($claimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$scenarioRun.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$scenarioRun.candidateBranch, [string]$scenarioRun.worktree, $base) | Out-Null
  Write-Utf8 -Path (Join-Path ([string]$scenarioRun.worktree) 'fixture/rename-source.txt') -Text 'checkpoint replay fixture'
  Invoke-Git -Root ([string]$scenarioRun.worktree) -Arguments @('add', '--', 'fixture/rename-source.txt') | Out-Null

  $context = [ordered]@{
    schemaVersion = 1; taskId = $taskId; decisionId = 'DEC-20260825-RESUMEFIXTURE'
    replyKind = 'option'; replyValue = 'A'; source = 'feishu_card'; evidenceHash = ('a' * 64)
    checkpointCommit = $base; checkpointChangedPaths = @('fixture/rename-source.txt')
  }
  if ($Mode -ceq 'valid') { $context['kind'] = 'decision_checkpoint' }
  elseif ($Mode -ceq 'unknown') { $context['kind'] = 'unknown_checkpoint' }
  $resumeDirectory = Join-Path $scenarioStateRoot 'resume-contexts'
  [IO.Directory]::CreateDirectory($resumeDirectory) | Out-Null
  Set-PrivatePathAcl -Path $resumeDirectory -Directory
  $resumePath = Join-Path $resumeDirectory "$($scenarioRun.runId).json"
  Write-Utf8 -Path $resumePath -Text ($context | ConvertTo-Json -Compress -Depth 20)
  Set-PrivatePathAcl -Path $resumePath
  Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
  $scenarioOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Candidate -Route Execution -RepositoryRoot ([string]$scenarioRun.worktree) -TaskId $taskId -RunId ([string]$scenarioRun.runId) -Model 'test-codex-model' -StateRoot $scenarioStateRoot -ResumeContextPath $resumePath -ResponsibilityTimeoutSeconds 30)
  Assert-Equal $scenarioOutput.Count 1 "Codex resume context output count mismatch: $Mode"
  [pscustomobject]@{
    Terminal = ($scenarioOutput[0] | ConvertFrom-Json -Depth 50)
    Run = $scenarioRun
    RunnerStarted = Test-Path -LiteralPath $tracePath
    Trace = if (Test-Path -LiteralPath $tracePath) { [IO.File]::ReadAllText($tracePath) } else { '' }
  }
}

try {
  [IO.Directory]::CreateDirectory((Join-Path $mainRoot 'tools')) | Out-Null
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  foreach ($tool in @('automation-finalize-commit.ps1', 'automation-commit-metadata.ps1', 'check-pending-whitespace.ps1', 'check-task-cards.ps1', 'private-path-acl.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $mainRoot "tools/$tool")
  }
  Write-Utf8 -Path (Join-Path $mainRoot '.gitignore') -Text ".worktrees/`n"
  Write-Utf8 -Path (Join-Path $mainRoot 'AGENTS.md') -Text '# Codex candidate fixture'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/自动工作流规则.txt') -Text '# workflow rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/AI协作规则.txt') -Text '# collaboration rules'
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/未通过审核清单.txt') -Text '# review list fixture'
  Write-Utf8 -Path (Join-Path $mainRoot 'fixture/rename-source.txt') -Text 'rename fixture'
  $metadata = [ordered]@{
    schemaVersion = 1; id = $taskId; title = 'Codex candidate fixture'; priority = 'P1'; route = 'codex_execute'; owner = 'codex'
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @(); stateReason = 'fixture'
    expectedPaths = @('fixture/rename-source.txt', 'fixture/rename-target.txt', '开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt', "开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt", '开发管理/未通过审核清单.txt')
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $taskId · Codex candidate fixture",
    '## 来源与当前边界', '- fixture', '## 必查范围', '- fixture', '## 实施范围', '- block fixture task',
    '## 禁止项', '- no extra paths', '## 验证', '- task-card checker', '## 完成条件', '- blocked state', '## 停止条件', '- invalid projection'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $mainRoot "开发管理/任务卡/$taskId.txt") -Text $card
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/当前任务队列.txt') -Text (@(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | codex_execute | codex | P1 | automation | implementation | Codex candidate fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Write-Utf8 -Path (Join-Path $mainRoot '开发管理/任务列表/自动化任务.txt') -Text (@(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | P1 | codex | 已排队 | — | Codex candidate fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Invoke-Git -Root $mainRoot -Arguments @('init') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.name', 'Codex Candidate Test') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('config', 'user.email', 'codex-candidate@example.invalid') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('add', '--', '.') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('commit', '-m', 'test: initialize Codex candidate fixture') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('branch', '-M', 'master') | Out-Null
  $base = Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')
  $cardText = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes((Join-Path $mainRoot "开发管理/任务卡/$taskId.txt"))).TrimStart([char]0xFEFF)
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($cardText.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
  $claimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $stateRoot -Owner codex -TaskId $taskId -Route codex_execute -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $base -TaskCardDigest $digest)
  $run = ($claimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$run.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$run.candidateBranch, [string]$run.worktree, $base) | Out-Null

  Write-Utf8 -Path (Join-Path $fakeBin 'fake-codex.ps1') -Text @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CliArguments)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$prompt = [Console]::In.ReadToEnd()
[IO.File]::WriteAllText($env:TZG_FAKE_CODEX_TRACE, $prompt, [Text.UTF8Encoding]::new($false))
$outputIndex = [Array]::IndexOf($CliArguments, '--output-last-message')
if ($outputIndex -lt 0) { throw 'fake Codex output path missing' }
$outputPath = $CliArguments[$outputIndex + 1]
$schemaIndex = [Array]::IndexOf($CliArguments, '--output-schema')
if ($schemaIndex -lt 0) { throw 'fake Codex schema path missing' }
$schema = [IO.File]::ReadAllText($CliArguments[$schemaIndex + 1], [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50
$optionsProperty = $schema.properties.PSObject.Properties['options']
if ($null -ne $optionsProperty -and @($optionsProperty.Value.items.required) -cnotcontains 'targetState') {
  throw 'candidate output schema does not require targetState'
}
$decisionIdProperty = $schema.properties.PSObject.Properties['decisionId']
if ($null -ne $decisionIdProperty -and [string]$decisionIdProperty.Value.description -cnotmatch 'deterministic wrapper') {
  throw 'candidate output schema does not identify deterministic decision ID ownership'
}
$modelIndex = [Array]::IndexOf($CliArguments, '-m')
$model = $CliArguments[$modelIndex + 1]
if ($prompt.Contains('[TZG_CODEX_CANARY]')) {
  $probePath = Join-Path ([Environment]::CurrentDirectory) '.tzg-codex-canary-probe.txt'
  $probeText = if ($env:TZG_FAKE_CODEX_MISMATCH -eq '1') { 'TZG_CODEX_CANDIDATE_METADATA_CANARY_MISMATCH' } else { 'TZG_CODEX_CANDIDATE_METADATA_CANARY' }
  [IO.File]::WriteAllText($probePath, $probeText, [Text.UTF8Encoding]::new($false))
  $resultText = '问题=候选提交合同需要真实核验；完成=canary 已通过正式 finalizer 创建提交'
  $impactText = '影响=验证 Codex 候选提交元数据链路；边界=仅修改隔离 canary worktree'
  $verifyText = '验证=提交元数据与终态字段一致；后续=由外层清理 canary worktree'
  $plainText = '发生=自动化完成了一次隔离提交探针；影响=不会进入主分支；需要=无需处理'
  $commit = [string](& pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 `
    -RepositoryRoot ([Environment]::CurrentDirectory) -ExpectedPaths '.tzg-codex-canary-probe.txt' `
    -CommitMessage 'canary: verify Codex candidate metadata contract' -RequireAutomationMetadata `
    -AutomationTask 'CANARY' -AutomationState 'completed' -AutomationResult $resultText `
    -AutomationImpact $impactText -AutomationVerify $verifyText -AutomationPlain $plainText | Select-Object -Last 1)
  if ($LASTEXITCODE -ne 0) { throw 'fake Codex canary commit failed' }
  $terminal = [ordered]@{
    status = 'verified'; identity = 'Codex'; model = $model; candidateCommit = [string]$commit
    result = $(if ($env:TZG_FAKE_CODEX_MISMATCH -eq '1') { '问题=终态故意不一致；完成=验证拒绝路径' } else { $resultText })
    impact = $impactText; verify = $verifyText; plain = $plainText
  }
} elseif (-not [string]::IsNullOrWhiteSpace($env:TZG_FAKE_CODEX_TERMINAL_MODE)) {
  $mode = $env:TZG_FAKE_CODEX_TERMINAL_MODE
  $candidateCommit = ''
  $changedPaths = @()
  $options = @(
    [ordered]@{ key = 'A'; label = 'option A'; targetState = 'ready' },
    [ordered]@{ key = 'B'; label = 'option B'; targetState = 'ready' },
    [ordered]@{ key = 'C'; label = 'option C'; targetState = 'blocked' }
  )
  if ($mode -ceq 'blocked-restored') {
    $queuePath = Join-Path ([Environment]::CurrentDirectory) '开发管理/当前任务队列.txt'
    $originalQueue = [IO.File]::ReadAllBytes($queuePath)
    [IO.File]::WriteAllText($queuePath, ([IO.File]::ReadAllText($queuePath) + "`n<!-- restored blocker fixture -->"), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes($queuePath, $originalQueue)
  } elseif ($mode -ceq 'blocked-dirty') {
    [IO.File]::WriteAllText((Join-Path ([Environment]::CurrentDirectory) 'fixture/dirty.txt'), 'dirty blocker fixture', [Text.UTF8Encoding]::new($false))
  } elseif ($mode -cin @('checkpoint-valid', 'checkpoint-empty-mechanical', 'checkpoint-empty-label', 'checkpoint-invalid-recommended', 'checkpoint-head-changed', 'checkpoint-wrong-path', 'checkpoint-incomplete-abc')) {
    $sourcePath = Join-Path ([Environment]::CurrentDirectory) 'fixture/rename-source.txt'
    [IO.File]::WriteAllText($sourcePath, 'checkpoint fixture', [Text.UTF8Encoding]::new($false))
    & git add -- 'fixture/rename-source.txt'
    & git commit -m 'test: create decision checkpoint'
    if ($LASTEXITCODE -ne 0) { throw 'fake decision checkpoint failed' }
    $actualCommit = [string](& git rev-parse HEAD)
    $candidateCommit = if ($mode -ceq 'checkpoint-head-changed') { '' } else { $actualCommit }
    $changedPaths = if ($mode -ceq 'checkpoint-wrong-path') { @('fixture/wrong-path.txt') } else { @('fixture/rename-source.txt') }
    if ($mode -ceq 'checkpoint-incomplete-abc') { $options = @($options[0], $options[1]) }
    if ($mode -ceq 'checkpoint-empty-label') { $options[1].label = '' }
  } elseif ($mode -ceq 'checkpoint-fake-sha') {
    $candidateCommit = '0' * 40
    $changedPaths = @('fixture/rename-source.txt')
  } elseif ($mode -cne 'blocked-clean') {
    throw "unknown fake terminal mode: $mode"
  }
  $terminal = [ordered]@{
    status = 'needs_decision'; identity = 'Codex'; model = $model; candidateCommit = $candidateCommit; expectedTransition = ''
    changedPaths = $changedPaths; verified = @('fixture evidence'); unverified = @('owner decision')
    residualRisk = 'fixture only'; result = ''; impact = ''; verify = ''; plain = ''
    decisionId = $(if ($mode -ceq 'checkpoint-empty-mechanical') { '' } else { 'DEC-20260811-CANDIDATEFIXTURE' }); question = 'Choose the fixture outcome.'; options = $options
    recommendedOption = $(if ($mode -ceq 'checkpoint-invalid-recommended') { 'D' } else { 'A' }); impactSummary = 'fixture impact'
    plainSummary = $(if ($mode -ceq 'checkpoint-empty-mechanical') { [ordered]@{ situation = ''; impact = ''; action = '' } } else { [ordered]@{ situation = 'fixture situation'; impact = 'fixture impact'; action = 'choose A' } })
    detailCode = 'combat_core_switch_contract_incomplete'
  }
} elseif ($env:TZG_FAKE_CODEX_DIRTY_FAILURE -eq '1') {
  $queuePath = Join-Path ([Environment]::CurrentDirectory) '开发管理/当前任务队列.txt'
  [IO.File]::WriteAllText($queuePath, ([IO.File]::ReadAllText($queuePath) + "`n<!-- dirty failure fixture -->"), [Text.UTF8Encoding]::new($false))
  $terminal = [ordered]@{
    status = 'failed'; identity = 'Codex'; model = $model; candidateCommit = ''; expectedTransition = ''
    changedPaths = @('开发管理/当前任务队列.txt'); verified = @(); unverified = @('candidate commit not created')
    residualRisk = 'fixture dirty worktree'; result = ''; impact = ''; verify = ''; plain = ''
    detailCode = 'AUTOMATION_METADATA_CONTRACT_INVALID'
  }
} elseif ($prompt.Contains('Route: QueueMaintenance')) {
  $terminal = [ordered]@{
    status = 'no_candidate'; identity = 'Codex'; model = $model
    candidateCommit = ''; changedPaths = @(); verified = @(); unverified = @()
    residualRisk = ''; result = ''; impact = ''; verify = ''; plain = ''
  }
} else {
  $taskId = 'TASK-CODEX-CANDIDATE'
  & git mv -- 'fixture/rename-source.txt' 'fixture/rename-target.txt'
  if ($LASTEXITCODE -ne 0) { throw 'fake Codex rename failed' }
  $cardPath = Join-Path ([Environment]::CurrentDirectory) "开发管理/任务卡/$taskId.txt"
  $card = [IO.File]::ReadAllText($cardPath)
  $card = $card.Replace('"dispatchState": "ready"', '"dispatchState": "blocked"').Replace('"stateReason": "fixture"', '"stateReason": "fixture blocker confirmed"')
  [IO.File]::WriteAllText($cardPath, $card, [Text.UTF8Encoding]::new($false))
  $queuePath = Join-Path ([Environment]::CurrentDirectory) '开发管理/当前任务队列.txt'
  $queue = @([IO.File]::ReadAllLines($queuePath) | Where-Object { $_ -notmatch '^\| TASK-CODEX-CANDIDATE \|' }) -join "`n"
  [IO.File]::WriteAllText($queuePath, $queue, [Text.UTF8Encoding]::new($false))
  $backlogPath = Join-Path ([Environment]::CurrentDirectory) '开发管理/任务列表/自动化任务.txt'
  $backlog = [IO.File]::ReadAllText($backlogPath).Replace('| TASK-CODEX-CANDIDATE | P1 | codex | 已排队 |', '| TASK-CODEX-CANDIDATE | P1 | codex | 阻塞 |')
  [IO.File]::WriteAllText($backlogPath, $backlog, [Text.UTF8Encoding]::new($false))
  $resultText = '问题=测试任务仍可调度；完成=确认阻塞并移出队列'
  $impactText = '影响=验证 Codex 候选入口；边界=不修改真实任务'
  $verifyText = '验证=任务投影检查通过；后续=等待固定入口集成'
  $plainText = '发生=测试任务被标记为暂不可执行；影响=只验证自动流程；需要=无需处理'
  $paths = "fixture/rename-source.txt|fixture/rename-target.txt|开发管理/任务列表/自动化任务.txt|开发管理/当前任务队列.txt|开发管理/任务卡/$taskId.txt"
  $commit = [string](& pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 `
    -RepositoryRoot ([Environment]::CurrentDirectory) -ExpectedPaths $paths `
    -CommitMessage 'test: close Codex candidate fixture' -RequireAutomationMetadata `
    -AutomationTask $taskId -AutomationState 'completed' -AutomationResult $resultText `
    -AutomationImpact $impactText -AutomationVerify $verifyText -AutomationPlain $plainText | Select-Object -Last 1)
  if ($LASTEXITCODE -ne 0) { throw 'fake Codex commit failed' }
  $changedPaths = if ($env:TZG_FAKE_CODEX_RENAME_COMPRESSED -eq '1') {
    @(& git -c core.quotepath=false show --format= --name-only $commit | Where-Object { $_ } | Sort-Object -Unique)
  } else {
    @(& git -c core.quotepath=false diff --name-only --no-renames "$commit^..$commit" | Where-Object { $_ } | Sort-Object -Unique)
  }
  $terminal = [ordered]@{
    status = 'completed'; identity = 'Codex'; model = $model; candidateCommit = [string]$commit
    expectedTransition = 'blocked'; changedPaths = $changedPaths; verified = @('task-card checker passed')
    unverified = @('none'); residualRisk = 'fixture only'; result = $resultText; impact = $impactText; verify = $verifyText; plain = $plainText
  }
}
[IO.File]::WriteAllText($outputPath, ($terminal | ConvertTo-Json -Compress -Depth 20), [Text.UTF8Encoding]::new($false))
[Console]::Out.WriteLine(([ordered]@{ type = 'thread.started'; thread_id = [Guid]::NewGuid().ToString() } | ConvertTo-Json -Compress))
'@
  Write-Utf8 -Path (Join-Path $fakeBin 'codex.cmd') -Text "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"%~dp0fake-codex.ps1`" %*"
  $env:PATH = "$fakeBin;$originalPath"
  $env:TZG_FAKE_CODEX_TRACE = $tracePath
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Candidate -Route Execution -RepositoryRoot ([string]$run.worktree) -TaskId $taskId -RunId ([string]$run.runId) -Model 'test-codex-model' -StateRoot $stateRoot -ResponsibilityTimeoutSeconds 30)
  Assert-Equal $LASTEXITCODE 0 'Codex candidate wrapper process failed'
  Assert-Equal $output.Count 1 'Codex candidate wrapper output count mismatch'
  $candidate = $output[0] | ConvertFrom-Json -Depth 50
  $candidateRepositoryEvidence = [ordered]@{
    head = Invoke-Git -Root ([string]$run.worktree) -Arguments @('rev-parse', 'HEAD')
    base = $base
    count = Invoke-Git -Root ([string]$run.worktree) -Arguments @('rev-list', '--count', "$base..HEAD")
    parent = Invoke-Git -Root ([string]$run.worktree) -Arguments @('rev-parse', 'HEAD^')
    status = Invoke-Git -Root ([string]$run.worktree) -Arguments @('status', '--porcelain=v1', '--untracked-files=all')
  }
  Assert-Equal ([string]$candidate.status) 'completed' "Codex candidate failed: $($candidate | ConvertTo-Json -Compress -Depth 20); repository=$($candidateRepositoryEvidence | ConvertTo-Json -Compress)"
  Assert-True ([string]$candidate.sessionId -ne '') 'Codex candidate sessionId is missing'
  Assert-Equal ([string]$candidate.candidateResult.expectedTransition) 'blocked' 'Codex candidate transition mismatch'
  Assert-True (@($candidate.candidateResult.changedPaths) -ccontains 'fixture/rename-source.txt') "Codex candidate lost rename source path: $(@($candidate.candidateResult.changedPaths) -join '|')"
  Assert-True (@($candidate.candidateResult.changedPaths) -ccontains 'fixture/rename-target.txt') "Codex candidate lost rename target path: $(@($candidate.candidateResult.changedPaths) -join '|')"
  Assert-True (@($candidate.candidateResult.changedPaths) -ccontains "开发管理/任务卡/$taskId.txt") "Codex candidate lost task-card path: $(@($candidate.candidateResult.changedPaths) -join '|')"
  $trace = [IO.File]::ReadAllText($tracePath)
  Assert-True ($trace -match '\[TZG_CODEX_CANDIDATE\]') 'Codex candidate prompt marker is missing'
  Assert-True ($trace -match 'claim') 'Codex candidate prompt omitted fixed claim boundary'
  Assert-True ($trace -match 'worktree') 'Codex candidate prompt omitted worktree boundary'
  Assert-True ($trace.Contains('除 QueueMaintenance 外，不得从其他任务卡的 blockedBy 或 backlog 行的「阻塞于」投影移除当前 taskId')) 'Codex candidate prompt omitted the downstream dependency boundary'
  Assert-True ($trace.Contains('只有正式结果进入 master 后，后续 QueueMaintenance 才按既有事实源同时更新下游任务卡和 backlog 投影')) 'Codex candidate prompt moved downstream dependency cleanup outside QueueMaintenance'
  Assert-True ($trace -match 'automation-finalize-commit\.ps1') 'Codex candidate prompt omitted the formal finalizer'
  Assert-True ($trace -match '-RequireAutomationMetadata') 'Codex candidate prompt omitted required automation metadata'
  Assert-True ($trace -match "-AutomationTask '$taskId'") 'Codex candidate prompt omitted the exact task metadata'
  Assert-True ($trace -match '-AutomationResult 的值精确为 问题=<问题>；完成=<完成>') 'Codex candidate prompt omitted the raw Result parameter grammar'
  Assert-True ($trace -match '-AutomationImpact 的值精确为 影响=<影响>；边界=<边界>') 'Codex candidate prompt omitted the raw Impact parameter grammar'
  Assert-True ($trace -match '-AutomationVerify 的值精确为 验证=<验证>；后续=<后续>') 'Codex candidate prompt omitted the raw Verify parameter grammar'
  Assert-True ($trace -match '-AutomationPlain 的值精确为 发生=<发生>；影响=<影响>；需要=<需要>') 'Codex candidate prompt omitted the raw Plain parameter grammar'
  Assert-True ($trace -match '不得把 result=、impact=、verify=、plain= 写入对应参数值') 'Codex candidate prompt did not reject JSON field prefixes inside metadata parameter values'
  Assert-True ($trace -notmatch '四值必须逐字满足以下格式[^\r\n]*result=问题=') 'Codex candidate prompt retained the ambiguous prefixed metadata grammar'
  Assert-True ($trace -match '技术失败同样先恢复工作树到本轮初始状态') 'Codex candidate prompt omitted the clean technical-failure boundary'
  Assert-True ($trace -match 'direct needs_decision 的 decisionId 返回空字符串' -and $trace -match '固定 wrapper 会从 run/checkpoint 身份') 'Codex candidate prompt omitted deterministic direct-decision ownership'
  Assert-True ($trace -match '缺少 obj/project\.assets\.json' -and $trace -match '先在同一 worktree 对该项目执行一次 dotnet restore' -and $trace -match '不得改用主工作区、其他 worktree 或其 obj/bin 缓存') 'Codex candidate prompt omitted the fresh-worktree restore boundary'
  Assert-True ($trace -match '必须与该提交的四个元数据值逐字一致') 'Codex candidate prompt omitted terminal/commit value synchronization'
  Assert-True ($trace -match "BaseCommit: $base") 'Codex candidate prompt omitted the exact base commit'
  Assert-True ($trace -match 'diff --name-only --no-renames') 'Codex candidate prompt omitted the canonical changed-path command'
  Assert-True ($trace -match '不得使用 git show --name-only') 'Codex candidate prompt did not forbid rename-compressed path output'
  Assert-Equal (Invoke-Git -Root ([string]$run.worktree) -Arguments @('rev-list', '--count', "$base..HEAD")) '1' 'Codex candidate did not create exactly one commit'
  Assert-Equal (Invoke-Git -Root ([string]$run.worktree) -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex candidate worktree is dirty'

  $cleanBlocker = Invoke-TerminalScenario -Mode 'blocked-clean'
  Assert-Equal ([string]$cleanBlocker.Terminal.status) 'blocked' 'Codex clean empty decision was not classified as blocked'
  Assert-Equal ([string]$cleanBlocker.Terminal.detailCode) 'combat_core_switch_contract_incomplete' 'Codex clean blocker lost its business detail code'
  Assert-True ($cleanBlocker.Terminal.PSObject.Properties.Name -cnotcontains 'candidateCommit') 'Codex classified blocker retained a candidateCommit'
  Assert-True ($cleanBlocker.Terminal.PSObject.Properties.Name -cnotcontains 'candidateResult') 'Codex classified blocker retained a decision candidateResult'
  Assert-Equal (Invoke-Git -Root ([string]$cleanBlocker.Run.worktree) -Arguments @('rev-parse', 'HEAD')) $base 'Codex clean blocker changed HEAD'
  Assert-Equal (Invoke-Git -Root ([string]$cleanBlocker.Run.worktree) -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex clean blocker left worktree changes'

  $restoredBlocker = Invoke-TerminalScenario -Mode 'blocked-restored'
  Assert-Equal ([string]$restoredBlocker.Terminal.status) 'blocked' 'Codex restored empty decision was not classified as blocked'
  Assert-Equal ([string]$restoredBlocker.Terminal.detailCode) 'combat_core_switch_contract_incomplete' 'Codex restored blocker lost its business detail code'
  Assert-Equal (Invoke-Git -Root ([string]$restoredBlocker.Run.worktree) -Arguments @('rev-parse', 'HEAD')) $base 'Codex restored blocker changed HEAD'
  Assert-Equal (Invoke-Git -Root ([string]$restoredBlocker.Run.worktree) -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex restored blocker did not return to a clean base'

  $validCheckpoint = Invoke-TerminalScenario -Mode 'checkpoint-valid'
  Assert-Equal ([string]$validCheckpoint.Terminal.status) 'needs_decision' 'Codex valid direct checkpoint no longer pauses for decision'
  Assert-Equal ([string]$validCheckpoint.Terminal.candidateResult.category) 'decision_checkpoint' 'Codex valid checkpoint category changed'
  Assert-Equal (Invoke-Git -Root ([string]$validCheckpoint.Run.worktree) -Arguments @('rev-list', '--count', "$base..HEAD")) '1' 'Codex valid checkpoint is not the unique direct successor'
  Assert-Equal (Invoke-Git -Root ([string]$validCheckpoint.Run.worktree) -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex valid checkpoint worktree is dirty'

  $normalizedCheckpoint = Invoke-TerminalScenario -Mode 'checkpoint-empty-mechanical'
  $normalizedCommit = Invoke-Git -Root ([string]$normalizedCheckpoint.Run.worktree) -Arguments @('rev-parse', 'HEAD')
  $expectedDecisionId = Get-ExpectedDirectDecisionId -TaskId $taskId -Run $normalizedCheckpoint.Run -CheckpointCommit $normalizedCommit
  Assert-Equal ([string]$normalizedCheckpoint.Terminal.status) 'needs_decision' 'Codex did not normalize empty direct-decision mechanical fields'
  Assert-Equal ([string]$normalizedCheckpoint.Terminal.candidateResult.decisionId) $expectedDecisionId 'Codex direct decision ID is not deterministic'
  Assert-Equal ([string]$normalizedCheckpoint.Terminal.candidateResult.plainSummary.situation) 'Choose the fixture outcome.' 'Codex direct decision situation summary mismatch'
  Assert-Equal ([string]$normalizedCheckpoint.Terminal.candidateResult.plainSummary.impact) 'fixture impact' 'Codex direct decision impact summary mismatch'
  Assert-Equal ([string]$normalizedCheckpoint.Terminal.candidateResult.plainSummary.action) '建议选择 A：option A' 'Codex direct decision action summary mismatch'

  foreach ($invalidMode in @('checkpoint-head-changed', 'blocked-dirty', 'checkpoint-fake-sha', 'checkpoint-wrong-path', 'checkpoint-incomplete-abc', 'checkpoint-empty-label', 'checkpoint-invalid-recommended')) {
    $invalidCheckpoint = Invoke-TerminalScenario -Mode $invalidMode
    Assert-Equal ([string]$invalidCheckpoint.Terminal.status) 'failed' "Codex accepted invalid decision evidence: $invalidMode"
  }

  $validResume = Invoke-ResumeContextScenario -Mode 'valid'
  Assert-Equal ([string]$validResume.Terminal.status) 'completed' "Codex rejected a valid normal checkpoint resume context: $($validResume.Terminal | ConvertTo-Json -Compress -Depth 20)"
  Assert-True $validResume.RunnerStarted 'Codex valid normal checkpoint resume did not reach the runner'
  Assert-True ($validResume.Trace -match '"kind":"decision_checkpoint"') 'Codex resume prompt lost the validated normal checkpoint kind'
  foreach ($invalidResumeMode in @('missing', 'unknown')) {
    $invalidResume = Invoke-ResumeContextScenario -Mode $invalidResumeMode
    Assert-Equal ([string]$invalidResume.Terminal.status) 'failed' "Codex accepted invalid resume context kind: $invalidResumeMode"
    Assert-Equal ([string]$invalidResume.Terminal.detailCode) 'codex_resume_context_invalid' "Codex resume kind failure code changed: $invalidResumeMode"
    Assert-True (-not $invalidResume.RunnerStarted) "Codex invalid resume context reached the runner: $invalidResumeMode"
  }

  $pathMismatchClaimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $pathMismatchStateRoot -Owner codex -TaskId $taskId -Route codex_execute -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $base -TaskCardDigest $digest)
  $pathMismatchRun = ($pathMismatchClaimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$pathMismatchRun.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$pathMismatchRun.candidateBranch, [string]$pathMismatchRun.worktree, $base) | Out-Null
  $env:TZG_FAKE_CODEX_RENAME_COMPRESSED = '1'
  $pathMismatchOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Candidate -Route Execution -RepositoryRoot ([string]$pathMismatchRun.worktree) -TaskId $taskId -RunId ([string]$pathMismatchRun.runId) -Model 'test-codex-model' -StateRoot $pathMismatchStateRoot -ResponsibilityTimeoutSeconds 30)
  $env:TZG_FAKE_CODEX_RENAME_COMPRESSED = $null
  $pathMismatch = $pathMismatchOutput[0] | ConvertFrom-Json -Depth 30
  Assert-Equal ([string]$pathMismatch.status) 'failed' 'Codex candidate accepted rename-compressed terminal paths'
  Assert-Equal ([string]$pathMismatch.detailCode) 'codex_candidate_path_mismatch' 'Codex rename-compressed failure code is unstable'
  $pathMismatchRuntime = (@(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action Show -StateRoot $pathMismatchStateRoot)[0] | ConvertFrom-Json -Depth 30).state.runs.codex
  Assert-Equal ([string]$pathMismatchRuntime.state) 'developing' 'Codex path mismatch advanced the runtime state'

  $failureClaimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $failureStateRoot -Owner codex -TaskId $taskId -Route codex_execute -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $base -TaskCardDigest $digest)
  $failureRun = ($failureClaimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$failureRun.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$failureRun.candidateBranch, [string]$failureRun.worktree, $base) | Out-Null
  $env:TZG_FAKE_CODEX_DIRTY_FAILURE = '1'
  $failureOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Candidate -Route Execution -RepositoryRoot ([string]$failureRun.worktree) -TaskId $taskId -RunId ([string]$failureRun.runId) -Model 'test-codex-model' -StateRoot $failureStateRoot -ResponsibilityTimeoutSeconds 30)
  $env:TZG_FAKE_CODEX_DIRTY_FAILURE = $null
  $failure = $failureOutput[0] | ConvertFrom-Json -Depth 30
  Assert-Equal ([string]$failure.status) 'failed' 'Codex dirty technical failure was not rejected'
  Assert-Equal ([string]$failure.detailCode) 'codex_failed_dirty_worktree' 'Codex dirty technical failure kept the misleading terminal-invalid code'

  $qmStateRoot = Join-Path $approvedState "tzg-codex-candidate-test-$testId-qm"
  $qmTaskId = 'QUEUE-MAINTENANCE'
  $queueText = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes((Join-Path $mainRoot '开发管理/当前任务队列.txt'))).TrimStart([char]0xFEFF)
  $queueDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($queueText.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
  $qmClaimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $qmStateRoot -Owner codex -TaskId $qmTaskId -Route queue_maintenance -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $base -TaskCardDigest $queueDigest)
  $qmRun = ($qmClaimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$qmRun.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$qmRun.candidateBranch, [string]$qmRun.worktree, $base) | Out-Null
  $qmOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Candidate -Route QueueMaintenance -RepositoryRoot ([string]$qmRun.worktree) -TaskId $qmTaskId -RunId ([string]$qmRun.runId) -Model 'test-codex-model' -StateRoot $qmStateRoot -ResponsibilityTimeoutSeconds 30)
  Assert-Equal $LASTEXITCODE 0 'Codex QueueMaintenance wrapper process failed'
  Assert-Equal $qmOutput.Count 1 'Codex QueueMaintenance wrapper output count mismatch'
  $qm = $qmOutput[0] | ConvertFrom-Json -Depth 30
  Assert-Equal ([string]$qm.status) 'no_candidate' "Codex QueueMaintenance no_candidate failed: $($qm | ConvertTo-Json -Compress -Depth 20)"
  Assert-Equal ([string]$qm.detailCode) 'no_runnable_candidate' 'Codex QueueMaintenance no_candidate detail code mismatch'
  Assert-True ([string]$qm.sessionId -ne '') 'Codex QueueMaintenance sessionId is missing'
  Assert-Equal (Invoke-Git -Root ([string]$qmRun.worktree) -Arguments @('rev-parse', 'HEAD')) $base 'Codex QueueMaintenance must not create a commit'
  Assert-Equal (Invoke-Git -Root ([string]$qmRun.worktree) -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex QueueMaintenance worktree must stay clean'
  $qmTrace = [IO.File]::ReadAllText($tracePath)
  Assert-True ($qmTrace -match 'Route: QueueMaintenance') 'QueueMaintenance prompt lost its route'
  Assert-True ($qmTrace -match '扫描各分线 backlog 中所有明确标为阻塞的任务') 'QueueMaintenance prompt must scan all backlog blocking items'
  Assert-True ($qmTrace -match '开发管理/任务卡/<ID>\.txt 与 开发管理/任务归档/<ID>\.txt') 'QueueMaintenance prompt must query active cards and completion archives'
  Assert-True ($qmTrace -match '不能证明前置仍未完成') 'QueueMaintenance prompt must not trust stale backlog text directly'
  Assert-True ($qmTrace -match '在活跃任务卡和完成归档中都不存在时保持阻塞') 'QueueMaintenance prompt must require both blocker facts to be absent'
  Assert-True ($qmTrace -match '同一 ID 同时存在活跃任务卡与完成归档时保持阻塞') 'QueueMaintenance prompt must keep conflicting blocker facts blocked'
  Assert-True ($qmTrace.Contains('本轮移除使该卡的 blockedBy 从非空变为空')) 'QueueMaintenance prompt must close the directly affected card when the final named blocker is removed'
  Assert-True ($qmTrace.Contains('不得顺带扫描其他原本就是 blockedBy=[] 的活跃卡')) 'QueueMaintenance prompt must not classify unrelated blocker-free cards'
  Assert-True ($qmTrace.Contains('负责人在两条可确定性形成完整 ready 卡的路线间选择') -and $qmTrace.Contains('其他内容冻结、外部工作面、项目闸门、事实冲突或停止条件保持阻塞')) 'QueueMaintenance prompt must distinguish resumable owner decisions from other blockers'
  Assert-True ($qmTrace.Contains('不得因准确的 stateReason 未变化而机械重写或制造维护提交')) 'QueueMaintenance prompt must not manufacture unchanged maintenance commits'
  Assert-True ($qmTrace -match '完成全部阻塞项核对及上述直接受影响卡的收口后仍没有合法候选，才允许返回 no_candidate') 'QueueMaintenance prompt must only return no_candidate after checking all blockers'
  Assert-True ($qmTrace -match '本轮不执行新增业务任务') 'QueueMaintenance prompt must not execute new business tasks'
  Assert-True ($qmTrace -match 'automation-finalize-commit\.ps1') 'QueueMaintenance prompt omitted the formal finalizer'
  Assert-True ($qmTrace -match '-RequireAutomationMetadata') 'QueueMaintenance prompt omitted required automation metadata'

  $canaryBase = Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')
  $canaryOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Canary -RepositoryRoot $mainRoot -TaskId 'CANARY' -RunId "CANARY-$testId" -Model 'test-codex-model' -StateRoot $stateRoot -ResponsibilityTimeoutSeconds 30)
  $canary = $canaryOutput[0] | ConvertFrom-Json -Depth 30
  Assert-Equal ([string]$canary.status) 'verified' "Codex canary failed: $($canary | ConvertTo-Json -Compress -Depth 20)"
  Assert-True ([string]$canary.candidateCommit -cmatch '^[0-9a-f]{40}$') 'Codex canary commit is invalid'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('rev-list', '--count', "$canaryBase..HEAD")) '1' 'Codex canary did not create one probe commit'
  Assert-Equal (Invoke-Git -Root $mainRoot -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) '' 'Codex canary repository is dirty'
  $canaryTrace = [IO.File]::ReadAllText($tracePath)
  Assert-True ($canaryTrace -match '\[TZG_CODEX_CANARY\]') 'Codex canary prompt marker is missing'
  Assert-True ($canaryTrace -match '-RequireAutomationMetadata') 'Codex canary did not exercise metadata finalization'

  $env:TZG_FAKE_CODEX_MISMATCH = '1'
  $mismatchOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Canary -RepositoryRoot $mainRoot -TaskId 'CANARY' -RunId "CANARY-MISMATCH-$testId" -Model 'test-codex-model' -StateRoot $stateRoot -ResponsibilityTimeoutSeconds 30)
  $mismatch = $mismatchOutput[0] | ConvertFrom-Json -Depth 30
  Assert-Equal ([string]$mismatch.status) 'failed' 'Codex canary accepted terminal/commit metadata mismatch'
  Assert-Equal ([string]$mismatch.detailCode) 'codex_canary_metadata_invalid' 'Codex canary mismatch failure code is unstable'
  $env:TZG_FAKE_CODEX_MISMATCH = $null

  $reviewCardPath = Join-Path $mainRoot "开发管理/任务卡/$taskId.txt"
  $reviewCard = [IO.File]::ReadAllText($reviewCardPath).Replace('"route": "codex_execute"', '"route": "codex_review"')
  [IO.File]::WriteAllText($reviewCardPath, $reviewCard, [Text.UTF8Encoding]::new($false))
  $reviewQueuePath = Join-Path $mainRoot '开发管理/当前任务队列.txt'
  $reviewQueue = [IO.File]::ReadAllText($reviewQueuePath).Replace('| codex_execute |', '| codex_review |')
  [IO.File]::WriteAllText($reviewQueuePath, $reviewQueue, [Text.UTF8Encoding]::new($false))
  Invoke-Git -Root $mainRoot -Arguments @('add', '--', "开发管理/任务卡/$taskId.txt", '开发管理/当前任务队列.txt') | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('commit', '-m', 'test: prepare Codex review prompt fixture') | Out-Null
  $reviewBase = Invoke-Git -Root $mainRoot -Arguments @('rev-parse', 'HEAD')
  $reviewCardText = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($reviewCardPath)).TrimStart([char]0xFEFF)
  $reviewDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($reviewCardText.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
  $reviewClaimOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath -Action ClaimRun -StateRoot $reviewStateRoot -Owner codex -TaskId $taskId -Route codex_review -RepositoryRoot $mainRoot -MainBranch master -BaseCommit $reviewBase -TaskCardDigest $reviewDigest)
  $reviewRun = ($reviewClaimOutput[0] | ConvertFrom-Json).run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$reviewRun.worktree))) | Out-Null
  Invoke-Git -Root $mainRoot -Arguments @('worktree', 'add', '-b', [string]$reviewRun.candidateBranch, [string]$reviewRun.worktree, $reviewBase) | Out-Null
  Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
  $reviewOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath -Action Candidate -Route Review -RepositoryRoot ([string]$reviewRun.worktree) -TaskId $taskId -RunId ([string]$reviewRun.runId) -Model 'test-codex-model' -StateRoot $reviewStateRoot -ResponsibilityTimeoutSeconds 30)
  Assert-Equal $reviewOutput.Count 1 'Codex review wrapper output count mismatch'
  $review = $reviewOutput[0] | ConvertFrom-Json -Depth 30
  Assert-Equal ([string]$review.status) 'completed' "Codex review prompt fixture failed: $($review | ConvertTo-Json -Compress -Depth 20)"
  $reviewTrace = [IO.File]::ReadAllText($tracePath)
  Assert-True ($reviewTrace -match 'Route: Review') 'Codex review prompt lost its route'
  Assert-True ($reviewTrace.Contains('不得修改被审业务语义来消除缺口或制造通过')) 'Codex review prompt omitted the no-self-repair boundary'
  Assert-True ($reviewTrace.Contains('结论通过时，只可更新任务生命周期、索引中的内容状态以及被审文件中明确存在的审核标记／审核记录')) 'Codex review prompt omitted the passing-review mutation boundary'
  Assert-True ($reviewTrace.Contains('结论为部分通过或不通过时，保留被审业务文件，把当前任务设为 blocked 并移出 ready 队列')) 'Codex review prompt omitted the failed-review mutation boundary'
  Assert-True ($reviewTrace.Contains('只有正式结果进入 master 后，后续 QueueMaintenance 才按既有事实源同时更新下游任务卡和 backlog 投影')) 'Codex review prompt omitted the QueueMaintenance dependency boundary'

  Write-Output 'test-invoke-codex-candidate: OK'
} finally {
  $env:PATH = $originalPath; $env:TZG_FAKE_CODEX_TRACE = $originalTrace; $env:TZG_FAKE_CODEX_MISMATCH = $originalMismatch; $env:TZG_FAKE_CODEX_DIRTY_FAILURE = $originalDirtyFailure; $env:TZG_FAKE_CODEX_RENAME_COMPRESSED = $originalRenameCompressed; $env:TZG_FAKE_CODEX_TERMINAL_MODE = $originalTerminalMode
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($tempBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-codex-candidate-test-$testId") { throw "Unsafe Codex candidate test cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedState = [IO.Path]::GetFullPath($stateRoot)
    if (-not $resolvedState.StartsWith($approvedState + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedState) -cne "tzg-codex-candidate-test-$testId") { throw "Unsafe Codex candidate state cleanup: $resolvedState" }
    Remove-Item -LiteralPath $resolvedState -Recurse -Force
  }
  if (Test-Path -LiteralPath $qmStateRoot) {
    $resolvedQmState = [IO.Path]::GetFullPath($qmStateRoot)
    if (-not $resolvedQmState.StartsWith($approvedState + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedQmState) -cne "tzg-codex-candidate-test-$testId-qm") { throw "Unsafe Codex QueueMaintenance state cleanup: $resolvedQmState" }
    Remove-Item -LiteralPath $resolvedQmState -Recurse -Force
  }
  if (Test-Path -LiteralPath $failureStateRoot) {
    $resolvedFailureState = [IO.Path]::GetFullPath($failureStateRoot)
    if (-not $resolvedFailureState.StartsWith($approvedState + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedFailureState) -cne "tzg-codex-candidate-test-$testId-failure") { throw "Unsafe Codex failure state cleanup: $resolvedFailureState" }
    Remove-Item -LiteralPath $resolvedFailureState -Recurse -Force
  }
  if (Test-Path -LiteralPath $pathMismatchStateRoot) {
    $resolvedPathMismatchState = [IO.Path]::GetFullPath($pathMismatchStateRoot)
    if (-not $resolvedPathMismatchState.StartsWith($approvedState + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedPathMismatchState) -cne "tzg-codex-candidate-test-$testId-path-mismatch") { throw "Unsafe Codex path-mismatch state cleanup: $resolvedPathMismatchState" }
    Remove-Item -LiteralPath $resolvedPathMismatchState -Recurse -Force
  }
  if (Test-Path -LiteralPath $reviewStateRoot) {
    $resolvedReviewState = [IO.Path]::GetFullPath($reviewStateRoot)
    if (-not $resolvedReviewState.StartsWith($approvedState + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedReviewState) -cne "tzg-codex-candidate-test-$testId-review") { throw "Unsafe Codex review state cleanup: $resolvedReviewState" }
    Remove-Item -LiteralPath $resolvedReviewState -Recurse -Force
  }
  foreach ($scenarioStateRoot in $scenarioStateRoots) {
    if (Test-Path -LiteralPath $scenarioStateRoot) {
      $resolvedScenarioState = [IO.Path]::GetFullPath($scenarioStateRoot)
      if (-not $resolvedScenarioState.StartsWith($approvedState + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedScenarioState) -cnotlike "tzg-codex-candidate-test-$testId-*") { throw "Unsafe Codex terminal-scenario state cleanup: $resolvedScenarioState" }
      Remove-Item -LiteralPath $resolvedScenarioState -Recurse -Force
    }
  }
}
