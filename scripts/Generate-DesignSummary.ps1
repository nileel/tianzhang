<# 
.SYNOPSIS
    扫描 docs/ 下所有设计文档，生成设计总结报告并写入 设计总结.txt。
.DESCRIPTION
    每周五定时运行，统计各分类下的设定文件数量、总字数、最近修改情况，
    并结合固定章节（设计决策、风险、亮点）生成一份结构化的设计总览。
#>

param(
    [string]$DocsRoot = "$PSScriptRoot\..\docs",
    [string]$OutputFile = "$PSScriptRoot\..\设计总结.txt",
    [string]$StateFile = "$PSScriptRoot\..\.agents\summary_state.json"
)

$ErrorActionPreference = "Stop"
$UTF8NoBom = New-Object System.Text.UTF8Encoding $false
$DocsRoot = (Resolve-Path -LiteralPath $DocsRoot).Path
$OutputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputFile)
$StateFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($StateFile)

function ConvertTo-StateHashtable {
    param([object]$JsonObject)

    $table = @{}
    if ($null -eq $JsonObject) {
        return $table
    }

    foreach ($prop in $JsonObject.PSObject.Properties) {
        $table[$prop.Name] = @{
            LastWriteTime = $prop.Value.LastWriteTime
            Size          = $prop.Value.Size
        }
    }

    return $table
}

function Get-DocStats {
    param([string]$Path)
    $files = Get-ChildItem -Path $Path -Recurse -Filter "*.txt" -ErrorAction SilentlyContinue
    $totalChars = 0
    $totalLines = 0
    foreach ($f in $files) {
        $content = Get-Content $f.FullName -Encoding UTF8 -Raw -ErrorAction SilentlyContinue
        if ($content) {
            $totalChars += $content.Length
            $totalLines += ($content -split "\n").Count
        }
    }
    return [PSCustomObject]@{
        Count      = $files.Count
        TotalChars = $totalChars
        TotalLines = $totalLines
        Files      = $files
    }
}

function Get-CategoryName {
    param([string]$DirName)
    switch ($DirName) {
        "基础设定"   { return "基础设定" }
        "角色养成"   { return "角色养成" }
        "门派"       { return "门派与势力" }
        "地图"       { return "地图与区域" }
        default      { return $DirName }
    }
}

