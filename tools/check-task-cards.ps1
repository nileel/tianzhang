#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$TaskCardRoot = '开发管理/任务卡',
  [string]$QueuePath = '开发管理/当前任务队列.txt',
  [string]$BacklogRoot = '开发管理/任务列表',
  [string]$TaskId,
  [ValidateSet('CodexDispatchReady', 'ExternalDispatchReady', 'CodexClosedOrNonReady', 'ExternalPendingReview', 'MaintenancePendingDecision', 'MaintenanceResolvedReady', 'MaintenanceResolvedBlocked', 'MaintenanceExpiredBlocked')]
  [string]$Postcondition,
  [ValidateSet('codex_execute', 'codex_review')]
  [string]$ExpectedRoute,
  [ValidateSet('deepseek', 'claude')]
  [string]$ExpectedOwner,
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

function Assert-AutomationInputs {
  param([object]$Metadata, [string]$Path)
  if ($Metadata.PSObject.Properties.Name -cnotcontains 'automationInputs') { return }
  $inputs = $Metadata.automationInputs
  Assert-Contract ([string]$Metadata.route -ceq 'codex_execute' -and [string]$Metadata.owner -ceq 'codex') "automationInputs requires route=codex_execute owner=codex: $Path"
  Assert-Contract (($inputs -is [System.Collections.IEnumerable]) -and -not ($inputs -is [string])) "invalid automationInputs: $Path"
  $items = @($inputs)
  Assert-Contract ($items.Count -gt 0) "invalid automationInputs: $Path"
  $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($input in $items) {
    Assert-Contract ($null -ne $input) "invalid automationInputs: $Path"
    $names = @($input.PSObject.Properties.Name)
    Assert-Contract ($names.Count -eq 3 -and @($names | Where-Object { $_ -cnotin @('path', 'bytes', 'sha256') }).Count -eq 0 -and @(@('path', 'bytes', 'sha256') | Where-Object { $names -cnotcontains $_ }).Count -eq 0) "invalid automationInputs: $Path"
    $inputPath = [string]$input.path
    Assert-RepositoryFilePath $inputPath 'automationInputs.path'
    Assert-Contract ($inputPath.StartsWith('assets/source/', [StringComparison]::Ordinal)) "automationInputs path must be under assets/source/: $Path"
    Assert-Contract ($paths.Add($inputPath)) "duplicate automationInputs path: $Path"
    $rawBytes = $input.bytes
    $isIntegral = $rawBytes -is [byte] -or $rawBytes -is [sbyte] -or $rawBytes -is [int16] -or $rawBytes -is [uint16] -or
      $rawBytes -is [int32] -or $rawBytes -is [uint32] -or $rawBytes -is [int64] -or $rawBytes -is [uint64]
    Assert-Contract $isIntegral "invalid automationInputs bytes: $Path"
    try { $bytes = [Convert]::ToInt64($rawBytes) } catch { throw "invalid automationInputs bytes: $Path" }
    Assert-Contract ($bytes -gt 0) "invalid automationInputs bytes: $Path"
    Assert-Contract ([string]$input.sha256 -cmatch '^[0-9A-Fa-f]{64}$') "invalid automationInputs sha256: $Path"
  }
}

function Test-ObjectField {
  param([object]$Value, [string]$Name)
  $null -ne $Value -and $Value.PSObject.Properties.Name -ccontains $Name
}

function Test-ArrayEquals {
  param([object[]]$Left, [object[]]$Right)
  if ($Left.Count -ne $Right.Count) { return $false }
  for ($index = 0; $index -lt $Left.Count; $index++) {
    if ([string]$Left[$index] -cne [string]$Right[$index]) { return $false }
  }
  return $true
}

function Assert-RiskPreflight {
  param([object]$Metadata, [string]$Path)
  $schemaVersion = [int]$Metadata.schemaVersion
  $hasRisk = Test-ObjectField $Metadata 'riskPreflight'
  if ($schemaVersion -eq 1) {
    Assert-Contract (-not $hasRisk) "schema 1 must not include riskPreflight: $Path"
    return
  }
  Assert-Contract $hasRisk "schema 2 requires riskPreflight: $Path"
  $risk = $Metadata.riskPreflight
  Assert-Contract ($risk -is [System.Management.Automation.PSCustomObject]) "schema 2 riskPreflight must be an object: $Path"
  $riskNames = @($risk.PSObject.Properties.Name)
  Assert-Contract (
    (@($riskNames | Where-Object { $_ -cnotin @('explicitRefs', 'matched', 'gates') }).Count -eq 0) -and
    (@(@('explicitRefs', 'matched', 'gates') | Where-Object { $riskNames -cnotcontains $_ }).Count -eq 0)
  ) "schema 2 riskPreflight field set must be exactly explicitRefs/matched/gates: $Path"
  foreach ($field in @('explicitRefs', 'matched', 'gates')) {
    Assert-Contract ($risk.$field -is [System.Array]) "schema 2 riskPreflight '$field' must be an array: $Path"
  }
}

