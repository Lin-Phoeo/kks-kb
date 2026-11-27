using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Codex.StudioFontController
{
    internal sealed class ChineseFontProbeResult
    {
        internal readonly bool Supported;
        internal readonly string Reason, ActualFace;
        internal ChineseFontProbeResult(bool supported, string reason, string actualFace)
        { Supported = supported; Reason = reason; ActualFace = actualFace; }
    }

    internal static class ChineseFontProbe
    {
        private const uint GdiError = 0xffffffff, MarkMissingGlyphs = 1;

        internal static ChineseFontProbeResult Check(string family, ChineseFontData data)
        {
            if (!data.HasName(family)) return new ChineseFontProbeResult(false, "not-first-face", null);
            if (data.Variable) return new ChineseFontProbeResult(false, "variable-font-unverified", null);
            if (!data.HasChineseSample) return new ChineseFontProbeResult(false, "file-missing-chinese", null);
            IntPtr dc = IntPtr.Zero, font = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return new ChineseFontProbeResult(false, "gdi-dc-failed", null);
                font = CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, 1, 4, 0, 0, 0, family);
                if (font == IntPtr.Zero) return new ChineseFontProbeResult(false, "gdi-font-failed", null);
                previous = SelectObject(dc, font);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return new ChineseFontProbeResult(false, "gdi-select-failed", null);
                var face = new StringBuilder(128);
                if (GetTextFaceW(dc, face.Capacity, face) == 0) return new ChineseFontProbeResult(false, "gdi-face-unavailable", null);
                byte[] selectedName = ReadNameTable(dc);
                // The OS can substitute a different face, even one with Chinese glyphs.
                if (!SameBytes(data.NameTable, selectedName)) return new ChineseFontProbeResult(false, "gdi-source-face-differs", face.ToString());
                var glyphs = new ushort[ChineseFontData.ProbeText.Length];
                uint converted = GetGlyphIndicesW(dc, ChineseFontData.ProbeText, glyphs.Length, glyphs, MarkMissingGlyphs);
                if (converted == GdiError || converted != glyphs.Length) return new ChineseFontProbeResult(false, "gdi-glyph-query-failed", face.ToString());
                foreach (ushort glyph in glyphs)
                    if (glyph == 0 || glyph == 0xffff) return new ChineseFontProbeResult(false, "gdi-missing-chinese", face.ToString());
                return new ChineseFontProbeResult(true, "native-and-file-chinese", face.ToString());
            }
            finally
            {
                if (dc != IntPtr.Zero && previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
                if (font != IntPtr.Zero) DeleteObject(font);
                if (dc != IntPtr.Zero) DeleteDC(dc);
            }
        }

        private static byte[] ReadNameTable(IntPtr dc)
        {
            const uint nameTag = 0x656d616e;
            uint size = GetFontData(dc, nameTag, 0, null, 0);
            if (size == GdiError || size == 0 || size > 1024 * 1024) return null;
            var result = new byte[size];
            return GetFontData(dc, nameTag, 0, result, size) == size ? result : null;
        }
        internal static bool SameBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint characterSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int GetTextFaceW(IntPtr dc, int count, StringBuilder name);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, [Out] ushort[] glyphs, uint flags);
        [DllImport("gdi32.dll")] private static extern uint GetFontData(IntPtr dc, uint table, uint offset, [Out] byte[] data, uint size);
    }
}
