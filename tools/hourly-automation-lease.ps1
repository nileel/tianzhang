#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Show', 'Acquire', 'ResumeBatch', 'SaveBatch', 'ClearBatch', 'SaveRecovery', 'SaveInterruption', 'ClearRecovery', 'RecordResult', 'ClearBlocking', 'Release')]
  [string]$Action,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [string]$TaskId,
  [string]$Owner,
  [string]$RepositoryRoot,
  [ValidateRange(1, 86400)]
  [int]$LeaseSeconds = 3600,
  [string]$RunId,
  [ValidateSet('success', 'refilled', 'blocked', 'failed', 'waiting_decision')]
  [string]$Category,
  [string]$DetailCode,
  [string]$BlockingFingerprint,
  [string]$DecisionId,
  [string]$DecisionRequestPath,
  [string]$CodexThreadId,
  [string]$ClaudeSessionId,
  [switch]$HasUncommittedChanges,
  [switch]$ResumeRecovery,
  [string]$ChangedPaths,
  [string]$BatchStatePath,
  [string]$BatchId
)

$ErrorActionPreference = 'Stop'

$aclScript = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $aclScript

function New-RuntimeState {
  [pscustomobject][ordered]@{
    schemaVersion = 4
    lease = $null
    batch = $null
    recovery = $null
    blocking = [pscustomobject][ordered]@{
      fingerprint = $null
      count = 0
      pauseRequested = $false
    }
    lastResult = $null
  }
}

function New-Result {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Status,
    [hashtable]$Values = @{}
  )

  $result = [ordered]@{ status = $Status }
  foreach ($entry in $Values.GetEnumerator()) {
    $result[$entry.Key] = $entry.Value
  }
  [pscustomobject]$result
}

function Write-ResultAndExit {
  param(
    [Parameter(Mandatory = $true)]
    [object]$Result,
    [int]$ExitCode = 0
  )

  $json = $Result | ConvertTo-Json -Compress -Depth 30
  [Console]::Out.WriteLine($json)
  exit $ExitCode
}

function Test-PathWithinRoot {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$Root
  )

  $fullPath = [IO.Path]::GetFullPath($Path)
  $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
  )
  if ($fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
    return $true
  }
  $rootPrefix = $fullRoot + [IO.Path]::DirectorySeparatorChar
  $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Get-ApprovedPrivateRoot {
  [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state'))
}

function Resolve-StateRoot {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    throw [ArgumentException]::new('StateRoot must be an absolute path')
  }
  $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
  )
  if (-not (Test-PathWithinRoot -Path $fullPath -Root (Get-ApprovedPrivateRoot))) {
    throw [ArgumentException]::new('StateRoot must be inside the approved automation-state root')
  }
  $fullPath
}

function Resolve-ApprovedPrivateFile {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$ParameterName
  )

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    throw [ArgumentException]::new("$ParameterName must be an absolute path")
  }
  $fullPath = [IO.Path]::GetFullPath($Path)
  if (-not (Test-PathWithinRoot -Path $fullPath -Root (Get-ApprovedPrivateRoot))) {
    throw [ArgumentException]::new("$ParameterName must be inside the approved automation-state root")
  }
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    throw [ArgumentException]::new("$ParameterName must reference an existing file")
  }
  $fullPath
}

function Assert-DecisionConsumeRequest {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedDecisionId
  )

  $bytes = [IO.File]::ReadAllBytes($Path)
  if ($bytes.Length -gt 65536) {
    throw [ArgumentException]::new('DecisionRequestPath exceeds 65536 bytes')
  }
  try {
    $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $root = $json | ConvertFrom-Json -AsHashtable -Depth 20 -DateKind String
  } catch {
    throw [ArgumentException]::new('DecisionRequestPath must contain valid UTF-8 JSON')
  }
  if ($root -isnot [Collections.IDictionary]) {
    throw [ArgumentException]::new('DecisionRequestPath root must be an object')
  }
  if (@($root.Keys).Count -ne 1 -or -not $root.Contains('pendingDecision')) {
    throw [ArgumentException]::new('DecisionRequestPath must contain only pendingDecision')
  }

  $pending = $root.pendingDecision
  if ($pending -isnot [Collections.IDictionary]) {
    throw [ArgumentException]::new('pendingDecision must be an object')
  }
  $expectedKeys = @(
    'decisionId',
    'allowedOptions',
    'allowCustomReply',
    'createdAt',
    'expiresAt',
    'cardNonceHash',
    'providerMessageIdHash',
    'providerChatIdHash'
  )
  if (@($pending.Keys).Count -ne $expectedKeys.Count) {
    throw [ArgumentException]::new('pendingDecision fields are invalid')
  }
  foreach ($key in $expectedKeys) {
    if (-not $pending.Contains($key)) {
      throw [ArgumentException]::new('pendingDecision fields are invalid')
    }
  }

  if (
    $pending.decisionId -isnot [string] -or
    -not $pending.decisionId.Equals($ExpectedDecisionId, [StringComparison]::Ordinal)
  ) {
    throw [ArgumentException]::new('pendingDecision decisionId does not match')
  }
  $options = @($pending.allowedOptions)
  if (
    $options.Count -ne 3 -or
    $options[0] -isnot [string] -or $options[0] -cne 'A' -or
    $options[1] -isnot [string] -or $options[1] -cne 'B' -or
    $options[2] -isnot [string] -or $options[2] -cne 'C'
  ) {
    throw [ArgumentException]::new('pendingDecision allowedOptions are invalid')
  }
  if ($pending.allowCustomReply -isnot [bool]) {
    throw [ArgumentException]::new('pendingDecision allowCustomReply must be boolean')
  }

  $timestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"
  $timestampStyles = [Globalization.DateTimeStyles]::AssumeUniversal
  $createdAt = [DateTimeOffset]::MinValue
  $expiresAt = [DateTimeOffset]::MinValue
  if (
    $pending.createdAt -isnot [string] -or
    -not [DateTimeOffset]::TryParseExact(
      $pending.createdAt,
      $timestampFormat,
      [Globalization.CultureInfo]::InvariantCulture,
      $timestampStyles,
      [ref]$createdAt
    ) -or
    $pending.expiresAt -isnot [string] -or
    -not [DateTimeOffset]::TryParseExact(
      $pending.expiresAt,
      $timestampFormat,
      [Globalization.CultureInfo]::InvariantCulture,
      $timestampStyles,
      [ref]$expiresAt
    ) -or
    $createdAt -gt $expiresAt
  ) {
    throw [ArgumentException]::new('pendingDecision timestamps are invalid')
  }

  foreach ($hashKey in @('cardNonceHash', 'providerMessageIdHash', 'providerChatIdHash')) {
    $hash = $pending[$hashKey]
    if ($hash -isnot [string] -or $hash -cnotmatch '\A[0-9a-f]{64}\z') {
      throw [ArgumentException]::new("pendingDecision $hashKey is invalid")
    }
  }
}

