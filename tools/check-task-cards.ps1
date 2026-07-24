#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$TaskCardRoot = '开发管理/任务卡',
  [string]$QueuePath = '开发管理/当前任务队列.txt',
  [string]$BacklogRoot = '开发管理/任务列表',
  [string]$TaskId,
  [ValidateSet('CodexDispatchReady', 'CodexClosedOrNonReady', 'ExternalPendingReview')]
  [string]$Postcondition,
  [ValidateSet('codex_execute', 'codex_review')]
  [string]$ExpectedRoute,
  [switch]$OutputJson
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

function Assert-RepositoryRelativePath {
  param([string]$Value, [string]$Label)
  $invalid = [string]::IsNullOrWhiteSpace($Value) -or
    $Value -cne $Value.Trim() -or
    [IO.Path]::IsPathRooted($Value) -or
    $Value.Contains('\') -or
    $Value -match '[*?\[\]]' -or
    $Value.EndsWith('/') -or
    (@(($Value -split '/') | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0)
  Assert-Contract (-not $invalid) "invalid repository-relative path in ${Label}: $Value"
}

function Assert-RepositoryFilePath {
  param([string]$Value, [string]$Label)
  Assert-RepositoryRelativePath $Value $Label
  Assert-Contract (-not [string]::IsNullOrEmpty([IO.Path]::GetExtension($Value))) "invalid repository-relative path in ${Label}: $Value"
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

  $separatorIndex = $headerIndex + 1
  Assert-Contract ($separatorIndex -lt $lines.Count) "missing $Kind table separator: $Path"
  $separatorLine = $lines[$separatorIndex].Trim()
  Assert-Contract ($separatorLine.StartsWith('|') -and $separatorLine.EndsWith('|')) "invalid $Kind table separator: $Path"
  $separatorCells = @($separatorLine.Trim([char]'|').Split([char]'|') | ForEach-Object { Normalize-Cell $_ })
  Assert-Contract ($separatorCells.Count -eq $Header.Count -and (@($separatorCells | Where-Object { $_ -notmatch '^:?-{3,}:?$' }).Count -eq 0)) "invalid $Kind table separator: $Path"

  $rows = @()
  for ($index = $separatorIndex + 1; $index -lt $lines.Count; $index++) {
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
  param([string]$Path, [switch]$AllowCompleted)
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
  if (-not $AllowCompleted) {
    Assert-Contract ($metadata.dispatchState -cne 'completed') "completed card in active task-card directory: $Path"
  }

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
  Assert-RepositoryRelativePath $TaskCardRoot 'TaskCardRoot'
  Assert-RepositoryRelativePath $QueuePath 'QueuePath'
  Assert-RepositoryRelativePath $BacklogRoot 'BacklogRoot'
  $hasTaskId = -not [string]::IsNullOrWhiteSpace($TaskId)
  $hasPostcondition = -not [string]::IsNullOrWhiteSpace($Postcondition)
  $hasExpectedRoute = -not [string]::IsNullOrWhiteSpace($ExpectedRoute)
  $taskState = $null
  Assert-Contract ($hasTaskId -eq $hasPostcondition) 'TaskId and Postcondition must be provided together'
  if ($Postcondition -ceq 'CodexDispatchReady') {
    Assert-Contract $hasExpectedRoute 'ExpectedRoute is required for CodexDispatchReady'
  } else {
    Assert-Contract (-not $hasExpectedRoute) 'ExpectedRoute is only valid for CodexDispatchReady'
  }
  if ($hasTaskId) {
    Assert-Contract (
      $TaskId -ceq $TaskId.Trim() -and
      $TaskId -ceq [IO.Path]::GetFileName($TaskId) -and
      $TaskId -cnotin @('.', '..')
    ) "invalid TaskId: $TaskId"
  }
  $taskCardPath = Join-Path $repositoryPath $TaskCardRoot
  Assert-Contract (Test-Path -LiteralPath $taskCardPath -PathType Container) "missing task-card directory: $taskCardPath"
  $cards = @()
  $cardById = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
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
    $expectedPaths = @($card.Metadata.expectedPaths | ForEach-Object { [string]$_ })
    Assert-Contract ($expectedPaths -ccontains "开发管理/任务卡/$id.txt") "missing exact active-card authorization: $id"
    Assert-Contract ($expectedPaths -ccontains "开发管理/任务归档/$id.txt") "missing exact archive authorization: $id"
  }

  $queueFile = Join-Path $repositoryPath $QueuePath
  Assert-Contract (Test-Path -LiteralPath $queueFile -PathType Leaf) "missing queue file: $queueFile"
  $queueRows = Get-TableRows $queueFile @('ID', '路由', '主责', '优先级', '领域', '阶段', '标题', '任务卡') 'queue'
  $queueIds = [Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::Ordinal)
  foreach ($row in $queueRows) {
    $id = $row[0]
    Assert-Contract (-not $queueIds.ContainsKey($id)) "duplicate queue ID: $id"
    $caseInsensitiveCards = @($cards | Where-Object {
      [string]::Equals([string]$_.Metadata.id, $id, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($caseInsensitiveCards.Count -eq 1) {
      Assert-Contract ([string]$caseInsensitiveCards[0].Metadata.id -ceq $id) "queue ID case mismatch: $id"
    }
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

  $visitState = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
  function Visit-Card {
    param([string]$Id)
    $state = if ($visitState.ContainsKey($Id)) { $visitState[$Id] } else { 0 }
    if ($state -eq 1) { throw "dependency cycle: $Id" }
    if ($state -eq 2) { return }
    $visitState[$Id] = 1
    foreach ($dependency in $cardById[$Id].Metadata.blockedBy) {
      $dependencyId = [string]$dependency
      if ($dependencyId -ceq $Id) { throw "self-dependency: $Id" }
      if (-not $cardById.ContainsKey($dependencyId)) {
        $caseInsensitiveDependencies = @($cards | Where-Object {
          [string]::Equals([string]$_.Metadata.id, $dependencyId, [StringComparison]::OrdinalIgnoreCase)
        })
        Assert-Contract ($caseInsensitiveDependencies.Count -eq 0) "dependency ID case mismatch: $dependencyId"
      }
      if ($cardById.ContainsKey($dependencyId)) { Visit-Card $dependencyId }
    }
    $visitState[$Id] = 2
  }
  foreach ($card in $cards) { Visit-Card ([string]$card.Metadata.id) }

  if ($hasPostcondition -and -not $cardById.ContainsKey($TaskId)) {
    $caseInsensitiveTaskIds = @($cards | Where-Object {
      [string]::Equals([string]$_.Metadata.id, $TaskId, [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-Contract ($caseInsensitiveTaskIds.Count -eq 0) "TaskId case mismatch: $TaskId"
  }

  if ($Postcondition -ceq 'CodexDispatchReady') {
    $postconditionSatisfied = $false
    if ($cardById.ContainsKey($TaskId)) {
      $metadata = $cardById[$TaskId].Metadata
      $postconditionSatisfied =
        [string]$metadata.route -ceq $ExpectedRoute -and
        [string]$metadata.owner -ceq 'codex' -and
        [string]$metadata.dispatchState -ceq 'ready'
      if ($postconditionSatisfied) {
        $taskState = [string]$metadata.dispatchState
      }
    }
    Assert-Contract $postconditionSatisfied "CodexDispatchReady requires route=$ExpectedRoute owner=codex dispatchState=ready: $TaskId"
  }
  if ($Postcondition -ceq 'CodexClosedOrNonReady') {
    $postconditionSatisfied = $false
    if ($cardById.ContainsKey($TaskId)) {
      $activeState = [string]$cardById[$TaskId].Metadata.dispatchState
      $postconditionSatisfied = @('blocked', 'frozen', 'pending_decision', 'waiting_reply') -ccontains $activeState
      if ($postconditionSatisfied) {
        $taskState = $activeState
      }
    } else {
      $archiveRelativePath = "开发管理/任务归档/$TaskId.txt"
      $archivePath = Join-Path $repositoryPath $archiveRelativePath
      if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        $archiveFile = Get-Item -LiteralPath $archivePath
        Assert-Contract ($archiveFile.Name -ceq "$TaskId.txt") "TaskId case mismatch: $TaskId"
        $archiveCard = Get-Card -Path $archiveFile.FullName -AllowCompleted
        Assert-Contract ([string]$archiveCard.Metadata.id -ceq $TaskId) "archive ID mismatch: $TaskId"
        Assert-Contract ([string]$archiveCard.Metadata.dispatchState -ceq 'completed') "archive is not completed: $TaskId"
        $archiveExpectedPaths = @($archiveCard.Metadata.expectedPaths | ForEach-Object { [string]$_ })
        Assert-Contract ($archiveExpectedPaths -ccontains "开发管理/任务卡/$TaskId.txt") "missing exact active-card authorization: $TaskId"
        Assert-Contract ($archiveExpectedPaths -ccontains $archiveRelativePath) "missing exact archive authorization: $TaskId"
        $backlogPath = Join-Path $repositoryPath $BacklogRoot
        foreach ($backlogFile in Get-ChildItem -LiteralPath $backlogPath -Filter '*.txt' -File) {
          $rows = Get-TableRows $backlogFile.FullName @('ID', '优先级', '主责', '状态投影', '阻塞于', '摘要', '任务卡') 'backlog'
          Assert-Contract (@($rows | Where-Object { $_[0] -ceq $TaskId }).Count -eq 0) "archived TaskId remains in backlog: $TaskId"
        }
        $postconditionSatisfied = $true
        $taskState = 'completed'
      }
    }
    Assert-Contract $postconditionSatisfied "CodexClosedOrNonReady requires a non-ready active card or exact completed archive: $TaskId"
  }
  if ($Postcondition -ceq 'ExternalPendingReview') {
    $postconditionSatisfied = $false
    if ($cardById.ContainsKey($TaskId)) {
      $metadata = $cardById[$TaskId].Metadata
      $postconditionSatisfied =
        [string]$metadata.route -ceq 'codex_review' -and
        [string]$metadata.owner -ceq 'codex' -and
        [string]$metadata.dispatchState -ceq 'ready'
      if ($postconditionSatisfied) {
        $taskState = [string]$metadata.dispatchState
      }
    }
    Assert-Contract $postconditionSatisfied "ExternalPendingReview requires route=codex_review owner=codex dispatchState=ready: $TaskId"
  }

  if ($OutputJson) {
    [Console]::Out.WriteLine(([ordered]@{
      status = 'ok'
      cardCount = $cards.Count
      readyCount = $readyCards.Count
      taskId = if ($hasTaskId) { $TaskId } else { $null }
      taskState = $taskState
      postcondition = if ($hasPostcondition) { $Postcondition } else { $null }
    } | ConvertTo-Json -Compress))
  } else {
    Write-Output "check-task-cards: OK (cards=$($cards.Count) ready=$($readyCards.Count))"
  }
} catch {
  [Console]::Error.WriteLine("check-task-cards: $($_.Exception.Message)")
  exit 1
}
