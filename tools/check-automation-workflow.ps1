#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$AutomationRoot = (Join-Path $env:USERPROFILE '.codex\automations'),
  [switch]$RepositoryOnly,
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
  Assert-Contract (Test-Path -LiteralPath $Path -PathType Leaf) "missing contract file: $Path"
  try {
    [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  } catch {
    throw "contract is not valid UTF-8: $Path"
  }
}

function Assert-Contains {
  param([string]$Text, [string[]]$Values, [string]$Context)
  foreach ($value in $Values) {
    Assert-Contract $Text.Contains($value, [StringComparison]::OrdinalIgnoreCase) "$Context is missing: $value"
  }
}

function Assert-TwoConditionRecoveryRoute {
  param([string]$Text, [string]$Context)
  Assert-Contains $Text @(
    '控制器调度',
    'Show',
    'recovery',
    '开发管理/自动工作流恢复规则.txt',
    '普通责任方',
    '实际到达新的用户决定事件',
    '创建决定恢复',
    '未到达决定事件时不得读取恢复规则'
  ) $Context
}

function Normalize-Text {
  param([string]$Text)
  ($Text -replace "`r`n", "`n" -replace "`r", "`n").TrimEnd()
}

function Read-Automation {
  param([string]$Directory)
  $path = Join-Path $Directory 'automation.toml'
  $text = Read-Utf8Contract $path
  $status = @([regex]::Matches($text, '(?m)^status\s*=\s*"(?<value>ACTIVE|PAUSED)"\s*$'))
  $prompt = @([regex]::Matches($text, '(?m)^prompt\s*=\s*(?<value>"(?:[^"\\]|\\.)*")\s*$'))
  Assert-Contract ($status.Count -eq 1 -and $prompt.Count -eq 1) "automation config is invalid: $path"
  try {
    $decodedPrompt = [string]($prompt[0].Groups['value'].Value | ConvertFrom-Json)
  } catch {
    throw "automation prompt cannot be decoded: $path"
  }
  [pscustomobject]@{
    Id = Split-Path -Leaf $Directory
    Status = $status[0].Groups['value'].Value
    Prompt = $decodedPrompt
  }
}

$root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
Assert-Contract (Test-Path -LiteralPath $root -PathType Container) "RepositoryRoot does not exist: $root"

$paths = [ordered]@{
  prompt = '开发管理/自动工作流控制器提示词.txt'
  rules = '开发管理/自动工作流规则.txt'
  status = '开发管理/自动工作流状态.txt'
  recovery = '开发管理/自动工作流恢复规则.txt'
  maintenance = '开发管理/状态与建议维护规则.txt'
  daily = '开发管理/自动化简报提示词.txt'
  weekly = '开发管理/每周项目总结提示词.txt'
  collaboration = '开发管理/AI协作规则.txt'
  deepseek = '开发管理/DeepSeek工作提示词.txt'
  agents = 'AGENTS.md'
  claude = 'CLAUDE.md'
  lease = 'tools/hourly-automation-lease.ps1'
  core = 'tools/automation-lane-core.ps1'
  batch = 'tools/invoke-automation-lane-batch.ps1'
  codexWorker = 'tools/invoke-codex-lane-worker.ps1'
  externalWorker = 'tools/invoke-external-lane-worker.ps1'
  taskCards = 'tools/check-task-cards.ps1'
  runner = 'tools/codex-cli-session.ps1'
  finalizer = 'tools/automation-finalize-commit.ps1'
  metadata = 'tools/automation-commit-metadata.ps1'
  notification = 'tools/send-feishu-notification.ps1'
  legacyCodex = 'tools/invoke-codex-responsibility.ps1'
  legacyExternal = 'tools/invoke-external-responsibility.ps1'
  guard = 'tools/automation-workspace-guard.ps1'
  laneTests = 'tools/test-automation-lanes.ps1'
  batchTests = 'tools/test-automation-lane-batch.ps1'
}
$contracts = @{}
foreach ($entry in $paths.GetEnumerator()) {
  $contracts[$entry.Key] = Read-Utf8Contract (Join-Path $root $entry.Value)
}

