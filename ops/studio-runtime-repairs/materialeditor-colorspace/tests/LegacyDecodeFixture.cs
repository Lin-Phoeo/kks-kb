// Synthetic fixture reproducing the old second-decode and retained-cache pattern.
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace CodexMEColorSpaceFix
{
    internal static class MEColorSpaceFixPlugin
    {
        internal static ManualLogSource Log;
        private struct SourceInfo
        {
            internal byte[] Bytes;
            internal TextureFormat Format;
            internal bool Mipmaps;
        }
        private static readonly Dictionary<Texture, SourceInfo> SourceOf = new Dictionary<Texture, SourceInfo>();
        private static readonly Dictionary<byte[], Texture> LinearCache = new Dictionary<byte[], Texture>();
        internal static void Remember(Texture result, byte[] bytes, TextureFormat format, bool mipmaps)
        {
            SourceOf[result] = new SourceInfo { Bytes = bytes, Format = format, Mipmaps = mipmaps };
        }
        internal static Texture BuildLinear(Texture original)
        {
            SourceInfo source = SourceOf[original];
            Texture cached;
            if (LinearCache.TryGetValue(source.Bytes, out cached)) return cached;
            Texture2D decoded = new Texture2D(2, 2, source.Format, source.Mipmaps, true);
            decoded.LoadImage(source.Bytes);
            RenderTexture result = new RenderTexture(decoded.width, decoded.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(decoded, result);
            UnityEngine.Object.Destroy(decoded);
            LinearCache[source.Bytes] = result;
            return result;
        }
    }
}
