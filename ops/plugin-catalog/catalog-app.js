(function () {
  'use strict';
  const catalog = JSON.parse(document.getElementById('catalog-data').textContent);
  const records = catalog.records;
  const core = CatalogCore;
  const storageKey = 'kks-plugin-catalog:v1:' + catalog.catalog_sha256;
  const byId = new Map(records.map(record => [record.id, record]));
  const el = id => document.getElementById(id);
  const text = (tag, value, className) => { const node = document.createElement(tag); node.textContent = value; if (className) node.className = className; return node; };
  const knownFields = new Set(['id','name','category','resource_type','tutorials','purpose','compatibility','install_status','version','author','sources','dependencies','conflicts','difficulty','recommendation','archive_paths']);
  let storageAvailable = true, selected = new Set(), currentDetail = null, visible = [], noticeTimer;
  try { selected = core.restoreSelection(localStorage.getItem(storageKey), records); } catch (_) { storageAvailable = false; }

  function notify(message) {
    el('notice').textContent = message;
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => { el('notice').textContent = ''; }, 5500);
  }
  function persist() {
    try { localStorage.setItem(storageKey, JSON.stringify([...selected])); }
    catch (_) { if (storageAvailable) notify('浏览器不允许保存选择。本次仍可勾选并导出清单。'); storageAvailable = false; }
  }
  function filters() {
    return {query:el('query').value, category:el('category').value, install:el('install').value,
      archive:el('archive').value, resourceType:el('resource-type').value, withTutorial:el('with-tutorial').checked,
      selectedOnly:el('selected-only').checked, selected};
  }
  function statusText(record) {
    const raw = record.install_status;
    const group = core.installGroup(record);
    if (raw && /[\u3400-\u9fff]/.test(raw)) return raw;
    return group === 'installed' ? '已安装' : group === 'not_installed' ? '未安装' : '状态待核';
  }
  function updateSelection() {
    const count = selected.size;
    const installedCount = records.filter(record => selected.has(record.id) && core.installGroup(record) === 'installed').length;
    el('selected-count').textContent = count;
    el('selected-context').textContent = count ? (installedCount ? `其中 ${installedCount} 项已安装 · 供用途复核` : '导出后再决定如何处理') : '可跨筛选保留选择';
    for (const id of ['copy','export','clear-selection']) el(id).disabled = count === 0;
    if (currentDetail) el('detail-select').textContent = selected.has(currentDetail.id) ? '取消选择' : core.installGroup(currentDetail) === 'installed' ? '加入用途复核清单' : '加入选择清单';
  }
  function setSelected(id, value) {
    if (!byId.has(id)) return;
    if (value) selected.add(id); else selected.delete(id);
    persist(); render();
  }
  function render() {
    visible = records.filter(record => core.matches(record, filters()));
    const fragment = document.createDocumentFragment();
    for (const record of visible) {
      const tr = document.createElement('tr'); tr.dataset.recordId = record.id;
      if (selected.has(record.id)) tr.classList.add('selected');
      const pick = document.createElement('td');
      const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.checked = selected.has(record.id);
      checkbox.setAttribute('aria-label', '选择 ' + record.name); checkbox.dataset.selectId = record.id;
      checkbox.addEventListener('change', () => setSelected(record.id, checkbox.checked)); pick.append(checkbox);
      const identity = document.createElement('td');
      const name = text('button', record.name, 'plugin-name'); name.type = 'button'; name.addEventListener('click', () => showDetail(record));
      const meta = text('div', '', 'plugin-meta'); meta.append(text('span', record.category || '未分类', 'category-tag'));
      if (record.resource_type) meta.append(text('span', record.resource_type, 'category-tag'));
      if (record.tutorials?.length) meta.append(text('span', `${record.tutorials.length} 篇教程`, 'category-tag'));
      if (record.version) meta.append(text('span', record.version));
      identity.append(name, meta);
      const purpose = document.createElement('td'); purpose.append(text('p', record.purpose || '用途说明尚未提供。', 'purpose'));
      if (record.recommendation) purpose.append(text('p', record.recommendation, 'recommendation'));
      const state = document.createElement('td'); state.append(text('span', statusText(record), 'status ' + core.installGroup(record)));
      state.append(text('span', record.compatibility || '兼容性尚未核实', 'compat'));
      const more = document.createElement('td'); const open = text('button', '↗', 'details-button'); open.setAttribute('aria-label', '查看 ' + record.name + ' 的详情'); open.addEventListener('click', () => showDetail(record)); more.append(open);
      tr.append(pick, identity, purpose, state, more); fragment.append(tr);
    }
    el('rows').replaceChildren(fragment);
    el('empty').hidden = visible.length !== 0;
    el('result-count').textContent = `显示 ${visible.length} / ${records.length} 项`;
    el('select-visible').disabled = visible.length === 0;
    updateSelection();
  }
  function section(title, value, mode) {
    if (value == null || value === '' || Array.isArray(value) && value.length === 0) return;
    const wrapper = text('section', '', 'detail-section'); wrapper.append(text('h3', title));
    if (mode === 'sources') {
      const list = document.createElement('ul');
      for (const source of value) {
        const item = document.createElement('li');
        const url = typeof source === 'string' ? source : source.url;
        const label = typeof source === 'string' ? source : source.label || source.url;
        const safe = core.safeUrl(url);
        if (safe) { const link = text('a', label); link.href = safe; link.target = '_blank'; link.rel = 'noopener noreferrer'; item.append(link); }
        else item.append(text('span', label));
        if (typeof source === 'object' && source !== null) {
          const extra = Object.fromEntries(Object.entries(source).filter(([key]) => !['label','url'].includes(key)));
          if (Object.keys(extra).length) item.append(text('pre', JSON.stringify(extra, null, 2)));
        }
        list.append(item);
      }
      wrapper.append(list);
    } else if (Array.isArray(value)) {
      const list = document.createElement('ul'); value.forEach(item => list.append(text('li', item))); wrapper.append(list);
    } else if (mode === 'json') wrapper.append(text('pre', JSON.stringify(value, null, 2)));
    else wrapper.append(text('p', value));
    el('detail-content').append(wrapper);
  }
  function showDetail(record) {
    currentDetail = record;
    el('detail-title').textContent = record.name;
    el('detail-category').textContent = record.category || '未分类';
    el('detail-version').textContent = [record.version && '版本 ' + record.version, record.author && '作者 ' + record.author, 'ID ' + record.id].filter(Boolean).join('  ·  ');
    el('detail-content').replaceChildren();
    section('主要用途', record.purpose || '尚未提供');
    section('安装与兼容', [statusText(record), record.install_status && record.install_status !== statusText(record) ? '资料原状态：' + record.install_status : '', record.compatibility || '兼容性尚未核实'].filter(Boolean).join('\n'));
    section('适合怎样使用', record.recommendation);
    section('上手难度', record.difficulty);
    section('依赖项目', record.dependencies);
    section('冲突与限制', record.conflicts);
    for (const guide of record.tutorials || []) {
      const article = text('details', '', 'detail-section tutorial');
      article.append(text('summary', guide.title));
      article.append(text('div', guide.content, 'tutorial-content'));
      el('detail-content').append(article);
    }
    section('作者与资料来源', record.sources, 'sources');
    section('本地原包记录', record.archive_paths);
    const extras = Object.fromEntries(Object.entries(record).filter(([key]) => !knownFields.has(key)));
    if (Object.keys(extras).length) section('补充资料', extras, 'json');
    updateSelection();
    el('detail').showModal();
  }
  function resetFilters() {
    for (const id of ['query','category','resource-type','install','archive']) el(id).value = '';
    el('with-tutorial').checked = false;
    el('selected-only').checked = false; render();
  }
  function exportData() { return core.exportSelection(catalog, selected); }
  function plainList() {
    const data = exportData();
    return `${catalog.title}\n已选 ${data.selected_count} 项 · 仅选择清单，不执行安装\n\n` + data.records.map((record, index) =>
      `${index + 1}. ${record.name}${record.version ? ' / ' + record.version : ''}\n   ID: ${record.id}\n   状态：${statusText(record)}${core.installGroup(record) === 'installed' ? '（复核现有用途）' : ''}\n   用途：${record.purpose || '尚未提供'}\n   兼容：${record.compatibility || '尚未核实'}`).join('\n\n');
  }
  function downloadSelection() {
    const blob = new Blob([JSON.stringify(exportData(), null, 2) + '\n'], {type:'application/json;charset=utf-8'});
    const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
    const today = new Date();
    const localDate = [today.getFullYear(), String(today.getMonth() + 1).padStart(2, '0'), String(today.getDate()).padStart(2, '0')].join('-');
    link.download = 'studio-plugin-selection-' + localDate + '.json';
    document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    notify('已导出选择清单。已安装项目保留“复核现有用途”标记。');
  }
  async function copyList() {
    const value = plainList();
    try { if (!navigator.clipboard) throw new Error('unavailable'); await navigator.clipboard.writeText(value); notify('清单已复制。'); }
    catch (_) { el('copy-text').value = value; el('copy-dialog').showModal(); el('copy-text').focus(); el('copy-text').select(); }
  }
  el('total').textContent = records.length;
  el('installed-total').textContent = records.filter(record => core.installGroup(record) === 'installed').length;
  el('archive-total').textContent = records.filter(record => Array.isArray(record.archive_paths) && record.archive_paths.length).length;
  for (const category of [...new Set(records.map(record => record.category || '未分类'))].sort((a,b) => a.localeCompare(b, 'zh-CN'))) {
    const option = text('option', category); option.value = category; el('category').append(option);
  }
  const resourceTypes = [...new Set(records.map(record => record.resource_type).filter(Boolean))];
  for (const type of resourceTypes.sort((a,b) => a.localeCompare(b, 'zh-CN'))) {
    const option = text('option', type); option.value = type; el('resource-type').append(option);
  }
  el('guide-filters').hidden = !resourceTypes.length && !records.some(record => record.tutorials?.length);
  for (const id of ['query','category','resource-type','with-tutorial','install','archive','selected-only']) el(id).addEventListener(id === 'query' ? 'input' : 'change', render);
  el('reset-filters').addEventListener('click', resetFilters); el('empty-reset').addEventListener('click', resetFilters);
  el('select-visible').addEventListener('click', () => { visible.forEach(record => selected.add(record.id)); persist(); render(); notify('当前筛选结果已加入清单。'); });
  el('clear-selection').addEventListener('click', () => { selected.clear(); persist(); render(); });
  el('detail-select').addEventListener('click', () => setSelected(currentDetail.id, !selected.has(currentDetail.id)));
  el('close-detail').addEventListener('click', () => el('detail').close());
  el('close-copy').addEventListener('click', () => el('copy-dialog').close());
  for (const id of ['detail','copy-dialog']) el(id).addEventListener('click', event => { if (event.target === el(id)) { const rect = el(id).getBoundingClientRect(); if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) el(id).close(); } });
  el('export').addEventListener('click', downloadSelection); el('copy').addEventListener('click', copyList);
  render();
  if (!storageAvailable) notify('本次选择可正常导出；浏览器未允许保存到下次访问。');
})();
