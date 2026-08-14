#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Show', 'ClaimRun', 'UpdateRun', 'CompleteRun')]
  [string]$Action,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [ValidateSet('codex', 'deepseek')][string]$Owner,
  [string]$TaskId,
  [ValidateSet('codex_execute', 'codex_review', 'queue_maintenance', 'external_execute')][string]$Route,
  [string]$RepositoryRoot,
  [string]$MainBranch = 'master',
  [string]$BaseCommit,
  [string]$TaskCardDigest,
  [string]$RunId,
  [ValidateSet('developing', 'candidate_ready', 'canonical_ready', 'integrated', 'attention_required')][string]$RunState,
  [ValidateSet('codex_cli', 'claude_cli')][string]$SessionKind,
  [string]$SessionId,
  [string]$CandidateCommit,
  [string]$CandidateResultPath,
  [string]$CanonicalBranch,
  [string]$CanonicalBase,
  [string]$CanonicalHead,
  [string]$RecoveryReason,
  [string]$ExpectedRecoveryReason,
  [string]$ExpectedCandidateCommit,
  [string]$ExpectedWorktree,
  [string]$ExpectedWorktreeBranch,
  [string]$ExpectedWorktreeHead,
  [ValidateSet('success', 'no_candidate', 'failed', 'paused')][string]$CompletionCategory,
  [string]$DetailCode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'private-path-acl.ps1')
. (Join-Path $PSScriptRoot 'hourly-integration-lock.ps1')

function New-RuntimeState {
  [pscustomobject][ordered]@{
    schemaVersion = 5
    runs = [pscustomobject][ordered]@{ codex = $null; deepseek = $null }
  }
}

function New-Result {
  param([string]$Status, [hashtable]$Values = @{})
  $result = [ordered]@{ status = $Status }
  foreach ($entry in $Values.GetEnumerator()) { $result[$entry.Key] = $entry.Value }
  [pscustomobject]$result
}

function Write-ResultAndExit {
  param([object]$Result, [int]$ExitCode = 0)
  [Console]::Out.WriteLine(($Result | ConvertTo-Json -Compress -Depth 50))
  exit $ExitCode
}

function Assert-StableText {
  param([AllowNull()][string]$Value, [string]$Name, [int]$MaximumLength = 512, [switch]$AllowEmpty)
  if (-not $AllowEmpty -and [string]::IsNullOrWhiteSpace($Value)) { throw [ArgumentException]::new("$Name is required") }
  if ($null -ne $Value -and ($Value.Length -gt $MaximumLength -or $Value -match '[\x00-\x1F\x7F]')) { throw [ArgumentException]::new("$Name is invalid") }
}

function Assert-GitSha {
  param([AllowNull()][string]$Value, [string]$Name, [switch]$AllowNull)
  if ($AllowNull -and [string]::IsNullOrWhiteSpace($Value)) { return }
  if ($Value -cnotmatch '^[0-9a-f]{40,64}$') { throw [ArgumentException]::new("$Name is invalid") }
}

function Assert-Sha256 {
  param([AllowNull()][string]$Value, [string]$Name)
  if ($Value -cnotmatch '^[0-9a-f]{64}$') { throw [ArgumentException]::new("$Name is invalid") }
}

function Assert-PropertySet {
  param([object]$Value, [string[]]$Expected, [string]$Context)
  $actual = @($Value.PSObject.Properties.Name | Sort-Object)
  $wanted = @($Expected | Sort-Object)
  if (($actual -join ',') -cne ($wanted -join ',')) { throw [IO.InvalidDataException]::new("Unexpected fields in $Context") }
}

function Test-PathWithinRoot {
  param([string]$Path, [string]$Root)
  $full = [IO.Path]::GetFullPath($Path)
  $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
  $full.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-StateRoot {
  param([string]$Path)
  if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw [ArgumentException]::new('StateRoot must be absolute') }
  $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
  $approved = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
  if (-not (Test-PathWithinRoot -Path $full -Root $approved)) { throw [ArgumentException]::new('StateRoot is outside the approved private root') }
  $full
}

