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

function Write-TestUtf8 {
  param([string]$Path, [string]$Text)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-Facade {
  param([string[]]$Arguments, [int]$SenderExitCode = 0)

  Remove-Item -LiteralPath $tracePath -Force -ErrorAction SilentlyContinue
  $oldTrace = $env:FEISHU_NOTIFICATION_TEST_TRACE
  $oldExit = $env:FEISHU_NOTIFICATION_TEST_EXIT
  try {
    $env:FEISHU_NOTIFICATION_TEST_TRACE = $tracePath
    $env:FEISHU_NOTIFICATION_TEST_EXIT = [string]$SenderExitCode
    $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $facadePath @Arguments -NodePath $fakeNodePath)
    $exitCode = $LASTEXITCODE
  } finally {
    $env:FEISHU_NOTIFICATION_TEST_TRACE = $oldTrace
    $env:FEISHU_NOTIFICATION_TEST_EXIT = $oldExit
  }
  [pscustomobject]@{
    ExitCode = $exitCode
    Output = @($output) -join "`n"
    Request = if (Test-Path -LiteralPath $tracePath) {
      [IO.File]::ReadAllText($tracePath) | ConvertFrom-Json -Depth 30
    } else {
      $null
    }
  }
}

$facadePath = Join-Path $PSScriptRoot 'send-feishu-notification.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-feishu-notification-test-$([Guid]::NewGuid().ToString('N'))"
$repoRoot = Join-Path $testRoot 'repo'
$fakeNodePath = Join-Path $testRoot 'fake-node.cmd'
$fakeNodeScriptPath = Join-Path $testRoot 'fake-node.ps1'
$tracePath = Join-Path $testRoot 'request.json'

