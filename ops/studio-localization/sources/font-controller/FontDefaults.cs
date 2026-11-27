using System;

namespace Codex.StudioFontController
{
    internal static class FontDefaults
    {
        internal const bool Enabled = true;
        internal static string Family(string[] available)
        {
            if (Array.IndexOf(available, "宋体") >= 0) return "宋体";
            if (Array.IndexOf(available, "SimSun") >= 0) return "SimSun";
            return available.Length > 0 ? available[0] : "";
        }
    }
}
