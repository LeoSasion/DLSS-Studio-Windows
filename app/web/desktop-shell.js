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
    let preferred=false;
    try{preferred=localStorage.getItem('dlss-glass')==='on'}catch{}
    if(typeof glass==='function')glass(!maximized&&preferred,false);
    const button = controls.querySelector('.window-maximize');
    button.title = maximized ? '还原窗口' : '最大化窗口';
    button.setAttribute('aria-label', button.title);
    button.setAttribute('aria-pressed', String(maximized));
    button.querySelector('img').src = `/assets/icons/${maximized ? 'restore' : 'maximize'}.svg`;
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
  let regionFrame=0,lastRegions='';
  const scheduleRegions=()=>{
    if(regionFrame)return;
    regionFrame=requestAnimationFrame(()=>{
      regionFrame=0;
      const regions=[...document.querySelectorAll('.glass,dialog[open]')].flatMap(el=>{
        const r=el.getBoundingClientRect(),s=getComputedStyle(el),d=devicePixelRatio;
        return r.width&&r.height&&s.visibility!=='hidden'?[[r.left*d,r.top*d,r.width*d,r.height*d,(parseFloat(s.borderTopLeftRadius)||0)*d].map(Math.round)]:[];
      });
      const value=JSON.stringify(regions);
      if(value!==lastRegions){lastRegions=value;send(`blur-regions:${value}`)}
    });
  };
  const sizes=new ResizeObserver(scheduleRegions);
  document.querySelectorAll('.glass').forEach(el=>sizes.observe(el));
  new MutationObserver(scheduleRegions).observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['class','hidden','open']});
  window.addEventListener('resize',scheduleRegions);
  scheduleRegions();
  send('window-state');
})();
