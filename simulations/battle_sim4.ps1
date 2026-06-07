# Round 3: Balance adjustments - boost 魂魄 coefficients, reduce 根骨 HP coefficient
# Key changes:
# - 魂魄 攻系数: 筑基 5→6 (more 神攻 per point)
# - 根骨 HP系数: 筑基 12→10 (less HP from 根骨)
# - 魂魄 MP系数: 筑基 5→7 (more MP from 魂魄)

$REALM_ORDER = @("凡人","练气","筑基","金丹","元婴","化神","炼虚")
$SUBLEVELS = @{ "凡人"=1; "练气"=9; "筑基"=4; "金丹"=4; "元婴"=4; "化神"=4; "炼虚"=4 }
$TECH_INNATE = @{ "极品"=5; "上品"=4; "中品"=3; "下品"=2; "凡品"=1 }
$SPIRIT_MOD = @{ "凡品"=0.70; "下品"=0.85; "中品"=1.00; "上品"=1.20; "极品"=1.50 }

$BASE = @{
    "凡人" = @{ HP=30; MP=0; 肉攻=5; 神攻=5; 肉防=3; 神防=3; 反应=5; 移力=2; 神识=3 }
    "练气" = @{ HP=100; MP=10; 肉攻=25; 神攻=25; 肉防=20; 神防=20; 反应=15; 移力=3; 神识=5 }
    "筑基" = @{ HP=400; MP=100; 肉攻=120; 神攻=120; 肉防=100; 神防=100; 反应=50; 移力=4; 神识=8 }
}

# ADJUSTED: 根骨HP 12→10, 魂魄MP 5→7, 魂魄攻 5→6, 反应减半
$FACTOR = @{
    "凡人" = @{ HP=4; MP=0.5; 攻=1; 防=0.8; 反应=0.6; 移力=0.08; 神识=0.15 }
    "练气" = @{ HP=8; MP=2; 攻=3; 防=2; 反应=0.75; 移力=0.10; 神识=0.20 }
    "筑基" = @{ HP=10; MP=7; 攻=6; 防=4; 反应=1.5; 移力=0.12; 神识=0.25 }
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
    
    # 魂魄现在也有攻系数(6)，根骨用HP系数(10)
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
    $bonus = 1.0; if ($dir -eq 1) { $bonus = 1.10 } elseif ($dir -eq 2) { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($atk * $df * (1 - $res) * $bonus))
}
function SpirDmg($atk, $def, $resist, $dir) {
    $df = $atk / ($atk + $def); $res = $resist / 100.0
    $bonus = 1.0; if ($dir -eq 1) { $bonus = 1.10 } elseif ($dir -eq 2) { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($atk * $df * (1 - $res) * $bonus))
}

