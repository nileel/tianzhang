// Only trusted local markup fills header/body/footer. Display strings are escaped.
export const escapeText = value => String(value).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
export function panelContent({header = '', body = '', footer = '', headerClass = '', bodyClass = ''}) {
  return `${header ? `<header class="ui-panel-header ${headerClass}">${header}</header>` : ''}<div class="ui-panel-body ${bodyClass}">${body}</div>${footer ? `<footer class="ui-panel-footer">${footer}</footer>` : ''}`;
}
export function buttonTemplate({label, variant = 'normal', disabled = false, selected = false, key = '', hint = ''}) {
  if (!['normal','primary','quiet'].includes(variant)) throw new Error('Unknown button variant');
  return `<button class="ui-button ui-text-button" data-variant="${variant}" data-key="${escapeText(key)}" ${disabled ? 'disabled' : ''} ${selected ? 'aria-pressed="true"' : ''} title="${escapeText(hint)}">${escapeText(label)}</button>`;
}
export function listTemplate(items, emptyText = '暂无内容') {
  return `<div class="ui-list">${items.length ? items.map(item => `<button class="ui-list-item ui-button" data-key="${escapeText(item.key)}" aria-pressed="${!!item.selected}" ${item.disabled ? 'disabled' : ''} title="${escapeText(item.hint || '')}"><span class="ui-list-main"><strong>${escapeText(item.title)}</strong><small>${escapeText(item.disabled ? item.hint : item.subtitle || '')}</small></span><span class="ui-list-trailing">${escapeText(item.trailing || '›')}</span></button>`).join('') : `<div class="ui-empty" role="status">${escapeText(emptyText)}</div>`}</div>`;
}
export function windowTemplate({size, title, subtitle = '', body, footer = '', key}) {
  if (!['fullscreen','large','small'].includes(size)) throw new Error('Unknown window template size');
  return `<section class="ui-window ui-window--${size}" data-ui-panel="dark" data-template="${size}" data-window="${escapeText(key)}" aria-label="${escapeText(title)}">${panelContent({header:`<div><h2 class="ui-window-title">${escapeText(title)}</h2><p class="ui-window-subtitle">${escapeText(subtitle)}</p></div><button class="ui-button ui-close" data-variant="quiet" data-close="${escapeText(key)}" aria-label="关闭${escapeText(title)}">×</button>`,body,footer})}</section>`;
}
export function applyButtonSkin(root) {
  root.querySelectorAll('button').forEach(button => {
    // Dedicated picking markers and fixed HUD keep their accepted geometry/art.
    if (button.matches('.axis-unit,.unit-label,.town-player,.global-actions button')) return;
    button.classList.add('ui-button');
    if (button.matches('.confirm-cast,.end-turn')) button.dataset.variant = 'primary';
    if (button.matches('.cancel-cast,.detail-close,.wait-turn,#close-log')) button.dataset.variant = 'quiet';
  });
}
