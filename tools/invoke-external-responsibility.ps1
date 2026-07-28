#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Start', 'Resume')]
  [string]$Action,
  [Parameter(Mandatory = $true)]
  [string]$RepositoryRoot,
  [Parameter(Mandatory = $true)]
  [string]$TaskId,
  [Parameter(Mandatory = $true)]
  [string]$RunId,
  [Parameter(Mandatory = $true)]
  [ValidateSet('deepseek', 'claude')]
  [string]$Owner,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [string]$SessionId,
  [ValidateSet('A', 'B', 'C')]
  [string]$DecisionOption,
  [ValidateRange(1, 86400)]
  [int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$metadataContractPath = Join-Path $PSScriptRoot 'automation-commit-metadata.ps1'
$capturedSessionId = $null
$sessionToUse = $null
$result = $null

if (-not (Test-Path -LiteralPath $metadataContractPath -PathType Leaf)) {
  throw 'Automation commit metadata contract is unavailable'
}
. $metadataContractPath

function Stop-External {
  param([string]$DetailCode)
  $exception = [InvalidOperationException]::new($DetailCode)
  $exception.Data['DetailCode'] = $DetailCode
  throw $exception
}

function Assert-StableText {
  param([string]$Value, [string]$DetailCode, [int]$MaximumLength = 512)
  if (
    [string]::IsNullOrWhiteSpace($Value) -or
    $Value -cne $Value.Trim() -or
    $Value.Length -gt $MaximumLength -or
    $Value -match '[\x00-\x1F\x7F]'
  ) {
    Stop-External $DetailCode
  }
}

function Normalize-FullPath {
  param([string]$Path)
  [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-PathWithin {
  param([string]$Path, [string]$Root)
  $normalizedPath = Normalize-FullPath $Path
  $normalizedRoot = Normalize-FullPath $Root
  $prefix = $normalizedRoot + [IO.Path]::DirectorySeparatorChar
  $normalizedPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-GitRoot {
  param([string]$Path)
  $output = @(& git -C $Path rev-parse --show-toplevel 2>$null)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
    Stop-External 'external_repository_invalid'
  }
  Normalize-FullPath ([string]$output[0])
}

function Invoke-GitUtf8Text {
  param([string]$Path, [string[]]$Arguments, [string]$DetailCode)

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'
  $startInfo.WorkingDirectory = $Path
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    Stop-External $DetailCode
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $null = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()
  if ($exitCode -ne 0) {
    Stop-External $DetailCode
  }
  $stdout.TrimEnd()
}

function Invoke-PwshJson {
  param([string]$ScriptPath, [string[]]$Arguments, [string]$DetailCode)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>$null)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
    Stop-External $DetailCode
  }
  try {
    $output[0] | ConvertFrom-Json -Depth 100
  } catch {
    Stop-External $DetailCode
  }
}

function Get-ConfiguredBaseUrl {
  $baseUrl = [string]$env:ANTHROPIC_BASE_URL
  if (-not [string]::IsNullOrWhiteSpace($baseUrl)) {
    return $baseUrl
  }
  $settingsPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.claude\settings.json'
  if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    return $null
  }
  try {
    $settings = [IO.File]::ReadAllText(
      $settingsPath,
      [Text.UTF8Encoding]::new($false, $true)
    ) | ConvertFrom-Json -Depth 100
    [string]$settings.env.ANTHROPIC_BASE_URL
  } catch {
    Stop-External 'external_identity_unavailable'
  }
}

function Test-DeepSeekEndpoint {
  param([AllowNull()][string]$BaseUrl)
  if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    return $false
  }
  $uri = $null
  if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri)) {
    return $false
  }
  $uri.Scheme -ceq 'http' -and
    $uri.Host -ceq '127.0.0.1' -and
    $uri.Port -eq 15721
}

function Quote-Single {
  param([string]$Value)
  "'" + $Value.Replace("'", "''") + "'"
}

