#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Invoke-Git { param([string]$Root, [string[]]$Arguments) & git -C $Root @Arguments *> $null; if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" } }
function Reset-CheckerTrace { param([string]$Path) if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force } }
function Get-CheckerTrace { param([string]$Path) if (Test-Path -LiteralPath $Path) { @([IO.File]::ReadAllLines($Path) | Where-Object { $_ }) } else { @() } }

function Write-TaskFixture {
  param([string]$Root, [string]$Id, [string]$Route, [string]$Owner, [string]$Title)
  $metadata = [ordered]@{
    schemaVersion = 2; id = $Id; title = $Title; priority = 'P1'; route = $Route; owner = $Owner
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @()
    stateReason = 'selector fixture'; expectedPaths = @(
      "fixtures/$Id.txt", '开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt',
      "开发管理/任务卡/$Id.txt", "开发管理/任务归档/$Id.txt"
    ); riskPreflight = [ordered]@{ explicitRefs = @(); matched = @(); gates = @() }
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $text = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $Id · $Title",
    '## 来源与当前边界', '- 测试。', '## 必查范围', '- 测试。', '## 实施范围', '- 测试。',
    '## 禁止项', '- 不扩大。', '## 验证', '- 运行测试。', '## 完成条件', '- 选择正确。', '## 停止条件', '- 投影不一致。'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $Root "开发管理/任务卡/$Id.txt") -Text $text
}

