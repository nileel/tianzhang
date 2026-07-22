#requires -Version 7.0

[CmdletBinding()]
param(
  [switch]$FailOnMissingAssets,
  [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($ProjectRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "ProjectRoot does not exist: $ProjectRoot" }

$errors = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$waivers = @()
$findingKeys = [System.Collections.Generic.HashSet[string]]::new()

function Add-Error {
  param([string]$RuleId, [string]$Subject, [string]$Message)
  $errors.Add("ERROR`t$RuleId`t$Subject`t$Message") | Out-Null
}

function Add-Finding {
  param([string]$RuleId, [string]$Subject, [string]$Message)

  if (-not $findingKeys.Add($RuleId + [char]0x1F + $Subject)) { return }

  $waiver = @($waivers | Where-Object { $_.ruleId -ceq $RuleId -and $_.subject -ceq $Subject })
  if ($waiver.Count -eq 1) {
    $warnings.Add("WARNING`t$RuleId`t$Subject`tWAIVED: $Message") | Out-Null
    return
  }
  Add-Error $RuleId $Subject $Message
}

function Join-ProjectPath {
  param([string[]]$Parts)
  $path = $root
  foreach ($part in $Parts) { $path = Join-Path $path $part }
  return $path
}

function Get-CultivationName { return (-join @([char]0x89D2, [char]0x8272, [char]0x517B, [char]0x6210)) }
function Get-GongFaName { return (-join @([char]0x529F, [char]0x6CD5)) }
function Get-SpellName { return (-join @([char]0x672F, [char]0x6CD5)) }
function Get-SkillName { return (-join @([char]0x795E, [char]0x901A)) }

function Get-ContentDocs {
  param([string]$ContentKind)
  $path = Join-ProjectPath @('docs', (Get-CultivationName), $ContentKind)
  if (-not (Test-Path -LiteralPath $path -PathType Container)) {
    Add-Error 'MISSING_DOC_DIR' $ContentKind "Missing content document directory: $path"
    return @()
  }
  return @(Get-ChildItem -LiteralPath $path -Recurse -File -Filter *.txt | Where-Object { $_.DirectoryName -ne $path })
}

function Get-CsvTable {
  param([string]$RelativePath, [string[]]$ExpectedHeaders)
  $path = Join-Path $root $RelativePath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    Add-Error 'MISSING_CSV' $RelativePath "Missing CSV: $RelativePath"
    return [pscustomobject]@{ Name = $RelativePath; Headers = @(); Rows = @() }
  }

  $lines = @(Get-Content -LiteralPath $path -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
  if ($lines.Count -eq 0) {
    Add-Error 'CSV_HEADER_MISSING' $RelativePath 'CSV has no header row.'
    return [pscustomobject]@{ Name = $RelativePath; Headers = @(); Rows = @() }
  }

  $headers = @($lines[0].Split(',') | ForEach-Object { $_.Trim() })
  foreach ($duplicate in @($headers | Group-Object | Where-Object { $_.Count -gt 1 } | Select-Object -ExpandProperty Name)) {
    Add-Finding 'CSV_SCHEMA_DUPLICATE_COLUMN' "${RelativePath}:$duplicate" 'CSV header contains a duplicate column.'
  }
  foreach ($expected in $ExpectedHeaders) {
    if ($headers -notcontains $expected) { Add-Finding 'CSV_SCHEMA_MISSING_COLUMN' "${RelativePath}:$expected" 'Required schema column is missing.' }
  }
  foreach ($actual in $headers) {
    if ($actual -notin $ExpectedHeaders) { Add-Finding 'CSV_SCHEMA_UNKNOWN_COLUMN' "${RelativePath}:$actual" 'Unknown schema column is not approved.' }
  }

  $rows = @()
  for ($index = 1; $index -lt $lines.Count; $index++) {
    $values = @($lines[$index].Split(',') | ForEach-Object { $_.Trim() })
    $rowKey = if ($values.Count -gt 0 -and $values[0]) { $values[0] } else { "line-$($index + 1)" }
    if ($values.Count -ne $headers.Count) {
      Add-Finding 'CSV_ROW_COLUMN_COUNT' "${RelativePath}:$rowKey" "Expected $($headers.Count) columns but found $($values.Count)."
      continue
    }
    $row = [ordered]@{}
    for ($column = 0; $column -lt $headers.Count; $column++) { $row[$headers[$column]] = $values[$column] }
    foreach ($header in $ExpectedHeaders) {
      if (-not $row.Contains($header) -or [string]::IsNullOrWhiteSpace($row[$header])) {
        Add-Finding 'REQUIRED_FIELD_EMPTY' "${RelativePath}:${rowKey}:$header" 'Required field is empty.'
      }
    }
    if ($row.Contains('contentScope') -and $row['contentScope'] -notin @('player', 'reserved')) {
      Add-Finding 'CONTENT_SCOPE_INVALID' "${RelativePath}:$rowKey" "contentScope '$($row['contentScope'])' is not player or reserved."
    }
    $rows += [pscustomobject]$row
  }
  return [pscustomobject]@{ Name = $RelativePath; Headers = $headers; Rows = $rows }
}

function Get-LanguageIds {
  $path = Join-Path $root 'src/Assets/DataConfig/Language.csv'
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Add-Error 'MISSING_LANGUAGE' 'Language.csv' 'Language CSV is missing.'; return @() }
  $lines = @(Get-Content -LiteralPath $path -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
  if ($lines.Count -eq 0) { Add-Finding 'LANGUAGE_SCHEMA_INVALID' 'Language.csv' 'Language CSV has no data rows.'; return @() }
  $ids = @($lines | ForEach-Object { ($_ -split ',', 2)[0].Trim() })
  foreach ($duplicate in @($ids | Group-Object | Where-Object { $_.Count -gt 1 } | Select-Object -ExpandProperty Name)) { Add-Finding 'DUPLICATE_ID' "Language.csv:$duplicate" 'Language ID is duplicated.' }
  return $ids
}

function Test-AssetCoverage {
  param([string]$Label, [object[]]$Rows, [string]$AssetDir, [string]$AssetPrefix)
  $dir = Join-Path $root $AssetDir
  if (-not (Test-Path -LiteralPath $dir -PathType Container)) { Add-Error 'MISSING_ASSET_DIR' $AssetDir "Missing asset directory: $AssetDir"; return }
  $expectedIds = @($Rows | ForEach-Object { $_.name })
  foreach ($duplicate in @($expectedIds | Group-Object | Where-Object { $_.Count -gt 1 } | Select-Object -ExpandProperty Name)) { Add-Finding 'DUPLICATE_ID' "${Label}:$duplicate" 'CSV content ID is duplicated.' }
  $assetNames = @(Get-ChildItem -LiteralPath $dir -File -Filter *.asset | Select-Object -ExpandProperty Name)
  $expectedNames = @($expectedIds | ForEach-Object { "$AssetPrefix`_$_.asset" })
  foreach ($missing in @($expectedNames | Where-Object { $assetNames -notcontains $_ })) { Add-Finding 'ASSET_MISSING' "${Label}:$missing" 'CSV content has no matching asset.' }
  foreach ($extra in @($assetNames | Where-Object { $expectedNames -notcontains $_ })) { Add-Finding 'ASSET_EXTRA' "${Label}:$extra" 'Asset has no matching CSV content.' }
  foreach ($row in $Rows) {
    $assetName = "$AssetPrefix`_$($row.name).asset"
    $assetPath = Join-Path $dir $assetName
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) { continue }
    $scopeLine = @(Select-String -LiteralPath $assetPath -Pattern '^\s*contentScope:\s*(\S+)\s*$').Matches
    if ($scopeLine.Count -ne 1) { Add-Finding 'ASSET_CONTENT_SCOPE_MISSING' "${Label}:$($row.name)" 'Asset must serialize exactly one contentScope field.'; continue }
    $assetScope = $scopeLine[0].Groups[1].Value
    if ($assetScope -ne $row.contentScope) { Add-Finding 'ASSET_CONTENT_SCOPE_MISMATCH' "${Label}:$($row.name)" "Asset scope '$assetScope' differs from CSV scope '$($row.contentScope)'." }

    $fieldMappings = if ($Label -eq 'Spells') {
      @(
        @{ Csv = 'realmReq'; Asset = 'realmRequirement' },
        @{ Csv = 'elementReq'; Asset = 'elementRequirement' },
        @{ Csv = 'sourceAffiliation'; Asset = 'sourceAffiliation' }
      )
    } elseif ($Label -eq 'Skills') {
      @(
        @{ Csv = 'realmReq'; Asset = 'realmRequirement' },
        @{ Csv = 'sourceAffiliation'; Asset = 'sourceAffiliation' }
      )
    } else {
      @()
    }

    foreach ($mapping in $fieldMappings) {
      $assetField = [string]$mapping.Asset
      $csvField = [string]$mapping.Csv
      $matches = @(Select-String -LiteralPath $assetPath -Pattern "^\s*$([regex]::Escape($assetField)):\s*(\S+)\s*$").Matches
      if ($matches.Count -ne 1) {
        Add-Finding 'ASSET_REQUIREMENT_FIELD_MISSING' "${Label}:$($row.name):$assetField" "Asset must serialize exactly one $assetField field."
        continue
      }
      $assetValue = $matches[0].Groups[1].Value
      $csvValue = [string]$row.$csvField
      if ($assetValue -cne $csvValue) {
        Add-Finding 'ASSET_REQUIREMENT_MISMATCH' "${Label}:$($row.name):$assetField" "Asset value '$assetValue' differs from CSV $csvField '$csvValue'."
      }
    }
  }
}

function Load-Waivers {
  $path = Join-Path $root 'tools/data-chain-warning-waivers.json'
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Add-Error 'WAIVER_FILE_MISSING' 'tools/data-chain-warning-waivers.json' 'The precise-warning waiver file is required.'; return @() }
  try { $items = @(Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json | Where-Object { $null -ne $_ }) } catch { Add-Error 'WAIVER_FILE_INVALID' 'tools/data-chain-warning-waivers.json' $_.Exception.Message; return @() }
  foreach ($item in $items) {
    if ($null -eq $item -or ($item -is [System.Array] -and $item.Count -eq 0)) { continue }
    foreach ($field in @('ruleId', 'subject', 'reason', 'owner', 'removalCondition')) {
      if ([string]::IsNullOrWhiteSpace([string]$item.$field)) { Add-Error 'WAIVER_RECORD_INVALID' "tools/data-chain-warning-waivers.json:$field" 'Waiver record has a missing required field.' }
    }
    if ([string]$item.subject -match '[*?]' -or [string]$item.subject -match '(^|:)(all|category|prefix)($|:)') { Add-Error 'WAIVER_RECORD_INVALID' ([string]$item.subject) 'Waiver subject must be an exact content/file/row key, not a wildcard or category.' }
  }
  return $items
}

