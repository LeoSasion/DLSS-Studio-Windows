/* Zoom both halves as one surface so comparison always stays aligned. */
window.StudioZoom = class StudioZoom {
  constructor(getMedia) {
    this.getMedia=getMedia;this.factor=null;this.x=0;this.y=0;this.pan=false;this.space=false;
    this.buttons=['zoom-fit','zoom-native','zoom-in','zoom-out','zoom-pan'].map(id=>document.getElementById(id));
    this.label=document.getElementById('zoom-level');
    this.buttons[0].onclick=()=>this.reset();
    this.buttons[1].onclick=()=>this.set(1);
    this.buttons[2].onclick=()=>this.step(1);
    this.buttons[3].onclick=()=>this.step(-1);
    this.buttons[4].onclick=()=>{this.pan=!this.pan;this.fit()};
    for(const area of [document.querySelector('#canvas'),document.querySelector('#video-picture')]){
      area.tabIndex=0;area.setAttribute('aria-label','预览画面；滚轮缩放，按住空格拖动，方向键平移，Escape 恢复适应');
      area.addEventListener('wheel',event=>{
        const media=this.getMedia();
        if(!media||media.area!==area)return;
        event.preventDefault();event.stopPropagation();this.step(event.deltaY<0?1:-1);
      },{passive:false});
      area.addEventListener('pointerdown',event=>{
        const media=this.getMedia();
        if(!media||media.area!==area||event.target.closest('.video-transport')||!(this.pan||this.space||event.button===1))return;
        if(event.button!==0&&event.button!==1)return;
        event.preventDefault();event.stopPropagation();area.setPointerCapture(event.pointerId);
        this.drag={id:event.pointerId,x:event.clientX,y:event.clientY,oldX:this.x,oldY:this.y};
      },true);
      area.addEventListener('pointermove',event=>{
        if(!this.drag||this.drag.id!==event.pointerId)return;
        this.x=this.drag.oldX+event.clientX-this.drag.x;this.y=this.drag.oldY+event.clientY-this.drag.y;this.fit();
      });
      for(const name of ['pointerup','pointercancel','lostpointercapture'])area.addEventListener(name,()=>this.drag=null);
      area.addEventListener('keydown',event=>{
        if(event.target!==area)return;
        const keys={ArrowLeft:[40,0],ArrowRight:[-40,0],ArrowUp:[0,40],ArrowDown:[0,-40]};
        if(event.key in keys){event.preventDefault();const [x,y]=keys[event.key];this.x+=x;this.y+=y;this.fit()}
        if(event.key==='Escape'){event.preventDefault();this.reset()}
        if(event.key==='+'||event.key==='='){event.preventDefault();this.step(1)}
        if(event.key==='-'){event.preventDefault();this.step(-1)}
      });
    }
    document.addEventListener('keydown',event=>{if(event.code==='Space'&&!event.target.closest('input,select,button,a,summary,dialog')){this.space=true;event.preventDefault()}});
    document.addEventListener('keyup',event=>{if(event.code==='Space')this.space=false});
    window.addEventListener('blur',()=>{this.space=false;this.drag=null});
    for(const id of ['source-image','result-image','video-frame-result'])document.getElementById(id).addEventListener('load',()=>this.fit());
  }
  reset(){this.factor=null;this.x=this.y=0;this.pan=false;this.fit()}
  set(factor){this.factor=Math.max(.05,Math.min(8,factor));this.x=this.y=0;this.fit()}
  step(direction){const m=this.getMedia();if(m)this.set((this.factor??parseFloat(getComputedStyle(m.stage).width)/m.width)*Math.pow(1.25,direction))}
  fit(){
    const m=this.getMedia();
    this.buttons.forEach(button=>button.disabled=!m);
    if(!m){this.label.textContent='适应';return}
    // clientWidth/clientHeight round fractional fitted sizes, which magnifies the
    // rounding error at native zoom on a tall 4K preview.
    const style=getComputedStyle(m.stage),width=parseFloat(style.width)||m.stage.clientWidth,height=parseFloat(style.height)||m.stage.clientHeight;
    this.buttons[1].title=`按当前预览的 ${m.width} 像素宽度显示；视频预览可能小于导出尺寸。`;
    const scale=this.factor===null?1:this.factor*m.width/Math.max(1,width);
    this.x=Math.max(-Math.max(0,(width*scale-m.area.clientWidth)/2),Math.min(Math.max(0,(width*scale-m.area.clientWidth)/2),this.x));
    this.y=Math.max(-Math.max(0,(height*scale-m.area.clientHeight)/2),Math.min(Math.max(0,(height*scale-m.area.clientHeight)/2),this.y));
    m.stage.style.transform=`translate(${this.x}px,${this.y}px) scale(${scale})`;
    m.area.classList.toggle('pan-enabled',this.pan);
    this.label.textContent=this.factor===null?'适应':`${Math.round(this.factor*100)}%`;
    this.buttons[0].setAttribute('aria-pressed',String(this.factor===null));
    this.buttons[1].setAttribute('aria-pressed',String(this.factor===1));
    this.buttons[4].setAttribute('aria-pressed',String(this.pan));
  }
};
