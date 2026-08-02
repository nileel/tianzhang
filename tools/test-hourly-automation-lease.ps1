#requires -Version 7.0

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'private-path-acl.ps1')

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -ne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" } }
function Normalize-FullPath { param([string]$Path) [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }

function Invoke-Runtime {
  param([string]$Action, [hashtable]$Parameters = @{}, [int[]]$Allowed = @(0))
  $arguments = @('-Action', $Action, '-StateRoot', $stateRoot)
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) { $arguments += @("-$($entry.Key)", [string]$entry.Value) }
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $runtimePath @arguments 2>$null)
  $code = $LASTEXITCODE
  Assert-True ($code -in $Allowed) "$Action exit code $code was not allowed"
  Assert-Equal $output.Count 1 "$Action did not return one line"
  [pscustomobject]@{ Code = $code; Json = $output[0] | ConvertFrom-Json -Depth 50 }
}

function New-ResultFile {
  param([string]$RunId)
  $directory = Join-Path $stateRoot 'candidate-results'
  [IO.Directory]::CreateDirectory($directory) | Out-Null
  Set-PrivatePathAcl -Path $directory -Directory
  $path = Join-Path $directory "$RunId.json"
  $value = [ordered]@{
    category = 'completed'; expectedTransition = 'completed'; changedPaths = @('result.txt')
    verified = @('test'); unverified = @('none'); residualRisk = 'none'
    result = '问题=test；完成=test'; impact = '影响=test；边界=test'; verify = '验证=test；后续=test'; plain = '发生=test；影响=test；需要=test'
  }
  [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Compress -Depth 10), [Text.UTF8Encoding]::new($false))
  Set-PrivatePathAcl -Path $path
  $path
}

