#requires -Version 7.0

[CmdletBinding()]
param([switch]$ExternalPreflightOnly)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositorySource = Split-Path -Parent $PSScriptRoot
$coordinator = Join-Path $PSScriptRoot 'invoke-automation-lane-batch.ps1'
$productionExternalWorker = Join-Path $PSScriptRoot 'invoke-external-lane-worker.ps1'
$gitCommonDirectory = (& git -C $repositorySource rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to locate the main repository for batch test isolation.' }
$mainRepositoryRoot = Split-Path -Parent ([IO.Path]::GetFullPath($gitCommonDirectory))
$workspaceTempBase = Join-Path $mainRepositoryRoot '.worktrees'
$tempRoot = Join-Path $workspaceTempBase "tb-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$privateBase = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\automation-state'
$stateRoot = Join-Path $privateBase "tzg-lane-batch-test-$([Guid]::NewGuid().ToString('N'))"
$failureStateRoot = Join-Path $privateBase "tzg-lane-batch-failure-test-$([Guid]::NewGuid().ToString('N'))"
$workerFailureStateRoot = Join-Path $privateBase "tzg-lane-worker-failure-test-$([Guid]::NewGuid().ToString('N'))"
$externalPreflightStateRoot = Join-Path $privateBase "tzg-external-lane-preflight-test-$([Guid]::NewGuid().ToString('N'))"
$externalPreflightRepository = $null
$externalPreflightWorktree = $null

function Invoke-Git {
  param([string]$Root, [string[]]$Arguments)
  $output = @(& git -C $Root @Arguments 2>&1)
  if ($LASTEXITCODE -ne 0) { throw "git failed: $(@($output) -join ' ')" }
  @($output)
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($true))
}

