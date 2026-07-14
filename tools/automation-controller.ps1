[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Contract','Start','RegisterCandidate','BeginMutation','Renew','Finish','CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure','CreateDecision','MarkDecisionNotified','MarkDecisionDeliveryFailed','ResolveDecisionReply')]
  [string]$Action,
  [string]$RepositoryRoot = (Get-Location).Path,
  [string]$StatePath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json",
  [string]$RunRoot = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller-runs",
  [string]$RunId,
  [string]$ActualModel,
  [string]$WorkType,
  [string]$TaskId,
  [string]$Executor,
  [string]$ExpectedPaths,
  [string]$CommitMessage,
  [string]$ErrorMessage,
  [string]$QueueFingerprint,
  [int]$RunnableCount = -1,
  [switch]$QueueAuditCompleted,
  [switch]$NoCandidate,
  [string]$WorkerError,
  [int]$BackoffMinutes = 180,
  [string]$TaskSummary,
  [string]$DecisionQuestion,
  [string]$DecisionOptions,
  [string]$RecommendedOption,
  [string]$ImpactSummary,
  [string]$ReplyText,
  [string]$NotificationError,
  [int]$LeaseMinutes = 180,
  [string]$Now
)

$ErrorActionPreference = 'Stop'
$script:ProtocolVersion = 1
$script:StateTool = Join-Path $PSScriptRoot 'automation-controller-state.ps1'
$script:GuardTool = Join-Path $PSScriptRoot 'automation-workspace-guard.ps1'
$script:FinalizerTool = Join-Path $PSScriptRoot 'automation-finalize-commit.ps1'
$script:TaskKindMapping = [ordered]@{
  execution = 'execute'
  review = 'review'
  maintenance = 'maintenance'
  recovery = 'recovery'
}

