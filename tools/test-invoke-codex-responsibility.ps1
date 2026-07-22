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

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$invokerPath = Join-Path $PSScriptRoot 'invoke-codex-responsibility.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$automationStateRoot = Join-Path $env:USERPROFILE '.codex\automation-state'
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-invoke-responsibility-test-$testId"
$fakeBin = Join-Path $testRoot 'bin'
$gitRoot = Join-Path $testRoot 'repo'
$tracePath = Join-Path $testRoot 'stdin.txt'
$stateRoot = Join-Path $automationStateRoot "tzg-invoke-responsibility-tests\$testId"
$bridgeRoot = Join-Path $automationStateRoot "tzg-feishu-decision-bridge\invoke-test-$testId"
$decisionRequestPath = Join-Path $bridgeRoot 'decision-request.json'
$fakeCodexPath = Join-Path $fakeBin 'codex.ps1'
$sessionId = '22222222-3333-4444-8555-666666666666'

function Invoke-LeaseJson {
  param([string]$Action, [hashtable]$Parameters = @{}, [int[]]$AllowedExitCodes = @(0))

  $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $leasePath, '-Action', $Action)
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) {
    if ($entry.Value -is [bool]) {
      if ($entry.Value) { $arguments += "-$($entry.Key)" }
      continue
    }
    $arguments += "-$($entry.Key)"
    $arguments += if ($entry.Value -is [Collections.IEnumerable] -and $entry.Value -isnot [string]) {
      @($entry.Value) -join '|'
    } else {
      [string]$entry.Value
    }
  }
  $output = & pwsh @arguments
  $exitCode = $LASTEXITCODE
  Assert-True -Condition ($exitCode -in $AllowedExitCodes) -Message "$Action failed with exit code $exitCode"
  @($output)[-1] | ConvertFrom-Json -Depth 100
}

function Reset-GitFixture {
  & git -C $gitRoot reset --hard HEAD | Out-Null
  & git -C $gitRoot clean -fd | Out-Null
}

function Acquire-TestLease {
  param([string]$TaskId)

  $result = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = $TaskId
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    LeaseSeconds = 300
  }
  Assert-Equal -Actual $result.status -Expected 'ACQUIRED' -Message 'Test lease was not acquired'
  [string]$result.runId
}

function Invoke-Responsibility {
  param(
    [string]$Case,
    [string]$TaskId,
    [string]$RunId,
    [ValidateSet('Execution', 'Review', 'QueueMaintenance', 'Recovery')]
    [string]$Route = 'Execution',
    [ValidateSet('Start', 'Resume')]
    [string]$Action = 'Start',
    [string]$ResumeSessionId,
    [string]$DecisionId,
    [ValidateSet('A', 'B', 'C')]
    [string]$DecisionOption,
    [string]$DecisionInput
  )

  Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.RedirectStandardInput = -not [string]::IsNullOrEmpty($DecisionInput)
  foreach ($argument in @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $invokerPath,
      '-Action', $Action, '-Route', $Route,
      '-RepositoryRoot', $gitRoot,
      '-TaskId', $TaskId,
      '-RunId', $RunId,
      '-StateRoot', $stateRoot
    )) {
    $startInfo.ArgumentList.Add($argument)
  }
  if ($Action -ceq 'Resume') {
    foreach ($argument in @('-SessionId', $ResumeSessionId)) {
      $startInfo.ArgumentList.Add($argument)
    }
  } else {
    foreach ($argument in @('-Model', 'gpt-5.6-terra')) {
      $startInfo.ArgumentList.Add($argument)
    }
  }
  if (-not [string]::IsNullOrWhiteSpace($DecisionId)) {
    $decisionArguments = if (-not [string]::IsNullOrEmpty($DecisionInput)) {
      @('-DecisionId', $DecisionId, '-ReadDecisionReplyFromStdin')
    } else {
      @('-DecisionId', $DecisionId, '-DecisionOption', $DecisionOption)
    }
    foreach ($argument in $decisionArguments) {
      $startInfo.ArgumentList.Add($argument)
    }
  }
  $startInfo.Environment['Path'] = $fakeBin + [IO.Path]::PathSeparator + [Environment]::GetEnvironmentVariable('Path')
  $startInfo.Environment['RESPONSIBILITY_TEST_CASE'] = $Case
  $startInfo.Environment['RESPONSIBILITY_TEST_SESSION_ID'] = $sessionId
  $startInfo.Environment['RESPONSIBILITY_TEST_STDIN_PATH'] = $tracePath
  $startInfo.Environment['RESPONSIBILITY_TEST_TASK_ID'] = $TaskId
  $startInfo.Environment['RESPONSIBILITY_TEST_RUN_ID'] = $RunId
  $startInfo.Environment['RESPONSIBILITY_TEST_STATE_ROOT'] = $stateRoot
  $startInfo.Environment['RESPONSIBILITY_TEST_LEASE_PATH'] = $leasePath
  $startInfo.Environment['RESPONSIBILITY_TEST_DECISION_PATH'] = $decisionRequestPath

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  Assert-True -Condition $process.Start() -Message 'Failed to start responsibility invoker'
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  if ($startInfo.RedirectStandardInput) {
    $process.StandardInput.Write($DecisionInput)
    $process.StandardInput.Close()
  }
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()

  $lines = @($stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  Assert-Equal -Actual $lines.Count -Expected 1 -Message "$Case must emit exactly one stdout line; stderr=$stderr"
  $json = $lines[0] | ConvertFrom-Json -Depth 100
  [pscustomobject]@{ ExitCode = $exitCode; Json = $json; Stderr = $stderr }
}

