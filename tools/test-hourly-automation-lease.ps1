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
  [IO.File]::WriteAllText((Join-Path $repo '.gitignore'), ".worktrees/`n", [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $repo 'seed.txt'), 'seed', [Text.UTF8Encoding]::new($false)); & git -C $repo add .gitignore seed.txt; & git -C $repo commit -q -m 'test: seed'
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

  $abandon = Invoke-Runtime ClaimRun @{ Owner='codex'; TaskId='TASK-A'; Route='codex_review'; RepositoryRoot=$repo; MainBranch='master'; BaseCommit=$head; TaskCardDigest=('e' * 64) }
  $abandonRun = $abandon.Json.run
  [IO.Directory]::CreateDirectory((Split-Path -Parent ([string]$abandonRun.worktree))) | Out-Null
  & git -C $repo worktree add -q -b ([string]$abandonRun.candidateBranch) ([string]$abandonRun.worktree) $head
  Assert-Equal $LASTEXITCODE 0 'Candidate evidence worktree was not created'
  [IO.File]::WriteAllText((Join-Path ([string]$abandonRun.worktree) 'result.txt'), 'candidate', [Text.UTF8Encoding]::new($false))
  & git -C ([string]$abandonRun.worktree) add result.txt
  & git -C ([string]$abandonRun.worktree) commit -q -m 'test: candidate evidence'
  $abandonCandidate = [string](& git -C ([string]$abandonRun.worktree) rev-parse HEAD)
  $null = Invoke-Runtime UpdateRun @{ Owner='codex'; RunId=$abandonRun.runId; RunState='candidate_ready'; SessionKind='codex_cli'; SessionId='session-a'; CandidateCommit=$abandonCandidate; CandidateResultPath=(New-ResultFile $abandonRun.runId) }
  $formalBranch = "codex/automation/codex/$($abandonRun.runId)/canonical-manual"
  & git -C ([string]$abandonRun.worktree) switch -q -c $formalBranch
  [IO.File]::WriteAllText((Join-Path ([string]$abandonRun.worktree) 'formal.txt'), 'formal', [Text.UTF8Encoding]::new($false))
  & git -C ([string]$abandonRun.worktree) add formal.txt
  & git -C ([string]$abandonRun.worktree) commit -q -m 'test: unrecorded formal evidence'
  $formalHead = [string](& git -C ([string]$abandonRun.worktree) rev-parse HEAD)
  $attentionWithCandidate = Invoke-Runtime UpdateRun @{ Owner='codex'; RunId=$abandonRun.runId; RunState='attention_required'; RecoveryReason='formal integration stopped: hourly_whitespace_failed' }
  Assert-Equal $attentionWithCandidate.Json.run.state 'attention_required' 'Candidate attention transition failed'
  $closeEvidence = @{
    Owner='codex'; RunId=$abandonRun.runId; CompletionCategory='failed'; DetailCode='manual_abandon'
    ExpectedRecoveryReason='formal integration stopped: hourly_whitespace_failed'; ExpectedCandidateCommit=$abandonCandidate
    ExpectedWorktree=[string]$abandonRun.worktree; ExpectedWorktreeBranch=$formalBranch; ExpectedWorktreeHead=$formalHead
  }
  $wrongCandidate = @{} + $closeEvidence; $wrongCandidate.ExpectedCandidateCommit = 'f' * 40
  $rejectedCandidate = Invoke-Runtime CompleteRun $wrongCandidate @(2)
  Assert-Equal $rejectedCandidate.Json.status 'RUN_NOT_COMPLETABLE' 'Mismatched candidate evidence was accepted'
  $wrongRecovery = @{} + $closeEvidence; $wrongRecovery.ExpectedRecoveryReason = 'wrong recovery reason'
  $rejectedRecovery = Invoke-Runtime CompleteRun $wrongRecovery @(2)
  Assert-Equal $rejectedRecovery.Json.status 'RUN_NOT_COMPLETABLE' 'Mismatched candidate recovery reason was accepted'
  $wrongWorktree = @{} + $closeEvidence; $wrongWorktree.ExpectedWorktree = Join-Path $repo '.worktrees/automation/wrong/codex'
  $rejectedWorktree = Invoke-Runtime CompleteRun $wrongWorktree @(2)
  Assert-Equal $rejectedWorktree.Json.status 'RUN_NOT_COMPLETABLE' 'Mismatched evidence worktree was accepted'
  $wrongBranch = @{} + $closeEvidence; $wrongBranch.ExpectedWorktreeBranch = "$formalBranch-wrong"
  $rejectedBranch = Invoke-Runtime CompleteRun $wrongBranch @(2)
  Assert-Equal $rejectedBranch.Json.status 'RUN_NOT_COMPLETABLE' 'Mismatched worktree branch was accepted'
  $wrongHead = @{} + $closeEvidence; $wrongHead.ExpectedWorktreeHead = 'a' * 40
  $rejectedHead = Invoke-Runtime CompleteRun $wrongHead @(2)
  Assert-Equal $rejectedHead.Json.status 'RUN_NOT_COMPLETABLE' 'Mismatched worktree HEAD was accepted'
  [IO.File]::WriteAllText((Join-Path ([string]$abandonRun.worktree) 'dirty.txt'), 'dirty', [Text.UTF8Encoding]::new($false))
  $rejectedDirty = Invoke-Runtime CompleteRun $closeEvidence @(2)
  Assert-Equal $rejectedDirty.Json.status 'RUN_NOT_COMPLETABLE' 'Dirty evidence worktree was accepted'
  Remove-Item -LiteralPath (Join-Path ([string]$abandonRun.worktree) 'dirty.txt') -Force
  $masterBeforeAbandon = [string](& git -C $repo rev-parse master)
  $abandoned = Invoke-Runtime CompleteRun $closeEvidence
  Assert-Equal $abandoned.Json.status 'RUN_COMPLETED' 'Exact candidate attention closeout failed'
  Assert-True ([bool]$abandoned.Json.evidenceRetained) 'Candidate attention closeout did not report retained evidence'
  Assert-True (Test-Path -LiteralPath ([string]$abandonRun.worktree) -PathType Container) 'Candidate attention closeout removed evidence worktree'
  Assert-Equal ([string](& git -C $repo rev-parse "refs/heads/$([string]$abandonRun.candidateBranch)")) $abandonCandidate 'Candidate attention closeout changed evidence branch'
  Assert-Equal ([string](& git -C $repo rev-parse master)) $masterBeforeAbandon 'Candidate attention closeout changed master'
  $afterAbandon = Invoke-Runtime Show @{ RepositoryRoot=$repo }
  Assert-True ($null -eq $afterAbandon.Json.state.runs.codex) 'Candidate attention closeout left the owner occupied'

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
