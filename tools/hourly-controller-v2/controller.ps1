#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet(
    'Start', 'RecordTitleResult', 'DiscoverRead', 'DiscoverSearch', 'DiscoverList',
    'DiscoverCheck', 'SubmitManifest', 'BeginMutation', 'Finish', 'Abort',
    'CreateDecision', 'SendDecision', 'ConsumeDecision', 'MigrateLegacy', 'Show'
  )]
  [string]$Action,

  [Parameter(Mandatory = $true)]
  [string]$RepositoryRoot,

  [Parameter(Mandatory = $true)]
  [string]$StatePath,

  [Parameter(Mandatory = $true)]
  [string]$RequestPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

Import-Module (Join-Path $PSScriptRoot 'protocol.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'registry.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'state.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'discovery.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'manifest.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'title.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'decision-adapter.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'verification.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'protocol.psm1') -Force -DisableNameChecking
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'private-path-acl.ps1')

$script:KnownErrorCodes = @(
  'invalid_request', 'invalid_state', 'metadata_missing', 'thread_id_mismatch',
  'registry_invalid', 'task_not_found', 'task_not_executable', 'discovery_denied',
  'discovery_incomplete', 'source_changed', 'manifest_invalid',
  'decision_coverage_incomplete', 'baseline_changed', 'head_changed',
  'path_outside_scope', 'check_failed', 'decision_invalid', 'feishu_unavailable',
  'migration_invalid', 'internal_error'
)
$script:Repository = $null
$script:State = $null
$script:Request = $null
$script:TaskContract = $null
$script:Mutex = $null
$script:OwnsMutex = $false

function Throw-ControllerError {
  param(
    [Parameter(Mandatory = $true)][string]$Code,
    [Parameter(Mandatory = $true)][string]$Message,
    [string[]]$ChangedPaths = @(),
    [AllowNull()][string]$Diagnostic = $null
  )

  $exception = [InvalidOperationException]::new("$Code`: $Message")
  $exception.Data['errorCode'] = $Code
  $exception.Data['changedPaths'] = [string[]]@($ChangedPaths)
  if ($null -ne $Diagnostic) {
    $exception.Data['diagnostic'] = $Diagnostic
  }
  throw $exception
}

function Get-RequestValue {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [switch]$Required
  )

  if ($script:Request.Contains($Name)) {
    return $script:Request[$Name]
  }
  if ($Required) {
    Throw-ControllerError -Code 'invalid_request' -Message "request field is missing: $Name"
  }
  $null
}

function Set-ObjectField {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string]$Name,
    [AllowNull()]$FieldValue
  )

  if ($Value -is [Collections.IDictionary]) {
    $Value[$Name] = $FieldValue
  } else {
    $Value | Add-Member -NotePropertyName $Name -NotePropertyValue $FieldValue -Force
  }
}

function Resolve-ControllerRepository {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
    Throw-ControllerError -Code 'invalid_request' -Message 'repository root must be an existing absolute directory'
  }
  $root = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
  $gitRoot = @(& git -C $root rev-parse --show-toplevel 2>&1)
  if ($LASTEXITCODE -ne 0 -or $gitRoot.Count -ne 1 -or
      -not [IO.Path]::GetFullPath(([string]$gitRoot[0]).Trim()).TrimEnd('\', '/').Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
    Throw-ControllerError -Code 'invalid_request' -Message 'repository root must be the Git root'
  }
  $root
}

function Get-PrivateStateRoot {
  $root = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    Throw-ControllerError -Code 'invalid_state' -Message 'private state root does not exist'
  }
  $root
}

function Assert-PrivateControllerPath {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Label,
    [switch]$MustExist
  )

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    Throw-ControllerError -Code 'invalid_request' -Message "$Label must be absolute"
  }
  $root = Get-PrivateStateRoot
  $fullPath = [IO.Path]::GetFullPath($Path)
  $prefix = $root + [IO.Path]::DirectorySeparatorChar
  if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    Throw-ControllerError -Code 'invalid_request' -Message "$Label must be inside the private state root"
  }
  if ($MustExist -and -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    Throw-ControllerError -Code 'invalid_request' -Message "$Label does not exist"
  }
  $fullPath
}

