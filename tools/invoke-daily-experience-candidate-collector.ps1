#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [DateTimeOffset]$Now = [DateTimeOffset]::Now,
  [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:root = $null
$script:baseCommit = $null
$script:worktree = $null
$script:branch = $null

function Stop-Collector {
  param([string]$DetailCode)
  $exception = [InvalidOperationException]::new($DetailCode)
  $exception.Data['DetailCode'] = $DetailCode
  throw $exception
}

function Read-Utf8Text {
  param([string]$Path)
  try {
    [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  } catch {
    Stop-Collector 'daily_collector_invalid_utf8'
  }
}

function Write-Utf8Text {
  param([string]$Path, [string]$Text)
  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-NormalizedDigest {
  param([string]$Text)
  $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($normalized))).ToLowerInvariant()
}

function Invoke-Native {
  param([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory)
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = $FileName
  $start.WorkingDirectory = $WorkingDirectory
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  if (-not $process.Start()) { Stop-Collector 'daily_collector_process_start_failed' }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $result = [pscustomobject]@{
    ExitCode = $process.ExitCode
    Stdout = $stdoutTask.GetAwaiter().GetResult().TrimEnd("`r", "`n")
    Stderr = $stderrTask.GetAwaiter().GetResult().TrimEnd("`r", "`n")
  }
  $process.Dispose()
  $result
}

function Invoke-Git {
  param([string]$Root, [string[]]$Arguments, [string]$DetailCode = 'daily_collector_git_failed')
  $result = Invoke-Native -FileName 'git' -Arguments (@('-C', $Root) + $Arguments) -WorkingDirectory $Root
  if ($result.ExitCode -ne 0) { Stop-Collector $DetailCode }
  [string]$result.Stdout
}

function Get-TaskDocument {
  param([string]$Text, [string]$ExpectedId, [bool]$RequireCompleted)
  $metaMarkers = [regex]::Matches($Text, '(?m)^---TASK-META---\r?$')
  $bodyMarkers = [regex]::Matches($Text, '(?m)^---TASK-BODY---\r?$')
  if ($metaMarkers.Count -ne 1 -or $bodyMarkers.Count -ne 1 -or $metaMarkers[0].Index -ge $bodyMarkers[0].Index) {
    Stop-Collector 'daily_collector_source_invalid'
  }
  $jsonText = $Text.Substring(
    $metaMarkers[0].Index + $metaMarkers[0].Length,
    $bodyMarkers[0].Index - $metaMarkers[0].Index - $metaMarkers[0].Length
  ).Trim()
  try { $metadata = $jsonText | ConvertFrom-Json -Depth 100 } catch { Stop-Collector 'daily_collector_source_invalid' }
  if ($null -eq $metadata -or [string]$metadata.id -cne $ExpectedId) { Stop-Collector 'daily_collector_source_invalid' }
  if ($RequireCompleted -and [string]$metadata.dispatchState -cne 'completed') { return $null }
  [pscustomobject]@{ Metadata = $metadata; Body = $Text.Substring($bodyMarkers[0].Index + $bodyMarkers[0].Length) }
}

function Get-CandidateBlocks {
  param([string]$Text, [string]$SourceTaskId)
  $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
  $lines = @($normalized -split "`n")
  $sections = @()
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -ceq '## 经验候选') { $sections += $i } }
  if ($sections.Count -eq 0) { return @() }
  if ($sections.Count -ne 1) { Stop-Collector 'daily_collector_candidate_invalid' }
  $sectionStart = $sections[0]
  $sectionEnd = $lines.Count
  for ($i = $sectionStart + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -cmatch '^##\s') { $sectionEnd = $i; break }
  }
  $headings = @()
  for ($i = $sectionStart + 1; $i -lt $sectionEnd; $i++) { if ($lines[$i] -cmatch '^###\s') { $headings += $i } }
  if ($headings.Count -eq 0) { Stop-Collector 'daily_collector_candidate_invalid' }
  $expectedFields = @(
    [pscustomobject]@{ Name = 'collectionState'; Pattern = '^- collectionState:\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'promotionTaskId'; Pattern = '^- promotionTaskId:\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'resolution'; Pattern = '^- resolution:\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'experienceId'; Pattern = '^- experienceId:\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'domain'; Pattern = '^- 经验领域：\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'symptom'; Pattern = '^- 现象：\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'rootCause'; Pattern = '^- 已证实根因：\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'reuseScope'; Pattern = '^- 可能复用范围：\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'evidence'; Pattern = '^- 证据：\s*(?<value>.*)$' },
    [pscustomobject]@{ Name = 'gatePossible'; Pattern = '^- 门禁可能：\s*(?<value>.*)$' }
  )
  $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $result = [Collections.Generic.List[object]]::new()
  for ($h = 0; $h -lt $headings.Count; $h++) {
    $start = $headings[$h]
    $end = if ($h + 1 -lt $headings.Count) { $headings[$h + 1] } else { $sectionEnd }
    $candidateId = $lines[$start].Substring(4)
    if ($candidateId -cne $candidateId.Trim() -or $candidateId -cnotmatch '^[A-Z0-9][A-Z0-9._-]{2,160}$' -or -not $seen.Add($candidateId)) {
      Stop-Collector 'daily_collector_candidate_invalid'
    }
    $contentIndexes = @()
    for ($i = $start + 1; $i -lt $end; $i++) { if (-not [string]::IsNullOrWhiteSpace($lines[$i])) { $contentIndexes += $i } }
    if ($contentIndexes.Count -ne $expectedFields.Count) { Stop-Collector 'daily_collector_candidate_invalid' }
    $values = [ordered]@{}
    for ($i = 0; $i -lt $expectedFields.Count; $i++) {
      $match = [regex]::Match($lines[$contentIndexes[$i]], $expectedFields[$i].Pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
      if (-not $match.Success) { Stop-Collector 'daily_collector_candidate_invalid' }
      $value = $match.Groups['value'].Value
      if ($value -cne $value.Trim()) { Stop-Collector 'daily_collector_candidate_invalid' }
      $values[$expectedFields[$i].Name] = $value
    }
    if ($values.collectionState -cnotin @('provisional', 'pending', 'collected') -or
        $values.resolution -cnotin @('unresolved', 'promoted', 'duplicate', 'rejected') -or
        $values.domain -cnotin @('automation', 'management', 'unity', 'battlesim', 'data', 'design', 'content', 'numeric') -or
        $values.gatePossible -cnotin @('yes', 'no') -or
        @(@($values.symptom, $values.rootCause, $values.reuseScope, $values.evidence) | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0) {
      Stop-Collector 'daily_collector_candidate_invalid'
    }
    if ($values.collectionState -cin @('provisional', 'pending')) {
      if (-not [string]::IsNullOrEmpty($values.promotionTaskId) -or $values.resolution -cne 'unresolved' -or -not [string]::IsNullOrEmpty($values.experienceId)) {
        Stop-Collector 'daily_collector_candidate_invalid'
      }
    } else {
      if ([string]::IsNullOrWhiteSpace($values.promotionTaskId)) { Stop-Collector 'daily_collector_candidate_invalid' }
      if ($values.resolution -cin @('promoted', 'duplicate')) {
        if ($values.experienceId -cnotmatch '^EXP-(UNITY|BS|DATA|CONTENT|MGMT|AUTO)-\d{3}$') { Stop-Collector 'daily_collector_candidate_invalid' }
      } elseif (-not [string]::IsNullOrEmpty($values.experienceId)) {
        Stop-Collector 'daily_collector_candidate_invalid'
      }
    }
    $blockText = ($lines[$start..($end - 1)] -join "`n").TrimEnd("`n")
    $result.Add([pscustomobject]@{
      SourceTaskId = $SourceTaskId
      CandidateId = $candidateId
      Digest = Get-NormalizedDigest $blockText
      StartLine = $start
      EndLine = $end
      StateLine = $contentIndexes[0]
      PromotionLine = $contentIndexes[1]
      Values = [pscustomobject]$values
    })
  }
  @($result)
}

function Get-HongKongTimeZone {
  foreach ($id in @('Asia/Hong_Kong', 'China Standard Time')) {
    try { return [TimeZoneInfo]::FindSystemTimeZoneById($id) } catch { }
  }
  Stop-Collector 'daily_collector_timezone_unavailable'
}

function Get-ArchiveCandidatesAtCommit {
  param([string]$Commit, [DateTime]$LatestDate)
  $pathsText = Invoke-Git -Root $script:root -Arguments @('-c', 'core.quotepath=false', 'ls-tree', '-r', '--name-only', $Commit, '--', '开发管理/任务归档')
  $paths = @($pathsText -split "`r?`n" | Where-Object { $_ -cmatch '^开发管理/任务归档/[^/]+\.txt$' } | Sort-Object)
  $items = [Collections.Generic.List[object]]::new()
  foreach ($path in $paths) {
    $sourceTaskId = [IO.Path]::GetFileNameWithoutExtension($path)
    $text = Invoke-Git -Root $script:root -Arguments @('show', "$Commit`:$path")
    if ($text -cnotmatch '(?m)^## 经验候选\r?$') { continue }
    $document = Get-TaskDocument -Text $text -ExpectedId $sourceTaskId -RequireCompleted $true
    if ($null -eq $document) { continue }
    $dateText = Invoke-Git -Root $script:root -Arguments @('log', '-1', '--format=%cI', $Commit, '--', $path)
    $archiveMoment = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($dateText, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$archiveMoment)) {
      Stop-Collector 'daily_collector_archive_date_invalid'
    }
    $archiveDate = [TimeZoneInfo]::ConvertTime($archiveMoment, (Get-HongKongTimeZone)).Date
    if ($archiveDate -gt $LatestDate) { continue }
    foreach ($candidate in @(Get-CandidateBlocks -Text $text -SourceTaskId $sourceTaskId)) {
      if ([string]$candidate.Values.collectionState -cne 'pending') { continue }
      $items.Add([pscustomobject]@{
        SourcePath = $path
        SourceTaskId = $sourceTaskId
        CandidateId = [string]$candidate.CandidateId
        Digest = [string]$candidate.Digest
        Values = $candidate.Values
      })
    }
  }
  @($items | Sort-Object SourceTaskId, CandidateId)
}

