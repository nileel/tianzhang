#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -ne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" } }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }

function New-Card {
  param([string]$Id, [string]$Route, [string]$Owner, [string]$State, [string]$Title)
  $meta = [ordered]@{
    schemaVersion = 1; id = $Id; title = $Title; priority = 'P1'; route = $Route; owner = $Owner
    domain = 'automation'; stage = 'implementation'; dispatchState = $State; blockedBy = @(); stateReason = 'Codex 复审不通过，等待返工决定'
    expectedPaths = @("开发管理/任务卡/$Id.txt", "开发管理/任务归档/$Id.txt", '开发管理/当前任务队列.txt', '开发管理/任务列表/自动化任务.txt', '开发管理/未通过审核清单.txt')
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $body = @("# $Id · $Title", '', '## 来源与当前边界', 'fixture', '## 必查范围', 'fixture', '## 实施范围', 'fixture', '## 禁止项', 'fixture', '## 验证', 'fixture', '## 完成条件', 'fixture', '## 停止条件', 'fixture') -join "`n"
  @('---TASK-META---', ($meta | ConvertTo-Json -Depth 20), '---TASK-BODY---', $body) -join "`n"
}

function Read-Meta {
  param([string]$Path)
  $text = [IO.File]::ReadAllText($Path)
  [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---').Groups['json'].Value | ConvertFrom-Json -Depth 30
}

function Get-TextDigest {
  param([string]$Path)
  $text = [IO.File]::ReadAllText($Path).TrimStart([char]0xFEFF).Replace("`r`n", "`n").Replace("`r", "`n")
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text))).ToLowerInvariant()
}

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

function New-Context {
  param([string]$Id, [string]$Option, [int]$QueueIndex)
  $path = Join-Path $root "开发管理/任务卡/$Id.txt"
  $meta = Read-Meta $path
  [ordered]@{
    schemaVersion = 1; kind = 'review_rework'; taskId = $Id; decisionId = "DEC-20260803-$($Id.Replace('-', ''))"; optionKey = $Option
    queueIndex = $QueueIndex; taskDigest = Get-TextDigest $path; taskContextDigest = Get-ContextDigest $meta
    reviewCommit = ('a' * 40); reviewEntryDigest = ('b' * 64); replyEvidenceHash = ('c' * 64)
  }
}

function Invoke-State {
  param([string]$TaskId, [object]$Context, [int[]]$Allowed = @(0))
  $contextPath = Join-Path $root "$TaskId-$($Context.optionKey).json"
  Write-Utf8 $contextPath ($Context | ConvertTo-Json -Compress -Depth 20)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Action RequeueReview -RepositoryRoot $root -TaskId $TaskId -ContextPath $contextPath 2>&1)
  Assert-True ($LASTEXITCODE -in $Allowed) "RequeueReview exit code was invalid: $(@($output) -join '; ')"
  $output[0] | ConvertFrom-Json -Depth 30
}

