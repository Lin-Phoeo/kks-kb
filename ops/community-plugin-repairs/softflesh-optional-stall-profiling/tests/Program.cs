using System.Text.Json;

namespace Synthetic;

public sealed class Plugin
{
    public static readonly object ExpectedLogger = new();
    public static object? SLog;
    public static int LoggerCalls;
    public object Logger { get { LoggerCalls++; return ExpectedLogger; } }
    public void AwakePrefix() => throw new InvalidOperationException("Actual CIL was not extracted");
}
public sealed class HarmonyStub
{
    public static int Calls;
    public static bool Throw;
    public void UnpatchSelf() { Calls++; if (Throw) throw new InvalidOperationException("synthetic unpatch fault"); }
}
public static class Spy
{
    public static HarmonyStub? Handle;
    public static int ApplyCalls;
    public static void Apply() { ApplyCalls++; Handle = new HarmonyStub(); }
    public static void Remove() => throw new InvalidOperationException("Actual Remove CIL was not extracted");
}
public static class Program
{
    public static int Main(string[] args)
    {
        int expectedCalls = int.Parse(args[0]);
        var results = new List<object>(); int failures = 0;
        void Check(string name, bool value) { results.Add(new { name, passed = value }); if (!value) failures++; }
        var plugin = new Plugin();
        plugin.AwakePrefix();
        Check("actual_Awake_prefix_preserves_logger_call", Plugin.LoggerCalls == 1);
        Check("actual_Awake_prefix_preserves_logger_field", ReferenceEquals(Plugin.SLog, Plugin.ExpectedLogger));
        Check("actual_diagnostic_apply_count", Spy.ApplyCalls == expectedCalls);
        Check("actual_diagnostic_handle_state", (Spy.Handle != null) == (expectedCalls != 0));
        Spy.Handle = null; HarmonyStub.Calls = 0; HarmonyStub.Throw = false;
        Spy.Remove();
        Check("actual_Remove_absent_handle_no_call", HarmonyStub.Calls == 0 && Spy.Handle == null);
        Spy.Handle = new HarmonyStub(); Spy.Remove();
        Check("actual_Remove_present_handle_once", HarmonyStub.Calls == 1 && Spy.Handle == null);
        Spy.Remove();
        Check("actual_Remove_repeat_no_call", HarmonyStub.Calls == 1 && Spy.Handle == null);
        Spy.Handle = new HarmonyStub(); HarmonyStub.Throw = true;
        bool escaped = false; try { Spy.Remove(); } catch { escaped = true; }
        Check("actual_Remove_original_exception_contract", !escaped && HarmonyStub.Calls == 2 && Spy.Handle == null);
        Console.WriteLine(JsonSerializer.Serialize(new { checks = results.Count, failed = failures, results }));
        return failures == 0 ? 0 : 1;
    }
}