function Get-DomainProjection {
  param([string]$Domain)
  $map = @{
    automation = @('automation', 'AUTO', $null)
    management = @('management', 'MGMT', $null)
    unity = @('unity', 'UNITY', $null)
    battlesim = @('battlesim', 'BS', $null)
    data = @('data', 'DATA', $null)
    design = @('management', 'MGMT', 'M-EXP-EXT-DESIGN-01')
    content = @('content', 'CONTENT', 'M-EXP-EXT-CONTENT-01')
    numeric = @('battlesim', 'BS', 'M-EXP-EXT-NUMERIC-01')
  }
  $value = $map[$Domain]
  if ($null -eq $value) { Stop-Collector 'daily_collector_candidate_invalid' }
  [pscustomobject]@{ TaskDomain = $value[0]; ExperienceCode = $value[1]; Blocker = $value[2] }
}

function Get-PromotionTaskId {
  param([string]$SourceTaskId, [string]$CandidateId)
  $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes("$SourceTaskId`n$CandidateId")))
  'M-EXP-PROMOTE-' + $hash.Substring(0, 12)
}

function Get-ReservedExperienceNumbers {
  param([string]$Commit)
  $result = @{}
  foreach ($code in @('AUTO', 'MGMT', 'UNITY', 'BS', 'DATA', 'CONTENT')) { $result[$code] = [Collections.Generic.HashSet[int]]::new() }
  $indexText = Invoke-Git -Root $script:root -Arguments @('show', "$Commit`:开发管理/经验库/风险索引.json")
  try { $index = $indexText | ConvertFrom-Json -Depth 100 } catch { Stop-Collector 'daily_collector_index_invalid' }
  if ($null -eq $index -or -not ($index.experiences -is [Array])) { Stop-Collector 'daily_collector_index_invalid' }
  foreach ($experience in @($index.experiences)) {
    $match = [regex]::Match([string]$experience.id, '^EXP-(?<code>UNITY|BS|DATA|CONTENT|MGMT|AUTO)-(?<number>\d{3})$')
    if (-not $match.Success) { Stop-Collector 'daily_collector_index_invalid' }
    [void]$result[$match.Groups['code'].Value].Add([int]$match.Groups['number'].Value)
  }
  $cardPathsText = Invoke-Git -Root $script:root -Arguments @('-c', 'core.quotepath=false', 'ls-tree', '-r', '--name-only', $Commit, '--', '开发管理/任务卡', '开发管理/任务归档')
  foreach ($path in @($cardPathsText -split "`r?`n" | Where-Object { $_ -cmatch '^开发管理/(任务卡|任务归档)/[^/]+\.txt$' })) {
    $taskId = [IO.Path]::GetFileNameWithoutExtension($path)
    $text = Invoke-Git -Root $script:root -Arguments @('show', "$Commit`:$path")
    $document = Get-TaskDocument -Text $text -ExpectedId $taskId -RequireCompleted $false
    foreach ($expectedPath in @($document.Metadata.expectedPaths | ForEach-Object { [string]$_ })) {
      $match = [regex]::Match($expectedPath, '^开发管理/经验库/经验卡/EXP-(?<code>UNITY|BS|DATA|CONTENT|MGMT|AUTO)-(?<number>\d{3})\.txt$')
      if ($match.Success) { [void]$result[$match.Groups['code'].Value].Add([int]$match.Groups['number'].Value) }
    }
  }
  $result
}

