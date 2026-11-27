(function (root) {
  'use strict';
  const installed = new Set(['installed', 'installed_exact', 'installed_script', 'installed_framework', '已安装', '已装', 'installed_verified', 'installed_repaired', 'installed_identity_verified', 'installed_repaired_startup_verified', 'installed_upstream_package_unverified']);
  const absent = new Set(['not_installed', 'not_installed_candidate', 'not_installed_source_only', '未安装', '未装', 'archived', '已归档未装']);
  function installGroup(record) {
    const status = String(record.install_status_code || record.install_status || '').trim();
    return installed.has(status) ? 'installed' : absent.has(status) ? 'not_installed' : 'unknown';
  }
  function searchText(record) {
    return JSON.stringify(record).normalize('NFKC').toLocaleLowerCase();
  }
  function matches(record, filters) {
    const words = String(filters.query || '').normalize('NFKC').toLocaleLowerCase().trim().split(/\s+/).filter(Boolean);
    return words.every(word => searchText(record).includes(word)) &&
      (!filters.category || (record.category || '未分类') === filters.category) &&
      (!filters.resourceType || record.resource_type === filters.resourceType) &&
      (!filters.withTutorial || Array.isArray(record.tutorials) && record.tutorials.length > 0) &&
      (!filters.install || installGroup(record) === filters.install) &&
      (!filters.archive || (Array.isArray(record.archive_paths) && record.archive_paths.length > 0 ? 'yes' : 'no') === filters.archive) &&
      (!filters.selectedOnly || filters.selected.has(record.id));
  }
  function safeUrl(value) {
    try { const url = new URL(value); return ['https:', 'http:'].includes(url.protocol) ? url.href : null; }
    catch (_) { return null; }
  }
  function restoreSelection(value, records) {
    const known = new Set(records.map(record => record.id));
    try { const parsed = JSON.parse(value); return new Set(Array.isArray(parsed) ? parsed.filter(id => typeof id === 'string' && known.has(id)) : []); }
    catch (_) { return new Set(); }
  }
  function exportSelection(catalog, selected, date) {
    const records = catalog.records.filter(record => selected.has(record.id));
    return {schema_version: 1, action: 'review_selection_only', catalog_sha256: catalog.catalog_sha256,
      catalog_title: catalog.title, exported_at: date || new Date().toISOString(), selected_count: records.length,
      records, choices: records.map(record => ({id:record.id, intent:installGroup(record) === 'installed' ? 'review_existing_usage' : 'consider_after_review'}))};
  }
  const api = {installGroup, matches, safeUrl, restoreSelection, exportSelection};
  root.CatalogCore = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
