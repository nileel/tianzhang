#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$tempRoot = Join-Path $temporaryBase "tzg-maintenance-completion-test-$testId"
$repository = Join-Path $tempRoot 'repository'
$approvedStateParent = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex/automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedStateParent "tzg-maintenance-completion-test-$testId"
$toolsRoot = Join-Path $repository 'tools'
$guardTracePath = Join-Path $tempRoot 'queue-guard-trace.txt'
$originalGuardTrace = $env:TZG_QUEUE_GUARD_TRACE
$originalMainRoot = $env:TZG_QUEUE_MAIN_ROOT

try {
  [IO.Directory]::CreateDirectory($toolsRoot) | Out-Null
  foreach ($name in @(
      'invoke-hourly-owner.ps1', 'hourly-automation-lease.ps1', 'select-hourly-task.ps1', 'check-task-cards.ps1', 'get-experience-risk-preflight.ps1', 'set-task-automation-state.ps1',
      'set-task-pending-review.ps1', 'automation-finalize-commit.ps1', 'automation-commit-metadata.ps1', 'check-pending-whitespace.ps1',
      'send-feishu-notification.ps1', 'private-path-acl.ps1', 'hourly-integration-lock.ps1', 'hourly-owner-adapter.ps1'
    )) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $toolsRoot $name)
  }
  Move-Item -LiteralPath (Join-Path $toolsRoot 'check-task-cards.ps1') -Destination (Join-Path $toolsRoot 'check-task-cards-core.ps1')
  $checkerWrapper = @'
#requires -Version 7.0
param(
  [string]$RepositoryRoot, [string]$TaskCardRoot = '开发管理/任务卡', [string]$QueuePath = '开发管理/当前任务队列.txt',
  [string]$BacklogRoot = '开发管理/任务列表', [string]$TaskId, [string]$Postcondition, [string]$BaseCommit,
  [string]$ExpectedRoute, [string]$ExpectedOwner, [switch]$OutputJson
)
if ([string]$Postcondition -ceq 'QueueMaintenanceReadySchema2Guard') {
  [IO.File]::AppendAllText($env:TZG_QUEUE_GUARD_TRACE, "$Postcondition|$BaseCommit`n", [Text.UTF8Encoding]::new($false))
}
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
  Write-Utf8 (Join-Path $toolsRoot 'check-task-cards.ps1') $checkerWrapper

  $candidateSource = @'
