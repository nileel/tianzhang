#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$TaskCardRoot = '开发管理/任务卡',
  [string]$QueuePath = '开发管理/当前任务队列.txt',
  [string]$BacklogRoot = '开发管理/任务列表'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Read-Utf8Text {
  param([string]$Path)
  try {
    [Text.UTF8Encoding]::new($false, $true).GetString(
      [IO.File]::ReadAllBytes($Path)
    ).TrimStart([char]0xFEFF)
  } catch {
    throw "invalid UTF-8: $Path"
  }
}

function Normalize-Cell {
  param([string]$Value)
  $Value.Trim().Trim([char]96)
}

function Assert-RepositoryFilePath {
  param([string]$Value, [string]$Label)
  $invalid = [string]::IsNullOrWhiteSpace($Value) -or
    [IO.Path]::IsPathRooted($Value) -or
    $Value.Contains('\') -or
    $Value -match '[*?\[\]]' -or
    $Value.EndsWith('/') -or
    (@(($Value -split '/') | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0) -or
    [string]::IsNullOrEmpty([IO.Path]::GetExtension($Value))
  Assert-Contract (-not $invalid) "invalid repository-relative path in ${Label}: $Value"
}

function Get-TableRows {
  param([string]$Path, [string[]]$Header, [string]$Kind)
  $lines = @((Read-Utf8Text $Path) -split "`n")
  $headerIndex = -1
  for ($index = 0; $index -lt $lines.Count; $index++) {
    $line = $lines[$index].Trim()
    if (-not ($line.StartsWith('|') -and $line.EndsWith('|'))) { continue }
    $cells = @($line.Trim([char]'|').Split([char]'|') | ForEach-Object { Normalize-Cell $_ })
    if ($cells.Count -ne $Header.Count) { continue }
    $matches = $true
    for ($cellIndex = 0; $cellIndex -lt $Header.Count; $cellIndex++) {
      if ($cells[$cellIndex] -cne $Header[$cellIndex]) { $matches = $false; break }
    }
    if ($matches) { $headerIndex = $index; break }
  }
  Assert-Contract ($headerIndex -ge 0) "missing $Kind table header: $Path"

  $rows = @()
  for ($index = $headerIndex + 1; $index -lt $lines.Count; $index++) {
    $line = $lines[$index].Trim()
    if ([string]::IsNullOrWhiteSpace($line) -or -not $line.StartsWith('|')) { break }
    Assert-Contract ($line.EndsWith('|')) "$Kind row has wrong cell count: $Path"
    $cells = @($line.Trim([char]'|').Split([char]'|') | ForEach-Object { Normalize-Cell $_ })
    Assert-Contract ($cells.Count -eq $Header.Count) "$Kind row has wrong cell count: $Path"
    $rows += ,$cells
  }
  Write-Output -NoEnumerate $rows
}

function Get-Card {
  param([string]$Path)
  $text = Read-Utf8Text $Path
  $metaMarkers = [regex]::Matches($text, '(?m)^---TASK-META---\r?$')
  $bodyMarkers = [regex]::Matches($text, '(?m)^---TASK-BODY---\r?$')
  Assert-Contract ($metaMarkers.Count -eq 1) "metadata delimiter count invalid: $Path"
  Assert-Contract ($bodyMarkers.Count -eq 1) "body delimiter count invalid: $Path"
  Assert-Contract ($metaMarkers[0].Index -lt $bodyMarkers[0].Index) "metadata delimiter order invalid: $Path"

  $jsonText = $text.Substring($metaMarkers[0].Index + $metaMarkers[0].Length, $bodyMarkers[0].Index - ($metaMarkers[0].Index + $metaMarkers[0].Length)).Trim()
  try { $metadata = $jsonText | ConvertFrom-Json -Depth 100 } catch { throw "invalid JSON: $Path" }
  Assert-Contract ($null -ne $metadata) "invalid JSON: $Path"
  $required = @('schemaVersion', 'id', 'title', 'priority', 'route', 'owner', 'domain', 'stage', 'dispatchState', 'blockedBy', 'stateReason', 'expectedPaths', 'sourceBacklog')
  foreach ($field in $required) {
    Assert-Contract ($metadata.PSObject.Properties.Name -contains $field) "missing metadata field '$field': $Path"
  }

  Assert-Contract ($metadata.schemaVersion -eq 1) "illegal schemaVersion: $Path"
  $routes = @('codex_execute', 'external_execute', 'codex_review')
  $owners = @('codex', 'deepseek', 'claude')
  $domains = @('unity', 'battlesim', 'data', 'content', 'management', 'automation')
  $stages = @('discovery', 'decision', 'design', 'implementation', 'migration', 'verification')
  $states = @('ready', 'blocked', 'frozen', 'pending_decision', 'waiting_reply', 'completed')
  Assert-Contract ($routes -ccontains $metadata.route) "invalid route: $Path"
  Assert-Contract ($owners -ccontains $metadata.owner) "invalid owner: $Path"
  Assert-Contract ($domains -ccontains $metadata.domain) "invalid domain: $Path"
  Assert-Contract ($stages -ccontains $metadata.stage) "invalid stage: $Path"
  Assert-Contract ($states -ccontains $metadata.dispatchState) "invalid dispatch state: $Path"
  Assert-Contract ($metadata.priority -cmatch '^P[0-3]$') "invalid priority: $Path"
  Assert-Contract (($metadata.expectedPaths -is [System.Collections.IEnumerable]) -and -not ($metadata.expectedPaths -is [string])) "invalid expectedPaths: $Path"
  foreach ($expectedPath in $metadata.expectedPaths) { Assert-RepositoryFilePath ([string]$expectedPath) 'expectedPaths' }
  Assert-RepositoryFilePath ([string]$metadata.sourceBacklog) 'sourceBacklog'
  Assert-Contract ((($metadata.route -in @('codex_execute', 'codex_review')) -and $metadata.owner -ceq 'codex') -or ($metadata.route -ceq 'external_execute' -and $metadata.owner -in @('deepseek', 'claude'))) "route/owner mismatch: $Path"
  Assert-Contract (($null -eq $metadata.blockedBy) -or (($metadata.blockedBy -is [System.Collections.IEnumerable]) -and -not ($metadata.blockedBy -is [string]))) "invalid blockedBy: $Path"
  Assert-Contract ($metadata.dispatchState -cne 'completed') "completed card in active task-card directory: $Path"

  $body = $text.Substring($bodyMarkers[0].Index + $bodyMarkers[0].Length)
  $h1 = "# $($metadata.id) · $($metadata.title)"
  Assert-Contract ([regex]::IsMatch($body, '(?m)^' + [regex]::Escape($h1) + '\r?$')) "H1 mismatch: $Path"
  foreach ($heading in @('来源与当前边界', '必查范围', '实施范围', '禁止项', '验证', '完成条件', '停止条件')) {
    Assert-Contract ([regex]::IsMatch($body, '(?m)^' + [regex]::Escape("## $heading") + '\r?$')) "missing body heading '$heading': $Path"
  }
  [pscustomobject]@{ Metadata = $metadata; Path = $Path }
}

try {
  $repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
  $taskCardPath = Join-Path $repositoryPath $TaskCardRoot
  Assert-Contract (Test-Path -LiteralPath $taskCardPath -PathType Container) "missing task-card directory: $taskCardPath"
  $cards = @()
  $cardById = @{}
  foreach ($file in Get-ChildItem -LiteralPath $taskCardPath -Filter '*.txt' -File) {
    $card = Get-Card $file.FullName
    $id = [string]$card.Metadata.id
    Assert-Contract (-not $cardById.ContainsKey($id)) "duplicate card ID: $id"
    $cardById[$id] = $card
    $cards += $card
  }
  foreach ($card in $cards) {
    $id = [string]$card.Metadata.id
    Assert-Contract ((Split-Path -Leaf $card.Path) -ceq "$id.txt") "filename/id mismatch: $($card.Path)"
  }

  $queueFile = Join-Path $repositoryPath $QueuePath
  Assert-Contract (Test-Path -LiteralPath $queueFile -PathType Leaf) "missing queue file: $queueFile"
  $queueRows = Get-TableRows $queueFile @('ID', '路由', '主责', '优先级', '领域', '阶段', '标题', '任务卡') 'queue'
  $queueIds = @{}
  foreach ($row in $queueRows) {
    $id = $row[0]
    Assert-Contract (-not $queueIds.ContainsKey($id)) "duplicate queue ID: $id"
    Assert-Contract ($cardById.ContainsKey($id)) "queue references missing card: $id"
    $queueIds[$id] = $true
    $card = $cardById[$id]
    $metadata = $card.Metadata
    Assert-Contract ($row[7] -ceq "开发管理/任务卡/$id.txt") "queue card path mismatch: $id"
    Assert-Contract ($metadata.dispatchState -ceq 'ready') "non-ready card in queue: $id"
    $expected = @($metadata.route, $metadata.owner, $metadata.priority, $metadata.domain, $metadata.stage, $metadata.title)
    for ($index = 0; $index -lt $expected.Count; $index++) {
      Assert-Contract ($row[$index + 1] -ceq $expected[$index]) "queue projection mismatch: $id"
    }
  }
  $readyCards = @($cards | Where-Object { $_.Metadata.dispatchState -ceq 'ready' })
  foreach ($card in $readyCards) {
    Assert-Contract ($queueIds.ContainsKey([string]$card.Metadata.id)) "ready card missing from queue: $($card.Metadata.id)"
  }

  $backlogPrefix = ($BacklogRoot.TrimEnd('/') + '/')
  $backlogTables = @{}
  foreach ($card in $cards) {
    $metadata = $card.Metadata
    $source = [string]$metadata.sourceBacklog
    Assert-Contract ($source.StartsWith($backlogPrefix, [StringComparison]::Ordinal)) "sourceBacklog outside backlog root: $source"
    $sourcePath = Join-Path $repositoryPath $source
    Assert-Contract (Test-Path -LiteralPath $sourcePath -PathType Leaf) "missing sourceBacklog: $source"
    if (-not $backlogTables.ContainsKey($sourcePath)) {
      $backlogTables[$sourcePath] = Get-TableRows $sourcePath @('ID', '优先级', '主责', '状态投影', '阻塞于', '摘要', '任务卡') 'backlog'
    }
    $rows = @($backlogTables[$sourcePath] | Where-Object { $_[0] -ceq $metadata.id -and $_[6] -ceq "开发管理/任务卡/$($metadata.id).txt" })
    Assert-Contract ($rows.Count -eq 1) "missing backlog row: $($metadata.id)"
    $row = $rows[0]
    $projection = @{ ready = '已排队'; blocked = '阻塞'; frozen = '冻结'; pending_decision = '待决定'; waiting_reply = '等待回复' }
    Assert-Contract ($row[1] -ceq $metadata.priority -and $row[2] -ceq $metadata.owner -and $row[3] -ceq $projection[$metadata.dispatchState] -and $row[5] -ceq $metadata.title) "backlog projection mismatch: $($metadata.id)"
    $blockedBy = @($metadata.blockedBy | ForEach-Object { [string]$_ })
    $expectedBlockers = if ($blockedBy.Count) { $blockedBy -join '、' } else { '—' }
    Assert-Contract ($row[4] -ceq $expectedBlockers) "backlog blocker mismatch: $($metadata.id)"
  }

  $visitState = @{}
  function Visit-Card {
    param([string]$Id)
    $state = if ($visitState.ContainsKey($Id)) { $visitState[$Id] } else { 0 }
    if ($state -eq 1) { throw "dependency cycle: $Id" }
    if ($state -eq 2) { return }
    $visitState[$Id] = 1
    foreach ($dependency in $cardById[$Id].Metadata.blockedBy) {
      $dependencyId = [string]$dependency
      if ($dependencyId -ceq $Id) { throw "self-dependency: $Id" }
      if ($cardById.ContainsKey($dependencyId)) { Visit-Card $dependencyId }
    }
    $visitState[$Id] = 2
  }
  foreach ($card in $cards) { Visit-Card ([string]$card.Metadata.id) }

  Write-Output "check-task-cards: OK (cards=$($cards.Count) ready=$($readyCards.Count))"
} catch {
  [Console]::Error.WriteLine("check-task-cards: $($_.Exception.Message)")
  exit 1
}