function Assert-LeaseReleased {
  $shown = Invoke-LeaseJson -Action Show -Parameters @{ StateRoot = $stateRoot }
  Assert-True -Condition ($null -eq $shown.state.lease) -Message 'Invoker did not release the lease'
  $shown
}

try {
  Assert-True -Condition (Test-Path -LiteralPath $invokerPath -PathType Leaf) -Message "Expected implementation is missing: $invokerPath"

  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  [IO.Directory]::CreateDirectory($gitRoot) | Out-Null
  [IO.Directory]::CreateDirectory($bridgeRoot) | Out-Null
  & git -C $gitRoot init -q
  & git -C $gitRoot config user.name 'Automation Test'
  & git -C $gitRoot config user.email 'automation-test@example.invalid'
  [IO.File]::WriteAllText((Join-Path $gitRoot 'seed.txt'), 'seed', [Text.UTF8Encoding]::new($false))
  & git -C $gitRoot add seed.txt
  & git -C $gitRoot commit -q -m 'test: seed'

  $decisionFixture = [ordered]@{
    pendingDecision = [ordered]@{
      decisionId = 'decision-invoker-test'
      allowedOptions = @('A', 'B', 'C')
      allowCustomReply = $true
      createdAt = '2026-07-22T00:00:00.000Z'
      expiresAt = '2026-07-23T00:00:00.000Z'
      cardNonceHash = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
      providerMessageIdHash = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
      providerChatIdHash = 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'
    }
  }
  [IO.File]::WriteAllText($decisionRequestPath, ($decisionFixture | ConvertTo-Json -Depth 10 -Compress), [Text.UTF8Encoding]::new($false))
  . (Join-Path $PSScriptRoot 'private-path-acl.ps1')
  Set-PrivatePathAcl -Path $bridgeRoot -Directory
  Set-PrivatePathAcl -Path $decisionRequestPath

  $fakeCodex = @'
$ErrorActionPreference = 'Stop'
$stdinText = @($input) -join "`n"
[IO.File]::WriteAllText($env:RESPONSIBILITY_TEST_STDIN_PATH, $stdinText, [Text.UTF8Encoding]::new($false))
$sessionId = $env:RESPONSIBILITY_TEST_SESSION_ID
[pscustomobject]@{ type = 'thread.started'; thread_id = $sessionId } | ConvertTo-Json -Compress

switch ($env:RESPONSIBILITY_TEST_CASE) {
  'commit-success' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'result.txt'), $env:RESPONSIBILITY_TEST_TASK_ID, [Text.UTF8Encoding]::new($false))
    & git add result.txt
    $message = "test: automation result`n`nAutomation: tzg-hourly-controller`nTask: $($env:RESPONSIBILITY_TEST_TASK_ID)`nState: completed`nResult: fixture completed`nImpact: no downstream impact`nVerify: fixture"
    & git commit -q -m $message
  }
  'child-failed-with-change' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'orphan.txt'), 'preserve me', [Text.UTF8Encoding]::new($false))
    $global:LASTEXITCODE = 9
    exit 9
  }
  'child-failed-removes-baseline-changes' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'seed.txt'), 'seed', [Text.UTF8Encoding]::new($false))
    Remove-Item -LiteralPath (Join-Path (Get-Location) 'existing-untracked.txt') -Force
    $global:LASTEXITCODE = 9
    exit 9
  }
  'decision-waiting' {
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $env:RESPONSIBILITY_TEST_LEASE_PATH `
      -Action SaveRecovery `
      -StateRoot $env:RESPONSIBILITY_TEST_STATE_ROOT `
      -RunId $env:RESPONSIBILITY_TEST_RUN_ID `
      -DecisionId 'decision-invoker-test' `
      -DecisionRequestPath $env:RESPONSIBILITY_TEST_DECISION_PATH `
      -CodexThreadId $sessionId | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
  'no-outcome' { }
  default { throw "Unknown case: $($env:RESPONSIBILITY_TEST_CASE)" }
}
$global:LASTEXITCODE = 0
'@
  [IO.File]::WriteAllText($fakeCodexPath, $fakeCodex, [Text.UTF8Encoding]::new($false))

  $taskId = 'task-invoker-unicode-prompt'
  $runId = Acquire-TestLease -TaskId $taskId
  $completed = Invoke-Responsibility -Case 'commit-success' -TaskId $taskId -RunId $runId
  Assert-Equal -Actual $completed.ExitCode -Expected 0 -Message ("Completed invocation failed: json=$($completed.Json | ConvertTo-Json -Compress -Depth 10) stderr=$($completed.Stderr)")
  Assert-Equal -Actual $completed.Json.status -Expected 'completed' -Message 'Completed status mismatch'
  Assert-Equal -Actual $completed.Json.category -Expected 'success' -Message 'Completed category mismatch'
  Assert-Equal -Actual $completed.Json.sessionId -Expected $sessionId -Message 'Completed session mismatch'
  Assert-True -Condition ([string]$completed.Json.commitSha -match '^[0-9a-f]{40}$') -Message 'Completed invocation did not return a commit SHA'
  $transportedPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($transportedPrompt.Contains($taskId)) -Message 'Task id was not transported through stdin'
  Assert-True -Condition ($transportedPrompt.Contains('模型核验证明')) -Message 'Unicode prompt text was not transported through stdin'
  Assert-True -Condition ($transportedPrompt.Contains("`n")) -Message 'Multiline prompt was not transported through stdin'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  $queueRun = Acquire-TestLease -TaskId 'QUEUE-MAINTENANCE'
  $queueCompleted = Invoke-Responsibility -Case 'commit-success' -TaskId 'QUEUE-MAINTENANCE' -RunId $queueRun -Route 'QueueMaintenance'
  Assert-Equal -Actual $queueCompleted.ExitCode -Expected 0 -Message 'Queue maintenance invocation failed'
  Assert-Equal -Actual $queueCompleted.Json.status -Expected 'completed' -Message 'Queue maintenance status mismatch'
  Assert-Equal -Actual $queueCompleted.Json.category -Expected 'refilled' -Message 'Queue maintenance category mismatch'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  $noOutcomeRun = Acquire-TestLease -TaskId 'task-no-outcome'
  $noOutcome = Invoke-Responsibility -Case 'no-outcome' -TaskId 'task-no-outcome' -RunId $noOutcomeRun
  Assert-True -Condition ($noOutcome.ExitCode -ne 0) -Message 'No-outcome invocation unexpectedly succeeded'
  Assert-Equal -Actual $noOutcome.Json.status -Expected 'failed' -Message 'No-outcome status mismatch'
  Assert-True -Condition ($null -eq $noOutcome.Json.commitSha) -Message 'No-outcome invocation invented a commit SHA'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  $interruptedRun = Acquire-TestLease -TaskId 'task-interrupted'
  $interrupted = Invoke-Responsibility -Case 'child-failed-with-change' -TaskId 'task-interrupted' -RunId $interruptedRun
  Assert-True -Condition ($interrupted.ExitCode -ne 0) -Message 'Interrupted invocation unexpectedly succeeded'
  Assert-Equal -Actual $interrupted.Json.status -Expected 'interrupted' -Message 'Interrupted status mismatch'
  Assert-True -Condition (Test-Path -LiteralPath (Join-Path $gitRoot 'orphan.txt')) -Message 'Interrupted invocation removed the orphaned change'
  $interruptedState = Assert-LeaseReleased
  Assert-Equal -Actual $interruptedState.state.recovery.trigger -Expected 'interruption' -Message 'Interrupted invocation did not save interruption recovery'
  Assert-Equal -Actual $interruptedState.state.recovery.resumeId -Expected $sessionId -Message 'Interruption recovery lost session id'
  Assert-True -Condition ('orphan.txt' -in @($interruptedState.state.recovery.changedPaths)) -Message 'Interruption recovery lost changed path'

  $recoveryLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-interrupted'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $recoveryLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Interrupted fixture could not reacquire recovery'
  Invoke-LeaseJson -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $recoveryLease.runId } | Out-Null
  Invoke-LeaseJson -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $recoveryLease.runId } | Out-Null

  Reset-GitFixture
  [IO.File]::WriteAllText((Join-Path $gitRoot 'seed.txt'), 'modified before resume', [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $gitRoot 'existing-untracked.txt'), 'existing before resume', [Text.UTF8Encoding]::new($false))
  $removedBaselineRun = Acquire-TestLease -TaskId 'task-removes-baseline'
  $removedBaseline = Invoke-Responsibility -Case 'child-failed-removes-baseline-changes' -TaskId 'task-removes-baseline' -RunId $removedBaselineRun
  Assert-True -Condition ($removedBaseline.ExitCode -ne 0) -Message 'Baseline-removal invocation unexpectedly succeeded'
  Assert-Equal -Actual $removedBaseline.Json.status -Expected 'interrupted' -Message 'Baseline-removal invocation did not preserve interruption recovery'
  $removedBaselineState = Assert-LeaseReleased
  Assert-Equal -Actual $removedBaselineState.state.recovery.trigger -Expected 'interruption' -Message 'Baseline-removal recovery trigger mismatch'
  Assert-True -Condition ('seed.txt' -in @($removedBaselineState.state.recovery.changedPaths)) -Message 'Restored tracked file was not recorded as changed by the resume'
  Assert-True -Condition ('existing-untracked.txt' -in @($removedBaselineState.state.recovery.changedPaths)) -Message 'Deleted untracked file was not recorded as changed by the resume'
  $removedBaselineRecoveryLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-removes-baseline'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Invoke-LeaseJson -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $removedBaselineRecoveryLease.runId } | Out-Null
  Invoke-LeaseJson -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $removedBaselineRecoveryLease.runId } | Out-Null
  Reset-GitFixture
  $decisionRun = Acquire-TestLease -TaskId 'task-decision'
  $waiting = Invoke-Responsibility -Case 'decision-waiting' -TaskId 'task-decision' -RunId $decisionRun
  Assert-Equal -Actual $waiting.ExitCode -Expected 0 -Message 'Decision waiting invocation failed'
  Assert-Equal -Actual $waiting.Json.status -Expected 'waiting_decision' -Message 'Decision waiting status mismatch'
  $waitingState = Assert-LeaseReleased
  Assert-Equal -Actual $waitingState.state.recovery.trigger -Expected 'decision' -Message 'Decision recovery trigger mismatch'

  $decisionResumeLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-decision'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
    DecisionId = 'decision-invoker-test'
  }
  Assert-Equal -Actual $decisionResumeLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Decision recovery could not be reacquired'
  $resumed = Invoke-Responsibility `
    -Case 'commit-success' `
    -TaskId 'task-decision' `
    -RunId $decisionResumeLease.runId `
    -Route 'Recovery' `
    -Action 'Resume' `
    -ResumeSessionId $sessionId `
    -DecisionId 'decision-invoker-test' `
    -DecisionOption 'A'
  Assert-Equal -Actual $resumed.ExitCode -Expected 0 -Message 'Decision resume invocation failed'
  Assert-Equal -Actual $resumed.Json.status -Expected 'completed' -Message 'Decision resume status mismatch'
  $decisionPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($decisionPrompt.StartsWith("[TZG_DECISION_RESUME runId=$($decisionResumeLease.runId)]`nA")) -Message 'Decision option was not transported with the existing resume protocol'
  $resumedState = Assert-LeaseReleased
  Assert-True -Condition ($null -eq $resumedState.state.recovery) -Message 'Completed decision resume did not clear recovery'

  Reset-GitFixture
  $stdinDecisionRun = Acquire-TestLease -TaskId 'task-decision-stdin'
  $stdinDecisionFixture = [ordered]@{
    pendingDecision = [ordered]@{
      decisionId = 'decision-invoker-stdin'
      allowedOptions = @('A', 'B', 'C')
      allowCustomReply = $true
      createdAt = '2026-07-22T00:00:00.000Z'
      expiresAt = '2026-07-23T00:00:00.000Z'
      cardNonceHash = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
      providerMessageIdHash = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
      providerChatIdHash = 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'
    }
  }
  [IO.File]::WriteAllText($decisionRequestPath, ($stdinDecisionFixture | ConvertTo-Json -Depth 10 -Compress), [Text.UTF8Encoding]::new($false))
  Set-PrivatePathAcl -Path $decisionRequestPath
  Invoke-LeaseJson -Action SaveRecovery -Parameters @{
    StateRoot = $stateRoot
    RunId = $stdinDecisionRun
    DecisionId = 'decision-invoker-stdin'
    DecisionRequestPath = $decisionRequestPath
    CodexThreadId = $sessionId
  } | Out-Null
  Invoke-LeaseJson -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $stdinDecisionRun } | Out-Null
  $stdinResumeLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-decision-stdin'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
    DecisionId = 'decision-invoker-stdin'
  }
  $customDecision = '保持原任务边界，不新增兼容分支'
  $stdinResumed = Invoke-Responsibility `
    -Case 'commit-success' `
    -TaskId 'task-decision-stdin' `
    -RunId $stdinResumeLease.runId `
    -Route 'Recovery' `
    -Action 'Resume' `
    -ResumeSessionId $sessionId `
    -DecisionId 'decision-invoker-stdin' `
    -DecisionInput $customDecision
  Assert-Equal -Actual $stdinResumed.ExitCode -Expected 0 -Message 'Stdin decision resume invocation failed'
  $stdinDecisionPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($stdinDecisionPrompt.StartsWith("[TZG_DECISION_RESUME runId=$($stdinResumeLease.runId)]`n$customDecision")) -Message 'Signed bridge decision was not transported through stdin'
  Assert-LeaseReleased | Out-Null

  Write-Output 'test-invoke-codex-responsibility: OK'
} finally {
  foreach ($cleanup in @($stateRoot, $bridgeRoot)) {
    if (Test-Path -LiteralPath $cleanup) {
      $resolved = [IO.Path]::GetFullPath($cleanup)
      $approved = [IO.Path]::GetFullPath($automationStateRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
      if (-not $resolved.StartsWith($approved, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside automation state root: $resolved"
      }
      Remove-Item -LiteralPath $resolved -Recurse -Force
    }
  }
  if (Test-Path -LiteralPath $testRoot) {
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing cleanup outside temp root: $resolvedTest"
    }
    Remove-Item -LiteralPath $resolvedTest -Recurse -Force
  }
}
