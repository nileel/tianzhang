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
  $name = [regex]::Match($text, '(?m)^name\s*=\s*"(?<v>[^"]+)"\s*$')
  $status = [regex]::Match($text, '(?m)^status\s*=\s*"(?<v>ACTIVE|PAUSED)"\s*$')
  $prompt = [regex]::Match($text, '(?m)^prompt\s*=\s*(?<v>"(?:[^"\\]|\\.)*")\s*$')
  Assert-Contract ($id.Success -and $name.Success -and $status.Success -and $prompt.Success) "automation contract is invalid: $path"
  try { $decodedPrompt = $prompt.Groups['v'].Value | ConvertFrom-Json } catch { throw "automation prompt cannot be decoded: $path" }
  [pscustomobject]@{ Id = $id.Groups['v'].Value; Name = $name.Groups['v'].Value; Status = $status.Groups['v'].Value; Prompt = [string]$decodedPrompt }
}

$root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
$automationDirectory = [IO.Path]::GetFullPath($AutomationRoot).TrimEnd('\', '/')
Assert-Contract (Test-Path -LiteralPath $root -PathType Container) "RepositoryRoot does not exist: $root"
Assert-Contract (Test-Path -LiteralPath $automationDirectory -PathType Container) "AutomationRoot does not exist: $automationDirectory"

$requiredScripts = @(
  'tools/hourly-automation-lease.ps1', 'tools/hourly-integration-lock.ps1', 'tools/hourly-owner-adapter.ps1',
  'tools/invoke-hourly-owner.ps1', 'tools/invoke-project-integration.ps1', 'tools/select-hourly-task.ps1',
  'tools/invoke-codex-candidate.ps1', 'tools/codex-cli-session.ps1',
  'tools/invoke-deepseek-responsibility.ps1', 'tools/set-task-pending-review.ps1', 'tools/set-task-automation-state.ps1',
  'tools/automation-finalize-commit.ps1', 'tools/send-feishu-notification.ps1', 'tools/test-hourly-task-input-materialization.ps1',
  'tools/get-project-summary-source.ps1', 'tools/test-get-project-summary-source.ps1',
  'tools/get-experience-risk-preflight.ps1', 'tools/test-hourly-experience-preflight.ps1'
)
foreach ($relative in $requiredScripts) {
  $path = Join-Path $root $relative
  Assert-Contract (Test-Path -LiteralPath $path -PathType Leaf) "missing workflow component: $relative"
  Assert-PowerShellParses $path
}
foreach ($retiredSummaryPath in @('tools/get-automation-briefing-source.ps1', 'tools/test-get-automation-briefing-source.ps1')) {
  Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $root $retiredSummaryPath))) "retired summary source still exists: $retiredSummaryPath"
}

$runtime = Read-Utf8 (Join-Path $root 'tools/hourly-automation-lease.ps1')
Assert-Contains $runtime 'schema 5 runtime' @(
  "[ValidateSet('Show', 'ClaimRun', 'UpdateRun', 'CompleteRun')]",
  'schemaVersion = 5', "runs = [pscustomobject][ordered]@{", 'integrationLockStatus'
)
Assert-DoesNotContain $runtime 'schema 5 runtime' @("'AcquireIntegration'", "'ReleaseIntegration'", 'integrationLease =', "'RecordResult'", "'SaveRecovery'", "'SaveInterruption'")

