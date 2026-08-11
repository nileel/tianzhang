#requires -Version 7.0

param(
  [Parameter(Mandatory = $true, Position = 0)]
  [ValidateSet('Configure', 'Pair', 'Canary', 'CanaryCardCustom', 'CanaryTextCustom', 'ShowSanitized')]
  [string]$Action,

  [string]$ConfigPath = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller.feishu.private.json'),

  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-feishu-decision-bridge'),

  [hashtable]$ConfigValues,

  [ValidateRange(1, 900)]
  [int]$PairTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'private-path-acl.ps1')
$script:StateRootExplicit = $PSBoundParameters.ContainsKey('StateRoot')
$script:ConfigPathExplicit = $PSBoundParameters.ContainsKey('ConfigPath')
$script:ExpectedConfigKeys = @(
  'schemaVersion', 'appId', 'appSecret', 'recipient', 'expectedTenantKey',
  'pairedOperatorOpenIdHash', 'hmacKey', 'stateRoot'
)
$script:ExpectedRecipientKeys = @('type', 'value')
$script:HexPattern = '^[0-9a-f]{64}$'
$script:IdentifierPattern = '^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$'

function Get-Sha256 {
  param([string]$Value)

  if ($null -eq $Value) { throw 'Invalid setup value' }
  $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
  try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
  finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Test-ExactKeys {
  param([Collections.IDictionary]$Value, [string[]]$Expected)

  if ($null -eq $Value -or $Value.Count -ne $Expected.Count) { return $false }
  foreach ($key in $Expected) {
    if (-not $Value.Contains($key)) { return $false }
  }
  return $true
}

function Test-NonEmptySafeString {
  param($Value, [int]$MaximumLength = 4096)

  return $Value -is [string] -and
    $Value.Length -gt 0 -and
    $Value.Length -le $MaximumLength -and
    -not [string]::IsNullOrWhiteSpace($Value) -and
    $Value -notmatch '[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]'
}

function Convert-JsonToHashtable {
  param([string]$Json)

  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    return $Json | ConvertFrom-Json -AsHashtable -DateKind String
  }
  return $Json | ConvertFrom-Json -AsHashtable
}

function Resolve-AbsolutePath {
  param([string]$Path, [string]$Label)

  if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Label is invalid" }
  $resolved = [IO.Path]::GetFullPath($Path)
  if (-not [IO.Path]::IsPathFullyQualified($resolved)) { throw "$Label is invalid" }
  return $resolved
}

function Assert-TestInjectionSafe {
  if ($null -eq $ConfigValues) { return }
  if (-not $script:StateRootExplicit -or -not $script:ConfigPathExplicit) {
    throw 'ConfigValues requires explicit temporary paths'
  }
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
  $prefix = $tempRoot + [IO.Path]::DirectorySeparatorChar
  foreach ($candidate in @($StateRoot, $ConfigPath)) {
    $full = Resolve-AbsolutePath $candidate 'test path'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw 'ConfigValues is restricted to the current temporary directory'
    }
  }
}

function Initialize-PrivateDirectory {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Set-PrivatePathAcl -Path $Path -Directory
    return
  }
  $acl = Get-Acl -LiteralPath $Path
  $allowed = @((Get-PrivateAclSids).Value)
  $rules = @($acl.Access)
  if (-not $acl.AreAccessRulesProtected -or $rules.Count -ne 2) { throw 'Private state directory ACL is unsafe' }
  foreach ($rule in $rules) {
    $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    if (
      $sid -notin $allowed -or
      $rule.IsInherited -or
      $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
      ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl
    ) {
      throw 'Private state directory ACL is unsafe'
    }
  }
}

