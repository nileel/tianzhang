#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Canary', 'Candidate')]
  [string]$Action,
  [Parameter(Mandatory = $true)]
  [string]$RepositoryRoot,
  [string]$TaskId,
  [string]$RunId,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [ValidateRange(1, 86400)]
  [int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$capturedSessionId = $null
$modelName = 'deepseek-v4-flash'

function Stop-DeepSeek {
  param([string]$DetailCode)
  $exception = [InvalidOperationException]::new($DetailCode)
  $exception.Data['DetailCode'] = $DetailCode
  throw $exception
}

function Assert-StableText {
  param([AllowNull()][string]$Value, [string]$DetailCode, [int]$MaximumLength = 2000)
  if (
    [string]::IsNullOrWhiteSpace($Value) -or
    $Value -cne $Value.Trim() -or
    $Value.Length -gt $MaximumLength -or
    $Value -match '[\x00-\x1F\x7F]'
  ) { Stop-DeepSeek $DetailCode }
}

function Normalize-FullPath { param([string]$Path) [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }

function Invoke-GitText {
  param([string[]]$Arguments, [string]$DetailCode = 'deepseek_git_failed')
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'
  $startInfo.WorkingDirectory = $script:resolvedRepositoryRoot
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @('-C', $script:resolvedRepositoryRoot) + $Arguments) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) { Stop-DeepSeek $DetailCode }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $null = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()
  if ($exitCode -ne 0) { Stop-DeepSeek $DetailCode }
  $stdout.TrimEnd()
}

function Invoke-PwshJson {
  param([string]$ScriptPath, [string[]]$Arguments, [string]$DetailCode)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>$null)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { Stop-DeepSeek $DetailCode }
  try { $output[0] | ConvertFrom-Json -Depth 100 } catch { Stop-DeepSeek $DetailCode }
}

function Get-ConfiguredBaseUrl {
  $baseUrl = [string]$env:ANTHROPIC_BASE_URL
  if (-not [string]::IsNullOrWhiteSpace($baseUrl)) { return $baseUrl }
  $settingsPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.claude\settings.json'
  if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) { return $null }
  try {
    $settings = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($settingsPath)) | ConvertFrom-Json -Depth 100
    [string]$settings.env.ANTHROPIC_BASE_URL
  } catch { Stop-DeepSeek 'deepseek_identity_unavailable' }
}

function Test-DeepSeekEndpoint {
  param([AllowNull()][string]$BaseUrl)
  if ([string]::IsNullOrWhiteSpace($BaseUrl)) { return $false }
  $uri = $null
  if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri)) { return $false }
  $uri.Scheme -ceq 'http' -and $uri.Host -ceq '127.0.0.1' -and $uri.Port -eq 15721
}

function Read-TaskMetadata {
  param([string]$Path)
  $bytes = [IO.File]::ReadAllBytes($Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
  $match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
  if (-not $match.Success) { Stop-DeepSeek 'deepseek_task_not_ready' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-DeepSeek 'deepseek_task_not_ready' }
  [pscustomobject]@{
    Metadata = $metadata
    Digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
      [Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n"))
    )).ToLowerInvariant()
  }
}

function Get-CandidatePaths {
  param([object]$Metadata)
  $excluded = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  [void]$excluded.Add("开发管理/任务卡/$TaskId.txt")
  [void]$excluded.Add("开发管理/任务归档/$TaskId.txt")
  [void]$excluded.Add('开发管理/当前任务队列.txt')
  [void]$excluded.Add('开发管理/AI合作沟通.txt')
  [void]$excluded.Add([string]$Metadata.sourceBacklog)
  @($Metadata.expectedPaths | ForEach-Object { [string]$_ } | Where-Object { -not $excluded.Contains($_) })
}

function Quote-Single { param([string]$Value) "'" + $Value.Replace("'", "''") + "'" }