$sharedEntry = Read-Utf8 (Join-Path $root 'tools/invoke-hourly-owner.ps1')
$adapter = Read-Utf8 (Join-Path $root 'tools/hourly-owner-adapter.ps1')
Assert-Contains $sharedEntry 'shared owner entry' @("[ValidateSet('codex', 'deepseek')]", 'Enter-TzgIntegrationLock', 'maintenance_completed', 'existing_run', 'Remove-ExactSuccessfulWorktree', 'review_rework', 'Apply-AnsweredReviewRework', 'allowCustomReply = $false', 'hourly_codex_model_unverified', 'Add-AttentionNotification', 'Get-HourlyFormalCommitContract -Adapter $adapter -Run $Run', "@('cherry-pick', '--no-commit', [string]`$Run.candidateCommit)", '& $finalizerPath -RepositoryRoot $Worktree @Parameters', 'AutomationState = [string]$formalContract.state')
Assert-Contains $sharedEntry 'successful cleanup run evidence' @(
  '$run.canonicalBranch = [string]$outcome.stateBranch', '$run.canonicalHead = [string]$outcome.formalHead', '$outcome.cleanup = Remove-ExactSuccessfulWorktree -Run $run',
  '$run.canonicalBranch = [string]$run.candidateBranch', '$run.canonicalHead = [string]$run.baseCommit', 'Remove-ExactSuccessfulWorktree -Run $run -FormalHead ([string]$run.baseCommit)',
  '$run.canonicalBranch = [string]$transition.stateBranch', '$run.canonicalHead = [string]$transition.formalHead', '$transition.cleanup = Remove-ExactSuccessfulWorktree -Run $run'
)
Assert-DoesNotContain $sharedEntry 'successful cleanup run evidence' @('Remove-ExactSuccessfulWorktree -Run ([pscustomobject]', '$emptyRun = [pscustomobject]@{')
Assert-Contains $sharedEntry 'task input materialization' @('Get-TaskAutomationInputs', 'Materialize-TaskAutomationInputs', 'Assert-MaterializedAutomationInputs', 'Read-RunTaskMetadata', 'Read-TaskMetadataAtCommit', 'hourly_task_input_validation_failed', 'hourly_task_changed_after_claim')
$sharedDigestMatch = [regex]::Match($sharedEntry, '(?s)function Get-TaskContextDigest\s*\{(?<body>.*?)\r?\n\}')
Assert-Contract $sharedDigestMatch.Success 'shared owner entry is missing Get-TaskContextDigest'
Assert-Contains $sharedDigestMatch.Groups['body'].Value 'shared task context digest' @('automationInputs = @(', 'path =', 'bytes =', 'sha256 =', 'sourceBacklog =')
$taskCards = Read-Utf8 (Join-Path $root 'tools/check-task-cards.ps1')
Assert-Contains $taskCards 'task-card automation inputs' @('Assert-AutomationInputs', 'automationInputs requires route=codex_execute owner=codex', 'automationInputs path must be under assets/source/')
Assert-Contains $taskCards 'QueueMaintenance ready schema 2 guard' @(
  'QueueMaintenanceReadySchema2Guard', 'BaseCommit is required for QueueMaintenanceReadySchema2Guard',
  "@('-c', 'core.quotepath=false', 'ls-tree'", '@(''show'', "${resolvedBaseCommit}:$relativePath")',
  "@('merge-base', '--is-ancestor'", 'QueueMaintenance ready transition requires schemaVersion=2', 'Assert-Schema2Projection'
)
$combinedValidationMatch = [regex]::Match($sharedEntry, '(?s)function Invoke-CombinedValidation\s*\{(?<body>.*?)\r?\n\}\r?\n\r?\nfunction Test-MainPathConflict')
Assert-Contract $combinedValidationMatch.Success 'shared owner entry is missing combined validation'
$combinedValidation = $combinedValidationMatch.Groups['body'].Value
Assert-Contract ([regex]::Matches($combinedValidation, [regex]::Escape('Push-Location -LiteralPath $Worktree')).Count -ge 2) 'shared owner entry does not run data-chain validation from the worktree'
Assert-Contains $combinedValidation 'combined validation' @('$dataChainExitCode = $LASTEXITCODE', "Stop-Hourly 'hourly_data_chain_failed'")
$taskState = Read-Utf8 (Join-Path $root 'tools/set-task-automation-state.ps1')
Assert-Contains $taskState 'task state transition' @('RequeueReview', 'review_rework', 'ExternalDispatchReady', 'CodexDispatchReady')
$taskStateDigestMatch = [regex]::Match($taskState, '(?s)function Get-TaskContextDigest\s*\{(?<body>.*?)\r?\n\}')
Assert-Contract $taskStateDigestMatch.Success 'task state transition is missing Get-TaskContextDigest'
Assert-Contains $taskStateDigestMatch.Groups['body'].Value 'task state context digest' @('automationInputs =', 'path =', 'bytes =', 'sha256 =', 'sourceBacklog =')
Assert-Contains $adapter 'owner adapter' @('codex_execute', 'codex_review', 'queue_maintenance', 'external_execute', 'deepseek-v4-pro', 'Test-HourlyOwnerModelVerified')
Assert-DoesNotContain $adapter 'owner adapter' @('git ', 'hourly-automation-lease.ps1', 'Enter-TzgIntegrationLock', 'CompleteRun')
$codexCandidate = Read-Utf8 (Join-Path $root 'tools/invoke-codex-candidate.ps1')
$deepseekCandidate = Read-Utf8 (Join-Path $root 'tools/invoke-deepseek-responsibility.ps1')
Assert-Contains $sharedEntry 'shared owner entry preflight' @('get-experience-risk-preflight.ps1', 'Invoke-ExperiencePreflight', 'preflight-results', 'experience_preflight_schema_invalid', 'experience_preflight_matcher_failed', 'experience_preflight_overbroad', 'experience_preflight_binding_mismatch', 'experience_preflight_projection_mismatch', 'experience_preflight_gate_invalid')
Assert-Contains $adapter 'owner adapter preflight' @('PreflightResultPath', "'-PreflightResultPath'")
Assert-Contains $codexCandidate 'Codex candidate preflight' @('PreflightResultPath', 'Read-PreflightResult', 'codex_preflight_required', 'codex_preflight_invalid')
Assert-Contains $deepseekCandidate 'DeepSeek candidate preflight' @('PreflightResultPath', 'Read-PreflightResult', 'deepseek_preflight_required', 'deepseek_preflight_invalid')
Assert-Contains $codexCandidate 'QueueMaintenance candidate schema 2 guard' @(
  '新建 ready 卡或把非 ready 卡重新置为 ready 前', 'tools/get-experience-risk-preflight.ps1',
  'status=preflight_overbroad', 'gatePointers', "'-Postcondition', 'QueueMaintenanceReadySchema2Guard'", "'-BaseCommit', [string]`$run.baseCommit"
)
Assert-Contains $sharedEntry 'QueueMaintenance canonical schema 2 guard' @(
  "'-Postcondition', 'QueueMaintenanceReadySchema2Guard'", "'-BaseCommit', `$BaseCommit",
  'Assert-Postcondition -Run $Run -Worktree $Worktree -BaseCommit $Base',
  'Assert-Postcondition -Run $Run -Worktree $script:root -BaseCommit $latest'
)