function Write-PrivateJsonAtomic {
  param([string]$Path, $Value)

  $fullPath = Resolve-AbsolutePath $Path 'private file path'
  $parent = Split-Path -Parent $fullPath
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
  }
  $temporaryPath = Join-Path $parent ('.' + [IO.Path]::GetFileName($fullPath) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  $json = ConvertTo-Json -InputObject $Value -Depth 12 -Compress
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
  try {
    New-Item -ItemType File -Path $temporaryPath -ErrorAction Stop | Out-Null
    Set-PrivatePathAcl -Path $temporaryPath
    $stream = [IO.FileStream]::new(
      $temporaryPath,
      [IO.FileMode]::Open,
      [IO.FileAccess]::Write,
      [IO.FileShare]::None,
      4096,
      [IO.FileOptions]::WriteThrough
    )
    try {
      $stream.Write($bytes, 0, $bytes.Length)
      $stream.Flush($true)
    } finally {
      $stream.Dispose()
    }
    [IO.File]::Move($temporaryPath, $fullPath, $true)
    Assert-PrivatePathAcl $fullPath
  } finally {
    [Array]::Clear($bytes, 0, $bytes.Length)
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
  }
}

function Read-PrivateConfig {
  param([string]$Path)

  $fullPath = Resolve-AbsolutePath $Path 'ConfigPath'
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw 'Private configuration is missing' }
  Assert-PrivatePathAcl $fullPath
  $file = Get-Item -LiteralPath $fullPath
  if ($file.Length -gt 64KB) { throw 'Private configuration is invalid' }
  try {
    $config = Convert-JsonToHashtable ([IO.File]::ReadAllText($fullPath, [Text.UTF8Encoding]::new($false, $true)))
  } catch {
    throw 'Private configuration is invalid'
  }
  if (-not (Test-ExactKeys $config $script:ExpectedConfigKeys)) { throw 'Private configuration is invalid' }
  if (-not (Test-ExactKeys $config.recipient $script:ExpectedRecipientKeys)) { throw 'Private configuration is invalid' }
  if (
    $config.schemaVersion -ne 1 -or
    -not (Test-NonEmptySafeString $config.appId 512) -or
    -not (Test-NonEmptySafeString $config.appSecret 4096) -or
    $config.recipient.type -notin @('email', 'open_id') -or
    -not (Test-NonEmptySafeString $config.recipient.value 512) -or
    ($null -ne $config.expectedTenantKey -and -not (Test-NonEmptySafeString $config.expectedTenantKey 512)) -or
    ($null -ne $config.pairedOperatorOpenIdHash -and [string]$config.pairedOperatorOpenIdHash -notmatch $script:HexPattern) -or
    -not (Test-NonEmptySafeString $config.hmacKey 128)
  ) {
    throw 'Private configuration is invalid'
  }
  try {
    $hmacBytes = [Convert]::FromBase64String([string]$config.hmacKey)
    if ($hmacBytes.Length -ne 32 -or [Convert]::ToBase64String($hmacBytes) -cne [string]$config.hmacKey) {
      throw 'invalid'
    }
  } catch {
    throw 'Private configuration is invalid'
  } finally {
    if ($null -ne $hmacBytes) { [Array]::Clear($hmacBytes, 0, $hmacBytes.Length) }
  }
  $config.stateRoot = Resolve-AbsolutePath ([string]$config.stateRoot) 'stateRoot'
  if ($script:StateRootExplicit) {
    $requested = Resolve-AbsolutePath $StateRoot 'StateRoot'
    if ($requested -cne $config.stateRoot) { throw 'StateRoot does not match private configuration' }
  }
  return $config
}

function Write-SanitizedJson {
  param($Value)

  Write-Output ($Value | ConvertTo-Json -Depth 8 -Compress)
}

function Get-InteractiveSecret {
  $secure = Read-Host 'Feishu App Secret' -AsSecureString
  $pointer = [IntPtr]::Zero
  try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
  } finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $secure.Dispose()
  }
}

function Get-ConfigureValues {
  if ($null -ne $ConfigValues) {
    if (-not (Test-ExactKeys $ConfigValues @('appId', 'appSecret', 'recipientType', 'recipientValue'))) {
      throw 'Configure test values are invalid'
    }
    return @{
      appId = $ConfigValues.appId
      appSecret = $ConfigValues.appSecret
      recipientType = $ConfigValues.recipientType
      recipientValue = $ConfigValues.recipientValue
    }
  }
  return @{
    appId = Read-Host 'Feishu App ID'
    appSecret = Get-InteractiveSecret
    recipientType = Read-Host 'Recipient type (email/open_id)'
    recipientValue = Read-Host 'Recipient value'
  }
}

