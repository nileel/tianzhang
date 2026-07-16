[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('PublishPending','PublishImplementationPending','Clear')]
  [string]$Action,

  [Parameter(Mandatory = $true)]
  [string]$StatusPath,

  [string]$DecisionStateJsonBase64,

  [string]$FeishuHealthPath
)

$ErrorActionPreference = 'Stop'
$script:ExitInvalidArguments = 15
$script:Heading = '## 当前待决策'
$script:EmailPattern = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
$script:NotificationStatuses = @(
  'PENDING','PROVIDER_ACCEPTED','PROVIDER_OUTCOME_UNKNOWN','DELIVERY_FAILED',
  'MISADDRESSED','RETRY_EXHAUSTED'
)
$script:ResolutionSourceLabels = @{
  email = '旧 Gmail 通道（仅历史）'
  manual = '人工确认'
  feishu_card = '飞书互动卡片'
  feishu_card_input = '飞书卡片输入'
  feishu_text = '飞书普通文本'
  manual_custom = '人工自定义'
}
$script:OptionResolutionSources = @('email','manual','feishu_card')
$script:CustomResolutionSources = @('feishu_card_input','feishu_text','manual_custom')

function Require-Text {
  param([object]$Value, [string]$Name)
  if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$Value)) {
    throw "$Name is required"
  }
  if ([string]$Value -match '[\r\n]') { throw "$Name must be a single line" }
}

function Test-HasProperty {
  param([object]$Value, [string]$Name)
  $null -ne $Value -and $null -ne $Value.PSObject.Properties[$Name]
}

function Assert-AllowedProperties {
  param([object]$Value, [string[]]$Allowed, [string]$Path)
  if ($null -eq $Value) { throw "$Path is required" }
  foreach ($property in $Value.PSObject.Properties) {
    if ($Allowed -cnotcontains [string]$property.Name) {
      throw "$Path contains unsupported field: $($property.Name)"
    }
  }
}

function Assert-DecisionId {
  param([object]$Value, [string]$Name)
  Require-Text $Value $Name
  if ([string]$Value -notmatch '^DEC-[0-9]{8}-[A-Z0-9]+$') { throw "$Name has an invalid format" }
}

function Assert-CustomText {
  param([object]$Value, [string]$Name)
  if ($Value -isnot [string]) { throw "$Name must be a string" }
  $text = [string]$Value
  if ($text.Replace("`r`n", "`n").Replace("`r", "`n").Trim() -cne $text) {
    throw "$Name must be canonically normalized"
  }
  $codePointCount = @($text.EnumerateRunes()).Count
  if ($codePointCount -lt 1 -or $codePointCount -gt 1000) { throw "$Name has an invalid length" }
  $unsafeScan = $text.Replace("`n", '').Replace("`t", '')
  if ($unsafeScan -match '[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]') {
    throw "$Name contains unsafe characters"
  }
}

function Assert-ResolvedDecisions {
  param($DecisionFlow)
  if (-not (Test-HasProperty $DecisionFlow 'resolvedDecisions')) { throw 'decisionFlow.resolvedDecisions is required' }
  $resolved = @($DecisionFlow.resolvedDecisions)
  foreach ($entry in $resolved) {
    if ($null -eq $entry) { throw 'decisionFlow.resolvedDecisions cannot contain null' }
    Assert-AllowedProperties $entry @('decisionId','resolution') 'resolvedDecision'
    Assert-DecisionId $entry.decisionId 'resolvedDecision.decisionId'
    if ($null -eq $entry.resolution) { throw 'resolvedDecision.resolution is required' }
    Assert-AllowedProperties $entry.resolution @('optionKey','customText','source') 'resolvedDecision.resolution'
    Require-Text $entry.resolution.source 'resolvedDecision.resolution.source'
    $hasOption = Test-HasProperty $entry.resolution 'optionKey'
    $hasCustom = Test-HasProperty $entry.resolution 'customText'
    if ($hasOption -eq $hasCustom) { throw 'resolvedDecision.resolution must contain exactly one reply kind' }
    if ($hasOption) {
      Require-Text $entry.resolution.optionKey 'resolvedDecision.resolution.optionKey'
      if ([string]$entry.resolution.source -notin $script:OptionResolutionSources) {
        throw 'resolvedDecision option source is not supported'
      }
    } else {
      Assert-CustomText $entry.resolution.customText 'resolvedDecision.resolution.customText'
      if ([string]$entry.resolution.source -notin $script:CustomResolutionSources) {
        throw 'resolvedDecision custom source is not supported'
      }
    }
  }
  $resolved
}

