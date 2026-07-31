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
$notificationTracePath = Join-Path $testRoot 'notification-args.json'
$stateRoot = Join-Path $automationStateRoot "tzg-invoke-responsibility-tests\$testId"
$bridgeRoot = Join-Path $automationStateRoot "tzg-feishu-decision-bridge\invoke-test-$testId"
$decisionRequestPath = Join-Path $bridgeRoot 'decision-request.json'
$fakeCodexPath = Join-Path $fakeBin 'codex.ps1'
$fakeNotificationPath = Join-Path $testRoot 'fake-notification.ps1'
$missingNotificationPath = Join-Path $testRoot 'missing-notification.ps1'
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
    schemaVersion = 2
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
    workerPaths = @(
      'result.txt'
    )
    coordinatorPaths = @(
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
    [ValidateSet('QueueMaintenance', 'Recovery')]
    [string]$Route = 'QueueMaintenance',
    [ValidateSet('Start', 'Resume')]
    [string]$Action = 'Start',
    [string]$ResumeSessionId,
    [string]$DecisionId,
    [ValidateSet('A', 'B', 'C')]
    [string]$DecisionOption,
    [string]$DecisionInput,
    [int]$ResponsibilityTimeoutSeconds,
    [switch]$UseFakeNotification,
    [int]$NotificationExitCode = 0
  )

  Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
  if ($UseFakeNotification) {
    Remove-Item -LiteralPath $notificationTracePath -Force -ErrorAction SilentlyContinue
  }
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
      '-StateRoot', $stateRoot,
      '-NotificationPath', $(if ($UseFakeNotification) { $fakeNotificationPath } else { $missingNotificationPath })
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
  $startInfo.Environment['RESPONSIBILITY_TEST_NOTIFICATION_TRACE'] = $notificationTracePath
  $startInfo.Environment['RESPONSIBILITY_TEST_NOTIFICATION_EXIT'] = [string]$NotificationExitCode

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
  $message = "test: automation result`n`nAutomation: tzg-hourly-controller`nTask: $($env:RESPONSIBILITY_TEST_TASK_ID)`nState: completed`nResult: 问题=fixture problem；完成=fixture completed`nImpact: 影响=no downstream impact；边界=fixture boundary`nVerify: 验证=fixture verification；后续=fixture next`nPlain: 发生=测试任务形成了已核验结果；影响=负责人可以直接理解本轮结果；需要=无需处理"
  & git commit -q -m $message
}

switch ($env:RESPONSIBILITY_TEST_CASE) {
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
  'child-failed-with-change' {
    [IO.File]::WriteAllText((Join-Path (Get-Location) 'orphan.txt'), 'preserve me', [Text.UTF8Encoding]::new($false))
    $global:LASTEXITCODE = 9
    exit 9
  }
  'no-outcome' { }
  default { throw "Unknown case: $($env:RESPONSIBILITY_TEST_CASE)" }
}
$global:LASTEXITCODE = 0
'@
  [IO.File]::WriteAllText($fakeCodexPath, $fakeCodex, [Text.UTF8Encoding]::new($false))
  $fakeNotification = @'
