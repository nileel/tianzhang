# Round 4: Fine-tune - 根骨HP 10→9, more iterations

$REALM_ORDER = @("凡人","练气","筑基","金丹","元婴","化神","炼虚")
$SUBLEVELS = @{ "凡人"=1; "练气"=9; "筑基"=4; "金丹"=4; "元婴"=4; "化神"=4; "炼虚"=4 }
$TECH_INNATE = @{ "极品"=5; "上品"=4; "中品"=3; "下品"=2; "凡品"=1 }
$SPIRIT_MOD = @{ "凡品"=0.70; "下品"=0.85; "中品"=1.00; "上品"=1.20; "极品"=1.50 }

$BASE = @{
    "凡人" = @{ HP=30; MP=0; 肉攻=5; 神攻=5; 肉防=3; 神防=3; 反应=5; 移力=2; 神识=3 }
    "练气" = @{ HP=100; MP=10; 肉攻=25; 神攻=25; 肉防=20; 神防=20; 反应=15; 移力=3; 神识=5 }
    "筑基" = @{ HP=400; MP=100; 肉攻=120; 神攻=120; 肉防=100; 神防=100; 反应=50; 移力=4; 神识=8 }
}

# Final coefficients: 根骨HP 12→9, 魂魄MP 5→7, 魂魄攻 5→6, 反应减半
$FACTOR = @{
    "凡人" = @{ HP=4; MP=0.5; 攻=1; 防=0.8; 反应=0.6; 移力=0.08; 神识=0.15 }
    "练气" = @{ HP=8; MP=2; 攻=3; 防=2; 反应=0.75; 移力=0.10; 神识=0.20 }
    "筑基" = @{ HP=9; MP=7; 攻=6; 防=4; 反应=1.5; 移力=0.12; 神识=0.25 }
}

$TECH_GROWTH = @{
    "极品" = @{ HP=65; MP=45; 肉攻=22; 神攻=22; 肉防=18; 神防=18; 反应=12 }
    "上品" = @{ HP=40; MP=25; 肉攻=12; 神攻=12; 肉防=10; 神防=10; 反应=6 }
    "中品" = @{ HP=20; MP=12; 肉攻=6; 神攻=6; 肉防=5; 神防=5; 反应=4 }
}

function TotalSubs($r, $s) { $t=0; foreach($x in $REALM_ORDER){if($x -eq $r){$t+=($s+1);break};$t+=$SUBLEVELS[$x]};return $t }

