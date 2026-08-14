#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Write-Automation {
  param([string]$Id, [string]$Status, [string]$Prompt)
  $encoded = $Prompt | ConvertTo-Json -Compress
  Write-Utf8 (Join-Path $automationRoot "$Id/automation.toml") "version = 1`nid = `"$Id`"`nprompt = $encoded`nstatus = `"$Status`"`n"
}
function Invoke-Checker { param([switch]$Active) $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $checker, '-RepositoryRoot', $repositoryRoot, '-AutomationRoot', $automationRoot, '-RequireLegacyRetired'); if ($Active) { $args += '-RequireActive' }; $output = @(& pwsh @args 2>&1); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = @($output) -join "`n" } }

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $temporaryBase "tzg-workflow-check-test-$testId"
$automationRoot = Join-Path $testRoot 'automations'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$checker = Join-Path $PSScriptRoot 'check-automation-workflow.ps1'

try {
  [IO.Directory]::CreateDirectory($automationRoot) | Out-Null
  $prompts = [ordered]@{
    'codex-hourly-worker' = [IO.File]::ReadAllText((Join-Path $repositoryRoot '开发管理/自动工作流控制器提示词.txt'))
    'deepseek-hourly-trigger' = [IO.File]::ReadAllText((Join-Path $repositoryRoot '开发管理/DeepSeek小时触发提示词.txt'))
    'tzg-daily-automation-briefing' = [IO.File]::ReadAllText((Join-Path $repositoryRoot '开发管理/自动化简报提示词.txt'))
    'tzg-weekly-project-summary' = [IO.File]::ReadAllText((Join-Path $repositoryRoot '开发管理/每周项目总结提示词.txt'))
  }
  Assert-True ($prompts['codex-hourly-worker'] -match 'tools\.mcp__node_repl__js' -and $prompts['codex-hourly-worker'] -match 'codex_model_metadata_invalid') 'Canonical Codex prompt is missing the request metadata channel'
  Assert-True ($prompts['codex-hourly-worker'] -match 'shouldSelfPause' -and $prompts['codex-hourly-worker'] -match 'tools\.codex_app__automation_update' -and $prompts['codex-hourly-worker'] -match "terminal\.cleanup === 'cleaned'") 'Canonical Codex prompt is missing the exact empty-queue self-pause contract'
  $codexCodeBlocks = @([regex]::Matches($prompts['codex-hourly-worker'], '(?s)```js\r?\n(?<body>.*?)\r?\n\s*```'))
  Assert-True ($codexCodeBlocks.Count -eq 2) 'Canonical Codex prompt must contain exactly one long entry cell and one short self-pause cell'
  $longEntryCell = $codexCodeBlocks[0].Groups['body'].Value
  $shortPauseCell = $codexCodeBlocks[1].Groups['body'].Value
  Assert-True ($longEntryCell -match 'invoke-hourly-owner\.ps1' -and $longEntryCell -match 'shouldSelfPause') 'Long Codex entry cell is missing its fixed shared-entry contract'
  Assert-True ($longEntryCell -notmatch 'codex_app__automation_update|automation\.toml') 'Long Codex entry cell still embeds automation management'
  Assert-True ($shortPauseCell -match 'tools\.codex_app__automation_update' -and $shortPauseCell -match "status: 'PAUSED'" -and $shortPauseCell -match 'promptLength' -and $shortPauseCell -match 'promptSha256' -and $shortPauseCell -match 'const updated = parseSnapshot') 'Short Codex self-pause cell is missing deterministic update, readback, or prompt-integrity proof'
  Assert-True ($shortPauseCell -match 'codex_automation_config_invalid' -and $shortPauseCell -match 'codex_self_pause_failed' -and $shortPauseCell -match 'codex_self_pause_config_mismatch') 'Short Codex self-pause cell is missing stable failure codes'
  Assert-True ($shortPauseCell -notmatch "current\.status\s*!==\s*'PAUSED'") 'Short Codex self-pause cell skips the real management call while already paused'
  Assert-True ($shortPauseCell -notmatch 'invoke-hourly-owner\.ps1') 'Short Codex self-pause cell must not invoke the shared entry'
  foreach ($entry in $prompts.GetEnumerator()) { Write-Automation -Id $entry.Key -Status 'PAUSED' -Prompt $entry.Value }
  $paused = Invoke-Checker
  Assert-True ($paused.ExitCode -eq 0 -and $paused.Text -match 'check-automation-workflow: OK') "Paused contract failed: $($paused.Text)"
  $activeRequired = Invoke-Checker -Active
  Assert-True ($activeRequired.ExitCode -ne 0 -and $activeRequired.Text -match 'not ACTIVE') 'RequireActive accepted paused writers'
  foreach ($entry in $prompts.GetEnumerator()) { Write-Automation -Id $entry.Key -Status 'ACTIVE' -Prompt $entry.Value }
  $active = Invoke-Checker -Active
  Assert-True ($active.ExitCode -eq 0) "Active contract failed: $($active.Text)"
  Write-Automation -Id 'deepseek-hourly-trigger' -Status 'ACTIVE' -Prompt 'tampered prompt'
  $tampered = Invoke-Checker
  Assert-True ($tampered.ExitCode -ne 0 -and $tampered.Text -match 'prompt does not match') 'Checker accepted a tampered trigger prompt'
  Write-Output 'test-check-automation-workflow: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($temporaryBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-workflow-check-test-$testId") { throw "Unsafe workflow test cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
