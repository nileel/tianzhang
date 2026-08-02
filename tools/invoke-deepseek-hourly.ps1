#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Canary', 'RunOnce')]
  [string]$Action,
  [Parameter(Mandatory = $true)]
  [string]$RepositoryRoot,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [switch]$OutputJson,
  [ValidateRange(1, 86400)]
  [int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$selectorPath = Join-Path $PSScriptRoot 'select-hourly-task.ps1'
$responsibilityPath = Join-Path $PSScriptRoot 'invoke-deepseek-responsibility.ps1'
$transitionPath = Join-Path $PSScriptRoot 'set-task-pending-review.ps1'
$finalizerPath = Join-Path $PSScriptRoot 'automation-finalize-commit.ps1'
$checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
$notificationPath = Join-Path $PSScriptRoot 'send-feishu-notification.ps1'
$privateAclPath = Join-Path $PSScriptRoot 'private-path-acl.ps1'
. $privateAclPath

function Stop-Hourly {
  param([string]$DetailCode)
  $exception = [InvalidOperationException]::new($DetailCode)
  $exception.Data['DetailCode'] = $DetailCode
  throw $exception
}

function Normalize-FullPath { param([string]$Path) [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }

function Get-InvocationMutexName {
  param([string]$Owner, [string]$Root)
  $identity = "$Owner`n$((Normalize-FullPath $Root).ToUpperInvariant())"
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.UTF8Encoding]::new($false).GetBytes($identity)
  )).ToLowerInvariant()
  "Local\TZG-Hourly-$Owner-$digest"
}

function Test-PathWithin {
  param([string]$Path, [string]$Root)
  $fullPath = Normalize-FullPath $Path
  $fullRoot = Normalize-FullPath $Root
  $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-BestEffortNotification {
  param([object]$Run, [string]$BusinessCommit)
  if (-not (Test-Path -LiteralPath $notificationPath -PathType Leaf)) { return 'skipped' }
  try {
    $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $notificationPath `
      -Kind TaskOutcome -RepositoryRoot $script:resolvedRepositoryRoot -TaskId ([string]$Run.taskId) `
      -Status pending_review -RunId ([string]$Run.runId) -CommitSha $BusinessCommit 2>$null)
    if ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) { return [string]$output[0] }
  } catch {
    # Delivery is isolated from the already committed and closed run.
  }
  'failed'
}

function Invoke-GitText {
  param([string]$Root, [string[]]$Arguments, [string]$DetailCode = 'deepseek_git_failed')
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
  param([string]$ScriptPath, [string[]]$Arguments, [string]$DetailCode, [int[]]$AllowedExitCodes = @(0))
  $stdout = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>$null)
  $exitCode = $LASTEXITCODE
  if ($exitCode -notin $AllowedExitCodes) { Stop-Hourly $DetailCode }
  $lines = @($stdout | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($lines.Count -ne 1) { Stop-Hourly $DetailCode }
  try { $lines[0] | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly $DetailCode }
}

function Invoke-Runtime {
  param([string]$RuntimeAction, [hashtable]$Parameters = @{}, [int[]]$AllowedExitCodes = @(0))
  $arguments = @('-Action', $RuntimeAction, '-StateRoot', $script:effectiveStateRoot)
  foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) {
    $arguments += @("-$($entry.Key)", [string]$entry.Value)
  }
  Invoke-JsonTool -ScriptPath $runtimePath -Arguments $arguments -DetailCode 'deepseek_runtime_failed' -AllowedExitCodes $AllowedExitCodes
}

function Get-NormalizedTextDigest {
  param([string]$Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n"))
  )).ToLowerInvariant()
}

function Read-TaskMetadata {
  param([string]$Root, [string]$TaskId)
  $path = Join-Path $Root "开发管理/任务卡/$TaskId.txt"
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
  $match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
  if (-not $match.Success) { Stop-Hourly 'deepseek_task_invalid' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly 'deepseek_task_invalid' }
  [pscustomobject]@{ Path = $path; Metadata = $metadata; Digest = Get-NormalizedTextDigest -Path $path }
}

