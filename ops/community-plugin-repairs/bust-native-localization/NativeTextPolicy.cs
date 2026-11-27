using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Codex.StudioBustNativeText
{
    internal static class NativeTextPolicy
    {
        internal const string Category = "Studio Bust Adjuster";
        internal const string Header = "胸部调节 / Studio Bust Adjuster";
        internal const string HeaderNode = Category + "_Header_SAPI";
        internal const string ItemsNode = Category + "_Items_SAPI";
        internal const string BustSha = "a07e33d8768b6a1540aee3b727cf3ad43cd62335fc3ecad04f6256e5ac53098f";
        internal const string ApiSha = "866464809d1bd04450a35850c23f9d17a0d1e5a166ed9f5dc525c506fc04ef01";
        internal static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"   Size", "   大小"},
            {"   Vertical Position", "   上下位置"},
            {"   Horizontal Angle", "   水平角度"},
            {"   Horizontal Position", "   左右间距"},
            {"   Vertical Angle", "   垂直角度"},
            {"   Pointiness", "   前凸程度"},
            {"   Shape", "   圆润度"},
            {"   Softness", "   柔软度"},
            {"   Weight", "   重量"},
            {"   Areola Bulge", "   乳晕隆起"},
            {"   Nipple Circumference", "   乳头宽度"},
            {"   Nipple Protrusion", "   乳头突出"}
        };

        internal static bool TryLabel(string categoryNode, string label, out string value)
        {
            value = null;
            return categoryNode == ItemsNode && label != null && Labels.TryGetValue(label, out value);
        }

        internal static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static void RequireFile(string path, string expected)
        {
            if (!string.Equals(Hash(path), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Unsupported plugin or KKAPI identity.");
        }
    }
}
