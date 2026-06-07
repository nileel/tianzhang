# Fix: minor adjustment - reduce A's innate 神识 to bring 反应 within cap
# Also fix battle sim logic

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
    "练气" = @{ HP=8; MP=2; 攻=3; 防=2; 反应=1.5; 移力=0.10; 神识=0.20 }
    "筑基" = @{ HP=12; MP=5; 攻=5; 防=4; 反应=3; 移力=0.12; 神识=0.25 }
}

$TECH_GROWTH = @{
    "极品" = @{ HP=65; MP=45; 肉攻=22; 神攻=22; 肉防=18; 神防=18; 反应=12 }
    "上品" = @{ HP=40; MP=25; 肉攻=12; 神攻=12; 肉防=10; 神防=10; 反应=6 }
    "中品" = @{ HP=20; MP=12; 肉攻=6; 神攻=6; 肉防=5; 神防=5; 反应=4 }
}

function TotalSubs($realm, $subIdx) {
    $t = 0
    foreach ($r in $REALM_ORDER) {
        if ($r -eq $realm) { $t += ($subIdx + 1); break }
        $t += $SUBLEVELS[$r]
    }
    return $t
}

function BuildChar($name, $innate, $realm, $sub, $techGrade, $weights, $spirit, $style) {
    $totalSub = TotalSubs $realm $sub
    $totalPts = $totalSub * $TECH_INNATE[$techGrade]
    $tw = $weights.根骨 + $weights.魂魄 + $weights.神识 + $weights.资质 + $weights.气运
    
    $c = @{}
    $ks = @("根骨","魂魄","神识","资质","气运")
    $sumAlloc = 0
    foreach ($k in $ks) {
        $c[$k] = $innate[$k] + [Math]::Round($totalPts * $weights[$k] / $tw)
        $sumAlloc += [Math]::Round($totalPts * $weights[$k] / $tw)
    }
    $diff = $totalPts - ($sumAlloc)
    if ($diff -ne 0) {
        $maxK = $ks | Sort-Object { $weights[$_] } -Descending | Select-Object -First 1
        $c[$maxK] += $diff
    }
    
    $base = $BASE[$realm]; $fac = $FACTOR[$realm]; $gr = $TECH_GROWTH[$techGrade]
    $mod = $SPIRIT_MOD[$spirit]; $ri = [array]::IndexOf($REALM_ORDER, $realm)
    
    $ipHP   = [Math]::Round($base.HP   + $c.根骨 * $fac.HP   * $weights.根骨)
    $ipMP   = [Math]::Round($base.MP   + $c.魂魄 * $fac.MP   * $weights.魂魄)
    $ip肉攻 = [Math]::Round($base.肉攻 + $c.根骨 * $fac.攻   * $weights.根骨)
    $ip神攻 = [Math]::Round($base.神攻 + $c.魂魄 * $fac.攻   * $weights.魂魄)
    $ip肉防 = [Math]::Round($base.肉防 + $c.根骨 * $fac.防   * $weights.根骨)
    $ip神防 = [Math]::Round($base.神防 + $c.神识 * $fac.防   * $weights.神识)
    $ip反应 = [Math]::Round($base.反应 + $c.神识 * $fac.反应 * $weights.神识)
    $ip移力 = [Math]::Round($base.移力 + $c.气运 * $fac.移力 * $weights.气运)
    $ip神识 = [Math]::Round($base.神识 + $c.神识 * $fac.神识 * $weights.神识)
    
    $c.HP   = [Math]::Round(($ipHP   + $totalSub * $gr.HP)   * $mod)
    $c.MP   = [Math]::Round(($ipMP   + $totalSub * $gr.MP)   * $mod)
    $c.肉攻 = [Math]::Round(($ip肉攻 + $totalSub * $gr.肉攻) * $mod)
    $c.神攻 = [Math]::Round(($ip神攻 + $totalSub * $gr.神攻) * $mod)
    $c.肉防 = [Math]::Round(($ip肉防 + $totalSub * $gr.肉防) * $mod)
    $c.神防 = [Math]::Round(($ip神防 + $totalSub * $gr.神防) * $mod)
    $c.反应 = [Math]::Round(($ip反应 + $totalSub * $gr.反应) * $mod)
    $c.移力 = [Math]::Round($ip移力 + $ri)
    $c.神识 = [Math]::Round($ip神识 + $ri)
    
    # 二级属性
    $c.生命恢复率 = [Math]::Max(0, [Math]::Min(6, 1.0 + $c.根骨 * 0.05))
    $c.生命恢复  = [Math]::Round($c.HP * $c.生命恢复率 / 100)
    $c.格挡率    = [Math]::Max(0, [Math]::Min(40, $c.根骨 * 0.3))
    $c.物抗率    = [Math]::Max(0, [Math]::Min(50, $c.根骨 * 0.4))
    $c.灵力恢复率 = [Math]::Max(0, [Math]::Min(5, 0.5 + $c.魂魄 * 0.05))
    $c.灵力恢复   = [Math]::Round($c.MP * $c.灵力恢复率 / 100)
    $c.神魂抗率   = [Math]::Max(0, [Math]::Min(50, $c.魂魄 * 0.4))
    $c.暴击伤害   = [Math]::Max(150, [Math]::Min(300, 150 + $c.魂魄 * 1.0))
    $c.暴击率     = [Math]::Max(0, [Math]::Min(40, $c.神识 * 0.25))
    $c.命中率     = [Math]::Max(0, [Math]::Min(50, $c.神识 * 0.30))
    $c.闪避率     = [Math]::Max(0, [Math]::Min(50, $c.气运 * 0.3))
    $c.Style = $style
    $c.Name = $name
    
    return $c
}