function Get-NewOrModified {
    param($Files, $PreviousState)
    $result = @()
    foreach ($f in $Files) {
        $relPath = $f.FullName.Replace($DocsRoot, "").TrimStart("\")
        $prev = $PreviousState[$relPath]
        $lastWrite = $f.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
        if (-not $prev) {
            $result += "  [新增] $relPath ($lastWrite)"
        }
        elseif ($prev.LastWriteTime -ne $f.LastWriteTime.ToString("yyyy-MM-dd HH:mm")) {
            $result += "  [修改] $relPath ($lastWrite)"
        }
    }
    return $result
}

# ---------- 主逻辑 ----------

Write-Host "=== 天章设计总结生成器 ===" -ForegroundColor Cyan

# 1. 读取上次状态
$prevState = @{}
if (Test-Path $StateFile) {
    try {
        $prevStateJson = Get-Content $StateFile -Encoding UTF8 -Raw
        $prevState = ConvertTo-StateHashtable -JsonObject ($prevStateJson | ConvertFrom-Json)
    } catch {
        Write-Host "警告: 无法读取上次状态文件，视为首次运行" -ForegroundColor Yellow
    }
}

# 2. 全量统计
$allStats = Get-DocStats -Path $DocsRoot
Write-Host "总计: $($allStats.Count) 个设定文件, $($allStats.TotalChars) 字符" -ForegroundColor Green

# 按一级子目录分类
$categories = [ordered]@{}
$subdirs = Get-ChildItem -Path $DocsRoot -Directory | Sort-Object Name
foreach ($dir in $subdirs) {
    $stats = Get-DocStats -Path $dir.FullName
    if ($stats.Count -gt 0) {
        $catName = Get-CategoryName -DirName $dir.Name
        $categories[$catName] = $stats
    }
}

# 3. 检测变更
$allChanges = @()
$allNewModified = Get-NewOrModified -Files $allStats.Files -PreviousState $prevState
if ($allNewModified.Count -gt 0) {
    $allChanges += "### 本周变更"
    $allChanges += $allNewModified
    $allChanges += ""
}

# 4. 保存当前状态
$newState = @{}
foreach ($f in $allStats.Files) {
    $relPath = $f.FullName.Replace($DocsRoot, "").TrimStart("\")
    $newState[$relPath] = @{
        LastWriteTime = $f.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
        Size          = $f.Length
    }
}
$newStateJson = $newState | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText($StateFile, $newStateJson, $UTF8NoBom)

# 5. 子分类统计
$gongfaCategories = @{}
$gongfaDir = Join-Path $DocsRoot "角色养成\功法"
if (Test-Path $gongfaDir) {
    $gongfaSubdirs = Get-ChildItem -Path $gongfaDir -Directory
    foreach ($sd in $gongfaSubdirs) {
        $sdFiles = Get-ChildItem -Path $sd.FullName -Filter "*.txt" -ErrorAction SilentlyContinue
        $gongfaCategories[$sd.Name] = $sdFiles.Count
    }
}

$shufaCategories = @{}
$shufaDir = Join-Path $DocsRoot "角色养成\术法"
if (Test-Path $shufaDir) {
    $shufaSubdirs = Get-ChildItem -Path $shufaDir -Directory
    foreach ($sd in $shufaSubdirs) {
        $sdFiles = Get-ChildItem -Path $sd.FullName -Filter "*.txt" -ErrorAction SilentlyContinue
        $shufaCategories[$sd.Name] = $sdFiles.Count
    }
}

$shentongCategories = @{}
$shentongDir = Join-Path $DocsRoot "角色养成\神通"
if (Test-Path $shentongDir) {
    $shentongSubdirs = Get-ChildItem -Path $shentongDir -Directory
    foreach ($sd in $shentongSubdirs) {
        $sdFiles = Get-ChildItem -Path $sd.FullName -Filter "*.txt" -ErrorAction SilentlyContinue
        $shentongCategories[$sd.Name] = $sdFiles.Count
    }
}

$now = Get-Date -Format "yyyy-MM-dd HH:mm"

# 6. 组装输出
$summary = "# 天章游戏开发 — 核心系统设计总览`n`n"
$summary += "> 自动生成时间: $now`n"
$summary += "> 下次更新: 下周五`n"
$summary += "> 文档根目录: docs/`n`n"
$summary += "---`n`n"
$summary += "## 文档规模统计`n`n"
$summary += "| 分类 | 文件数 | 总字符数 |`n"
$summary += "|------|--------|----------|`n"

foreach ($catName in $categories.Keys) {
    $s = $categories[$catName]
    $charsFormatted = "{0:N0}" -f $s.TotalChars
    $summary += "| $catName | $($s.Count) | $charsFormatted |`n"
}

$summary += "`n---`n`n## 内容结构概览`n`n"
$summary += "### 基础设定`n"
$summary += "- 战斗系统: CTB 充能制 + 六角格战棋`n"
$summary += "- 修行境界: 7 境体系（凡人→炼虚）`n"
$summary += "- 伤害公式: 物理/神魂双线独立结算`n"
$summary += "- 灵根、属性、境界特性体系已建立`n`n"

$summary += "### 角色养成`n"
$summary += "- **功法**: 设计规范 + 模版已建立`n"
foreach ($k in $gongfaCategories.Keys | Sort-Object) {
    $summary += "  - $($k): $($gongfaCategories[$k]) 本`n"
}
$summary += "- **术法**: 设计规范 + 模版 + 6 种效能类型已建立`n"
foreach ($k in $shufaCategories.Keys | Sort-Object) {
    $summary += "  - $($k): $($shufaCategories[$k]) 个`n"
}
$summary += "- **神通**: 设计规范 + 模版 + 3 类上限已建立`n"
foreach ($k in $shentongCategories.Keys | Sort-Object) {
    $summary += "  - $($k): $($shentongCategories[$k]) 项`n"
}

$summary += "`n### 门派与势力`n"
$summary += "- 8 大天域、30+ 门派/势力覆盖`n"
$summary += "- 隐世势力: 守天人、无生海`n`n"

$summary += "### 地图`n"
$summary += "- 9 张区域地图（中州/关陇/太行/江左/河西/漠北/蜀川/辽海/陇西）`n`n"

$summary += "---`n`n## 设计决策汇总`n`n"
$summary += "### 战斗系统`n"
$summary += "- 时间以`刻`推进，CT ≥ 100 行动`n"
$summary += "- 术法冷却：大技能 +30~+80 CT`n"
$summary += "- 六角格 + 朝向：正面/侧面/背面影响命中率和伤害`n"
$summary += "- 移动可拆分、防御姿态、待机保留 50% CT`n`n"

$summary += "### 修行境界`n"
$summary += "- 凡人(1.0)→练气(1.5)→筑基(3.0)→金丹(6.0)→元婴(12.0)→化神(25.0)→炼虚(50.0)`n"
$summary += "- 每境界有独特机制解锁`n`n"

$summary += "### 伤害公式`n"
$summary += "- 物理/神魂双线独立结算`n"
$summary += "- 体修↔魂修形成克制三角`n`n"

$summary += "---`n`n## 待关注的设计风险`n`n"

$risks = @(
    "含弘光大典`被克制属性额外受伤`机制需要数值校验",
    "术法总量控制缺失 — 缺少`术法槽位`概念",
    "本命神通仅 1~2 个 — 是否加入额外变更途径",
    "元婴出窍风险极高 — 风险回报比需实战验证",
    "炼虚期功法未设计 — 现存功法多止于化神",
    "多炼虚同场 — 3+ 炼虚的领域碰撞规则未定义",
    "散修与门派差距 — 散修通用内容需确保可玩性",
    "功法篇章数一致性 — 未强制要求篇章数与境界上限对应"
)

for ($i = 0; $i -lt $risks.Count; $i++) {
    $summary += "$($i + 1). $($risks[$i])`n"
}

$summary += "`n---`n`n## 设计亮点`n`n"
$summary += "- CTB + 六角格朝向提供深度战术选择`n"
$summary += "- 物理/神魂双线独立结算创造有意义的克制关系`n"
$summary += "- 功法转化系统让换流派有代价但非不可行`n"
$summary += "- 太一道庭四大道脉血脉神通各具特色`n"
$summary += "- `命名不出典不立`保证文化一致性`n`n"

$summary += "---`n`n## 最近变更`n`n"
if ($allChanges.Count -gt 0) {
    $summary += ($allChanges -join "`n")
} else {
    $summary += "本周无文档变更。`n"
}

# 写入文件
[System.IO.File]::WriteAllText($OutputFile, $summary, $UTF8NoBom)

Write-Host ""
Write-Host "=== 完成 ===" -ForegroundColor Green
Write-Host "总览已写入: $OutputFile"
Write-Host "状态已保存: $StateFile"
