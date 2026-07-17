#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $v2Root 'state.psm1'
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
  throw 'state.psm1 is missing'
}
Import-Module $modulePath -Force -DisableNameChecking

$fixtureRoot = Join-Path $PSScriptRoot 'fixtures'
$legacyPath = Join-Path $fixtureRoot 'legacy-v8-tq057.json'
$expectedPath = Join-Path $fixtureRoot 'migrated-v1-tq057.expected.json'
$expectedText = [IO.File]::ReadAllText($expectedPath)
$expectedState = if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
  $expectedText | ConvertFrom-Json -DateKind String
} else {
  $expectedText | ConvertFrom-Json
}
$fixtureContract = @($expectedState.decisionLedger)

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-hourly-controller-v2-state-' + [guid]::NewGuid().ToString('N'))
$statePath = Join-Path $sandbox 'state.json'
$migrationPath = Join-Path $sandbox 'migrated.json'
$secondMigrationPath = Join-Path $sandbox 'migrated-second.json'

try {
  [IO.Directory]::CreateDirectory($sandbox) | Out-Null

  $newState = New-ControllerState
  Assert-Equal (($newState.PSObject.Properties.Name) -join '|') 'schemaVersion|controllerVersion|phase|activeRun|decisionLedger|migration' 'new state fields'
  Assert-Equal $newState.schemaVersion 1 'new state schema'
  Assert-Equal $newState.phase 'IDLE' 'new state phase'
  Assert-True ($null -eq $newState.activeRun) 'new state active run'
  Assert-Equal @($newState.decisionLedger).Count 0 'new state decision ledger'
  Assert-True ($null -eq $newState.migration) 'new state migration'

  Assert-Throws `
    -Script { Move-ControllerPhase -State $newState -From @('IDLE') -To 'MUTATING' } `
    -MessageLike 'invalid_state' `
    -Label 'illegal state transition'

  $discoveringState = Move-ControllerPhase -State $newState -From @('IDLE') -To 'DISCOVERING'
  Assert-Equal $discoveringState.phase 'DISCOVERING' 'legal state transition'
  Write-ControllerStateAtomic -Path $statePath -State $discoveringState
  Assert-Equal (Read-ControllerState -Path $statePath).phase 'DISCOVERING' 'atomic state round trip'
  Assert-Equal @([IO.Directory]::GetFiles($sandbox, '*.tmp')).Count 0 'atomic write temp cleanup'

  Import-LegacyV8State -LegacyPath $legacyPath -DestinationPath $migrationPath -FixtureContract $fixtureContract | Out-Null
  $firstBytes = [IO.File]::ReadAllBytes($migrationPath)
  $expectedBytes = [IO.File]::ReadAllBytes($expectedPath)
  Assert-Equal ([Convert]::ToHexString($firstBytes)) ([Convert]::ToHexString($expectedBytes)) 'migration expected bytes'

  Import-LegacyV8State -LegacyPath $legacyPath -DestinationPath $migrationPath -FixtureContract $fixtureContract | Out-Null
  $repeatBytes = [IO.File]::ReadAllBytes($migrationPath)
  Assert-Equal ([Convert]::ToHexString($repeatBytes)) ([Convert]::ToHexString($firstBytes)) 'same destination migration idempotency'

  Import-LegacyV8State -LegacyPath $legacyPath -DestinationPath $secondMigrationPath -FixtureContract $fixtureContract | Out-Null
  Assert-Equal ([Convert]::ToHexString([IO.File]::ReadAllBytes($secondMigrationPath))) ([Convert]::ToHexString($firstBytes)) 'second destination migration idempotency'

  $migrated = Read-ControllerState -Path $migrationPath
  Assert-Equal @($migrated.decisionLedger).Count 5 'migrated decision count'
  $decisionIds = @($migrated.decisionLedger.decisionId)
  foreach ($decisionId in @(
      'DEC-20260715-35ACB87E6C10',
      'DEC-20260715-75D7BA2AF210',
      'DEC-20260714-29A5D1356CC8',
      'DEC-20260714-320075D033A5',
      'DEC-20260713-A07FA708DB22'
    )) {
    Assert-True ($decisionId -cin $decisionIds) "migrated decision $decisionId"
  }

  $multiplierDecision = @($migrated.decisionLedger | Where-Object { $_.decisionId -ceq 'DEC-20260715-75D7BA2AF210' })[0]
  $scopeJson = $multiplierDecision.scopeContract | ConvertTo-Json -Depth 20 -Compress
  foreach ($requiredText in @(
      'src/Assets/DataConfig/Spells.csv',
      'src/Assets/Scripts/Editor/DataConfigImporter.cs',
      'src/Assets/Scripts/Combat/SpellData.cs',
      'src/Assets/Scripts/Combat/CombatResolver.cs',
      'src/Assets/Tests/EditMode',
      'src/Assets/Data/Spells/*.asset'
    )) {
    Assert-True ([bool]$scopeJson.Contains($requiredText)) "multiplier scope $requiredText"
  }

  $serialized = [Text.UTF8Encoding]::new($false, $true).GetString($firstBytes)
  Assert-False ([bool]($serialized -match '(?i)"(appSecret|tenantKey|openId|chatId|messageId|eventId|providerMessageId|providerEventId|evidenceHash|rawEvent)"')) 'forbidden migration fields'

  $changedLegacyPath = Join-Path $sandbox 'changed-legacy.json'
  $changedLegacy = [IO.File]::ReadAllText($legacyPath) | ConvertFrom-Json
  $changedLegacy.state = 'DISCOVERING'
  Write-TestUtf8 -Path $changedLegacyPath -Value (($changedLegacy | ConvertTo-Json -Depth 100) + "`n")
  Assert-Throws `
    -Script { Import-LegacyV8State -LegacyPath $changedLegacyPath -DestinationPath $migrationPath -FixtureContract $fixtureContract } `
    -MessageLike 'migration_invalid' `
    -Label 'different migration source hash'

  Write-Output 'state.tests: OK'
} finally {
  $resolvedSandbox = [IO.Path]::GetFullPath($sandbox)
  if ($resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSandbox)) {
    [IO.Directory]::Delete($resolvedSandbox, $true)
  }
}