function Assert-RunWorktreePath {
  param([object]$Run)
  $expected = Normalize-FullPath (Join-Path $script:resolvedRepositoryRoot ".worktrees\automation\$($Run.runId)\deepseek")
  $actual = Normalize-FullPath ([string]$Run.worktree)
  if ($actual -cne $expected -or -not (Test-PathWithin -Path $actual -Root (Join-Path $script:resolvedRepositoryRoot '.worktrees\automation'))) {
    Stop-Hourly 'deepseek_worktree_path_invalid'
  }
  $actual
}

function New-CandidateWorktree {
  param([object]$Run)
  $worktree = Assert-RunWorktreePath -Run $Run
  if (Test-Path -LiteralPath $worktree) {
    if (
      (Invoke-GitText -Root $worktree -Arguments @('branch', '--show-current') -DetailCode 'deepseek_worktree_invalid') -cne [string]$Run.candidateBranch -or
      (Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD') -DetailCode 'deepseek_worktree_invalid') -cne [string]$Run.baseCommit
    ) { Stop-Hourly 'deepseek_worktree_invalid' }
    return $worktree
  }
  [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
  $branchExists = @(& git -C $script:resolvedRepositoryRoot show-ref --verify --quiet "refs/heads/$($Run.candidateBranch)" 2>$null; $LASTEXITCODE)
  if ([int]$branchExists[-1] -eq 0) { Stop-Hourly 'deepseek_candidate_branch_exists' }
  $null = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('worktree', 'add', '-b', [string]$Run.candidateBranch, $worktree, [string]$Run.baseCommit) -DetailCode 'deepseek_worktree_create_failed'
  if (
    (Invoke-GitText -Root $worktree -Arguments @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or
    (Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.baseCommit
  ) { Stop-Hourly 'deepseek_worktree_invalid' }
  $worktree
}

function Write-CandidateResult {
  param([string]$RunId, [object]$CandidateResult)
  $directory = Join-Path $script:effectiveStateRoot 'candidate-results'
  [IO.Directory]::CreateDirectory($directory) | Out-Null
  Set-PrivatePathAcl -Path $directory -Directory
  Assert-PrivatePathAcl -Path $directory -Directory
  $path = Join-Path $directory "$RunId.json"
  [IO.File]::WriteAllText($path, ($CandidateResult | ConvertTo-Json -Compress -Depth 30), [Text.UTF8Encoding]::new($false))
  Set-PrivatePathAcl -Path $path
  Assert-PrivatePathAcl -Path $path
  $path
}

function Set-Attention {
  param([object]$Run, [string]$Reason)
  Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{
    Owner = 'deepseek'; RunId = [string]$Run.runId; RunState = 'attention_required'; RecoveryReason = $Reason
  } | Out-Null
}

function Invoke-Candidate {
  param([object]$Run)
  $worktree = New-CandidateWorktree -Run $Run
  if (
    (Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.baseCommit -or
    -not [string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))
  ) {
    Set-Attention -Run $Run -Reason 'developing worktree contains unverifiable changes'
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'deepseek_unverifiable_developing_worktree' }
  }
  $wrapper = Invoke-JsonTool -ScriptPath $responsibilityPath -Arguments @(
    '-Action', 'Candidate', '-RepositoryRoot', $worktree, '-TaskId', [string]$Run.taskId,
    '-RunId', [string]$Run.runId, '-StateRoot', $script:effectiveStateRoot,
    '-ResponsibilityTimeoutSeconds', [string]$ResponsibilityTimeoutSeconds
  ) -DetailCode 'deepseek_responsibility_failed'
  if ([string]$wrapper.status -cne 'completed') {
    $wrapperDetail = if ($wrapper.PSObject.Properties.Name -contains 'detailCode') { [string]$wrapper.detailCode } else { [string]$wrapper.status }
    Set-Attention -Run $Run -Reason "DeepSeek responsibility ended with $([string]$wrapper.status)/$wrapperDetail"
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; sessionId = $wrapper.sessionId; detailCode = $wrapperDetail }
  }
  $resultPath = Write-CandidateResult -RunId ([string]$Run.runId) -CandidateResult $wrapper.candidateResult
  $updated = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{
    Owner = 'deepseek'; RunId = [string]$Run.runId; RunState = 'candidate_ready';
    SessionKind = 'claude_cli'; SessionId = [string]$wrapper.sessionId;
    CandidateCommit = [string]$wrapper.candidateCommit; CandidateResultPath = $resultPath
  }
  [ordered]@{ status = 'candidate_ready'; run = $updated.run }
}

function Test-PathOverlap {
  param([string]$Left, [string]$Right)
  $leftPath = $Left.Replace('\', '/').TrimEnd('/')
  $rightPath = $Right.Replace('\', '/').TrimEnd('/')
  $leftPath.Equals($rightPath, [StringComparison]::OrdinalIgnoreCase) -or
    $leftPath.StartsWith($rightPath + '/', [StringComparison]::OrdinalIgnoreCase) -or
    $rightPath.StartsWith($leftPath + '/', [StringComparison]::OrdinalIgnoreCase)
}

function Get-ChangedPaths {
  param([string]$Root, [string]$Range)
  @((Invoke-GitText -Root $Root -Arguments @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', $Range)) -split '\r?\n' |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
}

function Test-GitAncestor {
  param([string]$Root, [string]$Ancestor, [string]$Descendant)
  & git -C $Root merge-base --is-ancestor $Ancestor $Descendant 2>$null
  if ($LASTEXITCODE -eq 0) { return $true }
  if ($LASTEXITCODE -eq 1) { return $false }
  Stop-Hourly 'deepseek_git_ancestry_failed'
}

function Invoke-Finalizer {
  param([string]$Worktree, [string[]]$Arguments)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $finalizerPath -RepositoryRoot $Worktree @Arguments 2>&1)
  $lines = @($output | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  $commit = if ($lines.Count -gt 0) { [string]$lines[-1] } else { $null }
  if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40,64}$') {
    Stop-Hourly 'deepseek_formal_commit_failed'
  }
  $commit
}

function Write-Handoff {
  param([object]$Run, [string]$BusinessCommit)
  $path = Join-Path ([string]$Run.worktree) '开发管理\AI合作沟通.txt'
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
  $marker = "### DSH-$($Run.taskId)-$($BusinessCommit.Substring(0, 12))"
  if ($text.Contains($marker, [StringComparison]::Ordinal)) { Stop-Hourly 'deepseek_handoff_duplicate' }
  $candidateResult = $Run.candidateResult
  $entry = @(
    "$marker · DeepSeek 自动交接（⚠️ 未审核）",
    '',
    "- 方向：任务 $($Run.taskId) 的实现结果等待 Codex 独立复审。",
    "- 任务：$($Run.taskId)",
    "- 业务提交：$BusinessCommit",
    "- 修改文件：$(@($candidateResult.changedPaths) -join '、')",
    "- 已验证：$(@($candidateResult.verified) -join '；')",
    "- 未验证：$(@($candidateResult.unverified) -join '；')",
    "- 残留风险：$($candidateResult.residualRisk)",
    '- 请求判断：请 Codex 按审核入口复审实际集成结果。',
    '- 建议下一步：通过则关闭原任务并解锁依赖；不通过则按同一任务卡定向返工。'
  ) -join "`n"
  $text = $text.Replace('# AI合作沟通（✅ 已审核）', '# AI合作沟通（⚠️ 存在待审核交接）')
  if ($text.Contains('当前无有效交接条目。', [StringComparison]::Ordinal)) {
    $text = $text.Replace('当前无有效交接条目。', $entry)
  } else {
    $text = $text.TrimEnd() + "`n`n" + $entry + "`n"
  }
  [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

function Build-Canonical {
  param([object]$Run)
  $worktree = Assert-RunWorktreePath -Run $Run
  if (
    -not (Test-Path -LiteralPath $worktree -PathType Container) -or
    (Invoke-GitText -Root $worktree -Arguments @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or
    (Invoke-GitText -Root $worktree -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.candidateCommit -or
    -not [string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))
  ) {
    Set-Attention -Run $Run -Reason 'candidate evidence does not match the project-owned worktree'
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'deepseek_candidate_evidence_mismatch' }
  }
  $task = Read-TaskMetadata -Root $script:resolvedRepositoryRoot -TaskId ([string]$Run.taskId)
  if (
    [string]$task.Digest -cne [string]$Run.taskCardDigest -or
    [string]$task.Metadata.route -cne 'external_execute' -or [string]$task.Metadata.owner -cne 'deepseek' -or [string]$task.Metadata.dispatchState -cne 'ready'
  ) {
    Set-Attention -Run $Run -Reason 'task facts changed after claim'
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'deepseek_task_changed_after_claim' }
  }
  $currentMaster = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'master')
  if ([string]$Run.baseCommit -cne $currentMaster) {
    $mainChanges = Get-ChangedPaths -Root $script:resolvedRepositoryRoot -Range "$($Run.baseCommit)..$currentMaster"
    $expectedPaths = @($task.Metadata.expectedPaths | ForEach-Object { [string]$_ })
    foreach ($mainPath in $mainChanges) {
      foreach ($expectedPath in $expectedPaths) {
        if (Test-PathOverlap -Left $mainPath -Right $expectedPath) {
          Set-Attention -Run $Run -Reason 'master changed a task-authorized path after claim'
          return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'deepseek_revalidation_required' }
        }
      }
    }
  }
  $canonicalBranch = "codex/automation/deepseek/$($Run.runId)/canonical-$($currentMaster.Substring(0, 12))"
  $branchExistsCode = 0
  & git -C $script:resolvedRepositoryRoot show-ref --verify --quiet "refs/heads/$canonicalBranch" 2>$null
  $branchExistsCode = $LASTEXITCODE
  if ($branchExistsCode -eq 0) {
    Set-Attention -Run $Run -Reason 'canonical branch already exists without matching runtime evidence'
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'deepseek_canonical_evidence_incomplete' }
  }
  $null = Invoke-GitText -Root $worktree -Arguments @('switch', '-c', $canonicalBranch, $currentMaster) -DetailCode 'deepseek_canonical_branch_failed'
  try {
    $null = Invoke-GitText -Root $worktree -Arguments @('cherry-pick', '--no-commit', [string]$Run.candidateCommit) -DetailCode 'deepseek_candidate_replay_failed'
    $transition = Invoke-JsonTool -ScriptPath $transitionPath -Arguments @('-RepositoryRoot', $worktree, '-TaskId', [string]$Run.taskId) -DetailCode 'deepseek_pending_review_projection_failed'
    if ([string]$transition.status -cne 'updated') { Stop-Hourly 'deepseek_pending_review_projection_failed' }
    $expectedPathsText = @($task.Metadata.expectedPaths | ForEach-Object { [string]$_ }) -join '|'
    $candidateResult = $Run.candidateResult
    $businessCommit = Invoke-Finalizer -Worktree $worktree -Arguments @(
      '-ExpectedPaths', $expectedPathsText, '-CommitMessage', "feat($($Run.taskId)): complete DeepSeek task",
      '-RequireAutomationMetadata', '-AutomationTask', [string]$Run.taskId, '-AutomationState', 'pending_review',
      '-AutomationResult', [string]$candidateResult.result, '-AutomationImpact', [string]$candidateResult.impact,
      '-AutomationVerify', [string]$candidateResult.verify, '-AutomationPlain', [string]$candidateResult.plain
    )
    Write-Handoff -Run $Run -BusinessCommit $businessCommit
    $handoffCommit = Invoke-Finalizer -Worktree $worktree -Arguments @(
      '-ExpectedPaths', '开发管理/AI合作沟通.txt', '-CommitMessage', "handoff($($Run.taskId)): register DeepSeek result for Codex review"
    )
    if ((Invoke-GitText -Root $worktree -Arguments @('rev-parse', "$handoffCommit^")) -cne $businessCommit) { Stop-Hourly 'deepseek_formal_parent_chain_invalid' }
    $evidence = Invoke-JsonTool -ScriptPath $checkerPath -Arguments @('-RepositoryRoot', $worktree, '-TaskId', [string]$Run.taskId, '-Postcondition', 'ExternalPendingReview', '-OutputJson') -DetailCode 'deepseek_formal_projection_invalid'
    if ([string]$evidence.status -cne 'ok') { Stop-Hourly 'deepseek_formal_projection_invalid' }
    if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'deepseek_canonical_worktree_dirty' }
    $updated = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{
      Owner = 'deepseek'; RunId = [string]$Run.runId; RunState = 'canonical_ready'; CanonicalBranch = $canonicalBranch;
      CanonicalBase = $currentMaster; CanonicalHead = $handoffCommit
    }
    [ordered]@{ status = 'canonical_ready'; run = $updated.run; businessCommit = $businessCommit; handoffCommit = $handoffCommit }
  } catch {
    Set-Attention -Run $Run -Reason "canonical build failed: $($_.Exception.Message)"
    [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'deepseek_canonical_failed' } }
  }
}