function Invoke-ProductionExternalPreflightCase {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$BatchId,
    [Parameter(Mandatory = $true)][string]$ExpectedDetailCode
  )

  $resultPath = Join-Path $externalPreflightStateRoot "results\$Name.json"
  $output = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $productionExternalWorker `
      -Action Start `
      -RepositoryRoot $RepositoryRoot `
      -TaskId 'EXTERNAL-PREFLIGHT' `
      -RunId ([Guid]::NewGuid().ToString()) `
      -BatchId $BatchId `
      -LaneId 'deepseek' `
      -ResultPath $resultPath `
      -StateRoot $externalPreflightStateRoot `
      -ResponsibilityTimeoutSeconds 1 2>&1
  )
  if ($LASTEXITCODE -ne 1 -or $output.Count -ne 1) {
    throw "production external preflight case $Name returned an invalid result: $(@($output) -join ' ')"
  }
  $terminal = $output[0] | ConvertFrom-Json -Depth 100
  if (
    [string]$terminal.status -cne 'failed' -or
    [string]$terminal.detailCode -cne $ExpectedDetailCode -or
    $null -ne $terminal.sessionId -or
    $terminal.PSObject.Properties.Name -contains 'candidateCommit'
  ) {
    throw "production external preflight case $Name reached the wrong boundary: $($output[0])"
  }
  if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
    throw "production external preflight case $Name did not persist its terminal"
  }
  $persisted = [Text.UTF8Encoding]::new($false, $true).GetString(
    [IO.File]::ReadAllBytes($resultPath)
  ) | ConvertFrom-Json -Depth 100
  if ([string]$persisted.detailCode -cne $ExpectedDetailCode) {
    throw "production external preflight case $Name persisted the wrong detailCode"
  }
}

function Remove-ProductionExternalPreflightWorktree {
  if (
    -not [string]::IsNullOrWhiteSpace([string]$script:externalPreflightRepository) -and
    -not [string]::IsNullOrWhiteSpace([string]$script:externalPreflightWorktree) -and
    (Test-Path -LiteralPath $script:externalPreflightRepository -PathType Container) -and
    (Test-Path -LiteralPath $script:externalPreflightWorktree -PathType Container)
  ) {
    $output = @(
      & git -C $script:externalPreflightRepository worktree remove $script:externalPreflightWorktree 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
      throw "unable to remove production external preflight worktree: $(@($output) -join ' ')"
    }
  }
  $script:externalPreflightWorktree = $null
}

function Test-ProductionExternalWorkerPreflight {
  if (-not (Test-Path -LiteralPath $productionExternalWorker -PathType Leaf)) {
    throw 'production external worker is missing'
  }
  $script:externalPreflightRepository = Join-Path $tempRoot 'external-preflight'
  [IO.Directory]::CreateDirectory($script:externalPreflightRepository) | Out-Null
  $null = Invoke-Git $script:externalPreflightRepository @('init', '-q')
  $null = Invoke-Git $script:externalPreflightRepository @('config', 'user.email', 'external-preflight@example.invalid')
  $null = Invoke-Git $script:externalPreflightRepository @('config', 'user.name', 'External Preflight Test')
  $null = Invoke-Git $script:externalPreflightRepository @('commit', '--allow-empty', '-q', '-m', 'external preflight base')

  $batchId = [Guid]::NewGuid().ToString()
  $script:externalPreflightWorktree = Join-Path `
    $script:externalPreflightRepository `
    ".worktrees\automation\$batchId\deepseek"
  $null = Invoke-Git $script:externalPreflightRepository @(
    'worktree', 'add', '--detach', $script:externalPreflightWorktree, 'HEAD'
  )
  $nonGitDirectory = Join-Path $externalPreflightStateRoot 'non-git-directory'
  $childDirectory = Join-Path $script:externalPreflightWorktree 'child'
  [IO.Directory]::CreateDirectory($nonGitDirectory) | Out-Null
  [IO.Directory]::CreateDirectory($childDirectory) | Out-Null

  $originalBaseUrl = [string]$env:ANTHROPIC_BASE_URL
  try {
    $env:ANTHROPIC_BASE_URL = 'http://127.0.0.1:1'
    Invoke-ProductionExternalPreflightCase `
      -Name 'linked-worktree' `
      -RepositoryRoot $script:externalPreflightWorktree `
      -BatchId $batchId `
      -ExpectedDetailCode 'external_lane_claim_mismatch'
    Invoke-ProductionExternalPreflightCase `
      -Name 'non-git-directory' `
      -RepositoryRoot $nonGitDirectory `
      -BatchId ([Guid]::NewGuid().ToString()) `
      -ExpectedDetailCode 'external_lane_git_root_unavailable'
    Invoke-ProductionExternalPreflightCase `
      -Name 'worktree-child' `
      -RepositoryRoot $childDirectory `
      -BatchId $batchId `
      -ExpectedDetailCode 'external_lane_repository_mismatch'
    Invoke-ProductionExternalPreflightCase `
      -Name 'missing-path' `
      -RepositoryRoot (Join-Path $tempRoot 'external-preflight-missing') `
      -BatchId ([Guid]::NewGuid().ToString()) `
      -ExpectedDetailCode 'external_lane_repository_path_invalid'
    Invoke-ProductionExternalPreflightCase `
      -Name 'relative-path' `
      -RepositoryRoot 'external-preflight-relative' `
      -BatchId ([Guid]::NewGuid().ToString()) `
      -ExpectedDetailCode 'external_lane_repository_path_invalid'
  } finally {
    if ([string]::IsNullOrEmpty($originalBaseUrl)) {
      Remove-Item Env:ANTHROPIC_BASE_URL -ErrorAction SilentlyContinue
    } else {
      $env:ANTHROPIC_BASE_URL = $originalBaseUrl
    }
  }
  Remove-ProductionExternalPreflightWorktree
}

function New-Card {
  param([string]$Id, [string]$Route, [string]$Owner, [string]$WorkerPath, [switch]$External)
  $coordinatorPaths = @(
    '开发管理/任务列表/自动化任务.txt'
    '开发管理/当前任务队列.txt'
    "开发管理/任务卡/$Id.txt"
    "开发管理/任务归档/$Id.txt"
  )
  if ($External) { $coordinatorPaths += '开发管理/AI合作沟通.txt' }
  $metadata = [ordered]@{
    schemaVersion = 2
    id = $Id
    title = "批次集成 $Id"
    priority = 'P1'
    route = $Route
    owner = $Owner
    domain = 'automation'
    stage = 'implementation'
    dispatchState = 'ready'
    blockedBy = @()
    stateReason = 'batch integration test'
    expectedPaths = @($WorkerPath) + $coordinatorPaths
    workerPaths = @($WorkerPath)
    coordinatorPaths = $coordinatorPaths
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  @(
    '---TASK-META---'
    ($metadata | ConvertTo-Json -Depth 20)
    '---TASK-BODY---'
    "# $Id · 批次集成 $Id"
    '## 来源与当前边界'
    '## 必查范围'
    '## 实施范围'
    '## 禁止项'
    '## 验证'
    '## 完成条件'
    '## 停止条件'
  ) -join "`n"
}