[IO.File]::WriteAllText(
  $env:RESPONSIBILITY_TEST_NOTIFICATION_TRACE,
  ($args | ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false)
)
[Console]::Out.WriteLine('{"result":"DELIVERY_FAILED"}')
exit [int]$env:RESPONSIBILITY_TEST_NOTIFICATION_EXIT
'@
  [IO.File]::WriteAllText($fakeNotificationPath, $fakeNotification, [Text.UTF8Encoding]::new($false))

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

  Reset-GitFixture
  Set-TaskProjectionFixture -TaskId 'task-global-projection' -DispatchState 'blocked'
  $queueEmptyRun = Acquire-TestLease -TaskId 'QUEUE-MAINTENANCE'
  $queueEmpty = Invoke-Responsibility -Case 'no-outcome' -TaskId 'QUEUE-MAINTENANCE' -RunId $queueEmptyRun -Route 'QueueMaintenance' -UseFakeNotification
  Assert-Equal -Actual $queueEmpty.ExitCode -Expected 2 -Message 'Clean empty queue returned wrong exit code'
  Assert-Equal -Actual $queueEmpty.Json.status -Expected 'blocked' -Message 'Clean empty queue status mismatch'
  Assert-Equal -Actual $queueEmpty.Json.category -Expected 'blocked' -Message 'Clean empty queue category mismatch'
  Assert-Equal -Actual $queueEmpty.Json.detailCode -Expected 'no_runnable_candidate' -Message 'Clean empty queue detail mismatch'
  Assert-Equal -Actual $queueEmpty.Json.readyCount -Expected 0 -Message 'Clean empty queue readyCount mismatch'
  $queueEmptyState = Assert-LeaseReleased
  Assert-Equal -Actual $queueEmptyState.state.blocking.fingerprint -Expected 'queue:no_runnable_candidate' -Message 'Clean empty queue fingerprint mismatch'
  Assert-Equal -Actual $queueEmptyState.state.blocking.count -Expected 1 -Message 'Clean empty queue blocker count mismatch'
  Assert-True -Condition (-not (Test-Path -LiteralPath $notificationTracePath)) -Message 'No-op queue maintenance emitted a notification'

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
  Set-TaskProjectionFixture -TaskId 'task-maintenance-recovery' -DispatchState 'blocked'
  $interruptedRun = Acquire-TestLease -TaskId 'QUEUE-MAINTENANCE'
  $interrupted = Invoke-Responsibility `
    -Case 'child-failed-with-change' `
    -TaskId 'QUEUE-MAINTENANCE' `
    -RunId $interruptedRun `
    -Route 'QueueMaintenance'
  Assert-True -Condition ($interrupted.ExitCode -ne 0) -Message 'Queue maintenance interruption unexpectedly succeeded'
  Assert-Equal -Actual $interrupted.Json.status -Expected 'interrupted' -Message 'Queue maintenance interruption status mismatch'
  Assert-True -Condition (Test-Path -LiteralPath (Join-Path $gitRoot 'orphan.txt')) -Message 'Queue maintenance interruption removed unfinished work'
  $interruptedState = Assert-LeaseReleased
  Assert-Equal -Actual $interruptedState.state.recovery.trigger -Expected 'interruption' -Message 'Queue maintenance interruption did not save recovery'
  Assert-Equal -Actual $interruptedState.state.recovery.resumeId -Expected $sessionId -Message 'Queue maintenance interruption lost session id'
  Assert-True -Condition ('orphan.txt' -in @($interruptedState.state.recovery.changedPaths)) -Message 'Queue maintenance interruption lost changed path'

  $recoveryLease = Invoke-LeaseJson -Action Acquire -Parameters @{
    StateRoot = $stateRoot
    TaskId = 'QUEUE-MAINTENANCE'
    Owner = 'codex'
    RepositoryRoot = $gitRoot
    ResumeRecovery = $true
  }
  Assert-Equal -Actual $recoveryLease.status -Expected 'RECOVERY_ACQUIRED' -Message 'Queue maintenance recovery could not be acquired'
  $interruptionResumed = Invoke-Responsibility `
    -Case 'commit-queue-corrected-empty' `
    -TaskId 'QUEUE-MAINTENANCE' `
    -RunId $recoveryLease.runId `
    -Route 'Recovery' `
    -Action 'Resume' `
    -ResumeSessionId $sessionId
  Assert-Equal -Actual $interruptionResumed.ExitCode -Expected 0 -Message 'Queue maintenance recovery invocation failed'
  Assert-Equal -Actual $interruptionResumed.Json.status -Expected 'completed' -Message 'Queue maintenance recovery status mismatch'
  Assert-Equal -Actual $interruptionResumed.Json.readyCount -Expected 0 -Message 'Queue maintenance recovery readyCount mismatch'
  $interruptionPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($interruptionPrompt.Contains('开发管理/自动工作流恢复规则.txt')) -Message 'Interruption recovery did not load recovery rules'
  Assert-True -Condition ($interruptionPrompt.Contains('这是原 CLI session 的续跑，不创建新责任方。')) -Message 'Interruption recovery did not resume the original session'
  $interruptionState = Assert-LeaseReleased
  Assert-True -Condition ($null -eq $interruptionState.state.recovery) -Message 'Completed interruption recovery was not cleared'

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
    -DecisionInput $customDecision `
    -UseFakeNotification `
    -NotificationExitCode 21
  Assert-Equal -Actual $stdinResumed.ExitCode -Expected 0 -Message 'Stdin fresh decision invocation failed'
  Assert-Equal -Actual $stdinResumed.Json.taskState -Expected 'blocked' -Message 'Stdin fresh decision taskState mismatch'
  $stdinDecisionPrompt = [IO.File]::ReadAllText($tracePath)
  Assert-True -Condition ($stdinDecisionPrompt.StartsWith("[TZG_DECISION_REPLY runId=$($stdinResumeLease.runId)]`n$customDecision")) -Message 'Signed bridge decision was not transported through stdin'
  Assert-LeaseReleased | Out-Null
  Assert-True -Condition (Test-Path -LiteralPath $notificationTracePath) -Message 'Completed task-bearing recovery did not invoke notification adapter'
  $notificationArguments = @([IO.File]::ReadAllText($notificationTracePath) | ConvertFrom-Json)
  foreach ($requiredArgument in @('TaskOutcome', 'task-decision-stdin', 'completed', [string]$stdinResumed.Json.commitSha)) {
    Assert-True -Condition ($requiredArgument -in $notificationArguments) -Message "Notification adapter missed recovery argument: $requiredArgument"
  }

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