function Invoke-Configure {
  $values = Get-ConfigureValues
  if (
    -not (Test-NonEmptySafeString $values.appId 512) -or
    -not (Test-NonEmptySafeString $values.appSecret 4096) -or
    $values.recipientType -notin @('email', 'open_id') -or
    -not (Test-NonEmptySafeString $values.recipientValue 512)
  ) {
    throw 'Configure values are invalid'
  }
  $resolvedConfigPath = Resolve-AbsolutePath $ConfigPath 'ConfigPath'
  $resolvedStateRoot = Resolve-AbsolutePath $StateRoot 'StateRoot'
  Initialize-PrivateDirectory $resolvedStateRoot
  $keyBytes = [byte[]]::new(32)
  [Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
  try { $hmacKey = [Convert]::ToBase64String($keyBytes) }
  finally { [Array]::Clear($keyBytes, 0, $keyBytes.Length) }
  $config = [ordered]@{
    schemaVersion = 1
    appId = [string]$values.appId
    appSecret = [string]$values.appSecret
    recipient = [ordered]@{ type = [string]$values.recipientType; value = [string]$values.recipientValue }
    expectedTenantKey = $null
    pairedOperatorOpenIdHash = $null
    hmacKey = $hmacKey
    stateRoot = $resolvedStateRoot
  }
  Write-PrivateJsonAtomic $resolvedConfigPath $config
  Write-SanitizedJson ([ordered]@{
    result = 'CONFIGURED'
    schemaVersion = 1
    appIdHash = Get-Sha256 $config.appId
    recipientType = $config.recipient.type
    recipientHash = Get-Sha256 $config.recipient.value
    paired = $false
  })
}

function Convert-PairingPayloadToCanonicalJson {
  param([Collections.IDictionary]$Payload)

  $ordered = [ordered]@{}
  foreach ($key in @($Payload.Keys | Sort-Object)) { $ordered[$key] = $Payload[$key] }
  return $ordered | ConvertTo-Json -Compress
}

function Read-ValidPairingEnvelope {
  param(
    [string]$Path,
    [string]$ExpectedNonceHash,
    [string]$HmacKey,
    [datetime]$ExpiresAt
  )

  try {
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -gt 64KB -or $item.Name -notmatch '^([0-9a-f]{64})\.json$') { return $null }
    $eventHash = $Matches[1]
    $envelope = Convert-JsonToHashtable ([IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)))
    if (-not (Test-ExactKeys $envelope @('schemaVersion', 'payload', 'signature')) -or $envelope.schemaVersion -ne 1) { return $null }
    $payloadKeys = @('kind', 'pairingNonceHash', 'providerEventIdHash', 'operatorOpenIdHash', 'tenantKey', 'tenantKeyHash', 'receivedAt')
    if (-not (Test-ExactKeys $envelope.payload $payloadKeys)) { return $null }
    $payload = $envelope.payload
    if (
      $payload.kind -cne 'operator_pairing' -or
      [string]$payload.pairingNonceHash -cne $ExpectedNonceHash -or
      [string]$payload.providerEventIdHash -cne $eventHash -or
      [string]$payload.providerEventIdHash -notmatch $script:HexPattern -or
      [string]$payload.operatorOpenIdHash -notmatch $script:HexPattern -or
      [string]$payload.tenantKeyHash -notmatch $script:HexPattern -or
      [string]$envelope.signature -notmatch $script:HexPattern -or
      -not (Test-NonEmptySafeString $payload.tenantKey 512) -or
      [string]$payload.tenantKey -notmatch $script:IdentifierPattern -or
      (Get-Sha256 ([string]$payload.tenantKey)) -cne [string]$payload.tenantKeyHash
    ) {
      return $null
    }
    $received = [DateTimeOffset]::Parse([string]$payload.receivedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
    $canonicalReceived = $received.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    if ($canonicalReceived -cne [string]$payload.receivedAt -or $received.UtcDateTime -gt $ExpiresAt) { return $null }
    $canonical = Convert-PairingPayloadToCanonicalJson $payload
    $keyBytes = [Convert]::FromBase64String($HmacKey)
    $hmac = [Security.Cryptography.HMACSHA256]::new($keyBytes)
    try {
      $expected = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))
      $actual = [Convert]::FromHexString([string]$envelope.signature)
      if (-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($expected, $actual)) { return $null }
    } finally {
      $hmac.Dispose()
      [Array]::Clear($keyBytes, 0, $keyBytes.Length)
      if ($null -ne $expected) { [Array]::Clear($expected, 0, $expected.Length) }
      if ($null -ne $actual) { [Array]::Clear($actual, 0, $actual.Length) }
    }
    return [pscustomobject]@{
      TenantKey = [string]$payload.tenantKey
      TenantKeyHash = [string]$payload.tenantKeyHash
      OperatorOpenIdHash = [string]$payload.operatorOpenIdHash
      ProviderEventIdHash = [string]$payload.providerEventIdHash
    }
  } catch {
    return $null
  }
}