$fakeWorker = @'
#requires -Version 7.0
param(
  [string]$Action,
  [string]$RepositoryRoot,
  [string]$TaskId,
  [string]$RunId,
  [string]$BatchId,
  [string]$LaneId,
  [string]$ResultPath,
  [string]$StateRoot,
  [string]$Model
)
$ErrorActionPreference = 'Stop'
$isA = $TaskId -ceq 'A'
$workerPath = if ($isA) { 'a.txt' } else { 'b.txt' }
Start-Sleep -Milliseconds $(if ($isA) { 650 } else { 250 })
[IO.File]::WriteAllText((Join-Path $RepositoryRoot $workerPath), "$TaskId-candidate`n", [Text.UTF8Encoding]::new($false))
& git -C $RepositoryRoot add -- $workerPath
& git -C $RepositoryRoot commit -q -m "candidate-$TaskId"
$candidate = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
$cardPath = Join-Path $RepositoryRoot "开发管理/任务卡/$TaskId.txt"
$cardText = [IO.File]::ReadAllText($cardPath)
$metaMarker = '---TASK-META---'
$bodyMarker = '---TASK-BODY---'
$bodyIndex = $cardText.IndexOf($bodyMarker, [StringComparison]::Ordinal)
$metaText = $cardText.Substring($metaMarker.Length, $bodyIndex - $metaMarker.Length).Trim()
$metadata = $metaText | ConvertFrom-Json -AsHashtable -Depth 50
$body = $cardText.Substring($bodyIndex)
$queuePath = Join-Path $RepositoryRoot '开发管理/当前任务队列.txt'
$backlogPath = Join-Path $RepositoryRoot '开发管理/任务列表/自动化任务.txt'
$queue = [IO.File]::ReadAllText($queuePath)
$backlog = [IO.File]::ReadAllText($backlogPath)
$changes = [Collections.Generic.List[object]]::new()
if ($isA) {
  $metadata.dispatchState = 'completed'
  $archiveContent = "$metaMarker`n$($metadata | ConvertTo-Json -Depth 50)`n$body"
  $queue = (($queue -split '\r?\n') | Where-Object { $_ -notmatch '^\| A \|' }) -join "`n"
  $backlog = (($backlog -split '\r?\n') | Where-Object { $_ -notmatch '^\| A \|' }) -join "`n"
  $changes.Add([ordered]@{ path='开发管理/任务卡/A.txt'; operation='delete' })
  $changes.Add([ordered]@{ path='开发管理/任务归档/A.txt'; operation='write'; content=$archiveContent })
} else {
  $metadata.route = 'codex_review'
  $metadata.owner = 'codex'
  $updatedCard = "$metaMarker`n$($metadata | ConvertTo-Json -Depth 50)`n$body"
  $queue = $queue.Replace('| B | external_execute | deepseek |', '| B | codex_review | codex |')
  $backlog = $backlog.Replace('| B | P1 | deepseek |', '| B | P1 | codex |')
  $handoff = "# AI合作沟通`n`n## 当前交接队列`n`n- B：DeepSeek V4 Flash 已修改/未审核；已验证=候选测试；未验证=正式复审；残留风险=待 Codex 判断。`n"
  $changes.Add([ordered]@{ path='开发管理/任务卡/B.txt'; operation='write'; content=$updatedCard })
  $changes.Add([ordered]@{ path='开发管理/AI合作沟通.txt'; operation='write'; content=$handoff })
}
$changes.Add([ordered]@{ path='开发管理/当前任务队列.txt'; operation='write'; content=$queue })
$changes.Add([ordered]@{ path='开发管理/任务列表/自动化任务.txt'; operation='write'; content=$backlog })
$terminal = [ordered]@{
  status='completed'
  batchId=$BatchId
  laneId=$LaneId
  taskId=$TaskId
  identity=if ($isA) { 'Codex' } else { 'DeepSeek V4 Flash' }
  sessionId=[Guid]::NewGuid().ToString()
  candidateCommit=$candidate
  changedPaths=@($workerPath)
  validationResults=@([ordered]@{name='fixture';outcome='passed';detail='fake worker passed'})
  goal="测试 $TaskId"
  completed="完成 $TaskId"
  impact='验证并行候选'
  boundary='仅临时仓库'
  verification='批次测试通过'
  next=if ($isA) { '任务归档' } else { '等待 Codex 复审' }
  plainHappened="完成 $TaskId"
  plainImpact='只影响临时测试'
  plainAction='无需处理'
  transition=if ($isA) {
    [ordered]@{route='codex_execute';owner='codex';dispatchState='completed'}
  } else {
    [ordered]@{route='codex_review';owner='codex';dispatchState='ready'}
  }
  coordinatorChanges=@($changes)
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $ResultPath)) | Out-Null
[IO.File]::WriteAllText($ResultPath, ($terminal | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
[Console]::Out.WriteLine(($terminal | ConvertTo-Json -Compress -Depth 100))
'@

$lostWorker = @'
#requires -Version 7.0
param(
  [string]$Action,
  [string]$RepositoryRoot,
  [string]$TaskId,
  [string]$RunId,
  [string]$BatchId,
  [string]$LaneId,
  [string]$ResultPath,
  [string]$StateRoot,
  [string]$Model
)
exit 17
'@

try {
  Test-ProductionExternalWorkerPreflight
  if ($ExternalPreflightOnly) {
    Write-Output 'test-automation-lane-batch external preflight: OK'
    return
  }

  $repository = Join-Path $tempRoot 'r'
  [IO.Directory]::CreateDirectory($repository) | Out-Null
  $null = Invoke-Git $repository @('init', '-q')
  $null = Invoke-Git $repository @('config', 'user.email', 'batch-test@example.invalid')
  $null = Invoke-Git $repository @('config', 'user.name', 'Batch Test')
  Write-Utf8 (Join-Path $repository '.gitignore') ".worktrees/`n"
  Write-Utf8 (Join-Path $repository 'a.txt') "a0`n"
  Write-Utf8 (Join-Path $repository 'b.txt') "b0`n"
  Write-Utf8 `
    (Join-Path $repository 'src/Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_npc-cultivation-production-v1.asset.meta') `
    "production-length fixture`n"
  Write-Utf8 (Join-Path $repository '开发管理/任务卡/A.txt') (New-Card A codex_execute codex a.txt)
  Write-Utf8 (Join-Path $repository '开发管理/任务卡/B.txt') (New-Card B external_execute deepseek b.txt -External)
  Write-Utf8 (Join-Path $repository '开发管理/当前任务队列.txt') @'
# 当前任务队列

| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |
|----|------|------|--------|------|------|------|--------|
| A | codex_execute | codex | P1 | automation | implementation | 批次集成 A | `开发管理/任务卡/A.txt` |
| B | external_execute | deepseek | P1 | automation | implementation | 批次集成 B | `开发管理/任务卡/B.txt` |
'@
  Write-Utf8 (Join-Path $repository '开发管理/任务列表/自动化任务.txt') @'
| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |
|----|--------|------|----------|--------|------|--------|
| A | P1 | codex | 已排队 | — | 批次集成 A | `开发管理/任务卡/A.txt` |
| B | P1 | deepseek | 已排队 | — | 批次集成 B | `开发管理/任务卡/B.txt` |
'@
  Write-Utf8 (Join-Path $repository '开发管理/AI合作沟通.txt') "# AI合作沟通`n`n## 当前交接队列`n"
  [IO.Directory]::CreateDirectory((Join-Path $repository 'tools')) | Out-Null
  foreach ($tool in @(
      'check-task-cards.ps1',
      'automation-finalize-commit.ps1',
      'automation-commit-metadata.ps1',
      'check-pending-whitespace.ps1'
    )) {
    Copy-Item -LiteralPath (Join-Path $repositorySource "tools/$tool") -Destination (Join-Path $repository "tools/$tool")
  }
  Write-Utf8 (Join-Path $repository 'tools/invoke-codex-lane-worker.ps1') $fakeWorker
  Write-Utf8 (Join-Path $repository 'tools/invoke-external-lane-worker.ps1') $fakeWorker
  $null = Invoke-Git $repository @('add', '--', '.')
  $null = Invoke-Git $repository @('commit', '-q', '-m', 'fixture base')

  $productionLengthRelativePath = 'src/Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_npc-cultivation-production-v1.asset.meta'
  $legacyWorktreeFixture = Join-Path $stateRoot "batches\$('0' * 36)\worktrees\deepseek"
  if ((Join-Path $legacyWorktreeFixture $productionLengthRelativePath).Length -lt 260) {
    throw 'production-length path was not checked out at the Windows boundary fixture'
  }

  $failureRepository = Join-Path $tempRoot 'f'
  $cloneOutput = @(& git clone -q $repository $failureRepository 2>&1)
  if ($LASTEXITCODE -ne 0) { throw "failure fixture clone failed: $(@($cloneOutput) -join ' ')" }
  Write-Utf8 (Join-Path $failureRepository '.worktrees') "worktree-root-blocker`n"
  $failureOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $coordinator `
      -Action Start `
      -RepositoryRoot $failureRepository `
      -StateRoot $failureStateRoot `
      -Model test-model `
      -CoordinatorTimeoutSeconds 30 2>&1
  )
  $failureExitCode = $LASTEXITCODE
  if ($failureExitCode -ne 1 -or $failureOutput.Count -ne 1) {
    throw "initialization failure fixture returned an invalid result: $(@($failureOutput) -join ' ')"
  }
  $failureResult = $failureOutput[0] | ConvertFrom-Json -Depth 100
  if (
    [string]$failureResult.status -cne 'failed' -or
    [string]$failureResult.detailCode -cne 'batch_create_lanes_failed' -or
    [string]$failureResult.initializationCleanup -cne 'completed'
  ) {
    throw "initialization failure was not closed safely: $($failureOutput[0])"
  }
  $failureShow = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'hourly-automation-lease.ps1') `
      -Action Show `
      -StateRoot $failureStateRoot
  )[0] | ConvertFrom-Json -Depth 100
  if ($null -ne $failureShow.state.lease) {
    throw 'failed initialization left coordinator lease'
  }
  if ($null -ne $failureShow.state.batch) {
    throw 'failed initialization left batch claim'
  }
  if ([string]$failureShow.state.lastResult.detailCode -cne 'batch_create_lanes_failed') {
    throw 'failed initialization did not record its terminal detail'
  }
  $failureBranches = @(& git -C $failureRepository branch --list 'automation/*')
  if ($LASTEXITCODE -ne 0 -or $failureBranches.Count -ne 0) {
    throw "failed initialization left lane branch: $(@($failureBranches) -join ' ')"
  }
  $failureWorktrees = @(& git -C $failureRepository worktree list --porcelain | Where-Object { $_ -like 'worktree *' })
  if ($LASTEXITCODE -ne 0 -or $failureWorktrees.Count -ne 1) {
    throw "failed initialization left lane worktree: $(@($failureWorktrees) -join ' ')"
  }

  $workerFailureRepository = Join-Path $tempRoot 'w'
  $cloneOutput = @(& git clone -q $repository $workerFailureRepository 2>&1)
  if ($LASTEXITCODE -ne 0) { throw "worker failure fixture clone failed: $(@($cloneOutput) -join ' ')" }
  $null = Invoke-Git $workerFailureRepository @('config', 'user.email', 'batch-test@example.invalid')
  $null = Invoke-Git $workerFailureRepository @('config', 'user.name', 'Batch Test')
  Write-Utf8 (Join-Path $workerFailureRepository 'tools/invoke-codex-lane-worker.ps1') $lostWorker
  $workerFailureOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $coordinator `
      -Action Start `
      -RepositoryRoot $workerFailureRepository `
      -StateRoot $workerFailureStateRoot `
      -Model test-model `
      -CoordinatorTimeoutSeconds 30 2>&1
  )
  if ($LASTEXITCODE -ne 0 -or $workerFailureOutput.Count -ne 1) {
    throw "worker failure batch did not close: $(@($workerFailureOutput) -join ' ')"
  }
  $workerFailureResult = $workerFailureOutput[0] | ConvertFrom-Json -Depth 100
  if (
    [string]$workerFailureResult.status -cne 'completed' -or
    [string]$workerFailureResult.lanes[0].workerStatus -cne 'failed' -or
    [string]$workerFailureResult.lanes[0].integrationState -cne 'failed' -or
    [string]$workerFailureResult.lanes[1].workerStatus -cne 'completed' -or
    [string]$workerFailureResult.lanes[1].integrationState -cne 'integrated'
  ) {
    throw "one worker failure did not preserve independent lane progress: $($workerFailureOutput[0])"
  }
  $workerFailureShow = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'hourly-automation-lease.ps1') `
      -Action Show `
      -StateRoot $workerFailureStateRoot
  )[0] | ConvertFrom-Json -Depth 100
  if ($null -ne $workerFailureShow.state.lease -or $null -ne $workerFailureShow.state.batch) {
    throw 'worker failure batch did not release and clear runtime'
  }
  $workerFailureBranches = @(& git -C $workerFailureRepository branch --list 'automation/*')
  if ($LASTEXITCODE -ne 0 -or $workerFailureBranches.Count -ne 0) {
    throw "worker failure batch left lane branch: $(@($workerFailureBranches) -join ' ')"
  }

  $output = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $coordinator `
      -Action Start `
      -RepositoryRoot $repository `
      -StateRoot $stateRoot `
      -Model test-model `
      -CoordinatorTimeoutSeconds 30 2>&1
  )
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
    throw "batch coordinator failed: $(@($output) -join ' ')"
  }
  $result = $output[0] | ConvertFrom-Json -Depth 100
  if ([string]$result.status -cne 'completed' -or @($result.lanes).Count -ne 2) {
    throw "batch result invalid: $($output[0])"
  }
  if (
    [string]$result.lanes[0].taskId -cne 'A' -or
    [string]$result.lanes[1].taskId -cne 'B' -or
    [string]$result.lanes[0].integrationState -cne 'integrated' -or
    [string]$result.lanes[1].integrationState -cne 'integrated'
  ) {
    throw "batch did not integrate both lanes in queue order: $($output[0])"
  }
  if (
    @($result.lanes[0].canonicalCommits).Count -ne 1 -or
    @($result.lanes[1].canonicalCommits).Count -ne 2 -or
    [string]$result.lanes[0].notificationStatus -cne 'not_configured' -or
    [string]$result.lanes[1].notificationStatus -cne 'not_configured'
  ) {
    throw "batch canonical/notification summary is invalid: $($output[0])"
  }
  $subjects = @(Invoke-Git $repository @('log', '-3', '--format=%s'))
  if ($subjects[0] -cne '交接：B' -or $subjects[1] -cne '自动化：B' -or $subjects[2] -cne '自动化：A') {
    throw "canonical commit order is invalid: $($subjects -join ',')"
  }
  $show = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'hourly-automation-lease.ps1') `
      -Action Show `
      -StateRoot $stateRoot
  )[0] | ConvertFrom-Json -Depth 100
  if ($null -ne $show.state.lease -or $null -ne $show.state.batch) {
    throw 'completed batch did not release and clear runtime'
  }
  Write-Output 'test-automation-lane-batch: OK'
} finally {
  Remove-ProductionExternalPreflightWorktree
  foreach ($path in @(
      $tempRoot,
      $stateRoot,
      $failureStateRoot,
      $workerFailureStateRoot,
      $externalPreflightStateRoot
    )) {
    if (Test-Path -LiteralPath $path) {
      $fullPath = [IO.Path]::GetFullPath($path)
      $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
      $privatePrefix = [IO.Path]::GetFullPath($privateBase).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
      $workspaceTempPrefix = [IO.Path]::GetFullPath($workspaceTempBase).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
      if (
        $fullPath.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($privatePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($workspaceTempPrefix, [StringComparison]::OrdinalIgnoreCase)
      ) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
      }
    }
  }
}
