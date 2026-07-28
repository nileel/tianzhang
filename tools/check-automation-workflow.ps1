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

function Assert-TwoConditionRecoveryRoute {
  param([string]$Text, [string]$Context)
  Assert-Contains -Text $Text -Context $Context -Values @(
    '控制器调度',
    'Show',
    'existing recovery',
    '开发管理/自动工作流恢复规则.txt',
    '普通责任方',
    '实际到达新的用户决定事件',
    '只读取',
    '创建决定恢复',
    '未到达决定事件时不得读取恢复规则'
  )
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
$recoveryRules = Read-Utf8Contract -Path (Join-Path $root '开发管理\自动工作流恢复规则.txt')
$maintenanceRules = Read-Utf8Contract -Path (Join-Path $root '开发管理\状态与建议维护规则.txt')
$status = Read-Utf8Contract -Path (Join-Path $root '开发管理\自动工作流状态.txt')
$dailyPrompt = Read-Utf8Contract -Path (Join-Path $root '开发管理\自动化简报提示词.txt')
$weeklyPrompt = Read-Utf8Contract -Path (Join-Path $root '开发管理\每周项目总结提示词.txt')
$leaseTool = Read-Utf8Contract -Path (Join-Path $root 'tools\hourly-automation-lease.ps1')
$invokerPath = Join-Path $root 'tools\invoke-codex-responsibility.ps1'
Assert-Contract -Condition (Test-Path -LiteralPath $invokerPath -PathType Leaf) -Message 'missing workflow component: tools\invoke-codex-responsibility.ps1'
$invoker = Read-Utf8Contract -Path $invokerPath
$runnerPath = Join-Path $root 'tools\codex-cli-session.ps1'
Assert-Contract -Condition (Test-Path -LiteralPath $runnerPath -PathType Leaf) -Message 'missing workflow component: tools\codex-cli-session.ps1'
$runner = Read-Utf8Contract -Path $runnerPath
$agentsRules = Read-Utf8Contract -Path (Join-Path $root 'AGENTS.md')
$claudeRules = Read-Utf8Contract -Path (Join-Path $root 'CLAUDE.md')
$collaborationRules = Read-Utf8Contract -Path (Join-Path $root '开发管理\AI协作规则.txt')

Assert-Contains -Text $prompt -Context 'thin controller prompt' -Values @(
  'tools/hourly-automation-lease.ps1',
  'Show',
  '开发管理/自动工作流规则.txt',
  '开发管理/当前任务队列.txt',
  'Acquire',
  'tools/invoke-codex-responsibility.ps1',
  '每轮只',
  'commitSha'
)
Assert-Contains -Text $prompt -Context 'recovery rule route' -Values @(
  'Show` 返回 recovery',
  '开发管理/自动工作流恢复规则.txt',
  '没有 recovery 时不得读取恢复规则',
  'Recovery'
)
Assert-Contains -Text $prompt -Context 'ordered queue route' -Values @(
  '按行顺序',
  '第一项当前可安全执行',
  '临时运行冲突',
  '不修改任务卡或队列顺序',
  'codex_execute -> Execution',
  'codex_review -> Review',
  '队列为空',
  'QueueMaintenance',
  '本轮不执行新任务'
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
Assert-Contains -Text $prompt -Context 'external closeout contract' -Values @(
  'ANTHROPIC_BASE_URL',
  '~/.claude/settings.json',
  'http://127.0.0.1:15721',
  'DeepSeek V4 Pro',
  'RecordResult -Category success',
  'Release',
  '相对基线新增未提交路径',
  '保留现场和租约'
)
Assert-Contains -Text $prompt -Context 'identity source precedence' -Values @(
  '先读进程 `ANTHROPIC_BASE_URL`，为空时只补读 `~/.claude/settings.json`'
)
Assert-Contains -Text $prompt -Context 'external completed gate' -Values @(
  'owner 对应 identity',
  '`sessionId`',
  '`businessCommit`',
  '`handoffCommit`'
)
Assert-Contains -Text $prompt -Context 'success closeout order' -Values @(
  '全部成立后依次调用 `RecordResult -Category success` 与 `Release`'
)
Assert-Contains -Text $prompt -Context 'external outcome notification' -Values @(
  '九个结构化通知子字段',
  'Plain: 发生=<负责人短句>；影响=<负责人短句>；需要=<负责人短句>',
  'tools/send-feishu-notification.ps1',
  '-Kind TaskOutcome',
  '-Status pending_review',
  '飞书返回任何失败都不得改变'
)
Assert-Contains -Text $prompt -Context 'lifecycle result report' -Values @(
  'taskState',
  'readyCount'
)
$identityTokens = @(
  '~/.claude/settings.json',
  'http://127.0.0.1:15721',
  '同源地址',
  'DeepSeek V4 Pro'
)
$externalOwnerMappingTokens = @(
  '已选中的 `external_execute` 同一任务卡',
  '不得重新扫描候选',
  'owner=deepseek -> DeepSeek V4 Pro',
  'owner=claude -> native Claude Code'
)
Assert-Contains -Text $prompt -Context 'external owner mapping in controller prompt' -Values $externalOwnerMappingTokens
Assert-Contains -Text $rules -Context 'external owner mapping in core rules' -Values $externalOwnerMappingTokens
Assert-Contains -Text $claudeRules -Context 'external owner mapping in CLAUDE.md' -Values $externalOwnerMappingTokens
Assert-Contains -Text $collaborationRules -Context 'external owner mapping in collaboration rules' -Values $externalOwnerMappingTokens
$externalTransitionGateTokens = @(
  'tools/check-task-cards.ps1',
  '-TaskId <同一 TaskId>',
  '-Postcondition ExternalPendingReview',
  '生命周期/投影',
  '不读取业务 diff',
  '不读取业务 diff 或重跑领域验证'
)
Assert-Contains -Text $prompt -Context 'external transition gate in controller prompt' -Values $externalTransitionGateTokens
Assert-Contains -Text $rules -Context 'external transition gate in core rules' -Values $externalTransitionGateTokens
Assert-Contains -Text $collaborationRules -Context 'external transition gate in collaboration rules' -Values $externalTransitionGateTokens
foreach ($gateContract in @(
    @{ Text = $prompt; Context = 'controller prompt' },
    @{ Text = $rules; Context = 'core rules' },
    @{ Text = $collaborationRules; Context = 'collaboration rules' }
  )) {
  $gateIndex = $gateContract.Text.IndexOf('-Postcondition ExternalPendingReview', [StringComparison]::Ordinal)
  $successIndex = $gateContract.Text.IndexOf('RecordResult -Category success', [StringComparison]::Ordinal)
  Assert-Contract -Condition ($gateIndex -ge 0 -and $successIndex -gt $gateIndex) -Message "external transition gate must precede success closeout in $($gateContract.Context)"
}
Assert-Contains -Text $claudeRules -Context 'DeepSeek identity contract in CLAUDE.md' -Values $identityTokens
Assert-Contains -Text $collaborationRules -Context 'DeepSeek identity contract in collaboration rules' -Values $identityTokens
Assert-TwoConditionRecoveryRoute -Text $agentsRules -Context 'two-condition recovery route in AGENTS.md'
Assert-TwoConditionRecoveryRoute -Text $collaborationRules -Context 'two-condition recovery route in collaboration rules'
Assert-TwoConditionRecoveryRoute -Text $maintenanceRules -Context 'two-condition recovery route in maintenance rules'
Assert-TwoConditionRecoveryRoute -Text $rules -Context 'two-condition recovery route in core rules'
Assert-Contains -Text $rules -Context 'workflow rules' -Values @(
  '单写入租约',
  'Show',
  'pauseRequested=true',
  '开发管理/自动工作流恢复规则.txt',
  '每轮一个责任方',
  'tools/invoke-codex-responsibility.ps1',
  'RecordResult',
  'Release',
  'businessCommit',
  'handoffCommit'
)
Assert-Contains -Text $rules -Context 'fixed queue order' -Values @(
  '开发管理/当前任务队列.txt',
  'dispatchState=ready',
  'codex_execute',
  'external_execute',
  'codex_review',
  '第一项当前可安全执行'
)
Assert-Contains -Text $rules -Context 'temporary skip contract' -Values @(
  '临时运行条件',
  '跳过本轮',
  '不修改任务卡或队列顺序'
)
Assert-Contains -Text $rules -Context 'two-round pause' -Values @(
  '同一稳定 fingerprint 连续两轮',
  '明确任务阻塞',
  '状态纠正事件'
)
Assert-Contains -Text $rules -Context 'empty queue maintenance-only' -Values @(
  '事件发生时',
  '队列为空',
  'QueueMaintenance',
  '本轮不执行新任务'
)
Assert-Contains -Text $rules -Context 'Codex responsibility preflight' -Values @(
  'CodexDispatchReady',
  'ExpectedRoute',
  'route',
  'owner'
)
Assert-Contains -Text $rules -Context 'task-bearing recovery closeout' -Values @(
  'task-bearing `Recovery`',
  'CodexClosedOrNonReady',
  'QUEUE-MAINTENANCE'
)
Assert-Contains -Text $rules -Context 'queue outcome classification' -Values @(
  'readyCount',
  'refilled',
  'blocked/no_runnable_candidate',
  '不制造'
)
Assert-Contains -Text $rules -Context 'automation notification metadata' -Values @(
  'Result: 问题=<原问题>；完成=<具体交付>',
  'Impact: 影响=<实际行为变化>；边界=<明确未涉及范围>',
  'Verify: 验证=<关键检查与结果>；后续=<解锁项、剩余依赖或下一状态>',
  'Plain: 发生=<负责人短句>；影响=<负责人短句>；需要=<负责人短句>',
  '九个子字段',
  'tools/send-feishu-notification.ps1 -Kind TaskOutcome',
  '普通队列维护和无业务变化轮询不发送',
  '通知失败只记录脱敏投递状态'
)
Assert-Contains -Text $maintenanceRules -Context 'queue absence evidence' -Values @(
  'tools/check-task-cards.ps1 -OutputJson',
  'readyCount',
  '`rg` 无匹配',
  '无需'
)
Assert-Contract `
  -Condition (-not $collaborationRules.Contains('QueueMaintenance / Recovery 只运行全局任务卡检查', [StringComparison]::Ordinal)) `
  -Message 'retired recovery-global-only contract remains in collaboration rules'
Assert-Contains -Text $rules -Context 'fixed automation responsibility contract' -Values @(
  'RepositoryRoot',
  'current branch',
  'using-git-worktrees',
  'git worktree add',
  'linked worktree',
  'workspace guard',
  'automation-finalize-commit.ps1'
)
Assert-Contains -Text $recoveryRules -Context 'recovery read contract' -Values @(
  '只有两个读取条件',
  'Show.recovery != null',
  '普通责任方实际到达新的用户决定事件',
  '只读取 `创建决定恢复`',
  '未到达决定事件时不得读取本文件',
  '## 创建决定恢复'
)
Assert-Contains -Text $recoveryRules -Context 'decision plain-language contract' -Values @(
  'plainSummary.situation',
  'plainSummary.impact',
  'plainSummary.action',
  '先显示专业内容，再显示通俗版和回复控件',
  '不得临时猜测、补写或截断'
)
$creationSectionIndex = $recoveryRules.IndexOf('## 创建决定恢复', [StringComparison]::Ordinal)
$decisionSectionIndex = $recoveryRules.IndexOf('## 决定恢复', [StringComparison]::Ordinal)
$providerAcceptedIndex = $recoveryRules.IndexOf('PROVIDER_ACCEPTED', [StringComparison]::Ordinal)
$saveRecoveryIndex = $recoveryRules.IndexOf('SaveRecovery', [StringComparison]::Ordinal)
Assert-Contract `
  -Condition (
    $creationSectionIndex -ge 0 -and
    $decisionSectionIndex -gt $creationSectionIndex -and
    $providerAcceptedIndex -gt $creationSectionIndex -and
    $providerAcceptedIndex -lt $decisionSectionIndex -and
    $saveRecoveryIndex -gt $creationSectionIndex -and
    $saveRecoveryIndex -lt $decisionSectionIndex
  ) `
  -Message 'decision creation protocol must remain inside 创建决定恢复'
Assert-Contains -Text $recoveryRules -Context 'recovery rules' -Values @(
  'PROVIDER_ACCEPTED',
  'SaveRecovery',
  'decision recovery',
  'consume-reply.mjs',
  'Acquire -ResumeRecovery',
  'RECOVERY_ONLY',
  'UTF-8'
)
Assert-Contains -Text $recoveryRules -Context 'decision recovery Start' -Values @(
  'decision recovery',
  '-Action Start -Route Recovery',
  '不得 `Resume` 原 session'
)
Assert-Contains -Text $recoveryRules -Context 'interruption recovery Resume' -Values @(
  'interruption recovery',
  'Resume 原 session'
)
Assert-Contains -Text $invoker -Context 'fixed root/worktree contract' -Values @(
  'RepositoryRoot',
  'using-git-worktrees',
  'git worktree add'
)
Assert-Contains -Text $runner -Context 'UTF-8 stdin contract' -Values @(
  'IO.StreamReader',
  'Console]::OpenStandardInput',
  'Text.UTF8Encoding'
)
Assert-Contains -Text $invoker -Context 'UTF-8 stdin contract' -Values @(
  'StandardInputEncoding',
  'Text.UTF8Encoding'
)
Assert-Contains -Text $invoker -Context 'responsibility child deadline contract' -Values @(
  'ResponsibilityTimeoutSeconds',
  '[int]$ResponsibilityTimeoutSeconds = 3000',
  '$process.WaitForExit($timeoutMilliseconds)',
  '$process.Kill($true)',
  'exitCode = 124'
)
Assert-Contains -Text $invoker -Context 'responsibility outcome notification contract' -Values @(
  'Test-NotificationMetadata',
  'Pattern = ''^问题=',
  'Pattern = ''^影响=',
  'Pattern = ''^验证=',
  'Plain = [pscustomobject]',
  '发生=(?<happened>',
  'send-feishu-notification.ps1',
  '$runClosed',
  '$TaskId -cne ''QUEUE-MAINTENANCE'''
)
Assert-Contains -Text $runner -Context 'live session contract' -Values @(
  'codex_session_id='
)
Assert-Contains -Text $rules -Context 'responsibility closeout reserve' -Values @(
  '3000 秒',
  '300 秒',
  'SaveInterruption',
  'interruption recovery'
)
$normalContract = $prompt + "`n" + $rules + "`n" + $agentsRules + "`n" + $collaborationRules + "`n" + $maintenanceRules
foreach ($detailToken in @(
    'consume-reply.mjs',
    'PROVIDER_ACCEPTED',
    'SaveRecovery',
    'Resume 原 session'
  )) {
  Assert-Contract `
    -Condition (-not $normalContract.Contains($detailToken, [StringComparison]::OrdinalIgnoreCase)) `
    -Message "recovery detail leak in normal contract: $detailToken"
}
Assert-Contract `
  -Condition (-not $rules.Contains('三类候选每轮汇总后统一排序', [StringComparison]::OrdinalIgnoreCase)) `
  -Message 'workflow rules reintroduced per-round unified sorting'
Assert-Contains -Text $leaseTool -Context 'runtime schema 3 contract' -Values @(
  'schemaVersion = 3',
  "'SaveRecovery'",
  "'SaveInterruption'",
  "'ClearRecovery'"
)
foreach ($retiredAction in @('QueueResume', 'TakeResume')) {
  Assert-Contract -Condition (-not $leaseTool.Contains($retiredAction, [StringComparison]::Ordinal)) -Message "runtime schema 3 contract contains retired action: $retiredAction"
}
Assert-Contains -Text $dailyPrompt -Context 'daily briefing prompt' -Values @(
  'tools/get-automation-briefing-source.ps1',
  'Result',
  'Impact',
  'Verify',
  'Task',
  'memory',
  'handoff'
)
Assert-Contains -Text $dailyPrompt -Context 'daily briefing lifecycle categories' -Values @(
  'completed',
  'blocked',
  'frozen',
  'pending_decision',
  'waiting_reply',
  'pending_review',
  'queue_maintenance',
  'outcome_unverifiable'
)
Assert-Contains -Text $dailyPrompt -Context 'daily Feishu delivery contract' -Values @(
  'tools/get-feishu-notification-summary.ps1',
  'undelivered>0',
  'tools/send-feishu-notification.ps1 -Kind DailyReport',
  '完整正文',
  '6000 个 Unicode code point',
  '飞书投递状态'
)
Assert-Contains -Text $weeklyPrompt -Context 'weekly project summary prompt' -Values @(
  '不得修改项目文件',
  'lastSuccessfulUntil',
  '全部 Git 提交',
  '开发管理/当前任务队列.txt',
  '下周重点',
  '最多三项'
)
Assert-Contains -Text $weeklyPrompt -Context 'weekly risk classification and delivery contract' -Values @(
  '`ready`',
  '`blocked`',
  '`frozen`',
  '`pending_decision` / `waiting_reply`',
  '`pending_review`',
  '`completed`',
  '`no_task`',
  '`boundary_only`',
  '`evidence_conflict`',
  'completed` 不得列为当前风险',
  'Unity `src/` 当前行为',
  'tools/send-feishu-notification.ps1 -Kind WeeklyReport',
  '完整正文',
  '6000 个 Unicode code point'
)

$showIndex = $prompt.IndexOf('Show', [StringComparison]::Ordinal)
$queueIndex = $prompt.IndexOf('开发管理/当前任务队列.txt', [StringComparison]::Ordinal)
Assert-Contract -Condition ($showIndex -ge 0 -and $queueIndex -ge 0 -and $showIndex -lt $queueIndex) -Message 'runtime Show must occur before queue source'

foreach ($token in @('Buffer', 'TextEncoder', 'ProcessStartInfo', "@'", '@"')) {
  Assert-Contract -Condition (-not $prompt.Contains($token, [StringComparison]::OrdinalIgnoreCase)) -Message "controller contains forbidden implementation token: $token"
}

$activeText = $prompt + "`n" + $rules + "`n" + $recoveryRules
foreach ($token in @(
    'RecordQueueState',
    'ClearWorkerFailure',
    'SubmitManifest',
    'DiscoverRead',
    'planOnly',
    '自动工作流任务注册表',
    'hourly-controller-v2',
    'QueueResume',
    'TakeResume',
    'resume-trigger.mjs',
    'decision-trigger.mjs',
    'TZG_DECISION_TRIGGER',
    'TZG_DECISION_RESUME'
  )) {
  Assert-Contract -Condition (-not $activeText.Contains($token, [StringComparison]::OrdinalIgnoreCase)) -Message "active contract contains retired workflow token: $token"
}
Assert-Contract -Condition (-not [regex]::IsMatch($activeText, '(?i)\b(?:TQ|HANDOFF|DEC|REVIEW)-[A-Z0-9-]+')) -Message 'active contract contains a concrete task or decision id'
Assert-Contract -Condition (-not [regex]::IsMatch($status, '(?im)^.*生产入口.*\b(?:ACTIVE|PAUSED)\b.*$')) -Message 'workflow status contains a static live status claim'

foreach ($requiredPath in @(
    '开发管理\自动工作流恢复规则.txt',
    'tools\hourly-automation-lease.ps1',
    'tools\check-task-cards.ps1',
    'tools\codex-cli-session.ps1',
    'tools\invoke-codex-responsibility.ps1',
    'tools\automation-workspace-guard.ps1',
    'tools\automation-finalize-commit.ps1',
    'tools\get-automation-briefing-source.ps1',
    'tools\send-feishu-notification.ps1',
    'tools\get-feishu-notification-summary.ps1',
    'tools\feishu-decision-bridge\src\send-notification.mjs',
    'tools\feishu-decision-bridge\src\notification-summary.mjs',
    'tools\feishu-decision-bridge\src\consume-reply.mjs'
  )) {
  Assert-Contract -Condition (Test-Path -LiteralPath (Join-Path $root $requiredPath) -PathType Leaf) -Message "missing workflow component: $requiredPath"
}
Assert-Contract `
  -Condition (-not (Test-Path -LiteralPath (Join-Path $root 'tools\feishu-decision-bridge\src\decision-trigger.mjs'))) `
  -Message 'retired decision trigger component still exists'

$automationDirectories = @(Get-ChildItem -LiteralPath $automationDirectory -Directory -Filter 'tzg-*')
$automations = @($automationDirectories | ForEach-Object { Read-Automation -Directory $_.FullName })
$controllers = @($automations | Where-Object { $_.Id -eq 'tzg-hourly-controller' })
$dailyBriefings = @($automations | Where-Object { $_.Id -eq 'tzg-daily-automation-briefing' })
$weeklySummaries = @($automations | Where-Object { $_.Id -eq 'tzg-weekly-project-summary' })
Assert-Contract -Condition ($controllers.Count -eq 1) -Message 'tzg-hourly-controller configuration is missing or duplicated'
Assert-Contract -Condition ($dailyBriefings.Count -eq 1) -Message 'tzg-daily-automation-briefing configuration is missing or duplicated'
Assert-Contract -Condition ($weeklySummaries.Count -eq 1) -Message 'tzg-weekly-project-summary configuration is missing or duplicated'
Assert-Contract `
  -Condition ((Normalize-ContractText -Text $controllers[0].Prompt) -ceq (Normalize-ContractText -Text $prompt)) `
  -Message 'controller prompt does not match the canonical prompt'
Assert-Contract `
  -Condition ((Normalize-ContractText -Text $dailyBriefings[0].Prompt) -ceq (Normalize-ContractText -Text $dailyPrompt)) `
  -Message 'daily briefing prompt does not match the canonical prompt'
Assert-Contract `
  -Condition ((Normalize-ContractText -Text $weeklySummaries[0].Prompt) -ceq (Normalize-ContractText -Text $weeklyPrompt)) `
  -Message 'weekly project summary prompt does not match the canonical prompt'

$readOnlyAutomationIds = @($dailyBriefings[0].Id, $weeklySummaries[0].Id)
$writers = @($automations | Where-Object { $_.Id -cnotin $readOnlyAutomationIds })
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