function Invoke-NodeJson {
  param([string]$ScriptPath, [string]$RequestPath)

  $node = Get-Command node -ErrorAction SilentlyContinue
  if ($null -eq $node) { throw 'Node runtime is unavailable' }
  $oldConfig = [Environment]::GetEnvironmentVariable('FEISHU_DECISION_CONFIG_PATH', 'Process')
  try {
    [Environment]::SetEnvironmentVariable('FEISHU_DECISION_CONFIG_PATH', (Resolve-AbsolutePath $ConfigPath 'ConfigPath'), 'Process')
    $raw = @(& $node.Source $ScriptPath '--request-file' $RequestPath 2>&1)
    $code = $LASTEXITCODE
  } finally {
    [Environment]::SetEnvironmentVariable('FEISHU_DECISION_CONFIG_PATH', $oldConfig, 'Process')
  }
  $jsonLine = @($raw | ForEach-Object { [string]$_ } | Where-Object { $_.TrimStart().StartsWith('{') }) | Select-Object -Last 1
  if ([string]::IsNullOrWhiteSpace($jsonLine)) { throw 'Feishu helper returned no valid result' }
  try { $result = Convert-JsonToHashtable $jsonLine }
  catch { throw 'Feishu helper returned no valid result' }
  return [pscustomobject]@{ Code = $code; Result = $result }
}