try {
  [IO.Directory]::CreateDirectory($repoRoot) | Out-Null
  & git -C $repoRoot init -q
  & git -C $repoRoot config user.name 'Notification Test'
  & git -C $repoRoot config user.email 'notification-test@example.invalid'
  & git -C $repoRoot config core.autocrlf false

  $taskId = 'TEST-NOTIFY-01'
  $meta = [ordered]@{
    schemaVersion = 1
    id = $taskId
    title = '普通通知适配器测试'
    priority = 'P2'
    route = 'codex_execute'
    owner = 'codex'
    domain = 'automation'
    stage = 'verification'
    dispatchState = 'blocked'
    blockedBy = @()
    stateReason = '等待直接验证完成'
    expectedPaths = @('result.txt')
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---'
    ($meta | ConvertTo-Json -Depth 10)
    '---TASK-BODY---'
    "# $taskId · 普通通知适配器测试"
  ) -join "`n"
  Write-TestUtf8 -Path (Join-Path $repoRoot "开发管理/任务卡/$taskId.txt") -Text $card
  Write-TestUtf8 -Path (Join-Path $repoRoot 'seed.txt') -Text 'seed'
  & git -C $repoRoot add -A
  & git -C $repoRoot commit -q -m 'test: seed notification fixture'

  Write-TestUtf8 -Path (Join-Path $repoRoot 'result.txt') -Text 'verified result'
  & git -C $repoRoot add result.txt
  $message = @'
test: structured outcome

Automation: tzg-hourly-controller
Task: TEST-NOTIFY-01
State: completed
Result: 问题=缺少可理解的任务通知；完成=形成五段式普通通知
Impact: 影响=任务终态可直接理解；边界=未改变任务或租约状态
Verify: 验证=适配器直接测试通过；后续=等待飞书金丝雀
Plain: 发生=任务通知已经同时包含专业内容和通俗解释；影响=负责人不参与写代码也能看懂本轮结果；需要=无需处理
'@
  & git -C $repoRoot commit -q -m $message
  $structuredSha = [string](& git -C $repoRoot rev-parse HEAD)

  $fakeNode = @'
$raw = @($input) -join "`n"
[IO.File]::WriteAllText(
  $env:FEISHU_NOTIFICATION_TEST_TRACE,
  $raw,
  [Text.UTF8Encoding]::new($false)
)
$exitCode = [int]$env:FEISHU_NOTIFICATION_TEST_EXIT
$result = if ($exitCode -eq 0) { 'PROVIDER_ACCEPTED' } else { 'DELIVERY_FAILED' }
[Console]::Out.WriteLine((@{ result = $result } | ConvertTo-Json -Compress))
exit $exitCode
'@
  Write-TestUtf8 -Path $fakeNodeScriptPath -Text $fakeNode
  Write-TestUtf8 -Path $fakeNodePath -Text "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"$fakeNodeScriptPath`" %*`r`nexit /b %ERRORLEVEL%`r`n"

  $taskResult = Invoke-Facade -Arguments @(
    '-Kind', 'TaskOutcome',
    '-RepositoryRoot', $repoRoot,
    '-TaskId', $taskId,
    '-Status', 'completed',
    '-RunId', 'run-structured',
    '-CommitSha', $structuredSha
  )
  Assert-Equal -Actual $taskResult.ExitCode -Expected 0 -Message 'Structured task notification failed'
  Assert-Equal -Actual $taskResult.Request.notification.goal -Expected '缺少可理解的任务通知' -Message 'Task goal was not parsed'
  Assert-Equal -Actual $taskResult.Request.notification.completed -Expected '形成五段式普通通知' -Message 'Completed work was not parsed'
  Assert-Equal -Actual $taskResult.Request.notification.boundary -Expected '未改变任务或租约状态' -Message 'Task boundary was not parsed'
  Assert-Equal -Actual $taskResult.Request.notification.next -Expected '等待飞书金丝雀' -Message 'Task next relationship was not parsed'
  Assert-Equal -Actual $taskResult.Request.notification.plainHappened -Expected '任务通知已经同时包含专业内容和通俗解释' -Message 'Plain-language outcome was not parsed'
  Assert-Equal -Actual $taskResult.Request.notification.plainImpact -Expected '负责人不参与写代码也能看懂本轮结果' -Message 'Plain-language impact was not parsed'
  Assert-Equal -Actual $taskResult.Request.notification.plainAction -Expected '无需处理' -Message 'Plain-language action was not parsed'
  Assert-Equal -Actual $taskResult.Request.notification.commitSha -Expected $structuredSha -Message 'Commit SHA was not transported'

  $blockedResult = Invoke-Facade -Arguments @(
    '-Kind', 'TaskOutcome',
    '-RepositoryRoot', $repoRoot,
    '-TaskId', $taskId,
    '-Status', 'blocked',
    '-RunId', 'run-blocked',
    '-DetailCode', 'dependency_missing'
  )
  Assert-Equal -Actual $blockedResult.ExitCode -Expected 0 -Message 'No-commit terminal notification failed'
  Assert-True -Condition $blockedResult.Request.notification.completed.Contains('未形成已核验业务提交') -Message 'No-commit terminal fabricated completed work'
  Assert-True -Condition $blockedResult.Request.notification.plainHappened.Contains('没有形成已经核验的完成结果') -Message 'No-commit terminal lacked a plain-language outcome'
  Assert-Equal -Actual $blockedResult.Request.notification.plainImpact -Expected '这项任务还不能算完成，目前没有确认游戏内容或项目行为已经改变' -Message 'No-commit terminal plain-language impact mismatch'
  Assert-Equal -Actual $blockedResult.Request.notification.plainAction -Expected '需要先解除通知中说明的阻塞条件，再继续推进' -Message 'No-commit terminal plain-language action mismatch'
  Assert-Equal -Actual $blockedResult.Request.notification.commitSha -Expected $null -Message 'No-commit terminal invented a commit'

  $reportBody = "# 净成果`n`n- 完整正文`n- 第二行"
  $reportResult = Invoke-Facade -Arguments @(
    '-Kind', 'DailyReport',
    '-WindowUntil', '2026-07-27T08:00:00.000Z',
    '-Title', '天章日报 · 2026-07-27',
    '-Body', $reportBody
  )
  Assert-Equal -Actual $reportResult.ExitCode -Expected 0 -Message 'Daily report notification failed'
  Assert-Equal -Actual $reportResult.Request.notification.body -Expected $reportBody -Message 'Report body changed in transport'
  Assert-Equal -Actual $reportResult.Request.idempotencyKey -Expected 'daily_report:tzg-daily-automation-briefing:2026-07-27T08:00:00.000Z' -Message 'Daily report idempotency key mismatch'

  Write-TestUtf8 -Path (Join-Path $repoRoot 'legacy.txt') -Text 'legacy metadata'
  & git -C $repoRoot add legacy.txt
  $legacyMessage = @'
test: legacy outcome

Automation: tzg-hourly-controller
Task: TEST-NOTIFY-01
State: completed
Result: completed
Impact: broad impact
Verify: passed
'@
  & git -C $repoRoot commit -q -m $legacyMessage
  $legacySha = [string](& git -C $repoRoot rev-parse HEAD)
  $legacyResult = Invoke-Facade -Arguments @(
    '-Kind', 'TaskOutcome',
    '-RepositoryRoot', $repoRoot,
    '-TaskId', $taskId,
    '-Status', 'completed',
    '-RunId', 'run-legacy',
    '-CommitSha', $legacySha
  )
  Assert-Equal -Actual $legacyResult.ExitCode -Expected 22 -Message 'Legacy task metadata bypassed the notification contract'
  Assert-Equal -Actual $legacyResult.Output -Expected '{"result":"INVALID_INPUT"}' -Message 'Legacy metadata failure leaked details'
  Assert-True -Condition ($null -eq $legacyResult.Request) -Message 'Invalid task reached the sender'

  $senderFailure = Invoke-Facade -Arguments @(
    '-Kind', 'WeeklyReport',
    '-WindowUntil', '2026-07-27T08:00:00Z',
    '-Title', '天章周报 · 2026-07-20—2026-07-27',
    '-Body', '# 完整周报'
  ) -SenderExitCode 21
  Assert-Equal -Actual $senderFailure.ExitCode -Expected 21 -Message 'Facade did not preserve sanitized sender failure code'
  Assert-Equal -Actual $senderFailure.Output -Expected '{"result":"DELIVERY_FAILED"}' -Message 'Facade changed sender output'

  Write-Output 'test-send-feishu-notification: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing cleanup outside temp root: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