$root = Join-Path ([IO.Path]::GetTempPath()) "tzg-review-rework-test-$([Guid]::NewGuid().ToString('N'))"
$scriptPath = Join-Path $PSScriptRoot 'set-task-automation-state.ps1'
try {
  [IO.Directory]::CreateDirectory((Join-Path $root 'tools')) | Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $root 'tools\check-task-cards.ps1')
  & git -C $root init -q
  & git -C $root config user.name 'Review Rework Test'
  & git -C $root config user.email 'review-rework@example.invalid'

  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-A.txt') (New-Card 'TASK-A' 'external_execute' 'deepseek' 'blocked' '返工 A')
  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-B.txt') (New-Card 'TASK-B' 'external_execute' 'deepseek' 'blocked' '返工 B')
  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-C.txt') (New-Card 'TASK-C' 'external_execute' 'deepseek' 'blocked' '保持阻塞 C')
  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-Z.txt') (New-Card 'TASK-Z' 'codex_execute' 'codex' 'ready' '已有任务 Z')
  $queue = @(
    '# 当前任务队列', '',
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |',
    '|---|---|---|---|---|---|---|---|',
    '| TASK-Z | codex_execute | codex | P1 | automation | implementation | 已有任务 Z | 开发管理/任务卡/TASK-Z.txt |', ''
  ) -join "`n"
  $backlog = @(
    '# 自动化任务', '',
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |',
    '|---|---|---|---|---|---|---|',
    '| TASK-A | P1 | deepseek | 阻塞 | — | 返工 A | 开发管理/任务卡/TASK-A.txt |',
    '| TASK-B | P1 | deepseek | 阻塞 | — | 返工 B | 开发管理/任务卡/TASK-B.txt |',
    '| TASK-C | P1 | deepseek | 阻塞 | — | 保持阻塞 C | 开发管理/任务卡/TASK-C.txt |',
    '| TASK-Z | P1 | codex | 已排队 | — | 已有任务 Z | 开发管理/任务卡/TASK-Z.txt |', ''
  ) -join "`n"
  Write-Utf8 (Join-Path $root '开发管理\当前任务队列.txt') $queue
  Write-Utf8 (Join-Path $root '开发管理\任务列表\自动化任务.txt') $backlog
  Write-Utf8 (Join-Path $root '开发管理\未通过审核清单.txt') "# 未通过审核清单`n"

  $a = Invoke-State 'TASK-A' (New-Context 'TASK-A' 'A' 0)
  Assert-Equal $a.dispatchState 'ready' 'A did not restore ready'
  $aMeta = Read-Meta (Join-Path $root '开发管理\任务卡\TASK-A.txt')
  Assert-Equal $aMeta.route 'external_execute' 'A route changed incorrectly'
  Assert-Equal $aMeta.owner 'deepseek' 'A owner changed incorrectly'

  $b = Invoke-State 'TASK-B' (New-Context 'TASK-B' 'B' 1)
  Assert-Equal $b.dispatchState 'ready' 'B did not restore ready'
  $bMeta = Read-Meta (Join-Path $root '开发管理\任务卡\TASK-B.txt')
  Assert-Equal $bMeta.route 'codex_execute' 'B route was not transferred to Codex execution'
  Assert-Equal $bMeta.owner 'codex' 'B owner was not transferred to Codex'

  $queueText = [IO.File]::ReadAllText((Join-Path $root '开发管理\当前任务队列.txt'))
  Assert-True ($queueText.IndexOf('TASK-A', [StringComparison]::Ordinal) -lt $queueText.IndexOf('TASK-B', [StringComparison]::Ordinal)) 'A queue position was not restored'
  Assert-True ($queueText.IndexOf('TASK-B', [StringComparison]::Ordinal) -lt $queueText.IndexOf('TASK-Z', [StringComparison]::Ordinal)) 'B queue position was not restored'

  $cContext = New-Context 'TASK-C' 'C' 0
  $c = Invoke-State 'TASK-C' $cContext @(1)
  Assert-Equal $c.status 'failed' 'C incorrectly entered the requeue path'
  Assert-Equal (Read-Meta (Join-Path $root '开发管理\任务卡\TASK-C.txt')).dispatchState 'blocked' 'C did not remain blocked'

  $stale = New-Context 'TASK-C' 'A' 0
  $stale.taskDigest = 'd' * 64
  $staleResult = Invoke-State 'TASK-C' $stale @(1)
  Assert-Equal $staleResult.status 'failed' 'Stale task evidence was accepted'
  Assert-Equal (Read-Meta (Join-Path $root '开发管理\任务卡\TASK-C.txt')).dispatchState 'blocked' 'Stale evidence changed the task'

  $integrationRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-review-rework-integration-$([Guid]::NewGuid().ToString('N'))"
  $stateRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-review-rework-state-$([Guid]::NewGuid().ToString('N'))"
  try {
    [IO.Directory]::CreateDirectory((Join-Path $integrationRoot 'tools')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $integrationRoot 'tools\check-task-cards.ps1')
    & git -C $integrationRoot init -q -b master
    & git -C $integrationRoot config user.name 'Review Rework Integration Test'
    & git -C $integrationRoot config user.email 'review-rework-integration@example.invalid'
    & git -C $integrationRoot config core.autocrlf true
    Write-Utf8 (Join-Path $integrationRoot '.gitignore') ".worktrees/`n"
    $integrationTask = 'TASK-INTEGRATION'
    Write-Utf8 (Join-Path $integrationRoot "开发管理\任务卡\$integrationTask.txt") (New-Card $integrationTask 'codex_review' 'codex' 'ready' '集成返工')
    $integrationQueue = @(
      '# 当前任务队列', '',
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |',
      '|---|---|---|---|---|---|---|---|',
      "| $integrationTask | codex_review | codex | P1 | automation | implementation | 集成返工 | 开发管理/任务卡/$integrationTask.txt |", ''
    ) -join "`n"
    $integrationBacklog = @(
      '# 自动化任务', '',
      '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |',
      '|---|---|---|---|---|---|---|',
      "| $integrationTask | P1 | codex | 已排队 | — | 集成返工 | 开发管理/任务卡/$integrationTask.txt |", ''
    ) -join "`n"
    Write-Utf8 (Join-Path $integrationRoot '开发管理\当前任务队列.txt') $integrationQueue
    Write-Utf8 (Join-Path $integrationRoot '开发管理\任务列表\自动化任务.txt') $integrationBacklog
    Write-Utf8 (Join-Path $integrationRoot '开发管理\未通过审核清单.txt') "# 未通过审核清单`n`n## 当前未通过/待复核项`n"
    & git -C $integrationRoot add -A
    & git -C $integrationRoot commit -q -m 'feat(TASK-INTEGRATION): reviewed result'
    $reviewedCommit = [string](& git -C $integrationRoot rev-parse HEAD)

    Write-Utf8 (Join-Path $integrationRoot "开发管理\任务卡\$integrationTask.txt") (New-Card $integrationTask 'external_execute' 'deepseek' 'blocked' '集成返工')
    $blockedQueue = @(
      '# 当前任务队列', '',
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |',
      '|---|---|---|---|---|---|---|---|', ''
    ) -join "`n"
    $blockedBacklog = @(
      '# 自动化任务', '',
      '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |',
      '|---|---|---|---|---|---|---|',
      "| $integrationTask | P1 | deepseek | 阻塞 | — | 集成返工 | 开发管理/任务卡/$integrationTask.txt |", ''
    ) -join "`n"
    $reviewList = @(
      '# 未通过审核清单', '', '## 当前未通过/待复核项', '',
      "### $integrationTask · 集成返工", '',
      "- 审核对象：正式提交 ``$reviewedCommit``；结论：不通过。",
      '- 最窄返工：只修直接问题。', '', '## 复审路由', '', '按审核入口复审。', ''
    ) -join "`n"
    Write-Utf8 (Join-Path $integrationRoot '开发管理\当前任务队列.txt') $blockedQueue
    Write-Utf8 (Join-Path $integrationRoot '开发管理\任务列表\自动化任务.txt') $blockedBacklog
    Write-Utf8 (Join-Path $integrationRoot '开发管理\未通过审核清单.txt') $reviewList
    & git -C $integrationRoot add -A
    & git -C $integrationRoot commit -q -m 'review(TASK-INTEGRATION): block result'
    $reviewCommit = [string](& git -C $integrationRoot rev-parse HEAD)

    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'))
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
    Assert-Equal @($errors).Count 0 'Shared owner entry did not parse for integration test'
    foreach ($functionAst in @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false))) {
      Invoke-Expression $functionAst.Extent.Text
    }
    . (Join-Path $PSScriptRoot 'private-path-acl.ps1')
    . (Join-Path $PSScriptRoot 'hourly-integration-lock.ps1')
    $script:root = $integrationRoot
    $script:effectiveStateRoot = $stateRoot
    $script:Owner = 'codex'
    $script:IntegrationLockTimeoutSeconds = 10
    $script:runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
    $script:checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
    $script:taskStatePath = Join-Path $PSScriptRoot 'set-task-automation-state.ps1'
    $script:finalizerPath = Join-Path $PSScriptRoot 'automation-finalize-commit.ps1'
    $script:whitespacePath = Join-Path $PSScriptRoot 'check-pending-whitespace.ps1'
    $script:notificationPath = Join-Path $PSScriptRoot 'send-feishu-notification.ps1'
    function Invoke-ReviewReworkNotification { param([object]$Result) 'test_notification' }
    function Invoke-Runtime {
      param(
        [string]$RuntimeAction,
        [hashtable]$Parameters = @{},
        [int[]]$AllowedExitCodes = @(0)
      )
      if ($RuntimeAction -cne 'Show') { throw "Unexpected runtime action in review-rework integration test: $RuntimeAction" }
      [pscustomobject]@{
        status = 'OK'
        activeTaskIds = @()
        integrationLockStatus = 'none'
        state = [pscustomobject]@{
          schemaVersion = 5
          runs = [pscustomobject]@{ codex = $null; deepseek = $null }
        }
      }
    }

    $runEvidence = [pscustomobject]@{
      taskId = $integrationTask
      route = 'codex_review'
      candidateResult = [pscustomobject]@{ expectedTransition = 'blocked' }
    }
    $outcomeEvidence = [pscustomobject]@{ formalHead = $reviewCommit; reviewedCommit = $reviewedCommit; reviewQueueIndex = 0 }
    $decision = New-ReviewReworkDecisionContext -Run $runEvidence -Outcome $outcomeEvidence
    Assert-Equal $decision.recommendedOption 'A' 'Review decision did not recommend DeepSeek rework'
    Assert-Equal @($decision.options).Count 3 'Review decision did not expose the fixed three options'
    Assert-True ($decision.PSObject.Properties.Name -notcontains 'checkpointCommit') 'Review decision incorrectly reused checkpoint evidence'
    $decisionPath = Write-ReviewReworkRecord $decision
    $answer = [ordered]@{
      status = 'answered'; taskId = $integrationTask; context = $decision; contextPath = $decisionPath
      reply = [ordered]@{ result = 'OPTION_ACCEPTED'; optionKey = 'A'; source = 'feishu_card'; evidenceHash = ('e' * 64) }
    }
    $applied = Apply-AnsweredReviewRework $answer
    $appliedRecord = [IO.File]::ReadAllText($decisionPath) | ConvertFrom-Json -Depth 30
    $appliedEvidence = $appliedRecord | ConvertTo-Json -Compress -Depth 30
    if ([string]$applied.status -cne 'review_rework_requeued' -and $appliedRecord.PSObject.Properties.Name -contains 'evidenceWorktree') {
      $failedWorktree = Join-Path $integrationRoot ([string]$appliedRecord.evidenceWorktree)
      $failedStatus = if (Test-Path -LiteralPath $failedWorktree) { @(& git -C $failedWorktree status --short 2>&1) -join '; ' } else { 'missing' }
      $failedCheck = if (Test-Path -LiteralPath $failedWorktree) { @(& git -C $failedWorktree -c core.quotepath=false diff --cached --check 2>&1) -join '; ' } else { 'missing' }
      $appliedEvidence = "$appliedEvidence; worktreeStatus=$failedStatus; diffCheck=$failedCheck"
    }
    $appliedDetail = if ($applied.PSObject.Properties.Name -contains 'detailCode') { [string]$applied.detailCode } else { 'none' }
    Assert-Equal $applied.status 'review_rework_requeued' "Integrated A decision did not requeue: $appliedDetail; evidence=$appliedEvidence"
    Assert-Equal $applied.cleanup 'cleaned' 'Integrated A decision did not clean its exact worktree'
    Assert-Equal $applied.notification 'test_notification' 'Integrated A decision did not confirm the transition'
    $integratedMeta = Read-Meta (Join-Path $integrationRoot "开发管理\任务卡\$integrationTask.txt")
    Assert-Equal $integratedMeta.route 'external_execute' 'Integrated A decision changed route incorrectly'
    Assert-Equal $integratedMeta.owner 'deepseek' 'Integrated A decision changed owner incorrectly'
    Assert-Equal $integratedMeta.dispatchState 'ready' 'Integrated A decision did not restore ready'
    $consumed = [IO.File]::ReadAllText($decisionPath) | ConvertFrom-Json -Depth 30
    Assert-Equal $consumed.status 'consumed' 'Integrated decision evidence was not consumed'
    Assert-Equal $consumed.formalHead $applied.formalHead 'Integrated decision did not bind its formal commit'
    $decisionWorktree = Join-Path $integrationRoot ".worktrees\automation\decisions\$(([string]$decision.decisionId).ToLowerInvariant())"
    Assert-True (-not (Test-Path -LiteralPath $decisionWorktree)) 'Integrated decision worktree remained after exact cleanup'

    $duplicate = Apply-AnsweredReviewRework $answer
    Assert-Equal $duplicate.status 'review_rework_already_consumed' 'Repeated decision was applied twice'
    Assert-Equal ([string](& git -C $integrationRoot rev-parse HEAD)) $applied.formalHead 'Repeated decision changed master'
  } finally {
    foreach ($candidate in @($integrationRoot, $stateRoot)) {
      if (Test-Path -LiteralPath $candidate) {
        $resolvedCandidate = [IO.Path]::GetFullPath($candidate)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedCandidate.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing unsafe integrated review-rework cleanup' }
        Remove-Item -LiteralPath $resolvedCandidate -Recurse -Force
      }
    }
  }

  Write-Output 'test-review-rework-decision: OK'
} finally {
  if (Test-Path -LiteralPath $root) {
    $resolved = [IO.Path]::GetFullPath($root)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing unsafe review-rework test cleanup' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
