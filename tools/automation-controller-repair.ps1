[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('DryRun', 'Apply')]
  [string]$Action,
  [string]$RepositoryRoot = 'D:\天章游戏开发',
  [string]$StatePath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json",
  [string]$RunRoot = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller-runs",
  [string]$MemoryPath = "$env:USERPROFILE\.codex\automations\tzg-hourly-controller\memory.md",
  [string]$IncidentDecisionId,
  [string]$SelectedOption,
  [string]$EvidenceThreadId,
  [switch]$ManualOverride
)

$ErrorActionPreference = 'Stop'
$script:ExitInvalidState = 13
$script:ExitInvalidArguments = 15
$script:ExpectedDecisionId = 'DEC-20260715-35ACB87E6C10'
$script:ExpectedTaskId = 'TQ-057'
$script:ExpectedPath = '开发管理/自动工作流状态.txt'
$script:CorrectionReason = 'Correct the incident decision resolution source using the approved conversation evidence.'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateTool = Join-Path $scriptRoot 'automation-controller-state.ps1'
$guardTool = Join-Path $scriptRoot 'automation-workspace-guard.ps1'
$engine = (Get-Process -Id $PID).Path

function Exit-Repair {
  param([string]$Message, [int]$Code)
  [Console]::Error.WriteLine($Message)
  exit $Code
}

function Require-RepairInput {
  param([string]$Value, [string]$Name)
  if ([string]::IsNullOrWhiteSpace($Value)) {
    Exit-Repair "$Name is required" $script:ExitInvalidArguments
  }
}

