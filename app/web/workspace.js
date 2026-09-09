/* Keep secondary options in a paged dialog without growing the workbench. */
(() => {
  const form = document.querySelector('#settings-form');
  const dialog = document.createElement('dialog');
  dialog.id = 'settings-dialog';
  dialog.setAttribute('aria-labelledby', 'settings-title');
  dialog.innerHTML = `<div class="dialog-heading"><h2 id="settings-title"></h2><button type="button" class="icon-button" aria-label="关闭参数面板"><img class="icon" src="/assets/icons/x.svg" alt=""></button></div><div class="settings-pages"></div><div class="settings-footer"><div class="pager"><button type="button" class="glass-button previous" aria-label="上一页">上一页</button><span class="page-number" aria-live="polite"></span><button type="button" class="glass-button next" aria-label="下一页">下一页</button></div><button type="button" class="glass-button done">完成</button></div>`;
  document.body.append(dialog);
  const pagesHost = dialog.querySelector('.settings-pages');
  let owner, items = [], pages = [], page = 0, trigger;
  function render() {
    pages.forEach((el, i) => el.hidden = i !== page);
    dialog.querySelector('.page-number').textContent = `${page + 1} / ${pages.length}`;
    dialog.querySelector('.previous').disabled = page === 0;
    dialog.querySelector('.next').disabled = page === pages.length - 1;
    dialog.querySelector('.pager').hidden = pages.length <= 1;
  }
  function paginate() {
    if (!owner) return;
    const previousPage = page;
    pagesHost.replaceChildren(); pages = []; page = 0;
    const budget = Math.max(80, innerHeight - 190);
    let current;
    for (const item of items) {
      if (!current) { current = document.createElement('div'); current.className = 'settings-page'; pages.push(current); pagesHost.append(current); }
      current.append(item);
      if (current.getBoundingClientRect().height > budget && current.children.length > 1) {
        current = document.createElement('div'); current.className = 'settings-page'; pages.push(current); pagesHost.append(current); current.append(item);
      }
    }
    page = Math.min(previousPage, pages.length - 1);
    render();
  }
  function open(details) {
    if (dialog.open) dialog.close();
    owner = details;
    trigger = details.querySelector('summary');
    dialog.querySelector('h2').textContent = trigger.textContent.trim();
    const content = details.querySelector('.detail-content');
    // Keep the controls associated with their original form and event listeners.
    items = [...content.children];
    content.querySelectorAll('input,select,textarea').forEach(el => el.setAttribute('form', form.id));
    page = 0;
    dialog.showModal(); paginate();
    dialog.querySelector('.dialog-heading button').focus();
  }
  // Flatten only the advanced slider container, so each control can be paged.
  const sliders = document.querySelector('#advanced-sliders');
  sliders.replaceWith(...sliders.children);
  document.querySelectorAll('#settings-form details').forEach(details => {
    details.open = false;
    const summary = details.querySelector('summary');
    summary.setAttribute('aria-haspopup', 'dialog');
    summary.addEventListener('click', event => { event.preventDefault(); open(details); });
    // Also handles the resolution selector opening custom dimensions.
    details.addEventListener('toggle', () => { if (details.open) { details.open = false; open(details); } });
  });
  dialog.addEventListener('close', () => {
    if (owner) owner.querySelector('.detail-content').append(...items);
    owner = null; items = []; pagesHost.replaceChildren(); trigger?.focus();
  });
  dialog.querySelector('.dialog-heading button').onclick = () => dialog.close();
  dialog.querySelector('.done').onclick = () => dialog.close();
  dialog.querySelector('.previous').onclick = () => { page--; render(); };
  dialog.querySelector('.next').onclick = () => { page++; render(); };
  // Reflow when conditional codec / quality fields appear, or the window resizes.
  dialog.addEventListener('change', () => requestAnimationFrame(paginate));
  window.addEventListener('resize', paginate);
  document.addEventListener('invalid', event => {
    if (dialog.contains(event.target)) {
      page = pages.findIndex(p => p.contains(event.target)); render();
    } else {
      const details = event.target.closest('details');
      if (details) { open(details); page = pages.findIndex(p => p.contains(event.target)); render(); }
    }
  }, true);
  const inspector = document.querySelector('.inspector');
  const settingsButton = document.createElement('button');
  settingsButton.type = 'button'; settingsButton.className = 'glass-button mobile-settings'; settingsButton.textContent = '增强设置';
  settingsButton.setAttribute('aria-expanded', 'false');
  document.querySelector('.canvas-toolbar').append(settingsButton);
  const close = document.createElement('button'); close.type = 'button'; close.className = 'icon-button inspector-close'; close.setAttribute('aria-label','收起增强设置'); close.innerHTML = '<img class="icon" src="/assets/icons/x.svg" alt="">';
  document.querySelector('.inspector-title').append(close);
  const background = [document.querySelector('.topbar'), ...document.querySelector('.workspace').children].filter(el => el !== inspector);
  function closeInspector() { inspector.classList.remove('mobile-open'); background.forEach(el => el.inert = false); settingsButton.setAttribute('aria-expanded','false'); settingsButton.focus(); }
  settingsButton.onclick = () => { inspector.classList.add('mobile-open'); background.forEach(el => el.inert = true); settingsButton.setAttribute('aria-expanded','true'); close.focus(); };
  close.onclick = closeInspector;
  window.addEventListener('resize', () => { if (getComputedStyle(settingsButton).display === 'none' && inspector.classList.contains('mobile-open')) closeInspector(); });
  document.addEventListener('keydown', e => {
    if (dialog.open || !inspector.classList.contains('mobile-open')) return;
    if (e.key === 'Escape') closeInspector();
    if (e.key === 'Tab') {
      const controls = [...inspector.querySelectorAll('button,select,input,summary')].filter(el => !el.disabled && el.getClientRects().length);
      const first = controls[0], last = controls[controls.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    }
  });
})();