function Assert-DecisionFlow {
  param($DecisionFlow)
  if ($null -eq $DecisionFlow) { throw 'decisionFlow is required' }
  Assert-AllowedProperties $DecisionFlow @('taskId','status','resolvedDecisions') 'decisionFlow'
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
  Assert-AllowedProperties $PendingDecision @(
    'decisionId','createdAt','taskId','taskSummary','question','options','recommendedOption','status','notificationProvider'
  ) 'pendingDecision'
  foreach ($name in @('createdAt','taskId','taskSummary','question','recommendedOption','status')) {
    Require-Text $PendingDecision.$name "pendingDecision.$name"
  }
  Assert-DecisionId $PendingDecision.decisionId 'pendingDecision.decisionId'
  if ([string]$PendingDecision.status -notin $script:NotificationStatuses) { throw 'pendingDecision.status is not supported' }
  if (Test-HasProperty $PendingDecision 'notificationProvider') {
    Require-Text $PendingDecision.notificationProvider 'pendingDecision.notificationProvider'
    if ([string]$PendingDecision.notificationProvider -notin @('feishu','gmail_legacy')) {
      throw 'pendingDecision.notificationProvider is not supported'
    }
  }
  if ($PendingDecision.options -isnot [System.Array] -or @($PendingDecision.options).Count -lt 2) {
    throw 'pendingDecision.options requires at least two entries'
  }

  $keys = [Collections.Generic.List[string]]::new()
  foreach ($option in @($PendingDecision.options)) {
    Assert-AllowedProperties $option @('key','label') 'pendingDecision.option'
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
  Assert-AllowedProperties $payload @('pendingDecision','decisionFlow') 'payload'
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
    $sourceLabel = [string]$script:ResolutionSourceLabels[[string]$entry.resolution.source]
    $choice = if (Test-HasProperty $entry.resolution 'optionKey') {
      [string]$entry.resolution.optionKey
    } else {
      '自定义'
    }
    $choices.Add("$label=$choice（$sourceLabel）")
    $identifiers.Add("$label=$([string]$entry.decisionId)")
  }
  $lines = [Collections.Generic.List[string]]::new()
  $lines.Add('- 已登记选择：' + ($choices -join '；'))
  $lines.Add('- 决策编号摘要：' + ($identifiers -join '；'))
  $lines.ToArray()
}

function Get-FeishuHealthSummary {
  if ([string]::IsNullOrWhiteSpace($FeishuHealthPath)) { return $null }
  $summary = [ordered]@{ available = $false; status = 'UNAVAILABLE' }
  try {
    $fullPath = [IO.Path]::GetFullPath($FeishuHealthPath)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf) -or (Get-Item -LiteralPath $fullPath).Length -gt 16KB) {
      return [pscustomobject]$summary
    }
    $health = [IO.File]::ReadAllText($fullPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -DateKind String
    $status = [string]$health.status
    $updatedAt = [DateTimeOffset]::Parse([string]$health.updatedAt, [Globalization.CultureInfo]::InvariantCulture)
    $appIdHash = [string]$health.appIdHash
    $age = [DateTimeOffset]::UtcNow - $updatedAt.ToUniversalTime()
    if ($status -ceq 'CONNECTED' -and $appIdHash -cmatch '^[0-9a-f]{64}$' -and
        $age.TotalSeconds -ge 0 -and $age.TotalSeconds -le 120) {
      $summary.available = $true
      $summary.status = 'CONNECTED'
    }
  } catch {
    $summary.available = $false
    $summary.status = 'UNAVAILABLE'
  }
  [pscustomobject]$summary
}

function Get-NotificationLabel {
  param($PendingDecision, [AllowNull()][object]$Health)

  $status = [string]$PendingDecision.status
  $provider = if (Test-HasProperty $PendingDecision 'notificationProvider') {
    [string]$PendingDecision.notificationProvider
  } else {
    $null
  }
  if ($status -ceq 'PENDING' -and $null -ne $Health -and -not $Health.available) {
    return '飞书桥接不可用，未消耗发送重试'
  }
  if ($provider -ceq 'gmail_legacy') {
    $label = switch ($status) {
      'PROVIDER_ACCEPTED' { '旧 Gmail 通道已由提供方接受（仅历史）' }
      'DELIVERY_FAILED' { '旧 Gmail 通道发送失败（仅历史）' }
      'MISADDRESSED' { '旧 Gmail 通道目标不一致（仅历史）' }
      'RETRY_EXHAUSTED' { '旧 Gmail 通道已达尝试上限（仅历史）' }
      default { '旧 Gmail 通道待处理（仅历史）' }
    }
    return $label
  }
  switch ($status) {
    'PENDING' { '等待发送飞书卡片' }
    'PROVIDER_ACCEPTED' { '飞书卡片已送达，等待选择' }
    'PROVIDER_OUTCOME_UNKNOWN' { '飞书发送结果待人工核对，已停止自动补发' }
    'DELIVERY_FAILED' { '飞书发送失败，可在下一轮重试' }
    'MISADDRESSED' { '飞书目标证据不一致，等待人工处理' }
    'RETRY_EXHAUSTED' { '飞书明确失败已达三次，等待人工处理' }
  }
}

function Get-PendingBody {
  param($Payload, [AllowNull()][object]$Health)
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
  $body.Add("- 通知状态：$(Get-NotificationLabel $decision $Health)")
  if ($null -ne $Health) {
    $body.Add($(if ($Health.available) { '- 飞书桥接：已连接。' } else { '- 飞书桥接：不可用。' }))
  }
  $body.Add('- 卡片选择：请在飞书互动卡片中选择一个选项。')
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
  $operationId = [guid]::NewGuid().ToString('N')
  $temporary = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + $operationId + '.tmp')
  $backup = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + $operationId + '.backup')
  $replaceSucceeded = $false
  try {
    [IO.File]::WriteAllText($temporary, $Content, [Text.UTF8Encoding]::new($WithBom))
    [IO.File]::Replace($temporary, $Path, $backup, $true)
    $replaceSucceeded = $true
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
  } catch {
    if (-not [IO.File]::Exists($Path) -and [IO.File]::Exists($backup)) {
      try { [IO.File]::Move($backup, $Path) } catch { }
    }
    throw
  } finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    if ($replaceSucceeded -and [IO.File]::Exists($backup)) {
      Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    }
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
    'PublishPending' { Get-PendingBody (ConvertFrom-DecisionStateBase64) (Get-FeishuHealthSummary); break }
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
