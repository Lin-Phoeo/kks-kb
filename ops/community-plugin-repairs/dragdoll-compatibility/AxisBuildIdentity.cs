using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Codex.DragDollCompatibility
{
    internal static class AxisBuildIdentity
    {
        internal static bool IsKnownBase(Type type)
        {
            if (type == null || type.FullName != "StudioExtraMoveAxis.StudioExtraMoveAxisPlugin" ||
                type.Assembly.GetName().Name != "KKS_StudioExtraMoveAxis") return false;
            return Matches(type, ReadHash(type.Assembly));
        }

        internal static bool Matches(Type type, string hash)
        {
            return type != null && type.FullName == "StudioExtraMoveAxis.StudioExtraMoveAxisPlugin" &&
                type.Assembly.GetName().Name == "KKS_StudioExtraMoveAxis" &&
                hash == "c75421f9f12e23b797883469b313c794568db8c2242c44e30a1541360170cc74";
        }

        private static string ReadHash(Assembly assembly)
        {
            using (var stream = File.OpenRead(assembly.Location))
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
