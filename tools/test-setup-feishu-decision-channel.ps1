#requires -Version 7.0

$ErrorActionPreference = 'Stop'

$tool = Join-Path $PSScriptRoot 'setup-feishu-decision-channel.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-feishu-setup-test-' + [guid]::NewGuid().ToString('N'))
$safeToRemove = $false
$originalPath = $env:PATH
$originalRealNode = [Environment]::GetEnvironmentVariable('TZG_TEST_REAL_NODE', 'Process')
$originalCanaryTrace = [Environment]::GetEnvironmentVariable('TZG_TEST_CANARY_TRACE', 'Process')

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

function Start-CanaryEnvelopeWriter {
  param(
    [string]$StateRoot,
    [string]$HmacKey,
    [string]$TenantKeyHash,
    [string]$OperatorOpenIdHash,
    [ValidateSet('feishu_card', 'feishu_card_input', 'feishu_text')]
    [string]$Source,
    [string]$EventHash
  )

  Start-Job -ScriptBlock {
    param($Root, $Key, $TenantHash, $OperatorHash, $ReplySource, $ReplyEventHash)

    $bindingPath = Join-Path $Root 'pending-bindings.json'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    $binding = $null
    do {
      if (Test-Path -LiteralPath $bindingPath -PathType Leaf) {
        try {
          $value = [IO.File]::ReadAllText($bindingPath) | ConvertFrom-Json -NoEnumerate
          if ($value -isnot [array] -or $value.Count -ne 1) {
            throw 'Canary binding root must be a single-item array'
          }
          $binding = $value[0]
        } catch {
          throw
        }
      }
      if ($null -eq $binding) { Start-Sleep -Milliseconds 50 }
    } while ($null -eq $binding -and [DateTimeOffset]::UtcNow -lt $deadline)
    if ($null -eq $binding) { throw 'Canary binding was not observed' }

    $receivedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    if ($ReplySource -ceq 'feishu_card') {
      $payload = [ordered]@{
        cardNonceHash = [string]$binding.cardNonceHash
        decisionId = [string]$binding.decisionId
        kind = 'decision_reply'
        operatorOpenIdHash = $OperatorHash
        optionKey = 'A'
        providerEventIdHash = $ReplyEventHash
        providerMessageIdHash = [string]$binding.providerMessageIdHash
        receivedAt = $receivedAt
        tenantKeyHash = $TenantHash
      }
    } elseif ($ReplySource -ceq 'feishu_card_input') {
      $payload = [ordered]@{
        cardNonceHash = [string]$binding.cardNonceHash
        customText = 'CANARY_CUSTOM_OK'
        decisionId = [string]$binding.decisionId
        kind = 'decision_custom_reply'
        operatorOpenIdHash = $OperatorHash
        providerEventIdHash = $ReplyEventHash
        providerMessageIdHash = [string]$binding.providerMessageIdHash
        receivedAt = $receivedAt
        source = 'feishu_card_input'
        tenantKeyHash = $TenantHash
      }
    } else {
      $payload = [ordered]@{
        customText = 'CANARY_CUSTOM_OK'
        decisionId = [string]$binding.decisionId
        kind = 'decision_custom_reply'
        operatorOpenIdHash = $OperatorHash
        providerChatIdHash = [string]$binding.providerChatIdHash
        providerEventIdHash = $ReplyEventHash
        providerMessageIdHash = [string]$binding.providerMessageIdHash
        receivedAt = $receivedAt
        source = 'feishu_text'
        tenantKeyHash = $TenantHash
      }
    }
    $canonical = $payload | ConvertTo-Json -Compress
    $keyBytes = [Convert]::FromBase64String($Key)
    $hmac = [Security.Cryptography.HMACSHA256]::new($keyBytes)
    try {
      $signature = [Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
    } finally {
      $hmac.Dispose()
      [Array]::Clear($keyBytes, 0, $keyBytes.Length)
    }
    $envelope = [ordered]@{ schemaVersion = 1; payload = $payload; signature = $signature }
    $inbox = Join-Path $Root 'inbox'
    New-Item -ItemType Directory -Path $inbox -Force | Out-Null
    [IO.File]::WriteAllText(
      (Join-Path $inbox "$ReplyEventHash.json"),
      ($envelope | ConvertTo-Json -Depth 5 -Compress),
      [Text.UTF8Encoding]::new($false)
    )
  } -ArgumentList $StateRoot, $HmacKey, $TenantKeyHash, $OperatorOpenIdHash, $Source, $EventHash
}

function Complete-CanaryEnvelopeWriter {
  param($Job, [string]$Label)

  $completed = Wait-Job -Job $Job -Timeout 20
  if ($null -eq $completed) {
    Stop-Job -Job $Job -ErrorAction SilentlyContinue
    Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue
    throw "$Label envelope writer timed out"
  }
  $errors = @($Job.ChildJobs[0].Error)
  Receive-Job -Job $Job | Out-Null
  Remove-Job -Job $Job -Force
  if ($errors.Count -gt 0) { throw "$Label envelope writer failed: $($errors[0])" }
}
$toolSource = Get-Content -Raw -LiteralPath $tool
foreach ($requiredAction in @('CanaryCardCustom', 'CanaryTextCustom')) {
  if (-not $toolSource.Contains("'$requiredAction'", [StringComparison]::Ordinal)) {
    throw "Setup action contract is missing $requiredAction"
  }
}
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

  $realNode = (Get-Command node -ErrorAction Stop).Source
  $fakeNodeRoot = Join-Path $sandbox 'fake-node'
  $fakeNodeScript = Join-Path $fakeNodeRoot 'fake-node.ps1'
  $fakeNodeCommand = Join-Path $fakeNodeRoot 'node.cmd'
  $canaryTracePath = Join-Path $fakeNodeRoot 'canary-send-trace.jsonl'
  New-Item -ItemType Directory -Path $fakeNodeRoot -Force | Out-Null
  Write-Utf8 $fakeNodeScript @'
param([string]$ScriptPath, [string]$RequestPath)
if ([IO.Path]::GetFileName($ScriptPath) -ceq 'send-canary.mjs') {
  $request = [IO.File]::ReadAllText($RequestPath) | ConvertFrom-Json
  if ([string]$request.decision.decisionId -cnotmatch '^DEC-[0-9]{8}-CANARY[A-F0-9]+$') { exit 22 }
  function Get-TestHash([string]$Value) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
  }
  [IO.File]::AppendAllText(
    $env:TZG_TEST_CANARY_TRACE,
    (($request | ConvertTo-Json -Depth 8 -Compress) + "`n"),
    [Text.UTF8Encoding]::new($false)
  )
  [ordered]@{
    result = 'PROVIDER_ACCEPTED'
    targetHash = ('a' * 64)
    providerMessageIdHash = Get-TestHash ([string]$request.decision.decisionId)
    providerChatIdHash = ('3' * 64)
    cardNonceHash = Get-TestHash ([string]$request.cardNonce)
    intentKeyHash = ('2' * 64)
  } | ConvertTo-Json -Compress
  exit 0
}
& $env:TZG_TEST_REAL_NODE $ScriptPath '--request-file' $RequestPath
exit $LASTEXITCODE
'@
  Write-Utf8 $fakeNodeCommand @'
