#requires -Version 7.0

Set-StrictMode -Version Latest

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'private-path-acl.ps1')

$script:ForbiddenFields = @(
  'appSecret',
  'tenantKey',
  'openId',
  'chatId',
  'messageId',
  'eventId',
  'providerMessageId',
  'providerEventId',
  'evidenceHash',
  'rawEvent'
)

function ConvertTo-DecisionJson {
  param([Parameter(Mandatory = $true)]$Value)

  ($Value | ConvertTo-Json -Depth 100) -replace "`r`n?", "`n"
}

function Copy-DecisionValue {
  param([Parameter(Mandatory = $true)]$Value)

  $json = ConvertTo-DecisionJson -Value $Value
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    $json | ConvertFrom-Json -DateKind String
  } else {
    $json | ConvertFrom-Json
  }
}

function Assert-NoForbiddenDecisionFields {
  param([Parameter(Mandatory = $true)]$Value)

  $json = ConvertTo-DecisionJson -Value $Value
  foreach ($field in $script:ForbiddenFields) {
    if ($json -match ('(?i)"' + [regex]::Escape($field) + '"\s*:')) {
      throw 'decision_invalid: forbidden field'
    }
  }
}

function Assert-SafeDisplayText {
  param(
    [Parameter(Mandatory = $true)][string]$Value,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '[\x00-\x1f\x7f]') {
    throw "decision_invalid: $Label must be non-empty safe text"
  }
}

function Get-ObjectPropertyValue {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string]$Name,
    [switch]$Required
  )

  if ($Value -is [Collections.IDictionary]) {
    if ($Value.Contains($Name)) {
      return $Value[$Name]
    }
  } else {
    $property = $Value.PSObject.Properties[$Name]
    if ($null -ne $property) {
      return $property.Value
    }
  }
  if ($Required) {
    throw "decision_invalid: missing $Name"
  }
  $null
}

function Initialize-PrivateDecisionRoot {
  param([Parameter(Mandatory = $true)][string]$RunRoot)

  if (-not [IO.Path]::IsPathFullyQualified($RunRoot)) {
    throw 'decision_invalid: run root must be absolute'
  }
  $fullRoot = [IO.Path]::GetFullPath($RunRoot)
  [IO.Directory]::CreateDirectory($fullRoot) | Out-Null
  Set-PrivatePathAcl -Path $fullRoot -Directory
  Assert-PrivatePathAcl -Path $fullRoot -Directory
  foreach ($name in @('decisions', 'bridge', 'requests')) {
    $child = Join-Path $fullRoot $name
    [IO.Directory]::CreateDirectory($child) | Out-Null
    Set-PrivatePathAcl -Path $child -Directory
    Assert-PrivatePathAcl -Path $child -Directory
  }
  $fullRoot
}

function Write-PrivateDecisionJson {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)]$Value
  )

  $fullPath = [IO.Path]::GetFullPath($Path)
  $parent = Split-Path -Parent $fullPath
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    throw 'decision_invalid: private parent is missing'
  }
  $tempPath = Join-Path $parent ('.' + [IO.Path]::GetFileName($fullPath) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  $backupPath = Join-Path $parent ('.' + [IO.Path]::GetFileName($fullPath) + '.' + [guid]::NewGuid().ToString('N') + '.bak')
  try {
    [IO.File]::WriteAllText($tempPath, (ConvertTo-DecisionJson -Value $Value) + "`n", [Text.UTF8Encoding]::new($false))
    Set-PrivatePathAcl -Path $tempPath
    Assert-PrivatePathAcl -Path $tempPath
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
      [IO.File]::Replace($tempPath, $fullPath, $backupPath, $true)
      Assert-PrivatePathAcl -Path $backupPath
      [IO.File]::Delete($backupPath)
    } else {
      [IO.File]::Move($tempPath, $fullPath)
    }
    Assert-PrivatePathAcl -Path $fullPath
  } finally {
    if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
      [IO.File]::Delete($tempPath)
    }
    if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
      [IO.File]::Delete($backupPath)
    }
  }
}