function Initialize-ControllerState {
  $fullStatePath = Assert-PrivateControllerPath -Path $StatePath -Label 'state path'
  if (Test-Path -LiteralPath $fullStatePath -PathType Leaf) {
    return Read-ControllerState -Path $fullStatePath
  }
  $state = New-ControllerState
  Write-ControllerStateAtomic -Path $fullStatePath -State $state
  $state
}

function Save-ControllerState {
  Write-ControllerStateAtomic -Path ([IO.Path]::GetFullPath($StatePath)) -State $script:State
}

function Get-ActiveRun {
  param([switch]$RequireRequestRunId)

  if ($null -eq $script:State.activeRun) {
    Throw-ControllerError -Code 'invalid_state' -Message 'no active controller run'
  }
  if ($RequireRequestRunId) {
    $requestRunId = [string](Get-RequestValue -Name 'runId' -Required)
    if ($requestRunId -cne [string]$script:State.activeRun.runId) {
      Throw-ControllerError -Code 'invalid_state' -Message 'request runId does not match the active run'
    }
  }
  $script:State.activeRun
}

function Assert-Phase {
  param([Parameter(Mandatory = $true)][string[]]$Allowed)

  if ([string]$script:State.phase -cnotin $Allowed) {
    Throw-ControllerError -Code 'invalid_state' -Message "action $Action is not allowed from phase $($script:State.phase)"
  }
}

function Read-ControllerRegistry {
  $registryPath = Join-Path $script:Repository '开发管理\自动工作流任务注册表.json'
  $queuePath = Join-Path $script:Repository '开发管理\当前任务队列.txt'
  $registry = Read-TaskRegistry -Path $registryPath
  Assert-RegistryMatchesQueue -Registry $registry -QueuePath $queuePath
  $registry
}

function Set-CurrentTaskContract {
  param([Parameter(Mandatory = $true)]$Registry)

  if ($null -eq $script:State.activeRun) {
    $script:TaskContract = $null
    return
  }
  $script:TaskContract = Get-TaskContract -Registry $Registry -TaskId ([string]$script:State.activeRun.taskId)
}

function Get-DiscoveryContext {
  $activeRun = Get-ActiveRun -RequireRequestRunId
  [pscustomobject]@{
    repositoryRoot = $script:Repository
    runRoot = [string]$activeRun.runRoot
    requiredSources = @($script:TaskContract.requiredSources)
    allowedRoots = @($script:TaskContract.allowedRoots)
    discoveryChecks = @($script:TaskContract.discoveryChecks)
  }
}

function Get-DecisionConstraints {
  if ($null -eq $script:TaskContract) {
    return @()
  }
  $ids = @($script:TaskContract.decisionIds)
  @($script:State.decisionLedger | Where-Object { [string]$_.decisionId -cin $ids } | ForEach-Object {
      [pscustomobject][ordered]@{
        decisionId = [string]$_.decisionId
        resolutionText = [string]$_.resolutionText
        scopeContract = $_.scopeContract
      }
    })
}