function New-PromotionCardText {
  param([object]$Item, [object]$RiskPreflight)
  [object[]]$blockedBy = @()
  if (-not [string]::IsNullOrEmpty([string]$Item.Blocker)) { $blockedBy = [object[]]@([string]$Item.Blocker) }
  $state = if ($blockedBy.Count -eq 0) { 'ready' } else { 'blocked' }
  $reason = if ($state -ceq 'ready') {
    "每日收集器已从 $($Item.SourceTaskId)/$($Item.CandidateId) 建立独立整理任务；等待 Codex 进行经验语义核验。"
  } else {
    "每日收集器已收集 $($Item.SourceTaskId)/$($Item.CandidateId)；等待具名前置 $($Item.Blocker) 完成后再进行经验语义核验。"
  }
  $metadata = [ordered]@{
    schemaVersion = 2
    id = [string]$Item.TaskId
    title = "经验候选整理：$($Item.CandidateId)"
    priority = 'P2'
    route = 'codex_execute'
    owner = 'codex'
    domain = [string]$Item.TaskDomain
    stage = 'implementation'
    dispatchState = $state
    blockedBy = $blockedBy
    stateReason = $reason
    expectedPaths = [object[]]@($Item.ExpectedPaths)
    sourceBacklog = '开发管理/任务列表/管理与自动化任务.txt'
    riskPreflight = [ordered]@{
      explicitRefs = [object[]]@($RiskPreflight.explicitRefs)
      matched = [object[]]@($RiskPreflight.matched)
      gates = [object[]]@($RiskPreflight.gates)
    }
  }
  $json = $metadata | ConvertTo-Json -Depth 20
  @(
    '---TASK-META---', $json, '---TASK-BODY---', "# $($Item.TaskId) · 经验候选整理：$($Item.CandidateId)", '',
    '## 来源与当前边界', '',
    "- 单一结果：独立核验来源候选，确定 promoted／duplicate／rejected 之一，并按真实结论原子回链来源。",
    "- 来源任务：$($Item.SourceTaskId)", "- 候选 ID：$($Item.CandidateId)", "- 来源候选 digest：$($Item.Digest)",
    "- 原经验领域：$($Item.OriginalDomain)", "- 预留经验 ID：$($Item.ExperienceId)", '',
    '## 必查范围', '',
    "- ``$($Item.SourcePath)`` 中 ``$($Item.CandidateId)`` 的完整候选字段及其直接证据。",
    '- `开发管理/经验库/风险索引.json` 的现有经验、状态、触发和门禁合同。',
    '- `docs/superpowers/specs/2026-08-24-preventive-experience-memory-system-design.md#独立整理任务`。', '',
    '## 实施范围', '',
    "- 核验候选是否可泛化、是否与现有 active 经验重复，以及适用／排除／失效边界；本卡是唯一语义裁决点。",
    "- promoted 时使用预留 ID ``$($Item.ExperienceId)`` 创建正式经验卡并登记索引；duplicate 时只回链既有经验；rejected 时记录精确原因。",
    "- 完成时更新来源候选的 resolution／experienceId，归档本卡并同步队列与管理 backlog。", '',
    '## 禁止项', '',
    '- 不合并其他候选，不修改未在 expectedPaths 中冻结的既有经验，不顺带新增门禁或开放领域。',
    '- 不更换预留经验 ID，不把证据不足的候选伪造成正式经验。', '',
    '## 验证', '',
    '- 运行 `tools/check-task-cards.ps1`，核对来源／整理卡双向回链、风险索引 schema 和精确 changed paths。',
    '- 对本轮路径运行 `tools/check-pending-whitespace.ps1`、`tools/check-review-text.ps1` 和 `git diff --cached --check`。', '',
    '## 完成条件', '',
    '- 候选以 promoted／duplicate／rejected 之一真实闭环；来源候选、整理归档、风险索引／经验卡按所选终态一致。', '',
    '## 停止条件', '',
    '- 直接证据不足、需要修改未授权既有经验、预留 ID 发生冲突，或无法在一个正式提交中完成语义结论与回链。', ''
  ) -join "`n"
}