function Resolve-RepositoryRoot {
  param([string]$Path)
  if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw [ArgumentException]::new('RepositoryRoot must be absolute') }
  $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath (Join-Path $full '.git'))) { throw [ArgumentException]::new('RepositoryRoot must be a Git root') }
  $inside = @(& git -C $full rev-parse --is-inside-work-tree 2>$null)
  if ($LASTEXITCODE -ne 0 -or $inside.Count -ne 1 -or [string]$inside[0] -cne 'true') { throw [ArgumentException]::new('RepositoryRoot must be a Git root') }
  $full
}

function Normalize-FullPath {
  param([string]$Path)
  [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-AbandonedAttentionEvidence {
  param(
    [object]$Run,
    [string]$CandidateCommit,
    [string]$Worktree,
    [string]$WorktreeBranch,
    [string]$WorktreeHead
  )
  try {
    $repository = Resolve-RepositoryRoot -Path ([string]$Run.repositoryRoot)
    $recordedWorktree = Normalize-FullPath ([string]$Run.worktree)
    $providedWorktree = Normalize-FullPath $Worktree
    $ownedWorktree = Normalize-FullPath (Join-Path $repository ".worktrees\automation\$([string]$Run.runId)\$([string]$Run.owner)")
    if ($providedWorktree -cne $recordedWorktree -or $providedWorktree -cne $ownedWorktree) { return $false }
    if ([string]$Run.candidateCommit -cne $CandidateCommit -or $null -eq $Run.candidateResult) { return $false }
    if ($null -ne $Run.canonicalBranch -or $null -ne $Run.canonicalBase -or $null -ne $Run.canonicalHead) { return $false }
    if (-not $WorktreeBranch.StartsWith("codex/automation/$([string]$Run.owner)/$([string]$Run.runId)/", [StringComparison]::Ordinal)) { return $false }
    if (-not (Test-Path -LiteralPath $providedWorktree -PathType Container)) { return $false }

    $registered = $false
    $worktreeList = @(& git -C $repository worktree list --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0) { return $false }
    foreach ($line in $worktreeList) {
      if ($line.StartsWith('worktree ', [StringComparison]::Ordinal) -and (Normalize-FullPath $line.Substring(9)) -ceq $providedWorktree) { $registered = $true; break }
    }
    if (-not $registered) { return $false }

    $topLevel = @(& git -C $providedWorktree rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or $topLevel.Count -ne 1 -or (Normalize-FullPath ([string]$topLevel[0])) -cne $providedWorktree) { return $false }
    $branch = @(& git -C $providedWorktree branch --show-current 2>$null)
    if ($LASTEXITCODE -ne 0 -or $branch.Count -ne 1 -or [string]$branch[0] -cne $WorktreeBranch) { return $false }
    $head = @(& git -C $providedWorktree rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1 -or [string]$head[0] -cne $WorktreeHead) { return $false }
    $candidateHead = @(& git -C $repository rev-parse "refs/heads/$([string]$Run.candidateBranch)" 2>$null)
    if ($LASTEXITCODE -ne 0 -or $candidateHead.Count -ne 1 -or [string]$candidateHead[0] -cne $CandidateCommit) { return $false }
    $status = @(& git -C $providedWorktree status --porcelain=v1 --untracked-files=all 2>$null)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) { return $false }
    $true
  } catch { $false }
}

function Get-StateMutexName {
  param([string]$Root)
  $identity = [IO.Path]::GetFullPath($Root).ToUpperInvariant()
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($identity))).ToLowerInvariant()
  "Local\TZG-Hourly-State-$digest"
}

