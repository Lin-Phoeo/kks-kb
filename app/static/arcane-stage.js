/* Original KKS.ARCANE scene; only the MIT renderer/Reflector addon are vendored. */
const PRESETS = {
  silk: { title: '流光织物', description: '光沿着细密的褶皱流动。观察曲面与明暗之间的关系。', mode: 0, color: '#dbe4c7', accent: '#789e82' },
  mineral: { title: '矿物切面', description: '锋利的棱线，缓慢的色变。让光在矿物般的切面上留下轨迹。', mode: 1, color: '#d9a879', accent: '#7c6650' },
  contour: { title: '等高光场', description: '把连续的曲面描成一条条光线，观察流动、梯度与地形。', mode: 2, color: '#9ccad0', accent: '#4a8491' },
  pearl: { title: '珍珠薄膜', description: '温柔的珠光，微妙的干涉色。转动视角，让同一表面显露不同的色调。', mode: 3, color: '#e4d8d5', accent: '#a29cab' }
};

const VERTEX = `
  uniform float uTime;
  uniform float uMode;
  varying vec3 vPosition;
  varying vec3 vLocal;
  varying vec3 vSurfaceNormal;
  varying vec2 vUv;
  float relief(vec2 p) {
    float t = uTime * 0.23;
    float a = sin(p.x * 2.7 + sin(p.y * 1.8 + t) * 1.9 + t);
    float b = cos(p.y * 3.4 - p.x * 1.2 - t * 0.6);
    float folds = sin(p.x * 4.6 + p.y * 2.0 + a * 2.1 + t);
    float silk = a * 0.24 + b * 0.14 + folds * 0.09;
    float mineral = sin(p.x * 4.0 + t) * cos(p.y * 3.8 - t) * 0.27;
    float pearl = sin(length(p * vec2(1.0, 0.8)) * 7.0 - t * 1.6) * 0.13 + a * 0.18;
    if (uMode > 2.5) return pearl;
    if (uMode > 0.5 && uMode < 1.5) return mineral;
    return silk;
  }
  void main() {
    vUv = uv;
    vec3 p = position;
    p.z += relief(position.xy);
    float e = 0.012;
    float dx = (relief(position.xy + vec2(e, 0.0)) - relief(position.xy - vec2(e, 0.0))) / (2.0 * e);
    float dy = (relief(position.xy + vec2(0.0, e)) - relief(position.xy - vec2(0.0, e))) / (2.0 * e);
    vSurfaceNormal = normalize(mat3(modelMatrix) * vec3(-dx, -dy, 1.0));
    vLocal = p;
    vec4 world = modelMatrix * vec4(p, 1.0);
    vPosition = world.xyz;
    gl_Position = projectionMatrix * viewMatrix * world;
  }
`;

