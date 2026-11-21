/* Progressive reading enhancement. No source edits or automatic AI submissions. */
(() => {
  const root=document.querySelector('[data-reader]');
  const scope=document.body.dataset.readerScope||'local';
  const recentKey='kks:reading:v1:'+scope;
  const read=(k,d)=>{try{return JSON.parse(localStorage.getItem(k))??d;}catch{return d;}};
  const write=(k,d)=>{try{localStorage.setItem(k,JSON.stringify(d));}catch{}};
  let anchorMap={};try{anchorMap=JSON.parse(root?.dataset.anchorMap||'{}')||{};}catch{}
  const resolveAnchor=id=>anchorMap[id]||id;
  if(root?.dataset.semanticReader==='true'&&location.hash){const old=decodeURIComponent(location.hash.slice(1)),mapped=resolveAnchor(old);if(mapped!==old)history.replaceState(history.state,'',location.pathname+location.search+'#'+encodeURIComponent(mapped));}
  if(!root){
    const resume=document.getElementById('learn-resume'),last=read(recentKey,null);
    if(resume&&last&&typeof last.path==='string'&&typeof last.title==='string'){
      const a=document.getElementById('learn-resume-link');a.href='/doc?path='+encodeURIComponent(last.path)+(last.anchor?'#'+encodeURIComponent(last.anchor):'');a.textContent=last.title;resume.hidden=false;
    }
    document.querySelectorAll('[data-open-assistant]').forEach(a=>a.addEventListener('click',()=>{const d=document.getElementById('ask-entry');if(d)d.open=true;}));
    const params=new URLSearchParams(location.search);
    if(params.has('q')||params.has('ask')){const d=document.getElementById('ask-entry'),q=document.getElementById('q');if(d)d.open=true;if(q&&params.has('q'))q.value=params.get('q');if(d)requestAnimationFrame(()=>d.scrollIntoView({block:'start'}));}
    return;
  }
  const content=document.getElementById('doc');
  const size=document.getElementById('reader-size');
  let fontSize=read('kks:font:v1',16);if(![16,18,20].includes(fontSize))fontSize=16;
  const setSize=()=>{root.style.setProperty('--reader-size',fontSize+'px');size.textContent='字号 '+fontSize;write('kks:font:v1',fontSize);};
  size.addEventListener('click',()=>{fontSize=fontSize===20?16:fontSize+2;setSize();});setSize();
  const focus=document.getElementById('reader-focus');
  focus.addEventListener('click',()=>{const on=document.body.classList.toggle('is-focus');focus.setAttribute('aria-pressed',String(on));focus.textContent=on?'退出专注':'专注阅读';});
  const headings=[...content.querySelectorAll('h2[id],h3[id]')];
  const recent={path:root.dataset.path,title:root.dataset.title,anchor:resolveAnchor(location.hash.slice(1))};write(recentKey,recent);
  if('IntersectionObserver' in window){const observer=new IntersectionObserver(entries=>{const v=entries.filter(e=>e.isIntersecting).sort((a,b)=>a.boundingClientRect.top-b.boundingClientRect.top);if(!v.length)return;const id=v[0].target.id;document.querySelectorAll('.reader-outline a').forEach(a=>a.setAttribute('aria-current',String(a.hash.slice(1)===id)));recent.anchor=id;write(recentKey,recent);},{rootMargin:'-120px 0px -65% 0px'});headings.forEach(h=>observer.observe(h));}
  document.getElementById('reader-ai').addEventListener('click',e=>{const selected=getSelection().toString().trim().slice(0,800),section=headings.find(h=>h.id===recent.anchor);const q='我在阅读《'+root.dataset.title+'》'+(section?'的“'+section.textContent+'”小节':'')+'。请结合适用版本解释此处，区分原文证据和建议；资料未给出数值时不要补造参数。资料路径：'+root.dataset.path+(selected?'\n选中内容：'+selected:'');e.currentTarget.href='/?q='+encodeURIComponent(q)+'#ask-entry';});
  content.querySelectorAll('table').forEach(table=>{const wrapper=document.createElement('div');wrapper.className='reader-table-scroll';wrapper.tabIndex=0;wrapper.setAttribute('role','region');wrapper.setAttribute('aria-label','可横向滚动的参数表');table.before(wrapper);wrapper.append(table);});
  const images=[...document.querySelectorAll('#doc img,#doc-images img,.albumgrid img')];
  const dialog=document.getElementById('reader-image-dialog'),full=document.getElementById('reader-full-image'),stage=document.getElementById('reader-image-stage'),label=document.getElementById('reader-image-label'),caption=document.getElementById('reader-image-caption'),original=document.getElementById('reader-original-link'),zoomLabel=document.getElementById('reader-zoom-label');
  let index=0,zoom=1,panX=0,panY=0,opener=null,pinch=null;
  const pointers=new Map();
  const paint=()=>{full.style.transform='translate('+panX+'px,'+panY+'px) scale('+zoom+')';zoomLabel.textContent=Math.round(zoom*100)+'%';stage.classList.toggle('is-zoomed',zoom>1);};
  const reset=()=>{zoom=1;panX=panY=0;paint();};
  const changeZoom=value=>{zoom=Math.max(1,Math.min(8,value));if(zoom===1)panX=panY=0;paint();};
  function show(i){
    if(!images.length)return;index=(i+images.length)%images.length;const img=images[index],link=img.closest('a');
    const candidate=img.dataset.original||(link&&link.getAttribute('href')?.includes('/orig/')?link.href:img.currentSrc||img.src);const url=new URL(candidate,location.href);if(!['http:','https:'].includes(url.protocol))return;
    label.textContent=(index+1)+' / '+images.length+' · 正在加载原图';caption.textContent=img.closest('figure')?.querySelector('figcaption')?.textContent||img.alt||'原文配图';original.href=url.href;full.alt=img.alt||'教程原图';reset();full.src=url.href;
    if(!dialog.open){opener=img;dialog.showModal();}
  }
  full.addEventListener('load',()=>{label.textContent=(index+1)+' / '+images.length+' · '+full.naturalWidth+' × '+full.naturalHeight;});
  full.addEventListener('error',()=>{label.textContent='原图加载失败，可单独打开原图重试';});
  images.forEach((img,i)=>{
    img.tabIndex=0;img.setAttribute('role','button');img.setAttribute('aria-label','放大图片：'+(img.alt||String(i+1)));
    img.addEventListener('click',e=>{e.preventDefault();show(i);});img.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();show(i);}});
    img.addEventListener('error',()=>{if(img.dataset.failed)return;img.dataset.failed='true';const note=document.createElement('p');note.className='reader-missing-image';note.textContent='此处配图暂时无法加载，请核对原件或来源。';img.hidden=true;img.after(note);});
  });
  document.getElementById('reader-image-prev').onclick=()=>show(index-1);document.getElementById('reader-image-next').onclick=()=>show(index+1);document.getElementById('reader-image-close').onclick=()=>dialog.close();
  document.getElementById('reader-zoom-in').onclick=()=>changeZoom(zoom*1.3);document.getElementById('reader-zoom-out').onclick=()=>changeZoom(zoom/1.3);document.getElementById('reader-zoom-fit').onclick=reset;
  dialog.addEventListener('close',()=>{pointers.clear();pinch=null;opener?.focus({preventScroll:true});});
  dialog.addEventListener('click',e=>{if(e.target===dialog)dialog.close();});
  dialog.addEventListener('keydown',e=>{if(e.key==='ArrowLeft'){e.preventDefault();show(index-1);}if(e.key==='ArrowRight'){e.preventDefault();show(index+1);}if(e.key==='+'){e.preventDefault();changeZoom(zoom*1.3);}if(e.key==='-'){e.preventDefault();changeZoom(zoom/1.3);}});
  stage.addEventListener('wheel',e=>{e.preventDefault();changeZoom(zoom*(e.deltaY<0?1.12:1/1.12));},{passive:false});
  stage.addEventListener('dblclick',()=>changeZoom(zoom>1?1:2.5));full.draggable=false;
  stage.addEventListener('pointerdown',e=>{if(e.button>0)return;pointers.set(e.pointerId,{x:e.clientX,y:e.clientY});stage.setPointerCapture(e.pointerId);if(pointers.size===2){const[a,b]=[...pointers.values()];pinch={distance:Math.hypot(a.x-b.x,a.y-b.y),zoom};}});
  stage.addEventListener('pointermove',e=>{const old=pointers.get(e.pointerId);if(!old)return;pointers.set(e.pointerId,{x:e.clientX,y:e.clientY});if(pointers.size===2&&pinch){const[a,b]=[...pointers.values()];if(pinch.distance>0)changeZoom(pinch.zoom*Math.hypot(a.x-b.x,a.y-b.y)/pinch.distance);}else if(zoom>1){panX+=e.clientX-old.x;panY+=e.clientY-old.y;paint();}});
  const release=e=>{pointers.delete(e.pointerId);if(pointers.size<2)pinch=null;};stage.addEventListener('pointerup',release);stage.addEventListener('pointercancel',release);
})();