function Invoke-Selector {
  param([string]$Root, [string]$Owner)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $selectorPath -RepositoryRoot $Root -Owner $Owner 2>&1)
  Assert-Equal $LASTEXITCODE 0 "Selector failed: $(@($output) -join "`n")"
  Assert-Equal $output.Count 1 'Selector did not emit one line'
  $output[0] | ConvertFrom-Json -Depth 20
}

function Assert-SelectorFailure {
  param([string]$Root, [string]$Owner, [string]$Message)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $selectorPath -RepositoryRoot $Root -Owner $Owner 2>&1)
  Assert-True ($LASTEXITCODE -ne 0) $Message
  Assert-True (@($output | Where-Object { [string]$_ -match '"status":"failed"' }).Count -eq 1) "$Message did not return the stable failed result"
}

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $temporaryBase "tzg-hourly-selector-test-$testId"
$selectorPath = Join-Path $PSScriptRoot 'select-hourly-task.ps1'
$checkerTracePath = Join-Path $testRoot 'checker-trace.txt'
$originalCheckerTrace = $env:TZG_SELECTOR_CHECK_TRACE
$originalForcedPostcondition = $env:TZG_SELECTOR_FORCE_BAD_POSTCONDITION

try {
  [IO.Directory]::CreateDirectory((Join-Path $testRoot 'tools')) | Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $testRoot 'tools/check-task-cards.ps1')
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'get-experience-risk-preflight.ps1') -Destination (Join-Path $testRoot 'tools/get-experience-risk-preflight.ps1')
  Move-Item -LiteralPath (Join-Path $testRoot 'tools/check-task-cards.ps1') -Destination (Join-Path $testRoot 'tools/check-task-cards-core.ps1')
  $checkerWrapper = @'
#requires -Version 7.0
param(
  [string]$RepositoryRoot, [string]$TaskCardRoot = '开发管理/任务卡', [string]$QueuePath = '开发管理/当前任务队列.txt',
  [string]$BacklogRoot = '开发管理/任务列表', [string]$TaskId, [string]$Postcondition, [string]$BaseCommit,
  [string]$ExpectedRoute, [string]$ExpectedOwner, [switch]$OutputJson
)
[IO.File]::AppendAllText($env:TZG_SELECTOR_CHECK_TRACE, "$TaskId|$Postcondition`n", [Text.UTF8Encoding]::new($false))
if ($env:TZG_SELECTOR_FORCE_BAD_POSTCONDITION -ceq '1' -and $Postcondition -ceq 'CodexDispatchReady') { $ExpectedRoute = 'codex_review' }
$arguments = @('-RepositoryRoot', $RepositoryRoot, '-TaskCardRoot', $TaskCardRoot, '-QueuePath', $QueuePath, '-BacklogRoot', $BacklogRoot)
if (-not [string]::IsNullOrWhiteSpace($TaskId)) { $arguments += @('-TaskId', $TaskId) }
if (-not [string]::IsNullOrWhiteSpace($Postcondition)) { $arguments += @('-Postcondition', $Postcondition) }
if (-not [string]::IsNullOrWhiteSpace($BaseCommit)) { $arguments += @('-BaseCommit', $BaseCommit) }
if (-not [string]::IsNullOrWhiteSpace($ExpectedRoute)) { $arguments += @('-ExpectedRoute', $ExpectedRoute) }
if (-not [string]::IsNullOrWhiteSpace($ExpectedOwner)) { $arguments += @('-ExpectedOwner', $ExpectedOwner) }
if ($OutputJson) { $arguments += '-OutputJson' }
& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-task-cards-core.ps1') @arguments
exit $LASTEXITCODE
'@
  Write-Utf8 -Path (Join-Path $testRoot 'tools/check-task-cards.ps1') -Text $checkerWrapper
  Write-Utf8 -Path (Join-Path $testRoot '开发管理/经验库/风险索引.json') -Text '{"schemaVersion":1,"experiences":[],"gates":[]}'
  Write-TaskFixture -Root $testRoot -Id 'TASK-DS-FIRST' -Route external_execute -Owner deepseek -Title 'DeepSeek first'
  Write-TaskFixture -Root $testRoot -Id 'TASK-CODEX' -Route codex_execute -Owner codex -Title 'Codex task'
  Write-TaskFixture -Root $testRoot -Id 'TASK-DS-SECOND' -Route external_execute -Owner deepseek -Title 'DeepSeek second'
  $rows = @(
    @('TASK-DS-FIRST', 'external_execute', 'deepseek', 'P1', 'automation', 'implementation', 'DeepSeek first'),
    @('TASK-CODEX', 'codex_execute', 'codex', 'P1', 'automation', 'implementation', 'Codex task'),
    @('TASK-DS-SECOND', 'external_execute', 'deepseek', 'P1', 'automation', 'implementation', 'DeepSeek second')
  )
  $queue = @('| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- | --- |')
  $backlog = @('| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- |')
  foreach ($row in $rows) {
    $queue += "| $($row[0]) | $($row[1]) | $($row[2]) | $($row[3]) | $($row[4]) | $($row[5]) | $($row[6]) | 开发管理/任务卡/$($row[0]).txt |"
    $backlog += "| $($row[0]) | $($row[3]) | $($row[2]) | 已排队 | — | $($row[6]) | 开发管理/任务卡/$($row[0]).txt |"
  }
  Write-Utf8 -Path (Join-Path $testRoot '开发管理/当前任务队列.txt') -Text ($queue -join "`n")
  Write-Utf8 -Path (Join-Path $testRoot '开发管理/任务列表/自动化任务.txt') -Text ($backlog -join "`n")
  Invoke-Git -Root $testRoot -Arguments @('init')
  $env:TZG_SELECTOR_CHECK_TRACE = $checkerTracePath

  Reset-CheckerTrace $checkerTracePath
  $deepseek = Invoke-Selector -Root $testRoot -Owner deepseek
  Assert-Equal ([string]$deepseek.status) 'selected' 'DeepSeek selector did not select'
  Assert-Equal ([string]$deepseek.taskId) 'TASK-DS-FIRST' 'DeepSeek selector did not preserve queue order'
  Assert-Equal ([string]$deepseek.route) 'external_execute' 'DeepSeek route mismatch'
  Assert-True ([string]$deepseek.taskCardDigest -cmatch '^[0-9a-f]{64}$') 'Task-card digest is invalid'
  Assert-True (@($deepseek.expectedPaths) -ccontains 'fixtures/TASK-DS-FIRST.txt') 'Selector lost expected paths'
  Assert-Equal ([int]$deepseek.readyCount) 3 'DeepSeek selector lost global ready count evidence'
  Assert-Equal (@(Get-CheckerTrace $checkerTracePath).Count) 1 'DeepSeek unchanged candidate ran more than one full check'
  Assert-Equal ([string]@(Get-CheckerTrace $checkerTracePath)[0]) 'TASK-DS-FIRST|ExternalDispatchReady' 'DeepSeek did not combine global and target validation'

  Reset-CheckerTrace $checkerTracePath
  $codex = Invoke-Selector -Root $testRoot -Owner codex
  Assert-Equal ([string]$codex.taskId) 'TASK-CODEX' 'Codex selector did not skip DeepSeek row'
  Assert-Equal ([int]$codex.readyCount) 3 'Codex selector lost global ready count evidence'
  Assert-Equal (@(Get-CheckerTrace $checkerTracePath).Count) 1 'Codex unchanged candidate ran more than one full check'
  Assert-Equal ([string]@(Get-CheckerTrace $checkerTracePath)[0]) 'TASK-CODEX|CodexDispatchReady' 'Codex did not combine global and target validation'

  Reset-CheckerTrace $checkerTracePath
  $env:TZG_SELECTOR_FORCE_BAD_POSTCONDITION = '1'
  Assert-SelectorFailure -Root $testRoot -Owner codex -Message 'Selector accepted a bad target postcondition'
  $env:TZG_SELECTOR_FORCE_BAD_POSTCONDITION = $null
  Assert-Equal @(Get-CheckerTrace $checkerTracePath).Count 1 'Bad target postcondition ran more than one full check'

  $queueWithoutDeepSeek = @($queue | Where-Object { $_ -notmatch '^\| TASK-DS-' })
  Write-Utf8 -Path (Join-Path $testRoot '开发管理/当前任务队列.txt') -Text ($queueWithoutDeepSeek -join "`n")
  foreach ($id in @('TASK-DS-FIRST', 'TASK-DS-SECOND')) {
    $cardPath = Join-Path $testRoot "开发管理/任务卡/$id.txt"
    $text = [IO.File]::ReadAllText($cardPath)
    $text = $text.Replace('"dispatchState": "ready"', '"dispatchState": "blocked"')
    Write-Utf8 -Path $cardPath -Text $text
  }
  $backlogWithoutDeepSeek = @($backlog | ForEach-Object { if ($_ -match '^\| TASK-DS-') { $_.Replace('| 已排队 |', '| 阻塞 |') } else { $_ } })
  Write-Utf8 -Path (Join-Path $testRoot '开发管理/任务列表/自动化任务.txt') -Text ($backlogWithoutDeepSeek -join "`n")
  Reset-CheckerTrace $checkerTracePath
  $none = Invoke-Selector -Root $testRoot -Owner deepseek
  Assert-Equal ([string]$none.status) 'no_candidate' 'DeepSeek no-candidate result mismatch'
  Assert-Equal ([int]$none.readyCount) 1 'No-candidate selector lost global ready count evidence'
  Assert-Equal @(Get-CheckerTrace $checkerTracePath).Count 1 'No-candidate selection ran more than one full check'
  Assert-Equal ([string]@(Get-CheckerTrace $checkerTracePath)[0]) '|' 'No-candidate selection did not run the single global check'

  $backlogPath = Join-Path $testRoot '开发管理/任务列表/自动化任务.txt'
  $validBacklog = [IO.File]::ReadAllText($backlogPath)
  Write-Utf8 -Path $backlogPath -Text $validBacklog.Replace('| 已排队 | — | Codex task |', '| 已排队 | — | Broken projection |')
  Reset-CheckerTrace $checkerTracePath
  Assert-SelectorFailure -Root $testRoot -Owner codex -Message 'Selector accepted a bad global projection'
  Assert-Equal @(Get-CheckerTrace $checkerTracePath).Count 1 'Bad global projection ran more than one full check'

  Write-Output 'test-select-hourly-task: OK'
} finally {
  $env:TZG_SELECTOR_CHECK_TRACE = $originalCheckerTrace
  $env:TZG_SELECTOR_FORCE_BAD_POSTCONDITION = $originalForcedPostcondition
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $prefix = $temporaryBase + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-hourly-selector-test-$testId") {
      throw "Refusing unsafe selector-test cleanup: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
