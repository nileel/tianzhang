#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$TaskId
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Utf8Text { param([string]$Path) [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF) }
function Write-Utf8Text { param([string]$Path, [string]$Text) [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Normalize-Cell { param([string]$Value) $Value.Trim().Trim([char]96) }

function Update-TableRow {
  param(
    [string]$Path,
    [string[]]$Header,
    [string]$ExpectedId,
    [scriptblock]$Transform
  )

  $text = Read-Utf8Text -Path $Path
  $hasFinalNewline = $text.EndsWith("`n", [StringComparison]::Ordinal)
  $lines = [Collections.Generic.List[string]]::new()
  foreach ($line in @($text -split "`n")) { $lines.Add($line.TrimEnd("`r")) }
  $headerIndex = -1
  for ($index = 0; $index -lt $lines.Count; $index++) {
    $line = $lines[$index].Trim()
    if (-not ($line.StartsWith('|') -and $line.EndsWith('|'))) { continue }
    $cells = @($line.Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ })
    if ($cells.Count -eq $Header.Count -and ($cells -join "`0") -ceq ($Header -join "`0")) {
      $headerIndex = $index
      break
    }
  }
  if ($headerIndex -lt 0) { throw "Table header is missing: $Path" }
  $matches = [Collections.Generic.List[int]]::new()
  for ($index = $headerIndex + 2; $index -lt $lines.Count; $index++) {
    $line = $lines[$index].Trim()
    if ([string]::IsNullOrWhiteSpace($line) -or -not $line.StartsWith('|')) { break }
    if (-not $line.EndsWith('|')) { throw "Table row is malformed: $Path" }
    $cells = @($line.Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ })
    if ($cells.Count -ne $Header.Count) { throw "Table row has an invalid cell count: $Path" }
    if ($cells[0] -ceq $ExpectedId) {
      $replacement = & $Transform $cells
      if (@($replacement).Count -ne $Header.Count) { throw "Transformed row has an invalid cell count: $Path" }
      $lines[$index] = '| ' + (@($replacement) -join ' | ') + ' |'
      $matches.Add($index)
    }
  }
  if ($matches.Count -ne 1) { throw "Expected exactly one $ExpectedId row: $Path" }
  $updated = $lines -join "`n"
  if ($hasFinalNewline -and -not $updated.EndsWith("`n", [StringComparison]::Ordinal)) { $updated += "`n" }
  Write-Utf8Text -Path $Path -Text $updated
}

try {
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { throw 'RepositoryRoot must be absolute' }
  $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { throw 'RepositoryRoot must be a Git root' }
  if ([string]::IsNullOrWhiteSpace($TaskId) -or $TaskId -cne [IO.Path]::GetFileName($TaskId)) { throw 'TaskId is invalid' }

  $cardRelativePath = "开发管理/任务卡/$TaskId.txt"
  $cardPath = Join-Path $root $cardRelativePath
  $cardText = Read-Utf8Text -Path $cardPath
  $match = [regex]::Match($cardText, '(?ms)\A---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---\r?\n(?<body>.*)\z')
  if (-not $match.Success) { throw 'Task-card delimiters are invalid' }
  $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100
  if (
    [string]$metadata.id -cne $TaskId -or
    [string]$metadata.route -cne 'external_execute' -or
    [string]$metadata.owner -cne 'deepseek' -or
    [string]$metadata.dispatchState -cne 'ready'
  ) { throw 'Task card is not external_execute/deepseek/ready' }
  $expectedPaths = @($metadata.expectedPaths | ForEach-Object { [string]$_ })
  $sourceBacklog = [string]$metadata.sourceBacklog
  foreach ($requiredPath in @($cardRelativePath, '开发管理/当前任务队列.txt', $sourceBacklog)) {
    if ($expectedPaths -cnotcontains $requiredPath) { throw "Task expectedPaths does not authorize $requiredPath" }
  }
  $reviewListPath = '开发管理/未通过审核清单.txt'
  if ($expectedPaths -cnotcontains $reviewListPath) {
    $metadata.expectedPaths = @($expectedPaths + $reviewListPath)
  }

  $metadata.route = 'codex_review'
  $metadata.owner = 'codex'
  $metadata.dispatchState = 'ready'
  $metadata.stateReason = 'DeepSeek 候选已形成正式业务提交，等待 Codex 独立复审'
  foreach ($field in @('automationCheckpoint', 'automationReply')) {
    if ($metadata.PSObject.Properties.Name -contains $field) { $metadata.PSObject.Properties.Remove($field) }
  }
  $newCard = @(
    '---TASK-META---',
    ($metadata | ConvertTo-Json -Depth 100),
    '---TASK-BODY---',
    $match.Groups['body'].Value
  ) -join "`n"
  Write-Utf8Text -Path $cardPath -Text $newCard

  $queuePath = Join-Path $root '开发管理\当前任务队列.txt'
  Update-TableRow -Path $queuePath -Header @('ID', '路由', '主责', '优先级', '领域', '阶段', '标题', '任务卡') -ExpectedId $TaskId -Transform {
    param($cells)
    @($cells[0], 'codex_review', 'codex', $cells[3], $cells[4], $cells[5], $cells[6], $cells[7])
  }

  $backlogPath = Join-Path $root $sourceBacklog
  Update-TableRow -Path $backlogPath -Header @('ID', '优先级', '主责', '状态投影', '阻塞于', '摘要', '任务卡') -ExpectedId $TaskId -Transform {
    param($cells)
    @($cells[0], $cells[1], 'codex', '已排队', $cells[4], $cells[5], $cells[6])
  }

  $checker = Join-Path $root 'tools\check-task-cards.ps1'
  $evidence = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -RepositoryRoot $root -TaskId $TaskId -Postcondition ExternalPendingReview -OutputJson 2>$null)
  if ($LASTEXITCODE -ne 0 -or $evidence.Count -ne 1 -or [string](($evidence[0] | ConvertFrom-Json).status) -cne 'ok') {
    throw 'Pending-review projection check failed'
  }
  [Console]::Out.WriteLine(([ordered]@{
    status = 'updated'
    taskId = $TaskId
    changedPaths = @($cardRelativePath, '开发管理/当前任务队列.txt', $sourceBacklog)
  } | ConvertTo-Json -Compress))
} catch {
  [Console]::Error.WriteLine('set-task-pending-review: FAILED')
  [Console]::Out.WriteLine(([ordered]@{ status = 'failed'; taskId = $TaskId; detailCode = 'pending_review_projection_failed' } | ConvertTo-Json -Compress))
  exit 1
}
