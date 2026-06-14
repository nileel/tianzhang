# ============================================================
# 导表工具 — CSV → Unity .asset 文件
# 用法: .\tools\import_csv.ps1
#       或 .\tools\import_csv.ps1 -Target spells
# ============================================================
param(
    [string]$Target = "all",  # all | gongfa | spells | skills | characters | enemies
    [switch]$DryRun           # 仅打印，不写文件
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$DataConfig = Join-Path $Root "src\Assets\DataConfig"
$DataOut = Join-Path $Root "src\Assets\Data"

# ═══ Script GUIDs ═══
$GUID_GongFa = "64215da84d9d4c9db5712467bda7dd09"
$GUID_Spell  = "0d2294bd6db111a4da8262a00f7142e2"
$GUID_Skill  = "2d548610d152a8a4cbc641db07baa4cb"
$GUID_Char   = "ed487ba0dcb5e9a43a39f8b1c4a934d1"

# ═══ 加载 Language.csv ═══
Write-Host "[1/2] 加载 Language.csv ..."
$lang = @{}
$langPath = Join-Path $DataConfig "Language.csv"
if (Test-Path $langPath) {
    Get-Content $langPath -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(#|$)' } | ForEach-Object {
        $cols = $_ -split ',', 2
        if ($cols.Length -ge 2 -and $cols[0].Trim()) {
            $lang[$cols[0].Trim()] = $cols[1].Trim()
        }
    }
}
Write-Host "  已加载 $($lang.Count) 条文本映射"

function T($id) {
    if ($lang.ContainsKey($id)) { return $lang[$id] }
    return $id
}

# ═══ 工具函数 ═══
function ParseCSV($line) {
    $result = @()
    $current = ""
    $inQuote = $false
    foreach ($c in $line.ToCharArray()) {
        if ($c -eq '"') { $inQuote = -not $inQuote; continue }
        if ($c -eq ',' -and -not $inQuote) { $result += $current; $current = ""; continue }
        $current += $c
    }
    $result += $current
    return $result
}

function New-Guid { [System.Guid]::NewGuid().ToString("N") }

function Write-Asset($path, $content) {
    if ($DryRun) {
        Write-Host "  [DRY] $path"
        return
    }
    $dir = Split-Path $path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($true))
    Write-Host "  OK  $path"
}

function Write-Meta($assetPath, $guid) {
    $metaPath = "$assetPath.meta"
    if ($DryRun) { return }
    $meta = @"
fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@
    [System.IO.File]::WriteAllText($metaPath, $meta, [System.Text.UTF8Encoding]::new($true))
}

function Make-AssetHeader($guid, $name) {
    return @"
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: $guid, type: 3}
  m_Name: $name
  m_EditorClassIdentifier: 
"@
}

function Esc-YAML($s) {
    # 中文转 Unicode 转义（Unity YAML 兼容）
    $result = ""
    foreach ($c in $s.ToCharArray()) {
        $code = [int]$c
        if ($code -gt 127) {
            $result += "\u{0:X4}" -f $code
        } else {
            $result += $c
        }
    }
    return $result
}

