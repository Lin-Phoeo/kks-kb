using System;
using Codex.StudioFontController;

internal static class FontDefaultsRegression
{
    internal static void Run(Action<bool, string> check)
    {
        check(FontDefaults.Enabled, "new configurations enable the preferred font");
        check(FontDefaults.Family(new[] { "SimSun", "DengXian", "STXihei", "华文细黑" }) == "华文细黑", "preferred family uses the localized registration");
        check(FontDefaults.Family(new[] { "DengXian", "SimSun", "STXihei" }) == "STXihei", "English Xihei registration outranks fallback fonts");
        check(FontDefaults.Family(new[] { "STXihei" }) == "STXihei", "English-only Xihei registration remains usable");
        check(FontDefaults.Family(new[] { "SimSun", "楷体", "DengXian", "等线" }) == "等线", "localized DengXian is the first fallback");
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
