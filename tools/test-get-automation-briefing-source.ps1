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

$sourcePath = Join-Path $PSScriptRoot 'get-automation-briefing-source.ps1'
$testId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryRoot "tzg-briefing-source-test-$testId"
$repositoryRoot = Join-Path $testRoot 'repo'

function New-FixtureCommit {
  param(
    [string]$FileName,
    [string]$Content,
    [string]$Message,
    [string]$Timestamp
  )

  [IO.File]::WriteAllText((Join-Path $repositoryRoot $FileName), $Content, [Text.UTF8Encoding]::new($false))
  & git -C $repositoryRoot add -- $FileName
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

try {
  Assert-True -Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) -Message "Expected implementation is missing: $sourcePath"
  [IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null
  & git -C $repositoryRoot init -q
  & git -C $repositoryRoot config user.name 'Briefing Test'
  & git -C $repositoryRoot config user.email 'briefing-test@example.invalid'

  New-FixtureCommit -FileName 'outside.txt' -Content 'outside' -Message (New-AutomationMessage -Subject 'test: outside' -Task 'TASK-OUTSIDE' -State 'completed' -Result 'outside' -Impact 'none' -Verify 'fixture') -Timestamp '2026-07-20T10:00:00+08:00' | Out-Null
  $taskOneFirst = New-FixtureCommit -FileName 'task-one-a.txt' -Content 'a' -Message (New-AutomationMessage -Subject 'feat: task one a' -Task 'TASK-ONE' -State 'completed' -Result 'first result' -Impact 'first impact' -Verify 'test a') -Timestamp '2026-07-22T01:00:00+08:00'
  $taskOneSecond = New-FixtureCommit -FileName 'task-one-b.txt' -Content 'b' -Message (New-AutomationMessage -Subject 'fix: task one b' -Task 'TASK-ONE' -State 'completed' -Result 'second result' -Impact 'second impact' -Verify 'test b') -Timestamp '2026-07-22T02:00:00+08:00'
  $external = New-FixtureCommit -FileName 'external.txt' -Content 'external' -Message (New-AutomationMessage -Subject 'feat: external' -Task 'TASK-EXTERNAL' -State 'pending_review' -Result 'external result' -Impact 'pending review impact' -Verify 'external test') -Timestamp '2026-07-22T03:00:00+08:00'
  New-FixtureCommit -FileName 'handoff.txt' -Content $external -Message "chore: handoff`n`nBusiness-Commit: $external" -Timestamp '2026-07-22T03:10:00+08:00' | Out-Null
  $queue = New-FixtureCommit -FileName 'queue.txt' -Content 'queue' -Message (New-AutomationMessage -Subject 'chore: refill queue' -Task 'QUEUE-MAINTENANCE' -State 'completed' -Result 'queue result' -Impact 'queue impact' -Verify 'queue test') -Timestamp '2026-07-22T04:00:00+08:00'
  $malformedMessage = "fix: malformed`n`nAutomation: tzg-hourly-controller`nTask: TASK-BAD`nState: completed`nResult: bad`nImpact: bad"
  $malformed = New-FixtureCommit -FileName 'bad.txt' -Content 'bad' -Message $malformedMessage -Timestamp '2026-07-22T05:00:00+08:00'
  New-FixtureCommit -FileName 'human.txt' -Content 'human' -Message 'docs: human commit' -Timestamp '2026-07-22T06:00:00+08:00' | Out-Null

  $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $sourcePath `
    -RepositoryRoot $repositoryRoot `
    -Since '2026-07-22T00:00:00+08:00' `
    -Until '2026-07-23T00:00:00+08:00'
  Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message 'Briefing source failed'
  $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  Assert-Equal -Actual $lines.Count -Expected 1 -Message 'Briefing source must emit one JSON line'
  $result = $lines[0] | ConvertFrom-Json -Depth 100

  Assert-Equal -Actual @($result.groups).Count -Expected 3 -Message 'Briefing group count mismatch'
  $taskOne = @($result.groups | Where-Object task -eq 'TASK-ONE')
  Assert-Equal -Actual $taskOne.Count -Expected 1 -Message 'TASK-ONE group missing'
  Assert-Equal -Actual @($taskOne[0].commits).Count -Expected 2 -Message 'Same-task commits were not grouped'
  Assert-Equal -Actual $taskOne[0].category -Expected 'completed' -Message 'TASK-ONE category mismatch'
  Assert-Equal -Actual $taskOne[0].commits[0].sha -Expected $taskOneFirst -Message 'First task commit mismatch'
  Assert-Equal -Actual $taskOne[0].commits[1].sha -Expected $taskOneSecond -Message 'Second task commit mismatch'

  $externalGroup = @($result.groups | Where-Object task -eq 'TASK-EXTERNAL')
  Assert-Equal -Actual $externalGroup[0].category -Expected 'pending_review' -Message 'External category mismatch'
  Assert-Equal -Actual $externalGroup[0].commits[0].sha -Expected $external -Message 'External commit mismatch'
  $queueGroup = @($result.groups | Where-Object task -eq 'QUEUE-MAINTENANCE')
  Assert-Equal -Actual $queueGroup[0].category -Expected 'queue_maintenance' -Message 'Queue category mismatch'
  Assert-Equal -Actual $queueGroup[0].commits[0].sha -Expected $queue -Message 'Queue commit mismatch'

  Assert-Equal -Actual @($result.errors).Count -Expected 1 -Message 'Malformed metadata error count mismatch'
  Assert-Equal -Actual $result.errors[0].sha -Expected $malformed -Message 'Malformed commit was not reported'
  Assert-Equal -Actual $result.errors[0].reason -Expected 'invalid_metadata' -Message 'Malformed reason mismatch'

  $allCandidateShas = @($result.groups.commits.sha)
  Assert-True -Condition ($external -in $allCandidateShas) -Message 'External business commit missing'
  Assert-True -Condition ($allCandidateShas.Count -eq 4) -Message 'Handoff, human, or outside-window commit leaked into candidates'

  Write-Output 'test-get-automation-briefing-source: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $prefix = $temporaryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -ne "tzg-briefing-source-test-$testId") {
      throw "Refusing unsafe briefing-test cleanup: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
