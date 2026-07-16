#requires -Version 7.0

$ErrorActionPreference = 'Stop'

$tool = Join-Path $PSScriptRoot 'setup-feishu-decision-channel.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-feishu-setup-test-' + [guid]::NewGuid().ToString('N'))
$safeToRemove = $false

function Get-Sha256 {
  param([string]$Value)

  $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
  try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
  finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Write-Utf8 {
  param([string]$Path, [string]$Value)

  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
  }
  [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Invoke-Setup {
  param([object[]]$Arguments)

  if ($Arguments.Count -lt 1 -or (($Arguments.Count - 1) % 2) -ne 0) {
    throw 'Invoke-Setup received invalid test arguments'
  }
  $parameters = @{ Action = [string]$Arguments[0] }
  for ($index = 1; $index -lt $Arguments.Count; $index += 2) {
    $name = [string]$Arguments[$index]
    if (-not $name.StartsWith('-', [StringComparison]::Ordinal)) { throw 'Invoke-Setup received an invalid parameter name' }
    $parameters[$name.Substring(1)] = $Arguments[$index + 1]
  }
  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    try {
      $output = @(& $tool @parameters 2>&1)
      [pscustomobject]@{ Code = 0; Output = ($output -join "`n") }
    } catch {
      [pscustomobject]@{ Code = 1; Output = [string]$_.Exception.Message }
    }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)

  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function Assert-NoLiteral {
  param([string]$Text, [string[]]$Literals, [string]$Label)

  foreach ($literal in $Literals) {
    if (-not [string]::IsNullOrEmpty($literal) -and $Text.Contains($literal, [StringComparison]::Ordinal)) {
      throw "$Label exposed a protected literal"
    }
  }
}

function Assert-PrivateFileAcl {
  param([string]$Path)

  $acl = Get-Acl -LiteralPath $Path
  if (-not $acl.AreAccessRulesProtected) { throw 'private config ACL still inherits' }
  $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  $allowed = @($currentSid, 'S-1-5-18')
  $rules = @($acl.Access)
  if ($rules.Count -ne 2) { throw "private config ACL expected 2 rules, got $($rules.Count)" }
  foreach ($rule in $rules) {
    $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    if ($sid -notin $allowed) { throw "private config ACL contains unexpected SID $sid" }
    if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) {
      throw 'private config ACL contains a deny rule'
    }
    if (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl) {
      throw "private config ACL does not grant FullControl to $sid"
    }
    if ($rule.IsInherited) { throw "private config ACL rule for $sid is inherited" }
  }
}

function New-PairingEnvelope {
  param(
    [string]$StateRoot,
    [string]$HmacKey,
    [string]$PairingNonce,
    [string]$TenantKey,
    [string]$OperatorOpenId,
    [datetime]$ReceivedAt
  )

  $eventHash = Get-Sha256 'evt_pairing_fixture'
  $payload = [ordered]@{
    kind = 'operator_pairing'
    operatorOpenIdHash = Get-Sha256 $OperatorOpenId
    pairingNonceHash = Get-Sha256 $PairingNonce
    providerEventIdHash = $eventHash
    receivedAt = $ReceivedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    tenantKey = $TenantKey
    tenantKeyHash = Get-Sha256 $TenantKey
  }
  $canonical = $payload | ConvertTo-Json -Compress
  $keyBytes = [Convert]::FromBase64String($HmacKey)
  $hmac = [Security.Cryptography.HMACSHA256]::new($keyBytes)
  try {
    $signature = [Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
  } finally {
    $hmac.Dispose()
    [Array]::Clear($keyBytes, 0, $keyBytes.Length)
  }
  $envelope = [ordered]@{
    schemaVersion = 1
    payload = $payload
    signature = $signature
  }
  Write-Utf8 (Join-Path $StateRoot "pairing-inbox\$eventHash.json") ($envelope | ConvertTo-Json -Depth 5 -Compress)
}

if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
  throw "production script is missing: $tool"
}
$toolSource = Get-Content -Raw -LiteralPath $tool
foreach ($requiredBindingField in @('allowCustomReply = $true', 'providerChatIdHash = [string]$send.Result.providerChatIdHash')) {
  if (-not $toolSource.Contains($requiredBindingField, [StringComparison]::Ordinal)) {
    throw "Canary binding is missing $requiredBindingField"
  }
}

New-Item -ItemType Directory -Path $sandbox | Out-Null
$resolvedSandbox = (Resolve-Path -LiteralPath $sandbox).Path
$tempPrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedSandbox.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing fixture outside temp root: $resolvedSandbox"
}
$safeToRemove = $true

