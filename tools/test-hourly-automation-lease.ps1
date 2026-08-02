#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Assert-Equal {
  param([AllowNull()]$Actual, [AllowNull()]$Expected, [string]$Message)
  if ($Actual -cne $Expected) {
    throw "$Message (actual=$Actual expected=$Expected)"
  }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolPath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$privateAclPath = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $privateAclPath

function Invoke-Runtime {
  param(
    [string]$Action,
    [hashtable]$Parameters = @{},
    [int[]]$AllowedExitCodes = @(0)
  )

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $toolPath, '-Action', $Action)) {
    $startInfo.ArgumentList.Add($argument)
  }
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) {
    $startInfo.ArgumentList.Add("-$($entry.Key)")
    $startInfo.ArgumentList.Add([string]$entry.Value)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  Assert-True $process.Start() "Unable to start runtime action $Action"
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()
  $lines = @($stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  Assert-Equal $lines.Count 1 "$Action must emit exactly one stdout line; stderr=$stderr"
  Assert-True ($exitCode -in $AllowedExitCodes) "$Action exit code $exitCode was not allowed; parameters=$($Parameters | ConvertTo-Json -Compress); stderr=$stderr"
  [pscustomobject]@{
    ExitCode = $exitCode
    Json = $lines[0] | ConvertFrom-Json -Depth 100
    Stderr = $stderr
  }
}

function Get-FileSha256 {
  param([string]$Path)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path)))
}

function Write-PrivateJson {
  param([string]$Path, [object]$Value)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  Set-PrivatePathAcl -Path (Split-Path -Parent $Path) -Directory
  [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Compress -Depth 20), [Text.UTF8Encoding]::new($false))
  Set-PrivatePathAcl -Path $Path
}

function New-CandidateResult {
  param([string]$ChangedPath = 'fixtures/result.txt')
  [ordered]@{
    category = 'completed'
    expectedTransition = 'codex_review/codex/ready'
    changedPaths = @($ChangedPath)
    verified = @('fixture verification passed')
    unverified = @('none')
    residualRisk = 'fixture only'
    result = '问题=缺少候选；完成=生成候选'
    impact = '影响=验证双运行状态；边界=不修改生产状态'
    verify = '验证=runtime 测试通过；后续=等待集成'
    plain = '发生=生成了测试候选；影响=只验证自动流程；需要=无需处理'
  }
}

function Claim-Run {
  param([string]$StateRoot, [string]$Owner, [string]$TaskId, [string]$Route)
  Invoke-Runtime -Action ClaimRun -Parameters @{
    StateRoot = $StateRoot
    Owner = $Owner
    TaskId = $TaskId
    Route = $Route
    RepositoryRoot = $repositoryRoot
    MainBranch = 'master'
    BaseCommit = $script:baseCommit
    TaskCardDigest = (('a' * 64) -join '')
  }
}

