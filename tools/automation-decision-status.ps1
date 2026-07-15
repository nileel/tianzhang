[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('PublishPending','PublishImplementationPending','Clear')]
  [string]$Action,

  [Parameter(Mandatory = $true)]
  [string]$StatusPath,

  [string]$DecisionStateJsonBase64
)

$ErrorActionPreference = 'Stop'
$script:ExitInvalidArguments = 15
$script:Heading = '## 当前待决策'
$script:EmailPattern = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
$script:ForbiddenPropertyPattern = '(?i)(email|evidence|provider|message)'
$script:NotificationLabels = @{
  PENDING = '尚未尝试发送'
  PROVIDER_ACCEPTED = '发送请求已被提供方接受（不代表已收件）'
  DELIVERY_FAILED = '发送失败，可重试'
  MISADDRESSED = 'Sent 目标不一致，未完成通知'
  RETRY_EXHAUSTED = '已达三次尝试上限，等待人工处理'
}

function Require-Text {
  param([object]$Value, [string]$Name)
  if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$Value)) {
    throw "$Name is required"
  }
}

function Test-HasProperty {
  param([object]$Value, [string]$Name)
  $null -ne $Value -and $null -ne $Value.PSObject.Properties[$Name]
}

function Assert-NoSensitiveProperties {
  param([object]$Value, [string]$Path = 'payload')
  if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) { return }

  if ($Value -is [Collections.IDictionary]) {
    foreach ($key in $Value.Keys) {
      if ([string]$key -match $script:ForbiddenPropertyPattern) { throw "$Path contains forbidden field: $key" }
      Assert-NoSensitiveProperties $Value[$key] "$Path.$key"
    }
    return
  }

  if ($Value -is [Collections.IEnumerable] -and $Value -isnot [pscustomobject]) {
    $index = 0
    foreach ($entry in $Value) {
      Assert-NoSensitiveProperties $entry "$Path[$index]"
      $index++
    }
    return
  }

  foreach ($property in $Value.PSObject.Properties) {
    if ($property.Name -match $script:ForbiddenPropertyPattern) { throw "$Path contains forbidden field: $($property.Name)" }
    Assert-NoSensitiveProperties $property.Value "$Path.$($property.Name)"
  }
}

function Assert-DecisionId {
  param([object]$Value, [string]$Name)
  Require-Text $Value $Name
  if ([string]$Value -notmatch '^DEC-[0-9]{8}-[A-Z0-9]+$') { throw "$Name has an invalid format" }
}

function Assert-ResolvedDecisions {
  param($DecisionFlow)
  if (-not (Test-HasProperty $DecisionFlow 'resolvedDecisions')) { throw 'decisionFlow.resolvedDecisions is required' }
  $resolved = @($DecisionFlow.resolvedDecisions)
  foreach ($entry in $resolved) {
    if ($null -eq $entry) { throw 'decisionFlow.resolvedDecisions cannot contain null' }
    Assert-DecisionId $entry.decisionId 'resolvedDecision.decisionId'
    if ($null -eq $entry.resolution) { throw 'resolvedDecision.resolution is required' }
    Require-Text $entry.resolution.optionKey 'resolvedDecision.resolution.optionKey'
    Require-Text $entry.resolution.source 'resolvedDecision.resolution.source'
    if ([string]$entry.resolution.source -notin @('email','manual')) { throw 'resolvedDecision.resolution.source is not supported' }
  }
  $resolved
}

function Assert-DecisionFlow {
  param($DecisionFlow)
  if ($null -eq $DecisionFlow) { throw 'decisionFlow is required' }
  Require-Text $DecisionFlow.taskId 'decisionFlow.taskId'
  Require-Text $DecisionFlow.status 'decisionFlow.status'
  if ([string]$DecisionFlow.status -notin @('AWAITING_DECISION','IMPLEMENTATION_PENDING')) {
    throw 'decisionFlow.status is not supported'
  }
  Assert-ResolvedDecisions $DecisionFlow | Out-Null
}

function Assert-PendingDecision {
  param($PendingDecision)
  if ($null -eq $PendingDecision) { throw 'pendingDecision is required for PublishPending' }
  foreach ($name in @('createdAt','taskId','taskSummary','question','recommendedOption','status')) {
    Require-Text $PendingDecision.$name "pendingDecision.$name"
  }
  Assert-DecisionId $PendingDecision.decisionId 'pendingDecision.decisionId'
  if ([string]$PendingDecision.status -notin @($script:NotificationLabels.Keys)) { throw 'pendingDecision.status is not supported' }
  if ($PendingDecision.options -isnot [System.Array] -or @($PendingDecision.options).Count -lt 2) {
    throw 'pendingDecision.options requires at least two entries'
  }

  $keys = [Collections.Generic.List[string]]::new()
  foreach ($option in @($PendingDecision.options)) {
    Require-Text $option.key 'pendingDecision.option.key'
    Require-Text $option.label 'pendingDecision.option.label'
    $keys.Add([string]$option.key)
  }
  if (@($keys | Sort-Object -Unique).Count -ne $keys.Count) { throw 'pendingDecision option keys must be unique' }
  if (-not $keys.Contains([string]$PendingDecision.recommendedOption)) {
    throw 'pendingDecision.recommendedOption must match an option key'
  }
}