function SimOne($ca, $cb) {
    $hpA = $ca.HP; $hpB = $cb.HP; $ctA = (Get-Random -Min 0 -Max 100); $ctB = (Get-Random -Min 0 -Max 100)
    $aliveA=$true; $aliveB=$true; $actsA=0; $actsB=0; $dmgA=0; $dmgB=0
    $rng = New-Object System.Random
    for ($t=1; $t -le 300; $t++) {
        if ($aliveA) { $hpA=[Math]::Min($ca.HP,$hpA+$ca.生命恢复); $ctA+=$ca.反应 }
        if ($aliveB) { $hpB=[Math]::Min($cb.HP,$hpB+$cb.生命恢复); $ctB+=$cb.反应 }
        $actA=($aliveA -and $ctA -ge 100); $actB=($aliveB -and $ctB -ge 100)
        if ($actA -and $actB) {
            if ($ca.反应 -ge $cb.反应) {
                $dir=if($rng.NextDouble() -lt 0.33){2}elseif($rng.NextDouble() -lt 0.5){1}else{0}
                if($ca.Style -eq "physical"){$d=PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir; if($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率){$d=[Math]::Round($d/2)}}
                else{$d=SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir}
                if($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避率-$ca.命中率)){$d=0}
                if($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率){$d=[Math]::Round($d*$ca.暴击伤害/100)}
                $hpB-=$d; $actsA++; $dmgA+=$d; $ctA=0
                if($hpB -le 0){$hpB=0;$aliveB=$false;break}
                if($aliveB -and $ctB -ge 100){
                    $dir=if($rng.NextDouble() -lt 0.33){2}elseif($rng.NextDouble() -lt 0.5){1}else{0}
                    if($cb.Style -eq "physical"){$d=PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir; if($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率){$d=[Math]::Round($d/2)}}
                    else{$d=SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir}
                    if($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避率-$cb.命中率)){$d=0}
                    if($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率){$d=[Math]::Round($d*$cb.暴击伤害/100)}
                    $hpA-=$d; $actsB++; $dmgB+=$d; $ctB=0
                    if($hpA -le 0){$hpA=0;$aliveA=$false;break}
                }
            } else {
                $dir=if($rng.NextDouble() -lt 0.33){2}elseif($rng.NextDouble() -lt 0.5){1}else{0}
                if($cb.Style -eq "physical"){$d=PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir; if($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率){$d=[Math]::Round($d/2)}}
                else{$d=SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir}
                if($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避率-$cb.命中率)){$d=0}
                if($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率){$d=[Math]::Round($d*$cb.暴击伤害/100)}
                $hpA-=$d; $actsB++; $dmgB+=$d; $ctB=0
                if($hpA -le 0){$hpA=0;$aliveA=$false;break}
                if($aliveA -and $ctA -ge 100){
                    $dir=if($rng.NextDouble() -lt 0.33){2}elseif($rng.NextDouble() -lt 0.5){1}else{0}
                    if($ca.Style -eq "physical"){$d=PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir; if($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率){$d=[Math]::Round($d/2)}}
                    else{$d=SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir}
                    if($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避率-$ca.命中率)){$d=0}
                    if($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率){$d=[Math]::Round($d*$ca.暴击伤害/100)}
                    $hpB-=$d; $actsA++; $dmgA+=$d; $ctA=0
                    if($hpB -le 0){$hpB=0;$aliveB=$false;break}
                }
            }
        } elseif ($actA) {
            $dir=if($rng.NextDouble() -lt 0.33){2}elseif($rng.NextDouble() -lt 0.5){1}else{0}
            if($ca.Style -eq "physical"){$d=PhysDmg $ca.肉攻 $cb.肉防 $cb.物抗率 $dir; if($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率){$d=[Math]::Round($d/2)}}
            else{$d=SpirDmg $ca.神攻 $cb.神防 $cb.神魂抗率 $dir}
            if($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避率-$ca.命中率)){$d=0}
            if($d -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率){$d=[Math]::Round($d*$ca.暴击伤害/100)}
            $hpB-=$d; $actsA++; $dmgA+=$d; $ctA=0
            if($hpB -le 0){$hpB=0;$aliveB=$false;break}
        } elseif ($actB) {
            $dir=if($rng.NextDouble() -lt 0.33){2}elseif($rng.NextDouble() -lt 0.5){1}else{0}
            if($cb.Style -eq "physical"){$d=PhysDmg $cb.肉攻 $ca.肉防 $ca.物抗率 $dir; if($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率){$d=[Math]::Round($d/2)}}
            else{$d=SpirDmg $cb.神攻 $ca.神防 $ca.神魂抗率 $dir}
            if($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避率-$cb.命中率)){$d=0}
            if($d -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率){$d=[Math]::Round($d*$cb.暴击伤害/100)}
            $hpA-=$d; $actsB++; $dmgB+=$d; $ctB=0
            if($hpA -le 0){$hpA=0;$aliveA=$false;break}
        }
        if(-not $aliveA -or -not $aliveB){break}
    }
    $winner = if($aliveA -and -not $aliveB){"A"}elseif($aliveB -and -not $aliveA){"B"}else{"D"}
    return @{Winner=$winner;Ticks=$t;ActsA=$actsA;ActsB=$actsB;DmgA=$dmgA;DmgB=$dmgB}
}

# ===== Test builds =====
$wPhys = @{ 根骨=0.8; 魂魄=0.6; 神识=0.7; 资质=0.6; 气运=0.5 }
$wSpir = @{ 根骨=0.6; 魂魄=1.0; 神识=0.7; 资质=0.6; 气运=0.5 }
$wBal  = @{ 根骨=0.7; 魂魄=0.7; 神识=0.7; 资质=0.7; 气运=0.5 }

