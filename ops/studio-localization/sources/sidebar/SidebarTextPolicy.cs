using System;

namespace Codex.StudioPanelTypography
{
    internal struct SidebarTextStyle
    {
        internal int Size;
        internal int Minimum;
        internal bool Wrap;
        internal bool Center;
        internal string Role;
    }

    internal static class SidebarTextPolicy
    {
        internal const string Root = "StudioScene/Canvas Main Menu/";
        internal static readonly string[] Groups =
        {
            "01_Add/", "02_Manipulate/", "03_Sound/", "04_System/",
            "02_01_Panel/", "02_05_00_Route Control/", "03_00_Anime Control/"
        };

        internal static bool Includes(string path)
        {
            if (!path.StartsWith(Root, StringComparison.Ordinal)) return false;
            string relative = path.Substring(Root.Length);
            foreach (string group in Groups)
                if (relative.StartsWith(group, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static SidebarTextStyle Resolve(string path, float width, float height, bool input, bool control, bool dropdown)
        {
            bool header = !input && !control && !dropdown && height >= 26 &&
                (path.IndexOf("/Image Menu/", StringComparison.Ordinal) >= 0 ||
                 path.EndsWith("/TextMeshPro Menu", StringComparison.Ordinal) ||
                 path.EndsWith("/01_Item/Text (TMP)", StringComparison.Ordinal) ||
                 path.EndsWith("/08_Text/Text (TMP)", StringComparison.Ordinal));
            bool navigation = path.IndexOf("/Viewport/Content/", StringComparison.Ordinal) >= 0 && control;
            bool compact = input || dropdown || width < 64 || height < 18;
            int size = header ? 16 : compact ? 13 : 14;
            return new SidebarTextStyle
            {
                Size = size,
                Minimum = header ? 14 : 13,
                Wrap = !input && !dropdown && height >= 30,
                Center = control && !input && !dropdown,
                Role = header ? "header" : input ? "input" : dropdown ? "choice" : navigation ? "navigation" : compact ? "compact" : "body"
            };
        }
    }
}
