#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Assert-Equal {
  param([AllowNull()][object]$Actual, [AllowNull()][object]$Expected, [string]$Message)
  if ($Actual -ne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" }
}

$sourcePath = Join-Path $PSScriptRoot 'get-project-summary-source.ps1'
$testId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryRoot "tzg-project-summary-source-test-$testId"
$repositoryRoot = Join-Path $testRoot 'repo'

function New-FixtureCommit {
  param(
    [string]$FileName,
    [string]$Content,
    [string]$Message,
    [string]$Timestamp,
    [hashtable]$AdditionalFiles = @{},
    [string[]]$RemovedFiles = @()
  )

  [IO.File]::WriteAllText((Join-Path $repositoryRoot $FileName), $Content, [Text.UTF8Encoding]::new($false))
  foreach ($entry in $AdditionalFiles.GetEnumerator()) {
    $path = Join-Path $repositoryRoot ([string]$entry.Key)
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    [IO.File]::WriteAllText($path, [string]$entry.Value, [Text.UTF8Encoding]::new($false))
  }
  foreach ($removedFile in $RemovedFiles) {
    $removedPath = Join-Path $repositoryRoot $removedFile
    if (Test-Path -LiteralPath $removedPath -PathType Leaf) {
      Remove-Item -LiteralPath $removedPath -Force
    }
  }
  $pathsToStage = @($FileName) + @($AdditionalFiles.Keys) + @($RemovedFiles)
  & git -C $repositoryRoot add -f -- @pathsToStage
  if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
  $oldAuthorDate = $env:GIT_AUTHOR_DATE
  $oldCommitterDate = $env:GIT_COMMITTER_DATE
  try {
    $env:GIT_AUTHOR_DATE = $Timestamp
    $env:GIT_COMMITTER_DATE = $Timestamp
    & git -C $repositoryRoot commit -q -m $Message
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed' }
  } finally {
    $env:GIT_AUTHOR_DATE = $oldAuthorDate
    $env:GIT_COMMITTER_DATE = $oldCommitterDate
  }
  (& git -C $repositoryRoot rev-parse HEAD).Trim()
}

function New-TaskCardText {
  param(
    [string]$Task,
    [string]$State,
    [string]$Route = 'external_execute',
    [string]$Owner = 'deepseek'
  )

  @(
    '---TASK-META---',
    ([ordered]@{
      schemaVersion = 1
      id = $Task
      route = $Route
      owner = $Owner
      dispatchState = $State
    } | ConvertTo-Json -Compress),
    '---TASK-BODY---',
    "# $Task"
  ) -join "`n"
}

function New-QueueText {
  param([string[]]$Rows = @())

  (@(
      '# 当前任务队列',
      '',
      '| ID | 路由 | 主责 |',
      '|----|------|------|'
    ) + @($Rows)) -join "`n"
}

function New-AutomationMessage {
  param([string]$Subject, [string]$Task, [string]$State, [string]$Result, [string]$Impact, [string]$Verify)
  @(
    $Subject, '',
    'Automation: tzg-hourly-controller',
    "Task: $Task",
    "State: $State",
    "Result: $Result",
    "Impact: $Impact",
    "Verify: $Verify"
  ) -join "`n"
}

