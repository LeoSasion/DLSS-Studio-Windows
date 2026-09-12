/* Durable server records plus a small per-browser workbench snapshot. */
let sessionReady=false;
const sessionKey='dlss-session-v2';
function saveSession(){
  if(!sessionReady)return;
  sessions[state.mode]={asset:state.asset,output:state.output,framePreview:state.framePreview};
  try{localStorage.setItem(sessionKey,JSON.stringify({mode:state.mode,sessions,job:state.job,busy:state.busy,
    jobKind:state.jobKind,jobContext:state.jobContext,pendingPayload:state.pendingPayload,
    view:state.view,time:videoPlayer.source.currentTime,settings:state.asset?settings():null}))}catch{}
}
window.addEventListener('pagehide',saveSession);
document.querySelector('.brand').addEventListener('click',event=>event.preventDefault());
form.addEventListener('change',saveSession);

function applySettings(values){
  if(!values)return;
  // Change the codec first, then restore its dependent container/quality fields.
  if(values.codec&&[...$('#codec').options].some(o=>o.value===values.codec&&!o.disabled))$('#codec').value=values.codec;
  updateCodec();
  for(const control of form.elements){
    if(!control.name||!(control.name in values)||control.name==='codec')continue;
    if(control.type==='checkbox')control.checked=!!values[control.name];else control.value=values[control.name];
  }
  state.style=values.style||'默认';
  $$('[data-style]').forEach(button=>{const active=button.dataset.style===state.style;button.classList.toggle('active',active);button.setAttribute('aria-pressed',String(active))});
  $('#intensity-number').value=$('#intensity').value;
  const cq=String(values.cq??19);$('#quality').value=values.bitrate||!['15','19','23','28'].includes(cq)?'custom':cq;
  $$('.advanced-range').forEach(row=>row.querySelector('output').textContent=`${Math.round(Number(row.querySelector('input').value)*100)}%`);
  updateCodec();paintRanges();updateOutputSize();
}

async function seekRestoredVideo(time){
  if(state.mode!=='video'||!Number.isFinite(time))return;
  const source=videoPlayer.source;
  const waitEvent=(name)=>new Promise(resolve=>{
    const done=()=>{clearTimeout(timer);source.removeEventListener(name,done);source.removeEventListener('error',done);resolve()};
    const timer=setTimeout(done,8000);source.addEventListener(name,done,{once:true});source.addEventListener('error',done,{once:true});
  });
  if(source.readyState<1)await waitEvent('loadedmetadata');
  if(!source.duration||source.error)return;
  videoPlayer.seek(time);
  if(source.seeking)await waitEvent('seeked');
}

async function restoreJob(job){
  const frame=job.job_kind==='frame',asset=frame?job.source_asset:job.asset;
  if(!asset)throw Error('原素材已被清理，仍可下载已完成的结果。');
  const saved=sessions[asset.kind];
  const prior=state.mode===asset.kind&&state.asset?.id===asset.id?state.output:saved?.asset?.id===asset.id?saved.output:null;
  state.busy=false;switchMode(asset.kind);state.asset=asset;state.framePreview=null;state.output=frame?prior:job.status==='done'?job:null;
  state.job=job.id;state.jobKind=frame?'frame':'full';state.jobContext=frame?{source:job.asset.url,time:job.frame_time}:null;
  state.pendingPayload=null;applySettings(job.settings);showAsset();
  if(frame){await seekRestoredVideo(job.frame_time);state.framePreview=job.status==='done'?{job,time:job.frame_time,source:job.asset.url}:null;videoPlayer.setFrame(state.framePreview)}
  if(['queued','running','cancelling'].includes(job.status)){
    state.busy=true;refreshControls();showProgress(job);schedulePoll(job.id,100);
  }else{
    view(job.status==='done'&&job.preview?(frame?'compare':'result'):'original');
    status(job.status==='done'?'处理完成':job.status==='cancelled'?'已取消':'处理失败',job.status==='error'?'error':'');
    if(job.status==='error')showNotice(job.message,true);refreshControls();
  }
  updateOutputSize();saveSession();
}

