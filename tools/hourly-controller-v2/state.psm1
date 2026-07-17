#requires -Version 7.0

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'protocol.psm1') -Force -DisableNameChecking
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'private-path-acl.ps1')

$script:ControllerVersion = '2.0.0'
$script:Phases = @(
  'IDLE',
  'DISCOVERING',
  'AUTHORIZED',
  'MUTATING',
  'VERIFYING',
  'COMMITTED',
  'WAITING_DECISION',
  'IMPLEMENTATION_PENDING'
)
$script:StateFields = @('schemaVersion', 'controllerVersion', 'phase', 'activeRun', 'decisionLedger', 'migration')
$script:LedgerFields = @(
  'decisionId',
  'taskId',
  'question',
  'resolutionKind',
  'selectedOptionId',
  'resolutionText',
  'impactSummary',
  'scopeContract',
  'resolvedAt',
  'source',
  'migratedFrom'
)
$script:ScopeFields = @('affectedRoots', 'requiredChecks', 'migrationFacts', 'compatibilityFacts')
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
$script:AllowedTransitions = @{
  IDLE = @('DISCOVERING')
  DISCOVERING = @('AUTHORIZED', 'WAITING_DECISION', 'IMPLEMENTATION_PENDING', 'IDLE')
  AUTHORIZED = @('MUTATING', 'IDLE')
  MUTATING = @('VERIFYING', 'IDLE')
  VERIFYING = @('COMMITTED', 'IDLE')
  COMMITTED = @('IDLE')
  WAITING_DECISION = @('IMPLEMENTATION_PENDING', 'IDLE')
  IMPLEMENTATION_PENDING = @('AUTHORIZED', 'IDLE')
}

function Throw-StateError {
  param(
    [Parameter(Mandatory = $true)][string]$Code,
    [Parameter(Mandatory = $true)][string]$Message
  )

  throw "$Code`: $Message"
}

function Get-ObjectFieldNames {
  param([Parameter(Mandatory = $true)]$Value)

  if ($Value -is [Collections.IDictionary]) {
    return @($Value.Keys)
  }
  @($Value.PSObject.Properties.Name)
}

function Assert-ObjectFields {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string[]]$Expected,
    [Parameter(Mandatory = $true)][string]$Label,
    [string]$ErrorCode = 'invalid_state'
  )

  $actual = @(Get-ObjectFieldNames -Value $Value)
  if (@($actual | Where-Object { $_ -cnotin $Expected }).Count -gt 0 -or
      @($Expected | Where-Object { $_ -cnotin $actual }).Count -gt 0) {
    Throw-StateError -Code $ErrorCode -Message "$Label fields do not match schema"
  }
}

function ConvertTo-StableStateJson {
  param([Parameter(Mandatory = $true)]$State)

  $json = $State | ConvertTo-Json -Depth 100
  ($json -replace "`r`n?", "`n") + "`n"
}

function New-ControllerState {
  [CmdletBinding()]
  param()

  [pscustomobject][ordered]@{
    schemaVersion = 1
    controllerVersion = $script:ControllerVersion
    phase = 'IDLE'
    activeRun = $null
    decisionLedger = @()
    migration = $null
  }
}

function Assert-ControllerState {
  param(
    [Parameter(Mandatory = $true)]$State,
    [string]$ErrorCode = 'invalid_state'
  )

  Assert-ObjectFields -Value $State -Expected $script:StateFields -Label 'state' -ErrorCode $ErrorCode
  if ([int]$State.schemaVersion -ne 1 -or
      [string]$State.controllerVersion -cne $script:ControllerVersion -or
      [string]$State.phase -cnotin $script:Phases) {
    Throw-StateError -Code $ErrorCode -Message 'state schema or phase is invalid'
  }
  $ledger = $State.decisionLedger
  if ($ledger -is [string] -or $ledger -isnot [Collections.IEnumerable]) {
    Throw-StateError -Code $ErrorCode -Message 'decisionLedger must be an array'
  }
}

function Read-ControllerState {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Throw-StateError -Code 'invalid_state' -Message 'state path must be an existing absolute file'
  }
  try {
    $decoder = [Text.UTF8Encoding]::new($false, $true)
    $text = $decoder.GetString([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Path))).TrimStart([char]0xFEFF)
    $state = if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
      $text | ConvertFrom-Json -AsHashtable -DateKind String
    } else {
      $text | ConvertFrom-Json -AsHashtable
    }
  } catch {
    Throw-StateError -Code 'invalid_state' -Message 'state must be valid UTF-8 JSON'
  }
  if ($state -isnot [Collections.IDictionary]) {
    Throw-StateError -Code 'invalid_state' -Message 'state root must be an object'
  }
  Assert-ControllerState -State $state
  $state
}

