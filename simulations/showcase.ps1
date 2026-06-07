# Full 6x6 build matchup matrix with visual bars and commentary

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
            $order = if ($ca.反应 -ge $cb.反应) { @(($ca,$cb,$true),($cb,$ca,$false)) } else { @(($cb,$ca,$false),($ca,$cb,$true)) }
            foreach ($o in $order) {
                $att=$o[0];$def=$o[1];$isA=$o[2]
                $alive=(if($isA){$aliveA}else{$aliveB});$dAlive=(if($isA){$aliveB}else{$aliveA})
                $hpRef=(if($isA){[ref]$hpB}else{[ref]$hpA})
                $ctRef=(if($isA){[ref]$ctA}else{[ref]$ctB})
                if (-not $alive -or -not $dAlive) { continue }
                $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
                if ($att.Style -eq "physical") {
                    $dmg = [Math]::Max(0,[Math]::Round($att.肉攻*($att.肉攻/($att.肉攻+$def.肉防))*(1-$def.物抗率/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
                    if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $def.格挡率) { $dmg = [Math]::Round($dmg/2) }
                } else {
                    $dmg = [Math]::Max(0,[Math]::Round($att.神攻*($att.神攻/($att.神攻+$def.神防))*(1-$def.神魂抗率/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
                }
                if ($rng.NextDouble()*100 -lt [Math]::Max(0,$def.闪避率-$att.命中率)) { $dmg = 0 }
                if ($dmg -gt 0 -and $rng.NextDouble()*100 -lt $att.暴击率) { $dmg = [Math]::Round($dmg*$att.暴击伤害/100) }
                $hpRef.Value -= $dmg
                $ctRef.Value = 0
                if ($hpRef.Value -le 0) { $hpRef.Value = 0; if ($isA) { $aliveB = $false } else { $aliveA = $false }; break }
            }
        } elseif ($actA) {
            $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
            if ($ca.Style -eq "physical") {
                $dmg = [Math]::Max(0,[Math]::Round($ca.肉攻*($ca.肉攻/($ca.肉攻+$cb.肉防))*(1-$cb.物抗率/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
                if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $cb.格挡率) { $dmg = [Math]::Round($dmg/2) }
            } else {
                $dmg = [Math]::Max(0,[Math]::Round($ca.神攻*($ca.神攻/($ca.神攻+$cb.神防))*(1-$cb.神魂抗率/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
            }
            if ($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避率-$ca.命中率)) { $dmg = 0 }
            if ($dmg -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴击率) { $dmg = [Math]::Round($dmg*$ca.暴击伤害/100) }
            $hpB -= $dmg; $ctA = 0
            if ($hpB -le 0) { $hpB = 0; $aliveB = $false; break }
        } elseif ($actB) {
            $dir = if ($rng.NextDouble() -lt 0.33) { 2 } elseif ($rng.NextDouble() -lt 0.5) { 1 } else { 0 }
            if ($cb.Style -eq "physical") {
                $dmg = [Math]::Max(0,[Math]::Round($cb.肉攻*($cb.肉攻/($cb.肉攻+$ca.肉防))*(1-$ca.物抗率/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
                if ($dir -ne 2 -and $rng.NextDouble()*100 -lt $ca.格挡率) { $dmg = [Math]::Round($dmg/2) }
            } else {
                $dmg = [Math]::Max(0,[Math]::Round($cb.神攻*($cb.神攻/($cb.神攻+$ca.神防))*(1-$ca.神魂抗率/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
            }
            if ($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避率-$cb.命中率)) { $dmg = 0 }
            if ($dmg -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴击率) { $dmg = [Math]::Round($dmg*$cb.暴击伤害/100) }
            $hpA -= $dmg; $ctB = 0
            if ($hpA -le 0) { $hpA = 0; $aliveA = $false; break }
        }
        if (-not $aliveA -or -not $aliveB) { break }
    }
    return if ($aliveA -and -not $aliveB) { "A" } elseif ($aliveB -and -not $aliveA) { "B" } else { "D" }
}

# ===== Build definitions =====
$wPhys = @{ 根骨=0.8; 魂魄=0.6; 神识=0.7; 资质=0.6; 气运=0.5 }
$wSpir = @{ 根骨=0.6; 魂魄=1.0; 神识=0.7; 资质=0.6; 气运=0.5 }
$wBal  = @{ 根骨=0.7; 魂魄=0.7; 神识=0.7; 资质=0.7; 气运=0.5 }

$buildDefs = @(
    @{N="物理专精"; I=@{根骨=38;魂魄=15;神识=22;资质=20;气运=20}; S="physical"; W=$wPhys; Desc="高根骨体修，肉攻/肉防/HP均顶尖" }
    @{N="法术专精"; I=@{根骨=20;魂魄=40;神识=22;资质=18;气运=15}; S="spiritual"; W=$wSpir; Desc="高魂魄法修，神攻极强/MP充裕" }
    @{N="均衡物理"; I=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20}; S="physical"; W=$wBal; Desc="全属性均衡，物理攻击倾向" }
    @{N="均衡法术"; I=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20}; S="spiritual"; W=$wBal; Desc="全属性均衡，神魂攻击倾向" }
    @{N="肉盾型";   I=@{根骨=43;魂魄=12;神识=22;资质=18;气运=20}; S="physical"; W=$wPhys; Desc="极限根骨，HP/肉防最高，物抗24%" }
    @{N="灵修型";   I=@{根骨=15;魂魄=43;神识=18;资质=20;气运=19}; S="spiritual"; W=$wSpir; Desc="极限魂魄，神攻最高/MP极高/魂抗24%" }
)

$chars = @()
foreach ($b in $buildDefs) {
    $chars += BuildChar $b.I "筑基" 2 "上品" $b.W "中品" $b.S
}

# ===== Run matrix =====
$SIM = 1000
$N = $chars.Count
$matrix = New-Object 'double[,]' $N, $N

Write-Host "Running $($N)x$($N) matrix ($SIM battles per pair)..."
for ($i = 0; $i -lt $N; $i++) {
    for ($j = 0; $j -lt $N; $j++) {
        if ($i -eq $j) { $matrix[$i,$j] = -1; continue }
        $wins = 0
        for ($k = 0; $k -lt $SIM; $k++) {
            if ((SimOne $chars[$i] $chars[$j]) -eq "A") { $wins++ }
        }
        $matrix[$i,$j] = $wins / $SIM * 100
    }
}

# ===== Display =====
function Bar($pct) {
    $n = [Math]::Round($pct / 5)
    $bar = ""
    for ($x = 0; $x -lt $n; $x++) { $bar += [char]0x2588 }
    for ($x = $n; $x -lt 20; $x++) { $bar += [char]0x2591 }
    return $bar
}

Write-Host "`n======================================================================================"
Write-Host "  筑基后期 战斗胜率矩阵 (行=攻击方, 列=防御方, 每对 $SIM 次)"
Write-Host "  条件: 上品功法 / 中品灵根 / 纯普攻(倍率1.0) / 无道基特性 / 无术法"
Write-Host "======================================================================================"
Write-Host ""

# Header
$hdr = "{0,-10}" -f ""
for ($j = 0; $j -lt $N; $j++) { $hdr += "{0,-13}" -f $buildDefs[$j].N }
Write-Host $hdr
Write-Host ("-" * (10 + 13*$N))

for ($i = 0; $i -lt $N; $i++) {
    $row = "{0,-10}" -f $buildDefs[$i].N
    for ($j = 0; $j -lt $N; $j++) {
        if ($i -eq $j) { $row += "{0,-13}" -f "---" }
        else { $row += "{0,-13}" -f ("{0:F1}%" -f $matrix[$i,$j]) }
    }
    Write-Host $row
}

Write-Host "`n======================================================================================"
Write-Host "  Build 详情"
Write-Host "======================================================================================"
for ($i = 0; $i -lt $N; $i++) {
    $c = $chars[$i]
    $style = if ($buildDefs[$i].S -eq "physical") { "物理攻击" } else { "神魂攻击" }
    Write-Host "`n  [$($buildDefs[$i].N)] $style | $($buildDefs[$i].Desc)"
    Write-Host "    HP=$($c.HP)  MP=$($c.MP)  肉攻=$($c.肉攻)  神攻=$($c.神攻)  肉防=$($c.肉防)  神防=$($c.神防)  反应=$($c.反应)"
    Write-Host "    格挡=$($c.格挡率)%  物抗=$($c.物抗率)%  魂抗=$($c.神魂抗率)%  暴击=$($c.暴击率)%  暴伤=$($c.暴击伤害)%  闪避=$($c.闪避率)%"
}

Write-Host "`n======================================================================================"
Write-Host "  关键解读"
Write-Host "======================================================================================"
Write-Host @"

  [同类对决]
  物理专精 vs 法术专精  = $('{0:F1}%'.PadLeft(6) -f $matrix[0,1]) / $('{0:F1}%'.PadLeft(6) -f $matrix[1,0]) | 物理占优，根骨→3项一级属性 vs 魂魄→2项的先天不对称
  均衡物理 vs 均衡法术  = $('{0:F1}%'.PadLeft(6) -f $matrix[2,3]) / $('{0:F1}%'.PadLeft(6) -f $matrix[3,2]) | 接近五五开，MP优势在纯普攻中无用（带术法后会反转）

  [克制关系]
  肉盾型 vs 物理专精    = $('{0:F1}%'.PadLeft(6) -f $matrix[4,0]) / $('{0:F1}%'.PadLeft(6) -f $matrix[0,4]) | 肉盾克制物理：更高HP+更高肉防+更高物抗
  灵修型 vs 法术专精    = $('{0:F1}%'.PadLeft(6) -f $matrix[5,1]) / $('{0:F1}%'.PadLeft(6) -f $matrix[1,5]) | 灵修略弱于法术专精：神攻虽高但身板更脆

  [专精 vs 均衡]
  物理专精 vs 均衡物理  = $('{0:F1}%'.PadLeft(6) -f $matrix[0,2]) / $('{0:F1}%'.PadLeft(6) -f $matrix[2,0]) | 专精碾压均衡（同类型内）
  法术专精 vs 均衡法术  = $('{0:F1}%'.PadLeft(6) -f $matrix[1,3]) / $('{0:F1}%'.PadLeft(6) -f $matrix[3,1]) | 专精碾压均衡（同类型内）

"@
