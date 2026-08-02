#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidateSet('PauseDecision', 'ResumeReady', 'Block')][string]$Action,
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$TaskId,
  [Parameter(Mandatory = $true)][string]$ContextPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Utf8 { param([string]$Path) [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF) }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Normalize-Cell { param([string]$Value) $Value.Trim().Trim([char]96) }

function Get-TaskContextDigest {
  param([object]$Metadata)
  $context = [ordered]@{
    id = [string]$Metadata.id; title = [string]$Metadata.title; priority = [string]$Metadata.priority
    route = [string]$Metadata.route; owner = [string]$Metadata.owner; domain = [string]$Metadata.domain; stage = [string]$Metadata.stage
    blockedBy = @($Metadata.blockedBy | ForEach-Object { [string]$_ }); expectedPaths = @($Metadata.expectedPaths | ForEach-Object { [string]$_ })
    sourceBacklog = [string]$Metadata.sourceBacklog
  }
  $json = $context | ConvertTo-Json -Compress -Depth 20
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
}

function Read-Card {
  param([string]$Path)
  $text = Read-Utf8 $Path
  $match = [regex]::Match($text, '(?ms)\A---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---\r?\n(?<body>.*)\z')
  if (-not $match.Success) { throw 'Task card is invalid' }
  [pscustomobject]@{ Metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100; Body = $match.Groups['body'].Value }
}

function Write-Card {
  param([string]$Path, [object]$Metadata, [string]$Body)
  Write-Utf8 $Path ((@('---TASK-META---', ($Metadata | ConvertTo-Json -Depth 100), '---TASK-BODY---', $Body) -join "`n"))
}

function Get-Table {
  param([string]$Path, [string[]]$Header)
  $text = Read-Utf8 $Path
  $finalNewline = $text.EndsWith("`n", [StringComparison]::Ordinal)
  $lines = [Collections.Generic.List[string]]::new()
  foreach ($line in @($text -split "`n")) { $lines.Add($line.TrimEnd("`r")) }
  $headerIndex = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $cells = if ($lines[$i].Trim().StartsWith('|')) { @($lines[$i].Trim().Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ }) } else { @() }
    if (($cells -join "`0") -ceq ($Header -join "`0")) { $headerIndex = $i; break }
  }
  if ($headerIndex -lt 0) { throw 'Table header is missing' }
  $rows = [Collections.Generic.List[object]]::new()
  $start = $headerIndex + 2
  $end = $start
  while ($end -lt $lines.Count -and $lines[$end].Trim().StartsWith('|') -and -not [string]::IsNullOrWhiteSpace($lines[$end])) {
    $cells = @($lines[$end].Trim().Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ })
    if ($cells.Count -ne $Header.Count) { throw 'Table row is invalid' }
    $rows.Add($cells); $end++
  }
  [pscustomobject]@{ Path = $Path; Lines = $lines; HeaderIndex = $headerIndex; Start = $start; End = $end; Rows = $rows; FinalNewline = $finalNewline }
}

function Write-Table {
  param([object]$Table)
  $replacement = @($Table.Rows | ForEach-Object { '| ' + (@($_) -join ' | ') + ' |' })
  $count = $Table.End - $Table.Start
  if ($count -gt 0) { $Table.Lines.RemoveRange($Table.Start, $count) }
  if ($replacement.Count -gt 0) { $Table.Lines.InsertRange($Table.Start, [string[]]$replacement) }
  $text = @($Table.Lines) -join "`n"
  if ($Table.FinalNewline -and -not $text.EndsWith("`n", [StringComparison]::Ordinal)) { $text += "`n" }
  Write-Utf8 $Table.Path $text
}

function Find-RowIndex { param([object]$Table, [string]$Id) $matches = @(for ($i = 0; $i -lt $Table.Rows.Count; $i++) { if ([string]$Table.Rows[$i][0] -ceq $Id) { $i } }); if ($matches.Count -gt 1) { throw 'Duplicate table row' }; if ($matches.Count -eq 0) { return -1 }; [int]$matches[0] }

