using TMPro;
using System.Runtime.CompilerServices;
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
        public static bool Bold { get; internal set; }
        public static int SizeDelta { get; internal set; }
        public static bool TypographyActive { get { return Bold || SizeDelta != 0; } }
        private static readonly ConditionalWeakTable<Component, object> TypographyOwners = new ConditionalWeakTable<Component, object>();
        private static readonly object OwnerMarker = new object();
        public static void SetExternalTypographyOwner(Component component, bool owned)
        {
            if (!component) return;
            object marker;
            if (owned)
            {
                if (TypographyOwners.TryGetValue(component, out marker)) return;
                Plugin.ReleaseTypography(component);
                TypographyOwners.Add(component, OwnerMarker);
            }
            else TypographyOwners.Remove(component);
        }
        internal static bool ExternalTypography(Component component)
        {
            object marker;
            return TypographyOwners.TryGetValue(component, out marker);
        }
    }
}
