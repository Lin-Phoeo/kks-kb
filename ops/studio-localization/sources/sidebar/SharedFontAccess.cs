using System;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace Codex.StudioPanelTypography
{
    internal static class SharedFontAccess
    {
        private static Type _type;
        private static PropertyInfo _active, _ugui, _tmp;
        internal static bool Read(out Font font, out TMP_FontAsset tmp)
        {
            font = null; tmp = null;
            if (_type == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if (assembly.GetName().Name == "Codex.StudioFontController")
                    {
                        _type = assembly.GetType("Codex.StudioFontController.FontSelection", false);
                        break;
                    }
                if (_type == null) return false;
                _active = _type.GetProperty("Active");
                _ugui = _type.GetProperty("UguiFont");
                _tmp = _type.GetProperty("TmpFont");
            }
            if (_active == null || _ugui == null || _tmp == null || !(bool)_active.GetValue(null, null)) return false;
            font = _ugui.GetValue(null, null) as Font;
            tmp = _tmp.GetValue(null, null) as TMP_FontAsset;
            return font && tmp;
        }
    }
}