function Add-TableRows {
  param([string]$Path, [string[]]$Rows)
  $text = Read-Utf8Text $Path
  $eol = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $lines = [Collections.Generic.List[string]]::new()
  foreach ($line in @($text.Replace("`r`n", "`n").Replace("`r", "`n") -split "`n")) { $lines.Add($line) }
  $separator = -1
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -cmatch '^\|[-:|]+\|$') { $separator = $i; break } }
  if ($separator -lt 0) { Stop-Collector 'daily_collector_projection_invalid' }
  $insert = $separator + 1
  while ($insert -lt $lines.Count -and $lines[$insert].TrimStart().StartsWith('|')) { $insert++ }
  for ($i = $Rows.Count - 1; $i -ge 0; $i--) { $lines.Insert($insert, $Rows[$i]) }
  Write-Utf8Text -Path $Path -Text (($lines -join "`n").Replace("`n", $eol))
}

function Add-QueueRows {
  param([string]$Path, [object[]]$Items)
  $text = Read-Utf8Text $Path
  $eol = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $lines = [Collections.Generic.List[string]]::new()
  foreach ($line in @($text.Replace("`r`n", "`n").Replace("`r", "`n") -split "`n")) { $lines.Add($line) }
  $separator = -1
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -cmatch '^\|[-:|]+\|$') { $separator = $i; break } }
  if ($separator -lt 0) { Stop-Collector 'daily_collector_projection_invalid' }
  $insert = $separator + 1
  while ($insert -lt $lines.Count -and $lines[$insert].TrimStart().StartsWith('|')) {
    $cells = @($lines[$insert].Trim([char]'|').Split([char]'|') | ForEach-Object { $_.Trim() })
    if ($cells.Count -ne 8) { Stop-Collector 'daily_collector_projection_invalid' }
    if ([int]$cells[3].Substring(1) -gt 2) { break }
    $insert++
  }
  $rows = @($Items | Sort-Object TaskId | ForEach-Object {
    "| $($_.TaskId) | codex_execute | codex | P2 | $($_.TaskDomain) | implementation | 经验候选整理：$($_.CandidateId) | 开发管理/任务卡/$($_.TaskId).txt |"
  })
  for ($i = $rows.Count - 1; $i -ge 0; $i--) { $lines.Insert($insert, $rows[$i]) }
  Write-Utf8Text -Path $Path -Text (($lines -join "`n").Replace("`n", $eol))
}

