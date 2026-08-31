#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'private-path-acl.ps1')

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Assert-Equal {
  param($Actual, $Expected, [string]$Message)
  if ($Actual -cne $Expected) { throw "$Message (actual=$Actual expected=$Expected)" }
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  $parent = Split-Path -Parent $Path
  [IO.Directory]::CreateDirectory($parent) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-TextDigest {
  param([string]$Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
}

function Assert-HourlyFailure {
  param([string]$Name, [string]$ExpectedCode, [scriptblock]$Action)
  try { & $Action } catch {
    Assert-True ($_.Exception.Message -ceq $ExpectedCode) "$Name returned $($_.Exception.Message) (expected $ExpectedCode)"
    return
  }
  throw "$Name should fail closed with $ExpectedCode"
}

function Import-SharedEntryFunctions {
  $target = Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'
  $tokens = $null; $errors = $null
  $ast = [Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$errors)
  Assert-True (@($errors).Count -eq 0) 'cannot parse invoke-hourly-owner.ps1'
  $names = @(
    'Stop-Hourly', 'Normalize-FullPath', 'Get-NormalizedTextDigestFromText', 'Get-NormalizedTextDigest',
    'Read-TaskMetadata', 'Read-RunTaskMetadata', 'Write-PrivateJson', 'Invoke-ExperiencePreflight'
  )
  foreach ($name in $names) {
    $matches = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $true))
    Assert-True ($matches.Count -eq 1) "missing shared-entry function: $name"
    $definition = $matches[0].Extent.Text -replace ('(?m)^function\s+' + [regex]::Escape($name) + '\b'), "function global:$name"
    Invoke-Expression $definition
  }
}

function New-CardText {
  param(
    [string]$Id,
    [string]$Route,
    [string]$OwnerValue,
    [string[]]$ExpectedPaths = @('tools/fake.ps1'),
    [string[]]$Matched = @(),
    [string[]]$Gates = @(),
    [string[]]$ExplicitRefs = @()
  )
  $meta = [ordered]@{
    schemaVersion = 2
    id = $Id
    title = "fixture $Id"
    priority = 'P2'
    route = $Route
    owner = $OwnerValue
    domain = 'automation'
    stage = 'implementation'
    dispatchState = 'ready'
    blockedBy = @()
    stateReason = 'fixture'
    expectedPaths = @($ExpectedPaths)
    sourceBacklog = '开发管理/任务列表/管理与自动化任务.txt'
    riskPreflight = [ordered]@{ explicitRefs = @($ExplicitRefs); matched = @($Matched); gates = @($Gates) }
  }
  $body = @("# $Id · fixture", '## 必查范围', '- fixture', '', '## 实施范围', '- fixture') -join "`n"
  @('---TASK-META---', ($meta | ConvertTo-Json -Depth 20), '---TASK-BODY---', $body) -join "`n"
}

function New-IndexJson {
  param([object[]]$Experiences = @(), [object[]]$Gates = @())
  ([ordered]@{ schemaVersion = 1; experiences = @($Experiences); gates = @($Gates) } | ConvertTo-Json -Depth 30 -Compress)
}

function New-Experience {
  param(
    [string]$Id,
    [string]$Level = 'notice',
    [string]$Trigger = 'path',
    [string[]]$Paths = @('tools/fake.ps1'),
    [string]$DetailRef = '',
    [string[]]$GateRefs = @()
  )
  [ordered]@{
    id = $Id; title = "风险 $Id"; preflightSummary = '开工前提示'; status = 'active'; level = $Level; triggerMode = $Trigger
    domains = @(); stages = @(); pathPatterns = @($Paths); textPatterns = @(); detailRef = $DetailRef; gateRefs = @($GateRefs); lastVerified = '2026-08-31'
  }
}

function New-ExperienceCard {
  param([string]$Id)
  @("# $Id · 风险", '', '## 状态', 'active', '', '## 开工前', '- 开工前正文。', '', '## 正确处理', '- 已验证顺序。') -join "`n"
}

function Set-Card {
  param([string]$Root, [string]$Id, [string]$Text)
  Write-Utf8 (Join-Path $Root "开发管理/任务卡/$Id.txt") $Text
}

function Set-Index {
  param([string]$Root, [string]$Json)
  Write-Utf8 (Join-Path $Root '开发管理/经验库/风险索引.json') $Json
}

function New-Run {
  param([string]$Id, [string]$TaskId, [string]$Route, [string]$Root, [string]$Digest)
  [pscustomobject]@{ runId = $Id; taskId = $TaskId; route = $Route; worktree = $Root; taskCardDigest = $Digest }
}

Import-SharedEntryFunctions

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('hourly-experience-preflight-' + [guid]::NewGuid().ToString('N'))
$stateRoot = Join-Path $tempRoot 'state'
$matcherPath = Join-Path $PSScriptRoot 'get-experience-risk-preflight.ps1'
$script:matcherPath = $matcherPath
$global:matcherPath = $matcherPath
$script:effectiveStateRoot = $stateRoot
$global:effectiveStateRoot = $stateRoot