function New-CandidatePrompt {
  param([string[]]$CandidatePaths)
  $pathText = $CandidatePaths -join '|'
  $quotedRoot = Quote-Single $script:resolvedRepositoryRoot
  $quotedPaths = Quote-Single $pathText
  @(
    '[TZG_DEEPSEEK_CANDIDATE]'
    "TaskId: $TaskId"
    "RunId: $RunId"
    'Owner: deepseek'
    'Identity: DeepSeek V4 Flash'
    "Model: $modelName"
    "RepositoryRoot: $script:resolvedRepositoryRoot"
    "CandidatePaths: $pathText"
    ''
    'The fixed Windows entry already selected and atomically claimed this exact task. Do not scan the queue, claim another task, or modify runtime.'
    'Read AGENTS.md, CLAUDE.md, 开发管理/自动工作流规则.txt, 开发管理/AI协作规则.txt, 开发管理/DeepSeek工作提示词.txt, and the exact task card.'
    'If the task card stateReason names a REV-* finding or says the task was returned by review, read only the matching review entry routed by 开发管理/审核入口.txt before editing.'
    'Implement and verify only this task in RepositoryRoot. Candidate changes are limited to CandidatePaths.'
    'Do not modify the task card, current queue, source backlog, task archive, AI合作沟通, main workspace, runtime, another worktree, or any branch other than the current candidate branch.'
    'Do not stash, reset, checkout, switch, clean, push, self-review, dispatch another agent, or start/manage Codex automation.'
    'Before committing, run the task-card checks and required path/whitespace/Git checks. Do not claim a verification that was not run.'
    "Create exactly one candidate commit with: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -RepositoryRoot $quotedRoot -ExpectedPaths $quotedPaths -CommitMessage 'candidate($TaskId): DeepSeek implementation'"
    'Do not use RequireAutomationMetadata for the candidate. The fixed Windows entry creates formal business and handoff commits later.'
    'Return only the supplied structured object. completed requires the full candidate SHA, exact changed paths, verified/unverified arrays, residual risk, and the four finalizer-ready metadata values.'
    'Use expectedTransition=codex_review/codex/ready. The four finalizer-ready values must use these exact single-line forms: result="问题=...；完成=...", impact="影响=...；边界=...", verify="验证=...；后续=...", and plain="发生=...；影响=...；需要=...".'
    'needs_decision, blocked or failed must preserve the worktree and return a stable detailCode; do not retry with widened permissions.'
  ) -join "`n"
}

function New-CanaryPrompt {
  @(
    '[TZG_DEEPSEEK_WINDOWS_CANARY]'
    'Read AGENTS.md and CLAUDE.md from the current repository.'
    'Do not read the task queue, claim work, modify files, create commits, or invoke another agent.'
    'Return only the supplied structured object with status=verified, identity=DeepSeek V4 Flash, model=deepseek-v4-flash.'
  ) -join "`n"
}

function New-TerminalSchema {
  if ($Action -ceq 'Canary') {
    return ([ordered]@{
      type = 'object'
      properties = [ordered]@{
        status = [ordered]@{ type = 'string'; enum = @('verified', 'failed') }
        identity = [ordered]@{ type = 'string' }
        model = [ordered]@{ type = 'string' }
        detailCode = [ordered]@{ type = 'string' }
      }
      required = @('status')
      additionalProperties = $false
    } | ConvertTo-Json -Compress -Depth 20)
  }
  ([ordered]@{
    type = 'object'
    properties = [ordered]@{
      status = [ordered]@{ type = 'string'; enum = @('completed', 'needs_decision', 'blocked', 'failed') }
      identity = [ordered]@{ type = 'string' }
      model = [ordered]@{ type = 'string' }
      candidateCommit = [ordered]@{ type = 'string'; minLength = 40; maxLength = 64; pattern = '^[0-9a-f]{40,64}$' }
      expectedTransition = [ordered]@{ type = 'string' }
      changedPaths = [ordered]@{ type = 'array'; items = [ordered]@{ type = 'string' } }
      verified = [ordered]@{ type = 'array'; items = [ordered]@{ type = 'string' } }
      unverified = [ordered]@{ type = 'array'; items = [ordered]@{ type = 'string' } }
      residualRisk = [ordered]@{ type = 'string' }
      result = [ordered]@{ type = 'string' }
      impact = [ordered]@{ type = 'string' }
      verify = [ordered]@{ type = 'string' }
      plain = [ordered]@{ type = 'string' }
      decisionId = [ordered]@{ type = 'string' }
      question = [ordered]@{ type = 'string' }
      options = [ordered]@{ type = 'array'; items = [ordered]@{ type = 'string' }; minItems = 2; maxItems = 3 }
      detailCode = [ordered]@{ type = 'string' }
    }
    required = @('status')
    additionalProperties = $false
  } | ConvertTo-Json -Compress -Depth 20)
}

