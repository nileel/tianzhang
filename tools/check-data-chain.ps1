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

function Test-IsDataBackedContentDoc {
  param([System.IO.FileInfo]$File, [string]$ContentKind)

  if ($ContentKind -cne (Get-SpellName)) { return $true }
  return -not (Select-String -LiteralPath $File.FullName -Pattern '^\s*-\s*内容类型\s*[：:]\s*功能术法[。.]?\s*$' -Quiet)
}

function Get-ContentDocs {
  param([string]$ContentKind)
  $path = Join-ProjectPath @('docs', (Get-CultivationName), $ContentKind)
  if (-not (Test-Path -LiteralPath $path -PathType Container)) {
    Add-Error 'MISSING_DOC_DIR' $ContentKind "Missing content document directory: $path"
    return @()
  }
  return @(Get-ChildItem -LiteralPath $path -Recurse -File -Filter *.txt |
    Where-Object { $_.DirectoryName -ne $path -and (Test-IsDataBackedContentDoc -File $_ -ContentKind $ContentKind) })
}

function Get-CsvTable {
  param(
    [string]$RelativePath,
    [string[]]$ExpectedHeaders,
    [string[]]$OptionalHeaders = @(),
    [string[]]$AllowedContentScopes = @('player', 'reserved')
  )
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
      if ($header -in $OptionalHeaders) { continue }
      if (-not $row.Contains($header) -or [string]::IsNullOrWhiteSpace($row[$header])) {
        Add-Finding 'REQUIRED_FIELD_EMPTY' "${RelativePath}:${rowKey}:$header" 'Required field is empty.'
      }
    }
    if ($row.Contains('contentScope') -and -not [string]::IsNullOrWhiteSpace([string]$row['contentScope']) -and $row['contentScope'] -notin $AllowedContentScopes) {
      Add-Finding 'CONTENT_SCOPE_INVALID' "${RelativePath}:$rowKey" "contentScope '$($row['contentScope'])' is not approved for this table."
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

function Test-FormalContentAsset {
  param([string]$Label, [string]$RelativePath, [hashtable]$ExpectedFields)

  $path = Join-Path $root $RelativePath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    Add-Finding 'FORMAL_CONTENT_ASSET_MISSING' $Label "Missing generated asset: $RelativePath"
    return
  }

  foreach ($field in $ExpectedFields.Keys) {
    $expected = [string]$ExpectedFields[$field]
    $matches = @(Select-String -LiteralPath $path -Pattern "^  $([regex]::Escape($field)):\s*(\S+)\s*$").Matches
    if ($matches.Count -ne 1 -or $matches[0].Groups[1].Value -cne $expected) {
      Add-Finding 'FORMAL_CONTENT_ASSET_FIELD_MISMATCH' "${Label}:$field" "Asset field must equal '$expected'."
    }
  }
}

