using System;
using System.IO;
using UnityEngine;

namespace GreyExportRepair
{
    internal static class ExportSafety
    {
        internal static bool RequireWrite(bool result)
        {
            if (!result) throw new IOException("A texture could not be written; export was stopped.");
            return true;
        }
        internal static IntPtr RequireMesh(IntPtr mesh)
        {
            if (mesh == IntPtr.Zero) throw new InvalidOperationException("FBX mesh conversion failed; export was stopped.");
            return mesh;
        }
        internal static string UniqueDirectory(string requested)
        {
            string prefix = Path.GetFullPath(requested).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate;
            do { candidate = prefix + "_" + Guid.NewGuid().ToString("N"); }
            while (Directory.Exists(candidate) || File.Exists(candidate));
            Directory.CreateDirectory(candidate);
            return candidate + Path.DirectorySeparatorChar;
        }
        internal static void DestroyOwned(Texture2D texture)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
        internal static void FinishTexture(RenderTexture previous, RenderTexture temporary, Texture2D original,
            Texture2D current, Texture2D converted)
        {
            try { RenderTexture.active = previous; }
            finally
            {
                try { if (temporary != null) RenderTexture.ReleaseTemporary(temporary); }
                finally
                {
                    try { DestroyOwned(original); }
                    finally
                    {
                        try { if (!System.Object.ReferenceEquals(current, original)) DestroyOwned(current); }
                        finally
                        {
                            if (!System.Object.ReferenceEquals(converted, original) && !System.Object.ReferenceEquals(converted, current))
                                DestroyOwned(converted);
                        }
                    }
                }
            }
        }
        internal static void FinishSkinned(SkinnedMeshRenderer renderer, float[] weights, int captured, Mesh ownedMesh,
            ref SkinnedMeshRenderer cachedRenderer, ref Mesh cachedMesh, ref float[] cachedWeights)
        {
            Exception failure = null;
            bool ownRenderer = (ownedMesh != null && System.Object.ReferenceEquals(cachedMesh, ownedMesh)) ||
                (weights != null && System.Object.ReferenceEquals(cachedWeights, weights));
            try
            {
                if (renderer != null && weights != null)
                    for (int index = 0; index < captured; index++)
                        try { renderer.SetBlendShapeWeight(index, weights[index]); }
                        catch (Exception error) { if (failure == null) failure = error; }
            }
            finally
            {
                try { if (ownedMesh != null) UnityEngine.Object.DestroyImmediate(ownedMesh, false); }
                finally
                {
                    if (ownRenderer && System.Object.ReferenceEquals(cachedRenderer, renderer)) cachedRenderer = null;
                    if (!System.Object.ReferenceEquals(ownedMesh, null) && System.Object.ReferenceEquals(cachedMesh, ownedMesh)) cachedMesh = null;
                    if (weights != null && System.Object.ReferenceEquals(cachedWeights, weights)) cachedWeights = null;
                }
            }
            if (failure != null) throw failure;
        }
    }
}