const FRAGMENT = `
  uniform float uTime;
  uniform float uMode;
  uniform vec3 uBase;
  uniform vec3 uAccent;
  uniform float uExposure;
  varying vec3 vPosition;
  varying vec3 vLocal;
  varying vec3 vSurfaceNormal;
  varying vec2 vUv;
  void main() {
    vec3 normal = normalize(vSurfaceNormal);
    if (!gl_FrontFacing) normal *= -1.0;
    vec3 viewDir = normalize(cameraPosition - vPosition);
    vec3 lightDir = normalize(vec3(-2.8, 5.4, 4.5) - vPosition);
    vec3 fillDir = normalize(vec3(4.0, 1.8, 3.0) - vPosition);
    float diffuse = max(dot(normal, lightDir), 0.0);
    float fill = max(dot(normal, fillDir), 0.0);
    float spec = pow(max(dot(normal, normalize(lightDir + viewDir)), 0.0), 42.0);
    float rim = pow(1.0 - max(dot(normal, viewDir), 0.0), 2.2);
    float shade = smoothstep(-0.3, 0.35, vLocal.z);
    vec3 color = mix(uAccent * 0.64, uBase, 0.65 + shade * 0.35);
    color *= 0.17 + diffuse * 0.79 + fill * 0.17;
    color += vec3(1.0, 0.97, 0.86) * spec * 0.38;
    color += uAccent * rim * 0.2;
    if (uMode > 0.5 && uMode < 1.5) {
      float facets = floor((vLocal.z + 0.5) * 14.0) / 14.0;
      color = mix(color, color * (0.65 + facets * 0.7), 0.65);
      color += vec3(0.3, 0.16, 0.05) * spec;
    }
    if (uMode > 1.5 && uMode < 2.5) {
      float line = 1.0 - smoothstep(0.025, 0.08, abs(fract(vLocal.z * 19.0 + uTime * 0.04) - 0.5));
      color = mix(vec3(0.025, 0.1, 0.12), uBase * 1.2, line);
      color += uAccent * diffuse * 0.13;
    }
    if (uMode > 2.5) {
      vec3 iridescence = 0.65 + 0.35 * cos(vec3(0.0, 2.1, 4.2) + rim * 5.0 + vLocal.z * 2.0);
      color = mix(color, iridescence * (0.35 + diffuse * 0.6), 0.32);
      color += vec3(0.8, 0.9, 1.0) * spec * 0.4;
    }
    float vignette = 1.0 - 0.18 * length(vUv - 0.5);
    gl_FragColor = vec4(color * vignette * uExposure, 1.0);
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }
`;