function Read-PrivateDecisionJson {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw 'decision_invalid: private decision file is missing'
  }
  Assert-PrivatePathAcl -Path $Path
  $bytes = [IO.File]::ReadAllBytes($Path)
  if ($bytes.Length -gt 1024 * 1024) {
    throw 'decision_invalid: private decision file is too large'
  }
  try {
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
      $text | ConvertFrom-Json -DateKind String
    } else {
      $text | ConvertFrom-Json
    }
  } catch {
    throw 'decision_invalid: private decision file is invalid'
  }
}

function Test-ExactObjectProperties {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string[]]$Names
  )

  if ($null -eq $Value -or $Value -is [Collections.IDictionary]) {
    return $false
  }
  $actual = @($Value.PSObject.Properties.Name | Sort-Object)
  $expected = @($Names | Sort-Object)
  ($actual -join '|') -ceq ($expected -join '|')
}

function ConvertTo-FeishuExactIso {
  param([Parameter(Mandatory = $true)][string]$Value)

  try {
    [DateTimeOffset]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime.ToString(
      "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
      [Globalization.CultureInfo]::InvariantCulture
    )
  } catch {
    throw 'decision_invalid: malformed decision lifetime'
  }
}

function Get-FeishuBridgeStateRoot {
  $configuredPath = [Environment]::GetEnvironmentVariable('FEISHU_DECISION_CONFIG_PATH')
  $configPath = if ($null -eq $configuredPath) {
    Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.codex\automation-state\tzg-hourly-controller.feishu.private.json'
  } else {
    $configuredPath
  }
  if ([string]::IsNullOrWhiteSpace($configPath) -or -not [IO.Path]::IsPathFullyQualified($configPath)) {
    throw 'decision_invalid: Feishu private config is invalid'
  }
  $fullConfigPath = [IO.Path]::GetFullPath($configPath)
  if (-not (Test-Path -LiteralPath $fullConfigPath -PathType Leaf)) {
    throw 'decision_invalid: Feishu private config is invalid'
  }
  $bytes = [IO.File]::ReadAllBytes($fullConfigPath)
  if ($bytes.Length -gt 64KB) {
    throw 'decision_invalid: Feishu private config is invalid'
  }
  try {
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
      $config = $text | ConvertFrom-Json -DateKind String
    } else {
      $config = $text | ConvertFrom-Json
    }
  } catch {
    throw 'decision_invalid: Feishu private config is invalid'
  }
  $configKeys = @(
    'schemaVersion', 'appId', 'appSecret', 'recipient', 'expectedTenantKey',
    'pairedOperatorOpenIdHash', 'hmacKey', 'stateRoot'
  )
  if (-not (Test-ExactObjectProperties -Value $config -Names $configKeys) -or
      [int]$config.schemaVersion -ne 1 -or
      [string]::IsNullOrWhiteSpace([string]$config.stateRoot) -or
      -not [IO.Path]::IsPathFullyQualified([string]$config.stateRoot)) {
    throw 'decision_invalid: Feishu private config is invalid'
  }
  $stateRoot = [IO.Path]::GetFullPath([string]$config.stateRoot)
  [IO.Directory]::CreateDirectory($stateRoot) | Out-Null
  Set-PrivatePathAcl -Path $stateRoot -Directory
  Assert-PrivatePathAcl -Path $stateRoot -Directory
  $stateRoot
}

function New-FeishuPendingRecord {
  param(
    [Parameter(Mandatory = $true)]$Decision,
    [Parameter(Mandatory = $true)]$Evidence
  )

  foreach ($hashName in @('providerMessageIdHash', 'providerChatIdHash', 'cardNonceHash')) {
    $hashValue = [string](Get-ObjectPropertyValue -Value $Evidence -Name $hashName -Required)
    if ($hashValue -notmatch '^[0-9a-f]{64}$') {
      throw 'decision_invalid: bridge accepted malformed evidence'
    }
  }
  [pscustomobject][ordered]@{
    decisionId = [string]$Decision.decisionId
    allowedOptions = @('A', 'B', 'C')
    allowCustomReply = $true
    createdAt = ConvertTo-FeishuExactIso -Value ([string]$Decision.createdAt)
    expiresAt = ConvertTo-FeishuExactIso -Value ([string]$Decision.expiresAt)
    cardNonceHash = [string]$Evidence.cardNonceHash
    providerMessageIdHash = [string]$Evidence.providerMessageIdHash
    providerChatIdHash = [string]$Evidence.providerChatIdHash
  }
}

