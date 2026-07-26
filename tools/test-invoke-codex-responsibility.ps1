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
$childPidPath = Join-Path $testRoot 'timeout-child-pid.txt'
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

function Write-TestUtf8 {
  param([string]$Path, [string]$Text)

  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Set-TaskProjectionFixture {
  param(
    [string]$TaskId,
    [ValidateSet('codex_execute', 'codex_review')]
    [string]$Route = 'codex_execute',
    [ValidateSet('ready', 'blocked')]
    [string]$DispatchState = 'ready'
  )

  $managementRoot = Join-Path $gitRoot '开发管理'
  if (Test-Path -LiteralPath $managementRoot) {
    Remove-Item -LiteralPath $managementRoot -Recurse -Force
  }
  $title = "Invoker fixture $TaskId"
  $stateReason = if ($DispatchState -ceq 'blocked') { 'test blocked' } else { $null }
  $metadata = [ordered]@{
    schemaVersion = 1
    id = $TaskId
    title = $title
    priority = 'P2'
    route = $Route
    owner = 'codex'
    domain = 'automation'
    stage = 'verification'
    dispatchState = $DispatchState
    blockedBy = @()
    stateReason = $stateReason
    expectedPaths = @(
      'result.txt'
      '开发管理/当前任务队列.txt'
      '开发管理/任务列表/自动化任务.txt'
      "开发管理/任务卡/$TaskId.txt"
      "开发管理/任务归档/$TaskId.txt"
    )
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $cardText = @(
    '---TASK-META---'
    ($metadata | ConvertTo-Json -Depth 10)
    '---TASK-BODY---'
    "# $TaskId · $title"
    '## 来源与当前边界'
    '## 必查范围'
    '## 实施范围'
    '## 禁止项'
    '## 验证'
    '## 完成条件'
    '## 停止条件'
  ) -join "`n"
  Write-TestUtf8 -Path (Join-Path $managementRoot "任务卡/$TaskId.txt") -Text $cardText

  $queueLines = @(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |'
    '| --- | --- | --- | --- | --- | --- | --- | --- |'
  )
  if ($DispatchState -ceq 'ready') {
    $queueLines += "| $TaskId | $Route | codex | P2 | automation | verification | $title | 开发管理/任务卡/$TaskId.txt |"
  }
  Write-TestUtf8 -Path (Join-Path $managementRoot '当前任务队列.txt') -Text ($queueLines -join "`n")

  $projection = if ($DispatchState -ceq 'ready') { '已排队' } else { '阻塞' }
  $backlogText = @(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |'
    '| --- | --- | --- | --- | --- | --- | --- |'
    "| $TaskId | P2 | codex | $projection | — | $title | 开发管理/任务卡/$TaskId.txt |"
  ) -join "`n"
  Write-TestUtf8 -Path (Join-Path $managementRoot '任务列表/自动化任务.txt') -Text $backlogText

  & git -C $gitRoot add -A -- '开发管理'
  & git -C $gitRoot commit -q -m "test: prepare task projection $TaskId"
  Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message 'Could not commit task projection fixture'
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
    [string]$DecisionInput,
    [int]$ResponsibilityTimeoutSeconds
  )

  Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.RedirectStandardInput = -not [string]::IsNullOrEmpty($DecisionInput)
  if ($startInfo.RedirectStandardInput) {
    $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  }
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
  if ($ResponsibilityTimeoutSeconds -gt 0) {
    $startInfo.ArgumentList.Add('-ResponsibilityTimeoutSeconds')
    $startInfo.ArgumentList.Add([string]$ResponsibilityTimeoutSeconds)
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
  $startInfo.Environment['RESPONSIBILITY_TEST_CHILD_PID_PATH'] = $childPidPath

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

function Assert-NormalRoutePrompt {
  param([string]$Prompt, [string]$Context)

  Assert-True `
    -Condition $Prompt.Contains('开发管理/自动工作流恢复规则.txt') `
    -Message "$Context did not carry the conditional recovery-rule route"
  Assert-True `
    -Condition $Prompt.Contains('创建决定恢复') `
    -Message "$Context did not name the just-in-time decision section"
  Assert-True `
    -Condition $Prompt.Contains('实际到达新的用户决定事件') `
    -Message "$Context did not require an actual decision event"
  Assert-True `
    -Condition $Prompt.Contains('未到达决定事件时不得读取该文件') `
    -Message "$Context did not prohibit eager recovery-rule reads"
  foreach ($detailToken in @(
      'send-decision.mjs',
      'PROVIDER_ACCEPTED',
      'SaveRecovery',
      'consume-reply.mjs',
      '-Action Start -Route Recovery',
      'Resume 原 session'
    )) {
    Assert-True `
      -Condition (-not $Prompt.Contains($detailToken)) `
      -Message "$Context leaked detailed recovery protocol: $detailToken"
  }
}

try {
  Assert-True -Condition (Test-Path -LiteralPath $invokerPath -PathType Leaf) -Message "Expected implementation is missing: $invokerPath"

  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  [IO.Directory]::CreateDirectory($gitRoot) | Out-Null
  [IO.Directory]::CreateDirectory($bridgeRoot) | Out-Null
  & git -C $gitRoot init -q
  & git -C $gitRoot config user.name 'Automation Test'
  & git -C $gitRoot config user.email 'automation-test@example.invalid'
  & git -C $gitRoot config core.autocrlf false
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

function Write-FakeUtf8 {
  param([string]$Path, [string]$Text)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Commit-CompletedResult {
  param([string[]]$Paths)
  & git add -A -- @Paths
  $message = "test: automation result`n`nAutomation: tzg-hourly-controller`nTask: $($env:RESPONSIBILITY_TEST_TASK_ID)`nState: completed`nResult: fixture completed`nImpact: no downstream impact`nVerify: fixture"
  & git commit -q -m $message
}

switch ($env:RESPONSIBILITY_TEST_CASE) {
  'commit-success' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'result.txt'), $env:RESPONSIBILITY_TEST_TASK_ID, [Text.UTF8Encoding]::new($false))
    Commit-CompletedResult -Paths @('result.txt')
  }
  'commit-blocked' {
    $taskId = $env:RESPONSIBILITY_TEST_TASK_ID
    $cardPath = Join-Path (Get-Location) "开发管理/任务卡/$taskId.txt"
    $cardText = [IO.File]::ReadAllText($cardPath)
    $cardText = $cardText.Replace('"dispatchState": "ready"', '"dispatchState": "blocked"').Replace('"stateReason": null', '"stateReason": "test blocked"')
    Write-FakeUtf8 -Path $cardPath -Text $cardText
    Write-FakeUtf8 -Path (Join-Path (Get-Location) '开发管理/当前任务队列.txt') -Text (@(
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |'
      '| --- | --- | --- | --- | --- | --- | --- | --- |'
    ) -join "`n")
    $backlogPath = Join-Path (Get-Location) '开发管理/任务列表/自动化任务.txt'
    $backlogText = [IO.File]::ReadAllText($backlogPath).Replace('| 已排队 |', '| 阻塞 |')
    Write-FakeUtf8 -Path $backlogPath -Text $backlogText
    Write-FakeUtf8 -Path (Join-Path (Get-Location) 'result.txt') -Text $taskId
    Commit-CompletedResult -Paths @('result.txt', '开发管理/任务卡', '开发管理/当前任务队列.txt', '开发管理/任务列表/自动化任务.txt')
  }
  'commit-archived' {
    $taskId = $env:RESPONSIBILITY_TEST_TASK_ID
    $cardPath = Join-Path (Get-Location) "开发管理/任务卡/$taskId.txt"
    $archivePath = Join-Path (Get-Location) "开发管理/任务归档/$taskId.txt"
    $cardText = [IO.File]::ReadAllText($cardPath).Replace('"dispatchState": "ready"', '"dispatchState": "completed"')
    Write-FakeUtf8 -Path $archivePath -Text $cardText
    Remove-Item -LiteralPath $cardPath -Force
    Write-FakeUtf8 -Path (Join-Path (Get-Location) '开发管理/当前任务队列.txt') -Text (@(
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |'
      '| --- | --- | --- | --- | --- | --- | --- | --- |'
    ) -join "`n")
    Write-FakeUtf8 -Path (Join-Path (Get-Location) '开发管理/任务列表/自动化任务.txt') -Text (@(
      '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |'
      '| --- | --- | --- | --- | --- | --- | --- |'
    ) -join "`n")
    Write-FakeUtf8 -Path (Join-Path (Get-Location) 'result.txt') -Text $taskId
    Commit-CompletedResult -Paths @('result.txt', '开发管理')
  }
  'commit-queue-corrected-empty' {
    $cardPath = @(Get-ChildItem -LiteralPath (Join-Path (Get-Location) '开发管理/任务卡') -Filter '*.txt' -File)[0].FullName
    $cardText = [IO.File]::ReadAllText($cardPath).Replace('"stateReason": "test blocked"', '"stateReason": "verified no runnable source"')
    Write-FakeUtf8 -Path $cardPath -Text $cardText
    Write-FakeUtf8 -Path (Join-Path (Get-Location) 'result.txt') -Text 'queue corrected with zero ready tasks'
    Commit-CompletedResult -Paths @('result.txt', '开发管理/任务卡')
  }
  'commit-queue-refilled' {
    $cardPath = @(Get-ChildItem -LiteralPath (Join-Path (Get-Location) '开发管理/任务卡') -Filter '*.txt' -File)[0].FullName
    $cardText = [IO.File]::ReadAllText($cardPath)
    $cardText = $cardText.Replace('"dispatchState": "blocked"', '"dispatchState": "ready"').Replace('"stateReason": "test blocked"', '"stateReason": null')
    Write-FakeUtf8 -Path $cardPath -Text $cardText
    $metadataText = [regex]::Match($cardText, '(?s)---TASK-META---\s*(?<json>.*?)\s*---TASK-BODY---').Groups['json'].Value
    $metadata = $metadataText | ConvertFrom-Json
    Write-FakeUtf8 -Path (Join-Path (Get-Location) '开发管理/当前任务队列.txt') -Text (@(
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |'
      '| --- | --- | --- | --- | --- | --- | --- | --- |'
      "| $($metadata.id) | $($metadata.route) | $($metadata.owner) | $($metadata.priority) | $($metadata.domain) | $($metadata.stage) | $($metadata.title) | 开发管理/任务卡/$($metadata.id).txt |"
    ) -join "`n")
    $backlogPath = Join-Path (Get-Location) '开发管理/任务列表/自动化任务.txt'
    $backlogText = [IO.File]::ReadAllText($backlogPath).Replace('| 阻塞 |', '| 已排队 |')
    Write-FakeUtf8 -Path $backlogPath -Text $backlogText
    Write-FakeUtf8 -Path (Join-Path (Get-Location) 'result.txt') -Text 'queue refilled'
    Commit-CompletedResult -Paths @('result.txt', '开发管理')
  }
  'matching-commit-with-lifecycle-residue' {
    $taskId = $env:RESPONSIBILITY_TEST_TASK_ID
    Write-FakeUtf8 -Path (Join-Path (Get-Location) 'result.txt') -Text $taskId
    Commit-CompletedResult -Paths @('result.txt')

    $cardPath = Join-Path (Get-Location) "开发管理/任务卡/$taskId.txt"
    $cardText = [IO.File]::ReadAllText($cardPath)
    $cardText = $cardText.Replace('"dispatchState": "ready"', '"dispatchState": "blocked"').Replace('"stateReason": null', '"stateReason": "test blocked"')
    Write-FakeUtf8 -Path $cardPath -Text $cardText
    Write-FakeUtf8 -Path (Join-Path (Get-Location) '开发管理/当前任务队列.txt') -Text (@(
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |'
      '| --- | --- | --- | --- | --- | --- | --- | --- |'
    ) -join "`n")
    $backlogPath = Join-Path (Get-Location) '开发管理/任务列表/自动化任务.txt'
    $backlogText = [IO.File]::ReadAllText($backlogPath).Replace('| 已排队 |', '| 阻塞 |')
    Write-FakeUtf8 -Path $backlogPath -Text $backlogText
  }
  'child-failed-with-change' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'orphan.txt'), 'preserve me', [Text.UTF8Encoding]::new($false))
    $global:LASTEXITCODE = 9
    exit 9
  }
  'timeout-with-change' {
    Write-FakeUtf8 -Path (Join-Path (Get-Location) 'orphan-timeout.txt') -Text 'preserve timed-out work'
    Write-FakeUtf8 -Path $env:RESPONSIBILITY_TEST_CHILD_PID_PATH -Text ([string]$PID)
    Start-Sleep -Seconds 10
  }
  'unverified-commit-with-change' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'manual-with-residue.txt'), 'manual commit', [Text.UTF8Encoding]::new($false))
    & git add manual-with-residue.txt
    & git commit -q -m 'test: unrelated manual commit with residue'
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'orphan-after-commit.txt'), 'preserve me', [Text.UTF8Encoding]::new($false))
    $global:LASTEXITCODE = 9
    exit 9
  }
  'unverified-commit-only' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'manual-only.txt'), 'manual commit', [Text.UTF8Encoding]::new($false))
    & git add manual-only.txt
    & git commit -q -m 'test: unrelated manual commit only'
  }
  'unverified-two-commits-only' {
    foreach ($index in 1..2) {
      $path = "manual-only-$index.txt"
      [IO.File]::WriteAllText((Join-Path (Get-Location) $path), "manual commit $index", [Text.UTF8Encoding]::new($false))
      & git add -- $path
      & git commit -q -m "test: unrelated manual commit $index"
    }
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
      -DecisionRequestPath $env:RESPONSIBILITY_TEST_DECISION_PATH | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
  'no-outcome' { }
  default { throw "Unknown case: $($env:RESPONSIBILITY_TEST_CASE)" }
}
$global:LASTEXITCODE = 0
'@
  [IO.File]::WriteAllText($fakeCodexPath, $fakeCodex, [Text.UTF8Encoding]::new($false))

  foreach ($preflightCase in @(
      @{
        Name = 'execution rejects review card'
        TaskId = 'task-preflight-review'
        CardRoute = 'codex_review'
        CardState = 'ready'
        InvokeRoute = 'Execution'
      },
      @{
        Name = 'review rejects execution card'
        TaskId = 'task-preflight-execution'
        CardRoute = 'codex_execute'
        CardState = 'ready'
        InvokeRoute = 'Review'
      },
      @{
        Name = 'execution rejects non-ready card'
        TaskId = 'task-preflight-blocked'
        CardRoute = 'codex_execute'
        CardState = 'blocked'
        InvokeRoute = 'Execution'
      }
    )) {
    Reset-GitFixture
    Set-TaskProjectionFixture `
      -TaskId $preflightCase.TaskId `
      -Route $preflightCase.CardRoute `
      -DispatchState $preflightCase.CardState
    $preflightRun = Acquire-TestLease -TaskId $preflightCase.TaskId
    $preflightResult = Invoke-Responsibility `
      -Case 'no-outcome' `
      -TaskId $preflightCase.TaskId `
      -RunId $preflightRun `
      -Route $preflightCase.InvokeRoute
    Assert-True -Condition ($preflightResult.ExitCode -ne 0) -Message "$($preflightCase.Name) unexpectedly succeeded"
    Assert-Equal -Actual $preflightResult.Json.status -Expected 'failed' -Message "$($preflightCase.Name) status mismatch"
    Assert-Equal -Actual $preflightResult.Json.detailCode -Expected 'route_precondition_failed' -Message "$($preflightCase.Name) detail mismatch"
    Assert-True -Condition (-not (Test-Path -LiteralPath $tracePath)) -Message "$($preflightCase.Name) started the runner"
    Assert-LeaseReleased | Out-Null
  }

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-queue-route-mismatch' -DispatchState 'blocked'
  $queueRouteMismatchRun = Acquire-TestLease -TaskId 'task-queue-route-mismatch'
  $queueRouteMismatch = Invoke-Responsibility `
    -Case 'no-outcome' `
    -TaskId 'task-queue-route-mismatch' `
    -RunId $queueRouteMismatchRun `
    -Route 'QueueMaintenance'
  Assert-Equal -Actual $queueRouteMismatch.ExitCode -Expected 1 -Message 'QueueMaintenance accepted a non-special TaskId'
  Assert-Equal -Actual $queueRouteMismatch.Json.detailCode -Expected 'route_precondition_failed' -Message 'QueueMaintenance TaskId binding detail mismatch'
  Assert-True -Condition (-not (Test-Path -LiteralPath $tracePath)) -Message 'QueueMaintenance TaskId mismatch started the runner'
  Assert-LeaseReleased | Out-Null

  $taskId = 'task-invoker-unicode-prompt'
  Set-TaskProjectionFixture -TaskId $taskId
  $runId = Acquire-TestLease -TaskId $taskId
  $unchangedReady = Invoke-Responsibility -Case 'commit-success' -TaskId $taskId -RunId $runId
  Assert-True -Condition ($unchangedReady.ExitCode -ne 0) -Message 'Result-only completed commit unexpectedly succeeded'
  Assert-Equal -Actual $unchangedReady.Json.status -Expected 'blocked' -Message 'Result-only completed status mismatch'
  Assert-Equal -Actual $unchangedReady.Json.category -Expected 'blocked' -Message 'Result-only completed category mismatch'
  Assert-Equal -Actual $unchangedReady.Json.detailCode -Expected 'unverified_commit_shape' -Message 'Result-only completed detail mismatch'
  Assert-True -Condition ($null -eq $unchangedReady.Json.commitSha) -Message 'Result-only completed invocation returned a commit SHA'
  $transportedPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($transportedPrompt.Contains($taskId)) -Message 'Task id was not transported through stdin'
  Assert-True -Condition ($transportedPrompt.Contains('模型核验证明')) -Message 'Unicode prompt text was not transported through stdin'
  Assert-True -Condition ($transportedPrompt.Contains("`n")) -Message 'Multiline prompt was not transported through stdin'
  Assert-True -Condition ($transportedPrompt.Contains("RepositoryRoot: $gitRoot")) -Message 'Repository root was not transported through stdin'
  Assert-True -Condition ($transportedPrompt.Contains('不得创建或切换 linked worktree、任务分支')) -Message 'Automated responsibility worktree prohibition was not transported through stdin'
  Assert-NormalRoutePrompt -Prompt $transportedPrompt -Context 'Normal execution'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  $blockedTaskId = 'task-blocked-closeout'
  Set-TaskProjectionFixture -TaskId $blockedTaskId
  $blockedRun = Acquire-TestLease -TaskId $blockedTaskId
  $blockedCompleted = Invoke-Responsibility -Case 'commit-blocked' -TaskId $blockedTaskId -RunId $blockedRun
  Assert-Equal -Actual $blockedCompleted.ExitCode -Expected 0 -Message ("Blocked transition invocation failed: json=$($blockedCompleted.Json | ConvertTo-Json -Compress -Depth 10) stderr=$($blockedCompleted.Stderr)")
  Assert-Equal -Actual $blockedCompleted.Json.status -Expected 'completed' -Message 'Blocked transition status mismatch'
  Assert-Equal -Actual $blockedCompleted.Json.category -Expected 'success' -Message 'Blocked transition category mismatch'
  Assert-Equal -Actual $blockedCompleted.Json.taskState -Expected 'blocked' -Message 'Blocked transition taskState mismatch'
  Assert-True -Condition ([string]$blockedCompleted.Json.commitSha -match '^[0-9a-f]{40}$') -Message 'Blocked transition did not return a commit SHA'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  $archivedTaskId = 'task-archived-closeout'
  Set-TaskProjectionFixture -TaskId $archivedTaskId
  $archivedRun = Acquire-TestLease -TaskId $archivedTaskId
  $archivedCompleted = Invoke-Responsibility -Case 'commit-archived' -TaskId $archivedTaskId -RunId $archivedRun
  Assert-Equal -Actual $archivedCompleted.ExitCode -Expected 0 -Message ("Archived transition invocation failed: json=$($archivedCompleted.Json | ConvertTo-Json -Compress -Depth 10) stderr=$($archivedCompleted.Stderr)")
  Assert-Equal -Actual $archivedCompleted.Json.status -Expected 'completed' -Message 'Archived transition status mismatch'
  Assert-Equal -Actual $archivedCompleted.Json.category -Expected 'success' -Message 'Archived transition category mismatch'
  Assert-Equal -Actual $archivedCompleted.Json.taskState -Expected 'completed' -Message 'Archived transition taskState mismatch'
  Assert-True -Condition ([string]$archivedCompleted.Json.commitSha -match '^[0-9a-f]{40}$') -Message 'Archived transition did not return a commit SHA'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-review' -Route 'codex_review'
  $reviewRun = Acquire-TestLease -TaskId 'task-review'
  $reviewCompleted = Invoke-Responsibility -Case 'commit-archived' -TaskId 'task-review' -RunId $reviewRun -Route 'Review'
  Assert-Equal -Actual $reviewCompleted.ExitCode -Expected 0 -Message 'Review invocation failed'
  Assert-Equal -Actual $reviewCompleted.Json.status -Expected 'completed' -Message 'Review status mismatch'
  $reviewPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-NormalRoutePrompt -Prompt $reviewPrompt -Context 'Normal review'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-global-projection' -DispatchState 'blocked'
  $queueEmptyRun = Acquire-TestLease -TaskId 'QUEUE-MAINTENANCE'
  $queueEmpty = Invoke-Responsibility -Case 'no-outcome' -TaskId 'QUEUE-MAINTENANCE' -RunId $queueEmptyRun -Route 'QueueMaintenance'
  Assert-Equal -Actual $queueEmpty.ExitCode -Expected 2 -Message 'Clean empty queue returned wrong exit code'
  Assert-Equal -Actual $queueEmpty.Json.status -Expected 'blocked' -Message 'Clean empty queue status mismatch'
  Assert-Equal -Actual $queueEmpty.Json.category -Expected 'blocked' -Message 'Clean empty queue category mismatch'
  Assert-Equal -Actual $queueEmpty.Json.detailCode -Expected 'no_runnable_candidate' -Message 'Clean empty queue detail mismatch'
  Assert-Equal -Actual $queueEmpty.Json.readyCount -Expected 0 -Message 'Clean empty queue readyCount mismatch'
  $queueEmptyState = Assert-LeaseReleased
  Assert-Equal -Actual $queueEmptyState.state.blocking.fingerprint -Expected 'queue:no_runnable_candidate' -Message 'Clean empty queue fingerprint mismatch'
  Assert-Equal -Actual $queueEmptyState.state.blocking.count -Expected 1 -Message 'Clean empty queue blocker count mismatch'

  $queueRepeatRun = Acquire-TestLease -TaskId 'QUEUE-MAINTENANCE'
  $queueRepeat = Invoke-Responsibility -Case 'no-outcome' -TaskId 'QUEUE-MAINTENANCE' -RunId $queueRepeatRun -Route 'QueueMaintenance'
  Assert-Equal -Actual $queueRepeat.Json.detailCode -Expected 'no_runnable_candidate' -Message 'Repeated empty queue detail mismatch'
  $queueRepeatState = Assert-LeaseReleased
  Assert-Equal -Actual $queueRepeatState.state.blocking.count -Expected 2 -Message 'Repeated empty queue blocker count mismatch'
  Assert-True -Condition ([bool]$queueRepeatState.state.blocking.pauseRequested) -Message 'Repeated empty queue did not request pause'
  $clearedQueueBlocking = Invoke-LeaseJson -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal -Actual $clearedQueueBlocking.status -Expected 'BLOCKING_CLEARED' -Message 'Could not clear queue blocker fixture'

  $queueCorrectedRun = Acquire-TestLease -TaskId 'QUEUE-MAINTENANCE'
  $queueCorrected = Invoke-Responsibility -Case 'commit-queue-corrected-empty' -TaskId 'QUEUE-MAINTENANCE' -RunId $queueCorrectedRun -Route 'QueueMaintenance'
  Assert-Equal -Actual $queueCorrected.ExitCode -Expected 0 -Message 'Queue correction invocation failed'
  Assert-Equal -Actual $queueCorrected.Json.category -Expected 'success' -Message 'Zero-ready queue correction category mismatch'
  Assert-Equal -Actual $queueCorrected.Json.readyCount -Expected 0 -Message 'Zero-ready queue correction readyCount mismatch'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-global-projection' -DispatchState 'blocked'
  $queueRun = Acquire-TestLease -TaskId 'QUEUE-MAINTENANCE'
  $queueCompleted = Invoke-Responsibility -Case 'commit-queue-refilled' -TaskId 'QUEUE-MAINTENANCE' -RunId $queueRun -Route 'QueueMaintenance'
  Assert-Equal -Actual $queueCompleted.ExitCode -Expected 0 -Message 'Queue refill invocation failed'
  Assert-Equal -Actual $queueCompleted.Json.status -Expected 'completed' -Message 'Queue refill status mismatch'
  Assert-Equal -Actual $queueCompleted.Json.category -Expected 'refilled' -Message 'Queue refill category mismatch'
  Assert-Equal -Actual $queueCompleted.Json.readyCount -Expected 1 -Message 'Queue refill readyCount mismatch'
  $queuePrompt = [IO.File]::ReadAllText($tracePath)
  Assert-NormalRoutePrompt -Prompt $queuePrompt -Context 'Queue maintenance'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-no-outcome'
  $noOutcomeRun = Acquire-TestLease -TaskId 'task-no-outcome'
  $noOutcome = Invoke-Responsibility -Case 'no-outcome' -TaskId 'task-no-outcome' -RunId $noOutcomeRun
  Assert-True -Condition ($noOutcome.ExitCode -ne 0) -Message 'No-outcome invocation unexpectedly succeeded'
  Assert-Equal -Actual $noOutcome.Json.status -Expected 'failed' -Message 'No-outcome status mismatch'
  Assert-True -Condition ($null -eq $noOutcome.Json.commitSha) -Message 'No-outcome invocation invented a commit SHA'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-interrupted'
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
  $invalidInterruptionResume = Invoke-Responsibility `
    -Case 'commit-success' `
    -TaskId 'task-interrupted' `
    -RunId $recoveryLease.runId `
    -Route 'Recovery' `
    -Action 'Resume' `
    -ResumeSessionId $sessionId
  Assert-Equal -Actual $invalidInterruptionResume.ExitCode -Expected 2 -Message 'Ready interruption recovery returned wrong exit code'
  Assert-Equal -Actual $invalidInterruptionResume.Json.status -Expected 'blocked' -Message 'Ready interruption recovery status mismatch'
  $invalidInterruptionState = Assert-LeaseReleased
  Assert-True -Condition ($null -ne $invalidInterruptionState.state.recovery) -Message 'Invalid interruption recovery was cleared'

  $validInterruptionLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-interrupted'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $validInterruptionLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Interrupted fixture could not reacquire after invalid closeout'
  $interruptionResumed = Invoke-Responsibility `
    -Case 'commit-blocked' `
    -TaskId 'task-interrupted' `
    -RunId $validInterruptionLease.runId `
    -Route 'Recovery' `
    -Action 'Resume' `
    -ResumeSessionId $sessionId
  Assert-Equal -Actual $interruptionResumed.ExitCode -Expected 0 -Message 'Valid interruption recovery invocation failed'
  Assert-Equal -Actual $interruptionResumed.Json.status -Expected 'completed' -Message 'Valid interruption recovery status mismatch'
  Assert-Equal -Actual $interruptionResumed.Json.taskState -Expected 'blocked' -Message 'Valid interruption recovery taskState mismatch'
  $interruptionPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($interruptionPrompt.Contains('开发管理/自动工作流恢复规则.txt')) -Message 'Interruption recovery did not load recovery rules'
  Assert-True -Condition ($interruptionPrompt.Contains('这是原 CLI session 的续跑，不创建新责任方。')) -Message 'Interruption recovery did not Resume the original session'
  Assert-True -Condition ($interruptionPrompt.Contains('这是中断恢复，恢复原责任方的同一 TaskId')) -Message 'Interruption recovery lost its route wording'
  Assert-LeaseReleased | Out-Null

  Reset-GitFixture
  Remove-Item -LiteralPath $childPidPath -Force -ErrorAction SilentlyContinue
  Set-TaskProjectionFixture -TaskId 'task-timeout'
  $timeoutRun = Acquire-TestLease -TaskId 'task-timeout'
  $timeoutWatch = [Diagnostics.Stopwatch]::StartNew()
  $timedOut = Invoke-Responsibility `
    -Case 'timeout-with-change' `
    -TaskId 'task-timeout' `
    -RunId $timeoutRun `
    -ResponsibilityTimeoutSeconds 1
  $timeoutWatch.Stop()
  Assert-True -Condition ($timedOut.ExitCode -ne 0) -Message 'Timed-out invocation unexpectedly succeeded'
  Assert-Equal -Actual $timedOut.Json.status -Expected 'interrupted' -Message 'Timed-out invocation status mismatch'
  Assert-Equal -Actual $timedOut.Json.sessionId -Expected $sessionId -Message 'Timed-out invocation lost live session id'
  Assert-True -Condition ($timeoutWatch.Elapsed.TotalSeconds -lt 8) -Message 'Timed-out invocation did not honor its internal deadline'
  Assert-True -Condition (Test-Path -LiteralPath (Join-Path $gitRoot 'orphan-timeout.txt')) -Message 'Timed-out invocation removed unfinished work'
  Assert-True -Condition (Test-Path -LiteralPath $childPidPath) -Message 'Timed-out fixture did not record its child pid'
  $timedOutChildPid = [int][IO.File]::ReadAllText($childPidPath)
  Assert-True -Condition ($null -eq (Get-Process -Id $timedOutChildPid -ErrorAction SilentlyContinue)) -Message 'Timed-out responsibility child process leaked'
  $timedOutState = Assert-LeaseReleased
  Assert-Equal -Actual $timedOutState.state.recovery.trigger -Expected 'interruption' -Message 'Timed-out invocation did not save interruption recovery'
  Assert-Equal -Actual $timedOutState.state.recovery.resumeId -Expected $sessionId -Message 'Timed-out recovery lost session id'
  Assert-True -Condition ('orphan-timeout.txt' -in @($timedOutState.state.recovery.changedPaths)) -Message 'Timed-out recovery lost changed path'
  $timedOutRecoveryLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-timeout'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $timedOutRecoveryLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Timed-out recovery could not be reacquired'
  Invoke-LeaseJson -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $timedOutRecoveryLease.runId } | Out-Null
  Invoke-LeaseJson -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $timedOutRecoveryLease.runId } | Out-Null

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-commit-with-residue'
  $commitWithResidueRun = Acquire-TestLease -TaskId 'task-commit-with-residue'
  $commitWithResidue = Invoke-Responsibility -Case 'unverified-commit-with-change' -TaskId 'task-commit-with-residue' -RunId $commitWithResidueRun
  Assert-True -Condition ($commitWithResidue.ExitCode -ne 0) -Message 'Commit-with-residue invocation unexpectedly succeeded'
  Assert-Equal -Actual $commitWithResidue.Json.status -Expected 'interrupted' -Message 'Commit-with-residue status mismatch'
  Assert-True -Condition (Test-Path -LiteralPath (Join-Path $gitRoot 'orphan-after-commit.txt')) -Message 'Commit-with-residue invocation removed task residue'
  $commitWithResidueState = Assert-LeaseReleased
  Assert-Equal -Actual $commitWithResidueState.state.recovery.trigger -Expected 'interruption' -Message 'Commit-with-residue recovery trigger mismatch'
  Assert-Equal -Actual $commitWithResidueState.state.recovery.resumeId -Expected $sessionId -Message 'Commit-with-residue recovery lost session id'
  Assert-True -Condition ('orphan-after-commit.txt' -in @($commitWithResidueState.state.recovery.changedPaths)) -Message 'Commit-with-residue recovery lost changed path'

  $commitWithResidueRecoveryLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-commit-with-residue'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $commitWithResidueRecoveryLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Commit-with-residue recovery could not be reacquired'
  Invoke-LeaseJson -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $commitWithResidueRecoveryLease.runId } | Out-Null
  Invoke-LeaseJson -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $commitWithResidueRecoveryLease.runId } | Out-Null

  Reset-GitFixture
  $lifecycleResidueTaskId = 'task-matching-lifecycle-residue'
  Set-TaskProjectionFixture -TaskId $lifecycleResidueTaskId
  $lifecycleResidueRun = Acquire-TestLease -TaskId $lifecycleResidueTaskId
  $lifecycleResidue = Invoke-Responsibility `
    -Case 'matching-commit-with-lifecycle-residue' `
    -TaskId $lifecycleResidueTaskId `
    -RunId $lifecycleResidueRun
  Assert-True -Condition ($lifecycleResidue.ExitCode -ne 0) -Message 'Matching commit with lifecycle residue unexpectedly succeeded'
  Assert-Equal -Actual $lifecycleResidue.Json.status -Expected 'interrupted' -Message 'Matching commit with lifecycle residue status mismatch'
  Assert-True -Condition ($null -eq $lifecycleResidue.Json.commitSha) -Message 'Matching commit with lifecycle residue returned a verified commit'
  $lifecycleResidueState = Assert-LeaseReleased
  Assert-Equal -Actual $lifecycleResidueState.state.recovery.trigger -Expected 'interruption' -Message 'Matching commit with lifecycle residue recovery trigger mismatch'
  Assert-Equal -Actual $lifecycleResidueState.state.recovery.resumeId -Expected $sessionId -Message 'Matching commit with lifecycle residue recovery lost session id'
  Assert-Equal `
    -Actual @($lifecycleResidueState.state.recovery.changedPaths).Count `
    -Expected 3 `
    -Message 'Matching commit with lifecycle residue recovery changed-path count mismatch'
  foreach ($residuePath in @(
      "开发管理/任务卡/$lifecycleResidueTaskId.txt",
      '开发管理/当前任务队列.txt',
      '开发管理/任务列表/自动化任务.txt'
    )) {
    & git -C $gitRoot diff --quiet -- $residuePath
    $residueDiffExit = $LASTEXITCODE
    Assert-Equal `
      -Actual $residueDiffExit `
      -Expected 1 `
      -Message "Matching commit with lifecycle residue did not preserve uncommitted path: $residuePath"
  }
  $lifecycleResidueRecoveryLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = $lifecycleResidueTaskId
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $lifecycleResidueRecoveryLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Matching commit with lifecycle residue recovery could not be reacquired'
  Invoke-LeaseJson -Action ClearRecovery -Parameters @{ StateRoot = $stateRoot; RunId = $lifecycleResidueRecoveryLease.runId } | Out-Null
  Invoke-LeaseJson -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $lifecycleResidueRecoveryLease.runId } | Out-Null

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-commit-only'
  $commitOnlyRun = Acquire-TestLease -TaskId 'task-commit-only'
  $commitOnly = Invoke-Responsibility -Case 'unverified-commit-only' -TaskId 'task-commit-only' -RunId $commitOnlyRun
  Assert-True -Condition ($commitOnly.ExitCode -ne 0) -Message 'Commit-only invocation unexpectedly succeeded'
  Assert-Equal -Actual $commitOnly.Json.status -Expected 'blocked' -Message 'Commit-only status mismatch'
  Assert-Equal -Actual $commitOnly.Json.detailCode -Expected 'unverified_commit_shape' -Message 'Commit-only detail code mismatch'
  $commitOnlyState = Assert-LeaseReleased
  Assert-Equal -Actual $commitOnlyState.state.lastResult.category -Expected 'blocked' -Message 'Commit-only result category mismatch'
  Assert-Equal -Actual $commitOnlyState.state.lastResult.detailCode -Expected 'unverified_commit_shape' -Message 'Commit-only recorded detail mismatch'
  Assert-True -Condition ($null -eq $commitOnlyState.state.recovery) -Message 'Commit-only invocation invented recovery'

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-two-commits-only'
  $twoCommitsOnlyRun = Acquire-TestLease -TaskId 'task-two-commits-only'
  $twoCommitsOnly = Invoke-Responsibility -Case 'unverified-two-commits-only' -TaskId 'task-two-commits-only' -RunId $twoCommitsOnlyRun
  Assert-True -Condition ($twoCommitsOnly.ExitCode -ne 0) -Message 'Two-commit-only invocation unexpectedly succeeded'
  Assert-Equal -Actual $twoCommitsOnly.Json.status -Expected 'blocked' -Message 'Two-commit-only status mismatch'
  Assert-Equal -Actual $twoCommitsOnly.Json.detailCode -Expected 'unverified_commit_shape' -Message 'Two-commit-only detail code mismatch'
  $twoCommitsOnlyState = Assert-LeaseReleased
  Assert-Equal -Actual $twoCommitsOnlyState.state.lastResult.category -Expected 'blocked' -Message 'Two-commit-only result category mismatch'
  Assert-Equal -Actual $twoCommitsOnlyState.state.lastResult.detailCode -Expected 'unverified_commit_shape' -Message 'Two-commit-only recorded detail mismatch'
  Assert-True -Condition ($null -eq $twoCommitsOnlyState.state.recovery) -Message 'Two-commit-only invocation invented recovery'

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-removes-baseline'
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
  Set-TaskProjectionFixture -TaskId 'task-decision'
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
  $oldSessionDecision = Invoke-Responsibility `
    -Case 'commit-success' `
    -TaskId 'task-decision' `
    -RunId $decisionResumeLease.runId `
    -Route 'Recovery' `
    -Action 'Resume' `
    -ResumeSessionId $sessionId `
    -DecisionId 'decision-invoker-test' `
    -DecisionOption 'A'
  Assert-Equal -Actual $oldSessionDecision.ExitCode -Expected 1 -Message 'Decision reply incorrectly accepted an original-session resume'
  Assert-Equal -Actual $oldSessionDecision.Json.status -Expected 'failed' -Message 'Rejected original-session decision returned wrong status'
  $invalidDecisionResume = Invoke-Responsibility `
    -Case 'commit-success' `
    -TaskId 'task-decision' `
    -RunId $decisionResumeLease.runId `
    -Route 'Recovery' `
    -Action 'Start' `
    -DecisionId 'decision-invoker-test' `
    -DecisionOption 'A'
  Assert-Equal -Actual $invalidDecisionResume.ExitCode -Expected 2 -Message 'Ready decision recovery returned wrong exit code'
  Assert-Equal -Actual $invalidDecisionResume.Json.status -Expected 'blocked' -Message 'Ready decision recovery status mismatch'
  $invalidDecisionState = Assert-LeaseReleased
  Assert-True -Condition ($null -ne $invalidDecisionState.state.recovery) -Message 'Invalid decision recovery was cleared'

  $validDecisionLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'task-decision'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
    DecisionId = 'decision-invoker-test'
  }
  Assert-Equal -Actual $validDecisionLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Decision recovery could not reacquire after invalid closeout'
  $resumed = Invoke-Responsibility `
    -Case 'commit-blocked' `
    -TaskId 'task-decision' `
    -RunId $validDecisionLease.runId `
    -Route 'Recovery' `
    -Action 'Start' `
    -DecisionId 'decision-invoker-test' `
    -DecisionOption 'A'
  Assert-Equal -Actual $resumed.ExitCode -Expected 0 -Message 'Valid fresh decision invocation failed'
  Assert-Equal -Actual $resumed.Json.status -Expected 'completed' -Message 'Valid fresh decision invocation status mismatch'
  Assert-Equal -Actual $resumed.Json.taskState -Expected 'blocked' -Message 'Valid decision recovery taskState mismatch'
  $decisionPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($decisionPrompt.StartsWith("[TZG_DECISION_REPLY runId=$($validDecisionLease.runId)]`nA")) -Message 'Decision option was not transported with the fresh-session protocol'
  Assert-True -Condition ($decisionPrompt.Contains('这是新的 CLI-native 责任方会话。')) -Message 'Decision reply did not start a new responsibility session'
  Assert-True `
    -Condition $decisionPrompt.Contains('开发管理/自动工作流恢复规则.txt') `
    -Message 'Recovery route did not load recovery rules'
  Assert-True -Condition ($decisionPrompt.Contains('这是带决定回复的新责任方会话')) -Message 'Decision recovery lost its fresh-session route wording'
  $resumedState = Assert-LeaseReleased
  Assert-True -Condition ($null -eq $resumedState.state.recovery) -Message 'Completed decision resume did not clear recovery'

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-decision-stdin'
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
    -Case 'commit-blocked' `
    -TaskId 'task-decision-stdin' `
    -RunId $stdinResumeLease.runId `
    -Route 'Recovery' `
    -Action 'Start' `
    -DecisionId 'decision-invoker-stdin' `
    -DecisionInput $customDecision
  Assert-Equal -Actual $stdinResumed.ExitCode -Expected 0 -Message 'Stdin fresh decision invocation failed'
  Assert-Equal -Actual $stdinResumed.Json.taskState -Expected 'blocked' -Message 'Stdin fresh decision taskState mismatch'
  $stdinDecisionPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($stdinDecisionPrompt.StartsWith("[TZG_DECISION_REPLY runId=$($stdinResumeLease.runId)]`n$customDecision")) -Message 'Signed bridge decision was not transported through stdin'
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
