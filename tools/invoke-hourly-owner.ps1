#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidateSet('codex', 'deepseek')][string]$Owner,
  [Parameter(Mandatory = $true)][ValidateSet('RunOnce', 'Canary')][string]$Action,
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [string]$Model,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [switch]$OutputJson,
  [ValidateRange(1, 86400)][int]$ResponsibilityTimeoutSeconds = 3000,
  [ValidateRange(0, 86400)][int]$IntegrationLockTimeoutSeconds = 3600
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$selectorPath = Join-Path $PSScriptRoot 'select-hourly-task.ps1'
$checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
$transitionPath = Join-Path $PSScriptRoot 'set-task-pending-review.ps1'
$taskStatePath = Join-Path $PSScriptRoot 'set-task-automation-state.ps1'
$finalizerPath = Join-Path $PSScriptRoot 'automation-finalize-commit.ps1'
$notificationPath = Join-Path $PSScriptRoot 'send-feishu-notification.ps1'
$decisionSenderPath = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\send-decision.mjs'
$decisionConsumerPath = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\consume-reply.mjs'

. (Join-Path $PSScriptRoot 'private-path-acl.ps1')
. (Join-Path $PSScriptRoot 'hourly-integration-lock.ps1')
. (Join-Path $PSScriptRoot 'hourly-owner-adapter.ps1')

function Stop-Hourly { param([string]$Code) $e = [InvalidOperationException]::new($Code); $e.Data['DetailCode'] = $Code; throw $e }
function Normalize-FullPath { param([string]$Path) [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }

function Get-InvocationMutexName {
  param([string]$CurrentOwner, [string]$Root)
  $identity = "$CurrentOwner`n$((Normalize-FullPath $Root).ToUpperInvariant())"
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($identity))).ToLowerInvariant()
  "Local\TZG-Hourly-$CurrentOwner-$digest"
}

function Invoke-GitText {
  param([string]$Root, [string[]]$Arguments, [string]$DetailCode = 'hourly_git_failed')
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = 'git'; $start.WorkingDirectory = $Root; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
  $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false); $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @('-C', $Root) + $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  if (-not $process.Start()) { Stop-Hourly $DetailCode }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult(); $null = $stderrTask.GetAwaiter().GetResult(); $code = $process.ExitCode; $process.Dispose()
  if ($code -ne 0) { Stop-Hourly $DetailCode }
  $stdout.TrimEnd()
}

function Invoke-JsonTool {
  param([string]$Path, [string[]]$Arguments, [string]$DetailCode, [int[]]$AllowedExitCodes = @(0))
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>$null)
  $code = $LASTEXITCODE
  if ($code -notin $AllowedExitCodes) { Stop-Hourly $DetailCode }
  $lines = @($output | ForEach-Object { [string]$_ } | Where-Object { $_ })
  if ($lines.Count -ne 1) { Stop-Hourly $DetailCode }
  try { $lines[0] | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly $DetailCode }
}

function Invoke-Runtime {
  param([string]$RuntimeAction, [hashtable]$Parameters = @{}, [int[]]$AllowedExitCodes = @(0))
  $arguments = @('-Action', $RuntimeAction, '-StateRoot', $script:effectiveStateRoot)
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) { $arguments += @("-$($entry.Key)", [string]$entry.Value) }
  Invoke-JsonTool -Path $runtimePath -Arguments $arguments -DetailCode 'hourly_runtime_failed' -AllowedExitCodes $AllowedExitCodes
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
  if (-not $match.Success) { Stop-Hourly 'hourly_task_invalid' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly 'hourly_task_invalid' }
  [pscustomobject]@{ Path = $path; Metadata = $metadata; Digest = Get-NormalizedTextDigest $path }
}

function Get-TaskContextDigest {
  param([object]$Metadata)
  $context = [ordered]@{
    id = [string]$Metadata.id; title = [string]$Metadata.title; priority = [string]$Metadata.priority
    route = [string]$Metadata.route; owner = [string]$Metadata.owner; domain = [string]$Metadata.domain; stage = [string]$Metadata.stage
    blockedBy = @($Metadata.blockedBy | ForEach-Object { [string]$_ }); expectedPaths = @($Metadata.expectedPaths | ForEach-Object { [string]$_ })
    sourceBacklog = [string]$Metadata.sourceBacklog
  }
  $json = $context | ConvertTo-Json -Compress -Depth 20
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
}