async function mountStage(root) {
  const host = root.closest('.lab-workspace') || root.parentElement;
  const control = selector => host.querySelector(selector);
  const canvas = root.querySelector('canvas');
  const note = root.querySelector('[data-fallback-note]');
  const status = control('[data-render-status]');
  const pauseButton = control('[data-stage-pause]');
  const qualityInput = control('[data-stage-quality]');
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const signal = new AbortController();
  const defaults = { preset: 'silk', speed: 0.6, exposure: 1.1, reflection: 0.45 };
  const state = { ...defaults, quality: 'auto', paused: reduced.matches };
  let engine = null;
  let visible = true;
  let disposed = false;
  let frame = 0;
  let lastTime = 0;
  let phase = 2.2;
  let fpsStart = 0;
  let fpsFrames = 0;
  let loading = false;
  let fallbackReason = '';

  try {
    const stored = localStorage.getItem('arcane-render-quality');
    if (['auto', 'low', 'high', 'static'].includes(stored)) state.quality = stored;
  } catch { /* Storage is optional; the scene must still render. */ }

  function setStatus(text) { if (status) status.textContent = text; }
  function syncControls() {
    if (pauseButton) {
      pauseButton.textContent = state.paused ? '继续动效' : '暂停动效';
      pauseButton.setAttribute('aria-pressed', String(state.paused));
      pauseButton.disabled = state.quality === 'static' || Boolean(fallbackReason);
    }
    if (qualityInput) qualityInput.value = state.quality;
    for (const key of ['speed', 'exposure', 'reflection']) {
      const input = control(`[data-stage-${key}]`);
      if (input) input.value = state[key];
      const output = host.querySelector(`[data-value="${key}"]`);
      if (output) output.value = state[key].toFixed(2);
    }
    const presetInput = control('[data-stage-preset]');
    if (presetInput) presetInput.value = state.preset;
    const title = host.querySelector('#material-title');
    if (title) title.textContent = PRESETS[state.preset].title;
    const description = host.querySelector('[data-material-description]');
    if (description) description.textContent = PRESETS[state.preset].description;
    root.dataset.preset = state.preset;
    root.dataset.paused = String(state.paused);
    root.dataset.quality = state.quality;
    root.style.setProperty('--preset-color', PRESETS[state.preset].color);
  }

  function stopFrame() {
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
    lastTime = 0;
  }
  function canAnimate() {
    return engine && !disposed && visible && !document.hidden && !state.paused && state.speed > 0 && state.quality !== 'static' && !fallbackReason;
  }
  function showFallback(reason) {
    stopFrame();
    root.dataset.render = 'fallback';
    note.hidden = false;
    fallbackReason = reason;
    setStatus(reason || '静态展台');
    syncControls();
  }
  function destroyEngine() {
    if (!engine) return;
    stopFrame();
    const { scene, reflector, renderer, observer } = engine;
    observer.disconnect();
    reflector.dispose();
    const materials = new Set();
    const geometries = new Set();
    scene.traverse(object => {
      if (object.geometry) geometries.add(object.geometry);
      if (object.material) {
        for (const material of Array.isArray(object.material) ? object.material : [object.material]) materials.add(material);
      }
    });
    geometries.forEach(geometry => geometry.dispose());
    materials.forEach(material => material.dispose());
    renderer.dispose();
    engine = null;
  }
  function qualityScale() {
    if (state.quality === 'low') return 0.7;
    if (state.quality === 'high') return Math.min(devicePixelRatio || 1, 1.7);
    return Math.min(devicePixelRatio || 1, root.clientWidth < 600 ? 1 : 1.3);
  }
  function resize() {
    if (!engine) return;
    const width = Math.max(1, root.clientWidth);
    const height = Math.max(1, root.clientHeight);
    engine.renderer.setPixelRatio(qualityScale());
    engine.renderer.setSize(width, height, false);
    engine.camera.aspect = width / height;
    engine.camera.position.z = width / height < 0.9 ? 9.8 : 8.7;
    engine.camera.updateProjectionMatrix();
    const resolution = state.quality === 'high' ? 768 : state.quality === 'low' ? 256 : 512;
    engine.reflector.getRenderTarget().setSize(resolution, resolution);
    render();
  }
  function render() {
    if (!engine || state.quality === 'static' || disposed || fallbackReason) return;
    const { renderer, scene, camera, material, sculpture, floorVeil, pointer } = engine;
    material.uniforms.uTime.value = phase;
    material.uniforms.uExposure.value = state.exposure;
    material.uniforms.uMode.value = PRESETS[state.preset].mode;
    material.uniforms.uBase.value.set(PRESETS[state.preset].color);
    material.uniforms.uAccent.value.set(PRESETS[state.preset].accent);
    floorVeil.material.opacity = 1 - state.reflection;
    sculpture.rotation.y = phase * 0.08;
    camera.position.x += (pointer.x * 0.38 - camera.position.x) * 0.035;
    camera.position.y += (2.8 + pointer.y * 0.2 - camera.position.y) * 0.035;
    camera.lookAt(0, 2.25, -0.5);
    renderer.render(scene, camera);
  }
  function tick(time) {
    frame = 0;
    if (!canAnimate()) return;
    const interval = state.quality === 'low' || root.dataset.mode === 'hero' ? 1000 / 30 : 1000 / 60;
    if (!lastTime || time - lastTime >= interval - 1) {
      phase += Math.min(lastTime ? (time - lastTime) / 1000 : 0, 0.1) * state.speed;
      lastTime = time;
      render();
      fpsFrames++;
      if (time - fpsStart > 1500) {
        setStatus(`${Math.round(fpsFrames * 1000 / (time - fpsStart))} FPS / ${state.quality === 'low' ? '节能' : state.quality === 'high' ? '精细' : '自动'}`);
        fpsStart = time;
        fpsFrames = 0;
      }
    }
    frame = requestAnimationFrame(tick);
  }
  function resumeFrame() {
    stopFrame();
    if (canAnimate()) {
      fpsStart = performance.now();
      fpsFrames = 0;
      frame = requestAnimationFrame(tick);
    } else if (engine && state.quality !== 'static' && !fallbackReason) {
      render();
      setStatus(state.paused ? (reduced.matches ? '已暂停 / 减少动态效果' : '动效已暂停') : state.speed === 0 ? '静止 / 速度为 0' : '待机');
    }
  }

  async function startEngine() {
    if (engine || loading || disposed || state.quality === 'static') return;
    loading = true;
    fallbackReason = '';
    root.dataset.render = 'loading';
    setStatus('准备展台');
    try {
      const [THREE, { Reflector }] = await Promise.all([
        import('./vendor/three/three.module.js'),
        import('./vendor/three/Reflector.js')
      ]);
      if (disposed || state.quality === 'static') return;
      const renderer = new THREE.WebGLRenderer({ canvas, alpha: false, antialias: state.quality !== 'low', powerPreference: 'low-power' });
      renderer.outputColorSpace = THREE.SRGBColorSpace;
      renderer.toneMapping = THREE.ACESFilmicToneMapping;
      renderer.toneMappingExposure = 1.1;
      renderer.debug.onShaderError = () => showFallback('Shader 编译失败 / 静态展台');
      const scene = new THREE.Scene();
      scene.background = new THREE.Color(0x07120f);
      scene.fog = new THREE.FogExp2(0x07120f, 0.055);
      const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 50);
      camera.position.set(0, 2.8, 8.7);
      const material = new THREE.ShaderMaterial({
        uniforms: {
          uTime: { value: phase }, uMode: { value: 0 },
          uExposure: { value: state.exposure },
          uBase: { value: new THREE.Color(PRESETS.silk.color) },
          uAccent: { value: new THREE.Color(PRESETS.silk.accent) }
        }, vertexShader: VERTEX, fragmentShader: FRAGMENT,
        side: THREE.DoubleSide
      });
      const frameMesh = new THREE.Mesh(new THREE.BoxGeometry(4.8, 4.9, 0.14), new THREE.MeshStandardMaterial({ color: 0x23352a, metalness: 0.7, roughness: 0.36 }));
      frameMesh.position.set(0, 3, -1.4);
      scene.add(frameMesh);
      const panel = new THREE.Mesh(new THREE.PlaneGeometry(4.48, 4.58, 92, 92), material);
      panel.position.set(0, 3, -0.72);
      scene.add(panel);
      const back = new THREE.Mesh(new THREE.BoxGeometry(18, 9, 0.2), new THREE.MeshStandardMaterial({ color: 0x10241c, roughness: 0.88 }));
      back.position.set(0, 4, -2.4);
      scene.add(back);
      for (let index = 0; index < 4; index++) {
        const line = new THREE.Mesh(new THREE.BoxGeometry(18, 0.018, 0.03), new THREE.MeshBasicMaterial({ color: 0x1a2d21 }));
        line.position.set(0, 0.4 + index * 1.35, -2.27);
        scene.add(line);
      }
      const reflector = new Reflector(new THREE.PlaneGeometry(24, 22), { color: 0x6f8374, textureWidth: 512, textureHeight: 512, clipBias: 0.003, multisample: 0 });
      reflector.rotation.x = -Math.PI / 2;
      reflector.position.y = -0.03;
      scene.add(reflector);
      const floorVeil = new THREE.Mesh(new THREE.PlaneGeometry(24, 22), new THREE.MeshBasicMaterial({ color: 0x07120f, transparent: true, opacity: 1 - state.reflection, depthWrite: false }));
      floorVeil.rotation.x = -Math.PI / 2;
      floorVeil.position.y = -0.018;
      floorVeil.renderOrder = 3;
      scene.add(floorVeil);
      const sculpture = new THREE.Mesh(new THREE.TorusKnotGeometry(0.27, 0.055, 100, 12, 2, 3), new THREE.MeshStandardMaterial({ color: 0x95ada0, metalness: 0.9, roughness: 0.23 }));
      sculpture.position.set(-2.72, 0.93, 0.6);
      sculpture.scale.set(1, 1.9, 1);
      scene.add(sculpture);
      const pedestal = new THREE.Mesh(new THREE.CylinderGeometry(0.27, 0.32, 0.12, 32), new THREE.MeshStandardMaterial({ color: 0x172c20, metalness: 0.6, roughness: 0.4 }));
      pedestal.position.set(-2.72, 0.06, 0.6);
      scene.add(pedestal);
      scene.add(new THREE.HemisphereLight(0xcbd6b2, 0x102d1b, 1.9));
      const key = new THREE.PointLight(0xd8ebbd, 48, 15, 2);
      key.position.set(-2, 5.5, 3);
      scene.add(key);
      const fill = new THREE.PointLight(0x8dbba5, 18, 12, 2);
      fill.position.set(3, 2.5, 2);
      scene.add(fill);
      const observer = new ResizeObserver(resize);
      engine = { renderer, scene, camera, material, sculpture, reflector, floorVeil, observer, pointer: { x: 0, y: 0 } };
      observer.observe(root);
      note.hidden = true;
      resize();
      if (!fallbackReason) root.dataset.render = 'ready';
      syncControls();
      resumeFrame();
    } catch (error) {
      destroyEngine();
      showFallback('当前设备使用静态展台');
    } finally { loading = false; }
  }

  pauseButton?.addEventListener('click', () => {
    state.paused = !state.paused;
    syncControls();
    resumeFrame();
  }, { signal: signal.signal });
  control('[data-stage-preset]')?.addEventListener('change', event => {
    if (!Object.hasOwn(PRESETS, event.target.value)) return;
    state.preset = event.target.value;
    syncControls();
    render();
  }, { signal: signal.signal });
  for (const key of ['speed', 'exposure', 'reflection']) {
    control(`[data-stage-${key}]`)?.addEventListener('input', event => {
      const value = Number(event.target.value);
      if (!Number.isFinite(value)) return;
      state[key] = value;
      syncControls();
      if (key === 'speed') resumeFrame(); else render();
    }, { signal: signal.signal });
  }
  qualityInput?.addEventListener('change', async event => {
    if (!['auto', 'low', 'high', 'static'].includes(event.target.value)) return;
    state.quality = event.target.value;
    try { localStorage.setItem('arcane-render-quality', state.quality); } catch { /* Optional preference. */ }
    if (state.quality === 'static') {
      showFallback('静态展台');
      destroyEngine();
      fallbackReason = '';
    } else {
      fallbackReason = '';
      if (!engine) await startEngine(); else { resize(); resumeFrame(); }
    }
    syncControls();
  }, { signal: signal.signal });
  control('[data-stage-reset]')?.addEventListener('click', () => {
    Object.assign(state, defaults, { paused: reduced.matches });
    phase = 2.2;
    if (engine) Object.assign(engine.pointer, { x: 0, y: 0 });
    syncControls();
    resumeFrame();
  }, { signal: signal.signal });
  root.addEventListener('pointermove', event => {
    if (!engine || reduced.matches || state.paused) return;
    const rect = root.getBoundingClientRect();
    engine.pointer.x = ((event.clientX - rect.left) / rect.width - 0.5) * 2;
    engine.pointer.y = -((event.clientY - rect.top) / rect.height - 0.5) * 2;
  }, { signal: signal.signal });
  root.addEventListener('pointerleave', () => {
    if (engine) Object.assign(engine.pointer, { x: 0, y: 0 });
  }, { signal: signal.signal });
  canvas.addEventListener('webglcontextlost', event => {
    event.preventDefault();
    showFallback('图形上下文已暂停 / 静态展台');
  }, { signal: signal.signal });
  document.addEventListener('visibilitychange', resumeFrame, { signal: signal.signal });
  reduced.addEventListener('change', event => {
    if (event.matches) state.paused = true;
    syncControls();
    resumeFrame();
  }, { signal: signal.signal });
  const intersection = new IntersectionObserver(entries => {
    visible = entries[0].isIntersecting;
    resumeFrame();
  }, { threshold: 0.01 });
  intersection.observe(root);
  window.addEventListener('pagehide', () => {
    disposed = true;
    signal.abort();
    intersection.disconnect();
    destroyEngine();
  }, { once: true });
  syncControls();
  if (state.quality === 'static') showFallback('静态展台');
  else await startEngine();
}

for (const root of document.querySelectorAll('[data-arcane-stage]')) mountStage(root);