function New-CurrentResponse {
  param(
    [AllowNull()]$Result = $null,
    [AllowNull()]$ErrorCode = $null,
    [string[]]$ChangedPaths = @(),
    [AllowNull()]$RunId = $null,
    [AllowNull()]$TaskId = $null,
    [AllowNull()]$Phase = $null,
    [AllowNull()]$NextAction = $null
  )

  $active = if ($null -eq $script:State) { $null } else { $script:State.activeRun }
  $resolvedRunId = if ($null -ne $RunId) { $RunId } elseif ($null -ne $active) { [string]$active.runId } else { '00000000-0000-0000-0000-000000000000' }
  $resolvedTaskId = if ($null -ne $TaskId) { $TaskId } elseif ($null -ne $active) { [string]$active.taskId } else { '' }
  $resolvedPhase = if ($null -ne $Phase) { $Phase } elseif ($null -ne $script:State) { [string]$script:State.phase } else { 'IDLE' }
  $resolvedNextAction = if ($null -ne $NextAction) {
    $NextAction
  } elseif ($null -ne $active -and $null -ne $active.nextAction) {
    [string]$active.nextAction
  } else {
    ''
  }
  $requiredSources = if ($null -ne $script:TaskContract) { @($script:TaskContract.requiredSources) } else { @() }
  $requiredChecks = if ($null -ne $script:TaskContract) { @($script:TaskContract.requiredChecks) } else { @() }
  if ($null -eq $Result) {
    $Result = [ordered]@{}
  }
  New-ControllerResponse `
    -Action $Action `
    -RunId $resolvedRunId `
    -TaskId $resolvedTaskId `
    -Phase $resolvedPhase `
    -NextAction $resolvedNextAction `
    -ErrorCode $ErrorCode `
    -ChangedPaths $ChangedPaths `
    -RequiredSources $requiredSources `
    -RequiredChecks $requiredChecks `
    -DecisionConstraints (Get-DecisionConstraints) `
    -Result $Result
}

function Get-GuardFailure {
  param(
    [Parameter(Mandatory = $true)]$GuardResult,
    [Parameter(Mandatory = $true)][string]$BaselinePath
  )

  if ($GuardResult.exitCode -eq 0 -and [bool]$GuardResult.payload.safe) {
    return $null
  }
  $changedPaths = @($GuardResult.payload.conflictingPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  $baseline = Read-TestableJson -Path $BaselinePath
  $head = @(& git -C $script:Repository rev-parse HEAD 2>&1)
  $headChanged = $LASTEXITCODE -ne 0 -or $head.Count -ne 1 -or [string]$baseline.head -cne ([string]$head[0]).Trim()
  [pscustomobject]@{
    errorCode = if ($headChanged) { 'head_changed' } else { 'baseline_changed' }
    changedPaths = if ($headChanged -and '<HEAD>' -cnotin $changedPaths) { @($changedPaths) + '<HEAD>' } else { @($changedPaths) }
  }
}

function Read-TestableJson {
  param([Parameter(Mandatory = $true)][string]$Path)

  $text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($Path))
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    $text | ConvertFrom-Json -DateKind String
  } else {
    $text | ConvertFrom-Json
  }
}

function Assert-WorkspaceGuardSafe {
  param(
    [Parameter(Mandatory = $true)][ValidateSet('Check', 'Verify')][string]$GuardAction,
    [Parameter(Mandatory = $true)]$ActiveRun
  )

  $result = Invoke-WorkspaceGuard `
    -Action $GuardAction `
    -RepositoryRoot $script:Repository `
    -BaselinePath ([string]$ActiveRun.baselinePath) `
    -ExpectedPaths @($ActiveRun.expectedPaths)
  $failure = Get-GuardFailure -GuardResult $result -BaselinePath ([string]$ActiveRun.baselinePath)
  if ($null -ne $failure) {
    Throw-ControllerError -Code $failure.errorCode -Message "workspace guard $GuardAction failed" -ChangedPaths $failure.changedPaths
  }
}

function Get-InterruptionClassification {
  param([Parameter(Mandatory = $true)]$ActiveRun)

  $expectedPaths = @($ActiveRun.expectedPaths)
  if ($expectedPaths.Count -eq 0) {
    return [pscustomobject][ordered]@{
      classification = 'clean'
      changedExpectedPaths = @()
      conflictingPaths = @()
    }
  }
  $evidencePath = Join-Path ([string]$ActiveRun.runRoot) 'interruption-evidence.json'
  try {
    $guard = Invoke-WorkspaceGuard `
      -Action 'CaptureInterruptionEvidence' `
      -RepositoryRoot $script:Repository `
      -BaselinePath ([string]$ActiveRun.baselinePath) `
      -ExpectedPaths $expectedPaths `
      -EvidencePath $evidencePath
    [pscustomobject][ordered]@{
      classification = [string]$guard.payload.classification
      changedExpectedPaths = @($guard.payload.changedExpectedPaths | ForEach-Object { [string]$_ })
      conflictingPaths = @($guard.payload.conflictingPaths | ForEach-Object { [string]$_ })
    }
  } catch {
    [pscustomobject][ordered]@{
      classification = 'unsafe'
      changedExpectedPaths = @()
      conflictingPaths = @('<UNKNOWN>')
    }
  }
}