async function restoreSession(){
  let saved=null;
  try{saved=JSON.parse(localStorage.getItem(sessionKey)||'null')}catch{}
  try{
    if(saved?.sessions){
      for(const mode of ['image','video']){
        const record=saved.sessions[mode];if(!record?.asset)continue;
        try{
          const asset=await apiRequest(`/api/assets/${record.asset.id}/info`);
          const result=record.output?await apiRequest(`/api/jobs/${record.output.id}`).catch(()=>null):null;
          sessions[mode]={asset,output:result?.status==='done'?result:null,framePreview:null};
        }catch(error){if(!error.status)throw error}
      }
      state.mode=saved.mode==='video'?'video':'image';
      const record=sessions[state.mode];state.asset=record?.asset||null;state.output=record?.output||null;
      $$('[data-mode]').forEach(button=>{const active=button.dataset.mode===state.mode;button.classList.toggle('active',active);button.setAttribute('aria-pressed',String(active))});
      $('#video-settings').hidden=state.mode!=='video';$('#file-input').accept=state.mode==='image'?'image/png,image/jpeg,image/webp,image/bmp,image/tiff':'.mp4,.mov,.mkv,.webm,.avi,.m4v';
      applySettings(saved.settings);showAsset();
    }
    const recent=await apiRequest('/api/jobs?limit=1');
    if(recent.active.length){await restoreJob(recent.active[0]);return}
    if(saved?.busy&&saved.job){
      try{await restoreJob(await apiRequest(`/api/jobs/${saved.job}`));return}catch(error){if(error.status!==404)throw error}
    }
    if(saved?.pendingPayload&&state.asset){
      state.busy=true;state.jobKind=saved.jobKind;state.jobContext=saved.jobContext;refreshControls();
      sessionReady=true;submitJob(saved.pendingPayload).catch(error=>{state.busy=false;showNotice(error.message,true);refreshControls();saveSession()});return;
    }
    const frame=saved?.sessions?.[state.mode]?.framePreview;
    if(frame?.job&&state.mode==='video'){
      try{await restoreJob(await apiRequest(`/api/jobs/${frame.job.id}`));return}catch(error){if(!error.status)throw error}
    }
    await seekRestoredVideo(saved?.time);
    view(state.output?.preview?(saved?.view||'result'):'original');
    status(state.output?'处理完成':state.asset?'就绪':'等待素材');
  }finally{sessionReady=true;saveSession()}
}