function Write-FeishuCallbackBinding {
  param([Parameter(Mandatory = $true)]$Pending)

  $binding = [pscustomobject][ordered]@{
    kind = 'decision_reply'
    decisionId = [string]$Pending.decisionId
    allowedOptions = @($Pending.allowedOptions)
    allowCustomReply = [bool]$Pending.allowCustomReply
    issuedAt = [string]$Pending.createdAt
    expiresAt = [string]$Pending.expiresAt
    cardNonceHash = [string]$Pending.cardNonceHash
    providerMessageIdHash = [string]$Pending.providerMessageIdHash
    providerChatIdHash = [string]$Pending.providerChatIdHash
  }
  $bindingPath = Join-Path (Get-FeishuBridgeStateRoot) 'pending-bindings.json'
  Write-PrivateDecisionJson -Path $bindingPath -Value (,$binding)
}

function Get-DecisionRecordPath {
  param(
    [Parameter(Mandatory = $true)][string]$RunRoot,
    [Parameter(Mandatory = $true)][string]$DecisionId
  )

  Join-Path $RunRoot "decisions\$DecisionId.json"
}

function Get-BridgePendingPath {
  param(
    [Parameter(Mandatory = $true)][string]$RunRoot,
    [Parameter(Mandatory = $true)][string]$DecisionId
  )

  Join-Path $RunRoot "bridge\$DecisionId.pending.json"
}

function Assert-DecisionShape {
  param([Parameter(Mandatory = $true)]$Decision)

  $decisionId = [string](Get-ObjectPropertyValue -Value $Decision -Name 'decisionId' -Required)
  $taskId = [string](Get-ObjectPropertyValue -Value $Decision -Name 'taskId' -Required)
  $question = [string](Get-ObjectPropertyValue -Value $Decision -Name 'question' -Required)
  $options = @(Get-ObjectPropertyValue -Value $Decision -Name 'options' -Required)
  $recommended = [string](Get-ObjectPropertyValue -Value $Decision -Name 'recommendedOption' -Required)
  $impactSummary = [string](Get-ObjectPropertyValue -Value $Decision -Name 'impactSummary' -Required)
  $createdAt = [string](Get-ObjectPropertyValue -Value $Decision -Name 'createdAt' -Required)
  $expiresAt = [string](Get-ObjectPropertyValue -Value $Decision -Name 'expiresAt' -Required)
  if ($decisionId -notmatch '^DEC-[0-9]{8}-[A-Z0-9]+$' -or
      $taskId -notmatch '^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$' -or
      $options.Count -ne 3 -or
      $recommended -cnotin @('A', 'B', 'C')) {
    throw 'decision_invalid: malformed decision'
  }
  Assert-SafeDisplayText -Value $question -Label 'question'
  Assert-SafeDisplayText -Value $impactSummary -Label 'impactSummary'
  if (-not [datetime]::TryParseExact($createdAt, 'o', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]([datetime]$created = [datetime]::MinValue)) -or
      -not [datetime]::TryParseExact($expiresAt, 'o', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]([datetime]$expires = [datetime]::MinValue)) -or
      $created.ToUniversalTime() -gt $expires.ToUniversalTime()) {
    throw 'decision_invalid: malformed decision lifetime'
  }
  for ($index = 0; $index -lt 3; $index++) {
    $option = $options[$index]
    $key = [string](Get-ObjectPropertyValue -Value $option -Name 'key' -Required)
    $text = [string](Get-ObjectPropertyValue -Value $option -Name 'text' -Required)
    $scope = Get-ObjectPropertyValue -Value $option -Name 'scopeContract' -Required
    if ($key -cne @('A', 'B', 'C')[$index] -or $null -eq $scope) {
      throw 'decision_invalid: malformed options'
    }
    Assert-SafeDisplayText -Value $text -Label "option $key"
  }
  Assert-NoForbiddenDecisionFields -Value $Decision
}

