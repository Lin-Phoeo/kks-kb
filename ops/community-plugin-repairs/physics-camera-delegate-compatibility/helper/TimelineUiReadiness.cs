using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

// Copy as one internal helper into each patched assembly; never patch Timeline itself.
internal static class TimelineUiReadiness
{
    private const string TimelineGuid = "com.joan6694.illusionplugins.timeline";
    private static Type cachedType;
    private static FieldInfo selfField;
    private static FieldInfo loadedField;
    private static FieldInfo allToggleField;
    private static bool validSchema;

    internal static bool IsReady()
    {
        PluginInfo info;
        if (!Chainloader.PluginInfos.TryGetValue(TimelineGuid, out info)) return false;
        BaseUnityPlugin instance = info.Instance;
        if (instance == null) return false;
        Type type = instance.GetType();
        if (type != cachedType)
        {
            cachedType = type;
            selfField = type.GetField("_self", BindingFlags.Static | BindingFlags.NonPublic);
            loadedField = type.GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic);
            allToggleField = type.GetField("_allToggle", BindingFlags.Instance | BindingFlags.NonPublic);
            validSchema = selfField != null && selfField.FieldType == type
                && loadedField != null && loadedField.FieldType == typeof(bool)
                && allToggleField != null && typeof(UnityEngine.Object).IsAssignableFrom(allToggleField.FieldType);
        }
        if (!validSchema) return false;
        if (!ReferenceEquals(selfField.GetValue(null), instance)) return false;
        if (!(bool)loadedField.GetValue(instance)) return false;
        UnityEngine.Object toggle = allToggleField.GetValue(instance) as UnityEngine.Object;
        return toggle != null;
    }
}
