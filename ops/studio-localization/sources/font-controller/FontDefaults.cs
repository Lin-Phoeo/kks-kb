using System;

namespace Codex.StudioFontController
{
    internal static class FontDefaults
    {
        internal const bool Enabled = true;
        internal static string Family(string[] available)
        {
            foreach (string family in new[] { "华文细黑", "STXihei", "等线", "DengXian", "楷体", "KaiTi", "微软雅黑", "Microsoft YaHei" })
                if (Array.IndexOf(available, family) >= 0) return family;
            return available.Length > 0 ? available[0] : "";
        }
    }
}
