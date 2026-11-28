using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;

public static class TestReadiness
{
    static int checks;
    static int failures;
    static readonly string Key = "com.joan6694.illusionplugins.timeline";
    static void Check(string name, bool pass)
    {
        checks++;
        if (!pass) failures++;
        Console.WriteLine((pass ? "PASS " : "FAIL ") + name);
    }
    static void Put(BaseUnityPlugin instance) { Chainloader.PluginInfos[Key] = new PluginInfo { Instance = instance }; }
    static void Reset(Type fixture)
    {
        fixture.GetMethod("Reset").Invoke(null, null);
        Bridge.InitCalls = 0; Bridge.Delegate = null; Bridge.Populate = true; Bridge.ThrowOnInit = false;
        Chainloader.PluginInfos.Clear(); TimelineHost._self = null;
    }
    static bool Run(Type fixture, string method) { return (bool)fixture.GetMethod(method).Invoke(null, null); }
    static bool State(Type fixture) { return (bool)fixture.GetProperty("Ready").GetValue(null, null); }
    static int Finds(Type fixture) { return (int)fixture.GetField("FindCalls").GetValue(null); }
    static void One(Type f)
    {
        string n = f.Name + " ";
        Reset(f); Put(new TimelineHost(false, null));
        Check(n + "original body reproduces early-ready", Run(f, "EnsureReadyOriginal") && Bridge.InitCalls == 1 && State(f));
        Reset(f); Put(new TimelineHost(false, null));
        Check(n + "guard blocks UI-not-built without changing ready", !Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 0 && !State(f) && Finds(f) == 0);
        Reset(f);
        Check(n + "absent Timeline blocks without discovery", !Run(f, "EnsureReadyGuarded") && Finds(f) == 0 && Bridge.InitCalls == 0);
        Put(null);
        Check(n + "null instance blocks", !Run(f, "EnsureReadyGuarded"));
        var host = new TimelineHost(true, new UnityEngine.Object()); Put(host); host.destroyed = true;
        Check(n + "destroyed instance blocks", !Run(f, "EnsureReadyGuarded"));
        Reset(f); Put(new MissingSchemaHost());
        for (int i = 0; i < 100; i++) if (Run(f, "EnsureReadyGuarded")) throw new Exception("missing schema allowed");
        Check(n + "missing schema stable false on100 retries", Bridge.InitCalls == 0 && Finds(f) == 0);
        Reset(f); Put(new WrongTypeHost());
        Check(n + "wrong field types fail closed", !Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 0);
        Reset(f); host = new TimelineHost(true, null); Put(host);
        Check(n + "loaded true but toggle absent blocked", !Run(f, "EnsureReadyGuarded"));
        var toggle = new UnityEngine.Object(); toggle.destroyed = true; host.Set(true, toggle);
        Check(n + "destroyed UI blocked even stale loaded true", !Run(f, "EnsureReadyGuarded"));
        toggle = new UnityEngine.Object(); host.Set(false, toggle);
        Check(n + "UI object present but init incomplete blocked", !Run(f, "EnsureReadyGuarded"));
        host.Set(true, toggle); TimelineHost._self = new TimelineHost(true, new UnityEngine.Object());
        Check(n + "foreign self blocked", !Run(f, "EnsureReadyGuarded"));
        TimelineHost._self = host;
        Check(n + "later ready preserves original registration", Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 1 && State(f) && Finds(f) == 1);
        Check(n + "ready repeat does not reinitialize bridge", Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 1);
        host.Set(true, null);
        Check(n + "guard precedes cached ready fast path", !Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 1);
        host.Set(true, new UnityEngine.Object());
        Check(n + "restored UI keeps cached behavior", Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 1);
        Reset(f); Put(new TimelineHost(true, new UnityEngine.Object())); f.GetField("MissingApi").SetValue(null, true);
        Check(n + "original missing API false preserved", !Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 0);
        Reset(f); Put(new TimelineHost(true, new UnityEngine.Object())); Bridge.ThrowOnInit = true;
        Check(n + "original Init exception remains false", !Run(f, "EnsureReadyGuarded") && !State(f));
        Bridge.ThrowOnInit = false;
        Check(n + "retry after Init exception succeeds", Run(f, "EnsureReadyGuarded") && Bridge.InitCalls == 2);
        Reset(f); Put(new TimelineHost(true, new UnityEngine.Object())); Bridge.Populate = false;
        Check(n + "unpopulated bridge stays false", !Run(f, "EnsureReadyGuarded") && !State(f));
        Bridge.Populate = true;
        Check(n + "original retry allowed after incomplete bridge", Run(f, "EnsureReadyGuarded") && State(f));
    }
    public static int Main()
    {
        One(typeof(FixtureSoft)); One(typeof(FixtureLiquid));
        Console.WriteLine("TOTAL checks=" + checks + " failed=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
