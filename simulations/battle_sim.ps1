# 太玄界战斗数值模拟器 v1.0 - PowerShell版

# ============ 游戏常量 ============
$REALM_COEFF = @{ "凡人"=1.0; "练气"=1.5; "筑基"=3.0; "金丹"=6.0; "元婴"=12.0; "化神"=25.0; "炼虚"=50.0 }
$REALM_ORDER = @("凡人","练气","筑基","金丹","元婴","化神","炼虚")
$SUBLEVELS = @{ "凡人"=1; "练气"=9; "筑基"=4; "金丹"=4; "元婴"=4; "化神"=4; "炼虚"=4 }
$TECH_INNATE = @{ "极品"=5; "上品"=4; "中品"=3; "下品"=2; "凡品"=1 }
$SPIRIT_MOD = @{ "凡品"=0.70; "下品"=0.85; "中品"=1.00; "上品"=1.20; "极品"=1.50 }

# 境界基础值
$BASE = @{
    "凡人" = @{ HP=30; MP=0; 肉攻=5; 神攻=5; 肉防=3; 神防=3; 反应=5; 移力=2; 神识=3 }
    "练气" = @{ HP=100; MP=10; 肉攻=25; 神攻=25; 肉防=20; 神防=20; 反应=15; 移力=3; 神识=5 }
    "筑基" = @{ HP=400; MP=100; 肉攻=120; 神攻=120; 肉防=100; 神防=100; 反应=50; 移力=4; 神识=8 }
}

# 境界系数
$FACTOR = @{
    "凡人" = @{ HP=4; MP=0.5; 攻=1; 防=0.8; 反应=0.6; 移力=0.08; 神识=0.15 }
    "练气" = @{ HP=8; MP=2; 攻=3; 防=2; 反应=1.5; 移力=0.10; 神识=0.20 }
    "筑基" = @{ HP=12; MP=5; 攻=5; 防=4; 反应=3; 移力=0.12; 神识=0.25 }
}

# 功法子等级增长
$TECH_GROWTH = @{
    "极品" = @{ HP=65; MP=45; 肉攻=22; 神攻=22; 肉防=18; 神防=18; 反应=12 }
    "上品" = @{ HP=40; MP=25; 肉攻=12; 神攻=12; 肉防=10; 神防=10; 反应=6 }
    "中品" = @{ HP=20; MP=12; 肉攻=6; 神攻=6; 肉防=5; 神防=5; 反应=4 }
}

function Clamp($v, $lo, $hi) { return [Math]::Max($lo, [Math]::Min($hi, $v)) }

function TotalSubs($realm, $subIdx) {
    $t = 0
    foreach ($r in $REALM_ORDER) {
        if ($r -eq $realm) { $t += ($subIdx + 1); break }
        $t += $SUBLEVELS[$r]
    }
    return $t
}