function Invoke-Pair {
  $config = Read-PrivateConfig $ConfigPath
  Initialize-PrivateDirectory $config.stateRoot
  if ($null -ne $ConfigValues -and -not (Test-ExactKeys $ConfigValues @('pairingNonce', 'skipProviderSend'))) {
    throw 'Pair test values are invalid'
  }
  $pairingNonce = if ($null -ne $ConfigValues) { [string]$ConfigValues.pairingNonce } else {
    $bytes = [byte[]]::new(24)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    try { [Convert]::ToHexString($bytes).ToLowerInvariant() }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
  }
  if ($pairingNonce -notmatch $script:IdentifierPattern) { throw 'Pairing nonce is invalid' }
  $skipProviderSend = $null -ne $ConfigValues -and $ConfigValues.skipProviderSend -eq $true
  $startedAt = [datetime]::UtcNow
  $expiresAt = $startedAt.AddSeconds($PairTimeoutSeconds)
  $nonceHash = Get-Sha256 $pairingNonce
  $bindingPath = Join-Path $config.stateRoot 'pairing-binding.json'
  $requestPath = Join-Path $config.stateRoot ('.pairing-request-' + [guid]::NewGuid().ToString('N') + '.json')
  try {
    Write-PrivateJsonAtomic $bindingPath ([ordered]@{
      kind = 'operator_pairing'
      pairingNonceHash = $nonceHash
      expiresAt = $expiresAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    })
    if (-not $skipProviderSend) {
      Write-PrivateJsonAtomic $requestPath ([ordered]@{ pairingNonce = $pairingNonce })
      $helper = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\send-pairing.mjs'
      $send = Invoke-NodeJson $helper $requestPath
      if ($send.Result.result -notin @('PROVIDER_ACCEPTED', 'PROVIDER_OUTCOME_UNKNOWN')) {
        throw 'Pairing card was not accepted by Feishu'
      }
    }

    $inbox = Join-Path $config.stateRoot 'pairing-inbox'
    do {
      if (Test-Path -LiteralPath $inbox -PathType Container) {
        foreach ($file in @(Get-ChildItem -LiteralPath $inbox -Filter '*.json' -File | Sort-Object Name)) {
          $accepted = Read-ValidPairingEnvelope -Path $file.FullName -ExpectedNonceHash $nonceHash -HmacKey $config.hmacKey -ExpiresAt $expiresAt
          if ($null -ne $accepted) {
            $config.expectedTenantKey = $accepted.TenantKey
            $config.pairedOperatorOpenIdHash = $accepted.OperatorOpenIdHash
            Write-PrivateJsonAtomic (Resolve-AbsolutePath $ConfigPath 'ConfigPath') $config
            Write-SanitizedJson ([ordered]@{
              result = 'PAIRED'
              tenantKeyHash = $accepted.TenantKeyHash
              operatorOpenIdHash = $accepted.OperatorOpenIdHash
              providerEventIdHash = $accepted.ProviderEventIdHash
            })
            return
          }
        }
      }
      if ([datetime]::UtcNow -lt $expiresAt) { Start-Sleep -Milliseconds 200 }
    } while ([datetime]::UtcNow -lt $expiresAt)
    throw 'Pairing confirmation timed out'
  } finally {
    foreach ($path in @($bindingPath, $requestPath)) {
      if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
  }
}

function Remove-CanaryBinding {
  param([string]$Path, [string]$DecisionId)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
  try {
    $value = Convert-JsonToHashtable ([IO.File]::ReadAllText($Path))
    [object[]]$items = @($value)
    if ($items.Count -eq 1 -and [string]$items[0].decisionId -ceq $DecisionId) {
      Remove-Item -LiteralPath $Path -Force
    }
  } catch {
    # Never remove an unrecognized binding file.
  }
}

function Write-TextReplyHealth {
  param([Collections.IDictionary]$Config, [ValidateSet('TEXT_REPLY_READY', 'TEXT_REPLY_UNAVAILABLE')][string]$Status)

  Write-PrivateJsonAtomic (Join-Path $Config.stateRoot 'text-reply-health.json') ([ordered]@{
    schemaVersion = 1
    status = $Status
    updatedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
  })
}

function Invoke-Canary {
  param([ValidateSet('option', 'card_custom', 'text_custom')][string]$Mode = 'option')

  $config = Read-PrivateConfig $ConfigPath
  if (
    -not (Test-NonEmptySafeString $config.expectedTenantKey 512) -or
    [string]$config.pairedOperatorOpenIdHash -notmatch $script:HexPattern
  ) {
    throw 'Canary requires a paired operator'
  }
  if ($null -ne $ConfigValues -and -not (Test-ExactKeys $ConfigValues @('skipProviderSend'))) {
    throw 'Canary test values are invalid'
  }
  if ($null -ne $ConfigValues -and $ConfigValues.skipProviderSend -eq $true) {
    throw 'Canary provider bypass is not permitted after identity validation'
  }
  Initialize-PrivateDirectory $config.stateRoot
  $bindingPath = Join-Path $config.stateRoot 'pending-bindings.json'
  if (Test-Path -LiteralPath $bindingPath) { throw 'A pending decision binding already exists' }
  $decisionDate = [datetime]::UtcNow.ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture)
  $decisionId = 'DEC-' + $decisionDate + '-CANARY' + [guid]::NewGuid().ToString('N').ToUpperInvariant()
  $nonceBytes = [byte[]]::new(24)
  [Security.Cryptography.RandomNumberGenerator]::Fill($nonceBytes)
  try { $cardNonce = [Convert]::ToHexString($nonceBytes).ToLowerInvariant() }
  finally { [Array]::Clear($nonceBytes, 0, $nonceBytes.Length) }
  $createdAt = [datetime]::UtcNow
  $expiresAt = $createdAt.AddSeconds($PairTimeoutSeconds)
  $exactTextCommand = "$decisionId：自定义 CANARY_CUSTOM_OK"
  $sendRequestPath = Join-Path $config.stateRoot ('.canary-send-' + [guid]::NewGuid().ToString('N') + '.json')
  $consumeRequestPath = Join-Path $config.stateRoot ('.canary-consume-' + [guid]::NewGuid().ToString('N') + '.json')
  try {
    $decision = [ordered]@{
      decisionId = $decisionId
      taskId = 'FEISHU-CANARY'
      question = switch ($Mode) {
        'option' { '飞书决策通道金丝雀验证：请选择 A。' }
        'card_custom' { '飞书卡片自定义回复金丝雀验证：请在输入框填写 CANARY_CUSTOM_OK 并提交。' }
        'text_custom' { "飞书文字自定义回复金丝雀验证：请复制并发送：$exactTextCommand" }
      }
      options = @(
        [ordered]@{ key = 'A'; label = '确认通道正常' },
        [ordered]@{ key = 'B'; label = '不确认' },
        [ordered]@{ key = 'C'; label = '稍后处理' }
      )
      recommendedOption = 'A'
      impactSummary = '仅验证通知、身份与回执，不修改项目业务状态。'
      plainSummary = [ordered]@{
        situation = '现在需要确认飞书决策通道能正常发送并接收选择。'
        impact = '这只影响通道验证，不会修改游戏内容或项目任务状态。'
        action = '按卡片说明选择 A 或提交指定的验证文字。'
      }
    }
    Write-PrivateJsonAtomic $sendRequestPath ([ordered]@{ decision = $decision; cardNonce = $cardNonce })
    $sendHelper = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\send-canary.mjs'
    $send = Invoke-NodeJson $sendHelper $sendRequestPath
    if ($send.Result.result -cne 'PROVIDER_ACCEPTED') { throw 'Canary card was not accepted by Feishu' }
    foreach ($key in @('providerMessageIdHash', 'providerChatIdHash', 'cardNonceHash')) {
      if ([string]$send.Result[$key] -notmatch $script:HexPattern) { throw 'Canary send evidence is invalid' }
    }
    $createdIso = $createdAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $expiresIso = $expiresAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $binding = [ordered]@{
      kind = 'decision_reply'
      decisionId = $decisionId
      allowedOptions = @('A', 'B', 'C')
      allowCustomReply = $true
      issuedAt = $createdIso
      expiresAt = $expiresIso
      cardNonceHash = [string]$send.Result.cardNonceHash
      providerMessageIdHash = [string]$send.Result.providerMessageIdHash
      providerChatIdHash = [string]$send.Result.providerChatIdHash
    }
    Write-PrivateJsonAtomic $bindingPath @($binding)
    $pending = [ordered]@{
      decisionId = $decisionId
      allowedOptions = @('A', 'B', 'C')
      allowCustomReply = $true
      createdAt = $createdIso
      expiresAt = $expiresIso
      cardNonceHash = [string]$send.Result.cardNonceHash
      providerMessageIdHash = [string]$send.Result.providerMessageIdHash
      providerChatIdHash = [string]$send.Result.providerChatIdHash
    }
    Write-PrivateJsonAtomic $consumeRequestPath ([ordered]@{ pendingDecision = $pending })
    $consumeHelper = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\consume-reply.mjs'
    do {
      $consume = Invoke-NodeJson $consumeHelper $consumeRequestPath
      if ($consume.Code -ne 0) { throw 'Canary reply consumer failed' }
      $accepted = $false
      if ($Mode -ceq 'option' -and $consume.Result.result -ceq 'OPTION_ACCEPTED') {
        if ($consume.Result.optionKey -cne 'A' -or $consume.Result.source -cne 'feishu_card') {
          throw 'Canary received an invalid option selection'
        }
        $accepted = $true
      } elseif ($Mode -cne 'option' -and $consume.Result.result -ceq 'CUSTOM_ACCEPTED') {
        $expectedSource = if ($Mode -ceq 'card_custom') { 'feishu_card_input' } else { 'feishu_text' }
        if (
          $consume.Result.decisionId -cne $decisionId -or
          $consume.Result.customText -cne 'CANARY_CUSTOM_OK' -or
          $consume.Result.source -cne $expectedSource -or
          $consume.Result.providerMessageIdHash -cne [string]$send.Result.providerMessageIdHash -or
          ($Mode -ceq 'card_custom' -and $consume.Result.cardNonceHash -cne [string]$send.Result.cardNonceHash) -or
          ($Mode -ceq 'text_custom' -and $consume.Result.providerChatIdHash -cne [string]$send.Result.providerChatIdHash)
        ) {
          throw 'Canary received invalid custom reply evidence'
        }
        $accepted = $true
      }
      if ($accepted) {
        foreach ($key in @('providerMessageIdHash', 'providerEventIdHash', 'operatorOpenIdHash', 'tenantKeyHash', 'evidenceHash')) {
          if ([string]$consume.Result[$key] -notmatch $script:HexPattern) { throw 'Canary reply evidence is invalid' }
        }
        $consumedAgain = Invoke-NodeJson $consumeHelper $consumeRequestPath
        $firstEvidence = $consume.Result | ConvertTo-Json -Compress -Depth 20
        $replayedEvidence = $consumedAgain.Result | ConvertTo-Json -Compress -Depth 20
        if ($consumedAgain.Code -ne 0 -or $replayedEvidence -cne $firstEvidence) {
          throw 'Canary reply idempotency validation failed'
        }
        if ($Mode -ceq 'text_custom') { Write-TextReplyHealth $config 'TEXT_REPLY_READY' }
        $sanitized = [ordered]@{
          result = switch ($Mode) {
            'option' { 'CANARY_ACCEPTED' }
            'card_custom' { 'CANARY_CARD_CUSTOM_ACCEPTED' }
            'text_custom' { 'CANARY_TEXT_CUSTOM_ACCEPTED' }
          }
          providerMessageIdHash = [string]$consume.Result.providerMessageIdHash
          providerEventIdHash = [string]$consume.Result.providerEventIdHash
          operatorOpenIdHash = [string]$consume.Result.operatorOpenIdHash
          tenantKeyHash = [string]$consume.Result.tenantKeyHash
          evidenceHash = [string]$consume.Result.evidenceHash
        }
        if ($Mode -ceq 'option') {
          $sanitized['optionKey'] = 'A'
        } else {
          $sanitized['customCodePointCount'] = @('CANARY_CUSTOM_OK'.EnumerateRunes()).Count
        }
        Write-SanitizedJson $sanitized
        return
      }
      if ([datetime]::UtcNow -lt $expiresAt) { Start-Sleep -Milliseconds 500 }
    } while ([datetime]::UtcNow -lt $expiresAt)
    if ($Mode -ceq 'text_custom') {
      Write-TextReplyHealth $config 'TEXT_REPLY_UNAVAILABLE'
      Write-SanitizedJson ([ordered]@{
        result = 'TEXT_REPLY_UNAVAILABLE'
        cardStatus = 'CONNECTED'
      })
      return
    }
    throw 'Canary confirmation timed out'
  } finally {
    Remove-CanaryBinding $bindingPath $decisionId
    foreach ($path in @($sendRequestPath, $consumeRequestPath)) {
      if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
  }
}

function Invoke-ShowSanitized {
  $config = Read-PrivateConfig $ConfigPath
  Write-SanitizedJson ([ordered]@{
    result = 'CONFIGURATION_SUMMARY'
    schemaVersion = 1
    appIdHash = Get-Sha256 $config.appId
    recipientType = $config.recipient.type
    recipientHash = Get-Sha256 $config.recipient.value
    paired = $null -ne $config.expectedTenantKey -and $null -ne $config.pairedOperatorOpenIdHash
  })
}

Assert-TestInjectionSafe
switch ($Action) {
  'Configure' { Invoke-Configure }
  'Pair' { Invoke-Pair }
  'Canary' { Invoke-Canary 'option' }
  'CanaryCardCustom' { Invoke-Canary 'card_custom' }
  'CanaryTextCustom' { Invoke-Canary 'text_custom' }
  'ShowSanitized' { Invoke-ShowSanitized }
}
