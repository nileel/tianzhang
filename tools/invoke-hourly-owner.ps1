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
$whitespacePath = Join-Path $PSScriptRoot 'check-pending-whitespace.ps1'
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

function Get-NormalizedTextDigestFromText {
  param([string]$Text)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($Text.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
}

function Get-NormalizedTextDigest {
  param([string]$Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  Get-NormalizedTextDigestFromText $text
}

function Assert-AutomationInputPath {
  param([string]$Path)
  $invalid = [string]::IsNullOrWhiteSpace($Path) -or
    $Path -cne $Path.Trim() -or
    [IO.Path]::IsPathRooted($Path) -or
    $Path.Contains('\') -or
    $Path -match '[*?\[\]]' -or
    $Path.EndsWith('/') -or
    (@(($Path -split '/') | Where-Object { $_ -cin @('.', '..') }).Count -gt 0) -or
    [string]::IsNullOrEmpty([IO.Path]::GetExtension($Path)) -or
    -not $Path.StartsWith('assets/source/', [StringComparison]::Ordinal)
  if ($invalid) { Stop-Hourly 'hourly_task_input_validation_failed' }
}

function Get-TaskAutomationInputs {
  param([object]$Metadata)
  if ($Metadata.PSObject.Properties.Name -cnotcontains 'automationInputs') { return @() }
  if ([string]$Metadata.route -cne 'codex_execute' -or [string]$Metadata.owner -cne 'codex' -or
    -not (($Metadata.automationInputs -is [System.Collections.IEnumerable]) -and -not ($Metadata.automationInputs -is [string]))) {
    Stop-Hourly 'hourly_task_input_validation_failed'
  }
  $inputs = @($Metadata.automationInputs)
  if ($inputs.Count -eq 0) { Stop-Hourly 'hourly_task_input_validation_failed' }
  $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $validated = @()
  foreach ($input in $inputs) {
    $names = if ($null -eq $input) { @() } else { @($input.PSObject.Properties.Name) }
    if ($null -eq $input -or $names.Count -ne 3 -or
      @($names | Where-Object { $_ -cnotin @('path', 'bytes', 'sha256') }).Count -ne 0 -or
      @(@('path', 'bytes', 'sha256') | Where-Object { $names -cnotcontains $_ }).Count -ne 0) {
      Stop-Hourly 'hourly_task_input_validation_failed'
    }
    $path = [string]$input.path
    Assert-AutomationInputPath $path
    if (-not $paths.Add($path)) { Stop-Hourly 'hourly_task_input_validation_failed' }
    $rawBytes = $input.bytes
    $isIntegral = $rawBytes -is [byte] -or $rawBytes -is [sbyte] -or $rawBytes -is [int16] -or $rawBytes -is [uint16] -or
      $rawBytes -is [int32] -or $rawBytes -is [uint32] -or $rawBytes -is [int64] -or $rawBytes -is [uint64]
    if (-not $isIntegral) { Stop-Hourly 'hourly_task_input_validation_failed' }
    try { $bytes = [Convert]::ToInt64($rawBytes) } catch { Stop-Hourly 'hourly_task_input_validation_failed' }
    $hash = [string]$input.sha256
    if ($bytes -le 0 -or $hash -cnotmatch '^[0-9A-Fa-f]{64}$') { Stop-Hourly 'hourly_task_input_validation_failed' }
    $validated += [pscustomobject]@{ Path = $path; Bytes = $bytes; Sha256 = $hash.ToUpperInvariant() }
  }
  @($validated)
}

function Get-AutomationInputFileEvidence {
  param([string]$Root, [pscustomobject]$ContractInput)
  $rootPath = Normalize-FullPath $Root
  $relative = $ContractInput.Path.Replace('/', [IO.Path]::DirectorySeparatorChar)
  $path = [IO.Path]::GetFullPath((Join-Path $rootPath $relative))
  if (-not $path.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Stop-Hourly 'hourly_task_input_validation_failed' }
  $current = $rootPath
  $rootItem = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
  if ($null -eq $rootItem -or (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { Stop-Hourly 'hourly_task_input_validation_failed' }
  foreach ($part in @($relative -split [regex]::Escape([string][IO.Path]::DirectorySeparatorChar))) {
    $current = Join-Path $current $part
    $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
    if ($null -eq $item -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { Stop-Hourly 'hourly_task_input_validation_failed' }
  }
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-Hourly 'hourly_task_input_validation_failed' }
  $file = Get-Item -LiteralPath $path -Force
  if ($file.Length -ne $ContractInput.Bytes -or ([string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash).ToUpperInvariant() -cne $ContractInput.Sha256) { Stop-Hourly 'hourly_task_input_validation_failed' }
  [pscustomobject]@{ Path = $path; Length = [int64]$file.Length }
}

function New-AutomationInputDestination {
  param([string]$Worktree, [pscustomobject]$ContractInput)
  $rootPath = Normalize-FullPath $Worktree
  $relative = $ContractInput.Path.Replace('/', [IO.Path]::DirectorySeparatorChar)
  $path = [IO.Path]::GetFullPath((Join-Path $rootPath $relative))
  if (-not $path.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Stop-Hourly 'hourly_task_input_validation_failed' }
  $current = $rootPath
  $rootItem = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
  if ($null -eq $rootItem -or (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { Stop-Hourly 'hourly_task_input_validation_failed' }
  $parts = @($relative -split [regex]::Escape([string][IO.Path]::DirectorySeparatorChar))
  for ($index = 0; $index -lt $parts.Count - 1; $index++) {
    $current = Join-Path $current $parts[$index]
    $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
    if ($null -ne $item) {
      if (-not $item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { Stop-Hourly 'hourly_task_input_validation_failed' }
    } else {
      [IO.Directory]::CreateDirectory($current) | Out-Null
    }
  }
  if ($null -ne (Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue)) { Stop-Hourly 'hourly_task_input_validation_failed' }
  $path
}

function Assert-MaterializedAutomationInputs {
  param([string]$Worktree, [object]$Metadata)
  foreach ($contractInput in @(Get-TaskAutomationInputs $Metadata)) {
    $null = Get-AutomationInputFileEvidence -Root $script:root -ContractInput $contractInput
    $copy = Get-AutomationInputFileEvidence -Root $Worktree -ContractInput $contractInput
    $item = Get-Item -LiteralPath $copy.Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReadOnly) -eq 0) { Stop-Hourly 'hourly_task_input_validation_failed' }
  }
}

function Materialize-TaskAutomationInputs {
  param([object]$Run, [string]$Worktree)
  if ([string]$Run.route -ceq 'queue_maintenance') { return }
  $task = Read-RunTaskMetadata $Run
  $inputs = @(Get-TaskAutomationInputs $task.Metadata)
  foreach ($contractInput in $inputs) { $null = Get-AutomationInputFileEvidence -Root $script:root -ContractInput $contractInput }
  foreach ($contractInput in $inputs) {
    $source = Get-AutomationInputFileEvidence -Root $script:root -ContractInput $contractInput
    $destination = New-AutomationInputDestination -Worktree $Worktree -ContractInput $contractInput
    try {
      [IO.File]::Copy($source.Path, $destination, $false)
      $target = Get-Item -LiteralPath $destination -Force
      $target.Attributes = $target.Attributes -bor [IO.FileAttributes]::ReadOnly
    } catch { Stop-Hourly 'hourly_task_input_validation_failed' }
    $null = Get-AutomationInputFileEvidence -Root $Worktree -ContractInput $contractInput
    $null = Get-AutomationInputFileEvidence -Root $script:root -ContractInput $contractInput
  }
  Assert-MaterializedAutomationInputs -Worktree $Worktree -Metadata $task.Metadata
}

function Read-TaskMetadata {
  param([string]$Root, [string]$TaskId)
  $path = Join-Path $Root "开发管理/任务卡/$TaskId.txt"
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
  $match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
  if (-not $match.Success) { Stop-Hourly 'hourly_task_invalid' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly 'hourly_task_invalid' }
  [pscustomobject]@{ Path = $path; Metadata = $metadata; Digest = Get-NormalizedTextDigestFromText $text }
}

function Read-RunTaskMetadata {
  param([object]$Run)
  $task = Read-TaskMetadata $script:root ([string]$Run.taskId)
  if ([string]$task.Digest -cne [string]$Run.taskCardDigest -or
    [string]$task.Metadata.route -cne [string]$Run.route -or
    [string]$task.Metadata.owner -cne $Owner -or
    [string]$task.Metadata.dispatchState -cne 'ready') {
    Stop-Hourly 'hourly_task_changed_after_claim'
  }
  $task
}

function Read-TaskMetadataAtCommit {
  param([string]$Root, [string]$Commit, [string]$TaskId)
  $path = "开发管理/任务卡/$TaskId.txt"
  $text = Invoke-GitText $Root @('show', "$Commit`:$path") 'hourly_task_input_validation_failed'
  $match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
  if (-not $match.Success) { Stop-Hourly 'hourly_task_input_validation_failed' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-Hourly 'hourly_task_input_validation_failed' }
  [pscustomobject]@{ Metadata = $metadata }
}

function Get-TaskContextDigest {
  param([object]$Metadata)
  $context = [ordered]@{
    id = [string]$Metadata.id; title = [string]$Metadata.title; priority = [string]$Metadata.priority
    route = [string]$Metadata.route; owner = [string]$Metadata.owner; domain = [string]$Metadata.domain; stage = [string]$Metadata.stage
    blockedBy = @($Metadata.blockedBy | ForEach-Object { [string]$_ }); expectedPaths = @($Metadata.expectedPaths | ForEach-Object { [string]$_ })
    automationInputs = @(Get-TaskAutomationInputs $Metadata | ForEach-Object { [ordered]@{ path = $_.Path; bytes = $_.Bytes; sha256 = $_.Sha256 } })
    sourceBacklog = [string]$Metadata.sourceBacklog
  }
  $json = $context | ConvertTo-Json -Compress -Depth 20
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
}

function Get-MaintenanceDecisionId {
  param([string]$TaskId, [string]$BaseCommit, [string]$TaskContextDigest)
  $inputText = "$TaskId$([char]0)$BaseCommit$([char]0)$TaskContextDigest"
  $hex = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($inputText)))
  "DEC-$([DateTimeOffset]::Now.ToString('yyyyMMdd'))-QM$($hex.Substring(0, 12))"
}

function Get-QueueTaskIndex {
  param([string]$Root, [string]$TaskId)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes((Join-Path $Root '开发管理\当前任务队列.txt'))).TrimStart([char]0xFEFF)
  $ids = [Collections.Generic.List[string]]::new()
  $inTable = $false
  foreach ($line in @($text -split '\r?\n')) {
    if (-not $line.Trim().StartsWith('|')) {
      if ($inTable -and $ids.Count -gt 0) { break }
      continue
    }
    $cells = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim().Trim([char]96) })
    if (-not $inTable) {
      if (($cells -join "`0") -ceq (@('ID', '路由', '主责', '优先级', '领域', '阶段', '标题', '任务卡') -join "`0")) { $inTable = $true }
      continue
    }
    if ($cells.Count -ne 8 -or $cells[0] -match '^-+$') { continue }
    $ids.Add([string]$cells[0])
  }
  $matches = @(for ($index = 0; $index -lt $ids.Count; $index++) { if ($ids[$index] -ceq $TaskId) { $index } })
  if ($matches.Count -gt 1) { Stop-Hourly 'hourly_queue_duplicate' }
  if ($matches.Count -eq 0) { return -1 }
  [int]$matches[0]
}

function Get-ReviewEntryEvidence {
  param([string]$Root, [string]$TaskId, [string]$ExpectedReviewedCommit)
  if ($ExpectedReviewedCommit -cnotmatch '^[0-9a-f]{40,64}$') { Stop-Hourly 'review_rework_entry_invalid' }
  $path = Join-Path $Root '开发管理\未通过审核清单.txt'
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-Hourly 'review_rework_entry_missing' }
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF).Replace("`r`n", "`n").Replace("`r", "`n")
  $pattern = '(?ms)^###\s+' + [regex]::Escape($TaskId) + '\s+·[^\n]*\n.*?(?=^###\s+|^##\s+|\z)'
  $matches = [regex]::Matches($text, $pattern)
  if ($matches.Count -ne 1) { Stop-Hourly 'review_rework_entry_invalid' }
  $entry = $matches[0].Value.TrimEnd()
  $commitMatches = @([regex]::Matches($entry, '审核对象：正式提交 `(?<sha>[0-9a-f]{40,64})`；结论：(?:不通过|部分通过)') | Where-Object { [string]$_.Groups['sha'].Value -ceq $ExpectedReviewedCommit })
  if ($commitMatches.Count -ne 1) { Stop-Hourly 'review_rework_entry_invalid' }
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($entry))).ToLowerInvariant()
  [pscustomobject]@{ Path = '开发管理/未通过审核清单.txt'; Digest = $digest; ReviewedCommit = $ExpectedReviewedCommit }
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
  } else {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
    & git -C $script:root show-ref --verify --quiet "refs/heads/$($Run.candidateBranch)" 2>$null
    if ($LASTEXITCODE -eq 0) { Stop-Hourly 'hourly_candidate_branch_exists' }
    $null = Invoke-GitText $script:root @('worktree', 'add', '-b', [string]$Run.candidateBranch, $worktree, [string]$Run.baseCommit) 'hourly_worktree_create_failed'
  }
  Materialize-TaskAutomationInputs -Run $Run -Worktree $worktree
  $worktree
}

