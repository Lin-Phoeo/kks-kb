using System;
using Codex.StudioFontController;

internal static class FontDefaultsRegression
{
    internal static void Run(Action<bool, string> check)
    {
        check(!FontDefaults.Enabled, "new configurations preserve the original Chinese font");
        check(FontDefaults.Family(new[] { "SimSun", "DengXian", "STXihei", "华文细黑" }) == "DengXian", "standby family no longer prefers Song or Xihei");
        check(FontDefaults.Family(new[] { "DengXian", "SimSun", "STXihei" }) == "DengXian", "English DengXian registration is usable without an alias");
        check(FontDefaults.Family(new[] { "STXihei" }) == "STXihei", "optional installed fonts remain available without being enabled");
        check(FontDefaults.Family(new[] { "SimSun", "楷体", "DengXian", "等线" }) == "等线", "localized DengXian is the preferred standby family");
        check(FontDefaults.Family(new[] { "Noto Sans SC", "KaiTi", "DengXian" }) == "DengXian", "DengXian outranks an unverified variable Noto family");
        check(FontDefaults.Family(new[] { "SimSun", "Microsoft YaHei", "楷体" }) == "楷体", "known Chinese fallback precedes arbitrary sorted families");
        check(FontDefaults.Family(new[] { "SimSun", "KaiTi" }) == "KaiTi", "English-only KaiTi remains a valid fallback");
        check(FontDefaults.Family(new[] { "SimSun", "Microsoft YaHei" }) == "Microsoft YaHei", "known sans-serif fallback precedes Song");
        check(FontDefaults.Family(new[] { "Custom Installed Font" }) == "Custom Installed Font", "unrecognized installations retain an available fallback");
        check(FontDefaults.Family(new string[0]) == "", "no installed font does not invent a family");

        string[] available = { "SimSun", "DengXian", "华文细黑", "Noto Sans SC" };
        string[] original = (string[])available.Clone();
        FontDefaults.Family(available);
        check(Array.TrueForAll(new[] { 0, 1, 2, 3 }, index => available[index] == original[index]), "default selection does not reorder the installed list");
    }
}