function Reset-ActiveRunToIdle {
  param([Parameter(Mandatory = $true)]$ActiveRun)

  if ([string]$script:State.phase -ceq 'COMMITTED') {
    $script:State = Move-ControllerPhase -State $script:State -From @('COMMITTED') -To 'IDLE'
  } elseif ([string]$script:State.phase -cne 'IDLE') {
    $script:State = Move-ControllerPhase -State $script:State -From @([string]$script:State.phase) -To 'IDLE'
  }
  $script:State.activeRun = $null
}

function Invoke-StartAction {
  Assert-Phase -Allowed @('IDLE')
  $registry = Read-ControllerRegistry
  $task = Select-ExecutableTask -Registry $registry
  if ($null -eq $task) {
    Throw-ControllerError -Code 'task_not_executable' -Message 'no execution-enabled Codex task is ready'
  }
  $model = [string](Get-RequestValue -Name 'model' -Required)
  $threadId = [string](Get-RequestValue -Name 'threadId' -Required)
  $metadataThreadId = [string](Get-RequestValue -Name 'metadataThreadId' -Required)
  $titleRequest = New-TitleRequest -Model $model -ThreadId $threadId -MetadataThreadId $metadataThreadId -TaskTitle ([string]$task.title)
  $runId = [guid]::NewGuid().ToString()
  $privateRoot = Get-PrivateStateRoot
  $runsRoot = Join-Path $privateRoot 'tzg-hourly-controller-v2-runs'
  if (-not (Test-Path -LiteralPath $runsRoot -PathType Container)) {
    [IO.Directory]::CreateDirectory($runsRoot) | Out-Null
  }
  Set-PrivatePathAcl -Path $runsRoot -Directory
  Assert-PrivatePathAcl -Path $runsRoot -Directory
  $runRoot = Join-Path $runsRoot $runId
  [IO.Directory]::CreateDirectory($runRoot) | Out-Null
  Set-PrivatePathAcl -Path $runRoot -Directory
  Assert-PrivatePathAcl -Path $runRoot -Directory
  $baselinePath = Join-Path $runRoot 'baseline.json'
  Invoke-WorkspaceGuard -Action 'Snapshot' -RepositoryRoot $script:Repository -BaselinePath $baselinePath | Out-Null
  Set-PrivatePathAcl -Path $baselinePath
  Assert-PrivatePathAcl -Path $baselinePath
  $script:State.activeRun = [ordered]@{
    runId = $runId
    taskId = [string]$task.taskId
    taskTitle = [string]$task.title
    model = $model
    threadId = $threadId
    repositoryRoot = $script:Repository
    runRoot = $runRoot
    baselinePath = $baselinePath
    nextAction = 'RecordTitleResult'
    titleStatus = 'PENDING'
    titleDiagnostic = ''
    expectedPaths = @()
    requiredChecks = @($task.requiredChecks)
    manifestPath = $null
    pendingDecision = $null
    checkEvidence = @()
    commitSha = $null
  }
  $script:State = Move-ControllerPhase -State $script:State -From @('IDLE') -To 'DISCOVERING'
  $script:TaskContract = $task
  Save-ControllerState
  New-CurrentResponse -Result ([ordered]@{ titleRequest = Get-TitleToolPayload -TitleRequest $titleRequest })
}

function Invoke-RecordTitleAction {
  Assert-Phase -Allowed @('DISCOVERING')
  Get-ActiveRun -RequireRequestRunId | Out-Null
  $succeededValue = Get-RequestValue -Name 'succeeded' -Required
  if ($succeededValue -isnot [bool]) {
    Throw-ControllerError -Code 'invalid_request' -Message 'succeeded must be a boolean'
  }
  $diagnostic = [string](Get-RequestValue -Name 'diagnostic')
  $script:State = Record-TitleResult -State $script:State -Succeeded ([bool]$succeededValue) -Diagnostic $diagnostic
  Save-ControllerState
  New-CurrentResponse -Result ([ordered]@{
      titleStatus = [string]$script:State.activeRun.titleStatus
      titleDiagnostic = [string]$script:State.activeRun.titleDiagnostic
    })
}