Assert-Contains $contracts.prompt @(
  'hourly-automation-lease.ps1 -Action Show',
  'Show.state.batch != null',
  'invoke-automation-lane-batch.ps1 -Action Recover',
  'invoke-automation-lane-batch.ps1 -Action Start',
  'maxConcurrent=2',
  '不滚动补位',
  'Script running with cell ID',
  '3300000',
  '候选 SHA 不得当成正式交付',
  '不得自动 stash、reset、checkout、clean',
  '不直接调用 Codex/Claude CLI',
  '编辑 automation TOML'
) 'controller prompt'
$showIndex = $contracts.prompt.IndexOf('Show', [StringComparison]::Ordinal)
$queueIndex = $contracts.prompt.IndexOf('队列', [StringComparison]::Ordinal)
Assert-Contract ($showIndex -ge 0 -and $queueIndex -gt $showIndex) 'runtime Show must occur before queue routing'

Assert-Contains $contracts.rules @(
  'schema 4',
  '协调器 lease',
  '通用 `lanes[]`',
  '`maxConcurrent=2`',
  '只启用 `codex` 与 `deepseek`',
  '每 lane 每 batch 最多一项',
  '不滚动补位',
  '`workerPaths`',
  '`coordinatorPaths`',
  '候选提交不是正式交付',
  '完成事件',
  '原队列序号',
  '`held_conflict`',
  '`stale_selection`',
  '人工修改',
  '固定串行集成器',
  'canonical businessCommit',
  'handoffCommit',
  'DeepSeek',
  '不得自审',
  '模拟第三 lane',
  '生产配置不得包含虚假第三 AI',
  '安全清理'
) 'workflow rules'
Assert-Contains $contracts.lease @(
  'schemaVersion = 4',
  "'ResumeBatch'",
  "'SaveBatch'",
  "'ClearBatch'",
  'taskClaim',
  'integrationState',
  'BATCH_EVIDENCE_PRESERVED',
  'BATCH_OPEN'
) 'schema-4 runtime'
Assert-Contains $contracts.taskCards @(
  "'schemaVersion'",
  "'workerPaths'",
  "'coordinatorPaths'",
  'expectedPaths must equal workerPaths union coordinatorPaths',
  'sourceBacklog must be a coordinatorPath',
  'queue must be a coordinatorPath'
) 'task-card schema 2'
Assert-Contains $contracts.core @(
  'Get-TzgAutomationLaneConfiguration',
  "laneId = 'codex'",
  "laneId = 'deepseek'",
  'maxConcurrent = 2',
  'Select-TzgAutomationLaneBatch',
  'Test-TzgTaskDependsOn',
  'Get-TzgLaneIntegrationPreflight',
  'Merge-TzgCoordinatorChanges',
  'Merge-TzgTaskProjectionTable',
  'Test-TzgCandidateCommit',
  'Invoke-TzgLaneCanonicalIntegration',
  'Test-TzgLaneCleanupAllowed'
) 'lane core'
Assert-Contract (-not $contracts.core.Contains("laneId = 'simulated-third'", [StringComparison]::Ordinal)) 'simulated third lane leaked into production configuration'
Assert-Contains $contracts.batch @(
  "ValidateSet('Start', 'Recover')",
  'Start-BatchLaneWorker',
  'Complete-FinishedLaneProcesses',
  'Invoke-ReadyLaneIntegrations',
  'Send-LaneOutcomeNotification',
  'send-feishu-notification.ps1',
  'Sort-Object queueIndex',
  'Close-BatchIfTerminal',
  'Remove-SafeLaneWorktrees',
  'ResumeBatch',
  'workerTerminal',
  'integrationState'
) 'fixed batch coordinator'
Assert-Contains $contracts.codexWorker @(
  '[TZG_AUTOMATION_LANE_WORKER]',
  'WorkerPaths',
  'CoordinatorPaths',
  '候选提交',
  '不得修改、stage 或提交 CoordinatorPaths',
  'Assert-TzgLaneWorkerTerminal',
  'Test-TzgCandidateCommit'
) 'Codex lane worker'
Assert-Contains $contracts.externalWorker @(
  'DeepSeek V4 Flash',
  'http',
  '127.0.0.1',
  '15721',
  "'--json-schema'",
  "'--permission-mode', 'dontAsk'",
  'codex_review',
  '不得自审',
  'Test-TzgCandidateCommit'
) 'external lane worker'
foreach ($worker in @($contracts.codexWorker, $contracts.externalWorker)) {
  foreach ($forbidden in @('-Action RecordResult', '-Action Release', 'send-feishu-notification.ps1')) {
    Assert-Contract (-not $worker.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) "lane worker contains forbidden closeout token: $forbidden"
  }
}
Assert-Contains $contracts.runner @('--output-schema', 'OutputLastMessagePath', 'codex_session_id=') 'Codex structured runner'
Assert-Contains $contracts.agents @('taskClaim', 'workerPaths', '固定串行集成器') 'AGENTS automation boundary'
Assert-Contains $contracts.collaboration @('candidate', '固定串行集成器', '不得自审') 'AI collaboration boundary'
Assert-Contains $contracts.deepseek @('candidate commit', 'coordinatorChanges', '不得先写入', '固定集成器') 'DeepSeek lane boundary'
Assert-Contains $contracts.claude @('task claim', 'lane worktree', 'candidate') 'Claude/DeepSeek entry boundary'
Assert-Contains $contracts.status @(
  '实时 status 以 automation 配置为准'
  'schema 4 runtime'
  '生产配置只允许 Codex 与 DeepSeek V4 Flash'
) 'deployment status routing'
Assert-Contains $contracts.laneTests @('simulated-third', 'worker intervals did not overlap', 'held_conflict', 'stale_selection', 'ResumeBatch') 'lane test coverage'
Assert-Contains $contracts.batchTests @('fake worker passed', 'batch did not integrate both lanes in queue order', 'canonical commit order is invalid') 'batch canary coverage'