#requires -Version 7.0
param(
  [string]$Action, [string]$Route, [string]$RepositoryRoot, [string]$TaskId, [string]$RunId,
  [string]$Model, [string]$StateRoot, [int]$ResponsibilityTimeoutSeconds, [string]$ResumeContextPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$statusPath = Join-Path $RepositoryRoot '开发管理/自动工作流状态.txt'
$before = [IO.File]::ReadAllText($statusPath)
$after = $before.Replace('维护摘要：待校准', '维护摘要：已校准')
if ($after -ceq $before) { throw 'fixture maintenance source did not change' }
[IO.File]::WriteAllText($statusPath, $after, [Text.UTF8Encoding]::new($false))

$resultText = '问题=维护摘要仍为旧值；完成=已校准普通空队列维护摘要'
$impactText = '影响=仅维护状态摘要；边界=未建立维护型决策'
$verifyText = '验证=普通 QueueMaintenance fixture 通过；后续=无'
$plainText = '发生=维护摘要已校准；影响=没有业务任务变化；需要=无需处理'
$paths = @('开发管理/自动工作流状态.txt')
& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'automation-finalize-commit.ps1') -RepositoryRoot $RepositoryRoot -ExpectedPaths ($paths -join '|') -CommitMessage 'candidate(QUEUE-MAINTENANCE): test ordinary completion' -RequireAutomationMetadata -AutomationTask 'QUEUE-MAINTENANCE' -AutomationState completed -AutomationResult $resultText -AutomationImpact $impactText -AutomationVerify $verifyText -AutomationPlain $plainText *> $null
if ($LASTEXITCODE -ne 0) { throw 'fixture finalizer failed' }
$commit = @(& git -C $RepositoryRoot rev-parse HEAD)[0]
$mainRoot = $env:TZG_QUEUE_MAIN_ROOT
$concurrentPath = Join-Path $mainRoot '开发管理/并发事实.txt'
[IO.File]::WriteAllText($concurrentPath, "# 并发事实`n", [Text.UTF8Encoding]::new($false))
& git -C $mainRoot add -- '开发管理/并发事实.txt'
& git -C $mainRoot commit -q -m 'test: advance master before canonical replay'
if ($LASTEXITCODE -ne 0) { throw 'fixture concurrent master commit failed' }
$candidateResult = [ordered]@{
  category = 'completed'; expectedTransition = 'queue_ready_count=0'; changedPaths = $paths
  verified = @('ordinary QueueMaintenance fixture'); unverified = @(); residualRisk = 'none'
  result = $resultText; impact = $impactText; verify = $verifyText; plain = $plainText
}
$json = [ordered]@{
  status = 'completed'; taskId = 'QUEUE-MAINTENANCE'; runId = $RunId; sessionId = 'fixture-session'
  candidateCommit = $commit; candidateResult = $candidateResult
} | ConvertTo-Json -Compress -Depth 50
[Console]::Out.WriteLine($json)
'@
  Write-Utf8 (Join-Path $toolsRoot 'invoke-codex-candidate.ps1') $candidateSource
  Write-Utf8 (Join-Path $toolsRoot 'feishu-decision-bridge/src/send-decision.mjs') "process.stdout.write('{\"result\":\"UNUSED\"}\\n');`n"
  Write-Utf8 (Join-Path $toolsRoot 'feishu-decision-bridge/src/consume-reply.mjs') "process.stdout.write('{\"result\":\"NO_REPLY\"}\\n');`n"

  & git -C $repository init -q -b master
  if ($LASTEXITCODE -ne 0) { throw 'fixture git init failed' }
  & git -C $repository config user.name 'Maintenance Completion Test'
  & git -C $repository config user.email 'maintenance-completion@example.invalid'
  Write-Utf8 (Join-Path $repository '开发管理/任务卡/.gitkeep') ''
  Write-Utf8 (Join-Path $repository '开发管理/当前任务队列.txt') (@(
      '# 当前任务队列', '',
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |',
      '|---|---|---|---|---|---|---|---|', ''
    ) -join "`n")
  Write-Utf8 (Join-Path $repository '开发管理/自动工作流状态.txt') "# 自动工作流状态`n`n维护摘要：待校准`n"
  & git -C $repository add -A
  & git -C $repository commit -q -m 'test: seed ordinary queue maintenance'
  if ($LASTEXITCODE -ne 0) { throw 'fixture seed commit failed' }
  $candidateBase = [string]@(& git -C $repository rev-parse HEAD)[0]
  $env:TZG_QUEUE_GUARD_TRACE = $guardTracePath
  $env:TZG_QUEUE_MAIN_ROOT = $repository

  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolsRoot 'invoke-hourly-owner.ps1') -Owner codex -Action RunOnce -RepositoryRoot $repository -Model gpt-test -StateRoot $stateRoot 2>$null)
  Assert-True ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) "Ordinary maintenance run failed: $($output -join ' | ')"
  $terminal = $output[0] | ConvertFrom-Json -Depth 50
  Assert-True ([string]$terminal.status -ceq 'maintenance_completed') "Ordinary maintenance status mismatch: $($output[0])"
  Assert-True ([string]$terminal.category -ceq 'success') 'Ordinary maintenance result was not successful'
  Assert-True ([string]$terminal.taskId -ceq 'QUEUE-MAINTENANCE') 'Ordinary maintenance taskId changed'
  Assert-True ([string]$terminal.cleanup -ceq 'cleaned') 'Ordinary maintenance worktree was not cleaned'
  Assert-True ($terminal.PSObject.Properties.Name -cnotcontains 'decisionId') 'Ordinary maintenance unexpectedly returned decisionId'
  Assert-True ($terminal.PSObject.Properties.Name -cnotcontains 'decisionTaskId') 'Ordinary maintenance unexpectedly returned decisionTaskId'
  Assert-True ([string]$terminal.status -cne 'attention_required') 'Ordinary maintenance was misreported as attention_required'
  $canonicalBase = [string]@(& git -C $repository rev-parse "$([string]$terminal.formalHead)^")[0]
  Assert-True ($canonicalBase -cne $candidateBase) 'Fixture did not advance master between candidate and canonical replay'
  $guardCalls = @([IO.File]::ReadAllLines($guardTracePath) | Where-Object { $_ })
  Assert-True ($guardCalls.Count -eq 2) "Canonical and post-fast-forward checks did not both call the QueueMaintenance guard: $($guardCalls -join '|')"
  foreach ($guardCall in $guardCalls) {
    Assert-True ($guardCall -ceq "QueueMaintenanceReadySchema2Guard|$canonicalBase") "QueueMaintenance guard reused the candidate base instead of the latest canonical base: $guardCall"
  }

  $runtimeOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolsRoot 'hourly-automation-lease.ps1') -Action Show -StateRoot $stateRoot -RepositoryRoot $repository 2>$null)
  Assert-True ($LASTEXITCODE -eq 0 -and $runtimeOutput.Count -eq 1) 'Fixture runtime Show failed'
  $runtime = $runtimeOutput[0] | ConvertFrom-Json -Depth 30
  Assert-True ($null -eq $runtime.state.runs.codex -and $null -eq $runtime.state.runs.deepseek) 'Ordinary maintenance left an owner run'
  Assert-True ([string]$runtime.integrationLockStatus -ceq 'none') 'Ordinary maintenance left the integration lock held'
  Write-Output 'test-queue-maintenance-completion: PASS'
} finally {
  $env:TZG_QUEUE_GUARD_TRACE = $originalGuardTrace
  $env:TZG_QUEUE_MAIN_ROOT = $originalMainRoot
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedState = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $stateRoot).Path)
    Assert-True ($resolvedState.StartsWith($approvedStateParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Split-Path -Leaf $resolvedState) -ceq "tzg-maintenance-completion-test-$testId") 'Refusing to remove unsafe fixture state root'
    Remove-Item -LiteralPath $resolvedState -Recurse -Force
  }
  if (Test-Path -LiteralPath $tempRoot) {
    $resolvedTemp = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $tempRoot).Path)
    Assert-True ($resolvedTemp.StartsWith($temporaryBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Split-Path -Leaf $resolvedTemp) -ceq "tzg-maintenance-completion-test-$testId") 'Refusing to remove unsafe fixture repository'
    Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
  }
}