function Set-Attention {
  param([object]$Run, [string]$Reason)
  try { Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'attention_required'; RecoveryReason = $Reason } | Out-Null } catch {}
}

function Assert-CandidateEvidence {
  param([object]$Run, [object]$Candidate)
  $worktree = Assert-WorktreePath $Run
  if ([string]$Candidate.status -cnotin @('completed', 'maintenance_decision') -or [string]$Candidate.candidateCommit -cnotmatch '^[0-9a-f]{40,64}$' -or
    (Invoke-GitText $worktree @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or
    (Invoke-GitText $worktree @('rev-parse', 'HEAD')) -cne [string]$Candidate.candidateCommit -or
    (Invoke-GitText $worktree @('rev-list', '--count', "$($Run.baseCommit)..$($Candidate.candidateCommit)")) -cne '1' -or
    (Invoke-GitText $worktree @('rev-parse', "$($Candidate.candidateCommit)^")) -cne [string]$Run.baseCommit -or
    -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'hourly_candidate_evidence_invalid' }
  $actual = @(Get-ChangedPaths $worktree "$($Run.baseCommit)..$($Candidate.candidateCommit)")
  $reported = @($Candidate.candidateResult.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  if ($actual.Count -eq 0 -or ($actual -join "`0") -cne ($reported -join "`0")) { Stop-Hourly 'hourly_candidate_evidence_invalid' }
  $task = if ([string]$Run.route -ceq 'queue_maintenance') { $null } else { Read-RunTaskMetadata $Run }
  $allowed = if ($null -eq $task) { $actual } else { @($task.Metadata.expectedPaths | ForEach-Object { [string]$_ }) }
  foreach ($path in $actual) { if ($allowed -cnotcontains $path) { Stop-Hourly 'hourly_candidate_path_violation' } }
  if ($null -ne $task) { Assert-MaterializedAutomationInputs -Worktree $worktree -Metadata $task.Metadata }
}

function Get-FormalPaths {
  param([object]$Run, [object]$Task, [AllowNull()][object]$MaintenanceTask)
  if ([string]$Run.route -ceq 'queue_maintenance') {
    $paths = @($Run.candidateResult.changedPaths | ForEach-Object { [string]$_ })
    if ($null -ne $MaintenanceTask) { $paths += @("开发管理/任务卡/$($MaintenanceTask.Metadata.id).txt", '开发管理/当前任务队列.txt', [string]$MaintenanceTask.Metadata.sourceBacklog) }
    return @($paths | Sort-Object -Unique)
  }
  $paths = @($Task.Metadata.expectedPaths | ForEach-Object { [string]$_ })
  if ($Owner -ceq 'deepseek' -and $paths -cnotcontains '开发管理/AI合作沟通.txt') { $paths += '开发管理/AI合作沟通.txt' }
  @($paths | Sort-Object -Unique)
}

function Invoke-Finalizer {
  param([string]$Worktree, [hashtable]$Parameters)
  try {
    $output = @(& $finalizerPath -RepositoryRoot $Worktree @Parameters *>&1)
    $invocationSucceeded = $?
  } catch {
    Stop-Hourly 'hourly_formal_commit_failed'
  }
  $commit = if ($output.Count) { [string]$output[-1] } else { $null }
  if (-not $invocationSucceeded -or $commit -cnotmatch '^[0-9a-f]{40,64}$') { Stop-Hourly 'hourly_formal_commit_failed' }
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
  if ([string]$Run.candidateResult.expectedTransition -ceq 'maintenance_pending_decision') {
    $evidence = Invoke-JsonTool $checkerPath @('-RepositoryRoot', $Worktree, '-TaskId', [string]$Run.candidateResult.decisionTaskId, '-Postcondition', 'MaintenancePendingDecision', '-OutputJson') 'hourly_postcondition_failed'
    if ([string]$evidence.taskState -cne 'pending_decision') { Stop-Hourly 'hourly_postcondition_failed' }
  } elseif ([string]$Run.candidateResult.expectedTransition -ceq 'maintenance_resolution') {
    $postcondition = if ([string]$Run.candidateResult.resolutionState -ceq 'ready') { 'MaintenanceResolvedReady' } else { 'MaintenanceResolvedBlocked' }
    $evidence = Invoke-JsonTool $checkerPath @('-RepositoryRoot', $Worktree, '-TaskId', [string]$Run.candidateResult.decisionTaskId, '-Postcondition', $postcondition, '-OutputJson') 'hourly_postcondition_failed'
    if ([string]$evidence.taskState -cne [string]$Run.candidateResult.resolutionState) { Stop-Hourly 'hourly_postcondition_failed' }
  } elseif ([string]$Run.route -ceq 'queue_maintenance') {
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
    try { $null = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $whitespacePath -ExpectedPaths $expected 2>&1) } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { Stop-Hourly 'hourly_whitespace_failed' }
  }
  $null = Invoke-GitText $Worktree @('diff', '--check', "$Base..$Head") 'hourly_diff_check_failed'
  Assert-Postcondition -Run $Run -Worktree $Worktree
  if (@($changed | Where-Object { $_ -match '^(docs/|src/Assets/(?:Resources|StreamingAssets)/|.+\.(?:csv|json)$)' }).Count -gt 0) {
    $dataChainExitCode = 1
    Push-Location -LiteralPath $Worktree
    try {
      $null = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Worktree 'tools\check-data-chain.ps1') 2>&1)
      $dataChainExitCode = $LASTEXITCODE
    } finally { Pop-Location }
    if ($dataChainExitCode -ne 0) { Stop-Hourly 'hourly_data_chain_failed' }
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
  $sourceHead = $null
  $reviewedCommit = $null
  $reviewQueueIndex = -1
  try {
    $worktree = Assert-WorktreePath $Run
    if ((Invoke-GitText $worktree @('branch', '--show-current')) -cne [string]$Run.candidateBranch -or (Invoke-GitText $worktree @('rev-parse', 'HEAD')) -cne [string]$Run.candidateCommit -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Hourly 'hourly_candidate_evidence_invalid' }
    $task = if ([string]$Run.route -ceq 'queue_maintenance') { $null } else { Read-TaskMetadata $script:root ([string]$Run.taskId) }
    if ($null -ne $task -and ([string]$task.Digest -cne [string]$Run.taskCardDigest -or [string]$task.Metadata.route -cne [string]$Run.route -or [string]$task.Metadata.owner -cne $Owner -or [string]$task.Metadata.dispatchState -cne 'ready')) { Stop-Hourly 'hourly_task_changed_after_claim' }
    if ($null -eq $task -and (Get-NormalizedTextDigest (Join-Path $script:root '开发管理\当前任务队列.txt')) -cne [string]$Run.taskCardDigest) { Stop-Hourly 'hourly_queue_changed_after_claim' }
    if ($Owner -ceq 'codex' -and [string]$Run.route -ceq 'codex_review') {
      $reviewedCommit = Invoke-GitText $script:root @('log', '-1', '--format=%H', '--', "开发管理/任务卡/$($Run.taskId).txt") 'review_rework_reviewed_commit_missing'
      $reviewQueueIndex = Get-QueueTaskIndex -Root $script:root -TaskId ([string]$Run.taskId)
      if ($reviewedCommit -cnotmatch '^[0-9a-f]{40,64}$' -or $reviewQueueIndex -lt 0) { Stop-Hourly 'review_rework_source_invalid' }
    }
    $latest = Invoke-GitText $script:root @('rev-parse', 'master')
    $maintenanceTaskId = if ([string]$Run.candidateResult.category -ceq 'maintenance_decision') { [string]$Run.candidateResult.decisionTaskId } elseif ($Run.candidateResult.PSObject.Properties.Name -contains 'maintenanceResolution') { [string]$Run.candidateResult.maintenanceResolution.decisionTaskId } else { $null }
    $maintenanceTask = if ([string]::IsNullOrWhiteSpace($maintenanceTaskId)) { $null } else { Read-TaskMetadata $script:root $maintenanceTaskId }
    if ([string]$Run.candidateResult.category -ceq 'maintenance_decision') {
      if ([string]$maintenanceTask.Metadata.dispatchState -cne 'blocked' -or @($maintenanceTask.Metadata.blockedBy).Count -eq 0 -or $maintenanceTask.Metadata.PSObject.Properties.Name -contains 'automationDecision') { Stop-Hourly 'maintenance_decision_source_invalid' }
    } elseif ($null -ne $maintenanceTask) {
      $resume = $Run.candidateResult.maintenanceResolution
      if ([string]$maintenanceTask.Digest -cne [string]$resume.pendingTaskDigest -or [string]$maintenanceTask.Metadata.dispatchState -cne 'pending_decision' -or [string]$maintenanceTask.Metadata.automationDecision.decisionId -cne [string]$resume.decisionId -or [string]$maintenanceTask.Metadata.automationDecision.status -cne 'awaiting_reply') { Stop-Hourly 'maintenance_decision_task_context_changed' }
    }
    $formalPaths = Get-FormalPaths -Run $Run -Task $task -MaintenanceTask $maintenanceTask
    if ($latest -cne [string]$Run.baseCommit) {
      foreach ($mainPath in Get-ChangedPaths $script:root "$($Run.baseCommit)..$latest") { foreach ($formal in $formalPaths) { if (Test-PathOverlap $mainPath $formal) { Stop-Hourly 'hourly_revalidation_required' } } }
    }
    if (Test-MainPathConflict $formalPaths) { Stop-Hourly 'hourly_main_path_conflict' }
    $canonicalBranch = "codex/automation/$Owner/$($Run.runId)/canonical-$($latest.Substring(0, 12))"
    & git -C $script:root show-ref --verify --quiet "refs/heads/$canonicalBranch" 2>$null
    if ($LASTEXITCODE -eq 0) { Stop-Hourly 'hourly_canonical_evidence_incomplete' }
    $null = Invoke-GitText $worktree @('switch', '-c', $canonicalBranch, $latest) 'hourly_canonical_branch_failed'
    $formalContract = Get-HourlyFormalCommitContract -Adapter $adapter -Run $Run
    $null = Invoke-GitText $worktree @('cherry-pick', '--no-commit', [string]$Run.candidateCommit) 'hourly_candidate_replay_failed'
    if ($Owner -ceq 'deepseek') {
      $transition = Invoke-JsonTool $transitionPath @('-RepositoryRoot', $worktree, '-TaskId', [string]$Run.taskId) 'hourly_pending_review_failed'
      if ([string]$transition.status -cne 'updated') { Stop-Hourly 'hourly_pending_review_failed' }
      Write-Handoff -Run $Run -CandidateCommit ([string]$Run.candidateCommit)
    }
    $sourceHead = Invoke-Finalizer $worktree @{
      ExpectedPaths = $formalPaths -join '|'; CommitMessage = [string]$formalContract.subject
      RequireAutomationMetadata = $true; AutomationTask = [string]$Run.taskId; AutomationState = [string]$formalContract.state
      AutomationResult = [string]$Run.candidateResult.result; AutomationImpact = [string]$Run.candidateResult.impact
      AutomationVerify = [string]$Run.candidateResult.verify; AutomationPlain = [string]$Run.candidateResult.plain
    }
    $formalHead = $sourceHead
    if ([string]$Run.candidateResult.category -ceq 'maintenance_decision') {
      $preparedTask = Read-TaskMetadata $worktree $maintenanceTaskId
      if ([string]$preparedTask.Metadata.dispatchState -cne 'blocked' -or @($preparedTask.Metadata.blockedBy).Count -ne 0) { Stop-Hourly 'maintenance_decision_source_invalid' }
      $beforeContextDigest = Get-TaskContextDigest $maintenanceTask.Metadata
      $decisionId = Get-MaintenanceDecisionId -TaskId $maintenanceTaskId -BaseCommit $latest -TaskContextDigest $beforeContextDigest
      $context = [ordered]@{
        schemaVersion = 1; kind = 'queue_maintenance'; taskId = $maintenanceTaskId; sourceRunId = [string]$Run.runId; decisionId = $decisionId
        question = [string]$Run.candidateResult.question; options = @($Run.candidateResult.options); recommendedOption = [string]$Run.candidateResult.recommendedOption
        impactSummary = [string]$Run.candidateResult.impactSummary; plainSummary = $Run.candidateResult.plainSummary; allowCustomReply = $false
        sourceCommit = $sourceHead; sourceTaskDigest = [string]$preparedTask.Digest; taskContextDigest = Get-TaskContextDigest $preparedTask.Metadata
        createdAt = [DateTimeOffset]::Now.ToString('o')
      }
      $contextPath = Write-PrivateJson 'state-transitions' "$($Run.runId)-PauseMaintenanceDecision.json" $context
      $projection = Invoke-JsonTool $taskStatePath @('-Action', 'PauseMaintenanceDecision', '-RepositoryRoot', $worktree, '-TaskId', $maintenanceTaskId, '-ContextPath', $contextPath) 'maintenance_decision_projection_failed'
      if ([string]$projection.status -cne 'updated') { Stop-Hourly 'maintenance_decision_projection_failed' }
      $formalHead = Invoke-Finalizer $worktree @{
        ExpectedPaths = (@($projection.changedPaths | ForEach-Object { [string]$_ }) -join '|'); CommitMessage = "chore($maintenanceTaskId): request maintenance decision"
        RequireAutomationMetadata = $true; AutomationTask = [string]$Run.taskId; AutomationState = 'completed'
        AutomationResult = '问题=空队列维护暴露负责人路线选择；完成=目标任务已转为维护型待决策'
        AutomationImpact = '影响=任务不会被自动领取；边界=本轮未等待或消费回复'
        AutomationVerify = '验证=维护型 pending_decision 投影通过；后续=发送唯一飞书决策卡'
        AutomationPlain = '发生=任务需要你选择路线；影响=选择前不会继续开发；需要=在飞书决策卡选择 A、B 或 C'
      }
      $Run.candidateResult | Add-Member -NotePropertyName expectedTransition -NotePropertyValue 'maintenance_pending_decision' -Force
      $Run.candidateResult | Add-Member -NotePropertyName decisionTaskId -NotePropertyValue $maintenanceTaskId -Force
      $Run.candidateResult | Add-Member -NotePropertyName decisionId -NotePropertyValue $decisionId -Force
    } elseif ($null -ne $maintenanceTask) {
      $resume = $Run.candidateResult.maintenanceResolution
      $preparedTask = Read-TaskMetadata $worktree $maintenanceTaskId
      $resolveContext = [ordered]@{
        schemaVersion = 1; kind = 'queue_maintenance'; taskId = $maintenanceTaskId; decisionId = [string]$resume.decisionId
        optionKey = [string]$resume.replyValue; source = [string]$resume.source; evidenceHash = [string]$resume.evidenceHash; resolvedAt = [DateTimeOffset]::Now.ToString('o'); preparedTaskDigest = [string]$preparedTask.Digest
      }
      $contextPath = Write-PrivateJson 'state-transitions' "$($Run.runId)-ResolveMaintenanceDecision.json" $resolveContext
      $projection = Invoke-JsonTool $taskStatePath @('-Action', 'ResolveMaintenanceDecision', '-RepositoryRoot', $worktree, '-TaskId', $maintenanceTaskId, '-ContextPath', $contextPath) 'maintenance_decision_projection_failed'
      if ([string]$projection.status -cne 'updated') { Stop-Hourly 'maintenance_decision_projection_failed' }
      $formalHead = Invoke-Finalizer $worktree @{
        ExpectedPaths = (@($projection.changedPaths | ForEach-Object { [string]$_ }) -join '|'); CommitMessage = "chore($maintenanceTaskId): apply maintenance decision"
        RequireAutomationMetadata = $true; AutomationTask = [string]$Run.taskId; AutomationState = 'completed'
        AutomationResult = "问题=维护型决策等待回复；完成=已按选项 $([string]$resume.replyValue) 形成确定状态"
        AutomationImpact = '影响=任务调度投影已同步；边界=未执行任务业务内容'
        AutomationVerify = '验证=维护型决策回复与任务投影通过；后续=ready 任务由新 run 领取'
        AutomationPlain = '发生=负责人路线选择已应用；影响=任务已进入对应调度状态；需要=无需重复回复'
      }
      $resolvedTask = Read-TaskMetadata $worktree $maintenanceTaskId
      $Run.candidateResult | Add-Member -NotePropertyName expectedTransition -NotePropertyValue 'maintenance_resolution' -Force
      $Run.candidateResult | Add-Member -NotePropertyName decisionTaskId -NotePropertyValue $maintenanceTaskId -Force
      $Run.candidateResult | Add-Member -NotePropertyName decisionId -NotePropertyValue ([string]$resume.decisionId) -Force
      $Run.candidateResult | Add-Member -NotePropertyName resolutionState -NotePropertyValue ([string]$resolvedTask.Metadata.dispatchState) -Force
    }
    if ($Owner -ceq 'codex' -and [string]$Run.route -ceq 'codex_review' -and [string]$Run.candidateResult.expectedTransition -ceq 'blocked') {
      $reviewEntry = Get-ReviewEntryEvidence -Root $worktree -TaskId ([string]$Run.taskId) -ExpectedReviewedCommit $reviewedCommit
      if ([string]$reviewEntry.ReviewedCommit -cne [string]$reviewedCommit) { Stop-Hourly 'review_rework_reviewed_commit_changed' }
    }
    Invoke-CombinedValidation -Run $Run -Worktree $worktree -Base $latest -Head $formalHead -Paths $formalPaths
    $updated = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'canonical_ready'; CanonicalBranch = $canonicalBranch; CanonicalBase = $latest; CanonicalHead = $formalHead }
    $Run.canonicalBranch = [string]$updated.run.canonicalBranch; $Run.canonicalBase = [string]$updated.run.canonicalBase; $Run.canonicalHead = [string]$updated.run.canonicalHead; $Run.state = [string]$updated.run.state
    if ((Invoke-GitText $script:root @('branch', '--show-current')) -cne 'master' -or (Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $latest -or (Test-MainPathConflict $formalPaths)) { Stop-Hourly 'hourly_integration_precondition_changed' }
    $null = Invoke-GitText $script:root @('merge', '--ff-only', $formalHead) 'hourly_fast_forward_failed'
    if ((Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $formalHead) { Stop-Hourly 'hourly_fast_forward_verification_failed' }
    Assert-Postcondition -Run $Run -Worktree $script:root
    $null = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; RunState = 'integrated'; CanonicalHead = $formalHead }
    $closed = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{ Owner = $Owner; RunId = [string]$Run.runId; CompletionCategory = 'success'; DetailCode = "commit_$($formalHead.Substring(0, 12))" }
    $resultStatus = if ([string]$Run.candidateResult.expectedTransition -ceq 'maintenance_pending_decision') { 'decision_requested' } elseif ([string]$Run.route -ceq 'queue_maintenance') { 'maintenance_completed' } else { 'completed' }
    $result = [ordered]@{ status = $resultStatus; category = 'success'; taskId = $Run.taskId; runId = $Run.runId; formalHead = $formalHead; canonicalBranch = $canonicalBranch; detailCode = if ($resultStatus -ceq 'decision_requested') { 'maintenance_decision_requested' } else { $closed.detailCode } }
    if ($resultStatus -cin @('decision_requested', 'maintenance_completed') -and
      $Run.candidateResult.PSObject.Properties.Name -contains 'decisionId' -and
      -not [string]::IsNullOrWhiteSpace([string]$Run.candidateResult.decisionId)) {
      $result.decisionTaskId = [string]$Run.candidateResult.decisionTaskId; $result.decisionId = [string]$Run.candidateResult.decisionId
      if ($Run.candidateResult.PSObject.Properties.Name -contains 'resolutionState') { $result.resolutionState = [string]$Run.candidateResult.resolutionState }
    }
    if ($Owner -ceq 'codex' -and [string]$Run.route -ceq 'codex_review' -and [string]$Run.candidateResult.expectedTransition -ceq 'blocked') {
      $result.reviewedCommit = $reviewedCommit
      $result.reviewQueueIndex = $reviewQueueIndex
    }
    $result
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
  if ($Owner -ceq 'codex' -and [string]$Run.route -ceq 'codex_review' -and [string]$Run.candidateResult.expectedTransition -ceq 'blocked') {
    try {
      $context = New-ReviewReworkDecisionContext -Run $Run -Outcome $Outcome
      return Send-ReviewReworkDecision -Context $context
    } catch {
      return '{"result":"INVALID_INPUT"}'
    }
  }
  $status = if ($Owner -ceq 'deepseek') { 'pending_review' } else { switch ([string]$Run.candidateResult.expectedTransition) { 'completed' { 'completed' }; 'blocked' { 'blocked' }; 'frozen' { 'blocked' }; 'pending_decision' { 'waiting_decision' }; 'waiting_reply' { 'waiting_reply' }; default { 'failed' } } }
  $arguments = @('-Kind', 'TaskOutcome', '-RepositoryRoot', $script:root, '-TaskId', [string]$Run.taskId, '-Status', $status, '-RunId', [string]$Run.runId)
  if ($status -cin @('completed', 'pending_review')) { $arguments += @('-CommitSha', [string]$Outcome.formalHead) } else { $arguments += @('-DetailCode', "task_$status") }
  try { $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $notificationPath @arguments 2>$null); if ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) { return [string]$output[0] } } catch {}
  'failed'
}

