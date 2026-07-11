[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Acquire','Renew','Checkpoint','Complete','Fail','Show','ResetBlocked')]
  [string]$Action,
  [string]$StatePath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json",
  [string]$ControllerId = 'tzg-hourly-controller',
  [string]$RunId,
  [ValidateSet('recovery','review','maintenance','execute')]
  [string]$TaskKind,
  [string]$TaskId,
  [ValidateSet('identity_checked','queues_loaded','task_selected','mutation_started','verification_completed','commit_completed')]
  [string]$Checkpoint,
  [string]$ExpectedPaths,
  [switch]$WasRecovery,
  [switch]$QueueAuditCompleted,
  [string]$ErrorMessage,
  [int]$LeaseMinutes = 180,
  [string]$Now
)

$ErrorActionPreference = 'Stop'
$script:ExitBusy = 10
$script:ExitBlocked = 11
$script:ExitOwnerMismatch = 12
$script:ExitInvalidState = 13
$script:ExitLockContention = 14
$script:ExitInvalidArguments = 15

function Exit-WithCode {
  param([string]$Message, [int]$Code)
  [Console]::Error.WriteLine($Message)
  exit $Code
}

function Get-NowValue {
  if ([string]::IsNullOrWhiteSpace($Now)) { return [DateTimeOffset]::UtcNow }
  [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture)
}

function New-State {
  [ordered]@{
    schemaVersion = 1
    controllerId = $ControllerId
    runId = $null
    state = 'IDLE'
    leaseExpiresAt = $null
    taskKind = $null
    taskId = $null
    checkpoint = $null
    expectedPaths = @()
    recoveryCount = 0
    lastQueueAuditAt = $null
    lastError = $null
  }
}

function Import-State {
  if (-not (Test-Path -LiteralPath $StatePath)) { return (New-State) }
  $raw = [IO.File]::ReadAllText($StatePath)
  $parsed = $raw | ConvertFrom-Json
  if ($parsed.schemaVersion -ne 1) { throw "Unsupported schemaVersion: $($parsed.schemaVersion)" }
  $state = New-State
  foreach ($key in @($state.Keys)) {
    $property = $parsed.PSObject.Properties[$key]
    if ($null -ne $property) { $state[$key] = $property.Value }
  }
  $state
}

function Export-State {
  param([System.Collections.IDictionary]$State)
  $directory = Split-Path -Parent $StatePath
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $temporary = Join-Path $directory ('.state-' + [guid]::NewGuid().ToString('N') + '.tmp')
  $encoding = New-Object Text.UTF8Encoding($false)
  [IO.File]::WriteAllText($temporary, ([pscustomobject]$State | ConvertTo-Json -Depth 6), $encoding)
  if (Test-Path -LiteralPath $StatePath) {
    $backup = "$StatePath.backup"
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    [IO.File]::Replace($temporary, $StatePath, $backup, $true)
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
  } else {
    [IO.File]::Move($temporary, $StatePath)
  }
}

function Require-RunId {
  if ([string]::IsNullOrWhiteSpace($RunId)) { Exit-WithCode 'RunId is required' $script:ExitInvalidArguments }
}

function Require-Owner {
  param([System.Collections.IDictionary]$State)
  Require-RunId
  if ($State.runId -ne $RunId) { Exit-WithCode 'RunId does not own the lease' $script:ExitOwnerMismatch }
}

function Set-Lease {
  param([System.Collections.IDictionary]$State, [DateTimeOffset]$At)
  $State.leaseExpiresAt = $At.AddMinutes($LeaseMinutes).ToString('o')
}

$nowValue = Get-NowValue
$directory = Split-Path -Parent $StatePath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$guardPath = "$StatePath.guard"
$guard = $null
try {
  try {
    $guard = [IO.File]::Open($guardPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
  } catch [IO.IOException] {
    Exit-WithCode 'State transaction lock is busy' $script:ExitLockContention
  }

  try { $state = Import-State } catch {
    Exit-WithCode "Invalid state file: $($_.Exception.Message)" $script:ExitInvalidState
  }

  switch ($Action) {
    'Show' {
      [pscustomobject]$state | ConvertTo-Json -Depth 6
      exit 0
    }
    'Acquire' {
      Require-RunId
      if ($state.state -eq 'AUTO-BLOCKED') { Exit-WithCode 'Controller is AUTO-BLOCKED' $script:ExitBlocked }
      if ($state.state -eq 'RUNNING' -and $state.leaseExpiresAt) {
        $expires = [DateTimeOffset]::Parse($state.leaseExpiresAt)
        if ($expires -gt $nowValue) { Exit-WithCode 'An active lease already exists' $script:ExitBusy }
      }
      if ($state.state -eq 'IDLE') {
        $state.taskKind = $null
        $state.taskId = $null
        $state.checkpoint = $null
        $state.expectedPaths = @()
        $state.recoveryCount = 0
        $state.lastError = $null
      }
      $state.controllerId = $ControllerId
      $state.runId = $RunId
      $state.state = 'RUNNING'
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Renew' {
      Require-Owner $state
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Checkpoint' {
      Require-Owner $state
      if ($TaskKind) { $state.taskKind = $TaskKind }
      if ($TaskId) { $state.taskId = $TaskId }
      if ($Checkpoint) { $state.checkpoint = $Checkpoint }
      if ($PSBoundParameters.ContainsKey('ExpectedPaths')) {
        $paths = @($ExpectedPaths -split '\|' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $state.expectedPaths = @($paths | ForEach-Object { ([string]$_).Replace('\','/') } | Sort-Object -Unique)
      }
      Set-Lease $state $nowValue
      Export-State $state
    }
    'Complete' {
      Require-Owner $state
      if ($QueueAuditCompleted) { $state.lastQueueAuditAt = $nowValue.ToString('o') }
      $state.state = 'IDLE'
      $state.runId = $null
      $state.leaseExpiresAt = $null
      $state.taskKind = $null
      $state.taskId = $null
      $state.checkpoint = $null
      $state.expectedPaths = @()
      $state.recoveryCount = 0
      $state.lastError = $null
      Export-State $state
    }
    'Fail' {
      Require-Owner $state
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'ErrorMessage is required' $script:ExitInvalidArguments }
      $state.lastError = $ErrorMessage
      if ($WasRecovery) { $state.recoveryCount = [int]$state.recoveryCount + 1 }
      if ([int]$state.recoveryCount -ge 2) {
        $state.state = 'AUTO-BLOCKED'
        $state.leaseExpiresAt = $null
      } else {
        $state.state = 'RUNNING'
        $state.leaseExpiresAt = $nowValue.ToString('o')
      }
      Export-State $state
    }
    'ResetBlocked' {
      if ($state.state -ne 'AUTO-BLOCKED') { Exit-WithCode 'State is not AUTO-BLOCKED' $script:ExitInvalidArguments }
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { Exit-WithCode 'A manual reset reason is required' $script:ExitInvalidArguments }
      $state.state = 'IDLE'
      $state.runId = $null
      $state.leaseExpiresAt = $null
      $state.taskKind = $null
      $state.taskId = $null
      $state.checkpoint = $null
      $state.expectedPaths = @()
      $state.recoveryCount = 0
      $state.lastError = "Manual reset: $ErrorMessage"
      Export-State $state
    }
  }
  [pscustomobject]$state | ConvertTo-Json -Depth 6
} finally {
  if ($null -ne $guard) { $guard.Dispose() }
}
