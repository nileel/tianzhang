#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Show', 'Acquire', 'SaveRecovery', 'ClearRecovery', 'QueueResume', 'TakeResume', 'RecordResult', 'ClearBlocking', 'Release')]
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
  [string]$ChangedPaths,
  [string]$ReplyPath
)

$ErrorActionPreference = 'Stop'

$aclScript = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $aclScript

function New-RuntimeState {
  [pscustomobject][ordered]@{
    schemaVersion = 1
    lease = $null
    recovery = $null
    pendingResumes = @()
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

function Assert-RuntimeState {
  param([Parameter(Mandatory = $true)][object]$State)

  Assert-PropertySet -Object $State -Expected @(
    'schemaVersion',
    'lease',
    'recovery',
    'pendingResumes',
    'blocking',
    'lastResult'
  ) -Context 'runtime state'
  if ($State.schemaVersion -ne 1) {
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
  if ($null -ne $State.recovery) {
    Assert-PropertySet -Object $State.recovery -Expected @(
      'taskId',
      'owner',
      'repositoryRoot',
      'resumeKind',
      'resumeId',
      'decisionId',
      'decisionRequestPath',
      'hasUncommittedChanges',
      'changedPaths'
    ) -Context 'recovery'
  }
  foreach ($pending in @($State.pendingResumes)) {
    Assert-PropertySet -Object $pending -Expected @('decisionId', 'replyPath', 'queuedAt') -Context 'pending resume'
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
      'category',
      'taskId',
      'detailCode',
      'recordedAt'
    ) -Context 'last result'
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
  Assert-RuntimeState -State $state
  $state.pendingResumes = @($state.pendingResumes)
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
    [int]$DurationSeconds
  )

  [pscustomobject][ordered]@{
    runId = [Guid]::NewGuid().ToString()
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

function New-DispatchResult {
  param(
    [Parameter(Mandatory = $true)]
    [object]$State,
    [Parameter(Mandatory = $true)]
    [string]$DispatchReplyPath
  )

  New-Result -Status 'DISPATCH' -Values @{
    runId = $State.lease.runId
    taskId = $State.recovery.taskId
    owner = $State.recovery.owner
    repositoryRoot = $State.recovery.repositoryRoot
    resumeKind = $State.recovery.resumeKind
    resumeId = $State.recovery.resumeId
    decisionId = $State.recovery.decisionId
    decisionRequestPath = $State.recovery.decisionRequestPath
    replyPath = $DispatchReplyPath
  }
}

function Start-RecoveryLease {
  param(
    [Parameter(Mandatory = $true)]
    [object]$State,
    [Parameter(Mandatory = $true)]
    [DateTimeOffset]$Now,
    [Parameter(Mandatory = $true)]
    [int]$DurationSeconds
  )

  if ($null -eq $State.recovery) {
    throw [IO.InvalidDataException]::new('Pending resume has no recovery pointer')
  }
  $State.lease = New-Lease `
    -LeaseTaskId ([string]$State.recovery.taskId) `
    -LeaseOwner ([string]$State.recovery.owner) `
    -LeaseRepositoryRoot ([string]$State.recovery.repositoryRoot) `
    -Now $Now `
    -DurationSeconds $DurationSeconds
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
        $result = New-Result -Status 'OK' -Values @{ state = $state }
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
        if (
          $leaseExpired -and
          $null -ne $state.recovery -and
          [bool]$state.recovery.hasUncommittedChanges
        ) {
          $result = New-Result -Status 'RECOVERY_ONLY' -Values @{
            taskId = $state.recovery.taskId
            owner = $state.recovery.owner
            resumeKind = $state.recovery.resumeKind
            resumeId = $state.recovery.resumeId
            decisionId = $state.recovery.decisionId
            changedPaths = @($state.recovery.changedPaths)
          }
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
        $hasCodex = -not [string]::IsNullOrWhiteSpace($CodexThreadId)
        $hasClaude = -not [string]::IsNullOrWhiteSpace($ClaudeSessionId)
        if ($hasCodex -eq $hasClaude) {
          throw [ArgumentException]::new('Exactly one CodexThreadId or ClaudeSessionId is required')
        }
        $resumeKind = if ($hasCodex) { 'codex' } else { 'claude' }
        $resumeId = if ($hasCodex) { $CodexThreadId } else { $ClaudeSessionId }
        Assert-StableText -Value $resumeId -ParameterName 'resumeId' -MaximumLength 512
        $normalizedChangedPaths = Convert-ChangedPaths -Value $ChangedPaths
        if (-not $HasUncommittedChanges -and $normalizedChangedPaths.Count -gt 0) {
          throw [ArgumentException]::new('ChangedPaths require HasUncommittedChanges')
        }
        $state.recovery = [pscustomobject][ordered]@{
          taskId = $state.lease.taskId
          owner = $state.lease.owner
          repositoryRoot = $state.lease.repositoryRoot
          resumeKind = $resumeKind
          resumeId = $resumeId
          decisionId = $DecisionId
          decisionRequestPath = $normalizedRequestPath
          hasUncommittedChanges = [bool]$HasUncommittedChanges
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

      'QueueResume' {
        Assert-StableText -Value $DecisionId -ParameterName 'DecisionId'
        $normalizedReplyPath = Resolve-ApprovedPrivateFile -Path $ReplyPath -ParameterName 'ReplyPath'
        if ($null -eq $state.recovery -or $state.recovery.decisionId -ne $DecisionId) {
          $result = New-Result -Status 'RECOVERY_NOT_FOUND'
          $resultExitCode = 2
          break
        }
        $leaseExpired = Test-LeaseExpired -Lease $state.lease -Now $now
        if ($null -ne $state.lease -and -not $leaseExpired) {
          $duplicate = $false
          foreach ($pending in @($state.pendingResumes)) {
            if (
              ([string]$pending.decisionId).Equals($DecisionId, [StringComparison]::Ordinal) -and
              ([string]$pending.replyPath).Equals($normalizedReplyPath, [StringComparison]::OrdinalIgnoreCase)
            ) {
              $duplicate = $true
              break
            }
          }
          if (-not $duplicate) {
            $pendingList = [Collections.Generic.List[object]]::new()
            foreach ($pending in @($state.pendingResumes)) {
              $pendingList.Add($pending)
            }
            $pendingList.Add([pscustomobject][ordered]@{
              decisionId = $DecisionId
              replyPath = $normalizedReplyPath
              queuedAt = $now.ToString('o')
            })
            $state.pendingResumes = @($pendingList)
            Write-RuntimeState -Path $statePath -State $state
          }
          $result = New-Result -Status 'QUEUED' -Values @{
            duplicate = $duplicate
            pendingCount = @($state.pendingResumes).Count
          }
          break
        }
        Start-RecoveryLease -State $state -Now $now -DurationSeconds $LeaseSeconds
        $state.pendingResumes = @(
          $state.pendingResumes | Where-Object {
            -not (
              ([string]$_.decisionId).Equals($DecisionId, [StringComparison]::Ordinal) -and
              ([string]$_.replyPath).Equals($normalizedReplyPath, [StringComparison]::OrdinalIgnoreCase)
            )
          }
        )
        Write-RuntimeState -Path $statePath -State $state
        $result = New-DispatchResult -State $state -DispatchReplyPath $normalizedReplyPath
      }

      'TakeResume' {
        $leaseExpired = Test-LeaseExpired -Lease $state.lease -Now $now
        if ($null -ne $state.lease -and -not $leaseExpired) {
          $result = New-Result -Status 'BUSY' -Values @{
            runId = $state.lease.runId
            taskId = $state.lease.taskId
            expiresAt = $state.lease.expiresAt
          }
          break
        }
        $pending = @($state.pendingResumes)
        if ($pending.Count -eq 0) {
          $result = New-Result -Status 'EMPTY'
          break
        }
        $next = $pending[0]
        if ($null -eq $state.recovery -or $state.recovery.decisionId -ne $next.decisionId) {
          throw [IO.InvalidDataException]::new('Pending resume does not match the recovery pointer')
        }
        Start-RecoveryLease -State $state -Now $now -DurationSeconds $LeaseSeconds
        if ($pending.Count -eq 1) {
          $state.pendingResumes = [object[]]@()
        } else {
          $state.pendingResumes = @($pending[1..($pending.Count - 1)])
        }
        Write-RuntimeState -Path $statePath -State $state
        $result = New-DispatchResult -State $state -DispatchReplyPath ([string]$next.replyPath)
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
        if (@($state.pendingResumes).Count -gt 0) {
          $result = New-Result -Status 'PENDING_RESUMES'
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
        $state.lease = $null
        $readyResume = @($state.pendingResumes).Count -gt 0
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'RELEASED' -Values @{
          readyResume = $readyResume
          pendingCount = @($state.pendingResumes).Count
        }
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
