#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$corePath = Join-Path $PSScriptRoot 'automation-lane-core.ps1'
$integrationPath = Join-Path $PSScriptRoot 'automation-lane-integration.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
. $corePath
. $integrationPath

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Assert-Equal {
  param($Actual, $Expected, [string]$Message)
  if ($Actual -cne $Expected) {
    throw "$Message (expected=$Expected actual=$Actual)"
  }
}

function Invoke-Git {
  param([string]$Root, [string[]]$Arguments)
  $output = @(& git -C $Root @Arguments 2>&1)
  if ($LASTEXITCODE -ne 0) {
    throw "git $($Arguments -join ' ') failed: $(@($output) -join ' ')"
  }
  @($output)
}

function New-TestRepository {
  param([string]$Path)
  [IO.Directory]::CreateDirectory($Path) | Out-Null
  $null = Invoke-Git $Path @('init', '-q')
  $null = Invoke-Git $Path @('config', 'user.email', 'automation-lane-test@example.invalid')
  $null = Invoke-Git $Path @('config', 'user.name', 'Automation Lane Test')
  [IO.File]::WriteAllText((Join-Path $Path 'a.txt'), "a0`n", [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $Path 'b.txt'), "b0`n", [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $Path 'c.txt'), "c0`n", [Text.UTF8Encoding]::new($false))
  [IO.Directory]::CreateDirectory((Join-Path $Path '开发管理/任务卡')) | Out-Null
  [IO.File]::WriteAllText(
    (Join-Path $Path '开发管理/当前任务队列.txt'),
    @"
# 当前任务队列

| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |
|----|------|------|--------|------|------|------|--------|
| A | codex_execute | codex | P1 | automation | implementation | A | `开发管理/任务卡/A.txt` |
| B | external_execute | deepseek | P1 | automation | implementation | B | `开发管理/任务卡/B.txt` |
"@,
    [Text.UTF8Encoding]::new($true)
  )
  foreach ($id in @('A', 'B')) {
    [IO.File]::WriteAllText((Join-Path $Path "开发管理/任务卡/$id.txt"), "$id`n", [Text.UTF8Encoding]::new($true))
  }
  $null = Invoke-Git $Path @('add', '--', '.')
  $null = Invoke-Git $Path @('commit', '-q', '-m', 'base')
  @(Invoke-Git $Path @('rev-parse', 'HEAD'))[0]
}

function New-SelectionCard {
  param(
    [string]$Id,
    [string]$Route,
    [string]$Owner,
    [string[]]$WorkerPaths,
    [string[]]$FactPaths = @(),
    [string[]]$BlockedBy = @()
  )
  [pscustomobject]@{
    metadata = [pscustomobject]@{
      id = $Id
      route = $Route
      owner = $Owner
      dispatchState = 'ready'
      blockedBy = $BlockedBy
    }
    cardHash = ('a' * 64)
    workerPaths = $WorkerPaths
    coordinatorPaths = @("开发管理/任务卡/$Id.txt", '开发管理/当前任务队列.txt')
    factPaths = $FactPaths
  }
}

