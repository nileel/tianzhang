/* Static HUD review only. No game state, save, command or timing implementation. */
const root=document.querySelector('#app');
const query=new URLSearchParams(location.search);
let group=Math.max(0,sequences.findIndex(s=>s.id===query.get('sequence')));
let frame=Math.min(Math.max(0,Number(query.get('frame'))||0),sequences[group].frames.length-1);
let selected=null,detail=null,cancelled=false;
const artSource=id=>document.getElementById('art-'+id).src;
const portrait=artSource('portrait');
const icons={
 move:'M5 22L14 5l3 8 10 5-17 9 1-8M15 5l-4 14',
 attack:'M9 23l-3-9 3-2 3 5-2-10 3-1 3 10-1-12 3 0 2 12 1-9 3 1 0 12-4 7-9 0z',
 star:'M5 27L20 10M5 20L15 10M12 28L24 16M23 3l2 6 6 2-6 2-2 6-2-6-6-2 6-2z',
 stone:'M4 24l5-11 8-6 9 6 3 11-13 5zM9 13l7 16 2-17 8 1M4 8l6-3M23 3l5 3',
 gather:'M5 14c0-9 23-9 23 0s-23 9-23 0M7 20c3 7 18 7 20 0M10 25c4 5 11 5 15 0',
 fire:'M18 3c6 9-3 10 5 16 3-4 4-5 3-8 11 13-3 21-12 17C0 22 8 14 11 9c-1 9 6 10 7-6zM16 20c-5 6-2 9 2 8',
 sun:'M16 9a7 7 0 1 0 0 14 7 7 0 0 0 0-14M16 2v4M16 26v4M2 16h4M26 16h4M6 6l3 3M23 23l3 3M6 26l3-3M23 9l3-3',
 scale:'M4 24L27 7M8 21l-3-4M13 17l-3-4M18 13l-3-4M23 9l-3-4M5 27l23-17',
 flight:'M3 20c10-1 13-12 25-15-4 10-8 15-20 19M9 19l8 2M15 14l8 1M4 27l10-4',
 guard:'M5 6l11-3 11 3v10c0 8-11 13-11 13S5 24 5 16zM16 7v17M9 12h14',
 more:'M5 9h22M5 16h22M5 23h22M9 6v20',
 close:'M6 8h20M12 5h8M9 8l1 19h12l1-19M14 12v10M18 12v10'
};
const actions={move:['移动','路径报价','M'],attack:['普攻','250 AP','1'],star:['飞星指','50 AP','2'],stone:['贯石击','50 AP','3'],gather:['聚灵术','25 AP','4'],fire:['长明','500 AP','5'],sun:['阳照','250 AP','6'],scale:['定尺','250 AP','5'],flight:['御风','250 AP','7'],guard:['防御','50 AP','G'],close:['收势','25 AP','X'],more:['行囊','换术 / 装备','I']};
const costs={move:'按合法路径报价',attack:'250 AP · 0 MP',star:'50 AP · 10 MP',stone:'50 AP · 12 MP',gather:'25 AP · 0 MP',fire:'500 AP · 1000 MP',sun:'250 AP · 400 MP',scale:'250 AP · 400 MP',flight:'250 AP · 200 MP',guard:'50 AP',close:'25 AP · 0 MP',more:'打开后查看操作与报价'};
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function skillState(id,s,f){
 const cd=(s.id==='fire'&&id==='fire'&&frame===2)||(s.id==='sight'&&id==='sun'&&frame>=2&&frame<=5);
 const noInstance=id==='close'&&(s.id==='fire'?(frame<2||frame>4):(frame<2));
 let domainReason='';
 if(id==='fire'&&frame>=2)domainReason=frame<5?'主域已展开，可选择收势':'长明之根已毁，不能再次展开';
 if(id==='sun'&&frame>=2)domainReason='阳照已展开，可选择收势';
 return {cd,reason:f.enemy?'对手行动中':f.conflict?'战斗暂挂':f.disabled&&id!=='more'?'行动力不足':noInstance?'无可关闭主实例':cd?'冷却 1 tick':domainReason};
}
function go(g,f){group=g;frame=Math.max(0,Math.min(f,sequences[g].frames.length-1));selected=null;detail=null;cancelled=false;history.replaceState(null,'','?sequence='+sequences[g].id+'&frame='+frame);render();}
function next(){go(group,frame+1);}
function icon(id){return `<svg viewBox="0 0 32 32" aria-hidden="true"><path d="${icons[id]}"></path></svg>`;}
function drawMarkers(f,isSight){
 if(!f.target||cancelled||f.conflict||(selected&&selected!==f.select))return '';
 const xy=isSight?[670,245]:f.target==='attack'?[487,268]:[557,254];
 let drawing='';
 if(f.target==='move')drawing='<path d="M422 313 L389 317 L348 322" stroke="#f4ffff" stroke-width="3" fill="none" stroke-dasharray="7 5"/><polygon points="348,302 379,313 379,330 348,341 317,330 317,313" fill="#8ae1da22" stroke="#e7fffa" stroke-width="2"/>';
 else if(f.target==='center')drawing='<path d="M665 257v-25h28v25M675 240h8" fill="none" stroke="#f3e4ae" stroke-width="2"/>';
 else if(f.target==='hit')drawing='<path d="M458 287l13-10" fill="none" stroke="#fff6da" stroke-width="3"/>';
 else drawing=`<path d="M${xy[0]-34} ${xy[1]-18}l-12 6v12l12 7M${xy[0]+34} ${xy[1]-18}l12 6v12l-12 7" fill="none" stroke="#e6fffa" stroke-width="2.5"/><path d="M${isSight?285:450} ${isSight?228:304}L${xy[0]-45} ${xy[1]}" fill="none" stroke="#f0f7e8" stroke-width="1.5" stroke-dasharray="3 7"/>`;
 return `<svg class="targeting" viewBox="0 0 1000 563" preserveAspectRatio="none" aria-label="方案中的选格与路径标记，非实际几何">${drawing}</svg>`;
}
function render(){
 const s=sequences[group],f=s.frames[frame],isSight=s.id==='sight',normal=s.id==='normal';
 const sel=cancelled?null:(selected||f.select),maxAp=normal?100:1000,maxMp=normal?180:6000;
 const skills=normal?['move','attack','star','stone','gather','guard','more']:isSight?['move','attack','star','stone','scale','sun','flight','guard','close','more']:['move','attack','star','fire','guard','close','more'];
 const pct=Number.isFinite(f.ap)?Math.max(0,f.ap/maxAp*100):60;
 const mpPct=Number.isFinite(f.mp)?f.mp/maxMp*100:65;
 root.className=normal?'normal':'';
 root.innerHTML=`<div class="world ${isSight?'v2':''}" style="background-image:url('${artSource(isSight?'visibility':f.art)}');${isSight?'background-position:'+(f.v2%2*100)+'% '+(Math.floor(f.v2/2)*50)+'%;':''}"></div><div class="vignette"></div>
 <div class="proof">D12.0 / V3 · 方案图，非 Unity 实机 · 数值为分镜示例</div>
 <nav class="review" aria-label="方案审阅工具"><select id="sequence" aria-label="独立构筑分镜">${sequences.map((x,i)=>`<option value="${i}" ${i===group?'selected':''}>${x.name}</option>`).join('')}</select><button id="prev" aria-label="上一帧">←</button><select id="frame" aria-label="分镜状态">${s.frames.map((x,i)=>`<option value="${i}" ${i===frame?'selected':''}>${i+1}. ${x.title}</option>`).join('')}</select><button id="next" aria-label="下一帧">→</button><a href="README.md">说明</a></nav>
 <section class="identity" aria-label="同一行动者与共享资源"><img class="portrait" src="${portrait}" alt="沈行舟"><div class="vitals"><div class="name">沈行舟<span class="build">${s.build}</span></div><div class="resource hp"><label>生命</label><span class="meter"><i style="width:100%"></i></span><b>${normal?'571 / 571':'3474 / 3474'}</b></div><div class="resource mp"><label>灵力</label><span class="meter"><i style="width:${mpPct}%"></i></span><b>${f.mp}${Number.isFinite(f.mp)?' / '+maxMp:''}</b></div><div class="resource ap"><label>AP</label><span class="meter"><i style="width:${pct}%"></i></span><b>${f.ap}${Number.isFinite(f.ap)?' / '+maxAp:''}</b></div></div></section>
 <div class="statuses" aria-label="持续状态不随所选行动切换">${f.status.filter(Boolean).map((x,i)=>`<div class="status"><span class="sigil">${['◈','◇','◷'][i]}</span>${esc(x)}</div>`).join('')}</div>
 <div class="turns" aria-label="公开行动次序"><span class="turn-token current">${f.enemy?'敌':'你'}</span><span>›</span><span class="turn-token">${f.enemy?'你':'敌'}</span></div>
 <aside class="objective">关中阶地 · 演武<small>${s.objective}</small><div class="tools"><button data-detail="objective" aria-label="挑战详情">目</button><button data-detail="log" aria-label="战报">录</button><button data-detail="settings" aria-label="菜单与输入说明">☰</button></div></aside>
 ${f.target==='hit'?'<div class="toast">普通攻击已提交 · 仍可继续行动</div>':''}
 ${drawMarkers(f,isSight)}
 ${!isSight&&['deployed','maintained'].includes(f.art)?'<div class="world-label support-label">长明之根 ◇ 600 / 600</div>':''}
 ${isSight?'<div class="world-label public-label">日坛 · '+(frame>1?'阳照生效':'未展开')+'</div>':''}
 ${(!selected||selected===f.select)&&!cancelled&&['spell','attack','sight-target'].includes(f.target)?`<div class="world-label target-label" style="${isSight?'left:69%;top:33%':f.target==='attack'?'left:47%;top:28%':''}">${f.target==='attack'?'徒手 · 相邻敌手':'飞星指 · 选定地格'}</div>`:''}
 ${!f.conflict&&!cancelled?`<div class="quote ${f.disabled?'disabled':''}"><span class="choice">${sel?actions[sel][0]:f.id==='07-end'?'结束行动':'行动反馈'}</span><div><div class="cost">${selected&&selected!==f.select?(normal&&selected==='attack'?'40 AP · 0 MP':costs[selected]):f.quote}</div><div class="balance">${selected&&selected!==f.select?'方案交互示意 · 确认前取真实报价':f.after}</div></div><button class="confirm" id="confirm" aria-disabled="${!!f.disabled}">${f.disabled?'不可施放':selected&&selected!==f.select?'查看说明':f.next}</button><button class="cancel" id="cancel" aria-label="取消本次选择">Esc</button></div>`:''}
 <div class="hint">${cancelled?'已取消选择 · 持续状态保持不变':f.hint}</div>
 <footer class="dock"><div class="actions" aria-label="同一行动栏，不分普通和金丹模式">${skills.map(id=>{const {cd,reason}=skillState(id,s,f);return `<button class="skill ${sel===id?'selected':''} ${['fire','sun','scale','flight','close'].includes(id)?'owned':''} ${reason?'unavailable':''}" data-skill="${id}" data-reason="${reason}" aria-label="${actions[id][0]} ${reason}" aria-disabled="${!!reason}"><span class="iconframe">${icon(id)}<span class="key">${actions[id][2]}</span>${cd?'<span class="cd">1 tick</span>':''}</span><span class="label">${actions[id][0]}</span><span class="sub">${id==='attack'&&normal?'40 AP':id==='fire'&&frame>=2?(frame<5?'领域生效':'余火尚存'):id==='close'&&reason==='无可关闭主实例'?'无主实例':actions[id][1]}</span></button>`;}).join('')}</div><button class="end" id="end" aria-disabled="${!!f.conflict||!!f.enemy}">结束行动<small>主动结束 · Enter</small></button></footer>
 <div class="caption"><b>${String(frame+1).padStart(2,'0')} / ${s.frames.length}</b>　${esc(f.title)}　<button data-detail="story">分镜注释</button></div><div class="stepper">${innerWidth} × ${innerHeight} · <button data-detail="source">参考来源</button></div>
 ${f.conflict?renderConflict(f):''}`;
 if(detail)renderDetail(detail);
 if(f.conflict)document.querySelector('.conflict button')?.focus({preventScroll:true});
 document.querySelector('#sequence').onchange=e=>go(Number(e.target.value),0);
 document.querySelector('#frame').onchange=e=>go(group,Number(e.target.value));
 document.querySelector('#prev').onclick=()=>go(group,frame-1);
 document.querySelector('#next').onclick=next;
}
function renderConflict(f){return `<section class="conflict" role="dialog" aria-modal="true" aria-label="本次显隐冲突"><small>战斗暂挂 · 不推进维护或冷却</small><h2>昭 明　与　藏 真</h2><div class="clash"><span>我方可用<br><strong>${f.conflict==='wait'?'200':'150'} MP</strong></span><span>本次观察</span><span>对方可用<br><strong class="moon">${f.conflict==='wait'?'200':'150'} MP</strong></span></div><div class="window">${f.conflict==='wait'?'按住 Space 维持':'保持 Space　　按 A'}</div><div class="progress"><i style="width:0%"></i></div><p>首按即付，随后每秒各付 50 MP · 已付 ${f.conflict==='wait'?0:50} MP</p><footer><button id="clash-next">${f.conflict==='wait'?'查看维持窗口':'查看阳胜结果'}</button><button id="clash-yin">查看阴胜分支</button><button id="skip-paid">付费跳过</button><button data-detail="skip">改键说明</button></footer></section>`;}
function renderDetail(type){
 const s=sequences[group],f=s.frames[frame],id=selected||f.select;
 const descriptions={
 story:[f.title,f.note],
 objective:['挑战与构筑',s.build+'。'+s.objective+'。普通与金丹开局只改变初始角色配置，不是两种战斗模式。'],
 log:['战报 · 按需展开','这里承载行动者、扣费、时间与失效原因；不在主 HUD 常驻内部来源 ID、坐标或大段规则。本浏览器不执行战斗。'],
 settings:['键鼠与导航','数字键选择技能；M 移动；G 防御；X 收势；I 行囊。左键锁定候选，确认按钮提交；右键／Esc 取消一层。Tab 顺序遍历；左右箭头切换审阅帧。建议键位均可改绑，尚未接入 Unity。'],
 source:['参考与证据','火域底图由内置 imagegen 参考 R4 真实阶地及 V2 重绘；显隐沿用 V2 imagegen 分镜。新 HUD 为 HTML/CSS 排版。全部是方案，几何、角色位置与数值不是 Unity 实测。'],
 skip:['同一付费冲突规则','跳过／本场自动跳过沿既有付费规则，不免费判胜。例：200 对 200，双方各付 200 后中性，恢复正常观察基础值；本见证为可见。沿用维持键与交替键改绑，焦点丢失暂停输入，不把失焦判作松开落败。'],
 skill:[id?actions[id][0]:'行动',id==='fire'?'展开 500 AP／1000 MP（含余火预付 200）；后续本人结束维护 200 MP，最多 3 个主周期。最多 3 格火；主规则结束后合法余火最多 2 个本人结束周期。':id==='star'?'飞星指：首用 50 AP／10 MP，射程 1–5，地面出手与地面目标，垂直触及 2；空中先降落。费用、CT 与 CD 以真实当次报价为准。':id==='close'?'收势展开本人现存实例的轻量列表，选择实例后报价 25 AP，不退款。火域关闭仍可能留下合法余火；没有可关闭主实例时禁用，不能删掉余火。':id==='more'?'行囊内放换术、装备与道具；显示已知／已装配及实际费用。换术与战中切装仍遵守原限制，不免费换构筑。':(id?costs[id]:'')+'。只有当前角色持有、装配且合法的能力可提交；普通与金丹共用预览确认。'],
 disabled:['暂不可用',f.disabled?'行动力不足：当前 0 AP。可查看详情、镜头或手动结束。':document.querySelector('[data-skill="'+id+'"]')?.dataset.reason+'。仍可查看详情，不提交命令。']};
 const text=descriptions[type]||descriptions.skill;
 document.querySelector('.detail')?.remove();
 root.insertAdjacentHTML('beforeend',`<aside class="detail" role="dialog" aria-label="按需详情"><button id="close-detail">关闭</button><h3>${esc(text[0])}</h3><p>${esc(text[1])}</p></aside>`);
}
root.addEventListener('click',e=>{
 const b=e.target.closest('button');if(!b)return;
 if(b.dataset.detail){detail=b.dataset.detail;renderDetail(detail);return;}
 if(b.id==='close-detail'){detail=null;document.querySelector('.detail')?.remove();return;}
 if(b.dataset.skill){if(b.getAttribute('aria-disabled')==='true'){detail='disabled';renderDetail(detail);document.querySelector('.detail p').textContent=b.dataset.reason+'。当前操作不会扣费。';return;}selected=b.dataset.skill;cancelled=false;render();return;}
 if(b.id==='cancel'){cancelled=true;selected=null;detail=null;render();}
 if(b.id==='confirm'&&b.getAttribute('aria-disabled')!=='true'){if(selected&&selected!==sequences[group].frames[frame].select){detail='skill';renderDetail(detail);}else if(sequences[group].frames[frame].next==='收起预览'){cancelled=true;render();}else if(sequences[group].frames[frame].next==='重看本段'){go(group,0);}else next();}
 if(b.id==='end'&&b.getAttribute('aria-disabled')!=='true'){if(group===0&&(frame===6||frame===8)){go(0,7);}else{detail='story';renderDetail(detail);document.querySelector('.detail h3').textContent='结束行动';document.querySelector('.detail p').textContent='结束后剩余 AP 放弃使用，按本人结束时点结算维护或余火。长明分镜第 7–8 帧可查看连续结果。';}}
 if(b.id==='skip-paid')go(1,7);if(b.id==='clash-next')next();if(b.id==='clash-yin')go(1,5);
});
window.addEventListener('keydown',e=>{
 const clash=document.querySelector('.conflict');
 if(e.key==='Tab'&&clash){const list=[...document.querySelectorAll('.conflict button,.detail button')];const i=list.indexOf(document.activeElement);e.preventDefault();list[(i+(e.shiftKey?-1:1)+list.length)%list.length].focus();return;}
 if(['SELECT','INPUT'].includes(document.activeElement.tagName))return;
 if(e.key==='ArrowRight'){e.preventDefault();next();}else if(e.key==='ArrowLeft'){e.preventDefault();go(group,frame-1);}else if(e.key==='Escape'){e.preventDefault();if(detail){detail=null;document.querySelector('.detail')?.remove();}else if(!clash){cancelled=true;selected=null;render();}}
 else if(!clash&&e.key==='Enter'&&document.activeElement===document.body){document.querySelector('#end').click();}
 else if(!clash){const b=[...document.querySelectorAll('.skill')].find(x=>x.querySelector('.key').textContent.toLowerCase()===e.key.toLowerCase());if(b){e.preventDefault();b.click();b.focus();}}
});
window.addEventListener('contextmenu',e=>{if(document.querySelector('.conflict'))return;e.preventDefault();cancelled=true;selected=null;detail=null;render();});
window.addEventListener('resize',render);
render();
