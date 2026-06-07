# Fix: use temp var for matrix access in string interpolation

$REALM_ORDER = @("凡人","练气","筑基","金丹","元婴","化神","炼虚")
$SUBLEVELS = @{ "凡人"=1; "练气"=9; "筑基"=4; "金丹"=4; "元婴"=4; "化神"=4; "炼虚"=4 }
$TECH_INNATE = @{ "极品"=5; "上品"=4; "中品"=3; "下品"=2; "凡品"=1 }
$SPIRIT_MOD = @{ "凡品"=0.70; "下品"=0.85; "中品"=1.00; "上品"=1.20; "极品"=1.50 }

$BASE = @{
    "凡人" = @{ HP=30; MP=0; 肉攻=5; 神攻=5; 肉防=3; 神防=3; 反应=5; 移力=2; 神识=3 }
    "练气" = @{ HP=100; MP=10; 肉攻=25; 神攻=25; 肉防=20; 神防=20; 反应=15; 移力=3; 神识=5 }
    "筑基" = @{ HP=400; MP=100; 肉攻=120; 神攻=120; 肉防=100; 神防=100; 反应=50; 移力=4; 神识=8 }
}

$FACTOR = @{
    "凡人" = @{ HP=4; MP=0.5; 攻=1; 防=0.8; 反应=0.6; 移力=0.08; 神识=0.15 }
    "练气" = @{ HP=8; MP=2; 攻=3; 防=2; 反应=0.75; 移力=0.10; 神识=0.20 }
    "筑基" = @{ HP=9; MP=7; 攻=6.5; 防=4; 反应=1.5; 移力=0.12; 神识=0.25 }
}

$TECH_GROWTH = @{
    "极品" = @{ HP=65; MP=45; 肉攻=22; 神攻=22; 肉防=18; 神防=18; 反应=12 }
    "上品" = @{ HP=40; MP=25; 肉攻=12; 神攻=12; 肉防=10; 神防=10; 反应=6 }
    "中品" = @{ HP=20; MP=12; 肉攻=6; 神攻=6; 肉防=5; 神防=5; 反应=4 }
}

function TotalSubs($realm, $subIdx) {
    $t = 0
    foreach ($r in $REALM_ORDER) { if ($r -eq $realm) { $t += ($subIdx + 1); break }; $t += $SUBLEVELS[$r] }
    return $t
}

function BuildChar($innate, $realm, $sub, $techGrade, $weights, $spirit, $style) {
    $totalSub = TotalSubs $realm $sub; $totalPts = $totalSub * $TECH_INNATE[$techGrade]
    $tw = $weights.根骨 + $weights.魂魄 + $weights.神识 + $weights.资质 + $weights.气运
    $c = @{}; $ks = @("根骨","魂魄","神识","资质","气运"); $sumAlloc = 0
    foreach ($k in $ks) {
        $c[$k] = $innate[$k] + [Math]::Round($totalPts * $weights[$k] / $tw)
        $sumAlloc += [Math]::Round($totalPts * $weights[$k] / $tw)
    }
    $diff = $totalPts - $sumAlloc
    if ($diff -ne 0) { $maxK = $ks | Sort-Object { $weights[$_] } -Descending | Select-Object -First 1; $c[$maxK] += $diff }
    
    $base = $BASE[$realm]; $fac = $FACTOR[$realm]; $gr = $TECH_GROWTH[$techGrade]
    $mod = $SPIRIT_MOD[$spirit]; $ri = [array]::IndexOf($REALM_ORDER, $realm)
    
    $c.HP   = [Math]::Round(([Math]::Round($base.HP   + $c.根骨 * $fac.HP   * $weights.根骨) + $totalSub * $gr.HP)   * $mod)
    $c.MP   = [Math]::Round(([Math]::Round($base.MP   + $c.魂魄 * $fac.MP   * $weights.魂魄) + $totalSub * $gr.MP)   * $mod)
    $c.肉攻 = [Math]::Round(([Math]::Round($base.肉攻 + $c.根骨 * $fac.攻   * $weights.根骨) + $totalSub * $gr.肉攻) * $mod)
    $c.神攻 = [Math]::Round(([Math]::Round($base.神攻 + $c.魂魄 * $fac.攻   * $weights.魂魄) + $totalSub * $gr.神攻) * $mod)
    $c.肉防 = [Math]::Round(([Math]::Round($base.肉防 + $c.根骨 * $fac.防   * $weights.根骨) + $totalSub * $gr.肉防) * $mod)
    $c.神防 = [Math]::Round(([Math]::Round($base.神防 + $c.神识 * $fac.防   * $weights.神识) + $totalSub * $gr.神防) * $mod)
    $c.反应 = [Math]::Round(([Math]::Round($base.反应 + $c.神识 * $fac.反应 * $weights.神识) + $totalSub * $gr.反应) * $mod)
    $c.移力 = [Math]::Round([Math]::Round($base.移力 + $c.气运 * $fac.移力 * $weights.气运) + $ri)
    $c.神识 = [Math]::Round([Math]::Round($base.神识 + $c.神识 * $fac.神识 * $weights.神识) + $ri)
    
    $c.生命恢复 = [Math]::Round($c.HP * [Math]::Max(0,[Math]::Min(6, 1.0+$c.根骨*0.05)) / 100)
    $c.格挡率   = [Math]::Max(0,[Math]::Min(40,$c.根骨*0.3))
    $c.物抗率   = [Math]::Max(0,[Math]::Min(50,$c.根骨*0.4))
    $c.灵力恢复 = [Math]::Round($c.MP * [Math]::Max(0,[Math]::Min(5, 0.5+$c.魂魄*0.05)) / 100)
    $c.神魂抗率 = [Math]::Max(0,[Math]::Min(50,$c.魂魄*0.4))
    $c.暴击伤害 = [Math]::Max(150,[Math]::Min(300,150+$c.魂魄*1.0))
    $c.暴击率   = [Math]::Max(0,[Math]::Min(40,$c.神识*0.25))
    $c.命中率   = [Math]::Max(0,[Math]::Min(50,$c.神识*0.30))
    $c.闪避率   = [Math]::Max(0,[Math]::Min(50,$c.气运*0.3))
    $c.Style = $style
    return $c
}