function BuildChar($inn, $realm, $sub, $tg, $w, $sp, $st) {
    $ts=TotalSubs $realm $sub; $tp=$ts*$TECH_INNATE[$tg]; $tw=$w.根骨+$w.魂魄+$w.神识+$w.资质+$w.气运
    $c=@{}; $ks=@("根骨","魂魄","神识","资质","气运"); $sa=0
    foreach($k in $ks){$c[$k]=$inn[$k]+[Math]::Round($tp*$w[$k]/$tw);$sa+=[Math]::Round($tp*$w[$k]/$tw)}
    $d=$tp-$sa; if($d -ne 0){$mk=$ks|Sort{$w[$_]}-Desc|Select -First 1;$c[$mk]+=$d}
    $b=$BASE[$realm];$f=$FACTOR[$realm];$g=$TECH_GROWTH[$tg];$m=$SPIRIT_MOD[$sp];$ri=[array]::IndexOf($REALM_ORDER,$realm)
    $c.HP =[Math]::Round(([Math]::Round($b.HP  +$c.根骨*$f.HP  *$w.根骨)+$ts*$g.HP)  *$m)
    $c.MP =[Math]::Round(([Math]::Round($b.MP  +$c.魂魄*$f.MP  *$w.魂魄)+$ts*$g.MP)  *$m)
    $c.肉攻=[Math]::Round(([Math]::Round($b.肉攻+$c.根骨*$f.攻  *$w.根骨)+$ts*$g.肉攻)*$m)
    $c.神攻=[Math]::Round(([Math]::Round($b.神攻+$c.魂魄*$f.攻  *$w.魂魄)+$ts*$g.神攻)*$m)
    $c.肉防=[Math]::Round(([Math]::Round($b.肉防+$c.根骨*$f.防  *$w.根骨)+$ts*$g.肉防)*$m)
    $c.神防=[Math]::Round(([Math]::Round($b.神防+$c.神识*$f.防  *$w.神识)+$ts*$g.神防)*$m)
    $c.反应=[Math]::Round(([Math]::Round($b.反应+$c.神识*$f.反应*$w.神识)+$ts*$g.反应)*$m)
    $c.移力=[Math]::Round([Math]::Round($b.移力+$c.气运*$f.移力*$w.气运)+$ri)
    $c.神识=[Math]::Round([Math]::Round($b.神识+$c.神识*$f.神识*$w.神识)+$ri)
    $c.生回=[Math]::Round($c.HP*[Math]::Max(0,[Math]::Min(6,1+$c.根骨*0.05))/100)
    $c.格挡=[Math]::Max(0,[Math]::Min(40,$c.根骨*0.3))
    $c.物抗=[Math]::Max(0,[Math]::Min(50,$c.根骨*0.4))
    $c.灵回=[Math]::Round($c.MP*[Math]::Max(0,[Math]::Min(5,0.5+$c.魂魄*0.05))/100)
    $c.魂抗=[Math]::Max(0,[Math]::Min(50,$c.魂魄*0.4))
    $c.暴伤=[Math]::Max(150,[Math]::Min(300,150+$c.魂魄*1))
    $c.暴率=[Math]::Max(0,[Math]::Min(40,$c.神识*0.25))
    $c.命中=[Math]::Max(0,[Math]::Min(50,$c.神识*0.3))
    $c.闪避=[Math]::Max(0,[Math]::Min(50,$c.气运*0.3))
    $c.Style=$st; return $c
}
function PD($a,$d,$r,$dir){$df=$a/($a+$d);$res=$r/100;$b=1;if($dir-eq1){$b=1.1}elseif($dir-eq2){$b=1.25};return[Math]::Max(0,[Math]::Round($a*$df*(1-$res)*$b))}
function SD($a,$d,$r,$dir){$df=$a/($a+$d);$res=$r/100;$b=1;if($dir-eq1){$b=1.1}elseif($dir-eq2){$b=1.25};return[Math]::Max(0,[Math]::Round($a*$df*(1-$res)*$b))}
function SO($ca,$cb){
    $ha=$ca.HP;$hb=$cb.HP;$cta=(Get-Random -Min 0 -Max 100);$ctb=(Get-Random -Min 0 -Max 100)
    $aa=$true;$ab=$true;$na=0;$nb=0;$da=0;$db=0;$rng=New-Object System.Random
    for($t=1;$t-le300;$t++){
        if($aa){$ha=[Math]::Min($ca.HP,$ha+$ca.生回);$cta+=$ca.反应}
        if($ab){$hb=[Math]::Min($cb.HP,$hb+$cb.生回);$ctb+=$cb.反应}
        $acta=($aa -and $cta -ge 100);$actb=($ab -and $ctb -ge 100)
        if($acta -and $actb){
            if($ca.反应 -ge $cb.反应){
                $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
                if($ca.Style-eq"physical"){$d=PD $ca.肉攻 $cb.肉防 $cb.物抗 $dir;if($dir-ne2-and$rng.NextDouble()*100-lt$cb.格挡){$d=[Math]::Round($d/2)}}
                else{$d=SD $ca.神攻 $cb.神防 $cb.魂抗 $dir}
                if($rng.NextDouble()*100-lt[Math]::Max(0,$cb.闪避-$ca.命中)){$d=0}
                if($d-gt0-and$rng.NextDouble()*100-lt$ca.暴率){$d=[Math]::Round($d*$ca.暴伤/100)}
                $hb-=$d;$na++;$da+=$d;$cta=0
                if($hb-le0){$hb=0;$ab=$false;break}
                if($ab -and $ctb -ge 100){
                    $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
                    if($cb.Style-eq"physical"){$d=PD $cb.肉攻 $ca.肉防 $ca.物抗 $dir;if($dir-ne2-and$rng.NextDouble()*100-lt$ca.格挡){$d=[Math]::Round($d/2)}}
                    else{$d=SD $cb.神攻 $ca.神防 $ca.魂抗 $dir}
                    if($rng.NextDouble()*100-lt[Math]::Max(0,$ca.闪避-$cb.命中)){$d=0}
                    if($d-gt0-and$rng.NextDouble()*100-lt$cb.暴率){$d=[Math]::Round($d*$cb.暴伤/100)}
                    $ha-=$d;$nb++;$db+=$d;$ctb=0
                    if($ha-le0){$ha=0;$aa=$false;break}
                }
            } else {
                $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
                if($cb.Style-eq"physical"){$d=PD $cb.肉攻 $ca.肉防 $ca.物抗 $dir;if($dir-ne2-and$rng.NextDouble()*100-lt$ca.格挡){$d=[Math]::Round($d/2)}}
                else{$d=SD $cb.神攻 $ca.神防 $ca.魂抗 $dir}
                if($rng.NextDouble()*100-lt[Math]::Max(0,$ca.闪避-$cb.命中)){$d=0}
                if($d-gt0-and$rng.NextDouble()*100-lt$cb.暴率){$d=[Math]::Round($d*$cb.暴伤/100)}
                $ha-=$d;$nb++;$db+=$d;$ctb=0
                if($ha-le0){$ha=0;$aa=$false;break}
                if($aa -and $cta -ge 100){
                    $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
                    if($ca.Style-eq"physical"){$d=PD $ca.肉攻 $cb.肉防 $cb.物抗 $dir;if($dir-ne2-and$rng.NextDouble()*100-lt$cb.格挡){$d=[Math]::Round($d/2)}}
                    else{$d=SD $ca.神攻 $cb.神防 $cb.魂抗 $dir}
                    if($rng.NextDouble()*100-lt[Math]::Max(0,$cb.闪避-$ca.命中)){$d=0}
                    if($d-gt0-and$rng.NextDouble()*100-lt$ca.暴率){$d=[Math]::Round($d*$ca.暴伤/100)}
                    $hb-=$d;$na++;$da+=$d;$cta=0
                    if($hb-le0){$hb=0;$ab=$false;break}
                }
            }
        } elseif($acta){
            $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
            if($ca.Style-eq"physical"){$d=PD $ca.肉攻 $cb.肉防 $cb.物抗 $dir;if($dir-ne2-and$rng.NextDouble()*100-lt$cb.格挡){$d=[Math]::Round($d/2)}}
            else{$d=SD $ca.神攻 $cb.神防 $cb.魂抗 $dir}
            if($rng.NextDouble()*100-lt[Math]::Max(0,$cb.闪避-$ca.命中)){$d=0}
            if($d-gt0-and$rng.NextDouble()*100-lt$ca.暴率){$d=[Math]::Round($d*$ca.暴伤/100)}
            $hb-=$d;$na++;$da+=$d;$cta=0
            if($hb-le0){$hb=0;$ab=$false;break}
        } elseif($actb){
            $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
            if($cb.Style-eq"physical"){$d=PD $cb.肉攻 $ca.肉防 $ca.物抗 $dir;if($dir-ne2-and$rng.NextDouble()*100-lt$ca.格挡){$d=[Math]::Round($d/2)}}
            else{$d=SD $cb.神攻 $ca.神防 $ca.魂抗 $dir}
            if($rng.NextDouble()*100-lt[Math]::Max(0,$ca.闪避-$cb.命中)){$d=0}
            if($d-gt0-and$rng.NextDouble()*100-lt$cb.暴率){$d=[Math]::Round($d*$cb.暴伤/100)}
            $ha-=$d;$nb++;$db+=$d;$ctb=0
            if($ha-le0){$ha=0;$aa=$false;break}
        }
        if(-not$aa -or -not$ab){break}
    }
    $win=if($aa -and -not$ab){"A"}elseif($ab -and -not$aa){"B"}else{"D"}
    return @{W=$win;T=$t;AA=$na;AB=$nb;DA=$da;DB=$db}
}