# ═══ 导入各表 ═══
function Import-GongFa {
    Write-Host "  导入功法..."
    $csv = Join-Path $DataConfig "GongFa.csv"
    if (-not (Test-Path $csv)) { Write-Host "  [跳过] 文件不存在"; return }
    $lines = Get-Content $csv -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(#|$)' } | Select-Object -Skip 1
    foreach ($line in $lines) {
        $cols = ParseCSV $line
        if ($cols.Length -lt 14) { continue }
        $nameId = $cols[0].Trim()
        $fileName = "GongFa_$nameId"
        $path = Join-Path $DataOut "GongFa\$fileName.asset"

        $name = T $nameId
        $affiliation = T $cols[1]
        $grade = T $cols[2]
        $elMain = T $cols[3]
        $elSub = T $cols[4]

        # 境界成长
        $growthYaml = ""
        foreach ($entry in $cols[12].Split('|')) {
            $parts = $entry.Split(':')
            if ($parts.Length -lt 2) { continue }
            $vals = $parts[1].Split(',')
            if ($vals.Length -lt 9) { continue }
            $realm = T $parts[0]
            $growthYaml += @"
  - realm: "$(Esc-YAML $realm)"
    hp: $($vals[0])
    mp: $($vals[1])
    physAtk: $($vals[2])
    magAtk: $($vals[3])
    physDef: $($vals[4])
    magDef: $($vals[5])
    reaction: $($vals[6])
    movePoints: $($vals[7])
    mindGrowth: $($vals[8])
"@
        }

        # 篇章
        $chapterYaml = ""
        if ($cols.Length -gt 13) {
            foreach ($entry in $cols[13].Split('|')) {
                $parts = $entry.Split(':')
                if ($parts.Length -lt 10) { continue }
                $chName = T $parts[0]
                $chRealm = T $parts[1]
                $chEffect = if ($parts.Length -gt 10) { T $parts[10] } else { "" }
                $chapterYaml += @"
  - chapterName: "$(Esc-YAML $chName)"
    realm: "$(Esc-YAML $chRealm)"
    soulShieldRate: $($parts[2])
    hitRate: $($parts[3])
    blockRate: $($parts[4])
    critRate: $($parts[5])
    critDamage: $($parts[6])
    dodgeRate: $($parts[7])
    magAtkBonus: $($parts[8])
    magDefBonus: $($parts[9])
    specialEffect: "$(Esc-YAML $chEffect)"
"@
            }
        }

        $asset = (Make-AssetHeader $GUID_GongFa $fileName) + @"
  gongFaName: "$(Esc-YAML $name)"
  affiliation: "$(Esc-YAML $affiliation)"
  grade: "$(Esc-YAML $grade)"
  elementMain: "$(Esc-YAML $elMain)"
  elementSub: "$(Esc-YAML $elSub)"
  starRootBone: $($cols[5])
  starPhysique: $($cols[6])
  starSpirit: $($cols[7])
  starMind: $($cols[8])
  starReaction: $($cols[9])
  starTalent: $($cols[10])
  starFortune: $($cols[11])
  subGrowth:
$growthYaml  chapters:
$chapterYaml
"@
        Write-Asset $path $asset
        Write-Meta $path (New-Guid)
    }
}

function Import-Spells {
    Write-Host "  导入术法..."
    $csv = Join-Path $DataConfig "Spells.csv"
    if (-not (Test-Path $csv)) { Write-Host "  [跳过] 文件不存在"; return }
    $lines = Get-Content $csv -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(#|$)' } | Select-Object -Skip 1
    foreach ($line in $lines) {
        $cols = ParseCSV $line
        if ($cols.Length -lt 14) { continue }
        $nameId = $cols[0].Trim()
        $fileName = "Spell_$nameId"
        $path = Join-Path $DataOut "Spells\$fileName.asset"
        $name = T $nameId

        $asset = (Make-AssetHeader $GUID_Spell $fileName) + @"
  spellName: "$(Esc-YAML $name)"
  type: $($cols[1])
  range: 1
  minRange: $($cols[2])
  maxRange: $($cols[3])
  mpCost: $($cols[4])
  cooldownTicks: $($cols[5])
  damageMultiplier: $($cols[6])
  healAmount: $($cols[7])
  buffMultiplier: 1
  cannotBlock: $($cols[8])
  cannotDodge: $($cols[9])
  penetratingShield: $($cols[10])
  stunChance: $($cols[11])
  stunDuration: 1
  realmScaleBase: 1
"@
        Write-Asset $path $asset
        Write-Meta $path (New-Guid)
    }
}

function Import-Skills {
    Write-Host "  导入神通..."
    $csv = Join-Path $DataConfig "Skills.csv"
    if (-not (Test-Path $csv)) { Write-Host "  [跳过] 文件不存在"; return }
    $lines = Get-Content $csv -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(#|$)' } | Select-Object -Skip 1
    foreach ($line in $lines) {
        $cols = ParseCSV $line
        if ($cols.Length -lt 16) { continue }
        $nameId = $cols[0].Trim()
        $fileName = "Skill_$nameId"
        $path = Join-Path $DataOut "Skills\$fileName.asset"
        $name = T $nameId
        $desc = T $cols[14]

        $asset = (Make-AssetHeader $GUID_Skill $fileName) + @"
  skillName: "$(Esc-YAML $name)"
  type: $($cols[1])
  range: 1
  minRange: $($cols[2])
  maxRange: $($cols[3])
  mpCost: $($cols[4])
  cooldownTicks: $($cols[5])
  damageMultiplier: $($cols[6])
  healAmount: $($cols[7])
  buffMultiplier: 1
  cannotBlock: $($cols[8])
  cannotDodge: $($cols[9])
  penetratingShield: $($cols[10])
  stunChance: $($cols[11])
  stunDuration: 1
  isDomain: $($cols[12])
  isBloodline: $($cols[13])
  specialEffectDesc: "$(Esc-YAML $desc)"
  realmScaleBase: 1
"@
        Write-Asset $path $asset
        Write-Meta $path (New-Guid)
    }
}

function Import-Characters {
    Write-Host "  导入角色..."
    $csv = Join-Path $DataConfig "Characters.csv"
    if (-not (Test-Path $csv)) { Write-Host "  [跳过] 文件不存在"; return }
    $lines = Get-Content $csv -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(#|$)' } | Select-Object -Skip 1
    foreach ($line in $lines) {
        $cols = ParseCSV $line
        if ($cols.Length -lt 19) { continue }
        $nameId = $cols[0].Trim()
        $fileName = "Char_$nameId"
        $path = Join-Path $DataOut "Characters\$fileName.asset"
        $name = T $nameId
        $gongFa = if ($cols[16]) { T $cols[16] } else { "" }
        $eqSpells = if ($cols[17]) { ($cols[17].Split('|') | ForEach-Object { "`"- " + (Esc-YAML $_) + "`"" }) -join "`n" } else { "[]" }
        $eqSkills = if ($cols[18]) { ($cols[18].Split('|') | ForEach-Object { "`"- " + (Esc-YAML $_) + "`"" }) -join "`n" } else { "[]" }

        $asset = (Make-AssetHeader $GUID_Char $fileName) + @"
  charName: "$(Esc-YAML $name)"
  baseLevel: 1
  rootBone: $($cols[2])
  physique: $($cols[3])
  spirit: $($cols[4])
  mind: $($cols[5])
  reaction: $($cols[6])
  talent: $($cols[7])
  hpBonus: 0
  mpBonus: 0
  physAtkBonus: 0
  magAtkBonus: 0
  physDefBonus: 0
  magDefBonus: 0
  blockRate: $($cols[8])
  blockReduction: $($cols[9])
  soulShieldRate: $($cols[10])
  soulShieldReduction: $($cols[11])
  dodgeRate: $($cols[12])
  critRate: $($cols[13])
  critDamage: $($cols[14])
  hitRateBonus: $($cols[15])
  realmMultiplier: $($cols[1])
  gongFaName: "$(Esc-YAML $gongFa)"
  equippedSpells:
$eqSpells
  equippedSkills:
$eqSkills
"@
        Write-Asset $path $asset
        Write-Meta $path (New-Guid)
    }
}

function Import-Enemies {
    Write-Host "  导入敌人..."
    $csv = Join-Path $DataConfig "Enemies.csv"
    if (-not (Test-Path $csv)) { Write-Host "  [跳过] 文件不存在"; return }
    $lines = Get-Content $csv -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(#|$)' } | Select-Object -Skip 1
    foreach ($line in $lines) {
        $cols = ParseCSV $line
        if ($cols.Length -lt 22) { continue }
        $nameId = $cols[0].Trim()
        $fileName = "Char_Enemy_$nameId"
        $path = Join-Path $DataOut "Characters\$fileName.asset"
        $name = T $nameId
        $eqSpells = if ($cols[19]) { ($cols[19].Split('|') | ForEach-Object { "`"- " + (Esc-YAML $_) + "`"" }) -join "`n" } else { "[]" }

        $asset = (Make-AssetHeader $GUID_Char $fileName) + @"
  charName: "$(Esc-YAML $name)"
  baseLevel: 1
  rootBone: $($cols[5])
  physique: $($cols[6])
  spirit: $($cols[7])
  mind: $($cols[8])
  reaction: $($cols[9])
  talent: $($cols[10])
  hpBonus: 0
  mpBonus: 0
  physAtkBonus: 0
  magAtkBonus: 0
  physDefBonus: 0
  magDefBonus: 0
  blockRate: $($cols[11])
  blockReduction: $($cols[12])
  soulShieldRate: $($cols[13])
  soulShieldReduction: $($cols[14])
  dodgeRate: $($cols[15])
  critRate: $($cols[16])
  critDamage: $($cols[17])
  hitRateBonus: $($cols[18])
  realmMultiplier: $($cols[4])
  gongFaName: ""
  equippedSpells:
$eqSpells
  equippedSkills:
[]
"@
        Write-Asset $path $asset
        Write-Meta $path (New-Guid)
    }
}

# ═══ 主流程 ═══
Write-Host "[2/2] 导入数据 ..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()

switch ($Target) {
    "all"        { Import-GongFa; Import-Spells; Import-Skills; Import-Characters; Import-Enemies }
    "gongfa"     { Import-GongFa }
    "spells"     { Import-Spells }
    "skills"     { Import-Skills }
    "characters" { Import-Characters }
    "enemies"    { Import-Enemies }
    default      { Write-Host "未知目标: $Target" }
}

$sw.Stop()
Write-Host "`n导表完成 ($($sw.ElapsedMilliseconds)ms)"
if ($DryRun) { Write-Host "[DRY RUN 模式 — 未写入文件]" }
