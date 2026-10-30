/* Static review navigation only. No runtime commands, save data or costs are executed. */
const params = new URLSearchParams(location.search);
let current = Math.max(0, sequences.findIndex(s => s.id === params.get('sequence')));
let frameIndex = Math.max(0, Math.min(Number(params.get('frame')) || 0, sequences[current].frames.length - 1));
const app = document.getElementById('app');
const selectSequence = document.getElementById('sequence');
const selectFrame = document.getElementById('frame');
const portrait = '../../../../../src/Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png';
const sourceRoot = '../../';
const escapeText = s => String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
selectSequence.innerHTML = sequences.map((s,i)=>'<option value="'+i+'">'+s.name+'</option>').join('');
function render() {
 const s = sequences[current], f = s.frames[frameIndex], col = frameIndex % 2, row = Math.floor(frameIndex / 2);
 selectSequence.value = current;
 selectFrame.innerHTML = s.frames.map((v,i)=>'<option value="'+i+'">'+(i+1)+' / '+v[0]+'</option>').join('');
 selectFrame.value = frameIndex;
 document.getElementById('resolution').textContent = innerWidth+' × '+innerHeight;
 document.getElementById('previous').disabled = frameIndex === 0;
 document.getElementById('next').disabled = frameIndex === s.frames.length-1;
 history.replaceState(null,'','?sequence='+s.id+'&frame='+frameIndex);
 app.innerHTML = `<header class="topbar"><div class="brand">天章</div><div class="breadcrumb">关中阶地 / ${s.name}</div><div class="row"><a href="../index.html?state=battle">V1 操作方向</a><a href="README.md">V2 说明</a></div></header>
 <section class="panel player"><div class="row"><img class="avatar" src="${portrait}" alt="玩家头像"><div><h3>沈行舟</h3><span class="pill">金丹试玩 · ${s.build}</span></div></div><div class="stats"><span>当前构筑</span><strong class="gold">${s.build}</strong><span>画面状态</span><strong>${frameIndex+1} / ${s.frames.length}</strong></div><p class="cast">${s.cast}</p></section>
 <aside class="timeline"><span>独立构筑</span><div class="turn current"><img src="${portrait}" alt="当前角色">本人能力</div><div class="turn muted">其他道路<br>归独立角色</div></aside>
 <div class="v2-stage"><div class="scene-crop"><img id="scene-art" src="art/${s.id}.png" alt="${s.name}：${f[0]}，视觉方案非实机" style="width:200%;height:${s.rows*100}%;left:${-col*100}%;top:${-row*100}%"></div></div>
 <div class="scene-note">固定局部镜头分镜 · 原地图辨识参考 · 不是几何／落点运行证明</div>
 <aside class="panel objective"><div class="eyebrow">${f[0]} · ${frameIndex+1}/${s.frames.length}</div><h3>${f[1]}</h3><div class="steps"><span class="active">${f[2]}</span></div></aside>
 <aside class="panel context"><div class="eyebrow">当前规则边界</div><p>${f[3]}</p><div class="rule"></div><div class="eyebrow">已有表现 / 建议补充</div><p>${s.existing}</p><div class="links"><button data-evidence="1">原始实机 ①</button><button data-evidence="2">原始实机 ②</button><a href="art/${s.id}.png" target="_blank">完整分镜图</a></div></aside>
 <footer class="dock"><div><div class="actions-header"><strong>行动</strong><span>常用</span><span class="gold">金丹能力</span><span class="muted">沿用 V1 布局 · 此页仅审阅</span></div><div class="actions">${s.actions.map((a,i)=>'<button aria-disabled="true" class="action '+(i===0?'selected':'')+'"><b>'+a+'</b><span>当前合法构筑</span></button>').join('')}</div></div><div class="quote"><div><strong>${s.price}</strong><div class="price">${f[0]} · ${f[1]}</div><small class="muted">顶部切换分镜；游戏确认方向保持 V1。</small></div><div class="row"><button aria-disabled="true" class="subtle">取消</button><button aria-disabled="true" class="primary">确认施行</button></div></div></footer>`;
 const art = document.getElementById('scene-art');
 art.onload = () => {
   const ratio = (art.naturalWidth / 2) / (art.naturalHeight / s.rows);
   const crop=art.parentElement,stage=crop.parentElement;
   crop.style.aspectRatio=ratio;
   crop.style.width=Math.min(stage.clientWidth,stage.clientHeight*ratio)+'px';
 };
 if (art.complete) art.onload();
 app.querySelectorAll('[data-evidence]').forEach(b=>b.addEventListener('click',()=>showEvidence(b.dataset.evidence==='1'?s.evidence:s.evidence2)));
}
function showEvidence(file) {
 const url=sourceRoot+file;
 document.getElementById('evidence-image').src=url;
 document.getElementById('evidence-link').href=url;
 document.getElementById('evidence').showModal();
}
selectSequence.onchange=()=>{current=Number(selectSequence.value);frameIndex=0;render();};
selectFrame.onchange=()=>{frameIndex=Number(selectFrame.value);render();};
document.getElementById('previous').onclick=()=>{if(frameIndex>0){frameIndex--;render();}};
document.getElementById('next').onclick=()=>{if(frameIndex<sequences[current].frames.length-1){frameIndex++;render();}};
document.getElementById('close-evidence').onclick=()=>document.getElementById('evidence').close();
addEventListener('keydown',e=>{
 if(document.getElementById('evidence').open||['SELECT','INPUT'].includes(e.target.tagName))return;
 if(e.key==='ArrowLeft')document.getElementById('previous').click();
 if(e.key==='ArrowRight')document.getElementById('next').click();
});
addEventListener('resize',render);
render();
