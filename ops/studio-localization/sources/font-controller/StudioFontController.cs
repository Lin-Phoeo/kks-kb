using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Codex.StudioFontController
{
    [BepInPlugin(PluginGuid, "自定义字体 / Studio Font Controller", "1.1.0")]
    [BepInProcess("CharaStudio")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "codex.studio.fontcontroller";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static Plugin _instance;
        private static bool _assigning;
        private ConfigEntry<bool> _enabled, _bold;
        private ConfigEntry<int> _sizeDelta;
        private ConfigEntry<string> _family, _file;
        private FontSourceResolver _resolver;
        private Harmony _harmony;
        private readonly List<MethodBase> _patches = new List<MethodBase>();
        private readonly List<FontResources> _cache = new List<FontResources>();
        private FontResources _selected;
        private Font _fallback;
        private Font _baselineUgui;
        private TMP_FontAsset _baselineTmp;
        private bool _studioReady;
        private float _changeAt = -1;
        private float _nextPrune;
        private string _status = "保留当前字体";
        private readonly ConditionalWeakTable<Text, TextOriginal> _texts = new ConditionalWeakTable<Text, TextOriginal>();
        private readonly ConditionalWeakTable<TMP_Text, TmpOriginal> _tmp = new ConditionalWeakTable<TMP_Text, TmpOriginal>();
        private readonly ConditionalWeakTable<GUIStyle, StyleOriginal> _styles = new ConditionalWeakTable<GUIStyle, StyleOriginal>();
        private readonly List<WeakReference> _textRefs = new List<WeakReference>(), _tmpRefs = new List<WeakReference>(), _styleRefs = new List<WeakReference>();
        private long _switches, _fontAssignments, _styleAssignments;
        private long _fallbackSelections;
        private string _verifyOutput;
        private float _sampleAfter;
        private int _sampleFrames;
        private double _sampleFrameSeconds, _sampleUpdateMilliseconds;
        private long _sampleFontAssignments, _sampleStyleAssignments;
        private bool _sampleArmed;
        private bool _showFamilies, _pendingFamilyList;
        private Vector2 _familyScroll;
        private string[] _chineseFamilies, _familyLabels;
        private bool _catalogPublished;

        private sealed class TextOriginal { internal Font Font; internal TypographyState Typography; }
        private sealed class TmpOriginal { internal TMP_FontAsset Font; internal Material Material; internal TypographyState Typography; }
        private sealed class StyleOriginal { internal Font Font; internal readonly TypographyScope Scope = new TypographyScope(); }
        private struct StyleCall { internal GUIStyle Style; internal StyleOriginal Original; }
        public sealed class ConfigurationManagerAttributes { public Action<ConfigEntryBase> CustomDrawer; public bool HideDefaultButton = true; public bool HideSettingName = true; }

        private void Awake()
        {
            _instance = this;
            _resolver = new FontSourceResolver(Paths.GameRootPath);
            _resolver.StartCatalogBuild();
            _enabled = Config.Bind("字体", "统一界面字体", FontDefaults.Enabled, "默认保留原中文字体。开启后替换字体家族；保留原中文字体也可单独加粗、微调字号。");
            string[] families = _resolver.Families();
            string defaultFamily = FontDefaults.Family(families);
            _family = Config.Bind("字体", "系统字体", defaultFamily, new ConfigDescription("仅在统一界面字体开启时生效。列表只显示已检测到中文字形的本机字体。", null,
                new ConfigurationManagerAttributes { CustomDrawer = DrawFamily }));
            _file = Config.Bind("字体", "字体文件", "", "可填写本地 TTF、OTF 或 TTC 路径；字体须已安装到系统，才能用于全部界面。填写后优先使用该文件；相对路径从 UserData/StudioFonts 查找。");
            _bold = Config.Bind("字体", "文字加粗", false, "保留原中文字体也可单独加粗，原有斜体等样式会保留。关闭后恢复各控件本来的粗细。");
            _sizeDelta = Config.Bind("字体", "字号微调", 0, new ConfigDescription("在各控件原字号上微调，0 为原大小；紧凑输入框会限制放大，避免文字被裁切。", new AcceptableValueRange<int>(-2, 3)));
            Config.Bind("字体", "预览与状态", 0, new ConfigDescription("", null, new ConfigurationManagerAttributes { CustomDrawer = DrawStatus }));
            _enabled.SettingChanged += Changed; _family.SettingChanged += Changed; _file.SettingChanged += Changed;
            _bold.SettingChanged += Changed; _sizeDelta.SettingChanged += Changed;
            _chineseFamilies = new string[0]; _familyLabels = new string[0];
            _harmony = new Harmony(PluginGuid);
            string verify = Environment.GetEnvironmentVariable("KKS_FONT_CONTROLLER_VERIFY_OUTPUT");
            if (!string.IsNullOrEmpty(verify))
            {
                string allowed = Path.GetFullPath(Path.Combine(Paths.GameRootPath, ".scratch/studio-translation-20261126/distribution-fonts-20261127/fonts")) + Path.DirectorySeparatorChar;
                string absolute = Path.GetFullPath(verify);
                if (Path.IsPathRooted(verify) && absolute.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(absolute) == ".json") _verifyOutput = absolute;
            }
            try
            {
                Patch(AccessTools.PropertySetter(typeof(Text), "font"), "TextFontPrefix", null);
                Patch(AccessTools.PropertySetter(typeof(TMP_Text), "font"), "TmpFontPrefix", null);
                Patch(AccessTools.Method(typeof(Text), "OnEnable"), null, "TextEnabled");
                Patch(AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable"), null, "TmpEnabled");
                Patch(AccessTools.PropertySetter(typeof(Text), "text"), null, "TextEnabled");
                foreach (string property in new[] { "fontSize", "resizeTextMinSize", "resizeTextMaxSize" })
                    Patch(AccessTools.PropertySetter(typeof(Text), property), "TextSizePrefix", "TextEnabled");
                Patch(AccessTools.PropertySetter(typeof(Text), "fontStyle"), "TextStylePrefix", "TextEnabled");
                Patch(AccessTools.PropertySetter(typeof(Text), "resizeTextForBestFit"), "TextAutoPrefix", "TextEnabled");
                foreach (string property in new[] { "fontSize", "fontSizeMin", "fontSizeMax" })
                    Patch(AccessTools.PropertySetter(typeof(TMP_Text), property), "TmpSizePrefix", "TmpChanged");
                Patch(AccessTools.PropertySetter(typeof(TMP_Text), "fontStyle"), "TmpStylePrefix", "TmpChanged");
                Patch(AccessTools.PropertySetter(typeof(TMP_Text), "enableAutoSizing"), "TmpAutoPrefix", "TmpChanged");
                Patch(typeof(GUIStyle).GetConstructor(new[] { typeof(GUIStyle) }), null, "StyleCloned");
                Patch(typeof(GUIStyle).GetMethod("Draw", All, null, new[] {typeof(Rect),typeof(GUIContent),typeof(int),typeof(bool),typeof(bool),typeof(bool),typeof(bool)}, null), "DrawStyle", null, "StyleFinalizer");
                Patch(typeof(GUIStyle).GetMethod("CalcSizeWithConstraints", All, null, new[] {typeof(GUIContent),typeof(Vector2)}, null), "MeasureStyle", null, "StyleFinalizer");
                Patch(typeof(GUI).GetMethod("DoWindow", All), "WindowStyle", null, "StyleFinalizer");
                Patch(typeof(GUI).GetMethod("DoModalWindow", All), "WindowStyle", null, "StyleFinalizer");
                foreach (string method in new[] {"CalcSize", "CalcHeight", "CalcMinMaxWidth", "GetCursorPixelPosition", "GetCursorStringIndex"})
                {
                    foreach (MethodInfo target in typeof(GUIStyle).GetMethods(BindingFlags.Public|BindingFlags.Instance))
                        if (target.Name == method && target.GetParameters().Length > 0 && target.GetParameters()[0].ParameterType == typeof(GUIContent))
                            Patch(target, "MeasureStyle", null, "StyleFinalizer");
                }
                foreach (string method in new[] {"GetCursorPixelPosition", "GetCursorStringIndex", "DrawCursor", "DrawWithTextSelection"})
                    foreach (MethodInfo target in typeof(GUIStyle).GetMethods(BindingFlags.Public|BindingFlags.Instance))
                        if (target.Name == method && target.GetParameters().Length > 1 && target.GetParameters()[1].ParameterType == typeof(GUIContent))
                            Patch(target, "DrawStyle", null, "StyleFinalizer");
                SceneManager.sceneLoaded += SceneLoaded;
                if (_enabled.Value || _bold.Value || _sizeDelta.Value != 0) _changeAt = Time.unscaledTime + 1f;
                Logger.LogInfo("STUDIO_FONT_CONTROLLER_READY defaultPreserved=" + (!_enabled.Value));
            }
            catch (Exception error) { Logger.LogError("STUDIO_FONT_CONTROLLER_REFUSED " + error.GetType().Name); Cleanup(); }
        }

        private void Patch(MethodBase method, string prefix, string postfix, string finalizer = null)
        {
            if (method == null) throw new MissingMethodException("Font control hook");
            _harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(Plugin).GetMethod(prefix, All)),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Plugin).GetMethod(postfix, All)),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Plugin).GetMethod(finalizer, All)));
            _patches.Add(method);
        }
        private void Changed(object sender, EventArgs args)
        {
            _changeAt = Time.unscaledTime + 0.65f;
            string changed = ReferenceEquals(sender, _family) ? "system-family" : ReferenceEquals(sender, _file) ? "local-file" : ReferenceEquals(sender, _bold) ? "bold" : ReferenceEquals(sender, _sizeDelta) ? "size" : "enabled";
            _status = "正在应用字体设置…";
            Logger.LogInfo("STUDIO_FONT_CHANGE_QUEUED field=" + changed + " enabled=" + _enabled.Value +
                " source=" + (string.IsNullOrWhiteSpace(_file.Value) ? "system-family" : "local-file"));
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { if (_enabled.Value || _bold.Value || _sizeDelta.Value != 0) _changeAt = Time.unscaledTime + 0.5f; }
        private static bool Ready { get { return _instance != null && !_assigning && !ImGuiMetricObservation.Measuring && (FontSelection.Active || FontSelection.TypographyActive) && _instance._studioReady; } }
        private static bool ScreenUi(Component component)
        {
            if (!component || !component.gameObject.scene.IsValid()) return false;
            Canvas canvas = component.GetComponentInParent<Canvas>();
            return canvas && canvas.renderMode != RenderMode.WorldSpace;
        }
        private void Update()
        {
            if (!_catalogPublished && _resolver.CatalogReady)
            {
                _chineseFamilies = _resolver.FilterRuntimeFamilies(Font.GetOSInstalledFontNames());
                _familyLabels = Array.ConvertAll(_chineseFamilies, FontSourceResolver.DisplayName);
                _catalogPublished = true;
                Logger.LogInfo("STUDIO_FONT_CATALOG_READY families=" + _chineseFamilies.Length + " files=" + _resolver.CatalogFileReads +
                    " probes=" + _resolver.CatalogNativeProbes + " elapsedMs=" + _resolver.CatalogMilliseconds.ToString("F1"));
            }
            bool sample = _sampleArmed && _verifyOutput != null && Time.unscaledTime >= _sampleAfter;
            long updateStart = sample ? Stopwatch.GetTimestamp() : 0;
            _studioReady = SceneManager.GetActiveScene().name == "Studio";
            if (_changeAt >= 0 && Time.unscaledTime >= _changeAt && _studioReady)
            {
                _changeAt = -1;
                Switch();
            }
            if (Time.unscaledTime >= _nextPrune)
            {
                _nextPrune = Time.unscaledTime + 5f;
                _textRefs.RemoveAll(item => !(item.Target as Text));
                _tmpRefs.RemoveAll(item => !(item.Target as TMP_Text));
                _styleRefs.RemoveAll(item => !item.IsAlive);
            }
            if (sample)
            {
                _sampleUpdateMilliseconds += (Stopwatch.GetTimestamp() - updateStart) * 1000.0 / Stopwatch.Frequency;
                _sampleFrameSeconds += Time.unscaledDeltaTime;
                if (++_sampleFrames >= 120) FinishSteadyObservation();
            }
        }
        private void Switch()
        {
            bool previousBold = FontSelection.Bold;
            int previousDelta = FontSelection.SizeDelta;
            FontSelection.Bold = _bold.Value;
            FontSelection.SizeDelta = TypographyPolicy.Delta(_sizeDelta.Value);
            if (!_enabled.Value)
            {
                _sampleArmed = false;
                FontSelection.Active = false;
                Restore();
                if (FontSelection.TypographyActive)
                {
                    foreach (Text text in Resources.FindObjectsOfTypeAll<Text>()) if (ScreenUi(text)) Apply(text);
                    foreach (TextMeshProUGUI text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>()) if (ScreenUi(text)) Apply(text);
                }
                FontSelection.Generation++;
                _status = FontSelection.TypographyActive ? "保留原中文字体；已应用粗细与字号" : "已恢复默认中文字体和字号";
                LogTypography();
                return;
            }
            FontResources previous = _selected;
            bool previousActive = FontSelection.Active;
            int previousGeneration = FontSelection.Generation;
            var undo = new List<Action>();
            try
            {
                string path = _resolver.Resolve(_family.Value, _file.Value);
                FontResources candidate = _cache.Find(item => item.Matches(path));
                bool cached = candidate != null;
                if (candidate == null) { candidate = new FontResources(path, _resolver, _family.Value); _cache.Add(candidate); }
                if (!_baselineUgui)
                    foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
                        if (ScreenUi(text) && text.font && !IsOwned(text.font)) { _baselineUgui = text.font; break; }
                if (!_baselineTmp)
                    foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                        if (font && font.name == "ARIALUNI SDF") { _baselineTmp = font; break; }
                if (!_baselineTmp) _baselineTmp = TMP_Settings.defaultFontAsset;
                _selected = candidate;
                if (!_fallback) _fallback = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
                foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    if (font && font.name == "ARIALUNI SDF") candidate.KeepFallback(font);
                FontSelection.UguiFont = candidate.Font; FontSelection.TmpFont = candidate.Tmp;
                FontSelection.SourcePath = candidate.Path; FontSelection.Family = candidate.Family;
                FontSelection.Generation++; FontSelection.Active = true;
                var watch = Stopwatch.StartNew();
                long fallbackBefore = _fallbackSelections;
                foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
                    if (ScreenUi(text))
                    {
                        Remember(text);
                        TextOriginal original; _texts.TryGetValue(text, out original);
                        TypographyState savedState = original.Typography.Copy(); TypographyValue typography = Read(text);
                        Text captured = text; Font font = text.font;
                        undo.Add(delegate { if (captured) { captured.font = font; Write(captured, typography); original.Typography = savedState; } });
                        Apply(text);
                    }
                foreach (TextMeshProUGUI text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                    if (ScreenUi(text))
                    {
                        Remember(text);
                        TmpOriginal original; _tmp.TryGetValue(text, out original);
                        TypographyState savedState = original.Typography.Copy(); TypographyValue typography = Read(text);
                        TextMeshProUGUI captured = text; TMP_FontAsset font = text.font; Material material = text.fontSharedMaterial;
                        undo.Add(delegate { if (captured) { captured.font = font; captured.fontSharedMaterial = material; Write(captured, typography); original.Typography = savedState; } });
                        Apply(text);
                    }
                RestoreOutOfScope();
                if (!FontSelection.Active) throw new InvalidOperationException("Selected font has invalid IMGUI metrics");
                long applyMilliseconds = watch.ElapsedMilliseconds;
                int validationMissing = CountMissingActiveTmp();
                _switches++;
                _status = "已应用 " + FontSourceResolver.DisplayName(candidate.Family) + "；缺字使用后备字体";
                if (Path.GetExtension(path).Equals(".ttc", StringComparison.OrdinalIgnoreCase)) _status += "（字体集首个字形）";
                Logger.LogInfo("STUDIO_FONT_SWITCH_APPLIED family=" + candidate.Family + " cached=" + cached + " buildMs=" + candidate.BuildMilliseconds +
                    " applyMs=" + applyMilliseconds + " ugui=" + _textRefs.Count + " tmp=" + _tmpRefs.Count + " styles=" + _styleRefs.Count + " switches=" + _switches + " tmpMissing=" + validationMissing +
                    " fallbackSelections=" + (_fallbackSelections - fallbackBefore) + " generation=" + FontSelection.Generation +
                    " sameSource=" + (previous != null && string.Equals(previous.Path, candidate.Path, StringComparison.OrdinalIgnoreCase)) +
                    " legacyFamily=" + candidate.LegacyFamily + " legacyHeight=" + candidate.LegacyPreferredHeight);
                WriteSwitchObservation(candidate, cached, applyMilliseconds, validationMissing);
                LogTypography();
                if (_verifyOutput != null)
                {
                    _sampleAfter = Time.unscaledTime + 2f;
                    _sampleFrames = 0;
                    _sampleFrameSeconds = _sampleUpdateMilliseconds = 0;
                    _sampleFontAssignments = _fontAssignments;
                    _sampleStyleAssignments = _styleAssignments;
                    _sampleArmed = true;
                }
                while (_cache.Count > 2)
                {
                    FontResources old = _cache.Find(item => item != _selected && !StillReferenced(item));
                    if (old == null) break;
                    _cache.Remove(old); old.Dispose();
                }
            }
            catch (Exception error)
            {
                _assigning = true;
                int restoreFailures = 0;
                for (int index = undo.Count - 1; index >= 0; index--)
                    try { undo[index](); } catch { restoreFailures++; }
                _assigning = false;
                _selected = previous;
                FontSelection.UguiFont = previous == null ? null : previous.Font;
                FontSelection.TmpFont = previous == null ? null : previous.Tmp;
                FontSelection.SourcePath = previous == null ? null : previous.Path;
                FontSelection.Family = previous == null ? null : previous.Family;
                FontSelection.Generation = previousGeneration;
                FontSelection.Active = previousActive;
                FontSelection.Bold = previousBold;
                FontSelection.SizeDelta = previousDelta;
                _status = restoreFailures == 0 ? error is ArgumentException ? error.Message : "未切换字体，已保留上一字体。请检查字体名称或文件。" : "切换中断，部分控件恢复失败。请关闭统一字体后重试。";
                Logger.LogWarning("STUDIO_FONT_SWITCH_FAILED " + error.GetType().Name);
            }
        }
        private void LogTypography()
        {
            Logger.LogInfo("STUDIO_FONT_TYPOGRAPHY_APPLIED bold=" + FontSelection.Bold + " delta=" + FontSelection.SizeDelta +
                " ugui=" + _textRefs.Count + " tmp=" + _tmpRefs.Count + " fontOverride=" + FontSelection.Active);
        }
        private void RestoreOutOfScope()
        {
            _assigning = true;
            try
            {
                foreach (WeakReference reference in _textRefs)
                {
                    Text text = reference.Target as Text; TextOriginal original;
                    if (text && !ScreenUi(text) && _texts.TryGetValue(text, out original))
                    { if (IsOwned(text.font)) text.font = original.Font; Write(text, original.Typography.Restore(Read(text))); }
                }
                foreach (WeakReference reference in _tmpRefs)
                {
                    TMP_Text text = reference.Target as TMP_Text; TmpOriginal original;
                    if (text && !ScreenUi(text) && _tmp.TryGetValue(text, out original))
                    { if (IsOwned(text.font)) { text.font = original.Font; text.fontSharedMaterial = original.Material; } Write(text, original.Typography.Restore(Read(text))); }
                }
            }
            finally { _assigning = false; }
        }
        private bool StillReferenced(FontResources resource)
        {
            foreach (WeakReference reference in _textRefs) { Text text = reference.Target as Text; if (text && text.font == resource.Font) return true; }
            foreach (WeakReference reference in _tmpRefs) { TMP_Text text = reference.Target as TMP_Text; if (text && text.font == resource.Tmp) return true; }
            foreach (WeakReference reference in _styleRefs) { GUIStyle style = reference.Target as GUIStyle; if (style != null && style.font == resource.Font) return true; }
            return false;
        }
        private int CountMissingActiveTmp()
        {
            if (_verifyOutput == null) return -1;
            var missing = new HashSet<uint>();
            foreach (WeakReference reference in _tmpRefs)
            {
                TMP_Text text = reference.Target as TMP_Text;
                if (!text || !text.isActiveAndEnabled || !ScreenUi(text)) continue;
                foreach (char character in text.text ?? "")
                    if (!char.IsWhiteSpace(character) && !_selected.Tmp.HasCharacter(character, true, true)) missing.Add(character);
            }
            return missing.Count;
        }
        private void WriteSwitchObservation(FontResources candidate, bool cached, long applyMs, int missing)
        {
            if (_verifyOutput == null) return;
            try
            {
                string output = Path.Combine(Path.GetDirectoryName(_verifyOutput), Path.GetFileNameWithoutExtension(_verifyOutput) + "-switch-" + _switches + ".json");
                var report = new { schema_version = 1, family = candidate.Family, file = Path.GetFileName(candidate.Path), cached,
                    fontBuildMilliseconds = candidate.BuildMilliseconds, applyMilliseconds = applyMs,
                    uguiTracked = _textRefs.Count, tmpTracked = _tmpRefs.Count, imguiStylesTracked = _styleRefs.Count,
                    fontAssignments = _fontAssignments, styleAssignments = _styleAssignments,
                    generation = FontSelection.Generation, active = FontSelection.Active,
                    atlasCount = candidate.Tmp.atlasTextures.Length, atlasSize = candidate.Tmp.atlasWidth,
                    fallbackCount = candidate.Tmp.fallbackFontAssetTable.Count,
                    activeTmpMissingCharacters = missing,
                    fallbackSelections = _fallbackSelections,
                    legacyFamily = candidate.LegacyFamily, legacyFontSize = candidate.Font.fontSize,
                    legacyPreferredHeight = candidate.LegacyPreferredHeight, separateTmpSourceFont = candidate.PathFont != candidate.Font,
                    limitation = "One font-switch snapshot. Native creation and assignment timing measured on main thread. Pixel appearance, input response and steady frame cost require live interaction." };
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            catch (Exception error) { Logger.LogWarning("STUDIO_FONT_SWITCH_REPORT_REFUSED " + error.GetType().Name); }
        }
        private void FinishSteadyObservation()
        {
            _sampleArmed = false;
            try
            {
                string output = Path.Combine(Path.GetDirectoryName(_verifyOutput), Path.GetFileNameWithoutExtension(_verifyOutput) + "-switch-" + _switches + "-steady.json");
                var report = new { schema_version = 1, frames = _sampleFrames, active = FontSelection.Active,
                    family = FontSelection.Family, averageFrameMilliseconds = _sampleFrameSeconds * 1000.0 / _sampleFrames,
                    controllerUpdateAverageMilliseconds = _sampleUpdateMilliseconds / _sampleFrames,
                    additionalFontAssignments = _fontAssignments - _sampleFontAssignments,
                    additionalStyleAssignments = _styleAssignments - _sampleStyleAssignments,
                    atlasCount = _selected.Tmp.atlasTextures.Length,
                    limitation = "120 observed frames after a two-second settling period. Frame time includes the whole game. Controller Update timing excludes Harmony hooks, font rendering and native GPU work, so this is not a full performance benchmark." };
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonConvert.SerializeObject(report, Formatting.Indented));
                Logger.LogInfo("STUDIO_FONT_STEADY_OBSERVED frames=120 updateAvgMs=" + (_sampleUpdateMilliseconds / _sampleFrames).ToString("F4"));
            }
            catch (Exception error) { Logger.LogWarning("STUDIO_FONT_STEADY_REPORT_REFUSED " + error.GetType().Name); }
        }

        private static void TextFontPrefix(Text __instance, ref Font value)
        {
            if (!Ready || !ScreenUi(__instance)) return;
            _instance.Remember(__instance);
            TextOriginal original; if (!IsSelected(value) && _instance._texts.TryGetValue(__instance, out original)) original.Font = value;
            if (FontSelection.Active) value = _instance.Choose(__instance.text);
        }
        private static void TmpFontPrefix(TMP_Text __instance, ref TMP_FontAsset value)
        {
            if (!Ready || !(__instance is TextMeshProUGUI) || !ScreenUi(__instance)) return;
            _instance.Remember(__instance);
            TmpOriginal original;
            if (!_instance.IsOwned(value) && _instance._tmp.TryGetValue(__instance, out original))
            { original.Font = value; original.Material = value ? value.material : null; }
            if (FontSelection.Active) value = FontSelection.TmpFont;
        }
        private static void TextEnabled(Text __instance) { if (Ready) _instance.Apply(__instance); }
        private static void TmpEnabled(TextMeshProUGUI __instance) { if (Ready) _instance.Apply(__instance); }
        private static void TmpChanged(TMP_Text __instance) { if (Ready) _instance.Apply(__instance); }
        private static bool IsSelected(Font font) { return _instance != null && _instance.IsOwned(font); }
        private static TypographyState Track(Text text)
        {
            if (!Ready || !ScreenUi(text) || FontSelection.ExternalTypography(text)) return null;
            _instance.Remember(text); TextOriginal original; _instance._texts.TryGetValue(text, out original); return original.Typography;
        }
        private static TypographyState Track(TMP_Text text)
        {
            if (!Ready || !(text is TextMeshProUGUI) || !ScreenUi(text) || FontSelection.ExternalTypography(text)) return null;
            _instance.Remember(text); TmpOriginal original; _instance._tmp.TryGetValue(text, out original); return original.Typography;
        }
        private static void TextSizePrefix(Text __instance, int value, MethodBase __originalMethod)
        {
            TypographyState state = Track(__instance); if (state == null) return;
            if (__originalMethod.Name == "set_fontSize") state.ExternalSize(value, __instance.fontSize);
            else if (__originalMethod.Name == "set_resizeTextMinSize") state.ExternalMinimum(value, __instance.resizeTextMinSize); else state.ExternalMaximum(value, __instance.resizeTextMaxSize);
        }
        private static void TextStylePrefix(Text __instance, FontStyle value) { TypographyState state = Track(__instance); if (state != null) state.ExternalStyle((int)value, (int)__instance.fontStyle); }
        private static void TextAutoPrefix(Text __instance, bool value) { TypographyState state = Track(__instance); if (state != null) state.ExternalAutomatic(value, __instance.resizeTextForBestFit); }
        private static void TmpSizePrefix(TMP_Text __instance, float value, MethodBase __originalMethod)
        {
            TypographyState state = Track(__instance); if (state == null) return;
            if (__originalMethod.Name == "set_fontSize") state.ExternalSize(value, __instance.fontSize);
            else if (__originalMethod.Name == "set_fontSizeMin") state.ExternalMinimum(value, __instance.fontSizeMin); else state.ExternalMaximum(value, __instance.fontSizeMax);
        }
        private static void TmpStylePrefix(TMP_Text __instance, FontStyles value) { TypographyState state = Track(__instance); if (state != null) state.ExternalStyle((int)value, (int)__instance.fontStyle); }
        private static void TmpAutoPrefix(TMP_Text __instance, bool value) { TypographyState state = Track(__instance); if (state != null) state.ExternalAutomatic(value, __instance.enableAutoSizing); }
        private static void DrawStyle(GUIStyle __instance, GUIContent __1, out StyleCall __state)
        { __state = default(StyleCall); if (Ready) _instance.BeginStyle(__instance, __1, out __state); }
        private static void MeasureStyle(GUIStyle __instance, GUIContent __0, out StyleCall __state)
        {
            __state = default(StyleCall);
            if (!Ready) return;
            if (__instance.name == "textfield")
                try { ImGuiMetricObservation.Capture(_instance._verifyOutput, FontSelection.SourcePath); }
                catch (Exception error) { _instance.Logger.LogWarning("STUDIO_FONT_METRICS_REFUSED " + error.GetType().Name); }
            _instance.BeginStyle(__instance, __0, out __state);
        }
        private static void WindowStyle(GUIContent __3, GUIStyle __4, out StyleCall __state)
        { __state = default(StyleCall); if (Ready && __4 != null) _instance.BeginStyle(__4, __3, out __state); }
        private static void StyleFinalizer(StyleCall __state)
        {
            if (__state.Original == null || !__state.Original.Scope.Exit()) return;
            __state.Style.font = __state.Original.Font;
            __state.Style.fontSize = (int)__state.Original.Scope.Original.Size;
            __state.Style.fontStyle = (FontStyle)__state.Original.Scope.Original.Style;
        }
        private static void StyleCloned(GUIStyle __instance, GUIStyle __0)
        {
            StyleOriginal original;
            if (_instance == null || __0 == null || !_instance._styles.TryGetValue(__0, out original) || original.Scope.Depth == 0) return;
            __instance.font = original.Font;
            __instance.fontSize = (int)original.Scope.Original.Size;
            __instance.fontStyle = (FontStyle)original.Scope.Original.Style;
        }
        private Font Choose(string text)
        {
            if (_selected.Contains(text) || !_fallback) return _selected.Font;
            _fallbackSelections++;
            return _fallback;
        }

        private void Remember(Text text)
        {
            TextOriginal state;
            if (_texts.TryGetValue(text, out state)) { if (!IsOwned(text.font)) state.Font = text.font; return; }
            _texts.Add(text, new TextOriginal { Font = IsOwned(text.font) ? _baselineUgui : text.font, Typography = new TypographyState(Read(text)) }); _textRefs.Add(new WeakReference(text));
        }
        private void Remember(TMP_Text text)
        {
            TmpOriginal state;
            if (!_tmp.TryGetValue(text, out state))
            {
                bool owned = IsOwned(text.font);
                state = new TmpOriginal { Font = owned ? _baselineTmp : text.font, Material = owned && _baselineTmp ? _baselineTmp.material : text.fontSharedMaterial, Typography = new TypographyState(Read(text)) };
                _tmp.Add(text, state); _tmpRefs.Add(new WeakReference(text));
            }
            else if (!IsOwned(text.font)) { state.Font = text.font; state.Material = text.fontSharedMaterial; }
            if (FontSelection.Active && _selected != null) _selected.KeepFallback(state.Font);
        }
        private bool IsOwned(Font font) { foreach (FontResources item in _cache) if (font == item.Font) return true; return font == _fallback && font != null; }
        private bool IsOwned(TMP_FontAsset font) { foreach (FontResources item in _cache) if (font == item.Tmp) return true; return false; }
        private void Apply(Text text)
        {
            if (!ScreenUi(text)) return;
            Remember(text);
            _assigning = true;
            try
            {
                if (FontSelection.Active) { Font chosen = Choose(text.text); if (text.font != chosen) { text.font = chosen; _fontAssignments++; } }
                if (!FontSelection.ExternalTypography(text))
                {
                    TextOriginal original; _texts.TryGetValue(text, out original);
                    Write(text, original.Typography.Apply(Read(text), FontSelection.Bold, FontSelection.SizeDelta, InputCeiling(text)));
                }
            }
            finally { _assigning = false; }
        }
        private void Apply(TMP_Text text)
        {
            if (!(text is TextMeshProUGUI) || !ScreenUi(text)) return;
            Remember(text);
            _assigning = true;
            try
            {
                if (FontSelection.Active && text.font != _selected.Tmp) { text.font = _selected.Tmp; _fontAssignments++; }
                if (!FontSelection.ExternalTypography(text))
                {
                    TmpOriginal original; _tmp.TryGetValue(text, out original);
                    Write(text, original.Typography.Apply(Read(text), FontSelection.Bold, FontSelection.SizeDelta, InputCeiling(text)));
                }
            }
            finally { _assigning = false; }
        }
        private void BeginStyle(GUIStyle style, GUIContent content, out StyleCall state)
        {
            state = default(StyleCall);
            if (FontSelection.Active && !_selected.ImGuiValidated)
            {
                GUIStyle probe = new GUIStyle(style) { font = _selected.Font, fontSize = 14 };
                if (probe.lineHeight <= 1f || float.IsNaN(probe.lineHeight))
                {
                    FontSelection.Active = false;
                    Restore();
                    _status = "所选字体行高无效，已恢复原字体。";
                    Logger.LogWarning("STUDIO_FONT_INVALID_IMGUI_RESTORED");
                    return;
                }
                _selected.ImGuiValidated = true;
            }
            StyleOriginal original;
            if (!_styles.TryGetValue(style, out original))
            {
                original = new StyleOriginal();
                _styles.Add(style, original); _styleRefs.Add(new WeakReference(style));
            }
            bool outermost = original.Scope.Enter(new TypographyValue { Size = style.fontSize, Style = (int)style.fontStyle });
            state = new StyleCall { Style = style, Original = original };
            if (!outermost) return;
            original.Font = style.font;
            Font inherited = style.font ? style.font : GUI.skin ? GUI.skin.font : null;
            int effective = FontSourceResolver.EffectiveStyleSize(style.fontSize, inherited ? inherited.fontSize : 0);
            if (FontSelection.Active) { style.font = Choose(content == null ? null : content.text); _styleAssignments++; }
            if (FontSelection.SizeDelta != 0 || FontSelection.Active && style.fontSize == 0)
                style.fontSize = (int)TypographyPolicy.Size(effective, FontSelection.SizeDelta);
            style.fontStyle = (FontStyle)TypographyPolicy.Style(original.Scope.Original.Style, FontSelection.Bold);
            if (style.fixedHeight > 0 && FontSelection.SizeDelta > 0)
                while (style.fontSize > effective && style.lineHeight + style.padding.vertical > style.fixedHeight) style.fontSize--;
        }

        private static TypographyValue Read(Text text)
        { return new TypographyValue { Size = text.fontSize, Minimum = text.resizeTextMinSize, Maximum = text.resizeTextMaxSize, Automatic = text.resizeTextForBestFit, Style = (int)text.fontStyle }; }
        private static TypographyValue Read(TMP_Text text)
        { return new TypographyValue { Size = text.fontSize, Minimum = text.fontSizeMin, Maximum = text.fontSizeMax, Automatic = text.enableAutoSizing, Style = (int)text.fontStyle }; }
        private static void Write(Text text, TypographyValue value)
        {
            if (text.fontStyle != (FontStyle)value.Style) text.fontStyle = (FontStyle)value.Style;
            if (text.fontSize != (int)value.Size) text.fontSize = (int)value.Size;
            if (text.resizeTextMinSize != (int)value.Minimum) text.resizeTextMinSize = (int)value.Minimum;
            if (text.resizeTextMaxSize != (int)value.Maximum) text.resizeTextMaxSize = (int)value.Maximum;
            if (text.resizeTextForBestFit != value.Automatic) text.resizeTextForBestFit = value.Automatic;
        }
        private static void Write(TMP_Text text, TypographyValue value)
        {
            if (text.fontStyle != (FontStyles)value.Style) text.fontStyle = (FontStyles)value.Style;
            if (text.fontSize != value.Size) text.fontSize = value.Size;
            if (text.fontSizeMin != value.Minimum) text.fontSizeMin = value.Minimum;
            if (text.fontSizeMax != value.Maximum) text.fontSizeMax = value.Maximum;
            if (text.enableAutoSizing != value.Automatic) text.enableAutoSizing = value.Automatic;
        }
        private static float InputCeiling(Text text)
        {
            if (!text.GetComponentInParent<InputField>()) return float.PositiveInfinity;
            float ratio = text.font && text.font.fontSize > 0 ? Math.Max(1f, text.font.lineHeight / (float)text.font.fontSize) : 1.2f;
            return (float)Math.Floor(text.rectTransform.rect.height / (ratio * Math.Max(1f, text.lineSpacing)));
        }
        private static float InputCeiling(TMP_Text text)
        {
            if (!text.GetComponentInParent<TMP_InputField>()) return float.PositiveInfinity;
            float ratio = text.font && text.font.faceInfo.pointSize > 0 ? Math.Max(1f, text.font.faceInfo.lineHeight / text.font.faceInfo.pointSize) : 1.2f;
            return (float)Math.Floor(text.rectTransform.rect.height / ratio);
        }
        internal static void ReleaseTypography(Component component)
        {
            if (_instance == null) return;
            bool assigning = _assigning; _assigning = true;
            try
            {
                Text text = component as Text; TextOriginal original;
                if (text) _instance.Remember(text);
                if (text && _instance._texts.TryGetValue(text, out original))
                { if (_instance.IsOwned(text.font)) text.font = original.Font; Write(text, original.Typography.Restore(Read(text))); }
                TMP_Text tmp = component as TMP_Text; TmpOriginal tmpOriginal;
                if (tmp) _instance.Remember(tmp);
                if (tmp && _instance._tmp.TryGetValue(tmp, out tmpOriginal))
                {
                    if (_instance.IsOwned(tmp.font)) { tmp.font = tmpOriginal.Font; tmp.fontSharedMaterial = tmpOriginal.Material; }
                    Write(tmp, tmpOriginal.Typography.Restore(Read(tmp)));
                }
            }
            finally { _assigning = assigning; }
        }

        private void Restore()
        {
            _assigning = true;
            try
            {
                foreach (WeakReference reference in _textRefs)
                {
                    Text text = reference.Target as Text; TextOriginal original;
                    if (text && _texts.TryGetValue(text, out original))
                    { if (IsOwned(text.font)) text.font = original.Font; if (!FontSelection.ExternalTypography(text)) Write(text, original.Typography.Restore(Read(text))); }
                }
                foreach (WeakReference reference in _tmpRefs)
                {
                    TMP_Text text = reference.Target as TMP_Text; TmpOriginal original;
                    if (text && _tmp.TryGetValue(text, out original))
                    { if (IsOwned(text.font)) { text.font = original.Font; text.fontSharedMaterial = original.Material; } if (!FontSelection.ExternalTypography(text)) Write(text, original.Typography.Restore(Read(text))); }
                }
            }
            finally { _assigning = false; }
        }
        private void DrawStatus(ConfigEntryBase entry)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(_status);
            GUILayout.Label("字体预览　工作室 Studio 0123");
            GUILayout.Label("保留原中文字体也可单独加粗、微调字号。");
            GUILayout.BeginHorizontal();
            QuickChoice("等线");
            QuickChoice("楷体");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("恢复默认中文")) RestoreDefaultChinese();
            if (GUILayout.Button("重新应用字体")) _changeAt = Time.unscaledTime;
            GUILayout.EndVertical();
        }
        private void DrawFamily(ConfigEntryBase entry)
        {
            if (Event.current.type == EventType.Layout) _showFamilies = _pendingFamilyList;
            GUILayout.BeginVertical();
            GUILayout.Label("中文字体：" + FontSourceResolver.DisplayName(_family.Value));
            if (!_catalogPublished)
            {
                GUILayout.Label(_resolver.CatalogError == null ? "正在筛选中文字体…" : "字体列表暂不可用，当前设置已保留。");
                GUILayout.EndVertical();
                return;
            }
            if (Array.IndexOf(_chineseFamilies, _family.Value) < 0) GUILayout.Label("当前选项未通过中文检测，保留设置值但不列入列表。");
            if (GUILayout.Button(_showFamilies ? "收起字体列表" : "选择中文字体")) _pendingFamilyList = !_showFamilies;
            if (_showFamilies)
            {
                _familyScroll = GUILayout.BeginScrollView(_familyScroll, GUILayout.Height(200));
                for (int index = 0; index < _chineseFamilies.Length; index++)
                    if (GUILayout.Button(_familyLabels[index])) { SelectFamily(_chineseFamilies[index]); _pendingFamilyList = false; }
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
        }
        private void QuickChoice(string family)
        {
            if (!_resolver.HasFamily(family)) return;
            if (!GUILayout.Button(family)) return;
            SelectFamily(family);
        }
        private void RestoreDefaultChinese()
        {
            _enabled.Value = false;
            _bold.Value = false;
            _sizeDelta.Value = 0;
            _changeAt = Time.unscaledTime;
        }
        private void SelectFamily(string family)
        {
            if (!_resolver.HasFamily(family)) return;
            _file.Value = "";
            _family.Value = family;
            _enabled.Value = true;
            _changeAt = Time.unscaledTime + 0.1f;
        }
        private void OnDestroy() { Cleanup(); }
        private void Cleanup()
        {
            FontSelection.Active = false;
            FontSelection.Bold = false;
            FontSelection.SizeDelta = 0;
            try { Restore(); }
            catch (Exception error) { Logger.LogWarning("STUDIO_FONT_RESTORE_PARTIAL " + error.GetType().Name); }
            finally
            {
                SceneManager.sceneLoaded -= SceneLoaded;
                if (_enabled != null) _enabled.SettingChanged -= Changed;
                if (_family != null) _family.SettingChanged -= Changed;
                if (_file != null) _file.SettingChanged -= Changed;
                if (_bold != null) _bold.SettingChanged -= Changed;
                if (_sizeDelta != null) _sizeDelta.SettingChanged -= Changed;
                if (_harmony != null)
                    foreach (MethodBase method in _patches)
                        try { _harmony.Unpatch(method, HarmonyPatchType.All, PluginGuid); }
                        catch (Exception error) { Logger.LogWarning("STUDIO_FONT_UNPATCH_PARTIAL " + error.GetType().Name); }
                _patches.Clear();
                foreach (FontResources resource in _cache)
                    if (!StillReferenced(resource)) resource.Dispose();
                _cache.Clear();
                FontSelection.UguiFont = null; FontSelection.TmpFont = null;
                FontSelection.SourcePath = null; FontSelection.Family = null;
                _instance = null;
            }
        }
    }
}
