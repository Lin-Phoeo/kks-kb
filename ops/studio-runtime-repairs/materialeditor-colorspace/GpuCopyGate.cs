using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CodexMEColorSpaceFix
{
    internal static class GpuCopyGate
    {
        private static readonly Dictionary<GraphicsFormat, bool> Results = new Dictionary<GraphicsFormat, bool>();

        internal static bool Allows(RenderTextureDescriptor input)
        {
            bool result;
            GraphicsFormat sourceFormat = input.graphicsFormat;
            if (Results.TryGetValue(input.graphicsFormat, out result)) return result;
            RenderTexture source = null, target = null;
            RenderTexture previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite;
            try
            {
                input.width = 4;
                input.height = 4;
                input.depthBufferBits = 0;
                input.useMipMap = false;
                input.autoGenerateMips = false;
                input.mipCount = 1;
                input.msaaSamples = 1;
                source = new RenderTexture(input);
                input.graphicsFormat = GraphicsFormatUtility.GetLinearFormat(sourceFormat);
                target = new RenderTexture(input);
                if (!source.Create() || !target.Create()) throw new InvalidOperationException("Probe allocation failed.");
                GL.sRGBWrite = false;
                RenderTexture.active = target;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = source;
                GL.Clear(false, true, new Color(0.23f, 0.51f, 0.79f, 0.37f));
                Color32[] expected = ReadStored(source);
                Graphics.CopyTexture(source, target);
                Color32[] actual = ReadStored(target);
                result = source.IsCreated() && target.IsCreated() && expected.Length == actual.Length;
                for (int i = 0; result && i < expected.Length; i++)
                    result = Math.Abs(expected[i].r - actual[i].r) <= 1 && Math.Abs(expected[i].g - actual[i].g) <= 1
                        && Math.Abs(expected[i].b - actual[i].b) <= 1 && Math.Abs(expected[i].a - actual[i].a) <= 1;
                result = result && expected.Length > 0 && expected[0].r > 0 && expected[0].g > 0;
                Results[sourceFormat] = result;
                MEColorSpaceFixPlugin.Log.LogInfo("Linear GPU copy pixel probe: " + sourceFormat + " => " + (result ? "PASS" : "FAIL; original textures retained"));
                return result;
            }
            catch (Exception e)
            {
                Results[sourceFormat] = false;
                MEColorSpaceFixPlugin.Log.LogWarning("Linear GPU copy pixel probe unavailable: " + e.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousWrite;
                if (source != null) { source.Release(); UnityEngine.Object.Destroy(source); }
                if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            }
        }

        internal static Color32[] ReadStored(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                RenderTexture.active = source;
                readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, !source.sRGB);
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readback.Apply(false, false);
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null) UnityEngine.Object.Destroy(readback);
            }
        }
    }
}
