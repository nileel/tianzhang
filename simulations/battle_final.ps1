# Final verification: fixed syntax, 500 iterations, key matchups only
$REALM_ORDER="凡人","练气","筑基","金丹","元婴","化神","炼虚"
$SL=@{凡人=1;练气=9;筑基=4;金丹=4;元婴=4;化神=4;炼虚=4}
$TI=@{极品=5;上品=4;中品=3;下品=2;凡品=1}
$SM=@{凡品=0.70;下品=0.85;中品=1.00;上品=1.20;极品=1.50}
$BA=@{"凡人"=@{HP=30;MP=0;肉攻=5;神攻=5;肉防=3;神防=3;反应=5;移力=2;神识=3};"练气"=@{HP=100;MP=10;肉攻=25;神攻=25;肉防=20;神防=20;反应=15;移力=3;神识=5};"筑基"=@{HP=400;MP=100;肉攻=120;神攻=120;肉防=100;神防=100;反应=50;移力=4;神识=8}}
$FA=@{"凡人"=@{HP=4;MP=0.5;攻=1;防=0.8;反应=0.6;移力=0.08;神识=0.15};"练气"=@{HP=8;MP=2;攻=3;防=2;反应=0.75;移力=0.10;神识=0.20};"筑基"=@{HP=9;MP=7;攻=6;防=4;反应=1.5;移力=0.12;神识=0.25}}
$TG=@{"极品"=@{HP=65;MP=45;肉攻=22;神攻=22;肉防=18;神防=18;反应=12};"上品"=@{HP=40;MP=25;肉攻=12;神攻=12;肉防=10;神防=10;反应=6};"中品"=@{HP=20;MP=12;肉攻=6;神攻=6;肉防=5;神防=5;反应=4}}

function TS($r,$s){$t=0;foreach($x in $REALM_ORDER){if($x-eq$r){$t+=($s+1);break};$t+=$SL[$x]};$t}
function BC($inn,$r,$s,$tg,$w,$sp,$st){
    $ts=TS $r $s;$tp=$ts*$TI[$tg];$tw=$w.根骨+$w.魂魄+$w.神识+$w.资质+$w.气运
    $c=@{};$ks="根骨","魂魄","神识","资质","气运";$sa=0
    foreach($k in $ks){$c[$k]=$inn[$k]+[Math]::Round($tp*$w[$k]/$tw);$sa+=[Math]::Round($tp*$w[$k]/$tw)}
    $d=$tp-$sa;if($d-ne0){$mk=$ks|Sort{$w[$_]}-Desc|Select -First 1;$c[$mk]+=$d}
    $b=$BA[$r];$f=$FA[$r];$g=$TG[$tg];$m=$SM[$sp];$ri=[array]::IndexOf($REALM_ORDER,$r)
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
    $c.Style=$st;return $c
}

