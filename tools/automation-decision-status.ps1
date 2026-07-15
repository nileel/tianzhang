[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Publish','Clear')]
  [string]$Action,

  [Parameter(Mandatory = $true)]
  [string]$StatusPath,

  [string]$DecisionJsonBase64
)

$ErrorActionPreference = 'Stop'
$script:ExitInvalidArguments = 15
$script:Heading = '## 当前待决策'
$script:EmailPattern = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'

function Require-Text {
  param([object]$Value, [string]$Name)
  if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$Value)) {
    throw "$Name is required"
  }
}

function ConvertFrom-DecisionBase64 {
  if ([string]::IsNullOrWhiteSpace($DecisionJsonBase64)) { throw 'DecisionJsonBase64 is required for Publish' }
  try {
    $jsonBytes = [Convert]::FromBase64String($DecisionJsonBase64)
    $json = [Text.UTF8Encoding]::new($false, $true).GetString($jsonBytes)
    $decision = $json | ConvertFrom-Json -DateKind String
  } catch {
    throw "DecisionJsonBase64 does not contain valid UTF-8 JSON: $($_.Exception.Message)"
  }

  foreach ($name in @('decisionId','createdAt','taskId','taskSummary','question','recommendedOption','status')) {
    Require-Text $decision.$name $name
  }
  if ($decision.decisionId -notmatch '^DEC-[0-9]{8}-[A-Z0-9]+$') { throw 'decisionId has an invalid format' }
  if ($decision.status -notin @('PENDING','NOTIFIED','DELIVERY_FAILED','REPLY_INVALID','RESOLVED')) { throw 'status is not supported' }
  if ($decision.options -isnot [System.Array] -or @($decision.options).Count -lt 2) { throw 'options requires at least two entries' }

  $keys = [Collections.Generic.List[string]]::new()
  foreach ($option in @($decision.options)) {
    Require-Text $option.key 'option.key'
    Require-Text $option.label 'option.label'
    $keys.Add([string]$option.key)
  }
  if (@($keys | Sort-Object -Unique).Count -ne $keys.Count) { throw 'option keys must be unique' }
  if (-not $keys.Contains([string]$decision.recommendedOption)) { throw 'recommendedOption must match an option key' }
  $decision
}

function Get-PublishBody {
  param($Decision)
  $body = [Collections.Generic.List[string]]::new()
  $body.Add('')
  $body.Add("- 决策编号：``$([string]$Decision.decisionId)``")
  $body.Add("- 关联任务：``$([string]$Decision.taskId)`` — $([string]$Decision.taskSummary)")
  $body.Add("- 问题：$([string]$Decision.question)")
  foreach ($option in @($Decision.options)) {
    $body.Add("- 选项 $([string]$option.key)：$([string]$option.label)")
  }
  $body.Add("- 推荐项：$([string]$Decision.recommendedOption)")
  $body.Add("- 创建时间：$([string]$Decision.createdAt)")
  $body.Add("- 通知状态：$([string]$Decision.status)")
  $body.Add("- 严格回复：``$([string]$Decision.decisionId)：选 $([string]$Decision.recommendedOption)``（也可选择其他单一选项）")
  $body.Add('')
  if (($body -join "`n") -match $script:EmailPattern) { throw 'decision status contains an email address' }
  $body.ToArray()
}

function Write-Atomically {
  param([string]$Path, [string]$Content, [bool]$WithBom)
  $directory = Split-Path -Parent $Path
  $temporary = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  $backup = "$Path.backup"
  try {
    [IO.File]::WriteAllText($temporary, $Content, [Text.UTF8Encoding]::new($WithBom))
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    [IO.File]::Replace($temporary, $Path, $backup, $true)
  } finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
  }
}

try {
  if (-not (Test-Path -LiteralPath $StatusPath -PathType Leaf)) { throw 'StatusPath must reference an existing file' }
  $fullPath = [IO.Path]::GetFullPath($StatusPath)
  $raw = [IO.File]::ReadAllBytes($fullPath)
  $hasBom = $raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB -and $raw[2] -eq 0xBF
  $offset = if ($hasBom) { 3 } else { 0 }
  $content = [Text.UTF8Encoding]::new($false, $true).GetString($raw, $offset, $raw.Length - $offset)
  $newline = if ($content.Contains("`r`n", [StringComparison]::Ordinal)) { "`r`n" } else { "`n" }
  $lines = [regex]::Split($content, '\r\n|\n')

  $headingIndexes = @()
  for ($index = 0; $index -lt $lines.Count; $index++) {
    if ([string]$lines[$index] -ceq $script:Heading) { $headingIndexes += $index }
  }
  if ($headingIndexes.Count -ne 1) { throw 'Status file must contain exactly one pending-decision heading' }
  $headingIndex = [int]$headingIndexes[0]
  $nextHeadingIndex = -1
  for ($index = $headingIndex + 1; $index -lt $lines.Count; $index++) {
    if ([string]$lines[$index] -match '^##\s+') { $nextHeadingIndex = $index; break }
  }
  if ($nextHeadingIndex -lt 0) { throw 'Pending-decision section must be followed by another level-two heading' }

  $replacement = if ($Action -eq 'Publish') {
    Get-PublishBody (ConvertFrom-DecisionBase64)
  } else {
    @('', '当前无待决策项。', '')
  }
  $newLines = @($lines[0..$headingIndex]) + @($replacement) + @($lines[$nextHeadingIndex..($lines.Count - 1)])
  Write-Atomically $fullPath ($newLines -join $newline) $hasBom
  exit 0
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit $script:ExitInvalidArguments
}
