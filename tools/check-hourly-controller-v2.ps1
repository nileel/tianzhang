#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$promptPath = Join-Path $root '开发管理\自动工作流控制器v2提示词.txt'
$rulesPath = Join-Path $root '开发管理\自动工作流v2规则.txt'
$statusPath = Join-Path $root '开发管理\自动工作流状态.txt'
$registryPath = Join-Path $root '开发管理\自动工作流任务注册表.json'
$controllerPath = Join-Path $root 'tools\hourly-controller-v2\controller.ps1'

function Assert-Contract {
  param(
    [Parameter(Mandatory = $true)][bool]$Condition,
    [Parameter(Mandatory = $true)][string]$Message
  )

  if (-not $Condition) { throw $Message }
}

function Read-ContractText {
  param([Parameter(Mandatory = $true)][string]$Path)

  Assert-Contract (Test-Path -LiteralPath $Path -PathType Leaf) "missing contract file: $Path"
  [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
}

function Get-ContractSection {
  param(
    [Parameter(Mandatory = $true)][string]$Text,
    [Parameter(Mandatory = $true)][string]$Heading
  )

  $pattern = '(?ms)^##\s+' + [regex]::Escape($Heading) + '\s*\r?\n(?<body>.*?)(?=^##\s+|\z)'
  $match = [regex]::Match($Text, $pattern)
  Assert-Contract $match.Success "missing contract section: $Heading"
  $match.Groups['body'].Value
}

function Get-DecisionIds {
  param([Parameter(Mandatory = $true)][string]$Text)

  @([regex]::Matches($Text, 'DEC-[0-9]{8}-[A-Z0-9]+') | ForEach-Object { $_.Value } | Select-Object -Unique)
}

$prompt = Read-ContractText -Path $promptPath
$rules = Read-ContractText -Path $rulesPath
$status = Read-ContractText -Path $statusPath
$controller = Read-ContractText -Path $controllerPath

$metadataLiterals = @(
  'const meta = nodeRepl.requestMeta;',
  "const turnMeta = meta && meta['x-codex-turn-metadata'];",
  'threadId: meta && meta.threadId,',
  'metadataThreadId: turnMeta && turnMeta.thread_id'
)
foreach ($literal in $metadataLiterals) {
  Assert-Contract $prompt.Contains($literal, [StringComparison]::Ordinal) "prompt metadata contract is missing: $literal"
}
foreach ($forbiddenMetadata in @('meta.turn.thread_id', 'tzgTurn.turn.thread_id')) {
  Assert-Contract (-not $prompt.Contains($forbiddenMetadata, [StringComparison]::Ordinal)) "prompt contains forbidden metadata path: $forbiddenMetadata"
}

$actions = @(
  'Start', 'RecordTitleResult', 'DiscoverRead', 'DiscoverSearch', 'DiscoverList',
  'DiscoverCheck', 'SubmitManifest', 'BeginMutation', 'Finish', 'Abort',
  'CreateDecision', 'SendDecision', 'ConsumeDecision', 'MigrateLegacy', 'Show'
)
$actionLine = '固定 Action 白名单：' + ($actions -join '|')
Assert-Contract $prompt.Contains($actionLine, [StringComparison]::Ordinal) 'prompt fixed action whitelist differs'
foreach ($promptLiteral in @(
    'tools.codex_app__set_thread_title',
    '禁止在发现阶段调用 Shell',
    '禁止解析 Markdown 自行选任务',
    '不自行宣称检查通过',
    '不得修改状态枚举',
    '不得编辑任何 automation TOML',
    'planOnly=true',
    '脱敏最终摘要'
  )) {
  Assert-Contract $prompt.Contains($promptLiteral, [StringComparison]::OrdinalIgnoreCase) "prompt constraint is missing: $promptLiteral"
}

foreach ($rulesLiteral in @(
    '生产环境任何时刻最多一个写入控制器',
    'planOnly=true',
    '%USERPROFILE%\.codex\automation-state\',
    '最小验证预算',
    '输入未变化时不重复同一检查',
    'feishu_unavailable',
    '不丢弃决定',
    '不回退 Gmail',
    'pending-whitespace',
    'cached-diff-check',
    'git commit --only'
  )) {
  Assert-Contract $rules.Contains($rulesLiteral, [StringComparison]::OrdinalIgnoreCase) "v2 rule is missing: $rulesLiteral"
}

foreach ($oldFile in @(
    'tools\automation-controller.ps1',
    'tools\automation-controller-state.ps1',
    'tools\automation-decision-status.ps1'
  )) {
  Assert-Contract (Test-Path -LiteralPath (Join-Path $root $oldFile) -PathType Leaf) "preserved controller file is missing: $oldFile"
}
$v2ImplementationText = $prompt + "`n" + $rules + "`n" + $controller
foreach ($oldModuleReference in @(
    'tools/automation-controller.ps1',
    'automation-controller-state.ps1',
    'automation-decision-status.ps1'
  )) {
  Assert-Contract (-not $v2ImplementationText.Contains($oldModuleReference, [StringComparison]::OrdinalIgnoreCase)) "v2 references an old implementation module: $oldModuleReference"
}

$registryText = Read-ContractText -Path $registryPath
$registry = $registryText | ConvertFrom-Json
$tq057 = @($registry.tasks | Where-Object { $_.taskId -ceq 'TQ-057' })
Assert-Contract ($tq057.Count -eq 1) 'registry must contain exactly one TQ-057 task'
$registryIds = @($tq057[0].decisionIds | ForEach-Object { [string]$_ })
$promptIds = @(Get-DecisionIds -Text (Get-ContractSection -Text $prompt -Heading 'TQ-057 v2 冻结决策'))
$statusIds = @(Get-DecisionIds -Text (Get-ContractSection -Text $status -Heading 'TQ-057 v2 冻结决策'))
Assert-Contract ($registryIds.Count -eq 5) 'TQ-057 registry decision count must be five'
Assert-Contract (($promptIds -join '|') -ceq ($registryIds -join '|')) 'prompt TQ-057 decisions differ from registry'
Assert-Contract (($statusIds -join '|') -ceq ($registryIds -join '|')) 'status TQ-057 decisions differ from registry'

$statusSummary = Get-ContractSection -Text $status -Heading 'v2 建设摘要'
foreach ($statusLiteral in @('旧生产控制器仍暂停（PAUSED）', 'v2 尚未切换', '实施待定', '离线测试不代表生产成功')) {
  Assert-Contract $statusSummary.Contains($statusLiteral, [StringComparison]::Ordinal) "v2 status summary is missing: $statusLiteral"
}

Write-Output 'check-hourly-controller-v2: OK'