# ============ 角色构建 ============
function Build-Character {
    param($Name, $Config)
    
    $c = @{
        Name = $Name
        Realm = $Config.Realm
        Sub = $Config.Sub
        Style = if ($Config.Style) { $Config.Style } else { "physical" }
        Innate = @{
            根骨 = $Config.InnateRoot.根骨
            魂魄 = $Config.InnateRoot.魂魄
            神识 = $Config.InnateRoot.神识
            资质 = $Config.InnateRoot.资质
            气运 = $Config.InnateRoot.气运
        }
    }
    
    $totalSub = TotalSubs $c.Realm $c.Sub
    $totalPts = $totalSub * $TECH_INNATE[$Config.TechGrade]
    $w = $Config.Weights
    $tw = $w.根骨 + $w.魂魄 + $w.神识 + $w.资质 + $w.气运
    
    $keys = @("根骨","魂魄","神识","资质","气运")
    $alloc = @{}
    $sumAlloc = 0
    foreach ($k in $keys) {
        $alloc[$k] = [Math]::Round($totalPts * $w[$k] / $tw)
        $sumAlloc += $alloc[$k]
    }
    $diff = $totalPts - $sumAlloc
    if ($diff -ne 0) {
        $maxK = $keys | Sort-Object { $w[$_] } -Descending | Select-Object -First 1
        $alloc[$maxK] += $diff
    }
    foreach ($k in $keys) { $c.Innate[$k] += $alloc[$k] }
    
    $base = $BASE[$c.Realm]
    $fac = $FACTOR[$c.Realm]
    $gr = $TECH_GROWTH[$Config.TechGrade]
    $mod = $SPIRIT_MOD[$Config.SpiritGrade]
    $ri = [array]::IndexOf($REALM_ORDER, $c.Realm)
    
    $ip = @{}
    $ip.HP   = [Math]::Round($base.HP   + $c.Innate.根骨 * $fac.HP   * $w.根骨)
    $ip.MP   = [Math]::Round($base.MP   + $c.Innate.魂魄 * $fac.MP   * $w.魂魄)
    $ip.肉攻 = [Math]::Round($base.肉攻 + $c.Innate.根骨 * $fac.攻   * $w.根骨)
    $ip.神攻 = [Math]::Round($base.神攻 + $c.Innate.魂魄 * $fac.攻   * $w.魂魄)
    $ip.肉防 = [Math]::Round($base.肉防 + $c.Innate.根骨 * $fac.防   * $w.根骨)
    $ip.神防 = [Math]::Round($base.神防 + $c.Innate.神识 * $fac.防   * $w.神识)
    $ip.反应 = [Math]::Round($base.反应 + $c.Innate.神识 * $fac.反应 * $w.神识)
    $ip.移力 = [Math]::Round($base.移力 + $c.Innate.气运 * $fac.移力 * $w.气运)
    $ip.神识 = [Math]::Round($base.神识 + $c.Innate.神识 * $fac.神识 * $w.神识)
    
    $c.Primary = @{}
    $c.Primary.HP   = [Math]::Round(($ip.HP   + $totalSub * $gr.HP)   * $mod)
    $c.Primary.MP   = [Math]::Round(($ip.MP   + $totalSub * $gr.MP)   * $mod)
    $c.Primary.肉攻 = [Math]::Round(($ip.肉攻 + $totalSub * $gr.肉攻) * $mod)
    $c.Primary.神攻 = [Math]::Round(($ip.神攻 + $totalSub * $gr.神攻) * $mod)
    $c.Primary.肉防 = [Math]::Round(($ip.肉防 + $totalSub * $gr.肉防) * $mod)
    $c.Primary.神防 = [Math]::Round(($ip.神防 + $totalSub * $gr.神防) * $mod)
    $c.Primary.反应 = [Math]::Round(($ip.反应 + $totalSub * $gr.反应) * $mod)
    $c.Primary.移力 = [Math]::Round($ip.移力 + $ri)
    $c.Primary.神识 = [Math]::Round($ip.神识 + $ri)
    
    $s = @{}
    $s.生命恢复率 = Clamp (1.0 + $c.Innate.根骨 * 0.05) 0 6
    $s.生命恢复   = [Math]::Round($c.Primary.HP * $s.生命恢复率 / 100)
    $s.格挡率     = Clamp ($c.Innate.根骨 * 0.3) 0 40
    $s.物理抗性   = Clamp ($c.Innate.根骨 * 0.4) 0 50
    $s.灵力恢复率 = Clamp (0.5 + $c.Innate.魂魄 * 0.05) 0 5
    $s.灵力恢复   = [Math]::Round($c.Primary.MP * $s.灵力恢复率 / 100)
    $s.神魂抗性   = Clamp ($c.Innate.魂魄 * 0.4) 0 50
    $s.暴击伤害   = Clamp (150 + $c.Innate.魂魄 * 1.0) 150 300
    $s.暴击率     = Clamp ($c.Innate.神识 * 0.25) 0 40
    $s.命中率     = Clamp ($c.Innate.神识 * 0.30) 0 50
    $s.闪避率     = Clamp ($c.Innate.气运 * 0.3) 0 50
    $c.Secondary = $s
    
    return $c
}

