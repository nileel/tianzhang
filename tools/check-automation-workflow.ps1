#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$AutomationRoot = (Join-Path $env:USERPROFILE '.codex\automations'),
  [switch]$RequireActive,
  [switch]$RequireLegacyRetired
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Read-Utf8 {
  param([string]$Path)
  Assert-Contract (Test-Path -LiteralPath $Path -PathType Leaf) "missing contract file: $Path"
  try { [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF) } catch { throw "contract is not valid UTF-8: $Path" }
}
function Normalize-Text { param([string]$Text) ($Text -replace "`r`n", "`n" -replace "`r", "`n").TrimEnd() }
function Assert-Contains {
  param([string]$Text, [string]$Context, [string[]]$Values)
  foreach ($value in $Values) { Assert-Contract $Text.Contains($value, [StringComparison]::OrdinalIgnoreCase) "$Context is missing: $value" }
}
function Assert-DoesNotContain {
  param([string]$Text, [string]$Context, [string[]]$Values)
  foreach ($value in $Values) { Assert-Contract (-not $Text.Contains($value, [StringComparison]::OrdinalIgnoreCase)) "$Context contains retired contract: $value" }
}
function Assert-PowerShellParses {
  param([string]$Path)
  $tokens = $null; $errors = $null
  [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
  Assert-Contract (@($errors).Count -eq 0) "PowerShell parse failed: $Path"
}
function Read-Automation {
  param([string]$Directory)
  $path = Join-Path $Directory 'automation.toml'; $text = Read-Utf8 $path
  $id = [regex]::Match($text, '(?m)^id\s*=\s*"(?<v>[^"]+)"\s*$')
  $status = [regex]::Match($text, '(?m)^status\s*=\s*"(?<v>ACTIVE|PAUSED)"\s*$')
  $prompt = [regex]::Match($text, '(?m)^prompt\s*=\s*(?<v>"(?:[^"\\]|\\.)*")\s*$')
  Assert-Contract ($id.Success -and $status.Success -and $prompt.Success) "automation contract is invalid: $path"
  try { $decodedPrompt = $prompt.Groups['v'].Value | ConvertFrom-Json } catch { throw "automation prompt cannot be decoded: $path" }
  [pscustomobject]@{ Id = $id.Groups['v'].Value; Status = $status.Groups['v'].Value; Prompt = [string]$decodedPrompt }
}

$root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
$automationDirectory = [IO.Path]::GetFullPath($AutomationRoot).TrimEnd('\', '/')
Assert-Contract (Test-Path -LiteralPath $root -PathType Container) "RepositoryRoot does not exist: $root"
Assert-Contract (Test-Path -LiteralPath $automationDirectory -PathType Container) "AutomationRoot does not exist: $automationDirectory"

$requiredScripts = @(
  'tools/hourly-automation-lease.ps1', 'tools/select-hourly-task.ps1',
  'tools/invoke-codex-hourly.ps1', 'tools/invoke-codex-candidate.ps1', 'tools/codex-cli-session.ps1',
  'tools/invoke-deepseek-hourly.ps1', 'tools/invoke-deepseek-responsibility.ps1', 'tools/set-task-pending-review.ps1',
  'tools/automation-finalize-commit.ps1', 'tools/send-feishu-notification.ps1'
)
foreach ($relative in $requiredScripts) {
  $path = Join-Path $root $relative
  Assert-Contract (Test-Path -LiteralPath $path -PathType Leaf) "missing workflow component: $relative"
  Assert-PowerShellParses $path
}

$runtime = Read-Utf8 (Join-Path $root 'tools/hourly-automation-lease.ps1')
Assert-Contains $runtime 'schema 4 runtime' @(
  "[ValidateSet('Show', 'ClaimRun', 'UpdateRun', 'AcquireIntegration', 'ReleaseIntegration', 'CompleteRun')]",
  'schemaVersion = 4', "runs = [pscustomobject][ordered]@{", 'integrationLease = $null'
)
Assert-DoesNotContain $runtime 'schema 4 runtime' @("'Acquire'", "'RecordResult'", "'SaveRecovery'", "'SaveInterruption'")

$rules = Read-Utf8 (Join-Path $root '开发管理/自动工作流规则.txt')
$recovery = Read-Utf8 (Join-Path $root '开发管理/自动工作流恢复规则.txt')
$codexPrompt = Read-Utf8 (Join-Path $root '开发管理/自动工作流控制器提示词.txt')
$deepseekPrompt = Read-Utf8 (Join-Path $root '开发管理/DeepSeek小时触发提示词.txt')
Assert-Contains $rules 'workflow rules' @('codex-hourly-worker', 'deepseek-hourly-trigger', 'runs.codex', 'runs.deepseek', 'integrationLease', '.worktrees/automation/<runId>/<owner>', 'candidate_ready', 'canonical_ready', 'CompleteRun')
Assert-DoesNotContain $rules 'workflow rules' @('invoke-codex-responsibility.ps1', 'invoke-external-responsibility.ps1', 'RecordResult -Category success', 'pauseRequested=true')
Assert-Contains $recovery 'recovery rules' @('developing', 'candidate_ready', 'canonical_ready', 'integrated', 'attention_required', '不重复')
Assert-Contains $codexPrompt 'Codex worker prompt' @('invoke-codex-hourly.ps1', '-Model "<实际 model>"', '不启动 DeepSeek', '不读取队列或任务卡')
Assert-Contains $deepseekPrompt 'DeepSeek trigger prompt' @('invoke-deepseek-hourly.ps1', '-Action RunOnce', '不得读取队列', '不得选择或 claim 任务')

if ($RequireLegacyRetired) {
  foreach ($relative in @(
      'tools/invoke-codex-responsibility.ps1', 'tools/test-invoke-codex-responsibility.ps1',
      'tools/invoke-external-responsibility.ps1', 'tools/test-invoke-external-responsibility.ps1',
      'tools/test-external-ai-self-commit.ps1', 'tools/hourly-controller-v2', 'tools/automation-controller.ps1'
    )) { Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $root $relative))) "legacy workflow path still exists: $relative" }
}

$automations = @(Get-ChildItem -LiteralPath $automationDirectory -Directory | ForEach-Object { Read-Automation $_.FullName })
$expected = [ordered]@{
  'codex-hourly-worker' = $codexPrompt
  'deepseek-hourly-trigger' = $deepseekPrompt
  'tzg-daily-automation-briefing' = Read-Utf8 (Join-Path $root '开发管理/自动化简报提示词.txt')
  'tzg-weekly-project-summary' = Read-Utf8 (Join-Path $root '开发管理/每周项目总结提示词.txt')
}
foreach ($entry in $expected.GetEnumerator()) {
  $matches = @($automations | Where-Object { $_.Id -ceq $entry.Key })
  Assert-Contract ($matches.Count -eq 1) "automation configuration is missing or duplicated: $($entry.Key)"
  Assert-Contract ((Normalize-Text $matches[0].Prompt) -ceq (Normalize-Text ([string]$entry.Value))) "automation prompt does not match canonical prompt: $($entry.Key)"
  if ($RequireActive) { Assert-Contract ($matches[0].Status -ceq 'ACTIVE') "automation is not ACTIVE: $($entry.Key)" }
}
if ($RequireLegacyRetired) {
  Assert-Contract (@($automations | Where-Object { $_.Id -ceq 'tzg-hourly-controller' }).Count -eq 0) 'legacy automation still exists: tzg-hourly-controller'
}

Write-Output 'check-automation-workflow: OK'