function New-DecisionAdapterResult {
  param(
    [Parameter(Mandatory = $true)]$Decision,
    [Parameter(Mandatory = $true)][bool]$Ok,
    [Parameter(Mandatory = $true)][string]$Phase,
    [Parameter(Mandatory = $true)][string]$NextAction,
    [AllowNull()]$ErrorCode = $null,
    [AllowNull()]$Resolution = $null
  )

  [pscustomobject][ordered]@{
    ok = $Ok
    decisionId = [string]$Decision.decisionId
    taskId = [string]$Decision.taskId
    phase = $Phase
    nextAction = $NextAction
    errorCode = $ErrorCode
    authorized = $false
    requiresManifestApproval = $Phase -ceq 'IMPLEMENTATION_PENDING'
    resolutionKind = if ($null -eq $Resolution) { $null } else { [string]$Resolution.resolutionKind }
    selectedOptionId = if ($null -eq $Resolution) { $null } else { $Resolution.selectedOptionId }
    resolutionText = if ($null -eq $Resolution) { $null } else { [string]$Resolution.resolutionText }
    scopeContract = if ($null -eq $Resolution) { $null } else { $Resolution.scopeContract }
  }
}

function Invoke-DecisionBridge {
  param(
    [Parameter(Mandatory = $true)][ValidateSet('send-decision.mjs', 'consume-reply.mjs')][string]$EntryPoint,
    [Parameter(Mandatory = $true)][string]$RequestPath,
    [Parameter(Mandatory = $true)][string]$BridgeRoot
  )

  if (-not [IO.Path]::IsPathFullyQualified($BridgeRoot)) {
    return [pscustomobject]@{ exitCode = 20; payload = $null }
  }
  $scriptPath = Join-Path ([IO.Path]::GetFullPath($BridgeRoot)) "src\$EntryPoint"
  if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    return [pscustomobject]@{ exitCode = 20; payload = $null }
  }
  $output = @(& node $scriptPath '--request-file' ([IO.Path]::GetFullPath($RequestPath)) 2>&1)
  $exitCode = $LASTEXITCODE
  $payload = $null
  if ($output.Count -eq 1) {
    try {
      $payload = ([string]$output[0]) | ConvertFrom-Json
    } catch {
      $payload = $null
    }
  }
  [pscustomobject]@{ exitCode = $exitCode; payload = $payload }
}

function New-DecisionRequest {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$TaskId,
    [Parameter(Mandatory = $true)][string]$Question,
    [Parameter(Mandatory = $true)][object[]]$Options
  )

  if ($TaskId -notmatch '^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$' -or $Options.Count -ne 3) {
    throw 'decision_invalid: task and exactly three options are required'
  }
  Assert-SafeDisplayText -Value $Question -Label 'question'
  $snapshots = @()
  $recommended = @()
  for ($index = 0; $index -lt 3; $index++) {
    $option = $Options[$index]
    $key = [string](Get-ObjectPropertyValue -Value $option -Name 'key' -Required)
    $text = [string](Get-ObjectPropertyValue -Value $option -Name 'text' -Required)
    $scope = Get-ObjectPropertyValue -Value $option -Name 'scopeContract' -Required
    $isRecommended = [bool](Get-ObjectPropertyValue -Value $option -Name 'recommended')
    if ($key -cne @('A', 'B', 'C')[$index] -or $null -eq $scope) {
      throw 'decision_invalid: options must be ordered A, B, C with scope contracts'
    }
    Assert-SafeDisplayText -Value $text -Label "option $key"
    if ($isRecommended) {
      $recommended += $key
    }
    $snapshots += [pscustomobject][ordered]@{
      key = $key
      text = $text
      scopeContract = Copy-DecisionValue -Value $scope
    }
  }
  if ($recommended.Count -ne 1) {
    throw 'decision_invalid: exactly one option must be recommended'
  }
  $now = [datetime]::UtcNow
  $decisionId = 'DEC-' + $now.ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 12).ToUpperInvariant()
  $impactSummary = (@($snapshots | ForEach-Object { "$($_.key)：$($_.text)" }) -join '；') + "；可复制回复：$decisionId：自定义 <你的方案>"
  $decision = [pscustomobject][ordered]@{
    schemaVersion = 1
    decisionId = $decisionId
    taskId = $TaskId
    question = $Question
    options = $snapshots
    recommendedOption = $recommended[0]
    impactSummary = $impactSummary
    createdAt = $now.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    expiresAt = $now.AddHours(24).ToString('o', [Globalization.CultureInfo]::InvariantCulture)
  }
  Assert-DecisionShape -Decision $decision
  $decision
}

