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
  $rules = [IO.File]::ReadAllText((Join-Path $repositoryRoot '开发管理/自动工作流规则.txt'))
  Assert-True ($prompts['codex-hourly-worker'] -match 'tools\.mcp__node_repl__js' -and $prompts['codex-hourly-worker'] -match 'codex_model_metadata_invalid') 'Canonical Codex prompt is missing the request metadata channel'
  Assert-True ($prompts['codex-hourly-worker'] -match 'tools\.exec_command' -and $prompts['codex-hourly-worker'] -match 'tools\.write_stdin' -and $prompts['codex-hourly-worker'] -match 'yield_time_ms: 60000') 'Canonical Codex prompt is missing the exec_command/write_stdin polling contract'
  Assert-True ($prompts['codex-hourly-worker'] -match 'Script running with cell ID' -and $prompts['codex-hourly-worker'] -match '同一 cell 调用 `wait`') 'Canonical Codex prompt is missing the outer functions.exec wait contract'
  Assert-True ($prompts['codex-hourly-worker'] -match '不读取队列或任务卡' -and $prompts['codex-hourly-worker'] -match 'Desktop automation memory' -and $prompts['codex-hourly-worker'] -match '恰好一个简短 `::inbox-item`' -and $prompts['codex-hourly-worker'] -match 'memory 不得改变固定命令') 'Canonical Codex prompt is missing the thin-trigger soft contract'
  $codexCodeBlocks = @([regex]::Matches($prompts['codex-hourly-worker'], '(?s)```js\r?\n(?<body>.*?)\r?\n\s*```'))
  Assert-True ($codexCodeBlocks.Count -eq 1) 'Canonical Codex prompt must contain exactly one shared-entry cell'
  $longEntryCell = $codexCodeBlocks[0].Groups['body'].Value
  Assert-True ($longEntryCell -match 'invoke-hourly-owner\.ps1' -and $longEntryCell -match 'tools\.exec_command' -and $longEntryCell -match 'tools\.write_stdin') 'Long Codex entry cell is missing its fixed shared-entry contract'
  Assert-True ($rules -notmatch '短命配置 cell|自暂停') 'Workflow rules still authorize Codex trigger self-pause'
  foreach ($retiredToken in @('shell_command', 'timeout_ms: 3060000', 'shouldSelfPause', "terminal.status === 'no_candidate'", "terminal.owner === 'codex'", "terminal.taskId === 'QUEUE-MAINTENANCE'", "terminal.detailCode === 'no_runnable_candidate'", "terminal.cleanup === 'cleaned'", 'tools.codex_app__automation_update', "status: 'PAUSED'", 'automation.toml')) {
    Assert-True (-not $prompts['codex-hourly-worker'].Contains($retiredToken)) "Canonical Codex prompt still contains retired token: $retiredToken"
  }
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