function Write-ControllerStateAtomic {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    $State
  )

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    Throw-StateError -Code 'invalid_state' -Message 'state path must be absolute'
  }
  Assert-ControllerState -State $State
  $fullPath = [IO.Path]::GetFullPath($Path)
  $parent = Split-Path -Parent $fullPath
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    Throw-StateError -Code 'invalid_state' -Message 'state parent directory does not exist'
  }
  $tempPath = Join-Path $parent ('.' + [IO.Path]::GetFileName($fullPath) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  try {
    $json = ConvertTo-StableStateJson -State $State
    [IO.File]::WriteAllText($tempPath, $json, [Text.UTF8Encoding]::new($false))
    Set-PrivatePathAcl -Path $tempPath
    Assert-PrivatePathAcl -Path $tempPath
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
      [IO.File]::Replace($tempPath, $fullPath, $null, $true)
    } else {
      [IO.File]::Move($tempPath, $fullPath)
    }
    Assert-PrivatePathAcl -Path $fullPath
  } finally {
    if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
      [IO.File]::Delete($tempPath)
    }
  }
}

function Move-ControllerPhase {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    $State,
    [Parameter(Mandatory = $true)]
    [string[]]$From,
    [Parameter(Mandatory = $true)]
    [string]$To
  )

  Assert-ControllerState -State $State
  $current = [string]$State.phase
  if ($current -cnotin $From -or $To -cnotin $script:Phases -or $To -cnotin @($script:AllowedTransitions[$current])) {
    Throw-StateError -Code 'invalid_state' -Message "transition $current -> $To is not allowed"
  }
  if ($State -is [Collections.IDictionary]) {
    $State['phase'] = $To
  } else {
    $State.phase = $To
  }
  $State
}

function Read-LegacyStateForMigration {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Throw-StateError -Code 'migration_invalid' -Message 'legacy path must be an existing absolute file'
  }
  try {
    $decoder = [Text.UTF8Encoding]::new($false, $true)
    $text = $decoder.GetString([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Path))).TrimStart([char]0xFEFF)
    $legacy = if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
      $text | ConvertFrom-Json -AsHashtable -DateKind String
    } else {
      $text | ConvertFrom-Json -AsHashtable
    }
  } catch {
    Throw-StateError -Code 'migration_invalid' -Message 'legacy state must be valid UTF-8 JSON'
  }
  if ($legacy -isnot [Collections.IDictionary] -or [int]$legacy.schemaVersion -ne 8) {
    Throw-StateError -Code 'migration_invalid' -Message 'legacy state must use schema v8'
  }
  $canonical = $legacy | ConvertTo-Json -Depth 100 -Compress
  [pscustomobject]@{
    state = $legacy
    sha256 = Get-Sha256Text -Text $canonical
  }
}

function Get-LegacyResolvedDecisions {
  param([Parameter(Mandatory = $true)][Collections.IDictionary]$Legacy)

  $decisions = @()
  foreach ($flowName in @('decisionFlow', 'lastCompletedDecisionFlow')) {
    $flow = $Legacy[$flowName]
    if ($null -ne $flow -and $flow -is [Collections.IDictionary] -and $flow.Contains('resolvedDecisions')) {
      $decisions += @($flow.resolvedDecisions)
    }
  }
  $unique = [ordered]@{}
  foreach ($decision in $decisions) {
    if ($decision -is [Collections.IDictionary] -and -not [string]::IsNullOrWhiteSpace([string]$decision.decisionId)) {
      $unique[[string]$decision.decisionId] = $decision
    }
  }
  $unique
}

function Assert-MigrationContractEntry {
  param([Parameter(Mandatory = $true)]$Entry)

  Assert-ObjectFields -Value $Entry -Expected $script:LedgerFields -Label 'migration contract entry' -ErrorCode 'migration_invalid'
  foreach ($field in @('decisionId', 'taskId', 'question', 'resolutionKind', 'selectedOptionId', 'resolutionText', 'impactSummary', 'resolvedAt', 'source', 'migratedFrom')) {
    if ([string]::IsNullOrWhiteSpace([string]$Entry.$field)) {
      Throw-StateError -Code 'migration_invalid' -Message "migration contract $field is empty"
    }
  }
  if ([string]$Entry.resolutionKind -cne 'option' -or
      [string]$Entry.source -cne 'legacy_v8_migration' -or
      [string]$Entry.migratedFrom -cne 'schema-v8') {
    Throw-StateError -Code 'migration_invalid' -Message 'migration contract resolution metadata is invalid'
  }
  $resolutionPrefix = '选择 ' + [string]$Entry.selectedOptionId + '：'
  if (-not ([string]$Entry.resolutionText).StartsWith($resolutionPrefix, [StringComparison]::Ordinal) -or
      ([string]$Entry.resolutionText).Length -le $resolutionPrefix.Length) {
    Throw-StateError -Code 'migration_invalid' -Message 'migration contract must contain the full selected option text'
  }
  $scope = $Entry.scopeContract
  Assert-ObjectFields -Value $scope -Expected $script:ScopeFields -Label 'scopeContract' -ErrorCode 'migration_invalid'
  foreach ($field in $script:ScopeFields) {
    $items = $scope.$field
    if ($items -is [string] -or $items -isnot [Collections.IEnumerable]) {
      Throw-StateError -Code 'migration_invalid' -Message "scopeContract $field must be an array"
    }
  }
  $contractJson = $Entry | ConvertTo-Json -Depth 100 -Compress
  foreach ($forbidden in $script:ForbiddenFields) {
    if ($contractJson -match ('(?i)"' + [regex]::Escape($forbidden) + '"')) {
      Throw-StateError -Code 'migration_invalid' -Message 'migration contract contains a forbidden field'
    }
  }
}