function Resolve-RepositoryRoot {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    throw [ArgumentException]::new('RepositoryRoot must be an absolute path')
  }
  $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
  )
  if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
    throw [ArgumentException]::new('RepositoryRoot must be an existing directory')
  }
  if (-not (Test-Path -LiteralPath (Join-Path $fullPath '.git'))) {
    throw [ArgumentException]::new('RepositoryRoot must be an existing Git root')
  }
  $fullPath
}

function Assert-StableText {
  param(
    [AllowNull()]
    [string]$Value,
    [Parameter(Mandatory = $true)]
    [string]$ParameterName,
    [int]$MaximumLength = 256,
    [switch]$AllowEmpty
  )

  if (-not $AllowEmpty -and [string]::IsNullOrWhiteSpace($Value)) {
    throw [ArgumentException]::new("$ParameterName is required")
  }
  if ($null -ne $Value -and $Value.Length -gt $MaximumLength) {
    throw [ArgumentException]::new("$ParameterName is too long")
  }
  if ($null -ne $Value -and $Value -match '[\x00-\x1F\x7F]') {
    throw [ArgumentException]::new("$ParameterName contains control characters")
  }
}

function Convert-ChangedPaths {
  param([AllowNull()][string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) {
    return @()
  }
  $result = [Collections.Generic.List[string]]::new()
  foreach ($path in $Value.Split('|', [StringSplitOptions]::RemoveEmptyEntries)) {
    $candidate = $path.Trim().Replace('\', '/')
    if (
      [string]::IsNullOrWhiteSpace($candidate) -or
      [IO.Path]::IsPathFullyQualified($candidate) -or
      $candidate.Split('/') -contains '..' -or
      $candidate -match '[\x00-\x1F\x7F|]'
    ) {
      throw [ArgumentException]::new('ChangedPaths must contain repository-relative paths')
    }
    if (-not $result.Contains($candidate)) {
      $result.Add($candidate)
    }
  }
  @($result)
}

function Assert-PropertySet {
  param(
    [Parameter(Mandatory = $true)]
    [object]$Object,
    [Parameter(Mandatory = $true)]
    [string[]]$Expected,
    [Parameter(Mandatory = $true)]
    [string]$Context
  )

  $actual = @($Object.PSObject.Properties.Name | Sort-Object)
  $wanted = @($Expected | Sort-Object)
  if (($actual -join ',') -ne ($wanted -join ',')) {
    throw [IO.InvalidDataException]::new("Unexpected fields in $Context")
  }
}

function Assert-AutomationBatch {
  param(
    [Parameter(Mandatory = $true)]
    [object]$Batch,
    [AllowNull()]
    [object]$Lease
  )

  Assert-PropertySet -Object $Batch -Expected @(
    'schemaVersion',
    'batchId',
    'runId',
    'repositoryRoot',
    'status',
    'baseCommit',
    'queueHash',
    'manualBaselinePath',
    'startedAt',
    'maxConcurrent',
    'lanes'
  ) -Context 'automation batch'
  if ($Batch.schemaVersion -ne 1) {
    throw [IO.InvalidDataException]::new('Unsupported automation batch schema')
  }
  foreach ($property in @('batchId', 'runId')) {
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$Batch.$property, [ref]$parsed)) {
      throw [IO.InvalidDataException]::new("Automation batch $property is invalid")
    }
  }
  if ([string]$Batch.status -cnotin @('open', 'closed')) {
    throw [IO.InvalidDataException]::new('Automation batch status is invalid')
  }
  if ([string]$Batch.baseCommit -cnotmatch '\A[0-9a-f]{40,64}\z') {
    throw [IO.InvalidDataException]::new('Automation batch baseCommit is invalid')
  }
  if ([string]$Batch.queueHash -cnotmatch '\A[0-9a-f]{64}\z') {
    throw [IO.InvalidDataException]::new('Automation batch queueHash is invalid')
  }
  if ($Batch.maxConcurrent -isnot [long] -or [int]$Batch.maxConcurrent -lt 1 -or [int]$Batch.maxConcurrent -gt 16) {
    throw [IO.InvalidDataException]::new('Automation batch maxConcurrent is invalid')
  }
  $batchRepositoryRoot = Resolve-RepositoryRoot -Path ([string]$Batch.repositoryRoot)
  $baselinePath = Resolve-ApprovedPrivateFile -Path ([string]$Batch.manualBaselinePath) -ParameterName 'manualBaselinePath'
  if ([string]::IsNullOrWhiteSpace($baselinePath)) {
    throw [IO.InvalidDataException]::new('Automation batch baseline is invalid')
  }
  if ($null -ne $Lease) {
    if (
      [string]$Lease.runId -cne [string]$Batch.runId -or
      [string]$Lease.repositoryRoot -ine $batchRepositoryRoot
    ) {
      throw [IO.InvalidDataException]::new('Automation batch does not match the coordinator lease')
    }
  }

  $lanes = @($Batch.lanes)
  if ($lanes.Count -lt 1 -or $lanes.Count -gt [int]$Batch.maxConcurrent) {
    throw [IO.InvalidDataException]::new('Automation batch lane count is invalid')
  }
  $laneIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $taskIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $queueIndexes = [Collections.Generic.HashSet[int]]::new()
  foreach ($lane in $lanes) {
    Assert-PropertySet -Object $lane -Expected @(
      'laneId',
      'owner',
      'identity',
      'acceptedRoutes',
      'invoker',
      'taskClaim',
      'worktree',
      'branch',
      'baseCommit',
      'workerPaths',
      'coordinatorPaths',
      'factPaths',
      'processOrSession',
      'workerTerminal',
      'integrationState',
      'queueIndex'
    ) -Context 'automation lane'
    foreach ($property in @('laneId', 'owner', 'identity', 'invoker', 'worktree', 'branch')) {
      if ([string]::IsNullOrWhiteSpace([string]$lane.$property)) {
        throw [IO.InvalidDataException]::new("Automation lane $property is invalid")
      }
    }
    if (-not $laneIds.Add([string]$lane.laneId)) {
      throw [IO.InvalidDataException]::new('Automation batch contains duplicate laneId')
    }
    if ([string]$lane.baseCommit -cne [string]$Batch.baseCommit) {
      throw [IO.InvalidDataException]::new('Automation lane baseCommit does not match the batch')
    }
    if ([string]$lane.integrationState -cnotin @('pending', 'waiting', 'integrating', 'integrated', 'held_conflict', 'stale_selection', 'failed')) {
      throw [IO.InvalidDataException]::new('Automation lane integrationState is invalid')
    }
    if ($lane.queueIndex -isnot [long] -or [int]$lane.queueIndex -lt 0 -or -not $queueIndexes.Add([int]$lane.queueIndex)) {
      throw [IO.InvalidDataException]::new('Automation lane queueIndex is invalid')
    }
    Assert-PropertySet -Object $lane.taskClaim -Expected @(
      'taskId',
      'route',
      'owner',
      'dispatchState',
      'cardHash',
      'queueRowHash'
    ) -Context 'automation task claim'
    if (
      [string]::IsNullOrWhiteSpace([string]$lane.taskClaim.taskId) -or
      -not $taskIds.Add([string]$lane.taskClaim.taskId) -or
      [string]$lane.taskClaim.owner -cne [string]$lane.owner -or
      [string]$lane.taskClaim.dispatchState -cne 'ready' -or
      [string]$lane.taskClaim.cardHash -cnotmatch '\A[0-9a-f]{64}\z' -or
      [string]$lane.taskClaim.queueRowHash -cnotmatch '\A[0-9a-f]{64}\z'
    ) {
      throw [IO.InvalidDataException]::new('Automation task claim is invalid')
    }
    if (@($lane.acceptedRoutes) -cnotcontains [string]$lane.taskClaim.route) {
      throw [IO.InvalidDataException]::new('Automation task route is not accepted by its lane')
    }
    $workerPaths = @(Convert-ChangedPaths -Value (@($lane.workerPaths) -join '|'))
    $coordinatorPaths = @(Convert-ChangedPaths -Value (@($lane.coordinatorPaths) -join '|'))
    $factPaths = @(Convert-ChangedPaths -Value (@($lane.factPaths) -join '|'))
    if ($workerPaths.Count -lt 1 -or $coordinatorPaths.Count -lt 1) {
      throw [IO.InvalidDataException]::new('Automation lane path classification is empty')
    }
    foreach ($workerPath in $workerPaths) {
      if ($coordinatorPaths -ccontains $workerPath) {
        throw [IO.InvalidDataException]::new('Automation lane path classifications overlap')
      }
    }
    Assert-PropertySet -Object $lane.processOrSession -Expected @(
      'state',
      'processId',
      'sessionId',
      'resultPath',
      'startedAt',
      'completedAt'
    ) -Context 'automation lane process/session'
    if ([string]$lane.processOrSession.state -cnotin @('pending', 'running', 'terminal')) {
      throw [IO.InvalidDataException]::new('Automation lane process/session state is invalid')
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$lane.processOrSession.resultPath)) {
      $resultPath = [IO.Path]::GetFullPath([string]$lane.processOrSession.resultPath)
      if (-not (Test-PathWithinRoot -Path $resultPath -Root (Get-ApprovedPrivateRoot))) {
        throw [IO.InvalidDataException]::new('Automation lane resultPath is outside the private runtime')
      }
    }
    if ($null -ne $lane.workerTerminal) {
      if (
        [string]$lane.workerTerminal.status -cnotin @('completed', 'needs_decision', 'blocked', 'failed') -or
        [string]$lane.workerTerminal.batchId -cne [string]$Batch.batchId -or
        [string]$lane.workerTerminal.laneId -cne [string]$lane.laneId -or
        [string]$lane.workerTerminal.taskId -cne [string]$lane.taskClaim.taskId
      ) {
        throw [IO.InvalidDataException]::new('Automation lane worker terminal is invalid')
      }
    }
  }
}