function Send-DecisionRequest {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$Decision,
    [Parameter(Mandatory = $true)][string]$RunRoot,
    [Parameter(Mandatory = $true)][string]$BridgeRoot
  )

  Assert-DecisionShape -Decision $Decision
  $privateRoot = Initialize-PrivateDecisionRoot -RunRoot $RunRoot
  $recordPath = Get-DecisionRecordPath -RunRoot $privateRoot -DecisionId $Decision.decisionId
  if (Test-Path -LiteralPath $recordPath -PathType Leaf) {
    $record = Read-PrivateDecisionJson -Path $recordPath
    if ((ConvertTo-DecisionJson -Value $record.decision) -cne (ConvertTo-DecisionJson -Value $Decision)) {
      throw 'decision_invalid: frozen decision changed'
    }
    if ($null -ne $record.resolution) {
      return New-DecisionAdapterResult -Decision $record.decision -Ok $true -Phase 'IMPLEMENTATION_PENDING' -NextAction 'SubmitManifest' -Resolution $record.resolution
    }
    $pendingPath = Get-BridgePendingPath -RunRoot $privateRoot -DecisionId $Decision.decisionId
    if (Test-Path -LiteralPath $pendingPath -PathType Leaf) {
      try {
        $pending = New-FeishuPendingRecord -Decision $Decision -Evidence (Read-PrivateDecisionJson -Path $pendingPath)
        Write-PrivateDecisionJson -Path $pendingPath -Value $pending
        Write-FeishuCallbackBinding -Pending $pending
      } catch {
        return New-DecisionAdapterResult -Decision $Decision -Ok $false -Phase 'WAITING_DECISION' -NextAction 'SendDecision' -ErrorCode 'feishu_unavailable'
      }
      return New-DecisionAdapterResult -Decision $record.decision -Ok $true -Phase 'WAITING_DECISION' -NextAction 'ConsumeDecisionReply'
    }
  } else {
    $record = [pscustomobject][ordered]@{
      schemaVersion = 1
      decision = Copy-DecisionValue -Value $Decision
      phase = 'WAITING_DECISION'
      sentAt = $null
      resolvedAt = $null
      resolution = $null
    }
    Write-PrivateDecisionJson -Path $recordPath -Value $record
  }

  $bridgeDecision = [pscustomobject][ordered]@{
    decisionId = [string]$Decision.decisionId
    taskId = [string]$Decision.taskId
    question = [string]$Decision.question
    options = @($Decision.options | ForEach-Object {
        [pscustomobject][ordered]@{ key = [string]$_.key; label = [string]$_.text }
      })
    recommendedOption = [string]$Decision.recommendedOption
    impactSummary = [string]$Decision.impactSummary
  }
  $request = [pscustomobject][ordered]@{ attemptNumber = 1; decision = $bridgeDecision }
  $requestPath = Join-Path $privateRoot "requests\$($Decision.decisionId).send.json"
  Write-PrivateDecisionJson -Path $requestPath -Value $request
  $bridgeResult = Invoke-DecisionBridge -EntryPoint 'send-decision.mjs' -RequestPath $requestPath -BridgeRoot $BridgeRoot
  if ($bridgeResult.exitCode -ne 0 -or $null -eq $bridgeResult.payload -or $bridgeResult.payload.result -cne 'PROVIDER_ACCEPTED') {
    return New-DecisionAdapterResult -Decision $Decision -Ok $false -Phase 'WAITING_DECISION' -NextAction 'SendDecision' -ErrorCode 'feishu_unavailable'
  }
  $pending = New-FeishuPendingRecord -Decision $Decision -Evidence $bridgeResult.payload
  Write-PrivateDecisionJson -Path (Get-BridgePendingPath -RunRoot $privateRoot -DecisionId $Decision.decisionId) -Value $pending
  $record.sentAt = [datetime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
  Write-PrivateDecisionJson -Path $recordPath -Value $record
  try {
    Write-FeishuCallbackBinding -Pending $pending
  } catch {
    return New-DecisionAdapterResult -Decision $Decision -Ok $false -Phase 'WAITING_DECISION' -NextAction 'SendDecision' -ErrorCode 'feishu_unavailable'
  }
  New-DecisionAdapterResult -Decision $Decision -Ok $true -Phase 'WAITING_DECISION' -NextAction 'ConsumeDecisionReply'
}