function Write-StateAtomic {
  param([string]$Path, [object]$State)
  $temporary = Join-Path (Split-Path -Parent $Path) ".runtime.$([Guid]::NewGuid().ToString('N')).tmp"
  try {
    [IO.File]::WriteAllText($temporary, ($State | ConvertTo-Json -Compress -Depth 50), [Text.UTF8Encoding]::new($false))
    Set-PrivatePathAcl -Path $temporary
    Move-Item -LiteralPath $temporary -Destination $Path -Force
    Set-PrivatePathAcl -Path $Path
    Assert-PrivatePathAcl -Path $Path
  } finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
  }
}

function Read-CandidateResult {
  param([string]$Path)
  if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-PathWithinRoot -Path $Path -Root $script:resolvedStateRoot)) { throw [ArgumentException]::new('CandidateResultPath is invalid') }
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw [ArgumentException]::new('CandidateResultPath is unavailable') }
  Assert-PrivatePathAcl -Path $Path
  try { $result = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50 } catch { throw [IO.InvalidDataException]::new('Candidate result is invalid') }
  $baseProperties = @('category', 'expectedTransition', 'changedPaths', 'verified', 'unverified', 'residualRisk', 'result', 'impact', 'verify', 'plain')
  $expectedProperties = switch ([string]$result.category) {
    'completed' { if ($result.PSObject.Properties.Name -contains 'maintenanceResolution') { $baseProperties + 'maintenanceResolution' } else { $baseProperties } }
    'maintenance_decision' { $baseProperties + @('decisionTaskId', 'question', 'options', 'recommendedOption', 'impactSummary', 'plainSummary') }
    default { throw [IO.InvalidDataException]::new('Candidate result category is invalid') }
  }
  Assert-PropertySet -Value $result -Expected $expectedProperties -Context 'candidate result'
  foreach ($name in @('expectedTransition', 'residualRisk', 'result', 'impact', 'verify', 'plain')) { Assert-StableText -Value ([string]$result.$name) -Name "candidateResult.$name" -MaximumLength 2000 }
  foreach ($name in @('changedPaths', 'verified', 'unverified')) {
    if ($result.$name -is [string] -or $result.$name -isnot [Collections.IEnumerable]) { throw [IO.InvalidDataException]::new("candidateResult.$name is invalid") }
    foreach ($item in @($result.$name)) { Assert-StableText -Value ([string]$item) -Name "candidateResult.$name" -MaximumLength 2000 }
  }
  if ([string]$result.category -ceq 'maintenance_decision') {
    foreach ($name in @('decisionTaskId', 'question', 'recommendedOption', 'impactSummary')) { Assert-StableText -Value ([string]$result.$name) -Name "candidateResult.$name" -MaximumLength 2000 }
    $options = @($result.options)
    if ($options.Count -ne 3 -or (@($options | ForEach-Object { [string]$_.key }) -join '') -cne 'ABC' -or (@($options | ForEach-Object { [string]$_.targetState }) -join ',') -cne 'ready,ready,blocked') { throw [IO.InvalidDataException]::new('Maintenance decision options are invalid') }
  }
  if ($result.PSObject.Properties.Name -contains 'maintenanceResolution') {
    $resume = $result.maintenanceResolution
    Assert-PropertySet -Value $resume -Expected @('schemaVersion', 'kind', 'taskId', 'decisionTaskId', 'decisionId', 'replyKind', 'replyValue', 'source', 'evidenceHash', 'pendingTaskDigest') -Context 'maintenance resolution'
    if ([int]$resume.schemaVersion -ne 1 -or [string]$resume.kind -cne 'queue_maintenance' -or [string]$resume.taskId -cne 'QUEUE-MAINTENANCE' -or [string]$resume.replyValue -cnotin @('A', 'B') -or [string]$resume.source -cne 'feishu_card') { throw [IO.InvalidDataException]::new('Maintenance resolution is invalid') }
    Assert-Sha256 -Value ([string]$resume.evidenceHash) -Name 'maintenanceResolution.evidenceHash'
    Assert-Sha256 -Value ([string]$resume.pendingTaskDigest) -Name 'maintenanceResolution.pendingTaskDigest'
  }
  $result
}

