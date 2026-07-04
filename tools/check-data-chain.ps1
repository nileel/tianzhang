param(
  [switch]$FailOnMissingAssets
)

$ErrorActionPreference = "Stop"
$root = (Get-Location).Path
$failures = New-Object System.Collections.Generic.List[string]

function Join-ProjectPath {
  param([string[]]$Parts)

  $path = $root
  foreach ($part in $Parts) {
    $path = Join-Path $path $part
  }
  return $path
}

function Get-CultivationName {
  return (-join @([char]0x89D2, [char]0x8272, [char]0x517B, [char]0x6210))
}

function Get-GongFaName {
  return (-join @([char]0x529F, [char]0x6CD5))
}

function Get-SpellName {
  return (-join @([char]0x672F, [char]0x6CD5))
}

function Get-SkillName {
  return (-join @([char]0x795E, [char]0x901A))
}

function Get-ContentDocs {
  param([string]$ContentKind)

  $path = Join-ProjectPath @("docs", (Get-CultivationName), $ContentKind)
  if (-not (Test-Path -Path $path -PathType Container)) {
    $failures.Add("MISSING_DOC_DIR`t$ContentKind")
    return @()
  }

  return @(Get-ChildItem -Path $path -Recurse -File -Filter *.txt | Where-Object {
    $_.DirectoryName -ne $path
  })
}

function Get-CsvRows {
  param([string]$RelativePath)

  $path = Join-Path $root $RelativePath
  if (-not (Test-Path -Path $path -PathType Leaf)) {
    $failures.Add("MISSING_CSV`t$RelativePath")
    return @()
  }

  $lines = Get-Content -Path $path -Encoding UTF8
  $data = @()
  $headerSeen = $false
  foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) {
      continue
    }
    if (-not $headerSeen) {
      $headerSeen = $true
      continue
    }
    $data += $trimmed
  }
  return $data
}

function Get-LanguageRows {
  param([string]$RelativePath)

  $path = Join-Path $root $RelativePath
  if (-not (Test-Path -Path $path -PathType Leaf)) {
    $failures.Add("MISSING_LANGUAGE`t$RelativePath")
    return @()
  }

  return @(Get-Content -Path $path -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object {
    $_.Length -gt 0 -and -not $_.StartsWith("#")
  })
}

function Get-FirstFieldIds {
  param([string[]]$Rows)

  return @($Rows | ForEach-Object {
    ($_ -split ",", 2)[0].Trim()
  } | Where-Object { $_.Length -gt 0 })
}

function Find-Duplicates {
  param([string[]]$Values)

  return @($Values | Group-Object | Where-Object { $_.Count -gt 1 } | Select-Object -ExpandProperty Name)
}

function Test-AssetCoverage {
  param(
    [string]$Label,
    [string[]]$Ids,
    [string]$AssetDir,
    [string]$AssetPrefix
  )

  $dir = Join-Path $root $AssetDir
  if (-not (Test-Path -Path $dir -PathType Container)) {
    $failures.Add("MISSING_ASSET_DIR`t$AssetDir")
    return
  }

  $assetNames = @(Get-ChildItem -Path $dir -File -Filter *.asset | Select-Object -ExpandProperty Name)
  $expected = @($Ids | ForEach-Object { "$AssetPrefix`_$_.asset" })
  $missing = @($expected | Where-Object { $assetNames -notcontains $_ })
  $extra = @($assetNames | Where-Object { $expected -notcontains $_ })
  $valid = $expected.Count - $missing.Count

  [PSCustomObject]@{
    Category = $Label
    CsvRows = $Ids.Count
    AssetTotal = $assetNames.Count
    ValidAssets = $valid
    Missing = $missing.Count
    ExtraOrLegacy = $extra.Count
  }

  if ($FailOnMissingAssets -and $missing.Count -gt 0) {
    $failures.Add("MISSING_ASSETS`t$Label`t$($missing.Count)")
  }
}

$gongfaDocs = Get-ContentDocs (Get-GongFaName)
$spellDocs = Get-ContentDocs (Get-SpellName)
$skillDocs = Get-ContentDocs (Get-SkillName)

$gongfaRows = Get-CsvRows "src/Assets/DataConfig/GongFa.csv"
$spellRows = Get-CsvRows "src/Assets/DataConfig/Spells.csv"
$skillRows = Get-CsvRows "src/Assets/DataConfig/Skills.csv"
$languageRows = Get-LanguageRows "src/Assets/DataConfig/Language.csv"

$gongfaIds = Get-FirstFieldIds $gongfaRows
$spellIds = Get-FirstFieldIds $spellRows
$skillIds = Get-FirstFieldIds $skillRows
$languageIds = Get-FirstFieldIds $languageRows

$dupChecks = @(
  @{ Label = "GongFa.csv"; Values = $gongfaIds },
  @{ Label = "Spells.csv"; Values = $spellIds },
  @{ Label = "Skills.csv"; Values = $skillIds },
  @{ Label = "Language.csv"; Values = $languageIds }
)

foreach ($check in $dupChecks) {
  $dups = Find-Duplicates $check.Values
  foreach ($dup in $dups) {
    $failures.Add("DUPLICATE_ID`t$($check.Label)`t$dup")
  }
}

"docs/csv counts:"
@(
  [PSCustomObject]@{ Category = "GongFa"; Docs = $gongfaDocs.Count; CsvRows = $gongfaIds.Count },
  [PSCustomObject]@{ Category = "Spells"; Docs = $spellDocs.Count; CsvRows = $spellIds.Count },
  [PSCustomObject]@{ Category = "Skills"; Docs = $skillDocs.Count; CsvRows = $skillIds.Count }
) | Format-Table -AutoSize | Out-String -Width 200

