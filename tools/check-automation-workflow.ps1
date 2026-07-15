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

function Get-TomlTopLevelRawValue {
  param([string]$Path, [string]$Key)
  $escapedKey = [regex]::Escape($Key)
  $matchingLines = @(Get-Content -LiteralPath $Path | Where-Object { $_ -match "^\s*$escapedKey\s*=" })
  if ($matchingLines.Count -ne 1) {
    $findings.Add("controller TOML must contain exactly one top-level $Key field")
    return $null
  }
  if ($matchingLines[0] -notmatch "^\s*$escapedKey\s*=\s*(?<value>.+?)\s*$") {
    $findings.Add("controller TOML has an invalid top-level $Key field")
    return $null
  }
  $Matches['value']
}

function ConvertFrom-TomlBasicString {
  param([AllowNull()][string]$RawValue, [string]$Label)
  if ($null -eq $RawValue -or $RawValue -notmatch '^"(?:\\.|[^"\\])*"$') {
    $findings.Add("controller TOML $Label must be a basic string")
    return $null
  }
  try {
    $RawValue | ConvertFrom-Json
  } catch {
    $findings.Add("controller TOML $Label contains an invalid basic string")
    $null
  }
}

function Require-TomlStringValue {
  param([string]$Path, [string]$Key, [string]$Expected)
  $actual = ConvertFrom-TomlBasicString (Get-TomlTopLevelRawValue $Path $Key) $Key
  if ($null -ne $actual -and $actual -cne $Expected) {
    $findings.Add("controller TOML $Key must equal $Expected")
  }
}

$devMgmt = ConvertFrom-Utf8Base64 '5byA5Y+R566h55CG'
$rulesName = ConvertFrom-Utf8Base64 '6Ieq5Yqo5bel5L2c5rWB6KeE5YiZLnR4dA=='
$statusName = ConvertFrom-Utf8Base64 '6Ieq5Yqo5bel5L2c5rWB54q25oCBLnR4dA=='
$maintenanceName = ConvertFrom-Utf8Base64 '54q25oCB5LiO5bu66K6u57u05oqk6KeE5YiZLnR4dA=='
$collaborationName = ConvertFrom-Utf8Base64 'QUnljY/kvZzop4TliJkudHh0'
$controllerPromptName = ConvertFrom-Utf8Base64 '6Ieq5Yqo5bel5L2c5rWB5o6n5Yi25Zmo5o+Q56S66K+NLnR4dA=='
$rulePattern = ConvertFrom-Utf8Base64 '6Ieq5Yqo5bel5L2c5rWB6KeE5YiZXC50eHQ='
$readOnlyPattern = (ConvertFrom-Utf8Base64 '5Y+q6K+7') + '|read-only'
$titleToolPattern = 'set-thread-name\.mjs'
$taskSummaryPattern = ConvertFrom-Utf8Base64 '5Lit5paH566A6L+w'
$titleFormatPattern = ConvertFrom-Utf8Base64 'VFpH772cPOS4reaWh+eugOi/sD4='
$titleFailurePattern = '改名失败|标题助手失败|助手失败'
$decisionBoundaryPattern = '待决策与邮件回执|CreateDecision|禁止自行决定'
$decisionVisibilityPattern = '自动工作流状态\.txt|TZG｜待决策|需要决策'
$decisionFallbackPattern = 'MarkDecisionDeliveryFailed|不得让控制器作出默认选择|继续正常动态路由'
$taskKindMappingPattern = 'TaskKind 固定映射：普通执行=`execute`、复审=`review`、维护=`maintenance`、恢复=`recovery`'
$finalizerScopePattern = 'Finalizer 固定边界：expectedPaths 是允许上界，只检查并提交其中的实际变化路径，不自动修复内容。'
$emailLiteralPattern = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'