function New-ExternalPrompt {
  param(
    [string]$ResolvedRepositoryRoot,
    [string]$Identity,
    [string]$BaselinePath,
    [string[]]$ExpectedPaths,
    [string]$Session
  )
  $expectedPathText = $ExpectedPaths -join '|'
  $quotedRoot = Quote-Single $ResolvedRepositoryRoot
  $quotedBaseline = Quote-Single $BaselinePath
  $quotedExpectedPaths = Quote-Single $expectedPathText
  $quotedTaskId = Quote-Single $TaskId
  @(
    '[TZG_EXTERNAL_RESPONSIBILITY]'
    "TaskId: $TaskId"
    "RunId: $RunId"
    "Owner: $Owner"
    "Identity: $Identity"
    "SessionId: $Session"
    "RepositoryRoot: $ResolvedRepositoryRoot"
    "ExpectedPaths: $expectedPathText"
    "BaselinePath: $BaselinePath"
    ''
    'The hourly controller already selected this exact external_execute task and holds its single-writer lease.'
    'Work directly in RepositoryRoot on its current branch. Do not create or switch worktrees or branches, and do not rescan the queue for another task.'
    'Read AGENTS.md, 开发管理/自动工作流规则.txt, 开发管理/AI协作规则.txt, 开发管理/DeepSeek工作提示词.txt, 开发管理/当前任务队列.txt, and the exact task card 开发管理/任务卡/' + $TaskId + '.txt.'
    'Before any repository change, run these exact commands separately:'
    "pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Snapshot -RepositoryRoot $quotedRoot -BaselinePath $quotedBaseline"
    "pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Check -RepositoryRoot $quotedRoot -BaselinePath $quotedBaseline -ExpectedPaths $quotedExpectedPaths"
    'Implement only the selected task-card scope, run its minimum sufficient checks, and keep every change within ExpectedPaths.'
    'On success, update the same task card and its canonical queue/backlog projection to route=codex_review, owner=codex, dispatchState=ready without changing the task ID or body.'
    "After that transition and before creating businessCommit, run pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -RepositoryRoot $quotedRoot -TaskId $quotedTaskId -Postcondition ExternalPendingReview -OutputJson. Continue only when it returns status=ok."
    'Create the path-limited businessCommit with tools/automation-finalize-commit.ps1 and RequireAutomationMetadata. Use these exact single-line structures: AutomationResult 问题=<原问题>；完成=<具体交付>, AutomationImpact 影响=<实际行为变化>；边界=<明确未涉及范围>, AutomationVerify 验证=<关键检查与结果>；后续=<解锁项、剩余依赖或下一状态>, AutomationPlain 发生=<负责人短句>；影响=<负责人短句>；需要=<负责人短句>. AutomationTask must equal this TaskId and AutomationState must be pending_review.'
    'Then modify only 开发管理/AI合作沟通.txt to record the real business SHA, verified and unverified work, and residual risk; create the handoffCommit with the same finalizer but without Automation metadata or repeated domain checks.'
    'Do not call hourly-automation-lease.ps1, self-review, widen paths, dispatch another agent, push, stash, reset, checkout, clean, or retry a failed command.'
    'Return only the structured object required by the supplied JSON schema. The wrapper uses the Claude CLI result envelope as the authoritative session ID.'
    'completed requires status, identity matching Identity, and the full 40-character lowercase hexadecimal SHA for both businessCommit and handoffCommit. Never return abbreviated Git SHAs.'
    'needs_decision requires status, stable decisionId, one question, and two or three A/B/C options.'
    'blocked or failed requires status and a stable detailCode. If sessionId is included, it must match SessionId.'
  ) -join "`n"
}

function New-TerminalSchema {
  ([ordered]@{
    type = 'object'
    properties = [ordered]@{
      status = [ordered]@{
        type = 'string'
        enum = @('completed', 'needs_decision', 'blocked', 'failed')
      }
      identity = [ordered]@{ type = 'string' }
      sessionId = [ordered]@{ type = 'string' }
      businessCommit = [ordered]@{
        type = 'string'
        minLength = 40
        maxLength = 40
        pattern = '[0-9a-f]{40}$'
      }
      handoffCommit = [ordered]@{
        type = 'string'
        minLength = 40
        maxLength = 40
        pattern = '[0-9a-f]{40}$'
      }
      decisionId = [ordered]@{ type = 'string' }
      question = [ordered]@{ type = 'string' }
      options = [ordered]@{
        type = 'array'
        items = [ordered]@{ type = 'string' }
        minItems = 2
        maxItems = 3
      }
      detailCode = [ordered]@{ type = 'string' }
    }
    required = @('status')
    additionalProperties = $false
  } | ConvertTo-Json -Compress -Depth 20)
}