function New-ArtActivityFile {
  param(
    [string]$RelativePath,
    [string]$Content,
    [string]$CreationTime,
    [string]$ModifiedTime
  )

  $path = Join-Path $repositoryRoot $RelativePath
  [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
  [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
  [IO.File]::SetCreationTimeUtc($path, ([DateTimeOffset]::Parse($CreationTime)).UtcDateTime)
  [IO.File]::SetLastWriteTimeUtc($path, ([DateTimeOffset]::Parse($ModifiedTime)).UtcDateTime)
  $path
}

try {
  Assert-True -Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) -Message "Expected implementation is missing: $sourcePath"
  [IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null
  & git -C $repositoryRoot init -q -b master
  & git -C $repositoryRoot config user.name 'Project Summary Test'
  & git -C $repositoryRoot config user.email 'project-summary-test@example.invalid'

  New-FixtureCommit -FileName 'baseline.txt' -Content 'baseline' -Message 'test: establish tracked source art' -Timestamp '2026-07-20T08:00:00+08:00' -AdditionalFiles @{
    'assets/source/characters/tracked-source.blend' = 'tracked source v1'
  } | Out-Null
  New-FixtureCommit -FileName '.gitignore' -Content "assets/source/`n" -Message 'test: ignore new source art' -Timestamp '2026-07-20T09:00:00+08:00' | Out-Null
  New-FixtureCommit -FileName 'outside.txt' -Content 'outside' -Message (New-AutomationMessage -Subject 'test: outside' -Task 'TASK-OUTSIDE' -State 'completed' -Result 'outside' -Impact 'none' -Verify 'fixture') -Timestamp '2026-07-20T10:00:00+08:00' | Out-Null
  $sinceBoundary = New-FixtureCommit -FileName 'since-boundary.txt' -Content 'since boundary' -Message 'test: since boundary' -Timestamp '2026-07-22T00:00:00+08:00'
  $taskOneFirst = New-FixtureCommit -FileName 'task-one-a.txt' -Content 'a' -Message (New-AutomationMessage -Subject 'feat: task one a' -Task 'TASK-ONE' -State 'completed' -Result 'blocked: first result' -Impact 'first impact' -Verify 'test a') -Timestamp '2026-07-22T01:00:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-ONE.txt' = New-TaskCardText -Task 'TASK-ONE' -State 'blocked'
  }
  $taskOneSecond = New-FixtureCommit -FileName 'task-one-b.txt' -Content 'b' -Message (New-AutomationMessage -Subject 'fix: task one b' -Task 'TASK-ONE' -State 'completed' -Result 'pending_decision: second result' -Impact 'second impact' -Verify 'test b') -Timestamp '2026-07-22T02:00:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-ONE.txt' = New-TaskCardText -Task 'TASK-ONE' -State 'pending_decision'
  }
  $completed = New-FixtureCommit -FileName 'completed.txt' -Content 'completed' -Message (New-AutomationMessage -Subject 'feat: completed' -Task 'TASK-COMPLETE' -State 'completed' -Result 'completed result' -Impact 'completed impact' -Verify 'completed test') -Timestamp '2026-07-22T02:30:00+08:00' -AdditionalFiles @{
    '开发管理/任务归档/TASK-COMPLETE.txt' = New-TaskCardText -Task 'TASK-COMPLETE' -State 'completed'
  }
  $external = New-FixtureCommit -FileName 'external.txt' -Content 'external' -Message (New-AutomationMessage -Subject 'feat: external' -Task 'TASK-EXTERNAL' -State 'pending_review' -Result 'external result' -Impact 'pending review impact' -Verify 'external test') -Timestamp '2026-07-22T03:00:00+08:00'
  New-FixtureCommit -FileName 'handoff.txt' -Content $external -Message "chore: handoff`n`nBusiness-Commit: $external" -Timestamp '2026-07-22T03:10:00+08:00' | Out-Null
  $queue = New-FixtureCommit -FileName 'queue.txt' -Content 'queue' -Message (New-AutomationMessage -Subject 'chore: refill queue' -Task 'QUEUE-MAINTENANCE' -State 'completed' -Result 'queue result' -Impact 'queue impact' -Verify 'queue test') -Timestamp '2026-07-22T04:00:00+08:00'
  $readyExternal = New-FixtureCommit -FileName 'ready-external.txt' -Content 'ready external' -Message (New-AutomationMessage -Subject 'chore: ready external' -Task 'TASK-READY-EXTERNAL' -State 'completed' -Result 'ready result' -Impact 'ready impact' -Verify 'ready test') -Timestamp '2026-07-22T04:10:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-READY-EXTERNAL.txt' = New-TaskCardText -Task 'TASK-READY-EXTERNAL' -State 'ready'
    '开发管理/当前任务队列.txt' = New-QueueText -Rows @('| TASK-READY-EXTERNAL | external_execute | deepseek |')
  }
  $readyBeforeComplete = New-FixtureCommit -FileName 'ready-before-complete.txt' -Content 'ready before complete' -Message (New-AutomationMessage -Subject 'chore: ready before complete' -Task 'TASK-READY-COMPLETE' -State 'completed' -Result 'ready result' -Impact 'ready impact' -Verify 'ready test') -Timestamp '2026-07-22T04:20:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-READY-COMPLETE.txt' = New-TaskCardText -Task 'TASK-READY-COMPLETE' -State 'ready'
    '开发管理/当前任务队列.txt' = New-QueueText -Rows @('| TASK-READY-COMPLETE | external_execute | deepseek |')
  }
  $completedAfterReady = New-FixtureCommit -FileName 'completed-after-ready.txt' -Content 'completed after ready' -Message (New-AutomationMessage -Subject 'review: completed after ready' -Task 'TASK-READY-COMPLETE' -State 'completed' -Result 'completed result' -Impact 'completed impact' -Verify 'completed test') -Timestamp '2026-07-22T04:30:00+08:00' -AdditionalFiles @{
    '开发管理/任务归档/TASK-READY-COMPLETE.txt' = New-TaskCardText -Task 'TASK-READY-COMPLETE' -State 'completed' -Route 'codex_review' -Owner 'codex'
    '开发管理/当前任务队列.txt' = New-QueueText
  } -RemovedFiles @('开发管理/任务卡/TASK-READY-COMPLETE.txt')
  $readyReview = New-FixtureCommit -FileName 'ready-review.txt' -Content 'ready review' -Message (New-AutomationMessage -Subject 'chore: ready review' -Task 'TASK-READY-REVIEW' -State 'completed' -Result 'ready review result' -Impact 'ready review impact' -Verify 'ready review test') -Timestamp '2026-07-22T04:40:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-READY-REVIEW.txt' = New-TaskCardText -Task 'TASK-READY-REVIEW' -State 'ready' -Route 'codex_review' -Owner 'codex'
    '开发管理/当前任务队列.txt' = New-QueueText -Rows @('| TASK-READY-REVIEW | codex_review | codex |')
  }
  $readyMissingRow = New-FixtureCommit -FileName 'ready-missing-row.txt' -Content 'ready missing row' -Message (New-AutomationMessage -Subject 'chore: ready missing row' -Task 'TASK-READY-NO-ROW' -State 'completed' -Result 'ready result' -Impact 'ready impact' -Verify 'ready test') -Timestamp '2026-07-22T04:50:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-READY-NO-ROW.txt' = New-TaskCardText -Task 'TASK-READY-NO-ROW' -State 'ready'
    '开发管理/当前任务队列.txt' = New-QueueText -Rows @('| TASK-OTHER | external_execute | deepseek |')
  }
  $readyMissingQueue = New-FixtureCommit -FileName 'ready-missing-queue.txt' -Content 'ready missing queue' -Message (New-AutomationMessage -Subject 'chore: ready missing queue' -Task 'TASK-READY-NO-QUEUE' -State 'completed' -Result 'ready result' -Impact 'ready impact' -Verify 'ready test') -Timestamp '2026-07-22T05:00:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-READY-NO-QUEUE.txt' = New-TaskCardText -Task 'TASK-READY-NO-QUEUE' -State 'ready'
  } -RemovedFiles @('开发管理/当前任务队列.txt')
  $readyDuplicateRow = New-FixtureCommit -FileName 'ready-duplicate-row.txt' -Content 'ready duplicate row' -Message (New-AutomationMessage -Subject 'chore: ready duplicate row' -Task 'TASK-READY-DUPLICATE' -State 'completed' -Result 'ready result' -Impact 'ready impact' -Verify 'ready test') -Timestamp '2026-07-22T05:10:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-READY-DUPLICATE.txt' = New-TaskCardText -Task 'TASK-READY-DUPLICATE' -State 'ready'
    '开发管理/当前任务队列.txt' = New-QueueText -Rows @('| TASK-READY-DUPLICATE | external_execute | deepseek |', '| TASK-READY-DUPLICATE | external_execute | deepseek |')
  }
  $activeAndArchive = New-FixtureCommit -FileName 'active-and-archive.txt' -Content 'active and archive' -Message (New-AutomationMessage -Subject 'fix: active and archive' -Task 'TASK-ACTIVE-ARCHIVE' -State 'completed' -Result 'conflict result' -Impact 'conflict impact' -Verify 'conflict test') -Timestamp '2026-07-22T05:20:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-ACTIVE-ARCHIVE.txt' = New-TaskCardText -Task 'TASK-ACTIVE-ARCHIVE' -State 'ready'
    '开发管理/任务归档/TASK-ACTIVE-ARCHIVE.txt' = New-TaskCardText -Task 'TASK-ACTIVE-ARCHIVE' -State 'completed'
    '开发管理/当前任务队列.txt' = New-QueueText -Rows @('| TASK-ACTIVE-ARCHIVE | external_execute | deepseek |')
  }
  $mismatchedCard = New-FixtureCommit -FileName 'mismatched-card.txt' -Content 'mismatched card' -Message (New-AutomationMessage -Subject 'fix: mismatched card' -Task 'TASK-MISMATCH' -State 'completed' -Result 'mismatch result' -Impact 'mismatch impact' -Verify 'mismatch test') -Timestamp '2026-07-22T05:30:00+08:00' -AdditionalFiles @{
    '开发管理/任务卡/TASK-MISMATCH.txt' = New-TaskCardText -Task 'TASK-OTHER-ID' -State 'ready'
    '开发管理/当前任务队列.txt' = New-QueueText -Rows @('| TASK-MISMATCH | external_execute | deepseek |')
  }
  $malformedMessage = "fix: malformed`n`nAutomation: tzg-hourly-controller`nTask: TASK-BAD`nState: completed`nResult: bad`nImpact: bad"
  $malformed = New-FixtureCommit -FileName 'bad.txt' -Content 'bad' -Message $malformedMessage -Timestamp '2026-07-22T05:40:00+08:00'
  $unverifiable = New-FixtureCommit -FileName 'unverifiable.txt' -Content 'unverifiable' -Message (New-AutomationMessage -Subject 'fix: unverifiable' -Task 'TASK-NO-FACT' -State 'completed' -Result 'claims completed' -Impact 'unknown' -Verify 'fixture') -Timestamp '2026-07-22T05:50:00+08:00'
  $human = New-FixtureCommit -FileName 'human.txt' -Content 'human' -Message 'docs: human commit' -Timestamp '2026-07-22T06:00:00+08:00' -AdditionalFiles @{
    'assets/source/characters/tracked-source.blend' = 'tracked source v2'
  }
  $untilBoundary = New-FixtureCommit -FileName 'until-boundary.txt' -Content 'until boundary' -Message 'test: until boundary' -Timestamp '2026-07-23T00:00:00+08:00'

  $trackedArtPath = Join-Path $repositoryRoot 'assets/source/characters/tracked-source.blend'
  [IO.File]::SetCreationTimeUtc($trackedArtPath, ([DateTimeOffset]::Parse('2026-07-22T06:10:00Z')).UtcDateTime)
  [IO.File]::SetLastWriteTimeUtc($trackedArtPath, ([DateTimeOffset]::Parse('2026-07-22T06:20:00Z')).UtcDateTime)
  New-ArtActivityFile -RelativePath 'assets/source/characters/new/untracked-model.fbx' -Content 'untracked model' -CreationTime '2026-07-22T07:00:00Z' -ModifiedTime '2026-07-22T07:10:00Z' | Out-Null
  New-ArtActivityFile -RelativePath 'assets/source/参考图片/new-reference.png' -Content 'untracked reference' -CreationTime '2026-07-22T08:00:00Z' -ModifiedTime '2026-07-22T07:50:00Z' | Out-Null
  New-ArtActivityFile -RelativePath 'assets/source/characters/old/outside-window.blend' -Content 'old' -CreationTime '2026-07-21T07:00:00Z' -ModifiedTime '2026-07-21T07:10:00Z' | Out-Null
  foreach ($excludedName in @('.DS_Store', '.DS_Storex', '._scratch', 'ehthumbs.db', 'Thumbs.db', 'desktop.ini', 'job.log', 'job.tmp', 'job.temp', 'job.bak', 'job.swp', '~$draft.blend')) {
    New-ArtActivityFile -RelativePath "assets/source/$excludedName" -Content 'excluded' -CreationTime '2026-07-22T09:00:00Z' -ModifiedTime '2026-07-22T09:10:00Z' | Out-Null
  }
  New-ArtActivityFile -RelativePath 'assets/source/.Spotlight-V100/cache.bin' -Content 'excluded directory' -CreationTime '2026-07-22T09:00:00Z' -ModifiedTime '2026-07-22T09:10:00Z' | Out-Null
  New-ArtActivityFile -RelativePath 'assets/source/.Trashes/trash.bin' -Content 'excluded directory' -CreationTime '2026-07-22T09:00:00Z' -ModifiedTime '2026-07-22T09:10:00Z' | Out-Null

  $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $sourcePath `
    -RepositoryRoot $repositoryRoot `
    -Since '2026-07-22T00:00:00+08:00' `
    -Until '2026-07-23T00:00:00+08:00'
  Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message 'Project summary source failed'
  $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  Assert-Equal -Actual $lines.Count -Expected 1 -Message 'Project summary source must emit one JSON line'
  $result = $lines[0] | ConvertFrom-Json -Depth 100

  Assert-Equal -Actual $result.status -Expected 'ok' -Message 'Project summary source status mismatch'
  Assert-Equal -Actual @($result.commits).Count -Expected 19 -Message 'All-commit candidate count mismatch'
  $commitShas = @($result.commits.sha)
  Assert-True -Condition ($human -in $commitShas) -Message 'Human commit missing from all-commit candidates'
  Assert-True -Condition ($untilBoundary -in $commitShas) -Message 'Until-boundary commit missing from candidates'
  Assert-True -Condition ($sinceBoundary -notin $commitShas) -Message 'Since-boundary commit leaked into candidates'
  $humanSummary = @($result.commits | Where-Object sha -eq $human)
  Assert-Equal -Actual $humanSummary.Count -Expected 1 -Message 'Human commit summary missing'
  Assert-True -Condition ($null -eq $humanSummary[0].automationMetadata) -Message 'Human commit received Automation metadata'
  Assert-True -Condition ('human.txt' -in @($humanSummary[0].changedPaths)) -Message 'Human commit path summary is incomplete'
  Assert-True -Condition ('assets/source/characters/tracked-source.blend' -in @($humanSummary[0].changedPaths)) -Message 'Tracked source-art commit path is missing'

  Assert-Equal -Actual @($result.sourceArtActivity).Count -Expected 2 -Message 'Source-art activity count mismatch'
  $artPaths = @($result.sourceArtActivity.path)
  Assert-True -Condition ('assets/source/characters/new/untracked-model.fbx' -in $artPaths) -Message 'Untracked model activity is missing'
  Assert-True -Condition ('assets/source/参考图片/new-reference.png' -in $artPaths) -Message 'Untracked reference activity is missing'
  Assert-True -Condition ('assets/source/characters/tracked-source.blend' -notin $artPaths) -Message 'Tracked source art leaked into local activity'
  Assert-True -Condition (-not ($artPaths | Where-Object { $_ -match 'Desktop|desktop|\.tmp|\.Spotlight|\.Trashes' })) -Message 'Excluded source-art path leaked into activity'

  Assert-Equal -Actual @($result.automationGroups).Count -Expected 7 -Message 'Automation group count mismatch'
  $taskOne = @($result.automationGroups | Where-Object task -eq 'TASK-ONE')
  Assert-Equal -Actual $taskOne.Count -Expected 1 -Message 'TASK-ONE group missing'
  Assert-Equal -Actual @($taskOne[0].commits).Count -Expected 2 -Message 'Same-task commits were not grouped'
  Assert-Equal -Actual $taskOne[0].category -Expected 'pending_decision' -Message 'TASK-ONE category mismatch'
  Assert-Equal -Actual $taskOne[0].commits[0].sha -Expected $taskOneFirst -Message 'First task commit mismatch'
  Assert-Equal -Actual $taskOne[0].commits[1].sha -Expected $taskOneSecond -Message 'Second task commit mismatch'

  $completedGroup = @($result.automationGroups | Where-Object task -eq 'TASK-COMPLETE')
  Assert-Equal -Actual $completedGroup[0].category -Expected 'completed' -Message 'Completed archive category mismatch'
  Assert-Equal -Actual $completedGroup[0].commits[0].sha -Expected $completed -Message 'Completed archive commit mismatch'
  $externalGroup = @($result.automationGroups | Where-Object task -eq 'TASK-EXTERNAL')
  Assert-Equal -Actual $externalGroup[0].category -Expected 'pending_review' -Message 'External category mismatch'
  Assert-Equal -Actual $externalGroup[0].commits[0].sha -Expected $external -Message 'External commit mismatch'
  $queueGroup = @($result.automationGroups | Where-Object task -eq 'QUEUE-MAINTENANCE')
  Assert-Equal -Actual $queueGroup[0].category -Expected 'queue_maintenance' -Message 'Queue category mismatch'
  Assert-Equal -Actual $queueGroup[0].commits[0].sha -Expected $queue -Message 'Queue commit mismatch'
  $readyExternalGroup = @($result.automationGroups | Where-Object task -eq 'TASK-READY-EXTERNAL')
  Assert-Equal -Actual $readyExternalGroup[0].category -Expected 'ready' -Message 'External ready category mismatch'
  Assert-Equal -Actual $readyExternalGroup[0].commits[0].sha -Expected $readyExternal -Message 'External ready commit mismatch'
  $readyCompleteGroup = @($result.automationGroups | Where-Object task -eq 'TASK-READY-COMPLETE')
  Assert-Equal -Actual $readyCompleteGroup[0].category -Expected 'completed' -Message 'Ready-to-completed category mismatch'
  Assert-Equal -Actual @($readyCompleteGroup[0].commits).Count -Expected 2 -Message 'Ready-to-completed commits were not grouped'
  Assert-Equal -Actual $readyCompleteGroup[0].commits[0].sha -Expected $readyBeforeComplete -Message 'Ready-to-completed first commit mismatch'
  Assert-Equal -Actual $readyCompleteGroup[0].commits[1].sha -Expected $completedAfterReady -Message 'Ready-to-completed final commit mismatch'
  $readyReviewGroup = @($result.automationGroups | Where-Object task -eq 'TASK-READY-REVIEW')
  Assert-Equal -Actual $readyReviewGroup[0].category -Expected 'ready' -Message 'Review ready category mismatch'
  Assert-Equal -Actual $readyReviewGroup[0].commits[0].sha -Expected $readyReview -Message 'Review ready commit mismatch'

  Assert-Equal -Actual @($result.errors).Count -Expected 7 -Message 'Briefing error count mismatch'
  $malformedError = @($result.errors | Where-Object sha -eq $malformed)
  Assert-Equal -Actual $malformedError[0].reason -Expected 'invalid_metadata' -Message 'Malformed reason mismatch'
  $unverifiableError = @($result.errors | Where-Object sha -eq $unverifiable)
  Assert-Equal -Actual $unverifiableError[0].reason -Expected 'outcome_unverifiable' -Message 'Unverifiable outcome reason mismatch'
  foreach ($expectedError in @($readyMissingRow, $readyMissingQueue, $readyDuplicateRow, $activeAndArchive, $mismatchedCard)) {
    $matchingError = @($result.errors | Where-Object sha -eq $expectedError)
    Assert-Equal -Actual $matchingError[0].reason -Expected 'outcome_unverifiable' -Message "Fail-closed outcome reason mismatch for $expectedError"
  }

  $allCandidateShas = @($result.automationGroups.commits.sha)
  Assert-True -Condition ($external -in $allCandidateShas) -Message 'External business commit missing'
  Assert-True -Condition ($allCandidateShas.Count -eq 9) -Message 'Handoff, human, invalid, fail-closed, or outside-window commit leaked into Automation groups'

  $repeatOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $sourcePath `
      -RepositoryRoot $repositoryRoot `
      -Since '2026-07-22T00:00:00+08:00' `
      -Until '2026-07-23T00:00:00+08:00')
  Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message 'Repeat project summary source run failed'
  Assert-Equal -Actual (@($repeatOutput) -join "`n") -Expected (@($output) -join "`n") -Message 'Project summary source output is not stable'

  $reparseTarget = Join-Path $testRoot 'reparse-target'
  $reparsePath = Join-Path $repositoryRoot 'assets/source/reparse-link'
  [IO.Directory]::CreateDirectory($reparseTarget) | Out-Null
  [IO.File]::WriteAllText((Join-Path $reparseTarget 'outside.bin'), 'outside', [Text.UTF8Encoding]::new($false))
  New-Item -ItemType Junction -Path $reparsePath -Target $reparseTarget | Out-Null
  try {
    $reparseOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $sourcePath `
        -RepositoryRoot $repositoryRoot `
        -Since '2026-07-22T00:00:00+08:00' `
        -Until '2026-07-23T00:00:00+08:00')
    Assert-Equal -Actual $LASTEXITCODE -Expected 1 -Message 'Reparse-point source scan unexpectedly succeeded'
    $reparseResult = (@($reparseOutput) -join "`n") | ConvertFrom-Json -Depth 20
    Assert-Equal -Actual $reparseResult.error -Expected 'art_source_error' -Message 'Reparse-point failure category mismatch'
  } finally {
    if (Test-Path -LiteralPath $reparsePath) { Remove-Item -LiteralPath $reparsePath -Force }
  }

  $notGitRoot = Join-Path $testRoot 'not-git'
  [IO.Directory]::CreateDirectory((Join-Path $notGitRoot 'assets/source')) | Out-Null
  $gitFailureOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $sourcePath `
      -RepositoryRoot $notGitRoot `
      -Since '2026-07-22T00:00:00+08:00' `
      -Until '2026-07-23T00:00:00+08:00')
  Assert-Equal -Actual $LASTEXITCODE -Expected 1 -Message 'Non-Git source unexpectedly succeeded'
  $gitFailureResult = (@($gitFailureOutput) -join "`n") | ConvertFrom-Json -Depth 20
  Assert-Equal -Actual $gitFailureResult.error -Expected 'source_error' -Message 'Git failure category mismatch'

  Write-Output 'test-get-project-summary-source: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $prefix = $temporaryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -ne "tzg-project-summary-source-test-$testId") {
      throw "Refusing unsafe project-summary-test cleanup: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