$builds = @(
    @{Name="物理专精"; Innate=@{根骨=38;魂魄=15;神识=22;资质=20;气运=20}; Style="physical"; W=$wPhys}
    @{Name="法术专精"; Innate=@{根骨=20;魂魄=40;神识=22;资质=18;气运=15}; Style="spiritual"; W=$wSpir}
    @{Name="均衡物理"; Innate=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20}; Style="physical"; W=$wBal}
    @{Name="均衡法术"; Innate=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20}; Style="spiritual"; W=$wBal}
    @{Name="肉盾型";   Innate=@{根骨=43;魂魄=12;神识=22;资质=18;气运=20}; Style="physical"; W=$wPhys}
    @{Name="灵修型";   Innate=@{根骨=15;魂魄=43;神识=18;资质=20;气运=19}; Style="spiritual"; W=$wSpir}
)

$chars = @()
foreach ($b in $builds) {
    $chars += BuildChar $b.Innate "筑基" 2 "上品" $b.W "中品" $b.Style
}

Write-Host "========== R3: 魂魄增强(MP系数5→7,攻系数5→6) 根骨削弱(HP系数12→10) 反应减半 =========="

foreach ($c in $chars) {
    Write-Host "`n$($c.Style) $($builds[[array]::IndexOf($chars,$c)].Name)"
    Write-Host "  先天: 骨$($c.根骨) 魂$($c.魂魄) 神$($c.神识) 资$($c.资质) 运$($c.气运)"
    Write-Host "  一级: HP$($c.HP) MP$($c.MP) 肉攻$($c.肉攻)/神攻$($c.神攻) 肉防$($c.肉防)/神防$($c.神防) 反应$($c.反应)"
    Write-Host "  二级: 生命恢复$($c.生命恢复)/t 格挡$($c.格挡率)% 物抗$($c.物抗率)% 神魂抗$($c.神魂抗率)% 暴击$($c.暴击率)% 暴伤$($c.暴击伤害)%"
}

$SIM = 2000
Write-Host "`n========== 关键对阵 ($SIM 次) =========="

$pairs = @(
    @{A=0;B=1;L="物理专精 vs 法术专精"}
    @{A=2;B=3;L="均衡物理 vs 均衡法术"}
    @{A=4;B=5;L="肉盾型 vs 灵修型"}
    @{A=0;B=4;L="物理专精 vs 肉盾型"}
    @{A=1;B=5;L="法术专精 vs 灵修型"}
)

foreach ($p in $pairs) {
    $a=$chars[$p.A]; $b=$chars[$p.B]
    $dAB = if($a.Style -eq "physical"){PhysDmg $a.肉攻 $b.肉防 $b.物抗率 0}else{SpirDmg $a.神攻 $b.神防 $b.神魂抗率 0}
    $dBA = if($b.Style -eq "physical"){PhysDmg $b.肉攻 $a.肉防 $a.物抗率 0}else{SpirDmg $b.神攻 $a.神防 $a.神魂抗率 0}
    $wA=0;$wB=0;$tT=0;$tAA=0;$tAB=0
    for($k=0;$k -lt $SIM;$k++){$r=SimOne $a $b;if($r.Winner -eq "A"){$wA++}elseif($r.Winner -eq "B"){$wB++};$tT+=$r.Ticks;$tAA+=$r.ActsA;$tAB+=$r.ActsB}
    $wrA=[Math]::Round($wA/$SIM*100,1); $wrB=[Math]::Round($wB/$SIM*100,1)
    Write-Host "$($p.L): A胜$wrA% B胜$wrB% | 伤害 $dAB/$dBA | HP $($a.HP)/$($b.HP) | 反应 $($a.反应)/$($b.反应) | t=$([Math]::Round($tT/$SIM,1)) AA=$([Math]::Round($tAA/$SIM,1)) AB=$([Math]::Round($tAB/$SIM,1))"
    if ($wrA -gt 30 -and $wrA -lt 70) { Write-Host "  ** BALANCED **" }
}

Write-Host "`n--- 区间验证 ---"
Write-Host "筑基区间: HP400~2000 攻120~600 防100~450 反应50~200"
foreach ($c in $chars) {
    $ok = ($c.HP -ge 400 -and $c.HP -le 2000 -and $c.反应 -ge 50 -and $c.反应 -le 200)
    Write-Host "$($builds[[array]::IndexOf($chars,$c)].Name): HP$($c.HP) 反应$($c.反应) -> $(if($ok){'OK'}else{'OUT'})"
}
