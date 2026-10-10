#requires -Version 7.0

# Shared by task-card validation and the daily collector; no I/O or collection here.
function Get-CandidateBlocks {
  param([string]$Text, [string]$SourceTaskId, [string]$SourcePath = $SourceTaskId)

  $candidateId = ''
  function Stop-CandidateFormat {
    param([int]$Line, [string]$Field, [string]$Reason)
    $exception = [InvalidOperationException]::new("Invalid experience candidate: ${SourcePath}:$Line [$candidateId] ${Field}: $Reason")
    $exception.Data['DetailCode'] = 'daily_collector_candidate_invalid'
    $exception.Data['candidateError'] = [ordered]@{
      sourcePath = $SourcePath; sourceTaskId = $SourceTaskId; candidateId = $candidateId
      line = $Line; field = $Field; reason = $Reason
    }
    throw $exception
  }

  $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
  $lines = @($normalized -split "`n")
  $sections = @()
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -ceq '## 经验候选') { $sections += $i } }
  if ($sections.Count -eq 0) { return @() }
  if ($sections.Count -ne 1) { Stop-CandidateFormat ($sections[1] + 1) 'section' 'expected one ## 经验候选 section' }
  $sectionStart = $sections[0]
  $sectionEnd = $lines.Count
  for ($i = $sectionStart + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -cmatch '^##\s') { $sectionEnd = $i; break }
  }
  $headings = @()
  for ($i = $sectionStart + 1; $i -lt $sectionEnd; $i++) { if ($lines[$i] -cmatch '^###\s') { $headings += $i } }
  if ($headings.Count -eq 0) { Stop-CandidateFormat ($sectionStart + 1) 'candidateId' 'missing ### <candidateId> heading' }
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
      Stop-CandidateFormat ($start + 1) 'candidateId' 'invalid or duplicate candidate ID'
    }
    $contentIndexes = @()
    for ($i = $start + 1; $i -lt $end; $i++) { if (-not [string]::IsNullOrWhiteSpace($lines[$i])) { $contentIndexes += $i } }
    $values = [ordered]@{}
    $fieldLines = @{}
    for ($i = 0; $i -lt $expectedFields.Count; $i++) {
      $field = $expectedFields[$i].Name
      if ($i -ge $contentIndexes.Count) { Stop-CandidateFormat ($end + 1) $field 'missing field' }
      $line = $contentIndexes[$i] + 1
      $match = [regex]::Match($lines[$contentIndexes[$i]], $expectedFields[$i].Pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
      if (-not $match.Success) { Stop-CandidateFormat $line $field 'missing, misspelled or out-of-order field' }
      $value = $match.Groups['value'].Value
      if ($value -cne $value.Trim()) { Stop-CandidateFormat $line $field 'unexpected surrounding whitespace' }
      $values[$field] = $value
      $fieldLines[$field] = $line
    }
    if ($contentIndexes.Count -gt $expectedFields.Count) {
      Stop-CandidateFormat ($contentIndexes[$expectedFields.Count] + 1) 'fields' 'unexpected content after the ten candidate fields'
    }
    $enums = [ordered]@{
      collectionState = @('provisional', 'pending', 'collected')
      resolution = @('unresolved', 'promoted', 'duplicate', 'rejected')
      domain = @('automation', 'management', 'unity', 'battlesim', 'data', 'design', 'content', 'numeric')
      gatePossible = @('yes', 'no')
    }
    foreach ($field in $enums.Keys) {
      if ($values[$field] -cnotin $enums[$field]) { Stop-CandidateFormat $fieldLines[$field] $field ("expected " + ($enums[$field] -join ' | ')) }
    }
    foreach ($field in @('symptom', 'rootCause', 'reuseScope', 'evidence')) {
      if ([string]::IsNullOrWhiteSpace($values[$field])) { Stop-CandidateFormat $fieldLines[$field] $field 'must not be empty' }
    }
    if ($values.collectionState -cin @('provisional', 'pending')) {
      foreach ($field in @('promotionTaskId', 'experienceId')) {
        if (-not [string]::IsNullOrEmpty($values[$field])) { Stop-CandidateFormat $fieldLines[$field] $field 'must be empty before collection' }
      }
      if ($values.resolution -cne 'unresolved') { Stop-CandidateFormat $fieldLines.resolution 'resolution' 'must be unresolved before collection' }
    } else {
      if ([string]::IsNullOrWhiteSpace($values.promotionTaskId)) { Stop-CandidateFormat $fieldLines.promotionTaskId 'promotionTaskId' 'required after collection' }
      if ($values.resolution -cin @('promoted', 'duplicate')) {
        if ($values.experienceId -cnotmatch '^EXP-(UNITY|BS|DATA|CONTENT|MGMT|AUTO)-\d{3}$') {
          Stop-CandidateFormat $fieldLines.experienceId 'experienceId' 'expected a formal EXP-<domain>-<number> ID'
        }
      } elseif (-not [string]::IsNullOrEmpty($values.experienceId)) {
        Stop-CandidateFormat $fieldLines.experienceId 'experienceId' 'must be empty for unresolved or rejected candidates'
      }
    }
    $blockText = ($lines[$start..($end - 1)] -join "`n").TrimEnd("`n")
    $result.Add([pscustomobject]@{
      SourceTaskId = $SourceTaskId
      CandidateId = $candidateId
      Digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($blockText))).ToLowerInvariant()
      StartLine = $start
      EndLine = $end
      StateLine = $contentIndexes[0]
      PromotionLine = $contentIndexes[1]
      Values = [pscustomobject]$values
    })
  }
  @($result)
}