function PhysDmg($atk, $def, $resist, $dir) {
    $df = $atk / ($atk + $def); $res = $resist / 100.0
    $bonus = 1.0
    if ($dir -eq 1) { $bonus = 1.10 } elseif ($dir -eq 2) { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($atk * $df * (1 - $res) * $bonus))
}
function SpirDmg($atk, $def, $resist, $dir) {
    $df = $atk / ($atk + $def); $res = $resist / 100.0
    $bonus = 1.0
    if ($dir -eq 1) { $bonus = 1.10 } elseif ($dir -eq 2) { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($atk * $df * (1 - $res) * $bonus))
}

function SimOne($ca, $cb) {
    $hpA = $ca.HP; $hpB = $cb.HP
    $ctA = (Get-Random -Minimum 0 -Maximum 100); $ctB = (Get-Random -Minimum 0 -Maximum 100)
    $aliveA = $true; $aliveB = $true
    $rng = New-Object System.Random
    for ($t = 1; $t -le 300; $t++) {
        if ($aliveA) { $hpA = [Math]::Min($ca.HP, $hpA + $ca.生命恢复); $ctA += $ca.反应 }
        if ($aliveB) { $hpB = [Math]::Min($cb.HP, $hpB + $cb.生命恢复); $ctB += $cb.反应 }
        $actA = ($aliveA -and $ctA -ge 100); $actB = ($aliveB -and $ctB -ge 100)
        if ($actA -and $actB) {
            if ($ca.反应 -ge $cb.反应) {
                $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                $d = if ($ca.Style -eq "physical") { $d = PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir; if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率) { $d = [Math]::Round($d/2) }; $d } else { SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir }
                if ($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避率-$ca.命中率)) { $d = 0 }
                if ($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率) { $d = [Math]::Round($d * $ca.暴击伤害 / 100) }
                $hpB -= $d; $ctA = 0
                if ($hpB -le 0) { $hpB = 0; $aliveB = $false; break }
                if ($aliveB -and $ctB -ge 100) {
                    $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                    $d = if ($cb.Style -eq "physical") { $d = PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir; if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率) { $d = [Math]::Round($d/2) }; $d } else { SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir }
                    if ($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避率-$cb.命中率)) { $d = 0 }
                    if ($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率) { $d = [Math]::Round($d * $cb.暴击伤害 / 100) }
                    $hpA -= $d; $ctB = 0
                    if ($hpA -le 0) { $hpA = 0; $aliveA = $false; break }
                }
            } else {
                $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                $d = if ($cb.Style -eq "physical") { $d = PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir; if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率) { $d = [Math]::Round($d/2) }; $d } else { SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir }
                if ($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避率-$cb.命中率)) { $d = 0 }
                if ($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率) { $d = [Math]::Round($d * $cb.暴击伤害 / 100) }
                $hpA -= $d; $ctB = 0
                if ($hpA -le 0) { $hpA = 0; $aliveA = $false; break }
                if ($aliveA -and $ctA -ge 100) {
                    $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                    $d = if ($ca.Style -eq "physical") { $d = PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir; if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率) { $d = [Math]::Round($d/2) }; $d } else { SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir }
                    if ($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避率-$ca.命中率)) { $d = 0 }
                    if ($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率) { $d = [Math]::Round($d * $ca.暴击伤害 / 100) }
                    $hpB -= $d; $ctA = 0
                    if ($hpB -le 0) { $hpB = 0; $aliveB = $false; break }
                }
            }
        } elseif ($actA) {
            $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
            $d = if ($ca.Style -eq "physical") { $d = PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir; if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率) { $d = [Math]::Round($d/2) }; $d } else { SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir }
            if ($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避率-$ca.命中率)) { $d = 0 }
            if ($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率) { $d = [Math]::Round($d * $ca.暴击伤害 / 100) }
            $hpB -= $d; $ctA = 0
            if ($hpB -le 0) { $hpB = 0; $aliveB = $false; break }
        } elseif ($actB) {
            $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
            $d = if ($cb.Style -eq "physical") { $d = PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir; if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率) { $d = [Math]::Round($d/2) }; $d } else { SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir }
            if ($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避率-$cb.命中率)) { $d = 0 }
            if ($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率) { $d = [Math]::Round($d * $cb.暴击伤害 / 100) }
            $hpA -= $d; $ctB = 0
            if ($hpA -le 0) { $hpA = 0; $aliveA = $false; break }
        }
        if (-not $aliveA -or -not $aliveB) { break }
    }
    if ($aliveA -and -not $aliveB) { return "A" } elseif ($aliveB -and -not $aliveA) { return "B" } else { return "D" }
}

