using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using Mono.Cecil;

internal static class ConsumerRegression
{
    private static bool _syntheticAddMode;

    internal static object Run(string game, Assembly adapter)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Type compatibility = adapter.GetType("Codex.StudioCoordinateNative.CoordinateConsumerCompatibility", true);
        MethodInfo rewrite = compatibility.GetMethod("Rewrite", flags);
        MethodInfo requireIdentity = compatibility.GetMethod("RequireIdentity", flags);
        MethodInfo bindMode = compatibility.GetMethod("BindMode", flags);
        FieldInfo modeReader = compatibility.GetField("AddMode", flags);
        modeReader.SetValue(null, bindMode.Invoke(null, new object[] { typeof(ConsumerRegression).GetField("_syntheticAddMode", flags) }));
        var observations = new List<string>();
        FieldInfo firstDecision = compatibility.GetField("FirstDecision", flags);
        firstDecision.SetValue(null, (Action<string, bool>)((kind, value) => observations.Add(kind + ":" + value)));
        var checks = new List<object>();
        void Check(string name, bool value)
        {
            checks.Add(new { name, passed = value });
            if (!value) throw new InvalidOperationException(name);
        }
        void Rejects(string name, Action action)
        {
            bool rejected = false;
            try { action(); }
            catch (TargetInvocationException error) { rejected = error.InnerException is InvalidOperationException; }
            Check(name, rejected);
        }
        var targets = new[] {
            new { id = "org.njaecha.plugins.dbde", file = "BepInEx/plugins/KKS_DynamicBoneDistributionEditor.dll",
                type = "DynamicBoneDistributionEditor.DBDECharaController", method = "CheckCoordinateLoadOption" },
            new { id = "org.njaecha.plugins.assetimport", file = "BepInEx/plugins/AssetImport/KKS_AssetImport.dll",
                type = "AssetImport.AssetCharaController", method = "LoadCoordinate" }
        };
        var sites = new List<object>();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)).ToDictionary(opcode => opcode.Value);
        foreach (var target in targets)
        {
            string path = Path.Combine(game, target.file);
            string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            requireIdentity.Invoke(null, new object[] { target.id, sha });
            Check(target.id + " reviewed identity accepted", true);
            Rejects(target.id + " unknown version rejected", () => requireIdentity.Invoke(null, new object[] { target.id, new string('0', 64) }));
            using var metadata = AssemblyDefinition.ReadAssembly(path);
            MethodDefinition method = metadata.MainModule.GetType(target.type).Methods.Single(item => item.Name == target.method);
            List<CodeInstruction> original = method.Body.Instructions.Select(instruction => {
                object operand = instruction.Operand;
                if (operand is MethodReference reference && reference.DeclaringType.FullName == "System.String" &&
                    (reference.Name == "op_Equality" || reference.Name == "op_Inequality"))
                    operand = typeof(string).GetMethod(reference.Name, new[] { typeof(string), typeof(string) });
                return new CodeInstruction(opcodes[instruction.OpCode.Value], operand);
            }).ToList();
            int modeIndex = original.FindIndex(instruction => instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "Replace Mode"));
            int emptyIndex = original.FindIndex(instruction => instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "Empty"));
            Check(target.id + " actual comparison sites found", modeIndex >= 0 && emptyIndex >= 0);
            var labelHost = new DynamicMethod("MetadataFixture", typeof(void), Type.EmptyTypes);
            original[modeIndex + 1].labels.Add(labelHost.GetILGenerator().DefineLabel());
            original[modeIndex + 1].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            var patched = ((IEnumerable<CodeInstruction>)rewrite.Invoke(null, new object[] { original })).ToList();
            Check(target.id + " instruction count unchanged", patched.Count == original.Count);
            int changed = 0;
            for (int index = 0; index < original.Count; index++)
            {
                bool comparison = index == modeIndex + 1 || index == emptyIndex + 1;
                if (!ReferenceEquals(original[index], patched[index])) changed++;
                if (!comparison && !ReferenceEquals(original[index], patched[index]))
                    throw new InvalidOperationException("Unrelated consumer instruction changed");
                if (original[index].opcode != patched[index].opcode || !original[index].labels.SequenceEqual(patched[index].labels) ||
                    !original[index].blocks.SequenceEqual(patched[index].blocks))
                    throw new InvalidOperationException("Consumer instruction metadata changed");
            }
            Check(target.id + " exactly two calls replaced with labels and blocks intact", changed == 2);
            Func<string, string, bool> Before(int index) => Compile((MethodInfo)original[index + 1].operand);
            Func<string, string, bool> After(int index) => Compile((MethodInfo)patched[index + 1].operand);
            var oldMode = Before(modeIndex); var mode = After(modeIndex);
            var oldEmpty = Before(emptyIndex); var empty = After(emptyIndex);
            _syntheticAddMode = false;
            Check(target.id + " compiled mode getter reads real field shape", ((Func<bool>)modeReader.GetValue(null))() == _syntheticAddMode);
            Check(target.id + " red baseline rejects Chinese replacement", oldMode("替换模式", "Replace Mode"));
            Check(target.id + " green Chinese replacement accepted", !mode("替换模式", "Replace Mode"));
            Check(target.id + " English replacement preserved", !mode("Replace Mode", "Replace Mode"));
            Check(target.id + " missing button still refused", mode(null, "Replace Mode") && mode("", "Replace Mode"));
            _syntheticAddMode = true;
            Check(target.id + " actual addition remains unsupported", mode("追加模式", "Replace Mode") && mode("Add Mode", "Replace Mode"));
            Check(target.id + " real mode wins over stale translated label", mode("Replace Mode", "Replace Mode"));
            Check(target.id + " red baseline imports Chinese empty slot", !oldEmpty("空", "Empty"));
            Check(target.id + " green Chinese empty slot excluded", empty("空", "Empty"));
            Check(target.id + " original Empty excluded", empty("Empty", "Empty"));
            Check(target.id + " other labels unchanged", !empty("空饰品", "Empty") && !empty("Unrecognized", "Empty") && !empty(null, "Empty"));
            modeReader.SetValue(null, null);
            Check(target.id + " unavailable mode reader preserves original comparison", mode("替换模式", "Replace Mode") && !mode("Replace Mode", "Replace Mode"));
            modeReader.SetValue(null, bindMode.Invoke(null, new object[] { typeof(ConsumerRegression).GetField("_syntheticAddMode", flags) }));
            var invalid = original.Select(instruction => new CodeInstruction(instruction)).ToList();
            invalid[modeIndex].operand = "Unknown Mode";
            Rejects(target.id + " changed IL pattern rejected", () => rewrite.Invoke(null, new object[] { invalid }));
            var duplicate = new List<CodeInstruction>(original) { new CodeInstruction(OpCodes.Ldstr, "Empty"), new CodeInstruction(OpCodes.Call, original[emptyIndex + 1].operand) };
            Rejects(target.id + " duplicate comparison rejected", () => rewrite.Invoke(null, new object[] { duplicate }));
            sites.Add(new {
                pluginId = target.id, sha256 = sha, method = method.FullName,
                modeLiteralOffset = method.Body.Instructions[modeIndex].Offset,
                modeComparisonOffset = method.Body.Instructions[modeIndex + 1].Offset,
                emptyLiteralOffset = method.Body.Instructions[emptyIndex].Offset,
                emptyComparisonOffset = method.Body.Instructions[emptyIndex + 1].Offset,
                changedInstructions = changed
            });
        }
        Rejects("unknown consumer id rejected", () => requireIdentity.Invoke(null, new object[] { "unknown.consumer", new string('0', 64) }));
        Rejects("missing real mode field rejected", () => bindMode.Invoke(null, new object[] { null }));
        Check("first decisions recorded once across both consumers", observations.Count == 4 && observations.Distinct().Count() == 4);
        compatibility.GetField("_reportedDecisions", flags).SetValue(null, 0);
        firstDecision.SetValue(null, (Action<string, bool>)((kind, value) => throw new IOException("synthetic log sink")));
        _syntheticAddMode = false;
        var modeCheck = (Func<string, string, bool>)compatibility.GetMethod("IsUnsupportedMode", flags)
            .CreateDelegate(typeof(Func<string, string, bool>));
        Check("diagnostic sink failure cannot interrupt replacement", !modeCheck("替换模式", "Replace Mode"));
        Type plugin = adapter.GetType("Codex.StudioCoordinateNative.Plugin", true);
        MethodInfo[] cleanupTargets = { typeof(string).GetMethod("op_Equality"), typeof(string).GetMethod("op_Inequality"), typeof(string).GetMethod("IsNullOrEmpty") };
        var attempted = new List<MethodInfo>();
        Action<MethodInfo> failingRemove = target => {
            attempted.Add(target);
            if (target == cleanupTargets[0]) throw new InvalidOperationException("synthetic first unpatch failure");
        };
        int cleanupFailures = (int)plugin.GetMethod("RemovePatches", flags).Invoke(null, new object[] { cleanupTargets, failingRemove });
        Check("unpatch failure still attempts every target", cleanupFailures == 1 && attempted.SequenceEqual(cleanupTargets));
        object fixture = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(plugin);
        const BindingFlags instanceFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        plugin.GetField("_consumers", instanceFlags).SetValue(fixture, new List<MethodInfo>(cleanupTargets));
        plugin.GetField("_target", instanceFlags).SetValue(fixture, cleanupTargets[0]);
        plugin.GetField("_ready", flags).SetValue(null, true);
        plugin.GetMethod("Cleanup", instanceFlags).Invoke(fixture, null);
        Check("cleanup always clears native readiness and callbacks", !(bool)plugin.GetField("_ready", flags).GetValue(null) &&
            modeReader.GetValue(null) == null && firstDecision.GetValue(null) == null &&
            ((List<MethodInfo>)plugin.GetField("_consumers", instanceFlags).GetValue(fixture)).Count == 0 &&
            plugin.GetField("_target", instanceFlags).GetValue(fixture) == null);
        firstDecision.SetValue(null, null);
        modeReader.SetValue(null, null);
        _syntheticAddMode = false;
        return new {
            passed = true, checks = checks.Count, results = checks, sites,
            comparisonExecution = "Actual installed IL comparison calls and actual adapter replacement methods emitted into managed delegates; mutable mode fixture uses the same compiled field-reader factory.",
            limitation = "Does not execute Unity UI creation, actual garment/asset transfer or full patched methods. Exact label 空 is treated as empty only in the two guarded consumers; an actual accessory with that exact display name shares the ambiguity already present for Empty."
        };
    }

    private static Func<string, string, bool> Compile(MethodInfo comparison)
    {
        var method = new DynamicMethod("CoordinateComparisonFixture", typeof(bool), new[] { typeof(string), typeof(string) }, comparison.DeclaringType, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, comparison);
        il.Emit(OpCodes.Ret);
        return (Func<string, string, bool>)method.CreateDelegate(typeof(Func<string, string, bool>));
    }
}
