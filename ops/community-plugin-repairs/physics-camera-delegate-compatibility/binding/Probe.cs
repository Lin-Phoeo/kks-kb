using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
public static class BindingProbe
{
    public static int Run(string candidate, string unused, string game, string tokens)
    {
        var roots = new[] { Path.GetDirectoryName(candidate), Path.Combine(game, "BepInEx/core"), Path.Combine(game, "BepInEx/plugins"), Path.Combine(game, "CharaStudio_Data/Managed") };
        ResolveEventHandler resolver = (s, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (var root in roots) { var path = Path.Combine(root, name); if (File.Exists(path)) return Assembly.LoadFrom(path); }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        int checks = 0, failures = 0;
        Action<string, bool> check = (name, value) => { checks++; if (!value) failures++; Console.WriteLine((value ? "PASS " : "FAIL ") + name); };
        try
        {
            var a = Assembly.LoadFrom(candidate);
            var privateRoot = Path.Combine(Path.GetDirectoryName(candidate), "binding-data-" + a.GetName().Name);
            Directory.CreateDirectory(privateRoot);
            var bepinex = Assembly.LoadFrom(Path.Combine(game, "BepInEx/core/BepInEx.dll"));
            var paths = bepinex.GetType("BepInEx.Paths", true);
            paths.GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Path.Combine(privateRoot, "BindingOnly.exe"), Path.Combine(privateRoot, "BepInEx"), Path.Combine(game, "CharaStudio_Data/Managed"), new string[0] });
            var helper = a.GetType("TimelineUiReadiness", true);
            check("helper_owned_by_exact_candidate", helper.Assembly == a);
            var ready = helper.GetMethod("IsReady", BindingFlags.NonPublic | BindingFlags.Static);
            RuntimeHelpers.PrepareMethod(ready.MethodHandle);
            check("actual_injected_helper_jits_and_returns_false_without_Timeline", (bool)ready.Invoke(null, null) == false);
            var bridge = a.GetType(a.GetName().Name.StartsWith("LiquidFluid") ? "LiquidFluid.LfTimeline" : "SoftFlesh.SfTimeline", true);
            var ensure = bridge.GetMethod("EnsureReady", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            RuntimeHelpers.PrepareMethod(ensure.MethodHandle);
            check("actual_patched_EnsureReady_jits_and_returns_false", (bool)ensure.Invoke(null, null) == false);
            var type = a.GetType(a.GetName().Name.StartsWith("LiquidFluid") ? "LiquidFluid.LiquidFluidPlugin" : "SoftFlesh.SoftFleshPlugin", true);
            var late = type.GetMethod("LateUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            RuntimeHelpers.PrepareMethod(late.MethodHandle);
            check("actual_camera_method_jits_without_creating_plugin", true);
        }
        catch (Exception e)
        {
            var actual = e is TargetInvocationException ? e.InnerException : e;
            check("actual_mono_binding_exception_" + actual.GetType().FullName, false);
            Console.WriteLine(actual.Message);
            while (actual.InnerException != null) { actual = actual.InnerException; Console.WriteLine("INNER " + actual.GetType().FullName + ": " + actual.Message); }
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= resolver; }
        Console.WriteLine("bindingChecks=" + checks + " failures=" + failures + " runtime=" + Environment.Version);
        return failures == 0 ? 0 : 1;
    }
}
