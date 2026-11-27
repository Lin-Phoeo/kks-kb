using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CodexMEColorSpaceFix
{
    internal static class RuntimeStress
    {
        private static void Check(bool passed, string label, StringBuilder report)
        {
            string text = (passed ? "PASS " : "FAIL ") + label;
            report.AppendLine(text);
            MEColorSpaceFixPlugin.Log.LogInfo(text);
            if (!passed) throw new InvalidOperationException(text);
        }

        internal static IEnumerator Run(string directory)
        {
            StringBuilder report = new StringBuilder("MaterialEditor GPU copy runtime regression\n");
            Type manager = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("KK_Plugins.TextureContainerManager", false)).FirstOrDefault(t => t != null);
            if (manager == null) { MEColorSpaceFixPlugin.Log.LogError("Runtime regression cannot locate TextureContainerManager."); yield break; }
            MethodInfo acquire = manager.GetMethod("Acquire", new[] { typeof(byte[]) });
            Type tokenType = manager.GetNestedType("Token");
            MethodInfo release = manager.GetMethod("Release", new[] { tokenType });
            PropertyInfo textureProperty = tokenType.GetProperty("Texture");
            for (int round = 0; round < 4; round++)
            {
                object token = null;
                var renderTargets = new SelfTestRenderTargetScope();
                try
                {
                    int size = round == 0 ? 256 : 4096;
                    int height = round == 0 ? 8 : 4096;
                    Texture2D image = new Texture2D(size, height, TextureFormat.RGBA32, false, true);
                    byte[] bytes;
                    try
                    {
                        Color32[] pixels = new Color32[size * height];
                        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(i % 256), (byte)((i / size) % 256), (byte)(255 - i % 256), 255);
                        image.SetPixels32(pixels);
                        image.Apply(false, false);
                        bytes = image.EncodeToPNG();
                    }
                    finally { UnityEngine.Object.Destroy(image); }
                    int sourceCount = MEColorSpaceFixPlugin.RecordedSourceCount;
                    int cacheCount = MEColorSpaceFixPlugin.CachedTextureCount;
                    token = acquire.Invoke(null, new object[] { bytes });
                    Texture original = (Texture)textureProperty.GetValue(token, null);
                    Texture linear = MEColorSpaceFixPlugin.BuildLinear(original);
                    Check(linear != null && !MEColorSpaceFixPlugin.IsSrgbTexture(linear), size + " linear result round " + round, report);
                    Check(ReferenceEquals(linear, MEColorSpaceFixPlugin.BuildLinear(original)), "cache reuse round " + round, report);
                    Texture color = original;
                    Patches.FixColorSpace("MainTex", ref color);
                    Check(ReferenceEquals(color, original), "color texture unchanged round " + round, report);
                    Check(original.filterMode == linear.filterMode && original.wrapModeU == linear.wrapModeU && original.wrapModeV == linear.wrapModeV && original.wrapModeW == linear.wrapModeW && original.anisoLevel == linear.anisoLevel && original.mipMapBias == linear.mipMapBias, "sampling properties round " + round, report);
                    if (round == 0)
                    {
                        Color32[] a = GpuCopyGate.ReadStored((RenderTexture)original);
                        Color32[] b = GpuCopyGate.ReadStored((RenderTexture)linear);
                        Check(a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x.r - y.r) <= 1 && Math.Abs(x.g - y.g) <= 1 && Math.Abs(x.b - y.b) <= 1 && Math.Abs(x.a - y.a) <= 1).All(x => x), "ramp stored pixel preservation", report);
                    }
                    renderTargets.Dispose();
                    release.Invoke(null, new[] { token });
                    token = null;
                    Check(MEColorSpaceFixPlugin.RecordedSourceCount == sourceCount && MEColorSpaceFixPlugin.CachedTextureCount == cacheCount, "token lifecycle releases cache round " + round, report);
                    if (!ReferenceEquals(linear, original))
                        Check(!((RenderTexture)linear).IsCreated(), "GPU output released round " + round, report);
                }
                catch (Exception e)
                {
                    report.AppendLine("FAIL " + e);
                    MEColorSpaceFixPlugin.Log.LogError("GPU copy runtime regression failed: " + e);
                    break;
                }
                finally
                {
                    renderTargets.Dispose();
                    if (token != null) release.Invoke(null, new[] { token });
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "me_gpu_copy_runtime_report.txt"), report.ToString(), Encoding.UTF8);
                }
                yield return new WaitForEndOfFrame();
                yield return Resources.UnloadUnusedAssets();
                GC.Collect();
            }
            MEColorSpaceFixPlugin.Log.LogInfo("GPU copy runtime regression finished; inspect me_gpu_copy_runtime_report.txt.");
        }
    }
}