$wPhys=@{根骨=0.8;魂魄=0.6;神识=0.7;资质=0.6;气运=0.5}
$wSpir=@{根骨=0.6;魂魄=1.0;神识=0.7;资质=0.6;气运=0.5}
$wBal=@{根骨=0.7;魂魄=0.7;神识=0.7;资质=0.7;气运=0.5}

$builds=@(
    @{N="物理专精";I=@{根骨=38;魂魄=15;神识=22;资质=20;气运=20};S="physical";W=$wPhys}
    @{N="法术专精";I=@{根骨=20;魂魄=40;神识=22;资质=18;气运=15};S="spiritual";W=$wSpir}
    @{N="均衡物理";I=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20};S="physical";W=$wBal}
    @{N="均衡法术";I=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20};S="spiritual";W=$wBal}
    @{N="肉盾型";I=@{根骨=43;魂魄=12;神识=22;资质=18;气运=20};S="physical";W=$wPhys}
    @{N="刺客型";I=@{根骨=18;魂魄=20;神识=40;资质=19;气运=18};S="physical";W=@{根骨=0.5;魂魄=0.5;神识=1.0;资质=0.6;气运=0.7}}
    @{N="灵修型";I=@{根骨=15;魂魄=43;神识=18;资质=20;气运=19};S="spiritual";W=$wSpir}
)
$chars=@();foreach($b in $builds){$chars+=BuildChar $b.I "筑基" 2 "上品" $b.W "中品" $b.S}

