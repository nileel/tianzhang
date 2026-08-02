#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$Model,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [switch]$OutputJson,
  [ValidateRange(1, 86400)][int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$selectorPath = Join-Path $PSScriptRoot 'select-hourly-task.ps1'
$candidatePath = Join-Path $PSScriptRoot 'invoke-codex-candidate.ps1'
$checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
$notificationPath = Join-Path $PSScriptRoot 'send-feishu-notification.ps1'
$privateAclPath = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $privateAclPath

function Stop-Hourly { param([string]$DetailCode) $e = [InvalidOperationException]::new($DetailCode); $e.Data['DetailCode'] = $DetailCode; throw $e }
function Normalize-FullPath { param([string]$Path) [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }
function Test-PathWithin { param([string]$Path, [string]$Root) (Normalize-FullPath $Path).StartsWith((Normalize-FullPath $Root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }

function Invoke-BestEffortNotification {
  param([object]$Run)
  if ([string]$Run.taskId -ceq 'QUEUE-MAINTENANCE' -or -not (Test-Path -LiteralPath $notificationPath -PathType Leaf)) { return 'skipped' }
  $transition = [string]$Run.candidateResult.expectedTransition
  $status = switch ($transition) {
    'completed' { 'completed' }
    'pending_decision' { 'waiting_decision' }
    'waiting_reply' { 'waiting_reply' }
    'blocked' { 'blocked' }
    'frozen' { 'blocked' }
    default { 'failed' }
  }
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $notificationPath, '-Kind', 'TaskOutcome',
    '-RepositoryRoot', $script:root, '-TaskId', [string]$Run.taskId, '-Status', $status, '-RunId', [string]$Run.runId
  )
  if ($status -ceq 'completed') { $arguments += @('-CommitSha', [string]$Run.canonicalHead) } else { $arguments += @('-DetailCode', "task_$transition") }
  try {
    $output = @(& pwsh @arguments 2>$null)
    if ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) { return [string]$output[0] }
  } catch {
    # Delivery is isolated from the already committed and closed run.
  }
  'failed'
}

function Invoke-GitText {
  param([string]$Root, [string[]]$Arguments, [string]$DetailCode = 'codex_git_failed')
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'; $startInfo.WorkingDirectory = $Root; $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false); $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @('-C', $Root) + $Arguments) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
  if (-not $process.Start()) { Stop-Hourly $DetailCode }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult(); $null = $stderrTask.GetAwaiter().GetResult(); $exitCode = $process.ExitCode; $process.Dispose()
  if ($exitCode -ne 0) { Stop-Hourly $DetailCode }
  $stdout.TrimEnd()
}

function Invoke-JsonTool {
  param([string]$Path, [string[]]$Arguments, [string]$DetailCode, [int[]]$AllowedExitCodes = @(0))
  $stdout = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>$null)
  $exitCode = $LASTEXITCODE
  if ($exitCode -notin $AllowedExitCodes) { Stop-Hourly $DetailCode }
  $lines = @($stdout | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($lines.Count -ne 1) { Stop-Hourly $DetailCode }
  try { $lines[0] | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly $DetailCode }
}

function Invoke-Runtime {
  param([string]$Action, [hashtable]$Parameters = @{}, [int[]]$AllowedExitCodes = @(0))
  $arguments = @('-Action', $Action, '-StateRoot', $StateRoot)
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) { $arguments += @("-$($entry.Key)", [string]$entry.Value) }
  Invoke-JsonTool -Path $runtimePath -Arguments $arguments -DetailCode 'codex_runtime_failed' -AllowedExitCodes $AllowedExitCodes
}

function Get-NormalizedTextDigest {
  param([string]$Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
}

function Read-TaskMetadata {
  param([string]$Root, [string]$TaskId)
  $path = Join-Path $Root "开发管理/任务卡/$TaskId.txt"
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
  $match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
  if (-not $match.Success) { Stop-Hourly 'codex_task_invalid' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly 'codex_task_invalid' }
  [pscustomobject]@{ Metadata = $metadata; Digest = Get-NormalizedTextDigest -Path $path }
}

function Get-CandidateRoute { param([string]$RuntimeRoute) switch ($RuntimeRoute) { 'codex_execute' { 'Execution' }; 'codex_review' { 'Review' }; 'queue_maintenance' { 'QueueMaintenance' }; default { Stop-Hourly 'codex_route_invalid' } } }

function Assert-WorktreePath {
  param([object]$Run)
  $expected = Normalize-FullPath (Join-Path $script:root ".worktrees\automation\$($Run.runId)\codex")
  $actual = Normalize-FullPath ([string]$Run.worktree)
  if ($actual -cne $expected -or -not (Test-PathWithin -Path $actual -Root (Join-Path $script:root '.worktrees\automation'))) { Stop-Hourly 'codex_worktree_path_invalid' }
  $actual
}

function New-CandidateWorktree {
  param([object]$Run)
  $worktree = Assert-WorktreePath -Run $Run
  if (Test-Path -LiteralPath $worktree) {
    if ((Invoke-GitText -Root $worktree -Arguments @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or (Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.baseCommit) { Stop-Hourly 'codex_worktree_invalid' }
    return $worktree
  }
  [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
  & git -C $script:root show-ref --verify --quiet "refs/heads/$($Run.candidateBranch)" 2>$null
  if ($LASTEXITCODE -eq 0) { Stop-Hourly 'codex_candidate_branch_exists' }
  $null = Invoke-GitText -Root $script:root -Arguments @('worktree', 'add', '-b', [string]$Run.candidateBranch, $worktree, [string]$Run.baseCommit) -DetailCode 'codex_worktree_create_failed'
  $worktree
}

function Write-CandidateResult {
  param([object]$Run, [object]$CandidateResult)
  $directory = Join-Path $StateRoot 'candidate-results'
  [IO.Directory]::CreateDirectory($directory) | Out-Null; Set-PrivatePathAcl -Path $directory -Directory; Assert-PrivatePathAcl -Path $directory -Directory
  $path = Join-Path $directory "$($Run.runId).json"
  [IO.File]::WriteAllText($path, ($CandidateResult | ConvertTo-Json -Compress -Depth 30), [Text.UTF8Encoding]::new($false)); Set-PrivatePathAcl -Path $path; Assert-PrivatePathAcl -Path $path
  $path
}

function Set-Attention { param([object]$Run, [string]$Reason) Invoke-Runtime -Action UpdateRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; RunState = 'attention_required'; RecoveryReason = $Reason } | Out-Null }

function Invoke-Candidate {
  param([object]$Run)
  $worktree = New-CandidateWorktree -Run $Run
  if ((Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.baseCommit -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))) {
    Set-Attention -Run $Run -Reason 'developing worktree contains unverifiable changes'
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'codex_unverifiable_developing_worktree' }
  }
  $candidate = Invoke-JsonTool -Path $candidatePath -Arguments @(
    '-Route', (Get-CandidateRoute -RuntimeRoute ([string]$Run.route)), '-RepositoryRoot', $worktree, '-TaskId', [string]$Run.taskId,
    '-RunId', [string]$Run.runId, '-Model', $Model, '-StateRoot', $StateRoot, '-ResponsibilityTimeoutSeconds', [string]$ResponsibilityTimeoutSeconds
  ) -DetailCode 'codex_responsibility_failed'
  if ([string]$candidate.status -ceq 'no_candidate' -and [string]$Run.route -ceq 'queue_maintenance') {
    $null = Invoke-Runtime -Action CompleteRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; CompletionCategory = 'no_candidate'; DetailCode = 'no_runnable_candidate' }
    return [ordered]@{ status = 'no_candidate'; owner = 'codex'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'no_runnable_candidate' }
  }
  if ([string]$candidate.status -cne 'completed') {
    $detail = if ($candidate.PSObject.Properties.Name -contains 'detailCode') { [string]$candidate.detailCode } else { [string]$candidate.status }
    Set-Attention -Run $Run -Reason "Codex responsibility ended with $([string]$candidate.status)/$detail"
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = $detail }
  }
  $resultPath = Write-CandidateResult -Run $Run -CandidateResult $candidate.candidateResult
  $updated = Invoke-Runtime -Action UpdateRun -Parameters @{
    Owner = 'codex'; RunId = [string]$Run.runId; RunState = 'candidate_ready'; SessionKind = 'codex_cli'; SessionId = [string]$candidate.sessionId;
    CandidateCommit = [string]$candidate.candidateCommit; CandidateResultPath = $resultPath
  }
  [ordered]@{ status = 'candidate_ready'; run = $updated.run }
}