function PhysDmg($a肉攻, $d肉防, $d物抗, $dir) {
    $df = $a肉攻 / ($a肉攻 + $d肉防)
    $res = $d物抗 / 100.0
    $bonus = 1.0
    if ($dir -eq 1) { $bonus = 1.10 } elseif ($dir -eq 2) { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($a肉攻 * $df * (1 - $res) * $bonus))
}

function SpirDmg($a神攻, $d神防, $d神魂抗, $dir) {
    $df = $a神攻 / ($a神攻 + $d神防)
    $res = $d神魂抗 / 100.0
    $bonus = 1.0
    if ($dir -eq 1) { $bonus = 1.10 } elseif ($dir -eq 2) { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($a神攻 * $df * (1 - $res) * $bonus))
}

# ===== 单场战斗 =====
function SimOne($ca, $cb) {
    $hpA = $ca.HP; $hpB = $cb.HP
    $ctA = (Get-Random -Minimum 0 -Maximum 100)
    $ctB = (Get-Random -Minimum 0 -Maximum 100)
    $aliveA = $true; $aliveB = $true
    $actsA = 0; $actsB = 0; $dmgA = 0; $dmgB = 0
    $rng = New-Object System.Random
    # dir: 0=正面(50%), 1=侧面(25%), 2=背面(25%)
    
    for ($t = 1; $t -le 200; $t++) {
        # Recovery
        if ($aliveA) { $hpA = [Math]::Min($ca.HP, $hpA + $ca.生命恢复); $ctA += $ca.反应 }
        if ($aliveB) { $hpB = [Math]::Min($cb.HP, $hpB + $cb.生命恢复); $ctB += $cb.反应 }
        
        # Determine who acts
        $actA = ($aliveA -and $ctA -ge 100)
        $actB = ($aliveB -and $ctB -ge 100)
        
        # If both ready, higher reaction goes first
        if ($actA -and $actB) {
            if ($ca.反应 -ge $cb.反应) {
                # A first
                $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                if ($ca.Style -eq "physical") {
                    $d = PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir
                    if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率) { $d = [Math]::Round($d/2) }
                } else {
                    $d = SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir
                }
                $dodge = [Math]::Max(0, $cb.闪避率 - $ca.命中率)
                if ($rng.NextDouble()*100 -lt $dodge) { $d = 0 }
                if ($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率) { $d = [Math]::Round($d * $ca.暴击伤害 / 100) }
                $hpB -= $d; $actsA++; $dmgA += $d; $ctA = 0
                if ($hpB -le 0) { $hpB = 0; $aliveB = $false; break }
                
                # B second if still alive
                if ($aliveB -and $ctB -ge 100) {
                    $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                    if ($cb.Style -eq "physical") {
                        $d = PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir
                        if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率) { $d = [Math]::Round($d/2) }
                    } else {
                        $d = SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir
                    }
                    $dodge = [Math]::Max(0, $ca.闪避率 - $cb.命中率)
                    if ($rng.NextDouble()*100 -lt $dodge) { $d = 0 }
                    if ($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率) { $d = [Math]::Round($d * $cb.暴击伤害 / 100) }
                    $hpA -= $d; $actsB++; $dmgB += $d; $ctB = 0
                    if ($hpA -le 0) { $hpA = 0; $aliveA = $false; break }
                }
            } else {
                # B first
                $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                if ($cb.Style -eq "physical") {
                    $d = PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir
                    if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率) { $d = [Math]::Round($d/2) }
                } else {
                    $d = SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir
                }
                $dodge = [Math]::Max(0, $ca.闪避率 - $cb.命中率)
                if ($rng.NextDouble()*100 -lt $dodge) { $d = 0 }
                if ($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率) { $d = [Math]::Round($d * $cb.暴击伤害 / 100) }
                $hpA -= $d; $actsB++; $dmgB += $d; $ctB = 0
                if ($hpA -le 0) { $hpA = 0; $aliveA = $false; break }
                
                if ($aliveA -and $ctA -ge 100) {
                    $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                    if ($ca.Style -eq "physical") {
                        $d = PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir
                        if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率) { $d = [Math]::Round($d/2) }
                    } else {
                        $d = SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir
                    }
                    $dodge = [Math]::Max(0, $cb.闪避率 - $ca.命中率)
                    if ($rng.NextDouble()*100 -lt $dodge) { $d = 0 }
                    if ($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率) { $d = [Math]::Round($d * $ca.暴击伤害 / 100) }
                    $hpB -= $d; $actsA++; $dmgA += $d; $ctA = 0
                    if ($hpB -le 0) { $hpB = 0; $aliveB = $false; break }
                }
            }
        } elseif ($actA) {
            $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
            if ($ca.Style -eq "physical") {
                $d = PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir
                if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率) { $d = [Math]::Round($d/2) }
            } else {
                $d = SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir
            }
            $dodge = [Math]::Max(0, $cb.闪避率 - $ca.命中率)
            if ($rng.NextDouble()*100 -lt $dodge) { $d = 0 }
            if ($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率) { $d = [Math]::Round($d * $ca.暴击伤害 / 100) }
            $hpB -= $d; $actsA++; $dmgA += $d; $ctA = 0
            if ($hpB -le 0) { $hpB = 0; $aliveB = $false; break }
        } elseif ($actB) {
            $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
            if ($cb.Style -eq "physical") {
                $d = PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir
                if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率) { $d = [Math]::Round($d/2) }
            } else {
                $d = SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir
            }
            $dodge = [Math]::Max(0, $ca.闪避率 - $cb.命中率)
            if ($rng.NextDouble()*100 -lt $dodge) { $d = 0 }
            if ($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率) { $d = [Math]::Round($d * $cb.暴击伤害 / 100) }
            $hpA -= $d; $actsB++; $dmgB += $d; $ctB = 0
            if ($hpA -le 0) { $hpA = 0; $aliveA = $false; break }
        }
        
        if (-not $aliveA -or -not $aliveB) { break }
    }
    
    $winner = if ($aliveA -and -not $aliveB) { "A" } elseif ($aliveB -and -not $aliveA) { "B" } else { "D" }
    return @{ Winner=$winner; Ticks=$t; ActsA=$actsA; ActsB=$actsB; DmgA=$dmgA; DmgB=$dmgB }
}

