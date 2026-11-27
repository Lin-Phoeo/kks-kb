using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace Codex.StudioFontController
{
    internal sealed class FontSourceResolver
    {
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _customRoot;
        private volatile ChineseFontCatalog _chineseCatalog;
        private string[] _runtimeFamilies;
        private HashSet<string> _runtimeNames;
        private volatile string _catalogError;
        private int _catalogStarted;
        private double _catalogMilliseconds;
        private static readonly string[] EmptyFamilies = new string[0];
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
        internal void StartCatalogBuild()
        {
            if (Interlocked.CompareExchange(ref _catalogStarted, 1, 0) != 0) return;
            var paths = new Dictionary<string, string>(_paths, StringComparer.OrdinalIgnoreCase);
            ThreadPool.QueueUserWorkItem(delegate
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    var catalog = new ChineseFontCatalog(paths);
                    _catalogMilliseconds = watch.Elapsed.TotalMilliseconds;
                    _chineseCatalog = catalog;
                }
                catch (Exception error) { _catalogError = error.GetType().Name; }
            });
        }
        internal bool CatalogReady { get { return _chineseCatalog != null; } }
        internal string CatalogError { get { return _catalogError; } }
        internal double CatalogMilliseconds { get { return CatalogReady ? _catalogMilliseconds : 0; } }
        internal int CatalogFileReads { get { return CatalogReady ? _chineseCatalog.FileReads : 0; } }
        internal int CatalogNativeProbes { get { return CatalogReady ? _chineseCatalog.NativeProbes : 0; } }
        internal int CatalogSupportedAliases { get { return CatalogReady ? _chineseCatalog.SupportedAliases : 0; } }
        internal Dictionary<string, int> CatalogRejections
        { get { return CatalogReady ? new Dictionary<string, int>(_chineseCatalog.Rejections) : new Dictionary<string, int>(); } }
        internal string[] ChineseFamilies() { return _runtimeFamilies ?? (CatalogReady ? _chineseCatalog.Families : EmptyFamilies); }
        internal string[] FilterRuntimeFamilies(string[] unityFamilies)
        {
            if (!CatalogReady) return EmptyFamilies;
            if (_runtimeFamilies == null)
            {
                _runtimeNames = new HashSet<string>(unityFamilies, StringComparer.OrdinalIgnoreCase);
                _runtimeFamilies = _chineseCatalog.FilterRuntimeFamilies(unityFamilies);
            }
            return _runtimeFamilies;
        }
        internal bool IsChineseFamily(string family)
        {
            string path;
            return CatalogReady && family != null && _paths.TryGetValue(family, out path) && _chineseCatalog.Supports(family, path) &&
                (_runtimeNames == null || _chineseCatalog.RuntimeFamilyForPath(path, _runtimeNames) != null);
        }
        internal string RegisteredFamily(string path, string actualFamily, string requestedFamily, string[] unityFamilies)
        {
            var available = new HashSet<string>(unityFamilies, StringComparer.OrdinalIgnoreCase);
            string mapped;
            if (!CatalogReady) throw new ArgumentException(CatalogError == null ? "正在筛选中文字体，请稍后重试。" : "中文字体列表未能完成检测，已保留原字体。");
            string verifiedFamily = _chineseCatalog.RuntimeFamilyForPath(path, available);
            if (verifiedFamily == null || !_chineseCatalog.HasFirstFaceName(actualFamily, path))
                throw new ArgumentException("此字体的中文覆盖或系统字面与字体文件不一致，请从兼容中文字体列表选择。");
            if (_paths.TryGetValue(requestedFamily ?? "", out mapped) &&
                string.Equals(Path.GetFullPath(mapped), path, StringComparison.OrdinalIgnoreCase) && !_chineseCatalog.Supports(requestedFamily, path))
                throw new ArgumentException("所选名称不对应字体文件首个字面，请从兼容中文字体列表选择。");
            return verifiedFamily;
        }
        internal static string DisplayName(string actualFamily)
        {
            return ChineseFontCatalog.DisplayName(actualFamily);
        }
        internal static int EffectiveStyleSize(int requestedSize, int originalFontSize)
        {
            return requestedSize > 0 ? requestedSize : originalFontSize > 0 ? originalFontSize : 14;
        }
    }
}