$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$stateRoot = Join-Path $env:USERPROFILE ".codex\automation-state\tzg-hourly-runtime-tests\$([Guid]::NewGuid().ToString('N'))"
$repo = Join-Path ([IO.Path]::GetTempPath()) "tzg-hourly-runtime-repo-$([Guid]::NewGuid().ToString('N'))"
try {
  [IO.Directory]::CreateDirectory($repo) | Out-Null
  & git -C $repo init -q; & git -C $repo config user.name 'Runtime Test'; & git -C $repo config user.email 'runtime@example.invalid'
  [IO.File]::WriteAllText((Join-Path $repo 'seed.txt'), 'seed', [Text.UTF8Encoding]::new($false)); & git -C $repo add seed.txt; & git -C $repo commit -q -m 'test: seed'
  $head = [string](& git -C $repo rev-parse HEAD)
  $digest = 'a' * 64

  $initial = Invoke-Runtime Show @{ RepositoryRoot = $repo }
  Assert-Equal $initial.Json.state.schemaVersion 5 'Fresh runtime did not use schema 5'
  Assert-True ($initial.Json.state.PSObject.Properties.Name -cnotcontains 'integrationLease') 'Persistent integration lease remained in schema 5'
  Assert-Equal $initial.Json.integrationLockStatus 'none' 'Fresh integration lock status mismatch'

  $codex = Invoke-Runtime ClaimRun @{ Owner='codex'; TaskId='TASK-C'; Route='codex_execute'; RepositoryRoot=$repo; MainBranch='master'; BaseCommit=$head; TaskCardDigest=$digest }
  $deepseek = Invoke-Runtime ClaimRun @{ Owner='deepseek'; TaskId='TASK-D'; Route='external_execute'; RepositoryRoot=$repo; MainBranch='master'; BaseCommit=$head; TaskCardDigest=('b' * 64) }
  Assert-Equal $codex.Json.status 'CLAIMED' 'Codex claim failed'
  Assert-Equal $deepseek.Json.status 'CLAIMED' 'DeepSeek parallel claim failed'
  $duplicate = Invoke-Runtime ClaimRun @{ Owner='deepseek'; TaskId='TASK-C'; Route='external_execute'; RepositoryRoot=$repo; MainBranch='master'; BaseCommit=$head; TaskCardDigest=('c' * 64) } @(2)
  Assert-True ($duplicate.Json.status -in @('OWNER_OCCUPIED','TASK_OCCUPIED')) 'Duplicate claim was accepted'

  $codexRun = $codex.Json.run
  $candidateSha = 'c' * 40; $canonicalSha = 'd' * 40
  $candidate = Invoke-Runtime UpdateRun @{ Owner='codex'; RunId=$codexRun.runId; RunState='candidate_ready'; SessionKind='codex_cli'; SessionId='session-c'; CandidateCommit=$candidateSha; CandidateResultPath=(New-ResultFile $codexRun.runId) }
  Assert-Equal $candidate.Json.run.state 'candidate_ready' 'Candidate transition failed'
  $canonical = Invoke-Runtime UpdateRun @{ Owner='codex'; RunId=$codexRun.runId; RunState='canonical_ready'; CanonicalBranch='codex/test/canonical'; CanonicalBase=$head; CanonicalHead=$canonicalSha }
  Assert-Equal $canonical.Json.run.state 'canonical_ready' 'Canonical transition failed'
  $integrated = Invoke-Runtime UpdateRun @{ Owner='codex'; RunId=$codexRun.runId; RunState='integrated'; CanonicalHead=$canonicalSha }
  Assert-Equal $integrated.Json.run.state 'integrated' 'Integrated transition failed'
  $closed = Invoke-Runtime CompleteRun @{ Owner='codex'; RunId=$codexRun.runId; CompletionCategory='success'; DetailCode='test_complete' }
  Assert-Equal $closed.Json.status 'RUN_COMPLETED' 'Successful run did not close'

  $deepRun = $deepseek.Json.run
  $attention = Invoke-Runtime UpdateRun @{ Owner='deepseek'; RunId=$deepRun.runId; RunState='attention_required'; RecoveryReason='exact reason' }
  Assert-Equal $attention.Json.run.state 'attention_required' 'Attention transition failed'
  $mismatch = Invoke-Runtime CompleteRun @{ Owner='deepseek'; RunId=$deepRun.runId; CompletionCategory='failed'; DetailCode='manual'; ExpectedRecoveryReason='wrong' } @(2)
  Assert-Equal $mismatch.Json.status 'RUN_NOT_COMPLETABLE' 'Mismatched attention closeout was accepted'
  $manual = Invoke-Runtime CompleteRun @{ Owner='deepseek'; RunId=$deepRun.runId; CompletionCategory='failed'; DetailCode='manual'; ExpectedRecoveryReason='exact reason' }
  Assert-Equal $manual.Json.status 'RUN_COMPLETED' 'Exact attention closeout failed'

  $pause = Invoke-Runtime ClaimRun @{ Owner='codex'; TaskId='TASK-P'; Route='codex_execute'; RepositoryRoot=$repo; MainBranch='master'; BaseCommit=$head; TaskCardDigest=('d' * 64) }
  $pauseRun = $pause.Json.run
  $pauseCanonical = Invoke-Runtime UpdateRun @{ Owner='codex'; RunId=$pauseRun.runId; RunState='canonical_ready'; CanonicalBranch='codex/test/pause'; CanonicalBase=$head; CanonicalHead=('e' * 40) }
  $null = $pauseCanonical
  $null = Invoke-Runtime UpdateRun @{ Owner='codex'; RunId=$pauseRun.runId; RunState='integrated'; CanonicalHead=('e' * 40) }
  $paused = Invoke-Runtime CompleteRun @{ Owner='codex'; RunId=$pauseRun.runId; CompletionCategory='paused'; DetailCode='state_pending_decision' }
  Assert-Equal $paused.Json.category 'paused' 'Controlled pause did not close'

  $migrationRoot = Join-Path $env:USERPROFILE ".codex\automation-state\tzg-hourly-runtime-tests\$([Guid]::NewGuid().ToString('N'))"
  [IO.Directory]::CreateDirectory($migrationRoot) | Out-Null; Set-PrivatePathAcl -Path $migrationRoot -Directory
  $migrationPath = Join-Path $migrationRoot 'runtime.json'
  [IO.File]::WriteAllText($migrationPath, '{"schemaVersion":4,"runs":{"codex":null,"deepseek":null},"integrationLease":null}', [Text.UTF8Encoding]::new($false)); Set-PrivatePathAcl -Path $migrationPath
  $oldStateRoot = $stateRoot; $stateRoot = $migrationRoot
  $migrated = Invoke-Runtime Show @{ RepositoryRoot = $repo }
  Assert-Equal $migrated.Json.state.schemaVersion 5 'Quiescent schema 4 did not migrate'
  Assert-True ($migrated.Json.state.PSObject.Properties.Name -cnotcontains 'integrationLease') 'Migration retained integrationLease'
  $stateRoot = $oldStateRoot

  Write-Output 'test-hourly-automation-lease: OK'
} finally {
  foreach ($path in @($stateRoot, $migrationRoot)) {
    if ($path -and (Test-Path -LiteralPath $path) -and (Normalize-FullPath $path).StartsWith((Normalize-FullPath (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-runtime-tests')) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $path -Recurse -Force }
  }
  if (Test-Path -LiteralPath $repo) { Remove-Item -LiteralPath $repo -Recurse -Force }
}
