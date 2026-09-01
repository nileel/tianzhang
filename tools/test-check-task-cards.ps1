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
  $expectedPaths = @(
    'simulations/BattleSim/Combat.cs'
    "开发管理/任务卡/$Id.txt"
    "开发管理/任务归档/$Id.txt"
  )
  if ($Route -ceq 'codex_review') {
    $expectedPaths += '开发管理/未通过审核清单.txt'
  }
  [ordered]@{
    schemaVersion = 1
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
    expectedPaths = $expectedPaths
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

function New-Experience {
  param(
    [string]$Id,
    [string]$Trigger = 'path',
    [string[]]$Paths = @()
  )
  [ordered]@{
    id = $Id
    title = 'fixture experience'
    preflightSummary = 'fixture summary'
    status = 'active'
    level = 'notice'
    triggerMode = $Trigger
    domains = @()
    stages = @()
    pathPatterns = @($Paths)
    textPatterns = @()
    detailRef = ''
    gateRefs = @()
    lastVerified = '2026-08-28'
  }
}

function Set-RiskIndex {
  param([string]$Root, [object[]]$Experiences = @())
  $index = [ordered]@{ schemaVersion = 1; experiences = @($Experiences); gates = @() }
  Write-Utf8 (Join-Path $Root '开发管理/经验库/风险索引.json') ($index | ConvertTo-Json -Depth 20 -Compress)
}

function Get-Schema2Metadata {
  param(
    [string]$Id = 'T-S2-READY-01',
    [string]$Title = '合法 schema 2 ready 卡',
    [string]$DispatchState = 'ready',
    [string[]]$ExpectedPaths = @('simulations/BattleSim/Combat.cs'),
    [string[]]$ExplicitRefs = @(),
    [string[]]$Matched = @(),
    [string[]]$Gates = @(),
    [string]$Route = 'codex_execute',
    [string]$Owner = 'codex',
    [string]$StateReason = $null
  )
  $expectedPaths = @($ExpectedPaths) + "开发管理/任务卡/$Id.txt" + "开发管理/任务归档/$Id.txt"
  if ($Route -ceq 'codex_review') { $expectedPaths += '开发管理/未通过审核清单.txt' }
  [ordered]@{
    schemaVersion = 2
    id = $Id
    title = $Title
    priority = 'P2'
    route = $Route
    owner = $Owner
    domain = 'battlesim'
    stage = 'implementation'
    dispatchState = $DispatchState
    blockedBy = @()
    stateReason = $StateReason
    expectedPaths = $expectedPaths
    sourceBacklog = '开发管理/任务列表/数值与战斗任务.txt'
    riskPreflight = [ordered]@{ explicitRefs = @($ExplicitRefs); matched = @($Matched); gates = @($Gates) }
  }
}

function Get-AutomationInput {
  param(
    [string]$Path = 'assets/source/fixture/input.fbx',
    [object]$Bytes = 17,
    [string]$Sha256 = ('A' * 64)
  )
  [ordered]@{ path = $Path; bytes = $Bytes; sha256 = $Sha256 }
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
  $ready = Get-Schema2Metadata -Id 'T-READY-01' -Title '合法 ready 卡'
  $blocked = Get-Metadata -Id 'T-BLOCKED-01' -Title '合法 blocked 卡' -DispatchState 'blocked' -StateReason '等待外部输入'
  Set-RiskIndex $Root
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

function Invoke-FixtureGit {
  param([string]$Root, [string[]]$Arguments)
  $output = @(& git -C $Root @Arguments 2>&1)
  if ($LASTEXITCODE -ne 0) { throw "fixture git failed: $($Arguments -join ' '): $($output -join "`n")" }
  (@($output) -join "`n").Trim()
}

function Initialize-FixtureRepository {
  param([string]$Root)
  Invoke-FixtureGit $Root @('init', '-q', '-b', 'master') | Out-Null
  Invoke-FixtureGit $Root @('config', 'user.name', 'Task Card Guard Test') | Out-Null
  Invoke-FixtureGit $Root @('config', 'user.email', 'task-card-guard@example.invalid') | Out-Null
  Invoke-FixtureGit $Root @('add', '-A') | Out-Null
  Invoke-FixtureGit $Root @('commit', '-q', '-m', 'test: seed task-card transition base') | Out-Null
  Invoke-FixtureGit $Root @('rev-parse', 'HEAD')
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

  $inputReady = Copy-Metadata $fixture.Ready
  $inputReady.automationInputs = @((Get-AutomationInput))
  Set-Card $tempRoot $inputReady
  Set-Queue $tempRoot @($inputReady)
  Set-Backlog $tempRoot @($inputReady, $fixture.Blocked)
  Assert-Success 'valid automationInputs' $tempRoot

  $automationInputCases = @(
    @{ Name = 'rooted automation input'; Expected = 'invalid repository-relative path'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Path 'C:/outside.fbx')) } },
    @{ Name = 'backslash automation input'; Expected = 'invalid repository-relative path'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Path 'assets/source\\bad.fbx')) } },
    @{ Name = 'wildcard automation input'; Expected = 'invalid repository-relative path'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Path 'assets/source/*.fbx')) } },
    @{ Name = 'parent automation input'; Expected = 'invalid repository-relative path'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Path 'assets/source/../bad.fbx')) } },
    @{ Name = 'directory automation input'; Expected = 'invalid repository-relative path'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Path 'assets/source/directory/')) } },
    @{ Name = 'outside source automation input'; Expected = 'automationInputs path must be under assets/source/'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Path 'assets/generated/input.fbx')) } },
    @{ Name = 'duplicate automation input'; Expected = 'duplicate automationInputs path'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput), (Get-AutomationInput)) } },
    @{ Name = 'nonpositive automation input bytes'; Expected = 'invalid automationInputs bytes'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Bytes 0)) } },
    @{ Name = 'fractional automation input bytes'; Expected = 'invalid automationInputs bytes'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Bytes 1.5)) } },
    @{ Name = 'invalid automation input hash'; Expected = 'invalid automationInputs sha256'; Change = { param($m) $m.automationInputs = @((Get-AutomationInput -Sha256 ('B' * 63))) } },
    @{ Name = 'extra automation input field'; Expected = 'invalid automationInputs'; Change = { param($m) $m.automationInputs = @([ordered]@{ path = 'assets/source/fixture/input.fbx'; bytes = 17; sha256 = ('A' * 64); extra = 'bad' }) } },
    @{ Name = 'external card automation input'; Expected = 'automationInputs requires route=codex_execute owner=codex'; Change = { param($m) $m.route = 'external_execute'; $m.owner = 'deepseek'; $m.automationInputs = @((Get-AutomationInput)) } }
  )
  foreach ($case in $automationInputCases) {
    $caseRoot = Join-Path $tempRoot ('automation-input-' + [guid]::NewGuid().ToString('N'))
    $caseFixture = New-Fixture $caseRoot
    $caseCard = Copy-Metadata $caseFixture.Ready
    & $case.Change $caseCard
    Set-Card $caseRoot $caseCard
    Set-Queue $caseRoot @($caseCard)
    Set-Backlog $caseRoot @($caseCard, $caseFixture.Blocked)
    Assert-Failure $case.Name $caseRoot $case.Expected
  }

  Set-Card $tempRoot $fixture.Ready
  Set-Queue $tempRoot @($fixture.Ready)
  Set-Backlog $tempRoot @($fixture.Ready, $fixture.Blocked)

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
  $reviewDispatchCard.expectedPaths += '开发管理/未通过审核清单.txt'
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

  $missingReviewAuthorizationRoot = Join-Path $tempRoot 'missing-review-authorization'
  $missingReviewAuthorizationFixture = New-Fixture $missingReviewAuthorizationRoot
  $missingReviewAuthorizationCard = Get-Metadata -Route 'codex_review' -Owner 'codex'
  $missingReviewAuthorizationCard.expectedPaths = @($missingReviewAuthorizationCard.expectedPaths | Where-Object { $_ -cne '开发管理/未通过审核清单.txt' })
  Set-Card $missingReviewAuthorizationRoot $missingReviewAuthorizationCard
  Set-Queue $missingReviewAuthorizationRoot @($missingReviewAuthorizationCard)
  Set-Backlog $missingReviewAuthorizationRoot @($missingReviewAuthorizationCard, $missingReviewAuthorizationFixture.Blocked)
  Assert-Failure 'review card missing review-list authorization' $missingReviewAuthorizationRoot 'missing review-list authorization: T-READY-01'

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
    @{ Name = 'illegal schema version'; Expected = 'illegal schemaVersion'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.schemaVersion = 3; Set-Card $root $m } },
    @{ Name = 'illegal priority'; Expected = 'invalid priority'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.priority = 'P4'; Set-Card $root $m } },
    @{ Name = 'illegal owner'; Expected = 'invalid owner'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.owner = 'bad'; Set-Card $root $m } },
    @{ Name = 'illegal domain'; Expected = 'invalid domain'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.domain = 'bad'; Set-Card $root $m } },
    @{ Name = 'illegal stage'; Expected = 'invalid stage'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.stage = 'bad'; Set-Card $root $m } },
    @{ Name = 'illegal dispatch state'; Expected = 'invalid dispatch state'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.dispatchState = 'bad'; Set-Card $root $m } },
    @{ Name = 'rooted expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('C:/bad.txt'); Set-Card $root $m } },
    @{ Name = 'backslash expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder\bad.txt'); Set-Card $root $m } },
    @{ Name = 'wildcard expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder/*.txt'); Set-Card $root $m } },
    @{ Name = 'dot expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder/./bad.txt'); Set-Card $root $m } },
    @{ Name = 'parent expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('../bad.txt'); Set-Card $root $m } },
    @{ Name = 'directory expected path'; Expected = 'invalid repository-relative path'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('folder'); Set-Card $root $m } },
    @{ Name = 'missing exact active-card authorization'; Expected = 'missing exact active-card authorization'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('simulations/BattleSim/Combat.cs', '开发管理/任务归档/T-READY-01.txt'); Set-Card $root $m } },
    @{ Name = 'missing exact archive authorization'; Expected = 'missing exact archive authorization'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.expectedPaths = @('simulations/BattleSim/Combat.cs', '开发管理/任务卡/T-READY-01.txt'); Set-Card $root $m } },
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
  $external = Get-Schema2Metadata -Id 'T-READY-01' -Title '合法 ready 卡' -Route 'external_execute' -Owner 'deepseek'
  Set-Card $transitionRoot $external
  Set-Queue $transitionRoot @($external)
  Set-Backlog $transitionRoot @($external, $transition.Blocked)
  Assert-Success 'external ready transition start' $transitionRoot
  $review = Get-Schema2Metadata -Id 'T-READY-01' -Title '合法 ready 卡' -Route 'codex_review' -Owner 'codex'
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
    if ($nonReadyState -cin @('pending_decision', 'waiting_reply')) {
      $nonReadyCard['automationCheckpoint'] = [ordered]@{
        schemaVersion = 1; taskId = 'T-BLOCKED-01'; sourceRunId = 'run-fixture'; owner = 'codex'; route = 'codex_execute'
        decisionId = 'DEC-20260814-FIXTURE'; question = 'fixture?'; options = @(@{ key = 'A'; label = 'A' }, @{ key = 'B'; label = 'B' }, @{ key = 'C'; label = 'C' })
        recommendedOption = 'A'; impactSummary = 'fixture'; plainSummary = @{ situation = 'fixture'; impact = 'fixture'; action = 'fixture' }
        checkpointCommit = ('a' * 40); baseCommit = ('b' * 40); branch = 'codex/automation/fixture'; changedPaths = @('fixture.txt')
        verified = @('fixture'); unverified = @(); residualRisk = 'fixture'; taskContextDigest = ('c' * 64); createdAt = '2026-08-14T00:00:00Z'; queueIndex = 0
      }
    }
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

  # ---- QueueMaintenance ready schema 2 transition guard ----
  $guardParameterRoot = Join-Path $tempRoot 'queue-ready-guard-parameters'
  New-Fixture $guardParameterRoot | Out-Null
  $guardParameterBase = Initialize-FixtureRepository $guardParameterRoot
  Assert-OverrideFailure 'QueueMaintenance guard requires BaseCommit' $guardParameterRoot 'BaseCommit is required for QueueMaintenanceReadySchema2Guard' @(
    '-Postcondition', 'QueueMaintenanceReadySchema2Guard'
  )
  Assert-OverrideFailure 'global check forbids BaseCommit' $guardParameterRoot 'BaseCommit is only valid for QueueMaintenanceReadySchema2Guard' @(
    '-BaseCommit', $guardParameterBase
  )
  Assert-OverrideFailure 'task-scoped postcondition forbids BaseCommit' $guardParameterRoot 'BaseCommit is only valid for QueueMaintenanceReadySchema2Guard' @(
    '-TaskId', 'T-READY-01', '-Postcondition', 'CodexClosedOrNonReady', '-BaseCommit', $guardParameterBase
  )
  Assert-OverrideFailure 'QueueMaintenance guard forbids TaskId' $guardParameterRoot 'TaskId is required for task-scoped postconditions and forbidden for QueueMaintenanceReadySchema2Guard' @(
    '-TaskId', 'T-READY-01', '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', $guardParameterBase
  )
  Assert-OverrideFailure 'QueueMaintenance guard rejects non-full base' $guardParameterRoot 'BaseCommit must be a full lowercase commit ID' @(
    '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', 'not-a-commit'
  )
  Assert-OverrideFailure 'QueueMaintenance guard rejects unresolvable base' $guardParameterRoot 'BaseCommit is not a resolvable commit' @(
    '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', ('f' * 40)
  )
  Invoke-FixtureGit $guardParameterRoot @('switch', '-q', '-c', 'side') | Out-Null
  Write-Utf8 (Join-Path $guardParameterRoot 'side-only.txt') 'side'
  Invoke-FixtureGit $guardParameterRoot @('add', 'side-only.txt') | Out-Null
  Invoke-FixtureGit $guardParameterRoot @('commit', '-q', '-m', 'test: side-only base') | Out-Null
  $nonAncestorBase = Invoke-FixtureGit $guardParameterRoot @('rev-parse', 'HEAD')
  Invoke-FixtureGit $guardParameterRoot @('switch', '-q', 'master') | Out-Null
  Assert-OverrideFailure 'QueueMaintenance guard rejects non-ancestor base' $guardParameterRoot 'BaseCommit must be an ancestor of current HEAD' @(
    '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', $nonAncestorBase
  )

  $guardSchema1TransitionRoot = Join-Path $tempRoot 'queue-ready-guard-schema1-transition'
  $guardSchema1Fixture = New-Fixture $guardSchema1TransitionRoot
  $guardSchema1Base = Initialize-FixtureRepository $guardSchema1TransitionRoot
  $guardSchema1Ready = Copy-Metadata $guardSchema1Fixture.Blocked
  $guardSchema1Ready.dispatchState = 'ready'
  $guardSchema1Ready.stateReason = 'fixture restored ready'
  Set-Card $guardSchema1TransitionRoot $guardSchema1Ready
  Set-Queue $guardSchema1TransitionRoot @($guardSchema1Fixture.Ready, $guardSchema1Ready)
  Set-Backlog $guardSchema1TransitionRoot @($guardSchema1Fixture.Ready, $guardSchema1Ready)
  Assert-OverrideFailure 'blocked schema 1 to ready schema 1 transition' $guardSchema1TransitionRoot 'QueueMaintenance ready transition requires schemaVersion=2: T-BLOCKED-01' @(
    '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', $guardSchema1Base
  )

  $guardSchema2TransitionRoot = Join-Path $tempRoot 'queue-ready-guard-schema2-transition'
  $guardSchema2Fixture = New-Fixture $guardSchema2TransitionRoot
  Set-RiskIndex $guardSchema2TransitionRoot
  $guardSchema2Base = Initialize-FixtureRepository $guardSchema2TransitionRoot
  $guardSchema2Ready = Get-Schema2Metadata -Id 'T-BLOCKED-01' -Title '合法 blocked 卡' -StateReason 'fixture restored ready with live projection'
  Set-Card $guardSchema2TransitionRoot $guardSchema2Ready
  Set-Queue $guardSchema2TransitionRoot @($guardSchema2Fixture.Ready, $guardSchema2Ready)
  Set-Backlog $guardSchema2TransitionRoot @($guardSchema2Fixture.Ready, $guardSchema2Ready)
  $guardSchema2Result = Invoke-Checker $guardSchema2TransitionRoot @('-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', $guardSchema2Base, '-OutputJson')
  Assert-True ($guardSchema2Result.ExitCode -eq 0) "blocked schema 1 to ready schema 2 transition should pass: $($guardSchema2Result.Output)"
  $guardSchema2Evidence = $guardSchema2Result.Output | ConvertFrom-Json
  Assert-True ([string]$guardSchema2Evidence.postcondition -ceq 'QueueMaintenanceReadySchema2Guard') 'QueueMaintenance guard JSON evidence lost its postcondition'

  $guardNewSchema1Root = Join-Path $tempRoot 'queue-ready-guard-new-schema1'
  $guardNewSchema1Fixture = New-Fixture $guardNewSchema1Root
  $guardNewSchema1Base = Initialize-FixtureRepository $guardNewSchema1Root
  $guardNewSchema1Card = Get-Metadata -Id 'T-NEW-READY-01' -Title '新建 ready schema 1 卡'
  Set-Card $guardNewSchema1Root $guardNewSchema1Card
  Set-Queue $guardNewSchema1Root @($guardNewSchema1Fixture.Ready, $guardNewSchema1Card)
  Set-Backlog $guardNewSchema1Root @($guardNewSchema1Fixture.Ready, $guardNewSchema1Fixture.Blocked, $guardNewSchema1Card)
  Assert-OverrideFailure 'new ready schema 1 card' $guardNewSchema1Root 'QueueMaintenance ready transition requires schemaVersion=2: T-NEW-READY-01' @(
    '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', $guardNewSchema1Base
  )

  $guardUnchangedSchema1Root = Join-Path $tempRoot 'queue-ready-guard-unchanged-schema1'
  $guardUnchangedSchema1Fixture = New-Fixture $guardUnchangedSchema1Root
  $guardUnchangedSchema1Legacy = Get-Metadata -Id 'T-READY-01' -Title '合法 ready 卡'
  Set-Card $guardUnchangedSchema1Root $guardUnchangedSchema1Legacy
  Set-Queue $guardUnchangedSchema1Root @($guardUnchangedSchema1Legacy)
  Set-Backlog $guardUnchangedSchema1Root @($guardUnchangedSchema1Legacy, $guardUnchangedSchema1Fixture.Blocked)
  $guardUnchangedSchema1Base = Initialize-FixtureRepository $guardUnchangedSchema1Root
  $guardUnchangedSchema1 = Invoke-Checker $guardUnchangedSchema1Root @('-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', $guardUnchangedSchema1Base)
  Assert-True ($guardUnchangedSchema1.ExitCode -ne 0) 'pre-existing ready schema 1 should be rejected after activation'
  Assert-True ($guardUnchangedSchema1.Output -match 'schema 1 ready is rejected') 'pre-existing ready schema 1 rejection diagnostic missing'

  $guardMissingArrayRoot = Join-Path $tempRoot 'queue-ready-guard-missing-array'
  $guardMissingArrayFixture = New-Fixture $guardMissingArrayRoot
  Set-RiskIndex $guardMissingArrayRoot
  $guardMissingArrayBase = Initialize-FixtureRepository $guardMissingArrayRoot
  $guardMissingArrayCard = Get-Schema2Metadata -Id 'T-BLOCKED-01' -Title '合法 blocked 卡' -StateReason 'fixture missing gates array'
  $guardMissingArrayCard.riskPreflight.Remove('gates')
  Set-Card $guardMissingArrayRoot $guardMissingArrayCard
  Set-Queue $guardMissingArrayRoot @($guardMissingArrayFixture.Ready, $guardMissingArrayCard)
  Set-Backlog $guardMissingArrayRoot @($guardMissingArrayFixture.Ready, $guardMissingArrayCard)
  Assert-OverrideFailure 'ready schema 2 transition missing one required array' $guardMissingArrayRoot 'schema 2 riskPreflight field set must be exactly explicitRefs/matched/gates' @(
    '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', $guardMissingArrayBase
  )

  # ---- schema 2 compatibility: structural field contract ----
  $schema1RiskRoot = Join-Path $tempRoot 'schema1-risk-preflight'
  $schema1RiskFixture = New-Fixture $schema1RiskRoot
  $schema1RiskCard = Get-Metadata -Id 'T-READY-01' -Title '合法 ready 卡'
  $schema1RiskCard['riskPreflight'] = [ordered]@{ explicitRefs = @(); matched = @(); gates = @() }
  Set-Card $schema1RiskRoot $schema1RiskCard
  Set-Queue $schema1RiskRoot @($schema1RiskCard)
  Set-Backlog $schema1RiskRoot @($schema1RiskCard, $schema1RiskFixture.Blocked)
  Assert-Failure 'schema 1 must not include riskPreflight' $schema1RiskRoot 'schema 1 must not include riskPreflight'

  $schema2MissingRiskRoot = Join-Path $tempRoot 'schema2-missing-risk'
  $schema2MissingRiskFixture = New-Fixture $schema2MissingRiskRoot
  $schema2MissingRiskCard = Get-Metadata -Id 'T-READY-01' -Title '合法 ready 卡'
  $schema2MissingRiskCard.schemaVersion = 2
  Set-Card $schema2MissingRiskRoot $schema2MissingRiskCard
  Set-Queue $schema2MissingRiskRoot @($schema2MissingRiskCard)
  Set-Backlog $schema2MissingRiskRoot @($schema2MissingRiskCard, $schema2MissingRiskFixture.Blocked)
  Assert-Failure 'schema 2 requires riskPreflight' $schema2MissingRiskRoot 'schema 2 requires riskPreflight'

  foreach ($illegalField in @(
      @{ Name = 'schema 2 riskPreflight extra field'; Risk = [ordered]@{ explicitRefs = @(); matched = @(); gates = @(); extra = @() }; Expected = 'field set must be exactly' },
      @{ Name = 'schema 2 riskPreflight missing field'; Risk = [ordered]@{ explicitRefs = @(); matched = @() }; Expected = 'field set must be exactly' },
      @{ Name = 'schema 2 riskPreflight non-array field'; Risk = [ordered]@{ explicitRefs = @(); matched = 'bad'; gates = @() }; Expected = 'must be an array' }
    )) {
    $illegalRoot = Join-Path $tempRoot ([guid]::NewGuid().ToString('N'))
    $illegalFixture = New-Fixture $illegalRoot
    $illegalCard = Copy-Metadata $illegalFixture.Ready
    $illegalCard.schemaVersion = 2
    $illegalCard['riskPreflight'] = $illegalField.Risk
    Set-Card $illegalRoot $illegalCard
    Set-Queue $illegalRoot @($illegalCard)
    Set-Backlog $illegalRoot @($illegalCard, $illegalFixture.Blocked)
    Assert-Failure $illegalField.Name $illegalRoot $illegalField.Expected
  }

  # ---- schema 2 ready projection recompute: zero-hit / normal-hit / explicit ----
  $schema2ZeroRoot = Join-Path $tempRoot 'schema2-zero-hit'
  Set-RiskIndex $schema2ZeroRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/other.ps1')))
  $schema2ZeroCard = Get-Schema2Metadata -Id 'T-S2-ZERO-01'
  Set-Card $schema2ZeroRoot $schema2ZeroCard
  Set-Queue $schema2ZeroRoot @($schema2ZeroCard)
  Set-Backlog $schema2ZeroRoot @($schema2ZeroCard)
  $schema2Zero = Invoke-Checker $schema2ZeroRoot
  Assert-True ($schema2Zero.ExitCode -eq 0) "schema 2 zero-hit ready should pass: $($schema2Zero.Output)"

  $schema2HitRoot = Join-Path $tempRoot 'schema2-normal-hit'
  Set-RiskIndex $schema2HitRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema2HitCard = Get-Schema2Metadata -Id 'T-S2-HIT-01' -ExpectedPaths @('tools/foo.ps1') -Matched @('EXP-AUTO-001')
  Set-Card $schema2HitRoot $schema2HitCard
  Set-Queue $schema2HitRoot @($schema2HitCard)
  Set-Backlog $schema2HitRoot @($schema2HitCard)
  $schema2Hit = Invoke-Checker $schema2HitRoot
  Assert-True ($schema2Hit.ExitCode -eq 0) "schema 2 normal-hit ready should pass: $($schema2Hit.Output)"

  $schema2ExplicitRoot = Join-Path $tempRoot 'schema2-explicit'
  Set-RiskIndex $schema2ExplicitRoot @((New-Experience -Id 'EXP-MGMT-001' -Trigger 'explicit_only'))
  $schema2ExplicitCard = Get-Schema2Metadata -Id 'T-S2-EXP-01' -ExplicitRefs @('EXP-MGMT-001') -Matched @('EXP-MGMT-001')
  Set-Card $schema2ExplicitRoot $schema2ExplicitCard
  Set-Queue $schema2ExplicitRoot @($schema2ExplicitCard)
  Set-Backlog $schema2ExplicitRoot @($schema2ExplicitCard)
  $schema2Explicit = Invoke-Checker $schema2ExplicitRoot
  Assert-True ($schema2Explicit.ExitCode -eq 0) "schema 2 explicit ready should pass: $($schema2Explicit.Output)"

  # ---- schema 2 ready projection recompute: stale / gates / explicit gap fail ----
  $schema2StaleRoot = Join-Path $tempRoot 'schema2-stale'
  Set-RiskIndex $schema2StaleRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema2StaleCard = Get-Schema2Metadata -Id 'T-S2-STALE-01' -ExpectedPaths @('tools/foo.ps1') -Matched @('EXP-AUTO-001', 'EXP-BS-003')
  Set-Card $schema2StaleRoot $schema2StaleCard
  Set-Queue $schema2StaleRoot @($schema2StaleCard)
  Set-Backlog $schema2StaleRoot @($schema2StaleCard)
  Assert-Failure 'schema 2 stale matched projection' $schema2StaleRoot 'riskPreflight matched projection mismatch'

  $schema2GatesRoot = Join-Path $tempRoot 'schema2-gates'
  Set-RiskIndex $schema2GatesRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema2GatesCard = Get-Schema2Metadata -Id 'T-S2-GATES-01' -ExpectedPaths @('tools/foo.ps1') -Matched @('EXP-AUTO-001') -Gates @('missing-gate')
  Set-Card $schema2GatesRoot $schema2GatesCard
  Set-Queue $schema2GatesRoot @($schema2GatesCard)
  Set-Backlog $schema2GatesRoot @($schema2GatesCard)
  Assert-Failure 'schema 2 gates projection mismatch' $schema2GatesRoot 'riskPreflight gates projection mismatch'

  $schema2ExplicitGapRoot = Join-Path $tempRoot 'schema2-explicit-gap'
  Set-RiskIndex $schema2ExplicitGapRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema2ExplicitGapCard = Get-Schema2Metadata -Id 'T-S2-EXPGAP-01' -ExpectedPaths @('tools/foo.ps1') -Matched @('EXP-AUTO-001') -ExplicitRefs @('EXP-MGMT-001')
  Set-Card $schema2ExplicitGapRoot $schema2ExplicitGapCard
  Set-Queue $schema2ExplicitGapRoot @($schema2ExplicitGapCard)
  Set-Backlog $schema2ExplicitGapRoot @($schema2ExplicitGapCard)
  Assert-Failure 'schema 2 explicitRef outside matched' $schema2ExplicitGapRoot 'riskPreflight explicitRef not in matched'

  $schema2NonExplicitRefRoot = Join-Path $tempRoot 'schema2-non-explicit-ref'
  Set-RiskIndex $schema2NonExplicitRefRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema2NonExplicitRefCard = Get-Schema2Metadata -Id 'T-S2-NONEXP-01' -ExpectedPaths @('tools/foo.ps1') -Matched @('EXP-AUTO-001') -ExplicitRefs @('EXP-AUTO-001')
  Set-Card $schema2NonExplicitRefRoot $schema2NonExplicitRefCard
  Set-Queue $schema2NonExplicitRefRoot @($schema2NonExplicitRefCard)
  Set-Backlog $schema2NonExplicitRefRoot @($schema2NonExplicitRefCard)
  Assert-Failure 'schema 2 non-explicit_only explicitRef' $schema2NonExplicitRefRoot 'riskPreflight explicitRef not explicit_only'

  # ---- activation: schema 1 ready is rejected even alongside a legal schema 2 ready card ----
  $schema1ReadyRejectedRoot = Join-Path $tempRoot 'schema1-ready-rejected'
  Set-RiskIndex $schema1ReadyRejectedRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema1ReadyRejectedS1 = Get-Metadata -Id 'T-S1-READY-01'
  $schema1ReadyRejectedS2 = Get-Schema2Metadata -Id 'T-S2-READY-01' -ExpectedPaths @('tools/foo.ps1') -Matched @('EXP-AUTO-001')
  Set-Card $schema1ReadyRejectedRoot $schema1ReadyRejectedS1
  Set-Card $schema1ReadyRejectedRoot $schema1ReadyRejectedS2
  Set-Queue $schema1ReadyRejectedRoot @($schema1ReadyRejectedS1, $schema1ReadyRejectedS2)
  Set-Backlog $schema1ReadyRejectedRoot @($schema1ReadyRejectedS1, $schema1ReadyRejectedS2)
  Assert-Failure 'schema 1 ready rejected' $schema1ReadyRejectedRoot 'schema 1 ready is rejected'

  # ---- schema 2 non-ready keeps stale projection without recompute ----
  $schema2NonReadyRoot = Join-Path $tempRoot 'schema2-non-ready-stale'
  Set-RiskIndex $schema2NonReadyRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema2NonReadyCard = Get-Schema2Metadata -Id 'T-S2-BLOCKED-01' -DispatchState 'blocked' -ExpectedPaths @('tools/foo.ps1') -Matched @('EXP-AUTO-001', 'EXP-BS-003') -StateReason '等待外部输入'
  Set-Card $schema2NonReadyRoot $schema2NonReadyCard
  Set-Queue $schema2NonReadyRoot @()
  Set-Backlog $schema2NonReadyRoot @($schema2NonReadyCard)
  $schema2NonReady = Invoke-Checker $schema2NonReadyRoot
  Assert-True ($schema2NonReady.ExitCode -eq 0) "schema 2 non-ready stale projection should pass: $($schema2NonReady.Output)"

  # ---- schema 2 completed archive is accepted without recompute (no batch migration) ----
  $schema2ArchiveRoot = Join-Path $tempRoot 'schema2-completed-archive'
  $schema2ArchiveFixture = New-Fixture $schema2ArchiveRoot
  Set-RiskIndex $schema2ArchiveRoot @((New-Experience -Id 'EXP-AUTO-001' -Paths @('tools/foo.ps1')))
  $schema2Completed = Get-Schema2Metadata -Id 'T-READY-01' -DispatchState 'completed' -Matched @('EXP-AUTO-001', 'EXP-BS-003')
  Set-Archive $schema2ArchiveRoot $schema2Completed
  Remove-Item -LiteralPath (Join-Path $schema2ArchiveRoot '开发管理/任务卡/T-READY-01.txt') -Force
  Set-Queue $schema2ArchiveRoot @()
  Set-Backlog $schema2ArchiveRoot @($schema2ArchiveFixture.Blocked)
  $schema2Archive = Invoke-Checker $schema2ArchiveRoot @('-TaskId', 'T-READY-01', '-Postcondition', 'CodexClosedOrNonReady', '-OutputJson')
  Assert-True ($schema2Archive.ExitCode -eq 0) "schema 2 completed archive should pass without recompute: $($schema2Archive.Output)"

  Write-Output 'test-check-task-cards: PASS'
} finally {
  if (Test-Path -LiteralPath $tempRoot) {
    $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'refusing to remove a non-temp test directory'
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
