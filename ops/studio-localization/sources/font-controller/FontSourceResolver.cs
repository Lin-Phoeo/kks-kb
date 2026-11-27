using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Codex.StudioFontController
{
    internal sealed class FontSourceResolver
    {
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _customRoot;
        internal FontSourceResolver(string gameRoot)
        {
            _customRoot = Path.Combine(gameRoot, "UserData", "StudioFonts");
            ReadRegistry(Registry.LocalMachine);
            ReadRegistry(Registry.CurrentUser);
            AddAlias("微软雅黑", "Microsoft YaHei");
            AddAlias("宋体", "SimSun");
            AddAlias("楷体", "KaiTi");
            AddAlias("仿宋", "FangSong");
            AddAlias("华文细黑", "STXihei");
            AddAlias("STXihei", "华文细黑");
            AddAlias("等线", "DengXian");
            AddAlias("DengXian", "等线");
        }
        private void ReadRegistry(RegistryKey hive)
        {
            using (RegistryKey fonts = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"))
            {
                if (fonts == null) return;
                foreach (string name in fonts.GetValueNames())
                {
                    string file = fonts.GetValue(name) as string;
                    if (string.IsNullOrEmpty(file)) continue;
                    string path = Path.IsPathRooted(file) ? file : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
                    if (!IsSupportedExtension(path) || !File.Exists(path)) continue;
                    string names = name.Replace(" (TrueType)", "").Replace(" (OpenType)", "");
                    foreach (string family in names.Split(new[] { " & " }, StringSplitOptions.RemoveEmptyEntries))
                        if (!_paths.ContainsKey(family)) _paths.Add(family, path);
                }
            }
        }
        private void AddAlias(string alias, string source) { string path; if (_paths.TryGetValue(source, out path)) _paths[alias] = path; }
        internal static bool IsSupportedExtension(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".ttf" || extension == ".otf" || extension == ".ttc";
        }
        internal string Resolve(string family, string requestedFile)
        {
            string path;
            if (!string.IsNullOrWhiteSpace(requestedFile))
                path = Path.IsPathRooted(requestedFile) ? requestedFile : Path.Combine(_customRoot, requestedFile);
            else if (!_paths.TryGetValue(family ?? "", out path))
                throw new ArgumentException("找不到此字体的文件，请在字体文件项填写 TTF、OTF 或 TTC 路径。");
            path = Path.GetFullPath(path);
            if (!IsSupportedExtension(path)) throw new ArgumentException("请选择 TTF、OTF 或 TTC 字体文件。");
            if (!File.Exists(path)) throw new FileNotFoundException("字体文件不存在。");
            return path;
        }
        internal string[] Families()
        {
            var result = new List<string>(_paths.Keys);
            result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result.ToArray();
        }
        internal bool HasFamily(string family) { return _paths.ContainsKey(family); }
        internal string RegisteredFamily(string path, string actualFamily, string requestedFamily, string[] unityFamilies)
        {
            var available = new HashSet<string>(unityFamilies, StringComparer.OrdinalIgnoreCase);
            string mapped;
            if (available.Contains(actualFamily) && _paths.TryGetValue(actualFamily, out mapped) &&
                string.Equals(Path.GetFullPath(mapped), path, StringComparison.OrdinalIgnoreCase)) return actualFamily;
            if (available.Contains(actualFamily))
                foreach (var item in _paths)
                    if (string.Equals(Path.GetFullPath(item.Value), path, StringComparison.OrdinalIgnoreCase)) return actualFamily;
            if (available.Contains(requestedFamily ?? "") && _paths.TryGetValue(requestedFamily, out mapped) &&
                string.Equals(Path.GetFullPath(mapped), path, StringComparison.OrdinalIgnoreCase)) return requestedFamily;
            foreach (var item in _paths)
                if (available.Contains(item.Key) && string.Equals(Path.GetFullPath(item.Value), path, StringComparison.OrdinalIgnoreCase)) return item.Key;
            throw new ArgumentException("此字体文件尚未安装到系统。请先安装字体，再在列表中选择，以统一全部界面文字。");
        }
        internal static string DisplayName(string actualFamily)
        {
            if (actualFamily == "STXihei" || actualFamily == "华文细黑") return "华文细黑 / STXihei";
            if (actualFamily == "DengXian" || actualFamily == "等线") return "等线 / DengXian";
            if (actualFamily == "Microsoft YaHei") return "微软雅黑 / Microsoft YaHei";
            if (actualFamily == "SimSun" || actualFamily == "NSimSun") return "宋体 / " + actualFamily;
            if (actualFamily == "KaiTi") return "楷体 / KaiTi";
            if (actualFamily == "FangSong") return "仿宋 / FangSong";
            return actualFamily;
        }
        internal static int EffectiveStyleSize(int requestedSize, int originalFontSize)
        {
            return requestedSize > 0 ? requestedSize : originalFontSize > 0 ? originalFontSize : 14;
        }
    }
}