# ===== 主程序 =====
$wa = @{ 根骨=0.8; 魂魄=0.8; 神识=0.7; 资质=0.6; 气运=0.5 }
$wb = @{ 根骨=0.6; 魂魄=1.0; 神识=0.7; 资质=0.6; 气运=0.5 }

# ADJUSTED: 神识从25降到22, 使筑基反应在200以内
$A = BuildChar "体修" @{根骨=40;魂魄=15;神识=22;资质=20;气运=15} "筑基" 2 "上品" $wa "中品" "physical"
$B = BuildChar "魂修" @{根骨=20;魂魄=40;神识=20;资质=20;气运=15} "筑基" 2 "上品" $wb "中品" "spiritual"

Write-Host "=== 角色A: 体修 ==="
Write-Host "先天: 根骨$($A.根骨) 魂魄$($A.魂魄) 神识$($A.神识) 资质$($A.资质) 气运$($A.气运)"
Write-Host "一级: HP$($A.HP) MP$($A.MP) 肉攻$($A.肉攻) 神攻$($A.神攻) 肉防$($A.肉防) 神防$($A.神防) 反应$($A.反应) 移力$($A.移力) 神识$($A.神识)"

Write-Host "`n=== 角色B: 魂修 ==="
Write-Host "先天: 根骨$($B.根骨) 魂魄$($B.魂魄) 神识$($B.神识) 资质$($B.资质) 气运$($B.气运)"
Write-Host "一级: HP$($B.HP) MP$($B.MP) 肉攻$($B.肉攻) 神攻$($B.神攻) 肉防$($B.肉防) 神防$($B.神防) 反应$($B.反应) 移力$($B.移力) 神识$($B.神识)"