function Test-FormalContentCatalog {
  param([object]$Settlements, [object]$Items, [object]$Bounties, [object]$Enemies, [string[]]$LanguageIds)

  $expectedIds = @{
    Settlements = @('guanzhong_city')
    Items = @('item_lingshi_low', 'item_shijia_piece')
    Bounties = @('bounty_guanzhong_shijiahou')
  }
  foreach ($kind in $expectedIds.Keys) {
    $rows = @((Get-Variable -Name $kind -ValueOnly).Rows)
    $idField = switch ($kind) {
      'Settlements' { 'settlementId' }
      'Items' { 'itemId' }
      'Bounties' { 'bountyId' }
    }
    $actualIds = @($rows | ForEach-Object { [string]$_.PSObject.Properties[$idField].Value } | Sort-Object -Unique)
    if ($rows.Count -ne $expectedIds[$kind].Count -or @($expectedIds[$kind] | Where-Object { $_ -notin $actualIds }).Count -ne 0) {
      Add-Finding 'FORMAL_CONTENT_ROW_SET_INVALID' $kind 'CSV must contain exactly the approved first-batch production IDs.'
    }
  }

  $requiredLanguageIds = @(
    'settlement_guanzhong_city',
    'settlement_feature_bounty_board',
    'item_lingshi_low',
    'item_shijia_piece',
    'item_lingshi_low_description',
    'item_shijia_piece_description',
    'enemy_shijiahou',
    'desc_enemy_shijiahou',
    'bounty_guanzhong_shijiahou_title',
    'bounty_guanzhong_shijiahou_description'
  )
  foreach ($languageId in $requiredLanguageIds) {
    if ($languageId -notin $LanguageIds) { Add-Finding 'FORMAL_CONTENT_LANGUAGE_MISSING' $languageId 'Formal content projection references a missing Language key.' }
  }

  $settlement = @($Settlements.Rows | Where-Object { $_.settlementId -ceq 'guanzhong_city' })
  if ($settlement.Count -eq 1 -and ($settlement[0].contentScope -cne 'content_scope_production' -or $settlement[0].features -cne 'bounty_board~settlement_feature_bounty_board~enabled~' -or $settlement[0].adventureEntranceIds -cne 'guanzhong_wild')) {
    Add-Finding 'FORMAL_SETTLEMENT_PROJECTION_INVALID' 'guanzhong_city' 'Settlement production fields differ from the approved projection.'
  }

  $itemsById = @{}
  foreach ($item in $Items.Rows) { $itemsById[[string]$item.itemId] = $item }
  foreach ($itemId in @('item_lingshi_low', 'item_shijia_piece')) {
    if (-not $itemsById.ContainsKey($itemId) -or $itemsById[$itemId].contentScope -cne 'content_scope_production' -or $itemsById[$itemId].maxStack -cne '99') {
      Add-Finding 'FORMAL_ITEM_PROJECTION_INVALID' $itemId 'Item production scope or maxStack differs from the approved parameter decision.'
    }
  }

  $bounty = @($Bounties.Rows | Where-Object { $_.bountyId -ceq 'bounty_guanzhong_shijiahou' })
  if ($bounty.Count -eq 1 -and ($bounty[0].issuerSettlementId -cne 'guanzhong_city' -or $bounty[0].targetEnemyId -cne 'enemy_shijiahou' -or $bounty[0].allowedAdventureId -cne 'guanzhong_wild' -or $bounty[0].rewardEntries -cne 'item_lingshi_low@3' -or $bounty[0].repeatPolicy -cne 'one_time')) {
    Add-Finding 'FORMAL_BOUNTY_PROJECTION_INVALID' 'bounty_guanzhong_shijiahou' 'Bounty production fields differ from the approved projection.'
  }

  $enemy = @($Enemies.Rows | Where-Object { $_.name -ceq 'enemy_shijiahou' })
  if ($enemy.Count -ne 1 -or $enemy[0].contentScope -cne 'guanzhong' -or $enemy[0].dropEntries -cne 'item_shijia_piece@100@1|item_lingshi_low@50@1' -or $enemy[0].unarmedBasicAttackProfileId -cne 'basic_unarmed') {
    Add-Finding 'FORMAL_ENEMY_PROJECTION_INVALID' 'enemy_shijiahou' 'Enemy production scope, structured drop entries or basic attack binding differ from the approved decision.'
  }
  foreach ($row in @($Enemies.Rows | Where-Object { $_.name -cne 'enemy_shijiahou' })) {
    if (-not [string]::IsNullOrWhiteSpace([string]$row.contentScope) -or -not [string]::IsNullOrWhiteSpace([string]$row.dropEntries) -or -not [string]::IsNullOrWhiteSpace([string]$row.unarmedBasicAttackProfileId)) {
      Add-Finding 'FORMAL_ENEMY_SCOPE_LEAK' ([string]$row.name) 'Only enemy_shijiahou may enter the formal content directory or bind the production basic attack.'
    }
  }

  Test-FormalContentAsset 'Settlement:guanzhong_city' 'src/Assets/Data/Settlements/Settlement_guanzhong_city.asset' @{
    settlementId = 'guanzhong_city'; contentScope = 'content_scope_production'; displayNameKey = 'settlement_guanzhong_city'
  }
  Test-FormalContentAsset 'Enemy:enemy_shijiahou' 'src/Assets/Data/Enemies/Enemy_enemy_shijiahou.asset' @{
    enemyId = 'enemy_shijiahou'; contentScope = 'guanzhong'; aiProfileId = 'ai_melee'; realmId = 'realm_lianqi'
  }
  Test-FormalContentAsset 'Item:item_lingshi_low' 'src/Assets/Data/Items/Item_item_lingshi_low.asset' @{
    itemId = 'item_lingshi_low'; contentScope = 'content_scope_production'; maxStack = '99'
  }
  Test-FormalContentAsset 'Item:item_shijia_piece' 'src/Assets/Data/Items/Item_item_shijia_piece.asset' @{
    itemId = 'item_shijia_piece'; contentScope = 'content_scope_production'; maxStack = '99'
  }
  Test-FormalContentAsset 'Bounty:bounty_guanzhong_shijiahou' 'src/Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset' @{
    bountyId = 'bounty_guanzhong_shijiahou'; contentScope = 'content_scope_production'; targetEnemyId = 'enemy_shijiahou'; repeatPolicy = 'one_time'
  }
  Test-FormalContentAsset 'EnemyTemplate:enemy_shijiahou' 'src/Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset' @{
    unarmedBasicAttackProfileId = 'basic_unarmed'
  }
  $catalogPath = Join-Path $root 'src/Assets/Data/ContentCatalog/ContentCatalog.asset'
  if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
    Add-Finding 'FORMAL_CONTENT_CATALOG_MISSING' 'ContentCatalog' 'The single read-only content catalog asset is missing.'
  }
}

