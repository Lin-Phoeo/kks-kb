namespace Codex.StudioFontController
{
    internal sealed class FontSettingsSnapshot
    {
        internal readonly bool Enabled, Bold;
        internal readonly string Family, File;
        internal readonly int SizeDelta;
        internal FontSettingsSnapshot(bool enabled, string family, string file, bool bold, int sizeDelta)
        { Enabled = enabled; Family = family; File = file; Bold = bold; SizeDelta = sizeDelta; }
    }
}