$rules = Read-Utf8 (Join-Path $root '开发管理/自动工作流规则.txt')
$recovery = Read-Utf8 (Join-Path $root '开发管理/自动工作流恢复规则.txt')
$codexPrompt = Read-Utf8 (Join-Path $root '开发管理/自动工作流控制器提示词.txt')
$deepseekPrompt = Read-Utf8 (Join-Path $root '开发管理/DeepSeek小时触发提示词.txt')
$collaborationRules = Read-Utf8 (Join-Path $root '开发管理/AI协作规则.txt')
$workflowState = Read-Utf8 (Join-Path $root '开发管理/自动工作流状态.txt')
$deepseekWorkPrompt = Read-Utf8 (Join-Path $root '开发管理/DeepSeek工作提示词.txt')
$dailySummaryPrompt = Read-Utf8 (Join-Path $root '开发管理/自动化简报提示词.txt')
$weeklySummaryPrompt = Read-Utf8 (Join-Path $root '开发管理/每周项目总结提示词.txt')
$projectSummarySource = Read-Utf8 (Join-Path $root 'tools/get-project-summary-source.ps1')
$deepseekTemplatePaths = @(
  '开发管理/DeepSeek任务卡-局部代码实现.txt',
  '开发管理/DeepSeek任务卡-批量设计内容.txt',
  '开发管理/DeepSeek任务卡-文档清洗.txt',
  '开发管理/DeepSeek任务卡-CSV数据链路.txt'
)
Assert-Contains $rules 'workflow rules' @('codex-hourly-worker', 'deepseek-hourly-trigger', 'invoke-hourly-owner.ps1', 'runs.codex', 'runs.deepseek', 'schemaVersion=5', '.worktrees/automation/<runId>/<owner>', 'candidate_ready', 'canonical_ready', 'CompleteRun', 'maintenance_completed', '一次性返工决策卡', '不含 checkpoint', 'automationDecision', 'decision_requested', 'waiting_decision', '7 天 TTL', 'status 白名单', 'automationInputs', 'hourly_task_input_validation_failed', '触发器不读写 automation memory', 'automation 运行历史与原始 JSON 承载')
Assert-DoesNotContain $rules 'workflow rules' @('integrationLease', 'invoke-codex-hourly.ps1', 'invoke-deepseek-hourly.ps1', 'automation memory 只记录本轮时间与脚本终态摘要')
Assert-DoesNotContain $rules 'workflow rules' @('invoke-codex-responsibility.ps1', 'invoke-external-responsibility.ps1', 'RecordResult -Category success', 'pauseRequested=true', '短命配置 cell', '自暂停')
Assert-Contains $recovery 'recovery rules' @('developing', 'candidate_ready', 'canonical_ready', 'integrated', 'attention_required', '只报告', 'decision checkpoint')
Assert-Contains $codexPrompt 'Codex worker prompt' @('tools.mcp__node_repl__js', 'nodeRepl.requestMeta', 'codex_model_metadata_invalid', 'modelTexts.length !== 1', 'invoke-hourly-owner.ps1', '-Owner codex', 'tools.exec_command', 'tools.write_stdin', 'yield_time_ms: 60000', 'Script running with cell ID', '不读取队列或任务卡', '不得读取、创建、更新或删除任何 `memory.md`', 'automation 运行历史和原始 JSON 承载终态', '恰好一个简短 `::inbox-item`', '两者之间不得执行文件写入')
Assert-Contains $deepseekPrompt 'DeepSeek trigger prompt' @('invoke-hourly-owner.ps1', '-Owner deepseek', '-Action RunOnce', 'tools.exec_command', 'tools.write_stdin', 'yield_time_ms: 60000', 'Script running with cell ID', 'deepseek_exec_session_invalid', 'deepseek_shared_entrypoint_failed', 'deepseek_terminal_json_invalid', '不读取队列、任务卡或业务事实', '不得读取、创建、更新或删除任何 `memory.md`', 'automation 运行历史和原始 JSON 承载终态', '恰好一个简短 `::inbox-item`', '两者之间不得执行文件写入')
Assert-DoesNotContain $codexPrompt 'Codex worker prompt' @('不得添加解释、其他 commentary、automation memory、`::inbox-item`', '最终回复只能是脚本返回的单个结构化终态 JSON 原文', 'shell_command', 'timeout_ms: 3060000', 'shouldSelfPause', "terminal.status === 'no_candidate'", "terminal.owner === 'codex'", "terminal.taskId === 'QUEUE-MAINTENANCE'", "terminal.detailCode === 'no_runnable_candidate'", "terminal.cleanup === 'cleaned'", 'tools.codex_app__automation_update', "status: 'PAUSED'", 'automation.toml', '读取并在结束时更新本 automation 的 memory', '按 Desktop automation memory 合同读取并在结束时更新', 'memory 只记录本轮时间')
Assert-DoesNotContain $deepseekPrompt 'DeepSeek trigger prompt' @('不得添加解释、其他 commentary、automation memory、`::inbox-item`', '最终回复只能是脚本返回的单个结构化终态 JSON 原文', 'shell_command', 'timeout_ms: 3060000', 'shouldSelfPause', 'tools.codex_app__automation_update', 'automation.toml', '读取并在结束时更新本 automation 的 memory', '按 Desktop automation memory 合同读取并在结束时更新', 'memory 只记录本轮时间')
Assert-Contains $dailySummaryPrompt 'daily project summary prompt' @('tools/get-project-summary-source.ps1', '`commits`', '`automationGroups`', '`sourceArtActivity`', '人工与 Automation', '只对拟写入正文的提交读取必要 diff', '`lastSuccessfulUntil`', '旧日报', '本地美术活动（待登记）')
Assert-DoesNotContain $dailySummaryPrompt 'daily project summary prompt' @('本时间窗没有带有效 Automation 元数据的业务提交', 'tools/get-automation-briefing-source.ps1')
Assert-Contains $weeklySummaryPrompt 'weekly project summary prompt' @('tools/get-project-summary-source.ps1', '`commits`', '`automationGroups`', '`sourceArtActivity`', '人工与 Automation', '只对拟写入正文的提交读取必要 diff', '`lastSuccessfulUntil`', '本地美术活动（待登记）')
Assert-DoesNotContain $weeklySummaryPrompt 'weekly project summary prompt' @('tools/get-automation-briefing-source.ps1')
Assert-Contains $projectSummarySource 'project summary source' @('refs/heads/master', "'ls-files', '--cached'", 'automationGroups', 'sourceArtActivity', 'ReparsePoint', 'Desktop.ini', 'art_source_error')
Assert-DoesNotContain $collaborationRules 'collaboration rules' @('外部两提交边界', '业务提交与交接提交')
Assert-DoesNotContain $workflowState 'workflow state' @('正式结果仍是连续的 `businessCommit`', '仅含交接登记的 `handoffCommit`')
Assert-DoesNotContain $deepseekWorkPrompt 'DeepSeek work prompt' $deepseekTemplatePaths
foreach ($relative in $deepseekTemplatePaths) {
  $template = Read-Utf8 (Join-Path $root $relative)
  Assert-Contains $template "retired DeepSeek template $relative" @('已退役', '不再是活动执行规则', 'AGENTS.md', '开发管理/DeepSeek工作提示词.txt')
  Assert-DoesNotContain $template "retired DeepSeek template $relative" @('## 必读', '## 执行要求')
}