function Assert-Run {
  param([object]$Run, [string]$ExpectedOwner)
  Assert-PropertySet -Value $Run -Expected @(
    'runId', 'taskId', 'route', 'owner', 'repositoryRoot', 'mainBranch', 'baseCommit', 'taskCardDigest',
    'worktree', 'candidateBranch', 'canonicalBranch', 'candidateCommit', 'canonicalBase', 'canonicalHead',
    'candidateResult', 'sessionKind', 'sessionId', 'state', 'startedAt', 'updatedAt', 'recoveryReason'
  ) -Context "$ExpectedOwner run"
  foreach ($name in @('runId', 'taskId', 'route', 'owner', 'repositoryRoot', 'mainBranch', 'worktree', 'candidateBranch', 'state', 'startedAt', 'updatedAt')) { Assert-StableText -Value ([string]$Run.$name) -Name "run.$name" -MaximumLength 2000 }
  if ([string]$Run.owner -cne $ExpectedOwner) { throw [IO.InvalidDataException]::new('Run owner is invalid') }
  $allowedRoute = if ($ExpectedOwner -ceq 'codex') { @('codex_execute', 'codex_review', 'queue_maintenance') } else { @('external_execute') }
  if ([string]$Run.route -cnotin $allowedRoute) { throw [IO.InvalidDataException]::new('Run route is invalid') }
  if ([string]$Run.state -cnotin @('developing', 'candidate_ready', 'canonical_ready', 'integrated', 'attention_required')) { throw [IO.InvalidDataException]::new('Run state is invalid') }
  Assert-GitSha -Value ([string]$Run.baseCommit) -Name 'run.baseCommit'
  Assert-Sha256 -Value ([string]$Run.taskCardDigest) -Name 'run.taskCardDigest'
  foreach ($name in @('candidateCommit', 'canonicalBase', 'canonicalHead')) { Assert-GitSha -Value ([string]$Run.$name) -Name "run.$name" -AllowNull }
  if ($null -ne $Run.candidateResult) {
    $baseProperties = @('category', 'expectedTransition', 'changedPaths', 'verified', 'unverified', 'residualRisk', 'result', 'impact', 'verify', 'plain')
    $expectedProperties = switch ([string]$Run.candidateResult.category) {
      'completed' { if ($Run.candidateResult.PSObject.Properties.Name -contains 'maintenanceResolution') { $baseProperties + 'maintenanceResolution' } else { $baseProperties } }
      'maintenance_decision' { $baseProperties + @('decisionTaskId', 'question', 'options', 'recommendedOption', 'impactSummary', 'plainSummary') }
      default { throw [IO.InvalidDataException]::new('Run candidate result category is invalid') }
    }
    Assert-PropertySet -Value $Run.candidateResult -Expected $expectedProperties -Context 'run.candidateResult'
  }
}

function Read-State {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [pscustomobject]@{ State = (New-RuntimeState); MigrationRequired = $false; Migrated = $true } }
  Assert-PrivatePathAcl -Path $Path
  try { $state = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50 } catch { throw [IO.InvalidDataException]::new('Runtime JSON is invalid') }
  if ([int]$state.schemaVersion -eq 4) {
    Assert-PropertySet -Value $state -Expected @('schemaVersion', 'runs', 'integrationLease') -Context 'schema 4 runtime'
    if ($null -ne $state.runs.codex -or $null -ne $state.runs.deepseek -or $null -ne $state.integrationLease) {
      return [pscustomobject]@{ State = $state; MigrationRequired = $true; Migrated = $false }
    }
    return [pscustomobject]@{ State = (New-RuntimeState); MigrationRequired = $false; Migrated = $true }
  }
  if ([int]$state.schemaVersion -ne 5) { throw [IO.InvalidDataException]::new('Unsupported runtime schema') }
  Assert-PropertySet -Value $state -Expected @('schemaVersion', 'runs') -Context 'schema 5 runtime'
  Assert-PropertySet -Value $state.runs -Expected @('codex', 'deepseek') -Context 'runtime runs'
  foreach ($name in @('codex', 'deepseek')) { if ($null -ne $state.runs.$name) { Assert-Run -Run $state.runs.$name -ExpectedOwner $name } }
  [pscustomobject]@{ State = $state; MigrationRequired = $false; Migrated = $false }
}

