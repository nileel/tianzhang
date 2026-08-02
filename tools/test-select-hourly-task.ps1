#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Invoke-Git { param([string]$Root, [string[]]$Arguments) & git -C $Root @Arguments *> $null; if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" } }

function Write-TaskFixture {
  param([string]$Root, [string]$Id, [string]$Route, [string]$Owner, [string]$Title)
  $metadata = [ordered]@{
    schemaVersion = 1; id = $Id; title = $Title; priority = 'P1'; route = $Route; owner = $Owner
    domain = 'automation'; stage = 'implementation'; dispatchState = 'ready'; blockedBy = @()
    stateReason = 'selector fixture'; expectedPaths = @(
      "fixtures/$Id.txt", '开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt',
      "开发管理/任务卡/$Id.txt", "开发管理/任务归档/$Id.txt"
    ); sourceBacklog = '开发管理/任务列表/自动化任务.txt'
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

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $temporaryBase "tzg-hourly-selector-test-$testId"
$selectorPath = Join-Path $PSScriptRoot 'select-hourly-task.ps1'

try {
  [IO.Directory]::CreateDirectory((Join-Path $testRoot 'tools')) | Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $testRoot 'tools/check-task-cards.ps1')
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

  $deepseek = Invoke-Selector -Root $testRoot -Owner deepseek
  Assert-Equal ([string]$deepseek.status) 'selected' 'DeepSeek selector did not select'
  Assert-Equal ([string]$deepseek.taskId) 'TASK-DS-FIRST' 'DeepSeek selector did not preserve queue order'
  Assert-Equal ([string]$deepseek.route) 'external_execute' 'DeepSeek route mismatch'
  Assert-True ([string]$deepseek.taskCardDigest -cmatch '^[0-9a-f]{64}$') 'Task-card digest is invalid'
  Assert-True (@($deepseek.expectedPaths) -ccontains 'fixtures/TASK-DS-FIRST.txt') 'Selector lost expected paths'

  $codex = Invoke-Selector -Root $testRoot -Owner codex
  Assert-Equal ([string]$codex.taskId) 'TASK-CODEX' 'Codex selector did not skip DeepSeek row'

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
  $none = Invoke-Selector -Root $testRoot -Owner deepseek
  Assert-Equal ([string]$none.status) 'no_candidate' 'DeepSeek no-candidate result mismatch'

  Write-Output 'test-select-hourly-task: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $prefix = $temporaryBase + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-hourly-selector-test-$testId") {
      throw "Refusing unsafe selector-test cleanup: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
