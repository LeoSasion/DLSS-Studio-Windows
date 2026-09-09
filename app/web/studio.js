const $ = (s) => document.querySelector(s);
const $$ = (s) => [...document.querySelectorAll(s)];
const form = $('#settings-form');
const state = {mode:'image', asset:null, output:null, view:'original', style:'默认', busy:false, uploading:false, sourceWidth:0,sourceHeight:0, job:null, hardware:null};
const sessions = {image:null,video:null};
let options = {containers:{hevc_nvenc:['mp4','mkv','mov']}};
let dragDepth=0;
const showNotice = (text,error=false) => {$('#notice-text').textContent=text;$('#notice').hidden=false;$('#notice').classList.toggle('error',error)};
const paintRanges=()=>$$('input[type=range]:not(#compare-slider)').forEach(el=>el.style.setProperty('--range',`${100*(Number(el.value)-Number(el.min))/(Number(el.max)-Number(el.min))}%`));
function theme(value){document.documentElement.dataset.theme=value;try{localStorage.setItem('dlss-theme',value)}catch{}$$('.theme-switch button').forEach(b=>{const active=b.dataset.theme===value;b.classList.toggle('active',active);b.setAttribute('aria-pressed',active)})}
$$('.theme-switch button').forEach(b=>b.addEventListener('click',()=>theme(b.dataset.theme)));
theme(document.documentElement.dataset.theme);

function status(text,kind=''){ $('#status-text').textContent=text;$('.status-dot').className=`status-dot ${kind}`; }
function fitStage(){if(!state.sourceWidth||!state.sourceHeight)return;const el=$('#canvas'); const ratio=state.sourceWidth/state.sourceHeight; const w=Math.min(el.clientWidth,el.clientHeight*ratio);$('#image-stage').style.width=`${w}px`;$('#image-stage').style.aspectRatio=String(ratio)}
new ResizeObserver(fitStage).observe($('#canvas'));
function sizeKey(){return $('#size-preset').value}
// Match Python round-to-even, then align video / preset dimensions to even pixels.
function evenSize(value){const base=Math.floor(value),fraction=value-base;const rounded=fraction===.5?(base%2?base+1:base):Math.round(value);return Math.max(2,rounded-rounded%2)}
function dimensions(){if(!state.asset)return [0,0];const w=state.sourceWidth,h=state.sourceHeight;const match=sizeKey().match(/(\d+)×(\d+)/);if(!match)return state.mode==='image'?[w,h]:[evenSize(w),evenSize(h)];let bw=Number(match[1]),bh=Number(match[2]);if(h>w)[bw,bh]=[bh,bw];const f=Math.min(bw/w,bh/h);return [evenSize(w*f),evenSize(h*f)]}
function updateOutputSize(){const [w,h]=dimensions();$('#output-size').textContent=!state.asset?'—':`${w} × ${h}`}
function view(value){if(value!=='original'&&!state.output?.preview)return;state.view=value;$$('[data-view]').forEach(b=>{b.classList.toggle('active',b.dataset.view===value);b.setAttribute('aria-pressed',b.dataset.view===value)});const compare=value==='compare'&&state.mode==='image';$('#compare-line').hidden=!compare;$('#compare-slider').hidden=!compare;$('#result-label').hidden=!compare;$('#source-label').hidden=value==='result';$('#result-image').hidden=value==='original'||state.mode!=='image';$('#result-image').style.clipPath=value==='result'?'none':`inset(0 0 0 ${$('#compare-slider').value}%)`;if(state.mode==='video'){$('#video-preview').src=value==='original'?state.asset?.url||'':state.output?.preview||''}fitStage()}
$$('[data-view]').forEach(b=>b.addEventListener('click',()=>view(b.dataset.view)));
$('#compare-slider').addEventListener('input',e=>{const value=e.target.value;$('#result-image').style.clipPath=`inset(0 0 0 ${value}%)`;$('#compare-line').style.left=`${value}%`});
function refreshControls(){const locked=state.busy||state.uploading;$('#start-button').disabled=locked||!state.asset||!state.hardware?.ready;$('#upload-button').disabled=locked;$('#empty-upload').disabled=locked;$$('[data-mode]').forEach(b=>b.disabled=locked);[...form.elements].forEach(e=>e.disabled=locked);$('#reset-settings').disabled=locked;$$('[data-view]').forEach(b=>b.disabled=b.dataset.view!=='original'&&(!state.output?.preview||state.mode==='video'&&b.dataset.view==='compare'));const download=$('#download');const ready=!!state.output?.download;download.classList.toggle('disabled',!ready);download.setAttribute('aria-disabled',String(!ready));download.tabIndex=ready?0:-1;if(ready){download.href=state.output.download;download.setAttribute('download','')}else{download.removeAttribute('href')}$('#start-button span').textContent=state.busy?'正在处理…':state.uploading?'正在上传…':'开始处理';}
function showAsset(){
  const item=state.asset, imageMode=state.mode==='image';
  $('#file-name').textContent=item?item.name:imageMode?'尚未选择图片':'尚未选择视频';
  $('#file-meta').textContent=item?`${item.width} × ${item.height}`:'';
  $('#source-label').textContent='原图';
  if(item?.kind==='image')$('#source-image').src=item.url;
  else $('#source-image').removeAttribute('src');
  $('#source-image').alt=item?.name||'原图';
  $('#image-stage').hidden=!imageMode||!item;
  $('#video-preview').hidden=imageMode||!item;
  $('#empty-state').hidden=!!item;
  $('#empty-state h2').textContent=imageMode?'把图片拖到这里':'把视频拖到这里';
  $('#empty-state p').textContent=imageMode?'支持 PNG、JPG、WebP 等常见格式':'支持 MP4、MOV、MKV 等常见格式';
  $('#empty-upload').textContent=imageMode?'选择图片':'选择视频';
  state.sourceWidth=item?.width||0;state.sourceHeight=item?.height||0;
  if(state.output?.preview&&imageMode)$('#result-image').src=state.output.preview;
  else $('#result-image').removeAttribute('src');
  view('original');refreshControls();updateOutputSize();fitStage();
}