function Test-NpcCultivationActionWeightProfile {
  param([object]$Table)

  $label = 'NpcCultivationActionWeightProfiles'
  $rows = @($Table.Rows)
  $manifests = @($rows | Where-Object { $_.recordKind -ceq 'MANIFEST' })
  if ($manifests.Count -ne 1) {
    Add-Finding 'NPC_WEIGHT_MANIFEST_COUNT' $label 'Production CSV must contain exactly one MANIFEST row.'
    return
  }

  $manifest = $manifests[0]
  foreach ($field in @('schemaId', 'schemaVersion', 'profileId', 'sourceContentHash', 'authorityKind', 'tieBreakPolicy')) {
    if ([string]::IsNullOrWhiteSpace([string]$manifest.$field)) {
      Add-Finding 'NPC_WEIGHT_MANIFEST_FIELD_MISSING' "${label}:$field" 'Manifest requires this explicit field.'
    }
  }
  if ($manifest.schemaId -cne 'npcCultivationActionWeightProfile' -or $manifest.schemaVersion -cne '1') {
    Add-Finding 'NPC_WEIGHT_SCHEMA_INVALID' $label 'Manifest schemaId/schemaVersion is not the production schema.'
  }
  if ($manifest.authorityKind -cne 'CSV_SOURCE_SET' -or $manifest.tieBreakPolicy -cne 'LEXICOGRAPHIC_ASC') {
    Add-Finding 'NPC_WEIGHT_AUTHORITY_INVALID' $label 'Manifest must declare CSV_SOURCE_SET and LEXICOGRAPHIC_ASC.'
  }

  $profileIds = @($rows | ForEach-Object { [string]$_.profileId } | Sort-Object -Unique)
  if ($profileIds.Count -ne 1 -or $profileIds[0] -cne $manifest.profileId) {
    Add-Finding 'NPC_WEIGHT_PROFILE_MIXED' $label 'All rows must belong to the single manifest profileId.'
  }

  $canonicalRows = @($rows | ForEach-Object {
    $values = foreach ($header in $Table.Headers) {
      if ($header -ceq 'sourceContentHash') { '' } else { [string]$_.PSObject.Properties[$header].Value }
    }
    $values -join ','
  })
  $canonical = (@($Table.Headers -join ',') + $canonicalRows) -join "`n"
  $sha256 = [System.Security.Cryptography.SHA256]::Create()
  try {
    $actualHash = -join ($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($canonical)) | ForEach-Object { $_.ToString('x2') })
  }
  finally {
    $sha256.Dispose()
  }
  if ($manifest.sourceContentHash -cne $actualHash) {
    Add-Finding 'NPC_WEIGHT_SOURCE_HASH_MISMATCH' $label 'Manifest sourceContentHash does not match the normalized complete CSV source set.'
  }

  $actions = @($rows | Where-Object { $_.recordKind -ceq 'ACTION' })
  $expectedActions = @('FOUNDATION_TRIAL', 'FOUNDATION_NURTURE', 'MANSION_EMBRYO_NURTURE', 'MANSION_OPENING_TRIAL', 'JINDAN_PROOF')
  $actualActions = @($actions | ForEach-Object { $_.actionStableId } | Sort-Object -Unique)
  if ($actualActions.Count -ne $expectedActions.Count -or @($expectedActions | Where-Object { $_ -notin $actualActions }).Count -ne 0) {
    Add-Finding 'NPC_WEIGHT_ACTION_SET_INVALID' $label 'Production profile must define the five contract action stable IDs exactly once.'
  }
  foreach ($kind in @('MODIFIER', 'CAP_POLICY', 'DIMINISHING_POLICY', 'RISK_GATE', 'TRIGGER')) {
    if (@($rows | Where-Object { $_.recordKind -ceq $kind }).Count -eq 0) {
      Add-Finding 'NPC_WEIGHT_RECORD_KIND_MISSING' "${label}:$kind" 'Production profile is missing this required record kind.'
    }
  }

  $assetDir = 'src/Assets/Data/NpcCultivationActionWeightProfiles'
  $assetName = "NpcCultivationActionWeightProfile_$($manifest.profileId).asset"
  $assetPath = Join-Path $root (Join-Path $assetDir $assetName)
  if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
    Add-Finding 'NPC_WEIGHT_ASSET_MISSING' $label "CSV profile has no matching asset: $assetDir/$assetName"
    return
  }
  $assetNames = @(Get-ChildItem -LiteralPath (Join-Path $root $assetDir) -File -Filter *.asset | Select-Object -ExpandProperty Name)
  if ($assetNames.Count -ne 1 -or $assetNames[0] -cne $assetName) {
    Add-Finding 'NPC_WEIGHT_ASSET_COVERAGE_INVALID' $label 'Production profile directory must contain exactly its one matching asset.'
  }
  foreach ($field in @('schemaId', 'schemaVersion', 'profileId', 'sourceContentHash', 'authorityKind', 'tieBreakPolicy')) {
    $matches = @(Select-String -LiteralPath $assetPath -Pattern "^\s*$([regex]::Escape($field)):\s*(\S+)\s*$").Matches
    if ($matches.Count -ne 1 -or $matches[0].Groups[1].Value -cne [string]$manifest.$field) {
      Add-Finding 'NPC_WEIGHT_ASSET_MANIFEST_MISMATCH' "${label}:$field" 'Asset manifest field must exactly match the CSV manifest.'
    }
  }
}