function Test-PathOverlap {
  param([string]$Left, [string]$Right)
  $a = $Left.Replace('\', '/').TrimEnd('/'); $b = $Right.Replace('\', '/').TrimEnd('/')
  $a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or $a.StartsWith($b + '/', [StringComparison]::OrdinalIgnoreCase) -or $b.StartsWith($a + '/', [StringComparison]::OrdinalIgnoreCase)
}

function Get-ChangedPaths {
  param([string]$Root, [string]$Range)
  @((Invoke-GitText -Root $Root -Arguments @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', $Range)) -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
}

function Test-GitAncestor {
  param([string]$Root, [string]$Ancestor, [string]$Descendant)
  & git -C $Root merge-base --is-ancestor $Ancestor $Descendant 2>$null
  if ($LASTEXITCODE -eq 0) { return $true }
  if ($LASTEXITCODE -eq 1) { return $false }
  Stop-Hourly 'codex_git_ancestry_failed'
}

function Assert-CanonicalPostcondition {
  param([object]$Run, [string]$Worktree)
  if ([string]$Run.route -ceq 'queue_maintenance') {
    $evidence = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $Worktree, '-OutputJson') -DetailCode 'codex_canonical_postcondition_failed'
    if ([string]$Run.candidateResult.expectedTransition -cne "queue_ready_count=$([int]$evidence.readyCount)") { Stop-Hourly 'codex_canonical_postcondition_failed' }
  } else {
    $evidence = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $Worktree, '-TaskId', [string]$Run.taskId, '-Postcondition', 'CodexClosedOrNonReady', '-OutputJson') -DetailCode 'codex_canonical_postcondition_failed'
    if ([string]$Run.candidateResult.expectedTransition -cne [string]$evidence.taskState) { Stop-Hourly 'codex_canonical_postcondition_failed' }
  }
}

function Build-Canonical {
  param([object]$Run)
  $worktree = Assert-WorktreePath -Run $Run
  if (-not (Test-Path -LiteralPath $worktree) -or (Invoke-GitText -Root $worktree -Arguments @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or (Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.candidateCommit -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))) {
    Set-Attention -Run $Run -Reason 'candidate evidence does not match the project-owned worktree'
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'codex_candidate_evidence_mismatch' }
  }
  $relevantPaths = @($Run.candidateResult.changedPaths | ForEach-Object { [string]$_ })
  if ([string]$Run.route -ne 'queue_maintenance') {
    $task = Read-TaskMetadata -Root $script:root -TaskId ([string]$Run.taskId)
    $expectedRoute = [string]$Run.route
    if ([string]$task.Digest -cne [string]$Run.taskCardDigest -or [string]$task.Metadata.route -cne $expectedRoute -or [string]$task.Metadata.owner -cne 'codex' -or [string]$task.Metadata.dispatchState -cne 'ready') {
      Set-Attention -Run $Run -Reason 'task facts changed after claim'; return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'codex_task_changed_after_claim' }
    }
    $relevantPaths = @($task.Metadata.expectedPaths | ForEach-Object { [string]$_ })
  } else {
    if ((Get-NormalizedTextDigest -Path (Join-Path $script:root '开发管理\当前任务队列.txt')) -cne [string]$Run.taskCardDigest) {
      Set-Attention -Run $Run -Reason 'queue changed after maintenance claim'; return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'codex_queue_changed_after_claim' }
    }
  }
  $master = Invoke-GitText -Root $script:root -Arguments @('rev-parse', 'master')
  if ($master -cne [string]$Run.baseCommit) {
    foreach ($mainPath in Get-ChangedPaths -Root $script:root -Range "$($Run.baseCommit)..$master") { foreach ($relevantPath in $relevantPaths) { if (Test-PathOverlap -Left $mainPath -Right $relevantPath) { Set-Attention -Run $Run -Reason 'master changed a task-relevant path after claim'; return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'codex_revalidation_required' } } } }
  }
  $canonicalBranch = "codex/automation/codex/$($Run.runId)/canonical-$($master.Substring(0, 12))"
  & git -C $script:root show-ref --verify --quiet "refs/heads/$canonicalBranch" 2>$null
  if ($LASTEXITCODE -eq 0) { Set-Attention -Run $Run -Reason 'canonical branch exists without runtime evidence'; return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'codex_canonical_evidence_incomplete' } }
  try {
    $null = Invoke-GitText -Root $worktree -Arguments @('switch', '-c', $canonicalBranch, $master) -DetailCode 'codex_canonical_branch_failed'
    $null = Invoke-GitText -Root $worktree -Arguments @('cherry-pick', [string]$Run.candidateCommit) -DetailCode 'codex_candidate_replay_failed'
    Assert-CanonicalPostcondition -Run $Run -Worktree $worktree
    if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'codex_canonical_worktree_dirty' }
    $head = Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD')
    $updated = Invoke-Runtime -Action UpdateRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; RunState = 'canonical_ready'; CanonicalBranch = $canonicalBranch; CanonicalBase = $master; CanonicalHead = $head }
    [ordered]@{ status = 'canonical_ready'; run = $updated.run }
  } catch {
    Set-Attention -Run $Run -Reason "canonical build failed: $($_.Exception.Message)"
    [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'codex_canonical_failed' } }
  }
}