function Invoke-ClaudeSession {
  param(
    [string]$Executable,
    [string]$InputText,
    [string]$Session,
    [string]$AllowedTools,
    [string]$TerminalSchema
  )
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
  if ($Action -ceq 'Start') {
    $startInfo.ArgumentList.Add('--session-id')
  } else {
    $startInfo.ArgumentList.Add('--resume')
  }
  $startInfo.ArgumentList.Add($Session)
  $startInfo.ArgumentList.Add('--print')
  $startInfo.ArgumentList.Add('--output-format')
  $startInfo.ArgumentList.Add('json')
  $startInfo.ArgumentList.Add('--json-schema')
  $startInfo.ArgumentList.Add($TerminalSchema)
  $startInfo.ArgumentList.Add('--permission-mode')
  $startInfo.ArgumentList.Add('dontAsk')
  $startInfo.ArgumentList.Add('--allowedTools')
  $startInfo.ArgumentList.Add($AllowedTools)

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    Stop-External 'external_cli_unavailable'
  }
  $script:capturedSessionId = $Session
  [Console]::Error.WriteLine('session_started')
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($InputText)
  $process.StandardInput.Close()
  [Console]::Error.WriteLine('running')
  $timedOut = -not $process.WaitForExit([int]($ResponsibilityTimeoutSeconds * 1000))
  if ($timedOut) {
    try {
      $process.Kill($true)
    } catch [InvalidOperationException] {
      if (-not $process.HasExited) {
        throw
      }
    }
    $process.WaitForExit()
  }
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $null = $stderrTask.GetAwaiter().GetResult()
  $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
  $process.Dispose()
  [pscustomobject]@{
    ExitCode = $exitCode
    Stdout = $stdout
    TimedOut = $timedOut
  }
}

function Assert-SessionId {
  param([string]$Value)
  $parsed = [Guid]::Empty
  if (-not [Guid]::TryParse($Value, [ref]$parsed)) {
    Stop-External 'external_session_invalid'
  }
}

function Assert-CommitSha {
  param([string]$Value)
  if ([string]::IsNullOrWhiteSpace($Value) -or $Value -cnotmatch '^[0-9a-f]{40,64}$') {
    Stop-External 'external_invalid_terminal'
  }
}

