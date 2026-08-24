#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$TaskId,
  [string]$TaskCardPath,
  [string]$IndexPath = '开发管理/经验库/风险索引.json',
  [string[]]$ExplicitRefs = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 预防型错误经验记忆系统的唯一只读匹配入口。
# 只读取任务卡与 L1 索引并输出一个 JSON 结果，不修改任务卡、索引、runtime 或工作树，也不执行 gate 命令。

function Read-Utf8Text {
  param([string]$Path)
  try {
    [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  } catch {
    throw '[invalid_utf8] ' + $Path
  }
}

function Get-TextDigest {
  param([string]$Path)
  $text = Read-Utf8Text $Path
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n"))
  )).ToLowerInvariant()
}

function Get-UnicodeLength {
  param([string]$Text)
  $count = 0
  foreach ($rune in $Text.EnumerateRunes()) { $count++ }
  $count
}

function Test-Prop {
  param([object]$Object, [string]$Name)
  $null -ne $Object -and $Object.PSObject.Properties.Name -ccontains $Name
}

function Get-SectionBody {
  param([string]$Text, [string]$Heading)
  $lines = $Text -split "`r?`n"
  $target = "## $Heading"
  $start = -1
  for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index].Trim() -ceq $target) { $start = $index; break }
  }
  if ($start -lt 0) { return $null }
  $buffer = [Collections.Generic.List[string]]::new()
  for ($index = $start + 1; $index -lt $lines.Count; $index++) {
    if ($lines[$index].TrimStart() -cmatch '^##\s') { break }
    $buffer.Add($lines[$index])
  }
  while ($buffer.Count -gt 0 -and [string]::IsNullOrWhiteSpace($buffer[0])) { $buffer.RemoveAt(0) }
  while ($buffer.Count -gt 0 -and [string]::IsNullOrWhiteSpace($buffer[$buffer.Count - 1])) { $buffer.RemoveAt($buffer.Count - 1) }
  ($buffer -join "`n").TrimEnd("`r")
}

function Convert-GlobToRegex {
  param([string]$Pattern)
  $builder = [Text.StringBuilder]::new()
  foreach ($ch in $Pattern.ToCharArray()) {
    if ($ch -eq '*') { [void]$builder.Append('.*') }
    elseif ($ch -eq '?') { [void]$builder.Append('.') }
    else { [void]$builder.Append([regex]::Escape([string]$ch)) }
  }
  '^' + $builder.ToString() + '$'
}

