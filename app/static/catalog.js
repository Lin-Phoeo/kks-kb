/* Filtering only affects this rendered catalogue. Search remains a normal GET form. */
(() => {
  const root = document.querySelector('[data-catalog]');
  if (!root) return;
  const field = root.querySelector('#filter');
  const clear = root.querySelector('#catalog-clear');
  const count = root.querySelector('#catalog-count');
  const empty = root.querySelector('#catalog-empty');
  const groupCount = root.querySelector('[data-catalog-group-count]');
  const groups = [...root.querySelectorAll('[data-catalog-group]')];
  const total = root.querySelectorAll('[data-catalog-item]').length;
  function filter() {
    const query = field.value.trim().toLocaleLowerCase();
    let visible = 0;
    let visibleGroups = 0;
    groups.forEach(group => {
      let matches = 0;
      group.querySelectorAll('[data-catalog-item]').forEach(item => {
        const show = !query || item.dataset.k.toLocaleLowerCase().includes(query);
        item.hidden = !show;
        if (show) matches++;
      });
      group.hidden = matches === 0;
      group.querySelector('[data-group-count]').textContent = matches + ' 篇';
      visible += matches;
      if (matches) visibleGroups++;
    });
    count.textContent = '显示 ' + visible + ' / ' + total + ' 篇';
    groupCount.textContent = String(visibleGroups);
    clear.hidden = field.value.length === 0;
    empty.hidden = visible > 0;
  }
  field.addEventListener('input', filter);
  clear.addEventListener('click', () => { field.value = ''; filter(); field.focus(); });
  filter();
})();