function switchMode(mode){if(state.busy||state.uploading||state.mode===mode)return;sessions[state.mode]={asset:state.asset,output:state.output};state.mode=mode;const saved=sessions[mode];state.asset=saved?.asset||null;state.output=saved?.output||null;$$('[data-mode]').forEach(b=>{b.classList.toggle('active',b.dataset.mode===mode);b.setAttribute('aria-pressed',b.dataset.mode===mode)});$('#video-settings').hidden=mode!=='video';$('#file-input').accept=mode==='image'?'image/png,image/jpeg,image/webp,image/bmp,image/tiff':'.mp4,.mov,.mkv,.webm,.avi,.m4v';$('#notice').hidden=true;$('#job-log').textContent='尚未开始处理。';status(state.output?'处理完成':state.asset?'就绪':'等待素材');showAsset()}
$$('[data-mode]').forEach(b=>b.addEventListener('click',()=>switchMode(b.dataset.mode)));
async function readResponse(response){let value;try{value=await response.json()}catch{throw new Error('服务响应异常，请稍后重试。')}if(!response.ok){throw new Error(typeof value.detail==='string'?value.detail:'参数无效，请检查输入数值。')}return value}
async function upload(file){if(!file||state.busy||state.uploading)return;const isImage=/\.(png|jpe?g|webp|bmp|tiff?)$/i.test(file.name);const isVideo=/\.(mp4|mov|mkv|webm|avi|m4v)$/i.test(file.name);if(state.mode==='image'&&!isImage||state.mode==='video'&&!isVideo){showNotice(`请在${state.mode==='image'?'图片':'视频'}增强中上传对应格式的文件。`,true);return}state.uploading=true;refreshControls();status('正在上传…','running');const body=new FormData();body.append('file',file);try{state.asset=await readResponse(await fetch('/api/upload',{method:'POST',body}));state.output=null;$('#notice').hidden=true;$('#job-log').textContent='素材已准备好。';status('就绪');showAsset()}catch(e){showNotice(e.message,true);status('上传失败','error')}finally{state.uploading=false;refreshControls();$('#file-input').value=''}}
$('#upload-button').addEventListener('click',()=>$('#file-input').click());$('#empty-upload').addEventListener('click',()=>$('#file-input').click());$('#file-input').addEventListener('change',e=>upload(e.target.files[0]));
const panel=$('.canvas-panel');panel.addEventListener('dragenter',e=>{e.preventDefault();if(state.busy||state.uploading)return;dragDepth++;$('#drop-overlay').hidden=false});panel.addEventListener('dragover',e=>e.preventDefault());panel.addEventListener('dragleave',()=>{dragDepth--;if(dragDepth<=0)$('#drop-overlay').hidden=true});panel.addEventListener('drop',e=>{e.preventDefault();dragDepth=0;$('#drop-overlay').hidden=true;upload(e.dataTransfer.files[0])});document.addEventListener('dragover',e=>e.preventDefault());document.addEventListener('drop',e=>e.preventDefault());
$$('[data-style]').forEach(b=>b.addEventListener('click',()=>{state.style=b.dataset.style;$$('[data-style]').forEach(e=>{e.classList.toggle('active',e===b);e.setAttribute('aria-pressed',e===b)})}));$('#size-preset').addEventListener('change',updateOutputSize);
$('#intensity').addEventListener('input',e=>{$('#intensity-number').value=Number(e.target.value).toFixed(2).replace(/0$/,'');paintRanges()});$('#intensity-number').addEventListener('input',e=>{if(e.target.value!==''&&e.target.validity.valid){$('#intensity').value=e.target.value;paintRanges()}});
const advanced=[['local_structure','局部结构',0,2,1],['local_tone','局部色调',0,2,1],['skin','皮肤细节',0,2,1],['global_tone','全局色调',0,2,1],['detail','效果融合',0,2,1],['color','色彩融合',0,1,1]];
for(const [name,label,min,max,val] of advanced){const div=document.createElement('div');div.className='advanced-range';div.innerHTML=`<label for="${name}">${label}<output>${Math.round(val*100)}%</output></label><input id="${name}" name="${name}" type="range" min="${min}" max="${max}" value="${val}" step="0.05">`;div.querySelector('input').addEventListener('input',e=>{div.querySelector('output').textContent=`${Math.round(Number(e.target.value)*100)}%`;paintRanges()});$('#advanced-sliders').append(div)}
function populateSelect(el,entries,selected){el.replaceChildren();entries.forEach(([text,value])=>{const option=document.createElement('option');option.textContent=text;option.value=value;el.append(option)});if(selected&&entries.some(([,v])=>v===selected))el.value=selected}
function updateCodec(){const codec=$('#codec').value;const allowed=options.containers[codec]||['mp4'];populateSelect($('#container'),allowed.map(v=>[v,v]),$('#container').value);$('#prores-field').hidden=codec!=='prores';}
$('#codec').addEventListener('change',updateCodec);$('#quality').addEventListener('change',()=>$('#custom-quality').hidden=$('#quality').value!=='custom');
$('#reset-settings').addEventListener('click',()=>{form.reset();state.style='默认';$('#intensity-number').value=1;$$('[data-style]').forEach(b=>{b.classList.toggle('active',b.dataset.style==='默认');b.setAttribute('aria-pressed',b.dataset.style==='默认')});$$('details').forEach(d=>d.open=false);$('#custom-quality').hidden=true;$$('.advanced-range').forEach(div=>div.querySelector('output').textContent=`${Math.round(Number(div.querySelector('input').value)*100)}%`);updateCodec();paintRanges();updateOutputSize()});
function settings(){const result={asset_id:state.asset.id,size:sizeKey(),style:state.style};for(const el of form.elements){if(!el.name)continue;result[el.name]=el.type==='checkbox'?el.checked:el.type==='number'||el.type==='range'?Number(el.value):el.value}result.size=sizeKey();result.bit_depth=Number(result.bit_depth);if($('#quality').value!=='custom'){result.cq=Number($('#quality').value);result.bitrate=0}return result}
async function poll(id){try{const job=await readResponse(await fetch(`/api/jobs/${id}`));$('#job-log').textContent=job.log||job.message;if(job.status==='queued'||job.status==='running'){status(job.message,'running');setTimeout(()=>poll(id),1200);return}state.busy=false;if(job.status==='done'){state.output=job;paintHardware(job.hardware);status('处理完成','done');if(state.mode==='image'&&job.preview)$('#result-image').src=job.preview;refreshControls();view(job.preview?'result':'original');showNotice(job.preview?'处理完成，可以切换预览或下载结果。':'处理完成，此格式无法直接预览，请下载结果。')}else{state.output=null;status('处理失败','error');showNotice(job.message,true);refreshControls()}}catch(e){state.busy=false;status('连接中断','error');showNotice('暂时无法获取处理状态，请稍后刷新页面查看。',true);refreshControls()}}
$('#start-button').addEventListener('click',async()=>{if(!state.asset||state.busy||!state.hardware?.ready)return;if(!form.reportValidity()||!$('#intensity-number').reportValidity())return;state.busy=true;state.output=null;view('original');refreshControls();$('#notice').hidden=true;status('准备处理…','running');try{const job=await readResponse(await fetch('/api/jobs',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(settings())}));state.job=job.id;poll(job.id)}catch(e){state.busy=false;showNotice(e.message,true);status('无法开始','error');refreshControls()}});
$('#close-notice').addEventListener('click',()=>$('#notice').hidden=true);$('#status-button').addEventListener('click',()=>$('#log-dialog').showModal());$('#close-log').addEventListener('click',()=>$('#log-dialog').close());$('#log-dialog').addEventListener('click',e=>{if(e.target===$('#log-dialog')){const r=e.target.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)e.target.close()}});
form.addEventListener('submit',e=>e.preventDefault());
async function init(){try{options=await readResponse(await fetch('/api/options'));paintHardware(options.hardware);populateSelect($('#codec'),Object.entries(options.codecs));if(!options.nvenc_available){const select=$('#codec');for(const option of select.options){if(option.value.endsWith('_nvenc')){option.disabled=true;option.textContent+='（当前不可用）'}option.defaultSelected=option.value==='prores'}select.value='prores';const hint=document.createElement('p');hint.className='helper';hint.textContent='当前驱动不支持显卡编码，默认使用 ProRes（CPU）。更新驱动后可重新启用。';select.parentElement.after(hint)}updateCodec()}catch(e){showNotice('无法连接处理服务，请刷新页面重试。',true)}paintRanges();showAsset();}
init();

function paintHardware(hardware){
  if(!hardware){
    state.hardware={ready:false};
    $('#gpu-name').textContent='处理服务版本不匹配';
    $('#gpu-status').textContent='请使用新版启动器打开页面';
    showNotice('当前页面连接的是旧版处理服务，无法识别显卡。请关闭此旧页面，通过 DLSS Studio 启动器打开当前服务，再上传素材。',true);
    refreshControls();
    return;
  }
  state.hardware=hardware;
  const name=$('#gpu-name'), status=$('#gpu-status');
  name.textContent=hardware.gpu_name||'未找到可用显卡';name.title=name.textContent;
  const series={rtx30:'30 系组件',rtx40:'40 系组件',rtx50:'50 系组件'}[hardware.profile];
  status.textContent=series?`${series} · ${hardware.status==='verified'?'已成功渲染':'已匹配'}`:'无法自动适配';
  status.title=hardware.message||'';
  if(!hardware.ready)showNotice(hardware.message,true);
  refreshControls();
}