function Assert-RuntimeState {
  param([Parameter(Mandatory = $true)][object]$State)

  Assert-PropertySet -Object $State -Expected @(
    'schemaVersion',
    'lease',
    'batch',
    'recovery',
    'blocking',
    'lastResult'
  ) -Context 'runtime state'
  if ($State.schemaVersion -ne 4) {
    throw [IO.InvalidDataException]::new('Unsupported runtime state schema')
  }
  if ($null -ne $State.lease) {
    Assert-PropertySet -Object $State.lease -Expected @(
      'runId',
      'taskId',
      'owner',
      'repositoryRoot',
      'startedAt',
      'expiresAt'
    ) -Context 'lease'
  }
  if ($null -ne $State.batch) {
    Assert-AutomationBatch -Batch $State.batch -Lease $State.lease
  }
  if ($null -ne $State.recovery) {
    $recoveryTrigger = [string]$State.recovery.trigger
    if ($recoveryTrigger -cnotin @('decision', 'interruption')) {
      throw [IO.InvalidDataException]::new('Recovery trigger is invalid')
    }
    if ($recoveryTrigger -ceq 'decision') {
      Assert-PropertySet -Object $State.recovery -Expected @(
        'trigger',
        'runId',
        'taskId',
        'owner',
        'repositoryRoot',
        'decisionId',
        'decisionRequestPath',
        'hasUncommittedChanges',
        'changedPaths'
      ) -Context 'decision recovery'
    } else {
      Assert-PropertySet -Object $State.recovery -Expected @(
        'trigger',
        'runId',
        'taskId',
        'owner',
        'repositoryRoot',
        'resumeKind',
        'resumeId',
        'hasUncommittedChanges',
        'changedPaths'
      ) -Context 'interruption recovery'
    }
  }
  if ($null -eq $State.blocking) {
    throw [IO.InvalidDataException]::new('Runtime state is missing blocking state')
  }
  Assert-PropertySet -Object $State.blocking -Expected @(
    'fingerprint',
    'count',
    'pauseRequested'
  ) -Context 'blocking'
  if ($null -ne $State.lastResult) {
    Assert-PropertySet -Object $State.lastResult -Expected @(
      'runId',
      'category',
      'taskId',
      'detailCode',
      'recordedAt'
    ) -Context 'last result'
  }
}

