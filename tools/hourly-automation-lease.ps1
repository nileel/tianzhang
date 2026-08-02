#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Show', 'ClaimRun', 'UpdateRun', 'AcquireIntegration', 'ReleaseIntegration', 'CompleteRun')]
  [string]$Action,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [ValidateSet('codex', 'deepseek')]
  [string]$Owner,
  [string]$TaskId,
  [ValidateSet('codex_execute', 'codex_review', 'queue_maintenance', 'external_execute')]
  [string]$Route,
  [string]$RepositoryRoot,
  [string]$MainBranch = 'master',
  [string]$BaseCommit,
  [string]$TaskCardDigest,
  [string]$RunId,
  [ValidateSet('developing', 'candidate_ready', 'canonical_ready', 'integrated', 'attention_required')]
  [string]$RunState,
  [ValidateSet('codex_cli', 'claude_cli')]
  [string]$SessionKind,
  [string]$SessionId,
  [string]$CandidateCommit,
  [string]$CandidateResultPath,
  [string]$CanonicalBranch,
  [string]$CanonicalBase,
  [string]$CanonicalHead,
  [string]$RecoveryReason,
  [string]$ExpectedRecoveryReason,
  [string]$ExpectedMainHead,
  [ValidateRange(1, 3600)]
  [int]$IntegrationLeaseSeconds = 300,
  [ValidateSet('success', 'no_candidate', 'failed')]
  [string]$CompletionCategory,
  [string]$DetailCode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$aclScript = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $aclScript

function New-RuntimeState {
  [pscustomobject][ordered]@{
    schemaVersion = 4
    runs = [pscustomobject][ordered]@{
      codex = $null
      deepseek = $null
    }
    integrationLease = $null
  }
}

function New-Result {
  param(
    [Parameter(Mandatory = $true)][string]$Status,
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
    [Parameter(Mandatory = $true)][object]$Result,
    [int]$ExitCode = 0
  )

  [Console]::Out.WriteLine(($Result | ConvertTo-Json -Compress -Depth 40))
  exit $ExitCode
}

function Assert-StableText {
  param(
    [AllowNull()][string]$Value,
    [Parameter(Mandatory = $true)][string]$ParameterName,
    [int]$MaximumLength = 512,
    [switch]$AllowEmpty
  )

  if (-not $AllowEmpty -and [string]::IsNullOrWhiteSpace($Value)) {
    throw [ArgumentException]::new("$ParameterName is required")
  }
  if ($null -ne $Value -and ($Value.Length -gt $MaximumLength -or $Value -match '[\x00-\x1F\x7F]')) {
    throw [ArgumentException]::new("$ParameterName is invalid")
  }
}

function Assert-GitSha {
  param(
    [AllowNull()][string]$Value,
    [Parameter(Mandatory = $true)][string]$ParameterName,
    [switch]$AllowNull
  )

  if ($AllowNull -and [string]::IsNullOrWhiteSpace($Value)) {
    return
  }
  if ([string]::IsNullOrWhiteSpace($Value) -or $Value -cnotmatch '\A[0-9a-f]{40,64}\z') {
    throw [ArgumentException]::new("$ParameterName must be a full lowercase Git object id")
  }
}

function Assert-Sha256 {
  param([AllowNull()][string]$Value, [string]$ParameterName)
  if ([string]::IsNullOrWhiteSpace($Value) -or $Value -cnotmatch '\A[0-9a-f]{64}\z') {
    throw [ArgumentException]::new("$ParameterName must be a lowercase SHA-256 digest")
  }
}

function Test-PathWithinRoot {
  param([string]$Path, [string]$Root)

  $fullPath = [IO.Path]::GetFullPath($Path)
  $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
  )
  if ($fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
    return $true
  }
  $fullPath.StartsWith(
    $fullRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase
  )
}

