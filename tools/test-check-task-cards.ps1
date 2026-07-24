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
    expectedPaths = @('simulations/BattleSim/Combat.cs', "开发管理/任务卡/$Id.txt")
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
    $copy[$key] = if ($Metadata[$key] -is [array]) { @($Metadata[$key]) } else { $Metadata[$key] }
  }
  $copy
}

function Set-Card {
  param([string]$Root, [hashtable]$Metadata, [string]$FileName = "$($Metadata.id).txt", [string[]]$Headings)
  $args = @{ Metadata = $Metadata }
  if ($PSBoundParameters.ContainsKey('Headings')) { $args.Headings = $Headings }
  Write-Utf8 (Join-Path $Root "开发管理/任务卡/$FileName") (Get-CardText @args)
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
    @{ Name = 'illegal schema version'; Expected = 'illegal schemaVersion'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.schemaVersion = 2; Set-Card $root $m } },
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
    @{ Name = 'route owner mismatch'; Expected = 'route/owner mismatch'; Change = { param($root, $f) $m = Copy-Metadata $f.Ready; $m.owner = 'claude'; Set-Card $root $m } },
    @{ Name = 'missing body heading'; Expected = 'missing body heading'; Change = { param($root, $f) Set-Card $root $f.Ready -Headings @('来源与当前边界') } },
    @{ Name = 'H1 mismatch'; Expected = 'H1 mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/任务卡/T-READY-01.txt'; (Get-Content -Raw $path).Replace('# T-READY-01 · 合法 ready 卡', '# bad') | Set-Content -LiteralPath $path -Encoding utf8 } },
    @{ Name = 'queue projection mismatch'; Expected = 'queue projection mismatch'; Change = { param($root, $f) $path = Join-Path $root '开发管理/当前任务队列.txt'; (Get-Content -Raw $path).Replace('codex_execute', 'codex_review') | Set-Content -LiteralPath $path -Encoding utf8 } },
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

  Write-Output 'test-check-task-cards: PASS'
} finally {
  if (Test-Path -LiteralPath $tempRoot) {
    $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'refusing to remove a non-temp test directory'
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
