using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using UnityEngine;

[BepInPlugin(PluginGuid, "Studio IMGUI Translation Route Adapter", "1.0.0")]
[BepInProcess("CharaStudio")]
[BepInDependency("gravydevsupreme.xunity.autotranslator", BepInDependency.DependencyFlags.HardDependency)]
public sealed class StudioImGuiRouteAdapter : BaseUnityPlugin
{
    internal const string PluginGuid = "codex.studio.imgui.route.adapter";
    private const string CoreSha = "9700854b20f25b7a81cff2c46da08c12bd5f27cc2544efdfe62dc4cb548bd002";
    private const string DpiSha = "aab0fd0ff3cda3a63170a0da4fac84f11234c05673a18ce8128e0699610760c0";
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static StudioImGuiRouteAdapter _instance;
    private static KnownWindowUnwrapper _unwrapper;
    private static readonly HashSet<MethodInfo> LoggedCallbacks = new HashSet<MethodInfo>();
    private Harmony _harmony;
    private MethodInfo _target;
    private RouteObservation _observation;
    private static string _lastCallback;

    private void Awake()
    {
        try
        {
            Assembly core = RequireAssembly("XUnity.AutoTranslator.Plugin.Core", CoreSha);
            Assembly dpi = RequireAssembly("IMGUI.HiDPI.Patcher.Mono", DpiSha);
            _unwrapper = new KnownWindowUnwrapper(typeof(GUI.WindowFunction).Assembly, dpi);
            Type helper = core.GetType("XUnity.AutoTranslator.Plugin.Core.Hooks.IMGUIWindowFunctions", true);
            _target = helper.GetMethod("GetPluginFunction", All, null, new Type[] { typeof(GUI.WindowFunction) }, null);
            if (_target == null || _target.ReturnType != typeof(GUI.WindowFunction))
                throw new InvalidOperationException("Window helper signature mismatch");
            _instance = this;
            _harmony = new Harmony(PluginGuid);
            _harmony.Patch(_target, postfix: new HarmonyMethod(typeof(StudioImGuiRouteAdapter).GetMethod("UnwrapPostfix", All)));
            Logger.LogInfo("STUDIO_IMGUI_ROUTE_READY knownWrappers=2 maxLayers=8");
            try { _observation = RouteObservation.Start(core, _harmony, Logger, delegate { return _lastCallback; }); }
            catch (Exception error) { Logger.LogWarning("STUDIO_IMGUI_ROUTE_OBSERVER_REFUSED " + error.GetType().Name); }
        }
        catch (Exception error)
        {
            Cleanup();
            Logger.LogError("STUDIO_IMGUI_ROUTE_REFUSED " + error.GetType().Name);
        }
    }

    private static Assembly RequireAssembly(string name, string sha)
    {
        Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(item => item.GetName().Name == name);
        if (assembly == null || string.IsNullOrEmpty(assembly.Location))
            throw new InvalidOperationException("Required loaded assembly missing");
        string actual;
        using (SHA256 hash = SHA256.Create())
        using (FileStream stream = File.OpenRead(assembly.Location))
            actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (actual != sha) throw new InvalidOperationException("Required loaded assembly hash differs");
        return assembly;
    }

    private static void UnwrapPostfix(GUI.WindowFunction __0, ref GUI.WindowFunction __result)
    {
        if (_unwrapper == null) return;
        GUI.WindowFunction final;
        int layers;
        if (!_unwrapper.TryUnwrap(__0, out final, out layers)) return;
        __result = final;
        MethodInfo method = final.Method;
        _lastCallback = method.DeclaringType.Assembly.GetName().Name + "/" + method.DeclaringType.FullName + "." + method.Name;
        if (LoggedCallbacks.Count < 32 && LoggedCallbacks.Add(method) && _instance != null)
            _instance.Logger.LogInfo("STUDIO_IMGUI_ROUTE callback=" + _lastCallback + " layers=" + layers);
    }

    private void OnDestroy() { Cleanup(); }

    private void Cleanup()
    {
        if (_observation != null) _observation.Stop();
        _observation = null;
        if (_harmony != null && _target != null) _harmony.Unpatch(_target, HarmonyPatchType.Postfix, PluginGuid);
        _harmony = null;
        _target = null;
        _unwrapper = null;
        _lastCallback = null;
        LoggedCallbacks.Clear();
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