function Get-Sha256Text {
  param([string]$Value)
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
  try {
    ([Security.Cryptography.SHA256]::HashData($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
  } finally {
    [Array]::Clear($bytes, 0, $bytes.Length)
  }
}

function Get-FileHashValue {
  param([string]$Path)
  $bytes = [IO.File]::ReadAllBytes($Path)
  ([Security.Cryptography.SHA256]::HashData($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Read-JsonFile {
  param([string]$Path, [string]$Label)
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Exit-Repair "$Label is missing" $script:ExitInvalidState
  }
  try {
    [IO.File]::ReadAllText($Path) | ConvertFrom-Json
  } catch {
    Exit-Repair "$Label is invalid" $script:ExitInvalidState
  }
}

function Invoke-ChildScript {
  param([string]$Path, [string[]]$Arguments)
  $oldPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>&1
    [pscustomobject]@{
      Code = $LASTEXITCODE
      Output = (($output | ForEach-Object { [string]$_ }) -join "`n")
    }
  } finally {
    $ErrorActionPreference = $oldPreference
  }
}

function Get-Decision {
  param($State)
  if ($null -eq $State.decisionFlow) { return $null }
  $matches = @($State.decisionFlow.resolvedDecisions | Where-Object { [string]$_.decisionId -ceq $IncidentDecisionId })
  if ($matches.Count -ne 1) { return $null }
  $matches[0]
}

function Test-ResetInvariant {
  param($State)
  $State.schemaVersion -eq 6 -and
    $State.state -ceq 'IDLE' -and
    $null -eq $State.runId -and
    $null -eq $State.runMode -and
    $null -eq $State.leaseExpiresAt -and
    $null -eq $State.taskKind -and
    $null -eq $State.taskId -and
    $null -eq $State.taskExecutor -and
    $null -eq $State.checkpoint -and
    @($State.expectedPaths).Count -eq 0 -and
    $null -eq $State.recoveryBaselinePath -and
    $null -eq $State.recoveryEvidencePath -and
    $null -eq $State.recoveryEvidenceHash -and
    [int]$State.recoveryCount -eq 0 -and
    $null -eq $State.pendingDecision -and
    $null -ne $State.decisionFlow -and
    $State.decisionFlow.status -ceq 'IMPLEMENTATION_PENDING'
}

function Test-AlreadyRepaired {
  param($State, [string]$ThreadHash)
  if (-not (Test-ResetInvariant $State)) { return $false }
  $decision = Get-Decision $State
  if ($null -eq $decision -or [string]$decision.resolution.optionKey -cne $SelectedOption -or [string]$decision.resolution.source -cne 'manual') {
    return $false
  }
  $corrections = @($State.auditCorrections | Where-Object {
    [string]$_.decisionId -ceq $IncidentDecisionId -and
      [string]$_.field -ceq 'resolution.source' -and
      [string]$_.oldValue -ceq 'email' -and
      [string]$_.newValue -ceq 'manual' -and
      [string]$_.evidenceHash -ceq $ThreadHash
  })
  $corrections.Count -eq 1
}

function Assert-IncidentShape {
  param($State)
  if ($State.schemaVersion -ne 5 -or
      [string]$State.controllerId -cne 'tzg-hourly-controller' -or
      [string]::IsNullOrWhiteSpace([string]$State.runId) -or
      [string]$State.taskKind -cne 'execute' -or
      [string]$State.taskId -cne $script:ExpectedTaskId -or
      [string]$State.taskExecutor -cne 'codex' -or
      [string]$State.checkpoint -cne 'mutation_started' -or
      @($State.expectedPaths).Count -ne 1 -or
      [string]$State.expectedPaths[0] -cne $script:ExpectedPath -or
      [string]::IsNullOrWhiteSpace([string]$State.recoveryBaselinePath) -or
      $null -ne $State.recoveryEvidencePath -or
      $null -ne $State.recoveryEvidenceHash -or
      $null -eq $State.pendingDecision -or
      [string]$State.pendingDecision.decisionId -cne $script:ExpectedDecisionId -or
      [string]$State.pendingDecision.taskId -cne $script:ExpectedTaskId -or
      [string]$State.pendingDecision.status -cne 'RESOLVED' -or
      [string]$State.pendingDecision.resolution.optionKey -cne 'B' -or
      [string]$State.pendingDecision.resolution.source -cne 'email') {
    Exit-Repair 'state does not match the bounded decision incident' $script:ExitInvalidState
  }

  $isCurrentBlockedShape = [string]$State.state -ceq 'AUTO-BLOCKED' -and
    [int]$State.recoveryCount -eq 2 -and
    $null -eq $State.runMode -and
    $null -eq $State.leaseExpiresAt
  $isExpiredRunningShape = $false
  if ([string]$State.state -ceq 'RUNNING' -and [int]$State.recoveryCount -eq 1 -and -not [string]::IsNullOrWhiteSpace([string]$State.leaseExpiresAt)) {
    try { $isExpiredRunningShape = [DateTimeOffset]::Parse([string]$State.leaseExpiresAt) -le [DateTimeOffset]::UtcNow } catch { $isExpiredRunningShape = $false }
  }
  if (-not $isCurrentBlockedShape -and -not $isExpiredRunningShape) {
    Exit-Repair 'state does not match an allowed incident lifecycle shape' $script:ExitInvalidState
  }
}

function Resolve-WithinRoot {
  param([string]$Path, [string]$Root, [string]$Label)
  $fullPath = [IO.Path]::GetFullPath($Path)
  $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
  if (-not $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    Exit-Repair "$Label is outside the configured run root" $script:ExitInvalidState
  }
  $fullPath
}

function Find-IncidentSession {
  param($State)
  $resolvedRunRoot = [IO.Path]::GetFullPath($RunRoot)
  $exactCandidate = Join-Path $resolvedRunRoot ("$([string]$State.runId).json")
  $baselinePath = Resolve-WithinRoot ([string]$State.recoveryBaselinePath) $resolvedRunRoot 'baseline path'
  $baselineName = [IO.Path]::GetFileName($baselinePath)
  $baselineCandidate = if ($baselineName.EndsWith('.baseline.json', [StringComparison]::Ordinal)) {
    Join-Path $resolvedRunRoot ($baselineName.Substring(0, $baselineName.Length - '.baseline.json'.Length) + '.json')
  } else { $null }
  $sessionPath = if (Test-Path -LiteralPath $exactCandidate -PathType Leaf) { $exactCandidate } elseif ($null -ne $baselineCandidate -and (Test-Path -LiteralPath $baselineCandidate -PathType Leaf)) { $baselineCandidate } else { $null }
  if ($null -eq $sessionPath) { Exit-Repair 'matching run session is missing' $script:ExitInvalidState }
  $session = Read-JsonFile $sessionPath 'run session'
  $sessionBaseline = Resolve-WithinRoot ([string]$session.baselinePath) $resolvedRunRoot 'session baseline path'
  if ([string]$session.taskId -cne $script:ExpectedTaskId -or
      -not $sessionBaseline.Equals($baselinePath, [StringComparison]::OrdinalIgnoreCase) -or
      -not ([IO.Path]::GetFullPath([string]$session.repositoryRoot)).Equals([IO.Path]::GetFullPath($RepositoryRoot), [StringComparison]::OrdinalIgnoreCase)) {
    Exit-Repair 'run session does not match the incident state' $script:ExitInvalidState
  }
  [pscustomobject]@{ Path = $sessionPath; BaselinePath = $baselinePath; Session = $session }
}

function Assert-CleanWorkspace {
  param($State, $SessionInfo)
  $evidencePath = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($StatePath))) ('.repair-evidence-' + [guid]::NewGuid().ToString('N') + '.json')
  try {
    $guardResult = Invoke-ChildScript $guardTool @(
      'CaptureInterruptionEvidence', '-RepositoryRoot', $RepositoryRoot,
      '-BaselinePath', $SessionInfo.BaselinePath,
      '-ExpectedPaths', (@($State.expectedPaths) -join '|'),
      '-EvidencePath', $evidencePath
    )
    if ($guardResult.Code -ne 0) { Exit-Repair 'workspace precondition check failed' $script:ExitInvalidState }
    try { $classification = $guardResult.Output | ConvertFrom-Json } catch { Exit-Repair 'workspace precondition result is invalid' $script:ExitInvalidState }
    if ([string]$classification.classification -cne 'clean') {
      Exit-Repair 'workspace has changed since the recorded baseline' $script:ExitInvalidState
    }
  } finally {
    Remove-Item -LiteralPath $evidencePath -Force -ErrorAction SilentlyContinue
  }
}

function Invoke-StateRepair {
  param([string]$TargetStatePath)
  $result = Invoke-ChildScript $stateTool @(
    'RepairDecisionFlow', '-StatePath', $TargetStatePath,
    '-DecisionId', $IncidentDecisionId,
    '-OptionKey', $SelectedOption,
    '-CorrectionReason', $script:CorrectionReason,
    '-CorrectionEvidenceThreadId', $EvidenceThreadId,
    '-ManualOverride'
  )
  if ($result.Code -ne 0) { Exit-Repair 'state repair transaction was rejected' $result.Code }
  try { $result.Output | ConvertFrom-Json } catch { Exit-Repair 'state repair result is invalid' $script:ExitInvalidState }
}

function New-RedactedProjection {
  param($State, [int]$SchemaVersion)
  $decision = if ($SchemaVersion -eq 5) { $State.pendingDecision } else { Get-Decision $State }
  [ordered]@{
    schemaVersion = $SchemaVersion
    state = [string]$State.state
    taskId = if ($null -eq $decision) { $null } else { [string]$decision.taskId }
    decisionId = if ($null -eq $decision) { $null } else { [string]$decision.decisionId }
    optionKey = if ($null -eq $decision) { $null } else { [string]$decision.resolution.optionKey }
    source = if ($null -eq $decision) { $null } else { [string]$decision.resolution.source }
  }
}

Require-RepairInput $IncidentDecisionId 'IncidentDecisionId'
Require-RepairInput $SelectedOption 'SelectedOption'
Require-RepairInput $EvidenceThreadId 'EvidenceThreadId'
if ($IncidentDecisionId -cne $script:ExpectedDecisionId -or $SelectedOption -cne 'B') {
  Exit-Repair 'incident decision or selected option does not match the bounded repair' $script:ExitInvalidArguments
}
if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) { Exit-Repair 'RepositoryRoot is missing' $script:ExitInvalidArguments }
$threadHash = Get-Sha256Text $EvidenceThreadId.Trim()
$rawState = Read-JsonFile $StatePath 'state file'