function Invoke-AttentionNotification {
  param([object]$Run, [string]$DetailCode)
  try {
    $output = @(
      & pwsh -NoProfile -ExecutionPolicy Bypass -File $notificationPath `
        -Kind TaskOutcome -RepositoryRoot $script:root -TaskId ([string]$Run.taskId) `
        -Status failed -RunId ([string]$Run.runId) -DetailCode $DetailCode 2>$null
    )
    if ($output.Count -eq 1) { return [string]$output[0] }
  } catch {}
  'failed'
}

function Add-AttentionNotification {
  param([AllowNull()][object]$Final, [AllowNull()][object]$Run)
  if (
    $null -eq $Final -or
    $null -eq $Run -or
    [string]$Final.status -cne 'attention_required' -or
    [string]::IsNullOrWhiteSpace([string]$Final.detailCode) -or
    [string]::IsNullOrWhiteSpace([string]$Run.taskId) -or
    [string]::IsNullOrWhiteSpace([string]$Run.runId)
  ) { return $Final }
  $Final.notification = Invoke-AttentionNotification -Run $Run -DetailCode ([string]$Final.detailCode)
  $Final
}

function Remove-ExactSuccessfulWorktree {
  param([object]$Run, [string]$FormalHead)
  try {
    $shown = Invoke-Runtime -RuntimeAction Show -Parameters @{ RepositoryRoot = $script:root }
    foreach ($active in @($shown.state.runs.codex, $shown.state.runs.deepseek)) { if ($null -ne $active -and (Normalize-FullPath ([string]$active.worktree)) -ceq (Normalize-FullPath ([string]$Run.worktree))) { return 'retained_runtime_reference' } }
    $worktree = Assert-WorktreePath $Run
    if (-not (Test-Path -LiteralPath $worktree) -or (Invoke-GitText $worktree @('rev-parse', 'HEAD')) -cne $FormalHead) { return 'retained_evidence_mismatch' }
    & git -C $worktree diff --quiet --ignore-submodules -- 2>$null
    if ($LASTEXITCODE -ne 0) { return 'retained_evidence_mismatch' }
    & git -C $worktree diff --cached --quiet --ignore-submodules -- 2>$null
    if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace((Invoke-GitText $worktree @('ls-files', '--others', '--exclude-standard')))) { return 'retained_evidence_mismatch' }
    if ([string]$Run.taskId -cne 'QUEUE-MAINTENANCE') {
      $task = Read-TaskMetadataAtCommit -Root $script:root -Commit ([string]$Run.baseCommit) -TaskId ([string]$Run.taskId)
      Assert-MaterializedAutomationInputs -Worktree $worktree -Metadata $task.Metadata
    }
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
  param([object]$Run, [ValidateSet('Block', 'PauseDecision', 'ResumeReady', 'ResolveMaintenanceDecision')][string]$Mode, [object]$Context, [AllowNull()][string]$ExistingWorktree)
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
    $actionName = if ($Mode -ceq 'PauseDecision') { 'PauseDecision' } elseif ($Mode -ceq 'ResumeReady') { 'ResumeReady' } elseif ($Mode -ceq 'ResolveMaintenanceDecision') { 'ResolveMaintenanceDecision' } else { 'Block' }
    $projectionTaskId = if ($Mode -ceq 'ResolveMaintenanceDecision') { [string]$Context.taskId } else { [string]$Run.taskId }
    $projection = Invoke-JsonTool $taskStatePath @('-Action', $actionName, '-RepositoryRoot', $worktree, '-TaskId', $projectionTaskId, '-ContextPath', $contextPath) 'hourly_state_projection_failed'
    if ([string]$projection.status -cne 'updated') { Stop-Hourly 'hourly_state_projection_failed' }
    $paths = @($projection.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    $stateText = if ($Mode -ceq 'PauseDecision') { 'pending_decision' } elseif ($Mode -ceq 'ResumeReady') { 'ready' } elseif ($Mode -ceq 'ResolveMaintenanceDecision') { [string]$projection.dispatchState } else { 'blocked' }
    $formalHead = Invoke-Finalizer $worktree @{
      ExpectedPaths = $paths -join '|'; CommitMessage = "chore($($Run.taskId)): set automation state $stateText"
      RequireAutomationMetadata = $true; AutomationTask = [string]$Run.taskId; AutomationState = 'completed'
      AutomationResult = "问题=任务需要确定终态；完成=任务已机械转换为 $stateText"
      AutomationImpact = '影响=任务调度投影已同步；边界=未合并未核验业务修改'
      AutomationVerify = '验证=任务卡投影检查通过；后续=按当前状态继续处理'
      AutomationPlain = "发生=任务状态已经变为 $stateText；影响=业务修改尚未作为完成结果进入主分支；需要=按通知说明处理"
    }
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
    if ($Mode -ceq 'ResolveMaintenanceDecision') {
      [ordered]@{ status = 'maintenance_completed'; taskId = $Run.taskId; decisionTaskId = $projectionTaskId; decisionId = [string]$Context.decisionId; resolutionState = $stateText; runId = $Run.runId; formalHead = $formalHead; stateBranch = $branch; worktree = $worktree; detailCode = "maintenance_decision_resolved_$stateText" }
    } else {
      [ordered]@{ status = $stateText; taskId = $Run.taskId; runId = $Run.runId; formalHead = $formalHead; stateBranch = $branch; worktree = $worktree }
    }
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

function Set-MaintenanceDecisionRecordField {
  param([object]$Record, [string]$Name, [object]$Value)
  if ($Record -is [Collections.IDictionary]) { $Record[$Name] = $Value }
  else { $Record | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force }
}

function Write-MaintenanceDecisionRecord {
  param([object]$Record)
  if ([string]$Record.decisionId -cnotmatch '^DEC-[0-9]{8}-QM[0-9A-F]{12}$') { Stop-Hourly 'maintenance_decision_context_invalid' }
  Write-PrivateJson 'maintenance-decisions' "$($Record.decisionId).json" $Record
}

function Send-MaintenanceDecision {
  param([object]$Outcome)
  $task = Read-TaskMetadata $script:root ([string]$Outcome.decisionTaskId)
  $decision = $task.Metadata.automationDecision
  if ([string]$task.Metadata.dispatchState -cne 'pending_decision' -or [string]$decision.status -cne 'awaiting_reply' -or [string]$decision.decisionId -cne [string]$Outcome.decisionId) { Stop-Hourly 'maintenance_decision_context_invalid' }
  $recordPath = Join-Path $script:effectiveStateRoot "maintenance-decisions\$($decision.decisionId).json"
  if (Test-Path -LiteralPath $recordPath -PathType Leaf) {
    $existing = Read-MaintenanceDecisionRecord $recordPath
    $same = [string]$existing.taskId -ceq [string]$task.Metadata.id -and [string]$existing.formalHead -ceq [string]$Outcome.formalHead
    return [ordered]@{ wire = '{"result":"ALREADY_REGISTERED"}'; accepted = ($same -and [string]$existing.status -cin @('awaiting_reply', 'answered', 'applied')); record = $existing }
  }
  $record = [ordered]@{
    schemaVersion = 1; kind = 'queue_maintenance'; status = 'awaiting_reply'; decisionId = [string]$decision.decisionId; taskId = [string]$task.Metadata.id
    sourceRunId = [string]$Outcome.runId; formalHead = [string]$Outcome.formalHead; pendingTaskDigest = [string]$task.Digest
    taskContextDigest = Get-TaskContextDigest $task.Metadata; createdAt = [string]$decision.createdAt; sendResult = $null
  }
  $null = Write-MaintenanceDecisionRecord $record
  $request = [ordered]@{
    decision = [ordered]@{
      decisionId = [string]$decision.decisionId; taskId = [string]$task.Metadata.id; question = [string]$decision.question
      options = @($decision.options | ForEach-Object { [ordered]@{ key = [string]$_.key; label = [string]$_.label } })
      recommendedOption = [string]$decision.recommendedOption; impactSummary = [string]$decision.impactSummary; plainSummary = $decision.plainSummary; allowCustomReply = $false
    }
    attemptNumber = 1
  }
  $requestPath = Write-PrivateJson 'decision-requests' "$($decision.decisionId).json" $request
  $wire = '{"result":"CHANNEL_UNAVAILABLE"}'
  try {
    $output = @(& node $decisionSenderPath --request-file $requestPath 2>$null)
    if ($output.Count -eq 1) { $wire = [string]$output[0] }
  } catch {}
  try { $sendResult = $wire | ConvertFrom-Json -Depth 30 } catch { $sendResult = [pscustomobject]@{ result = 'INVALID_INPUT' } }
  Set-MaintenanceDecisionRecordField $record 'sendResult' $sendResult
  $accepted = [string]$sendResult.result -ceq 'PROVIDER_ACCEPTED'
  if ($accepted) {
    try {
      $snapshot = [IO.File]::ReadAllText($requestPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 30
      if ($null -eq $snapshot.pendingDecision -or [string]::IsNullOrWhiteSpace([string]$snapshot.pendingDecision.createdAt) -or [string]::IsNullOrWhiteSpace([string]$snapshot.pendingDecision.expiresAt)) { throw 'missing binding' }
      Set-MaintenanceDecisionRecordField $record 'issuedAt' ([string]$snapshot.pendingDecision.createdAt)
      Set-MaintenanceDecisionRecordField $record 'expiresAt' ([string]$snapshot.pendingDecision.expiresAt)
    } catch {
      $accepted = $false
      Set-MaintenanceDecisionRecordField $record 'status' 'attention_required'
      Set-MaintenanceDecisionRecordField $record 'detailCode' 'maintenance_decision_binding_missing'
    }
  } else {
    Set-MaintenanceDecisionRecordField $record 'status' 'attention_required'
    Set-MaintenanceDecisionRecordField $record 'detailCode' 'maintenance_decision_delivery_failed'
  }
  $null = Write-MaintenanceDecisionRecord $record
  [ordered]@{ wire = $wire; accepted = $accepted; record = $record }
}

function Read-MaintenanceDecisionRecord {
  param([string]$Path)
  Assert-PrivatePathAcl -Path $Path
  try { [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50 } catch { Stop-Hourly 'maintenance_decision_context_invalid' }
}

function Find-AnsweredMaintenanceDecision {
  $directory = Join-Path $script:effectiveStateRoot 'maintenance-decisions'
  if (-not (Test-Path -LiteralPath $directory -PathType Container)) { return [ordered]@{ status = 'none' } }
  $records = @()
  foreach ($file in Get-ChildItem -LiteralPath $directory -Filter 'DEC-*.json' -File) {
    $record = Read-MaintenanceDecisionRecord $file.FullName
    if ([int]$record.schemaVersion -eq 1 -and [string]$record.kind -ceq 'queue_maintenance' -and [string]$record.status -cin @('awaiting_reply', 'answered')) { $records += [pscustomobject]@{ Path = $file.FullName; Record = $record } }
  }
  foreach ($item in @($records | Sort-Object { [string]$_.Record.createdAt }, { [string]$_.Record.taskId })) {
    $record = $item.Record
    $task = Read-TaskMetadata $script:root ([string]$record.taskId)
    if ([string]$task.Digest -cne [string]$record.pendingTaskDigest -or [string]$task.Metadata.dispatchState -cne 'pending_decision' -or [string]$task.Metadata.automationDecision.decisionId -cne [string]$record.decisionId -or [string]$task.Metadata.automationDecision.status -cne 'awaiting_reply') {
      return [ordered]@{ status = 'context_changed'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_task_context_changed'; context = $record; contextPath = $item.Path }
    }
    $acceptedPath = Join-Path $script:effectiveStateRoot "accepted-maintenance-replies\$($record.decisionId).json"
    if ([string]$record.status -ceq 'answered') {
      if (-not (Test-Path -LiteralPath $acceptedPath -PathType Leaf)) { return [ordered]@{ status = 'context_changed'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_reply_missing'; context = $record; contextPath = $item.Path } }
      $replyContext = Read-MaintenanceDecisionRecord $acceptedPath
      return [ordered]@{ status = 'answered'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; context = $record; contextPath = $item.Path; acceptedPath = $acceptedPath; replyContext = $replyContext }
    }
    $expiresAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$record.expiresAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$expiresAt)) { return [ordered]@{ status = 'context_changed'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_context_invalid'; context = $record; contextPath = $item.Path } }
    if ([DateTimeOffset]::Now -gt $expiresAt) { return [ordered]@{ status = 'expired'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_expired'; context = $record; contextPath = $item.Path } }
    $requestPath = Join-Path $script:effectiveStateRoot "decision-requests\$($record.decisionId).json"
    if (-not (Test-Path -LiteralPath $requestPath -PathType Leaf)) { return [ordered]@{ status = 'context_changed'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_request_missing'; context = $record; contextPath = $item.Path } }
    $output = @(& node $decisionConsumerPath --request-file $requestPath 2>$null)
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { return [ordered]@{ status = 'invalid_reply'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_reply_invalid'; context = $record; contextPath = $item.Path } }
    try { $reply = $output[0] | ConvertFrom-Json -Depth 30 } catch { return [ordered]@{ status = 'invalid_reply'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_reply_invalid'; context = $record; contextPath = $item.Path } }
    if ([string]$reply.result -ceq 'NO_REPLY') { return [ordered]@{ status = 'waiting'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId } }
    if ([string]$reply.result -cne 'OPTION_ACCEPTED' -or [string]$reply.optionKey -cnotin @('A', 'B', 'C') -or [string]$reply.source -cne 'feishu_card' -or [string]$reply.evidenceHash -cnotmatch '^[0-9a-f]{64}$') { return [ordered]@{ status = 'invalid_reply'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'maintenance_decision_reply_invalid'; context = $record; contextPath = $item.Path } }
    $replyContext = [ordered]@{
      schemaVersion = 1; kind = 'queue_maintenance'; taskId = 'QUEUE-MAINTENANCE'; decisionTaskId = [string]$record.taskId; decisionId = [string]$record.decisionId
      replyKind = 'option'; replyValue = [string]$reply.optionKey; source = [string]$reply.source; evidenceHash = [string]$reply.evidenceHash; pendingTaskDigest = [string]$record.pendingTaskDigest
    }
    $acceptedPath = Write-PrivateJson 'accepted-maintenance-replies' "$($record.decisionId).json" $replyContext
    Set-MaintenanceDecisionRecordField $record 'status' 'answered'
    Set-MaintenanceDecisionRecordField $record 'answeredAt' ([DateTimeOffset]::Now.ToString('o'))
    Set-MaintenanceDecisionRecordField $record 'optionKey' ([string]$reply.optionKey)
    Set-MaintenanceDecisionRecordField $record 'replyEvidenceHash' ([string]$reply.evidenceHash)
    $null = Write-MaintenanceDecisionRecord $record
    return [ordered]@{ status = 'answered'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; context = $record; contextPath = $item.Path; acceptedPath = $acceptedPath; replyContext = $replyContext }
  }
  [ordered]@{ status = 'none' }
}

function Apply-TerminatedMaintenanceDecision {
  param([object]$Found)
  $record = $Found.context
  if ([string]$Found.status -ceq 'context_changed') {
    Set-MaintenanceDecisionRecordField $record 'status' 'attention_required'
    Set-MaintenanceDecisionRecordField $record 'detailCode' ([string]$Found.detailCode)
    Set-MaintenanceDecisionRecordField $record 'terminatedAt' ([DateTimeOffset]::Now.ToString('o'))
    $null = Write-MaintenanceDecisionRecord $record
    return [ordered]@{ status = 'attention_required'; owner = $Owner; taskId = 'QUEUE-MAINTENANCE'; decisionTaskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = [string]$Found.detailCode; cleanup = 'none' }
  }
  $lock = Enter-TzgIntegrationLock -RepositoryRoot $script:root -TimeoutSeconds $IntegrationLockTimeoutSeconds
  if ($null -eq $lock) { return [ordered]@{ status = 'attention_required'; owner = $Owner; taskId = 'QUEUE-MAINTENANCE'; decisionTaskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'integration_lock_timeout'; cleanup = 'none' } }
  $worktree = $null; $branch = $null; $formalHead = $null
  try {
    $record = Read-MaintenanceDecisionRecord ([string]$Found.contextPath)
    if ([string]$record.status -cnotin @('awaiting_reply', 'answered')) { Stop-Hourly 'maintenance_decision_already_consumed' }
    $task = Read-TaskMetadata $script:root ([string]$record.taskId)
    if ([string]$task.Digest -cne [string]$record.pendingTaskDigest -or [string]$task.Metadata.dispatchState -cne 'pending_decision' -or [string]$task.Metadata.automationDecision.decisionId -cne [string]$record.decisionId) { Stop-Hourly 'maintenance_decision_task_context_changed' }
    $latest = Invoke-GitText $script:root @('rev-parse', 'master')
    $paths = @("开发管理/任务卡/$($record.taskId).txt", '开发管理/当前任务队列.txt', [string]$task.Metadata.sourceBacklog)
    if (Test-MainPathConflict $paths) { Stop-Hourly 'maintenance_decision_main_path_conflict' }
    $decisionKey = ([string]$record.decisionId).ToLowerInvariant()
    $worktree = Normalize-FullPath (Join-Path $script:root ".worktrees\automation\decisions\$decisionKey-maintenance")
    $branch = "codex/automation/decision/$decisionKey/terminate-$($latest.Substring(0, 12))"
    if (Test-Path -LiteralPath $worktree) { Stop-Hourly 'maintenance_decision_worktree_exists' }
    & git -C $script:root show-ref --verify --quiet "refs/heads/$branch" 2>$null
    if ($LASTEXITCODE -eq 0) { Stop-Hourly 'maintenance_decision_branch_exists' }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
    $null = Invoke-GitText $script:root @('worktree', 'add', '-b', $branch, $worktree, $latest) 'maintenance_decision_worktree_failed'
    $transition = [ordered]@{
      schemaVersion = 1; kind = 'queue_maintenance'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId
      detailCode = [string]$Found.detailCode; terminatedAt = [DateTimeOffset]::Now.ToString('o')
    }
    $transitionPath = Write-PrivateJson 'state-transitions' "$($record.decisionId)-ExpireMaintenanceDecision.json" $transition
    $projection = Invoke-JsonTool $taskStatePath @('-Action', 'ExpireMaintenanceDecision', '-RepositoryRoot', $worktree, '-TaskId', [string]$record.taskId, '-ContextPath', $transitionPath) 'maintenance_decision_projection_failed'
    if ([string]$projection.status -cne 'updated') { Stop-Hourly 'maintenance_decision_projection_failed' }
    $changedPaths = @($projection.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    $formalHead = Invoke-Finalizer $worktree @{
      ExpectedPaths = $changedPaths -join '|'; CommitMessage = "chore($($record.taskId)): terminate maintenance decision"
      RequireAutomationMetadata = $true; AutomationTask = 'QUEUE-MAINTENANCE'; AutomationState = 'completed'
      AutomationResult = "问题=维护型决策无法继续自动等待；完成=任务已转回 blocked 并记录 $([string]$Found.detailCode)"
      AutomationImpact = '影响=旧决策不再被自动消费；边界=未覆盖其他任务事实'
      AutomationVerify = '验证=终止投影与任务卡检查通过；后续=需要人工按当前事实重新发起'
      AutomationPlain = '发生=旧决策已停止；影响=任务保持阻塞；需要=如仍需继续请重新发起决策'
    }
    $null = Invoke-GitText $worktree @('diff', '--check', "$latest..$formalHead") 'maintenance_decision_diff_check_failed'
    $postcondition = if ([string]$Found.status -ceq 'expired') { @('-TaskId', [string]$record.taskId, '-Postcondition', 'MaintenanceExpiredBlocked') } else { @('-TaskId', [string]$record.taskId, '-Postcondition', 'CodexClosedOrNonReady') }
    $evidence = Invoke-JsonTool $checkerPath (@('-RepositoryRoot', $worktree) + $postcondition + '-OutputJson') 'maintenance_decision_postcondition_failed'
    if ([string]$evidence.status -cne 'ok') { Stop-Hourly 'maintenance_decision_postcondition_failed' }
    if ((Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $latest -or (Test-MainPathConflict $paths)) { Stop-Hourly 'maintenance_decision_integration_precondition_changed' }
    $null = Invoke-GitText $script:root @('merge', '--ff-only', $formalHead) 'maintenance_decision_fast_forward_failed'
    Set-MaintenanceDecisionRecordField $record 'status' $(if ([string]$Found.status -ceq 'expired') { 'expired' } else { 'attention_required' })
    Set-MaintenanceDecisionRecordField $record 'detailCode' ([string]$Found.detailCode)
    Set-MaintenanceDecisionRecordField $record 'terminatedAt' ([DateTimeOffset]::Now.ToString('o'))
    Set-MaintenanceDecisionRecordField $record 'formalHead' $formalHead
    $null = Write-MaintenanceDecisionRecord $record
    $cleanup = Remove-ReviewReworkWorktree -Worktree $worktree -Branch $branch -FormalHead $formalHead
    [ordered]@{ status = 'attention_required'; owner = $Owner; taskId = 'QUEUE-MAINTENANCE'; decisionTaskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = [string]$Found.detailCode; formalHead = $formalHead; cleanup = $cleanup }
  } catch {
    $detail = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'maintenance_decision_termination_failed' }
    Set-MaintenanceDecisionRecordField $record 'status' 'attention_required'; Set-MaintenanceDecisionRecordField $record 'detailCode' $detail
    try { $null = Write-MaintenanceDecisionRecord $record } catch {}
    [ordered]@{ status = 'attention_required'; owner = $Owner; taskId = 'QUEUE-MAINTENANCE'; decisionTaskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = $detail; cleanup = 'retained' }
  } finally { Exit-TzgIntegrationLock -Handle $lock }
}

function Complete-MaintenanceDecisionRecord {
  param([object]$Answered, [object]$Outcome)
  if ($null -eq $Answered -or [string]$Outcome.status -cne 'maintenance_completed') { return }
  $record = Read-MaintenanceDecisionRecord ([string]$Answered.contextPath)
  if ([string]$record.status -cne 'answered' -or [string]$record.decisionId -cne [string]$Outcome.decisionId) { Stop-Hourly 'maintenance_decision_record_changed' }
  Set-MaintenanceDecisionRecordField $record 'status' 'applied'
  Set-MaintenanceDecisionRecordField $record 'formalHead' ([string]$Outcome.formalHead)
  Set-MaintenanceDecisionRecordField $record 'resolutionState' ([string]$Outcome.resolutionState)
  Set-MaintenanceDecisionRecordField $record 'appliedAt' ([DateTimeOffset]::Now.ToString('o'))
  $null = Write-MaintenanceDecisionRecord $record
}

function Set-ReviewReworkRecordField {
  param([object]$Record, [string]$Name, [object]$Value)
  if ($Record -is [Collections.IDictionary]) { $Record[$Name] = $Value }
  else { $Record | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force }
}

function Write-ReviewReworkRecord {
  param([object]$Record)
  if ([string]$Record.decisionId -cnotmatch '^DEC-[0-9]{8}-[A-Z0-9]+$') { Stop-Hourly 'review_rework_context_invalid' }
  Write-PrivateJson 'review-rework-decisions' "$($Record.decisionId).json" $Record
}

function New-ReviewReworkDecisionContext {
  param([object]$Run, [object]$Outcome)
  if (
    $Owner -cne 'codex' -or
    [string]$Run.route -cne 'codex_review' -or
    [string]$Run.candidateResult.expectedTransition -cne 'blocked' -or
    [string]$Outcome.formalHead -cnotmatch '^[0-9a-f]{40,64}$' -or
    [string]$Outcome.reviewedCommit -cnotmatch '^[0-9a-f]{40,64}$' -or
    [int]$Outcome.reviewQueueIndex -lt 0
  ) { Stop-Hourly 'review_rework_context_invalid' }
  $task = Read-TaskMetadata $script:root ([string]$Run.taskId)
  if (
    [string]$task.Metadata.dispatchState -cne 'blocked' -or
    (Get-QueueTaskIndex -Root $script:root -TaskId ([string]$Run.taskId)) -ge 0 -or
    @($task.Metadata.expectedPaths | ForEach-Object { [string]$_ }) -cnotcontains '开发管理/未通过审核清单.txt'
  ) { Stop-Hourly 'review_rework_task_invalid' }
  $entry = Get-ReviewEntryEvidence -Root $script:root -TaskId ([string]$Run.taskId) -ExpectedReviewedCommit ([string]$Outcome.reviewedCommit)
  if ([string]$entry.ReviewedCommit -cne [string]$Outcome.reviewedCommit) { Stop-Hourly 'review_rework_reviewed_commit_changed' }
  $taskCommit = Invoke-GitText $script:root @('log', '-1', '--format=%H', '--', "开发管理/任务卡/$($Run.taskId).txt") 'review_rework_review_commit_invalid'
  if ($taskCommit -cne [string]$Outcome.formalHead) { Stop-Hourly 'review_rework_review_commit_invalid' }
  & git -C $script:root merge-base --is-ancestor ([string]$Outcome.reviewedCommit) ([string]$Outcome.formalHead) 2>$null
  if ($LASTEXITCODE -ne 0) { Stop-Hourly 'review_rework_review_commit_invalid' }
  $reason = [string]$task.Metadata.stateReason
  if ([string]::IsNullOrWhiteSpace($reason) -or $reason -match '[\r\n]') { Stop-Hourly 'review_rework_summary_invalid' }
  $decisionId = "DEC-$([DateTimeOffset]::Now.ToString('yyyyMMdd'))-REV$(([string]$Outcome.formalHead).Substring(0, 12).ToUpperInvariant())"
  [ordered]@{
    schemaVersion = 1; kind = 'review_rework'; status = 'awaiting_reply'; decisionId = $decisionId; taskId = [string]$Run.taskId
    reviewedCommit = [string]$Outcome.reviewedCommit; reviewCommit = [string]$Outcome.formalHead
    taskDigest = [string]$task.Digest; taskContextDigest = Get-TaskContextDigest $task.Metadata; reviewEntryDigest = [string]$entry.Digest
    queueIndex = [int]$Outcome.reviewQueueIndex; createdAt = [DateTimeOffset]::Now.ToString('o'); sendResult = $null
    question = "任务 $($Run.taskId) 复审未通过，是否安排返工？"
    options = @(
      [ordered]@{ key = 'A'; label = '交回 DeepSeek 返工' },
      [ordered]@{ key = 'B'; label = '改由 Codex 返工' },
      [ordered]@{ key = 'C'; label = '暂不返工，保持阻塞' }
    )
    recommendedOption = 'A'; impactSummary = $reason
    plainSummary = [ordered]@{
      situation = 'Codex 已确认任务存在需返工问题，当前已阻塞且不会被定时器领取。'
      impact = 'A 或 B 会重新排队并创建全新任务轮次；C 保持现状。'
      action = '建议选择 A；未选择前不会自动继续。'
    }
  }
}

function Send-ReviewReworkDecision {
  param([object]$Context)
  $recordPath = Join-Path $script:effectiveStateRoot "review-rework-decisions\$($Context.decisionId).json"
  if (Test-Path -LiteralPath $recordPath -PathType Leaf) {
    try { $existing = [IO.File]::ReadAllText($recordPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50 } catch { Stop-Hourly 'review_rework_context_invalid' }
    if ([string]$existing.taskId -cne [string]$Context.taskId -or [string]$existing.reviewCommit -cne [string]$Context.reviewCommit) { Stop-Hourly 'review_rework_context_collision' }
    return '{"result":"ALREADY_REGISTERED"}'
  }
  $null = Write-ReviewReworkRecord $Context
  $request = [ordered]@{
    decision = [ordered]@{
      decisionId = [string]$Context.decisionId; taskId = [string]$Context.taskId; question = [string]$Context.question
      options = @($Context.options); recommendedOption = [string]$Context.recommendedOption; impactSummary = [string]$Context.impactSummary
      plainSummary = $Context.plainSummary; allowCustomReply = $false
    }
    attemptNumber = 1
  }
  $requestPath = Write-PrivateJson 'decision-requests' "$($Context.decisionId).json" $request
  $wire = '{"result":"CHANNEL_UNAVAILABLE"}'
  try {
    $output = @(& node $decisionSenderPath --request-file $requestPath 2>$null)
    if ($output.Count -eq 1) { $wire = [string]$output[0] }
  } catch {}
  try { $sendResult = $wire | ConvertFrom-Json -Depth 20 } catch { $sendResult = [pscustomobject]@{ result = 'INVALID_INPUT' } }
  Set-ReviewReworkRecordField -Record $Context -Name sendResult -Value $sendResult
  if ([string]$sendResult.result -cne 'PROVIDER_ACCEPTED') { Set-ReviewReworkRecordField -Record $Context -Name status -Value 'delivery_failed' }
  $null = Write-ReviewReworkRecord $Context
  $wire
}

function Find-AnsweredReviewRework {
  $directory = Join-Path $script:effectiveStateRoot 'review-rework-decisions'
  if (-not (Test-Path -LiteralPath $directory -PathType Container)) { return [ordered]@{ status = 'none' } }
  $records = @()
  foreach ($file in Get-ChildItem -LiteralPath $directory -Filter 'DEC-*.json' -File) {
    try {
      Assert-PrivatePathAcl -Path $file.FullName
      $record = [IO.File]::ReadAllText($file.FullName, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50
      if ([int]$record.schemaVersion -eq 1 -and [string]$record.kind -ceq 'review_rework' -and [string]$record.status -ceq 'awaiting_reply') { $records += [pscustomobject]@{ Path = $file.FullName; Record = $record } }
    } catch { return [ordered]@{ status = 'attention_required'; detailCode = 'review_rework_context_invalid' } }
  }
  foreach ($item in @($records | Sort-Object { [string]$_.Record.createdAt }, { [string]$_.Record.decisionId })) {
    $record = $item.Record
    $requestPath = Join-Path $script:effectiveStateRoot "decision-requests\$($record.decisionId).json"
    if (-not (Test-Path -LiteralPath $requestPath -PathType Leaf)) { return [ordered]@{ status = 'attention_required'; taskId = [string]$record.taskId; detailCode = 'review_rework_request_missing'; context = $record; contextPath = $item.Path } }
    try { $snapshot = [IO.File]::ReadAllText($requestPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 30 } catch { return [ordered]@{ status = 'attention_required'; taskId = [string]$record.taskId; detailCode = 'review_rework_request_invalid'; context = $record; contextPath = $item.Path } }
    if ($snapshot.PSObject.Properties.Name -contains 'decision') { continue }
    $output = @(& node $decisionConsumerPath --request-file $requestPath 2>$null)
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { return [ordered]@{ status = 'attention_required'; taskId = [string]$record.taskId; detailCode = 'review_rework_reply_invalid'; context = $record; contextPath = $item.Path } }
    try { $reply = $output[0] | ConvertFrom-Json -Depth 30 } catch { return [ordered]@{ status = 'attention_required'; taskId = [string]$record.taskId; detailCode = 'review_rework_reply_invalid'; context = $record; contextPath = $item.Path } }
    if ([string]$reply.result -ceq 'NO_REPLY') { continue }
    if ([string]$reply.result -cne 'OPTION_ACCEPTED' -or [string]$reply.optionKey -cnotin @('A', 'B', 'C')) { return [ordered]@{ status = 'attention_required'; taskId = [string]$record.taskId; detailCode = 'review_rework_reply_invalid'; context = $record; contextPath = $item.Path } }
    return [ordered]@{ status = 'answered'; taskId = [string]$record.taskId; context = $record; contextPath = $item.Path; reply = $reply }
  }
  [ordered]@{ status = 'none' }
}

function Remove-ReviewReworkWorktree {
  param([string]$Worktree, [string]$Branch, [string]$FormalHead)
  try {
    if (
      -not (Test-Path -LiteralPath $Worktree) -or
      -not [string]::IsNullOrWhiteSpace((Invoke-GitText $Worktree @('status', '--porcelain=v1', '--untracked-files=all'))) -or
      (Invoke-GitText $Worktree @('branch', '--show-current')) -cne $Branch -or
      (Invoke-GitText $Worktree @('rev-parse', 'HEAD')) -cne $FormalHead
    ) { return 'retained_evidence_mismatch' }
    & git -C $script:root merge-base --is-ancestor $FormalHead master 2>$null
    if ($LASTEXITCODE -ne 0) { return 'retained_unintegrated' }
    $null = Invoke-GitText $script:root @('-c', 'core.longPaths=true', 'worktree', 'remove', '--force', $Worktree) 'review_rework_cleanup_failed'
    & git -C $script:root show-ref --verify --quiet "refs/heads/$Branch" 2>$null
    if ($LASTEXITCODE -eq 0) { $null = Invoke-GitText $script:root @('branch', '-D', $Branch) 'review_rework_cleanup_failed' }
    $parent = Split-Path -Parent $Worktree
    if ((Test-Path -LiteralPath $parent) -and @(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) { Remove-Item -LiteralPath $parent -Force }
    'cleaned'
  } catch { 'retained_cleanup_failed' }
}

function Invoke-ReviewReworkNotification {
  param([object]$Result)
  $arguments = @('-Kind', 'TaskOutcome', '-RepositoryRoot', $script:root, '-TaskId', [string]$Result.taskId, '-RunId', [string]$Result.decisionId)
  if ([string]$Result.optionKey -ceq 'C') { $arguments += @('-Status', 'blocked', '-DetailCode', 'review_rework_kept_blocked') }
  else { $arguments += @('-Status', 'requeued', '-CommitSha', [string]$Result.formalHead) }
  try {
    $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $notificationPath @arguments 2>$null)
    if ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) { return [string]$output[0] }
  } catch {}
  'failed'
}

function Apply-AnsweredReviewRework {
  param([object]$Answered)
  $record = $Answered.context
  $lock = Enter-TzgIntegrationLock -RepositoryRoot $script:root -TimeoutSeconds $IntegrationLockTimeoutSeconds
  if ($null -eq $lock) {
    Set-ReviewReworkRecordField -Record $record -Name status -Value 'attention_required'
    Set-ReviewReworkRecordField -Record $record -Name detailCode -Value 'integration_lock_timeout'
    $null = Write-ReviewReworkRecord $record
    return [ordered]@{ status = 'attention_required'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = 'integration_lock_timeout' }
  }
  $worktree = $null
  $branch = $null
  $formalHead = $null
  $result = $null
  try {
    Assert-PrivatePathAcl -Path ([string]$Answered.contextPath)
    $record = [IO.File]::ReadAllText([string]$Answered.contextPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50
    if ([string]$record.status -cne 'awaiting_reply') {
      return [ordered]@{ status = 'review_rework_already_consumed'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId }
    }
    $reply = $Answered.reply
    if (
      [int]$record.schemaVersion -ne 1 -or [string]$record.kind -cne 'review_rework' -or
      [string]$reply.result -cne 'OPTION_ACCEPTED' -or [string]$reply.optionKey -cnotin @('A', 'B', 'C') -or
      [string]$reply.source -cne 'feishu_card' -or [string]$reply.evidenceHash -cnotmatch '^[0-9a-f]{64}$'
    ) { Stop-Hourly 'review_rework_reply_invalid' }
    $shown = Invoke-Runtime -RuntimeAction Show -Parameters @{ RepositoryRoot = $script:root }
    if ([string]$shown.status -cne 'OK' -or @($shown.activeTaskIds | ForEach-Object { [string]$_ }) -ccontains [string]$record.taskId) { Stop-Hourly 'review_rework_task_occupied' }
    $task = Read-TaskMetadata $script:root ([string]$record.taskId)
    if (
      [string]$task.Digest -cne [string]$record.taskDigest -or
      (Get-TaskContextDigest $task.Metadata) -cne [string]$record.taskContextDigest -or
      [string]$task.Metadata.dispatchState -cne 'blocked' -or
      (Get-QueueTaskIndex -Root $script:root -TaskId ([string]$record.taskId)) -ge 0
    ) { Stop-Hourly 'review_rework_task_changed' }
    $entry = Get-ReviewEntryEvidence -Root $script:root -TaskId ([string]$record.taskId) -ExpectedReviewedCommit ([string]$record.reviewedCommit)
    if ([string]$entry.Digest -cne [string]$record.reviewEntryDigest -or [string]$entry.ReviewedCommit -cne [string]$record.reviewedCommit) { Stop-Hourly 'review_rework_entry_changed' }
    $taskCommit = Invoke-GitText $script:root @('log', '-1', '--format=%H', '--', "开发管理/任务卡/$($record.taskId).txt") 'review_rework_review_commit_invalid'
    if ($taskCommit -cne [string]$record.reviewCommit) { Stop-Hourly 'review_rework_review_commit_invalid' }
    & git -C $script:root merge-base --is-ancestor ([string]$record.reviewedCommit) ([string]$record.reviewCommit) 2>$null
    if ($LASTEXITCODE -ne 0) { Stop-Hourly 'review_rework_review_commit_invalid' }
    & git -C $script:root merge-base --is-ancestor ([string]$record.reviewCommit) master 2>$null
    if ($LASTEXITCODE -ne 0) { Stop-Hourly 'review_rework_review_commit_invalid' }
    if ([string]$reply.optionKey -ceq 'C') {
      Set-ReviewReworkRecordField -Record $record -Name status -Value 'consumed'
      Set-ReviewReworkRecordField -Record $record -Name optionKey -Value 'C'
      Set-ReviewReworkRecordField -Record $record -Name replyEvidenceHash -Value ([string]$reply.evidenceHash)
      Set-ReviewReworkRecordField -Record $record -Name consumedAt -Value ([DateTimeOffset]::Now.ToString('o'))
      $null = Write-ReviewReworkRecord $record
      $result = [ordered]@{ status = 'review_rework_blocked'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; optionKey = 'C'; detailCode = 'review_rework_kept_blocked' }
    } else {
      $latest = Invoke-GitText $script:root @('rev-parse', 'master')
      $paths = @("开发管理/任务卡/$($record.taskId).txt", '开发管理/当前任务队列.txt', [string]$task.Metadata.sourceBacklog)
      $evidencePaths = @($paths + '开发管理/未通过审核清单.txt')
      if (Test-MainPathConflict $evidencePaths) { Stop-Hourly 'review_rework_main_path_conflict' }
      $decisionKey = ([string]$record.decisionId).ToLowerInvariant()
      $worktree = Normalize-FullPath (Join-Path $script:root ".worktrees\automation\decisions\$decisionKey")
      $branch = "codex/automation/decision/$decisionKey/state-$($latest.Substring(0, 12))"
      if (Test-Path -LiteralPath $worktree) { Stop-Hourly 'review_rework_worktree_exists' }
      & git -C $script:root show-ref --verify --quiet "refs/heads/$branch" 2>$null
      if ($LASTEXITCODE -eq 0) { Stop-Hourly 'review_rework_branch_exists' }
      [IO.Directory]::CreateDirectory((Split-Path -Parent $worktree)) | Out-Null
      $null = Invoke-GitText $script:root @('worktree', 'add', '-b', $branch, $worktree, $latest) 'review_rework_worktree_failed'
      $transition = [ordered]@{
        schemaVersion = 1; kind = 'review_rework'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId
        optionKey = [string]$reply.optionKey; queueIndex = [int]$record.queueIndex; taskDigest = [string]$record.taskDigest
        taskContextDigest = [string]$record.taskContextDigest; reviewCommit = [string]$record.reviewCommit
        reviewEntryDigest = [string]$record.reviewEntryDigest; replyEvidenceHash = [string]$reply.evidenceHash
      }
      $transitionPath = Write-PrivateJson 'state-transitions' "$($record.decisionId)-RequeueReview.json" $transition
      $projection = Invoke-JsonTool $taskStatePath @('-Action', 'RequeueReview', '-RepositoryRoot', $worktree, '-TaskId', [string]$record.taskId, '-ContextPath', $transitionPath) 'review_rework_projection_failed'
      if ([string]$projection.status -cne 'updated') { Stop-Hourly 'review_rework_projection_failed' }
      $changedPaths = @($projection.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
      if (($changedPaths -join "`0") -cne (@($paths | Sort-Object -Unique) -join "`0")) { Stop-Hourly 'review_rework_projection_paths_invalid' }
      Push-Location -LiteralPath $worktree
      try { $null = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $whitespacePath -ExpectedPaths ($changedPaths -join '|') 2>&1) } finally { Pop-Location }
      if ($LASTEXITCODE -ne 0) { Stop-Hourly 'review_rework_whitespace_failed' }
      $targetLabel = if ([string]$reply.optionKey -ceq 'A') { 'DeepSeek' } else { 'Codex' }
      $formalHead = Invoke-Finalizer $worktree @{
        ExpectedPaths = $changedPaths -join '|'; CommitMessage = "chore($($record.taskId)): requeue review rework"
        RequireAutomationMetadata = $true; AutomationTask = [string]$record.taskId; AutomationState = 'completed'
        AutomationResult = "问题=Codex 复审未通过；完成=负责人选择 $targetLabel 按同一卡返工并重新排队"
        AutomationImpact = '影响=任务恢复为可领取状态；边界=没有恢复旧 run、旧会话或旧 worktree'
        AutomationVerify = '验证=任务卡、队列与 backlog 投影检查通过；后续=由对应 owner 的新轮次领取'
        AutomationPlain = "发生=负责人已选择 $targetLabel 返工；影响=任务已重新排队但尚未开始新 run；需要=无需再次手动确认"
      }
      if ((@(Get-ChangedPaths $worktree "$latest..$formalHead") -join "`0") -cne ($changedPaths -join "`0")) { Stop-Hourly 'review_rework_formal_paths_invalid' }
      $null = Invoke-GitText $worktree @('diff', '--check', "$latest..$formalHead") 'review_rework_diff_check_failed'
      $postcondition = if ([string]$reply.optionKey -ceq 'A') { @('-Postcondition', 'ExternalDispatchReady', '-ExpectedOwner', 'deepseek') } else { @('-Postcondition', 'CodexDispatchReady', '-ExpectedRoute', 'codex_execute') }
      $evidence = Invoke-JsonTool $checkerPath (@('-RepositoryRoot', $worktree, '-TaskId', [string]$record.taskId) + $postcondition + '-OutputJson') 'review_rework_postcondition_failed'
      if ([string]$evidence.status -cne 'ok') { Stop-Hourly 'review_rework_postcondition_failed' }
      $currentEntry = Get-ReviewEntryEvidence -Root $script:root -TaskId ([string]$record.taskId) -ExpectedReviewedCommit ([string]$record.reviewedCommit)
      if (
        [string]$currentEntry.Digest -cne [string]$record.reviewEntryDigest -or
        (Invoke-GitText $script:root @('branch', '--show-current')) -cne 'master' -or
        (Invoke-GitText $script:root @('rev-parse', 'HEAD')) -cne $latest -or
        (Test-MainPathConflict $evidencePaths)
      ) { Stop-Hourly 'review_rework_integration_precondition_changed' }
      $null = Invoke-GitText $script:root @('merge', '--ff-only', $formalHead) 'review_rework_fast_forward_failed'
      $rootEvidence = Invoke-JsonTool $checkerPath (@('-RepositoryRoot', $script:root, '-TaskId', [string]$record.taskId) + $postcondition + '-OutputJson') 'review_rework_postcondition_failed'
      if ([string]$rootEvidence.status -cne 'ok') { Stop-Hourly 'review_rework_postcondition_failed' }
      Set-ReviewReworkRecordField -Record $record -Name status -Value 'consumed'
      Set-ReviewReworkRecordField -Record $record -Name optionKey -Value ([string]$reply.optionKey)
      Set-ReviewReworkRecordField -Record $record -Name replyEvidenceHash -Value ([string]$reply.evidenceHash)
      Set-ReviewReworkRecordField -Record $record -Name formalHead -Value $formalHead
      Set-ReviewReworkRecordField -Record $record -Name consumedAt -Value ([DateTimeOffset]::Now.ToString('o'))
      $null = Write-ReviewReworkRecord $record
      $cleanup = Remove-ReviewReworkWorktree -Worktree $worktree -Branch $branch -FormalHead $formalHead
      $result = [ordered]@{ status = 'review_rework_requeued'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; optionKey = [string]$reply.optionKey; formalHead = $formalHead; cleanup = $cleanup }
    }
  } catch {
    $detail = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'review_rework_apply_failed' }
    Set-ReviewReworkRecordField -Record $record -Name status -Value 'attention_required'
    Set-ReviewReworkRecordField -Record $record -Name detailCode -Value $detail
    if (-not [string]::IsNullOrWhiteSpace($branch)) { Set-ReviewReworkRecordField -Record $record -Name evidenceBranch -Value $branch }
    if (-not [string]::IsNullOrWhiteSpace($worktree)) { Set-ReviewReworkRecordField -Record $record -Name evidenceWorktree -Value ".worktrees/automation/decisions/$(([string]$record.decisionId).ToLowerInvariant())" }
    if (-not [string]::IsNullOrWhiteSpace($formalHead)) { Set-ReviewReworkRecordField -Record $record -Name formalHead -Value $formalHead }
    try { $null = Write-ReviewReworkRecord $record } catch {}
    $result = [ordered]@{ status = 'attention_required'; taskId = [string]$record.taskId; decisionId = [string]$record.decisionId; detailCode = $detail }
  } finally { Exit-TzgIntegrationLock -Handle $lock }
  if ([string]$result.status -cin @('review_rework_requeued', 'review_rework_blocked')) { $result.notification = Invoke-ReviewReworkNotification $result }
  $result
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
    schemaVersion = 1; kind = 'decision_checkpoint'; taskId = [string]$Run.taskId; decisionId = [string]$checkpoint.decisionId
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
$maintenanceAnswered = $null
$script:stage = 'initialize'
$invocationMutex = $null
$invocationHeld = $false
try {
  $script:stage = 'dependencies'
  foreach ($path in @($runtimePath, $selectorPath, $checkerPath, $taskStatePath, $finalizerPath, $whitespacePath, $notificationPath, $decisionSenderPath, $decisionConsumerPath)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-Hourly 'hourly_dependency_missing' } }
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
  elseif ($Action -ceq 'Canary') {
    if (-not (Test-HourlyOwnerModelVerified -Owner $Owner -Model $Model)) { Stop-Hourly 'hourly_codex_model_unverified' }
    $final = Invoke-Canary
  }
  else {
    $script:stage = 'runtime_show'
    $shown = Invoke-Runtime -RuntimeAction Show -Parameters @{ RepositoryRoot = $script:root }
    if ([string]$shown.status -cne 'OK') { Stop-Hourly 'hourly_runtime_unavailable' }
    $run = $shown.state.runs.$Owner
    if ($null -ne $run) {
      $final = [ordered]@{ status = 'existing_run'; owner = $Owner; taskId = $run.taskId; runId = $run.runId; state = $run.state; detailCode = $run.recoveryReason }
    } else {
      if (-not (Test-HourlyOwnerModelVerified -Owner $Owner -Model $Model)) { Stop-Hourly 'hourly_codex_model_unverified' }
      $reviewDecision = Find-AnsweredReviewRework
      if ([string]$reviewDecision.status -ceq 'answered') { $final = Apply-AnsweredReviewRework $reviewDecision }
      elseif ([string]$reviewDecision.status -ceq 'attention_required') {
        if ($reviewDecision.PSObject.Properties.Name -contains 'context' -and $null -ne $reviewDecision.context) {
          try {
            Set-ReviewReworkRecordField -Record $reviewDecision.context -Name status -Value 'attention_required'
            Set-ReviewReworkRecordField -Record $reviewDecision.context -Name detailCode -Value ([string]$reviewDecision.detailCode)
            $null = Write-ReviewReworkRecord $reviewDecision.context
          } catch {}
        }
        $attentionTask = if ($reviewDecision.PSObject.Properties.Name -contains 'taskId') { [string]$reviewDecision.taskId } else { $null }
        $final = [ordered]@{ status = 'attention_required'; owner = $Owner; taskId = $attentionTask; detailCode = [string]$reviewDecision.detailCode }
      }
      $answered = if ($null -eq $final) { Find-AnsweredCheckpoint } else { [ordered]@{ status = 'none' } }
      $restored = $null
      if ([string]$answered.status -ceq 'answered') { $restored = Restore-AnsweredCheckpoint $answered }
      elseif ([string]$answered.status -ceq 'attention_required') { $final = $answered }
      if ($null -eq $final -and $Owner -ceq 'codex') {
        $queueEvidence = Invoke-JsonTool $checkerPath @('-RepositoryRoot', $script:root, '-OutputJson') 'hourly_task_projection_failed'
        if ([int]$queueEvidence.readyCount -eq 0) {
          $maintenance = Find-AnsweredMaintenanceDecision
          switch ([string]$maintenance.status) {
            'waiting' { $final = [ordered]@{ status = 'waiting_decision'; owner = $Owner; taskId = 'QUEUE-MAINTENANCE'; decisionTaskId = [string]$maintenance.taskId; decisionId = [string]$maintenance.decisionId; detailCode = 'maintenance_decision_no_reply'; cleanup = 'none' } }
            'answered' { $maintenanceAnswered = $maintenance }
            { $_ -cin @('expired', 'invalid_reply', 'context_changed') } { $final = Apply-TerminatedMaintenanceDecision $maintenance }
          }
        }
      }
      if ($null -eq $final) {
        $script:stage = 'selection'
        if ($null -ne $maintenanceAnswered) {
          $taskId = 'QUEUE-MAINTENANCE'; $route = 'queue_maintenance'; $digest = Get-NormalizedTextDigest (Join-Path $script:root '开发管理\当前任务队列.txt')
        } else {
          $selection = Invoke-JsonTool $selectorPath @('-RepositoryRoot', $script:root, '-Owner', $Owner) 'hourly_selection_failed'
          if ([string]$selection.status -ceq 'selected') { $taskId = [string]$selection.taskId; $route = [string]$selection.route; $digest = [string]$selection.taskCardDigest }
          elseif ($Owner -ceq 'codex' -and [string]$selection.status -ceq 'no_candidate' -and [int]$selection.queueCount -eq 0 -and $null -eq $shown.state.runs.deepseek) { $taskId = 'QUEUE-MAINTENANCE'; $route = 'queue_maintenance'; $digest = Get-NormalizedTextDigest (Join-Path $script:root '开发管理\当前任务队列.txt') }
          else { $final = [ordered]@{ status = 'no_candidate'; owner = $Owner; detailCode = 'no_runnable_candidate' } }
        }
      }
      if ($null -eq $final) {
        $script:stage = 'claim'
        $claim = Invoke-Runtime -RuntimeAction ClaimRun -Parameters @{ Owner = $Owner; TaskId = $taskId; Route = $route; RepositoryRoot = $script:root; MainBranch = 'master'; BaseCommit = (Invoke-GitText $script:root @('rev-parse', 'master')); TaskCardDigest = $digest }
        if ([string]$claim.status -cne 'CLAIMED') { $final = [ordered]@{ status = 'occupied'; owner = $Owner; detailCode = [string]$claim.status } } else { $run = $claim.run }
      }
      if ($null -eq $final) {
        $script:stage = 'candidate_worktree'
        $null = New-CandidateWorktree $run
        if ($null -ne $maintenanceAnswered -and [string]$maintenanceAnswered.replyContext.replyValue -ceq 'C') {
          $directContext = [ordered]@{
            schemaVersion = 1; kind = 'queue_maintenance'; taskId = [string]$maintenanceAnswered.taskId; decisionId = [string]$maintenanceAnswered.decisionId
            optionKey = 'C'; source = [string]$maintenanceAnswered.replyContext.source; evidenceHash = [string]$maintenanceAnswered.replyContext.evidenceHash; resolvedAt = [DateTimeOffset]::Now.ToString('o'); preparedTaskDigest = [string]$maintenanceAnswered.replyContext.pendingTaskDigest
          }
          $outcome = Integrate-StateTransition -Run $run -Mode ResolveMaintenanceDecision -Context $directContext
          $outcome.notification = 'skipped'
          $run.canonicalBranch = [string]$outcome.stateBranch
          $run.canonicalHead = [string]$outcome.formalHead
          $outcome.cleanup = Remove-ExactSuccessfulWorktree -Run $run -FormalHead ([string]$outcome.formalHead)
          Complete-MaintenanceDecisionRecord -Answered $maintenanceAnswered -Outcome $outcome
          $final = $outcome
        }
        $resumeContext = if ($null -ne $maintenanceAnswered) { [string]$maintenanceAnswered.acceptedPath } else { Apply-CheckpointToNewRun -Run $run -Restored $restored }
      }
      if ($null -eq $final) {
        $candidateArgs = Get-HourlyCandidateArguments -Adapter $script:adapter -Run $run -StateRoot $script:effectiveStateRoot -TimeoutSeconds $ResponsibilityTimeoutSeconds -ResumeContextPath $resumeContext
        $script:stage = 'candidate'
        $candidate = Invoke-JsonTool $script:adapter.candidateScript $candidateArgs 'hourly_candidate_failed'
        switch ([string]$candidate.status) {
          { $_ -cin @('completed', 'maintenance_decision') } {
            $script:stage = 'candidate_evidence'
            Assert-CandidateEvidence -Run $run -Candidate $candidate
            $resultPath = Write-PrivateJson 'candidate-results' "$($run.runId).json" $candidate.candidateResult
            $updated = Invoke-Runtime -RuntimeAction UpdateRun -Parameters @{ Owner = $Owner; RunId = [string]$run.runId; RunState = 'candidate_ready'; SessionKind = [string]$script:adapter.sessionKind; SessionId = [string]$candidate.sessionId; CandidateCommit = [string]$candidate.candidateCommit; CandidateResultPath = $resultPath }
            $run = $updated.run
            $script:stage = 'formal_integration'
            $outcome = Build-And-IntegrateCandidate $run
            if ([string]$outcome.status -cin @('completed', 'maintenance_completed', 'decision_requested')) {
              $run.canonicalBranch = [string]$outcome.canonicalBranch
              $run.canonicalHead = [string]$outcome.formalHead
              if ([string]$outcome.status -ceq 'decision_requested') {
                $sent = Send-MaintenanceDecision $outcome
                $outcome.notification = [string]$sent.wire
                if (-not [bool]$sent.accepted) {
                  $outcome.status = 'attention_required'; $outcome.detailCode = if ($null -ne $sent.record -and $sent.record.PSObject.Properties.Name -contains 'detailCode') { [string]$sent.record.detailCode } else { 'maintenance_decision_delivery_failed' }
                }
              } else {
                $outcome.notification = Invoke-BestEffortNotification -Run $run -Outcome $outcome
              }
              $outcome.cleanup = Remove-ExactSuccessfulWorktree -Run $run -FormalHead ([string]$outcome.formalHead)
              $outcome.checkpointCleanup = Remove-ConsumedCheckpointWorktree $restored
              if ([string]$outcome.status -ceq 'maintenance_completed' -and $null -ne $maintenanceAnswered) { Complete-MaintenanceDecisionRecord -Answered $maintenanceAnswered -Outcome $outcome }
            }
            $final = $outcome
          }
          'no_candidate' {
            if ([string]$run.route -cne 'queue_maintenance') { Stop-Hourly 'hourly_no_candidate_invalid' }
            $null = Invoke-Runtime -RuntimeAction CompleteRun -Parameters @{ Owner = $Owner; RunId = [string]$run.runId; CompletionCategory = 'no_candidate'; DetailCode = 'no_runnable_candidate' }
            $run.canonicalBranch = [string]$run.candidateBranch
            $run.canonicalHead = [string]$run.baseCommit
            $final = [ordered]@{ status = 'no_candidate'; owner = $Owner; taskId = $run.taskId; runId = $run.runId; detailCode = 'no_runnable_candidate'; cleanup = Remove-ExactSuccessfulWorktree -Run $run -FormalHead ([string]$run.baseCommit) }
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
            $run.canonicalBranch = [string]$transition.stateBranch
            $run.canonicalHead = [string]$transition.formalHead
            try { $notification = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $notificationPath -Kind TaskOutcome -RepositoryRoot $script:root -TaskId ([string]$run.taskId) -Status blocked -RunId ([string]$run.runId) -DetailCode ([string]$candidate.detailCode) 2>$null); $transition.notification = if ($notification.Count -eq 1) { [string]$notification[0] } else { 'failed' } } catch { $transition.notification = 'failed' }
            $transition.cleanup = Remove-ExactSuccessfulWorktree -Run $run -FormalHead ([string]$transition.formalHead)
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
  if ($null -ne $maintenanceAnswered -and $maintenanceAnswered.PSObject.Properties.Name -contains 'contextPath') {
    try {
      $maintenanceRecord = Read-MaintenanceDecisionRecord ([string]$maintenanceAnswered.contextPath)
      Set-MaintenanceDecisionRecordField $maintenanceRecord 'status' 'attention_required'
      Set-MaintenanceDecisionRecordField $maintenanceRecord 'detailCode' $detail
      $null = Write-MaintenanceDecisionRecord $maintenanceRecord
    } catch {}
  }
  $final = [ordered]@{ status = 'failed'; owner = $Owner; detailCode = $detail }
} finally {
  if ($invocationHeld) { $invocationMutex.ReleaseMutex() }
  if ($null -ne $invocationMutex) { $invocationMutex.Dispose() }
}

$final = Add-AttentionNotification -Final $final -Run $run
[Console]::Out.WriteLine(($final | ConvertTo-Json -Compress -Depth 50))