function Invoke-DiscoveryAction {
  Assert-Phase -Allowed @('DISCOVERING')
  $context = Get-DiscoveryContext
  $result = switch ($Action) {
    'DiscoverRead' { Invoke-DiscoverRead -Context $context -Path ([string](Get-RequestValue -Name 'path' -Required)) }
    'DiscoverSearch' {
      Invoke-DiscoverSearch `
        -Context $context `
        -Root ([string](Get-RequestValue -Name 'root' -Required)) `
        -Pattern ([string](Get-RequestValue -Name 'pattern' -Required)) `
        -Glob ([string](Get-RequestValue -Name 'glob' -Required))
    }
    'DiscoverList' {
      Invoke-DiscoverList `
        -Context $context `
        -Root ([string](Get-RequestValue -Name 'root' -Required)) `
        -Glob ([string](Get-RequestValue -Name 'glob' -Required))
    }
    'DiscoverCheck' { Invoke-DiscoverCheck -Context $context -CheckId ([string](Get-RequestValue -Name 'checkId' -Required)) }
  }
  Set-ObjectField -Value $script:State.activeRun -Name 'nextAction' -FieldValue 'DiscoverRead'
  Save-ControllerState
  New-CurrentResponse -Result $result
}

function Invoke-SubmitManifestAction {
  Assert-Phase -Allowed @('DISCOVERING')
  $activeRun = Get-ActiveRun -RequireRequestRunId
  $manifestPath = Assert-PrivateControllerPath -Path ([string](Get-RequestValue -Name 'manifestPath' -Required)) -Label 'manifest path' -MustExist
  $manifest = Read-WorkManifest -Path $manifestPath
  if ([string]$manifest.runId -cne [string]$activeRun.runId -or
      [string]$manifest.model -cne [string]$activeRun.model -or
      [string]$manifest.threadId -cne [string]$activeRun.threadId) {
    Throw-ControllerError -Code 'manifest_invalid' -Message 'manifest run identity does not match the active run'
  }
  Set-ObjectField -Value $activeRun -Name 'expectedPaths' -FieldValue @($manifest.expectedPaths)
  Set-ObjectField -Value $activeRun -Name 'requiredChecks' -FieldValue @($manifest.requiredChecks)
  $validated = Test-WorkManifest `
    -Manifest $manifest `
    -TaskContract $script:TaskContract `
    -DecisionLedger @($script:State.decisionLedger) `
    -DiscoveryLogPath (Join-Path ([string]$activeRun.runRoot) 'discovery-log.jsonl') `
    -BaselinePath ([string]$activeRun.baselinePath)
  if (-not [bool]$manifest.planOnly) {
    Throw-ControllerError -Code 'manifest_invalid' -Message 'first manifest submission must be planOnly=true'
  }
  $approval = New-ManifestApprovalDecision -Manifest $manifest
  Set-ObjectField -Value $activeRun -Name 'manifestPath' -FieldValue $manifestPath
  Set-ObjectField -Value $activeRun -Name 'pendingDecision' -FieldValue $approval
  Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'SendDecision'
  $script:State = Move-ControllerPhase -State $script:State -From @('DISCOVERING') -To 'IMPLEMENTATION_PENDING'
  Save-ControllerState
  New-CurrentResponse -Result ([ordered]@{
      planOnly = $true
      expectedPaths = @($validated.expectedPaths)
      approvalDecisionId = [string]$approval.decisionId
    })
}

function Invoke-CreateDecisionAction {
  Assert-Phase -Allowed @('DISCOVERING')
  $activeRun = Get-ActiveRun -RequireRequestRunId
  $decision = New-DecisionRequest `
    -TaskId ([string]$activeRun.taskId) `
    -Question ([string](Get-RequestValue -Name 'question' -Required)) `
    -Options @(Get-RequestValue -Name 'options' -Required)
  Set-ObjectField -Value $activeRun -Name 'pendingDecision' -FieldValue $decision
  Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'SendDecision'
  $script:State = Move-ControllerPhase -State $script:State -From @('DISCOVERING') -To 'WAITING_DECISION'
  Save-ControllerState
  New-CurrentResponse -Result ([ordered]@{ decisionId = [string]$decision.decisionId })
}

function Invoke-SendDecisionAction {
  Assert-Phase -Allowed @('WAITING_DECISION', 'IMPLEMENTATION_PENDING')
  $activeRun = Get-ActiveRun -RequireRequestRunId
  if ($null -eq $activeRun.pendingDecision) {
    Throw-ControllerError -Code 'decision_invalid' -Message 'active run has no pending decision'
  }
  $result = Send-DecisionRequest `
    -Decision $activeRun.pendingDecision `
    -RunRoot ([string]$activeRun.runRoot) `
    -BridgeRoot (Join-Path $script:Repository 'tools\feishu-decision-bridge')
  if (-not [bool]$result.ok) {
    Throw-ControllerError -Code 'feishu_unavailable' -Message 'Feishu decision channel is unavailable'
  }
  Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'ConsumeDecision'
  Save-ControllerState
  New-CurrentResponse -Result ([ordered]@{ decisionId = [string]$result.decisionId })
}

function Invoke-ConsumeDecisionAction {
  Assert-Phase -Allowed @('WAITING_DECISION', 'IMPLEMENTATION_PENDING')
  $activeRun = Get-ActiveRun -RequireRequestRunId
  if ($null -eq $activeRun.pendingDecision) {
    Throw-ControllerError -Code 'decision_invalid' -Message 'active run has no pending decision'
  }
  $originalPhase = [string]$script:State.phase
  $result = Consume-DecisionReply `
    -Decision $activeRun.pendingDecision `
    -RunRoot ([string]$activeRun.runRoot) `
    -BridgeRoot (Join-Path $script:Repository 'tools\feishu-decision-bridge')
  if (-not [bool]$result.ok) {
    Throw-ControllerError -Code 'feishu_unavailable' -Message 'Feishu decision channel is unavailable'
  }
  if ([string]$result.phase -ceq 'WAITING_DECISION') {
    Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'ConsumeDecision'
    Save-ControllerState
    return New-CurrentResponse -Result ([ordered]@{ decisionStatus = 'NO_REPLY' })
  }
  if ($originalPhase -ceq 'WAITING_DECISION') {
    Set-ObjectField -Value $activeRun -Name 'decisionResolution' -FieldValue $result
    Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'SubmitManifest'
    $script:State = Move-ControllerPhase -State $script:State -From @('WAITING_DECISION') -To 'IMPLEMENTATION_PENDING'
  } elseif ([string]$result.resolutionKind -ceq 'OPTION' -and [string]$result.selectedOptionId -ceq 'A') {
    Set-ObjectField -Value $activeRun -Name 'manifestApproval' -FieldValue $result
    Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'BeginMutation'
    $script:State = Move-ControllerPhase -State $script:State -From @('IMPLEMENTATION_PENDING') -To 'AUTHORIZED'
  } else {
    Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'SubmitManifest'
  }
  Save-ControllerState
  New-CurrentResponse -Result ([ordered]@{
      resolutionKind = [string]$result.resolutionKind
      selectedOptionId = $result.selectedOptionId
      requiresManifestApproval = [bool]$result.requiresManifestApproval
    })
}

function Invoke-BeginMutationAction {
  Assert-Phase -Allowed @('AUTHORIZED')
  $activeRun = Get-ActiveRun -RequireRequestRunId
  Assert-WorkspaceGuardSafe -GuardAction 'Check' -ActiveRun $activeRun
  Set-ObjectField -Value $activeRun -Name 'nextAction' -FieldValue 'Finish'
  $script:State = Move-ControllerPhase -State $script:State -From @('AUTHORIZED') -To 'MUTATING'
  Save-ControllerState
  New-CurrentResponse -Result ([ordered]@{ authorizedPaths = @($activeRun.expectedPaths) })
}

function Invoke-FinishAction {
  Assert-Phase -Allowed @('MUTATING')
  $activeRun = Get-ActiveRun -RequireRequestRunId
  $commitMessage = [string](Get-RequestValue -Name 'commitMessage' -Required)
  $script:State = Move-ControllerPhase -State $script:State -From @('MUTATING') -To 'VERIFYING'
  Save-ControllerState
  Assert-WorkspaceGuardSafe -GuardAction 'Verify' -ActiveRun $activeRun
  $checkEvidence = @(Invoke-RegisteredChecks -RepositoryRoot $script:Repository -RequiredChecks @($activeRun.requiredChecks))
  $finalized = Invoke-GuardedFinalizer `
    -RepositoryRoot $script:Repository `
    -ExpectedPaths @($activeRun.expectedPaths) `
    -CommitMessage $commitMessage
  Assert-WorkspaceGuardSafe -GuardAction 'Verify' -ActiveRun $activeRun
  Set-ObjectField -Value $activeRun -Name 'checkEvidence' -FieldValue (@($checkEvidence) + @($finalized.delegatedChecks))
  Set-ObjectField -Value $activeRun -Name 'commitSha' -FieldValue ([string]$finalized.commitSha)
  $script:State = Move-ControllerPhase -State $script:State -From @('VERIFYING') -To 'COMMITTED'
  Save-ControllerState
  $runId = [string]$activeRun.runId
  $taskId = [string]$activeRun.taskId
  $commitSha = [string]$activeRun.commitSha
  $evidence = @($activeRun.checkEvidence)
  Reset-ActiveRunToIdle -ActiveRun $activeRun
  Save-ControllerState
  New-CurrentResponse -RunId $runId -TaskId $taskId -Phase 'IDLE' -NextAction '' -Result ([ordered]@{
      commitSha = $commitSha
      checkEvidence = $evidence
    })
}

function Invoke-AbortAction {
  Assert-Phase -Allowed @('DISCOVERING', 'AUTHORIZED', 'MUTATING', 'VERIFYING', 'WAITING_DECISION', 'IMPLEMENTATION_PENDING')
  $activeRun = Get-ActiveRun -RequireRequestRunId
  $classification = Get-InterruptionClassification -ActiveRun $activeRun
  $runId = [string]$activeRun.runId
  $taskId = [string]$activeRun.taskId
  Reset-ActiveRunToIdle -ActiveRun $activeRun
  Save-ControllerState
  New-CurrentResponse -RunId $runId -TaskId $taskId -Phase 'IDLE' -NextAction '' -Result ([ordered]@{
      interruptionClassification = [string]$classification.classification
      changedExpectedPaths = @($classification.changedExpectedPaths)
      conflictingPaths = @($classification.conflictingPaths)
    })
}

function Get-ControllerErrorInfo {
  param([Parameter(Mandatory = $true)]$ErrorRecord)

  $exception = $ErrorRecord.Exception
  $code = if ($null -ne $exception.Data['errorCode'] -and [string]$exception.Data['errorCode'] -cin $script:KnownErrorCodes) {
    [string]$exception.Data['errorCode']
  } elseif ($exception.Message -match '^([a-z_]+):' -and $Matches[1] -cin $script:KnownErrorCodes) {
    [string]$Matches[1]
  } else {
    'internal_error'
  }
  $changedPaths = if ($null -ne $exception.Data['changedPaths']) { @($exception.Data['changedPaths'] | ForEach-Object { [string]$_ }) } else { @() }
  $diagnostic = if ($null -ne $exception.Data['diagnostic']) {
    ConvertTo-SanitizedVerificationDiagnostic -Value ([string]$exception.Data['diagnostic'])
  } else {
    ConvertTo-SanitizedVerificationDiagnostic -Value $exception.Message
  }
  [pscustomobject]@{
    code = $code
    changedPaths = @($changedPaths)
    diagnostic = $diagnostic
  }
}

try {
  if ($PSVersionTable.PSVersion.Major -lt 7) {
    Throw-ControllerError -Code 'invalid_request' -Message 'PowerShell 7 or newer is required'
  }
  $script:Repository = Resolve-ControllerRepository -Path $RepositoryRoot
  $null = Assert-PrivateControllerPath -Path $StatePath -Label 'state path'
  $script:Request = Read-ControllerRequest -Path $RequestPath
  if ([string](Get-RequestValue -Name 'action' -Required) -cne $Action) {
    Throw-ControllerError -Code 'invalid_request' -Message 'request action does not match -Action'
  }
  $mutexHash = (Get-Sha256Text -Text ([IO.Path]::GetFullPath($StatePath))).Substring(0, 24)
  $script:Mutex = [Threading.Mutex]::new($false, "Local\TZG-Hourly-Controller-V2-$mutexHash")
  try {
    $script:OwnsMutex = $script:Mutex.WaitOne(0, $false)
  } catch [Threading.AbandonedMutexException] {
    $script:OwnsMutex = $true
  }
  if (-not $script:OwnsMutex) {
    Throw-ControllerError -Code 'invalid_state' -Message 'another controller writer owns this state'
  }

  if ($Action -ceq 'MigrateLegacy') {
    $legacyPath = Assert-PrivateControllerPath -Path ([string](Get-RequestValue -Name 'legacyPath' -Required)) -Label 'legacy path' -MustExist
    $contractPath = Assert-PrivateControllerPath -Path ([string](Get-RequestValue -Name 'fixtureContractPath' -Required)) -Label 'fixture contract path' -MustExist
    $contract = Read-TestableJson -Path $contractPath
    $script:State = Import-LegacyV8State -LegacyPath $legacyPath -DestinationPath ([IO.Path]::GetFullPath($StatePath)) -FixtureContract @($contract)
    $response = New-CurrentResponse -Result ([ordered]@{ decisionCount = @($script:State.decisionLedger).Count })
    Write-ControllerResponse -Response $response
    exit 0
  }

  $script:State = Initialize-ControllerState
  $registry = if ($Action -ceq 'Show') { $null } else { Read-ControllerRegistry }
  if ($null -ne $registry) {
    Set-CurrentTaskContract -Registry $registry
  }
  $response = switch ($Action) {
    'Start' { Invoke-StartAction }
    'RecordTitleResult' { Invoke-RecordTitleAction }
    'DiscoverRead' { Invoke-DiscoveryAction }
    'DiscoverSearch' { Invoke-DiscoveryAction }
    'DiscoverList' { Invoke-DiscoveryAction }
    'DiscoverCheck' { Invoke-DiscoveryAction }
    'SubmitManifest' { Invoke-SubmitManifestAction }
    'CreateDecision' { Invoke-CreateDecisionAction }
    'SendDecision' { Invoke-SendDecisionAction }
    'ConsumeDecision' { Invoke-ConsumeDecisionAction }
    'BeginMutation' { Invoke-BeginMutationAction }
    'Finish' { Invoke-FinishAction }
    'Abort' { Invoke-AbortAction }
    'Show' { New-CurrentResponse -Result ([ordered]@{ state = $script:State }) }
  }
  Write-ControllerResponse -Response $response
  exit 0
} catch {
  $errorInfo = Get-ControllerErrorInfo -ErrorRecord $_
  $runId = '00000000-0000-0000-0000-000000000000'
  $taskId = ''
  $phase = 'IDLE'
  $nextAction = ''
  $result = [ordered]@{ diagnostic = $errorInfo.diagnostic }
  if ($null -ne $script:State) {
    $activeRun = $script:State.activeRun
    if ($null -ne $activeRun) {
      $runId = [string]$activeRun.runId
      $taskId = [string]$activeRun.taskId
      $nextAction = [string]$activeRun.nextAction
    }
    if ($errorInfo.code -cin @('baseline_changed', 'head_changed', 'check_failed') -and $null -ne $activeRun) {
      $classification = Get-InterruptionClassification -ActiveRun $activeRun
      $result.interruptionClassification = [string]$classification.classification
      $result.changedExpectedPaths = @($classification.changedExpectedPaths)
      $result.conflictingPaths = @($classification.conflictingPaths)
      if ($errorInfo.changedPaths.Count -eq 0) {
        $errorInfo.changedPaths = @($classification.conflictingPaths)
      }
      Reset-ActiveRunToIdle -ActiveRun $activeRun
      try { Save-ControllerState } catch { }
      $phase = 'IDLE'
      $nextAction = ''
    } else {
      $phase = [string]$script:State.phase
    }
  }
  $response = New-CurrentResponse `
    -Result $result `
    -ErrorCode $errorInfo.code `
    -ChangedPaths @($errorInfo.changedPaths) `
    -RunId $runId `
    -TaskId $taskId `
    -Phase $phase `
    -NextAction $nextAction
  Write-ControllerResponse -Response $response
  exit 1
} finally {
  if ($script:OwnsMutex -and $null -ne $script:Mutex) {
    $script:Mutex.ReleaseMutex()
  }
  if ($null -ne $script:Mutex) {
    $script:Mutex.Dispose()
  }
}
