using System.Reflection;
using System.Text.Json;

public class DynamicBone
{
    private void UpdateDynamicBones(float step) { }
    private void UpdateDynamicBones(int step) { }
}
namespace AntiGravity
{
    public sealed class TimelinePhysics
    {
        internal readonly HarmonyLib.Harmony _harmony;
        public TimelinePhysics(HarmonyLib.Harmony h) { _harmony = h; }
        public void Dispose() { throw new NotImplementedException("Actual Dispose CIL must be inserted"); }
        public sealed class BoneRestore { }
        private static Exception AfterBone(Exception __exception, BoneRestore __state, bool __runOriginal) => __exception;
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static bool Missing;
        public static readonly List<string> Queries = new();
        public static MethodInfo Method(Type type, string name, Type[] parameters, Type[] generics)
        {
            Queries.Add(type.FullName + ":" + name + ":" + string.Join(",", parameters.Select(t => t.FullName)) + ":" + (generics == null));
            return Missing ? null : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, parameters, null);
        }
    }
    public sealed class Harmony
    {
        public readonly List<string> Calls = new();
        public bool Prefix = true, Finalizer = true, Timeline = true, Other = true;
        public bool ThrowUnpatch, ThrowSelf, ExactTarget, ExactFinalizer;
        public readonly Exception Failure = new InvalidOperationException("Synthetic boundary failure");
        public void Unpatch(MethodBase target, MethodInfo patch)
        {
            Calls.Add("Unpatch");
            ExactTarget = target.DeclaringType == typeof(DynamicBone) && target.Name == "UpdateDynamicBones" && target.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(float) });
            ExactFinalizer = patch.DeclaringType == typeof(AntiGravity.TimelinePhysics) && patch.Name == "AfterBone" && patch.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "__exception", "__state", "__runOriginal" });
            if (!ExactTarget || !ExactFinalizer) throw new Exception("Wrong hook boundary");
            if (ThrowUnpatch) throw Failure;
            Finalizer = false;
        }
        public void UnpatchSelf()
        {
            Calls.Add("UnpatchSelf");
            if (ThrowSelf) throw Failure;
            Prefix = false;
            // This is an explicit lifecycle contract stub, not execution of Harmony's emitter.
            if (Finalizer) throw new InvalidOperationException("Synthetic model: finalizer still depends on removed prefix");
            Timeline = false;
        }
    }
}
public static class Program
{
    static readonly List<object> Checks = new();
    static int passed, failed;
    static void C(string name, bool value) { if (value) passed++; else failed++; Checks.Add(new { name, passed = value }); }
    static Exception Call(AntiGravity.TimelinePhysics p) { try { p.Dispose(); return null; } catch (Exception e) { return e; } }
    static HarmonyLib.Harmony New(bool missing = false)
    {
        HarmonyLib.AccessTools.Missing = missing; HarmonyLib.AccessTools.Queries.Clear(); return new HarmonyLib.Harmony();
    }
    public static int Main()
    {
        var h = New(); var p = new AntiGravity.TimelinePhysics(h); var e = Call(p);
        C("normal dispose returns", e == null);
        C("exact finalizer before original owner cleanup", h.Calls.SequenceEqual(new[] { "Unpatch", "UnpatchSelf" }));
        C("float target overload selected", h.ExactTarget);
        C("exact own finalizer selected", h.ExactFinalizer);
        C("own prefix finalizer and timeline removed", !h.Prefix && !h.Finalizer && !h.Timeline);
        C("unrelated owner retained", h.Other);
        C("lookup matches original constructor", HarmonyLib.AccessTools.Queries.SequenceEqual(new[] { "DynamicBone:UpdateDynamicBones:System.Single:True" }));
        h.Calls.Clear(); e = Call(p);
        C("repeat dispose returns", e == null);
        C("repeat uses same order", h.Calls.SequenceEqual(new[] { "Unpatch", "UnpatchSelf" }));
        h = New(); h.Prefix = h.Finalizer = false; e = Call(new AntiGravity.TimelinePhysics(h));
        C("absent own bone hook still cleans timeline", e == null && !h.Timeline && h.Other);
        h = New(true); h.Prefix = h.Finalizer = false; e = Call(new AntiGravity.TimelinePhysics(h));
        C("missing target follows original cleanup", e == null && h.Calls.SequenceEqual(new[] { "UnpatchSelf" }) && !h.Timeline);
        h = New(); h.ThrowUnpatch = true; e = Call(new AntiGravity.TimelinePhysics(h));
        C("unpatch failure propagates original exception", ReferenceEquals(e, h.Failure));
        C("unpatch failure does not run later cleanup", h.Calls.SequenceEqual(new[] { "Unpatch" }) && h.Prefix && h.Finalizer && h.Timeline);
        h = New(); h.ThrowSelf = true; e = Call(new AntiGravity.TimelinePhysics(h));
        C("original cleanup failure propagates", ReferenceEquals(e, h.Failure));
        C("finalizer removed before failing original cleanup", h.Calls.SequenceEqual(new[] { "Unpatch", "UnpatchSelf" }) && !h.Finalizer && h.Timeline && h.Other);
        Console.WriteLine(JsonSerializer.Serialize(new { passed, failed, actualDisposeCil = true, genuineHarmonyExecution = false, checks = Checks }));
        return failed == 0 ? 0 : 1;
    }
}
