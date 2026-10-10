#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Write-Utf8 {
  param([string]$Path, [string]$Text)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}
function Invoke-Git {
  param([string]$Root, [string[]]$Arguments)
  $output = @(& git -C $Root @Arguments 2>&1)
  if ($LASTEXITCODE -ne 0) { throw "git failed: $($output -join "`n")" }
  ($output -join "`n").Trim()
}

$testId = [guid]::NewGuid().ToString('N')
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $tempBase "hourly-execution-model-$testId"
$approvedState = Join-Path $env:USERPROFILE '.codex/automation-state'
$stateRoot = Join-Path $approvedState "hourly-execution-model-test-$testId"
$oldMutation = $env:TZG_MODEL_TEST_MUTATE_CARD
$scripts = @(
  'invoke-hourly-owner.ps1', 'hourly-owner-adapter.ps1', 'hourly-automation-lease.ps1', 'hourly-integration-lock.ps1',
  'private-path-acl.ps1', 'select-hourly-task.ps1', 'check-task-cards.ps1', 'experience-candidate-format.ps1', 'get-experience-risk-preflight.ps1',
  'set-task-automation-state.ps1', 'automation-finalize-commit.ps1', 'check-pending-whitespace.ps1'
)
$probe = @'
param([string]$Action, [string]$Route, [string]$RepositoryRoot, [string]$TaskId, [string]$RunId,
  [string]$Model, [string]$StateRoot, [int]$ResponsibilityTimeoutSeconds, [string]$PreflightResultPath, [string]$ResumeContextPath)
[IO.File]::WriteAllText((Join-Path $StateRoot 'model-probe.json'), (@{model=$Model; taskId=$TaskId; route=$Route} | ConvertTo-Json))
'{"status":"failed","detailCode":"model_probe_complete"}'
'@
$matcher = @'
param([string]$RepositoryRoot, [string]$TaskId)
& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'matcher-core.ps1') -RepositoryRoot $RepositoryRoot -TaskId $TaskId
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($env:TZG_MODEL_TEST_MUTATE_CARD -and $RepositoryRoot.Contains('.worktrees')) {
  $path = $env:TZG_MODEL_TEST_MUTATE_CARD
  [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($path).Replace('gpt-6.1-sol', 'gpt-6-astra'))
}
'@