function Get-ApprovedPrivateRoot {
  [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state'))
}

function Resolve-StateRoot {
  param([string]$Path)

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

function Resolve-RepositoryRoot {
  param([string]$Path)

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
  $gitMarker = Join-Path $fullPath '.git'
  $insideWorkTree = @(& git -C $fullPath rev-parse --is-inside-work-tree 2>$null)
  if (
    -not (Test-Path -LiteralPath $gitMarker) -or
    $LASTEXITCODE -ne 0 -or
    $insideWorkTree.Count -ne 1 -or
    [string]$insideWorkTree[0] -cne 'true'
  ) {
    throw [ArgumentException]::new('RepositoryRoot must be an existing Git root')
  }
  $fullPath
}

function Assert-PropertySet {
  param([object]$Object, [string[]]$Expected, [string]$Context)

  $actual = @($Object.PSObject.Properties.Name | Sort-Object)
  $wanted = @($Expected | Sort-Object)
  if (($actual -join ',') -cne ($wanted -join ',')) {
    throw [IO.InvalidDataException]::new("Unexpected fields in $Context")
  }
}

function Assert-RepositoryRelativePath {
  param([string]$Value, [string]$Context)

  if (
    [string]::IsNullOrWhiteSpace($Value) -or
    $Value -cne $Value.Trim() -or
    $Value.Contains('\') -or
    [IO.Path]::IsPathFullyQualified($Value) -or
    $Value -match '[\x00-\x1F\x7F|*?\[\]]' -or
    @($Value.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0
  ) {
    throw [IO.InvalidDataException]::new("Invalid repository-relative path in $Context")
  }
}

function Assert-StringArray {
  param([AllowNull()][object]$Value, [string]$Context, [switch]$Paths)

  if ($Value -is [string] -or $Value -isnot [Collections.IEnumerable]) {
    throw [IO.InvalidDataException]::new("$Context must be an array")
  }
  foreach ($item in @($Value)) {
    if ($item -isnot [string]) {
      throw [IO.InvalidDataException]::new("$Context must contain strings")
    }
    if ($Paths) {
      Assert-RepositoryRelativePath -Value ([string]$item) -Context $Context
    } else {
      Assert-StableText -Value ([string]$item) -ParameterName $Context -MaximumLength 2000
    }
  }
}

function Assert-CandidateResult {
  param([object]$CandidateResult)

  Assert-PropertySet -Object $CandidateResult -Expected @(
    'category',
    'expectedTransition',
    'changedPaths',
    'verified',
    'unverified',
    'residualRisk',
    'result',
    'impact',
    'verify',
    'plain'
  ) -Context 'candidateResult'
  if ([string]$CandidateResult.category -cne 'completed') {
    throw [IO.InvalidDataException]::new('candidateResult category must be completed')
  }
  Assert-StableText -Value ([string]$CandidateResult.expectedTransition) -ParameterName 'candidateResult.expectedTransition'
  Assert-StringArray -Value $CandidateResult.changedPaths -Context 'candidateResult.changedPaths' -Paths
  if (@($CandidateResult.changedPaths).Count -eq 0) {
    throw [IO.InvalidDataException]::new('candidateResult.changedPaths must not be empty')
  }
  Assert-StringArray -Value $CandidateResult.verified -Context 'candidateResult.verified'
  Assert-StringArray -Value $CandidateResult.unverified -Context 'candidateResult.unverified'
  foreach ($field in @('residualRisk', 'result', 'impact', 'verify', 'plain')) {
    Assert-StableText -Value ([string]$CandidateResult.$field) -ParameterName "candidateResult.$field" -MaximumLength 2000
  }
}

function Assert-Run {
  param([object]$Run, [string]$ExpectedOwner)

  Assert-PropertySet -Object $Run -Expected @(
    'runId', 'taskId', 'route', 'owner', 'mainBranch', 'baseCommit', 'taskCardDigest',
    'worktree', 'candidateBranch', 'canonicalBranch', 'candidateCommit', 'canonicalBase',
    'canonicalHead', 'candidateResult', 'sessionKind', 'sessionId', 'state', 'startedAt',
    'updatedAt', 'recoveryReason'
  ) -Context "$ExpectedOwner run"
  if ([string]$Run.owner -cne $ExpectedOwner) {
    throw [IO.InvalidDataException]::new("Run owner does not match $ExpectedOwner slot")
  }
  if ([string]$Run.state -cnotin @('developing', 'candidate_ready', 'canonical_ready', 'integrated', 'attention_required')) {
    throw [IO.InvalidDataException]::new('Run state is invalid')
  }
  Assert-StableText -Value ([string]$Run.runId) -ParameterName 'run.runId'
  Assert-StableText -Value ([string]$Run.taskId) -ParameterName 'run.taskId'
  Assert-GitSha -Value ([string]$Run.baseCommit) -ParameterName 'run.baseCommit'
  Assert-Sha256 -Value ([string]$Run.taskCardDigest) -ParameterName 'run.taskCardDigest'
  if ($null -ne $Run.candidateResult) {
    Assert-CandidateResult -CandidateResult $Run.candidateResult
  }
}

function Assert-RuntimeState {
  param([object]$State)

  Assert-PropertySet -Object $State -Expected @('schemaVersion', 'runs', 'integrationLease') -Context 'runtime state'
  if ($State.schemaVersion -ne 4) {
    throw [IO.InvalidDataException]::new('Unsupported runtime state schema')
  }
  Assert-PropertySet -Object $State.runs -Expected @('codex', 'deepseek') -Context 'runtime runs'
  $taskIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($slot in @('codex', 'deepseek')) {
    $run = $State.runs.$slot
    if ($null -eq $run) {
      continue
    }
    Assert-Run -Run $run -ExpectedOwner $slot
    if (-not $taskIds.Add([string]$run.taskId)) {
      throw [IO.InvalidDataException]::new('The same taskId appears in more than one run')
    }
  }
  if ($null -ne $State.integrationLease) {
    Assert-PropertySet -Object $State.integrationLease -Expected @(
      'runId', 'owner', 'taskId', 'mainHead', 'acquiredAt', 'expiresAt'
    ) -Context 'integrationLease'
    $leaseOwner = [string]$State.integrationLease.owner
    if ($leaseOwner -cnotin @('codex', 'deepseek')) {
      throw [IO.InvalidDataException]::new('Integration lease owner is invalid')
    }
    $ownerRun = $State.runs.$leaseOwner
    if (
      $null -eq $ownerRun -or
      [string]$ownerRun.runId -cne [string]$State.integrationLease.runId -or
      [string]$ownerRun.taskId -cne [string]$State.integrationLease.taskId
    ) {
      throw [IO.InvalidDataException]::new('Integration lease does not match its owner run')
    }
  }
}

function New-MigrationRequiredException {
  $exception = [InvalidOperationException]::new('Legacy runtime must be quiescent before schema migration')
  $exception.Data['RuntimeStatus'] = 'MIGRATION_REQUIRED'
  $exception
}

function Convert-RuntimeStateSchema {
  param([object]$State)

  if ($State.schemaVersion -eq 4) {
    return $State
  }
  if ($State.schemaVersion -cnotin @(1, 2, 3)) {
    throw [IO.InvalidDataException]::new('Unsupported runtime state schema')
  }
  $legacyLease = $State.PSObject.Properties.Name -contains 'lease' -and $null -ne $State.lease
  $legacyRecovery = $State.PSObject.Properties.Name -contains 'recovery' -and $null -ne $State.recovery
  $legacyPaused =
    $State.PSObject.Properties.Name -contains 'blocking' -and
    $null -ne $State.blocking -and
    [bool]$State.blocking.pauseRequested
  if ($legacyLease -or $legacyRecovery -or $legacyPaused) {
    throw (New-MigrationRequiredException)
  }
  New-RuntimeState
}

function Initialize-StateRoot {
  param([string]$Path)

  [IO.Directory]::CreateDirectory($Path) | Out-Null
  Set-PrivatePathAcl -Path $Path -Directory
  Assert-PrivatePathAcl -Path $Path -Directory
}

function Read-RuntimeState {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return New-RuntimeState
  }
  Assert-PrivatePathAcl -Path $Path
  try {
    $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path))
    $state = $text | ConvertFrom-Json -Depth 100
  } catch {
    throw [IO.InvalidDataException]::new('Runtime state is not valid UTF-8 JSON', $_.Exception)
  }
  $state = Convert-RuntimeStateSchema -State $state
  Assert-RuntimeState -State $state
  $state
}

function Write-RuntimeState {
  param([string]$Path, [object]$State)

  Assert-RuntimeState -State $State
  $json = $State | ConvertTo-Json -Compress -Depth 40
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
  param([string]$Path)

  $bytes = [Text.Encoding]::UTF8.GetBytes($Path.ToUpperInvariant())
  $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
  "Local\TZGHourlyAutomationLease-$hash"
}

function Test-IntegrationLeaseExpired {
  param([AllowNull()][object]$Lease, [DateTimeOffset]$Now)

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
    throw [IO.InvalidDataException]::new('Integration lease expiry is invalid')
  }
  $expiresAt -le $Now
}