function Test-PathOverlap {
  param([string]$Left, [string]$Right)
  $a = $Left.Replace('\', '/').TrimEnd('/'); $b = $Right.Replace('\', '/').TrimEnd('/')
  $a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or $a.StartsWith($b + '/', [StringComparison]::OrdinalIgnoreCase) -or $b.StartsWith($a + '/', [StringComparison]::OrdinalIgnoreCase)
}

function Get-ChangedPaths {
  param([string]$Root, [string]$Range, [string]$DiffFilter)
  $arguments = @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames')
  if (-not [string]::IsNullOrWhiteSpace($DiffFilter)) { $arguments += "--diff-filter=$DiffFilter" }
  $arguments += $Range
  @((Invoke-GitText -Root $Root -Arguments $arguments) -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
}

function Get-StatusPaths {
  param([string]$Root)
  $paths = @()
  foreach ($line in @((Invoke-GitText -Root $Root -Arguments @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')) -split '\r?\n')) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) { continue }
    $path = $line.Substring(3); $arrow = $path.LastIndexOf(' -> ', [StringComparison]::Ordinal)
    if ($arrow -ge 0) { $path = $path.Substring($arrow + 4) }
    $paths += $path.Replace('\', '/')
  }
  @($paths)
}

function Write-PrivateJson {
  param([string]$DirectoryName, [string]$FileName, [object]$Value)
  $directory = Join-Path $script:effectiveStateRoot $DirectoryName
  [IO.Directory]::CreateDirectory($directory) | Out-Null
  Set-PrivatePathAcl -Path $directory -Directory
  $path = Join-Path $directory $FileName
  $temporary = Join-Path $directory ".$FileName.$([Guid]::NewGuid().ToString('N')).tmp"
  try {
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Compress -Depth 50), [Text.UTF8Encoding]::new($false))
    Set-PrivatePathAcl -Path $temporary
    Move-Item -LiteralPath $temporary -Destination $path -Force
    Set-PrivatePathAcl -Path $path
  } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
  $path
}

function Assert-WorktreePath {
  param([object]$Run)
  $expected = Normalize-FullPath (Join-Path $script:root ".worktrees\automation\$($Run.runId)\$Owner")
  $actual = Normalize-FullPath ([string]$Run.worktree)
  $automationRoot = Normalize-FullPath (Join-Path $script:root '.worktrees\automation')
  if ($actual -cne $expected -or -not $actual.StartsWith($automationRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Stop-Hourly 'hourly_worktree_path_invalid' }
  $actual
}

function New-CandidateWorktree {
  param([object]$Run)
  $worktree = Assert-WorktreePath $Run
  if (Test-Path -LiteralPath $worktree) {
    if ((Invoke-GitText $worktree @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or (Invoke-GitText $worktree @('rev-parse', 'HEAD')) -cne [string]$Run.baseCommit) { Stop-Hourly 'hourly_worktree_invalid' }
    return $worktree
  }
  [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
  & git -C $script:root show-ref --verify --quiet "refs/heads/$($Run.candidateBranch)" 2>$null
  if ($LASTEXITCODE -eq 0) { Stop-Hourly 'hourly_candidate_branch_exists' }
  $null = Invoke-GitText $script:root @('worktree', 'add', '-b', [string]$Run.candidateBranch, $worktree, [string]$Run.baseCommit) 'hourly_worktree_create_failed'
  $worktree
}

function Set-Attention {
  param([object]$Run, [string]$Reason)
  try { Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'attention_required'; RecoveryReason = $Reason } | Out-Null } catch {}
}

function Assert-CandidateEvidence {
  param([object]$Run, [object]$Candidate)
  $worktree = Assert-WorktreePath $Run
  if ([string]$Candidate.status -cne 'completed' -or [string]$Candidate.candidateCommit -cnotmatch '^[0-9a-f]{40,64}$' -or
    (Invoke-GitText $worktree @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or
    (Invoke-GitText $worktree @('rev-parse', 'HEAD')) -cne [string]$Candidate.candidateCommit -or
    (Invoke-GitText $worktree @('rev-list', '--count', "$($Run.baseCommit)..$($Candidate.candidateCommit)")) -cne '1' -or
    (Invoke-GitText $worktree @('rev-parse', "$($Candidate.candidateCommit)^")) -cne [string]$Run.baseCommit -or
    -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'hourly_candidate_evidence_invalid' }
  $actual = @(Get-ChangedPaths $worktree "$($Run.baseCommit)..$($Candidate.candidateCommit)")
  $reported = @($Candidate.candidateResult.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  if ($actual.Count -eq 0 -or ($actual -join "`0") -cne ($reported -join "`0")) { Stop-Hourly 'hourly_candidate_evidence_invalid' }
  $task = if ([string]$Run.route -ceq 'queue_maintenance') { $null } else { Read-TaskMetadata $script:root ([string]$Run.taskId) }
  $allowed = if ($null -eq $task) { $actual } else { @($task.Metadata.expectedPaths | ForEach-Object { [string]$_ }) }
  foreach ($path in $actual) { if ($allowed -cnotcontains $path) { Stop-Hourly 'hourly_candidate_path_violation' } }
}

function Get-FormalPaths {
  param([object]$Run, [object]$Task)
  if ([string]$Run.route -ceq 'queue_maintenance') { return @($Run.candidateResult.changedPaths | ForEach-Object { [string]$_ }) }
  $paths = @($Task.Metadata.expectedPaths | ForEach-Object { [string]$_ })
  if ($Owner -ceq 'deepseek' -and $paths -cnotcontains '开发管理/AI合作沟通.txt') { $paths += '开发管理/AI合作沟通.txt' }
  @($paths | Sort-Object -Unique)
}

function Invoke-Finalizer {
  param([string]$Worktree, [string[]]$Arguments)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $finalizerPath -RepositoryRoot $Worktree @Arguments 2>&1)
  $commit = if ($output.Count) { [string]$output[-1] } else { $null }
  if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40,64}$') { Stop-Hourly 'hourly_formal_commit_failed' }
  $commit
}

function Write-Handoff {
  param([object]$Run, [string]$CandidateCommit)
  $path = Join-Path ([string]$Run.worktree) '开发管理\AI合作沟通.txt'
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
  $marker = "### DSH-$($Run.taskId)-$($Run.runId.Substring(0, 8))"
  if ($text.Contains($marker, [StringComparison]::Ordinal)) { Stop-Hourly 'hourly_handoff_duplicate' }
  $entry = @(
    "$marker · DeepSeek 自动交接（⚠️ 未审核）", '', "- 方向：任务 $($Run.taskId) 的原子正式结果等待 Codex 独立复审。",
    "- 任务：$($Run.taskId)", "- 候选提交：$CandidateCommit", '- 正式提交：与本交接、pending_review 投影处于同一原子提交。',
    "- 修改文件：$(@($Run.candidateResult.changedPaths) -join '、')", "- 已验证：$(@($Run.candidateResult.verified) -join '；')",
    "- 未验证：$(@($Run.candidateResult.unverified) -join '；')", "- 残留风险：$($Run.candidateResult.residualRisk)",
    '- 请求判断：请 Codex 按审核入口复审 master 实际原子结果。', '- 建议下一步：通过则关闭原任务并解锁依赖；不通过则按同一卡定向返工。'
  ) -join "`n"
  $text = $text.Replace('# AI合作沟通（✅ 已审核）', '# AI合作沟通（⚠️ 存在待审核交接）')
  $text = if ($text.Contains('当前无有效交接条目。', [StringComparison]::Ordinal)) { $text.Replace('当前无有效交接条目。', $entry) } else { $text.TrimEnd() + "`n`n" + $entry + "`n" }
  [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

function Assert-Postcondition {
  param([object]$Run, [string]$Worktree)
  if ([string]$Run.route -ceq 'queue_maintenance') {
    $evidence = Invoke-JsonTool $checkerPath @('-RepositoryRoot', $Worktree, '-OutputJson') 'hourly_postcondition_failed'
    if ([string]$Run.candidateResult.expectedTransition -cne "queue_ready_count=$([int]$evidence.readyCount)") { Stop-Hourly 'hourly_postcondition_failed' }
  } elseif ($Owner -ceq 'deepseek') {
    $evidence = Invoke-JsonTool $checkerPath @('-RepositoryRoot', $Worktree, '-TaskId', [string]$Run.taskId, '-Postcondition', 'ExternalPendingReview', '-OutputJson') 'hourly_postcondition_failed'
    if ([string]$evidence.status -cne 'ok') { Stop-Hourly 'hourly_postcondition_failed' }
  } else {
    $evidence = Invoke-JsonTool $checkerPath @('-RepositoryRoot', $Worktree, '-TaskId', [string]$Run.taskId, '-Postcondition', 'CodexClosedOrNonReady', '-OutputJson') 'hourly_postcondition_failed'
    if ([string]$evidence.taskState -cne [string]$Run.candidateResult.expectedTransition) { Stop-Hourly 'hourly_postcondition_failed' }
  }
}

function Invoke-CombinedValidation {
  param([object]$Run, [string]$Worktree, [string]$Base, [string]$Head, [string[]]$Paths)
  $changed = @(Get-ChangedPaths $Worktree "$Base..$Head")
  if ($changed.Count -eq 0) { Stop-Hourly 'hourly_formal_empty' }
  foreach ($path in $changed) { if ($Paths -cnotcontains $path) { Stop-Hourly 'hourly_formal_path_violation' } }
  $contentCheckPaths = @(Get-ChangedPaths $Worktree "$Base..$Head" 'ACMRTUXB')
  if ($contentCheckPaths.Count -gt 0) {
    $expected = $contentCheckPaths -join '|'
    Push-Location -LiteralPath $Worktree
    try { $null = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-pending-whitespace.ps1') -ExpectedPaths $expected 2>&1) } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { Stop-Hourly 'hourly_whitespace_failed' }
  }
  $null = Invoke-GitText $Worktree @('diff', '--check', "$Base..$Head") 'hourly_diff_check_failed'
  Assert-Postcondition -Run $Run -Worktree $Worktree
  if (@($changed | Where-Object { $_ -match '^(docs/|src/Assets/(?:Resources|StreamingAssets)/|.+\.(?:csv|json)$)' }).Count -gt 0) {
    $null = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Worktree 'tools\check-data-chain.ps1') 2>&1)
    if ($LASTEXITCODE -ne 0) { Stop-Hourly 'hourly_data_chain_failed' }
  }
}

function Test-MainPathConflict {
  param([string[]]$FormalPaths)
  foreach ($dirty in Get-StatusPaths $script:root) { foreach ($formal in $FormalPaths) { if (Test-PathOverlap $dirty $formal) { return $true } } }
  $false
}

function Build-And-IntegrateCandidate {
  param([object]$Run)
  $lock = Enter-TzgIntegrationLock -RepositoryRoot $script:root -TimeoutSeconds $IntegrationLockTimeoutSeconds
  if ($null -eq $lock) { Set-Attention $Run 'integration lock wait timed out'; return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'integration_lock_timeout' } }
  $formalHead = $null
  try {
    $worktree = Assert-WorktreePath $Run
    if ((Invoke-GitText $worktree @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or (Invoke-GitText $worktree @('rev-parse', 'HEAD')) -cne [string]$Run.candidateCommit -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'hourly_candidate_evidence_invalid' }
    $task = if ([string]$Run.route -ceq 'queue_maintenance') { $null } else { Read-TaskMetadata $script:root ([string]$Run.taskId) }
    if ($null -ne $task -and ([string]$task.Digest -cne [string]$Run.taskCardDigest -or [string]$task.Metadata.route -cne [string]$Run.route -or [string]$task.Metadata.owner -cne $Owner -or [string]$task.Metadata.dispatchState -cne 'ready')) { Stop-Hourly 'hourly_task_changed_after_claim' }
    if ($null -eq $task -and (Get-NormalizedTextDigest (Join-Path $script:root '开发管理\当前任务队列.txt')) -cne [string]$Run.taskCardDigest) { Stop-Hourly 'hourly_queue_changed_after_claim' }
    $latest = Invoke-GitText $script:root @('rev-parse', 'master')
    $formalPaths = Get-FormalPaths -Run $Run -Task $task
    if ($latest -cne [string]$Run.baseCommit) {
      foreach ($mainPath in Get-ChangedPaths $script:root "$($Run.baseCommit)..$latest") { foreach ($formal in $formalPaths) { if (Test-PathOverlap $mainPath $formal) { Stop-Hourly 'hourly_revalidation_required' } } }
    }
    if (Test-MainPathConflict $formalPaths) { Stop-Hourly 'hourly_main_path_conflict' }
    $canonicalBranch = "codex/automation/$Owner/$($Run.runId)/canonical-$($latest.Substring(0, 12))"
    & git -C $script:root show-ref --verify --quiet "refs/heads/$canonicalBranch" 2>$null
    if ($LASTEXITCODE -eq 0) { Stop-Hourly 'hourly_canonical_evidence_incomplete' }
    $null = Invoke-GitText $worktree @('switch', '-c', $canonicalBranch, $latest) 'hourly_canonical_branch_failed'
    if ($Owner -ceq 'deepseek') {
      $null = Invoke-GitText $worktree @('cherry-pick', '--no-commit', [string]$Run.candidateCommit) 'hourly_candidate_replay_failed'
      $transition = Invoke-JsonTool $transitionPath @('-RepositoryRoot', $worktree, '-TaskId', [string]$Run.taskId) 'hourly_pending_review_failed'
      if ([string]$transition.status -cne 'updated') { Stop-Hourly 'hourly_pending_review_failed' }
      Write-Handoff -Run $Run -CandidateCommit ([string]$Run.candidateCommit)
      $formalHead = Invoke-Finalizer $worktree @(
        '-ExpectedPaths', ($formalPaths -join '|'), '-CommitMessage', "feat($($Run.taskId)): complete DeepSeek task",
        '-RequireAutomationMetadata', '-AutomationTask', [string]$Run.taskId, '-AutomationState', 'pending_review',
        '-AutomationResult', [string]$Run.candidateResult.result, '-AutomationImpact', [string]$Run.candidateResult.impact,
        '-AutomationVerify', [string]$Run.candidateResult.verify, '-AutomationPlain', [string]$Run.candidateResult.plain
      )
    } else {
      $null = Invoke-GitText $worktree @('cherry-pick', [string]$Run.candidateCommit) 'hourly_candidate_replay_failed'
      $formalHead = Invoke-GitText $worktree @('rev-parse', 'HEAD')
    }
    Invoke-CombinedValidation -Run $Run -Worktree $worktree -Base $latest -Head $formalHead -Paths $formalPaths
    $updated = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'canonical_ready'; CanonicalBranch = $canonicalBranch; CanonicalBase = $latest; CanonicalHead = $formalHead }
    $Run = $updated.run
    if ((Invoke-GitText $script:root @('branch', '--show-current')) -cne 'master' -or (Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $latest -or (Test-MainPathConflict $formalPaths)) { Stop-Hourly 'hourly_integration_precondition_changed' }
    $null = Invoke-GitText $script:root @('merge', '--ff-only', $formalHead) 'hourly_fast_forward_failed'
    if ((Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $formalHead) { Stop-Hourly 'hourly_fast_forward_verification_failed' }
    Assert-Postcondition -Run $Run -Worktree $script:root
    $null = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = $formalHead }
    $closed = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; CompletionCategory = 'success'; DetailCode = "commit_$($formalHead.Substring(0, 12))" }
    [ordered]@{ status = if ([string]$Run.route -ceq 'queue_maintenance') { 'maintenance_completed' } else { 'completed' }; category = 'success'; taskId = $Run.taskId; runId = $Run.runId; formalHead = $formalHead; canonicalBranch = $canonicalBranch; detailCode = $closed.detailCode }
  } catch {
    $detail = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'hourly_formal_failed' }
    if ($null -ne $formalHead -and (Invoke-GitText $script:root @('rev-parse', 'HEAD')) -ceq $formalHead) {
      try { Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = $formalHead } | Out-Null } catch {}
    } else { Set-Attention $Run "formal integration stopped: $detail" }
    [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = $detail }
  } finally { Exit-TzgIntegrationLock -Handle $lock }
}

function Invoke-BestEffortNotification {
  param([object]$Run, [object]$Outcome)
  if ([string]$Run.route -ceq 'queue_maintenance' -or [string]$Outcome.status -ne 'completed') { return 'skipped' }
  $status = if ($Owner -ceq 'deepseek') { 'pending_review' } else { switch ([string]$Run.candidateResult.expectedTransition) { 'completed' { 'completed' }; 'blocked' { 'blocked' }; 'frozen' { 'blocked' }; 'pending_decision' { 'waiting_decision' }; 'waiting_reply' { 'waiting_reply' }; default { 'failed' } } }
  $arguments = @('-Kind', 'TaskOutcome', '-RepositoryRoot', $script:root, '-TaskId', [string]$Run.taskId, '-Status', $status, '-RunId', [string]$Run.runId)
  if ($status -cin @('completed', 'pending_review')) { $arguments += @('-CommitSha', [string]$Outcome.formalHead) } else { $arguments += @('-DetailCode', "task_$status") }
  try { $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $notificationPath @arguments 2>$null); if ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) { return [string]$output[0] } } catch {}
  'failed'
}

function Remove-ExactSuccessfulWorktree {
  param([object]$Run, [string]$FormalHead)
  try {
    $shown = Invoke-Runtime -RuntimeAction Show -Parameters @{ RepositoryRoot = $script:root }
    foreach ($active in @($shown.state.runs.codex, $shown.state.runs.deepseek)) { if ($null -ne $active -and (Normalize-FullPath ([string]$active.worktree)) -ceq (Normalize-FullPath ([string]$Run.worktree))) { return 'retained_runtime_reference' } }
    $worktree = Assert-WorktreePath $Run
    if (-not (Test-Path -LiteralPath $worktree) -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all'))) -or (Invoke-GitText $worktree @('rev-parse', 'HEAD')) -cne $FormalHead) { return 'retained_evidence_mismatch' }
    & git -C $script:root merge-base --is-ancestor $FormalHead master 2>$null
    if ($LASTEXITCODE -ne 0) { return 'retained_unintegrated' }
    $currentBranch = Invoke-GitText $worktree @('branch', '--show-current')
    if ([string]$Run.canonicalBranch -cne $currentBranch) { return 'retained_branch_mismatch' }
    $null = Invoke-GitText $script:root @('-c', 'core.longPaths=true', 'worktree', 'remove', '--force', $worktree) 'hourly_cleanup_failed'
    foreach ($branch in @([string]$Run.candidateBranch, [string]$Run.canonicalBranch) | Sort-Object -Unique) {
      & git -C $script:root show-ref --verify --quiet "refs/heads/$branch" 2>$null
      if ($LASTEXITCODE -eq 0) { $null = Invoke-GitText $script:root @('branch', '-D', $branch) 'hourly_cleanup_failed' }
    }
    $parent = Split-Path -Parent $worktree
    if ((Test-Path -LiteralPath $parent) -and @(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) { Remove-Item -LiteralPath $parent -Force }
    'cleaned'
  } catch { 'retained_cleanup_failed' }
}

function New-StateTransitionContext {
  param([object]$Run, [ValidateSet('Block', 'PauseDecision')][string]$Mode, [object]$Candidate)
  $task = Read-TaskMetadata $script:root ([string]$Run.taskId)
  if ([string]$task.Digest -cne [string]$Run.taskCardDigest) { Stop-Hourly 'hourly_task_changed_after_claim' }
  if ($Mode -ceq 'Block') {
    return [ordered]@{ schemaVersion = 1; taskId = [string]$Run.taskId; detailCode = [string]$Candidate.detailCode }
  }
  $decision = $Candidate.candidateResult
  [ordered]@{
    schemaVersion = 1; taskId = [string]$Run.taskId; sourceRunId = [string]$Run.runId; owner = $Owner; route = [string]$Run.route
    decisionId = [string]$decision.decisionId; question = [string]$decision.question; options = @($decision.options)
    recommendedOption = [string]$decision.recommendedOption; impactSummary = [string]$decision.impactSummary; plainSummary = $decision.plainSummary
    checkpointCommit = [string]$decision.checkpointCommit; baseCommit = [string]$decision.baseCommit; branch = [string]$decision.branch
    changedPaths = @($decision.changedPaths); verified = @($decision.verified); unverified = @($decision.unverified); residualRisk = [string]$decision.residualRisk
    taskContextDigest = Get-TaskContextDigest $task.Metadata; createdAt = [DateTimeOffset]::Now.ToString('o')
  }
}

function Integrate-StateTransition {
  param([object]$Run, [ValidateSet('Block', 'PauseDecision', 'ResumeReady')][string]$Mode, [object]$Context, [AllowNull()][string]$ExistingWorktree)
  $lock = Enter-TzgIntegrationLock -RepositoryRoot $script:root -TimeoutSeconds $IntegrationLockTimeoutSeconds
  if ($null -eq $lock) { Stop-Hourly 'integration_lock_timeout' }
  $formalHead = $null
  try {
    $latest = Invoke-GitText $script:root @('rev-parse', 'master')
    $worktree = if ([string]::IsNullOrWhiteSpace($ExistingWorktree)) { Assert-WorktreePath $Run } else { Normalize-FullPath $ExistingWorktree }
    if (-not (Test-Path -LiteralPath $worktree) -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'hourly_state_worktree_invalid' }
    $branch = "codex/automation/$Owner/$($Run.runId)/state-$($Mode.ToLowerInvariant())-$($latest.Substring(0, 12))"
    & git -C $script:root show-ref --verify --quiet "refs/heads/$branch" 2>$null
    if ($LASTEXITCODE -eq 0) { Stop-Hourly 'hourly_state_branch_exists' }
    $null = Invoke-GitText $worktree @('switch', '-c', $branch, $latest) 'hourly_state_branch_failed'
    $contextPath = Write-PrivateJson 'state-transitions' "$($Run.runId)-$Mode.json" $Context
    $actionName = if ($Mode -ceq 'PauseDecision') { 'PauseDecision' } elseif ($Mode -ceq 'ResumeReady') { 'ResumeReady' } else { 'Block' }
    $projection = Invoke-JsonTool $taskStatePath @('-Action', $actionName, '-RepositoryRoot', $worktree, '-TaskId', [string]$Run.taskId, '-ContextPath', $contextPath) 'hourly_state_projection_failed'
    if ([string]$projection.status -cne 'updated') { Stop-Hourly 'hourly_state_projection_failed' }
    $paths = @($projection.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    $stateText = if ($Mode -ceq 'PauseDecision') { 'pending_decision' } elseif ($Mode -ceq 'ResumeReady') { 'ready' } else { 'blocked' }
    $formalHead = Invoke-Finalizer $worktree @(
      '-ExpectedPaths', ($paths -join '|'), '-CommitMessage', "chore($($Run.taskId)): set automation state $stateText",
      '-RequireAutomationMetadata', '-AutomationTask', [string]$Run.taskId, '-AutomationState', 'completed',
      '-AutomationResult', "问题=任务需要确定终态；完成=任务已机械转换为 $stateText",
      '-AutomationImpact', '影响=任务调度投影已同步；边界=未合并未核验业务修改',
      '-AutomationVerify', '验证=任务卡投影检查通过；后续=按当前状态继续处理',
      '-AutomationPlain', "发生=任务状态已经变为 $stateText；影响=业务修改尚未作为完成结果进入主分支；需要=按通知说明处理"
    )
    if ($Mode -cne 'ResumeReady') {
      $null = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'canonical_ready'; CanonicalBranch = $branch; CanonicalBase = $latest; CanonicalHead = $formalHead }
    }
    if ((Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $latest -or (Test-MainPathConflict $paths)) { Stop-Hourly 'hourly_integration_precondition_changed' }
    $null = Invoke-GitText $script:root @('merge', '--ff-only', $formalHead) 'hourly_fast_forward_failed'
    if ($Mode -cne 'ResumeReady') {
      $null = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = $formalHead }
      $category = if ($Mode -ceq 'PauseDecision') { 'paused' } else { 'success' }
      $null = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; CompletionCategory = $category; DetailCode = "state_$stateText" }
    }
    [ordered]@{ status = $stateText; taskId = $Run.taskId; runId = $Run.runId; formalHead = $formalHead; stateBranch = $branch; worktree = $worktree }
  } catch {
    if ($Mode -cne 'ResumeReady') {
      $detail = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'hourly_state_transition_failed' }
      try { Set-Attention $Run "state transition ended with $Mode/$detail" } catch {}
    }
    throw
  } finally { Exit-TzgIntegrationLock -Handle $lock }
}

function Send-DecisionCheckpoint {
  param([object]$Context)
  $request = [ordered]@{
    decision = [ordered]@{ decisionId = [string]$Context.decisionId; taskId = [string]$Context.taskId; question = [string]$Context.question; options = @($Context.options); recommendedOption = [string]$Context.recommendedOption; impactSummary = [string]$Context.impactSummary; plainSummary = $Context.plainSummary }
    attemptNumber = 1
  }
  $path = Write-PrivateJson 'decision-requests' "$($Context.decisionId).json" $request
  try {
    $output = @(& node $decisionSenderPath --request-file $path 2>$null)
    if ($output.Count -eq 1) { return [string]$output[0] }
  } catch {}
  '{"result":"CHANNEL_UNAVAILABLE"}'
}

function Find-AnsweredCheckpoint {
  $cards = @()
  foreach ($file in Get-ChildItem -LiteralPath (Join-Path $script:root '开发管理\任务卡') -Filter '*.txt' -File) {
    try {
      $task = Read-TaskMetadata $script:root $file.BaseName
      $meta = $task.Metadata
      if ([string]$meta.owner -ceq $Owner -and [string]$meta.dispatchState -cin @('pending_decision', 'waiting_reply') -and $meta.PSObject.Properties.Name -contains 'automationCheckpoint') { $cards += $task }
    } catch {}
  }
  foreach ($task in @($cards | Sort-Object { [string]$_.Metadata.automationCheckpoint.createdAt }, { [string]$_.Metadata.id })) {
    $meta = $task.Metadata; $checkpoint = $meta.automationCheckpoint
    if ([string]$checkpoint.taskContextDigest -cne (Get-TaskContextDigest $meta)) { return [ordered]@{ status = 'attention_required'; detailCode = 'checkpoint_task_context_changed'; taskId = [string]$meta.id } }
    $acceptedPath = Join-Path $script:effectiveStateRoot "accepted-replies\$($checkpoint.decisionId).json"
    if (Test-Path -LiteralPath $acceptedPath) {
      try { $reply = [IO.File]::ReadAllText($acceptedPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 30 } catch { return [ordered]@{ status = 'attention_required'; detailCode = 'checkpoint_reply_invalid'; taskId = [string]$meta.id } }
    } else {
      $requestPath = Join-Path $script:effectiveStateRoot "decision-requests\$($checkpoint.decisionId).json"
      if (-not (Test-Path -LiteralPath $requestPath -PathType Leaf)) { continue }
      try { $requestSnapshot = [IO.File]::ReadAllText($requestPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 30 } catch { return [ordered]@{ status = 'attention_required'; detailCode = 'checkpoint_reply_invalid'; taskId = [string]$meta.id } }
      if ($requestSnapshot.PSObject.Properties.Name -contains 'decision') { continue }
      $output = @(& node $decisionConsumerPath --request-file $requestPath 2>$null)
      if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { return [ordered]@{ status = 'attention_required'; detailCode = 'checkpoint_reply_invalid'; taskId = [string]$meta.id } }
      try { $consumed = $output[0] | ConvertFrom-Json -Depth 30 } catch { return [ordered]@{ status = 'attention_required'; detailCode = 'checkpoint_reply_invalid'; taskId = [string]$meta.id } }
      if ([string]$consumed.result -ceq 'NO_REPLY') { continue }
      if ([string]$consumed.result -cnotin @('OPTION_ACCEPTED', 'CUSTOM_ACCEPTED')) { return [ordered]@{ status = 'attention_required'; detailCode = 'checkpoint_reply_invalid'; taskId = [string]$meta.id } }
      $replyValue = if ([string]$consumed.result -ceq 'OPTION_ACCEPTED') { [string]$consumed.optionKey } else { [string]$consumed.customText }
      $reply = [ordered]@{
        schemaVersion = 1; taskId = [string]$meta.id; decisionId = [string]$checkpoint.decisionId; result = [string]$consumed.result
        replyKind = if ([string]$consumed.result -ceq 'OPTION_ACCEPTED') { 'option' } else { 'custom' }; replyValue = $replyValue
        source = [string]$consumed.source; evidenceHash = [string]$consumed.evidenceHash
      }
      $acceptedPath = Write-PrivateJson 'accepted-replies' "$($checkpoint.decisionId).json" $reply
    }
    return [ordered]@{ status = 'answered'; task = $task; reply = $reply; acceptedPath = $acceptedPath }
  }
  [ordered]@{ status = 'none' }
}

function Restore-AnsweredCheckpoint {
  param([object]$Answered)
  $task = $Answered.task; $checkpoint = $task.Metadata.automationCheckpoint
  $oldRun = [pscustomobject]@{ runId = [string]$checkpoint.sourceRunId; taskId = [string]$task.Metadata.id; route = [string]$checkpoint.route; owner = $Owner; worktree = (Join-Path $script:root ".worktrees\automation\$($checkpoint.sourceRunId)\$Owner"); candidateBranch = [string]$checkpoint.branch }
  $result = Integrate-StateTransition -Run $oldRun -Mode ResumeReady -Context $Answered.reply -ExistingWorktree ([string]$oldRun.worktree)
  if ([string]$result.status -cne 'ready') { return $result }
  [ordered]@{ status = 'restored'; taskId = [string]$oldRun.taskId; checkpoint = $checkpoint; reply = $Answered.reply; oldWorktree = [string]$oldRun.worktree }
}

function Apply-CheckpointToNewRun {
  param([object]$Run, [object]$Restored)
  if ($null -eq $Restored -or [string]$Restored.taskId -cne [string]$Run.taskId) { return $null }
  $checkpoint = $Restored.checkpoint
  $branchSha = Invoke-GitText $script:root @('rev-parse', [string]$checkpoint.branch) 'checkpoint_branch_invalid'
  if ($branchSha -cne [string]$checkpoint.checkpointCommit -or (Invoke-GitText $script:root @('rev-parse', "$branchSha^")) -cne [string]$checkpoint.baseCommit) { Stop-Hourly 'checkpoint_commit_invalid' }
  $actual = Get-ChangedPaths $script:root "$($checkpoint.baseCommit)..$branchSha"
  $reported = @($checkpoint.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  if (($actual -join "`0") -cne ($reported -join "`0")) { Stop-Hourly 'checkpoint_paths_invalid' }
  $task = Read-TaskMetadata $script:root ([string]$Run.taskId)
  $allowed = @($task.Metadata.expectedPaths | ForEach-Object { [string]$_ })
  foreach ($path in $actual) { if ($allowed -cnotcontains $path) { Stop-Hourly 'checkpoint_paths_invalid' } }
  $worktree = Assert-WorktreePath $Run
  try { $null = Invoke-GitText $worktree @('cherry-pick', '--no-commit', $branchSha) 'checkpoint_replay_conflict' } catch { Set-Attention $Run 'checkpoint replay conflicts with latest master'; throw }
  $context = [ordered]@{
    schemaVersion = 1; taskId = [string]$Run.taskId; decisionId = [string]$checkpoint.decisionId
    replyKind = [string]$Restored.reply.replyKind; replyValue = [string]$Restored.reply.replyValue; source = [string]$Restored.reply.source
    evidenceHash = [string]$Restored.reply.evidenceHash; checkpointCommit = $branchSha; checkpointChangedPaths = $actual
  }
  Write-PrivateJson 'resume-contexts' "$($Run.runId).json" $context
}

function Remove-ConsumedCheckpointWorktree {
  param([object]$Restored)
  if ($null -eq $Restored) { return 'none' }
  try {
    $checkpoint = $Restored.checkpoint; $worktree = Normalize-FullPath ([string]$Restored.oldWorktree)
    if (-not (Test-Path -LiteralPath $worktree) -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all')))) { return 'retained_checkpoint_evidence' }
    if ((Invoke-GitText $script:root @('rev-parse', [string]$checkpoint.branch)) -cne [string]$checkpoint.checkpointCommit) { return 'retained_checkpoint_branch' }
    $null = Invoke-GitText $script:root @('-c', 'core.longPaths=true', 'worktree', 'remove', $worktree) 'checkpoint_cleanup_failed'
    $prefix = "refs/heads/codex/automation/$Owner/$($checkpoint.sourceRunId)/"
    $refs = @((Invoke-GitText $script:root @('for-each-ref', '--format=%(refname)', $prefix)) -split '\r?\n' | Where-Object { $_ })
    foreach ($ref in $refs) { $null = Invoke-GitText $script:root @('branch', '-D', $ref.Substring('refs/heads/'.Length)) 'checkpoint_cleanup_failed' }
    'cleaned'
  } catch { 'retained_checkpoint_cleanup_failed' }
}

function Invoke-Canary {
  $beforeHead = Invoke-GitText $script:root @('rev-parse', 'HEAD'); $beforeStatus = Invoke-GitText $script:root @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')
  $id = "canary-$([Guid]::NewGuid().ToString('N'))"; $worktree = Normalize-FullPath (Join-Path $script:root ".worktrees\automation\$id\$Owner"); $branch = "codex/automation/$Owner/$id/candidate"
  [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
  $null = Invoke-GitText $script:root @('worktree', 'add', '-b', $branch, $worktree, $beforeHead) 'hourly_canary_worktree_failed'
  $success = $false
  try {
    $private = Invoke-Runtime -RuntimeAction Show -Parameters @{ RepositoryRoot = $script:root }
    if ([string]$private.status -cne 'OK' -or $private.activeTaskIds.Count -ne 0) { Stop-Hourly 'hourly_canary_private_state_failed' }
    $arguments = Get-HourlyCanaryArguments -Adapter $script:adapter -RepositoryRoot $worktree -StateRoot $script:effectiveStateRoot -TimeoutSeconds $ResponsibilityTimeoutSeconds
    $wrapper = Invoke-JsonTool $script:adapter.candidateScript $arguments 'hourly_canary_adapter_failed'
    if ([string]$wrapper.status -cne 'verified' -or [string]$wrapper.model -cne [string]$script:adapter.model) { Stop-Hourly 'hourly_canary_identity_failed' }
    if ((Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $beforeHead -or (Invoke-GitText $script:root @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')) -cne $beforeStatus -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'hourly_canary_isolation_failed' }
    $success = $true
    [ordered]@{ status = 'verified'; owner = $Owner; identity = $wrapper.identity; model = $wrapper.model; privateState = 'isolated'; mainHead = $beforeHead }
  } finally {
    if ($success) {
      $null = Invoke-GitText $script:root @('-c', 'core.longPaths=true', 'worktree', 'remove', $worktree) 'hourly_canary_cleanup_failed'; $null = Invoke-GitText $script:root @('branch', '-D', $branch) 'hourly_canary_cleanup_failed'
      $parent = Split-Path -Parent $worktree; if ((Test-Path -LiteralPath $parent) -and @(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) { Remove-Item -LiteralPath $parent -Force }
    }
  }
}

$final = $null
$run = $null
$script:stage = 'initialize'
$invocationMutex = $null
$invocationHeld = $false
try {
  $script:stage = 'dependencies'
  foreach ($path in @($runtimePath, $selectorPath, $checkerPath, $taskStatePath, $finalizerPath)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-Hourly 'hourly_dependency_missing' } }
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { Stop-Hourly 'hourly_repository_invalid' }
  $script:root = Normalize-FullPath (Resolve-Path -LiteralPath $RepositoryRoot).Path
  if (-not (Test-Path -LiteralPath (Join-Path $script:root '.git'))) { Stop-Hourly 'hourly_repository_invalid' }
  $script:adapter = Get-HourlyOwnerAdapter -Owner $Owner -Model $Model -ToolsRoot $PSScriptRoot
  if (-not (Test-Path -LiteralPath $script:adapter.candidateScript -PathType Leaf)) { Stop-Hourly 'hourly_adapter_missing' }
  $boundState = $PSBoundParameters.ContainsKey('StateRoot')
  $script:effectiveStateRoot = if ($Action -ceq 'Canary' -and -not $boundState) { Join-Path $env:USERPROFILE ".codex\automation-state\tzg-hourly-canary\$([Guid]::NewGuid().ToString('N'))" } else { Normalize-FullPath $StateRoot }
  $invocationMutex = [Threading.Mutex]::new($false, (Get-InvocationMutexName $Owner $script:effectiveStateRoot))
  try { $invocationHeld = $invocationMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $invocationHeld = $true }
  if (-not $invocationHeld) { $final = [ordered]@{ status = 'occupied'; owner = $Owner; detailCode = 'owner_entry_running' } }
  elseif ($Action -ceq 'Canary') { $final = Invoke-Canary }
  else {
    $script:stage = 'runtime_show'
    $shown = Invoke-Runtime -RuntimeAction Show -Parameters @{ RepositoryRoot = $script:root }
    if ([string]$shown.status -cne 'OK') { Stop-Hourly 'hourly_runtime_unavailable' }
    $run = $shown.state.runs.$Owner
    if ($null -ne $run) {
      $final = [ordered]@{ status = 'existing_run'; owner = $Owner; taskId = $run.taskId; runId = $run.runId; state = $run.state; detailCode = $run.recoveryReason }
    } else {
      $answered = Find-AnsweredCheckpoint
      $restored = $null
      if ([string]$answered.status -ceq 'answered') { $restored = Restore-AnsweredCheckpoint $answered }
      elseif ([string]$answered.status -ceq 'attention_required') { $final = $answered }
      if ($null -eq $final) {
        $script:stage = 'selection'
        $selection = Invoke-JsonTool $selectorPath @('-RepositoryRoot', $script:root, '-Owner', $Owner) 'hourly_selection_failed'
        if ([string]$selection.status -ceq 'selected') { $taskId = [string]$selection.taskId; $route = [string]$selection.route; $digest = [string]$selection.taskCardDigest }
        elseif ($Owner -ceq 'codex' -and [string]$selection.status -ceq 'no_candidate' -and [int]$selection.queueCount -eq 0 -and $null -eq $shown.state.runs.deepseek) { $taskId = 'QUEUE-MAINTENANCE'; $route = 'queue_maintenance'; $digest = Get-NormalizedTextDigest (Join-Path $script:root '开发管理\当前任务队列.txt') }
        else { $final = [ordered]@{ status = 'no_candidate'; owner = $Owner; detailCode = 'no_runnable_candidate' } }
      }
      if ($null -eq $final) {
        $script:stage = 'claim'
        $claim = Invoke-Runtime -RuntimeAction ClaimRun -Parameters @{ Owner = $Owner; TaskId = $taskId; Route = $route; RepositoryRoot = $script:root; MainBranch = 'master'; BaseCommit = (Invoke-GitText $script:root @('rev-parse', 'master')); TaskCardDigest = $digest }
        if ([string]$claim.status -cne 'CLAIMED') { $final = [ordered]@{ status = 'occupied'; owner = $Owner; detailCode = [string]$claim.status } } else { $run = $claim.run }
      }
      if ($null -eq $final) {
        $script:stage = 'candidate_worktree'
        $null = New-CandidateWorktree $run
        $resumeContext = Apply-CheckpointToNewRun -Run $run -Restored $restored
        $candidateArgs = Get-HourlyCandidateArguments -Adapter $script:adapter -Run $run -StateRoot $script:effectiveStateRoot -TimeoutSeconds $ResponsibilityTimeoutSeconds -ResumeContextPath $resumeContext
        $script:stage = 'candidate'
        $candidate = Invoke-JsonTool $script:adapter.candidateScript $candidateArgs 'hourly_candidate_failed'
        switch ([string]$candidate.status) {
          'completed' {
            $script:stage = 'candidate_evidence'
            Assert-CandidateEvidence -Run $run -Candidate $candidate
            $resultPath = Write-PrivateJson 'candidate-results' "$($run.runId).json" $candidate.candidateResult
            $updated = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$run.runId; RunState = 'candidate_ready'; SessionKind = [string]$script:adapter.sessionKind; SessionId = [string]$candidate.sessionId; CandidateCommit = [string]$candidate.candidateCommit; CandidateResultPath = $resultPath }
            $run = $updated.run
            $script:stage = 'formal_integration'
            $outcome = Build-And-IntegrateCandidate $run
            if ([string]$outcome.status -cin @('completed', 'maintenance_completed')) {
              $run.canonicalBranch = [string]$outcome.canonicalBranch
              $run.canonicalHead = [string]$outcome.formalHead
              $outcome.notification = Invoke-BestEffortNotification -Run $run -Outcome $outcome
              $outcome.cleanup = Remove-ExactSuccessfulWorktree -Run $run -FormalHead ([string]$outcome.formalHead)
              $outcome.checkpointCleanup = Remove-ConsumedCheckpointWorktree $restored
            }
            $final = $outcome
          }
          'no_candidate' {
            if ([string]$run.route -cne 'queue_maintenance') { Stop-Hourly 'hourly_no_candidate_invalid' }
            $null = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{ Owner = $Owner; RunId = [string]$run.runId; CompletionCategory = 'no_candidate'; DetailCode = 'no_runnable_candidate' }
            $emptyRun = [pscustomobject]@{ runId = $run.runId; worktree = $run.worktree; candidateBranch = $run.candidateBranch; canonicalBranch = $run.candidateBranch }
            $final = [ordered]@{ status = 'no_candidate'; owner = $Owner; taskId = $run.taskId; runId = $run.runId; detailCode = 'no_runnable_candidate'; cleanup = Remove-ExactSuccessfulWorktree -Run $emptyRun -FormalHead ([string]$run.baseCommit) }
          }
          'needs_decision' {
            $context = New-StateTransitionContext -Run $run -Mode PauseDecision -Candidate $candidate
            $transition = Integrate-StateTransition -Run $run -Mode PauseDecision -Context $context
            $transition.notification = Send-DecisionCheckpoint $context
            $transition.checkpointWorktree = [string]$run.worktree
            $final = $transition
          }
          'blocked' {
            $context = New-StateTransitionContext -Run $run -Mode Block -Candidate $candidate
            $transition = Integrate-StateTransition -Run $run -Mode Block -Context $context
            try { $notification = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $notificationPath -Kind TaskOutcome -RepositoryRoot $script:root -TaskId ([string]$run.taskId) -Status blocked -RunId ([string]$run.runId) -DetailCode ([string]$candidate.detailCode) 2>$null); $transition.notification = if ($notification.Count -eq 1) { [string]$notification[0] } else { 'failed' } } catch { $transition.notification = 'failed' }
            $transition.cleanup = Remove-ExactSuccessfulWorktree -Run ([pscustomobject]@{ runId=$run.runId; worktree=$transition.worktree; candidateBranch=$run.candidateBranch; canonicalBranch=$transition.stateBranch }) -FormalHead ([string]$transition.formalHead)
            $final = $transition
          }
          default {
            $detail = if ($candidate.PSObject.Properties.Name -contains 'detailCode') { [string]$candidate.detailCode } else { [string]$candidate.status }
            Set-Attention $run "$($script:adapter.identity) responsibility ended with $([string]$candidate.status)/$detail"
            $final = [ordered]@{ status = 'attention_required'; owner = $Owner; taskId = $run.taskId; runId = $run.runId; detailCode = $detail }
          }
        }
      }
    }
  }
} catch {
  $detail = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { "hourly_owner_$($script:stage)" }
  if ($null -ne $run) {
    try { Set-Attention $run "$($script:adapter.identity) responsibility ended with failed/$detail" } catch {}
  }
  $final = [ordered]@{ status = 'failed'; owner = $Owner; detailCode = $detail }
} finally {
  if ($invocationHeld) { $invocationMutex.ReleaseMutex() }
  if ($null -ne $invocationMutex) { $invocationMutex.Dispose() }
}

[Console]::Out.WriteLine(($final | ConvertTo-Json -Compress -Depth 50))
