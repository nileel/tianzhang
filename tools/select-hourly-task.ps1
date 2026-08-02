#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$RepositoryRoot,
  [Parameter(Mandatory = $true)]
  [ValidateSet('codex', 'deepseek')]
  [string]$Owner
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Utf8Text {
  param([string]$Path)
  [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
}

function Normalize-Cell {
  param([string]$Value)
  $Value.Trim().Trim([char]96)
}

function Get-QueueRows {
  param([string]$Path)

  $header = @('ID', '路由', '主责', '优先级', '领域', '阶段', '标题', '任务卡')
  $lines = @((Read-Utf8Text -Path $Path) -split "`n")
  $headerIndex = -1
  for ($index = 0; $index -lt $lines.Count; $index++) {
    $line = $lines[$index].Trim()
    if (-not ($line.StartsWith('|') -and $line.EndsWith('|'))) { continue }
    $cells = @($line.Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ })
    if ($cells.Count -eq $header.Count -and ($cells -join "`0") -ceq ($header -join "`0")) {
      $headerIndex = $index
      break
    }
  }
  if ($headerIndex -lt 0 -or $headerIndex + 1 -ge $lines.Count) {
    throw 'Queue table header is missing'
  }
  $separatorCells = @($lines[$headerIndex + 1].Trim().Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ })
  if ($separatorCells.Count -ne $header.Count -or @($separatorCells | Where-Object { $_ -notmatch '^:?-{3,}:?$' }).Count -ne 0) {
    throw 'Queue table separator is invalid'
  }

  $rows = [Collections.Generic.List[object]]::new()
  for ($index = $headerIndex + 2; $index -lt $lines.Count; $index++) {
    $line = $lines[$index].Trim()
    if ([string]::IsNullOrWhiteSpace($line) -or -not $line.StartsWith('|')) { break }
    if (-not $line.EndsWith('|')) { throw 'Queue row is malformed' }
    $cells = @($line.Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ })
    if ($cells.Count -ne $header.Count) { throw 'Queue row has an invalid cell count' }
    $rows.Add([pscustomobject][ordered]@{
      taskId = $cells[0]
      route = $cells[1]
      owner = $cells[2]
      cardPath = $cells[7]
    })
  }
  @($rows)
}

function Invoke-TaskCardCheck {
  param([string[]]$Arguments)

  $checker = Join-Path $script:resolvedRepositoryRoot 'tools\check-task-cards.ps1'
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $checker @Arguments 2>$null)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
    throw 'Task-card projection check failed'
  }
  try {
    $output[0] | ConvertFrom-Json -Depth 50
  } catch {
    throw 'Task-card projection check returned invalid JSON'
  }
}

function Read-TaskMetadata {
  param([string]$Path, [string]$ExpectedTaskId)

  $bytes = [IO.File]::ReadAllBytes($Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
  $meta = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---\r?$')
  if (-not $meta.Success) { throw "Task metadata delimiters are invalid: $ExpectedTaskId" }
  try {
    $metadata = $meta.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 50
  } catch {
    throw "Task metadata JSON is invalid: $ExpectedTaskId"
  }
  if ([string]$metadata.id -cne $ExpectedTaskId) { throw "Task metadata ID mismatch: $ExpectedTaskId" }
  [pscustomobject]@{
    Metadata = $metadata
    Digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
      [Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n"))
    )).ToLowerInvariant()
  }
}

try {
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { throw 'RepositoryRoot must be absolute' }
  $script:resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath (Join-Path $script:resolvedRepositoryRoot '.git'))) {
    throw 'RepositoryRoot must be a Git root'
  }
  $queuePath = Join-Path $script:resolvedRepositoryRoot '开发管理\当前任务队列.txt'
  if (-not (Test-Path -LiteralPath $queuePath -PathType Leaf)) { throw 'Current queue is missing' }
  $globalEvidence = Invoke-TaskCardCheck -Arguments @('-RepositoryRoot', $script:resolvedRepositoryRoot, '-OutputJson')
  if ([string]$globalEvidence.status -cne 'ok') { throw 'Task-card projection is invalid' }

  $selected = $null
  $queueRows = @(Get-QueueRows -Path $queuePath)
  foreach ($row in $queueRows) {
    $matches = if ($Owner -ceq 'codex') {
      [string]$row.owner -ceq 'codex' -and [string]$row.route -cin @('codex_execute', 'codex_review')
    } else {
      [string]$row.owner -ceq 'deepseek' -and [string]$row.route -ceq 'external_execute'
    }
    if (-not $matches) { continue }

    $evidenceArguments = @(
      '-RepositoryRoot', $script:resolvedRepositoryRoot,
      '-TaskId', [string]$row.taskId,
      '-OutputJson'
    )
    if ($Owner -ceq 'codex') {
      $evidenceArguments += @('-Postcondition', 'CodexDispatchReady', '-ExpectedRoute', [string]$row.route)
    } else {
      $evidenceArguments += @('-Postcondition', 'ExternalDispatchReady', '-ExpectedOwner', 'deepseek')
    }
    $evidence = Invoke-TaskCardCheck -Arguments $evidenceArguments
    if ([string]$evidence.status -cne 'ok' -or [string]$evidence.taskState -cne 'ready') {
      throw "Selected task is not dispatch-ready: $($row.taskId)"
    }
    $expectedCardPath = "开发管理/任务卡/$($row.taskId).txt"
    if ([string]$row.cardPath -cne $expectedCardPath) { throw "Queue task-card path mismatch: $($row.taskId)" }
    $card = Read-TaskMetadata -Path (Join-Path $script:resolvedRepositoryRoot $expectedCardPath) -ExpectedTaskId ([string]$row.taskId)
    $metadata = $card.Metadata
    if (
      [string]$metadata.route -cne [string]$row.route -or
      [string]$metadata.owner -cne $Owner -or
      [string]$metadata.dispatchState -cne 'ready'
    ) {
      throw "Queue and task card disagree: $($row.taskId)"
    }
    $selected = [ordered]@{
      status = 'selected'
      taskId = [string]$row.taskId
      route = [string]$row.route
      owner = $Owner
      queueCount = $queueRows.Count
      taskCardDigest = [string]$card.Digest
      expectedPaths = @($metadata.expectedPaths | ForEach-Object { [string]$_ })
      sourceBacklog = [string]$metadata.sourceBacklog
    }
    break
  }

  if ($null -eq $selected) {
    $selected = [ordered]@{
      status = 'no_candidate'
      owner = $Owner
      queueCount = $queueRows.Count
    }
  }
  [Console]::Out.WriteLine(($selected | ConvertTo-Json -Compress -Depth 20))
} catch {
  [Console]::Error.WriteLine('select-hourly-task: FAILED')
  [Console]::Out.WriteLine(([ordered]@{ status = 'failed'; owner = $Owner; detailCode = 'task_selection_failed' } | ConvertTo-Json -Compress))
  exit 1
}