foreach ($routeContract in @(
    [pscustomobject]@{ Text = $contracts.agents; Context = 'AGENTS recovery route' },
    [pscustomobject]@{ Text = $contracts.collaboration; Context = 'collaboration recovery route' },
    [pscustomobject]@{ Text = $contracts.maintenance; Context = 'maintenance recovery route' }
  )) {
  Assert-TwoConditionRecoveryRoute $routeContract.Text $routeContract.Context
}
Assert-Contains $contracts.recovery @(
  '只有两个读取条件',
  'Show.recovery != null',
  '普通责任方实际到达新的用户决定事件',
  '只读取 `创建决定恢复`',
  'PROVIDER_ACCEPTED',
  'SaveRecovery',
  'Acquire -ResumeRecovery',
  'UTF-8'
) 'legacy recovery contract'
foreach ($consumer in @(
    [pscustomobject]@{ Text = $contracts.finalizer; Context = 'automation finalizer' },
    [pscustomobject]@{ Text = $contracts.notification; Context = 'notification sender' },
    [pscustomobject]@{ Text = $contracts.legacyCodex; Context = 'legacy Codex invoker' },
    [pscustomobject]@{ Text = $contracts.legacyExternal; Context = 'legacy external invoker' }
  )) {
  Assert-Contains $consumer.Text @('automation-commit-metadata.ps1', 'ConvertFrom-TzgAutomationCommitMessage') "$($consumer.Context) metadata contract"
}
Assert-Contains $contracts.daily @(
  'tools/get-automation-briefing-source.ps1',
  'tools/send-feishu-notification.ps1 -Kind DailyReport',
  '6000 个 Unicode code point'
) 'daily briefing contract'
Assert-Contains $contracts.weekly @(
  'lastSuccessfulUntil',
  '开发管理/当前任务队列.txt',
  '## 下周重点',
  'tools/send-feishu-notification.ps1 -Kind WeeklyReport',
  '6000 个 Unicode code point'
) 'weekly summary contract'
foreach ($token in @('Buffer', 'TextEncoder', 'ProcessStartInfo', "@'", '@"')) {
  Assert-Contract (-not $contracts.prompt.Contains($token, [StringComparison]::OrdinalIgnoreCase)) "controller contains forbidden implementation token: $token"
}
$normalContract = $contracts.prompt + "`n" + $contracts.rules + "`n" + $contracts.agents + "`n" + $contracts.collaboration + "`n" + $contracts.maintenance
foreach ($detailToken in @('consume-reply.mjs', 'PROVIDER_ACCEPTED', 'SaveRecovery', 'Resume 原 session')) {
  Assert-Contract (-not $normalContract.Contains($detailToken, [StringComparison]::OrdinalIgnoreCase)) "recovery detail leak in normal contract: $detailToken"
}
Assert-Contract (-not [regex]::IsMatch($contracts.status, '(?im)^.*生产入口.*\b(?:ACTIVE|PAUSED)\b.*$')) 'workflow status contains a static live status claim'