function Convert-RuntimeStateSchema {
  param([Parameter(Mandatory = $true)][object]$State)

  if ($State.schemaVersion -eq 4) {
    return $State
  }
  if ($State.schemaVersion -cnotin @(1, 2, 3)) {
    throw [IO.InvalidDataException]::new('Unsupported runtime state schema')
  }
  if ($State.schemaVersion -eq 1 -and $null -ne $State.recovery) {
    $State.recovery | Add-Member -NotePropertyName trigger -NotePropertyValue 'decision'
    $State.recovery | Add-Member -NotePropertyName runId -NotePropertyValue $null
  }
  if ($State.schemaVersion -eq 1 -and $null -ne $State.lastResult) {
    $State.lastResult | Add-Member -NotePropertyName runId -NotePropertyValue $null
  }
  $recovery = $null
  if ($null -ne $State.recovery) {
    if ([string]$State.recovery.trigger -ceq 'decision') {
      $recovery = [pscustomobject][ordered]@{
        trigger = 'decision'
        runId = $State.recovery.runId
        taskId = $State.recovery.taskId
        owner = $State.recovery.owner
        repositoryRoot = $State.recovery.repositoryRoot
        decisionId = $State.recovery.decisionId
        decisionRequestPath = $State.recovery.decisionRequestPath
        hasUncommittedChanges = [bool]$State.recovery.hasUncommittedChanges
        changedPaths = @($State.recovery.changedPaths)
      }
    } elseif ([string]$State.recovery.trigger -ceq 'interruption') {
      $recovery = [pscustomobject][ordered]@{
        trigger = 'interruption'
        runId = $State.recovery.runId
        taskId = $State.recovery.taskId
        owner = $State.recovery.owner
        repositoryRoot = $State.recovery.repositoryRoot
        resumeKind = $State.recovery.resumeKind
        resumeId = $State.recovery.resumeId
        hasUncommittedChanges = [bool]$State.recovery.hasUncommittedChanges
        changedPaths = @($State.recovery.changedPaths)
      }
    } else {
      throw [IO.InvalidDataException]::new('Recovery trigger is invalid')
    }
  }
  [pscustomobject][ordered]@{
    schemaVersion = 4
    lease = $State.lease
    batch = $null
    recovery = $recovery
    blocking = $State.blocking
    lastResult = $State.lastResult
  }
}