function Invoke-ChildPowerShell {
  param([string]$ScriptPath, [string[]]$Arguments)

  $positionals = [Collections.Generic.List[string]]::new()
  $parameters = [ordered]@{}
  $switchNames = @('WasRecovery', 'QueueAuditCompleted', 'NoCandidate', 'ManualOverride')
  for ($index = 0; $index -lt $Arguments.Count; $index++) {
    $token = [string]$Arguments[$index]
    if (-not $token.StartsWith('-', [StringComparison]::Ordinal)) {
      $positionals.Add($token)
      continue
    }
    $name = $token.Substring(1)
    if ($switchNames -contains $name) {
      $parameters[$name] = $true
      continue
    }
    if ($index + 1 -ge $Arguments.Count) { throw "Missing value for child parameter: $token" }
    $parameters[$name] = [string]$Arguments[++$index]
  }
  $request = [pscustomobject]@{
    scriptPath = $ScriptPath
    positionals = @($positionals)
    parameters = [pscustomobject]$parameters
  } | ConvertTo-Json -Depth 4 -Compress
  $requestBase64 = [Convert]::ToBase64String([Text.UTF8Encoding]::new($false).GetBytes($request))
  $command = @"
`$utf8 = [Text.UTF8Encoding]::new(`$false)
[Console]::OutputEncoding = `$utf8
`$OutputEncoding = `$utf8
`$requestJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$requestBase64'))
`$request = `$requestJson | ConvertFrom-Json -DateKind String
`$childPositionals = @(`$request.positionals | ForEach-Object { [string]`$_ })
`$childParameters = @{}
foreach (`$property in `$request.parameters.PSObject.Properties) { `$childParameters[[string]`$property.Name] = `$property.Value }
& ([string]`$request.scriptPath) @childPositionals @childParameters
exit `$LASTEXITCODE
"@
  $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = (Get-Process -Id $PID).Path
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false, $true)
  $startInfo.CreateNoWindow = $true
  $allArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encodedCommand)
  foreach ($argument in $allArguments) { $startInfo.ArgumentList.Add([string]$argument) }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  try {
    if (-not $process.Start()) { throw "Unable to start child PowerShell: $ScriptPath" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [pscustomobject]@{
      Code = $process.ExitCode
      Output = $stdoutTask.GetAwaiter().GetResult().Trim()
      Error = $stderrTask.GetAwaiter().GetResult().Trim()
    }
  } finally {
    $process.Dispose()
  }
}

function Invoke-StateTool {
  param([string[]]$Arguments)
  Invoke-ChildPowerShell $script:StateTool (@($Arguments) + @('-StatePath', $StatePath, '-LeaseMinutes', [string]$LeaseMinutes) + $(if ($Now) { @('-Now', $Now) } else { @() }))
}

function Invoke-GuardTool {
  param([string[]]$Arguments)
  Invoke-ChildPowerShell $script:GuardTool (@($Arguments) + @('-RepositoryRoot', $RepositoryRoot))
}

function Convert-ChildJson {
  param($Result, [string]$Label)

  if ([string]::IsNullOrWhiteSpace([string]$Result.Output)) {
    $detail = if ([string]::IsNullOrWhiteSpace([string]$Result.Error)) { '<no stderr>' } else { [string]$Result.Error }
    throw "$Label returned no JSON: $detail"
  }
  try { $Result.Output | ConvertFrom-Json } catch { throw "$Label returned invalid JSON: $($Result.Output)" }
}

function New-ProtocolResult {
  param(
    [bool]$Ok,
    [string]$NextAction,
    [string]$BranchKind,
    [string]$FailurePolicy,
    [AllowNull()][string]$ErrorCode,
    [AllowNull()][string]$Message
  )

  [ordered]@{
    protocolVersion = $script:ProtocolVersion
    ok = $Ok
    action = $NextAction
    runId = if ([string]::IsNullOrWhiteSpace($RunId)) { $null } else { $RunId }
    branchKind = $BranchKind
    taskId = $null
    executor = $null
    expectedPaths = @()
    requiredSources = @()
    requiredChecks = @()
    nextCommand = $null
    failurePolicy = $FailurePolicy
    errorCode = $ErrorCode
    message = $Message
  }
}

function Write-ProtocolResult {
  param([System.Collections.IDictionary]$Result, [int]$ExitCode = 0)

  [pscustomobject]$Result | ConvertTo-Json -Depth 8 -Compress
  exit $ExitCode
}

function Get-SessionPath {
  if ([string]::IsNullOrWhiteSpace($RunId)) { throw 'RunId is required' }
  Join-Path $RunRoot "$RunId.json"
}

function Read-Session {
  $path = Get-SessionPath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Run session does not exist: $RunId" }
  $session = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
  if ($session.protocolVersion -ne $script:ProtocolVersion -or $session.runId -ne $RunId) {
    throw 'Run session is invalid or belongs to another run.'
  }
  $session
}

function Save-Session {
  param($Session)
  Write-JsonAtomically $Session (Get-SessionPath)
}

function Remove-SessionFile {
  $path = Get-SessionPath
  if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
}

function Write-JsonAtomically {
  param([object]$Value, [string]$Path)

  $directory = Split-Path -Parent $Path
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $temporary = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  try {
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($Path)) {
      $backup = "$Path.backup"
      Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
      [IO.File]::Replace($temporary, $Path, $backup, $true)
      Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    } else {
      [IO.File]::Move($temporary, $Path)
    }
  } finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
  }
}

function Test-ValidRunId {
  param([string]$Value)

  $parsed = [guid]::Empty
  [guid]::TryParse($Value, [ref]$parsed) -and $parsed -ne [guid]::Empty
}

function Close-StartFailure {
  param([string]$Code, [string]$Message, [int]$ExitCode = 1)

  if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    [void](Invoke-StateTool @('Fail', '-RunId', $RunId, '-ErrorMessage', $Message))
  }
  $result = New-ProtocolResult $false 'stopped' 'none' 'close_empty_run' $Code $Message
  Write-ProtocolResult $result $ExitCode
}

function Close-EmptyRun {
  param([string]$Code, [string]$Message, [int]$ExitCode, [string]$FailurePolicy = 'close_empty_run')

  $failed = Invoke-StateTool @('Fail', '-RunId', $RunId, '-ErrorMessage', $Message)
  if ($failed.Code -ne 0) {
    $result = New-ProtocolResult $false 'stopped' 'none' 'preserve_recovery' 'fail_close_error' $(if ($failed.Error) { $failed.Error } else { 'State Fail failed.' })
    Write-ProtocolResult $result $failed.Code
  }
  Remove-SessionFile
  $result = New-ProtocolResult $false 'stopped' 'none' $FailurePolicy $Code $Message
  Write-ProtocolResult $result $ExitCode
}

function Get-BranchSources {
  param([string]$Branch, [string]$SelectedExecutor)

  $sources = [Collections.Generic.List[string]]::new()
  switch ($Branch) {
    'execution' {
      $sources.Add('开发管理/当前任务队列.txt')
      $sources.Add('开发管理/AI协作规则.txt')
    }
    'review' { $sources.Add('开发管理/审核入口.txt') }
    'maintenance' {
      $sources.Add('开发管理/状态与建议维护规则.txt')
      $sources.Add('开发管理/当前任务队列.txt')
    }
    'recovery' {
      $sources.Add('开发管理/当前任务队列.txt')
      $sources.Add('开发管理/AI协作规则.txt')
    }
  }
  if ($SelectedExecutor -eq 'deepseek') {
    if (-not $sources.Contains('开发管理/AI协作规则.txt')) { $sources.Add('开发管理/AI协作规则.txt') }
    $sources.Add('开发管理/DeepSeek工作提示词.txt')
  }
  @($sources | Select-Object -Unique)
}

function Get-ExternalWorkType {
  param([string]$InternalTaskKind)

  foreach ($entry in $script:TaskKindMapping.GetEnumerator()) {
    if ([string]$entry.Value -eq $InternalTaskKind) { return [string]$entry.Key }
  }
  throw "Unknown internal TaskKind: $InternalTaskKind"
}

function Get-StateSnapshot {
  $shown = Invoke-StateTool @('Show')
  if ($shown.Code -ne 0) { throw $(if ($shown.Error) { $shown.Error } else { 'State Show failed.' }) }
  Convert-ChildJson $shown 'state Show'
}

function Get-RecoveryFailurePolicy {
  param($State)
  if ($State.state -eq 'AUTO-BLOCKED') { 'auto_blocked' } else { 'preserve_recovery' }
}

function Get-NowValue {
  if ([string]::IsNullOrWhiteSpace($Now)) { return [DateTimeOffset]::UtcNow }
  [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture)
}

function Test-DeepSeekBackoffActive {
  param($State)

  $until = [string]$State.workerState.deepseek.backoffUntil
  if ([string]::IsNullOrWhiteSpace($until)) { return $false }
  [DateTimeOffset]::Parse($until, [Globalization.CultureInfo]::InvariantCulture) -gt (Get-NowValue)
}

function Remove-RunArtifacts {
  param($Session)

  $rootPath = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\', '/')
  $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
  $candidates = @(
    (Get-SessionPath),
    [string]$Session.currentBaselinePath,
    [string]$Session.baselinePath,
    [string]$Session.evidencePath
  ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
  foreach ($candidate in $candidates) {
    $fullPath = [IO.Path]::GetFullPath([string]$candidate)
    if ($fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
      Remove-Item -LiteralPath $fullPath -Force
    }
  }
}

function Stop-RegisteredWork {
  param($Session, [string]$Code, [string]$Message, [int]$ExitCode)

  $arguments = @('Fail', '-RunId', $RunId, '-ErrorMessage', $Message)
  if ([bool]$Session.isRecovery) { $arguments += '-WasRecovery' }
  $failed = Invoke-StateTool $arguments
  $failedState = if ($failed.Code -eq 0) { Convert-ChildJson $failed 'state Fail' } else { $null }
  $policy = if ($null -ne $failedState) { Get-RecoveryFailurePolicy $failedState } else { 'preserve_recovery' }
  $result = New-ProtocolResult $false 'stopped' ([string]$Session.branchKind) $policy $Code $Message
  if ($null -ne $failedState) {
    $result.taskId = [string]$failedState.taskId
    $result.executor = [string]$failedState.taskExecutor
    $result.expectedPaths = @($failedState.expectedPaths)
  }
  Write-ProtocolResult $result $ExitCode
}

try {
  switch ($Action) {
    'Start' {
      if ([string]::IsNullOrWhiteSpace($ActualModel) -or $ActualModel -eq 'unknown') {
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'identity_unknown' 'Actual model identity is unavailable.'
        Write-ProtocolResult $result 15
      }
      if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = [guid]::NewGuid().ToString() }
      if (-not (Test-ValidRunId $RunId)) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'invalid_run_id' 'RunId must be a non-empty UUID.'
        Write-ProtocolResult $result 15
      }

      New-Item -ItemType Directory -Path $RunRoot -Force | Out-Null
      $acquire = Invoke-StateTool @('Acquire', '-RunId', $RunId)
      if ($acquire.Code -eq 10) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'lease_busy' 'Another run owns the active lease.'
        Write-ProtocolResult $result 10
      }
      if ($acquire.Code -eq 11) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'auto_blocked' 'auto_blocked' 'Controller is AUTO-BLOCKED.'
        Write-ProtocolResult $result 11
      }
      if ($acquire.Code -ne 0) {
        $message = if ($acquire.Error) { $acquire.Error } else { 'State Acquire failed.' }
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'state_error' $message
        Write-ProtocolResult $result $acquire.Code
      }
      $acquiredState = Convert-ChildJson $acquire 'state Acquire'

      $currentBaselinePath = Join-Path $RunRoot "$RunId.baseline.json"
      $snapshot = Invoke-GuardTool @('Snapshot', '-BaselinePath', $currentBaselinePath)
      if ($snapshot.Code -ne 0) {
        $message = if ($snapshot.Error) { $snapshot.Error } else { 'Workspace Snapshot failed.' }
        Close-StartFailure 'snapshot_failed' $message $snapshot.Code
      }

      $hasRecovery = (
        -not [string]::IsNullOrWhiteSpace([string]$acquiredState.taskKind) -or
        -not [string]::IsNullOrWhiteSpace([string]$acquiredState.taskId) -or
        @($acquiredState.expectedPaths).Count -gt 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryBaselinePath) -or
        -not [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryEvidencePath) -or
        -not [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryEvidenceHash)
      )
      if ($hasRecovery) {
        if ([string]::IsNullOrWhiteSpace([string]$acquiredState.taskKind) -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.taskId) -or
            @($acquiredState.expectedPaths).Count -eq 0 -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryBaselinePath) -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryEvidencePath) -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryEvidenceHash)) {
          [void](Invoke-StateTool @('Fail', '-RunId', $RunId, '-WasRecovery', '-ErrorMessage', 'recovery_state_incomplete'))
          $result = New-ProtocolResult $false 'stopped' 'recovery' 'preserve_recovery' 'recovery_state_incomplete' 'Recovery state is incomplete.'
          Write-ProtocolResult $result 1
        }
        try {
          $evidence = [IO.File]::ReadAllText([string]$acquiredState.recoveryEvidencePath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
          if ([string]$evidence.payloadHash -cne [string]$acquiredState.recoveryEvidenceHash) { throw 'Recovery evidence hash does not match state.' }
        } catch {
          [void](Invoke-StateTool @('Fail', '-RunId', $RunId, '-WasRecovery', '-ErrorMessage', 'recovery_evidence_invalid'))
          $result = New-ProtocolResult $false 'stopped' 'recovery' 'preserve_recovery' 'recovery_evidence_invalid' $_.Exception.Message
          Write-ProtocolResult $result 1
        }

        $workType = Get-ExternalWorkType ([string]$acquiredState.taskKind)
        $session = [ordered]@{
          protocolVersion = $script:ProtocolVersion
          runId = $RunId
          repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
          baselinePath = [string]$acquiredState.recoveryBaselinePath
          currentBaselinePath = $currentBaselinePath
          evidencePath = [string]$acquiredState.recoveryEvidencePath
          isRecovery = $true
          phase = [string]$acquiredState.checkpoint
          branchKind = 'recovery'
          workType = $workType
          taskId = [string]$acquiredState.taskId
          executor = [string]$acquiredState.taskExecutor
        }
        Write-JsonAtomically ([pscustomobject]$session) (Get-SessionPath)

        $recovery = Invoke-GuardTool @(
          'CheckRecovery', '-BaselinePath', [string]$acquiredState.recoveryBaselinePath,
          '-EvidencePath', [string]$acquiredState.recoveryEvidencePath,
          '-ExpectedPaths', (@($acquiredState.expectedPaths) -join '|')
        )
        if ($recovery.Code -ne 0) {
          $recoveryJson = if ($recovery.Output) { Convert-ChildJson $recovery 'workspace CheckRecovery' } else { $null }
          $reason = if ($null -ne $recoveryJson -and $recoveryJson.reason) { [string]$recoveryJson.reason } else { 'recovery_check_failed' }
          $failed = Invoke-StateTool @('Fail', '-RunId', $RunId, '-WasRecovery', '-ErrorMessage', $reason)
          $failedState = if ($failed.Code -eq 0) { Convert-ChildJson $failed 'recovery Fail' } else { $acquiredState }
          $result = New-ProtocolResult $false 'stopped' 'recovery' (Get-RecoveryFailurePolicy $failedState) $reason "Recovery check failed: $reason"
          $result.taskId = [string]$acquiredState.taskId
          $result.executor = [string]$acquiredState.taskExecutor
          $result.expectedPaths = @($acquiredState.expectedPaths)
          $result.conflictingPaths = if ($null -ne $recoveryJson) { @($recoveryJson.conflictingPaths) } else { @() }
          Write-ProtocolResult $result $recovery.Code
        }

        $result = New-ProtocolResult $true 'resume_task' 'recovery' 'preserve_recovery' $null 'Recovery evidence exactly matches the controller residue.'
        $result.taskId = [string]$acquiredState.taskId
        $result.executor = [string]$acquiredState.taskExecutor
        $result.expectedPaths = @($acquiredState.expectedPaths)
        $result.requiredSources = @(Get-BranchSources 'recovery' ([string]$acquiredState.taskExecutor))
        $result.nextCommand = 'Finish'
        $result.baselinePath = [string]$acquiredState.recoveryBaselinePath
        Write-ProtocolResult $result
      }

      $checkpoint = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'identity_checked')
      if ($checkpoint.Code -ne 0) {
        $message = if ($checkpoint.Error) { $checkpoint.Error } else { 'Identity checkpoint failed.' }
        Close-StartFailure 'checkpoint_failed' $message $checkpoint.Code
      }
      $state = Convert-ChildJson $checkpoint 'identity checkpoint'

      $session = [ordered]@{
        protocolVersion = $script:ProtocolVersion
        runId = $RunId
        repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
        baselinePath = $currentBaselinePath
        currentBaselinePath = $currentBaselinePath
        evidencePath = (Join-Path $RunRoot "$RunId.evidence.json")
        isRecovery = $false
        phase = 'identity_checked'
        branchKind = 'selection'
        workType = $null
        taskId = $null
        executor = $null
      }
      Write-JsonAtomically ([pscustomobject]$session) (Get-SessionPath)

      $result = New-ProtocolResult $true 'select_candidate' 'selection' 'close_empty_run' $null 'Lease acquired and workspace baseline captured.'
      $result.nextCommand = 'RegisterCandidate'
      $result.baselinePath = $currentBaselinePath
      $result.requiredSources = @('开发管理/当前任务队列.txt')
      $result.workerBackoff = $state.workerState.deepseek
      if ($null -ne $state.pendingDecision) {
        $result.action = 'inspect_pending_decision'
        $result.branchKind = 'pending_decision'
        $result.requiredSources = @('开发管理/自动工作流状态.txt')
      }
      Write-ProtocolResult $result
    }
    'Contract' {
      $result = New-ProtocolResult $true 'contract' 'none' 'stop_read_only' $null 'Controller protocol contract.'
      $result.runId = $null
      $result.taskKindMapping = $script:TaskKindMapping
      Write-ProtocolResult $result
    }
    'RegisterCandidate' {
      $session = Read-Session
      if ($session.phase -ne 'identity_checked') {
        $result = New-ProtocolResult $false 'stopped' 'none' 'preserve_recovery' 'invalid_phase' 'RegisterCandidate requires the identity_checked phase.'
        Write-ProtocolResult $result 13
      }
      if (-not $script:TaskKindMapping.Contains($WorkType)) {
        Close-EmptyRun 'invalid_arguments' 'WorkType must be execution, review, maintenance, or recovery.' 15
      }
      if ([string]::IsNullOrWhiteSpace($TaskId)) { Close-EmptyRun 'invalid_arguments' 'TaskId is required.' 15 }
      if ($Executor -notin @('codex', 'deepseek')) { Close-EmptyRun 'invalid_arguments' 'Executor must be codex or deepseek.' 15 }
      if ([string]::IsNullOrWhiteSpace($ExpectedPaths)) { Close-EmptyRun 'invalid_arguments' 'ExpectedPaths is required.' 15 }
      if ($Executor -eq 'deepseek') {
        $workerState = Get-StateSnapshot
        if (Test-DeepSeekBackoffActive $workerState) {
          $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'worker_backoff' 'DeepSeek worker is in backoff.'
          $result.nextCommand = 'RegisterCandidate'
          $result.workerBackoff = $workerState.workerState.deepseek
          Write-ProtocolResult $result 23
        }
      }

      $check = Invoke-GuardTool @('Check', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', $ExpectedPaths)
      $checkJson = if ($check.Output) { Convert-ChildJson $check 'workspace Check' } else { $null }
      if ($check.Code -eq 20) {
        $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'candidate_conflict' 'Candidate paths overlap the captured workspace baseline.'
        $result.nextCommand = 'RegisterCandidate'
        $result.expectedPaths = @($checkJson.expectedPaths)
        $result.conflictingPaths = @($checkJson.conflictingPaths)
        Write-ProtocolResult $result 20
      }
      if ($check.Code -ne 0) {
        $reason = if ($null -ne $checkJson -and $checkJson.reason) { [string]$checkJson.reason } else { 'workspace_check_failed' }
        $policy = if ($check.Code -eq 21) { 'stop_read_only' } else { 'close_empty_run' }
        Close-EmptyRun $reason "Workspace Check failed: $reason" $check.Code $policy
      }
      $normalizedPaths = @($checkJson.expectedPaths)

      $queues = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'queues_loaded')
      if ($queues.Code -ne 0) {
        Close-EmptyRun 'checkpoint_failed' $(if ($queues.Error) { $queues.Error } else { 'queues_loaded checkpoint failed.' }) $queues.Code
      }
      $mappedTaskKind = [string]$script:TaskKindMapping[$WorkType]
      $selected = Invoke-StateTool @(
        'Checkpoint', '-RunId', $RunId,
        '-TaskKind', $mappedTaskKind, '-TaskId', $TaskId, '-TaskExecutor', $Executor,
        '-Checkpoint', 'task_selected', '-ExpectedPaths', ($normalizedPaths -join '|'),
        '-RecoveryBaselinePath', [string]$session.baselinePath
      )
      if ($selected.Code -ne 0) {
        Close-EmptyRun 'checkpoint_failed' $(if ($selected.Error) { $selected.Error } else { 'task_selected checkpoint failed.' }) $selected.Code
      }

      $session.phase = 'task_selected'
      $session.branchKind = $WorkType
      $session.workType = $WorkType
      $session.taskId = $TaskId
      $session.executor = $Executor
      Save-Session $session

      $result = New-ProtocolResult $true 'implement_task' $WorkType 'preserve_recovery' $null 'Candidate registered and isolated.'
      $result.taskId = $TaskId
      $result.executor = $Executor
      $result.expectedPaths = $normalizedPaths
      $result.requiredSources = @(Get-BranchSources $WorkType $Executor)
      $result.nextCommand = 'BeginMutation'
      Write-ProtocolResult $result
    }
    'BeginMutation' {
      $session = Read-Session
      if ($session.phase -ne 'task_selected') {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'BeginMutation requires the task_selected phase.'
        Write-ProtocolResult $result 13
      }
      $checkpoint = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'mutation_started')
      if ($checkpoint.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'checkpoint_failed' $(if ($checkpoint.Error) { $checkpoint.Error } else { 'mutation_started checkpoint failed.' })
        Write-ProtocolResult $result $checkpoint.Code
      }
      $session.phase = 'mutation_started'
      Save-Session $session
      $state = Convert-ChildJson $checkpoint 'mutation_started checkpoint'
      $result = New-ProtocolResult $true 'perform_semantic_work' ([string]$session.branchKind) 'preserve_recovery' $null 'Mutation checkpoint recorded.'
      $result.taskId = [string]$state.taskId
      $result.executor = [string]$state.taskExecutor
      $result.expectedPaths = @($state.expectedPaths)
      $result.nextCommand = 'Finish'
      Write-ProtocolResult $result
    }
    'Renew' {
      $session = Read-Session
      $renewed = Invoke-StateTool @('Renew', '-RunId', $RunId)
      if ($renewed.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'renew_failed' $(if ($renewed.Error) { $renewed.Error } else { 'Lease renewal failed.' })
        Write-ProtocolResult $result $renewed.Code
      }
      $state = Convert-ChildJson $renewed 'state Renew'
      $result = New-ProtocolResult $true 'lease_renewed' ([string]$session.branchKind) 'preserve_recovery' $null 'Lease renewed.'
      $result.taskId = [string]$state.taskId
      $result.executor = [string]$state.taskExecutor
      $result.expectedPaths = @($state.expectedPaths)
      Write-ProtocolResult $result
    }
    'Finish' {
      $session = Read-Session
      if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_arguments' 'CommitMessage is required.'
        Write-ProtocolResult $result 15
      }
      if ($session.phase -notin @('mutation_started', 'verification_completed', 'commit_completed')) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'Finish requires a mutated or recoverable work unit.'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      $paths = @($state.expectedPaths)
      if ($paths.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string]$session.baselinePath)) {
        Stop-RegisteredWork $session 'recovery_state_incomplete' 'Finish is missing expected paths or the original baseline.' 1
      }

      $verify = Invoke-GuardTool @('Verify', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', ($paths -join '|'))
      if ($verify.Code -ne 0) {
        $verifyJson = if ($verify.Output) { Convert-ChildJson $verify 'pre-commit Verify' } else { $null }
        $reason = if ($null -ne $verifyJson -and $verifyJson.reason) { [string]$verifyJson.reason } else { 'verify_failed' }
        Stop-RegisteredWork $session $reason "Pre-commit Verify failed: $reason" $verify.Code
      }

      if ([string]::IsNullOrWhiteSpace([string]$state.recoveryEvidencePath)) {
        $capture = Invoke-GuardTool @(
          'CaptureRecoveryEvidence', '-BaselinePath', [string]$session.baselinePath,
          '-EvidencePath', [string]$session.evidencePath, '-ExpectedPaths', ($paths -join '|')
        )
        if ($capture.Code -ne 0) {
          $captureJson = if ($capture.Output) { Convert-ChildJson $capture 'recovery evidence capture' } else { $null }
          $reason = if ($null -ne $captureJson -and $captureJson.reason) { [string]$captureJson.reason } else { 'evidence_capture_failed' }
          Stop-RegisteredWork $session $reason "Recovery evidence capture failed: $reason" $capture.Code
        }
        $captureJson = Convert-ChildJson $capture 'recovery evidence capture'
        $evidencePath = [string]$session.evidencePath
        $evidenceHash = [string]$captureJson.evidenceHash
      } else {
        $evidencePath = [string]$state.recoveryEvidencePath
        $evidenceHash = [string]$state.recoveryEvidenceHash
      }

      $verified = Invoke-StateTool @(
        'Checkpoint', '-RunId', $RunId, '-Checkpoint', 'verification_completed',
        '-RecoveryBaselinePath', [string]$session.baselinePath,
        '-RecoveryEvidencePath', $evidencePath, '-RecoveryEvidenceHash', $evidenceHash
      )
      if ($verified.Code -ne 0) {
        Stop-RegisteredWork $session 'checkpoint_failed' $(if ($verified.Error) { $verified.Error } else { 'verification_completed checkpoint failed.' }) $verified.Code
      }
      $session.phase = 'verification_completed'
      $session.evidencePath = $evidencePath
      Save-Session $session

      $finalized = Invoke-ChildPowerShell $script:FinalizerTool @(
        '-RepositoryRoot', $RepositoryRoot, '-ExpectedPaths', ($paths -join '|'), '-CommitMessage', $CommitMessage
      )
      if ($finalized.Code -ne 0) {
        $message = if ($finalized.Error) { $finalized.Error } elseif ($finalized.Output) { $finalized.Output } else { 'Finalizer failed.' }
        Stop-RegisteredWork $session 'finalizer_failed' $message $finalized.Code
      }
      $commitCandidates = @(([string]$finalized.Output) -split '\r?\n' | Where-Object { $_ -match '^[0-9a-f]{40,64}$' })
      if ($commitCandidates.Count -ne 1) {
        Stop-RegisteredWork $session 'finalizer_protocol_invalid' 'Finalizer did not return a commit hash.' 1
      }
      $commit = [string]$commitCandidates[0]

      $committed = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'commit_completed')
      if ($committed.Code -ne 0) {
        Stop-RegisteredWork $session 'checkpoint_failed' $(if ($committed.Error) { $committed.Error } else { 'commit_completed checkpoint failed.' }) $committed.Code
      }
      $session.phase = 'commit_completed'
      Save-Session $session

      $postVerify = Invoke-GuardTool @('Verify', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', ($paths -join '|'))
      if ($postVerify.Code -ne 0) {
        $postJson = if ($postVerify.Output) { Convert-ChildJson $postVerify 'post-commit Verify' } else { $null }
        $reason = if ($null -ne $postJson -and $postJson.reason) { [string]$postJson.reason } else { 'post_commit_verify_failed' }
        Stop-RegisteredWork $session $reason "Post-commit Verify failed: $reason" $postVerify.Code
      }

      $completed = Invoke-StateTool @('Complete', '-RunId', $RunId)
      if ($completed.Code -ne 0) {
        Stop-RegisteredWork $session 'complete_failed' $(if ($completed.Error) { $completed.Error } else { 'Complete failed.' }) $completed.Code
      }
      Remove-RunArtifacts $session
      $result = New-ProtocolResult $true 'completed' ([string]$session.branchKind) 'stop_read_only' $null 'Work unit committed and completed.'
      $result.taskId = [string]$session.taskId
      $result.executor = [string]$session.executor
      $result.expectedPaths = $paths
      $result.commit = $commit
      $result.nextCommand = $null
      Write-ProtocolResult $result
    }
    'CompleteNoChange' {
      $session = Read-Session
      if ($session.phase -eq 'mutation_started' -or $session.phase -eq 'verification_completed' -or $session.phase -eq 'commit_completed') {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'mutation_exists' 'CompleteNoChange cannot close a mutated work unit.'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      $paths = @($state.expectedPaths)
      if ($paths.Count -gt 0) {
        $noChangeCheck = Invoke-GuardTool @('Verify', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', ($paths -join '|'))
      } else {
        $noChangeCheck = Invoke-GuardTool @('Check', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', 'tools/.tzg-no-change-sentinel')
      }
      if ($noChangeCheck.Code -ne 0) {
        $json = if ($noChangeCheck.Output) { Convert-ChildJson $noChangeCheck 'CompleteNoChange guard' } else { $null }
        $reason = if ($null -ne $json -and $json.reason) { [string]$json.reason } else { 'baseline_changed' }
        Stop-RegisteredWork $session $reason "CompleteNoChange guard failed: $reason" $noChangeCheck.Code
      }
      $completed = Invoke-StateTool @('Complete', '-RunId', $RunId)
      if ($completed.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'complete_failed' $(if ($completed.Error) { $completed.Error } else { 'Complete failed.' })
        Write-ProtocolResult $result $completed.Code
      }
      Remove-RunArtifacts $session
      $result = New-ProtocolResult $true 'completed_no_change' ([string]$session.branchKind) 'stop_read_only' $null 'Run completed without project changes.'
      Write-ProtocolResult $result
    }
    'Fail' {
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'preserve_recovery' 'invalid_arguments' 'ErrorMessage is required.'
        Write-ProtocolResult $result 15
      }
      $session = Read-Session
      $state = Get-StateSnapshot
      if ($session.phase -eq 'mutation_started' -and [string]::IsNullOrWhiteSpace([string]$state.recoveryEvidencePath)) {
        $paths = @($state.expectedPaths)
        if ($paths.Count -gt 0) {
          $verify = Invoke-GuardTool @('Verify', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', ($paths -join '|'))
          if ($verify.Code -eq 0) {
            $capture = Invoke-GuardTool @(
              'CaptureRecoveryEvidence', '-BaselinePath', [string]$session.baselinePath,
              '-EvidencePath', [string]$session.evidencePath, '-ExpectedPaths', ($paths -join '|')
            )
            if ($capture.Code -eq 0) {
              $captureJson = Convert-ChildJson $capture 'recovery evidence capture'
              $saved = Invoke-StateTool @(
                'Checkpoint', '-RunId', $RunId,
                '-RecoveryBaselinePath', [string]$session.baselinePath,
                '-RecoveryEvidencePath', [string]$session.evidencePath,
                '-RecoveryEvidenceHash', [string]$captureJson.evidenceHash
              )
              if ($saved.Code -ne 0) { throw $(if ($saved.Error) { $saved.Error } else { 'Recovery evidence checkpoint failed.' }) }
            }
          }
        }
      }
      $failArguments = @('Fail', '-RunId', $RunId, '-ErrorMessage', $ErrorMessage)
      if ([bool]$session.isRecovery) { $failArguments += '-WasRecovery' }
      $failed = Invoke-StateTool $failArguments
      if ($failed.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'fail_close_error' $(if ($failed.Error) { $failed.Error } else { 'State Fail failed.' })
        Write-ProtocolResult $result $failed.Code
      }
      $failedState = Convert-ChildJson $failed 'state Fail'
      $session.phase = 'failed'
      Save-Session $session
      $result = New-ProtocolResult $false 'failed' ([string]$session.branchKind) (Get-RecoveryFailurePolicy $failedState) 'task_failed' $ErrorMessage
      $result.taskId = [string]$failedState.taskId
      $result.executor = [string]$failedState.taskExecutor
      $result.expectedPaths = @($failedState.expectedPaths)
      $result.nextCommand = 'Start'
      Write-ProtocolResult $result
    }
    'RecordQueueState' {
      [void](Read-Session)
      if ([string]::IsNullOrWhiteSpace($QueueFingerprint) -or $RunnableCount -lt 0) {
        $result = New-ProtocolResult $false 'stopped' 'maintenance' 'preserve_recovery' 'invalid_arguments' 'QueueFingerprint and non-negative RunnableCount are required.'
        Write-ProtocolResult $result 15
      }
      $arguments = @('RecordQueueState', '-RunId', $RunId, '-QueueFingerprint', $QueueFingerprint, '-RunnableCount', [string]$RunnableCount)
      if ($QueueAuditCompleted) { $arguments += '-QueueAuditCompleted' }
      if ($NoCandidate) { $arguments += '-NoCandidate' }
      $recorded = Invoke-StateTool $arguments
      if ($recorded.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'maintenance' 'preserve_recovery' 'state_error' $(if ($recorded.Error) { $recorded.Error } else { 'RecordQueueState failed.' })
        Write-ProtocolResult $result $recorded.Code
      }
      $state = Convert-ChildJson $recorded 'RecordQueueState'
      $result = New-ProtocolResult $true 'queue_state_recorded' 'maintenance' 'preserve_recovery' $null 'Queue state recorded.'
      $result.queueFingerprint = [string]$state.lastQueueFingerprint
      $result.runnableCount = $state.lastRunnableCount
      $result.nextCommand = 'CompleteNoChange'
      Write-ProtocolResult $result
    }
    'RecordWorkerFailure' {
      [void](Read-Session)
      if ([string]::IsNullOrWhiteSpace($WorkerError)) {
        $result = New-ProtocolResult $false 'stopped' 'execution' 'preserve_recovery' 'invalid_arguments' 'WorkerError is required.'
        Write-ProtocolResult $result 15
      }
      $recorded = Invoke-StateTool @(
        'RecordWorkerFailure', '-RunId', $RunId, '-WorkerId', 'deepseek',
        '-WorkerError', $WorkerError, '-BackoffMinutes', [string]$BackoffMinutes
      )
      if ($recorded.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'execution' 'preserve_recovery' 'state_error' $(if ($recorded.Error) { $recorded.Error } else { 'RecordWorkerFailure failed.' })
        Write-ProtocolResult $result $recorded.Code
      }
      $state = Convert-ChildJson $recorded 'RecordWorkerFailure'
      $result = New-ProtocolResult $true 'select_candidate' 'selection' 'skip_candidate' $null 'DeepSeek worker backoff recorded.'
      $result.workerBackoff = $state.workerState.deepseek
      $result.nextCommand = 'RegisterCandidate'
      Write-ProtocolResult $result
    }
    'ClearWorkerFailure' {
      [void](Read-Session)
      $cleared = Invoke-StateTool @('ClearWorkerFailure', '-RunId', $RunId, '-WorkerId', 'deepseek')
      if ($cleared.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'execution' 'preserve_recovery' 'state_error' $(if ($cleared.Error) { $cleared.Error } else { 'ClearWorkerFailure failed.' })
        Write-ProtocolResult $result $cleared.Code
      }
      $state = Convert-ChildJson $cleared 'ClearWorkerFailure'
      $result = New-ProtocolResult $true 'select_candidate' 'selection' 'skip_candidate' $null 'DeepSeek worker backoff cleared.'
      $result.workerBackoff = $state.workerState.deepseek
      $result.nextCommand = 'RegisterCandidate'
      Write-ProtocolResult $result
    }
    'CreateDecision' {
      $session = Read-Session
      $state = Get-StateSnapshot
      $statusPath = '开发管理/自动工作流状态.txt'
      if (@($state.expectedPaths) -notcontains $statusPath) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'decision_status_path_missing' 'The project-visible decision status path is not registered.'
        Write-ProtocolResult $result 15
      }
      foreach ($required in @(
        @{ Name = 'TaskSummary'; Value = $TaskSummary },
        @{ Name = 'DecisionQuestion'; Value = $DecisionQuestion },
        @{ Name = 'DecisionOptions'; Value = $DecisionOptions },
        @{ Name = 'RecommendedOption'; Value = $RecommendedOption },
        @{ Name = 'ImpactSummary'; Value = $ImpactSummary }
      )) {
        if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
          $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_arguments' "$($required.Name) is required."
          Write-ProtocolResult $result 15
        }
      }
      $created = Invoke-StateTool @(
        'CreateDecision', '-RunId', $RunId, '-TaskKind', [string]$state.taskKind,
        '-TaskId', [string]$state.taskId, '-TaskSummary', $TaskSummary,
        '-DecisionQuestion', $DecisionQuestion, '-DecisionOptions', $DecisionOptions,
        '-RecommendedOption', $RecommendedOption, '-ImpactSummary', $ImpactSummary
      )
      if ($created.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'decision_create_failed' $(if ($created.Error) { $created.Error } else { 'CreateDecision failed.' })
        Write-ProtocolResult $result $created.Code
      }
      $createdState = Convert-ChildJson $created 'CreateDecision'
      $result = New-ProtocolResult $true 'publish_pending_decision' 'pending_decision' 'preserve_recovery' $null 'Pending decision created.'
      $result.taskId = [string]$createdState.taskId
      $result.pendingDecision = $createdState.pendingDecision
      $result.requiredSources = @('开发管理/自动工作流状态.txt')
      $result.nextCommand = 'MarkDecisionNotified'
      Write-ProtocolResult $result
    }
    'MarkDecisionNotified' {
      [void](Read-Session)
      $marked = Invoke-StateTool @('MarkDecisionNotified', '-RunId', $RunId)
      if ($marked.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'decision_notification_failed' $(if ($marked.Error) { $marked.Error } else { 'MarkDecisionNotified failed.' })
        Write-ProtocolResult $result $marked.Code
      }
      $state = Convert-ChildJson $marked 'MarkDecisionNotified'
      $result = New-ProtocolResult $true 'decision_notified' 'pending_decision' 'preserve_recovery' $null 'Decision notification recorded.'
      $result.pendingDecision = $state.pendingDecision
      $result.nextCommand = 'Finish'
      Write-ProtocolResult $result
    }
    'MarkDecisionDeliveryFailed' {
      [void](Read-Session)
      if ([string]::IsNullOrWhiteSpace($NotificationError)) {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'invalid_arguments' 'NotificationError is required.'
        Write-ProtocolResult $result 15
      }
      $marked = Invoke-StateTool @('MarkDecisionDeliveryFailed', '-RunId', $RunId, '-NotificationError', $NotificationError)
      if ($marked.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'decision_notification_failed' $(if ($marked.Error) { $marked.Error } else { 'MarkDecisionDeliveryFailed failed.' })
        Write-ProtocolResult $result $marked.Code
      }
      $state = Convert-ChildJson $marked 'MarkDecisionDeliveryFailed'
      $result = New-ProtocolResult $true 'decision_delivery_failed' 'pending_decision' 'preserve_recovery' $null 'Decision delivery failure recorded.'
      $result.pendingDecision = $state.pendingDecision
      $result.nextCommand = 'Finish'
      Write-ProtocolResult $result
    }
    'ResolveDecisionReply' {
      [void](Read-Session)
      $state = Get-StateSnapshot
      $pattern = '^\s*(?<id>DEC-[0-9]{8}-[A-Z0-9]+)\s*[：:]\s*(?:选择|选)\s*(?<key>[A-Za-z0-9]+)\s*$'
      if ([string]::IsNullOrWhiteSpace($ReplyText) -or $ReplyText -notmatch $pattern) {
        if ($null -ne $state.pendingDecision) {
          [void](Invoke-StateTool @('MarkDecisionDeliveryFailed', '-RunId', $RunId, '-NotificationError', 'invalid_reply'))
        }
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_reply' 'Decision reply does not match the strict single-option format.'
        $result.nextCommand = 'ResolveDecisionReply'
        Write-ProtocolResult $result 15
      }
      $decisionId = [string]$Matches['id']
      $optionKey = [string]$Matches['key']
      if ($null -eq $state.pendingDecision -or $decisionId -cne [string]$state.pendingDecision.decisionId -or
          @($state.pendingDecision.options | Where-Object { [string]$_.key -ceq $optionKey }).Count -ne 1) {
        if ($null -ne $state.pendingDecision) {
          [void](Invoke-StateTool @('MarkDecisionDeliveryFailed', '-RunId', $RunId, '-NotificationError', 'invalid_reply'))
        }
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_reply' 'Decision id or option key is invalid.'
        $result.nextCommand = 'ResolveDecisionReply'
        Write-ProtocolResult $result 15
      }
      $resolved = Invoke-StateTool @(
        'ResolveDecision', '-RunId', $RunId, '-DecisionId', $decisionId,
        '-OptionKey', $optionKey, '-ReplySource', 'email'
      )
      if ($resolved.Code -ne 0) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'decision_resolve_failed' $(if ($resolved.Error) { $resolved.Error } else { 'ResolveDecision failed.' })
        Write-ProtocolResult $result $resolved.Code
      }
      $resolvedState = Convert-ChildJson $resolved 'ResolveDecision'
      $result = New-ProtocolResult $true 'resume_decision_task' 'pending_decision' 'preserve_recovery' $null 'Strict decision reply resolved.'
      $result.pendingDecision = $resolvedState.pendingDecision
      $result.nextCommand = 'Finish'
      Write-ProtocolResult $result
    }
    default {
      $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'not_implemented' "$Action is not implemented."
      Write-ProtocolResult $result 1
    }
  }
} catch {
  $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'controller_error' $_.Exception.Message
  Write-ProtocolResult $result 1
}