function Get-OwnerRun {
  param([object]$State, [string]$ExpectedOwner, [string]$ExpectedRunId)
  $run = $State.runs.$ExpectedOwner
  if ($null -eq $run -or [string]$run.runId -cne $ExpectedRunId) { return $null }
  $run
}

$script:resolvedStateRoot = $null
$mutex = $null
$held = $false
try {
  $script:resolvedStateRoot = Resolve-StateRoot -Path $StateRoot
  [IO.Directory]::CreateDirectory($script:resolvedStateRoot) | Out-Null
  Set-PrivatePathAcl -Path $script:resolvedStateRoot -Directory
  Assert-PrivatePathAcl -Path $script:resolvedStateRoot -Directory
  $statePath = Join-Path $script:resolvedStateRoot 'runtime.json'
  $mutex = [Threading.Mutex]::new($false, (Get-StateMutexName -Root $script:resolvedStateRoot))
  try { $held = $mutex.WaitOne([TimeSpan]::FromSeconds(30)) } catch [Threading.AbandonedMutexException] { $held = $true }
  if (-not $held) { Write-ResultAndExit -Result (New-Result -Status 'OCCUPIED') -ExitCode 2 }

  $read = Read-State -Path $statePath
  if ($read.MigrationRequired) { Write-ResultAndExit -Result (New-Result -Status 'MIGRATION_REQUIRED') -ExitCode 2 }
  $state = $read.State
  if ($read.Migrated) { Write-StateAtomic -Path $statePath -State $state }
  $now = [DateTimeOffset]::Now

  switch ($Action) {
    'Show' {
      $active = @($state.runs.codex, $state.runs.deepseek | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_.taskId })
      $lockRoot = $null
      if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        $lockRoot = Resolve-RepositoryRoot -Path $RepositoryRoot
      } else {
        $firstRun = @($state.runs.codex, $state.runs.deepseek | Where-Object { $null -ne $_ } | Select-Object -First 1)
        if ($firstRun.Count -eq 1) { $lockRoot = [string]$firstRun[0].repositoryRoot }
      }
      $lockStatus = if ($null -eq $lockRoot) { 'unknown' } else { Get-TzgIntegrationLockStatus -RepositoryRoot $lockRoot }
      Write-ResultAndExit -Result (New-Result -Status 'OK' -Values @{ state = $state; activeTaskIds = $active; integrationLockStatus = $lockStatus })
    }
    'ClaimRun' {
      foreach ($name in @('Owner', 'TaskId', 'Route', 'RepositoryRoot', 'MainBranch', 'BaseCommit', 'TaskCardDigest')) { Assert-StableText -Value ([string](Get-Variable -Name $name -ValueOnly)) -Name $name -MaximumLength 2000 }
      $root = Resolve-RepositoryRoot -Path $RepositoryRoot
      Assert-GitSha -Value $BaseCommit -Name 'BaseCommit'
      Assert-Sha256 -Value $TaskCardDigest -Name 'TaskCardDigest'
      $allowed = if ($Owner -ceq 'codex') { @('codex_execute', 'codex_review', 'queue_maintenance') } else { @('external_execute') }
      if ($Route -cnotin $allowed) { throw [ArgumentException]::new('Route does not belong to Owner') }
      if ($null -ne $state.runs.$Owner) { Write-ResultAndExit -Result (New-Result -Status 'OWNER_OCCUPIED' -Values @{ run = $state.runs.$Owner }) -ExitCode 2 }
      foreach ($other in @($state.runs.codex, $state.runs.deepseek)) { if ($null -ne $other -and [string]$other.taskId -ceq $TaskId) { Write-ResultAndExit -Result (New-Result -Status 'TASK_OCCUPIED' -Values @{ taskId = $TaskId }) -ExitCode 2 } }
      $newId = [Guid]::NewGuid().ToString()
      $worktree = [IO.Path]::GetFullPath((Join-Path $root ".worktrees\automation\$newId\$Owner"))
      $branch = "codex/automation/$Owner/$newId/candidate"
      $run = [pscustomobject][ordered]@{
        runId = $newId; taskId = $TaskId; route = $Route; owner = $Owner; repositoryRoot = $root; mainBranch = $MainBranch
        baseCommit = $BaseCommit; taskCardDigest = $TaskCardDigest; worktree = $worktree; candidateBranch = $branch
        canonicalBranch = $null; candidateCommit = $null; canonicalBase = $null; canonicalHead = $null; candidateResult = $null
        sessionKind = $null; sessionId = $null; state = 'developing'; startedAt = $now.ToString('o'); updatedAt = $now.ToString('o'); recoveryReason = $null
      }
      $state.runs.$Owner = $run
      Write-StateAtomic -Path $statePath -State $state
      Write-ResultAndExit -Result (New-Result -Status 'CLAIMED' -Values @{ run = $run })
    }
    'UpdateRun' {
      Assert-StableText -Value $Owner -Name 'Owner'
      Assert-StableText -Value $RunId -Name 'RunId'
      Assert-StableText -Value $RunState -Name 'RunState'
      $run = Get-OwnerRun -State $state -ExpectedOwner $Owner -ExpectedRunId $RunId
      if ($null -eq $run) { Write-ResultAndExit -Result (New-Result -Status 'RUN_ID_MISMATCH') -ExitCode 2 }
      $transitions = @{
        developing = @('candidate_ready', 'canonical_ready', 'attention_required')
        candidate_ready = @('canonical_ready', 'attention_required')
        canonical_ready = @('integrated', 'attention_required')
        integrated = @()
        attention_required = @()
      }
      if ($RunState -cnotin $transitions[[string]$run.state]) { throw [ArgumentException]::new('Run transition is invalid') }
      if ($RunState -ceq 'candidate_ready') {
        Assert-GitSha -Value $CandidateCommit -Name 'CandidateCommit'
        Assert-StableText -Value $CandidateResultPath -Name 'CandidateResultPath' -MaximumLength 2000
        Assert-StableText -Value $SessionKind -Name 'SessionKind'
        Assert-StableText -Value $SessionId -Name 'SessionId'
        $run.candidateCommit = $CandidateCommit
        $run.candidateResult = Read-CandidateResult -Path $CandidateResultPath
        $run.sessionKind = $SessionKind
        $run.sessionId = $SessionId
      }
      if ($RunState -ceq 'canonical_ready') {
        Assert-StableText -Value $CanonicalBranch -Name 'CanonicalBranch' -MaximumLength 500
        Assert-GitSha -Value $CanonicalBase -Name 'CanonicalBase'
        Assert-GitSha -Value $CanonicalHead -Name 'CanonicalHead'
        $run.canonicalBranch = $CanonicalBranch; $run.canonicalBase = $CanonicalBase; $run.canonicalHead = $CanonicalHead
      }
      if ($RunState -ceq 'integrated') {
        Assert-GitSha -Value $CanonicalHead -Name 'CanonicalHead'
        if ([string]$run.canonicalHead -cne $CanonicalHead) { throw [ArgumentException]::new('CanonicalHead mismatch') }
      }
      if ($RunState -ceq 'attention_required') { Assert-StableText -Value $RecoveryReason -Name 'RecoveryReason' -MaximumLength 2000; $run.recoveryReason = $RecoveryReason }
      $run.state = $RunState; $run.updatedAt = $now.ToString('o')
      Write-StateAtomic -Path $statePath -State $state
      Write-ResultAndExit -Result (New-Result -Status 'UPDATED' -Values @{ run = $run })
    }
    'CompleteRun' {
      foreach ($name in @('Owner', 'RunId', 'CompletionCategory', 'DetailCode')) { Assert-StableText -Value ([string](Get-Variable -Name $name -ValueOnly)) -Name $name -MaximumLength 2000 }
      $run = Get-OwnerRun -State $state -ExpectedOwner $Owner -ExpectedRunId $RunId
      if ($null -eq $run) { Write-ResultAndExit -Result (New-Result -Status 'RUN_ID_MISMATCH') -ExitCode 2 }
      $emptyAttentionClose = $CompletionCategory -ceq 'failed' -and [string]$run.state -ceq 'attention_required' -and
        -not [string]::IsNullOrWhiteSpace($ExpectedRecoveryReason) -and $ExpectedRecoveryReason -ceq [string]$run.recoveryReason -and
        $null -eq $run.candidateCommit -and $null -eq $run.candidateResult -and $null -eq $run.canonicalBranch -and $null -eq $run.canonicalBase -and $null -eq $run.canonicalHead
      $candidateAttentionClose = $false
      if ($CompletionCategory -ceq 'failed' -and [string]$run.state -ceq 'attention_required' -and
          -not [string]::IsNullOrWhiteSpace($ExpectedRecoveryReason) -and $ExpectedRecoveryReason -ceq [string]$run.recoveryReason -and
          -not [string]::IsNullOrWhiteSpace($ExpectedCandidateCommit) -and -not [string]::IsNullOrWhiteSpace($ExpectedWorktree) -and
          -not [string]::IsNullOrWhiteSpace($ExpectedWorktreeBranch) -and -not [string]::IsNullOrWhiteSpace($ExpectedWorktreeHead)) {
        Assert-GitSha -Value $ExpectedCandidateCommit -Name 'ExpectedCandidateCommit'
        Assert-StableText -Value $ExpectedWorktree -Name 'ExpectedWorktree' -MaximumLength 2000
        Assert-StableText -Value $ExpectedWorktreeBranch -Name 'ExpectedWorktreeBranch' -MaximumLength 500
        Assert-GitSha -Value $ExpectedWorktreeHead -Name 'ExpectedWorktreeHead'
        $candidateAttentionClose = Test-AbandonedAttentionEvidence -Run $run -CandidateCommit $ExpectedCandidateCommit -Worktree $ExpectedWorktree -WorktreeBranch $ExpectedWorktreeBranch -WorktreeHead $ExpectedWorktreeHead
      }
      $attentionClose = $emptyAttentionClose -or $candidateAttentionClose
      $valid = ($CompletionCategory -cin @('success', 'paused') -and [string]$run.state -ceq 'integrated') -or
        ($CompletionCategory -cin @('no_candidate', 'failed') -and [string]$run.state -ceq 'developing' -and $null -eq $run.candidateCommit) -or $attentionClose
      if (-not $valid) { Write-ResultAndExit -Result (New-Result -Status 'RUN_NOT_COMPLETABLE') -ExitCode 2 }
      $state.runs.$Owner = $null
      Write-StateAtomic -Path $statePath -State $state
      $values = @{ runId = $RunId; taskId = $run.taskId; owner = $Owner; category = $CompletionCategory; detailCode = $DetailCode }
      if ($attentionClose) { $values.recoveryReason = [string]$run.recoveryReason }
      if ($candidateAttentionClose) { $values.evidenceRetained = $true }
      Write-ResultAndExit -Result (New-Result -Status 'RUN_COMPLETED' -Values $values)
    }
  }
} catch [ArgumentException] {
  Write-ResultAndExit -Result (New-Result -Status 'INVALID_ARGUMENT') -ExitCode 2
} catch [IO.InvalidDataException] {
  Write-ResultAndExit -Result (New-Result -Status 'INVALID_STATE') -ExitCode 2
} catch {
  Write-ResultAndExit -Result (New-Result -Status 'FAILED') -ExitCode 1
} finally {
  if ($held) { $mutex.ReleaseMutex() }
  if ($null -ne $mutex) { $mutex.Dispose() }
}