function Test-FormalAttackProfileProjection {
  param([object]$Table, [string[]]$LanguageIds)

  $rows = @($Table.Rows)
  if ($rows.Count -ne 1 -or $rows[0].attackProfileId -cne 'basic_unarmed') {
    Add-Finding 'ATTACK_PROFILE_ROW_SET_INVALID' 'AttackProfiles' 'CSV must contain exactly the approved basic_unarmed production row.'
    return
  }

  $row = $rows[0]
  $fixedFields = @{
    profileKind = 'basic'
    basicBindingKind = 'unarmed_fallback'
    effectType = 'physical'
    damageElementId = 'element_none'
    physicalDamageMultiplier = '1.0'
    resourceKind = 'none'
    resourceCost = '0'
    cooldownTicks = '0'
    minCastRange = '1'
    maxCastRange = '1'
    targetingMode = 'single'
  }
  foreach ($name in $fixedFields.Keys) {
    $value = [string]$row.PSObject.Properties[$name].Value
    if ($value -cne $fixedFields[$name]) {
      Add-Finding 'ATTACK_PROFILE_PROJECTION_INVALID' "AttackProfiles:$name" "Field must equal '$($fixedFields[$name])'."
    }
  }

  foreach ($name in @('contentScope','sourceAffiliation','realmRequirementId','elementRequirementId','soulDamageMultiplier','healAmount','buffMultiplier','defensePenetration','areaCenterKind','areaShapeKind','areaRadius','areaLength','areaFanHalfAngleSteps','areaFacing','areaInnerRadius','areaEffectBlockers','areaAllowedFactions','areaAllowedStates','isDomain','isBloodline','specialEffectTextKey')) {
    $value = [string]$row.PSObject.Properties[$name].Value
    if (-not [string]::IsNullOrWhiteSpace($value)) {
      Add-Finding 'ATTACK_PROFILE_UNUSED_FIELD_NONEMPTY' "AttackProfiles:$name" 'The unarmed single-target row must leave this column empty.'
    }
  }

  $displayKey = [string]$row.displayNameKey
  if ($displayKey -notin $LanguageIds) {
    Add-Finding 'ATTACK_PROFILE_LANGUAGE_MISSING' "AttackProfiles:$displayKey" 'displayNameKey must exist in Language.csv.'
  }
  $languagePath = Join-ProjectPath @('src', 'Assets', 'DataConfig', 'Language.csv')
  $displayLine = @(Get-Content -LiteralPath $languagePath -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith('#') -and $_.StartsWith($displayKey + ',') })
  if ($displayLine.Count -ne 1 -or (($displayLine[0] -split ',', 2)[1].Trim() -cne '徒手')) {
    Add-Finding 'ATTACK_PROFILE_LANGUAGE_TEXT_INVALID' "AttackProfiles:$displayKey" 'Language value must be 徒手.'
  }

  # 单向链 CSV -> asset：规范路径必须存在且关键字段与 CSV 一致。
  $assetPath = 'src/Assets/Data/AttackProfiles/AttackProfile_basic_unarmed.asset'
  $absoluteAssetPath = Join-ProjectPath @('src', 'Assets', 'Data', 'AttackProfiles', 'AttackProfile_basic_unarmed.asset')
  if (-not (Test-Path -LiteralPath $absoluteAssetPath -PathType Leaf)) {
    Add-Finding 'ATTACK_PROFILE_ASSET_MISSING' 'AttackProfiles' "CSV profile has no matching asset: $assetPath"
    return
  }
  foreach ($field in @('attackProfileId', 'displayNameKey', 'damageElementId')) {
    $expected = [string]$row.PSObject.Properties[$field].Value
    $matches = @(Select-String -LiteralPath $absoluteAssetPath -Pattern "^  $([regex]::Escape($field)):\s*(\S+)\s*$").Matches
    if ($matches.Count -ne 1 -or $matches[0].Groups[1].Value -cne $expected) {
      Add-Finding 'ATTACK_PROFILE_ASSET_FIELD_MISMATCH' "AttackProfile:$field" "Asset field must equal '$expected'."
    }
  }
  foreach ($field in @('physicalDamageMultiplier', 'minCastRange', 'maxCastRange', 'resourceCost', 'cooldownTicks')) {
    $expected = [double][string]$row.PSObject.Properties[$field].Value
    $matches = @(Select-String -LiteralPath $absoluteAssetPath -Pattern "^  $([regex]::Escape($field)):\s*(\S+)\s*$").Matches
    if ($matches.Count -ne 1 -or [double]$matches[0].Groups[1].Value -ne $expected) {
      Add-Finding 'ATTACK_PROFILE_ASSET_FIELD_MISMATCH' "AttackProfile:$field" "Asset numeric field must equal '$expected'."
    }
  }
  # 枚举序列化：Basic=1、UnarmedFallback=2、Physical=1、None=1、Single=1。
  $enumFields = @{
    profileKind = '1'; basicBindingKind = '2'; effectType = '1'; resourceKind = '1'; targetingMode = '1'
  }
  foreach ($field in $enumFields.Keys) {
    $matches = @(Select-String -LiteralPath $absoluteAssetPath -Pattern "^  $([regex]::Escape($field)):\s*(\S+)\s*$").Matches
    if ($matches.Count -ne 1 -or $matches[0].Groups[1].Value -cne $enumFields[$field]) {
      Add-Finding 'ATTACK_PROFILE_ASSET_ENUM_MISMATCH' "AttackProfile:$field" "Asset enum must equal '$($enumFields[$field])'."
    }
  }

  # 场景引用：AdventureScene.unity 必须序列化同一 asset 的 GUID（场景重建不丢失引用）。
  $metaPath = Join-ProjectPath @('src', 'Assets', 'Data', 'AttackProfiles', 'AttackProfile_basic_unarmed.asset.meta')
  if (-not (Test-Path -LiteralPath $metaPath -PathType Leaf)) {
    Add-Finding 'ATTACK_PROFILE_ASSET_META_MISSING' 'AttackProfiles' 'Attack profile asset .meta is missing.'
    return
  }
  $guid = @(Select-String -LiteralPath $metaPath -Pattern '^\s*guid:\s*([0-9a-f]{32})\s*$').Matches
  if ($guid.Count -ne 1) {
    Add-Finding 'ATTACK_PROFILE_ASSET_META_INVALID' 'AttackProfiles' 'Attack profile asset .meta must declare exactly one guid.'
    return
  }
  $scenePath = Join-ProjectPath @('src', 'Assets', 'Scenes', 'AdventureScene.unity')
  if (-not (Test-Path -LiteralPath $scenePath -PathType Leaf) -or
      -not (Select-String -LiteralPath $scenePath -Pattern ([regex]::Escape($guid[0].Groups[1].Value)) -Quiet)) {
    Add-Finding 'ADVENTURE_SCENE_ATTACK_PROFILE_REFERENCE_MISSING' 'AdventureScene' 'AdventureScene.unity must reference the production basic_unarmed asset.'
  }
}