$waivers = Load-Waivers
$schemas = [ordered]@{
  GongFa = @('name','affiliation','grade','elementMain','elementSub','starRootBone','starPhysique','starSpirit','starMind','starReaction','starTalent','starFortune','growth','chapters','contentScope')
  Spells = @('name','type','minRange','maxRange','mpCost','cooldownTicks','physicalDamageMultiplier','soulDamageMultiplier','healAmount','cannotBlock','cannotDodge','penetratingShield','stunChance','realmReq','elementReq','element','sourceAffiliation','contentScope')
  Skills = @('name','type','minRange','maxRange','mpCost','cooldownTicks','damageMultiplier','healAmount','cannotBlock','cannotDodge','penetratingShield','stunChance','isDomain','isBloodline','specialEffectDesc','element','realmReq','sourceAffiliation','contentScope')
  EnvironmentProfiles = @('profileId','unitsPerRange','maxQueryRange','directedEdges','surfacePrototypeRefs','phenomenonChannels','phenomenonPairs','elementRelationRefs')
}
$tables = [ordered]@{
  GongFa = Get-CsvTable 'src/Assets/DataConfig/GongFa.csv' $schemas.GongFa
  Spells = Get-CsvTable 'src/Assets/DataConfig/Spells.csv' $schemas.Spells
  Skills = Get-CsvTable 'src/Assets/DataConfig/Skills.csv' $schemas.Skills
  EnvironmentProfiles = Get-CsvTable 'src/Assets/DataConfig/EnvironmentProfiles.csv' $schemas.EnvironmentProfiles
}
$docs = [ordered]@{ GongFa = Get-ContentDocs (Get-GongFaName); Spells = Get-ContentDocs (Get-SpellName); Skills = Get-ContentDocs (Get-SkillName) }
foreach ($kind in $docs.Keys) {
  if ($docs[$kind].Count -ne $tables[$kind].Rows.Count) { Add-Finding 'DOC_CSV_COUNT_MISMATCH' $kind "Docs=$($docs[$kind].Count); CSV=$($tables[$kind].Rows.Count)." }
}

