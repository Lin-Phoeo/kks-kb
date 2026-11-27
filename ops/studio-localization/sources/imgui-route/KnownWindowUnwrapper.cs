using System;
using System.Reflection;
using UnityEngine;

internal sealed class KnownWindowUnwrapper
{
    internal const int MaximumLayers = 8;
    private readonly Type _layoutType;
    private readonly Type _dpiType;
    private readonly FieldInfo _layoutField;
    private readonly FieldInfo _dpiField;
    private readonly MethodInfo _layoutMethod;
    private readonly MethodInfo _dpiMethod;

    internal KnownWindowUnwrapper(Assembly unity, Assembly dpi)
    {
        if (unity != typeof(GUI.WindowFunction).Assembly || unity.GetName().Name != "UnityEngine.IMGUIModule")
            throw new InvalidOperationException("Unexpected Unity IMGUI assembly");
        if (dpi.GetName().Name != "IMGUI.HiDPI.Patcher.Mono" || dpi.GetName().Version != new Version(0, 0, 4, 0))
            throw new InvalidOperationException("Unexpected HiDPI assembly");
        _layoutType = unity.GetType("UnityEngine.GUILayout+LayoutedWindow", true);
        _dpiType = dpi.GetType("IMGUI.HiDPI.Patcher.Common.Hooks+<>c__DisplayClass27_0", true);
        _layoutField = RequireField(_layoutType, "m_Func");
        _dpiField = RequireField(_dpiType, "orig");
        _layoutMethod = RequireMethod(_layoutType, "DoWindow");
        _dpiMethod = RequireMethod(_dpiType, "<WrapWindowFunc>b__0");
    }

    private static FieldInfo RequireField(Type owner, string name)
    {
        FieldInfo field = owner.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != typeof(GUI.WindowFunction))
            throw new InvalidOperationException("Known wrapper field signature mismatch");
        return field;
    }

    private static MethodInfo RequireMethod(Type owner, string name)
    {
        MethodInfo method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, new Type[] { typeof(int) }, null);
        if (method == null || method.ReturnType != typeof(void))
            throw new InvalidOperationException("Known wrapper callback signature mismatch");
        return method;
    }

    internal bool TryUnwrap(GUI.WindowFunction input, out GUI.WindowFunction final, out int layers)
    {
        final = input;
        layers = 0;
        if (input == null) return false;
        GUI.WindowFunction[] seen = new GUI.WindowFunction[MaximumLayers + 1];
        GUI.WindowFunction current = input;
        for (int depth = 0; depth <= MaximumLayers; depth++)
        {
            if (current.GetInvocationList().Length != 1) return false;
            for (int index = 0; index < depth; index++)
                if (ReferenceEquals(seen[index], current)) return false;
            seen[depth] = current;
            object target = current.Target;
            Type targetType = target == null ? null : target.GetType();
            FieldInfo field = null;
            if (targetType == _layoutType && current.Method == _layoutMethod) field = _layoutField;
            else if (targetType == _dpiType && current.Method == _dpiMethod) field = _dpiField;
            else
            {
                if (depth == 0 || current.Method.DeclaringType == null ||
                    current.Method.DeclaringType.Assembly == _layoutType.Assembly ||
                    current.Method.DeclaringType.Assembly == _dpiType.Assembly)
                    return false;
                final = current;
                layers = depth;
                return true;
            }
            if (depth == MaximumLayers) return false;
            current = field.GetValue(target) as GUI.WindowFunction;
            if (current == null) return false;
        }
        return false;
    }
}
