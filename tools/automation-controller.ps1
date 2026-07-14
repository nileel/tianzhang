[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Contract','Start','RegisterCandidate','BeginMutation','Renew','Finish','CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure','CreateDecision','ResolveDecisionReply')]
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

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = (Get-Process -Id $PID).Path
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.CreateNoWindow = $true
  $allArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + @($Arguments)
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

  if ([string]::IsNullOrWhiteSpace([string]$Result.Output)) { throw "$Label returned no JSON" }
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

function Write-JsonAtomically {
  param([object]$Value, [string]$Path)

  $directory = Split-Path -Parent $Path
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $temporary = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  try {
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, $null) } else { [IO.File]::Move($temporary, $Path) }
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

      $baselinePath = Join-Path $RunRoot "$RunId.baseline.json"
      $snapshot = Invoke-GuardTool @('Snapshot', '-BaselinePath', $baselinePath)
      if ($snapshot.Code -ne 0) {
        $message = if ($snapshot.Error) { $snapshot.Error } else { 'Workspace Snapshot failed.' }
        Close-StartFailure 'snapshot_failed' $message $snapshot.Code
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
        baselinePath = $baselinePath
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
      $result.baselinePath = $baselinePath
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
    default {
      $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'not_implemented' "$Action is not implemented."
      Write-ProtocolResult $result 1
    }
  }
} catch {
  $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'controller_error' $_.Exception.Message
  Write-ProtocolResult $result 1
}