try {
  foreach ($case in @('execute', 'review', 'default', 'maintenance', 'changed-card')) {
    $repo = Join-Path $testRoot $case
    $private = Join-Path $stateRoot $case
    [IO.Directory]::CreateDirectory((Join-Path $repo 'tools')) | Out-Null
    foreach ($name in $scripts) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $repo "tools/$name") }
    Write-Utf8 (Join-Path $repo 'tools/invoke-codex-candidate.ps1') $probe
    Write-Utf8 (Join-Path $repo 'tools/send-feishu-notification.ps1') "'{`"result`":`"TEST_ONLY`"}'"
    foreach ($name in @('send-decision', 'consume-reply')) { Write-Utf8 (Join-Path $repo "tools/feishu-decision-bridge/src/$name.mjs") 'throw new Error("Unexpected decision call in test");' }
    Write-Utf8 (Join-Path $repo '.gitignore') ".worktrees/`n"
    Write-Utf8 (Join-Path $repo '开发管理/经验库/风险索引.json') '{"schemaVersion":1,"experiences":[],"gates":[]}'
    $taskId = 'T-MODEL'
    $route = if ($case -ceq 'review') { 'codex_review' } else { 'codex_execute' }
    $meta = [ordered]@{
      schemaVersion=2; id=$taskId; title='model fixture'; priority='P2'; route=$route; owner='codex'
      domain='automation'; stage='implementation'; dispatchState='ready'; blockedBy=@(); stateReason='fixture'
      expectedPaths=@("开发管理/任务卡/$taskId.txt", "开发管理/任务归档/$taskId.txt", '开发管理/未通过审核清单.txt', '开发管理/AI合作沟通.txt', "开发管理/AI合作归档/$taskId-交接归档.txt")
      riskPreflight=@{explicitRefs=@();matched=@();gates=@()}; sourceBacklog='开发管理/任务列表/自动化任务.txt'
    }
    if ($case -cin @('execute', 'review', 'changed-card')) { $meta.executionModel = 'gpt-6.1-sol' }
    $cardPath = Join-Path $repo "开发管理/任务卡/$taskId.txt"
    [IO.Directory]::CreateDirectory((Split-Path -Parent $cardPath)) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $repo '开发管理/任务列表')) | Out-Null
    $queue = @('| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- | --- |')
    if ($case -cne 'maintenance') {
      $body = @("# $taskId · model fixture") + (@('来源与当前边界', '必查范围', '实施范围', '禁止项', '验证', '完成条件', '停止条件') | ForEach-Object { "## $_" })
      Write-Utf8 $cardPath ((@('---TASK-META---', ($meta | ConvertTo-Json -Depth 20), '---TASK-BODY---') + $body) -join "`n")
      $queue += "| $taskId | $route | codex | P2 | automation | implementation | model fixture | 开发管理/任务卡/$taskId.txt |"
      Write-Utf8 (Join-Path $repo $meta.sourceBacklog) (@('| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '| --- | --- | --- | --- | --- | --- | --- |', "| $taskId | P2 | codex | 已排队 | — | model fixture | 开发管理/任务卡/$taskId.txt |") -join "`n")
    }
    Write-Utf8 (Join-Path $repo '开发管理/当前任务队列.txt') ($queue -join "`n")
    if ($case -ceq 'changed-card') {
      Copy-Item -LiteralPath (Join-Path $repo 'tools/get-experience-risk-preflight.ps1') -Destination (Join-Path $repo 'tools/matcher-core.ps1')
      Write-Utf8 (Join-Path $repo 'tools/get-experience-risk-preflight.ps1') $matcher
    }
    Invoke-Git $repo @('init', '-q', '-b', 'master') | Out-Null
    Invoke-Git $repo @('config', 'user.name', 'Model Test') | Out-Null
    Invoke-Git $repo @('config', 'user.email', 'model-test@example.invalid') | Out-Null
    Invoke-Git $repo @('add', '-A') | Out-Null
    Invoke-Git $repo @('commit', '-qm', 'model fixture') | Out-Null
    $base = Invoke-Git $repo @('rev-parse', 'HEAD')
    $env:TZG_MODEL_TEST_MUTATE_CARD = if ($case -ceq 'changed-card') { $cardPath } else { $null }
    $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools/invoke-hourly-owner.ps1') -Owner codex -Action RunOnce -RepositoryRoot $repo -Model gpt-5.6-terra -StateRoot $private -OutputJson 2>&1)
    Assert-True ($LASTEXITCODE -eq 0 -and $output.Count -eq 1) "Shared entry failed: $($output -join "`n")"
    $terminal = $output[0] | ConvertFrom-Json
    $probePath = Join-Path $private 'model-probe.json'
    if ($case -ceq 'changed-card') {
      Assert-True ($terminal.detailCode -ceq 'hourly_task_changed_after_claim') 'Changed task model was not rejected'
      Assert-True (-not (Test-Path -LiteralPath $probePath)) 'Changed task reached the candidate process'
    } else {
      Assert-True ($terminal.status -ceq 'attention_required') "Unexpected terminal for ${case}: $($output[0])"
      Assert-True ($terminal.detailCode -ceq 'model_probe_complete') "Candidate probe was not reached: $($output[0])"
      $seen = Get-Content -Raw -LiteralPath $probePath | ConvertFrom-Json
      $expected = if ($case -cin @('execute', 'review')) { 'gpt-6.1-sol' } else { 'gpt-5.6-terra' }
      Assert-True ($seen.model -ceq $expected) "$case selected $($seen.model), expected $expected"
    }
    Assert-True ((Invoke-Git $repo @('rev-parse', 'HEAD')) -ceq $base) 'Model probe changed master'
  }

  # Both checkpoint digest implementations must bind the explicit model identically.
  $digests = @()
  foreach ($scriptName in @('invoke-hourly-owner.ps1', 'set-task-automation-state.ps1')) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $scriptName), [ref]$tokens, [ref]$errors)
    Assert-True (@($errors).Count -eq 0) "Cannot parse $scriptName"
    foreach ($name in @('Get-TaskAutomationInputs', 'Get-TaskContextDigest')) {
      foreach ($definition in @($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name}, $true))) { Invoke-Expression $definition.Extent.Text }
    }
    $metadata = $meta | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    $sol = Get-TaskContextDigest $metadata
    $metadata.executionModel = 'gpt-6-astra'
    $astra = Get-TaskContextDigest $metadata
    $metadata.PSObject.Properties.Remove('executionModel')
    $default = Get-TaskContextDigest $metadata
    Assert-True ($sol -cne $astra -and $sol -cne $default) 'Model changes did not invalidate decision context'
    $digests += "$sol|$astra|$default"
  }
  Assert-True ($digests[0] -ceq $digests[1]) 'Checkpoint model digests disagree between entry and transition'
  Write-Output 'test-hourly-execution-model: OK (execute, review, default, maintenance, changed-card, checkpoint digests)'
} finally {
  $env:TZG_MODEL_TEST_MUTATE_CARD = $oldMutation
  foreach ($pair in @(@($testRoot, $tempBase), @($stateRoot, [IO.Path]::GetFullPath($approvedState)))) {
    if (Test-Path -LiteralPath $pair[0]) {
      $resolved = (Resolve-Path -LiteralPath $pair[0]).Path
      Assert-True ($resolved.StartsWith($pair[1].TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Refusing cleanup outside test roots'
      Remove-Item -LiteralPath $resolved -Recurse -Force
    }
  }
}
