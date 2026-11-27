using System;
using BepInEx.Configuration;
using UnityEngine;

namespace Codex.StudioFontController
{
    public sealed partial class Plugin
    {
        private readonly FontPickerState _picker = new FontPickerState();
        private Vector2 _familyScroll;
        private bool _advanced, _pendingAdvanced;
        private string _fileDraft;
        private GUIContent[] _pickerButtons = new GUIContent[0];
        private string[] _pickerLabelSource;
        private string _pickerSelection;
        private GUIContent _selectedFontLabel = new GUIContent();

        public sealed class ConfigurationManagerAttributes
        {
            public Action<ConfigEntryBase> CustomDrawer;
            public int Order;
            public bool HideDefaultButton = true;
            public bool HideSettingName = true;
        }

        private void BindPanelSettings()
        {
            _enabled = Config.Bind("字体", "统一界面字体", FontDefaults.Enabled,
                new ConfigDescription("关闭时保留默认中文。加粗与字号仍可单独调整；选择列表中的字体会自动开启。", null,
                    new ConfigurationManagerAttributes { Order = 100, HideSettingName = false }));
            _family = Config.Bind("字体", "系统字体", FontDefaults.Family(_resolver.Families()),
                new ConfigDescription("可按中文名或原家族名搜索。仅显示已检测到中文字形且当前游戏可可靠选择的字体。", null,
                    new ConfigurationManagerAttributes { Order = 90, CustomDrawer = DrawFamily }));
            _bold = Config.Bind("字体", "文字加粗", false,
                new ConfigDescription("为文字增加粗体，保留原有斜体。关闭后恢复控件原来的粗细。", null,
                    new ConfigurationManagerAttributes { Order = 80, HideSettingName = false }));
            _sizeDelta = Config.Bind("字体", "字号微调", 0,
                new ConfigDescription("在各控件原字号上微调，0为原大小；紧凑输入框限制放大。", new AcceptableValueRange<int>(-2, 3),
                    new ConfigurationManagerAttributes { Order = 70, CustomDrawer = DrawSize }));
            _file = Config.Bind("字体", "字体文件", "",
                new ConfigDescription("可选的已安装TTF/OTF/TTC文件。输入后点击应用；留空使用列表选择。相对路径从UserData/StudioFonts读取。", null,
                    new ConfigurationManagerAttributes { Order = 10, CustomDrawer = DrawAdvanced }));
            Config.Bind("字体", "预览与状态", 0, new ConfigDescription("当前字体效果及恢复入口。", null,
                new ConfigurationManagerAttributes { Order = 110, CustomDrawer = DrawStatus }));
            _fileDraft = _file.Value;
        }

        private void DrawStatus(ConfigEntryBase entry)
        {
            GUILayout.BeginVertical();
            try
            {
            GUILayout.Label("当前：" + (FontSelection.Active ? ShortName(FontSourceResolver.DisplayName(FontSelection.Family)) : "默认中文"));
            GUILayout.Label("工作室 Studio 0123 · 字体预览");
            GUILayout.Label(_status);
            if (GUILayout.Button("恢复默认中文与字号")) RestoreDefaultChinese();
            }
            finally { GUILayout.EndVertical(); }
        }

        private void DrawSize(ConfigEntryBase entry)
        {
            GUILayout.BeginVertical();
            try
            {
            GUILayout.Label("字号微调");
            GUILayout.BeginHorizontal();
            try
            {
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && _sizeDelta.Value > TypographyPolicy.MinimumDelta;
                if (GUILayout.Button("减小", GUILayout.Width(52))) _sizeDelta.Value--;
                GUI.enabled = enabled;
                GUILayout.Label(_sizeDelta.Value == 0 ? "原大小" : (_sizeDelta.Value > 0 ? "+" : "") + _sizeDelta.Value,
                    GUILayout.Width(48));
                GUI.enabled = enabled && _sizeDelta.Value < TypographyPolicy.MaximumDelta;
                if (GUILayout.Button("增大", GUILayout.Width(52))) _sizeDelta.Value++;
                GUI.enabled = enabled && _sizeDelta.Value != 0;
                if (GUILayout.Button("归零", GUILayout.Width(52))) _sizeDelta.Value = 0;
            }
            finally { GUI.enabled = enabled; }
            }
            finally { GUILayout.EndHorizontal(); }
            }
            finally { GUILayout.EndVertical(); }
        }