# ===== Main =====
$wPhys = @{ 根骨=0.8; 魂魄=0.6; 神识=0.7; 资质=0.6; 气运=0.5 }
$wSpir = @{ 根骨=0.6; 魂魄=1.0; 神识=0.7; 资质=0.6; 气运=0.5 }
$wBal  = @{ 根骨=0.7; 魂魄=0.7; 神识=0.7; 资质=0.7; 气运=0.5 }

$buildDefs = @(
    @{N="物理专精"; I=@{根骨=38;魂魄=15;神识=22;资质=20;气运=20}; S="physical"; W=$wPhys }
    @{N="法术专精"; I=@{根骨=20;魂魄=40;神识=22;资质=18;气运=15}; S="spiritual"; W=$wSpir }
    @{N="均衡物理"; I=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20}; S="physical"; W=$wBal }
    @{N="均衡法术"; I=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20}; S="spiritual"; W=$wBal }
    @{N="肉盾型";   I=@{根骨=43;魂魄=12;神识=22;资质=18;气运=20}; S="physical"; W=$wPhys }
    @{N="灵修型";   I=@{根骨=15;魂魄=43;神识=18;资质=20;气运=19}; S="spiritual"; W=$wSpir }
)

$chars = @()
foreach ($b in $buildDefs) { $chars += BuildChar $b.I "筑基" 2 "上品" $b.W "中品" $b.S }

$SIM = 1000
$N = $chars.Count
$m = @( @(0)*$N ) * $N  # fix: proper 2D array init

Write-Host "Running $($N)x$($N) matrix ($SIM battles per pair)...`n"
for ($i = 0; $i -lt $N; $i++) {
    for ($j = 0; $j -lt $N; $j++) {
        if ($i -eq $j) { $m[$i][$j] = -1; continue }
        $wins = 0
        for ($k = 0; $k -lt $SIM; $k++) { if ((SimOne $chars[$i] $chars[$j]) -eq "A") { $wins++ } }
        $m[$i][$j] = [Math]::Round($wins / $SIM * 100, 1)
    }
}

Write-Host "============================================================"
Write-Host "  筑基后期 6 Build 战斗胜率矩阵"
Write-Host "  (row attack vs col defend, $SIM battles/pair, 纯普攻)"
Write-Host "============================================================"
Write-Host ""
$hdr = "{0,-10}" -f "ATK \\ DEF"
for ($j = 0; $j -lt $N; $j++) { $hdr += "{0,11}" -f $buildDefs[$j].N }
Write-Host $hdr
Write-Host ("-" * (10 + 11*$N))
for ($i = 0; $i -lt $N; $i++) {
    $row = "{0,-10}" -f $buildDefs[$i].N
    for ($j = 0; $j -lt $N; $j++) {
        if ($i -eq $j) { $row += "{0,11}" -f "---" }
        else {
            $p = $m[$i][$j]
            $tag = "   "
            if ($p -ge 75) { $tag = "WIN" } elseif ($p -ge 55) { $tag = " + " } elseif ($p -ge 35) { $tag = " ~ " } else { $tag = "LOW" }
            $row += "{0,11}" -f "$tag $p%"
        }
    }
    Write-Host $row
}

Write-Host ""
Write-Host "  WIN=75%+(碾压)  +=55-75%(优势)  ~=35-55%(均势)  LOW=<35%(劣势)"
Write-Host ""

Write-Host "============================================================"
Write-Host "  Build 面板"
Write-Host "============================================================"
for ($i = 0; $i -lt $N; $i++) {
    $c = $chars[$i]; $st = if ($buildDefs[$i].S -eq "physical") { "物攻" } else { "神攻" }
    Write-Host ("  {0} [{1}] HP={2} MP={3} 攻={4}/{5} 防={6}/{7} 反应={8} 格挡={9}% 物抗={10}% 魂抗={11}%" -f $buildDefs[$i].N, $st, $c.HP, $c.MP, $c.肉攻, $c.神攻, $c.肉防, $c.神防, $c.反应, $c.格挡率, $c.物抗率, $c.神魂抗率)
}
