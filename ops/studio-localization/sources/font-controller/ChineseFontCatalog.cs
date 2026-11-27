using System;
using System.Collections.Generic;
using System.IO;

namespace Codex.StudioFontController
{
    internal sealed class ChineseFontCatalog
    {
        private readonly Dictionary<string, string> _acceptedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _acceptedFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _faceNames = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly string[] Families;
        internal readonly int FileReads, NativeProbes;
        internal int SupportedAliases { get { return _acceptedFamilies.Count; } }
        internal readonly Dictionary<string, int> Rejections;

        internal ChineseFontCatalog(IDictionary<string, string> paths)
            : this(paths, ChineseFontData.Read, ChineseFontProbe.Check) { }

        internal ChineseFontCatalog(IDictionary<string, string> paths, Func<string, ChineseFontData> read, Func<string, ChineseFontData, ChineseFontProbeResult> probe)
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in paths)
            {
                List<string> names;
                if (!groups.TryGetValue(item.Value, out names)) { names = new List<string>(); groups.Add(item.Value, names); }
                names.Add(item.Key);
            }
            var accepted = new List<string>();
            foreach (var group in groups)
            {
                ChineseFontData data;
                try { FileReads++; data = read(group.Key); }
                catch (InvalidDataException) { SetReasons(group.Value, "font-data-unsupported"); continue; }
                catch (IOException) { SetReasons(group.Value, "font-file-unreadable"); continue; }
                catch (UnauthorizedAccessException) { SetReasons(group.Value, "font-file-unreadable"); continue; }
                if (data.Variable) { SetReasons(group.Value, "variable-font-unverified"); continue; }
                if (!data.HasChineseSample) { SetReasons(group.Value, "file-missing-chinese"); continue; }
                _faceNames[group.Key] = data.Names;
                group.Value.Sort(StringComparer.OrdinalIgnoreCase);
                if (group.Value.Contains(data.PreferredFamily))
                { group.Value.Remove(data.PreferredFamily); group.Value.Insert(0, data.PreferredFamily); }
                foreach (string family in group.Value)
                {
                    if (!data.HasName(family)) { _reasons[family] = "not-first-face"; continue; }
                    NativeProbes++;
                    ChineseFontProbeResult result = probe(family, data);
                    _reasons[family] = result.Reason;
                    if (!result.Supported) continue;
                    _acceptedFamilies[family] = group.Key;
                    if (!_acceptedPaths.ContainsKey(group.Key))
                    { accepted.Add(family); _acceptedPaths[group.Key] = family; }
                }
                if (_acceptedPaths.ContainsKey(group.Key) && !group.Value.Contains(data.PreferredFamily))
                {
                    NativeProbes++;
                    ChineseFontProbeResult result = probe(data.PreferredFamily, data);
                    if (result.Supported) _acceptedFamilies[data.PreferredFamily] = group.Key;
                }
            }
            accepted.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(DisplayName(left), DisplayName(right)));
            Families = accepted.ToArray();
            Rejections = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in _reasons)
            {
                if (_acceptedFamilies.ContainsKey(item.Key)) continue;
                int count;
                Rejections.TryGetValue(item.Value, out count);
                Rejections[item.Value] = count + 1;
            }
        }

        private void SetReasons(IEnumerable<string> families, string reason)
        { foreach (string family in families) _reasons[family] = reason; }

        internal bool Supports(string family, string path)
        {
            string acceptedPath;
            return family != null && _acceptedFamilies.TryGetValue(family, out acceptedPath) && string.Equals(path, acceptedPath, StringComparison.OrdinalIgnoreCase);
        }
        internal string Reason(string family, string path)
        {
            if (Supports(family, path)) return "native-and-file-chinese";
            string reason;
            return _reasons.TryGetValue(family, out reason) ? reason : "not-first-face";
        }
        internal string FamilyForPath(string path)
        {
            string family;
            return _acceptedPaths.TryGetValue(path, out family) ? family : null;
        }
        internal string RuntimeFamilyForPath(string path, HashSet<string> available)
        {
            foreach (var item in _acceptedFamilies)
                if (available.Contains(item.Key) && string.Equals(item.Value, path, StringComparison.OrdinalIgnoreCase)) return item.Key;
            return null;
        }
        internal string[] FilterRuntimeFamilies(string[] unityFamilies)
        {
            var available = new HashSet<string>(unityFamilies, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (string family in Families)
            {
                string path = _acceptedFamilies[family];
                if (RuntimeFamilyForPath(path, available) != null) result.Add(family);
            }
            return result.ToArray();
        }
        internal bool HasFirstFaceName(string family, string path)
        {
            string[] names;
            if (!_faceNames.TryGetValue(path, out names)) return false;
            foreach (string name in names) if (string.Equals(family, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string DisplayName(string family)
        {
            if (string.IsNullOrEmpty(family)) return family ?? "";
            lock (DisplayNames)
            {
                string display;
                if (!DisplayNames.TryGetValue(family, out display))
                { display = BuildDisplayName(family); DisplayNames.Add(family, display); }
                return display;
            }
        }

        private static string BuildDisplayName(string family)
        {
            if (family.Equals("Arial Unicode MS", StringComparison.OrdinalIgnoreCase)) return "Arial 多语言字体 / " + family;
            if (family.Equals("yyb Sans Regular", StringComparison.OrdinalIgnoreCase)) return "yyb Sans（常规） / " + family;
            string[] english = { "Microsoft YaHei UI", "Microsoft JhengHei UI", "Microsoft YaHei", "Microsoft JhengHei", "Noto Sans SC", "Noto Serif SC", "Source Han Sans SC", "Source Han Serif SC", "DengXian", "STXihei", "NSimSun", "SimSun", "SimHei", "KaiTi", "FangSong" };
            string[] chinese = { "微软雅黑（界面版）", "微软正黑体（界面版）", "微软雅黑", "微软正黑体", "Noto 黑体简中", "Noto 宋体简中", "思源黑体（简中）", "思源宋体（简中）", "等线", "华文细黑", "新宋体", "宋体", "黑体", "楷体", "仿宋" };
            for (int index = 0; index < english.Length; index++)
            {
                if (!family.Equals(english[index], StringComparison.OrdinalIgnoreCase) && !family.StartsWith(english[index] + " ", StringComparison.OrdinalIgnoreCase)) continue;
                string suffix = family.Substring(english[index].Length).Trim();
                string style = StyleName(suffix);
                return chinese[index] + (style.Length == 0 ? "" : "（" + style + "）") + " / " + family;
            }
            return family;
        }

        private static string StyleName(string style)
        {
            switch (style.ToLowerInvariant())
            {
                case "regular": return "常规";
                case "bold": return "粗体";
                case "italic": return "斜体";
                case "bold italic": return "粗斜体";
                case "semibold": case "semi bold": return "半粗";
                case "light": return "细体";
                case "semilight": return "半细";
                case "demilight": return "次细";
                case "extralight": return "特细";
                case "thin": return "极细";
                case "medium": return "中等";
                case "black": case "heavy": return "特粗";
                default: return style;
            }
        }
    }
}