$automationStateRoot = Join-Path $env:USERPROFILE '.codex\automation-state'
$testId = [Guid]::NewGuid().ToString('N')
$stateRoot = Join-Path $automationStateRoot "tzg-hourly-runtime-tests\$testId"
$migrationRoot = Join-Path $automationStateRoot "tzg-hourly-runtime-migration-tests\$testId"
$activeLegacyRoot = Join-Path $automationStateRoot "tzg-hourly-runtime-active-legacy-tests\$testId"
$attentionRoot = Join-Path $automationStateRoot "tzg-hourly-runtime-attention-tests\$testId"
$attentionCandidateRoot = Join-Path $automationStateRoot "tzg-hourly-runtime-attention-candidate-tests\$testId"
$statePath = Join-Path $stateRoot 'runtime.json'
$script:baseCommit = [string](& git -C $repositoryRoot rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve repository HEAD' }

try {
  $initial = Invoke-Runtime -Action Show -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal ([int]$initial.Json.state.schemaVersion) 4 'Initial schema version mismatch'
  Assert-Equal ([string]$initial.Json.integrationLeaseStatus) 'none' 'Initial integration lease status mismatch'
  Assert-Equal @($initial.Json.activeTaskIds).Count 0 'Initial active task list was not empty'

  $codex = Claim-Run -StateRoot $stateRoot -Owner codex -TaskId 'TASK-CODEX' -Route codex_execute
  Assert-Equal ([string]$codex.Json.status) 'CLAIMED' 'Codex run was not claimed'
  $codexRunId = [string]$codex.Json.run.runId
  Assert-True ([Guid]::TryParse($codexRunId, [ref]([Guid]::Empty))) 'Codex runId is not a GUID'
  Assert-True ([string]$codex.Json.run.worktree -like "*\.worktrees\automation\$codexRunId\codex") 'Codex worktree convention mismatch'
  Assert-Equal ([string]$codex.Json.run.candidateBranch) "codex/automation/codex/$codexRunId/candidate" 'Codex candidate branch mismatch'

  $deepseek = Claim-Run -StateRoot $stateRoot -Owner deepseek -TaskId 'TASK-DEEPSEEK' -Route external_execute
  Assert-Equal ([string]$deepseek.Json.status) 'CLAIMED' 'DeepSeek run was not claimed while Codex run existed'
  $deepseekRunId = [string]$deepseek.Json.run.runId

  $shown = Invoke-Runtime -Action Show -Parameters @{ StateRoot = $stateRoot }
  Assert-Equal @($shown.Json.activeTaskIds).Count 2 'Show did not expose both active taskIds'
  Assert-Equal ([string]$shown.Json.state.runs.codex.taskId) 'TASK-CODEX' 'Show lost Codex run'
  Assert-Equal ([string]$shown.Json.state.runs.deepseek.taskId) 'TASK-DEEPSEEK' 'Show lost DeepSeek run'

  $ownerBusy = Claim-Run -StateRoot $stateRoot -Owner codex -TaskId 'TASK-CODEX-SECOND' -Route codex_review
  Assert-Equal ([string]$ownerBusy.Json.status) 'OWNER_BUSY' 'Second Codex run was not rejected'
  $duplicateTask = Claim-Run -StateRoot $stateRoot -Owner deepseek -TaskId 'TASK-CODEX' -Route external_execute
  Assert-Equal ([string]$duplicateTask.Json.status) 'OWNER_BUSY' 'Owner occupancy must be reported before duplicate-task evaluation'

  $beforeWrongRun = Get-FileSha256 $statePath
  $wrongRun = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'codex'
    RunId = [Guid]::NewGuid().ToString()
    RunState = 'developing'
  } -AllowedExitCodes @(2)
  Assert-Equal ([string]$wrongRun.Json.status) 'RUN_ID_MISMATCH' 'Wrong runId was not rejected'
  Assert-Equal (Get-FileSha256 $statePath) $beforeWrongRun 'Wrong runId changed runtime bytes'

  $sessionUpdate = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'deepseek'
    RunId = $deepseekRunId
    RunState = 'developing'
    SessionKind = 'claude_cli'
    SessionId = [Guid]::NewGuid().ToString()
  }
  Assert-Equal ([string]$sessionUpdate.Json.run.sessionKind) 'claude_cli' 'DeepSeek session kind mismatch'

  $candidateDirectory = Join-Path $stateRoot 'candidate-results'
  $codexResultPath = Join-Path $candidateDirectory "$codexRunId.json"
  $deepseekResultPath = Join-Path $candidateDirectory "$deepseekRunId.json"
  Write-PrivateJson -Path $codexResultPath -Value (New-CandidateResult -ChangedPath 'fixtures/codex.txt')
  Write-PrivateJson -Path $deepseekResultPath -Value (New-CandidateResult -ChangedPath 'fixtures/deepseek.txt')

  $codexCandidate = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'codex'
    RunId = $codexRunId
    RunState = 'candidate_ready'
    CandidateCommit = (('b' * 40) -join '')
    CandidateResultPath = $codexResultPath
  }
  Assert-Equal ([string]$codexCandidate.Json.run.state) 'candidate_ready' 'Codex candidate transition failed'

  $deepseekCandidate = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'deepseek'
    RunId = $deepseekRunId
    RunState = 'candidate_ready'
    CandidateCommit = (('c' * 40) -join '')
    CandidateResultPath = $deepseekResultPath
  }
  Assert-Equal ([string]$deepseekCandidate.Json.run.candidateResult.changedPaths[0]) 'fixtures/deepseek.txt' 'Candidate result was not persisted'

  $codexCanonical = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'codex'
    RunId = $codexRunId
    RunState = 'canonical_ready'
    CanonicalBranch = "codex/automation/codex/$codexRunId/canonical"
    CanonicalBase = $script:baseCommit
    CanonicalHead = (('d' * 40) -join '')
  }
  Assert-Equal ([string]$codexCanonical.Json.run.state) 'canonical_ready' 'Codex canonical transition failed'

  $deepseekCanonical = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'deepseek'
    RunId = $deepseekRunId
    RunState = 'canonical_ready'
    CanonicalBranch = "codex/automation/deepseek/$deepseekRunId/canonical"
    CanonicalBase = $script:baseCommit
    CanonicalHead = (('e' * 40) -join '')
  }
  Assert-Equal ([string]$deepseekCanonical.Json.run.state) 'canonical_ready' 'DeepSeek canonical transition failed'

  $codexLease = Invoke-Runtime -Action AcquireIntegration -Parameters @{
    StateRoot = $stateRoot
    Owner = 'codex'
    RunId = $codexRunId
    ExpectedMainHead = $script:baseCommit
    IntegrationLeaseSeconds = 60
  }
  Assert-Equal ([string]$codexLease.Json.status) 'INTEGRATION_ACQUIRED' 'Codex integration lease failed'
  $busyLease = Invoke-Runtime -Action AcquireIntegration -Parameters @{
    StateRoot = $stateRoot
    Owner = 'deepseek'
    RunId = $deepseekRunId
    ExpectedMainHead = $script:baseCommit
  }
  Assert-Equal ([string]$busyLease.Json.status) 'INTEGRATION_BUSY' 'Second integration writer was not rejected'
  Invoke-Runtime -Action ReleaseIntegration -Parameters @{ StateRoot = $stateRoot; RunId = $codexRunId } | Out-Null

  $rebuildCodex = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'codex'
    RunId = $codexRunId
    RunState = 'candidate_ready'
  }
  Assert-True ($null -eq $rebuildCodex.Json.run.canonicalHead) 'Returning to candidate_ready retained stale canonical evidence'

  $deepseekLease = Invoke-Runtime -Action AcquireIntegration -Parameters @{
    StateRoot = $stateRoot
    Owner = 'deepseek'
    RunId = $deepseekRunId
    ExpectedMainHead = $script:baseCommit
  }
  Assert-Equal ([string]$deepseekLease.Json.status) 'INTEGRATION_ACQUIRED' 'DeepSeek integration lease failed after release'
  $deepseekIntegrated = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'deepseek'
    RunId = $deepseekRunId
    RunState = 'integrated'
    CanonicalHead = (('e' * 40) -join '')
  }
  Assert-Equal ([string]$deepseekIntegrated.Json.run.state) 'integrated' 'DeepSeek integrated transition failed'
  $deepseekComplete = Invoke-Runtime -Action CompleteRun -Parameters @{
    StateRoot = $stateRoot
    Owner = 'deepseek'
    RunId = $deepseekRunId
    CompletionCategory = 'success'
    DetailCode = 'fixture_integrated'
  }
  Assert-Equal ([string]$deepseekComplete.Json.status) 'RUN_COMPLETED' 'DeepSeek complete failed'
  $afterComplete = Invoke-Runtime -Action Show -Parameters @{ StateRoot = $stateRoot }
  Assert-True ($null -eq $afterComplete.Json.state.runs.deepseek) 'CompleteRun retained DeepSeek run'
  Assert-True ($null -eq $afterComplete.Json.state.integrationLease) 'CompleteRun retained its integration lease'

  $emptyStateRoot = Join-Path $stateRoot 'empty-completion'
  $emptyRun = Claim-Run -StateRoot $emptyStateRoot -Owner deepseek -TaskId 'TASK-EMPTY' -Route external_execute
  $emptyComplete = Invoke-Runtime -Action CompleteRun -Parameters @{
    StateRoot = $emptyStateRoot
    Owner = 'deepseek'
    RunId = [string]$emptyRun.Json.run.runId
    CompletionCategory = 'no_candidate'
    DetailCode = 'no_runnable_candidate'
  }
  Assert-Equal ([string]$emptyComplete.Json.category) 'no_candidate' 'Clean no-candidate run did not close'

  $attentionRun = Claim-Run -StateRoot $attentionRoot -Owner deepseek -TaskId 'TASK-ATTENTION' -Route external_execute
  $attention = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $attentionRoot
    Owner = 'deepseek'
    RunId = [string]$attentionRun.Json.run.runId
    RunState = 'attention_required'
    RecoveryReason = 'uncommitted changes remain'
  }
  Assert-Equal ([string]$attention.Json.run.state) 'attention_required' 'Attention transition failed'
  $attentionComplete = Invoke-Runtime -Action CompleteRun -Parameters @{
    StateRoot = $attentionRoot
    Owner = 'deepseek'
    RunId = [string]$attentionRun.Json.run.runId
    CompletionCategory = 'failed'
    DetailCode = 'must_not_clear_attention'
  } -AllowedExitCodes @(2)
  Assert-Equal ([string]$attentionComplete.Json.status) 'RUN_NOT_COMPLETABLE' 'Attention run was incorrectly cleared'

  $attentionMismatch = Invoke-Runtime -Action CompleteRun -Parameters @{
    StateRoot = $attentionRoot
    Owner = 'deepseek'
    RunId = [string]$attentionRun.Json.run.runId
    CompletionCategory = 'failed'
    DetailCode = 'manual_attention_closeout'
    ExpectedRecoveryReason = 'different recovery reason'
  } -AllowedExitCodes @(2)
  Assert-Equal ([string]$attentionMismatch.Json.status) 'RUN_NOT_COMPLETABLE' 'Attention run accepted mismatched recovery evidence'

  $attentionResolved = Invoke-Runtime -Action CompleteRun -Parameters @{
    StateRoot = $attentionRoot
    Owner = 'deepseek'
    RunId = [string]$attentionRun.Json.run.runId
    CompletionCategory = 'failed'
    DetailCode = 'manual_attention_closeout'
    ExpectedRecoveryReason = 'uncommitted changes remain'
  }
  Assert-Equal ([string]$attentionResolved.Json.status) 'RUN_COMPLETED' 'Attention run was not manually closed'
  Assert-Equal ([string]$attentionResolved.Json.recoveryReason) 'uncommitted changes remain' 'Attention closeout omitted recovery evidence'
  $afterAttention = Invoke-Runtime -Action Show -Parameters @{ StateRoot = $attentionRoot }
  Assert-True ($null -eq $afterAttention.Json.state.runs.deepseek) 'Manual attention closeout retained the owner run'

  $attentionCandidate = Claim-Run -StateRoot $attentionCandidateRoot -Owner deepseek -TaskId 'TASK-ATTENTION-CANDIDATE' -Route external_execute
  $attentionCandidateRunId = [string]$attentionCandidate.Json.run.runId
  $attentionCandidateResultPath = Join-Path $attentionCandidateRoot "candidate-results\$attentionCandidateRunId.json"
  Write-PrivateJson -Path $attentionCandidateResultPath -Value (New-CandidateResult -ChangedPath 'fixtures/recovered.txt')
  $null = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $attentionCandidateRoot
    Owner = 'deepseek'
    RunId = $attentionCandidateRunId
    RunState = 'attention_required'
    RecoveryReason = 'concurrent entry observed in-progress changes'
  }
  $attentionCandidateMismatch = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $attentionCandidateRoot
    Owner = 'deepseek'
    RunId = $attentionCandidateRunId
    RunState = 'candidate_ready'
    CandidateCommit = (('f' * 40) -join '')
    CandidateResultPath = $attentionCandidateResultPath
    ExpectedRecoveryReason = 'different recovery reason'
  } -AllowedExitCodes @(2)
  Assert-Equal ([string]$attentionCandidateMismatch.Json.status) 'INVALID_ARGUMENT' 'Attention candidate recovery accepted mismatched evidence'
  $attentionCandidateRecovered = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $attentionCandidateRoot
    Owner = 'deepseek'
    RunId = $attentionCandidateRunId
    RunState = 'candidate_ready'
    SessionKind = 'claude_cli'
    SessionId = [Guid]::NewGuid().ToString()
    CandidateCommit = (('f' * 40) -join '')
    CandidateResultPath = $attentionCandidateResultPath
    ExpectedRecoveryReason = 'concurrent entry observed in-progress changes'
  }
  Assert-Equal ([string]$attentionCandidateRecovered.Json.run.state) 'candidate_ready' 'Verified attention candidate was not recovered'
  Assert-Equal ([string]$attentionCandidateRecovered.Json.run.candidateCommit) (('f' * 40) -join '') 'Recovered candidate commit mismatch'
  Assert-True ($null -eq $attentionCandidateRecovered.Json.run.recoveryReason) 'Recovered candidate retained stale attention reason'
  $null = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $attentionCandidateRoot
    Owner = 'deepseek'
    RunId = $attentionCandidateRunId
    RunState = 'attention_required'
    RecoveryReason = 'canonical build failed before evidence was recorded'
  }
  $attentionCandidateRetried = Invoke-Runtime -Action UpdateRun -Parameters @{
    StateRoot = $attentionCandidateRoot
    Owner = 'deepseek'
    RunId = $attentionCandidateRunId
    RunState = 'candidate_ready'
    CandidateCommit = (('f' * 40) -join '')
    CandidateResultPath = $attentionCandidateResultPath
    ExpectedRecoveryReason = 'canonical build failed before evidence was recorded'
  }
  Assert-Equal ([string]$attentionCandidateRetried.Json.run.state) 'candidate_ready' 'Recorded candidate could not recover after a pre-evidence canonical failure'

  [IO.Directory]::CreateDirectory($migrationRoot) | Out-Null
  Set-PrivatePathAcl -Path $migrationRoot -Directory
  $migrationPath = Join-Path $migrationRoot 'runtime.json'
  $legacyQuiescent = [ordered]@{
    schemaVersion = 3
    lease = $null
    recovery = $null
    blocking = [ordered]@{ fingerprint = $null; count = 0; pauseRequested = $false }
    lastResult = $null
  }
  Write-PrivateJson -Path $migrationPath -Value $legacyQuiescent
  $migrated = Invoke-Runtime -Action Show -Parameters @{ StateRoot = $migrationRoot }
  Assert-Equal ([int]$migrated.Json.state.schemaVersion) 4 'Quiescent schema 3 runtime was not migrated in memory'
  $migrationClaim = Claim-Run -StateRoot $migrationRoot -Owner codex -TaskId 'TASK-MIGRATED' -Route codex_execute
  Assert-Equal ([string]$migrationClaim.Json.status) 'CLAIMED' 'Migrated runtime was not writable'
  $persistedMigration = Get-Content -Raw -LiteralPath $migrationPath | ConvertFrom-Json
  Assert-Equal ([int]$persistedMigration.schemaVersion) 4 'Migrated runtime did not persist schema 4 on mutation'

  [IO.Directory]::CreateDirectory($activeLegacyRoot) | Out-Null
  Set-PrivatePathAcl -Path $activeLegacyRoot -Directory
  $activeLegacyPath = Join-Path $activeLegacyRoot 'runtime.json'
  $legacyActive = [ordered]@{
    schemaVersion = 3
    lease = [ordered]@{
      runId = [Guid]::NewGuid().ToString()
      taskId = 'LEGACY-ACTIVE'
      owner = 'deepseek'
      repositoryRoot = $repositoryRoot
      startedAt = '2026-08-01T00:00:00.0000000+00:00'
      expiresAt = '2026-08-01T01:00:00.0000000+00:00'
    }
    recovery = $null
    blocking = [ordered]@{ fingerprint = $null; count = 0; pauseRequested = $false }
    lastResult = $null
  }
  Write-PrivateJson -Path $activeLegacyPath -Value $legacyActive
  $legacyHash = Get-FileSha256 $activeLegacyPath
  $migrationRequired = Invoke-Runtime -Action Show -Parameters @{ StateRoot = $activeLegacyRoot } -AllowedExitCodes @(2)
  Assert-Equal ([string]$migrationRequired.Json.status) 'MIGRATION_REQUIRED' 'Active legacy runtime did not fail closed'
  Assert-Equal (Get-FileSha256 $activeLegacyPath) $legacyHash 'Rejected legacy migration changed state bytes'

  $bytes = [IO.File]::ReadAllBytes($statePath)
  $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
  Assert-True (-not $hasBom) 'Runtime state has a UTF-8 BOM'
  $runtimeText = [Text.Encoding]::UTF8.GetString($bytes)
  Assert-True ($runtimeText -notmatch '"(?i:providerToken|tenantKey|openId|chatId|messageId|eventId|secret)"\s*:') 'Runtime contains forbidden secret fields'
  Assert-PrivatePathAcl -Path $stateRoot -Directory
  Assert-PrivatePathAcl -Path $statePath
  Assert-Equal @(Get-ChildItem -LiteralPath $stateRoot -File -Filter '*.tmp-*').Count 0 'Atomic write left temporary files'

  Write-Output 'test-hourly-automation-lease: OK'
} finally {
  foreach ($cleanupPath in @($stateRoot, $migrationRoot, $activeLegacyRoot, $attentionRoot, $attentionCandidateRoot)) {
    if (-not (Test-Path -LiteralPath $cleanupPath)) { continue }
    $resolved = [IO.Path]::GetFullPath($cleanupPath)
    $approvedPrefix = [IO.Path]::GetFullPath($automationStateRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($approvedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing cleanup outside automation state root: $resolved"
    }
    if ((Split-Path -Leaf $resolved) -cne $testId) {
      throw "Refusing cleanup of unexpected test path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
