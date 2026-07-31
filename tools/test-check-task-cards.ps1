#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  $parent = Split-Path -Parent $Path
  [IO.Directory]::CreateDirectory($parent) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-Metadata {
  param(
    [string]$Id = 'T-READY-01',
    [string]$Title = '合法 ready 卡',
    [string]$Priority = 'P2',
    [string]$Route = 'codex_execute',
    [string]$Owner = 'codex',
    [string]$Domain = 'battlesim',
    [string]$Stage = 'implementation',
    [string]$DispatchState = 'ready',
    [string[]]$BlockedBy = @(),
    [string]$StateReason = $null
  )
  [ordered]@{
    schemaVersion = 2
    id = $Id
    title = $Title
    priority = $Priority
    route = $Route
    owner = $Owner
    domain = $Domain
    stage = $Stage
    dispatchState = $DispatchState
    blockedBy = $BlockedBy
    stateReason = $StateReason
    expectedPaths = @(
      'simulations/BattleSim/Combat.cs'
      '开发管理/任务列表/数值与战斗任务.txt'
      '开发管理/当前任务队列.txt'
      "开发管理/任务卡/$Id.txt"
      "开发管理/任务归档/$Id.txt"
    )
    workerPaths = @(
      'simulations/BattleSim/Combat.cs'
    )
    coordinatorPaths = @(
      '开发管理/任务列表/数值与战斗任务.txt'
      '开发管理/当前任务队列.txt'
      "开发管理/任务卡/$Id.txt"
      "开发管理/任务归档/$Id.txt"
    )
    sourceBacklog = '开发管理/任务列表/数值与战斗任务.txt'
  }
}

function Get-CardText {
  param([hashtable]$Metadata, [string[]]$Headings = @('来源与当前边界', '必查范围', '实施范围', '禁止项', '验证', '完成条件', '停止条件'))
  $json = $Metadata | ConvertTo-Json -Depth 10
  $body = @("# $($Metadata.id) · $($Metadata.title)") + ($Headings | ForEach-Object { "## $_" })
  @('---TASK-META---', $json, '---TASK-BODY---') + $body -join "`n"
}

function Copy-Metadata {
  param([hashtable]$Metadata)
  $copy = [ordered]@{}
  foreach ($key in $Metadata.Keys) {
    if ($Metadata[$key] -is [array]) {
      $copy[$key] = @($Metadata[$key])
    } else {
      $copy[$key] = $Metadata[$key]
    }
  }
  $copy
}

function Set-Card {
  param([string]$Root, [hashtable]$Metadata, [string]$FileName = "$($Metadata.id).txt", [string[]]$Headings)
  $args = @{ Metadata = $Metadata }
  if ($PSBoundParameters.ContainsKey('Headings')) { $args.Headings = $Headings }
  Write-Utf8 (Join-Path $Root "开发管理/任务卡/$FileName") (Get-CardText @args)
}

function Set-Archive {
  param([string]$Root, [hashtable]$Metadata)
  Write-Utf8 (Join-Path $Root "开发管理/任务归档/$($Metadata.id).txt") (Get-CardText -Metadata $Metadata)
}

function Set-Queue {
  param([string]$Root, [object[]]$Cards)
  $lines = @('| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |')
  $lines += '| --- | --- | --- | --- | --- | --- | --- | --- |'
  foreach ($card in $Cards) {
    $lines += "| $($card.id) | $($card.route) | $($card.owner) | $($card.priority) | $($card.domain) | $($card.stage) | $($card.title) | 开发管理/任务卡/$($card.id).txt |"
  }
  Write-Utf8 (Join-Path $Root '开发管理/当前任务队列.txt') ($lines -join "`n")
}

function Set-Backlog {
  param([string]$Root, [object[]]$Cards)
  $projection = @{ ready = '已排队'; blocked = '阻塞'; frozen = '冻结'; pending_decision = '待决定'; waiting_reply = '等待回复' }
  $lines = @('| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |')
  $lines += '| --- | --- | --- | --- | --- | --- | --- |'
  foreach ($card in $Cards) {
    $blocked = if ($card.blockedBy.Count) { $card.blockedBy -join '、' } else { '—' }
    $lines += "| $($card.id) | $($card.priority) | $($card.owner) | $($projection[$card.dispatchState]) | $blocked | $($card.title) | 开发管理/任务卡/$($card.id).txt |"
  }
  Write-Utf8 (Join-Path $Root '开发管理/任务列表/数值与战斗任务.txt') ($lines -join "`n")
}

