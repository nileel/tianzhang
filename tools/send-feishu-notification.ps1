#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('TaskOutcome', 'DailyReport', 'WeeklyReport')]
  [string]$Kind,

  [string]$RepositoryRoot,
  [string]$TaskId,
  [ValidateSet('completed', 'pending_review', 'requeued', 'blocked', 'waiting_decision', 'waiting_reply', 'failed')]
  [string]$Status,
  [string]$RunId,
  [string]$CommitSha,
  [string]$DetailCode,

  [string]$WindowUntil,
  [string]$Title,
  [string]$Body,

  [string]$NodePath
)

$ErrorActionPreference = 'Stop'
$script:SenderEntry = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\send-notification.mjs'
$metadataContractPath = Join-Path $PSScriptRoot 'automation-commit-metadata.ps1'
if (-not (Test-Path -LiteralPath $metadataContractPath -PathType Leaf)) {
  throw 'Automation commit metadata contract is unavailable'
}
. $metadataContractPath

function Write-InvalidResult {
  [Console]::Out.WriteLine('{"result":"INVALID_INPUT"}')
}

function Assert-StableText {
  param(
    [AllowNull()][string]$Value,
    [string]$Name,
    [int]$MaximumLength = 1000,
    [switch]$AllowNewline
  )

  if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt $MaximumLength) {
    throw "$Name is invalid"
  }
  $controlPattern = if ($AllowNewline) {
    '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]'
  } else {
    '[\x00-\x1F\x7F]'
  }
  if ($Value -match $controlPattern) {
    throw "$Name is invalid"
  }
}

function Get-UnicodeCodePointCount {
  param([string]$Value)

  $count = 0
  for ($index = 0; $index -lt $Value.Length; $index++) {
    if (
      [char]::IsHighSurrogate($Value[$index]) -and
      $index + 1 -lt $Value.Length -and
      [char]::IsLowSurrogate($Value[$index + 1])
    ) {
      $index++
    }
    $count++
  }
  $count
}

function Assert-PlainText {
  param([string]$Value, [string]$Name)

  Assert-StableText -Value $Value -Name $Name -MaximumLength 400
  if ((Get-UnicodeCodePointCount -Value $Value) -gt 200) {
    throw "$Name is invalid"
  }
}

function Get-TaskMeta {
  param([string]$Root, [string]$Id)

  $activePath = Join-Path $Root "开发管理\任务卡\$Id.txt"
  $archivePath = Join-Path $Root "开发管理\任务归档\$Id.txt"
  $path = if (Test-Path -LiteralPath $activePath -PathType Leaf) {
    $activePath
  } elseif (Test-Path -LiteralPath $archivePath -PathType Leaf) {
    $archivePath
  } else {
    throw 'Task card is unavailable'
  }
  $raw = Get-Content -LiteralPath $path -Raw -Encoding UTF8
  $match = [regex]::Match(
    $raw,
    '(?s)\A---TASK-META---\s*(?<json>\{.*?\})\s*---TASK-BODY---'
  )
  if (-not $match.Success) {
    throw 'Task metadata is invalid'
  }
  $meta = $match.Groups['json'].Value | ConvertFrom-Json -Depth 30
  if (
    [string]$meta.id -cne $Id -or
    [string]::IsNullOrWhiteSpace([string]$meta.title)
  ) {
    throw 'Task metadata is invalid'
  }
  $meta
}

function Invoke-GitUtf8Text {
  param([string]$Root, [string[]]$Arguments)

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.ArgumentList.Add('-C')
  $startInfo.ArgumentList.Add($Root)
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw 'Commit is unavailable'
  }
  $stdout = $process.StandardOutput.ReadToEndAsync()
  $stderr = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $exitCode = $process.ExitCode
  $text = $stdout.GetAwaiter().GetResult()
  $null = $stderr.GetAwaiter().GetResult()
  $process.Dispose()
  if ($exitCode -ne 0) {
    throw 'Commit is unavailable'
  }
  $text.TrimEnd()
}