function New-QueueRow {
  param([int]$Index, [string]$Id, [string]$Route, [string]$Owner)
  [pscustomobject]@{
    queueIndex = $Index
    taskId = $Id
    route = $Route
    owner = $Owner
    rowHash = ('b' * 64)
  }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-automation-lanes-$([Guid]::NewGuid().ToString('N'))"
$privateBase = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\automation-state'
$stateRoot = Join-Path $privateBase "tzg-automation-lanes-test-$([Guid]::NewGuid().ToString('N'))"

try {
  [IO.Directory]::CreateDirectory($testRoot) | Out-Null

  $configuration = Get-TzgAutomationLaneConfiguration
  Assert-Equal $configuration.maxConcurrent 2 'production maxConcurrent changed'
  Assert-Equal @($configuration.lanes).Count 2 'production must enable exactly two lanes'
  Assert-Equal $configuration.lanes[0].laneId 'codex' 'Codex lane order changed'
  Assert-Equal $configuration.lanes[1].laneId 'deepseek' 'DeepSeek lane order changed'

  $cards = [ordered]@{
    A = New-SelectionCard A codex_execute codex @('a.txt')
    B = New-SelectionCard B external_execute deepseek @('b.txt')
    C = New-SelectionCard C codex_execute codex @('c.txt')
  }
  $rows = @(
    (New-QueueRow 0 A codex_execute codex),
    (New-QueueRow 1 B external_execute deepseek),
    (New-QueueRow 2 C codex_execute codex)
  )
  $selected = @(Select-TzgAutomationLaneBatch `
    -QueueRows $rows `
    -Cards $cards `
    -Lanes $configuration.lanes `
    -MaxConcurrent 2)
  Assert-Equal $selected.Count 2 'two safe lanes were not selected'
  Assert-Equal $selected[0].taskId A 'global queue order was not preserved'
  Assert-Equal $selected[1].taskId B 'second safe lane was not selected'
  Assert-True (@($selected | Where-Object taskId -eq 'C').Count -eq 0) 'batch rolled into a second Codex task'

  $cards.B.workerPaths = @('a.txt')
  $conflictSelection = @(Select-TzgAutomationLaneBatch `
    -QueueRows $rows `
    -Cards $cards `
    -Lanes $configuration.lanes `
    -MaxConcurrent 2)
  Assert-Equal $conflictSelection.Count 1 'worker path conflict did not degrade to one lane'
  $cards.B.workerPaths = @('b.txt')
  $cards.B.metadata.blockedBy = @('A')
  $dependencySelection = @(Select-TzgAutomationLaneBatch `
    -QueueRows $rows `
    -Cards $cards `
    -Lanes $configuration.lanes `
    -MaxConcurrent 2)
  Assert-Equal $dependencySelection.Count 1 'dependency conflict did not degrade to one lane'
  $cards.B.metadata.blockedBy = @()
  $cards.B.factPaths = @('a.txt')
  $factSelection = @(Select-TzgAutomationLaneBatch `
    -QueueRows $rows `
    -Cards $cards `
    -Lanes $configuration.lanes `
    -MaxConcurrent 2)
  Assert-Equal $factSelection.Count 1 'fact-source conflict did not degrade to one lane'
  $cards.B.factPaths = @()
  $manualSelection = @(Select-TzgAutomationLaneBatch `
    -QueueRows $rows `
    -Cards $cards `
    -Lanes $configuration.lanes `
    -MaxConcurrent 2 `
    -ManualPaths @('b.txt'))
  Assert-Equal $manualSelection.Count 1 'manual path conflict did not skip the candidate'

  $thirdLane = [pscustomobject]@{
    laneId = 'simulated-third'
    owner = 'simulated'
    identity = 'Simulated Third'
    acceptedRoutes = @('external_execute')
    invoker = 'test-only'
  }
  $thirdCards = [ordered]@{
    A = New-SelectionCard A codex_execute codex @('a.txt')
    B = New-SelectionCard B external_execute deepseek @('b.txt')
    T = New-SelectionCard T external_execute simulated @('c.txt')
  }
  $thirdRows = @(
    (New-QueueRow 0 A codex_execute codex),
    (New-QueueRow 1 B external_execute deepseek),
    (New-QueueRow 2 T external_execute simulated)
  )
  $thirdSelection = @(Select-TzgAutomationLaneBatch `
    -QueueRows $thirdRows `
    -Cards $thirdCards `
    -Lanes @($configuration.lanes + $thirdLane) `
    -MaxConcurrent 3)
  Assert-Equal $thirdSelection.Count 3 'generic selector is hard-coded to two lanes'
  Assert-True (@($configuration.lanes | Where-Object laneId -eq 'simulated-third').Count -eq 0) 'simulated lane leaked into production configuration'

  $projectionBase = "| A | codex_execute | codex |`n| B | external_execute | deepseek |`n"
  $projectionCurrent = "| B | external_execute | deepseek |`n"
  $projectionDesired = "| A | codex_execute | codex |`n| B | codex_review | codex |`n"
  $projectionMerge = Merge-TzgTaskProjectionTable B $projectionCurrent $projectionBase $projectionDesired
  Assert-Equal $projectionMerge.disposition 'merged' 'serialized task projection did not rebase'
  Assert-True (-not $projectionMerge.content.Contains('| A |', [StringComparison]::Ordinal)) 'projection rebase restored an earlier completed row'
  Assert-True ($projectionMerge.content.Contains('| B | codex_review | codex |', [StringComparison]::Ordinal)) 'projection rebase lost the current task transition'
  $projectionConflict = Merge-TzgTaskProjectionTable `
    B `
    "| B | codex_review | manual-owner |`n" `
    $projectionBase `
    $projectionDesired
  Assert-Equal $projectionConflict.disposition 'conflict' 'same-row coordinator conflict was not preserved'

  $overlapDirectory = Join-Path $testRoot 'overlap'
  [IO.Directory]::CreateDirectory($overlapDirectory) | Out-Null
  $workerScript = Join-Path $overlapDirectory 'worker.ps1'
  [IO.File]::WriteAllText(
    $workerScript,
    @'
param([string]$Path,[int]$Milliseconds,[int]$ExitCode)
$start = [DateTimeOffset]::UtcNow
Start-Sleep -Milliseconds $Milliseconds
$end = [DateTimeOffset]::UtcNow
[IO.File]::WriteAllText($Path, "$($start.ToUnixTimeMilliseconds()),$($end.ToUnixTimeMilliseconds())")
exit $ExitCode
'@,
    [Text.UTF8Encoding]::new($false)
  )
  $processOne = Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-File', $workerScript, '-Path', (Join-Path $overlapDirectory 'one.txt'), '-Milliseconds', '700', '-ExitCode', '1'
  )
  $processTwo = Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-File', $workerScript, '-Path', (Join-Path $overlapDirectory 'two.txt'), '-Milliseconds', '350', '-ExitCode', '0'
  )
  $processOne.WaitForExit()
  $processTwo.WaitForExit()
  $oneTimes = ([IO.File]::ReadAllText((Join-Path $overlapDirectory 'one.txt'))).Split(',')
  $twoTimes = ([IO.File]::ReadAllText((Join-Path $overlapDirectory 'two.txt'))).Split(',')
  Assert-True ([long]$oneTimes[0] -lt [long]$twoTimes[1] -and [long]$twoTimes[0] -lt [long]$oneTimes[1]) 'worker intervals did not overlap'
  Assert-True ($processOne.ExitCode -ne 0 -and $processTwo.ExitCode -eq 0) 'one worker failure incorrectly cancelled the other'
  Assert-True ([long]$twoTimes[1] -lt [long]$oneTimes[1]) 'reverse worker completion was not simulated'

  $failedLaneWithoutCandidate = [pscustomobject]@{
    integrationState = 'failed'
    workerTerminal = [pscustomobject]@{ status = 'failed'; detailCode = 'lane_process_lost' }
  }
  Assert-True `
    (Test-TzgLaneCleanupAllowed -Lane $failedLaneWithoutCandidate) `
    'failed lane without a candidate was not safe to clean'
  $failedLaneWithCandidate = [pscustomobject]@{
    integrationState = 'failed'
    workerTerminal = [pscustomobject]@{ status = 'failed'; candidateCommit = ('a' * 40) }
  }
  Assert-True `
    (-not (Test-TzgLaneCleanupAllowed -Lane $failedLaneWithCandidate)) `
    'failed lane candidate evidence was incorrectly safe to clean'

  $repository = Join-Path $testRoot 'repo'
  $baseCommit = New-TestRepository -Path $repository
  $worktreeA = Join-Path $testRoot 'worker-a'
  $worktreeB = Join-Path $testRoot 'worker-b'
  $null = Invoke-Git $repository @('worktree', 'add', '-q', '-b', 'worker-a', $worktreeA, $baseCommit)
  $null = Invoke-Git $repository @('worktree', 'add', '-q', '-b', 'worker-b', $worktreeB, $baseCommit)
  [IO.File]::WriteAllText((Join-Path $worktreeA 'a.txt'), "a1`n", [Text.UTF8Encoding]::new($false))
  $null = Invoke-Git $worktreeA @('add', '--', 'a.txt')
  $null = Invoke-Git $worktreeA @('commit', '-q', '-m', 'candidate-a')
  $candidateA = @(Invoke-Git $worktreeA @('rev-parse', 'HEAD'))[0]
  [IO.File]::WriteAllText((Join-Path $worktreeB 'b.txt'), "b1`n", [Text.UTF8Encoding]::new($false))
  $null = Invoke-Git $worktreeB @('add', '--', 'b.txt')
  $null = Invoke-Git $worktreeB @('commit', '-q', '-m', 'candidate-b')
  $candidateB = @(Invoke-Git $worktreeB @('rev-parse', 'HEAD'))[0]
  $patchA = Join-Path $testRoot 'a.patch'
  $patchB = Join-Path $testRoot 'b.patch'
  Write-TzgCandidatePatch $worktreeA $candidateA @('a.txt') $patchA
  Write-TzgCandidatePatch $worktreeB $candidateB @('b.txt') $patchB
  Invoke-TzgGitApply $repository $patchA -Check
  Invoke-TzgGitApply $repository $patchA
  $null = Invoke-Git $repository @('add', '--', 'a.txt')
  $null = Invoke-Git $repository @('commit', '-q', '-m', 'canonical-a')
  Invoke-TzgGitApply $repository $patchB -Check
  Invoke-TzgGitApply $repository $patchB
  $null = Invoke-Git $repository @('add', '--', 'b.txt')
  $null = Invoke-Git $repository @('commit', '-q', '-m', 'canonical-b')
  $subjects = @(Invoke-Git $repository @('log', '-2', '--format=%s'))
  Assert-Equal $subjects[0] 'canonical-b' 'second canonical commit missing'
  Assert-Equal $subjects[1] 'canonical-a' 'integration did not preserve queue order'

  $snapshot = @(New-TzgIntegrationSnapshot -RepositoryRoot $repository -Paths @('c.txt'))
  [IO.File]::WriteAllText((Join-Path $repository 'c.txt'), "partial`n", [Text.UTF8Encoding]::new($false))
  $null = Invoke-Git $repository @('add', '--', 'c.txt')
  Restore-TzgIntegrationSnapshot -RepositoryRoot $repository -Snapshot $snapshot
  Assert-Equal ([IO.File]::ReadAllText((Join-Path $repository 'c.txt'))) "c0`n" 'failed integration snapshot did not restore file bytes'

  $queueRows = @(Read-TzgLaneQueue -Path (Join-Path $repository '开发管理/当前任务队列.txt'))
  $lane = [pscustomobject][ordered]@{
    laneId = 'codex'
    owner = 'codex'
    identity = 'Codex'
    acceptedRoutes = @('codex_execute')
    invoker = 'test'
    taskClaim = [pscustomobject][ordered]@{
      taskId = 'A'
      route = 'codex_execute'
      owner = 'codex'
      dispatchState = 'ready'
      cardHash = Get-TzgFileSha256 (Join-Path $repository '开发管理/任务卡/A.txt')
      queueRowHash = [string]$queueRows[0].rowHash
    }
    worktree = $worktreeA
    branch = 'worker-a'
    baseCommit = @(Invoke-Git $repository @('rev-parse', 'HEAD'))[0]
    workerPaths = @('a.txt')
    coordinatorPaths = @('开发管理/任务卡/A.txt', '开发管理/当前任务队列.txt')
    factPaths = @()
    processOrSession = [pscustomobject]@{ state='terminal'; processId=$null; sessionId=$null; resultPath=(Join-Path $stateRoot 'a.json'); startedAt=$null; completedAt=$null }
    workerTerminal = $null
    integrationState = 'pending'
    queueIndex = 0
  }
  $batch = [pscustomobject]@{ baseCommit = $lane.baseCommit }
  [IO.File]::WriteAllText((Join-Path $repository 'unrelated.txt'), "u`n")
  $preflight = Get-TzgLaneIntegrationPreflight $repository $batch $lane
  Assert-Equal $preflight.classification 'ready' 'unrelated manual path blocked integration'
  [IO.File]::WriteAllText((Join-Path $repository 'a.txt'), "manual`n")
  $preflight = Get-TzgLaneIntegrationPreflight $repository $batch $lane
  Assert-Equal $preflight.classification 'held_conflict' 'manual worker-path conflict was not held'
  [IO.File]::WriteAllText((Join-Path $repository 'a.txt'), "a1`n")
  [IO.File]::WriteAllText((Join-Path $repository '开发管理/任务卡/A.txt'), "changed`n")
  $preflight = Get-TzgLaneIntegrationPreflight $repository $batch $lane
  Assert-Equal $preflight.classification 'stale_selection' 'manual task-card change was not stale'

  [IO.Directory]::CreateDirectory($stateRoot) | Out-Null
  $runtimeRepository = Join-Path $testRoot 'runtime-repo'
  $runtimeBase = New-TestRepository -Path $runtimeRepository
  $acquireOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action Acquire `
      -StateRoot $stateRoot `
      -TaskId AUTOMATION-BATCH `
      -Owner coordinator `
      -RepositoryRoot $runtimeRepository `
      -LeaseSeconds 1
  )
  Assert-Equal $LASTEXITCODE 0 'coordinator lease acquisition failed'
  $acquired = $acquireOutput[0] | ConvertFrom-Json -Depth 100
  $runtimeBatchId = [Guid]::NewGuid().ToString()
  $baselinePath = Join-Path $stateRoot 'baseline.json'
  [IO.File]::WriteAllText($baselinePath, '{}')
  $resultPath = Join-Path $stateRoot 'lane-result.json'
  $runtimeBatch = [pscustomobject][ordered]@{
    schemaVersion = 1
    batchId = $runtimeBatchId
    runId = [string]$acquired.runId
    repositoryRoot = [IO.Path]::GetFullPath($runtimeRepository).TrimEnd('\', '/')
    status = 'open'
    baseCommit = $runtimeBase
    queueHash = ('c' * 64)
    manualBaselinePath = $baselinePath
    startedAt = [DateTimeOffset]::UtcNow.ToString('o')
    maxConcurrent = 2
    lanes = @(
      [pscustomobject][ordered]@{
        laneId = 'codex'
        owner = 'codex'
        identity = 'Codex'
        acceptedRoutes = @('codex_execute', 'codex_review')
        invoker = 'tools/invoke-codex-lane-worker.ps1'
        taskClaim = [pscustomobject][ordered]@{
          taskId = 'A'
          route = 'codex_execute'
          owner = 'codex'
          dispatchState = 'ready'
          cardHash = ('d' * 64)
          queueRowHash = ('e' * 64)
        }
        worktree = (Join-Path $stateRoot 'worktree')
        branch = 'automation/test/codex/A'
        baseCommit = $runtimeBase
        workerPaths = @('a.txt')
        coordinatorPaths = @('开发管理/任务卡/A.txt')
        factPaths = @()
        processOrSession = [pscustomobject][ordered]@{
          state = 'running'
          processId = 123
          sessionId = $null
          resultPath = $resultPath
          startedAt = [DateTimeOffset]::UtcNow.ToString('o')
          completedAt = $null
        }
        workerTerminal = $null
        integrationState = 'pending'
        queueIndex = 0
      }
    )
  }
  $batchPath = Join-Path $stateRoot 'batch.json'
  Write-TzgPrivateJson $runtimeBatch $batchPath
  $saveOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action SaveBatch `
      -StateRoot $stateRoot `
      -RunId $acquired.runId `
      -BatchStatePath $batchPath
  )
  Assert-Equal $LASTEXITCODE 0 'SaveBatch failed'
  Assert-Equal (($saveOutput[0] | ConvertFrom-Json).status) 'BATCH_SAVED' 'batch was not saved'
  $showOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath -Action Show -StateRoot $stateRoot)
  $shown = $showOutput[0] | ConvertFrom-Json -Depth 100
  Assert-Equal $shown.state.schemaVersion 4 'runtime did not migrate to schema 4'
  Assert-Equal $shown.state.batch.lanes[0].taskClaim.taskId A 'task claim is not visible in Show'
  Start-Sleep -Milliseconds 1100
  $blockedAcquireOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action Acquire `
      -StateRoot $stateRoot `
      -TaskId OTHER `
      -Owner codex `
      -RepositoryRoot $runtimeRepository
  )
  Assert-Equal $LASTEXITCODE 0 'batch-only Acquire check failed'
  Assert-Equal (($blockedAcquireOutput[0] | ConvertFrom-Json).status) 'BATCH_ONLY' 'open batch did not block a new task lease'
  $resumeOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action ResumeBatch `
      -StateRoot $stateRoot `
      -BatchId $runtimeBatchId
  )
  Assert-Equal $LASTEXITCODE 0 'ResumeBatch failed'
  Assert-Equal (($resumeOutput[0] | ConvertFrom-Json).status) 'BATCH_RESUMED' 'open batch was not resumed'

  $runtimeBatch.status = 'closed'
  $runtimeBatch.lanes[0].processOrSession.state = 'terminal'
  $runtimeBatch.lanes[0].workerTerminal = [pscustomobject][ordered]@{
    status = 'failed'
    batchId = $runtimeBatchId
    laneId = 'codex'
    taskId = 'A'
    identity = 'Codex'
    sessionId = $null
    detailCode = 'test_failure'
  }
  $runtimeBatch.lanes[0].integrationState = 'failed'
  Write-TzgPrivateJson $runtimeBatch $batchPath
  $saveClosed = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action SaveBatch `
      -StateRoot $stateRoot `
      -RunId $acquired.runId `
      -BatchStatePath $batchPath
  )
  Assert-Equal $LASTEXITCODE 0 'closed batch save failed'
  $runtimeBatch.lanes[0].integrationState = 'held_conflict'
  Write-TzgPrivateJson $runtimeBatch $batchPath
  $saveHeld = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action SaveBatch `
      -StateRoot $stateRoot `
      -RunId $acquired.runId `
      -BatchStatePath $batchPath
  )
  Assert-Equal $LASTEXITCODE 0 'held batch save failed'
  $preserveOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action ClearBatch `
      -StateRoot $stateRoot `
      -RunId $acquired.runId
  )
  Assert-Equal $LASTEXITCODE 2 'held conflict was unexpectedly cleared'
  Assert-Equal (($preserveOutput[0] | ConvertFrom-Json).status) 'BATCH_EVIDENCE_PRESERVED' 'held conflict evidence was not preserved'

  $runtimeBatch.lanes[0].integrationState = 'failed'
  Write-TzgPrivateJson $runtimeBatch $batchPath
  $saveClosed = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action SaveBatch `
      -StateRoot $stateRoot `
      -RunId $acquired.runId `
      -BatchStatePath $batchPath
  )
  Assert-Equal $LASTEXITCODE 0 'stable failed batch resave failed'
  $clearOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action ClearBatch `
      -StateRoot $stateRoot `
      -RunId $acquired.runId
  )
  Assert-Equal $LASTEXITCODE 0 'stable failed batch did not clear'
  Assert-Equal (($clearOutput[0] | ConvertFrom-Json).status) 'BATCH_CLEARED' 'ClearBatch returned wrong status'
  $releaseOutput = @(
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action Release `
      -StateRoot $stateRoot `
      -RunId $acquired.runId
  )
  Assert-Equal $LASTEXITCODE 0 'coordinator lease did not release'

  Write-Output 'test-automation-lanes: OK'
} finally {
  foreach ($path in @($testRoot, $stateRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
      $fullPath = [IO.Path]::GetFullPath($path)
      $allowedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
      $allowedPrivate = [IO.Path]::GetFullPath($privateBase).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
      if (
        $fullPath.StartsWith($allowedTemp, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($allowedPrivate, [StringComparison]::OrdinalIgnoreCase)
      ) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
      }
    }
  }
}