@echo off
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-node.ps1" "%~1" "%~3"
exit /b %ERRORLEVEL%
'@
  [Environment]::SetEnvironmentVariable('TZG_TEST_REAL_NODE', $realNode, 'Process')
  [Environment]::SetEnvironmentVariable('TZG_TEST_CANARY_TRACE', $canaryTracePath, 'Process')
  $env:PATH = $fakeNodeRoot + [IO.Path]::PathSeparator + $originalPath
  if ((Get-Command node -ErrorAction Stop).Source -cne $fakeNodeCommand) {
    throw 'fake node sender adapter was not selected'
  }
  $fakeNodeProbePath = Join-Path $fakeNodeRoot 'probe.json'
  Write-Utf8 $fakeNodeProbePath (@{
    decision = @{ decisionId = 'DEC-20260716-CANARYA1B2C3D4' }
    cardNonce = 'probe'
  } | ConvertTo-Json -Compress)
  $fakeNodeProbe = @(& $fakeNodeCommand 'send-canary.mjs' '--request-file' $fakeNodeProbePath 2>&1)
  if ($LASTEXITCODE -ne 0 -or (@($fakeNodeProbe | Where-Object { ([string]$_).StartsWith('{') })).Count -ne 1) {
    throw "fake node sender adapter probe failed: $($fakeNodeProbe -join ' | ')"
  }

  $textUnavailable = Invoke-Setup -Arguments @(
    'CanaryTextCustom','-ConfigPath',$configPath,'-StateRoot',$stateRoot,'-PairTimeoutSeconds',1
  )
  Assert-Code $textUnavailable 0 'CanaryTextCustom unavailable'
  $textUnavailableJson = $textUnavailable.Output | ConvertFrom-Json
  if ($textUnavailableJson.result -cne 'TEXT_REPLY_UNAVAILABLE' -or $textUnavailableJson.cardStatus -cne 'CONNECTED') {
    throw "text-event unavailability disabled or misreported the card channel: $($textUnavailable.Output)"
  }
  $leftBindingPath = Join-Path $stateRoot 'pending-bindings.json'
  if (Test-Path -LiteralPath $leftBindingPath) {
    $leftBinding = [IO.File]::ReadAllText($leftBindingPath) | ConvertFrom-Json
    throw "text unavailable canary left its binding: type=$($leftBinding.GetType().Name);count=$(@($leftBinding).Count)"
  }

  $optionWriter = Start-CanaryEnvelopeWriter -StateRoot $stateRoot -HmacKey $pairedConfig.hmacKey `
    -TenantKeyHash (Get-Sha256 $tenantKey) -OperatorOpenIdHash $pairedConfig.pairedOperatorOpenIdHash `
    -Source feishu_card -EventHash ('d' * 64)
  $optionCanary = Invoke-Setup -Arguments @(
    'Canary','-ConfigPath',$configPath,'-StateRoot',$stateRoot,'-PairTimeoutSeconds',10
  )
  Complete-CanaryEnvelopeWriter $optionWriter 'option canary'
  Assert-Code $optionCanary 0 'Canary option after text unavailable'
  if (($optionCanary.Output | ConvertFrom-Json).result -cne 'CANARY_ACCEPTED') {
    throw "card option Canary stopped working after text unavailability: $($optionCanary.Output)"
  }

  $cardWriter = Start-CanaryEnvelopeWriter -StateRoot $stateRoot -HmacKey $pairedConfig.hmacKey `
    -TenantKeyHash (Get-Sha256 $tenantKey) -OperatorOpenIdHash $pairedConfig.pairedOperatorOpenIdHash `
    -Source feishu_card_input -EventHash ('e' * 64)
  $cardCustom = Invoke-Setup -Arguments @(
    'CanaryCardCustom','-ConfigPath',$configPath,'-StateRoot',$stateRoot,'-PairTimeoutSeconds',10
  )
  Complete-CanaryEnvelopeWriter $cardWriter 'card custom canary'
  if ($cardCustom.Code -ne 0) {
    $inboxCount = @(Get-ChildItem -LiteralPath (Join-Path $stateRoot 'inbox') -File -ErrorAction SilentlyContinue).Count
    $processedCount = @(Get-ChildItem -LiteralPath (Join-Path $stateRoot 'processed') -File -ErrorAction SilentlyContinue).Count
    $quarantineCount = @(Get-ChildItem -LiteralPath (Join-Path $stateRoot 'quarantine') -File -ErrorAction SilentlyContinue).Count
    throw "card custom canary failed with sanitized counts inbox=$inboxCount processed=$processedCount quarantine=${quarantineCount}: $($cardCustom.Output)"
  }
  Assert-Code $cardCustom 0 'CanaryCardCustom'
  $cardCustomJson = $cardCustom.Output | ConvertFrom-Json
  Assert-NoLiteral $cardCustom.Output @('CANARY_CUSTOM_OK', $tenantKey, $operatorOpenId) 'CanaryCardCustom output'
  if ($cardCustomJson.result -cne 'CANARY_CARD_CUSTOM_ACCEPTED' -or $cardCustomJson.customCodePointCount -ne 16) {
    throw "card custom canary did not consume exact custom evidence: $($cardCustom.Output)"
  }

  $textWriter = Start-CanaryEnvelopeWriter -StateRoot $stateRoot -HmacKey $pairedConfig.hmacKey `
    -TenantKeyHash (Get-Sha256 $tenantKey) -OperatorOpenIdHash $pairedConfig.pairedOperatorOpenIdHash `
    -Source feishu_text -EventHash ('f' * 64)
  $textCustom = Invoke-Setup -Arguments @(
    'CanaryTextCustom','-ConfigPath',$configPath,'-StateRoot',$stateRoot,'-PairTimeoutSeconds',10
  )
  Complete-CanaryEnvelopeWriter $textWriter 'text custom canary'
  Assert-Code $textCustom 0 'CanaryTextCustom'
  $textCustomJson = $textCustom.Output | ConvertFrom-Json
  Assert-NoLiteral $textCustom.Output @('CANARY_CUSTOM_OK', $tenantKey, $operatorOpenId) 'CanaryTextCustom output'
  if ($textCustomJson.result -cne 'CANARY_TEXT_CUSTOM_ACCEPTED' -or $textCustomJson.customCodePointCount -ne 16) {
    throw "text custom canary did not consume exact custom evidence: $($textCustom.Output)"
  }
  $textSendTrace = (Get-Content -LiteralPath $canaryTracePath | Select-Object -Last 1) | ConvertFrom-Json
  $exactCanaryCommand = "$($textSendTrace.decision.decisionId)：自定义 CANARY_CUSTOM_OK"
  if (-not ([string]$textSendTrace.decision.question).Contains($exactCanaryCommand, [StringComparison]::Ordinal)) {
    throw 'text custom canary card did not contain the exact copyable command'
  }
  $textHealth = [IO.File]::ReadAllText((Join-Path $stateRoot 'text-reply-health.json')) | ConvertFrom-Json
  if ($textHealth.status -cne 'TEXT_REPLY_READY' -or
      (Test-Path -LiteralPath (Join-Path $stateRoot 'pending-bindings.json'))) {
    throw 'successful text canary did not update sanitized health and remove only its binding'
  }

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
  $env:PATH = $originalPath
  [Environment]::SetEnvironmentVariable('TZG_TEST_REAL_NODE', $originalRealNode, 'Process')
  [Environment]::SetEnvironmentVariable('TZG_TEST_CANARY_TRACE', $originalCanaryTrace, 'Process')
  Get-Job -ErrorAction SilentlyContinue | Where-Object Name -like 'Job*' | Stop-Job -ErrorAction SilentlyContinue
  Get-Job -ErrorAction SilentlyContinue | Where-Object Name -like 'Job*' | Remove-Job -Force -ErrorAction SilentlyContinue
  if ($safeToRemove) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
