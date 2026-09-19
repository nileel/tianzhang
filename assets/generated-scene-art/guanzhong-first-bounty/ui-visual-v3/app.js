import { createBattlefield } from './battlefield.js';
const $=s=>document.querySelector(s), $$=s=>[...document.querySelectorAll(s)];
const state={surface:'city',bounty:'available',action:null,moved:false,finished:false,resultKind:null,grid:false,rewardCount:0,enemyHp:839};
let world,toastTimer;
// Grid is a view mode, not an independent overlay. Keep its button in sync.
function setWorldMode(mode){state.grid=mode==='grid';$('#grid-toggle').setAttribute('aria-pressed',String(state.grid));world?.setMode(mode);}
function scale(){const s=$('#viewport').clientWidth/1920;$('#game-frame').style.transform=`scale(${s})`;}
new ResizeObserver(scale).observe($('#viewport'));scale();
function toast(text){clearTimeout(toastTimer);$('#toast').textContent=text;$('#toast').hidden=false;toastTimer=setTimeout(()=>$('#toast').hidden=true,3200);}
function renderBounty(){
 const map={available:['可接取','接取悬赏 →','一纸除害令，候人揭取'],accepted:['已接取','前往关中野外 →','黄土旧道 · 击败石甲兽'],complete:['目标已完成','领取奖励 →','目标已完成 · 劣质灵石 ×3'],claimed:['已领取','关闭','此地暂无未完成悬赏']};
 const a=map[state.bounty];$('#bounty-panel').dataset.state=state.bounty;$('#bounty-state').textContent=a[0];$('#bounty-action').textContent=a[1];$('#affair-status').textContent=a[2];$('#bounty-count').textContent=['complete','claimed'].includes(state.bounty)?'1 / 1':'0 / 1';
 $('#affair-title').textContent=state.bounty==='claimed'?'暂无待办悬赏':'石甲兽悬赏';$('#open-bounty').disabled=state.bounty==='claimed';
 const hasBounty=state.bounty==='accepted'||(state.bounty==='complete'&&state.resultKind==='victory');$('#goal-mark').textContent=hasBounty?'赏':'遇';$('#goal-copy').textContent=hasBounty?'悬赏 · 击败石甲兽':'击败石甲兽';
}
function openBounty(){if(state.bounty==='claimed'){toast('此地暂无可接取或待领奖的悬赏。');return;}$('#bounty-panel').hidden=false;$('#affair').hidden=true;renderBounty();}
function closeBounty(){$('#bounty-panel').hidden=true;$('#affair').hidden=false;}
function setSurface(surface){
 clearTimeout(toastTimer);$('#toast').hidden=true;
 state.surface=surface;$$('[data-surface]').forEach(b=>b.classList.toggle('active',b.dataset.surface===surface));
 $('#city-screen').hidden=surface!=='city';$('#battle-screen').hidden=!['battle','height'].includes(surface);
 $('#battle-screen').classList.toggle('height-mode',surface==='height');$('#height-tools').hidden=surface!=='height';$('#context-panel').hidden=true;$('#preview-panel').hidden=true;$('#battle-log').hidden=true;
 const notes={city:'交互设计原型 · 城景为构图占位 · 不写入正式存档',battle:'交互设计原型 · 资源/行动结果为示例 · 未连接Unity或BattleSim',height:'真实WebGL网格 · 37格三档高度 · 单位为尺度代理 · 尚未接入正式游戏',art:'内置imagegen独立环境概念 · 无UI · 不作为精确地图或实机证据'};$('#review-note').textContent=notes[surface];
 if(['battle','height'].includes(surface)){if(!world)world=createBattlefield($('#battle-canvas'),$('#world-labels'),onPick);world.reset();if(surface==='battle'){if(state.moved)world.moveHero();if(state.resultKind==='victory')world.defeated();}world.resize();setWorldMode(surface==='height'?'path':'none');$$('[data-view]').forEach(b=>b.classList.toggle('active',b.dataset.view==='default'));}
 $('#result-panel').hidden=surface!=='battle'||!state.finished;state.action=null;updateActions();renderBounty();
}
function onPick(item){
 if(state.surface==='height'&&item.kind==='tile'){$('#review-note').textContent=`三维地形选中 (${item.q}, ${item.r}) · ${item.height}层 · 高度与网格来自同一设计数据`;return;}
 if(item.kind==='enemy'&&state.action==='attack'){showAttack();return;}
 $('#context-panel').hidden=false;
 if(item.kind==='tile'){
   state.grid=false;$('#grid-toggle').setAttribute('aria-pressed','false');
   $('#context-kind').textContent='地形';$('#context-title').textContent=['黄土低地','黄土台地','岩土高台'][item.height];$('#context-copy').textContent=`当前为${item.height}层地面。等高处平接；高差处须沿石阶，断崖不可直接步行跨越。`;
   $('#context-extra').innerHTML='<div class="mini-row"><span>地表</span><span>干燥</span></div><div class="mini-row"><span>相关异象</span><span>无</span></div>';
 }else{
   const enemy=item.kind==='enemy';$('#context-kind').textContent=enemy?'敌方 · 练气':'当前行动者';$('#context-title').textContent=enemy?'石甲兽':'无名修士';$('#context-copy').textContent=enemy?'石甲厚重，伏于高台。靠近后可进行徒手攻击。':'当前未装配术法与神通。可以移动、徒手攻击、防御或待机。';
   $('#context-extra').innerHTML=`<div class="mini-row"><span>生命</span><span>${enemy?state.enemyHp+' / 839':'571 / 571'}</span></div><div class="mini-row"><span>灵力</span><span>${enemy?'108 / 108':'180 / 180'}</span></div>`;
 }
}
function updateActions(){$$('[data-action]').forEach(b=>{b.classList.toggle('selected',b.dataset.action===state.action);b.setAttribute('aria-pressed',String(b.dataset.action===state.action));b.disabled=state.finished;});}
function cancelPreview(){state.action=null;$('#preview-panel').hidden=true;$('#battle-hint').hidden=false;$('#battle-hint').textContent='选择行动，或点击地形与单位查看详情';setWorldMode('none');updateActions();}
function preview(title,kind,values,confirm,disabled=false){$('#preview-panel').hidden=false;$('#battle-hint').hidden=true;$('#preview-title').textContent=title;$('#preview-kind').textContent=kind;$('#preview-values').innerHTML=values.map(([v,l])=>`<div><strong>${v}</strong><small>${l}</small></div>`).join('');$('#confirm-preview').textContent=confirm;$('#confirm-preview').disabled=disabled;}
function showAttack(){setWorldMode('attack');if(!state.moved){preview('徒手 · 石甲兽','目标预览', [['1格','徒手射程'],['超出','当前距离']],'需要先靠近',true);}else{preview('徒手 · 石甲兽','行动预览 · 示例结果',[['126','预计伤害'],['90%','命中率'],['0','灵力消耗']],'确认攻击 →');}}
function chooseAction(action){
 if(state.finished||state.surface!=='battle')return;state.action=action;$('#context-panel').hidden=true;updateActions();
 if(action==='spell'||action==='power'){cancelPreview();toast(action==='spell'?'尚未装配术法。':'尚未装配神通。');return;}
 if(action==='move'){setWorldMode('path');preview('沿石阶登高','通路演示 · 不代表本回合可达范围',[['0 → 2','地面层次']],'演示沿路移动 →');}
 if(action==='attack')showAttack();
 if(action==='defend')preview('防御','行动预览',[['守势','本次行动']],'确认防御 →');
 if(action==='wait')preview('待机','行动预览',[['等待','让出本次行动']],'确认待机 →');
}
function result(kind){
 state.finished=true;state.resultKind=kind;cancelPreview();updateActions();$('#result-panel').hidden=false;$('#turn-status').textContent=kind==='error'?'未开始':'战斗已结束';
 const copy={victory:['战斗胜利',state.bounty==='accepted'?'石甲兽已被击败。返回关中城领取悬赏。':'石甲兽已被击败。可返回关中城。'],defeat:['战斗失败','未能击败石甲兽。返回关中城，悬赏进度保持不变。'],error:['无法开始遭遇','当前遭遇配置不完整。可返回关中城，任务进度不变。']};
 $('#result-title').textContent=copy[kind][0];$('#result-copy').textContent=copy[kind][1];
 if(kind==='victory'){if(state.bounty==='accepted')state.bounty='complete';world.defeated();$('#battle-progress').textContent='1 / 1';}renderBounty();
}
function resetBattle(){state.finished=false;state.resultKind=null;state.moved=false;state.action=null;state.enemyHp=839;$('#battle-progress').textContent='0 / 1';$('#result-panel').hidden=true;$('#hero-hp').textContent='571 / 571';$('#turn-status').textContent='你的行动';$('#log-content').textContent='遭遇石甲兽。等待行动。';world?.reset();setWorldMode('none');updateActions();renderBounty();}
$$('[data-surface]').forEach(b=>b.onclick=()=>setSurface(b.dataset.surface));
$('#open-bounty').onclick=openBounty;$('#service-bounty').onclick=openBounty;$('#close-bounty').onclick=closeBounty;
$('#bounty-action').onclick=()=>{
 if(state.bounty==='available'){state.bounty='accepted';renderBounty();toast('已接取：石甲兽悬赏');}
 else if(state.bounty==='accepted'){closeBounty();setSurface('battle');resetBattle();}
 else if(state.bounty==='complete'){state.bounty='claimed';state.rewardCount++;renderBounty();closeBounty();toast('已领取：劣质灵石 ×3');}
 else closeBounty();
};
$('#enter-wild').onclick=()=>{setSurface('battle');resetBattle();};
$('#battle-return').onclick=()=>{setSurface('city');renderBounty();};$('#result-return').onclick=()=>{setSurface('city');renderBounty();if(state.bounty==='complete')openBounty();};
$('#service-market').onclick=()=>toast('散卖法器丹药的摊贩尚未出摊。');$('#service-inn').onclick=()=>toast('门口木牌写着：客房已满。');$('#service-info').onclick=()=>toast('今日无事。');$('#service-old').onclick=()=>toast('旧水驿沿用既有入口，本轮不展开其界面。');$('#world-return').onclick=()=>toast('返回世界地图沿用既有入口，本原型不展开世界地图。');$('#save-demo').onclick=()=>toast('设计原型不写入正式存档。');$('#settings-demo').onclick=()=>toast('本轮聚焦城镇与战斗，菜单沿用既有入口。');
$$('[data-action]').forEach(b=>b.onclick=()=>chooseAction(b.dataset.action));$$('[data-unit]').forEach(b=>b.onclick=()=>onPick({kind:b.dataset.unit}));
$('#cancel-preview').onclick=cancelPreview;$('#close-context').onclick=()=>$('#context-panel').hidden=true;
$('#confirm-preview').onclick=()=>{
 const action=state.action;cancelPreview();
 if(action==='move'){world.moveHero();state.moved=true;$('#log-content').textContent='原型演示：沿石阶通路从低地抵达高台。未计算回合移动成本。';toast('已演示石阶通路；可查看徒手预览。');}
 if(action==='attack'){state.enemyHp=Math.max(0,state.enemyHp-126);$('#log-content').textContent='原型示例：徒手命中，演示伤害126。不是实战结算。';toast('示例反馈：徒手命中 · 126');onPick({kind:'enemy'});if(state.enemyHp===0){$('#context-panel').hidden=true;result('victory');}}
 if(action==='defend'||action==='wait'){$('#log-content').textContent=action==='defend'?'原型演示：已确认防御。':'原型演示：已确认待机。';toast(action==='defend'?'已确认防御（原型演示）':'已确认待机（原型演示）');}
};
$('#toggle-log').onclick=()=>$('#battle-log').hidden=!$('#battle-log').hidden;$('#grid-toggle').onclick=()=>{setWorldMode(state.grid?'none':'grid');};$('#camera-reset').onclick=()=>world.cameraView();
$$('[data-view]').forEach(b=>b.onclick=()=>{world.cameraView(b.dataset.view);$$('[data-view]').forEach(x=>x.classList.toggle('active',x===b));});$('#inspect-path').onclick=()=>setWorldMode('path');$('#inspect-cliff').onclick=()=>setWorldMode('cliff');
$('#demo-state').onchange=e=>{const v=e.target.value;if(['victory','defeat','error'].includes(v)){resetBattle();setSurface('battle');result(v);}else{state.bounty={default:'available',accepted:'accepted',complete:'complete',claimed:'claimed'}[v];setSurface('city');renderBounty();closeBounty();if(v!=='claimed')openBounty();}};
$('#reset-demo').onclick=()=>{state.bounty='available';state.rewardCount=0;resetBattle();setSurface('city');renderBounty();closeBounty();$('#demo-state').value='default';};
document.addEventListener('keydown',e=>{if(e.target.matches('input,select,textarea'))return;if(e.key==='Escape'){cancelPreview();closeBounty();$('#context-panel').hidden=true;}if(state.surface==='battle'&&/^[1-6]$/.test(e.key))chooseAction(['move','attack','spell','power','defend','wait'][Number(e.key)-1]);});
window.tianzhangPrototype={getState:()=>({...state}),getGeometry:()=>world?.evidence(),screenFor:(q,r)=>world?.screenFor(q,r)};
renderBounty();
