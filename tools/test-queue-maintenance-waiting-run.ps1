#requires -Version 7.0

$ErrorActionPreference = 'Stop'
function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Read-Meta { param([string]$Path) $text = [IO.File]::ReadAllText($Path); ([regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---').Groups['json'].Value | ConvertFrom-Json -Depth 50) }

$testId = [Guid]::NewGuid().ToString('N')
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-maintenance-wait-test-$testId"
$repository = Join-Path $tempRoot 'repository'
$approvedStateParent = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex/automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedStateParent "tzg-maintenance-wait-test-$testId"
$toolsRoot = Join-Path $repository 'tools'
try {
  [IO.Directory]::CreateDirectory($toolsRoot) | Out-Null
  foreach ($name in @(
      'invoke-hourly-owner.ps1', 'hourly-automation-lease.ps1', 'select-hourly-task.ps1', 'check-task-cards.ps1', 'set-task-automation-state.ps1',
      'set-task-pending-review.ps1', 'automation-finalize-commit.ps1', 'automation-commit-metadata.ps1', 'check-pending-whitespace.ps1',
      'send-feishu-notification.ps1', 'private-path-acl.ps1', 'hourly-integration-lock.ps1', 'hourly-owner-adapter.ps1'
    )) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $toolsRoot $name) }
  $candidateSource = @'
#requires -Version 7.0
param(
  [string]$Action, [string]$Route, [string]$RepositoryRoot, [string]$TaskId, [string]$RunId,
  [string]$Model, [string]$StateRoot, [int]$ResponsibilityTimeoutSeconds, [string]$ResumeContextPath
)
$ErrorActionPreference = 'Stop'
trap { [IO.Directory]::CreateDirectory($StateRoot) | Out-Null; [IO.File]::WriteAllText((Join-Path $StateRoot 'fixture-candidate-error.txt'), ($_ | Out-String)); exit 1 }
[IO.File]::WriteAllText((Join-Path $StateRoot 'fixture-candidate-started.txt'), 'started')
$cardPath = Join-Path $RepositoryRoot '开发管理/任务卡/T-MAINT-01.txt'
$text = [IO.File]::ReadAllText($cardPath)
$match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---\r?\n(?<body>.*)$')
$meta = $match.Groups['json'].Value | ConvertFrom-Json -Depth 50
$meta.blockedBy = @()
$meta.stateReason = '具名前置已完成，唯一剩余条件是负责人选择路线'
[IO.File]::WriteAllText($cardPath, (@('---TASK-META---', ($meta | ConvertTo-Json -Depth 50), '---TASK-BODY---', $match.Groups['body'].Value) -join "`n"), [Text.UTF8Encoding]::new($false))
$backlogPath = Join-Path $RepositoryRoot '开发管理/任务列表/自动化任务.txt'
$backlog = [IO.File]::ReadAllText($backlogPath).Replace('| T-MAINT-01 | P1 | codex | 阻塞 | T-DONE-01 |', '| T-MAINT-01 | P1 | codex | 阻塞 | — |')
[IO.File]::WriteAllText($backlogPath, $backlog, [Text.UTF8Encoding]::new($false))
$resultText = '问题=具名前置已完成但路线未选择；完成=已移除最后一个命名前置并保留准确阻塞事实'
$impactText = '影响=任务只等待负责人路线选择；边界=未改业务文件或调度状态'
$verifyText = '验证=任务卡与 backlog 前置投影一致；后续=共享入口建立维护型决策'
$plainText = '发生=最后一个任务前置已完成；影响=还需要你选择路线；需要=等待飞书决策卡'
$paths = @('开发管理/任务卡/T-MAINT-01.txt', '开发管理/任务列表/自动化任务.txt')
& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'automation-finalize-commit.ps1') -RepositoryRoot $RepositoryRoot -ExpectedPaths ($paths -join '|') -CommitMessage 'candidate(QUEUE-MAINTENANCE): test maintenance decision' -RequireAutomationMetadata -AutomationTask 'QUEUE-MAINTENANCE' -AutomationState completed -AutomationResult $resultText -AutomationImpact $impactText -AutomationVerify $verifyText -AutomationPlain $plainText *> $null
if ($LASTEXITCODE -ne 0) { throw 'fixture finalizer failed' }
$commit = @(& git -C $RepositoryRoot rev-parse HEAD)[0]
$candidateResult = [ordered]@{
  category = 'maintenance_decision'; expectedTransition = 'maintenance_pending_decision'; changedPaths = $paths; verified = @('fixture'); unverified = @(); residualRisk = 'none'
  result = $resultText; impact = $impactText; verify = $verifyText; plain = $plainText; decisionTaskId = 'T-MAINT-01'; question = '选择哪条实现路线？'
  options = @(
    [ordered]@{ key = 'A'; label = '采用路线 A'; targetState = 'ready' },
    [ordered]@{ key = 'B'; label = '采用路线 B'; targetState = 'ready' },
    [ordered]@{ key = 'C'; label = '暂不批准'; targetState = 'blocked' }
  ); recommendedOption = 'A'; impactSummary = 'A/B 会形成 ready 卡，C 保持阻塞'; plainSummary = [ordered]@{ situation = '任务只差路线选择'; impact = '选择后可继续准备'; action = '请选择 A、B 或 C' }
}
$json = [ordered]@{ status = 'maintenance_decision'; taskId = 'QUEUE-MAINTENANCE'; runId = $RunId; sessionId = 'fixture-session'; candidateCommit = $commit; candidateResult = $candidateResult } | ConvertTo-Json -Compress -Depth 50
[IO.File]::WriteAllText((Join-Path $StateRoot 'fixture-candidate-output.json'), $json)
[Console]::Out.WriteLine($json)
exit 0
'@
  Write-Utf8 (Join-Path $toolsRoot 'invoke-codex-candidate.ps1') $candidateSource

  $bridgeRoot = Join-Path $toolsRoot 'feishu-decision-bridge/src'
  $counterPath = Join-Path $tempRoot 'consume-count.txt'
  $counterJson = $counterPath | ConvertTo-Json -Compress
  $consumerSource = "import { appendFileSync } from 'node:fs'; appendFileSync($counterJson, '1\n'); " + 'process.stdout.write(''{"result":"NO_REPLY"}\n'');'
  $senderSource = @'
import { readFileSync, writeFileSync } from 'node:fs';
const path = process.argv[process.argv.indexOf('--request-file') + 1];
const request = JSON.parse(readFileSync(path, 'utf8'));
writeFileSync(path, `${JSON.stringify({ pendingDecision: { decisionId: request.decision.decisionId, allowedOptions: ['A','B','C'], allowCustomReply: false, createdAt: '2026-08-14T00:00:00.000Z', expiresAt: '2099-08-21T00:00:00.000Z' } })}\n`);
process.stdout.write('{"result":"PROVIDER_ACCEPTED"}\n');
'@
  Write-Utf8 (Join-Path $bridgeRoot 'consume-reply.mjs') $consumerSource
  Write-Utf8 (Join-Path $bridgeRoot 'send-decision.mjs') $senderSource

  & git -C $repository init -q
  & git -C $repository config user.name 'Maintenance Waiting Test'
  & git -C $repository config user.email 'maintenance-waiting@example.invalid'
  $meta = [ordered]@{
    schemaVersion = 1; id = 'T-MAINT-01'; title = '维护等待测试'; priority = 'P1'; route = 'codex_execute'; owner = 'codex'; domain = 'automation'; stage = 'decision'
    dispatchState = 'blocked'; blockedBy = @('T-DONE-01'); stateReason = '阻塞于 T-DONE-01'; expectedPaths = @('开发管理/任务卡/T-MAINT-01.txt', '开发管理/任务归档/T-MAINT-01.txt')
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $body = @('# T-MAINT-01 · 维护等待测试', '', '## 来源与当前边界', 'fixture', '## 必查范围', 'fixture', '## 实施范围', 'fixture', '## 禁止项', 'fixture', '## 验证', 'fixture', '## 完成条件', 'fixture', '## 停止条件', 'fixture') -join "`n"
  $cardPath = Join-Path $repository '开发管理/任务卡/T-MAINT-01.txt'
  Write-Utf8 $cardPath (@('---TASK-META---', ($meta | ConvertTo-Json -Depth 50), '---TASK-BODY---', $body) -join "`n")
  Write-Utf8 (Join-Path $repository '开发管理/当前任务队列.txt') (@('# 当前任务队列', '', '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '|---|---|---|---|---|---|---|---|', '') -join "`n")
  Write-Utf8 (Join-Path $repository '开发管理/任务列表/自动化任务.txt') (@('# 自动化任务', '', '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '|---|---|---|---|---|---|---|', '| T-MAINT-01 | P1 | codex | 阻塞 | T-DONE-01 | 维护等待测试 | 开发管理/任务卡/T-MAINT-01.txt |', '') -join "`n")
  & git -C $repository add -A
  & git -C $repository commit -q -m 'test: seed maintenance decision'

  $firstOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolsRoot 'invoke-hourly-owner.ps1') -Owner codex -Action RunOnce -RepositoryRoot $repository -Model gpt-test -StateRoot $stateRoot 2>$null)
  Assert-True ($LASTEXITCODE -eq 0 -and $firstOutput.Count -eq 1) "First maintenance run failed: $($firstOutput -join ' | ')"
  $first = $firstOutput[0] | ConvertFrom-Json -Depth 50
  Assert-True ([string]$first.status -ceq 'decision_requested' -and [string]$first.taskId -ceq 'QUEUE-MAINTENANCE' -and [string]$first.decisionTaskId -ceq 'T-MAINT-01' -and [string]$first.detailCode -ceq 'maintenance_decision_requested' -and [string]$first.cleanup -ceq 'cleaned') "First terminal mismatch: $($firstOutput[0])"
  Assert-True ([string]$first.decisionId -cmatch '^DEC-[0-9]{8}-QM[0-9A-F]{12}$') 'Generated maintenance decisionId is invalid'
  $pendingMeta = Read-Meta $cardPath
  Assert-True ([string]$pendingMeta.dispatchState -ceq 'pending_decision' -and [string]$pendingMeta.automationDecision.status -ceq 'awaiting_reply' -and $pendingMeta.PSObject.Properties.Name -cnotcontains 'automationCheckpoint') 'First run did not establish the public maintenance projection'
  $recordPath = Join-Path $stateRoot "maintenance-decisions/$($first.decisionId).json"
  Assert-True (Test-Path -LiteralPath $recordPath -PathType Leaf) 'First run did not persist the private maintenance record'

  for ($runIndex = 1; $runIndex -le 2; $runIndex++) {
    $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolsRoot 'invoke-hourly-owner.ps1') -Owner codex -Action RunOnce -RepositoryRoot $repository -Model gpt-test -StateRoot $stateRoot 2>$null)
    Assert-True ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) "Waiting run $runIndex failed"
    $terminal = $output[0] | ConvertFrom-Json -Depth 50
    Assert-True ([string]$terminal.status -ceq 'waiting_decision' -and [string]$terminal.taskId -ceq 'QUEUE-MAINTENANCE' -and [string]$terminal.decisionTaskId -ceq 'T-MAINT-01' -and [string]$terminal.cleanup -ceq 'none') "Waiting terminal $runIndex mismatch: $($output[0])"
    $runtime = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolsRoot 'hourly-automation-lease.ps1') -Action Show -StateRoot $stateRoot 2>$null)[0] | ConvertFrom-Json -Depth 30
    Assert-True ($null -eq $runtime.state.runs.codex -and $null -eq $runtime.state.runs.deepseek) "Waiting run $runIndex claimed runtime"
  }
  Assert-True (@([IO.File]::ReadAllLines($counterPath)).Count -eq 2) 'Each waiting RunOnce must consume exactly one reply snapshot'
  Write-Output 'test-queue-maintenance-waiting-run: PASS'
} finally {
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedState = (Resolve-Path -LiteralPath $stateRoot).Path
    Assert-True ($resolvedState.StartsWith($approvedStateParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedState) -ceq "tzg-maintenance-wait-test-$testId") 'Refusing to remove unsafe state fixture'
    Remove-Item -LiteralPath $resolvedState -Recurse -Force
  }
  if (Test-Path -LiteralPath $tempRoot) {
    $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'Refusing to remove non-temp fixture'
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
