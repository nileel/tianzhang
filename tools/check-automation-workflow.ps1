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

function Assert-Contract {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Read-Utf8Contract {
  param([string]$Path)
  Assert-Contract -Condition (Test-Path -LiteralPath $Path -PathType Leaf) -Message "missing contract file: $Path"
  try {
    [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  } catch {
    throw "contract is not valid UTF-8: $Path"
  }
}

function Normalize-ContractText {
  param([string]$Text)
  ($Text -replace "`r`n", "`n" -replace "`r", "`n").TrimEnd()
}

function Assert-Contains {
  param([string]$Text, [string[]]$Values, [string]$Context)
  foreach ($value in $Values) {
    Assert-Contract -Condition $Text.Contains($value, [StringComparison]::OrdinalIgnoreCase) -Message "$Context is missing: $value"
  }
}

function Read-Automation {
  param([string]$Directory)
  $path = Join-Path $Directory 'automation.toml'
  $text = Read-Utf8Contract -Path $path
  $statusMatches = @([regex]::Matches($text, '(?m)^status\s*=\s*"(?<value>ACTIVE|PAUSED)"\s*$'))
  $promptMatches = @([regex]::Matches($text, '(?m)^prompt\s*=\s*(?<value>"(?:[^"\\]|\\.)*")\s*$'))
  Assert-Contract -Condition ($statusMatches.Count -eq 1) -Message "automation status is invalid: $path"
  Assert-Contract -Condition ($promptMatches.Count -eq 1) -Message "automation prompt is invalid: $path"
  try {
    $prompt = $promptMatches[0].Groups['value'].Value | ConvertFrom-Json
  } catch {
    throw "automation prompt cannot be decoded: $path"
  }
  [pscustomobject]@{
    Id = Split-Path -Leaf $Directory
    Status = $statusMatches[0].Groups['value'].Value
    Prompt = [string]$prompt
  }
}

$root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
$automationDirectory = [IO.Path]::GetFullPath($AutomationRoot).TrimEnd('\', '/')
Assert-Contract -Condition (Test-Path -LiteralPath $root -PathType Container) -Message "RepositoryRoot does not exist: $root"
Assert-Contract -Condition (Test-Path -LiteralPath $automationDirectory -PathType Container) -Message "AutomationRoot does not exist: $automationDirectory"

$prompt = Read-Utf8Contract -Path (Join-Path $root '开发管理\自动工作流控制器提示词.txt')
$rules = Read-Utf8Contract -Path (Join-Path $root '开发管理\自动工作流规则.txt')
$status = Read-Utf8Contract -Path (Join-Path $root '开发管理\自动工作流状态.txt')
$dailyPrompt = Read-Utf8Contract -Path (Join-Path $root '开发管理\自动化简报提示词.txt')

Assert-Contains -Text $prompt -Context 'thin controller prompt' -Values @(
  'tools/hourly-automation-lease.ps1',
  'Show',
  '开发管理/自动工作流规则.txt',
  'Acquire',
  'tools/invoke-codex-responsibility.ps1',
  '每轮只',
  'commitSha'
)
Assert-Contains -Text $prompt -Context 'deferred wait contract' -Values @(
  'Script running with cell ID',
  '同一 cell ID',
  'functions.wait',
  '空输出',
  '不是终态'
)
Assert-Contract `
  -Condition (-not [regex]::IsMatch($prompt, '(?i)timeout_ms\s*=\s*180000')) `
  -Message 'invocation timeout contract contains the forbidden 180000ms hard timeout'
Assert-Contains -Text $prompt -Context 'invocation timeout contract' -Values @(
  'tools.shell_command',
  '不得使用 180000 毫秒',
  'timeout_ms',
  '3300000 毫秒',
  '3600 秒租约',
  '5 分钟边界'
)
Assert-Contains -Text $rules -Context 'workflow rules' -Values @(
  '单写入租约',
  'RECOVERY_ONLY',
  'Acquire -ResumeRecovery',
  'tools/invoke-codex-responsibility.ps1',
  'RecordResult',
  'Release',
  'businessCommit',
  'handoffCommit',
  'PROVIDER_ACCEPTED',
  'SaveRecovery'
)
Assert-Contains -Text $dailyPrompt -Context 'daily briefing prompt' -Values @(
  'tools/get-automation-briefing-source.ps1',
  'Result',
  'Impact',
  'Verify',
  'Task',
  'memory',
  'handoff'
)

$showIndex = $prompt.IndexOf('Show', [StringComparison]::Ordinal)
$routingIndex = $prompt.IndexOf('开发管理/自动工作流规则.txt', [StringComparison]::Ordinal)
Assert-Contract -Condition ($showIndex -ge 0 -and $routingIndex -ge 0 -and $showIndex -lt $routingIndex) -Message 'runtime Show must occur before routing sources'

foreach ($token in @('Buffer', 'TextEncoder', 'ProcessStartInfo', "@'", '@"')) {
  Assert-Contract -Condition (-not $prompt.Contains($token, [StringComparison]::OrdinalIgnoreCase)) -Message "controller contains forbidden implementation token: $token"
}

$activeText = $prompt + "`n" + $rules
foreach ($token in @(
    'RecordQueueState',
    'ClearWorkerFailure',
    'SubmitManifest',
    'DiscoverRead',
    'planOnly',
    '自动工作流任务注册表',
    'hourly-controller-v2'
  )) {
  Assert-Contract -Condition (-not $activeText.Contains($token, [StringComparison]::OrdinalIgnoreCase)) -Message "active contract contains retired workflow token: $token"
}
Assert-Contract -Condition (-not [regex]::IsMatch($activeText, '(?i)\b(?:TQ|HANDOFF|DEC|REVIEW)-[A-Z0-9-]+')) -Message 'active contract contains a concrete task or decision id'
Assert-Contract -Condition (-not [regex]::IsMatch($status, '(?im)^.*生产入口.*\b(?:ACTIVE|PAUSED)\b.*$')) -Message 'workflow status contains a static live status claim'

foreach ($requiredPath in @(
    'tools\hourly-automation-lease.ps1',
    'tools\codex-cli-session.ps1',
    'tools\invoke-codex-responsibility.ps1',
    'tools\automation-workspace-guard.ps1',
    'tools\automation-finalize-commit.ps1',
    'tools\get-automation-briefing-source.ps1',
    'tools\feishu-decision-bridge\src\resume-trigger.mjs'
  )) {
  Assert-Contract -Condition (Test-Path -LiteralPath (Join-Path $root $requiredPath) -PathType Leaf) -Message "missing workflow component: $requiredPath"
}

$automationDirectories = @(Get-ChildItem -LiteralPath $automationDirectory -Directory -Filter 'tzg-*')
$automations = @($automationDirectories | ForEach-Object { Read-Automation -Directory $_.FullName })
$controllers = @($automations | Where-Object { $_.Id -eq 'tzg-hourly-controller' })
$dailyBriefings = @($automations | Where-Object { $_.Id -eq 'tzg-daily-automation-briefing' })
Assert-Contract -Condition ($controllers.Count -eq 1) -Message 'tzg-hourly-controller configuration is missing or duplicated'
Assert-Contract -Condition ($dailyBriefings.Count -eq 1) -Message 'tzg-daily-automation-briefing configuration is missing or duplicated'
Assert-Contract `
  -Condition ((Normalize-ContractText -Text $controllers[0].Prompt) -ceq (Normalize-ContractText -Text $prompt)) `
  -Message 'controller prompt does not match the canonical prompt'
Assert-Contract `
  -Condition ((Normalize-ContractText -Text $dailyBriefings[0].Prompt) -ceq (Normalize-ContractText -Text $dailyPrompt)) `
  -Message 'daily briefing prompt does not match the canonical prompt'

$writers = @($automations | Where-Object { $_.Id -ne 'tzg-daily-automation-briefing' })
$activeWriters = @($writers | Where-Object { $_.Status -eq 'ACTIVE' })
Assert-Contract -Condition ($activeWriters.Count -le 1) -Message 'more than one writer automation is ACTIVE'
if ($activeWriters.Count -eq 1) {
  Assert-Contract -Condition ($activeWriters[0].Id -eq 'tzg-hourly-controller') -Message "unexpected writer automation is ACTIVE: $($activeWriters[0].Id)"
}
if ($RequireActive) {
  Assert-Contract -Condition ($activeWriters.Count -eq 1 -and $controllers[0].Status -eq 'ACTIVE') -Message 'tzg-hourly-controller is not the unique ACTIVE writer automation'
}

if ($RequireLegacyRetired) {
  foreach ($legacyPath in @(
      'tools\hourly-controller-v2',
      'tools\automation-controller.ps1',
      'tools\automation-controller-state.ps1',
      'tools\automation-controller-repair.ps1',
      'tools\automation-decision-status.ps1',
      '开发管理\自动工作流任务注册表.json',
      '开发管理\自动工作流控制器v2提示词.txt',
      '开发管理\自动工作流v2规则.txt'
    )) {
    Assert-Contract -Condition (-not (Test-Path -LiteralPath (Join-Path $root $legacyPath))) -Message "legacy workflow path still exists: $legacyPath"
  }
}

Write-Output 'check-automation-workflow: OK'