function Get-CommitFields {
  param([string]$Root, [string]$Sha, [string]$Id, [string]$ExpectedState)

  if ($Sha -notmatch '^[0-9a-f]{40}$') {
    throw 'CommitSha is invalid'
  }
  $body = Invoke-GitUtf8Text -Root $Root -Arguments @('show', '-s', '--format=%B', $Sha)
  $metadata = ConvertFrom-TzgAutomationCommitMessage `
    -Message $body `
    -ExpectedTask $Id `
    -ExpectedState $ExpectedState
  [ordered]@{
    goal = $metadata.Goal
    completed = $metadata.Completed
    impact = $metadata.Impact
    boundary = $metadata.Boundary
    verification = $metadata.Verification
    next = $metadata.Next
    plainHappened = $metadata.PlainHappened
    plainImpact = $metadata.PlainImpact
    plainAction = $metadata.PlainAction
  }
}

function New-TaskRequest {
  $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) {
    throw 'RepositoryRoot is invalid'
  }
  Assert-StableText -Value $TaskId -Name 'TaskId' -MaximumLength 128
  Assert-StableText -Value $RunId -Name 'RunId' -MaximumLength 256
  $meta = Get-TaskMeta -Root $root -Id $TaskId
  $fields = if (-not [string]::IsNullOrWhiteSpace($CommitSha)) {
    if ($Status -notin @('completed', 'pending_review', 'requeued')) {
      throw 'CommitSha is invalid for this status'
    }
    $expectedCommitState = if ($Status -ceq 'requeued') { 'completed' } else { $Status }
    Get-CommitFields -Root $root -Sha $CommitSha -Id $TaskId -ExpectedState $expectedCommitState
  } else {
    if ($Status -in @('completed', 'pending_review', 'requeued')) {
      throw 'CommitSha is required for this status'
    }
    Assert-StableText -Value $DetailCode -Name 'DetailCode' -MaximumLength 256
    $statusLabel = switch ($Status) {
      'blocked' { '阻塞' }
      'waiting_decision' { '待决定' }
      'waiting_reply' { '待回复' }
      default { '失败' }
    }
    $reason = if ([string]::IsNullOrWhiteSpace([string]$meta.stateReason)) {
      "自动化终态：$statusLabel（$DetailCode）"
    } else {
      [string]$meta.stateReason
    }
    $next = switch ($Status) {
      'waiting_decision' { "等待负责人决定后恢复；当前原因：$reason" }
      'waiting_reply' { "等待负责人回复后恢复；当前原因：$reason" }
      'blocked' { "解除阻塞条件后再推进；当前原因：$reason" }
      default { "检查自动化终态 $DetailCode 后再决定是否重启；当前原因：$reason" }
    }
    [ordered]@{
      goal = "推进任务《$($meta.title)》"
      completed = "本轮未形成已核验业务提交；终态为 $statusLabel（$DetailCode）"
      impact = '未确认任何业务行为变化'
      boundary = '未把未提交或未核验内容计为完成'
      verification = '仅核验自动化终态与任务卡当前状态；没有业务提交可供领域验证'
      next = $next
      plainHappened = "任务《$($meta.title)》本轮没有形成已经核验的完成结果，当前状态是$statusLabel"
      plainImpact = '这项任务还不能算完成，目前没有确认游戏内容或项目行为已经改变'
      plainAction = switch ($Status) {
        'waiting_decision' { '请完成对应决策，自动工作流会在收到选择后继续' }
        'waiting_reply' { '请回复当前等待的问题，自动工作流会在收到回复后继续' }
        'blocked' { '需要先解除通知中说明的阻塞条件，再继续推进' }
        default { '请先查看失败原因，再决定是否重新启动该任务' }
      }
    }
  }
  foreach ($name in @('goal', 'completed', 'impact', 'boundary', 'verification', 'next')) {
    Assert-StableText -Value ([string]$fields[$name]) -Name $name
  }
  foreach ($name in @('plainHappened', 'plainImpact', 'plainAction')) {
    Assert-PlainText -Value ([string]$fields[$name]) -Name $name
  }
  $eventTail = if ([string]::IsNullOrWhiteSpace($CommitSha)) {
    "$RunId`:$DetailCode"
  } else {
    $CommitSha
  }
  [ordered]@{
    notification = [ordered]@{
      kind = 'task_outcome'
      taskId = $TaskId
      title = [string]$meta.title
      status = $Status
      goal = [string]$fields.goal
      completed = [string]$fields.completed
      impact = [string]$fields.impact
      boundary = [string]$fields.boundary
      verification = [string]$fields.verification
      next = [string]$fields.next
      plainHappened = [string]$fields.plainHappened
      plainImpact = [string]$fields.plainImpact
      plainAction = [string]$fields.plainAction
      commitSha = if ([string]::IsNullOrWhiteSpace($CommitSha)) { $null } else { $CommitSha }
    }
    idempotencyKey = "task_outcome:$TaskId`:$Status`:$eventTail"
  }
}

function New-ReportRequest {
  Assert-StableText -Value $WindowUntil -Name 'WindowUntil' -MaximumLength 64
  $parsedUntil = [datetimeoffset]::MinValue
  if (
    $WindowUntil -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$' -or
    -not [datetimeoffset]::TryParse(
      $WindowUntil,
      [Globalization.CultureInfo]::InvariantCulture,
      [Globalization.DateTimeStyles]::RoundtripKind,
      [ref]$parsedUntil
    )
  ) {
    throw 'WindowUntil is invalid'
  }
  Assert-StableText -Value $Title -Name 'Title' -MaximumLength 120
  Assert-StableText -Value $Body -Name 'Body' -MaximumLength 6000 -AllowNewline
  $wireKind = if ($Kind -ceq 'DailyReport') { 'daily_report' } else { 'weekly_report' }
  $automationId = if ($Kind -ceq 'DailyReport') {
    'tzg-daily-automation-briefing'
  } else {
    'tzg-weekly-project-summary'
  }
  [ordered]@{
    notification = [ordered]@{
      kind = $wireKind
      title = $Title
      body = $Body
    }
    idempotencyKey = "$wireKind`:$automationId`:$WindowUntil"
  }
}

try {
  if (-not (Test-Path -LiteralPath $script:SenderEntry -PathType Leaf)) {
    throw 'Notification sender is unavailable'
  }
  $resolvedNode = if ([string]::IsNullOrWhiteSpace($NodePath)) {
    $command = Get-Command node -ErrorAction Stop
    [IO.Path]::GetFullPath($command.Source)
  } else {
    [IO.Path]::GetFullPath($NodePath)
  }
  if (-not (Test-Path -LiteralPath $resolvedNode -PathType Leaf)) {
    throw 'Node runtime is unavailable'
  }
  $request = if ($Kind -ceq 'TaskOutcome') {
    New-TaskRequest
  } else {
    New-ReportRequest
  }
  $json = $request | ConvertTo-Json -Depth 20 -Compress
  $output = @($json | & $resolvedNode $script:SenderEntry 2>$null)
  $exitCode = $LASTEXITCODE
  $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  if ($lines.Count -ne 1) {
    throw 'Notification sender returned an invalid response'
  }
  [Console]::Out.WriteLine([string]$lines[0])
  exit $exitCode
} catch {
  Write-InvalidResult
  exit 22
}