function Initialize-StateRoot {
  param([Parameter(Mandatory = $true)][string]$Path)

  [IO.Directory]::CreateDirectory($Path) | Out-Null
  Set-PrivatePathAcl -Path $Path -Directory
  Assert-PrivatePathAcl -Path $Path -Directory
}

function Read-RuntimeState {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return New-RuntimeState
  }
  Assert-PrivatePathAcl -Path $Path
  $bytes = [IO.File]::ReadAllBytes($Path)
  try {
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $state = $text | ConvertFrom-Json -Depth 100
  } catch {
    throw [IO.InvalidDataException]::new('Runtime state is not valid UTF-8 JSON', $_.Exception)
  }
  $state = Convert-RuntimeStateSchema -State $state
  Assert-RuntimeState -State $state
  $state
}

function Write-RuntimeState {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [object]$State
  )

  Assert-RuntimeState -State $State
  $json = $State | ConvertTo-Json -Compress -Depth 30
  if ($json -match '"(?i:providerToken|tenantKey|openId|chatId|messageId|eventId|secret)"\s*:') {
    throw [IO.InvalidDataException]::new('Runtime state contains forbidden secret fields')
  }
  $temporaryPath = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
  try {
    [IO.File]::WriteAllText($temporaryPath, $json, [Text.UTF8Encoding]::new($false))
    Set-PrivatePathAcl -Path $temporaryPath
    Assert-PrivatePathAcl -Path $temporaryPath
    [IO.File]::Move($temporaryPath, $Path, $true)
    Set-PrivatePathAcl -Path $Path
    Assert-PrivatePathAcl -Path $Path
  } finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
      Remove-Item -LiteralPath $temporaryPath -Force
    }
  }
}

function Get-MutexName {
  param([Parameter(Mandatory = $true)][string]$Path)

  $bytes = [Text.Encoding]::UTF8.GetBytes($Path.ToUpperInvariant())
  $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
  "Local\TZGHourlyAutomationLease-$hash"
}

function Test-LeaseExpired {
  param(
    [AllowNull()]
    [object]$Lease,
    [Parameter(Mandatory = $true)]
    [DateTimeOffset]$Now
  )

  if ($null -eq $Lease) {
    return $false
  }
  $expiresAt = [DateTimeOffset]::MinValue
  if (-not [DateTimeOffset]::TryParse(
    [string]$Lease.expiresAt,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind,
    [ref]$expiresAt
  )) {
    throw [IO.InvalidDataException]::new('Lease expiry is invalid')
  }
  $expiresAt -le $Now
}

function New-Lease {
  param(
    [Parameter(Mandatory = $true)]
    [string]$LeaseTaskId,
    [Parameter(Mandatory = $true)]
    [string]$LeaseOwner,
    [Parameter(Mandatory = $true)]
    [string]$LeaseRepositoryRoot,
    [Parameter(Mandatory = $true)]
    [DateTimeOffset]$Now,
    [Parameter(Mandatory = $true)]
    [int]$DurationSeconds,
    [string]$ExistingRunId
  )

  [pscustomobject][ordered]@{
    runId = if ([string]::IsNullOrWhiteSpace($ExistingRunId)) { [Guid]::NewGuid().ToString() } else { $ExistingRunId }
    taskId = $LeaseTaskId
    owner = $LeaseOwner
    repositoryRoot = $LeaseRepositoryRoot
    startedAt = $Now.ToString('o')
    expiresAt = $Now.AddSeconds($DurationSeconds).ToString('o')
  }
}

function Test-CurrentRun {
  param(
    [AllowNull()]
    [object]$Lease,
    [AllowNull()]
    [string]$ExpectedRunId
  )

  $null -ne $Lease -and
    -not [string]::IsNullOrWhiteSpace($ExpectedRunId) -and
    ([string]$Lease.runId).Equals($ExpectedRunId, [StringComparison]::Ordinal)
}

$normalizedStateRoot = $null
$statePath = $null
$mutex = $null
$mutexHeld = $false
$result = $null
$resultExitCode = 0

