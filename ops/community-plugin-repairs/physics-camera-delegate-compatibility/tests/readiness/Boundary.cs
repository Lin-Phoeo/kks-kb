using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public bool destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool ln = ReferenceEquals(left, null) || (!ReferenceEquals(left, null) && left.destroyed);
            bool rn = ReferenceEquals(right, null) || (!ReferenceEquals(right, null) && right.destroyed);
            return ln && rn || !ln && !rn && ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
}
namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.Object { }
    public class PluginInfo { public BaseUnityPlugin Instance { get; set; } }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, BepInEx.PluginInfo> PluginInfos { get; private set; } = new Dictionary<string, BepInEx.PluginInfo>(); }
}
namespace BepInEx.Logging
{
    public class ManualLogSource { public void LogInfo(object message) { } }
}
namespace LiquidFluid
{
    public static class LiquidFluidPlugin { public static BepInEx.Logging.ManualLogSource SLog; }
}
public class TimelineHost : BepInEx.BaseUnityPlugin
{
    internal static TimelineHost _self;
    private bool _loaded;
    private UnityEngine.Object _allToggle;
    public TimelineHost(bool loaded, UnityEngine.Object toggle) { _loaded = loaded; _allToggle = toggle; _self = this; }
    public void Set(bool loaded, UnityEngine.Object toggle) { _loaded = loaded; _allToggle = toggle; }
}
public class MissingSchemaHost : BepInEx.BaseUnityPlugin { }
public class WrongTypeHost : BepInEx.BaseUnityPlugin
{
    internal static WrongTypeHost _self;
    private string _loaded = "true";
    private string _allToggle = "not Unity";
}
public static class Bridge
{
    public static int InitCalls;
    public static bool Populate = true;
    public static bool ThrowOnInit;
    public static object Delegate;
    public static void Add() { }
    public static void Init()
    {
        InitCalls++;
        if (ThrowOnInit) throw new InvalidOperationException("synthetic init failure");
        if (Populate) Delegate = new object();
    }
}
public static class FixtureSoft
{
    private static bool _ready;
    private static MethodInfo _add;
    private static MethodInfo _init;
    private static FieldInfo _fInterpDeleg;
    public static int FindCalls;
    public static bool MissingApi;
    private static void FindTypes()
    {
        FindCalls++;
        _add = MissingApi ? null : typeof(Bridge).GetMethod("Add");
        _init = MissingApi ? null : typeof(Bridge).GetMethod("Init");
        _fInterpDeleg = typeof(Bridge).GetField("Delegate");
    }
    public static bool EnsureReadyOriginal() { return false; }
    public static bool EnsureReadyGuarded() { return false; }
    public static void Reset() { _ready = false; _add = null; _init = null; _fInterpDeleg = null; FindCalls = 0; MissingApi = false; }
    public static bool Ready { get { return _ready; } }
}
public static class FixtureLiquid
{
    private static bool _ready;
    private static MethodInfo _add;
    private static MethodInfo _init;
    private static FieldInfo _fInterpDeleg;
    public static int FindCalls;
    public static bool MissingApi;
    private static void FindTypes()
    {
        FindCalls++;
        _add = MissingApi ? null : typeof(Bridge).GetMethod("Add");
        _init = MissingApi ? null : typeof(Bridge).GetMethod("Init");
        _fInterpDeleg = typeof(Bridge).GetField("Delegate");
    }
    public static bool EnsureReadyOriginal() { return false; }
    public static bool EnsureReadyGuarded() { return false; }
    public static void Reset() { _ready = false; _add = null; _init = null; _fInterpDeleg = null; FindCalls = 0; MissingApi = false; }
    public static bool Ready { get { return _ready; } }
}
