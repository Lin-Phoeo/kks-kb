using TMPro;
using UnityEngine;

namespace Codex.StudioFontController
{
    public static class FontSelection
    {
        public static bool Active { get; internal set; }
        public static Font UguiFont { get; internal set; }
        public static TMP_FontAsset TmpFont { get; internal set; }
        public static int Generation { get; internal set; }
        public static string SourcePath { get; internal set; }
        public static string Family { get; internal set; }
    }
}
