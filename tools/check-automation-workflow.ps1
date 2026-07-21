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
  param(
    [Parameter(Mandatory = $true)]
    [bool]$Condition,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Read-Utf8Contract {
  param([Parameter(Mandatory = $true)][string]$Path)

  Assert-Contract -Condition (Test-Path -LiteralPath $Path -PathType Leaf) -Message "missing contract file: $Path"
  try {
    [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  } catch {
    throw "contract is not valid UTF-8: $Path"
  }
}

function Assert-ContainsAll {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Text,
    [Parameter(Mandatory = $true)]
    [string[]]$Required,
    [Parameter(Mandatory = $true)]
    [string]$Context
  )

  foreach ($literal in $Required) {
    Assert-Contract `
      -Condition $Text.Contains($literal, [StringComparison]::OrdinalIgnoreCase) `
      -Message "$Context is missing: $literal"
  }
}

function Normalize-ContractText {
  param([Parameter(Mandatory = $true)][string]$Text)

  ($Text -replace "`r`n", "`n" -replace "`r", "`n").TrimEnd()
}

function Read-Automation {
  param([Parameter(Mandatory = $true)][string]$Directory)

  $path = Join-Path $Directory 'automation.toml'
  $text = Read-Utf8Contract -Path $path
  $statusMatches = @([regex]::Matches($text, '(?m)^status\s*=\s*"(?<value>ACTIVE|PAUSED)"\s*$'))
  Assert-Contract -Condition ($statusMatches.Count -eq 1) -Message "automation status is invalid: $path"
  $promptMatches = @([regex]::Matches($text, '(?m)^prompt\s*=\s*(?<value>"(?:[^"\\]|\\.)*")\s*$'))
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

$promptPath = Join-Path $root '开发管理\自动工作流控制器提示词.txt'
$rulesPath = Join-Path $root '开发管理\自动工作流规则.txt'
$statusPath = Join-Path $root '开发管理\自动工作流状态.txt'
$prompt = Read-Utf8Contract -Path $promptPath
$rules = Read-Utf8Contract -Path $rulesPath
$status = Read-Utf8Contract -Path $statusPath
$relayPath = Join-Path $root 'tools\feishu-decision-bridge\src\resume-trigger.mjs'
$relay = Read-Utf8Contract -Path $relayPath

Assert-ContainsAll -Text $prompt -Context 'thin prompt' -Required @(
  '开发管理/自动工作流规则.txt',
  '开发管理/当前任务队列.txt',
  '开发管理/审核入口.txt',
  '开发管理/AI合作沟通.txt',
  'tools/hourly-automation-lease.ps1',
  '统一排序',
  '三类均无合法候选时才',
  '队列维护正式入口必须保留两个顺序分支',
  '没有可提升的完整 backlog 任务卡不等于阻塞',
  '每轮只启动一个责任方',
  '纯 `1`',
  '纯 `2`',
  '开发管理/DeepSeek工作提示词.txt',
  '开发管理/状态与建议维护规则.txt',
  'tools/codex-cli-session.ps1',
  '`Start`',
  'stdin',
  '`Resume`',
  '`selected`',
  '`session_started`',
  '`running`',
  '`waiting_decision`',
  '`completed`',
  '`failed`',
  '工具等待超时、yield 或尚未返回不等于 runner 失败',
  '不得释放租约或启动第二写入者',
  'RECOVERY_ONLY',
  'businessCommit',
  'handoffCommit',
  '不消耗等待 token',
  '连续两次',
  '每轮第一项操作必须',
  '只有未逻辑暂停时，才读取',
  'pauseRequested=true 表示工具级逻辑暂停',
  'SUSPENDED',
  'ClearBlocking',
  '自动化任务不得调用自动化管理能力管理自身',
  '外部普通管理上下文',
  'runtime 已逻辑暂停，界面尚未同步',
  'PAUSED'
)
Assert-ContainsAll -Text $rules -Context 'short rules' -Required @(
  '单写入租约',
  '候选资格',
  '统一排序',
  '四种路由责任',
  'tools/codex-cli-session.ps1',
  '`Start`',
  'stdin',
  '`Resume`',
  'CLI-native',
  'RECOVERY_ONLY',
  '人工',
  '不 stash',
  'businessCommit',
  'handoffCommit',
  '决策恢复',
  '两轮',
  '在读取当前任务队列或任何候选事实源前',
  '队列补充顺序',
  '私有状态',
  '回滚'
)
Assert-ContainsAll -Text $status -Context 'workflow status' -Required @(
  'PAUSED',
  'recovery',
  'pending resume',
  'lease',
  'pauseRequested=true',
  'SUSPENDED',
  'ClearBlocking'
)

$showIndex = $prompt.IndexOf('每轮第一项操作必须', [StringComparison]::Ordinal)
$routingSourcesIndex = $prompt.IndexOf('开发管理/自动工作流规则.txt', [StringComparison]::Ordinal)
Assert-Contract `
  -Condition ($showIndex -ge 0 -and $routingSourcesIndex -ge 0 -and $showIndex -lt $routingSourcesIndex) `
  -Message 'runtime Show must occur before routing sources'

$activeText = $prompt + "`n" + $rules
$desktopBoundary = '普通 Codex 执行、复审和队列维护不得使用 Desktop/VS Code rollout'
$firstPhaseBoundary = '第一期不新增飞书 Tasks、task GUID 映射、进度数据库或阶段状态机'
Assert-ContainsAll -Text $activeText -Context 'CLI-native boundaries' -Required @(
  $desktopBoundary,
  $firstPhaseBoundary
)
$boundaryScan = $activeText.Replace($desktopBoundary, '').Replace($firstPhaseBoundary, '')
foreach ($forbiddenBoundary in @(
  '直接使用 Desktop',
  '直接使用 VS Code',
  '第一期接入飞书 Tasks',
  '第一期创建 task GUID 映射',
  '第一期创建进度数据库',
  '第一期创建阶段状态机'
)) {
  Assert-Contract `
    -Condition (-not $boundaryScan.Contains($forbiddenBoundary, [StringComparison]::OrdinalIgnoreCase)) `
    -Message "active prompt or rules violates CLI-native boundary: $forbiddenBoundary"
}
Assert-Contract `
  -Condition (-not [regex]::IsMatch($activeText, '(?i)\b(?:TQ|HANDOFF|DEC|REVIEW)-[A-Z0-9-]+')) `
  -Message 'active prompt or rules contains a concrete task, decision, handoff, or review id'
foreach ($forbidden in @(
  'manifest',
  'planOnly',
  'SubmitManifest',
  'DiscoverRead',
  '任务注册表',
  'hourly-controller-v2'
)) {
  Assert-Contract `
    -Condition (-not $activeText.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) `
    -Message "active prompt or rules contains old protocol token: $forbidden"
}

foreach ($forbiddenSelfManagement in @(
  '控制器直接更新自身为 PAUSED',
  '只做一次完整配置更新并等待同一调用返回',
  '调用自身 view'
)) {
  Assert-Contract `
    -Condition (-not $activeText.Contains($forbiddenSelfManagement, [StringComparison]::OrdinalIgnoreCase)) `
    -Message "active controller manages itself: $forbiddenSelfManagement"
}

Assert-ContainsAll -Text $relay -Context 'resume relay' -Required @(
  'pwsh',
  'codex-cli-session.ps1',
  '-Action',
  'Resume'
)
foreach ($pattern in @(
  '(?i)\?\s*["'']codex(?:\.cmd)?["'']',
  '(?i)(?:spawnChild|nodeSpawn)\s*\(\s*["'']codex(?:\.cmd)?["'']',
  '(?i)codex\.js'
)) {
  Assert-Contract `
    -Condition (-not [regex]::IsMatch($relay, $pattern)) `
    -Message "resume relay directly launches Codex outside the runner: $pattern"
}

foreach ($requiredPath in @(
  '开发管理\AI协作规则.txt',
  '开发管理\审核入口.txt',
  '开发管理\DeepSeek工作提示词.txt',
  '开发管理\状态与建议维护规则.txt',
  'tools\hourly-automation-lease.ps1',
  'tools\codex-cli-session.ps1',
  'tools\automation-workspace-guard.ps1',
  'tools\automation-finalize-commit.ps1',
  'tools\check-pending-whitespace.ps1',
  'tools\feishu-decision-bridge\src\bridge.mjs',
  'tools\feishu-decision-bridge\src\resume-trigger.mjs'
)) {
  Assert-Contract `
    -Condition (Test-Path -LiteralPath (Join-Path $root $requiredPath) -PathType Leaf) `
    -Message "preserved workflow component is missing: $requiredPath"
}

$automations = @(
  Get-ChildItem -LiteralPath $automationDirectory -Directory -Filter 'tzg-*' |
    Where-Object { $_.Name -ne 'tzg-daily-automation-briefing' } |
    ForEach-Object { Read-Automation -Directory $_.FullName }
)
$activeWriters = @($automations | Where-Object { $_.Status -eq 'ACTIVE' })
$activeWriterIds = @($activeWriters | ForEach-Object { $_.Id })
Assert-Contract -Condition ($activeWriters.Count -le 1) -Message "more than one writer automation is ACTIVE: $($activeWriterIds -join ',')"
if ($activeWriters.Count -eq 1) {
  Assert-Contract `
    -Condition ($activeWriters[0].Id -eq 'tzg-hourly-controller') `
    -Message "unexpected writer automation is ACTIVE: $($activeWriters[0].Id)"
  Assert-Contract `
    -Condition ((Normalize-ContractText -Text $activeWriters[0].Prompt) -ceq (Normalize-ContractText -Text $prompt)) `
    -Message 'active controller prompt does not match the canonical thin prompt'
}
if ($RequireActive) {
  Assert-Contract `
    -Condition ($activeWriters.Count -eq 1 -and $activeWriters[0].Id -eq 'tzg-hourly-controller') `
    -Message 'tzg-hourly-controller is not the unique ACTIVE writer automation'
}

if ($RequireLegacyRetired) {
  $legacyPaths = @(
    'tools\hourly-controller-v2',
    'tools\check-hourly-controller-v2.ps1',
    'tools\automation-controller.ps1',
    'tools\automation-controller-state.ps1',
    'tools\automation-controller-repair.ps1',
    'tools\automation-decision-status.ps1',
    'tools\test-automation-controller.ps1',
    'tools\test-automation-controller-state.ps1',
    'tools\test-automation-controller-repair.ps1',
    'tools\test-automation-decision-status.ps1',
    'tools\fixtures\automation-controller-v5-chained-decision-stuck.json',
    '开发管理\自动工作流任务注册表.json',
    '开发管理\自动工作流控制器v2提示词.txt',
    '开发管理\自动工作流v2规则.txt'
  )
  foreach ($legacyPath in $legacyPaths) {
    Assert-Contract `
      -Condition (-not (Test-Path -LiteralPath (Join-Path $root $legacyPath))) `
      -Message "legacy workflow path still exists: $legacyPath"
  }
}

Write-Output 'check-automation-workflow: OK'