$rules = Join-Path $root (Join-Path $devMgmt $rulesName)
$status = Join-Path $root (Join-Path $devMgmt $statusName)
$maintenance = Join-Path $root (Join-Path $devMgmt $maintenanceName)
$collaboration = Join-Path $root (Join-Path $devMgmt $collaborationName)
$controllerPromptSource = Join-Path $root (Join-Path $devMgmt $controllerPromptName)
if (-not (Test-Path -LiteralPath $rules)) { $findings.Add('missing workflow rules') }
if (Test-Path -LiteralPath $status) {
  Reject-Match $status 'WF1-QUEUE-MAINTENANCE|WF2-CODEX-ONE|WF3-CLAUDE-ONE|WF4-CODEX-TWO' 'project status still contains the legacy workflow table'
}

foreach ($entry in @('AGENTS.md','CLAUDE.md',(Join-Path $devMgmt $maintenanceName),(Join-Path $devMgmt $collaborationName))) {
  Require-Match (Join-Path $root $entry) $rulePattern "$entry does not route to workflow rules"
}

Require-Match $maintenance '状态[^\r\n]*待处理' 'maintenance rules do not require runnable tasks to be pending'
Require-Match $maintenance '前置[^\r\n]*完成[^\r\n]*DeepSeek[^\r\n]*Codex[^\r\n]*复审' 'maintenance rules do not require completed dependencies and reviewed DeepSeek prerequisites'
Require-Match $maintenance '主责[^\r\n]*配置执行器' 'maintenance rules do not require an owner-to-configured-executor mapping'
Require-Match $maintenance '待决策[^\r\n]*(全部传递后继|传递依赖)' 'maintenance rules do not exclude pending-decision tasks and their transitive successors'
Require-Match $maintenance '(内容冻结|冻结)[^\r\n]*闸门' 'maintenance rules do not enforce content freezes and gates'
Require-Match $maintenance '完整[^\r\n]*expectedPaths' 'maintenance rules do not require complete expectedPaths before mutation'
Require-Match $maintenance 'workspace[^\r\n]*(baseline|基线)[^\r\n]*不冲突' 'maintenance rules do not require a conflict-free workspace baseline'

$v1Spec = Join-Path $root 'docs/superpowers/specs/2026-07-11-hourly-automation-controller-design.md'
Require-Match $v1Spec 'docs/superpowers/specs/2026-07-13-hourly-automation-controller-v2-design\.md' 'v1 spec does not link to the exact v2 specification path'

$automationRoot = Join-Path $env:USERPROFILE '.codex\automations'
$controller = Join-Path $automationRoot 'tzg-hourly-controller\automation.toml'
$paused = @(
  'tzg-wf1-queue-and-review-maintenance',
  'tzg-wf3-claude-execute-1',
  'tzg-wf4-codex-execute-2'
)
Require-TomlStringValue $controller 'id' 'tzg-hourly-controller'
Require-TomlStringValue $controller 'kind' 'cron'
Require-TomlStringValue $controller 'rrule' 'FREQ=HOURLY;INTERVAL=1;BYMINUTE=15'
Require-TomlStringValue $controller 'model' 'gpt-5.6-terra'
Require-TomlStringValue $controller 'reasoning_effort' 'high'
Require-TomlStringValue $controller 'execution_environment' 'local'

$projectRoot = 'D:\天章游戏开发'
$targetRaw = Get-TomlTopLevelRawValue $controller 'target'
if ($null -ne $targetRaw) {
  if ($targetRaw -notmatch '^\{\s*type\s*=\s*(?<type>"(?:\\.|[^"\\])*")\s*,\s*project_id\s*=\s*(?<project>"(?:\\.|[^"\\])*")\s*\}$') {
    $findings.Add('controller TOML target must contain only type and project_id')
  } else {
    $targetType = ConvertFrom-TomlBasicString $Matches['type'] 'target.type'
    $targetProject = ConvertFrom-TomlBasicString $Matches['project'] 'target.project_id'
    if ($null -ne $targetType -and $targetType -cne 'project') { $findings.Add('controller TOML target.type must equal project') }
    if ($null -ne $targetProject -and $targetProject -cne $projectRoot) { $findings.Add("controller TOML target.project_id must equal $projectRoot") }
  }
}