"asset coverage:"
@(
  Test-AssetCoverage "GongFa" $gongfaIds "src/Assets/Data/GongFa" "GongFa"
  Test-AssetCoverage "Spells" $spellIds "src/Assets/Data/Spells" "Spell"
  Test-AssetCoverage "Skills" $skillIds "src/Assets/Data/Skills" "Skill"
) | Format-Table -AutoSize | Out-String -Width 200

"language keys: $($languageIds.Count)"

# === TQ-027: realm/contentScope alert layer (⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：check-data-chain.ps1 新增告警段) ===
"`n=== realm/contentScope alert (TQ-027) ==="

# -- realm_lianshen (P0: missing from Language.csv) --
$lianshenGongFa = @($gongfaRows | Where-Object { $_ -match 'realm_lianshen' })
$lianshenSpells = @($spellRows | Where-Object { $_ -match 'realm_lianshen' })
$lianshenSkills = @($skillRows | Where-Object { $_ -match 'realm_lianshen' })
$lianshenGongFaIds = Get-FirstFieldIds $lianshenGongFa
$lianshenSpellIds = Get-FirstFieldIds $lianshenSpells
$lianshenSkillIds = Get-FirstFieldIds $lianshenSkills
$lianshenTotal = $lianshenGongFa.Count + $lianshenSpells.Count + $lianshenSkills.Count

$hasLianshenInLang = ($languageRows | Where-Object { $_ -match '^realm_lianshen,' }).Count -gt 0

"realm_lianshen alert (P0: no Language key | F1: reserved/NPC/古修炼神):"
"  Language.csv has realm_lianshen: $hasLianshenInLang"
"  GongFa.csv rows: $($lianshenGongFa.Count) | IDs: $($lianshenGongFaIds -join ', ')"
"  Spells.csv rows: $($lianshenSpells.Count) | IDs: $($lianshenSpellIds -join ', ')"
"  Skills.csv rows: $($lianshenSkills.Count) | IDs: $($lianshenSkillIds -join ', ')"
"  TOTAL realm_lianshen: $lianshenTotal (8 expected: 4 GongFa + 2 Spells + 2 Skills)"
if (-not $hasLianshenInLang) {
  "  ** WARNING: realm_lianshen missing from Language.csv — runtime display-name lookup will fail **"
}

# -- realm_lianxu (P1: active in CSV but declared deleted in docs) --
$lianxuGongFa = @($gongfaRows | Where-Object { $_ -match 'realm_lianxu' })
$lianxuSpells = @($spellRows | Where-Object { $_ -match 'realm_lianxu' })
$lianxuSkills = @($skillRows | Where-Object { $_ -match 'realm_lianxu' })
$lianxuGongFaIds = Get-FirstFieldIds $lianxuGongFa
$lianxuSpellIds = Get-FirstFieldIds $lianxuSpells
$lianxuSkillIds = Get-FirstFieldIds $lianxuSkills
$lianxuTotal = $lianxuGongFa.Count + $lianxuSpells.Count + $lianxuSkills.Count

$hasLianxuInLang = ($languageRows | Where-Object { $_ -match '^realm_lianxu,' }).Count -gt 0

"realm_lianxu alert (P1: declared deleted but active in CSV; Language key exists):"
"  Language.csv has realm_lianxu: $hasLianxuInLang"
"  GongFa.csv rows: $($lianxuGongFa.Count) | IDs: $($lianxuGongFaIds -join ', ')"
"  Spells.csv rows: $($lianxuSpells.Count) | IDs: $($lianxuSpellIds -join ', ')"
"  Skills.csv rows: $($lianxuSkills.Count) | IDs: $($lianxuSkillIds -join ', ')"
"  TOTAL realm_lianxu: $lianxuTotal (12 expected: 6 GongFa + 3 Spells + 3 Skills)"

# -- contentScope distribution --
function Get-ContentScopeDist {
  param([string[]]$Rows)
  $dist = @{}
  foreach ($row in $Rows) {
    $parts = $row -split ','
    $last = $parts[-1].Trim()
    if ($last.Length -eq 0) { $last = "(empty)" }
    if (-not $dist.ContainsKey($last)) { $dist[$last] = 0 }
    $dist[$last]++
  }
  return $dist
}

$gongfaScope = Get-ContentScopeDist $gongfaRows
$spellScope = Get-ContentScopeDist $spellRows
$skillScope = Get-ContentScopeDist $skillRows

"contentScope distribution:"
"  GongFa.csv: $(($gongfaScope.Keys | ForEach-Object { "$_=$($gongfaScope[$_])" }) -join ', ')"
"  Spells.csv: $(($spellScope.Keys | ForEach-Object { "$_=$($spellScope[$_])" }) -join ', ')"
"  Skills.csv: $(($skillScope.Keys | ForEach-Object { "$_=$($skillScope[$_])" }) -join ', ')"

# -- Risk summary (read-only, never fails the check) --
"risk summary (TQ-027):"
"  realm_lianshen: $lianshenTotal rows across CSVs without Language key — runtime display-name failure risk"
"  realm_lianxu:   $lianxuTotal rows still active despite spec declaring it deleted — spec/implementation conflict"
"  contentScope:   reserved + (empty) rows should not leak into player-accessible content without review"

if ($failures.Count -gt 0) {
  "check-data-chain: FAILED"
  $failures | Sort-Object
  exit 1
}

"check-data-chain: OK"
