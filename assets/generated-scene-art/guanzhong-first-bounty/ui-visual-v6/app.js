import { applyButtonSkin, panelContent } from './ui-kit/templates.js';
import { createBattlefield } from '../ui-visual-v5/battlefield.js';

// UI fixture projection only. No CTB, damage, hit, movement or visibility rule is implemented here.
const fixture = await fetch('../ui-visual-v5/review-fixtures.json').then(r => { if (!r.ok) throw new Error('Missing UI fixtures'); return r.json(); });
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const icon = name => `<svg class="ui-icon" aria-hidden="true"><use href="#icon-${name}"/></svg>`;
const portrait = '<span class="portrait"><img src="../../../generated-character-art/dialogue-transparent/formal-player-default-male-v1.png" alt="无名修士头像"></span>';
let units, density = 8, selected = null, action = null, phase = 'player', actionUsed = false, moveUsed = false, nextState, itemCount = 3, grid = false, side = false;
let detailOpen = false, noticeTimer;
const field = createBattlefield($('#battle-canvas'), $('#world-labels'), pick, fixture.units);
const unit = id => units.find(u => u.id === id);
const visibleUnits = () => units.slice(0, density);
const activeOrder = () => (phase === 'player' ? fixture.order : nextState.order).filter(id => visibleUnits().some(u => u.id === id));
const badge = u => `<span class="unit-badge">${u.tag}</span>`;
const meter = (name, val, max, kind) => `<span class="town-meter-label"><span>${name}</span><b>${val} / ${max}</b></span><span class="meter ${kind}"><i style="width:${val / max * 100}%"></i></span>`;