function Get-StatusPaths {
  param([string]$Root)
  $paths = @(); foreach ($line in @((Invoke-GitText -Root $Root -Arguments @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')) -split '\r?\n')) { if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) { continue }; $path = $line.Substring(3); $arrow = $path.LastIndexOf(' -> ', [StringComparison]::Ordinal); if ($arrow -ge 0) { $path = $path.Substring($arrow + 4) }; $paths += $path.Replace('\', '/') }; @($paths)
}

function Integrate-Canonical {
  param([object]$Run)
  if ((Invoke-GitText -Root $script:root -Arguments @('branch', '--show-current')) -cne 'master') { return [ordered]@{ status = 'integration_deferred'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'main_branch_not_master' } }
  $mainHead = Invoke-GitText -Root $script:root -Arguments @('rev-parse', 'HEAD')
  if ($mainHead -ceq [string]$Run.canonicalHead -or (Test-GitAncestor -Root $script:root -Ancestor ([string]$Run.canonicalHead) -Descendant $mainHead)) {
    Assert-CanonicalPostcondition -Run $Run -Worktree $script:root
    $null = Invoke-Runtime -Action UpdateRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = [string]$Run.canonicalHead }
    $closed = Invoke-Runtime -Action CompleteRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; CompletionCategory = 'success'; DetailCode = 'integrated_recovery' }
    $delivery = Invoke-BestEffortNotification -Run $Run
    return [ordered]@{ status = 'completed'; category = 'success'; taskId = $Run.taskId; runId = $Run.runId; canonicalHead = $Run.canonicalHead; detailCode = $closed.detailCode; notification = $delivery }
  }
  if ($mainHead -cne [string]$Run.canonicalBase) {
    $worktree = Assert-WorktreePath -Run $Run
    if ([string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))) {
      $null = Invoke-GitText -Root $worktree -Arguments @('switch', [string]$Run.candidateBranch) -DetailCode 'codex_candidate_restore_failed'
      $null = Invoke-Runtime -Action UpdateRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; RunState = 'candidate_ready' }
      return [ordered]@{ status = 'rebuild_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'master_advanced' }
    }
    Set-Attention -Run $Run -Reason 'canonical worktree is dirty after master advanced'; return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'codex_canonical_dirty' }
  }
  $formalPaths = Get-ChangedPaths -Root $script:root -Range "$($Run.canonicalBase)..$($Run.canonicalHead)"
  foreach ($dirty in Get-StatusPaths -Root $script:root) { foreach ($formal in $formalPaths) { if (Test-PathOverlap -Left $dirty -Right $formal) { return [ordered]@{ status = 'integration_deferred'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'main_path_conflict' } } } }
  $lease = Invoke-Runtime -Action AcquireIntegration -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; ExpectedMainHead = $mainHead; IntegrationLeaseSeconds = 300 }
  if ([string]$lease.status -ceq 'INTEGRATION_BUSY') { return [ordered]@{ status = 'occupied'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'integration_busy' } }
  if ([string]$lease.status -cnotin @('INTEGRATION_ACQUIRED', 'ALREADY_ACQUIRED')) { Stop-Hourly 'codex_integration_lease_failed' }
  try {
    if ((Invoke-GitText -Root $script:root -Arguments @('branch', '--show-current')) -cne 'master' -or (Invoke-GitText -Root $script:root -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.canonicalBase) { Stop-Hourly 'codex_integration_precondition_changed' }
    $null = Invoke-GitText -Root $script:root -Arguments @('merge', '--ff-only', [string]$Run.canonicalHead) -DetailCode 'codex_fast_forward_failed'
    if ((Invoke-GitText -Root $script:root -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.canonicalHead) { Stop-Hourly 'codex_fast_forward_verification_failed' }
    Assert-CanonicalPostcondition -Run $Run -Worktree $script:root
    $null = Invoke-Runtime -Action UpdateRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = [string]$Run.canonicalHead }
    $closed = Invoke-Runtime -Action CompleteRun -Parameters @{ Owner = 'codex'; RunId = [string]$Run.runId; CompletionCategory = 'success'; DetailCode = "commit_$(([string]$Run.canonicalHead).Substring(0, 12))" }
    $delivery = Invoke-BestEffortNotification -Run $Run
    [ordered]@{ status = 'completed'; category = 'success'; taskId = $Run.taskId; runId = $Run.runId; canonicalHead = $Run.canonicalHead; detailCode = $closed.detailCode; notification = $delivery }
  } catch {
    if ((Invoke-GitText -Root $script:root -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.canonicalHead) { $null = Invoke-Runtime -Action ReleaseIntegration -Parameters @{ RunId = [string]$Run.runId } -AllowedExitCodes @(0, 2) }
    throw
  }
}

$final = $null
try {
  foreach ($path in @($runtimePath, $selectorPath, $candidatePath, $checkerPath)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-Hourly 'codex_dependency_missing' } }
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot) -or [string]::IsNullOrWhiteSpace($Model)) { Stop-Hourly 'codex_arguments_invalid' }
  $script:root = Normalize-FullPath (Resolve-Path -LiteralPath $RepositoryRoot).Path
  if (-not (Test-Path -LiteralPath (Join-Path $script:root '.git'))) { Stop-Hourly 'codex_repository_invalid' }
  $StateRoot = Normalize-FullPath $StateRoot
  $shown = Invoke-Runtime -Action Show
  if ([string]$shown.status -cne 'OK') { Stop-Hourly 'codex_runtime_unavailable' }
  $run = $shown.state.runs.codex
  if ($null -eq $run) {
    $selection = Invoke-JsonTool -Path $selectorPath -Arguments @('-RepositoryRoot', $script:root, '-Owner', 'codex') -DetailCode 'codex_selection_failed'
    if ([string]$selection.status -ceq 'selected') {
      $taskId = [string]$selection.taskId; $route = [string]$selection.route; $digest = [string]$selection.taskCardDigest
    } elseif ([string]$selection.status -ceq 'no_candidate' -and [int]$selection.queueCount -eq 0) {
      $otherRun = $shown.state.runs.deepseek
      if ($null -ne $shown.state.integrationLease -or ($null -ne $otherRun -and [string]$otherRun.state -cin @('candidate_ready', 'canonical_ready', 'integrated'))) {
        $final = [ordered]@{ status = 'occupied'; owner = 'codex'; detailCode = 'pending_integration' }
      } else {
        $taskId = 'QUEUE-MAINTENANCE'; $route = 'queue_maintenance'; $digest = Get-NormalizedTextDigest -Path (Join-Path $script:root '开发管理\当前任务队列.txt')
      }
    } else {
      $final = [ordered]@{ status = 'no_candidate'; owner = 'codex'; detailCode = 'no_codex_candidate' }
    }
    if ($null -eq $final) {
      $claim = Invoke-Runtime -Action ClaimRun -Parameters @{ Owner = 'codex'; TaskId = $taskId; Route = $route; RepositoryRoot = $script:root; MainBranch = 'master'; BaseCommit = (Invoke-GitText -Root $script:root -Arguments @('rev-parse', 'master')); TaskCardDigest = $digest }
      if ([string]$claim.status -cne 'CLAIMED') { $final = [ordered]@{ status = 'occupied'; owner = 'codex'; detailCode = [string]$claim.status } } else { $run = $claim.run }
    }
  }
  if ($null -eq $final -and $null -ne $run) {
    if ([string]$run.state -ceq 'attention_required') { $final = [ordered]@{ status = 'attention_required'; taskId = $run.taskId; runId = $run.runId; detailCode = $run.recoveryReason } }
    elseif ([string]$run.state -ceq 'developing') { $candidate = Invoke-Candidate -Run $run; if ([string]$candidate.status -cne 'candidate_ready') { $final = $candidate } else { $run = $candidate.run } }
    if ($null -eq $final -and [string]$run.state -ceq 'candidate_ready') { $canonical = Build-Canonical -Run $run; if ([string]$canonical.status -cne 'canonical_ready') { $final = $canonical } else { $run = $canonical.run } }
    if ($null -eq $final -and [string]$run.state -ceq 'canonical_ready') { $final = Integrate-Canonical -Run $run }
    if ($null -eq $final -and [string]$run.state -ceq 'integrated') { $closed = Invoke-Runtime -Action CompleteRun -Parameters @{ Owner = 'codex'; RunId = [string]$run.runId; CompletionCategory = 'success'; DetailCode = 'integrated_recovery' }; $final = [ordered]@{ status = 'completed'; taskId = $run.taskId; runId = $run.runId; canonicalHead = $run.canonicalHead; detailCode = $closed.detailCode } }
  }
} catch {
  $detail = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'codex_hourly_failed' }
  $final = [ordered]@{ status = 'failed'; owner = 'codex'; detailCode = $detail }
}

[Console]::Out.WriteLine(($final | ConvertTo-Json -Compress -Depth 40))
