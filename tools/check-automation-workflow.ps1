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
$titleToolPattern = 'set-thread-name\.mjs'
$taskSummaryPattern = ConvertFrom-Utf8Base64 '5Lit5paH566A6L+w'
$titleFormatPattern = ConvertFrom-Utf8Base64 'VFpH772cPOS4reaWh+eugOi/sD4='
$titleFailurePattern = '改名失败|标题助手失败|助手失败'
$decisionBoundaryPattern = '待决策与邮件回执|CreateDecision|禁止自行决定'
$decisionVisibilityPattern = '自动工作流状态\.txt|TZG｜待决策|需要决策'
$decisionFallbackPattern = 'MarkDecisionDeliveryFailed|不得让控制器作出默认选择|继续正常动态路由'
$emailLiteralPattern = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'

$rules = Join-Path $root (Join-Path $devMgmt $rulesName)
$status = Join-Path $root (Join-Path $devMgmt $statusName)
$collaboration = Join-Path $root (Join-Path $devMgmt $collaborationName)
if (-not (Test-Path -LiteralPath $rules)) { $findings.Add('missing workflow rules') }
if (Test-Path -LiteralPath $status) {
  Reject-Match $status 'WF1-QUEUE-MAINTENANCE|WF2-CODEX-ONE|WF3-CLAUDE-ONE|WF4-CODEX-TWO' 'project status still contains the legacy workflow table'
}

foreach ($entry in @('AGENTS.md','CLAUDE.md',(Join-Path $devMgmt $maintenanceName),(Join-Path $devMgmt $collaborationName))) {
  Require-Match (Join-Path $root $entry) $rulePattern "$entry does not route to workflow rules"
}

$automationRoot = Join-Path $env:USERPROFILE '.codex\automations'
$controller = Join-Path $automationRoot 'tzg-hourly-controller\automation.toml'
$paused = @(
  'tzg-wf1-queue-and-review-maintenance',
  'tzg-wf3-claude-execute-1',
  'tzg-wf4-codex-execute-2'
)
Require-Match $controller '^name = "TZG Hourly Controller"$' 'controller has not been renamed'
Reject-Match $controller 'TQ-[0-9]+|HANDOFF-[0-9]+|DEC-' 'controller prompt contains a hardcoded task, handoff, or decision id'
Reject-Match $controller '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}' 'controller prompt contains a historical thread id'
Require-Match $controller $titleToolPattern 'controller does not rename its conversation'
Require-Match $controller $taskSummaryPattern 'controller does not record the human-readable task summary in memory'
Require-Match $controller $titleFormatPattern 'controller does not use a human-readable title format'
Require-Match $controller $titleFailurePattern 'controller does not preserve execution when renaming fails'
Require-Match $controller $decisionBoundaryPattern 'controller lacks a decision-request boundary'
Require-Match $controller $decisionVisibilityPattern 'controller lacks decision visibility instructions'
Require-Match $controller $decisionFallbackPattern 'controller lacks decision delivery fallback instructions'
Reject-Match $controller $emailLiteralPattern 'controller prompt contains an email address'

$v2Sources = @(
  @{ Path = $rules; Label = 'workflow rules' },
  @{ Path = $controller; Label = 'controller prompt' }
)
foreach ($source in $v2Sources) {
  Require-Match $source.Path '低水位[^\r\n]*2|low[^\r\n]*2' "$($source.Label) lacks the runnable low-water mark of 2"
  Require-Match $source.Path '高水位[^\r\n]*5|high[^\r\n]*5' "$($source.Label) lacks the runnable high-water mark of 5"
  Require-Match $source.Path 'automation-workspace-guard\.ps1' "$($source.Label) does not require the workspace guard"
  Require-Match $source.Path 'RecordQueueState' "$($source.Label) does not persist queue/backlog fingerprints"
  Require-Match $source.Path 'RecordWorkerFailure' "$($source.Label) does not record worker preflight failures"
  Require-Match $source.Path 'DeepSeek工作提示词\.txt' "$($source.Label) does not route DeepSeek workers through their prompt"
  Require-Match $source.Path 'git commit --only' "$($source.Label) does not require path-limited commits"
  Require-Match $source.Path '控制器自有[^\r\n]*(提交|恢复指针)|(提交|恢复指针)[^\r\n]*控制器自有' "$($source.Label) does not submit controller-owned state or preserve a recovery pointer"
}

Reject-Match $controller '无恢复指针[^\r\n]*工作区不干净[^\r\n]*Complete[^\r\n]*只读退出' 'controller still globally exits on a dirty workspace without a recovery pointer'
Reject-Match $rules '当前?队列少于\s*5\s*条|队列少于\s*5\s*条' 'workflow rules still use total queue count below 5 as a maintenance trigger'
Reject-Match $controller '当前?队列少于\s*5\s*条|队列少于\s*5\s*条' 'controller still uses total queue count below 5 as a maintenance trigger'
Require-Match $controller '不得并行|禁止并行' 'controller prompt does not prohibit parallel workers'
Require-Match $controller 'DeepSeek[^\r\n]*(不得|禁止)[^\r\n]*(stage|暂存)[^\r\n]*(commit|提交)|DeepSeek[^\r\n]*(不得|禁止)[^\r\n]*(commit|提交)[^\r\n]*(stage|暂存)' 'controller prompt does not forbid DeepSeek workers from staging and committing'
Require-Match $controller '只有控制器|仅控制器[^\r\n]*(stage|暂存)[^\r\n]*(commit|提交)|控制器[^\r\n]*唯一[^\r\n]*(stage|暂存|提交)' 'controller prompt does not reserve staging and committing to the controller'
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
