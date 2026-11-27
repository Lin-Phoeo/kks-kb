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
        private static Func<bool> _bold;
        private static Func<int> _delta;
        private static Action<Component, bool> _owner;
        private static bool Prepare()
        {
            if (_type != null) return true;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "Codex.StudioFontController")
                { _type = assembly.GetType("Codex.StudioFontController.FontSelection", false); break; }
            if (_type == null) return false;
            _active = _type.GetProperty("Active"); _ugui = _type.GetProperty("UguiFont"); _tmp = _type.GetProperty("TmpFont");
            PropertyInfo bold = _type.GetProperty("Bold"), delta = _type.GetProperty("SizeDelta");
            MethodInfo owner = _type.GetMethod("SetExternalTypographyOwner");
            if (bold != null) _bold = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), bold.GetGetMethod());
            if (delta != null) _delta = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), delta.GetGetMethod());
            if (owner != null) _owner = (Action<Component, bool>)Delegate.CreateDelegate(typeof(Action<Component, bool>), owner);
            return true;
        }
        internal static void ReadTypography(out bool bold, out int delta)
        {
            Prepare(); bold = _bold != null && _bold(); delta = _delta == null ? 0 : _delta();
        }
        internal static void OwnTypography(Component component, bool owned)
        { if (Prepare() && _owner != null) _owner(component, owned); }
        internal static bool Read(out Font font, out TMP_FontAsset tmp)
        {
            font = null; tmp = null;
            if (!Prepare()) return false;
            if (_active == null || _ugui == null || _tmp == null || !(bool)_active.GetValue(null, null)) return false;
            font = _ugui.GetValue(null, null) as Font;
            tmp = _tmp.GetValue(null, null) as TMP_FontAsset;
            return font && tmp;
        }
    }
}