(() => {
  const dialog=document.createElement('dialog');dialog.className='history-dialog';dialog.id='history-dialog';dialog.setAttribute('aria-labelledby','history-title');
  dialog.innerHTML=`<div class="history-heading"><h2 id="history-title">最近结果</h2><button type="button" class="icon-button history-close" aria-label="关闭最近结果"><img class="icon" src="/assets/icons/x.svg" alt=""></button></div><div class="history-tabs"><button type="button" class="glass-button" data-history-tab="recent" aria-pressed="true">处理记录</button><button type="button" class="glass-button" data-history-tab="cache" aria-pressed="false">缓存管理</button></div><p class="history-message" id="history-message" role="status"></p><ul class="history-list"></ul><div class="cache-options" hidden><p class="history-message">闲置清理会保留已完成结果、正在使用的素材和最近一小时上传的素材。</p><label class="check-row"><input type="checkbox" id="clear-results">同时清理历史结果及其素材（当前打开的结果保留）</label><p class="history-message" id="cleanup-warning" hidden>清理后无法恢复这些历史结果，请先下载需要保留的文件。</p><button type="button" class="glass-button" id="cleanup-cache">清理闲置文件</button></div><div class="history-footer"><button type="button" class="glass-button" id="history-prev">上一页</button><span id="history-page"></span><button type="button" class="glass-button" id="history-next">下一页</button><button type="button" class="glass-button" id="history-refresh">刷新</button></div>`;
  document.body.append(dialog);
  let tab='recent',page=0,total=0,loading=false;
  const perPage=()=>Math.max(1,Math.min(4,Math.floor((innerHeight-270)/(innerWidth<=760?108:78))));
  const message=dialog.querySelector('#history-message'),list=dialog.querySelector('.history-list');
  const size=bytes=>bytes>=1024**3?`${(bytes/1024**3).toFixed(2)} GiB`:`${(bytes/1024**2).toFixed(1)} MiB`;
  const showError=error=>{message.textContent=error.message||'暂时无法读取，请点击刷新重试。';message.classList.add('error')};
  async function render(){
    if(loading)return;loading=true;message.classList.remove('error');message.textContent='正在读取…';
    list.replaceChildren();dialog.querySelector('.cache-options').hidden=tab!=='cache';
    try{
      if(tab==='cache'){
        const value=await apiRequest('/api/cache');message.textContent=`素材与结果共占用 ${size(value.bytes)}。`;
        dialog.querySelector('#cleanup-cache').disabled=state.busy||state.uploading;
      }else{
        const count=perPage(),value=await apiRequest(`/api/jobs?offset=${page*count}&limit=${count}`);total=value.total;
        message.textContent=total?'结果保存在本机，可重新打开或下载。':'还没有处理记录。上传素材并开始处理后，结果会出现在这里。';
        for(const job of value.jobs){
          const item=document.createElement('li');item.className='history-item';
          const content=document.createElement('div'),name=document.createElement('strong'),meta=document.createElement('p');
          name.textContent=(job.job_kind==='frame'?job.source_asset?.name:job.asset?.name)||'素材已清理';name.title=name.textContent;
          const labels={done:'已完成',error:'失败',cancelled:'已取消',running:'处理中',queued:'等待处理',cancelling:'取消中'};
          meta.textContent=`${labels[job.status]||job.status} · ${job.job_kind==='frame'?`单帧 ${StudioVideoPlayer.formatTime(job.frame_time)}`:job.asset?.kind==='video'?'视频':'图片'} · ${new Date(job.created_at*1000).toLocaleString('zh-CN',{month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit'})}${job.width?` · ${job.width} × ${job.height}`:''}`;
          content.append(name,meta);item.append(content);
          const open=document.createElement('button');open.type='button';open.className='glass-button';open.textContent=job.status==='done'?'查看':'恢复';open.disabled=state.busy&&state.job!==job.id||state.uploading;
          open.onclick=async()=>{open.disabled=true;try{await restoreJob(job);dialog.close()}catch(error){showError(error);open.disabled=false}};item.append(open);
          if(job.download){const download=document.createElement('a');download.className='glass-button';download.textContent='下载';download.href=job.download;download.setAttribute('download','');item.append(download)}
          list.append(item);
        }
      }
      const pages=Math.max(1,Math.ceil(total/perPage()));
      dialog.querySelector('#history-page').textContent=`${page+1} / ${pages}`;
      for(const id of ['history-prev','history-next','history-page'])dialog.querySelector('#'+id).hidden=tab==='cache';
      dialog.querySelector('#history-prev').disabled=page===0;dialog.querySelector('#history-next').disabled=page+1>=pages;
    }catch(error){showError(error)}finally{loading=false}
  }
  $('#history-button').onclick=()=>{page=0;dialog.showModal();render()};
  dialog.querySelector('.history-close').onclick=()=>dialog.close();
  dialog.querySelector('#history-refresh').onclick=render;
  for(const button of dialog.querySelectorAll('[data-history-tab]'))button.onclick=()=>{tab=button.dataset.historyTab;page=0;dialog.querySelectorAll('[data-history-tab]').forEach(b=>b.setAttribute('aria-pressed',String(b===button)));render()};
  dialog.querySelector('#history-prev').onclick=()=>{page--;render()};dialog.querySelector('#history-next').onclick=()=>{page++;render()};
  dialog.querySelector('#clear-results').onchange=event=>{dialog.querySelector('#cleanup-warning').hidden=!event.target.checked;dialog.querySelector('#cleanup-cache').textContent=event.target.checked?'清理历史素材与结果':'清理闲置文件'};
  dialog.querySelector('#cleanup-cache').onclick=async()=>{
    const button=dialog.querySelector('#cleanup-cache');button.disabled=true;
    const records=[...Object.values(sessions),{asset:state.asset,output:state.output,framePreview:state.framePreview}].filter(Boolean);
    const payload={include_results:dialog.querySelector('#clear-results').checked,
      keep_asset_ids:records.flatMap(r=>[r.asset?.id,r.framePreview?.job.asset_id]).filter(Boolean),
      keep_job_ids:records.flatMap(r=>[r.output?.id,r.framePreview?.job.id]).filter(Boolean)};
    try{const result=await apiRequest('/api/cache/cleanup',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)},60000);await render();message.textContent+=` 已释放 ${size(result.freed_bytes)}${result.skipped?`，${result.skipped} 个占用中的目录未清理`:''}。`}
    catch(error){showError(error)}finally{button.disabled=state.busy||state.uploading}
  };
  let resizeTimer;window.addEventListener('resize',()=>{clearTimeout(resizeTimer);resizeTimer=setTimeout(()=>{if(dialog.open){page=0;render()}},150)});
})();