function Assert-Schema2Projection {
  param([string]$RepositoryRoot, [string]$MatcherScript, [object]$Metadata, [string]$Path)
  $id = [string]$Metadata.id
  $output = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $MatcherScript -RepositoryRoot $RepositoryRoot -TaskId $id 2>&1 | Out-String)
  if ($LASTEXITCODE -ne 0) { throw "risk preflight failed for $id" }
  try { $result = $output | ConvertFrom-Json } catch { throw "risk preflight output invalid for $id" }
  Assert-Contract ($null -ne $result -and [string]$result.status -ceq 'ok') "risk preflight status must be ok for $id"
  $projection = $Metadata.riskPreflight
  $recomputedMatched = @($result.matched | ForEach-Object { [string]$_ })
  $recomputedGates = @($result.gates | ForEach-Object { [string]$_ })
  $projectedMatched = @($projection.matched | ForEach-Object { [string]$_ })
  $projectedGates = @($projection.gates | ForEach-Object { [string]$_ })
  Assert-Contract (Test-ArrayEquals $recomputedMatched $projectedMatched) "riskPreflight matched projection mismatch: $id"
  Assert-Contract (Test-ArrayEquals $recomputedGates $projectedGates) "riskPreflight gates projection mismatch: $id"
  foreach ($ref in @($projection.explicitRefs | ForEach-Object { [string]$_ })) {
    Assert-Contract ($projectedMatched -ccontains $ref) "riskPreflight explicitRef not in matched: $id"
  }
}

function Assert-AutomationDecision {
  param([object]$Metadata, [string]$Path)
  $decision = $Metadata.automationDecision
  foreach ($field in @('schemaVersion', 'kind', 'status', 'decisionId', 'taskId', 'question', 'options', 'recommendedOption', 'impactSummary', 'plainSummary', 'allowCustomReply', 'sourceCommit', 'sourceTaskDigest', 'taskContextDigest', 'queueIndex', 'createdAt')) {
    Assert-Contract (Test-ObjectField $decision $field) "missing automationDecision field '$field': $Path"
  }
  Assert-Contract ([int]$decision.schemaVersion -eq 1 -and [string]$decision.kind -ceq 'queue_maintenance') "invalid automationDecision kind: $Path"
  Assert-Contract ([string]$decision.decisionId -cmatch '^DEC-[0-9]{8}-QM[0-9A-F]{12}$') "invalid automationDecision decisionId: $Path"
  Assert-Contract ([string]$decision.taskId -ceq [string]$Metadata.id) "automationDecision task mismatch: $Path"
  Assert-Contract ([string]$decision.sourceCommit -cmatch '^[0-9a-f]{40,64}$') "invalid automationDecision sourceCommit: $Path"
  foreach ($field in @('sourceTaskDigest', 'taskContextDigest')) {
    Assert-Contract ([string]$decision.$field -cmatch '^[0-9a-f]{64}$') "invalid automationDecision ${field}: $Path"
  }
  Assert-Contract ([int]$decision.queueIndex -eq 0) "invalid automationDecision queueIndex: $Path"
  Assert-Contract ($decision.allowCustomReply -is [bool] -and $decision.allowCustomReply -eq $false) "automationDecision custom reply must be disabled: $Path"
  $options = @($decision.options)
  Assert-Contract ($options.Count -eq 3) "automationDecision requires exactly three options: $Path"
  $expectedKeys = @('A', 'B', 'C')
  $expectedTargets = @('ready', 'ready', 'blocked')
  for ($index = 0; $index -lt 3; $index++) {
    foreach ($field in @('key', 'label', 'targetState')) { Assert-Contract (Test-ObjectField $options[$index] $field) "missing automationDecision option field '$field': $Path" }
    Assert-Contract ([string]$options[$index].key -ceq $expectedKeys[$index] -and [string]$options[$index].targetState -ceq $expectedTargets[$index] -and -not [string]::IsNullOrWhiteSpace([string]$options[$index].label)) "invalid automationDecision option: $Path"
  }
  Assert-Contract ([string]$decision.recommendedOption -cin @('A', 'B')) "invalid automationDecision recommendation: $Path"
  foreach ($value in @([string]$decision.question, [string]$decision.impactSummary, [string]$decision.plainSummary.situation, [string]$decision.plainSummary.impact, [string]$decision.plainSummary.action, [string]$decision.createdAt)) {
    Assert-Contract (-not [string]::IsNullOrWhiteSpace($value)) "incomplete automationDecision summary: $Path"
  }
  switch ([string]$decision.status) {
    'awaiting_reply' {
      Assert-Contract ([string]$Metadata.dispatchState -ceq 'pending_decision') "awaiting maintenance decision must be pending_decision: $Path"
    }
    'resolved' {
      foreach ($field in @('optionKey', 'targetState', 'replySource', 'replyEvidenceHash', 'resolvedAt')) { Assert-Contract (Test-ObjectField $decision $field) "missing resolved automationDecision field '$field': $Path" }
      Assert-Contract ([string]$decision.optionKey -cin @('A', 'B', 'C')) "invalid resolved automationDecision option: $Path"
      $selected = @($options | Where-Object { [string]$_.key -ceq [string]$decision.optionKey })
      Assert-Contract ($selected.Count -eq 1 -and [string]$selected[0].targetState -ceq [string]$decision.targetState -and [string]$Metadata.dispatchState -ceq [string]$decision.targetState) "resolved automationDecision target mismatch: $Path"
      Assert-Contract ([string]$decision.replySource -ceq 'feishu_card' -and [string]$decision.replyEvidenceHash -cmatch '^[0-9a-f]{64}$' -and -not [string]::IsNullOrWhiteSpace([string]$decision.resolvedAt)) "invalid resolved automationDecision evidence: $Path"
    }
    { $_ -cin @('expired', 'attention_required') } {
      foreach ($field in @('detailCode', 'terminatedAt')) { Assert-Contract (Test-ObjectField $decision $field) "missing terminated automationDecision field '$field': $Path" }
      Assert-Contract ([string]$Metadata.dispatchState -ceq 'blocked' -and -not [string]::IsNullOrWhiteSpace([string]$decision.detailCode) -and -not [string]::IsNullOrWhiteSpace([string]$decision.terminatedAt)) "invalid terminated automationDecision: $Path"
    }
    default { throw "invalid automationDecision status: $Path" }
  }
}

