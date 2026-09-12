/* Regression workflow executed only by the application's native smoke-test host. */
(async()=>{
  const test=window.__workbenchTest={done:false,error:null,checks:[]};
  const assert=(value,message)=>{if(!value)throw Error(message)};
  const click=selector=>document.querySelector(selector).click();
  const wait=async(fn,label,timeout=30000)=>{const end=performance.now()+timeout;while(!fn()){if(performance.now()>end)throw Error(label+'; '+document.querySelector('#notice-text').textContent);await new Promise(resolve=>setTimeout(resolve,50))}};
  const originalFetch=window.fetch;
  try{
    await wait(()=>sessionReady,'Session initialization');
    assert(state.mode==='video'&&state.output,'Video fixture must finish first');
    form.elements.codec.value='prores';form.elements.codec.dispatchEvent(new Event('change',{bubbles:true}));
    assert(document.querySelector('#quality-field').hidden&&document.querySelector('#bit-depth-field').hidden&&document.querySelector('#enc-preset-field').hidden,'Ineffective ProRes controls still visible');
    assert(form.elements.cq.disabled&&form.elements.enc_preset.disabled,'Inactive codec controls not disabled');
    form.elements.cq.value=999;form.elements.frames.value=-1;
    click('[data-mode=image]');
    assert(form.reportValidity(),'Hidden video values block image processing');
    form.elements.cq.value=19;form.elements.frames.value=16;
    click('[data-view=result]');await wait(()=>document.querySelector('#result-image').naturalWidth>0,'Image result');
    click('#zoom-native');
    assert(Math.abs(document.querySelector('#image-stage').getBoundingClientRect().width-document.querySelector('#result-image').naturalWidth)<1,'100% is not one image pixel per CSS pixel');
    click('#zoom-in');click('#zoom-pan');
    const canvas=document.querySelector('#canvas');canvas.focus();canvas.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}));
    assert(previewZoom.x>0&&document.querySelector('#image-stage').style.transform.includes('translate'),'Zoomed image cannot pan');
    click('#zoom-fit');assert(previewZoom.factor===null&&previewZoom.x===0,'Fit does not reset pan');
    test.checks.push('codec-specific controls','hidden video validation','native-pixel zoom','keyboard pan and fit');
    const records=await(await originalFetch('/api/jobs?limit=50')).json();
    const savedVideo=sessions.video,oldFull=savedVideo.output.id;
    const frameRecord=records.jobs.find(job=>job.job_kind==='frame'&&job.source_asset_id===savedVideo.asset.id&&job.status==='done');
    assert(frameRecord,'Frame result missing from history');
    await restoreJob(frameRecord);
    assert(state.output?.id===oldFull&&state.framePreview,'Opening frame history lost the saved full video');
    videoPlayer.clearFrame();
    test.checks.push('frame history preserves full video across modes');
    click('[data-mode=video]');
    form.elements.codec.value='prores';form.elements.codec.dispatchEvent(new Event('change',{bubbles:true}));
    form.elements.frames.value=16;
    let failedPoll=false;
    window.fetch=async(url,...args)=>{if(!failedPoll&&/^\/api\/jobs\/[a-f0-9]{32}$/.test(String(url))){failedPoll=true;throw new TypeError('simulated polling disconnect')}return originalFetch(url,...args)};
    click('#start-button');await wait(()=>failedPoll&&document.querySelector('#status-text').textContent==='正在重连…','Polling retry');
    assert(state.busy&&!document.querySelector('#job-progress').hidden,'Polling disconnect unlocked a running job');
    const retryId=state.job;
    await wait(()=>!state.busy&&state.output?.id===retryId,'Polling recovery',120000);
    window.fetch=originalFetch;test.checks.push('transient polling recovery');
    const before=await(await originalFetch('/api/jobs')).json();let lostResponse=false,postCount=0,firstId;
    window.fetch=async(url,options,...rest)=>{
      const response=await originalFetch(url,options,...rest);
      if(url==='/api/jobs'&&options?.method==='POST'){postCount++;if(!lostResponse){lostResponse=true;firstId=(await response.clone().json()).id;throw new TypeError('simulated lost submit response')}}
      return response;
    };
    click('#start-button');await wait(()=>lostResponse,'Lost submit response');
    await wait(()=>postCount>=2&&!state.busy&&state.output?.id===firstId,'Idempotent submission recovery',120000);
    window.fetch=originalFetch;
    const after=await(await originalFetch('/api/jobs')).json();assert(after.total===before.total+1,'Retry created duplicate jobs');
    test.checks.push('idempotent submission recovery');
    click('#history-button');await wait(()=>document.querySelector('.history-item'),'History rows');
    assert(document.querySelector('.history-item a').getAttribute('href')===state.output.download,'Latest result download does not match');
    const response=await originalFetch(document.querySelector('.history-item a').href);assert(response.ok,'History download');
    click('[data-history-tab=cache]');await wait(()=>document.querySelector('#history-message').textContent.includes('占用'),'Cache statistics');
    assert(!document.querySelector('#clear-results').checked,'Completed-result cleanup must be opt-in');
    click('[data-history-tab=recent]');await wait(()=>document.querySelector('.history-item'),'History restored');
    test.checks.push('history result download','cache usage and safe default');
    test.done=true;
  }catch(error){test.error=error.stack||String(error)}finally{window.fetch=originalFetch}
})();