try {
  $root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RepositoryRoot).Path).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { throw 'Repository root is invalid' }
  if (-not [IO.Path]::IsPathFullyQualified($ContextPath) -or -not (Test-Path -LiteralPath $ContextPath -PathType Leaf)) { throw 'Context is invalid' }
  $context = Read-Utf8 $ContextPath | ConvertFrom-Json -Depth 100
  if ([int]$context.schemaVersion -ne 1 -or [string]$context.taskId -cne $TaskId) { throw 'Context is invalid' }
  $cardPath = Join-Path $root "开发管理\任务卡\$TaskId.txt"
  $card = Read-Card $cardPath
  $meta = $card.Metadata
  if ([string]$meta.id -cne $TaskId) { throw 'Task identity is invalid' }
  $queuePath = Join-Path $root '开发管理\当前任务队列.txt'
  $queue = Get-Table -Path $queuePath -Header @('ID', '路由', '主责', '优先级', '领域', '阶段', '标题', '任务卡')
  $backlogPath = Join-Path $root ([string]$meta.sourceBacklog)
  $backlog = Get-Table -Path $backlogPath -Header @('ID', '优先级', '主责', '状态投影', '阻塞于', '摘要', '任务卡')
  $queueIndex = Find-RowIndex -Table $queue -Id $TaskId
  $backlogIndex = Find-RowIndex -Table $backlog -Id $TaskId
  if ($backlogIndex -lt 0) { throw 'Backlog row is missing' }

  switch ($Action) {
    'PauseDecision' {
      if ([string]$meta.dispatchState -cne 'ready' -or $queueIndex -lt 0 -or [string]$context.taskContextDigest -cne (Get-TaskContextDigest $meta)) { throw 'Pause precondition failed' }
      foreach ($name in @('sourceRunId', 'owner', 'route', 'decisionId', 'question', 'checkpointCommit', 'baseCommit', 'branch', 'createdAt')) { if ([string]::IsNullOrWhiteSpace([string]$context.$name)) { throw 'Checkpoint context is incomplete' } }
      $context | Add-Member -NotePropertyName queueIndex -NotePropertyValue $queueIndex -Force
      $meta.dispatchState = 'pending_decision'
      $meta.stateReason = "等待负责人决定：$([string]$context.question)"
      $meta | Add-Member -NotePropertyName automationCheckpoint -NotePropertyValue $context -Force
      if ($meta.PSObject.Properties.Name -contains 'automationReply') { $meta.PSObject.Properties.Remove('automationReply') }
      $queue.Rows.RemoveAt($queueIndex)
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
      $backlog.Rows[$backlogIndex][3] = '待决定'
    }
    'ResumeReady' {
      if ([string]$meta.dispatchState -cnotin @('pending_decision', 'waiting_reply') -or $queueIndex -ge 0 -or $meta.PSObject.Properties.Name -cnotcontains 'automationCheckpoint') { throw 'Resume precondition failed' }
      $checkpoint = $meta.automationCheckpoint
      if ([string]$checkpoint.decisionId -cne [string]$context.decisionId -or [string]$checkpoint.taskContextDigest -cne (Get-TaskContextDigest $meta)) { throw 'Reply binding is invalid' }
      foreach ($name in @('result', 'replyKind', 'replyValue', 'source', 'evidenceHash')) { if ([string]::IsNullOrWhiteSpace([string]$context.$name)) { throw 'Reply evidence is incomplete' } }
      $reply = [ordered]@{ decisionId = [string]$context.decisionId; result = [string]$context.result; replyKind = [string]$context.replyKind; replyValue = [string]$context.replyValue; source = [string]$context.source; evidenceHash = [string]$context.evidenceHash }
      $meta | Add-Member -NotePropertyName automationReply -NotePropertyValue $reply -Force
      $meta.dispatchState = 'ready'
      $meta.stateReason = "负责人回复已核验，恢复原任务：$([string]$context.decisionId)"
      $row = @([string]$meta.id, [string]$meta.route, [string]$meta.owner, [string]$meta.priority, [string]$meta.domain, [string]$meta.stage, [string]$meta.title, "开发管理/任务卡/$TaskId.txt")
      $insert = [Math]::Min([Math]::Max(0, [int]$checkpoint.queueIndex), $queue.Rows.Count)
      $queue.Rows.Insert($insert, $row)
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
      $backlog.Rows[$backlogIndex][3] = '已排队'
    }
    'Block' {
      if ([string]$meta.dispatchState -cne 'ready' -or $queueIndex -lt 0 -or [string]::IsNullOrWhiteSpace([string]$context.detailCode)) { throw 'Block precondition failed' }
      $meta.dispatchState = 'blocked'
      $meta.stateReason = "自动责任方确认阻塞：$([string]$context.detailCode)"
      $queue.Rows.RemoveAt($queueIndex)
      $backlog.Rows[$backlogIndex][3] = '阻塞'
    }
  }

  Write-Card -Path $cardPath -Metadata $meta -Body $card.Body
  Write-Table $queue
  Write-Table $backlog
  $checkArgs = @('-RepositoryRoot', $root, '-TaskId', $TaskId)
  if ($Action -cne 'ResumeReady') {
    $checkArgs += @('-Postcondition', 'CodexClosedOrNonReady')
  } elseif ([string]$meta.owner -ceq 'codex') {
    $checkArgs += @('-Postcondition', 'CodexDispatchReady', '-ExpectedRoute', [string]$meta.route)
  } else {
    $checkArgs += @('-Postcondition', 'ExternalDispatchReady', '-ExpectedOwner', [string]$meta.owner)
  }
  $checkArgs += '-OutputJson'
  $check = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\check-task-cards.ps1') @checkArgs 2>$null)
  if ($LASTEXITCODE -ne 0 -or $check.Count -ne 1) { throw 'Task projection validation failed' }
  [Console]::Out.WriteLine(([ordered]@{ status = 'updated'; taskId = $TaskId; dispatchState = [string]$meta.dispatchState; changedPaths = @("开发管理/任务卡/$TaskId.txt", '开发管理/当前任务队列.txt', [string]$meta.sourceBacklog) } | ConvertTo-Json -Compress))
} catch {
  [Console]::Out.WriteLine(([ordered]@{ status = 'failed'; taskId = $TaskId; detailCode = 'task_automation_state_failed' } | ConvertTo-Json -Compress))
  exit 1
}