function Consume-DecisionReply {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$Decision,
    [Parameter(Mandatory = $true)][string]$RunRoot,
    [Parameter(Mandatory = $true)][string]$BridgeRoot
  )

  Assert-DecisionShape -Decision $Decision
  $privateRoot = Initialize-PrivateDecisionRoot -RunRoot $RunRoot
  $recordPath = Get-DecisionRecordPath -RunRoot $privateRoot -DecisionId $Decision.decisionId
  if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) {
    throw 'decision_invalid: decision was not sent'
  }
  $record = Read-PrivateDecisionJson -Path $recordPath
  if ((ConvertTo-DecisionJson -Value $record.decision) -cne (ConvertTo-DecisionJson -Value $Decision)) {
    throw 'decision_invalid: frozen decision changed'
  }
  if ($null -ne $record.resolution) {
    return New-DecisionAdapterResult -Decision $record.decision -Ok $true -Phase 'IMPLEMENTATION_PENDING' -NextAction 'SubmitManifest' -Resolution $record.resolution
  }
  $pendingPath = Get-BridgePendingPath -RunRoot $privateRoot -DecisionId $Decision.decisionId
  if (-not (Test-Path -LiteralPath $pendingPath -PathType Leaf)) {
    return New-DecisionAdapterResult -Decision $Decision -Ok $false -Phase 'WAITING_DECISION' -NextAction 'SendDecision' -ErrorCode 'feishu_unavailable'
  }
  $pending = Read-PrivateDecisionJson -Path $pendingPath
  $request = [pscustomobject][ordered]@{ pendingDecision = $pending }
  $requestPath = Join-Path $privateRoot "requests\$($Decision.decisionId).consume.json"
  Write-PrivateDecisionJson -Path $requestPath -Value $request
  $bridgeResult = Invoke-DecisionBridge -EntryPoint 'consume-reply.mjs' -RequestPath $requestPath -BridgeRoot $BridgeRoot
  if ($bridgeResult.exitCode -ne 0 -or $null -eq $bridgeResult.payload) {
    return New-DecisionAdapterResult -Decision $Decision -Ok $false -Phase 'WAITING_DECISION' -NextAction 'ConsumeDecisionReply' -ErrorCode 'feishu_unavailable'
  }
  if ($bridgeResult.payload.result -ceq 'NO_REPLY') {
    return New-DecisionAdapterResult -Decision $Decision -Ok $true -Phase 'WAITING_DECISION' -NextAction 'ConsumeDecisionReply' -ErrorCode 'decision_pending'
  }

  $resolution = $null
  if ($bridgeResult.payload.result -ceq 'OPTION_ACCEPTED') {
    $optionKey = [string](Get-ObjectPropertyValue -Value $bridgeResult.payload -Name 'optionKey' -Required)
    $option = @($record.decision.options | Where-Object { $_.key -ceq $optionKey })
    if ($option.Count -ne 1) {
      throw 'decision_invalid: bridge selected an unknown option'
    }
    $resolution = [pscustomobject][ordered]@{
      resolutionKind = 'OPTION'
      selectedOptionId = $optionKey
      resolutionText = [string]$option[0].text
      scopeContract = Copy-DecisionValue -Value $option[0].scopeContract
      source = 'feishu_card'
    }
  } elseif ($bridgeResult.payload.result -ceq 'CUSTOM_ACCEPTED') {
    $customText = [string](Get-ObjectPropertyValue -Value $bridgeResult.payload -Name 'customText' -Required)
    Assert-SafeDisplayText -Value $customText -Label 'custom reply'
    $resolution = [pscustomobject][ordered]@{
      resolutionKind = 'CUSTOM'
      selectedOptionId = $null
      resolutionText = $customText
      scopeContract = [pscustomobject][ordered]@{
        expectedPaths = @()
        requiredChecks = @()
      }
      source = [string](Get-ObjectPropertyValue -Value $bridgeResult.payload -Name 'source' -Required)
    }
  } else {
    return New-DecisionAdapterResult -Decision $Decision -Ok $false -Phase 'WAITING_DECISION' -NextAction 'ConsumeDecisionReply' -ErrorCode 'feishu_unavailable'
  }

  $record.phase = 'IMPLEMENTATION_PENDING'
  $record.resolvedAt = [datetime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
  $record.resolution = $resolution
  Write-PrivateDecisionJson -Path $recordPath -Value $record
  New-DecisionAdapterResult -Decision $record.decision -Ok $true -Phase 'IMPLEMENTATION_PENDING' -NextAction 'SubmitManifest' -Resolution $resolution
}