try {
  $configPath = Join-Path $sandbox 'private.json'
  $stateRoot = Join-Path $sandbox 'state'
  $appId = 'cli_test_app_id'
  $appSecret = 'test_app_secret_never_log'
  $recipient = 'operator@example.invalid'
  $configure = Invoke-Setup -Arguments @(
    'Configure', '-ConfigPath', $configPath, '-StateRoot', $stateRoot,
    '-ConfigValues', @{ appId = $appId; appSecret = $appSecret; recipientType = 'email'; recipientValue = $recipient }
  )
  Assert-Code $configure 0 'Configure'
  Assert-NoLiteral $configure.Output @($appId, $appSecret, $recipient) 'Configure output'
  $configuredOutput = $configure.Output | ConvertFrom-Json
  if ($configuredOutput.result -ne 'CONFIGURED' -or $configuredOutput.schemaVersion -ne 1 -or $configuredOutput.paired) {
    throw 'Configure output was not the expected sanitized summary'
  }
  if ($configuredOutput.appIdHash -ne (Get-Sha256 $appId) -or $configuredOutput.recipientHash -ne (Get-Sha256 $recipient)) {
    throw 'Configure output hashes did not match the fixture'
  }

  $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
  $expectedConfigKeys = @('appId', 'appSecret', 'expectedTenantKey', 'hmacKey', 'pairedOperatorOpenIdHash', 'recipient', 'schemaVersion', 'stateRoot')
  $actualConfigKeys = (@($config.psobject.Properties.Name | Sort-Object) -join '|')
  $expectedConfigKeyText = (($expectedConfigKeys | Sort-Object) -join '|')
  if ($actualConfigKeys -cne $expectedConfigKeyText) {
    throw 'Configure wrote an unexpected private config shape'
  }
  if ($config.schemaVersion -ne 1 -or $config.appId -ne $appId -or $config.appSecret -ne $appSecret) {
    throw 'Configure did not preserve required values'
  }
  if ($config.recipient.type -ne 'email' -or $config.recipient.value -ne $recipient) {
    throw 'Configure wrote an invalid recipient'
  }
  if ($null -ne $config.expectedTenantKey -or $null -ne $config.pairedOperatorOpenIdHash) {
    throw 'Configure unexpectedly pre-paired the operator'
  }
  if (-not [IO.Path]::IsPathFullyQualified([string]$config.stateRoot) -or $config.stateRoot -ne [IO.Path]::GetFullPath($stateRoot)) {
    throw 'Configure did not write the expected absolute stateRoot'
  }
  $hmacBytes = [Convert]::FromBase64String([string]$config.hmacKey)
  try {
    if ($hmacBytes.Length -ne 32) { throw 'Configure did not generate a 32-byte HMAC key' }
  } finally {
    [Array]::Clear($hmacBytes, 0, $hmacBytes.Length)
  }
  Assert-PrivateFileAcl $configPath

  $show = Invoke-Setup -Arguments @('ShowSanitized', '-ConfigPath', $configPath, '-StateRoot', $stateRoot)
  Assert-Code $show 0 'ShowSanitized'
  Assert-NoLiteral $show.Output @($appId, $appSecret, $recipient) 'ShowSanitized output'

  $beforeInvalid = [IO.File]::ReadAllText($configPath)
  $invalid = Invoke-Setup -Arguments @(
    'Configure', '-ConfigPath', $configPath, '-StateRoot', $stateRoot,
    '-ConfigValues', @{ appId = 'invalid_app'; appSecret = 'invalid_secret'; recipientType = 'chat_id'; recipientValue = 'oc_forbidden' }
  )
  if ($invalid.Code -eq 0) { throw 'Configure accepted a chat_id recipient' }
  if ([IO.File]::ReadAllText($configPath) -cne $beforeInvalid) { throw 'invalid Configure changed the existing config' }

  $outsideTemp = Invoke-Setup -Arguments @(
    'Configure', '-ConfigPath', $configPath, '-StateRoot', (Join-Path $env:USERPROFILE 'not-a-test-root'),
    '-ConfigValues', @{ appId = 'x'; appSecret = 'y'; recipientType = 'email'; recipientValue = 'x@example.invalid' }
  )
  if ($outsideTemp.Code -eq 0) { throw 'ConfigValues test mode accepted a stateRoot outside temp' }

  $pairingNonce = 'pairing-test-nonce-0123456789'
  $tenantKey = 'tenant_fixture_01'
  $operatorOpenId = 'ou_fixture_operator_01'
  New-PairingEnvelope -StateRoot $stateRoot -HmacKey $config.hmacKey -PairingNonce $pairingNonce -TenantKey $tenantKey -OperatorOpenId $operatorOpenId -ReceivedAt ([datetime]::UtcNow)
  $pair = Invoke-Setup -Arguments @(
    'Pair', '-ConfigPath', $configPath, '-StateRoot', $stateRoot, '-PairTimeoutSeconds', 2,
    '-ConfigValues', @{ pairingNonce = $pairingNonce; skipProviderSend = $true }
  )
  Assert-Code $pair 0 'Pair'
  Assert-NoLiteral $pair.Output @($appId, $appSecret, $recipient, $tenantKey, $operatorOpenId, $pairingNonce) 'Pair output'
  $pairOutput = $pair.Output | ConvertFrom-Json
  if ($pairOutput.result -ne 'PAIRED' -or $pairOutput.tenantKeyHash -ne (Get-Sha256 $tenantKey) -or $pairOutput.operatorOpenIdHash -ne (Get-Sha256 $operatorOpenId)) {
    throw 'Pair did not return the expected hash-only evidence'
  }
  $pairedConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
  if ($pairedConfig.expectedTenantKey -ne $tenantKey -or $pairedConfig.pairedOperatorOpenIdHash -ne (Get-Sha256 $operatorOpenId)) {
    throw 'Pair did not persist the expected tenant/operator identity'
  }
  if (Test-Path -LiteralPath (Join-Path $stateRoot 'pairing-binding.json')) {
    throw 'Pair left the one-time binding active after success'
  }
  Assert-PrivateFileAcl $configPath

  $unpairedConfigPath = Join-Path $sandbox 'unpaired-private.json'
  $unpairedStateRoot = Join-Path $sandbox 'unpaired-state'
  Assert-Code (Invoke-Setup -Arguments @(
    'Configure', '-ConfigPath', $unpairedConfigPath, '-StateRoot', $unpairedStateRoot,
    '-ConfigValues', @{ appId = 'unpaired_app'; appSecret = 'unpaired_secret'; recipientType = 'open_id'; recipientValue = 'ou_unpaired_target' }
  )) 0 'Configure unpaired canary fixture'
  $canary = Invoke-Setup -Arguments @(
    'Canary', '-ConfigPath', $unpairedConfigPath, '-StateRoot', $unpairedStateRoot, '-PairTimeoutSeconds', 1,
    '-ConfigValues', @{ skipProviderSend = $true }
  )
  if ($canary.Code -eq 0) { throw 'Canary bypassed the required paired identity' }
  if (Test-Path -LiteralPath (Join-Path $unpairedStateRoot 'pending-bindings.json')) {
    throw 'rejected Canary created a pending binding'
  }

  Write-Output 'test-setup-feishu-decision-channel: OK'
} finally {
  if ($safeToRemove) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
