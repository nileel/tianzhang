param([switch]$ExpectControllerActive)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$findings = New-Object System.Collections.Generic.List[string]

function ConvertFrom-Utf8Base64 {
  param([string]$Value)
  [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Value))
}

function Require-Match {
  param([string]$Path, [string]$Pattern, [string]$Message)
  if (-not (Select-String -Quiet -LiteralPath $Path -Pattern $Pattern)) { $findings.Add($Message) }
}

function Reject-Match {
  param([string]$Path, [string]$Pattern, [string]$Message)
  if (Select-String -Quiet -LiteralPath $Path -Pattern $Pattern) { $findings.Add($Message) }
}

$devMgmt = ConvertFrom-Utf8Base64 '5byA5Y+R566h55CG'
$rulesName = ConvertFrom-Utf8Base64 '6Ieq5Yqo5bel5L2c5rWB6KeE5YiZLnR4dA=='
$statusName = ConvertFrom-Utf8Base64 '6Ieq5Yqo5bel5L2c5rWB54q25oCBLnR4dA=='
$maintenanceName = ConvertFrom-Utf8Base64 '54q25oCB5LiO5bu66K6u57u05oqk6KeE5YiZLnR4dA=='
$collaborationName = ConvertFrom-Utf8Base64 'QUnljY/kvZzop4TliJkudHh0'
$rulePattern = ConvertFrom-Utf8Base64 '6Ieq5Yqo5bel5L2c5rWB6KeE5YiZXC50eHQ='
$readOnlyPattern = (ConvertFrom-Utf8Base64 '5Y+q6K+7') + '|read-only'
$titleToolPattern = 'set_thread_title'
$taskSummaryPattern = ConvertFrom-Utf8Base64 '5Lit5paH566A6L+w'

$rules = Join-Path $root (Join-Path $devMgmt $rulesName)
$status = Join-Path $root (Join-Path $devMgmt $statusName)
$reviewEntry = Join-Path $root (Join-Path $devMgmt (ConvertFrom-Utf8Base64 '5a6h5qC45YWl5Y+jLnR4dA=='))
$collaboration = Join-Path $root (Join-Path $devMgmt $collaborationName)
if (-not (Test-Path -LiteralPath $rules)) { $findings.Add('missing workflow rules') }
if (Test-Path -LiteralPath $status) {
  Reject-Match $status 'WF1-QUEUE-MAINTENANCE|WF2-CODEX-ONE|WF3-CLAUDE-ONE|WF4-CODEX-TWO' 'project status still contains the legacy workflow table'
}

foreach ($entry in @('AGENTS.md','CLAUDE.md',(Join-Path $devMgmt $maintenanceName),(Join-Path $devMgmt $collaborationName))) {
  Require-Match (Join-Path $root $entry) $rulePattern "$entry does not route to workflow rules"
}

$automationRoot = Join-Path $env:USERPROFILE '.codex\automations'
$controller = Join-Path $automationRoot 'tzg-wf2-codex-execute-1\automation.toml'
$paused = @(
  'tzg-wf1-queue-and-review-maintenance',
  'tzg-wf3-claude-execute-1',
  'tzg-wf4-codex-execute-2'
)
Require-Match $controller '^name = "TZG Hourly Controller"$' 'controller has not been renamed'
Reject-Match $controller 'TQ-[0-9]+|HANDOFF-[0-9]+' 'controller prompt contains a hardcoded task id'
Require-Match (Join-Path $root 'AGENTS.md') 'tools\.codex_app__set_thread_title' 'AGENTS manual workflow does not call the title tool'
Require-Match (Join-Path $root 'CLAUDE.md') 'tools\.codex_app__set_thread_title' 'CLAUDE manual workflow does not call the title tool'
Require-Match $reviewEntry 'tools\.codex_app__set_thread_title' 'review entry does not call the title tool'
Require-Match $collaboration 'tools\.codex_app__set_thread_title' 'collaboration rules do not call the title tool'
Reject-Match $controller $titleToolPattern 'controller still attempts to rename a thread'
Require-Match $controller $taskSummaryPattern 'controller does not record the human-readable task summary in memory'
foreach ($id in $paused) {
  Require-Match (Join-Path $automationRoot "$id\automation.toml") '^status = "PAUSED"$' "$id is not paused"
}

$expectedStatus = if ($ExpectControllerActive) { 'ACTIVE' } else { 'PAUSED' }
Require-Match $controller "^status = `"$expectedStatus`"$" "controller status is not $expectedStatus"
$daily = Join-Path $automationRoot 'tzg-daily-automation-briefing\automation.toml'
Require-Match $daily '^status = "ACTIVE"$' 'daily briefing is not active'
Require-Match $daily $readOnlyPattern 'daily briefing does not declare its read-only boundary'

$activeWriters = @(
  Get-ChildItem -Directory $automationRoot -Filter 'tzg-*' |
    Where-Object { $_.Name -ne 'tzg-daily-automation-briefing' } |
    Where-Object { Select-String -Quiet -LiteralPath (Join-Path $_.FullName 'automation.toml') -Pattern '^status = "ACTIVE"$' }
)
$expectedWriterCount = if ($ExpectControllerActive) { 1 } else { 0 }
if ($activeWriters.Count -ne $expectedWriterCount) {
  $findings.Add("expected $expectedWriterCount active writer(s), found $($activeWriters.Count)")
}

if ($findings.Count -gt 0) {
  'check-automation-workflow: FAILED'
  $findings | Sort-Object
  exit 1
}

'check-automation-workflow: OK'