function Assert-AutomationCheckpoint {
  param([object]$Metadata, [string]$Path)
  $checkpoint = $Metadata.automationCheckpoint
  foreach ($field in @('schemaVersion', 'taskId', 'sourceRunId', 'owner', 'route', 'decisionId', 'question', 'options', 'recommendedOption', 'impactSummary', 'plainSummary', 'checkpointCommit', 'baseCommit', 'branch', 'changedPaths', 'verified', 'unverified', 'residualRisk', 'taskContextDigest', 'createdAt', 'queueIndex')) {
    Assert-Contract (Test-ObjectField $checkpoint $field) "missing automationCheckpoint field '$field': $Path"
  }
  Assert-Contract ([int]$checkpoint.schemaVersion -eq 1 -and [string]$checkpoint.taskId -ceq [string]$Metadata.id -and [string]$checkpoint.owner -ceq [string]$Metadata.owner -and [string]$checkpoint.route -ceq [string]$Metadata.route) "invalid automationCheckpoint binding: $Path"
  Assert-Contract ([string]$checkpoint.decisionId -cmatch '^DEC-[0-9]{8}-[A-Z0-9]+$') "invalid automationCheckpoint decisionId: $Path"
  Assert-Contract ([string]$checkpoint.checkpointCommit -cmatch '^[0-9a-f]{40,64}$' -and [string]$checkpoint.baseCommit -cmatch '^[0-9a-f]{40,64}$' -and [string]$checkpoint.taskContextDigest -cmatch '^[0-9a-f]{64}$') "invalid automationCheckpoint evidence: $Path"
  $options = @($checkpoint.options)
  Assert-Contract ($options.Count -eq 3 -and (@($options | ForEach-Object { [string]$_.key }) -join '') -ceq 'ABC') "invalid automationCheckpoint options: $Path"
  Assert-Contract ([int]$checkpoint.queueIndex -ge 0) "invalid automationCheckpoint queueIndex: $Path"
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

  Assert-Contract ([int]$metadata.schemaVersion -in @(1, 2)) "illegal schemaVersion: $Path"
  Assert-RiskPreflight -Metadata $metadata -Path $Path
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
  Assert-AutomationInputs -Metadata $metadata -Path $Path
  $hasCheckpoint = Test-ObjectField $metadata 'automationCheckpoint'
  $hasDecision = Test-ObjectField $metadata 'automationDecision'
  Assert-Contract (-not ($hasCheckpoint -and $hasDecision)) "automationCheckpoint and automationDecision are mutually exclusive: $Path"
  if ([string]$metadata.dispatchState -cin @('pending_decision', 'waiting_reply')) {
    Assert-Contract ($hasCheckpoint -xor $hasDecision) "decision state requires exactly one automation projection: $Path"
  }
  if ($hasCheckpoint) { Assert-AutomationCheckpoint -Metadata $metadata -Path $Path }
  if ($hasDecision) { Assert-AutomationDecision -Metadata $metadata -Path $Path }
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
  $hasExpectedOwner = -not [string]::IsNullOrWhiteSpace($ExpectedOwner)
  $taskState = $null
  $taskExpectedPaths = $null
  Assert-Contract ($hasTaskId -eq $hasPostcondition) 'TaskId and Postcondition must be provided together'
  if ($Postcondition -ceq 'CodexDispatchReady') {
    Assert-Contract $hasExpectedRoute 'ExpectedRoute is required for CodexDispatchReady'
  } else {
    Assert-Contract (-not $hasExpectedRoute) 'ExpectedRoute is only valid for CodexDispatchReady'
  }
  if ($Postcondition -ceq 'ExternalDispatchReady') {
    Assert-Contract $hasExpectedOwner 'ExpectedOwner is required for ExternalDispatchReady'
  } else {
    Assert-Contract (-not $hasExpectedOwner) 'ExpectedOwner is only valid for ExternalDispatchReady'
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
    if ([string]$card.Metadata.route -ceq 'codex_review') {
      Assert-Contract ($expectedPaths -ccontains '开发管理/未通过审核清单.txt') "missing review-list authorization: $id"
    }
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

  $matcherScript = Join-Path $PSScriptRoot 'get-experience-risk-preflight.ps1'
  Assert-Contract (Test-Path -LiteralPath $matcherScript -PathType Leaf) 'missing risk preflight matcher'
  foreach ($card in $cards) {
    if ([int]$card.Metadata.schemaVersion -ne 2 -or [string]$card.Metadata.dispatchState -cne 'ready') { continue }
    Assert-Schema2Projection -RepositoryRoot $repositoryPath -MatcherScript $matcherScript -Metadata $card.Metadata -Path $card.Path
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
  if ($Postcondition -ceq 'ExternalDispatchReady') {
    $postconditionSatisfied = $false
    if ($cardById.ContainsKey($TaskId)) {
      $metadata = $cardById[$TaskId].Metadata
      $postconditionSatisfied =
        [string]$metadata.route -ceq 'external_execute' -and
        [string]$metadata.owner -ceq $ExpectedOwner -and
        [string]$metadata.dispatchState -ceq 'ready'
      if ($postconditionSatisfied) {
        $taskState = [string]$metadata.dispatchState
        $taskExpectedPaths = @($metadata.expectedPaths | ForEach-Object { [string]$_ })
      }
    }
    Assert-Contract $postconditionSatisfied "ExternalDispatchReady requires route=external_execute owner=$ExpectedOwner dispatchState=ready: $TaskId"
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
  if ($Postcondition -clike 'Maintenance*') {
    Assert-Contract ($cardById.ContainsKey($TaskId)) "$Postcondition requires an active task card: $TaskId"
    $metadata = $cardById[$TaskId].Metadata
    Assert-Contract (Test-ObjectField $metadata 'automationDecision') "$Postcondition requires automationDecision: $TaskId"
    $decision = $metadata.automationDecision
    $expected = switch ($Postcondition) {
      'MaintenancePendingDecision' { @('pending_decision', 'awaiting_reply') }
      'MaintenanceResolvedReady' { @('ready', 'resolved') }
      'MaintenanceResolvedBlocked' { @('blocked', 'resolved') }
      'MaintenanceExpiredBlocked' { @('blocked', 'expired') }
    }
    Assert-Contract ([string]$metadata.dispatchState -ceq $expected[0] -and [string]$decision.status -ceq $expected[1]) "$Postcondition mismatch: $TaskId"
    $taskState = [string]$metadata.dispatchState
  }

  if ($OutputJson) {
    [Console]::Out.WriteLine(([ordered]@{
      status = 'ok'
      cardCount = $cards.Count
      readyCount = $readyCards.Count
      taskId = if ($hasTaskId) { $TaskId } else { $null }
      taskState = $taskState
      postcondition = if ($hasPostcondition) { $Postcondition } else { $null }
      expectedPaths = $taskExpectedPaths
    } | ConvertTo-Json -Compress))
  } else {
    Write-Output "check-task-cards: OK (cards=$($cards.Count) ready=$($readyCards.Count))"
  }
} catch {
  [Console]::Error.WriteLine("check-task-cards: $($_.Exception.Message)")
  exit 1
}