if ($rawState.schemaVersion -eq 6) {
  if ($Action -eq 'Apply' -and (Test-AlreadyRepaired $rawState $threadHash)) {
    [pscustomobject][ordered]@{
      action = 'apply'
      result = 'already_repaired'
      decisionId = $IncidentDecisionId
      optionKey = $SelectedOption
      source = 'manual'
      state = 'IDLE'
      stateHash = Get-FileHashValue $StatePath
    } | ConvertTo-Json -Compress
    exit 0
  }
  Exit-Repair 'schema v6 state is not the expected repaired incident' $script:ExitInvalidState
}

Assert-IncidentShape $rawState
$sessionInfo = Find-IncidentSession $rawState
if (-not (Test-Path -LiteralPath $MemoryPath -PathType Leaf)) { Exit-Repair 'automation memory is missing' $script:ExitInvalidState }
Assert-CleanWorkspace $rawState $sessionInfo

if ($Action -eq 'DryRun') {
  $stateDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($StatePath))
  $temporaryState = Join-Path $stateDirectory ('.repair-dry-run-' + [guid]::NewGuid().ToString('N') + '.json')
  try {
    Copy-Item -LiteralPath $StatePath -Destination $temporaryState
    $projected = Invoke-StateRepair $temporaryState
    if (-not (Test-ResetInvariant $projected)) { Exit-Repair 'dry-run projection failed invariant validation' $script:ExitInvalidState }
    [pscustomobject][ordered]@{
      action = 'dry_run'
      result = 'projected'
      before = New-RedactedProjection $rawState 5
      after = New-RedactedProjection $projected 6
    } | ConvertTo-Json -Depth 5 -Compress
    exit 0
  } finally {
    Remove-Item -LiteralPath $temporaryState -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$temporaryState.guard" -Force -ErrorAction SilentlyContinue
  }
}