# ============ 伤害计算 ============
function PhysDmg($atk, $def, $resist, $mult, $dir) {
    $realmR = 1.0
    $df = $atk / ($atk + $def)
    $res = $resist / 100.0
    $bonus = 1.0
    if ($dir -eq "侧面") { $bonus = 1.10 }
    elseif ($dir -eq "背面") { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($atk * $mult * $realmR * $df * (1 - $res) * $bonus))
}

function SpirDmg($atk, $def, $resist, $mult, $dir) {
    $realmR = 1.0
    $df = $atk / ($atk + $def)
    $res = $resist / 100.0
    $bonus = 1.0
    if ($dir -eq "侧面") { $bonus = 1.10 }
    elseif ($dir -eq "背面") { $bonus = 1.25 }
    return [Math]::Max(0, [Math]::Round($atk * $mult * $realmR * $df * (1 - $res) * $bonus))
}

# ============ 战斗模拟 ============
function SimBattle($A, $B) {
    $a = @{ HP = $A.Primary.HP; MaxHP = $A.Primary.HP; MP = $A.Primary.MP; MaxMP = $A.Primary.MP; CT = (Get-Random -Minimum 0 -Maximum 100); Alive = $true }
    $b = @{ HP = $B.Primary.HP; MaxHP = $B.Primary.HP; MP = $B.Primary.MP; MaxMP = $B.Primary.MP; CT = (Get-Random -Minimum 0 -Maximum 100); Alive = $true }
    
    $stats = @{ Ticks = 0; ActsA = 0; ActsB = 0; DmgA = 0; DmgB = 0; Winner = $null }
    $dirs = @("正面","正面","正面","正面","侧面","背面")
    $rng = New-Object System.Random
    
    for ($tick = 1; $tick -le 200; $tick++) {
        $stats.Ticks = $tick
        
        # 恢复
        if ($a.Alive) {
            $a.HP = [Math]::Min($a.MaxHP, $a.HP + $A.Secondary.生命恢复)
            $a.MP = [Math]::Min($a.MaxMP, $a.MP + $A.Secondary.灵力恢复)
            $a.CT += $A.Primary.反应
        }
        if ($b.Alive) {
            $b.HP = [Math]::Min($b.MaxHP, $b.HP + $B.Secondary.生命恢复)
            $b.MP = [Math]::Min($b.MaxMP, $b.MP + $B.Secondary.灵力恢复)
            $b.CT += $B.Primary.反应
        }
        
        # 行动顺序
        $actors = @()
        if ($a.Alive -and $a.CT -ge 100) { $actors += @{ Who="A"; At=$a; Df=$b; AO=$A; DO=$B; React=$A.Primary.反应 } }
        if ($b.Alive -and $b.CT -ge 100) { $actors += @{ Who="B"; At=$b; Df=$a; AO=$B; DO=$A; React=$B.Primary.反应 } }
        $actors = $actors | Sort-Object React -Descending
        
        foreach ($act in $actors) {
            if (-not $act.At.Alive -or $act.At.CT -lt 100 -or -not $act.Df.Alive) { continue }
            
            $dir = $dirs[$rng.Next(0,6)]
            $dmg = 0
            
            if ($act.AO.Style -eq "physical") {
                $dmg = PhysDmg $act.AO.Primary.肉攻 $act.DO.Primary.肉防 $act.DO.Secondary.物理抗性 1.0 $dir
                if ($dir -ne "背面" -and $rng.NextDouble()*100 -lt $act.DO.Secondary.格挡率) {
                    $dmg = [Math]::Round($dmg / 2)
                }
            } else {
                $dmg = SpirDmg $act.AO.Primary.神攻 $act.DO.Primary.神防 $act.DO.Secondary.神魂抗性 1.0 $dir
            }
            
            # 闪避
            $dodge = [Math]::Max(0, $act.DO.Secondary.闪避率 - $act.AO.Secondary.命中率)
            if ($rng.NextDouble()*100 -lt $dodge) { $dmg = 0 }
            
            # 暴击
            if ($dmg -gt 0 -and $rng.NextDouble()*100 -lt $act.AO.Secondary.暴击率) {
                $dmg = [Math]::Round($dmg * $act.AO.Secondary.暴击伤害 / 100)
            }
            
            $act.Df.HP -= $dmg
            if ($act.Df.HP -le 0) { $act.Df.HP = 0; $act.Df.Alive = $false }
            
            if ($act.Who -eq "A") { $stats.ActsA++; $stats.DmgA += $dmg }
            else { $stats.ActsB++; $stats.DmgB += $dmg }
            $act.At.CT = 0
        }
        
        if (-not $a.Alive -or -not $b.Alive) {
            $stats.Winner = if ($a.Alive) { "A" } else { "B" }
            break
        }
    }
    return $stats
}

