import {windowTemplate, buttonTemplate as button, listTemplate, escapeText as esc} from './ui-kit/templates.js';
const $ = selector => document.querySelector(selector);
const items = [
 {key:'medicine',title:'回灵丹',subtitle:'药品 · 随身携带',trailing:'× 3',category:'药品',description:'收在随身行囊中的丹药。战斗中使用需经行动预览与确认；这里仅查看与整理。'},
 {key:'map',title:'关中行旅札记',subtitle:'杂物 · 长文样例',trailing:'× 1',category:'杂物',description:'一路所见，随手记下。此页用较长的记录检验阅读窗口：名称、正文与操作区各有自己的空间。'},
 {key:'long',title:'写有很长题签的旧道行旅抄录与附注',subtitle:'杂物 · 名称自然换行',trailing:'× 1',category:'杂物',description:'这份样例特意使用较长标题，验证标题与列表行都能够自然换行。确认按钮仍保留完整文字。'},
 {key:'locked',title:'封存卷册',subtitle:'',trailing:'封存',disabled:true,hint:'尚未鉴识，暂不可查看',category:'杂物',description:''}
];
let selected = 'medicine', empty = false, organized = false;
const status = text => { $('#sample-status').textContent = text; };
const current = () => items.find(item => item.key === selected);
function switchSkin() {
 const proof = document.documentElement.dataset.skin !== 'proof';
 document.documentElement.dataset.skin = proof ? 'proof' : 'jade';
 $('#skin-toggle').setAttribute('aria-pressed',String(proof));
 $('#skin-label').textContent = proof ? '绛紫 · 校验皮肤' : '青玉 · 当前皮肤';
 // Do not rerender templates: open dialogs, selection and content stay intact.
}
$('#skin-toggle').onclick = switchSkin;
document.addEventListener('keydown',event => {
 if (event.key.toLowerCase() === 'k' && !event.repeat && !event.ctrlKey && !event.altKey && !event.metaKey && !event.target.matches('input,textarea,select')) switchSkin();
});
function detailMarkup() {
 const item = current();
 return `<div class="item-seal" aria-hidden="true">囊</div><span class="item-kicker">随身物品 / ${esc(item.category)}</span><h3>${esc(item.title)}</h3><p>${esc(item.description)}</p><dl class="item-summary"><div><dt>持有数量</dt><dd>${esc(item.trailing)}</dd></div><div><dt>收纳状态</dt><dd id="organized-label">${organized ? '已整理' : '待整理'}</dd></div></dl><p class="item-note">可查看详录或整理行囊。此处不消耗物品，也不改变正式存档。</p><div class="sample-actions">${button({label:'查看详录',key:'detail',variant:'primary'})}${button({label:'整理行囊',key:'organize'})}</div>`;
}
function renderList() {
 $('#item-list').innerHTML = listTemplate(empty ? [] : items.map(item => ({...item,selected:item.key===selected})), '暂无任务物品');
 $('#item-sheet').hidden = empty;
 $('#empty-sheet').hidden = !empty;
}
function renderFull() {
 $('#sample-root').hidden = false; $('#closed-state').hidden = true;
 $('#sample-root').innerHTML = windowTemplate({
  size:'fullscreen', key:'inventory',title:'行囊',subtitle:'随身所携，皆有来处。',
  body:`<div class="inventory-layout"><div class="inventory-list"><p class="section-label">物品目录 / 点击查看</p><div class="inventory-filter">${button({label:'全部物品',key:'all',selected:!empty})}${button({label:'任务物品',key:'empty',selected:empty})}</div><div id="item-list"></div></div><article id="item-sheet" class="item-sheet" data-ui-panel="paper">${detailMarkup()}</article><article id="empty-sheet" class="item-sheet" data-ui-panel="paper" hidden><span class="item-kicker">任务物品</span><h3>行囊尚空</h3><p>获得任务物品后，将在此处查看。</p></article></div>`,
  footer:`<span class="ui-footer-note">物品总类 4 · 展示数据</span>${button({label:'收起行囊',key:'close-full',variant:'quiet'})}`
 });
 renderList();
}
function showLarge(mode = 'detail') {
 const item = current();
 const states = `<div class="state-grid"><div class="state-cell">${button({label:'普通按钮',key:'state-normal'})}<p>移入查看悬停，按住查看按下。</p></div><div class="state-cell">${button({label:'已选中',key:'state-selected',selected:true})}<p>选中保留底图与左侧标记；悬停只提亮。</p></div><div class="state-cell">${button({label:'确认操作',key:'state-primary',variant:'primary'})}<p>确认是用途；执行仍由内容事件决定。</p></div><div class="state-cell">${button({label:'暂不可用',key:'state-disabled',disabled:true,hint:'条件未满足'})}<p>条件未满足。禁用原因独立显示。</p></div><div class="state-cell">${button({label:'返回',key:'state-quiet',variant:'quiet'})}<p>弱操作共用文字、焦点和按下规则。</p></div><div class="state-cell">${button({label:'焦点样例',key:'state-focus'})}<p>按 Tab 查看键盘焦点，Enter 可操作。</p></div></div>`;
 const long = `<div class="long-copy"><div data-ui-panel="paper"><h3>${esc(item.title)}</h3><p>${esc(item.description)}</p></div><h3>行旅附记</h3>${[
 '行前清点随身物品，名称与数量分列。目录中的选中标记只表示正在查看，不代表已经使用。',
 '纸面用来承载需要连续阅读的内容。较长段落保留行距，正文超出窗口后在此处滚动，标题、关闭与底部操作始终可达。',
 '题签写得很长时，自然换行；不缩小字体来挤进一行。物品名称和正文仍由内容数据提供，背景纹理不包含文字。',
 '暂不能查看的卷册仍列在目录中，并显示原因。若分类为空，保留分类入口和空态说明。',
 '此处所有名称、数量及描述都是界面容量样例。整理只更新本地展示标签，关闭或刷新不影响游戏记录。',
 '记录末尾。点击“整理确认”可检验小弹窗；取消回到这里，确认只改变样板中的收纳状态。'
 ].map(text => `<p>${text}</p>`).join('')}</div>`;
 $('#large-dialog').innerHTML = windowTemplate({size:'large',key:'ledger',title:mode==='states'?'控件状态':'物品详录',subtitle:mode==='states'?'同一套按钮资源与状态规则':'随身行囊 / '+item.title,body:mode==='states'?states:long,footer:`<span class="ui-footer-note">K 切换皮肤 · Esc 返回</span>${button({label:'返回行囊',key:'close-large',variant:'quiet'})}${button({label:'整理确认',key:'organize',variant:'primary'})}`});
 $('#large-dialog').showModal();
}
function showSmall() {
 $('#small-dialog').innerHTML = windowTemplate({size:'small',key:'confirm',title:'整理行囊',subtitle:'操作确认',body:`<p class="confirm-copy">将当前行囊标记为已整理？</p><p class="dialog-note">物品数量保持不变。取消后回到原来的查看位置。</p>`,footer:`${button({label:'取消',key:'cancel-small',variant:'quiet'})}${button({label:'确认整理',key:'confirm-small',variant:'primary'})}`});
 $('#small-dialog').showModal();
}
document.addEventListener('click',event => {
 const target = event.target.closest('button'); if (!target || target.disabled) return;
 const key = target.dataset.key, close = target.dataset.close;
 if (close === 'confirm' || key === 'cancel-small') { $('#small-dialog').close(); status('已取消；物品与收纳状态未改变。'); }
 else if (close === 'ledger' || key === 'close-large') $('#large-dialog').close();
 else if (close === 'inventory' || key === 'close-full') { $('#sample-root').hidden = true; $('#closed-state').hidden = false; $('#open-full').focus(); }
 else if (key === 'all' || key === 'empty') {
  empty = key === 'empty'; renderList();
  document.querySelectorAll('.inventory-filter button').forEach(b => b.setAttribute('aria-pressed',String(b.dataset.key===key)));
  status(empty?'当前分类为空。':'已显示全部物品。');
 }
 else if (items.some(item => item.key === key)) {
  selected = key; $('#item-sheet').innerHTML = detailMarkup();
  document.querySelectorAll('#item-list button').forEach(b => b.setAttribute('aria-pressed',String(b.dataset.key===key)));
  status('正在查看：'+current().title);
 }
 else if (key === 'detail') showLarge();
 else if (key === 'organize') showSmall();
 else if (key === 'confirm-small') {
  organized = true; $('#organized-label').textContent = '已整理'; $('#small-dialog').close();
  status('整理完成；持有数量保持不变。');
 }
 else if (key?.startsWith('state-')) status('已操作：'+target.textContent);
});
$('#small-dialog').addEventListener('cancel',() => status('已取消；物品与收纳状态未改变。'));
$('#open-full').onclick = renderFull;
$('#show-states').onclick = () => showLarge('states');
renderFull();