$languageIds = Get-LanguageIds
Test-AssetCoverage 'GongFa' $tables.GongFa.Rows 'src/Assets/Data/GongFa' 'GongFa'
Test-AssetCoverage 'Spells' $tables.Spells.Rows 'src/Assets/Data/Spells' 'Spell'
Test-AssetCoverage 'Skills' $tables.Skills.Rows 'src/Assets/Data/Skills' 'Skill'

foreach ($table in $tables.Values) {
  foreach ($row in $table.Rows) {
    foreach ($field in @('growth', 'chapters', 'realmReq')) {
      if (-not $row.PSObject.Properties.Name.Contains($field)) { continue }
      $value = [string]$row.$field
      if ($value -match 'realm_lianshen' -and $languageIds -notcontains 'realm_lianshen') { Add-Finding 'LANGUAGE_KEY_MISSING' 'realm_lianshen' 'Active content references realm_lianshen without a Language.csv key.' }
      if ($value -match 'realm_lianxu') { Add-Finding 'DELETED_REALM_ACTIVE' "$($table.Name):$($row.name):realm_lianxu" 'Deleted realm_lianxu remains active in content data.' }
    }
  }
}

"docs/csv counts: GongFa=$($docs.GongFa.Count)/$($tables.GongFa.Rows.Count); Spells=$($docs.Spells.Count)/$($tables.Spells.Rows.Count); Skills=$($docs.Skills.Count)/$($tables.Skills.Rows.Count)"
"language keys: $($languageIds.Count)"
if ($warnings.Count -gt 0) { 'approved warnings:'; $warnings | Sort-Object }
if ($errors.Count -gt 0) { 'check-data-chain: FAILED'; $errors | Sort-Object; exit 1 }
'check-data-chain: OK'
