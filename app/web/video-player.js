/* One source clock drives both videos. Frame previews never replace the source media. */
window.StudioVideoPlayer = class StudioVideoPlayer {
  constructor({onChange,onFrameCleared,onError}) {
    this.source=document.querySelector('#video-preview');
    this.result=document.querySelector('#video-result');
    this.stage=document.querySelector('#video-stage');
    this.picture=document.querySelector('#video-picture');
    this.viewport=document.querySelector('#video-viewport');
    this.frameOriginal=document.querySelector('#video-frame-original');
    this.frameResult=document.querySelector('#video-frame-result');
    this.slider=document.querySelector('#video-compare-slider');
    this.line=document.querySelector('#video-compare-line');
    this.timeline=document.querySelector('#video-timeline');
    this.playButton=document.querySelector('#video-play');
    this.muteButton=document.querySelector('#video-mute');
    this.loading=document.querySelector('#video-loading');
    this.timeLabel=document.querySelector('#video-time');
    this.playIcon=this.playButton.querySelector('img');
    this.transportCache={};
    this.onChange=onChange;this.onFrameCleared=onFrameCleared;this.onError=onError;
    this.view='original';this.frame=null;this.locked=false;this.assetId=null;this.resultUrl=null;
    this.resultFailed=false;this.sourceFailed=false;this.wantPlaying=false;this.buffering=false;this.raf=0;
    this.result.muted=true;
    this.playButton.addEventListener('click',()=>this.source.paused?this.play():this.pause());
    this.timeline.addEventListener('input',()=>this.seek(Number(this.timeline.value)));
    this.muteButton.addEventListener('click',()=>{this.source.muted=!this.source.muted;this.paintTransport()});
    this.slider.addEventListener('input',()=>this.paintView());
    for(const event of ['loadedmetadata','loadeddata','canplay','durationchange']){
      this.source.addEventListener(event,()=>{this.fit();this.paint();this.resumeBuffered();this.onChange()});
      this.result.addEventListener(event,()=>{this.sync(this.source.paused);this.paint();this.resumeBuffered();this.onChange()});
    }
    this.source.addEventListener('play',()=>{
      if(this.locked){this.pause();return}
      this.clearFrame();this.wantPlaying=true;this.sync(true);this.tick();this.paintTransport();
    });
    this.source.addEventListener('playing',()=>{this.buffering=false;this.sync(true);this.paint()});
    this.source.addEventListener('pause',()=>{this.result.pause();cancelAnimationFrame(this.raf);this.raf=0;this.sync(true);this.paintTransport()});
    this.source.addEventListener('timeupdate',()=>{this.checkEnd();this.sync();this.paintTransport()});
    this.source.addEventListener('seeking',()=>{this.clearFrame();this.sync(true);this.paintTransport();this.onChange()});
    this.source.addEventListener('seeked',()=>{this.sync(true);this.paint();this.onChange()});
    this.result.addEventListener('seeked',()=>{this.sync(this.source.paused);this.paintView()});
    this.source.addEventListener('ratechange',()=>this.sync(true));
    this.source.addEventListener('volumechange',()=>this.paintTransport());
    this.source.addEventListener('ended',()=>this.pause());
    for(const video of [this.source,this.result])video.addEventListener('waiting',()=>{
      if(this.wantPlaying&&this.usingResult()){
        this.buffering=true;this.source.pause();this.result.pause();this.paint();
      }
    });
    this.source.addEventListener('error',()=>{
      if(!this.assetId)return;
      this.sourceFailed=true;this.pause();this.paint();this.onChange();
      this.onError('这个视频暂时无法在浏览器中播放，请使用 H.264 MP4 或 WebM 视频。');
    });
    this.result.addEventListener('error',()=>{
      if(!this.resultUrl)return;
      this.resultFailed=true;this.pause();this.paint();this.onChange();
      this.onError('增强结果的预览加载失败，可以下载视频后查看。');
    });
    for(const image of [this.frameOriginal,this.frameResult]){
      image.addEventListener('load',()=>{this.paint();this.onChange()});
      image.addEventListener('error',()=>{if(this.frame){this.onError('单帧预览加载失败，请重试或下载此帧。');this.clearFrame()}});
    }
    new ResizeObserver(()=>this.fit()).observe(this.picture);
  }
  static formatTime(value){
    value=Number.isFinite(value)?Math.max(0,value):0;
    const ms=Math.floor(value*1000),seconds=Math.floor(ms/1000),minutes=Math.floor(seconds/60);
    return `${String(minutes).padStart(2,'0')}:${String(seconds%60).padStart(2,'0')}.${String(ms%1000).padStart(3,'0')}`;
  }
  setSource(asset){
    if(this.assetId===asset?.id)return;
    this.pause();this.assetId=asset?.id||null;this.sourceFailed=false;
    this.clearFrame(false);this.setResult(null);
    if(asset)this.source.src=asset.url;else this.source.removeAttribute('src');
    this.source.load();this.paint();
  }
  setVisible(visible){this.stage.hidden=!visible;if(!visible)this.pause();else this.fit()}
  setResult(url){
    if(this.resultUrl===url)return;
    this.result.pause();this.resultUrl=url||null;this.resultFailed=false;
    if(url)this.result.src=url;else this.result.removeAttribute('src');
    this.result.load();this.paint();
  }
  setFrame(frame){
    if(this.frame?.job.id===frame?.job.id)return;
    this.pause();this.frame=frame;
    if(frame){this.frameOriginal.src=frame.source;this.frameResult.src=frame.job.preview}
    else{this.frameOriginal.removeAttribute('src');this.frameResult.removeAttribute('src')}
    this.paint();
  }
  clearFrame(notify=true){
    if(!this.frame)return;
    this.frame=null;this.frameOriginal.removeAttribute('src');this.frameResult.removeAttribute('src');
    if(notify)this.onFrameCleared();
    this.paint();
  }
  canCapture(){return !!this.assetId&&!this.sourceFailed&&this.source.readyState>=2&&!this.source.seeking&&this.source.videoWidth>0}
  canCompare(){return this.frame?this.frameOriginal.complete&&this.frameOriginal.naturalWidth>0&&this.frameResult.complete&&this.frameResult.naturalWidth>0:!!this.resultUrl&&!this.resultFailed&&this.result.readyState>=2&&this.source.readyState>=2}
  usingResult(){return !this.frame&&this.view!=='original'&&!!this.resultUrl&&!this.resultFailed}
  duration(){
    let duration=Number.isFinite(this.source.duration)?this.source.duration:0;
    if(this.usingResult()&&Number.isFinite(this.result.duration))duration=Math.min(duration,this.result.duration);
    return duration;
  }
  setLocked(locked){this.locked=locked;this.paintTransport()}
  setView(value){
    this.view=value;
    const end=this.duration();
    if(end>0&&this.source.currentTime>end)this.seek(Math.max(0,end-.001));
    this.sync(true);this.paint();
  }
  async play(){
    if(this.locked||!this.canCapture())return;
    this.clearFrame();this.wantPlaying=true;this.buffering=false;
    if(this.source.ended||this.source.currentTime>=this.duration()-.01)this.source.currentTime=0;
    try{await this.source.play();this.sync(true)}
    catch(error){this.wantPlaying=false;if(error.name!=='AbortError')this.onError('视频暂时无法播放，请稍后重试。')}
    this.paintTransport();
  }
  pause(){this.wantPlaying=false;this.buffering=false;this.source.pause();this.result.pause();this.paintTransport()}
  seek(time){
    if(this.locked||!Number.isFinite(time)||!this.assetId)return;
    this.pause();this.clearFrame();
    const duration=this.duration();
    this.source.currentTime=Math.max(0,Math.min(time,Math.max(0,duration-.0001)));
    this.sync(true);this.paintTransport();
  }
  sync(force=false){
    if(!this.usingResult()||this.result.readyState<1){this.result.pause();return}
    const target=Math.max(0,Math.min(this.source.currentTime,Math.max(0,this.result.duration-.0001)));
    const drift=target-this.result.currentTime;
    if(!this.result.seeking&&(Math.abs(drift)>(force ? .002 : .10)))this.result.currentTime=target;
    this.result.muted=true;
    this.result.playbackRate=this.source.playbackRate*(Math.abs(drift)>.025&&Math.abs(drift)<.10?(drift>0?1.03:.97):1);
    if(this.source.paused||this.buffering)this.result.pause();
    else if(this.result.paused)this.result.play().catch(error=>{if(error.name!=='AbortError'){this.pause();this.onError('视频对比播放暂时不可用，请暂停后拖动时间轴查看。')}});
  }
  resumeBuffered(){
    if(this.buffering&&this.wantPlaying&&this.source.readyState>=3&&this.result.readyState>=3){
      this.buffering=false;this.source.play().then(()=>this.sync(true)).catch(()=>this.pause());
    }
  }
  checkEnd(){if(this.usingResult()&&!this.source.paused&&this.source.currentTime>=this.duration()-.005)this.pause()}
  tick(){
    cancelAnimationFrame(this.raf);
    if(this.source.paused){this.raf=0;return}
    this.checkEnd();this.sync();this.paintTransport();
    this.raf=requestAnimationFrame(()=>this.tick());
  }
  fit(){
    const ratio=this.source.videoWidth/this.source.videoHeight;
    if(!Number.isFinite(ratio)||ratio<=0)return;
    const width=Math.min(this.picture.clientWidth,this.picture.clientHeight*ratio);
    this.viewport.style.width=`${width}px`;this.viewport.style.height=`${width/ratio}px`;
    this.onGeometry?.();
  }
  paintView(){
    const frame=!!this.frame,compare=this.view==='compare'&&this.canCompare(),enhanced=this.view!=='original';
    this.result.hidden=frame||!enhanced||!this.resultUrl||this.resultFailed;
    this.frameOriginal.hidden=!frame;
    this.frameResult.hidden=!frame||!enhanced;
    const clip=compare?`inset(0 0 0 ${this.slider.value}%)`:'none';
    this.result.style.clipPath=clip;this.frameResult.style.clipPath=clip;
    this.slider.hidden=!compare;this.line.hidden=!compare;this.line.style.left=`${this.slider.value}%`;
    const sourceLabel=document.querySelector('#video-source-label'),resultLabel=document.querySelector('#video-result-label');
    sourceLabel.hidden=enhanced&&!compare;sourceLabel.textContent=frame?`当前帧 · ${StudioVideoPlayer.formatTime(this.frame.time)}`:'原视频';
    resultLabel.hidden=!enhanced;resultLabel.textContent=frame?'单帧增强':'增强后';
    let loading='';
    if(this.assetId&&!this.sourceFailed&&this.source.readyState<2)loading='正在加载视频…';
    else if(this.buffering)loading='正在同步视频…';
    else if(enhanced&&(frame||this.resultUrl)&&!this.canCompare()&&!this.resultFailed)loading='正在加载增强预览…';
    this.loading.textContent=loading;this.loading.hidden=!loading;
  }
  paintTransport(){
    const duration=this.duration(),time=Math.min(this.source.currentTime||0,duration),paused=this.source.paused;
    const cache=this.transportCache;
    if(cache.duration!==duration){this.timeline.max=String(duration);cache.duration=duration}
    this.timeline.disabled=this.locked||!duration;this.playButton.disabled=this.locked||!this.canCapture();
    // Synchronization remains per frame; text and timeline paint at most 20 times/s.
    const bucket=Math.floor(time*20);
    if(paused||cache.bucket!==bucket){
      cache.bucket=bucket;this.timeline.value=String(time);
      this.timeline.style.setProperty('--range',`${duration?time/duration*100:0}%`);
      const stamp=StudioVideoPlayer.formatTime(time),label=`${stamp} / ${StudioVideoPlayer.formatTime(duration)}`;
      if(this.timeLabel.textContent!==label)this.timeLabel.textContent=label;
      if(cache.stamp!==stamp){this.timeline.setAttribute('aria-valuetext',stamp);cache.stamp=stamp}
    }
    if(cache.paused!==paused){
      cache.paused=paused;this.playButton.title=paused?'播放视频':'暂停视频';
      this.playButton.setAttribute('aria-label',this.playButton.title);this.playIcon.src=`/assets/icons/${paused?'play':'pause'}.svg`;
    }
    if(cache.muted!==this.source.muted){
      cache.muted=this.source.muted;this.muteButton.textContent=this.source.muted?'取消静音':'静音';this.muteButton.title=this.muteButton.textContent;
      this.muteButton.setAttribute('aria-pressed',String(this.source.muted));
    }
  }
  paint(){this.paintView();this.paintTransport();this.fit()}
  async captureFrame(){
    this.pause();
    if(this.source.seeking)await new Promise((resolve,reject)=>{
      const timeout=setTimeout(()=>finish(new Error('跳转尚未完成，请稍后重试。')),10000);
      const done=()=>finish(),fail=()=>finish(new Error('无法读取当前视频帧。'));
      const finish=error=>{clearTimeout(timeout);this.source.removeEventListener('seeked',done);this.source.removeEventListener('error',fail);error?reject(error):resolve()};
      this.source.addEventListener('seeked',done,{once:true});this.source.addEventListener('error',fail,{once:true});
    });
    if(!this.canCapture())throw new Error('当前视频帧还未准备好，请稍后重试。');
    await new Promise(requestAnimationFrame);
    const canvas=document.createElement('canvas');canvas.width=this.source.videoWidth;canvas.height=this.source.videoHeight;
    canvas.getContext('2d').drawImage(this.source,0,0);
    const time=this.source.currentTime;
    const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));
    if(!blob)throw new Error('无法读取当前视频帧，请换一个时间点重试。');
    return {blob,time,width:canvas.width,height:canvas.height};
  }
};
