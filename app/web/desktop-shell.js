/* Native caption controls are added only by the trusted desktop host. */
(() => {
  if (!window.__DLSS_DESKTOP__ || !window.chrome?.webview) return;
  const send = action => window.chrome.webview.postMessage(action);
  document.documentElement.classList.add('desktop-shell');
  const header = document.querySelector('.topbar');
  const actions = document.createElement('div');
  actions.className = 'desktop-actions';
  actions.append(document.querySelector('.theme-switch'));
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
  for (const [action, label, icon] of [['minimize', '最小化窗口', 'minus'], ['close', '关闭窗口', 'x']]) {
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
  header.addEventListener('mousedown', event => {
    if (event.button !== 0 || event.target.closest('button, a, input, select')) return;
    send(event.detail === 2 ? 'maximize' : 'drag');
  });
  const notifyTheme = () => send(`theme:${document.documentElement.dataset.theme}`);
  new MutationObserver(notifyTheme).observe(document.documentElement, {attributes:true, attributeFilter:['data-theme']});
  notifyTheme();
})();
