using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Codex.VanityCompatibility
{
    internal static class PseudoMakerIdentity
    {
        internal static bool Supports(string actualVersion, string originalVersion, Assembly assembly)
        {
            if (actualVersion == originalVersion) return true;
            if (actualVersion != "1.5.2.0" || assembly == null || assembly.GetName().Name != "KKS_StudioPseudoMaker") return false;
            return IsReviewedHash(ReadHash(assembly));
        }

        internal static bool IsReviewedHash(string hash)
        {
            return hash == "72e1c3a3dcb2120225f2d2823729cea81b2a2dffc8243de63ac6a4c901061882";
        }

        private static string ReadHash(Assembly assembly)
        {
            using (var stream = File.OpenRead(assembly.Location))
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