function ConvertFrom-DecisionStateBase64 {
  if ([string]::IsNullOrWhiteSpace($DecisionStateJsonBase64)) {
    throw 'DecisionStateJsonBase64 is required for publish actions'
  }
  try {
    $jsonBytes = [Convert]::FromBase64String($DecisionStateJsonBase64)
    $json = [Text.UTF8Encoding]::new($false, $true).GetString($jsonBytes)
    $payload = $json | ConvertFrom-Json -DateKind String
  } catch {
    throw "DecisionStateJsonBase64 does not contain valid UTF-8 JSON: $($_.Exception.Message)"
  }

  if ($null -eq $payload -or $payload -is [System.Array] -or $payload -is [string] -or $payload -is [ValueType]) {
    throw 'DecisionStateJsonBase64 must contain a JSON object'
  }
  if (-not (Test-HasProperty $payload 'pendingDecision') -or -not (Test-HasProperty $payload 'decisionFlow')) {
    throw 'payload must contain pendingDecision and decisionFlow'
  }
  if ($json -match $script:EmailPattern) { throw 'payload contains an email address' }
  Assert-NoSensitiveProperties $payload
  Assert-DecisionFlow $payload.decisionFlow
  $payload
}

function Get-OrdinalLabel {
  param([int]$Index)
  $known = @('第一项','第二项','第三项','第四项','第五项','第六项','第七项','第八项','第九项','第十项')
  if ($Index -ge 0 -and $Index -lt $known.Count) { return $known[$Index] }
  "第$($Index + 1)项"
}

function Get-ResolvedSummaryLines {
  param([object[]]$ResolvedDecisions)
  if ($ResolvedDecisions.Count -eq 0) { return @() }

  $choices = [Collections.Generic.List[string]]::new()
  $identifiers = [Collections.Generic.List[string]]::new()
  for ($index = 0; $index -lt $ResolvedDecisions.Count; $index++) {
    $label = Get-OrdinalLabel $index
    $entry = $ResolvedDecisions[$index]
    $choices.Add("$label=$([string]$entry.resolution.optionKey)（$([string]$entry.resolution.source)）")
    $identifiers.Add("$label=$([string]$entry.decisionId)")
  }
  @(
    '- 已登记选择：' + ($choices -join '；'),
    '- 决策编号摘要：' + ($identifiers -join '；')
  )
}

function Get-PendingBody {
  param($Payload)
  if ([string]$Payload.decisionFlow.status -ne 'AWAITING_DECISION') {
    throw 'PublishPending requires decisionFlow.status AWAITING_DECISION'
  }
  Assert-PendingDecision $Payload.pendingDecision
  if ([string]$Payload.pendingDecision.taskId -cne [string]$Payload.decisionFlow.taskId) {
    throw 'pendingDecision.taskId must match decisionFlow.taskId'
  }

  $decision = $Payload.pendingDecision
  $body = [Collections.Generic.List[string]]::new()
  $body.Add('')
  $body.Add("- 决策编号：``$([string]$decision.decisionId)``")
  $body.Add("- 关联任务：``$([string]$decision.taskId)`` — $([string]$decision.taskSummary)")
  $body.Add("- 问题：$([string]$decision.question)")
  foreach ($option in @($decision.options)) {
    $body.Add("- 选项 $([string]$option.key)：$([string]$option.label)")
  }
  $body.Add("- 推荐项：$([string]$decision.recommendedOption)")
  $body.Add("- 创建时间：$([string]$decision.createdAt)")
  $body.Add("- 通知状态：$([string]$script:NotificationLabels[[string]$decision.status])")
  $body.Add("- 严格回复：``$([string]$decision.decisionId)：选 $([string]$decision.recommendedOption)``（也可选择其他单一选项）")
  foreach ($line in @(Get-ResolvedSummaryLines @(Assert-ResolvedDecisions $Payload.decisionFlow))) {
    $body.Add([string]$line)
  }
  $body.Add('')
  if (($body -join "`n") -match $script:EmailPattern) { throw 'decision status contains an email address' }
  $body.ToArray()
}

function Get-ImplementationPendingBody {
  param($Payload)
  if ($null -ne $Payload.pendingDecision) {
    throw 'PublishImplementationPending requires pendingDecision to be null'
  }
  if ([string]$Payload.decisionFlow.status -ne 'IMPLEMENTATION_PENDING') {
    throw 'PublishImplementationPending requires decisionFlow.status IMPLEMENTATION_PENDING'
  }
  $resolved = @(Assert-ResolvedDecisions $Payload.decisionFlow)
  if ($resolved.Count -lt 1) { throw 'PublishImplementationPending requires at least one resolved decision' }

  $body = [Collections.Generic.List[string]]::new()
  $body.Add('')
  $body.Add("- 关联任务：``$([string]$Payload.decisionFlow.taskId)``")
  $body.Add('- 当前阶段：等待原任务实施。')
  foreach ($line in @(Get-ResolvedSummaryLines $resolved)) {
    $body.Add([string]$line)
  }
  $body.Add('')
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

  $replacement = switch ($Action) {
    'PublishPending' { Get-PendingBody (ConvertFrom-DecisionStateBase64); break }
    'PublishImplementationPending' { Get-ImplementationPendingBody (ConvertFrom-DecisionStateBase64); break }
    'Clear' { @('', '当前无待决策项。', ''); break }
  }
  $newLines = @($lines[0..$headingIndex]) + @($replacement) + @($lines[$nextHeadingIndex..($lines.Count - 1)])
  Write-Atomically $fullPath ($newLines -join $newline) $hasBom
  exit 0
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit $script:ExitInvalidArguments
}