function Invoke-Validation {
  param([string]$Root, [string[]]$ChangedPaths)
  $checks = @(
    @('tools/check-task-cards.ps1', @('-RepositoryRoot', $Root, '-OutputJson')),
    @('tools/check-review-text.ps1', @('-Paths', '开发管理')),
    @('tools/check-pending-whitespace.ps1', @('-ExpectedPaths', ($ChangedPaths -join '|')))
  )
  foreach ($check in $checks) {
    $scriptPath = Join-Path $Root ([string]$check[0])
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) { Stop-Collector 'daily_collector_validation_unavailable' }
    $result = Invoke-Native -FileName 'pwsh' -Arguments (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath) + [string[]]$check[1]) -WorkingDirectory $Root
    if ($result.ExitCode -ne 0) { Stop-Collector 'daily_collector_validation_failed' }
  }
  $diff = Invoke-Native -FileName 'git' -Arguments @('-C', $Root, '-c', 'core.whitespace=-blank-at-eol', 'diff', '--check') -WorkingDirectory $Root
  if ($diff.ExitCode -ne 0) { Stop-Collector 'daily_collector_validation_failed' }
}

function Write-Terminal {
  param([object]$Result, [int]$ExitCode)
  [Console]::Out.WriteLine(($Result | ConvertTo-Json -Compress -Depth 20))
  exit $ExitCode
}