        private void DrawFamily(ConfigEntryBase entry)
        {
            if (Event.current.type == EventType.Layout)
            {
                _picker.Layout(_chineseFamilies, _familyLabels, _catalogPublished, _resolver.CatalogError != null,
                    _family.Value, _resolver.CanonicalChineseFamily(_family.Value));
                if (!ReferenceEquals(_pickerLabelSource, _familyLabels) || _pickerSelection != _picker.SelectedFamily)
                {
                    _pickerLabelSource = _familyLabels; _pickerSelection = _picker.SelectedFamily;
                    _pickerButtons = new GUIContent[_familyLabels.Length];
                    for (int index = 0; index < _familyLabels.Length; index++)
                        _pickerButtons[index] = new GUIContent((_chineseFamilies[index] == _pickerSelection ? "已选 · " : "") +
                            ShortName(_familyLabels[index]), _familyLabels[index]);
                    string selected = FontSourceResolver.DisplayName(_picker.SelectedFamily);
                    _selectedFontLabel = new GUIContent("选定字体：" + ShortName(selected), selected);
                }
            }
            GUILayout.BeginVertical();
            try
            {
            GUILayout.Label(_selectedFontLabel);
            if (!_picker.Ready)
                GUILayout.Label(_picker.Failed ? "字体列表暂不可用，已保留当前显示。" : "正在准备中文字体列表…");
            else
            {
                if (!_picker.CurrentCompatible) GUILayout.Label("此旧选项未通过中文兼容检测，请从列表选择。");
                if (GUILayout.Button(_picker.Expanded ? "收起字体列表" : "选择中文字体（" + _chineseFamilies.Length + "）"))
                    _picker.PendingExpanded = !_picker.Expanded;
                if (_picker.Expanded)
                {
                    GUILayout.BeginHorizontal();
            try
            {
                    _picker.DraftQuery = GUILayout.TextField(_picker.DraftQuery, 80, GUILayout.MinHeight(24));
                    if (GUILayout.Button("清空", GUILayout.Width(52))) _picker.DraftQuery = "";
                    }
            finally { GUILayout.EndHorizontal(); }
                    GUILayout.Label("搜索中文名或原家族名 · " + _picker.Rows.Length + " 项");
                    _familyScroll = GUILayout.BeginScrollView(_familyScroll, GUILayout.Height(200));
                    try
                    {
                    if (_picker.Rows.Length == 0) GUILayout.Label("没有匹配字体，试试其他名称。");
                    foreach (int index in _picker.Rows)
                    {
                        if (!GUILayout.Button(_pickerButtons[index])) continue;
                        SelectFamily(_chineseFamilies[index]);
                        _picker.PendingExpanded = false;
                    }
                    }
                    finally { GUILayout.EndScrollView(); }
                }
            }
            }
            finally { GUILayout.EndVertical(); }
        }

        private static string ShortName(string label)
        {
            int separator = label.IndexOf(" / ", StringComparison.Ordinal);
            return separator < 0 ? label : label.Substring(0, separator);
        }

        private void DrawAdvanced(ConfigEntryBase entry)
        {
            if (Event.current.type == EventType.Layout)
            {
                if (_pendingAdvanced && !_advanced) _fileDraft = _file.Value;
                _advanced = _pendingAdvanced;
            }
            GUILayout.BeginVertical();
            try
            {
            if (GUILayout.Button(_advanced ? "收起高级设置" : "高级：自定义字体文件")) _pendingAdvanced = !_advanced;
            if (_advanced)
            {
                GUILayout.Label("使用已安装的 TTF / OTF / TTC 字体文件");
                _fileDraft = GUILayout.TextField(_fileDraft ?? "", 1024, GUILayout.MinHeight(24));
                GUILayout.BeginHorizontal();
            try
            {
                if (GUILayout.Button("应用文件"))
                {
                    _file.Value = (_fileDraft ?? "").Trim();
                    _enabled.Value = true;
                    _changeAt = Time.unscaledTime + 0.1f;
                }
                if (GUILayout.Button("清空文件路径")) { _fileDraft = ""; _file.Value = ""; }
                }
            finally { GUILayout.EndHorizontal(); }
                if (GUILayout.Button("重新应用当前设置")) _changeAt = Time.unscaledTime;
            }
            }
            finally { GUILayout.EndVertical(); }
        }

        private void RestoreDefaultChinese()
        {
            _enabled.Value = false; _bold.Value = false; _sizeDelta.Value = 0;
            _changeAt = Time.unscaledTime;
        }

        private void SelectFamily(string family)
        {
            if (!_resolver.IsChineseFamily(family)) return;
            _file.Value = ""; _family.Value = family; _enabled.Value = true;
            _changeAt = Time.unscaledTime + 0.1f;
        }
    }
}