function Get-StatusPaths {
  param([string]$Root)
  $paths = [Collections.Generic.List[string]]::new()
  foreach ($line in @((Invoke-GitText -Root $Root -Arguments @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')) -split '\r?\n')) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) { continue }
    $path = $line.Substring(3)
    $arrow = $path.LastIndexOf(' -> ', [StringComparison]::Ordinal)
    if ($arrow -ge 0) { $path = $path.Substring($arrow + 4) }
    $paths.Add($path.Replace('\', '/'))
  }
  @($paths)
}

function Integrate-Canonical {
  param([object]$Run)
  if ((Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('branch', '--show-current')) -cne 'master') {
    return [ordered]@{ status = 'integration_deferred'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'main_branch_not_master' }
  }
  $mainHead = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'HEAD')
  if ($mainHead -ceq [string]$Run.canonicalHead -or (Test-GitAncestor -Root $script:resolvedRepositoryRoot -Ancestor ([string]$Run.canonicalHead) -Descendant $mainHead)) {
    $evidence = Invoke-JsonTool -ScriptPath $checkerPath -Arguments @('-RepositoryRoot', $script:resolvedRepositoryRoot, '-TaskId', [string]$Run.taskId, '-Postcondition', 'ExternalPendingReview', '-OutputJson') -DetailCode 'deepseek_integrated_projection_invalid'
    if ([string]$evidence.status -cne 'ok') { Stop-Hourly 'deepseek_integrated_projection_invalid' }
    $null = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = 'deepseek'; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = [string]$Run.canonicalHead }
    $closed = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{ Owner = 'deepseek'; RunId = [string]$Run.runId; CompletionCategory = 'success'; DetailCode = 'integrated_recovery' }
    $businessCommit = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', "$($Run.canonicalHead)^")
    $delivery = Invoke-BestEffortNotification -Run $Run -BusinessCommit $businessCommit
    return [ordered]@{ status = 'completed'; category = 'success'; taskId = $Run.taskId; runId = $Run.runId; businessCommit = $businessCommit; canonicalHead = $Run.canonicalHead; detailCode = $closed.detailCode; notification = $delivery }
  }
  if ($mainHead -cne [string]$Run.canonicalBase) {
    $worktree = Assert-RunWorktreePath -Run $Run
    if ([string]::IsNullOrWhiteSpace((Invoke-GitText -Root $worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))) {
      $null = Invoke-GitText -Root $worktree -Arguments @('switch', [string]$Run.candidateBranch) -DetailCode 'deepseek_candidate_restore_failed'
      $null = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = 'deepseek'; RunId = [string]$Run.runId; RunState = 'candidate_ready' }
      return [ordered]@{ status = 'rebuild_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'master_advanced' }
    }
    Set-Attention -Run $Run -Reason 'canonical worktree is dirty after master advanced'
    return [ordered]@{ status = 'attention_required'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'deepseek_canonical_dirty' }
  }
  $formalPaths = Get-ChangedPaths -Root $script:resolvedRepositoryRoot -Range "$($Run.canonicalBase)..$($Run.canonicalHead)"
  foreach ($dirtyPath in Get-StatusPaths -Root $script:resolvedRepositoryRoot) {
    foreach ($formalPath in $formalPaths) {
      if (Test-PathOverlap -Left $dirtyPath -Right $formalPath) {
        return [ordered]@{ status = 'integration_deferred'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'main_path_conflict' }
      }
    }
  }
  $lease = Invoke-Runtime -RuntimeAction AcquireIntegration -Parameters @{
    Owner = 'deepseek'; RunId = [string]$Run.runId; ExpectedMainHead = $mainHead; IntegrationLeaseSeconds = 300
  }
  if ([string]$lease.status -cin @('INTEGRATION_BUSY')) {
    return [ordered]@{ status = 'occupied'; taskId = $Run.taskId; runId = $Run.runId; detailCode = 'integration_busy' }
  }
  if ([string]$lease.status -cnotin @('INTEGRATION_ACQUIRED', 'ALREADY_ACQUIRED')) { Stop-Hourly 'deepseek_integration_lease_failed' }
  try {
    if (
      (Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('branch', '--show-current')) -cne 'master' -or
      (Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.canonicalBase
    ) { Stop-Hourly 'deepseek_integration_precondition_changed' }
    $task = Read-TaskMetadata -Root ([string]$Run.worktree) -TaskId ([string]$Run.taskId)
    if ([string]$task.Metadata.route -cne 'codex_review' -or [string]$task.Metadata.owner -cne 'codex' -or [string]$task.Metadata.dispatchState -cne 'ready') {
      Stop-Hourly 'deepseek_formal_projection_invalid'
    }
    $null = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('merge', '--ff-only', [string]$Run.canonicalHead) -DetailCode 'deepseek_fast_forward_failed'
    if ((Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'HEAD')) -cne [string]$Run.canonicalHead) { Stop-Hourly 'deepseek_fast_forward_verification_failed' }
    $evidence = Invoke-JsonTool -ScriptPath $checkerPath -Arguments @('-RepositoryRoot', $script:resolvedRepositoryRoot, '-TaskId', [string]$Run.taskId, '-Postcondition', 'ExternalPendingReview', '-OutputJson') -DetailCode 'deepseek_integrated_projection_invalid'
    if ([string]$evidence.status -cne 'ok') { Stop-Hourly 'deepseek_integrated_projection_invalid' }
    $null = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = 'deepseek'; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = [string]$Run.canonicalHead }
    $closed = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{
      Owner = 'deepseek'; RunId = [string]$Run.runId; CompletionCategory = 'success'; DetailCode = "commit_$(([string]$Run.canonicalHead).Substring(0, 12))"
    }
    $businessCommit = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', "$($Run.canonicalHead)^")
    $delivery = Invoke-BestEffortNotification -Run $Run -BusinessCommit $businessCommit
    [ordered]@{ status = 'completed'; category = 'success'; taskId = $Run.taskId; runId = $Run.runId; businessCommit = $businessCommit; canonicalHead = $Run.canonicalHead; detailCode = $closed.detailCode; notification = $delivery }
  } catch {
    $currentHead = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'HEAD')
    if ($currentHead -cne [string]$Run.canonicalHead) {
      $release = Invoke-Runtime -RuntimeAction ReleaseIntegration -Parameters @{ RunId = [string]$Run.runId } -AllowedExitCodes @(0, 2)
      $null = $release
    }
    throw
  }
}

