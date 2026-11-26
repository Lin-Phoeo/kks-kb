using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CodexMEColorSpaceFix
{
    [BepInPlugin(GUID, "MaterialEditor ColorSpace Fix", "1.2.0")]
    [BepInDependency("com.deathweasel.bepinex.materialeditor", BepInDependency.DependencyFlags.HardDependency)]
    public class MEColorSpaceFixPlugin : BaseUnityPlugin
    {
        public const string GUID = "codex.kks.mecolorspacefix";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> VerboseLog;
        internal static ConfigEntry<bool> FixExport;
        internal static ConfigEntry<bool> SelfTestOnStart;
        internal static ConfigEntry<KeyboardShortcut> SelfTestHotkey;
        internal static ConfigEntry<string> NonColorNames;
        internal static ConfigEntry<string> NonColorSubstrings;
        internal static ConfigEntry<string> ForceColorNames;

        private static readonly HashSet<int> Sources = new HashSet<int>();
        private static readonly Dictionary<int, RenderTexture> LinearCache = new Dictionary<int, RenderTexture>();
        private static readonly HashSet<int> FailedSources = new HashSet<int>();
        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1. General", "Enabled", true,
                "关掉后行为完全回到 MaterialEditor 原样。已经导入过的贴图需要重新导入才会改变。");
            VerboseLog = Config.Bind("1. General", "Verbose log", false,
                "把每一次修正打进日志，用来确认哪些贴图走了 linear 路径。");
            FixExport = Config.Bind("1. General", "Fix export color space", true,
                "修 MaterialEditor 导出侧 MakeTextureReadable / SaveTex 的 RenderTexture 色彩空间。");
            SelfTestOnStart = Config.Bind("1. General", "Run roundtrip self-test on Studio start", false,
                "启动后延迟运行导入/导出往返自测。平时建议关闭，需要验证插件时再打开。");
            SelfTestHotkey = Config.Bind("1. General", "Roundtrip self-test hotkey",
                new KeyboardShortcut(KeyCode.F6, KeyCode.LeftControl, KeyCode.LeftShift),
                "手动运行导入/导出往返自测。");

            NonColorNames = Config.Bind("2. Rules", "Exact non-color property names",
                "NormalMap,NormalMapDetail,BumpMap,DetailNormalMap,NormalMask,"
                + "AlphaMask,ColorMask,DetailMask,LineMask,liquidmask,LiquidMask,"
                + "OcclusionMap,MetallicGlossMap,SpecularGlossMap,ParallaxMap,HeightMap",
                "逗号分隔。这些属性的贴图按数据贴图处理，不做 sRGB 解码。属性名不含前导下划线。");

            NonColorSubstrings = Config.Bind("2. Rules", "Non-color property substrings",
                "normal,bump,mask,occlusion,metallicgloss,parallax,height",
                "逗号分隔，不区分大小写。留空则只用精确名单。");

            ForceColorNames = Config.Bind("2. Rules", "Force color property names",
                "DetailAlbedoMap,MainTex,Texture2,Texture3",
                "优先级高于非颜色规则。名字里带 mask/detail 但其实是颜色贴图的放这里。");

            if (!Enabled.Value)
            {
                Logger.LogInfo("Disabled in config; MaterialEditor behavior is unchanged.");
                return;
            }

            try
            {
                var harmony = new Harmony(GUID);

                MethodInfo fromBytes = FindMethod("TextureContainerManager", "TextureFromBytes",
                    typeof(byte[]), typeof(TextureFormat), typeof(bool));
                if (fromBytes == null)
                {
                    Logger.LogError("Cannot find TextureContainerManager.TextureFromBytes; import patch is disabled.");
                    return;
                }
                Type token = fromBytes.DeclaringType.GetNestedType("Token", BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo destroy = token == null ? null : AccessTools.Method(token, "Destroy");
                if (destroy == null)
                {
                    Logger.LogError("Cannot find TextureContainerManager.Token.Destroy; import patch is disabled.");
                    return;
                }
                harmony.Patch(destroy, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches), "ReleaseSource")));
                harmony.Patch(fromBytes, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(Patches), "RecordSource")));

                MethodInfo setTex = FindMethod("MaterialAPI", "SetTexture",
                    typeof(GameObject), typeof(string), typeof(string), typeof(Texture));
                if (setTex == null)
                {
                    Logger.LogError("Cannot find MaterialAPI.SetTexture; import patch is disabled.");
                    return;
                }
                harmony.Patch(setTex, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(Patches), "FixColorSpace")));

                Logger.LogInfo("Import patch active: "
                    + fromBytes.DeclaringType.FullName + ".TextureFromBytes + "
                    + setTex.DeclaringType.FullName + ".SetTexture (GPU copy; no image re-decode; token lifecycle cleanup)");

                if (FixExport.Value) PatchExport(harmony);
                if (SelfTestOnStart.Value) StartCoroutine(DelayedSelfTest());
            }
            catch (Exception e)
            {
                Logger.LogError("Patch failed: " + e);
            }
        }

        private void Update()
        {
            if (Enabled.Value && SelfTestHotkey.Value.IsDown())
            {
                RunSelfTest();
            }
        }

        private IEnumerator DelayedSelfTest()
        {
            yield return new WaitForSeconds(5f);
            RunSelfTest();
        }

        private void RunSelfTest()
        {
            try
            {
                string directory = System.IO.Path.Combine(Paths.GameRootPath, "_codex_me_colorspacefix");
                RoundTripTest.Run(directory);
                StartCoroutine(RuntimeStress.Run(directory));
            }
            catch (Exception e)
            {
                Logger.LogError("Roundtrip self-test failed: " + e);
            }
        }

        private void PatchExport(Harmony harmony)
        {
            var prefix = new HarmonyMethod(
                AccessTools.Method(typeof(Patches), "FixExportColorSpace"));
            int done = 0;

            foreach (string name in new[] { "MakeTextureReadable", "SaveTex" })
            {
                MethodInfo m = FindMethodByName("MaterialEditorPluginBase", name,
                    p => p.Length >= 2
                         && typeof(Texture).IsAssignableFrom(p[0].ParameterType)
                         && p.Any(x => x.ParameterType == typeof(RenderTextureReadWrite)));
                if (m == null)
                {
                    Logger.LogWarning("Export patch: cannot find " + name + "; skipped.");
                    continue;
                }

                try
                {
                    harmony.Patch(m, prefix: prefix);
                    done++;
                }
                catch (Exception e)
                {
                    Logger.LogWarning("Export patch failed for " + name + ": " + e.Message);
                }
            }

            Logger.LogInfo("Export patch active: " + done + "/2 methods.");
        }

        private static MethodInfo FindMethod(string typeShortName, string methodName, params Type[] argTypes)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (Type t in types)
                {
                    if (t == null || t.Name != typeShortName) continue;
                    MethodInfo m = AccessTools.Method(t, methodName, argTypes);
                    if (m != null) return m;
                }
            }
            return null;
        }

        private static MethodInfo FindMethodByName(string typeShortName, string methodName,
            Func<ParameterInfo[], bool> match)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (Type t in types)
                {
                    if (t == null || t.Name != typeShortName) continue;
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                          | BindingFlags.Static | BindingFlags.Instance))
                    {
                        if (m.Name == methodName && match(m.GetParameters())) return m;
                    }
                }
            }
            return null;
        }

        internal static bool IsSrgbTexture(Texture tex)
        {
            RenderTexture rt = tex as RenderTexture;
            if (rt != null) return rt.sRGB;
            try
            {
                return UnityEngine.Experimental.Rendering.GraphicsFormatUtility
                    .IsSRGBFormat(tex.graphicsFormat);
            }
            catch
            {
                return true;
            }
        }

        internal static bool IsNonColor(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return false;
            string p = propertyName.TrimStart('_');

            if (Split(ForceColorNames.Value).Any(n => string.Equals(n, p, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (Split(NonColorNames.Value).Any(n => string.Equals(n, p, StringComparison.OrdinalIgnoreCase)))
                return true;

            string lower = p.ToLowerInvariant();
            return Split(NonColorSubstrings.Value).Any(s => lower.Contains(s.ToLowerInvariant()));
        }

        private static IEnumerable<string> Split(string csv)
        {
            if (string.IsNullOrEmpty(csv)) yield break;
            foreach (string s in csv.Split(','))
            {
                string t = s.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        internal static int CachedTextureCount { get { return LinearCache.Count; } }
        internal static int RecordedSourceCount { get { return Sources.Count; } }

        internal static void Remember(Texture result, byte[] bytes, TextureFormat format, bool mipmaps)
        {
            if (result != null && bytes != null) Sources.Add(result.GetInstanceID());
        }

        internal static void Release(Texture original)
        {
            if (ReferenceEquals(original, null)) return;
            int id = original.GetInstanceID();
            RenderTexture cached;
            if (LinearCache.TryGetValue(id, out cached))
            {
                LinearCache.Remove(id);
                if (cached != null)
                {
                    cached.Release();
                    UnityEngine.Object.Destroy(cached);
                }
            }
            Sources.Remove(id);
            FailedSources.Remove(id);
        }

        private void OnDestroy()
        {
            foreach (RenderTexture texture in LinearCache.Values.ToArray())
            {
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.Destroy(texture);
                }
            }
            LinearCache.Clear();
            Sources.Clear();
            FailedSources.Clear();
            new Harmony(GUID).UnpatchSelf();
        }

        internal static Texture BuildLinear(Texture original)
        {
            if (original == null) return null;
            int id = original.GetInstanceID();
            if (!Sources.Contains(id)) return null;
            if (!IsSrgbTexture(original)) return original;
            RenderTexture cached;
            if (LinearCache.TryGetValue(id, out cached) && cached != null) return cached;
            if (FailedSources.Contains(id)) return null;

            RenderTexture copy = null;
            try
            {
                RenderTexture source = original as RenderTexture;
                if (source == null || !source.IsCreated())
                    throw new InvalidOperationException("MaterialEditor source is not a created RenderTexture.");
                if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) == 0)
                    throw new NotSupportedException("GPU texture copy is unavailable.");

                if (source.dimension != UnityEngine.Rendering.TextureDimension.Tex2D || source.antiAliasing != 1)
                    throw new NotSupportedException("Only single-sample 2D MaterialEditor textures are supported.");
                RenderTextureDescriptor descriptor = source.descriptor;
                descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetLinearFormat(source.graphicsFormat);
                descriptor.depthBufferBits = 0;
                descriptor.autoGenerateMips = false;
                if (!GpuCopyGate.Allows(source.descriptor))
                    throw new NotSupportedException("The sRGB-to-linear GPU copy failed the pixel probe.");
                copy = new RenderTexture(descriptor);
                copy.filterMode = source.filterMode;
                copy.wrapModeU = source.wrapModeU;
                copy.wrapModeV = source.wrapModeV;
                copy.wrapModeW = source.wrapModeW;
                copy.anisoLevel = source.anisoLevel;
                copy.mipMapBias = source.mipMapBias;
                if (!copy.Create()) throw new InvalidOperationException("Cannot allocate the linear texture.");
                if (copy.graphicsFormat != descriptor.graphicsFormat || copy.mipmapCount != source.mipmapCount)
                    throw new NotSupportedException("Linear copy texture format or mip count differs from the source.");

                // Copy storage bits without shader sampling, sRGB decoding, or image re-decoding.
                Graphics.CopyTexture(source, copy);
                if (!copy.IsCreated()) throw new InvalidOperationException("GPU copy destination was lost.");
                LinearCache[id] = copy;
                return copy;
            }
            catch (Exception e)
            {
                FailedSources.Add(id);
                if (copy != null)
                {
                    copy.Release();
                    UnityEngine.Object.Destroy(copy);
                }
                Log.LogWarning("Linear GPU copy unavailable; original texture retained: " + e.Message);
                return null;
            }
        }

    }

    internal static class Patches
    {
        public static void ReleaseSource(Texture ____texture)
        {
            MEColorSpaceFixPlugin.Release(____texture);
        }

        public static void RecordSource(Texture __result, byte[] texBytes, TextureFormat format, bool mipmaps)
        {
            MEColorSpaceFixPlugin.Remember(__result, texBytes, format, mipmaps);
        }

        public static void FixColorSpace(string propertyName, ref Texture value)
        {
            if (value == null) return;
            if (!MEColorSpaceFixPlugin.IsNonColor(propertyName)) return;

            Texture linear = MEColorSpaceFixPlugin.BuildLinear(value);
            if (linear == null) return;

            if (MEColorSpaceFixPlugin.VerboseLog.Value)
            {
                MEColorSpaceFixPlugin.Log.LogInfo(
                    "Non-color texture rebuilt as linear: " + propertyName + " (" + linear.width + "x" + linear.height + ")");
            }
            value = linear;
        }

        public static void FixExportColorSpace(Texture tex, ref RenderTextureReadWrite cs)
        {
            if (tex == null) return;
            RenderTextureReadWrite want = MEColorSpaceFixPlugin.IsSrgbTexture(tex)
                ? RenderTextureReadWrite.sRGB
                : RenderTextureReadWrite.Linear;
            if (want == cs) return;

            if (MEColorSpaceFixPlugin.VerboseLog.Value)
            {
                MEColorSpaceFixPlugin.Log.LogInfo(
                    "Export color space corrected: " + cs + " -> " + want + " (" + tex.width + "x" + tex.height + ")");
            }
            cs = want;
        }
    }
}
