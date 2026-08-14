#requires -Version 7.0

$ErrorActionPreference = 'Stop'
function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -cne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Get-Digest { param([string]$Path) $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n"); [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text))).ToLowerInvariant() }
function Get-ContextDigest {
  param([object]$Metadata)
  $context = [ordered]@{
    id = [string]$Metadata.id; title = [string]$Metadata.title; priority = [string]$Metadata.priority
    route = [string]$Metadata.route; owner = [string]$Metadata.owner; domain = [string]$Metadata.domain; stage = [string]$Metadata.stage
    blockedBy = @($Metadata.blockedBy | ForEach-Object { [string]$_ }); expectedPaths = @($Metadata.expectedPaths | ForEach-Object { [string]$_ })
    sourceBacklog = [string]$Metadata.sourceBacklog
  }
  $json = $context | ConvertTo-Json -Compress -Depth 20
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
}
function Read-Meta { param([string]$Root) $text = [IO.File]::ReadAllText((Join-Path $Root '开发管理/任务卡/T-MAINT-01.txt')); ([regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---').Groups['json'].Value | ConvertFrom-Json -Depth 50) }
function Write-Card {
  param([string]$Root, [object]$Metadata)
  $body = @('# T-MAINT-01 · 维护型决策测试', '', '## 来源与当前边界', 'fixture', '## 必查范围', 'fixture', '## 实施范围', 'fixture', '## 禁止项', 'fixture', '## 验证', 'fixture', '## 完成条件', 'fixture', '## 停止条件', 'fixture') -join "`n"
  Write-Utf8 (Join-Path $Root '开发管理/任务卡/T-MAINT-01.txt') (@('---TASK-META---', ($Metadata | ConvertTo-Json -Depth 50), '---TASK-BODY---', $body) -join "`n")
}
function New-Fixture {
  param([string]$Root)
  [IO.Directory]::CreateDirectory((Join-Path $Root 'tools')) | Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $Root 'tools/check-task-cards.ps1')
  & git -C $Root init -q
  $meta = [ordered]@{
    schemaVersion = 1; id = 'T-MAINT-01'; title = '维护型决策测试'; priority = 'P1'; route = 'codex_execute'; owner = 'codex'
    domain = 'automation'; stage = 'decision'; dispatchState = 'blocked'; blockedBy = @(); stateReason = '等待负责人路线选择'
    expectedPaths = @('开发管理/任务卡/T-MAINT-01.txt', '开发管理/任务归档/T-MAINT-01.txt', '开发管理/当前任务队列.txt', '开发管理/任务列表/自动化任务.txt')
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  Write-Card $Root $meta
  Write-Utf8 (Join-Path $Root '开发管理/当前任务队列.txt') (@('# 当前任务队列', '', '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |', '|---|---|---|---|---|---|---|---|', '') -join "`n")
  Write-Utf8 (Join-Path $Root '开发管理/任务列表/自动化任务.txt') (@('# 自动化任务', '', '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |', '|---|---|---|---|---|---|---|', '| T-MAINT-01 | P1 | codex | 阻塞 | — | 维护型决策测试 | 开发管理/任务卡/T-MAINT-01.txt |', '') -join "`n")
  $meta
}
function New-PauseContext {
  param([string]$Root, [object]$Meta)
  $cardPath = Join-Path $Root '开发管理/任务卡/T-MAINT-01.txt'
  [ordered]@{
    schemaVersion = 1; kind = 'queue_maintenance'; taskId = 'T-MAINT-01'; sourceRunId = 'run-maint'; decisionId = 'DEC-20260814-QM0123456789AB'
    question = '选择哪条渲染路线？'; options = @(
      [ordered]@{ key = 'A'; label = '使用 Universal Renderer'; targetState = 'ready' },
      [ordered]@{ key = 'B'; label = '保留 Renderer2D 替代表现'; targetState = 'ready' },
      [ordered]@{ key = 'C'; label = '暂不批准'; targetState = 'blocked' }
    ); recommendedOption = 'A'; impactSummary = 'A/B 可恢复 ready，C 保持阻塞'
    plainSummary = [ordered]@{ situation = '任务只差路线选择'; impact = '选择后可确定准备'; action = '请选择 A、B 或 C' }
    allowCustomReply = $false; sourceCommit = ('a' * 40); sourceTaskDigest = Get-Digest $cardPath; taskContextDigest = Get-ContextDigest $Meta
    createdAt = '2026-08-14T00:00:00.0000000+00:00'
  }
}
function Invoke-State {
  param([string]$Root, [string]$Action, [object]$Context, [int[]]$AllowedExitCodes = @(0))
  $contextPath = Join-Path $Root "$Action.json"
  Write-Utf8 $contextPath ($Context | ConvertTo-Json -Compress -Depth 50)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $stateScript -Action $Action -RepositoryRoot $Root -TaskId 'T-MAINT-01' -ContextPath $contextPath 2>$null)
  Assert-True ($LASTEXITCODE -in $AllowedExitCodes) "$Action exit code mismatch"
  $output[0] | ConvertFrom-Json -Depth 50
}
function Invoke-Checker {
  param([string]$Root, [string[]]$Arguments)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $checkerScript -RepositoryRoot $Root @Arguments 2>&1)
  [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-maintenance-decision-test-$([Guid]::NewGuid().ToString('N'))"
$stateScript = Join-Path $PSScriptRoot 'set-task-automation-state.ps1'
$checkerScript = Join-Path $PSScriptRoot 'check-task-cards.ps1'
try {
  $readyRoot = Join-Path $tempRoot 'ready'
  $readyMeta = New-Fixture $readyRoot
  $pause = New-PauseContext $readyRoot $readyMeta
  $paused = Invoke-State $readyRoot 'PauseMaintenanceDecision' $pause
  Assert-Equal $paused.dispatchState 'pending_decision' 'PauseMaintenanceDecision did not pause'
  $pausedMeta = Read-Meta $readyRoot
  Assert-Equal $pausedMeta.automationDecision.status 'awaiting_reply' 'Public decision status mismatch'
  Assert-True (-not ($pausedMeta.PSObject.Properties.Name -contains 'automationCheckpoint')) 'Maintenance decision forged a checkpoint'
  $resolved = Invoke-State $readyRoot 'ResolveMaintenanceDecision' ([ordered]@{ schemaVersion = 1; kind = 'queue_maintenance'; taskId = 'T-MAINT-01'; decisionId = $pause.decisionId; optionKey = 'A'; source = 'feishu_card'; evidenceHash = ('b' * 64); resolvedAt = '2026-08-14T01:00:00.0000000+00:00'; preparedTaskDigest = Get-Digest (Join-Path $readyRoot '开发管理/任务卡/T-MAINT-01.txt') })
  Assert-Equal $resolved.dispatchState 'ready' 'Option A did not restore ready'
  $resolvedMeta = Read-Meta $readyRoot
  Assert-Equal $resolvedMeta.automationDecision.targetState 'ready' 'Resolved target was not recorded'
  Assert-True ([IO.File]::ReadAllText((Join-Path $readyRoot '开发管理/当前任务队列.txt')).Contains('T-MAINT-01', [StringComparison]::Ordinal)) 'Resolved ready task was not queued'

  $blockedRoot = Join-Path $tempRoot 'blocked'
  $blockedMeta = New-Fixture $blockedRoot
  $blockedPause = New-PauseContext $blockedRoot $blockedMeta
  $null = Invoke-State $blockedRoot 'PauseMaintenanceDecision' $blockedPause
  $blocked = Invoke-State $blockedRoot 'ResolveMaintenanceDecision' ([ordered]@{ schemaVersion = 1; kind = 'queue_maintenance'; taskId = 'T-MAINT-01'; decisionId = $blockedPause.decisionId; optionKey = 'C'; source = 'feishu_card'; evidenceHash = ('c' * 64); resolvedAt = '2026-08-14T01:00:00.0000000+00:00'; preparedTaskDigest = Get-Digest (Join-Path $blockedRoot '开发管理/任务卡/T-MAINT-01.txt') })
  Assert-Equal $blocked.dispatchState 'blocked' 'Option C did not keep blocked'

  $expiredRoot = Join-Path $tempRoot 'expired'
  $expiredMeta = New-Fixture $expiredRoot
  $expiredPause = New-PauseContext $expiredRoot $expiredMeta
  $null = Invoke-State $expiredRoot 'PauseMaintenanceDecision' $expiredPause
  $expired = Invoke-State $expiredRoot 'ExpireMaintenanceDecision' ([ordered]@{ schemaVersion = 1; kind = 'queue_maintenance'; taskId = 'T-MAINT-01'; decisionId = $expiredPause.decisionId; detailCode = 'maintenance_decision_expired'; terminatedAt = '2026-08-21T00:00:01.0000000+00:00' })
  Assert-Equal $expired.dispatchState 'blocked' 'Expired decision did not return blocked'
  Assert-Equal (Read-Meta $expiredRoot).automationDecision.status 'expired' 'Expired public status mismatch'

  $invalidRoot = Join-Path $tempRoot 'invalid'
  $invalidMeta = New-Fixture $invalidRoot
  $invalidPause = New-PauseContext $invalidRoot $invalidMeta
  $null = Invoke-State $invalidRoot 'PauseMaintenanceDecision' $invalidPause
  $invalidCard = Read-Meta $invalidRoot
  $invalidCard | Add-Member -NotePropertyName automationCheckpoint -NotePropertyValue ([ordered]@{ decisionId = 'DEC-20260814-OTHER' }) -Force
  Write-Card $invalidRoot $invalidCard
  $invalid = Invoke-Checker $invalidRoot @()
  Assert-True ($invalid.ExitCode -ne 0 -and $invalid.Output -match 'mutually exclusive') 'Checker accepted both automation projections'
  Write-Output 'test-queue-maintenance-decision: PASS'
} finally {
  if (Test-Path -LiteralPath $tempRoot) {
    $resolvedRoot = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolvedRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'Refusing to remove non-temp fixture'
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
  }
}
