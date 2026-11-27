using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace Codex.StudioCoordinateNative
{
    [BepInPlugin(Guid, "服装载入原生中文 / Coordinate Native Chinese", "1.0.1")]
    [BepInProcess("CharaStudio")]
    [BepInDependency(NativeCoordinateStrings.PluginId, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(CoordinateConsumerCompatibility.DbdeId, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(CoordinateConsumerCompatibility.AssetImportId, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string Guid = "codex.studio.coordinatenative";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static long _served;
        private static bool _ready;
        private Harmony _harmony;
        private MethodInfo _target;
        private readonly List<MethodInfo> _consumers = new List<MethodInfo>();

        private void Awake()
        {
            _ready = false;
            try
            {
                var dependency = Chainloader.PluginInfos[NativeCoordinateStrings.PluginId];
                using (var stream = File.OpenRead(dependency.Location))
                    RequireHash(stream, NativeCoordinateStrings.ExpectedAssemblySha256);
                Assembly assembly = dependency.Instance.GetType().Assembly;
                using (Stream resource = assembly.GetManifestResourceStream(NativeCoordinateStrings.ResourceName))
                    RequireHash(resource, NativeCoordinateStrings.ExpectedEnglishResourceSha256);
                _target = assembly.GetType(NativeCoordinateStrings.TargetType, true).GetMethod(
                    "GetString", Flags, null, new[] { typeof(string), typeof(string) }, null);
                if (_target == null || _target.ReturnType != typeof(string))
                    throw new InvalidOperationException("Resource lookup signature differs");
                var pending = new List<MethodInfo>();
                foreach (string id in new[] { CoordinateConsumerCompatibility.DbdeId, CoordinateConsumerCompatibility.AssetImportId })
                {
                    BepInEx.PluginInfo consumer;
                    if (!Chainloader.PluginInfos.TryGetValue(id, out consumer)) continue;
                    using (var stream = File.OpenRead(consumer.Location))
                        CoordinateConsumerCompatibility.RequireIdentity(id, Hash(stream));
                    Assembly targetAssembly = consumer.Instance.GetType().Assembly;
                    string typeName = id == CoordinateConsumerCompatibility.DbdeId ?
                        "DynamicBoneDistributionEditor.DBDECharaController" : "AssetImport.AssetCharaController";
                    string methodName = id == CoordinateConsumerCompatibility.DbdeId ? "CheckCoordinateLoadOption" : "LoadCoordinate";
                    MethodInfo target = targetAssembly.GetType(typeName, true).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        .Single(method => method.Name == methodName);
                    if (target.ReturnType != typeof(void)) throw new InvalidOperationException("Consumer method signature differs");
                    CoordinateConsumerCompatibility.Rewrite(PatchProcessor.GetOriginalInstructions(target)).ToList();
                    pending.Add(target);
                }
                CoordinateConsumerCompatibility.AddMode = CoordinateConsumerCompatibility.BindMode(
                    assembly.GetType("CoordinateLoadOption.Patches", true).GetField("addAccModeFlag", Flags));
                _harmony = new Harmony(Guid);
                foreach (MethodInfo consumer in pending)
                {
                    _consumers.Add(consumer);
                    _harmony.Patch(consumer, transpiler: new HarmonyMethod(typeof(CoordinateConsumerCompatibility).GetMethod("Rewrite", Flags)));
                }
                _harmony.Patch(_target, prefix: new HarmonyMethod(typeof(Plugin).GetMethod("LookupPrefix", Flags)));
                _ready = true;
                CoordinateConsumerCompatibility.FirstDecision = (kind, value) =>
                    Logger.LogInfo("STUDIO_COORDINATE_COMPAT_DECISION kind=" + kind + " value=" + value);
                Logger.LogInfo("STUDIO_COORDINATE_NATIVE_READY keys=" + NativeCoordinateStrings.Count +
                    " source=resource-id process=CharaStudio consumers=" + _consumers.Count + " modeFieldBound=True");
            }
            catch (Exception error)
            {
                int cleanupFailures = Cleanup();
                Logger.LogWarning("STUDIO_COORDINATE_NATIVE_REFUSED " + error.GetType().Name + " cleanupFailures=" + cleanupFailures);
            }
        }

        private static void RequireHash(Stream stream, string expected)
        {
            if (stream == null) throw new InvalidOperationException("Resource missing");
            if (Hash(stream) != expected)
                throw new InvalidOperationException("Dependency or resource differs");
        }

        private static string Hash(Stream stream)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static bool LookupPrefix(string __0, string __1, ref string __result)
        {
            string value;
            if (!_ready) return true;
            if (!NativeCoordinateStrings.TryGet(__0, __1, out value)) return true;
            __result = value;
            _served++;
            return false;
        }

        private void OnDestroy()
        {
            int cleanupFailures = Cleanup();
            Logger.LogInfo("STUDIO_COORDINATE_NATIVE_STOP served=" + _served +
                " replacementChecks=" + CoordinateConsumerCompatibility.ReplacementChecks +
                " additionRejected=" + CoordinateConsumerCompatibility.AdditionChecks +
                " missingButtonChecks=" + CoordinateConsumerCompatibility.MissingButtonChecks +
                " emptySlotChecks=" + CoordinateConsumerCompatibility.EmptyChecks + " cleanupFailures=" + cleanupFailures);
        }

        private static int RemovePatches(IEnumerable<MethodInfo> targets, Action<MethodInfo> remove)
        {
            int failures = 0;
            foreach (MethodInfo target in targets)
                try { remove(target); } catch (Exception) { failures++; }
            return failures;
        }

        private int Cleanup()
        {
            _ready = false;
            try
            {
                if (_harmony == null) return 0;
                var targets = new List<MethodInfo>();
                if (_target != null) targets.Add(_target);
                targets.AddRange(_consumers);
                return RemovePatches(targets, method => _harmony.Unpatch(method, HarmonyPatchType.All, Guid));
            }
            finally
            {
                _consumers.Clear();
                CoordinateConsumerCompatibility.AddMode = null;
                CoordinateConsumerCompatibility.FirstDecision = null;
                _harmony = null;
                _target = null;
            }
        }
    }
}