$cwdsRaw = Get-TomlTopLevelRawValue $controller 'cwds'
if ($null -ne $cwdsRaw) {
  if ($cwdsRaw -notmatch '^\[(?<items>.*)\]$') {
    $findings.Add('controller TOML cwds must be an array')
  } else {
    $itemText = $Matches['items']
    $cwdMatches = @([regex]::Matches($itemText, '"(?:\\.|[^"\\])*"'))
    $remainder = [regex]::Replace($itemText, '"(?:\\.|[^"\\])*"', '') -replace '[,\s]', ''
    if ($remainder.Length -ne 0 -or $cwdMatches.Count -ne 1) {
      $findings.Add('controller TOML cwds must contain exactly one project path')
    } else {
      $cwdValue = ConvertFrom-TomlBasicString $cwdMatches[0].Value 'cwds[0]'
      if ($null -ne $cwdValue -and $cwdValue -cne $projectRoot) { $findings.Add("controller TOML cwds must contain only $projectRoot") }
    }
  }
}

$controllerIdFiles = @(
  Get-ChildItem -Path $automationRoot -Recurse -Filter 'automation.toml' -File |
    Where-Object { Select-String -Quiet -LiteralPath $_.FullName -Pattern '^id\s*=\s*"tzg-hourly-controller"\s*$' }
)
if ($controllerIdFiles.Count -ne 1) {
  $findings.Add("expected exactly one tzg-hourly-controller id, found $($controllerIdFiles.Count)")
}

Require-Match $controller '^name = "TZG Hourly Controller"$' 'controller has not been renamed'
Reject-Match $controller 'TQ-[0-9]+|HANDOFF-[0-9]+|DEC-' 'controller prompt contains a hardcoded task, handoff, or decision id'
Reject-Match $controller '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}' 'controller prompt contains a historical thread id'
Require-Match $rules $titleToolPattern 'workflow rules do not preserve conversation renaming'
Require-Match $rules $taskSummaryPattern 'workflow rules do not record the human-readable task summary in memory'
Require-Match $rules $titleFormatPattern 'workflow rules do not preserve the human-readable title format'
Require-Match $rules $titleFailurePattern 'workflow rules do not preserve execution when renaming fails'
Require-Match $rules $decisionBoundaryPattern 'workflow rules lack a decision-request boundary'
Require-Match $rules $decisionVisibilityPattern 'workflow rules lack decision visibility instructions'
Require-Match $rules $decisionFallbackPattern 'workflow rules lack decision delivery fallback instructions'
Reject-Match $controller $emailLiteralPattern 'controller prompt contains an email address'