if (-not $ManualOverride) { Exit-Repair 'Apply requires -ManualOverride' $script:ExitInvalidArguments }
$stateDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($StatePath))
$backupName = 'decision-repair-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$backupDirectory = Join-Path $stateDirectory $backupName
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
Copy-Item -LiteralPath $StatePath -Destination (Join-Path $backupDirectory 'state.before.json')
Copy-Item -LiteralPath $sessionInfo.Path -Destination (Join-Path $backupDirectory 'session.before.json')
Copy-Item -LiteralPath $MemoryPath -Destination (Join-Path $backupDirectory 'memory.before.md')
$stateBeforeHash = Get-FileHashValue $StatePath
$sessionHash = Get-FileHashValue $sessionInfo.Path
$memoryHash = Get-FileHashValue $MemoryPath

$repairedState = Invoke-StateRepair $StatePath
if (-not (Test-ResetInvariant $repairedState)) { Exit-Repair 'applied repair failed invariant validation' $script:ExitInvalidState }
$repairedDecision = Get-Decision $repairedState
if ($null -eq $repairedDecision -or [string]$repairedDecision.resolution.optionKey -cne $SelectedOption -or [string]$repairedDecision.resolution.source -cne 'manual') {
  Exit-Repair 'applied repair did not preserve the selected decision' $script:ExitInvalidState
}
if (-not (Test-AlreadyRepaired $repairedState $threadHash)) { Exit-Repair 'applied repair audit validation failed' $script:ExitInvalidState }

[pscustomobject][ordered]@{
  action = 'apply'
  result = 'repaired'
  decisionId = $IncidentDecisionId
  optionKey = $SelectedOption
  source = 'manual'
  state = 'IDLE'
  backupDirectoryName = $backupName
  stateBeforeHash = $stateBeforeHash
  stateAfterHash = Get-FileHashValue $StatePath
  sessionHash = $sessionHash
  memoryHash = $memoryHash
} | ConvertTo-Json -Compress