function note(message) { $('#review-note').textContent = message; }
function notify(message) {
  clearTimeout(noticeTimer); $('#battle-notice').textContent = message; $('#battle-notice').hidden = false;
  noticeTimer = setTimeout(() => { $('#battle-notice').hidden = true; }, 6500);
}
function log(message) { const li = document.createElement('li'); li.textContent = message; $('#log-entries').append(li); }
function isUnavailable(a) {
  if (phase !== 'player') return '当前不是你的行动时机';
  if (a.id === 'move' && moveUsed) return '本次移动样例已完成；剩余移动仅作显示';
  if (a.id !== 'move' && actionUsed) return '本次行动权已用尽；仍可检查战场或结束行动';
  if (a.disabled) return a.id === 'power' ? `灵力不足：需要 140，当前 ${unit('p').mp}` : a.disabled;
  return '';
}
function targetUnavailable(a) {
  return a.target === 'enemy' && !a.eligibleTargetIds.includes(selected) ? a.ineligibleReason : '';
}
function axisButton(u, current) {
  const glyph = u.id === 'p' ? portrait : `${icon(u.side === 'enemy' ? 'beast' : 'person')}<span class="unit-id">${u.tag.slice(1)}</span>`;
  return `<button class="axis-unit ${u.side}${selected === u.id ? ' selected' : ''}" data-unit="${u.id}" aria-label="行动轴：${u.name}" aria-description="${current ? '当前行动者' : '接下来行动'}，CT ${u.ct}" aria-pressed="${selected === u.id}">${glyph}</button>`;
}
function renderAxis() {
  const focusedUnit = document.activeElement?.closest('.initiative [data-unit]')?.dataset.unit;
  const oldScroll = $('#order-list').scrollTop;
  const current = phase === 'player' ? 'p' : 'e1';
  $('#axis-peek').hidden = true;
  $('#current-actor').innerHTML = axisButton(unit(current), true);
  const order = activeOrder().filter(id => id !== current);
  const nextRank = phase === 'player' ? action?.next || (actionUsed ? nextState.rank : 0) : 0;
  field.setCurrent(current);
  $('#order-list').innerHTML = order.map((id, i) => {
    const forecast = nextRank === i + 1 ? '<div class="forecast-slot" aria-label="自身下次行动位置" title="自身下次行动位置">◇ 我</div>' : '';
    return `<li>${forecast}${axisButton(unit(id), false)}</li>`;
  }).join('');
  $('#order-list').scrollTop = oldScroll;
  $('#unit-count').textContent = `${density}人`;
  $('#toggle-density').textContent = density === 8 ? '12人样例' : '8人样例';
  $$('.initiative [data-unit]').forEach(b => {
    const u = unit(b.dataset.unit);
    b.onclick = () => pick({kind:'unit',id:u.id});
    b.onmouseenter = b.onfocus = () => { $('#axis-peek').textContent = `${u.name} · CT ${u.ct}${u.id === current ? ' · 当前行动' : ''}`; $('#axis-peek').hidden = false; };
    b.onmouseleave = b.onblur = () => { $('#axis-peek').hidden = true; };
  });
  if (focusedUnit) $('.initiative [data-unit="' + focusedUnit + '"]')?.focus({preventScroll:true});
}
function renderDetail() {
  const panel = $('#unit-detail'); panel.hidden = !detailOpen || !selected;
  if (panel.hidden) return;
  const u = unit(selected);
  panel.innerHTML = panelContent({header:`<button class="detail-close" aria-label="关闭目标详情">×</button><small>${u.side === 'enemy' ? '敌方' : '己方'} · 选中目标</small><div class="detail-identity ${u.side}">${badge(u)}<div><h2>${u.name}</h2><p>${u.realm}</p></div></div>${meter('生命', u.hp, u.maxHp, 'health')}${meter('灵力', u.mp, u.maxMp, 'spirit')}`,body:`<h3>当前状态</h3><div class="status-chips">${u.statuses.map((s,i) => `<button class="status-chip ${s.type}" data-status="${i}" title="${s.description}">${s.type === 'buff' ? '△' : '▽'} ${s.name}</button>`).join('')}</div><dl class="stat-list">${u.stats.map(([key,value]) => `<dt>${key}</dt><dd class="${value === '??' ? 'unknown' : ''}">${value}</dd>`).join('')}</dl>${u.side === 'enemy' ? '<p class="unknown-note">?? 神识不足 · 数值未探明</p>' : '<p class="unknown-note">五行抗性依次为金、木、水、火、土</p>'}<details><summary>已知术法与神通</summary><div class="known-arts"><button data-known="护体">${icon('defend')}护体</button><button data-known="突击">${icon('fist')}突击</button></div></details>`,headerClass:'detail-head',bodyClass:'detail-body'});
  applyButtonSkin(panel);
  panel.querySelector('.detail-close').onclick = () => { detailOpen = false; renderDetail(); };
  panel.querySelectorAll('[data-status]').forEach(b => b.onclick = () => notify(u.statuses[Number(b.dataset.status)].description));
  panel.querySelectorAll('[data-known]').forEach(b => b.onclick = () => notify(`${b.dataset.known}：仅显示已观察到的能力；未探明的倍率与条件不展示。`));
}
function renderHud() {
  const p = unit('p');
  $('.town-vitals').innerHTML = `<span class="player-name">无名修士 <small>道号未取</small></span><span class="realm-tag">练气 · 散修</span>${meter('生命',p.hp,p.maxHp,'health')}${meter('灵力',p.mp,p.maxMp,'spirit')}`;
  $('#battle-hotbar').hidden = phase !== 'player'; $('#other-turn').hidden = phase === 'player';
  $('#battle-hotbar').innerHTML = `<div class="actor-block"><div class="actor-title">正在操作 · 我方</div><div class="actor-face">${portrait}<div><strong>无名修士</strong><span class="actor-health">${p.hp} / ${p.maxHp}</span></div></div><div class="resource-pips">行动 <b>${actionUsed ? '○ 0' : '◆ 1'}</b>　移动 <b>${moveUsed ? '2' : '4'}</b><br>灵力 <b>${p.mp} / ${p.maxMp}</b></div></div><div class="action-groups">${['招式','物品','其他行为'].map(group => `<div class="action-group"><h3>${group}</h3><div class="action-buttons">${fixture.actions.filter(a => a.group === group).map(a => `<button class="action-tile${action?.id === a.id ? ' selected' : ''}" data-action="${a.id}" aria-label="${a.name}" aria-pressed="${action?.id === a.id}" aria-disabled="${!!isUnavailable(a)}" title="${isUnavailable(a) || `${a.summary} · ${a.cost}`}"><kbd>${a.key}</kbd>${icon(a.icon)}<strong>${a.name}</strong><small>${a.id === 'potion' ? `× ${itemCount}` : actionUsed && a.id !== 'move' ? '已用尽' : a.id === 'power' ? '不足' : a.id === 'escape' ? '不可用' : a.id === 'move' ? '移动' : a.id === 'spell' ? '灵力 18' : a.id === 'break' ? '灵力 24' : '行动 1'}</small></button>`).join('')}</div></div>`).join('')}</div><div class="turn-controls"><button class="end-turn" id="end-turn">结束<br>行动 →</button><button class="wait-turn" id="wait-turn">等待 ◷</button></div>`;
  applyButtonSkin($('#battle-hotbar'));
  $$('[data-action]').forEach(b => b.onclick = () => chooseAction(b.dataset.action));
  $('#end-turn').onclick = () => showTurnConfirmation('end');
  $('#wait-turn').onclick = () => { if (actionUsed || moveUsed) notify('等待需在尚未消耗行动或移动时选择（本轮交互样例）。'); else showTurnConfirmation('wait'); };
}
function renderPreview() {
  const panel = $('#action-preview'); panel.hidden = !action || phase !== 'player';
  if (panel.hidden) return;
  const turn = action.id === 'end' || action.id === 'wait';
  const target = action.target === 'self' ? unit('p') : action.target === 'enemy' && selected && unit(selected).side === 'enemy' ? unit(selected) : null;
  const targetReason = targetUnavailable(action);
  const ready = turn || action.target !== 'enemy' || (!!target && !targetReason);
  const title = turn ? action.name : `${action.name} ${target ? `→ ${target.name}` : action.target === 'ground' ? '→ 上层通路' : '· 请选择敌方目标'}`;
  const summary = turn ? action.summary : targetReason && target ? targetReason : `${action.cost}　·　${action.summary}`;
  // No hidden stat values exist in this projection; unknown result remains unknown.
  const result = turn ? action.result : action.target === 'enemy' ? `${action.knownEffect ? action.knownEffect + ' · ' : ''}伤害 / 命中：未探明` : action.result;
  panel.innerHTML = `<span class="cast-icon">${icon(action.icon)}</span><div class="cast-copy"><h2>${title}</h2><p>${summary}<br><b>${result}</b>${action.next ? `　·　自身下次位置：第 ${action.next} 位` : ''}</p></div><button class="cancel-cast" id="cancel-cast">取消</button><button class="confirm-cast" id="confirm-cast" ${ready ? '' : 'disabled'}>${turn ? '确认' : '施行'} →</button>`;
  applyButtonSkin(panel);
  $('#cancel-cast').onclick = cancel;
  $('#confirm-cast').onclick = confirm;
}
function render() { renderAxis(); renderDetail(); renderHud(); renderPreview(); }
function pick(hit) {
  if (hit.kind === 'unit') {
    selected = hit.id; detailOpen = true; field.highlight(selected); renderAxis(); renderDetail(); renderPreview();
    note(`正在查看 ${unit(selected).name}；操作栏仍属于无名修士`);
  } else if (hit.kind === 'tile') {
    if (action?.id === 'move') notify('移动仅演示亮起的石阶通路；点击“施行”查看到达结果。');
    else notify(`地形 (${hit.q}, ${hit.r}) · 高度 ${hit.height}；点选单位可查看详情。`);
  }
}
function chooseAction(id) {
  const a = fixture.actions.find(a => a.id === id); if (!a) return;
  const reason = isUnavailable(a); if (reason) { notify(reason); return; }
  action = a; $('#battle-notice').hidden = true;
  if (a.target === 'ground') field.path();
  else if (a.target === 'enemy') field.range(a.range);
  else field.clear();
  renderAxis(); renderHud(); renderPreview(); note(`${a.name}：先查看预览，再施行；取消不消耗资源`);
}
function cancel() { action = null; field.clear(); renderAxis(); renderHud(); renderPreview(); note('已取消选择；没有消耗资源'); }
function showTurnConfirmation(kind) {
  action = kind === 'wait' ? {id:'wait',name:'等待',icon:'wait',summary:'让出本次行动时机',result:fixture.waitPreview} : {id:'end',name:'结束行动',icon:'move',summary:`放弃剩余移动 ${moveUsed ? 2 : 4}${actionUsed ? '' : ' 与行动 1'}`,result:'交给下一位行动者；不会额外施放招式'};
  field.clear(); renderAxis(); renderHud(); renderPreview();
}
function confirm() {
  if (!action || phase !== 'player') return;
  const applied = action;
  if (applied.id === 'end' || applied.id === 'wait') {
    log(`${applied.name}：${applied.result}。`); if (applied.id === 'wait') nextState = fixture.afterTurns.wait; unit('p').ct = nextState.playerCt; unit('e1').ct = 100; action = null; phase = 'other'; field.clear(); field.highlight(selected); render(); note('他人行动展示；时间不会在思考时自行推进'); return;
  }
  if (isUnavailable(applied)) return;
  if (targetUnavailable(applied)) return;
  if (applied.id === 'move') { moveUsed = true; field.movePlayer(); log('无名修士沿石阶抵达上层通路；剩余移动 2（固定结果）。'); }
  else {
    actionUsed = true; unit('p').mp = applied.afterMp; nextState = fixture.afterTurns[applied.id];
    if (applied.afterCount !== undefined) itemCount = applied.afterCount;
    if (applied.afterStatuses) unit(applied.target === 'enemy' ? selected : 'p').statuses = structuredClone(applied.afterStatuses);
    if (applied.target === 'enemy') { unit(selected).hp = applied.afterHp; field.updateHealth(selected, applied.afterHp); }
    log(`无名修士施行${applied.name}${applied.target === 'enemy' ? `，${unit(selected).name}生命变为 ${applied.afterHp}` : ''}（固定结果）。`);
  }
  action = null; field.clear(); render(); notify(applied.id === 'move' ? '移动完成。仍有行动权，可继续选择招式。' : '行动完成。可检查目标、移动，或结束本次行动。');
}
function reset(state = 'player') {
  units = structuredClone(fixture.units); selected = null; action = null; phase = state === 'other' ? 'other' : 'player'; actionUsed = false; moveUsed = false; nextState = fixture.afterTurns[state === 'other' ? 'other' : 'end']; itemCount = 3; detailOpen = false;
  if (state === 'other') { unit('p').ct = nextState.playerCt; unit('e1').ct = 100; }
  clearTimeout(noticeTimer); $('#battle-notice').hidden = true; $('#battle-log').hidden = true; $('#log-toggle').setAttribute('aria-expanded','false');
  $('#more-menu').hidden = true; $('#more-toggle').setAttribute('aria-expanded','false');
  $('#log-entries').innerHTML = '<li>进入黄土旧道，轮到无名修士行动。</li>';
  field.reset(); field.setVisible(visibleUnits().map(u => u.id)); units.forEach(u => field.updateHealth(u.id,u.hp));
  side = false; grid = false; field.grid(false); $('#grid-toggle').setAttribute('aria-pressed','false'); $('#camera-toggle').setAttribute('aria-pressed','false');
  if (state === 'inspect' || state === 'preview') { selected = 'e1'; detailOpen = true; field.highlight(selected); }
  if (state === 'preview') { action = fixture.actions.find(a => a.id === 'spell'); field.range(fixture.spellRange); }
  if (state === 'other') field.highlight(null);
  $$('[data-state]').forEach(b => b.classList.toggle('active',b.dataset.state === state));
  $('#order-list').scrollTop = 0; render(); note('固定样例仅供布局与操作审阅；不代表正式战斗结果');
}
$$('[data-state]').forEach(b => b.onclick = () => reset(b.dataset.state));
$('#reset-demo').onclick = () => { density = 8; reset(); }; $('#return-player').onclick = () => reset();
$('#toggle-density').onclick = () => { density = density === 8 ? 12 : 8; if (selected && !visibleUnits().some(u => u.id === selected)) { selected = null; detailOpen = false; field.highlight(null); } field.setVisible(visibleUnits().map(u => u.id)); render(); note(density === 12 ? '12 人压力样例；行动轴向下滚动可查看全部参战者' : '8 人默认样例'); };
$('#grid-toggle').onclick = () => { grid = !grid; field.grid(grid); $('#grid-toggle').setAttribute('aria-pressed',String(grid)); };
$('#camera-toggle').onclick = () => { side = !side; field.cameraView(side ? 'side' : 'default'); $('#camera-toggle').setAttribute('aria-pressed',String(side)); };
$('#log-toggle').onclick = () => { const open = $('#battle-log').hidden; $('#battle-log').hidden = !open; $('#log-toggle').setAttribute('aria-expanded',String(open)); };
$('#close-log').onclick = () => { $('#battle-log').hidden = true; $('#log-toggle').setAttribute('aria-expanded','false'); };
$$('[data-global]').forEach(b => b.onclick = () => notify(`${b.dataset.global}入口保留；战斗中的使用行为需经操作栏确认。本原型不展开通用页面。`));
$('#more-toggle').onclick = () => { const open = $('#more-menu').hidden; $('#more-menu').hidden = !open; $('#more-toggle').setAttribute('aria-expanded',String(open)); };
['world-return','save-demo','settings-demo'].forEach(id => $(`#${id}`).onclick = () => { notify('此处仅演示固定菜单；不离开战斗或改写正式存档。'); });
document.addEventListener('keydown', e => {
  if (e.target.matches('input,select,textarea') || e.repeat || e.ctrlKey || e.altKey || e.metaKey) return;
  if (e.key === 'Escape') { cancel(); return; }
  const a = fixture.actions.find(a => a.key.toLowerCase() === e.key.toLowerCase());
  if (a) { e.preventDefault(); chooseAction(a.id); }
});
function scale() { $('#game-frame').style.transform = `scale(${$('#viewport').clientWidth / 1920})`; }
new ResizeObserver(scale).observe($('#viewport')); scale(); reset(); applyButtonSkin($('#battle-screen'));