$v2Sources = @(
  @{ Path = $rules; Label = 'workflow rules' }
)
foreach ($source in $v2Sources) {
  Require-Match $source.Path $taskKindMappingPattern "$($source.Label) lacks the fixed TaskKind mapping"
  Require-Match $source.Path $finalizerScopePattern "$($source.Label) lacks the side-effect-free finalizer boundary"
  Reject-Match $source.Path '`execution`|-TaskKind\s+execution|TaskKind\s*[=:：]\s*execution' "$($source.Label) uses invalid execution TaskKind"
  Require-Match $source.Path '低水位[^\r\n]*2|low[^\r\n]*2' "$($source.Label) lacks the runnable low-water mark of 2"
  Require-Match $source.Path '高水位[^\r\n]*5|high[^\r\n]*5' "$($source.Label) lacks the runnable high-water mark of 5"
  Require-Match $source.Path 'automation-workspace-guard\.ps1' "$($source.Label) does not require the workspace guard"
  Require-Match $source.Path 'automation-finalize-commit\.ps1' "$($source.Label) does not use the fixed commit helper"
  Require-Match $source.Path 'CaptureRecoveryEvidence' "$($source.Label) does not capture recovery evidence"
  Require-Match $source.Path 'CheckRecovery' "$($source.Label) does not use the recovery-specific guard"
  Require-Match $source.Path 'RecordQueueState' "$($source.Label) does not persist queue/backlog fingerprints"
  Require-Match $source.Path 'RecordWorkerFailure' "$($source.Label) does not record worker preflight failures"
  Require-Match $source.Path 'DeepSeek工作提示词\.txt' "$($source.Label) does not route DeepSeek workers through their prompt"
  Require-Match $source.Path 'git commit --only' "$($source.Label) does not require path-limited commits"
  Require-Match $source.Path '控制器自有[^\r\n]*(提交|恢复指针)|(提交|恢复指针)[^\r\n]*控制器自有' "$($source.Label) does not submit controller-owned state or preserve a recovery pointer"
  Require-Match $source.Path '扩展后的完整[^\r\n]*expectedPaths[^\r\n]*(再次|重新)[^\r\n]*(workspace guard[^\r\n]*)?Check' "$($source.Label) does not re-check expanded controller-owned expectedPaths"
  Require-Match $source.Path '冲突[^\r\n]*不得[^\r\n]*(写|暂存|提交)[^\r\n]*状态路径' "$($source.Label) does not protect a conflicting controller-owned state path"
  Require-Match $source.Path '冲突[^\r\n]*状态路径[^\r\n]*不得[^\r\n]*(Verify|排除)' "$($source.Label) may hide a conflicting state path from workspace verification"
  Require-Match $source.Path 'backoff[^\r\n]*(排除|不计入)[^\r\n]*DeepSeek[^\r\n]*(候选|库存)|DeepSeek[^\r\n]*backoff[^\r\n]*(排除|不计入)' "$($source.Label) does not exclude DeepSeek candidates during worker backoff"
  Require-Match $source.Path '排除后[^\r\n]*重新计算[^\r\n]*(低水位[^\r\n]*2[^\r\n]*高水位[^\r\n]*5|2[^\r\n]*5)' "$($source.Label) does not recompute 2-to-5 runnable inventory after backoff exclusion"
  Require-Match $source.Path '(backoff|退避)[^\r\n]*(过期|ClearWorkerFailure)[^\r\n]*(恢复|重新纳入)' "$($source.Label) restores DeepSeek candidates before backoff expiry or clear"
}

Reject-Match $rules '先用 workspace guard Check 证明当前基线和路径安全' 'workflow rules use ordinary candidate Check for recovery ownership'

Require-Match $maintenance 'backoff[^\r\n]*(排除|不计入)[^\r\n]*DeepSeek[^\r\n]*(候选|库存)|DeepSeek[^\r\n]*backoff[^\r\n]*(排除|不计入)' 'maintenance rules do not exclude DeepSeek candidates during worker backoff'
Require-Match $maintenance '排除后[^\r\n]*重新计算[^\r\n]*(低水位[^\r\n]*2[^\r\n]*高水位[^\r\n]*5|2[^\r\n]*5)' 'maintenance rules do not recompute 2-to-5 runnable inventory after backoff exclusion'
Require-Match $maintenance '(backoff|退避)[^\r\n]*(过期|ClearWorkerFailure)[^\r\n]*(恢复|重新纳入)' 'maintenance rules restore DeepSeek candidates before backoff expiry or clear'

Reject-Match $rules '当前?队列少于\s*5\s*条|队列少于\s*5\s*条' 'workflow rules still use total queue count below 5 as a maintenance trigger'

