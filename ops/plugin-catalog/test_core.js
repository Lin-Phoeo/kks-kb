'use strict';
const assert = require('node:assert/strict');
const test = require('node:test');
const core = require('./catalog-core.js');
const rows = [
  {id:'a',name:'Alpha 工具',category:'镜头',purpose:'保存构图',install_status:'installed',archive_paths:['synthetic/a.zip'],extra:{keep:true}},
  {id:'b',name:'Beta 工具',category:'场景',purpose:'调整灯光',install_status:'not_installed',archive_paths:[]},
  {id:'c',name:'Gamma',category:'镜头',purpose:'保存朝向',install_status:'future-unknown',archive_paths:[]}
];
test('search words combine with category and archive filters', () => {
  assert.deepEqual(rows.filter(row => core.matches(row,{query:'ALPHA 构图',category:'镜头',archive:'yes'})).map(row=>row.id), ['a']);
  assert.deepEqual(rows.filter(row => core.matches(row,{category:'镜头',install:'unknown'})).map(row=>row.id), ['c']);
});
test('selection is not limited to the current filter', () => {
  const selected = new Set(['a','b']);
  assert.deepEqual(rows.filter(row=>core.matches(row,{category:'镜头',selectedOnly:true,selected})).map(row=>row.id), ['a']);
  const result = core.exportSelection({records:rows,title:'例子',catalog_sha256:'synthetic'},selected,'fixed');
  assert.equal(result.selected_count,2);
  assert.equal(result.choices[0].intent,'review_existing_usage');
  assert.equal(result.choices[1].intent,'consider_after_review');
  assert.deepEqual(result.records[0].extra,{keep:true});
  assert.equal(result.action,'review_selection_only');
  assert.equal(result.records[0],rows[0]);
});
test('stored choices accept only known IDs and tolerate corrupt data', () => {
  assert.deepEqual([...core.restoreSelection('["a","missing",4,"a"]',rows)],['a']);
  assert.equal(core.restoreSelection('{broken',rows).size,0);
  assert.equal(core.restoreSelection('{}',rows).size,0);
});
test('external links cannot use script or local file schemes', () => {
  assert.equal(core.safeUrl('javascript:alert(1)'),null);
  assert.equal(core.safeUrl('file:///private'),null);
  assert.equal(core.safeUrl('https://example.invalid/path'),'https://example.invalid/path');
});
test('machine status takes precedence and partial installs stay uncertain', () => {
  assert.equal(core.installGroup({install_status:'已安装脚本',install_status_code:'installed_script'}),'installed');
  assert.equal(core.installGroup({install_status:'合集中部分已安装',install_status_code:'installed_partial_bundle'}),'unknown');
  assert.equal(core.installGroup({install_status:'仅有旧报告',install_status_code:'installed_report_only'}),'unknown');
});
