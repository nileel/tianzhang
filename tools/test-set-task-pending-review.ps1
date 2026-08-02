#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $temporaryBase "tzg-pending-review-test-$testId"
$toolPath = Join-Path $PSScriptRoot 'set-task-pending-review.ps1'
$taskId = 'TASK-PENDING-REVIEW'

try {
  [IO.Directory]::CreateDirectory((Join-Path $testRoot 'tools')) | Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $testRoot 'tools/check-task-cards.ps1')
  $metadata = [ordered]@{
    schemaVersion = 1; id = $taskId; title = 'Pending review fixture'; priority = 'P1'; route = 'external_execute'
    owner = 'deepseek'; domain = 'automation'; stage = 'verification'; dispatchState = 'ready'; blockedBy = @()
    stateReason = 'fixture'; expectedPaths = @(
      'fixtures/business.txt', '开发管理/任务列表/自动化任务.txt', '开发管理/当前任务队列.txt',
      "开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt"
    ); sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $card = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $taskId · Pending review fixture",
    '## 来源与当前边界', '- fixture', '## 必查范围', '- fixture', '## 实施范围', '- fixture',
    '## 禁止项', '- fixture', '## 验证', '- fixture', '## 完成条件', '- fixture', '## 停止条件', '- fixture'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $testRoot "开发管理/任务卡/$taskId.txt") -Text $card
  Write-Utf8 -Path (Join-Path $testRoot '开发管理/当前任务队列.txt') -Text (@(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |',
    '| --- | --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | external_execute | deepseek | P1 | automation | verification | Pending review fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  Write-Utf8 -Path (Join-Path $testRoot '开发管理/任务列表/自动化任务.txt') -Text (@(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |',
    '| --- | --- | --- | --- | --- | --- | --- |',
    "| $taskId | P1 | deepseek | 已排队 | — | Pending review fixture | 开发管理/任务卡/$taskId.txt |"
  ) -join "`n")
  & git -C $testRoot init *> $null
  if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize fixture repository' }

  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $toolPath -RepositoryRoot $testRoot -TaskId $taskId 2>&1)
  Assert-Equal $LASTEXITCODE 0 "Pending-review update failed: $(@($output) -join "`n")"
  Assert-Equal ([string](($output[0] | ConvertFrom-Json).status)) 'updated' 'Pending-review result mismatch'
  $updatedText = [IO.File]::ReadAllText((Join-Path $testRoot "开发管理/任务卡/$taskId.txt"))
  $updatedMetadataText = [regex]::Match($updatedText, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---').Groups['json'].Value
  $updatedMetadata = $updatedMetadataText | ConvertFrom-Json
  Assert-Equal ([string]$updatedMetadata.route) 'codex_review' 'Task route was not updated'
  Assert-Equal ([string]$updatedMetadata.owner) 'codex' 'Task owner was not updated'
  Assert-Equal ([string]$updatedMetadata.dispatchState) 'ready' 'Task state was not retained as ready'
  Assert-True (([IO.File]::ReadAllText((Join-Path $testRoot '开发管理/当前任务队列.txt'))) -match '\| codex_review \| codex \|') 'Queue projection was not updated'
  Assert-True (([IO.File]::ReadAllText((Join-Path $testRoot '开发管理/任务列表/自动化任务.txt'))) -match '\| codex \| 已排队 \|') 'Backlog projection was not updated'

  $second = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $toolPath -RepositoryRoot $testRoot -TaskId $taskId 2>&1)
  Assert-True ($LASTEXITCODE -ne 0) 'Second transition unexpectedly succeeded'
  Assert-Equal ([string](($second[-1] | ConvertFrom-Json).detailCode)) 'pending_review_projection_failed' 'Second transition failure code mismatch'

  Write-Output 'test-set-task-pending-review: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($temporaryBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cne "tzg-pending-review-test-$testId") {
      throw "Refusing unsafe pending-review-test cleanup: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