function New-MigratedLedgerEntry {
  param(
    [Parameter(Mandatory = $true)]$Contract,
    [Parameter(Mandatory = $true)][Collections.IDictionary]$LegacyDecision
  )

  $decisionId = [string]$Contract.decisionId
  $selectedOptionId = [string]$Contract.selectedOptionId
  if ([string]$LegacyDecision.taskId -cne [string]$Contract.taskId -or
      [string]$LegacyDecision.question -cne [string]$Contract.question -or
      [string]$LegacyDecision.impactSummary -cne [string]$Contract.impactSummary -or
      $LegacyDecision.resolution -isnot [Collections.IDictionary] -or
      [string]$LegacyDecision.resolution.optionKey -cne $selectedOptionId -or
      [string]$LegacyDecision.resolution.resolvedAt -cne [string]$Contract.resolvedAt) {
    Throw-StateError -Code 'migration_invalid' -Message "legacy decision does not match contract: $decisionId"
  }
  $selectedOptions = @($LegacyDecision.options | Where-Object { [string]$_.optionId -ceq $selectedOptionId })
  if ($selectedOptions.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$selectedOptions[0].text)) {
    Throw-StateError -Code 'migration_invalid' -Message "legacy decision option text does not match contract: $decisionId"
  }

  $scope = $Contract.scopeContract
  [pscustomobject][ordered]@{
    decisionId = $decisionId
    taskId = [string]$Contract.taskId
    question = [string]$Contract.question
    resolutionKind = 'option'
    selectedOptionId = $selectedOptionId
    resolutionText = [string]$Contract.resolutionText
    impactSummary = [string]$Contract.impactSummary
    scopeContract = [pscustomobject][ordered]@{
      affectedRoots = @($scope.affectedRoots)
      requiredChecks = @($scope.requiredChecks)
      migrationFacts = @($scope.migrationFacts)
      compatibilityFacts = @($scope.compatibilityFacts)
    }
    resolvedAt = [string]$Contract.resolvedAt
    source = 'legacy_v8_migration'
    migratedFrom = 'schema-v8'
  }
}

function Import-LegacyV8State {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$LegacyPath,
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,
    [Parameter(Mandatory = $true)]
    $FixtureContract
  )

  if (-not [IO.Path]::IsPathFullyQualified($DestinationPath)) {
    Throw-StateError -Code 'migration_invalid' -Message 'destination path must be absolute'
  }
  $legacyResult = Read-LegacyStateForMigration -Path $LegacyPath
  if (Test-Path -LiteralPath $DestinationPath -PathType Leaf) {
    $existing = Read-ControllerState -Path $DestinationPath
    if ($null -eq $existing.migration -or [string]$existing.migration.sourceSha256 -cne $legacyResult.sha256) {
      Throw-StateError -Code 'migration_invalid' -Message 'destination was created from a different legacy source'
    }
    return $existing
  }

  $contractEntries = @($FixtureContract)
  if ($contractEntries.Count -ne 5) {
    Throw-StateError -Code 'migration_invalid' -Message 'migration contract must contain five decisions'
  }
  $legacyDecisions = Get-LegacyResolvedDecisions -Legacy $legacyResult.state
  $ledger = @()
  foreach ($contract in $contractEntries) {
    Assert-MigrationContractEntry -Entry $contract
    $decisionId = [string]$contract.decisionId
    if (-not $legacyDecisions.Contains($decisionId)) {
      Throw-StateError -Code 'migration_invalid' -Message "legacy decision is missing: $decisionId"
    }
    $ledger += New-MigratedLedgerEntry -Contract $contract -LegacyDecision $legacyDecisions[$decisionId]
  }

  $state = [pscustomobject][ordered]@{
    schemaVersion = 1
    controllerVersion = $script:ControllerVersion
    phase = 'IDLE'
    activeRun = $null
    decisionLedger = @($ledger)
    migration = [pscustomobject][ordered]@{
      sourceSchemaVersion = 8
      sourceSha256 = [string]$legacyResult.sha256
      decisionCount = $ledger.Count
      status = 'IMPORTED'
    }
  }
  Write-ControllerStateAtomic -Path $DestinationPath -State $state
  $state
}

Export-ModuleMember -Function @(
  'New-ControllerState',
  'Read-ControllerState',
  'Write-ControllerStateAtomic',
  'Move-ControllerPhase',
  'Import-LegacyV8State'
)