function Test-CharterSiteProjection {
  param([object]$Table, [string[]]$LanguageIds)

  $rows = @($Table.Rows)
  if ($rows.Count -ne 1 -or $rows[0].siteId -cne 'charter_site_old_water_station') {
    Add-Finding 'CHARTER_SITE_ROW_SET_INVALID' 'CharterSites' 'CSV must contain exactly the approved charter_site_old_water_station production row.'
    return
  }

  $site = $rows[0]
  $fixedFields = @{
    settlementId = 'guanzhong_city'
    passageCapabilityId = 'capability_kaihe_jiuzhang_v1'
    passageProtocolState = 'compatible'
    passageStructureState = 'intact'
    passagePowerState = 'available'
    interactionTimeProfileId = 'interaction_time_old_water_station_gate_v1'
    recognitionTiming = 'instant'
    operationTiming = 'sustained_guided'
    cancellationPolicy = 'no_commit_on_cancel'
    sealRelicId = 'relic_taixuan_realm_seal'
    sealAuthorizationVersionId = 'authorization_taixuan_seal_old_water_station_management_v1'
    ruleEntryId = 'charter_entry_suifu_diji'
    grantId = 'cross_tier_charter_water_basin_v1'
    grantQualificationSource = 'JindanProtection'
  }
  foreach ($name in $fixedFields.Keys) {
    $value = [string]$site.PSObject.Properties[$name].Value
    if ($value -cne $fixedFields[$name]) {
      Add-Finding 'CHARTER_SITE_PROJECTION_INVALID' "CharterSites:$name" "Field must equal '$($fixedFields[$name])'."
    }
  }

  $displayNameKey = [string]$site.PSObject.Properties['displayNameKey'].Value
  if ($displayNameKey -notin $LanguageIds) {
    Add-Finding 'CHARTER_SITE_LANGUAGE_MISSING' "CharterSites:$displayNameKey" 'Site display name key must exist in Language.csv.'
  }

  # 金丹样例：两侧候选互异；册界侧唯一绑定右侧候选，且左侧位别更高 → 确定性赢家不是册界侧。
  $leftCandidateId = [string]$site.PSObject.Properties['leftCandidateId'].Value
  $rightCandidateId = [string]$site.PSObject.Properties['rightCandidateId'].Value
  $charterCandidateId = [string]$site.PSObject.Properties['charterCandidateId'].Value
  if ($leftCandidateId -ceq $rightCandidateId) {
    Add-Finding 'CHARTER_SITE_CANDIDATE_IDS_NOT_DISTINCT' 'CharterSites' 'Left and right candidate ids must be distinct.'
  }
  if ($charterCandidateId -cne $rightCandidateId) {
    Add-Finding 'CHARTER_SITE_CHARTER_SIDE_UNDECLARED' 'CharterSites' "charterCandidateId must uniquely bind the right candidate '$rightCandidateId'."
  }
  $leftRank = [int]$site.PSObject.Properties['leftCandidatePositionRank'].Value
  $rightRank = [int]$site.PSObject.Properties['rightCandidatePositionRank'].Value
  if ($leftRank -le $rightRank) {
    Add-Finding 'CHARTER_SITE_CHARTER_SIDE_NOT_STABLE' 'CharterSites' 'The charter side must deterministically lose the shared decision.'
  }

  # 元婴样例不得夹带金丹候选、grant 或可覆盖结果。
  $yuanyingVariable = [string]$site.PSObject.Properties['yuanyingTargetVariableId'].Value
  $grantVariable = [string]$site.PSObject.Properties['grantTargetVariableId'].Value
  if ($yuanyingVariable -ceq $grantVariable) {
    Add-Finding 'CHARTER_SITE_YUANYING_NOT_ISOLATED' 'CharterSites' 'Yuanying sample must target a different variable than the jindan grant.'
  }

  $assetDir = 'src/Assets/Data/CharterSites'
  $assetName = 'CharterSite_charter_site_old_water_station.asset'
  $assetPath = Join-Path $root (Join-Path $assetDir $assetName)
  if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
    Add-Finding 'CHARTER_SITE_ASSET_MISSING' 'CharterSites' "CSV site has no matching asset: $assetDir/$assetName"
    return
  }
  $assetNames = @(Get-ChildItem -LiteralPath (Join-Path $root $assetDir) -File -Filter *.asset | Select-Object -ExpandProperty Name)
  if ($assetNames.Count -ne 1 -or $assetNames[0] -cne $assetName) {
    Add-Finding 'CHARTER_SITE_ASSET_COVERAGE_INVALID' 'CharterSites' 'Charter site asset directory must contain exactly its one matching asset.'
  }
  foreach ($field in @('siteId', 'displayNameKey', 'settlementId', 'passageCapabilityId', 'interactionTimeProfileId', 'grantId', 'charterCandidateId')) {
    $expected = [string]$site.PSObject.Properties[$field].Value
    # grantId 序列化在 jindanGrant 嵌套对象内（四级缩进），其余检查字段为站点顶层字段。
    $indent = if ($field -ceq 'grantId') { '    ' } else { '  ' }
    $matches = @(Select-String -LiteralPath $assetPath -Pattern "^$([regex]::Escape($indent))$([regex]::Escape($field)):\s*(\S+)\s*$").Matches
    if ($matches.Count -ne 1 -or $matches[0].Groups[1].Value -cne $expected) {
      Add-Finding 'CHARTER_SITE_ASSET_FIELD_MISMATCH' "CharterSite:$field" "Asset field must equal '$expected'."
    }
  }

  # 唯一生产站点 asset 必须由内容目录按同一 GUID 引用。
  $metaPath = Join-Path $root (Join-Path $assetDir "$assetName.meta")
  $catalogPath = Join-Path $root 'src/Assets/Data/ContentCatalog/ContentCatalog.asset'
  if (-not (Test-Path -LiteralPath $metaPath -PathType Leaf)) {
    Add-Finding 'CHARTER_SITE_ASSET_META_MISSING' 'CharterSites' 'Charter site asset .meta is missing.'
    return
  }
  $guid = @(Select-String -LiteralPath $metaPath -Pattern '^\s*guid:\s*([0-9a-f]{32})\s*$').Matches
  if ($guid.Count -ne 1) {
    Add-Finding 'CHARTER_SITE_ASSET_META_INVALID' 'CharterSites' 'Charter site asset .meta must declare exactly one guid.'
    return
  }
  if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf) -or
      -not (Select-String -LiteralPath $catalogPath -Pattern ([regex]::Escape($guid[0].Groups[1].Value)) -Quiet)) {
    Add-Finding 'CHARTER_SITE_CATALOG_REFERENCE_MISSING' 'CharterSites' 'ContentCatalog.asset must reference the single approved charter site asset.'
  }
}