function ConvertTo-SafeBodyText {
  param([Parameter(Mandatory = $true)][string]$Value)

  (($Value -replace '[\x00-\x1f\x7f]+', ' ') -replace '\s+', ' ').Trim()
}

function New-ManifestApprovalDecision {
  [CmdletBinding()]
  param([Parameter(Mandatory = $true)]$Manifest)

  Assert-NoForbiddenDecisionFields -Value $Manifest
  $taskId = [string](Get-ObjectPropertyValue -Value $Manifest -Name 'taskId' -Required)
  $expectedPaths = @(Get-ObjectPropertyValue -Value $Manifest -Name 'expectedPaths' -Required)
  $intendedChanges = @(Get-ObjectPropertyValue -Value $Manifest -Name 'intendedChanges' -Required)
  $coverage = @(Get-ObjectPropertyValue -Value $Manifest -Name 'decisionCoverage' -Required)
  $checks = @(Get-ObjectPropertyValue -Value $Manifest -Name 'requiredChecks' -Required)
  if ($taskId -notmatch '^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$' -or
      $expectedPaths.Count -eq 0 -or $coverage.Count -ne 5 -or $checks.Count -eq 0) {
    throw 'decision_invalid: incomplete manifest approval input'
  }
  $pathBody = @($expectedPaths | ForEach-Object { [string]$_ }) -join '、'
  $intentBody = @($intendedChanges | ForEach-Object {
      ([string](Get-ObjectPropertyValue -Value $_ -Name 'path' -Required)) + '：' +
      ([string](Get-ObjectPropertyValue -Value $_ -Name 'summary' -Required))
    }) -join '；'
  $decisionBody = @($coverage | ForEach-Object {
      ([string](Get-ObjectPropertyValue -Value $_ -Name 'decisionId' -Required)) + '：' +
      ([string](Get-ObjectPropertyValue -Value $_ -Name 'resolutionText' -Required)) + '；实现：' +
      ([string](Get-ObjectPropertyValue -Value $_ -Name 'implementation' -Required))
    }) -join '；'
  $checkBody = @($checks | ForEach-Object { [string]$_ }) -join '、'
  $provisional = New-DecisionRequest -TaskId $taskId -Question '准备清单批准正文。' -Options @(
    [ordered]@{
      key = 'A'; text = '批准该 plan-only 清单；批准后仍只允许按冻结范围生成写入授权。'; recommended = $true
      scopeContract = [ordered]@{
        expectedPaths = @($expectedPaths)
        decisionIds = @($coverage | ForEach-Object { [string]$_.decisionId })
        requiredChecks = @($checks)
      }
    },
    [ordered]@{
      key = 'B'; text = '拒绝该清单；本次不允许写入。'; recommended = $false
      scopeContract = [ordered]@{ expectedPaths = @(); decisionIds = @(); requiredChecks = @() }
    },
    [ordered]@{
      key = 'C'; text = '要求修改清单；保持 IMPLEMENTATION_PENDING，修订后必须再次批准。'; recommended = $false
      scopeContract = [ordered]@{ expectedPaths = @(); decisionIds = @(); requiredChecks = @() }
    }
  )
  $provisional.question = ConvertTo-SafeBodyText -Value (
    "任务 $taskId 的 plan-only 清单请求批准；路径摘要（$($expectedPaths.Count)）：$pathBody；" +
    "逐组改动意图：$intentBody；五项决定覆盖：$decisionBody；requiredChecks：$checkBody；" +
    "批准后才允许首次写入；回复格式：$($provisional.decisionId)：选择 A；" +
    "$($provisional.decisionId)：自定义 <你的方案>"
  )
  Assert-DecisionShape -Decision $provisional
  $provisional
}

Export-ModuleMember -Function @(
  'New-DecisionRequest',
  'Send-DecisionRequest',
  'Consume-DecisionReply',
  'New-ManifestApprovalDecision'
)
