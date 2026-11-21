/* 首页和历史对话共用的手动帮助。 */
(function(){
  'use strict';
  if (typeof window === 'undefined') return;

  var REDUCED = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;

  function pick(selectors){
    var list = Array.isArray(selectors) ? selectors : [selectors];
    for (var i = 0; i < list.length; i++){
      var el = document.querySelector(list[i]);
      if (el){
        var rect = el.getBoundingClientRect();
        if (rect.width && rect.height && rect.right > 0 && rect.left < window.innerWidth) return el;
      }
    }
    return null;
  }

  var STEPS_ALL = [
    {
      target: '#learn-query', place: 'bottom', title: '先找教程',
      body: '输入创作目标、插件名或参数，查找相关教程。'
    },
    {
      target: '#thread ~ .ask textarea', place: 'top', title: '继续追问',
      body: '在这里接着上面的对话提问。'
    },
    {
      target: ['.learn-shortcuts a[href="/manual"]', '.side .mnav'],
      place: 'right',
      title: '手册 · 系统学',
      body: '按主题分章节：渲染原理 → 材质调参 → Mod 工程 → 教程案例。想系统学，从这里进。'
    },
    {
      target: ['.learn-shortcuts a[href="/params"]', 'a.slink[href="/params"]'],
      place: 'right',
      title: '参数索引 · 查值',
      body: '3,554 个 shader 参数全在这里，可搜。132 个默认值分歧用徽标标出来——避免你抄错值。'
    },
    {
      target: ['.learn-shortcuts a[href="/gallery"]', 'a.slink[href="/gallery"]'],
      place: 'right',
      title: '画廊 · 看实例',
      body: '4,122 张教程截图，带 OCR 搜索。看别人怎么做出那个效果。'
    },
    {
      target: ['.msg .fbv', '.msg .vote', '.msg .fbx'],
      place: 'top',
      title: '告诉它答得准不准',
      body: '觉得这条引用有用 / 没用，点一下——系统会学习，下次给你更准的。'
    },
    {
      target: ['#ob-help'],
      place: 'top',
      title: '随时回来',
      body: '按右下角的「?」浮按钮，可以重新打开这个引导。'
    }
  ];

  var state = {
    steps: [], idx: 0,
    root: null, bubble: null, mask: null, halo: null,
    keyHandler: null, resizeHandler: null, prevFocus: null
  };

  function build(){
    var out = [];
    for (var i = 0; i < STEPS_ALL.length; i++){
      var s = STEPS_ALL[i];
      if (pick(s.target)) out.push(s);
    }
    return out;
  }

  function start(){
    if (state.root) return; // 已经开着
    var steps = build();
    if (!steps.length) return;
    state.steps = steps;
    state.idx = 0;
    state.prevFocus = document.activeElement;

    document.documentElement.classList.add('ob-active');

    var root = document.createElement('div');
    root.className = 'ob-root';
    var mask = document.createElement('div');
    mask.className = 'ob-mask';
    // 遮罩吞点击：防误触关闭，必须按按钮
    mask.addEventListener('click', function(e){ e.stopPropagation(); });
    var halo = document.createElement('div');
    halo.className = 'ob-halo';
    root.appendChild(mask);
    root.appendChild(halo);
    document.body.appendChild(root);
    state.root = root; state.mask = mask; state.halo = halo;

    state.keyHandler = function(e){
      if (e.key === 'Escape'){ e.preventDefault(); finish(); }
      else if (e.key === 'ArrowRight'){ e.preventDefault(); next(); }
      else if (e.key === 'ArrowLeft'){ e.preventDefault(); prev(); }
      else if (e.key === 'Tab'){ trapFocus(e); }
    };
    document.addEventListener('keydown', state.keyHandler);

    state.resizeHandler = function(){ render(); };
    window.addEventListener('resize', state.resizeHandler);
    window.addEventListener('scroll', state.resizeHandler, true);

    render();
  }

  function trapFocus(e){
    if (!state.bubble) return;
    var btns = state.bubble.querySelectorAll('button:not(:disabled)');
    if (!btns.length) return;
    var first = btns[0], last = btns[btns.length - 1];
    if (e.shiftKey && document.activeElement === first){
      e.preventDefault(); last.focus();
    } else if (!e.shiftKey && document.activeElement === last){
      e.preventDefault(); first.focus();
    }
  }

  function render(){
    if (!state.steps.length) return;
    var step = state.steps[state.idx];
    var target = pick(step.target);
    if (state.bubble){ state.bubble.remove(); state.bubble = null; }
    if (target){
      try { target.scrollIntoView({ behavior: REDUCED ? 'auto' : 'smooth', block: 'center' }); } catch(e){}
    }
    // 等滚动稳一帧再绘，避免气泡定位抖
    (window.requestAnimationFrame || function(cb){ setTimeout(cb, 16); })(function(){ paint(step, target); });
  }

  function paint(step, target){
    var halo = state.halo;
    if (target){
      var r = target.getBoundingClientRect();
      halo.style.display = 'block';
      halo.style.left = (r.left - 6) + 'px';
      halo.style.top = (r.top - 6) + 'px';
      halo.style.width = (r.width + 12) + 'px';
      halo.style.height = (r.height + 12) + 'px';
    } else {
      halo.style.display = 'none';
    }

    var b = document.createElement('div');
    b.className = 'ob-bubble';
    b.setAttribute('role', 'dialog');
    b.setAttribute('aria-modal', 'true');
    var titleId = 'ob-title-' + state.idx;
    b.setAttribute('aria-labelledby', titleId);
    b.setAttribute('tabindex', '-1');
    var total = state.steps.length;
    var isLast = state.idx === total - 1;
    b.innerHTML =
      '<div class="ob-arrow" aria-hidden="true"></div>' +
      '<div class="ob-head">' +
        '<span class="ob-step">' + (state.idx + 1) + ' / ' + total + '</span>' +
        '<button type="button" class="ob-skip">' + (isLast ? '关闭' : '跳过') + '</button>' +
      '</div>' +
      '<h3 class="ob-title" id="' + titleId + '">' + esc(step.title) + '</h3>' +
      '<p class="ob-body">' + esc(step.body) + '</p>' +
      '<div class="ob-foot">' +
        '<button type="button" class="ob-prev"' + (state.idx === 0 ? ' disabled' : '') + '>上一步</button>' +
        '<button type="button" class="ob-next">' + (isLast ? '完成' : '下一步') + '</button>' +
      '</div>';
    state.root.appendChild(b);
    state.bubble = b;
    b.querySelector('.ob-skip').addEventListener('click', finish);
    b.querySelector('.ob-prev').addEventListener('click', prev);
    b.querySelector('.ob-next').addEventListener('click', next);

    position(b, target, step.place);
    var nextBtn = b.querySelector('.ob-next') || b;
    try { nextBtn.focus(); } catch(e){}
  }

  function position(b, target, prefer){
    var vw = window.innerWidth, vh = window.innerHeight;
    var bw = b.offsetWidth, bh = b.offsetHeight;
    var gap = 14;

    if (!target){
      b.dataset.place = 'center';
      b.style.left = Math.round((vw - bw) / 2) + 'px';
      b.style.top = Math.round((vh - bh) / 2) + 'px';
      return;
    }
    var r = target.getBoundingClientRect();

    var order;
    if (prefer === 'top') order = ['top','bottom','right','left'];
    else if (prefer === 'left') order = ['left','right','bottom','top'];
    else if (prefer === 'right') order = ['right','left','bottom','top'];
    else order = ['bottom','top','right','left'];

    var pos = null;
    for (var i = 0; i < order.length; i++){
      var q = calc(order[i], r, bw, bh, gap, vw, vh);
      if (q.fits){ pos = q; break; }
    }
    if (!pos) pos = calc(order[0], r, bw, bh, gap, vw, vh);

    b.dataset.place = pos.place;
    b.style.left = Math.round(pos.left) + 'px';
    b.style.top = Math.round(pos.top) + 'px';

    var arrow = b.querySelector('.ob-arrow');
    if (arrow){
      if (pos.place === 'bottom' || pos.place === 'top'){
        arrow.style.left = Math.round(pos.arrowX || bw/2) + 'px';
        arrow.style.top = '';
      } else {
        arrow.style.top = Math.round(pos.arrowY || bh/2) + 'px';
        arrow.style.left = '';
      }
    }
  }

  function calc(place, r, bw, bh, gap, vw, vh){
    var cx = r.left + r.width/2, cy = r.top + r.height/2;
    var pad = 10;
    var left, top, arrowX, arrowY, fits;
    if (place === 'bottom'){
      top = r.bottom + gap; left = cx - bw/2;
      fits = top + bh <= vh - pad;
      left = Math.max(pad, Math.min(vw - bw - pad, left));
      arrowX = cx - left;
      return { place: place, left: left, top: top, arrowX: arrowX, fits: fits };
    }
    if (place === 'top'){
      top = r.top - bh - gap; left = cx - bw/2;
      fits = top >= pad;
      left = Math.max(pad, Math.min(vw - bw - pad, left));
      arrowX = cx - left;
      return { place: place, left: left, top: top, arrowX: arrowX, fits: fits };
    }
    if (place === 'right'){
      left = r.right + gap; top = cy - bh/2;
      fits = left + bw <= vw - pad;
      top = Math.max(pad, Math.min(vh - bh - pad, top));
      arrowY = cy - top;
      return { place: place, left: left, top: top, arrowY: arrowY, fits: fits };
    }
    left = r.left - bw - gap; top = cy - bh/2;
    fits = left >= pad;
    top = Math.max(pad, Math.min(vh - bh - pad, top));
    arrowY = cy - top;
    return { place: 'left', left: left, top: top, arrowY: arrowY, fits: fits };
  }

  function next(){
    if (state.idx >= state.steps.length - 1){ finish(); return; }
    state.idx++; render();
  }
  function prev(){
    if (state.idx === 0) return;
    state.idx--; render();
  }
  function finish(){
    if (state.keyHandler) document.removeEventListener('keydown', state.keyHandler);
    if (state.resizeHandler){
      window.removeEventListener('resize', state.resizeHandler);
      window.removeEventListener('scroll', state.resizeHandler, true);
    }
    if (state.root && state.root.parentNode) state.root.parentNode.removeChild(state.root);
    state.root = state.bubble = state.mask = state.halo = null;
    state.keyHandler = state.resizeHandler = null;
    document.documentElement.classList.remove('ob-active');
    if (state.prevFocus && state.prevFocus.focus){
      try { state.prevFocus.focus(); } catch(e){}
    }
  }
  function esc(s){
    return String(s).replace(/[&<>"']/g, function(ch){
      return { '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[ch];
    });
  }

  function wireHelp(){
    var btn = document.getElementById('ob-help');
    if (!btn) return;
    btn.addEventListener('click', start);
  }

  if (document.readyState === 'loading'){
    document.addEventListener('DOMContentLoaded', wireHelp);
  } else {
    wireHelp();
  }

  window.KBOnboard = { start: start, finish: finish };
})();