try {
  foreach ($argument in @(
      @{ Value = $RepositoryRoot; Code = 'external_repository_invalid' },
      @{ Value = $TaskId; Code = 'external_task_invalid' },
      @{ Value = $RunId; Code = 'external_lease_mismatch' },
      @{ Value = $StateRoot; Code = 'external_state_root_invalid' }
    )) {
    Assert-StableText -Value $argument.Value -DetailCode $argument.Code
  }
  $parsedRunId = [Guid]::Empty
  if (-not [Guid]::TryParse($RunId, [ref]$parsedRunId)) {
    Stop-External 'external_lease_mismatch'
  }
  if ($Action -ceq 'Start') {
    if (
      -not [string]::IsNullOrWhiteSpace($SessionId) -or
      -not [string]::IsNullOrWhiteSpace($DecisionOption)
    ) {
      Stop-External 'external_arguments_invalid'
    }
  } else {
    Assert-StableText -Value $SessionId -DetailCode 'external_session_invalid'
    Assert-SessionId $SessionId
    if ([string]::IsNullOrWhiteSpace($DecisionOption)) {
      Stop-External 'external_decision_invalid'
    }
    $capturedSessionId = $SessionId
    $sessionToUse = $SessionId
  }

  $resolvedRepositoryRoot = Normalize-FullPath (Resolve-Path -LiteralPath $RepositoryRoot).Path
  $repositoryGitRoot = Invoke-GitRoot $resolvedRepositoryRoot
  if ($repositoryGitRoot -cne $resolvedRepositoryRoot) {
    Stop-External 'external_repository_invalid'
  }
  $workingGitRoot = Invoke-GitRoot ([Environment]::CurrentDirectory)
  if ($workingGitRoot -cne $resolvedRepositoryRoot) {
    Stop-External 'external_repository_mismatch'
  }

  if (-not (Test-Path -LiteralPath $leasePath -PathType Leaf)) {
    Stop-External 'external_lease_tool_unavailable'
  }
  $resolvedStateRoot = Normalize-FullPath $StateRoot
  if (Test-PathWithin -Path $resolvedStateRoot -Root $resolvedRepositoryRoot) {
    Stop-External 'external_state_root_invalid'
  }
  $lease = Invoke-PwshJson `
    -ScriptPath $leasePath `
    -Arguments @('-Action', 'Show', '-StateRoot', $resolvedStateRoot) `
    -DetailCode 'external_lease_mismatch'
  $leaseRepositoryRoot = if ($null -eq $lease.state.lease) {
    $null
  } else {
    Normalize-FullPath ([string]$lease.state.lease.repositoryRoot)
  }
  if (
    [string]$lease.status -cne 'OK' -or
    [string]$lease.leaseStatus -cne 'active' -or
    $null -eq $lease.state.lease -or
    [string]$lease.state.lease.runId -cne $RunId -or
    [string]$lease.state.lease.taskId -cne $TaskId -or
    [string]$lease.state.lease.owner -cne $Owner -or
    $leaseRepositoryRoot -cne $resolvedRepositoryRoot
  ) {
    Stop-External 'external_lease_mismatch'
  }

  $requiredRepositoryTools = @(
    'tools/automation-workspace-guard.ps1'
    'tools/automation-commit-metadata.ps1'
    'tools/check-pending-whitespace.ps1'
    'tools/check-task-cards.ps1'
    'tools/automation-finalize-commit.ps1'
  )
  foreach ($relativePath in $requiredRepositoryTools) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedRepositoryRoot $relativePath) -PathType Leaf)) {
      Stop-External 'external_wrapper_dependency_missing'
    }
  }
  $taskEvidence = Invoke-PwshJson `
    -ScriptPath (Join-Path $resolvedRepositoryRoot 'tools/check-task-cards.ps1') `
    -Arguments @(
      '-RepositoryRoot', $resolvedRepositoryRoot,
      '-TaskId', $TaskId,
      '-Postcondition', 'ExternalDispatchReady',
      '-ExpectedOwner', $Owner,
      '-OutputJson'
    ) `
    -DetailCode 'external_task_not_ready'
  $expectedPaths = @($taskEvidence.expectedPaths | ForEach-Object { [string]$_ })
  if (
    [string]$taskEvidence.status -cne 'ok' -or
    [string]$taskEvidence.taskState -cne 'ready' -or
    $expectedPaths.Count -eq 0
  ) {
    Stop-External 'external_task_not_ready'
  }

  $baseUrl = Get-ConfiguredBaseUrl
  $isDeepSeek = Test-DeepSeekEndpoint $baseUrl
  $identity = if ($isDeepSeek) { 'DeepSeek V4 Pro' } else { 'Claude Code' }
  if (
    ($Owner -ceq 'deepseek' -and -not $isDeepSeek) -or
    ($Owner -ceq 'claude' -and $isDeepSeek)
  ) {
    Stop-External 'external_identity_unavailable'
  }

  $claudeCommands = @(Get-Command 'claude.cmd' -CommandType Application -ErrorAction SilentlyContinue)
  if ($claudeCommands.Count -eq 0) {
    Stop-External 'external_cli_unavailable'
  }
  $claude = $claudeCommands[0]
  $baselineDirectory = Normalize-FullPath (Join-Path $resolvedStateRoot 'external-baselines')
  $baselinePath = Normalize-FullPath (Join-Path $baselineDirectory "$RunId.json")
  if (
    -not (Test-PathWithin -Path $baselinePath -Root $resolvedStateRoot) -or
    (Test-PathWithin -Path $baselinePath -Root $resolvedRepositoryRoot)
  ) {
    Stop-External 'external_baseline_invalid'
  }
  [IO.Directory]::CreateDirectory($baselineDirectory) | Out-Null
  if ($Action -ceq 'Start') {
    $sessionToUse = [Guid]::NewGuid().ToString()
  }

  $allowedTools = @(
    'Read'
    'Edit'
    'Write'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 *)'
    'Bash(git diff --check)'
  ) -join ','
  $inputText = if ($Action -ceq 'Start') {
    New-ExternalPrompt `
      -ResolvedRepositoryRoot $resolvedRepositoryRoot `
      -Identity $identity `
      -BaselinePath $baselinePath `
      -ExpectedPaths $expectedPaths `
      -Session $sessionToUse
  } else {
    $DecisionOption
  }
  $sessionResult = Invoke-ClaudeSession `
    -Executable $claude.Source `
    -InputText ($inputText + "`n") `
    -Session $sessionToUse `
    -AllowedTools $allowedTools `
    -TerminalSchema (New-TerminalSchema)
  if ($sessionResult.TimedOut) {
    Stop-External 'external_responsibility_timeout'
  }
  if ($sessionResult.ExitCode -ne 0) {
    Stop-External 'external_cli_failed'
  }
  $envelopeText = ([string]$sessionResult.Stdout).Trim()
  if (-not ($envelopeText.StartsWith('{') -and $envelopeText.EndsWith('}'))) {
    Stop-External 'external_invalid_terminal'
  }
  try {
    $envelope = $envelopeText | ConvertFrom-Json -Depth 100
  } catch {
    Stop-External 'external_invalid_terminal'
  }
  if (
    [string]$envelope.type -cne 'result' -or
    [string]$envelope.subtype -cne 'success' -or
    [bool]$envelope.is_error -or
    [string]$envelope.session_id -cne $capturedSessionId -or
    $null -eq $envelope.structured_output
  ) {
    Stop-External 'external_invalid_terminal'
  }
  $terminal = $envelope.structured_output
  if (
    $terminal.PSObject.Properties.Name -contains 'sessionId' -and
    -not [string]::IsNullOrWhiteSpace([string]$terminal.sessionId) -and
    [string]$terminal.sessionId -cne $capturedSessionId
  ) {
    Stop-External 'external_session_mismatch'
  }

  switch ([string]$terminal.status) {
    'completed' {
      if ([string]$terminal.identity -cne $identity) {
        Stop-External 'external_identity_mismatch'
      }
      Assert-CommitSha ([string]$terminal.businessCommit)
      Assert-CommitSha ([string]$terminal.handoffCommit)
      if ([string]$terminal.businessCommit -ceq [string]$terminal.handoffCommit) {
        Stop-External 'external_invalid_terminal'
      }
      try {
        $businessBody = Invoke-GitUtf8Text `
          -Path $resolvedRepositoryRoot `
          -Arguments @('show', '-s', '--format=%B', [string]$terminal.businessCommit) `
          -DetailCode 'external_commit_metadata_invalid'
        $null = ConvertFrom-TzgAutomationCommitMessage `
          -Message $businessBody `
          -ExpectedTask $TaskId `
          -ExpectedState 'pending_review'
      } catch {
        Stop-External 'external_commit_metadata_invalid'
      }
      $result = [ordered]@{
        status = 'completed'
        taskId = $TaskId
        runId = $RunId
        identity = $identity
        sessionId = $capturedSessionId
        businessCommit = [string]$terminal.businessCommit
        handoffCommit = [string]$terminal.handoffCommit
      }
    }
    'needs_decision' {
      Assert-StableText -Value ([string]$terminal.decisionId) -DetailCode 'external_invalid_terminal'
      Assert-StableText -Value ([string]$terminal.question) -DetailCode 'external_invalid_terminal' -MaximumLength 1000
      $options = @($terminal.options | ForEach-Object { [string]$_ })
      if ($options.Count -lt 2 -or $options.Count -gt 3) {
        Stop-External 'external_invalid_terminal'
      }
      foreach ($option in $options) {
        Assert-StableText -Value $option -DetailCode 'external_invalid_terminal' -MaximumLength 200
      }
      $result = [ordered]@{
        status = 'needs_decision'
        taskId = $TaskId
        runId = $RunId
        sessionId = $capturedSessionId
        decisionId = [string]$terminal.decisionId
        question = [string]$terminal.question
        options = $options
      }
    }
    { $_ -cin @('blocked', 'failed') } {
      Assert-StableText -Value ([string]$terminal.detailCode) -DetailCode 'external_invalid_terminal'
      $result = [ordered]@{
        status = [string]$terminal.status
        taskId = $TaskId
        runId = $RunId
        sessionId = $capturedSessionId
        detailCode = [string]$terminal.detailCode
      }
    }
    default {
      Stop-External 'external_invalid_terminal'
    }
  }
} catch {
  $detailCode = if ($_.Exception.Data.Contains('DetailCode')) {
    [string]$_.Exception.Data['DetailCode']
  } else {
    'external_wrapper_error'
  }
  $result = [ordered]@{
    status = 'failed'
    taskId = $TaskId
    runId = $RunId
    sessionId = $capturedSessionId
    detailCode = $detailCode
  }
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 20))