if ($RequireLegacyRetired) {
  foreach ($relative in @(
      'tools/invoke-codex-responsibility.ps1', 'tools/test-invoke-codex-responsibility.ps1',
      'tools/invoke-external-responsibility.ps1', 'tools/test-invoke-external-responsibility.ps1',
      'tools/test-external-ai-self-commit.ps1', 'tools/hourly-controller-v2', 'tools/automation-controller.ps1'
      'tools/invoke-codex-hourly.ps1', 'tools/test-invoke-codex-hourly.ps1',
      'tools/invoke-deepseek-hourly.ps1', 'tools/test-invoke-deepseek-hourly.ps1'
    )) { Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $root $relative))) "legacy workflow path still exists: $relative" }
}

$automations = @(Get-ChildItem -LiteralPath $automationDirectory -Directory | ForEach-Object { Read-Automation $_.FullName })
$expected = [ordered]@{
  'codex-hourly-worker' = $codexPrompt
  'deepseek-hourly-trigger' = $deepseekPrompt
  'tzg-daily-automation-briefing' = $dailySummaryPrompt
  'tzg-weekly-project-summary' = $weeklySummaryPrompt
}
foreach ($entry in $expected.GetEnumerator()) {
  $matches = @($automations | Where-Object { $_.Id -ceq $entry.Key })
  Assert-Contract ($matches.Count -eq 1) "automation configuration is missing or duplicated: $($entry.Key)"
  Assert-Contract ((Normalize-Text $matches[0].Prompt) -ceq (Normalize-Text ([string]$entry.Value))) "automation prompt does not match canonical prompt: $($entry.Key)"
  if ($entry.Key -ceq 'tzg-daily-automation-briefing') {
    Assert-Contract ($matches[0].Name -ceq 'TZG Daily Project Summary') 'daily project summary automation name is incorrect'
  }
  if ($RequireActive) { Assert-Contract ($matches[0].Status -ceq 'ACTIVE') "automation is not ACTIVE: $($entry.Key)" }
}
if ($RequireLegacyRetired) {
  Assert-Contract (@($automations | Where-Object { $_.Id -ceq 'tzg-hourly-controller' }).Count -eq 0) 'legacy automation still exists: tzg-hourly-controller'
}

Write-Output 'check-automation-workflow: OK'
