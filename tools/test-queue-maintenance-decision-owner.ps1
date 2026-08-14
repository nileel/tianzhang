#requires -Version 7.0

$ErrorActionPreference = 'Stop'
function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }
function Assert-Contains { param([string]$Text, [string[]]$Values, [string]$Label) foreach ($value in $Values) { Assert-True ($Text.Contains($value, [StringComparison]::Ordinal)) "$Label is missing: $value" } }

$ownerPath = Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'
$candidatePath = Join-Path $PSScriptRoot 'invoke-codex-candidate.ps1'
$statePath = Join-Path $PSScriptRoot 'set-task-automation-state.ps1'
$checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
$owner = [IO.File]::ReadAllText($ownerPath)
$candidate = [IO.File]::ReadAllText($candidatePath)
$state = [IO.File]::ReadAllText($statePath)
$checker = [IO.File]::ReadAllText($checkerPath)

foreach ($path in @($ownerPath, $candidatePath, $statePath, $checkerPath)) {
  $tokens = $null; $errors = $null
  [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
  Assert-True ($errors.Count -eq 0) "PowerShell parse failed: $path"
}

Assert-Contains $owner @(
  'function Send-MaintenanceDecision',
  'function Find-AnsweredMaintenanceDecision',
  'function Apply-TerminatedMaintenanceDecision',
  "status = 'waiting_decision'",
  "detailCode = 'maintenance_decision_no_reply'",
  "taskId = 'QUEUE-MAINTENANCE'",
  "'decision_requested'",
  "'maintenance_completed'",
  'ResolveMaintenanceDecision',
  'allowCustomReply = $false',
  '[DateTimeOffset]::Now -gt $expiresAt'
) 'owner lifecycle'
Assert-True ($owner.IndexOf('Find-AnsweredMaintenanceDecision', [StringComparison]::Ordinal) -lt $owner.LastIndexOf('Invoke-JsonTool $selectorPath', [StringComparison]::Ordinal)) 'Maintenance reply check is not before ordinary selection'
$senderOptionLines = @($owner -split '\r?\n' | Where-Object { $_.Contains('options = @($decision.options | ForEach-Object', [StringComparison]::Ordinal) })
Assert-True ($senderOptionLines.Count -eq 1 -and $senderOptionLines[0].Contains('label = [string]$_.label', [StringComparison]::Ordinal) -and -not $senderOptionLines[0].Contains('targetState', [StringComparison]::Ordinal)) 'Sender does not strip targetState from bridge options'
Assert-Contains $candidate @("'maintenance_decision'", "targetState = @{ type = 'string'", 'decisionTaskId', "'maintenance_resolution'") 'candidate contract'
Assert-Contains $state @("'PauseMaintenanceDecision'", "'ResolveMaintenanceDecision'", "'ExpireMaintenanceDecision'", "'ready,ready,blocked'") 'state actions'
Assert-Contains $checker @('automationCheckpoint and automationDecision are mutually exclusive', 'MaintenancePendingDecision', 'MaintenanceResolvedReady', 'MaintenanceResolvedBlocked', 'MaintenanceExpiredBlocked') 'checker contract'

$bridgeChanges = @(& git -C (Split-Path -Parent $PSScriptRoot) -c core.quotepath=false diff --name-only -- tools/feishu-decision-bridge/src)
Assert-True ($bridgeChanges.Count -eq 0) 'Maintenance lifecycle modified the existing bridge source'
Write-Output 'test-queue-maintenance-decision-owner: PASS'