function Invoke-Canary {
  $beforeHead = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'HEAD')
  $beforeStatus = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')
  $canaryId = "canary-$([Guid]::NewGuid().ToString('N'))"
  $canaryWorktree = Normalize-FullPath (Join-Path $script:resolvedRepositoryRoot ".worktrees\automation\$canaryId\deepseek")
  $canaryBranch = "codex/automation/deepseek/$canaryId"
  $success = $false
  [IO.Directory]::CreateDirectory((Split-Path -Parent $canaryWorktree)) | Out-Null
  $null = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('worktree', 'add', '-b', $canaryBranch, $canaryWorktree, $beforeHead) -DetailCode 'deepseek_canary_worktree_failed'
  try {
    $privateShow = Invoke-Runtime -RuntimeAction Show
    if ([string]$privateShow.status -cne 'OK' -or $privateShow.activeTaskIds.Count -ne 0) { Stop-Hourly 'deepseek_canary_private_state_failed' }
    $pwshCheck = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $canaryWorktree 'tools\check-pwsh-runtime.ps1') `
      -RepositoryRoot $canaryWorktree `
      -DocumentPaths 'AGENTS.md|CLAUDE.md' `
      -ScriptPaths 'tools/check-pwsh-runtime.ps1|tools/hourly-automation-lease.ps1' `
      -RequiredVersionPaths 'tools/hourly-automation-lease.ps1' 2>$null)
    if ($LASTEXITCODE -ne 0) { Stop-Hourly 'deepseek_canary_project_check_failed' }
    $wrapper = Invoke-JsonTool -ScriptPath $responsibilityPath -Arguments @('-Action', 'Canary', '-RepositoryRoot', $canaryWorktree, '-StateRoot', $script:effectiveStateRoot, '-ResponsibilityTimeoutSeconds', [string]$ResponsibilityTimeoutSeconds) -DetailCode 'deepseek_canary_failed'
    if ([string]$wrapper.status -cne 'verified') { Stop-Hourly 'deepseek_canary_failed' }
    if (
      (Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'HEAD')) -cne $beforeHead -or
      (Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')) -cne $beforeStatus -or
      -not [string]::IsNullOrWhiteSpace((Invoke-GitText -Root $canaryWorktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')))
    ) { Stop-Hourly 'deepseek_canary_isolation_failed' }
    $success = $true
    [ordered]@{
      status = 'verified'; identity = $wrapper.identity; model = $wrapper.model; providerEndpointCategory = $wrapper.providerEndpointCategory
      pwshMajor = $wrapper.pwshMajor; git = $wrapper.git; privateState = 'isolated'; worktree = $canaryWorktree; mainHead = $beforeHead
    }
  } finally {
    if ($success) {
      $null = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('worktree', 'remove', $canaryWorktree) -DetailCode 'deepseek_canary_cleanup_failed'
      $null = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('branch', '-D', $canaryBranch) -DetailCode 'deepseek_canary_cleanup_failed'
      $canaryParent = Split-Path -Parent $canaryWorktree
      if ((Test-Path -LiteralPath $canaryParent -PathType Container) -and @(Get-ChildItem -LiteralPath $canaryParent -Force).Count -eq 0) {
        Remove-Item -LiteralPath $canaryParent -Force
      }
      if (-not $stateRootWasBound -and (Test-Path -LiteralPath $script:effectiveStateRoot -PathType Container)) {
        $approvedCanaryRoot = Normalize-FullPath (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-deepseek-hourly-canary')
        if (-not (Test-PathWithin -Path $script:effectiveStateRoot -Root $approvedCanaryRoot)) { Stop-Hourly 'deepseek_canary_cleanup_failed' }
        Remove-Item -LiteralPath $script:effectiveStateRoot -Recurse -Force
      }
    }
  }
}

$finalResult = $null
$invocationMutex = $null
$invocationMutexHeld = $false
try {
  foreach ($path in @($runtimePath, $selectorPath, $responsibilityPath, $transitionPath, $finalizerPath, $checkerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-Hourly 'deepseek_dependency_missing' }
  }
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { Stop-Hourly 'deepseek_repository_invalid' }
  $script:resolvedRepositoryRoot = Normalize-FullPath (Resolve-Path -LiteralPath $RepositoryRoot).Path
  if (-not (Test-Path -LiteralPath (Join-Path $script:resolvedRepositoryRoot '.git'))) { Stop-Hourly 'deepseek_repository_invalid' }
  if ((Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', '--is-inside-work-tree')) -cne 'true') { Stop-Hourly 'deepseek_repository_invalid' }

  $stateRootWasBound = $PSBoundParameters.ContainsKey('StateRoot')
  $script:effectiveStateRoot = if ($Action -ceq 'Canary' -and -not $stateRootWasBound) {
    Join-Path $env:USERPROFILE ".codex\automation-state\tzg-deepseek-hourly-canary\$([Guid]::NewGuid().ToString('N'))"
  } else {
    Normalize-FullPath $StateRoot
  }

  $invocationMutex = [Threading.Mutex]::new($false, (Get-InvocationMutexName -Owner 'deepseek' -Root $script:effectiveStateRoot))
  try {
    $invocationMutexHeld = $invocationMutex.WaitOne(0)
  } catch [Threading.AbandonedMutexException] {
    $invocationMutexHeld = $true
  }

  if (-not $invocationMutexHeld) {
    $finalResult = [ordered]@{ status = 'occupied'; owner = 'deepseek'; detailCode = 'deepseek_entry_running' }
  } elseif ($Action -ceq 'Canary') {
    $finalResult = Invoke-Canary
  } else {
    $shown = Invoke-Runtime -RuntimeAction Show
    if ([string]$shown.status -cne 'OK') { Stop-Hourly 'deepseek_runtime_unavailable' }
    $run = $shown.state.runs.deepseek
    if ($null -eq $run) {
      $selection = Invoke-JsonTool -ScriptPath $selectorPath -Arguments @('-RepositoryRoot', $script:resolvedRepositoryRoot, '-Owner', 'deepseek') -DetailCode 'deepseek_selection_failed'
      if ([string]$selection.status -ceq 'no_candidate') {
        $finalResult = [ordered]@{ status = 'no_candidate'; owner = 'deepseek'; detailCode = 'no_runnable_candidate' }
      } elseif ([string]$selection.status -ceq 'selected') {
        $mainHead = Invoke-GitText -Root $script:resolvedRepositoryRoot -Arguments @('rev-parse', 'master')
        $claim = Invoke-Runtime -RuntimeAction ClaimRun -Parameters @{
          Owner = 'deepseek'; TaskId = [string]$selection.taskId; Route = 'external_execute'; RepositoryRoot = $script:resolvedRepositoryRoot;
          MainBranch = 'master'; BaseCommit = $mainHead; TaskCardDigest = [string]$selection.taskCardDigest
        }
        if ([string]$claim.status -cne 'CLAIMED') {
          $finalResult = [ordered]@{ status = 'occupied'; owner = 'deepseek'; detailCode = [string]$claim.status }
        } else {
          $run = $claim.run
        }
      } else { Stop-Hourly 'deepseek_selection_failed' }
    }
    if ($null -eq $finalResult -and $null -ne $run) {
      if ([string]$run.state -ceq 'attention_required') {
        $finalResult = [ordered]@{ status = 'attention_required'; taskId = $run.taskId; runId = $run.runId; detailCode = $run.recoveryReason }
      } elseif ([string]$run.state -ceq 'developing') {
        $candidate = Invoke-Candidate -Run $run
        if ([string]$candidate.status -cne 'candidate_ready') { $finalResult = $candidate } else { $run = $candidate.run }
      }
      if ($null -eq $finalResult -and [string]$run.state -ceq 'candidate_ready') {
        $canonical = Build-Canonical -Run $run
        if ([string]$canonical.status -cne 'canonical_ready') { $finalResult = $canonical } else { $run = $canonical.run }
      }
      if ($null -eq $finalResult -and [string]$run.state -ceq 'canonical_ready') {
        $finalResult = Integrate-Canonical -Run $run
      }
      if ($null -eq $finalResult -and [string]$run.state -ceq 'integrated') {
        $closed = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{ Owner = 'deepseek'; RunId = [string]$run.runId; CompletionCategory = 'success'; DetailCode = 'integrated_recovery' }
        $finalResult = [ordered]@{ status = 'completed'; taskId = $run.taskId; runId = $run.runId; canonicalHead = $run.canonicalHead; detailCode = $closed.detailCode }
      }
    }
  }
} catch {
  $detailCode = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'deepseek_hourly_failed' }
  $finalResult = [ordered]@{ status = 'failed'; owner = 'deepseek'; detailCode = $detailCode }
} finally {
  if ($invocationMutexHeld) {
    $invocationMutex.ReleaseMutex()
  }
  if ($null -ne $invocationMutex) {
    $invocationMutex.Dispose()
  }
}

[Console]::Out.WriteLine(($finalResult | ConvertTo-Json -Compress -Depth 40))