function BO($ca,$cb){
    $ha=$ca.HP;$hb=$cb.HP;$cta=Get-Random -Min 0 -Max 100;$ctb=Get-Random -Min 0 -Max 100
    $aa=$true;$ab=$true;$na=0;$nb=0;$da=0;$db=0;$rng=New-Object System.Random
    for($t=1;$t-le300;$t++){
        if($aa){$ha=[Math]::Min($ca.HP,$ha+$ca.生回);$cta+=$ca.反应}
        if($ab){$hb=[Math]::Min($cb.HP,$hb+$cb.生回);$ctb+=$cb.反应}
        $acta=($aa -and $cta -ge 100);$actb=($ab -and $ctb -ge 100)
        if($acta -and $actb){
            $order=if($ca.反应 -ge $cb.反应){@(($ca,$cb,$true),($cb,$ca,$false))}else{@(($cb,$ca,$false),($ca,$cb,$true))}
            foreach($o in $order){$att=$o[0];$def=$o[1];$isA=$o[2]
                if($isA){if(-not$aa){continue}}else{if(-not$ab){continue}}
                $aAlive=if($isA){$aa}else{$ab};$dAlive=if($isA){$ab}else{$aa}
                $ahp=if($isA){$ha}else{$hb};$dhp=if($isA){$hb}else{$ha}
                $aCt=if($isA){$cta}else{$ctb}
                if(-not$aAlive -or $aCt -lt 100 -or -not$dAlive){continue}
                $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
                if($att.Style-eq"physical"){
                    $dmg=[Math]::Max(0,[Math]::Round($att.肉攻*($att.肉攻/($att.肉攻+$def.肉防))*(1-$def.物抗/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
                    if($dir-ne2 -and $rng.NextDouble()*100 -lt $def.格挡){$dmg=[Math]::Round($dmg/2)}
                }else{
                    $dmg=[Math]::Max(0,[Math]::Round($att.神攻*($att.神攻/($att.神攻+$def.神防))*(1-$def.魂抗/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))
                }
                if($rng.NextDouble()*100 -lt [Math]::Max(0,$def.闪避-$att.命中)){$dmg=0}
                if($dmg -gt 0 -and $rng.NextDouble()*100 -lt $att.暴率){$dmg=[Math]::Round($dmg*$att.暴伤/100)}
                if($isA){$hb-=$dmg;$na++;$da+=$dmg;$cta=0;if($hb-le0){$hb=0;$ab=$false;break}}
                else{$ha-=$dmg;$nb++;$db+=$dmg;$ctb=0;if($ha-le0){$ha=0;$aa=$false;break}}
            }
        } elseif($acta){
            $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
            if($ca.Style-eq"physical"){$dmg=[Math]::Max(0,[Math]::Round($ca.肉攻*($ca.肉攻/($ca.肉攻+$cb.肉防))*(1-$cb.物抗/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})));if($dir-ne2 -and $rng.NextDouble()*100 -lt $cb.格挡){$dmg=[Math]::Round($dmg/2)}}
            else{$dmg=[Math]::Max(0,[Math]::Round($ca.神攻*($ca.神攻/($ca.神攻+$cb.神防))*(1-$cb.魂抗/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))}
            if($rng.NextDouble()*100 -lt [Math]::Max(0,$cb.闪避-$ca.命中)){$dmg=0}
            if($dmg -gt 0 -and $rng.NextDouble()*100 -lt $ca.暴率){$dmg=[Math]::Round($dmg*$ca.暴伤/100)}
            $hb-=$dmg;$na++;$da+=$dmg;$cta=0;if($hb-le0){$hb=0;$ab=$false;break}
        } elseif($actb){
            $dir=if($rng.NextDouble()-lt0.33){2}elseif($rng.NextDouble()-lt0.5){1}else{0}
            if($cb.Style-eq"physical"){$dmg=[Math]::Max(0,[Math]::Round($cb.肉攻*($cb.肉攻/($cb.肉攻+$ca.肉防))*(1-$ca.物抗/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})));if($dir-ne2 -and $rng.NextDouble()*100 -lt $ca.格挡){$dmg=[Math]::Round($dmg/2)}}
            else{$dmg=[Math]::Max(0,[Math]::Round($cb.神攻*($cb.神攻/($cb.神攻+$ca.神防))*(1-$ca.魂抗/100)*$(if($dir-eq1){1.1}elseif($dir-eq2){1.25}else{1})))}
            if($rng.NextDouble()*100 -lt [Math]::Max(0,$ca.闪避-$cb.命中)){$dmg=0}
            if($dmg -gt 0 -and $rng.NextDouble()*100 -lt $cb.暴率){$dmg=[Math]::Round($dmg*$cb.暴伤/100)}
            $ha-=$dmg;$nb++;$db+=$dmg;$ctb=0;if($ha-le0){$ha=0;$aa=$false;break}
        }
        if(-not$aa -or -not$ab){break}
    }
    $win=if($aa -and -not$ab){"A"}elseif($ab -and -not$aa){"B"}else{"D"}
    return @{W=$win;T=$t;AA=$na;AB=$nb;DA=$da;DB=$db}
}

# Build characters
$wPh=@{根骨=0.8;魂魄=0.6;神识=0.7;资质=0.6;气运=0.5}
$wSp=@{根骨=0.6;魂魄=1.0;神识=0.7;资质=0.6;气运=0.5}
$wBl=@{根骨=0.7;魂魄=0.7;神识=0.7;资质=0.7;气运=0.5}

$bt=@(
    @{N="物理专精";I=@{根骨=38;魂魄=15;神识=22;资质=20;气运=20};S="physical";W=$wPh}
    @{N="法术专精";I=@{根骨=20;魂魄=40;神识=22;资质=18;气运=15};S="spiritual";W=$wSp}
    @{N="均衡型";I=@{根骨=25;魂魄=25;神识=25;资质=20;气运=20};S="physical";W=$wBl}
    @{N="肉盾型";I=@{根骨=43;魂魄=12;神识=22;资质=18;气运=20};S="physical";W=$wPh}
    @{N="灵修型";I=@{根骨=15;魂魄=43;神识=18;资质=20;气运=19};S="spiritual";W=$wSp}
)
$chs=@();foreach($b in $bt){$chs+=BC $b.I "筑基" 2 "上品" $b.W "中品" $b.S}

Write-Host "=== 最终数值 (筑基后期/上品/中品) ==="
Write-Host "系数: 根骨HP9 魂魄MP7 魂魄攻6 防4 反应1.5`n"
foreach($i in 0..($chs.Count-1)){$c=$chs[$i];Write-Host "$($bt[$i].N): HP$($c.HP) 肉$($c.肉攻)/神$($c.神攻) 肉防$($c.肉防)/神防$($c.神防) 反应$($c.反应) 移$($c.移力) 格挡$($c.格挡)% 物抗$($c.物抗)% 魂抗$($c.魂抗)% 暴率$($c.暴率)% 暴伤$($c.暴伤)%"}

$SIM=500
Write-Host "`n=== 关键对阵 ($SIM 次) ==="
$pts=@(
    @{A=0;B=1;L="物理专精 vs 法术专精"}
    @{A=0;B=2;L="物理专精 vs 均衡型"}
    @{A=1;B=2;L="法术专精 vs 均衡型"}
    @{A=0;B=3;L="物理专精 vs 肉盾型"}
    @{A=1;B=4;L="法术专精 vs 灵修型"}
)
foreach($p in $pts){$a=$chs[$p.A];$b=$chs[$p.B]
    if($a.Style-eq"physical"){$dAB=[Math]::Max(0,[Math]::Round($a.肉攻*($a.肉攻/($a.肉攻+$b.肉防))*(1-$b.物抗/100)))}
    else{$dAB=[Math]::Max(0,[Math]::Round($a.神攻*($a.神攻/($a.神攻+$b.神防))*(1-$b.魂抗/100)))}
    if($b.Style-eq"physical"){$dBA=[Math]::Max(0,[Math]::Round($b.肉攻*($b.肉攻/($b.肉攻+$a.肉防))*(1-$a.物抗/100)))}
    else{$dBA=[Math]::Max(0,[Math]::Round($b.神攻*($b.神攻/($b.神攻+$a.神防))*(1-$a.魂抗/100)))}
    $wa=0;$wb=0;$tt=0;$ta=0;$tb=0
    for($k=0;$k-lt$SIM;$k++){$r=BO $a $b;if($r.W-eq"A"){$wa++}elseif($r.W-eq"B"){$wb++};$tt+=$r.T;$ta+=$r.AA;$tb+=$r.AB}
    $wrA=[Math]::Round($wa/$SIM*100,1);$wrB=[Math]::Round($wb/$SIM*100,1)
    $bMark="";if($wrA-gt30 -and $wrA-lt70){$bMark=" ** BALANCED **"}
    Write-Host "$($p.L): A $wrA% B $wrB% | dmg $dAB/$dBA | HP $($a.HP)/$($b.HP) | t=$([Math]::Round($tt/$SIM,1)) aA=$([Math]::Round($ta/$SIM,1)) aB=$([Math]::Round($tb/$SIM,1))$bMark"
}