function Invoke-ClaudeSession {
  param([string]$Executable, [string]$Prompt, [string]$AllowedTools)
  $session = [Guid]::NewGuid().ToString()
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $Executable
  $startInfo.WorkingDirectory = $script:resolvedRepositoryRoot
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @(
      '--session-id', $session, '--print', '--model', $modelName,
      '--output-format', 'json', '--json-schema', (New-TerminalSchema),
      '--permission-mode', 'dontAsk', '--allowedTools', $AllowedTools
    )) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) { Stop-DeepSeek 'deepseek_cli_unavailable' }
  $script:capturedSessionId = $session
  [Console]::Error.WriteLine('session_started')
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($Prompt + "`n")
  $process.StandardInput.Close()
  [Console]::Error.WriteLine('running')
  $timedOut = -not $process.WaitForExit([int]($ResponsibilityTimeoutSeconds * 1000))
  if ($timedOut) {
    try { $process.Kill($true) } catch [InvalidOperationException] { if (-not $process.HasExited) { throw } }
    $process.WaitForExit()
  }
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $null = $stderrTask.GetAwaiter().GetResult()
  $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
  $process.Dispose()
  if ($timedOut) { Stop-DeepSeek 'deepseek_responsibility_timeout' }
  if ($exitCode -ne 0) { Stop-DeepSeek 'deepseek_cli_failed' }
  $text = $stdout.Trim()
  try { $envelope = $text | ConvertFrom-Json -Depth 100 } catch { Stop-DeepSeek 'deepseek_invalid_terminal' }
  foreach ($name in @('type', 'subtype', 'is_error', 'session_id', 'structured_output')) {
    if ($envelope.PSObject.Properties.Name -cnotcontains $name) { Stop-DeepSeek 'deepseek_invalid_terminal' }
  }
  if (
    [string]$envelope.type -cne 'result' -or
    [string]$envelope.subtype -cne 'success' -or
    [bool]$envelope.is_error -or
    [string]$envelope.session_id -cne $session -or
    $null -eq $envelope.structured_output
  ) { Stop-DeepSeek 'deepseek_invalid_terminal' }
  $envelope.structured_output
}

function Assert-StringArray {
  param([object]$Value, [string]$DetailCode)
  if ($Value -is [string] -or $Value -isnot [Collections.IEnumerable]) { Stop-DeepSeek $DetailCode }
  foreach ($item in @($Value)) { Assert-StableText -Value ([string]$item) -DetailCode $DetailCode }
}