function BatchSim($A, $B, $n) {
    $r = @{ WinsA=0; WinsB=0; Draws=0; Ticks=0; ActsA=0; ActsB=0; DmgA=0; DmgB=0 }
    for ($i=0; $i -lt $n; $i++) {
        $s = SimBattle $A $B
        if ($s.Winner -eq "A") { $r.WinsA++ }
        elseif ($s.Winner -eq "B") { $r.WinsB++ }
        else { $r.Draws++ }
        $r.Ticks += $s.Ticks
        $r.ActsA += $s.ActsA
        $r.ActsB += $s.ActsB
        $r.DmgA += $s.DmgA
        $r.DmgB += $s.DmgB
    }
    return $r
}

# ============ 主程序 ============
$cfgA = @{
    InnateRoot = @{ 根骨=40; 魂魄=15; 神识=25; 资质=20; 气运=15 }
    Realm = "筑基"; Sub = 2; TechGrade = "上品"
    Weights = @{ 根骨=0.8; 魂魄=0.8; 神识=0.7; 资质=0.6; 气运=0.5 }
    SpiritGrade = "中品"; Style = "physical"
}

$cfgB = @{
    InnateRoot = @{ 根骨=20; 魂魄=40; 神识=20; 资质=20; 气运=15 }
    Realm = "筑基"; Sub = 2; TechGrade = "上品"
    Weights = @{ 根骨=0.6; 魂魄=1.0; 神识=0.7; 资质=0.6; 气运=0.5 }
    SpiritGrade = "中品"; Style = "spiritual"
}

Write-Host "Building characters..."
$A = Build-Character "体修" $cfgA
$B = Build-Character "魂修" $cfgB

Write-Host "`n=== 角色A: 体修 ==="
Write-Host "先天: 根骨$($A.Innate.根骨) 魂魄$($A.Innate.魂魄) 神识$($A.Innate.神识) 资质$($A.Innate.资质) 气运$($A.Innate.气运)"
Write-Host "一级: HP$($A.Primary.HP) MP$($A.Primary.MP) 肉攻$($A.Primary.肉攻) 神攻$($A.Primary.神攻) 肉防$($A.Primary.肉防) 神防$($A.Primary.神防) 反应$($A.Primary.反应) 移力$($A.Primary.移力) 神识$($A.Primary.神识)"
Write-Host "二级: 生命恢复$($A.Secondary.生命恢复)/t 格挡$($A.Secondary.格挡率)% 物抗$($A.Secondary.物理抗性)% 暴击$($A.Secondary.暴击率)% 暴伤$($A.Secondary.暴击伤害)% 命中$($A.Secondary.命中率)% 闪避$($A.Secondary.闪避率)%"

Write-Host "`n=== 角色B: 魂修 ==="
Write-Host "先天: 根骨$($B.Innate.根骨) 魂魄$($B.Innate.魂魄) 神识$($B.Innate.神识) 资质$($B.Innate.资质) 气运$($B.Innate.气运)"
Write-Host "一级: HP$($B.Primary.HP) MP$($B.Primary.MP) 肉攻$($B.Primary.肉攻) 神攻$($B.Primary.神攻) 肉防$($B.Primary.肉防) 神防$($B.Primary.神防) 反应$($B.Primary.反应) 移力$($B.Primary.移力) 神识$($B.Primary.神识)"
Write-Host "二级: 生命恢复$($B.Secondary.生命恢复)/t 神魂抗性$($B.Secondary.神魂抗性)% 暴击$($B.Secondary.暴击率)% 暴伤$($B.Secondary.暴击伤害)% 命中$($B.Secondary.命中率)% 闪避$($B.Secondary.闪避率)%"