foreach ($token in @('automation.toml', '~/.codex/automations')) {
  Assert-Contract (-not $contracts.batch.Contains($token, [StringComparison]::OrdinalIgnoreCase)) "batch coordinator must not manage live automation config: $token"
}

if (-not $RepositoryOnly) {
  $automationDirectory = [IO.Path]::GetFullPath($AutomationRoot).TrimEnd('\', '/')
  Assert-Contract (Test-Path -LiteralPath $automationDirectory -PathType Container) "AutomationRoot does not exist: $automationDirectory"
  $automations = @(Get-ChildItem -LiteralPath $automationDirectory -Directory -Filter 'tzg-*' | ForEach-Object { Read-Automation $_.FullName })
  $controllers = @($automations | Where-Object Id -eq 'tzg-hourly-controller')
  $dailyBriefings = @($automations | Where-Object Id -eq 'tzg-daily-automation-briefing')
  $weeklySummaries = @($automations | Where-Object Id -eq 'tzg-weekly-project-summary')
  Assert-Contract ($controllers.Count -eq 1) 'tzg-hourly-controller configuration is missing or duplicated'
  Assert-Contract ($dailyBriefings.Count -eq 1) 'tzg-daily-automation-briefing configuration is missing or duplicated'
  Assert-Contract ($weeklySummaries.Count -eq 1) 'tzg-weekly-project-summary configuration is missing or duplicated'
  Assert-Contract (
    (Normalize-Text $controllers[0].Prompt) -ceq (Normalize-Text $contracts.prompt)
  ) 'controller prompt does not match the canonical prompt'
  Assert-Contract (
    (Normalize-Text $dailyBriefings[0].Prompt) -ceq (Normalize-Text $contracts.daily)
  ) 'daily briefing prompt does not match the canonical prompt'
  Assert-Contract (
    (Normalize-Text $weeklySummaries[0].Prompt) -ceq (Normalize-Text $contracts.weekly)
  ) 'weekly project summary prompt does not match the canonical prompt'
  $readOnlyIds = @($dailyBriefings[0].Id, $weeklySummaries[0].Id)
  $activeWriters = @($automations | Where-Object { $_.Id -cnotin $readOnlyIds -and $_.Status -eq 'ACTIVE' })
  Assert-Contract ($activeWriters.Count -le 1) 'more than one writer automation is ACTIVE'
  if ($RequireActive) {
    Assert-Contract ($activeWriters.Count -eq 1 -and $activeWriters[0].Id -eq 'tzg-hourly-controller') 'tzg-hourly-controller is not the unique ACTIVE writer'
  }
}

if ($RequireLegacyRetired) {
  foreach ($legacyPath in @(
      'tools/hourly-controller-v2',
      'tools/automation-controller.ps1',
      '开发管理/自动工作流任务注册表.json'
    )) {
    Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $root $legacyPath))) "legacy workflow path still exists: $legacyPath"
  }
}

Write-Output 'check-automation-workflow: OK'