Write-Host "========== R4 最终平衡 =========="
Write-Host "系数: 根骨HP9 魂魄MP7 魂魄攻6 防4 反应1.5`n"
foreach($i in 0..($chars.Count-1)){$c=$chars[$i];Write-Host "$($builds[$i].N): HP$($c.HP) 肉攻$($c.肉攻)/神攻$($c.神攻) 肉防$($c.肉防)/神防$($c.神防) 反应$($c.反应) 移$($c.移力)"}

$SIM=2000
Write-Host "`n=== 对阵矩阵 ($SIM次) ==="
$h="";foreach($b in $builds){$h+="$($b.N.PadRight(10))"};Write-Host "           $h"
for($i=0;$i-lt$chars.Count;$i++){
    $row="$($builds[$i].N.PadRight(10))";for($j=0;$j-lt$chars.Count;$j++){
        if($i-eq$j){$row+="   ---    ";continue}
        $wa=0;$wb=0;for($k=0;$k-lt$SIM;$k++){$r=SO $chars[$i] $chars[$j];if($r.W-eq"A"){$wa++}elseif($r.W-eq"B"){$wb++}}
        $p=[Math]::Round($wa/$SIM*100);$row+=" $($p.ToString().PadLeft(3))%   "
    };Write-Host $row
}

Write-Host "`n=== 关键对阵详情 ==="
$pairs=@(@{A=0;B=1;L="物理 vs 法术"},@{A=2;B=3;L="均衡 vs 均衡"},@{A=0;B=4;L="物理 vs 肉盾"},@{A=1;B=6;L="法术 vs 灵修"},@{A=5;B=1;L="刺客 vs 法术"})
foreach($p in $pairs){$a=$chars[$p.A];$b=$chars[$p.B]
    $dAB=if($a.Style-eq"physical"){PD $a.肉攻 $b.肉防 $b.物抗 0}else{SD $a.神攻 $b.神防 $b.魂抗 0}
    $dBA=if($b.Style-eq"physical"){PD $b.肉攻 $a.肉防 $a.物抗 0}else{SD $b.神攻 $a.神防 $a.魂抗 0}
    $wa=0;$wb=0;$tt=0;$ta=0;$tb=0
    for($k=0;$k-lt$SIM;$k++){$r=SO $a $b;if($r.W-eq"A"){$wa++}elseif($r.W-eq"B"){$wb++};$tt+=$r.T;$ta+=$r.AA;$tb+=$r.AB}
    $bMark=if($wa/$SIM -gt 0.3 -and $wa/$SIM -lt 0.7){" ** BALANCED **"}else{""}
    Write-Host "$($p.L): A$([Math]::Round($wa/$SIM*100,1))% B$([Math]::Round($wb/$SIM*100,1))% | dmg $dAB/$dBA | HP $($a.HP)/$($b.HP) | t=$([Math]::Round($tt/$SIM,1)) aA=$([Math]::Round($ta/$SIM,1)) aB=$([Math]::Round($tb/$SIM,1))$bMark"
}

Write-Host "`n=== 区间验证(筑基:HP400~2000 攻120~600 防100~450 反应50~200) ==="
foreach($i in 0..($chars.Count-1)){$c=$chars[$i];$ok=($c.HP-ge400-and$c.HP-le2000-and$c.反应-ge50-and$c.反应-le200);Write-Host "$($builds[$i].N): HP$($c.HP) 反应$($c.反应) -> $(if($ok){'OK'}else{'OUT'})"}