try {
  [IO.Directory]::CreateDirectory($tempRoot) | Out-Null

  # ---- 1. Codex / DeepSeek 成功：共享入口写入绑定结果 ----
  $caseRoot = Join-Path $tempRoot 'success'
  $codexCardId = 'T-CODEX-01'
  $deepseekCardId = 'T-DEEPSEEK-01'
  Set-Index $caseRoot (New-IndexJson)
  Set-Card $caseRoot $codexCardId (New-CardText -Id $codexCardId -Route 'codex_execute' -OwnerValue 'codex')
  Set-Card $caseRoot $deepseekCardId (New-CardText -Id $deepseekCardId -Route 'external_execute' -OwnerValue 'deepseek')
  $script:root = $caseRoot
  $global:root = $caseRoot

  $codexDigest = Get-TextDigest (Join-Path $caseRoot "开发管理/任务卡/$codexCardId.txt")
  $deepseekDigest = Get-TextDigest (Join-Path $caseRoot "开发管理/任务卡/$deepseekCardId.txt")
  $indexDigest = Get-TextDigest (Join-Path $caseRoot '开发管理/经验库/风险索引.json')

  $script:Owner = 'codex'; $global:Owner = 'codex'
  $codexRun = New-Run -Id 'run-codex' -TaskId $codexCardId -Route 'codex_execute' -Root $caseRoot -Digest $codexDigest
  $codexPath = Invoke-ExperiencePreflight -Run $codexRun
  Assert-Equal ([IO.Path]::GetFileName($codexPath)) 'run-codex.json' 'Codex preflight result filename mismatch'
  Assert-PrivatePathAcl -Path $codexPath
  $codexResult = [IO.File]::ReadAllText($codexPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50
  Assert-Equal ([string]$codexResult.runId) 'run-codex' 'Codex preflight result runId mismatch'
  Assert-Equal ([string]$codexResult.taskId) $codexCardId 'Codex preflight result taskId mismatch'
  Assert-Equal ([string]$codexResult.taskCardDigest) $codexDigest 'Codex preflight result taskCardDigest mismatch'
  Assert-Equal ([string]$codexResult.indexDigest) $indexDigest 'Codex preflight result indexDigest mismatch'

  $script:Owner = 'deepseek'; $global:Owner = 'deepseek'
  $deepseekRun = New-Run -Id 'run-deepseek' -TaskId $deepseekCardId -Route 'external_execute' -Root $caseRoot -Digest $deepseekDigest
  $deepseekPath = Invoke-ExperiencePreflight -Run $deepseekRun
  Assert-Equal ([IO.Path]::GetFileName($deepseekPath)) 'run-deepseek.json' 'DeepSeek preflight result filename mismatch'
  Assert-PrivatePathAcl -Path $deepseekPath
  $deepseekResult = [IO.File]::ReadAllText($deepseekPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50
  Assert-Equal ([string]$deepseekResult.taskId) $deepseekCardId 'DeepSeek preflight result taskId mismatch'
  Assert-Equal ([string]$deepseekResult.taskCardDigest) $deepseekDigest 'DeepSeek preflight result taskCardDigest mismatch'

  # ---- 2. 过宽（超过 3 条 must_read） ----
  $overRoot = Join-Path $tempRoot 'overbroad'
  $overCardId = 'T-OVER-01'
  $overExps = @()
  for ($index = 1; $index -le 4; $index++) {
    $eid = 'EXP-AUTO-' + $index.ToString('D3')
    Write-Utf8 (Join-Path $overRoot "开发管理/经验库/经验卡/$eid.txt") (New-ExperienceCard -Id $eid)
    $overExps += (New-Experience -Id $eid -Level 'must_read' -DetailRef "开发管理/经验库/经验卡/$eid.txt#开工前")
  }
  Set-Index $overRoot (New-IndexJson -Experiences $overExps)
  Set-Card $overRoot $overCardId (New-CardText -Id $overCardId -Route 'external_execute' -OwnerValue 'deepseek')
  $script:root = $overRoot; $global:root = $overRoot
  $script:Owner = 'deepseek'; $global:Owner = 'deepseek'
  $overDigest = Get-TextDigest (Join-Path $overRoot "开发管理/任务卡/$overCardId.txt")
  $overRun = New-Run -Id 'run-over' -TaskId $overCardId -Route 'external_execute' -Root $overRoot -Digest $overDigest
  Assert-HourlyFailure -Name 'overbroad preflight' -ExpectedCode 'experience_preflight_overbroad' -Action { $null = Invoke-ExperiencePreflight -Run $overRun }

  # ---- 3. 缺指针（must_read 空 detailRef） ----
  $missingRoot = Join-Path $tempRoot 'missing-pointer'
  $missingCardId = 'T-MISSING-01'
  Set-Index $missingRoot (New-IndexJson -Experiences @((New-Experience -Id 'EXP-AUTO-001' -Level 'must_read' -DetailRef '')))
  Set-Card $missingRoot $missingCardId (New-CardText -Id $missingCardId -Route 'external_execute' -OwnerValue 'deepseek')
  $script:root = $missingRoot; $global:root = $missingRoot
  $script:Owner = 'deepseek'; $global:Owner = 'deepseek'
  $missingDigest = Get-TextDigest (Join-Path $missingRoot "开发管理/任务卡/$missingCardId.txt")
  $missingRun = New-Run -Id 'run-missing' -TaskId $missingCardId -Route 'external_execute' -Root $missingRoot -Digest $missingDigest
  Assert-HourlyFailure -Name 'missing body pointer preflight' -ExpectedCode 'experience_preflight_matcher_failed' -Action { $null = Invoke-ExperiencePreflight -Run $missingRun }

  # ---- 4. matcher 失败（非法索引 schema） ----
  $badIndexRoot = Join-Path $tempRoot 'matcher-failed'
  $badIndexCardId = 'T-BADIDX-01'
  Set-Index $badIndexRoot '{"schemaVersion":2,"experiences":[],"gates":[]}'
  Set-Card $badIndexRoot $badIndexCardId (New-CardText -Id $badIndexCardId -Route 'external_execute' -OwnerValue 'deepseek')
  $script:root = $badIndexRoot; $global:root = $badIndexRoot
  $script:Owner = 'deepseek'; $global:Owner = 'deepseek'
  $badIndexDigest = Get-TextDigest (Join-Path $badIndexRoot "开发管理/任务卡/$badIndexCardId.txt")
  $badIndexRun = New-Run -Id 'run-badindex' -TaskId $badIndexCardId -Route 'external_execute' -Root $badIndexRoot -Digest $badIndexDigest
  Assert-HourlyFailure -Name 'matcher failure preflight' -ExpectedCode 'experience_preflight_matcher_failed' -Action { $null = Invoke-ExperiencePreflight -Run $badIndexRun }

  # ---- 5. schema 2 投影失配 ----
  $projectionRoot = Join-Path $tempRoot 'projection-mismatch'
  $projectionCardId = 'T-PROJ-01'
  Set-Index $projectionRoot (New-IndexJson)
  Set-Card $projectionRoot $projectionCardId (New-CardText -Id $projectionCardId -Route 'external_execute' -OwnerValue 'deepseek' -Matched @('EXP-AUTO-999'))
  $script:root = $projectionRoot; $global:root = $projectionRoot
  $script:Owner = 'deepseek'; $global:Owner = 'deepseek'
  $projectionDigest = Get-TextDigest (Join-Path $projectionRoot "开发管理/任务卡/$projectionCardId.txt")
  $projectionRun = New-Run -Id 'run-proj' -TaskId $projectionCardId -Route 'external_execute' -Root $projectionRoot -Digest $projectionDigest
  Assert-HourlyFailure -Name 'projection mismatch preflight' -ExpectedCode 'experience_preflight_projection_mismatch' -Action { $null = Invoke-ExperiencePreflight -Run $projectionRun }

  # ---- 6. adapter：queue maintenance 无参数，非 queue maintenance 必填 ----
  . (Join-Path $PSScriptRoot 'hourly-owner-adapter.ps1')
  $codexAdapter = Get-HourlyOwnerAdapter -Owner codex -Model 'gpt-test' -ToolsRoot $PSScriptRoot
  $deepseekAdapter = Get-HourlyOwnerAdapter -Owner deepseek -Model $null -ToolsRoot $PSScriptRoot
  $qmArgs = Get-HourlyCandidateArguments -Adapter $codexAdapter -Run ([pscustomobject]@{ route = 'queue_maintenance'; worktree = 'C:\fixture'; taskId = 'QUEUE-MAINTENANCE'; runId = 'R-QM' }) -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath $null -PreflightResultPath $null
  Assert-True ($qmArgs -notcontains '-PreflightResultPath') 'queue maintenance arguments must not include the preflight result path'
  $deepArgs = Get-HourlyCandidateArguments -Adapter $deepseekAdapter -Run ([pscustomobject]@{ route = 'external_execute'; worktree = 'C:\fixture'; taskId = 'T-2'; runId = 'R-2' }) -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath $null -PreflightResultPath 'C:\state\preflight.json'
  Assert-True ($deepArgs -contains '-PreflightResultPath' -and $deepArgs -contains 'C:\state\preflight.json') 'external_execute arguments must include the preflight result path'
  $threwWithoutPath = $false
  try { $null = Get-HourlyCandidateArguments -Adapter $codexAdapter -Run ([pscustomobject]@{ route = 'codex_execute'; worktree = 'C:\fixture'; taskId = 'T-3'; runId = 'R-3' }) -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath $null -PreflightResultPath $null } catch { $threwWithoutPath = $true }
  Assert-True $threwWithoutPath 'non-queue-maintenance candidate without a preflight result path must fail'

  Write-Output 'test-hourly-experience-preflight: PASS'
} finally {
  if (Test-Path -LiteralPath $tempRoot) {
    $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'refusing to remove a non-temp test directory'
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