function Assert-CandidateEvidence {
  param([object]$Terminal, [string[]]$CandidatePaths, [string]$BaseCommit, [string]$CandidateBranch)
  foreach ($field in @('identity', 'model', 'candidateCommit', 'expectedTransition', 'changedPaths', 'verified', 'unverified', 'residualRisk', 'result', 'impact', 'verify', 'plain')) {
    if ($Terminal.PSObject.Properties.Name -cnotcontains $field) { Stop-DeepSeek 'deepseek_invalid_terminal' }
  }
  if ([string]$Terminal.identity -cne 'DeepSeek V4 Flash' -or [string]$Terminal.model -cne $modelName) { Stop-DeepSeek 'deepseek_identity_mismatch' }
  if ([string]$Terminal.expectedTransition -cne 'codex_review/codex/ready') { Stop-DeepSeek 'deepseek_transition_invalid' }
  $candidateCommit = [string]$Terminal.candidateCommit
  if ($candidateCommit -cnotmatch '^[0-9a-f]{40,64}$') { Stop-DeepSeek 'deepseek_candidate_invalid' }
  if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne $candidateCommit) { Stop-DeepSeek 'deepseek_candidate_invalid' }
  if ((Invoke-GitText @('branch', '--show-current')) -cne $CandidateBranch) { Stop-DeepSeek 'deepseek_candidate_invalid' }
  if ((Invoke-GitText @('rev-list', '--count', "$BaseCommit..$candidateCommit")) -cne '1') { Stop-DeepSeek 'deepseek_candidate_invalid' }
  if ((Invoke-GitText @('rev-parse', "$candidateCommit^")) -cne $BaseCommit) { Stop-DeepSeek 'deepseek_candidate_invalid' }
  if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-DeepSeek 'deepseek_worktree_dirty' }
  $actualPaths = @((Invoke-GitText @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', "$BaseCommit..$candidateCommit")) -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
  if ($actualPaths.Count -eq 0) { Stop-DeepSeek 'deepseek_candidate_empty' }
  foreach ($path in $actualPaths) { if ($CandidatePaths -cnotcontains $path) { Stop-DeepSeek 'deepseek_candidate_path_violation' } }
  Assert-StringArray -Value $Terminal.changedPaths -DetailCode 'deepseek_invalid_terminal'
  $reportedPaths = @($Terminal.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  if (($actualPaths -join "`0") -cne ($reportedPaths -join "`0")) { Stop-DeepSeek 'deepseek_candidate_path_mismatch' }
  Assert-StringArray -Value $Terminal.verified -DetailCode 'deepseek_invalid_terminal'
  Assert-StringArray -Value $Terminal.unverified -DetailCode 'deepseek_invalid_terminal'
  foreach ($field in @('residualRisk', 'result', 'impact', 'verify', 'plain')) { Assert-StableText -Value ([string]$Terminal.$field) -DetailCode 'deepseek_invalid_terminal' }
  if (
    [string]$Terminal.result -cnotmatch '^问题=.+；完成=.+' -or
    [string]$Terminal.impact -cnotmatch '^影响=.+；边界=.+' -or
    [string]$Terminal.verify -cnotmatch '^验证=.+；后续=.+' -or
    [string]$Terminal.plain -cnotmatch '^发生=.+；影响=.+；需要=.+'
  ) { Stop-DeepSeek 'deepseek_candidate_metadata_invalid' }
  [ordered]@{
    category = 'completed'
    expectedTransition = [string]$Terminal.expectedTransition
    changedPaths = $actualPaths
    verified = @($Terminal.verified | ForEach-Object { [string]$_ })
    unverified = @($Terminal.unverified | ForEach-Object { [string]$_ })
    residualRisk = [string]$Terminal.residualRisk
    result = [string]$Terminal.result
    impact = [string]$Terminal.impact
    verify = [string]$Terminal.verify
    plain = [string]$Terminal.plain
  }
}

$result = $null
try {
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { Stop-DeepSeek 'deepseek_repository_invalid' }
  $script:resolvedRepositoryRoot = Normalize-FullPath (Resolve-Path -LiteralPath $RepositoryRoot).Path
  if (-not (Test-Path -LiteralPath (Join-Path $script:resolvedRepositoryRoot '.git'))) { Stop-DeepSeek 'deepseek_repository_invalid' }
  if ((Invoke-GitText @('rev-parse', '--is-inside-work-tree') 'deepseek_repository_invalid') -cne 'true') { Stop-DeepSeek 'deepseek_repository_invalid' }
  if ($PSVersionTable.PSVersion.Major -lt 7) { Stop-DeepSeek 'deepseek_pwsh_unavailable' }
  if (-not (Test-DeepSeekEndpoint (Get-ConfiguredBaseUrl))) { Stop-DeepSeek 'deepseek_identity_unavailable' }
  $claudeCommands = @(Get-Command 'claude.cmd' -CommandType Application -ErrorAction SilentlyContinue)
  if ($claudeCommands.Count -eq 0) { Stop-DeepSeek 'deepseek_cli_unavailable' }
  foreach ($rulePath in @('AGENTS.md', 'CLAUDE.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $script:resolvedRepositoryRoot $rulePath) -PathType Leaf)) { Stop-DeepSeek 'deepseek_rules_unavailable' }
  }

  if ($Action -ceq 'Canary') {
    $beforeHead = Invoke-GitText @('rev-parse', 'HEAD')
    $beforeStatus = Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')
    $terminal = Invoke-ClaudeSession -Executable $claudeCommands[0].Source -Prompt (New-CanaryPrompt) -AllowedTools 'Read'
    if (
      [string]$terminal.status -cne 'verified' -or
      [string]$terminal.identity -cne 'DeepSeek V4 Flash' -or
      [string]$terminal.model -cne $modelName
    ) { Stop-DeepSeek 'deepseek_canary_identity_mismatch' }
    if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne $beforeHead -or (Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')) -cne $beforeStatus) {
      Stop-DeepSeek 'deepseek_canary_modified_repository'
    }
    $result = [ordered]@{
      status = 'verified'; identity = 'DeepSeek V4 Flash'; model = $modelName
      providerEndpointCategory = 'local_deepseek_gateway'; sessionId = $capturedSessionId
      pwshMajor = $PSVersionTable.PSVersion.Major; git = 'available'
    }
  } else {
    Assert-StableText -Value $TaskId -DetailCode 'deepseek_task_invalid'
    Assert-StableText -Value $RunId -DetailCode 'deepseek_run_invalid'
    $runtime = Invoke-PwshJson -ScriptPath $runtimePath -Arguments @('-Action', 'Show', '-StateRoot', $StateRoot) -DetailCode 'deepseek_runtime_mismatch'
    $run = $runtime.state.runs.deepseek
    if (
      [string]$runtime.status -cne 'OK' -or $null -eq $run -or
      [string]$run.runId -cne $RunId -or [string]$run.taskId -cne $TaskId -or
      [string]$run.route -cne 'external_execute' -or [string]$run.state -cne 'developing' -or
      (Normalize-FullPath ([string]$run.worktree)) -cne $script:resolvedRepositoryRoot
    ) { Stop-DeepSeek 'deepseek_runtime_mismatch' }
    if ((Invoke-GitText @('branch', '--show-current')) -cne [string]$run.candidateBranch) { Stop-DeepSeek 'deepseek_worktree_mismatch' }
    if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne [string]$run.baseCommit) { Stop-DeepSeek 'deepseek_worktree_mismatch' }
    if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-DeepSeek 'deepseek_worktree_dirty' }
    $taskCardPath = Join-Path $script:resolvedRepositoryRoot "开发管理/任务卡/$TaskId.txt"
    $task = Read-TaskMetadata -Path $taskCardPath
    if ([string]$task.Digest -cne [string]$run.taskCardDigest) { Stop-DeepSeek 'deepseek_task_changed' }
    $metadata = $task.Metadata
    if ([string]$metadata.id -cne $TaskId -or [string]$metadata.route -cne 'external_execute' -or [string]$metadata.owner -cne 'deepseek' -or [string]$metadata.dispatchState -cne 'ready') {
      Stop-DeepSeek 'deepseek_task_not_ready'
    }
    $candidatePaths = @(Get-CandidatePaths -Metadata $metadata)
    if ($candidatePaths.Count -eq 0) { Stop-DeepSeek 'deepseek_no_candidate_paths' }
    $allowedTools = @(
      'Read', 'Edit', 'Write',
      'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 *)',
      'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 *)',
      'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 *)',
      'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1 *)',
      'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 *)',
      'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1 *)',
      'Bash(dotnet build *)', 'Bash(dotnet run *)', 'Bash(git diff --check)', 'Bash(git status --short)'
    ) -join ','
    $terminal = Invoke-ClaudeSession -Executable $claudeCommands[0].Source -Prompt (New-CandidatePrompt -CandidatePaths $candidatePaths) -AllowedTools $allowedTools
    switch ([string]$terminal.status) {
      'completed' {
        $candidateResult = Assert-CandidateEvidence -Terminal $terminal -CandidatePaths $candidatePaths -BaseCommit ([string]$run.baseCommit) -CandidateBranch ([string]$run.candidateBranch)
        $result = [ordered]@{
          status = 'completed'; taskId = $TaskId; runId = $RunId; identity = 'DeepSeek V4 Flash'
          model = $modelName; sessionId = $capturedSessionId; candidateCommit = [string]$terminal.candidateCommit
          candidateResult = $candidateResult
        }
      }
      'needs_decision' {
        foreach ($field in @('decisionId', 'question', 'options')) { if ($terminal.PSObject.Properties.Name -cnotcontains $field) { Stop-DeepSeek 'deepseek_invalid_terminal' } }
        Assert-StableText -Value ([string]$terminal.decisionId) -DetailCode 'deepseek_invalid_terminal'
        Assert-StableText -Value ([string]$terminal.question) -DetailCode 'deepseek_invalid_terminal'
        Assert-StringArray -Value $terminal.options -DetailCode 'deepseek_invalid_terminal'
        $result = [ordered]@{ status = 'needs_decision'; taskId = $TaskId; runId = $RunId; sessionId = $capturedSessionId; decisionId = [string]$terminal.decisionId; question = [string]$terminal.question; options = @($terminal.options) }
      }
      { $_ -cin @('blocked', 'failed') } {
        if ($terminal.PSObject.Properties.Name -cnotcontains 'detailCode') { Stop-DeepSeek 'deepseek_invalid_terminal' }
        Assert-StableText -Value ([string]$terminal.detailCode) -DetailCode 'deepseek_invalid_terminal'
        $result = [ordered]@{ status = [string]$terminal.status; taskId = $TaskId; runId = $RunId; sessionId = $capturedSessionId; detailCode = [string]$terminal.detailCode }
      }
      default { Stop-DeepSeek 'deepseek_invalid_terminal' }
    }
  }
} catch {
  $detailCode = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'deepseek_wrapper_error' }
  $result = [ordered]@{ status = 'failed'; taskId = $TaskId; runId = $RunId; sessionId = $capturedSessionId; detailCode = $detailCode }
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 30))