function Get-OwnerRun {
  param([object]$State, [string]$ExpectedOwner, [string]$ExpectedRunId)

  $run = $State.runs.$ExpectedOwner
  if ($null -eq $run -or [string]$run.runId -cne $ExpectedRunId) {
    return $null
  }
  $run
}

function Read-CandidateResult {
  param([string]$Path, [string]$NormalizedStateRoot)

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    throw [ArgumentException]::new('CandidateResultPath must be absolute')
  }
  $fullPath = [IO.Path]::GetFullPath($Path)
  if (-not (Test-PathWithinRoot -Path $fullPath -Root $NormalizedStateRoot)) {
    throw [ArgumentException]::new('CandidateResultPath must be inside StateRoot')
  }
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    throw [ArgumentException]::new('CandidateResultPath must reference an existing file')
  }
  Assert-PrivatePathAcl -Path $fullPath
  $bytes = [IO.File]::ReadAllBytes($fullPath)
  if ($bytes.Length -gt 65536) {
    throw [ArgumentException]::new('CandidateResultPath exceeds 65536 bytes')
  }
  try {
    $candidateResult = [Text.UTF8Encoding]::new($false, $true).GetString($bytes) | ConvertFrom-Json -Depth 50
  } catch {
    throw [ArgumentException]::new('CandidateResultPath must contain valid UTF-8 JSON')
  }
  Assert-CandidateResult -CandidateResult $candidateResult
  $candidateResult
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
        $leaseStatus = if ($null -eq $state.integrationLease) {
          'none'
        } elseif (Test-IntegrationLeaseExpired -Lease $state.integrationLease -Now $now) {
          'expired'
        } else {
          'active'
        }
        $activeTaskIds = @(
          @($state.runs.codex, $state.runs.deepseek) |
            Where-Object { $null -ne $_ } |
            ForEach-Object { [string]$_.taskId }
        )
        $result = New-Result -Status 'OK' -Values @{
          state = $state
          activeTaskIds = $activeTaskIds
          integrationLeaseStatus = $leaseStatus
        }
      }

      'ClaimRun' {
        Assert-StableText -Value $TaskId -ParameterName 'TaskId'
        Assert-StableText -Value $Owner -ParameterName 'Owner'
        Assert-StableText -Value $Route -ParameterName 'Route'
        if (
          ($Owner -ceq 'codex' -and $Route -cnotin @('codex_execute', 'codex_review', 'queue_maintenance')) -or
          ($Owner -ceq 'deepseek' -and $Route -cne 'external_execute')
        ) {
          throw [ArgumentException]::new('Route does not match Owner')
        }
        if ($MainBranch -cne 'master') {
          throw [ArgumentException]::new('MainBranch must be master')
        }
        $resolvedRepositoryRoot = Resolve-RepositoryRoot -Path $RepositoryRoot
        Assert-GitSha -Value $BaseCommit -ParameterName 'BaseCommit'
        Assert-Sha256 -Value $TaskCardDigest -ParameterName 'TaskCardDigest'
        if ($null -ne $state.runs.$Owner) {
          $existingRun = $state.runs.$Owner
          $result = New-Result -Status 'OWNER_BUSY' -Values @{
            runId = $existingRun.runId
            taskId = $existingRun.taskId
            owner = $existingRun.owner
            state = $existingRun.state
          }
          break
        }
        $duplicateRun = @($state.runs.codex, $state.runs.deepseek) | Where-Object {
          $null -ne $_ -and [string]$_.taskId -ceq $TaskId
        } | Select-Object -First 1
        if ($null -ne $duplicateRun) {
          $result = New-Result -Status 'TASK_BUSY' -Values @{
            runId = $duplicateRun.runId
            taskId = $duplicateRun.taskId
            owner = $duplicateRun.owner
            state = $duplicateRun.state
          }
          break
        }
        $newRunId = [Guid]::NewGuid().ToString()
        $worktree = Join-Path $resolvedRepositoryRoot ".worktrees\automation\$newRunId\$Owner"
        $candidateBranch = "codex/automation/$Owner/$newRunId/candidate"
        $run = [pscustomobject][ordered]@{
          runId = $newRunId
          taskId = $TaskId
          route = $Route
          owner = $Owner
          mainBranch = $MainBranch
          baseCommit = $BaseCommit
          taskCardDigest = $TaskCardDigest
          worktree = $worktree
          candidateBranch = $candidateBranch
          canonicalBranch = $null
          candidateCommit = $null
          canonicalBase = $null
          canonicalHead = $null
          candidateResult = $null
          sessionKind = $null
          sessionId = $null
          state = 'developing'
          startedAt = $now.ToString('o')
          updatedAt = $now.ToString('o')
          recoveryReason = $null
        }
        $state.runs.$Owner = $run
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'CLAIMED' -Values @{ run = $run }
      }

      'UpdateRun' {
        Assert-StableText -Value $Owner -ParameterName 'Owner'
        Assert-StableText -Value $RunId -ParameterName 'RunId'
        Assert-StableText -Value $RunState -ParameterName 'RunState'
        $run = Get-OwnerRun -State $state -ExpectedOwner $Owner -ExpectedRunId $RunId
        if ($null -eq $run) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        $currentState = [string]$run.state
        $attentionCandidateRecovery =
          $Owner -ceq 'deepseek' -and
          $currentState -ceq 'attention_required' -and
          $RunState -ceq 'candidate_ready' -and
          -not [string]::IsNullOrWhiteSpace($ExpectedRecoveryReason) -and
          $ExpectedRecoveryReason -ceq [string]$run.recoveryReason -and
          $null -eq $run.candidateCommit -and
          $null -eq $run.candidateResult -and
          $null -eq $run.canonicalBranch -and
          $null -eq $run.canonicalBase -and
          $null -eq $run.canonicalHead -and
          $null -eq $state.integrationLease
        $allowed = switch ($currentState) {
          'developing' { @('developing', 'candidate_ready', 'attention_required') }
          'candidate_ready' { @('canonical_ready', 'attention_required') }
          'canonical_ready' { @('candidate_ready', 'integrated', 'attention_required') }
          'integrated' { @() }
          'attention_required' { @() }
        }
        if ($RunState -cnotin $allowed -and -not $attentionCandidateRecovery) {
          throw [ArgumentException]::new("Invalid run state transition: $currentState -> $RunState")
        }
        if (-not [string]::IsNullOrWhiteSpace($SessionKind)) {
          $expectedSessionKind = if ($Owner -ceq 'codex') { 'codex_cli' } else { 'claude_cli' }
          if ($SessionKind -cne $expectedSessionKind) {
            throw [ArgumentException]::new('SessionKind does not match Owner')
          }
          $run.sessionKind = $SessionKind
        }
        if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
          Assert-StableText -Value $SessionId -ParameterName 'SessionId'
          if ([string]::IsNullOrWhiteSpace([string]$run.sessionKind)) {
            throw [ArgumentException]::new('SessionKind is required with SessionId')
          }
          $run.sessionId = $SessionId
        }
        if ($RunState -ceq 'candidate_ready' -and ($currentState -ceq 'developing' -or $attentionCandidateRecovery)) {
          Assert-GitSha -Value $CandidateCommit -ParameterName 'CandidateCommit'
          $run.candidateCommit = $CandidateCommit
          $run.candidateResult = Read-CandidateResult -Path $CandidateResultPath -NormalizedStateRoot $normalizedStateRoot
          if ($attentionCandidateRecovery) {
            $run.recoveryReason = $null
          }
        }
        if ($RunState -ceq 'candidate_ready' -and $currentState -ceq 'canonical_ready') {
          $run.canonicalBranch = $null
          $run.canonicalBase = $null
          $run.canonicalHead = $null
        }
        if ($RunState -ceq 'canonical_ready') {
          Assert-StableText -Value $CanonicalBranch -ParameterName 'CanonicalBranch'
          Assert-GitSha -Value $CanonicalBase -ParameterName 'CanonicalBase'
          Assert-GitSha -Value $CanonicalHead -ParameterName 'CanonicalHead'
          $run.canonicalBranch = $CanonicalBranch
          $run.canonicalBase = $CanonicalBase
          $run.canonicalHead = $CanonicalHead
        }
        if ($RunState -ceq 'integrated') {
          Assert-GitSha -Value $CanonicalHead -ParameterName 'CanonicalHead'
          if ($CanonicalHead -cne [string]$run.canonicalHead) { throw [ArgumentException]::new('CanonicalHead does not match the run evidence') }
        }
        if ($RunState -ceq 'attention_required') {
          Assert-StableText -Value $RecoveryReason -ParameterName 'RecoveryReason' -MaximumLength 1000
          $run.recoveryReason = $RecoveryReason
        }
        $run.state = $RunState
        $run.updatedAt = $now.ToString('o')
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'UPDATED' -Values @{ run = $run }
      }

      'AcquireIntegration' {
        Assert-StableText -Value $Owner -ParameterName 'Owner'
        Assert-StableText -Value $RunId -ParameterName 'RunId'
        Assert-GitSha -Value $ExpectedMainHead -ParameterName 'ExpectedMainHead'
        $run = Get-OwnerRun -State $state -ExpectedOwner $Owner -ExpectedRunId $RunId
        if ($null -eq $run) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        if ([string]$run.state -cne 'canonical_ready' -or [string]$run.canonicalBase -cne $ExpectedMainHead) {
          $result = New-Result -Status 'RUN_NOT_CANONICAL_READY'
          $resultExitCode = 2
          break
        }
        $leaseExpired = Test-IntegrationLeaseExpired -Lease $state.integrationLease -Now $now
        if ($null -ne $state.integrationLease -and -not $leaseExpired) {
          if ([string]$state.integrationLease.runId -ceq $RunId) {
            $result = New-Result -Status 'ALREADY_ACQUIRED' -Values @{ integrationLease = $state.integrationLease }
          } else {
            $result = New-Result -Status 'INTEGRATION_BUSY' -Values @{ integrationLease = $state.integrationLease }
          }
          break
        }
        $state.integrationLease = [pscustomobject][ordered]@{
          runId = $RunId
          owner = $Owner
          taskId = $run.taskId
          mainHead = $ExpectedMainHead
          acquiredAt = $now.ToString('o')
          expiresAt = $now.AddSeconds($IntegrationLeaseSeconds).ToString('o')
        }
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'INTEGRATION_ACQUIRED' -Values @{ integrationLease = $state.integrationLease }
      }

      'ReleaseIntegration' {
        Assert-StableText -Value $RunId -ParameterName 'RunId'
        if ($null -eq $state.integrationLease -or [string]$state.integrationLease.runId -cne $RunId) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        $state.integrationLease = $null
        Write-RuntimeState -Path $statePath -State $state
        $result = New-Result -Status 'INTEGRATION_RELEASED'
      }

      'CompleteRun' {
        Assert-StableText -Value $Owner -ParameterName 'Owner'
        Assert-StableText -Value $RunId -ParameterName 'RunId'
        Assert-StableText -Value $CompletionCategory -ParameterName 'CompletionCategory'
        Assert-StableText -Value $DetailCode -ParameterName 'DetailCode'
        $run = Get-OwnerRun -State $state -ExpectedOwner $Owner -ExpectedRunId $RunId
        if ($null -eq $run) {
          $result = New-Result -Status 'RUN_ID_MISMATCH'
          $resultExitCode = 2
          break
        }
        $attentionCompletion =
          $CompletionCategory -ceq 'failed' -and
          [string]$run.state -ceq 'attention_required' -and
          -not [string]::IsNullOrWhiteSpace($ExpectedRecoveryReason) -and
          $ExpectedRecoveryReason -ceq [string]$run.recoveryReason -and
          $null -eq $run.candidateCommit -and
          $null -eq $run.candidateResult -and
          $null -eq $run.canonicalBranch -and
          $null -eq $run.canonicalBase -and
          $null -eq $run.canonicalHead -and
          $null -eq $state.integrationLease
        $validCompletion =
          ($CompletionCategory -ceq 'success' -and [string]$run.state -ceq 'integrated') -or
          ($CompletionCategory -cin @('no_candidate', 'failed') -and [string]$run.state -ceq 'developing' -and $null -eq $run.candidateCommit) -or
          $attentionCompletion
        if (-not $validCompletion) {
          $result = New-Result -Status 'RUN_NOT_COMPLETABLE'
          $resultExitCode = 2
          break
        }
        if ($null -ne $state.integrationLease -and [string]$state.integrationLease.runId -ceq $RunId) {
          if ($CompletionCategory -cne 'success') {
            $result = New-Result -Status 'INTEGRATION_LEASE_PRESENT'
            $resultExitCode = 2
            break
          }
          $state.integrationLease = $null
        }
        $completedRun = $run
        $state.runs.$Owner = $null
        Write-RuntimeState -Path $statePath -State $state
        $completionValues = @{
          runId = $RunId
          taskId = $completedRun.taskId
          owner = $Owner
          category = $CompletionCategory
          detailCode = $DetailCode
        }
        if ($attentionCompletion) {
          $completionValues.recoveryReason = [string]$completedRun.recoveryReason
        }
        $result = New-Result -Status 'RUN_COMPLETED' -Values $completionValues
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
} catch [InvalidOperationException] {
  if ($_.Exception.Data.Contains('RuntimeStatus')) {
    $runtimeStatus = [string]$_.Exception.Data['RuntimeStatus']
    [Console]::Error.WriteLine("hourly-automation-lease: $runtimeStatus")
    $result = New-Result -Status $runtimeStatus
    $resultExitCode = 2
  } else {
    throw
  }
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
  [Console]::Error.WriteLine("hourly-automation-lease: FAILED type=$($_.Exception.GetType().FullName) line=$($_.InvocationInfo.ScriptLineNumber)")
  $result = New-Result -Status 'FAILED'
  $resultExitCode = 3
}

Write-ResultAndExit -Result $result -ExitCode $resultExitCode