$deployedPrompt = ConvertFrom-TomlBasicString (Get-TomlTopLevelRawValue $controller 'prompt') 'prompt'
if (-not (Test-Path -LiteralPath $controllerPromptSource -PathType Leaf)) {
  $findings.Add('missing versioned controller prompt source')
} elseif ($null -ne $deployedPrompt) {
  $sourcePrompt = [IO.File]::ReadAllText($controllerPromptSource).TrimEnd("`r", "`n")
  if ($sourcePrompt -cne $deployedPrompt) { $findings.Add('deployed controller prompt does not match the versioned source') }
  if ($sourcePrompt.Length -gt 3000) { $findings.Add("controller prompt exceeds 3000 characters: $($sourcePrompt.Length)") }
  $numberedSteps = [regex]::Matches($sourcePrompt, '(?m)^\s*\d+\.\s').Count
  if ($numberedSteps -gt 10) { $findings.Add("controller prompt exceeds 10 numbered steps: $numberedSteps") }
}

foreach ($entryPoint in @('automation-controller\.ps1','Start','InspectCandidate','RegisterCandidate','BeginMutation','Finish','CompleteNoChange','Fail','requiredSources')) {
  Require-Match $controller $entryPoint "controller prompt lacks v3 entry contract: $entryPoint"
}
foreach ($promptSource in @(
  @{ Path = $controllerPromptSource; Label = 'versioned controller prompt' },
  @{ Path = $controller; Label = 'deployed controller prompt' }
)) {
  foreach ($entry in @('PrepareDecision', 'NotificationReceipt', 'CreateDecision', 'ResolveDecisionReply')) {
    Require-Match $promptSource.Path ([regex]::Escape($entry)) "$($promptSource.Label) lacks repaired decision contract: $entry"
  }
}
Require-Match $rules '有效回复[^\r\n]*InspectCandidate[^\r\n]*不得直接[^\r\n]*Finish' 'workflow rules lack decision reply re-registration'
if ($null -eq $deployedPrompt -or $deployedPrompt -notmatch 'Start\s+-RepositoryRoot\s+''D:\\天章游戏开发''\s+-RunId\s+"\$runId"\s+-ActualModel\s+"\$actualModel"') {
  $findings.Add('controller prompt lacks the exact Start parameter contract')
}
if ($null -eq $deployedPrompt -or $deployedPrompt -notmatch 'InspectCandidate') {
  $findings.Add('controller prompt lacks candidate inspection')
}
if ($null -eq $deployedPrompt -or
    $deployedPrompt -notmatch 'InspectCandidate\s+-RepositoryRoot\s+''D:\\天章游戏开发''\s+-RunId\s+"\$runId"\s+-TaskId\s+"\$taskId"') {
  $findings.Add('controller prompt does not expose TaskId-only candidate inspection')
}
if ($null -ne $deployedPrompt -and $deployedPrompt -match 'InspectCandidate[^\r\n]*-(?:WorkType|Executor)\b') {
  $findings.Add('controller prompt still asks the model to translate candidate protocol selectors')
}
if ($null -eq $deployedPrompt -or $deployedPrompt -notmatch 'RegisterCandidate[^\r\n]*-ExpectedPaths') {
  $findings.Add('controller prompt does not register discovered paths explicitly')
}
Require-Match $rules 'fresh[^\r\n]*select_candidate[^\r\n]*固定[^\r\n]*workType=execution' 'workflow rules do not fix fresh queue candidates to execution'
Require-Match $rules 'TaskId[^\r\n]*当前任务队列[^\r\n]*主责[^\r\n]*映射' 'workflow rules do not derive the executor from the current queue owner'
Require-Match $rules '复审[^\r\n]*维护[^\r\n]*(独立|专用)[^\r\n]*(解析|分支)' 'workflow rules do not preserve separate review and maintenance candidate resolution'
foreach ($forbidden in @(
  'automation-controller-state\.ps1',
  'automation-workspace-guard\.ps1',
  'automation-finalize-commit\.ps1',
  '\bTaskKind\b',
  'CaptureRecoveryEvidence',
  'CheckRecovery',
  'git commit'
)) {
  Reject-Match $controller $forbidden "controller prompt still implements deterministic internals: $forbidden"
}
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