function Test-PathHit {
  param([string]$Path, [string]$Pattern)
  if ($Pattern -notmatch '[*?]') {
    return [string]::Equals($Path, $Pattern, [StringComparison]::OrdinalIgnoreCase)
  }
  $regex = Convert-GlobToRegex $Pattern
  [regex]::IsMatch($Path, $regex, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Write-ResultJson {
  param([object]$Object)
  $json = $Object | ConvertTo-Json -Compress -Depth 20
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
  $stream = [Console]::OpenStandardOutput()
  $stream.Write($bytes, 0, $bytes.Length)
  $stream.Flush()
}

$repo = $null
$cardFull = $null
$indexFull = $null
$cardRel = $null
$indexRel = $null

function Resolve-RepoPath {
  param([string]$Relative)
  $normalized = $Relative.Replace('\', '/')
  $full = [IO.Path]::GetFullPath((Join-Path $repo ($normalized -replace '/', [IO.Path]::DirectorySeparatorChar)))
  $prefix = $repo + [IO.Path]::DirectorySeparatorChar
  if ($full -cne $repo -and -not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "[path_escape] $Relative"
  }
  $full
}

try {
  $repo = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath $repo -PathType Container)) { throw "[repository_not_found] $repo" }

  $hasTaskId = -not [string]::IsNullOrWhiteSpace($TaskId)
  $hasTaskCardPath = -not [string]::IsNullOrWhiteSpace($TaskCardPath)
  if ($hasTaskId -eq $hasTaskCardPath) { throw '[invalid_usage] provide exactly one of -TaskId or -TaskCardPath' }

  if ($hasTaskId) {
    if ($TaskId -cne $TaskId.Trim() -or $TaskId -cmatch '[/\\]' -or $TaskId -cin @('.', '..')) { throw "[invalid_usage] invalid TaskId: $TaskId" }
    $cardRel = "开发管理/任务卡/$TaskId.txt"
  } else {
    $cardRel = $TaskCardPath.Replace('\', '/')
    if ([IO.Path]::IsPathRooted($cardRel) -or $cardRel -match '^[A-Za-z]:' -or (@(($cardRel -split '/') | Where-Object { $_ -cin @('.', '..') }).Count -gt 0)) {
      throw "[invalid_usage] invalid TaskCardPath: $TaskCardPath"
    }
  }

  $indexRel = $IndexPath.Replace('\', '/')
  $cardFull = Resolve-RepoPath $cardRel
  $indexFull = Resolve-RepoPath $indexRel

  if (-not (Test-Path -LiteralPath $cardFull -PathType Leaf)) { throw "[task_card_not_found] $cardRel" }
  $cardText = Read-Utf8Text $cardFull
  $cardDigest = Get-TextDigest $cardFull
  $metaMarkers = [regex]::Matches($cardText, '(?m)^---TASK-META---\r?$')
  $bodyMarkers = [regex]::Matches($cardText, '(?m)^---TASK-BODY---\r?$')
  if ($metaMarkers.Count -ne 1 -or $bodyMarkers.Count -ne 1 -or $metaMarkers[0].Index -ge $bodyMarkers[0].Index) {
    throw "[invalid_task_card] $cardRel"
  }
  $metaJson = $cardText.Substring($metaMarkers[0].Index + $metaMarkers[0].Length, $bodyMarkers[0].Index - $metaMarkers[0].Index - $metaMarkers[0].Length).Trim()
  try { $meta = $metaJson | ConvertFrom-Json -Depth 100 } catch { throw "[invalid_task_card] $cardRel" }
  foreach ($field in @('id', 'domain', 'stage', 'title', 'expectedPaths')) {
    if (-not (Test-Prop $meta $field)) { throw "[invalid_task_card] missing field $field" }
  }
  if (-not ($meta.expectedPaths -is [System.Array])) { throw "[invalid_task_card] expectedPaths must be an array" }
  $cardBody = $cardText.Substring($bodyMarkers[0].Index + $bodyMarkers[0].Length)

  $taskIdValue = [string]$meta.id
  if ($hasTaskId -and $taskIdValue -cne $TaskId) { throw "[task_card_id_mismatch] $cardRel" }

  $domain = [string]$meta.domain
  $stage = [string]$meta.stage
  $title = [string]$meta.title
  $expectedPaths = @($meta.expectedPaths | ForEach-Object { ([string]$_).Replace('\', '/') } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

  $explicit = [Collections.Generic.List[string]]::new()
  if ($meta.PSObject.Properties.Name -ccontains 'riskPreflight') {
    $risk = $meta.riskPreflight
    if ($null -ne $risk -and ($risk.PSObject.Properties.Name -ccontains 'explicitRefs')) {
      foreach ($ref in @($risk.explicitRefs)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$ref) -and -not $explicit.Contains([string]$ref)) { [void]$explicit.Add([string]$ref) }
      }
    }
  }
  foreach ($ref in @($ExplicitRefs)) {
    if (-not [string]::IsNullOrWhiteSpace($ref) -and -not $explicit.Contains($ref)) { [void]$explicit.Add($ref) }
  }

  $haystack = ((@($title) + @((Get-SectionBody $cardBody '必查范围')) + @((Get-SectionBody $cardBody '实施范围'))) |
    Where-Object { -not [string]::IsNullOrEmpty($_) }) -join "`n"

  if (-not (Test-Path -LiteralPath $indexFull -PathType Leaf)) { throw "[index_not_found] $indexRel" }
  $indexText = Read-Utf8Text $indexFull
  $indexDigest = Get-TextDigest $indexFull
  try { $index = $indexText | ConvertFrom-Json -Depth 100 } catch { throw '[invalid_index_json] ' + $_.Exception.Message }

  $topKeys = @($index.PSObject.Properties.Name)
  $requiredTop = @('schemaVersion', 'experiences', 'gates')
  if (@($topKeys | Where-Object { $_ -cnotin $requiredTop }).Count -ne 0 -or @($requiredTop | Where-Object { $topKeys -cnotcontains $_ }).Count -ne 0) {
    throw '[invalid_index_schema] top-level keys must be exactly schemaVersion/experiences/gates'
  }
  if ($index.schemaVersion -ne 1) { throw '[invalid_index_schema] schemaVersion must be 1' }
  if (-not ($index.experiences -is [System.Array]) -or -not ($index.gates -is [System.Array])) {
    throw '[invalid_index_schema] experiences and gates must be arrays'
  }

  $validDomains = @('unity', 'battlesim', 'data', 'content', 'management', 'automation')
  $validStages = @('discovery', 'decision', 'design', 'implementation', 'migration', 'verification')
  $validStatus = @('candidate', 'active', 'review_required', 'retired')
  $validLevel = @('notice', 'must_read', 'gate')
  $validTrigger = @('path', 'path_and_text', 'explicit_only')

  $experienceIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $gateIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $gateEntries = @{}

  foreach ($gate in @($index.gates)) {
    foreach ($field in @('id', 'instructionRef', 'entryPaths', 'lastVerified')) {
      if (-not (Test-Prop $gate $field)) { throw "[invalid_gate_schema] gate missing $field" }
    }
    $gid = [string]$gate.id
    if ([string]::IsNullOrWhiteSpace($gid)) { throw '[invalid_gate_schema] gate id must not be empty' }
    if (-not $gateIds.Add($gid)) { throw "[duplicate_gate_id] $gid" }
    if (-not ($gate.entryPaths -is [System.Array])) { throw "[invalid_gate_schema] gate entryPaths must be an array: $gid" }
    $gateEntries[$gid] = $gate
  }

  foreach ($exp in @($index.experiences)) {
    foreach ($field in @('id', 'title', 'preflightSummary', 'status', 'level', 'triggerMode', 'domains', 'stages', 'pathPatterns', 'textPatterns', 'detailRef', 'gateRefs', 'lastVerified')) {
      if (-not (Test-Prop $exp $field)) { throw "[invalid_experience_schema] experience missing $field" }
    }
    $id = [string]$exp.id
    if (-not $experienceIds.Add($id)) { throw "[duplicate_experience_id] $id" }
    if ($id -cnotmatch '^EXP-(UNITY|BS|DATA|CONTENT|MGMT|AUTO)-\d{3}$') { throw "[invalid_experience_enum] invalid id format: $id" }
    if ($validStatus -cnotcontains [string]$exp.status) { throw "[invalid_experience_enum] invalid status: $id" }
    if ($validLevel -cnotcontains [string]$exp.level) { throw "[invalid_experience_enum] invalid level: $id" }
    if ($validTrigger -cnotcontains [string]$exp.triggerMode) { throw "[invalid_experience_enum] invalid triggerMode: $id" }
    if (-not ($exp.domains -is [System.Array]) -or -not ($exp.stages -is [System.Array]) -or -not ($exp.pathPatterns -is [System.Array]) -or -not ($exp.textPatterns -is [System.Array]) -or -not ($exp.gateRefs -is [System.Array])) {
      throw "[invalid_experience_enum] list fields must be arrays: $id"
    }
    foreach ($d in @($exp.domains)) { if ($validDomains -cnotcontains [string]$d) { throw "[invalid_experience_enum] invalid domain: $id" } }
    foreach ($s in @($exp.stages)) { if ($validStages -cnotcontains [string]$s) { throw "[invalid_experience_enum] invalid stage: $id" } }
    $refs = @($exp.gateRefs)
    if ([string]$exp.level -ceq 'gate' -and $refs.Count -eq 0) { throw "[missing_gate] gate-level experience has no gateRefs: $id" }
    foreach ($gr in $refs) {
      if (-not $gateIds.Contains([string]$gr)) { throw "[missing_gate] unknown gateRef: $gr" }
    }
  }

  $matched = [Collections.Generic.List[object]]::new()
  foreach ($exp in @($index.experiences)) {
    if ([string]$exp.status -cne 'active') { continue }
    $domains = @($exp.domains)
    $stages = @($exp.stages)
    if ($domains.Count -gt 0 -and $domains -cnotcontains $domain) { continue }
    if ($stages.Count -gt 0 -and $stages -cnotcontains $stage) { continue }

    $trigger = [string]$exp.triggerMode
    $isExplicit = $explicit -ccontains [string]$exp.id
    $hit = $false
    if ($trigger -ceq 'explicit_only') {
      $hit = $isExplicit
    } else {
      $pathHit = $false
      $patterns = @($exp.pathPatterns | ForEach-Object { ([string]$_).Replace('\', '/') })
      foreach ($p in $expectedPaths) {
        foreach ($pat in $patterns) {
          if (Test-PathHit $p $pat) { $pathHit = $true; break }
        }
        if ($pathHit) { break }
      }
      if ($trigger -ceq 'path') {
        $hit = $pathHit
      } elseif ($trigger -ceq 'path_and_text') {
        if ($pathHit) {
          $textHit = $false
          foreach ($t in @($exp.textPatterns)) {
            if ([string]::IsNullOrEmpty([string]$t)) { continue }
            if ($haystack.Contains([string]$t, [StringComparison]::Ordinal)) { $textHit = $true; break }
          }
          $hit = $textHit
        }
      }
    }
    if ($hit) { $matched.Add($exp) }
  }

  $ordered = @(@($matched | Where-Object { [string]$_.triggerMode -ceq 'explicit_only' })) +
    @(@($matched | Where-Object { [string]$_.triggerMode -ceq 'path_and_text' })) +
    @(@($matched | Where-Object { [string]$_.triggerMode -ceq 'path' }))

  $matchedIds = [Collections.Generic.List[string]]::new()
  $notices = [Collections.Generic.List[string]]::new()
  $mustReads = [Collections.Generic.List[object]]::new()
  $gateRefs = [Collections.Generic.List[string]]::new()

  foreach ($exp in $ordered) {
    $id = [string]$exp.id
    $matchedIds.Add($id)
    foreach ($gr in @($exp.gateRefs)) {
      $gs = [string]$gr
      if (-not $gateRefs.Contains($gs)) { [void]$gateRefs.Add($gs) }
    }
    $level = [string]$exp.level
    if ($level -ceq 'notice') {
      $summary = [string]$exp.preflightSummary
      if ($notices.Count -lt 2 -and -not $notices.Contains($summary)) { [void]$notices.Add($summary) }
    } elseif ($level -ceq 'must_read') {
      $ref = [string]$exp.detailRef
      if ([string]::IsNullOrWhiteSpace($ref)) { throw "[missing_body_pointer] must_read has empty detailRef: $id" }
      $filePart = $ref
      $sectionName = '开工前'
      $hashIndex = $ref.IndexOf('#')
      if ($hashIndex -ge 0) {
        $filePart = $ref.Substring(0, $hashIndex)
        $fragment = $ref.Substring($hashIndex + 1)
        if (-not [string]::IsNullOrWhiteSpace($fragment)) { $sectionName = $fragment }
      }
      if ([string]::IsNullOrWhiteSpace($filePart)) { throw "[missing_body_pointer] empty detailRef path: $id" }
      $detailFull = Resolve-RepoPath $filePart
      if (-not (Test-Path -LiteralPath $detailFull -PathType Leaf)) { throw "[missing_body_pointer] detailRef file not found: $ref" }
      $detailText = Read-Utf8Text $detailFull
      $sectionBody = Get-SectionBody $detailText $sectionName
      if ($null -eq $sectionBody) { throw "[missing_body_pointer] section not found: $ref" }
      $mustReads.Add([ordered]@{
        id = $id
        title = [string]$exp.title
        detailRef = $ref
        chars = (Get-UnicodeLength $sectionBody)
        body = $sectionBody
      })
    }
  }

  $mustReadCount = $mustReads.Count
  $totalChars = 0
  foreach ($m in $mustReads) { $totalChars += [int]$m.chars }
  if ($mustReadCount -gt 3) {
    Write-ResultJson ([ordered]@{
      status = 'preflight_overbroad'
      reason = 'must_read_count_exceeds_3'
      taskId = $taskIdValue
      taskCardDigest = $cardDigest
      indexDigest = $indexDigest
      matched = @($matchedIds)
      mustReadCount = $mustReadCount
      mustReadChars = $totalChars
    })
    exit 0
  }
  if ($totalChars -gt 600) {
    Write-ResultJson ([ordered]@{
      status = 'preflight_overbroad'
      reason = 'must_read_chars_exceeds_600'
      taskId = $taskIdValue
      taskCardDigest = $cardDigest
      indexDigest = $indexDigest
      matched = @($matchedIds)
      mustReadCount = $mustReadCount
      mustReadChars = $totalChars
    })
    exit 0
  }

  $gatePointers = [Collections.Generic.List[object]]::new()
  foreach ($gr in $gateRefs) {
    $gate = $gateEntries[$gr]
    $instRef = [string]$gate.instructionRef
    $instFile = $instRef
    $hIndex = $instRef.IndexOf('#')
    if ($hIndex -ge 0) { $instFile = $instRef.Substring(0, $hIndex) }
    if ([string]::IsNullOrWhiteSpace($instFile)) { throw "[missing_gate_entry] gate instructionRef empty: $gr" }
    $instFull = Resolve-RepoPath $instFile
    if (-not (Test-Path -LiteralPath $instFull -PathType Leaf)) { throw "[missing_gate_entry] gate instructionRef not found: $gr" }
    foreach ($ep in @($gate.entryPaths)) {
      $epNorm = ([string]$ep).Replace('\', '/')
      $epFull = Resolve-RepoPath $epNorm
      if (-not (Test-Path -LiteralPath $epFull -PathType Leaf)) { throw "[missing_gate_entry] gate entryPath not found: $gr -> $epNorm" }
    }
    $gatePointers.Add([ordered]@{ id = $gr; instructionRef = $instRef })
  }

  Write-ResultJson ([ordered]@{
    status = 'ok'
    taskId = $taskIdValue
    taskCardDigest = $cardDigest
    indexDigest = $indexDigest
    matched = @($matchedIds)
    notice = @($notices)
    mustRead = @($mustReads)
    gates = @($gatePointers | ForEach-Object { [string]$_.id })
    gatePointers = @($gatePointers)
    mustReadChars = $totalChars
  })
  exit 0
} catch {
  $message = $_.Exception.Message
  $code = 'matcher_error'
  if ($message -match '^\[([a-z_]+)\]') { $code = $Matches[1] }
  Write-ResultJson ([ordered]@{ status = 'error'; code = $code; message = $message })
  exit 1
}
