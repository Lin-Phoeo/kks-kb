using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace Codex.StudioCoordinateNative
{
    [BepInPlugin(Guid, "服装载入原生中文 / Coordinate Native Chinese", "1.0.0")]
    [BepInProcess("CharaStudio")]
    [BepInDependency(NativeCoordinateStrings.PluginId, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string Guid = "codex.studio.coordinatenative";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static long _served;
        private Harmony _harmony;
        private MethodInfo _target;

        private void Awake()
        {
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
                _harmony = new Harmony(Guid);
                _harmony.Patch(_target, prefix: new HarmonyMethod(typeof(Plugin).GetMethod("LookupPrefix", Flags)));
                Logger.LogInfo("STUDIO_COORDINATE_NATIVE_READY keys=" + NativeCoordinateStrings.Count +
                    " source=resource-id process=CharaStudio");
            }
            catch (Exception error)
            {
                Cleanup();
                Logger.LogWarning("STUDIO_COORDINATE_NATIVE_REFUSED " + error.GetType().Name);
            }
        }

        private static void RequireHash(Stream stream, string expected)
        {
            if (stream == null) throw new InvalidOperationException("Resource missing");
            using (var algorithm = SHA256.Create())
                if (BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != expected)
                    throw new InvalidOperationException("Dependency or resource differs");
        }

        private static bool LookupPrefix(string __0, string __1, ref string __result)
        {
            string value;
            if (!NativeCoordinateStrings.TryGet(__0, __1, out value)) return true;
            __result = value;
            _served++;
            return false;
        }

        private void OnDestroy()
        {
            Logger.LogInfo("STUDIO_COORDINATE_NATIVE_STOP served=" + _served);
            Cleanup();
        }

        private void Cleanup()
        {
            if (_harmony != null && _target != null)
                _harmony.Unpatch(_target, HarmonyPatchType.Prefix, Guid);
            _harmony = null;
            _target = null;
        }
    }
}
