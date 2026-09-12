/* Native caption controls are added only by the trusted desktop host. */
(() => {
  if (!window.__DLSS_DESKTOP__ || !window.chrome?.webview) return;
  const send = action => window.chrome.webview.postMessage(action);
  document.documentElement.classList.add('desktop-shell');
  const header = document.querySelector('.topbar');
  const actions = document.createElement('div');
  actions.className = 'desktop-actions';
  actions.append(document.querySelector('.appearance-controls'));
  const browser = document.createElement('button');
  browser.type = 'button';
  browser.className = 'open-browser';
  browser.textContent = '浏览器';
  browser.title = '在浏览器中打开';
  browser.setAttribute('aria-label', browser.title);
  browser.addEventListener('click', () => send('open-browser'));
  actions.append(browser);
  const controls = document.createElement('div');
  controls.className = 'desktop-window-controls';
  for (const [action, label, icon] of [['minimize', '最小化窗口', 'minus'], ['maximize', '最大化窗口', 'maximize'], ['close', '关闭窗口', 'x']]) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `window-${action}`;
    button.title = label;
    button.setAttribute('aria-label', label);
    button.innerHTML = `<img class="icon" src="/assets/icons/${icon}.svg" alt="">`;
    button.addEventListener('click', () => send(action));
    controls.append(button);
  }
  actions.append(controls);
  header.append(actions);
  window.chrome.webview.addEventListener('message', event => {
    if (event.data?.type !== 'window-state') return;
    const maximized = event.data.maximized === true;
    document.documentElement.classList.toggle('desktop-maximized', maximized);
    const button = controls.querySelector('.window-maximize');
    button.title = maximized ? '还原窗口' : '最大化窗口';
    button.setAttribute('aria-label', button.title);
    button.setAttribute('aria-pressed', String(maximized));
    button.querySelector('img').src = `/assets/icons/${maximized ? 'restore' : 'maximize'}.svg`;
    const glass = document.querySelector('#glass-toggle');
    glass.disabled = maximized;
    glass.title = maximized ? '最大化时使用不透明外观，还原后恢复玻璃设置' : document.documentElement.dataset.glass === 'on' ? '关闭毛玻璃面板' : '开启毛玻璃面板';
  });
  header.addEventListener('mousedown', event => {
    if (event.button !== 0 || event.target.closest('button, a, input, select')) return;
    send(event.detail === 2 ? 'maximize' : 'drag');
  });
  const notifyAppearance = () => {
    send(`theme:${document.documentElement.dataset.theme}`);
    send(`glass:${document.documentElement.dataset.glass==='on'?'on':'off'}`);
  };
  new MutationObserver(notifyAppearance).observe(document.documentElement, {attributes:true, attributeFilter:['data-theme','data-glass']});
  notifyAppearance();
  send('window-state');
})();