function New-Fixture {
  param([string]$Root)
  $ready = Get-Metadata
  $blocked = Get-Metadata -Id 'T-BLOCKED-01' -Title '合法 blocked 卡' -DispatchState 'blocked' -StateReason '等待外部输入'
  Set-Card $Root $ready
  Set-Card $Root $blocked
  Set-Queue $Root @($ready)
  Set-Backlog $Root @($ready, $blocked)
  [pscustomobject]@{ Ready = $ready; Blocked = $blocked }
}

function Invoke-Checker {
  param([string]$Root, [string[]]$Overrides = @())
  $checker = Join-Path $PSScriptRoot 'check-task-cards.ps1'
  $output = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -RepositoryRoot $Root @Overrides 2>&1 | Out-String)
  [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Assert-Success {
  param([string]$Name, [string]$Root)
  $result = Invoke-Checker $Root
  Assert-True ($result.ExitCode -eq 0) "$Name should pass, got: $($result.Output)"
  Assert-True ($result.Output -match 'check-task-cards: OK \(cards=2 ready=1\)') "$Name did not emit success output"
}

function Assert-Failure {
  param([string]$Name, [string]$Root, [string]$Expected)
  $result = Invoke-Checker $Root
  Assert-True ($result.ExitCode -ne 0) "$Name should fail"
  Assert-True ($result.Output -match [regex]::Escape($Expected)) "$Name missing '$Expected': $($result.Output)"
}

function Assert-OverrideFailure {
  param([string]$Name, [string]$Root, [string]$Expected, [string[]]$Overrides)
  $result = Invoke-Checker $Root $Overrides
  Assert-True ($result.ExitCode -ne 0) "$Name should fail"
  Assert-True ($result.Output -match [regex]::Escape($Expected)) "$Name missing '$Expected': $($result.Output)"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("check-task-cards-test-" + [guid]::NewGuid().ToString('N'))
try {
  [IO.Directory]::CreateDirectory($tempRoot) | Out-Null
  $fixture = New-Fixture $tempRoot
  Assert-Success 'canonical fixture' $tempRoot
  $globalJsonResult = Invoke-Checker $tempRoot @('-OutputJson')
  Assert-True ($globalJsonResult.ExitCode -eq 0) "global JSON evidence should pass: $($globalJsonResult.Output)"
  $globalJson = $globalJsonResult.Output | ConvertFrom-Json
  Assert-True ($globalJson.status -ceq 'ok') 'global JSON status mismatch'
  Assert-True ($globalJson.cardCount -eq 2) 'global JSON cardCount mismatch'
  Assert-True ($globalJson.readyCount -eq 1) 'global JSON readyCount mismatch'
  Assert-True ($null -eq $globalJson.taskId) 'global JSON invented taskId'
  Assert-True ($null -eq $globalJson.taskState) 'global JSON invented taskState'
  Assert-True ($null -eq $globalJson.postcondition) 'global JSON invented postcondition'

  $executionDispatch = Invoke-Checker $tempRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'CodexDispatchReady',
    '-ExpectedRoute', 'codex_execute',
    '-OutputJson'
  )
  Assert-True ($executionDispatch.ExitCode -eq 0) "ready execution should pass CodexDispatchReady: $($executionDispatch.Output)"
  $executionEvidence = $executionDispatch.Output | ConvertFrom-Json
  Assert-True ($executionEvidence.taskId -ceq 'T-READY-01') 'execution evidence taskId mismatch'
  Assert-True ($executionEvidence.taskState -ceq 'ready') 'execution evidence taskState mismatch'
  Assert-True ($executionEvidence.postcondition -ceq 'CodexDispatchReady') 'execution evidence postcondition mismatch'

  $reviewDispatchRoot = Join-Path $tempRoot 'review-dispatch'
  $reviewDispatchFixture = New-Fixture $reviewDispatchRoot
  $reviewDispatchCard = Copy-Metadata $reviewDispatchFixture.Ready
  $reviewDispatchCard.route = 'codex_review'
  Set-Card $reviewDispatchRoot $reviewDispatchCard
  Set-Queue $reviewDispatchRoot @($reviewDispatchCard)
  Set-Backlog $reviewDispatchRoot @($reviewDispatchCard, $reviewDispatchFixture.Blocked)
  $reviewDispatch = Invoke-Checker $reviewDispatchRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'CodexDispatchReady',
    '-ExpectedRoute', 'codex_review',
    '-OutputJson'
  )
  Assert-True ($reviewDispatch.ExitCode -eq 0) "ready review should pass CodexDispatchReady: $($reviewDispatch.Output)"
  Assert-True (($reviewDispatch.Output | ConvertFrom-Json).taskState -ceq 'ready') 'review evidence taskState mismatch'

  $externalDispatchRoot = Join-Path $tempRoot 'external-dispatch'
  $externalDispatchFixture = New-Fixture $externalDispatchRoot
  $externalDispatchCard = Copy-Metadata $externalDispatchFixture.Ready
  $externalDispatchCard.route = 'external_execute'
  $externalDispatchCard.owner = 'deepseek'
  Set-Card $externalDispatchRoot $externalDispatchCard
  Set-Queue $externalDispatchRoot @($externalDispatchCard)
  Set-Backlog $externalDispatchRoot @($externalDispatchCard, $externalDispatchFixture.Blocked)
  $externalDispatch = Invoke-Checker $externalDispatchRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'ExternalDispatchReady',
    '-ExpectedOwner', 'deepseek',
    '-OutputJson'
  )
  Assert-True ($externalDispatch.ExitCode -eq 0) "ready external task should pass ExternalDispatchReady: $($externalDispatch.Output)"
  $externalDispatchEvidence = $externalDispatch.Output | ConvertFrom-Json
  Assert-True ($externalDispatchEvidence.taskState -ceq 'ready') 'external dispatch taskState mismatch'
  Assert-True ($externalDispatchEvidence.postcondition -ceq 'ExternalDispatchReady') 'external dispatch postcondition mismatch'
  Assert-True (
    @($externalDispatchEvidence.expectedPaths) -ccontains 'simulations/BattleSim/Combat.cs'
  ) 'external dispatch evidence omitted expectedPaths'

  $externalOwnerMismatch = Invoke-Checker $externalDispatchRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'ExternalDispatchReady',
    '-ExpectedOwner', 'claude'
  )
  Assert-True ($externalOwnerMismatch.ExitCode -ne 0) 'ExternalDispatchReady accepted the wrong owner'
  Assert-True (
    $externalOwnerMismatch.Output -match 'ExternalDispatchReady requires route=external_execute owner=claude dispatchState=ready'
  ) "external owner mismatch diagnostic is missing: $($externalOwnerMismatch.Output)"

  $missingExpectedOwner = Invoke-Checker $externalDispatchRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'ExternalDispatchReady'
  )
  Assert-True ($missingExpectedOwner.ExitCode -ne 0) 'ExternalDispatchReady without ExpectedOwner should fail'
  Assert-True (
    $missingExpectedOwner.Output -match 'ExpectedOwner is required'
  ) "missing ExpectedOwner diagnostic mismatch: $($missingExpectedOwner.Output)"

  foreach ($dispatchFailure in @(
      @{
        Name = 'execution card under review route'
        Root = $tempRoot
        TaskId = 'T-READY-01'
        ExpectedRoute = 'codex_review'
        Expected = 'CodexDispatchReady requires route=codex_review owner=codex dispatchState=ready'
      },
      @{
        Name = 'non-ready execution card'
        Root = $tempRoot
        TaskId = 'T-BLOCKED-01'
        ExpectedRoute = 'codex_execute'
        Expected = 'CodexDispatchReady requires route=codex_execute owner=codex dispatchState=ready'
      },
      @{
        Name = 'case-mismatched execution card'
        Root = $tempRoot
        TaskId = 't-ready-01'
        ExpectedRoute = 'codex_execute'
        Expected = 'TaskId case mismatch'
      },
      @{
        Name = 'missing execution card'
        Root = $tempRoot
        TaskId = 'T-MISSING-01'
        ExpectedRoute = 'codex_execute'
        Expected = 'CodexDispatchReady requires route=codex_execute owner=codex dispatchState=ready'
      }
    )) {
    $dispatchResult = Invoke-Checker $dispatchFailure.Root @(
      '-TaskId', $dispatchFailure.TaskId,
      '-Postcondition', 'CodexDispatchReady',
      '-ExpectedRoute', $dispatchFailure.ExpectedRoute
    )
    Assert-True ($dispatchResult.ExitCode -ne 0) "$($dispatchFailure.Name) should fail CodexDispatchReady"
    Assert-True ($dispatchResult.Output -match [regex]::Escape($dispatchFailure.Expected)) "$($dispatchFailure.Name) missing diagnostic: $($dispatchResult.Output)"
  }

  $missingExpectedRoute = Invoke-Checker $tempRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'CodexDispatchReady'
  )
  Assert-True ($missingExpectedRoute.ExitCode -ne 0) 'CodexDispatchReady without ExpectedRoute should fail'
  Assert-True ($missingExpectedRoute.Output -match 'ExpectedRoute is required') "missing ExpectedRoute diagnostic mismatch: $($missingExpectedRoute.Output)"
  Assert-OverrideFailure 'rooted task-card override' $tempRoot 'invalid repository-relative path in TaskCardRoot' @('-TaskCardRoot', 'C:/outside')
  Assert-OverrideFailure 'traversal queue override' $tempRoot 'invalid repository-relative path in QueuePath' @('-QueuePath', '../outside.txt')
  Assert-OverrideFailure 'traversal backlog override' $tempRoot 'invalid repository-relative path in BacklogRoot' @('-BacklogRoot', '开发管理/../outside')
  $separatorRoot = Join-Path $tempRoot 'markdown-separators'
  $separatorFixture = New-Fixture $separatorRoot
  Assert-Success 'markdown table separator rows' $separatorRoot

  $cases = @(
    @{ Name = 'missing task-card directory'; Expected = 'task-card directory'; Change = { param($root, $f) Remove-Item -LiteralPath (Join-Path $root '开发管理/任务卡') -Recurse -Force } },
    @{ Name = 'invalid UTF-8'; Expected = 'invalid UTF-8'; Change = { param($root, $f) [IO.File]::WriteAllBytes((Join-Path $root '开发管理/任务卡/T-READY-01.txt'), [byte[]](0xFF, 0xFE)) } },
    @{ Name = 'invalid JSON'; Expected = 'invalid JSON'; Change = { param($root, $f) Write-Utf8 (Join-Path $root '开发管理/任务卡/T-READY-01.txt') "---TASK-META---`n{ bad }`n---TASK-BODY---" } },
    @{ Name = 'duplicate metadata delimiter'; Expected = 'metadata delimiter'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务卡/T-READY-01.txt'; Write-Utf8 $path ((Get-Content -Raw $path) + "`n---TASK-META---") } },
    @{ Name = 'missing metadata delimiter'; Expected = 'metadata delimiter'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务卡/T-READY-01.txt'; Write-Utf8 $path ((Get-Content -Raw $path).Replace('---TASK-META---', '---META---')) } },
    @{ Name = 'missing body delimiter'; Expected = 'body delimiter'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务卡/T-READY-01.txt'; Write-Utf8 $path ((Get-Content -Raw $path).Replace('---TASK-BODY---', '---BODY---')) } },
    @{ Name = 'duplicate body delimiter'; Expected = 'body delimiter'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务卡/T-READY-01.txt'; Write-Utf8 $path ((Get-Content -Raw $path) + "`n---TASK-BODY---") } },
    @{ Name = 'missing required metadata field'; Expected = 'missing metadata field'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.Remove('domain'); Set-Card $root $m } },
    @{ Name = 'filename ID mismatch'; Expected = 'filename/id mismatch'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.id = 'T-OTHER-01'; Set-Card $root $m -FileName 'T-READY-01.txt' } },
    @{ Name = 'duplicate ID'; Expected = 'duplicate card ID'; Change = { param($root, $f) $m = Copy-Metadata $f.Blocked; $m.id = 'T-READY-01'; Set-Card $root $m -FileName 'T-BLOCKED-01.txt' } },
    @{ Name = 'illegal enums'; Expected = 'invalid route'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.route = 'bad'; Set-Card $root $m } },
    @{ Name = 'illegal schema version'; Expected = 'illegal schemaVersion'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.schemaVersion = 1; Set-Card $root $m } },
    @{ Name = 'illegal priority'; Expected = 'invalid priority'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.priority = 'P4'; Set-Card $root $m } },
    @{ Name = 'illegal owner'; Expected = 'invalid owner'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.owner = 'bad'; Set-Card $root $m } },
    @{ Name = 'illegal domain'; Expected = 'invalid domain'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.domain = 'bad'; Set-Card $root $m } },
    @{ Name = 'illegal stage'; Expected = 'invalid stage'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.stage = 'bad'; Set-Card $root $m } },
    @{ Name = 'illegal dispatch state'; Expected = 'invalid dispatch state'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.dispatchState = 'bad'; Set-Card $root $m } },
    @{ Name = 'missing workerPaths'; Expected = "missing metadata field 'workerPaths'"; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.Remove('workerPaths'); Set-Card $root $m } },
    @{ Name = 'empty workerPaths'; Expected = 'workerPaths must not be empty'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.workerPaths = @(); $m.expectedPaths = @($m.coordinatorPaths); Set-Card $root $m } },
    @{ Name = 'overlapping path classes'; Expected = 'workerPaths/coordinatorPaths overlap'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.coordinatorPaths += 'simulations/BattleSim/Combat.cs'; Set-Card $root $m } },
    @{ Name = 'nested path classes'; Expected = 'workerPaths/coordinatorPaths overlap'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.workerPaths = @('simulations/BattleSim.v1'); $m.coordinatorPaths += 'simulations/BattleSim.v1/Combat.cs'; $m.expectedPaths = @($m.workerPaths + $m.coordinatorPaths); Set-Card $root $m } },
    @{ Name = 'classification union mismatch'; Expected = 'expectedPaths must equal workerPaths union coordinatorPaths'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths += 'extra.txt'; Set-Card $root $m } },
    @{ Name = 'source backlog outside coordinatorPaths'; Expected = 'sourceBacklog must be a coordinatorPath'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.coordinatorPaths = @($m.coordinatorPaths | Where-Object { $_ -cne $m.sourceBacklog }); $m.expectedPaths = @($m.workerPaths + $m.coordinatorPaths); Set-Card $root $m } },
    @{ Name = 'queue outside coordinatorPaths'; Expected = 'queue must be a coordinatorPath'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.coordinatorPaths = @($m.coordinatorPaths | Where-Object { $_ -cne '开发管理/当前任务队列.txt' }); $m.expectedPaths = @($m.workerPaths + $m.coordinatorPaths); Set-Card $root $m } },
    @{ Name = 'rooted expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('C:/bad.txt'); Set-Card $root $m } },
    @{ Name = 'backslash expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder\bad.txt'); Set-Card $root $m } },
    @{ Name = 'wildcard expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder/*.txt'); Set-Card $root $m } },
    @{ Name = 'dot expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder/./bad.txt'); Set-Card $root $m } },
    @{ Name = 'parent expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('../bad.txt'); Set-Card $root $m } },
    @{ Name = 'directory expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder'); Set-Card $root $m } },
    @{ Name = 'missing exact active-card authorization'; Expected = 'missing exact active-card authorization'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.coordinatorPaths = @($m.coordinatorPaths | Where-Object { $_ -cne '开发管理/任务卡/T-READY-01.txt' }); $m.expectedPaths = @($m.workerPaths + $m.coordinatorPaths); Set-Card $root $m } },
    @{ Name = 'missing exact archive authorization'; Expected = 'missing exact archive authorization'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.coordinatorPaths = @($m.coordinatorPaths | Where-Object { $_ -cne '开发管理/任务归档/T-READY-01.txt' }); $m.expectedPaths = @($m.workerPaths + $m.coordinatorPaths); Set-Card $root $m } },
    @{ Name = 'route owner mismatch'; Expected = 'route/owner mismatch'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.owner = 'claude'; Set-Card $root $m } },
    @{ Name = 'missing body heading'; Expected = 'missing body heading'; Change = { param($root, $f) Set-Card $root $f.Ready -Headings @('来源与当前边界') } },
    @{ Name = 'H1 mismatch'; Expected = 'H1 mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务卡/T-READY-01.txt'; (Get-Content -Raw $path).Replace('# T-READY-01 · 合法 ready 卡', '# bad') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'queue projection mismatch'; Expected = 'queue projection mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/当前任务队列.txt'; (Get-Content -Raw $path).Replace('codex_execute', 'codex_review') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'queue ID case mismatch'; Expected = 'queue ID case mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/当前任务队列.txt'; (Get-Content -Raw $path).Replace('| T-READY-01 |', '| t-ready-01 |').Replace('开发管理/任务卡/T-READY-01.txt', '开发管理/任务卡/t-ready-01.txt') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'non-ready in queue'; Expected = 'non-ready card in queue'; Change = { param($root, $f) Set-Queue $root @($f.Ready, $f.Blocked) } },
    @{ Name = 'ready missing from queue'; Expected = 'ready card missing from queue'; Change = { param($root, $f) Set-Queue $root @() } },
    @{ Name = 'ready duplicated in queue'; Expected = 'duplicate queue ID'; Change = { param($root, $f) Set-Queue $root @($f.Ready, $f.Ready) } },
    @{ Name = 'card absent from source backlog'; Expected = 'missing backlog row'; Change = { param($root, $f) Set-Backlog $root @($f.Blocked) } },
    @{ Name = 'backlog projection mismatch'; Expected = 'backlog projection mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务列表/数值与战斗任务.txt'; (Get-Content -Raw $path).Replace('| P2 | codex | 已排队 |', '| P1 | codex | 已排队 |') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'backlog owner mismatch'; Expected = 'backlog projection mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务列表/数值与战斗任务.txt'; (Get-Content -Raw $path).Replace('| P2 | codex | 已排队 |', '| P2 | claude | 已排队 |') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'backlog state mismatch'; Expected = 'backlog projection mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务列表/数值与战斗任务.txt'; (Get-Content -Raw $path).Replace('| P2 | codex | 已排队 |', '| P2 | codex | 阻塞 |') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'backlog blocker mismatch'; Expected = 'backlog blocker mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务列表/数值与战斗任务.txt'; (Get-Content -Raw $path).Replace('| 已排队 | — | 合法 ready 卡 |', '| 已排队 | T-X | 合法 ready 卡 |') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'backlog title mismatch'; Expected = 'backlog projection mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务列表/数值与战斗任务.txt'; (Get-Content -Raw $path).Replace('| 已排队 | — | 合法 ready 卡 |', '| 已排队 | — | 错误标题 |') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'backlog card path mismatch'; Expected = 'missing backlog row'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务列表/数值与战斗任务.txt'; (Get-Content -Raw $path).Replace('开发管理/任务卡/T-READY-01.txt', '开发管理/任务卡/T-OTHER-01.txt') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'completed active card'; Expected = 'completed card'; Change = { param($root, $f) $m = Copy-Metadata $f.Blocked; $m.dispatchState = 'completed'; Set-Card $root $m } },
    @{ Name = 'self dependency'; Expected = 'self-dependency'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.dispatchState = 'blocked'; $m.blockedBy = @('T-READY-01'); Set-Card $root $m; Set-Queue $root @(); Set-Backlog $root @($m, $f.Blocked) } },
    @{ Name = 'dependency ID case mismatch'; Expected = 'dependency ID case mismatch'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.dispatchState = 'blocked'; $m.blockedBy = @('t-blocked-01'); Set-Card $root $m; Set-Queue $root @(); Set-Backlog $root @($m, $f.Blocked) } },
    @{ Name = 'two-card dependency cycle'; Expected = 'dependency cycle'; Change = { param($root, $f) $a = Copy-Metadata $f.Ready; $b = Copy-Metadata $f.Blocked; $a.dispatchState = 'blocked'; $a.blockedBy = @('T-BLOCKED-01'); $b.blockedBy = @('T-READY-01'); Set-Card $root $a; Set-Card $root $b; Set-Queue $root @(); Set-Backlog $root @($a, $b) } }
  )

  foreach ($case in $cases) {
    $caseRoot = Join-Path $tempRoot ([guid]::NewGuid().ToString('N'))
    $caseFixture = New-Fixture $caseRoot
    & $case.Change $caseRoot $caseFixture
    Assert-Failure $case.Name $caseRoot $case.Expected
  }

  $transitionRoot = Join-Path $tempRoot 'transition'
  $transition = New-Fixture $transitionRoot
  $external = Get-Metadata -Route 'external_execute' -Owner 'deepseek'
  Set-Card $transitionRoot $external
  Set-Queue $transitionRoot @($external)
  Set-Backlog $transitionRoot @($external, $transition.Blocked)
  Assert-Success 'external ready transition start' $transitionRoot
  $review = Get-Metadata -Route 'codex_review' -Owner 'codex'
  Set-Card $transitionRoot $review
  Set-Queue $transitionRoot @($review)
  Set-Backlog $transitionRoot @($review, $transition.Blocked)
  Assert-Success 'codex review transition end' $transitionRoot
  Assert-True ((Get-ChildItem -LiteralPath (Join-Path $transitionRoot '开发管理/任务卡') -Filter '*.txt').Count -eq 2) 'transition created a second task-card ID'
  $externalPendingReview = Invoke-Checker $transitionRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'ExternalPendingReview'
  )
  Assert-True ($externalPendingReview.ExitCode -eq 0) "same-card pending review should pass ExternalPendingReview: $($externalPendingReview.Output)"
  $externalWrongCases = @(
    @{
      Name = 'wrong external pending-review route'
      Metadata = Get-Metadata -Route 'codex_execute' -Owner 'codex'
      Expected = 'ExternalPendingReview requires route=codex_review owner=codex dispatchState=ready'
    },
    @{
      Name = 'wrong external pending-review owner'
      Metadata = Get-Metadata -Route 'codex_review' -Owner 'deepseek'
      Expected = 'route/owner mismatch'
    },
    @{
      Name = 'wrong external pending-review state'
      Metadata = Get-Metadata -Route 'codex_review' -Owner 'codex' -DispatchState 'blocked' -StateReason 'not ready'
      Expected = 'ExternalPendingReview requires route=codex_review owner=codex dispatchState=ready'
    }
  )
  foreach ($externalWrongCase in $externalWrongCases) {
    $externalWrongRoot = Join-Path $tempRoot ([guid]::NewGuid().ToString('N'))
    $externalWrongFixture = New-Fixture $externalWrongRoot
    Set-Card $externalWrongRoot $externalWrongCase.Metadata
    $externalWrongQueue = if ($externalWrongCase.Metadata.dispatchState -ceq 'ready') { @($externalWrongCase.Metadata) } else { @() }
    Set-Queue $externalWrongRoot $externalWrongQueue
    Set-Backlog $externalWrongRoot @($externalWrongCase.Metadata, $externalWrongFixture.Blocked)
    $externalWrongResult = Invoke-Checker $externalWrongRoot @(
      '-TaskId', 'T-READY-01',
      '-Postcondition', 'ExternalPendingReview'
    )
    Assert-True ($externalWrongResult.ExitCode -ne 0) "$($externalWrongCase.Name) should fail"
    Assert-True ($externalWrongResult.Output -match [regex]::Escape($externalWrongCase.Expected)) "$($externalWrongCase.Name) missing diagnostic: $($externalWrongResult.Output)"
  }

  $readyPostconditionRoot = Join-Path $tempRoot 'ready-postcondition'
  New-Fixture $readyPostconditionRoot | Out-Null
  $readyPostcondition = Invoke-Checker $readyPostconditionRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'CodexClosedOrNonReady'
  )
  Assert-True ($readyPostcondition.ExitCode -ne 0) 'unchanged ready task should fail CodexClosedOrNonReady'
  Assert-True ($readyPostcondition.Output -match 'CodexClosedOrNonReady requires') "unchanged ready task missing postcondition diagnostic: $($readyPostcondition.Output)"

  $blockedPostconditionRoot = Join-Path $tempRoot 'blocked-postcondition'
  New-Fixture $blockedPostconditionRoot | Out-Null
  $blockedPostcondition = Invoke-Checker $blockedPostconditionRoot @(
    '-TaskId', 'T-BLOCKED-01',
    '-Postcondition', 'CodexClosedOrNonReady',
    '-OutputJson'
  )
  Assert-True ($blockedPostcondition.ExitCode -eq 0) "legal blocked task should pass CodexClosedOrNonReady: $($blockedPostcondition.Output)"
  $blockedEvidence = $blockedPostcondition.Output | ConvertFrom-Json
  Assert-True ($blockedEvidence.taskState -ceq 'blocked') 'blocked lifecycle evidence mismatch'
  foreach ($caseMismatch in @(
      @{ Root = $blockedPostconditionRoot; TaskId = 't-blocked-01'; Postcondition = 'CodexClosedOrNonReady' },
      @{ Root = $transitionRoot; TaskId = 't-ready-01'; Postcondition = 'ExternalPendingReview' }
    )) {
    $caseMismatchResult = Invoke-Checker $caseMismatch.Root @(
      '-TaskId', $caseMismatch.TaskId,
      '-Postcondition', $caseMismatch.Postcondition
    )
    Assert-True ($caseMismatchResult.ExitCode -ne 0) "$($caseMismatch.Postcondition) accepted a case-mismatched TaskId"
    Assert-True ($caseMismatchResult.Output -match 'TaskId case mismatch') "$($caseMismatch.Postcondition) missing TaskId case diagnostic: $($caseMismatchResult.Output)"
  }

  foreach ($nonReadyState in @('pending_decision', 'frozen', 'waiting_reply')) {
    $nonReadyRoot = Join-Path $tempRoot "non-ready-$nonReadyState"
    $nonReadyFixture = New-Fixture $nonReadyRoot
    $nonReadyCard = Copy-Metadata $nonReadyFixture.Blocked
    $nonReadyCard.dispatchState = $nonReadyState
    Set-Card $nonReadyRoot $nonReadyCard
    Set-Backlog $nonReadyRoot @($nonReadyFixture.Ready, $nonReadyCard)
    $nonReadyPostcondition = Invoke-Checker $nonReadyRoot @(
      '-TaskId', 'T-BLOCKED-01',
      '-Postcondition', 'CodexClosedOrNonReady'
    )
    Assert-True ($nonReadyPostcondition.ExitCode -eq 0) "legal $nonReadyState task should pass CodexClosedOrNonReady: $($nonReadyPostcondition.Output)"
  }

  $archivePostconditionRoot = Join-Path $tempRoot 'archive-postcondition'
  $archiveFixture = New-Fixture $archivePostconditionRoot
  $completedArchive = Copy-Metadata $archiveFixture.Ready
  $completedArchive.dispatchState = 'completed'
  Set-Archive $archivePostconditionRoot $completedArchive
  Remove-Item -LiteralPath (Join-Path $archivePostconditionRoot '开发管理/任务卡/T-READY-01.txt') -Force
  Set-Queue $archivePostconditionRoot @()
  Set-Backlog $archivePostconditionRoot @($archiveFixture.Blocked)
  $archivePostcondition = Invoke-Checker $archivePostconditionRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'CodexClosedOrNonReady',
    '-OutputJson'
  )
  Assert-True ($archivePostcondition.ExitCode -eq 0) "exact completed archive should pass CodexClosedOrNonReady: $($archivePostcondition.Output)"
  $archiveEvidence = $archivePostcondition.Output | ConvertFrom-Json
  Assert-True ($archiveEvidence.taskState -ceq 'completed') 'archive lifecycle evidence mismatch'

  $staleArchiveRoot = Join-Path $tempRoot 'stale-archive-postcondition'
  $staleArchiveFixture = New-Fixture $staleArchiveRoot
  $staleCompletedArchive = Copy-Metadata $staleArchiveFixture.Ready
  $staleCompletedArchive.dispatchState = 'completed'
  Set-Archive $staleArchiveRoot $staleCompletedArchive
  Remove-Item -LiteralPath (Join-Path $staleArchiveRoot '开发管理/任务卡/T-READY-01.txt') -Force
  Set-Queue $staleArchiveRoot @()
  Set-Backlog $staleArchiveRoot @($staleArchiveFixture.Ready, $staleArchiveFixture.Blocked)
  $staleArchivePostcondition = Invoke-Checker $staleArchiveRoot @(
    '-TaskId', 'T-READY-01',
    '-Postcondition', 'CodexClosedOrNonReady'
  )
  Assert-True ($staleArchivePostcondition.ExitCode -ne 0) 'stale backlog row after archive should fail CodexClosedOrNonReady'
  Assert-True ($staleArchivePostcondition.Output -match 'archived TaskId remains in backlog') "stale archive missing backlog diagnostic: $($staleArchivePostcondition.Output)"

  Write-Output 'test-check-task-cards: PASS'
} finally {
  if (Test-Path -LiteralPath $tempRoot) {
    $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'refusing to remove a non-temp test directory'
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