$dAB = PhysDmg $A.Primary.肉攻 $B.Primary.肉防 $B.Secondary.物理抗性 1.0 "正面"
$dBA = SpirDmg $B.Primary.神攻 $A.Primary.神防 $A.Secondary.神魂抗性 1.0 "正面"
$dABb = PhysDmg $A.Primary.肉攻 $B.Primary.肉防 $B.Secondary.物理抗性 1.0 "背面"
$dBAb = SpirDmg $B.Primary.神攻 $A.Primary.神防 $A.Secondary.神魂抗性 1.0 "背面"

Write-Host "`n--- 伤害验证 ---"
Write-Host "A->B 正面: $dAB (需$([Math]::Round($B.Primary.HP/$dAB,1))击)"
Write-Host "B->A 正面: $dBA (需$([Math]::Round($A.Primary.HP/$dBA,1))击)"
Write-Host "A->B 背面: $dABb (需$([Math]::Round($B.Primary.HP/$dABb,1))击)"
Write-Host "B->A 背面: $dBAb (需$([Math]::Round($A.Primary.HP/$dBAb,1))击)"

$SIM = 2000
Write-Host "`nRunning $SIM simulations..."
$r = BatchSim $A $B $SIM

Write-Host "`n--- 批量模拟 $SIM 次 ---"
Write-Host "胜率: A $([Math]::Round($r.WinsA/$SIM*100,1))% | B $([Math]::Round($r.WinsB/$SIM*100,1))% | 平 $([Math]::Round($r.Draws/$SIM*100,1))%"
Write-Host "平均ticks: $([Math]::Round($r.Ticks/$SIM,1))"
Write-Host "平均行动次数: A $([Math]::Round($r.ActsA/$SIM,1)) | B $([Math]::Round($r.ActsB/$SIM,1))"
$avgDmgA = if ($r.ActsA -gt 0) { [Math]::Round($r.DmgA/$r.ActsA) } else { 0 }
$avgDmgB = if ($r.ActsB -gt 0) { [Math]::Round($r.DmgB/$r.ActsB) } else { 0 }
Write-Host "平均每击伤害: A $avgDmgA | B $avgDmgB"

Write-Host "`n--- 区间验证 ---"
Write-Host "A.HP=$($A.Primary.HP) (筑基400~2000) $(if($A.Primary.HP -ge 400 -and $A.Primary.HP -le 2000){'OK'}else{'OUT!'})"
Write-Host "B.HP=$($B.Primary.HP) (400~2000) $(if($B.Primary.HP -ge 400 -and $B.Primary.HP -le 2000){'OK'}else{'OUT!'})"
Write-Host "A.肉攻=$($A.Primary.肉攻) (120~600) $(if($A.Primary.肉攻 -ge 120 -and $A.Primary.肉攻 -le 600){'OK'}else{'OUT!'})"
Write-Host "B.神攻=$($B.Primary.神攻) (120~600) $(if($B.Primary.神攻 -ge 120 -and $B.Primary.神攻 -le 600){'OK'}else{'OUT!'})"
Write-Host "A.肉防=$($A.Primary.肉防) (100~450) $(if($A.Primary.肉防 -ge 100 -and $A.Primary.肉防 -le 450){'OK'}else{'OUT!'})"
Write-Host "B.神防=$($B.Primary.神防) (100~450) $(if($B.Primary.神防 -ge 100 -and $B.Primary.神防 -le 450){'OK'}else{'OUT!'})"
Write-Host "A.反应=$($A.Primary.反应) (50~200) $(if($A.Primary.反应 -ge 50 -and $A.Primary.反应 -le 200){'OK'}else{'OUT!'})"
Write-Host "B.反应=$($B.Primary.反应) (50~200) $(if($B.Primary.反应 -ge 50 -and $B.Primary.反应 -le 200){'OK'}else{'OUT!'})"