$dAB = PhysDmg $A.肉攻 $B.肉防 $B.物抗率 0
$dBA = SpirDmg $B.神攻 $A.神防 $A.神魂抗率 0

Write-Host "`n--- 伤害验证(正面)---"
Write-Host "A->B: $dAB (需$([Math]::Round($B.HP/$dAB,1))击)  B.HP=$($B.HP)"
Write-Host "B->A: $dBA (需$([Math]::Round($A.HP/$dBA,1))击)  A.HP=$($A.HP)"
Write-Host "攻防比: A肉攻/B肉防=$([Math]::Round($A.肉攻/$B.肉防,2))  B神攻/A神防=$([Math]::Round($B.神攻/$A.神防,2))"

$SIM = 2000
Write-Host "`nRunning $SIM battles..."
$wA = 0; $wB = 0; $wD = 0; $tTicks = 0; $tActsA = 0; $tActsB = 0; $tDmgA = 0; $tDmgB = 0

for ($i = 0; $i -lt $SIM; $i++) {
    $r = SimOne $A $B
    if ($r.Winner -eq "A") { $wA++ } elseif ($r.Winner -eq "B") { $wB++ } else { $wD++ }
    $tTicks += $r.Ticks; $tActsA += $r.ActsA; $tActsB += $r.ActsB
    $tDmgA += $r.DmgA; $tDmgB += $r.DmgB
}

Write-Host "`n=== 战斗模拟 $SIM 次 ==="
Write-Host "胜率: A $([Math]::Round($wA/$SIM*100,1))% | B $([Math]::Round($wB/$SIM*100,1))% | 平 $([Math]::Round($wD/$SIM*100,1))%"
Write-Host "平均ticks: $([Math]::Round($tTicks/$SIM,1))"
Write-Host "平均行动: A $([Math]::Round($tActsA/$SIM,1)) | B $([Math]::Round($tActsB/$SIM,1))"
$adA = if ($tActsA -gt 0) { [Math]::Round($tDmgA/$tActsA) } else { 0 }
$adB = if ($tActsB -gt 0) { [Math]::Round($tDmgB/$tActsB) } else { 0 }
Write-Host "平均每击: A $adA | B $adB"

Write-Host "`n--- 区间验证 ---"
$checks = @(
    @{L="A.HP"; V=$A.HP; Lo=400; Hi=2000},
    @{L="B.HP"; V=$B.HP; Lo=400; Hi=2000},
    @{L="A.肉攻"; V=$A.肉攻; Lo=120; Hi=600},
    @{L="B.神攻"; V=$B.神攻; Lo=120; Hi=600},
    @{L="A.肉防"; V=$A.肉防; Lo=100; Hi=450},
    @{L="B.神防"; V=$B.神防; Lo=100; Hi=450},
    @{L="A.反应"; V=$A.反应; Lo=50; Hi=200},
    @{L="B.反应"; V=$B.反应; Lo=50; Hi=200}
)
foreach ($c in $checks) {
    $ok = if ($c.V -ge $c.Lo -and $c.V -le $c.Hi) { "OK" } else { "OUT!" }
    Write-Host "$($c.L)=$($c.V) ($($c.Lo)~$($c.Hi)) $ok"
}