function Test-UiTextProjection {
  param([string[]]$LanguageIds)

  # U-GZ-UI-TEXT-01：玩家显示边界引用的最小键集必须存在于 Language.csv（键值即稳定 ID / 稳定原因）。
  $requiredKeys = @(
    'region_guanzhong', 'adventure_guanzhong_wild', 'surface_grassland', 'surface_loess', 'world_node_type_hub',
    'charter_step_unopened', 'charter_step_passage', 'charter_step_management', 'charter_step_nodes',
    'charter_step_registration', 'charter_step_supplies', 'charter_step_evaluation', 'charter_step_committed',
    'bounty_status_available', 'bounty_status_accepted', 'bounty_status_completed', 'bounty_status_claimed',
    'settlement_catalog_missing', 'settlement_not_found', 'settlement_not_in_first_batch_production_scope',
    'settlement_adventure_not_available', 'settlement_feature_missing', 'settlement_feature_disabled',
    'settlement_feature_unknown', 'settlement_feature_dispatcher_missing', 'settlement_feature_handler_failed',
    'settlement_charter_site_panel_missing', 'settlement_charter_site_missing',
    'settlement_charter_site_not_current_settlement', 'settlement_charter_static_catalog_unavailable',
    'settlement_charter_session_missing', 'charter_site_entry_opened', 'charter_site_entry_unavailable',
    'bounty_board_entry_opened',
    'bounty_board_no_bounties', 'bounty_board_catalog_missing', 'bounty_board_session_missing',
    'bounty_catalog_missing', 'bounty_id_invalid', 'bounty_missing', 'bounty_not_production',
    'bounty_wrong_settlement', 'bounty_settlement_missing', 'bounty_accept_repeated',
    'bounty_objective_type_unsupported', 'bounty_repeat_policy_unsupported', 'bounty_required_count_invalid',
    'bounty_target_invalid', 'bounty_target_enemy_missing', 'bounty_adventure_invalid', 'bounty_reward_invalid',
    'bounty_reward_item_missing', 'bounty_reward_item_not_production', 'bounty_reward_item_stack_invalid',
    'bounty_not_accepted', 'bounty_not_completed', 'bounty_claim_repeated', 'bounty_defeat_wrong_adventure',
    'bounty_defeat_wrong_enemy', 'bounty_progress_invalid', 'bounty_progress_out_of_range',
    'bounty_claim_inventory_rejected',
    'charter_panel_formal_committed', 'charter_panel_controller_missing',
    'charter_interaction_input_invalid', 'charter_interaction_site_unavailable',
    'charter_interaction_site_not_current_settlement', 'charter_interaction_catalog_unavailable',
    'charter_interaction_definition_missing', 'charter_interaction_action_out_of_order',
    'charter_interaction_passage_unavailable', 'charter_interaction_passage_mismatch',
    'charter_interaction_management_mismatch', 'charter_interaction_seal_declaration_unresolved',
    'charter_interaction_node_unknown', 'charter_interaction_node_set_mismatch',
    'charter_interaction_entry_mismatch', 'charter_interaction_relic_mismatch',
    'charter_interaction_authorization_mismatch', 'charter_interaction_supply_unknown',
    'charter_interaction_supply_set_mismatch', 'charter_interaction_preparation_incomplete',
    'charter_interaction_grant_invalid', 'charter_interaction_candidate_invalid',
    'charter_invocation_request_invalid', 'charter_passage_denied', 'charter_seal_management_denied',
    'charter_authorization_version_denied', 'charter_node_disconnected', 'charter_coverage_out_of_boundary',
    'charter_reality_supply_unavailable', 'charter_atomic_commit_incomplete', 'charter_variable_out_of_boundary',
    'charter_unknown_conflict_grant', 'charter_cross_tier_authorization_denied', 'charter_conflict_not_won',
    'charter_environment_projection_no_long_term_state',
    'charter_environment_projection_no_current_region_entry',
    'charter_environment_projection_duplicate_current_region_entry',
    'charter_environment_projection_unknown_rule_entry',
    'charter_environment_projection_catalog_unavailable',
    'charter_environment_projection_duplicate_environment_id',
    'charter_environment_projection_asset_profile_mismatch',
    'formal_encounter_catalog_missing', 'formal_encounter_enemy_missing', 'formal_encounter_enemy_scope_invalid',
    'formal_encounter_combat_template_missing', 'formal_encounter_drops_missing', 'formal_encounter_drop_invalid',
    'formal_encounter_drop_item_missing', 'formal_encounter_drop_item_not_production',
    'formal_encounter_drop_item_stack_invalid', 'formal_encounter_already_consumed',
    'formal_encounter_session_missing', 'formal_encounter_inventory_grant_failed',
    'formal_encounter_enemy_not_configured', 'formal_encounter_spawn_failed',
    'formal_encounter_runtime_identity_invalid', 'formal_encounter_defeated_member_mismatch',
    'cell_not_configured', 'directed_edge_not_configured', 'reverse_directed_edge_not_permitted',
    'directed_edge_blocks_movement', 'directed_edge_blocks_effects', 'entity_obstacle', 'movement_blocked',
    'sight_blocked', 'height_rule_unconfigured', 'no_legal_path_within_query_limit', 'below_min_range',
    'above_max_range', 'occupied', 'distance_budget_exhausted', 'spatial_query_not_configured'
  )
  foreach ($key in $requiredKeys) {
    if ($key -notin $LanguageIds) {
      Add-Finding 'UI_TEXT_LANGUAGE_MISSING' $key 'Player-visible UI text projection references a missing Language key.'
    }
  }

  # 三个正式场景必须序列化 Language.csv TextAsset（场景重建不丢失玩家显示文本源）。
  $languageMetaPath = Join-Path $root 'src/Assets/DataConfig/Language.csv.meta'
  if (-not (Test-Path -LiteralPath $languageMetaPath -PathType Leaf)) {
    Add-Finding 'UI_TEXT_LANGUAGE_META_MISSING' 'Language.csv' 'Language.csv.meta is missing.'
    return
  }
  $languageGuid = @(Select-String -LiteralPath $languageMetaPath -Pattern '^\s*guid:\s*([0-9a-f]{32})\s*$').Matches
  if ($languageGuid.Count -ne 1) {
    Add-Finding 'UI_TEXT_LANGUAGE_META_INVALID' 'Language.csv' 'Language.csv.meta must declare exactly one guid.'
    return
  }
  foreach ($scene in @('WorldScene', 'SettlementScene', 'AdventureScene')) {
    $scenePath = Join-Path $root "src/Assets/Scenes/$scene.unity"
    if (-not (Test-Path -LiteralPath $scenePath -PathType Leaf) -or
        -not (Select-String -LiteralPath $scenePath -Pattern ([regex]::Escape($languageGuid[0].Groups[1].Value)) -Quiet)) {
      Add-Finding 'UI_TEXT_SCENE_LANGUAGE_REFERENCE_MISSING' $scene "$scene.unity must serialize the Language.csv TextAsset reference."
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
  Settlements = @('settlementId','displayNameKey','contentScope','settlementType','regionId','ownerFactionId','visualThemeId','features','adventureEntranceIds')
  Items = @('itemId','displayNameKey','descriptionKey','contentScope','itemCategory','maxStack')
  Bounties = @('bountyId','titleKey','descriptionKey','contentScope','issuerSettlementId','objectiveType','targetEnemyId','requiredCount','allowedAdventureId','rewardEntries','repeatPolicy')
  Enemies = @('name','type','aiType','realm','realmMultiplier','rootBone','physique','spirit','mind','reaction','talent','blockRate','blockReduction','soulShieldRate','soulShieldReduction','dodgeRate','critRate','critDamage','hitRateBonus','equippedSpells','dropTable','description','contentScope','dropEntries','unarmedBasicAttackProfileId')
  AttackProfiles = @('attackProfileId','displayNameKey','profileKind','basicBindingKind','contentScope','sourceAffiliation','realmRequirementId','elementRequirementId','effectType','damageElementId','physicalDamageMultiplier','soulDamageMultiplier','healAmount','buffMultiplier','defensePenetration','resourceKind','resourceCost','cooldownTicks','minCastRange','maxCastRange','targetingMode','areaCenterKind','areaShapeKind','areaRadius','areaLength','areaFanHalfAngleSteps','areaFacing','areaInnerRadius','areaEffectBlockers','areaAllowedFactions','areaAllowedStates','isDomain','isBloodline','specialEffectTextKey')
  EnvironmentProfiles = @('profileId','directedEdges','surfacePrototypeRefs','phenomenonChannels','phenomenonPairs','elementRelationRefs')
  CharterRuleDefinitions = @('ruleEntryId','displayName','ruleFamily','relationElement','compatiblePhenomena','positiveCommit','negativeCommit','requiredAuthority','requiredNodeTypes','scopeType','scopeTierCap','anchorNodeIds','propagationBoundaryProfileId','currentCoverageSet','affectedWorldVariables','conflictProfileId','failurePolicy','worldEventOutputs')
  CharterSites = @('siteId','displayNameKey','settlementId','passageCapabilityId','passageOperatorId','passageTargetId','passageProtocolState','passageStructureState','passagePowerState','interactionTimeProfileId','recognitionTiming','operationTiming','cancellationPolicy','facilityId','sealRelicId','sealManagerId','sealBeneficiaryId','sealAuthorizationVersionId','ruleEntryId','ruleEntryOccupancyId','nodeOccupancyId','jindanConflictEventId','jindanChallengeEventId','grantId','grantDefinitionVersion','grantTargetVariableId','grantChallengerId','grantQualificationSource','grantAllowedOperationId','grantTargetId','grantScopeId','grantBeneficiaryId','grantRealityAnchorId','grantResourceLedgerRef','grantCapacityLedgerRef','grantChallengeRuleTier','grantEffectiveAtTick','grantExpiresAtTick','grantIsRevoked','grantRevocationReason','grantDisplaySource','leftCandidateId','leftCandidateTargetVariableId','leftCandidateTargetId','leftCandidateHasVariableAuthority','leftCandidateHasLegalTarget','leftCandidatePositionRank','leftCandidateRealityAnchorRank','leftCandidateAlreadyPaidCost','leftCandidateHasActiveContinuousCarrier','leftCandidateConflictReserve','leftCandidatePulseCost','leftCandidateSettlementCooldown','rightCandidateId','rightCandidateTargetVariableId','rightCandidateTargetId','rightCandidateHasVariableAuthority','rightCandidateHasLegalTarget','rightCandidatePositionRank','rightCandidateRealityAnchorRank','rightCandidateAlreadyPaidCost','rightCandidateHasActiveContinuousCarrier','rightCandidateConflictReserve','rightCandidatePulseCost','rightCandidateSettlementCooldown','charterCandidateId','yuanyingConflictEventId','yuanyingTargetVariableId','yuanyingTargetId','yuanyingScopeId','yuanyingRealityAnchorId')
  FoundationPurpleMansionStates = @('schemaId','schemaVersion','characterId','foundationInstanceId','foundationDefinitionId','sourceGongFaId','phase','continuousProgress','phaseBoundarySetId','naturalMansionCapacity','releasedNaturalCapacity','expansionGrants','expandedMansionCapacity','totalMansionCapacity','mansionStates','effectBindings','guardianAbilities','enhancementNodes','cultivationActionState','closedRetreatPlan','jindanLock','fixtureId','expect','fixtureOnlyNumericProfile')
  JindanStaticStates = @('schemaId','schemaVersion','characterId','foundationPurpleMansionStateRef','mansionInputs','jindanCoreBinding','danxiang','stablePositionBindings','abilityLedgerBindings','fixtureId','expect','fixtureOnlyNumericProfile')
  NpcCultivationActionWeightProfiles = @('schemaId','schemaVersion','profileId','sourceContentHash','authorityKind','recordKind','recordId','actionStableId','legalityRuleSetRef','baseWeight','subjectiveRiskGateRef','enabled','sourceKind','selectorRef','priorityDelta','applicationOrder','capPolicyRef','diminishingPolicyRef','actionTotalCapPolicyRef','scope','minimum','maximum','appliesAfterSourceKind','inputBasis','activationThreshold','segments','outputBound','tieBreakPolicy','triggerStableId','riskThresholdDelta','knownEvidenceRefs','riskAssessmentRef','baseRiskThreshold','lifespanCapPolicyRef')
}
$tables = [ordered]@{
  GongFa = Get-CsvTable 'src/Assets/DataConfig/GongFa.csv' $schemas.GongFa
  Spells = Get-CsvTable 'src/Assets/DataConfig/Spells.csv' $schemas.Spells
  Skills = Get-CsvTable 'src/Assets/DataConfig/Skills.csv' $schemas.Skills
  Settlements = Get-CsvTable 'src/Assets/DataConfig/Settlements.csv' $schemas.Settlements @() @('content_scope_production')
  Items = Get-CsvTable 'src/Assets/DataConfig/Items.csv' $schemas.Items @() @('content_scope_production')
  Bounties = Get-CsvTable 'src/Assets/DataConfig/Bounties.csv' $schemas.Bounties @() @('content_scope_production')
  Enemies = Get-CsvTable 'src/Assets/DataConfig/Enemies.csv' $schemas.Enemies @('equippedSpells','contentScope','dropEntries','unarmedBasicAttackProfileId') @('guanzhong')
  AttackProfiles = Get-CsvTable 'src/Assets/DataConfig/AttackProfiles.csv' $schemas.AttackProfiles @('contentScope','sourceAffiliation','realmRequirementId','elementRequirementId','soulDamageMultiplier','healAmount','buffMultiplier','defensePenetration','areaCenterKind','areaShapeKind','areaRadius','areaLength','areaFanHalfAngleSteps','areaFacing','areaInnerRadius','areaEffectBlockers','areaAllowedFactions','areaAllowedStates','isDomain','isBloodline','specialEffectTextKey')
  EnvironmentProfiles = Get-CsvTable 'src/Assets/DataConfig/EnvironmentProfiles.csv' $schemas.EnvironmentProfiles
  CharterRuleDefinitions = Get-CsvTable 'src/Assets/DataConfig/CharterRuleDefinitions.csv' $schemas.CharterRuleDefinitions
  CharterSites = Get-CsvTable 'src/Assets/DataConfig/CharterSites.csv' $schemas.CharterSites
  FoundationPurpleMansionStates = Get-CsvTable 'src/Assets/DataConfig/FoundationPurpleMansionStates.csv' $schemas.FoundationPurpleMansionStates @('expansionGrants','effectBindings','guardianAbilities','enhancementNodes','cultivationActionState','closedRetreatPlan','fixtureId','expect','fixtureOnlyNumericProfile')
  JindanStaticStates = Get-CsvTable 'src/Assets/DataConfig/JindanStaticStates.csv' $schemas.JindanStaticStates @('fixtureId','expect','fixtureOnlyNumericProfile')
  NpcCultivationActionWeightProfiles = Get-CsvTable 'src/Assets/DataConfig/NpcCultivationActionWeightProfiles.csv' $schemas.NpcCultivationActionWeightProfiles $schemas.NpcCultivationActionWeightProfiles
}
$docs = [ordered]@{ GongFa = Get-ContentDocs (Get-GongFaName); Spells = Get-ContentDocs (Get-SpellName); Skills = Get-ContentDocs (Get-SkillName) }
foreach ($kind in $docs.Keys) {
  if ($docs[$kind].Count -ne $tables[$kind].Rows.Count) { Add-Finding 'DOC_CSV_COUNT_MISMATCH' $kind "Docs=$($docs[$kind].Count); CSV=$($tables[$kind].Rows.Count)." }
}

$languageIds = Get-LanguageIds
Test-AssetCoverage 'GongFa' $tables.GongFa.Rows 'src/Assets/Data/GongFa' 'GongFa'
Test-AssetCoverage 'Spells' $tables.Spells.Rows 'src/Assets/Data/Spells' 'Spell'
Test-AssetCoverage 'Skills' $tables.Skills.Rows 'src/Assets/Data/Skills' 'Skill'
Test-NpcCultivationActionWeightProfile $tables.NpcCultivationActionWeightProfiles
Test-FormalContentCatalog $tables.Settlements $tables.Items $tables.Bounties $tables.Enemies $languageIds
Test-FormalAttackProfileProjection $tables.AttackProfiles $languageIds
Test-CharterSiteProjection $tables.CharterSites $languageIds
Test-UiTextProjection $languageIds

foreach ($row in $tables.FoundationPurpleMansionStates.Rows) {
  foreach ($field in @('fixtureId', 'expect', 'fixtureOnlyNumericProfile')) {
    if (-not [string]::IsNullOrWhiteSpace([string]$row.$field)) {
      Add-Finding 'FPM_FIXTURE_IN_PRODUCTION' "FoundationPurpleMansionStates:$($row.characterId):$field" 'Fixture-only values are not an auditable production source.'
    }
  }
}

foreach ($row in $tables.JindanStaticStates.Rows) {
  foreach ($field in @('fixtureId', 'expect', 'fixtureOnlyNumericProfile')) {
    if (-not [string]::IsNullOrWhiteSpace([string]$row.$field)) {
      Add-Finding 'JD_FIXTURE_IN_PRODUCTION' "JindanStaticStates:$($row.characterId):$field" 'Fixture-only values are not an auditable production source.'
    }
  }
}

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