try {
  $normalizedStateRoot = Resolve-StateRoot -Path $StateRoot
  Initialize-StateRoot -Path $normalizedStateRoot
  $statePath = Join-Path $normalizedStateRoot 'runtime.json'

  $mutex = [Threading.Mutex]::new($false, (Get-MutexName -Path $normalizedStateRoot))
  try {
    try {
      $mutexHeld = $mutex.WaitOne([TimeSpan]::FromSeconds(10))
    } catch [Threading.AbandonedMutexException] {
      $mutexHeld = $true
    }
    if (-not $mutexHeld) {
      throw [TimeoutException]::new('Timed out waiting for runtime state mutex')
    }

    $state = Read-RuntimeState -Path $statePath
    $now = [DateTimeOffset]::UtcNow

    switch ($Action) {
      'Show' {
        $leaseStatus = if ($null -eq $state.lease) {
          'none'
        } elseif (Test-LeaseExpired -Lease $state.lease -Now $now) {
          'expired'
        } else {
          'active'
        }
        $result = New-Result -Status 'OK' -Values @{
          leaseStatus = $leaseStatus
          state = $state
        }
      }

      'Acquire' {
        Assert-StableText -Value $TaskId -ParameterName 'TaskId'
        Assert-StableText -Value $Owner -ParameterName 'Owner'
        $normalizedRepositoryRoot = Resolve-RepositoryRoot -Path $RepositoryRoot
        if ([bool]$state.blocking.pauseRequested) {
          $result = New-Result -Status 'SUSPENDED' -Values @{
            fingerprint = $state.blocking.fingerprint
            count = $state.blocking.count
          }
          break
        }
        $leaseExpired = Test-LeaseExpired -Lease $state.lease -Now $now
        if ($null -ne $state.lease -and -not $leaseExpired) {
          $result = New-Result -Status 'BUSY' -Values @{
            runId = $state.lease.runId
            taskId = $state.lease.taskId
            owner = $state.lease.owner
            expiresAt = $state.lease.expiresAt
          }
          break
        }
        if ($null -ne $state.batch) {
          $result = New-Result -Status 'BATCH_ONLY' -Values @{
            batchId = $state.batch.batchId
            batchStatus = $state.batch.status
          }
          break
        }
        if ($ResumeRecovery) {
          if ($null -eq $state.recovery) {
            $result = New-Result -Status 'RECOVERY_NOT_FOUND'
            $resultExitCode = 2
            break
          }
          $recoveryTrigger = [string]$state.recovery.trigger
          if ($recoveryTrigger -cnotin @('decision', 'interruption')) {
            $result = New-Result -Status 'RECOVERY_INVALID'
            $resultExitCode = 2
            break
          }
          if ($recoveryTrigger -ceq 'decision' -and [string]::IsNullOrWhiteSpace($DecisionId)) {
            $result = New-Result -Status 'DECISION_ID_REQUIRED'
            $resultExitCode = 2
            break
          }
          $matchesRecovery =
            ([string]$state.recovery.taskId).Equals($TaskId, [StringComparison]::Ordinal) -and
            ([string]$state.recovery.owner).Equals($Owner, [StringComparison]::Ordinal) -and
            ([string]$state.recovery.repositoryRoot).Equals($normalizedRepositoryRoot, [StringComparison]::OrdinalIgnoreCase)
          if ($recoveryTrigger -ceq 'decision') {
            $matchesRecovery =
              $matchesRecovery -and
              ([string]$state.recovery.decisionId).Equals($DecisionId, [StringComparison]::Ordinal)
          }
          if (-not $matchesRecovery) {
            $result = New-Result -Status 'RECOVERY_MISMATCH'
            $resultExitCode = 2
            break
          }
          $state.lease = New-Lease `
            -LeaseTaskId $TaskId `
            -LeaseOwner $Owner `
            -LeaseRepositoryRoot $normalizedRepositoryRoot `
            -Now $now `
            -DurationSeconds $LeaseSeconds
          Write-RuntimeState -Path $statePath -State $state
          $recoveryValues = @{
            runId = $state.lease.runId
            taskId = $state.recovery.taskId
            owner = $state.recovery.owner
            repositoryRoot = $state.recovery.repositoryRoot
            trigger = $state.recovery.trigger
            changedPaths = @($state.recovery.changedPaths)
          }
          if ($recoveryTrigger -ceq 'decision') {
            $recoveryValues.decisionId = $state.recovery.decisionId
            $recoveryValues.decisionRequestPath = $state.recovery.decisionRequestPath
          } else {
            $recoveryValues.resumeKind = $state.recovery.resumeKind
            $recoveryValues.resumeId = $state.recovery.resumeId
          }
          $result = New-Result -Status 'RECOVERY_ACQUIRED' -Values $recoveryValues
          break
        }
        if (
          ($null -eq $state.lease -or $leaseExpired) -and
          $null -ne $state.recovery
        ) {
          $recoveryValues = @{
            taskId = $state.recovery.taskId
            owner = $state.recovery.owner
            trigger = $state.recovery.trigger
            changedPaths = @($state.recovery.changedPaths)
          }
          if ([string]$state.recovery.trigger -ceq 'decision') {
            $recoveryValues.decisionId = $state.recovery.decisionId
          } else {
            $recoveryValues.resumeKind = $state.recovery.resumeKind
            $recoveryValues.resumeId = $state.recovery.resumeId
          }
          $result = New-Result -Status 'RECOVERY_ONLY' -Values $recoveryValues
          break
        }
        $state.lease = New-Lease `
          -LeaseTaskId $TaskId `
          -LeaseOwner $Owner `
          -LeaseRepositoryRoot $normalizedRepositoryRoot `
          -Now $now `
          -DurationSeconds $LeaseSeconds
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'ACQUIRED' -Values @{
          runId = $state.lease.runId
          taskId = $state.lease.taskId
          owner = $state.lease.owner
          repositoryRoot = $state.lease.repositoryRoot
          startedAt = $state.lease.startedAt
          expiresAt = $state.lease.expiresAt
        }
      }

      'SaveRecovery' {
        if (-not (Test-CurrentRun -Lease $state.lease -ExpectedRunId $RunId)) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        Assert-StableText -Value $DecisionId -ParameterName 'DecisionId'
        $normalizedRequestPath = Resolve-ApprovedPrivateFile `
          -Path $DecisionRequestPath `
          -ParameterName 'DecisionRequestPath'
        Assert-DecisionConsumeRequest `
          -Path $normalizedRequestPath `
          -ExpectedDecisionId $DecisionId
        if (
          -not [string]::IsNullOrWhiteSpace($CodexThreadId) -or
          -not [string]::IsNullOrWhiteSpace($ClaudeSessionId) -or
          [bool]$HasUncommittedChanges -or
          -not [string]::IsNullOrWhiteSpace($ChangedPaths)
        ) {
          throw [ArgumentException]::new('Decision recovery cannot contain session or uncommitted-change fields')
        }
        $state.recovery = [pscustomobject][ordered]@{
          trigger = 'decision'
          runId = $state.lease.runId
          taskId = $state.lease.taskId
          owner = $state.lease.owner
          repositoryRoot = $state.lease.repositoryRoot
          decisionId = $DecisionId
          decisionRequestPath = $normalizedRequestPath
          hasUncommittedChanges = $false
          changedPaths = @()
        }
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'RECOVERY_SAVED' -Values @{ recovery = $state.recovery }
      }

      'SaveBatch' {
        if (-not (Test-CurrentRun -Lease $state.lease -ExpectedRunId $RunId)) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        $normalizedBatchPath = Resolve-ApprovedPrivateFile `
          -Path $BatchStatePath `
          -ParameterName 'BatchStatePath'
        try {
          $batchBytes = [IO.File]::ReadAllBytes($normalizedBatchPath)
          if ($batchBytes.Length -gt 4194304) {
            throw [IO.InvalidDataException]::new('Automation batch exceeds 4 MiB')
          }
          $batchText = [Text.UTF8Encoding]::new($false, $true).GetString($batchBytes)
          $batch = $batchText | ConvertFrom-Json -Depth 100
        } catch {
          throw [IO.InvalidDataException]::new('BatchStatePath must contain valid UTF-8 JSON', $_.Exception)
        }
        Assert-AutomationBatch -Batch $batch -Lease $state.lease
        $state.batch = $batch
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'BATCH_SAVED' -Values @{
          batchId = $state.batch.batchId
          lanes = @($state.batch.lanes).Count
          batchStatus = $state.batch.status
        }
      }

      'ResumeBatch' {
        Assert-StableText -Value $BatchId -ParameterName 'BatchId'
        $parsedBatchId = [Guid]::Empty
        if (-not [Guid]::TryParse($BatchId, [ref]$parsedBatchId)) {
          throw [ArgumentException]::new('BatchId is invalid')
        }
        if ($null -eq $state.batch -or [string]$state.batch.batchId -cne $BatchId) {
          $result = New-Result -Status 'BATCH_NOT_FOUND'
          $resultExitCode = 2
          break
        }
        if ([bool]$state.blocking.pauseRequested) {
          $result = New-Result -Status 'SUSPENDED'
          break
        }
        $leaseExpired = Test-LeaseExpired -Lease $state.lease -Now $now
        if ($null -ne $state.lease -and -not $leaseExpired) {
          $result = New-Result -Status 'BUSY' -Values @{
            runId = $state.lease.runId
            taskId = $state.lease.taskId
            owner = $state.lease.owner
            expiresAt = $state.lease.expiresAt
          }
          break
        }
        $state.lease = New-Lease `
          -LeaseTaskId 'AUTOMATION-BATCH' `
          -LeaseOwner 'coordinator' `
          -LeaseRepositoryRoot ([string]$state.batch.repositoryRoot) `
          -Now $now `
          -DurationSeconds $LeaseSeconds `
          -ExistingRunId ([string]$state.batch.runId)
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'BATCH_RESUMED' -Values @{
          runId = $state.lease.runId
          batchId = $state.batch.batchId
          batchStatus = $state.batch.status
          expiresAt = $state.lease.expiresAt
        }
      }

      'ClearBatch' {
        if (-not (Test-CurrentRun -Lease $state.lease -ExpectedRunId $RunId)) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        if ($null -eq $state.batch) {
          $result = New-Result -Status 'BATCH_NOT_FOUND'
          $resultExitCode = 2
          break
        }
        if ([string]$state.batch.status -cne 'closed') {
          $result = New-Result -Status 'BATCH_NOT_CLOSED'
          $resultExitCode = 2
          break
        }
        $preserved = @($state.batch.lanes | Where-Object {
          [string]$_.integrationState -cin @('held_conflict', 'stale_selection') -or
          (
            [string]$_.integrationState -ceq 'failed' -and
            (
              $null -eq $_.workerTerminal -or
              [string]$_.workerTerminal.status -cin @('completed', 'needs_decision')
            )
          )
        })
        if ($preserved.Count -gt 0) {
          $result = New-Result -Status 'BATCH_EVIDENCE_PRESERVED'
          $resultExitCode = 2
          break
        }
        $state.batch = $null
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'BATCH_CLEARED'
      }

      'SaveInterruption' {
        if (-not (Test-CurrentRun -Lease $state.lease -ExpectedRunId $RunId)) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        $hasCodex = -not [string]::IsNullOrWhiteSpace($CodexThreadId)
        $hasClaude = -not [string]::IsNullOrWhiteSpace($ClaudeSessionId)
        if ($hasCodex -eq $hasClaude) {
          throw [ArgumentException]::new('Exactly one CodexThreadId or ClaudeSessionId is required')
        }
        $resumeKind = if ($hasCodex) { 'codex' } else { 'claude' }
        $resumeId = if ($hasCodex) { $CodexThreadId } else { $ClaudeSessionId }
        Assert-StableText -Value $resumeId -ParameterName 'resumeId' -MaximumLength 512
        $normalizedChangedPaths = Convert-ChangedPaths -Value $ChangedPaths
        if (-not $HasUncommittedChanges -or $normalizedChangedPaths.Count -eq 0) {
          throw [ArgumentException]::new('Interruption recovery requires uncommitted changed paths')
        }
        $state.recovery = [pscustomobject][ordered]@{
          trigger = 'interruption'
          runId = $state.lease.runId
          taskId = $state.lease.taskId
          owner = $state.lease.owner
          repositoryRoot = $state.lease.repositoryRoot
          resumeKind = $resumeKind
          resumeId = $resumeId
          hasUncommittedChanges = $true
          changedPaths = @($normalizedChangedPaths)
        }
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'RECOVERY_SAVED' -Values @{ recovery = $state.recovery }
      }

      'ClearRecovery' {
        if (-not (Test-CurrentRun -Lease $state.lease -ExpectedRunId $RunId)) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        $state.recovery = $null
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'RECOVERY_CLEARED'
      }

      'RecordResult' {
        if (-not (Test-CurrentRun -Lease $state.lease -ExpectedRunId $RunId)) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        Assert-StableText -Value $TaskId -ParameterName 'TaskId'
        Assert-StableText -Value $DetailCode -ParameterName 'DetailCode'
        if (-not ([string]$state.lease.taskId).Equals($TaskId, [StringComparison]::Ordinal)) {
          throw [ArgumentException]::new('TaskId must match the active lease')
        }
        if (
          $Category -eq 'waiting_decision' -and
          (
            $null -eq $state.recovery -or
            [string]$state.recovery.trigger -cne 'decision' -or
            -not ([string]$state.recovery.taskId).Equals($TaskId, [StringComparison]::Ordinal) -or
            -not ([string]$state.recovery.runId).Equals($RunId, [StringComparison]::Ordinal)
          )
        ) {
          $result = New-Result -Status 'RECOVERY_REQUIRED'
          $resultExitCode = 2
          break
        }
        if ($Category -eq 'blocked') {
          Assert-StableText -Value $BlockingFingerprint -ParameterName 'BlockingFingerprint' -MaximumLength 512
          if (
            $null -ne $state.blocking.fingerprint -and
            ([string]$state.blocking.fingerprint).Equals($BlockingFingerprint, [StringComparison]::Ordinal)
          ) {
            $state.blocking.count = [int]$state.blocking.count + 1
          } else {
            $state.blocking.fingerprint = $BlockingFingerprint
            $state.blocking.count = 1
          }
          $state.blocking.pauseRequested = [int]$state.blocking.count -ge 2
        } elseif ($Category -in @('success', 'refilled')) {
          $state.blocking.fingerprint = $null
          $state.blocking.count = 0
          $state.blocking.pauseRequested = $false
        }
        $state.lastResult = [pscustomobject][ordered]@{
          runId = $state.lease.runId
          category = $Category
          taskId = $TaskId
          detailCode = $DetailCode
          recordedAt = $now.ToString('o')
        }
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'RECORDED' -Values @{
          lastResult = $state.lastResult
          blocking = $state.blocking
        }
      }

      'ClearBlocking' {
        if ($null -ne $state.lease) {
          $result = New-Result -Status 'BUSY'
          break
        }
        if ($null -ne $state.recovery) {
          $result = New-Result -Status 'RECOVERY_PRESENT'
          break
        }
        $state.blocking.fingerprint = $null
        $state.blocking.count = 0
        $state.blocking.pauseRequested = $false
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'BLOCKING_CLEARED' -Values @{
          blocking = $state.blocking
        }
      }

      'Release' {
        if (-not (Test-CurrentRun -Lease $state.lease -ExpectedRunId $RunId)) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        if ($null -ne $state.batch -and [string]$state.batch.status -cne 'closed') {
          $result = New-Result -Status 'BATCH_OPEN'
          $resultExitCode = 2
          break
        }
        $state.lease = $null
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'RELEASED'
      }
    }
  } finally {
    if ($mutexHeld) {
      $mutex.ReleaseMutex()
      $mutexHeld = $false
    }
    if ($null -ne $mutex) {
      $mutex.Dispose()
      $mutex = $null
    }
  }
} catch [ArgumentException] {
  [Console]::Error.WriteLine('hourly-automation-lease: INVALID_ARGUMENT')
  $result = New-Result -Status 'INVALID_ARGUMENT'
  $resultExitCode = 2
} catch [IO.InvalidDataException] {
  [Console]::Error.WriteLine('hourly-automation-lease: STATE_CORRUPT')
  $result = New-Result -Status 'STATE_CORRUPT'
  $resultExitCode = 3
} catch [UnauthorizedAccessException] {
  [Console]::Error.WriteLine('hourly-automation-lease: ACL_UNSAFE')
  $result = New-Result -Status 'ACL_UNSAFE'
  $resultExitCode = 3
} catch [Security.SecurityException] {
  [Console]::Error.WriteLine('hourly-automation-lease: ACL_UNSAFE')
  $result = New-Result -Status 'ACL_UNSAFE'
  $resultExitCode = 3
} catch [TimeoutException] {
  [Console]::Error.WriteLine('hourly-automation-lease: LOCK_TIMEOUT')
  $result = New-Result -Status 'LOCK_TIMEOUT'
  $resultExitCode = 3
} catch {
  $failureType = $_.Exception.GetType().FullName
  $failureLine = $_.InvocationInfo.ScriptLineNumber
  [Console]::Error.WriteLine("hourly-automation-lease: FAILED type=$failureType line=$failureLine")
  $result = New-Result -Status 'FAILED'
  $resultExitCode = 3
}

Write-ResultAndExit -Result $result -ExitCode $resultExitCode