try {
  $script:root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RepositoryRoot).Path).TrimEnd('\', '/')
  $gitRoot = (Invoke-Git -Root $script:root -Arguments @('rev-parse', '--show-toplevel')).Trim()
  if (-not [IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/').Equals($script:root, [StringComparison]::OrdinalIgnoreCase)) { Stop-Collector 'daily_collector_repository_invalid' }
  if ((Invoke-Git -Root $script:root -Arguments @('branch', '--show-current')).Trim() -cne 'master') { Stop-Collector 'daily_collector_main_branch_invalid' }
  $script:baseCommit = (Invoke-Git -Root $script:root -Arguments @('rev-parse', 'HEAD')).Trim()
  $hongKongNow = [TimeZoneInfo]::ConvertTime($Now, (Get-HongKongTimeZone))
  $latestDate = $hongKongNow.Date.AddDays(-1)
  $pending = @(Get-ArchiveCandidatesAtCommit -Commit $script:baseCommit -LatestDate $latestDate)
  if ($pending.Count -eq 0) {
    Write-Terminal -Result ([ordered]@{ status = 'no_candidate'; category = 'no_candidate'; detailCode = 'no_pending_experience_candidates'; candidateCount = 0; baseCommit = $script:baseCommit }) -ExitCode 0
  }

  $treePathsText = Invoke-Git -Root $script:root -Arguments @('-c', 'core.quotepath=false', 'ls-tree', '-r', '--name-only', $script:baseCommit)
  $treePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($path in @($treePathsText -split "`r?`n" | Where-Object { $_ })) { [void]$treePaths.Add($path) }
  $reserved = Get-ReservedExperienceNumbers -Commit $script:baseCommit
  $items = [Collections.Generic.List[object]]::new()
  foreach ($candidate in $pending) {
    $projection = Get-DomainProjection -Domain ([string]$candidate.Values.domain)
    $taskId = Get-PromotionTaskId -SourceTaskId $candidate.SourceTaskId -CandidateId $candidate.CandidateId
    $activePath = "开发管理/任务卡/$taskId.txt"; $archivePath = "开发管理/任务归档/$taskId.txt"
    if ($treePaths.Contains($activePath) -or $treePaths.Contains($archivePath)) { Stop-Collector 'daily_collector_task_id_collision' }
    $number = 0
    for ($i = 1; $i -le 999; $i++) { if (-not $reserved[$projection.ExperienceCode].Contains($i)) { $number = $i; break } }
    if ($number -eq 0) { Stop-Collector 'daily_collector_experience_id_exhausted' }
    [void]$reserved[$projection.ExperienceCode].Add($number)
    $experienceId = 'EXP-{0}-{1:D3}' -f $projection.ExperienceCode, $number
    $experiencePath = "开发管理/经验库/经验卡/$experienceId.txt"
    if ($treePaths.Contains($experiencePath)) { Stop-Collector 'daily_collector_experience_id_collision' }
    $expectedPaths = [object[]]@(
      $candidate.SourcePath, '开发管理/经验库/风险索引.json', $experiencePath, $activePath, $archivePath,
      '开发管理/任务列表/管理与自动化任务.txt', '开发管理/当前任务队列.txt'
    )
    $items.Add([pscustomobject]@{
      SourcePath = $candidate.SourcePath; SourceTaskId = $candidate.SourceTaskId; CandidateId = $candidate.CandidateId
      Digest = $candidate.Digest; OriginalDomain = [string]$candidate.Values.domain; TaskDomain = $projection.TaskDomain
      ExperienceCode = $projection.ExperienceCode; Blocker = $projection.Blocker; TaskId = $taskId; ExperienceId = $experienceId
      ExperiencePath = $experiencePath; ActivePath = $activePath; ArchivePath = $archivePath; ExpectedPaths = $expectedPaths
    })
  }

  $runId = [guid]::NewGuid().ToString('D')
  $runRoot = [IO.Path]::GetFullPath((Join-Path $script:root ".worktrees/daily-experience-candidate-collector/$runId")).TrimEnd('\', '/')
  $approvedRoot = [IO.Path]::GetFullPath((Join-Path $script:root '.worktrees/daily-experience-candidate-collector')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $runRoot.StartsWith($approvedRoot, [StringComparison]::OrdinalIgnoreCase)) { Stop-Collector 'daily_collector_worktree_invalid' }
  $script:worktree = $runRoot
  $script:branch = "codex/daily-experience-candidate-collector/$runId"
  if (Test-Path -LiteralPath $script:worktree) { Stop-Collector 'daily_collector_worktree_collision' }
  $worktreeAdd = Invoke-Native -FileName 'git' -Arguments @('-C', $script:root, 'worktree', 'add', '-b', $script:branch, '--', $script:worktree, $script:baseCommit) -WorkingDirectory $script:root
  if ($worktreeAdd.ExitCode -ne 0) { Stop-Collector 'daily_collector_worktree_create_failed' }

  $bySource = @($items | Group-Object SourcePath)
  foreach ($group in $bySource) {
    $sourcePath = [string]$group.Name
    $fullPath = Join-Path $script:worktree $sourcePath
    $text = Read-Utf8Text $fullPath
    $sourceId = [IO.Path]::GetFileNameWithoutExtension($sourcePath)
    try {
      $document = Get-TaskDocument -Text $text -ExpectedId $sourceId -RequireCompleted $true
      $blocks = @(Get-CandidateBlocks -Text $text -SourceTaskId $sourceId)
    } catch {
      Stop-Collector 'daily_collector_source_changed'
    }
    if ($null -eq $document) { Stop-Collector 'daily_collector_source_changed' }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($line in @($text.Replace("`r`n", "`n").Replace("`r", "`n") -split "`n")) { $lines.Add($line) }
    foreach ($item in @($group.Group)) {
      $matches = @($blocks | Where-Object { $_.CandidateId -ceq $item.CandidateId })
      if ($matches.Count -ne 1 -or [string]$matches[0].Digest -cne [string]$item.Digest -or [string]$matches[0].Values.collectionState -cne 'pending' -or -not [string]::IsNullOrEmpty([string]$matches[0].Values.promotionTaskId)) {
        Stop-Collector 'daily_collector_source_changed'
      }
      $lines[$matches[0].StateLine] = '- collectionState: collected'
      $lines[$matches[0].PromotionLine] = "- promotionTaskId: $($item.TaskId)"
    }
    $eol = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    Write-Utf8Text -Path $fullPath -Text (($lines -join "`n").Replace("`n", $eol))
  }

  foreach ($item in $items) {
    $risk = [pscustomobject]@{ explicitRefs = [object[]]@(); matched = [object[]]@(); gates = [object[]]@() }
    $cardPath = Join-Path $script:worktree $item.ActivePath
    Write-Utf8Text -Path $cardPath -Text (New-PromotionCardText -Item $item -RiskPreflight $risk)
    $preflight = Invoke-Native -FileName 'pwsh' -Arguments @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $script:worktree 'tools/get-experience-risk-preflight.ps1'),
      '-RepositoryRoot', $script:worktree, '-TaskId', $item.TaskId
    ) -WorkingDirectory $script:worktree
    if ($preflight.ExitCode -ne 0) { Stop-Collector 'daily_collector_preflight_failed' }
    try { $preflightResult = $preflight.Stdout | ConvertFrom-Json -Depth 100 } catch { Stop-Collector 'daily_collector_preflight_failed' }
    if ([string]$preflightResult.status -cne 'ok' -or [string]$preflightResult.taskId -cne [string]$item.TaskId) { Stop-Collector 'daily_collector_preflight_failed' }
    $risk = [pscustomobject]@{ explicitRefs = [object[]]@(); matched = [object[]]@($preflightResult.matched); gates = [object[]]@($preflightResult.gates) }
    Write-Utf8Text -Path $cardPath -Text (New-PromotionCardText -Item $item -RiskPreflight $risk)
  }

  $backlogPath = Join-Path $script:worktree '开发管理/任务列表/管理与自动化任务.txt'
  $backlogRows = @($items | Sort-Object TaskId | ForEach-Object {
    $state = if ([string]::IsNullOrEmpty([string]$_.Blocker)) { '已排队' } else { '阻塞' }
    $blocker = if ([string]::IsNullOrEmpty([string]$_.Blocker)) { '—' } else { [string]$_.Blocker }
    "| $($_.TaskId) | P2 | codex | $state | $blocker | 经验候选整理：$($_.CandidateId) | 开发管理/任务卡/$($_.TaskId).txt |"
  })
  Add-TableRows -Path $backlogPath -Rows $backlogRows
  $readyItems = @($items | Where-Object { [string]::IsNullOrEmpty([string]$_.Blocker) })
  if ($readyItems.Count -gt 0) { Add-QueueRows -Path (Join-Path $script:worktree '开发管理/当前任务队列.txt') -Items $readyItems }

  foreach ($group in $bySource) {
    $sourcePath = [string]$group.Name
    $updatedText = Read-Utf8Text (Join-Path $script:worktree $sourcePath)
    $updatedBlocks = @(Get-CandidateBlocks -Text $updatedText -SourceTaskId ([IO.Path]::GetFileNameWithoutExtension($sourcePath)))
    foreach ($item in @($group.Group)) {
      $updated = @($updatedBlocks | Where-Object { $_.CandidateId -ceq $item.CandidateId })
      if ($updated.Count -ne 1 -or [string]$updated[0].Values.collectionState -cne 'collected' -or [string]$updated[0].Values.promotionTaskId -cne [string]$item.TaskId) {
        Stop-Collector 'daily_collector_source_update_failed'
      }
    }
  }

  $expectedChanged = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($item in $items) { [void]$expectedChanged.Add([string]$item.SourcePath); [void]$expectedChanged.Add([string]$item.ActivePath) }
  [void]$expectedChanged.Add('开发管理/任务列表/管理与自动化任务.txt')
  if ($readyItems.Count -gt 0) { [void]$expectedChanged.Add('开发管理/当前任务队列.txt') }
  $changedText = Invoke-Git -Root $script:worktree -Arguments @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames')
  $untrackedText = Invoke-Git -Root $script:worktree -Arguments @('-c', 'core.quotepath=false', 'ls-files', '--others', '--exclude-standard')
  $changedPaths = @(("$changedText`n$untrackedText") -split "`r?`n" | Where-Object { $_ } | Sort-Object -Unique)
  if ($changedPaths.Count -ne $expectedChanged.Count -or @($changedPaths | Where-Object { -not $expectedChanged.Contains($_) }).Count -ne 0) {
    Stop-Collector 'daily_collector_changed_paths_invalid'
  }
  Invoke-Validation -Root $script:worktree -ChangedPaths $changedPaths

  $resultText = "问题=存在 $($items.Count) 条待收集经验候选；完成=逐候选建立整理卡并在同一提交回链来源"
  $impactText = '影响=候选进入独立语义整理流程；边界=未修改风险索引或创建正式经验'
  $verifyText = '验证=任务卡、审核文本、空白和差异检查通过；后续=由对应 Codex 整理任务独立裁决'
  $plainText = "发生=收集了 $($items.Count) 条经验候选；影响=已建立独立整理任务；需要=无需人工处理"
  $finalizer = Invoke-Native -FileName 'pwsh' -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $script:worktree 'tools/automation-finalize-commit.ps1'),
    '-RepositoryRoot', $script:worktree, '-ExpectedPaths', ($changedPaths -join '|'),
    '-CommitMessage', 'automation: collect daily experience candidates', '-RequireAutomationMetadata',
    '-AutomationTask', 'DAILY-EXPERIENCE-COLLECTOR', '-AutomationState', 'completed',
    '-AutomationResult', $resultText, '-AutomationImpact', $impactText, '-AutomationVerify', $verifyText, '-AutomationPlain', $plainText
  ) -WorkingDirectory $script:worktree
  if ($finalizer.ExitCode -ne 0) { Stop-Collector 'daily_collector_commit_failed' }
  $commitSha = (Invoke-Git -Root $script:worktree -Arguments @('rev-parse', 'HEAD') -DetailCode 'daily_collector_commit_failed').Trim()
  if ($commitSha -cnotmatch '^[0-9a-f]{40,64}$' -or $commitSha -ceq $script:baseCommit) { Stop-Collector 'daily_collector_commit_failed' }
  $formalPathsText = Invoke-Git -Root $script:worktree -Arguments @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', $script:baseCommit, $commitSha, '--')
  $formalPaths = @($formalPathsText -split "`r?`n" | Where-Object { $_ } | Sort-Object -Unique)
  if ($formalPaths.Count -ne $changedPaths.Count -or @($formalPaths | Where-Object { $_ -cnotin $changedPaths }).Count -ne 0) { Stop-Collector 'daily_collector_formal_paths_invalid' }

  $integration = Invoke-Native -FileName 'pwsh' -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $script:worktree 'tools/invoke-project-integration.ps1'),
    '-RepositoryRoot', $script:root, '-ExpectedMainHead', $script:baseCommit, '-TargetCommit', $commitSha,
    '-ExpectedPaths', ($formalPaths -join '|'), '-LockTimeoutSeconds', '0'
  ) -WorkingDirectory $script:worktree
  if ($integration.ExitCode -ne 0) { Stop-Collector 'daily_collector_integration_failed' }
  try { $integrationResult = $integration.Stdout | ConvertFrom-Json -Depth 20 } catch { Stop-Collector 'daily_collector_integration_failed' }
  if ([string]$integrationResult.status -cne 'integrated' -or [string]$integrationResult.head -cne $commitSha) { Stop-Collector 'daily_collector_integration_failed' }

  if ((Invoke-Git -Root $script:worktree -Arguments @('status', '--porcelain=v1', '--untracked-files=all')).Length -ne 0) { Stop-Collector 'daily_collector_cleanup_unsafe' }
  $remove = Invoke-Native -FileName 'git' -Arguments @('-C', $script:root, 'worktree', 'remove', '--', $script:worktree) -WorkingDirectory $script:root
  if ($remove.ExitCode -ne 0 -or (Test-Path -LiteralPath $script:worktree)) { Stop-Collector 'daily_collector_cleanup_failed' }
  $delete = Invoke-Native -FileName 'git' -Arguments @('-C', $script:root, 'branch', '-d', '--', $script:branch) -WorkingDirectory $script:root
  if ($delete.ExitCode -ne 0) { Stop-Collector 'daily_collector_cleanup_failed' }
  $script:worktree = $null; $script:branch = $null

  Write-Terminal -Result ([ordered]@{
    status = 'completed'; category = 'completed'; detailCode = 'experience_candidates_collected'; candidateCount = $items.Count
    taskIds = [object[]]@($items | ForEach-Object TaskId); experienceIds = [object[]]@($items | ForEach-Object ExperienceId)
    commitSha = $commitSha; changedPaths = [object[]]$formalPaths; cleanup = 'completed'
  }) -ExitCode 0
} catch {
  $detailCode = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'daily_collector_unexpected_failure' }
  $result = [ordered]@{ status = 'failed'; category = 'failed'; detailCode = $detailCode }
  if (-not [string]::IsNullOrWhiteSpace($script:baseCommit)) { $result.baseCommit = $script:baseCommit }
  if (-not [string]::IsNullOrWhiteSpace($script:worktree) -and (Test-Path -LiteralPath $script:worktree)) {
    $result.worktreeRetained = $true; $result.worktree = $script:worktree; $result.branch = $script:branch
  }
  Write-Terminal -Result $result -ExitCode 1
}
