using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TextCore.LowLevel;

namespace Codex.StudioPanelTypography
{
    internal sealed class SidebarTypography
    {
        private readonly ManualLogSource _logger;
        private readonly Dictionary<Text, TextState> _texts = new Dictionary<Text, TextState>();
        private readonly Dictionary<TMP_Text, TmpState> _tmp = new Dictionary<TMP_Text, TmpState>();
        private Font _font;
        private TMP_FontAsset _tmpFont;
        private Transform _root;
        private float _nextScan;
        private int _scans;
        private bool _reported;
        private bool _fontAttempted;
        private bool _reportAttempted;
        private bool _finalReportAttempted;
        private bool _ownsTmpFont;
        private Font _externalFont;
        private TMP_FontAsset _externalTmp;
        private bool _sharedActive;
        private bool _usesSharedCjk;
        private string _effectiveTmpFamily = "original-fonts";
        private int _fontRetries;
        private int _activeScans;

        internal SidebarTypography(ManualLogSource logger) { _logger = logger; }

        internal void Tick()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.75f;
            if (SceneManager.GetActiveScene().name != "Studio")
            {
                if (_texts.Count > 0 || _tmp.Count > 0) RestoreTracked();
                _root = null;
                return;
            }
            if (!_root)
            {
                if (_texts.Count > 0 || _tmp.Count > 0) RestoreTracked();
                GameObject root = GameObject.Find("StudioScene/Canvas Main Menu");
                if (!root) return;
                _root = root.transform;
                _scans = 0;
            }
            _sharedActive = SharedFontAccess.Read(out _externalFont, out _externalTmp);
            if (!_sharedActive) EnsureFont();
            PruneDestroyed();
            foreach (Text text in _root.GetComponentsInChildren<Text>(true)) Apply(text);
            foreach (TMP_Text text in _root.GetComponentsInChildren<TMP_Text>(true)) Apply(text);
            _scans++;
            bool hasActiveText = false;
            foreach (Text text in _texts.Keys) if (text && text.isActiveAndEnabled) { hasActiveText = true; break; }
            if (!hasActiveText)
                foreach (TMP_Text text in _tmp.Keys) if (text && text.isActiveAndEnabled) { hasActiveText = true; break; }
            _activeScans = hasActiveText && _root.gameObject.activeInHierarchy ? _activeScans + 1 : 0;
            if (!_reported && _scans >= 4 && _activeScans >= 2)
            {
                _reported = true;
                _logger.LogInfo("STUDIO_SIDEBAR_TYPOGRAPHY_READY ugui=" + _texts.Count + " tmp=" + _tmp.Count +
                    " yahei=" + (_font != null) + " tmpFamily=" + _effectiveTmpFamily + " groups=7 minSize=13");
                WriteObservation(false);
            }
        }

        private void EnsureFont()
        {
            if (_fontAttempted)
            {
                if (!_tmpFont && _fontRetries++ < 20) AcquireCjkFallback();
                return;
            }
            _fontAttempted = true;
            _font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 14);
            if (!_font) { _logger.LogWarning("STUDIO_SIDEBAR_FONT_UNAVAILABLE"); return; }
            try
            {
                _tmpFont = TMP_FontAsset.CreateFontAsset(_font, 48, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (_tmpFont)
                {
                    _ownsTmpFont = true;
                    _effectiveTmpFamily = "Microsoft YaHei";
                    _tmpFont.name = "Studio Sidebar Microsoft YaHei";
                    _tmpFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    foreach (TMP_FontAsset available in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                        if (available && available != _tmpFont && available.name == "ARIALUNI SDF") _tmpFont.fallbackFontAssetTable.Add(available);
                }
                else AcquireCjkFallback();
            }
            catch (Exception error)
            {
                _logger.LogWarning("STUDIO_SIDEBAR_TMP_YAHEI_UNAVAILABLE " + error.GetType().Name);
                AcquireCjkFallback();
            }
        }

        private void AcquireCjkFallback()
        {
            foreach (TMP_FontAsset available in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (!available || available.name != "ARIALUNI SDF") continue;
                _tmpFont = UnityEngine.Object.Instantiate(available);
                _tmpFont.name = "Studio Sidebar Arial Unicode";
                _tmpFont.atlasTextures = available.atlasTextures;
                _tmpFont.material = available.material;
                _tmpFont.atlasPopulationMode = AtlasPopulationMode.Static;
                _tmpFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
                _ownsTmpFont = true;
                _usesSharedCjk = true;
                _effectiveTmpFamily = "Arial Unicode MS";
                foreach (TMP_Text text in _root.GetComponentsInChildren<TMP_Text>(true)) KeepFallback(text.font);
                _logger.LogInfo("STUDIO_SIDEBAR_TMP_SHARED_CJK_READY family=ArialUnicodeMS sharedAtlas=True");
                return;
            }
        }

        private void KeepFallback(TMP_FontAsset original)
        {
            if (!_usesSharedCjk || !_tmpFont || !original || original == _tmpFont ||
                _tmpFont.fallbackFontAssetTable.Contains(original)) return;
            _tmpFont.fallbackFontAssetTable.Add(original);
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            while (transform.parent) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }

        private static SidebarTextStyle Style(Component component, string path)
        {
            RectTransform rect = component.transform as RectTransform;
            bool input = component.GetComponentInParent<InputField>() || component.GetComponentInParent<TMP_InputField>();
            bool dropdown = component.GetComponentInParent<Dropdown>() || component.GetComponentInParent<TMP_Dropdown>();
            bool control = component.GetComponentInParent<Button>() || component.GetComponentInParent<Toggle>();
            return SidebarTextPolicy.Resolve(path, rect.rect.width, rect.rect.height, input, control, dropdown);
        }

        private void Apply(Text text)
        {
            string path = PathOf(text.transform);
            if (!SidebarTextPolicy.Includes(path)) return;
            TextState saved;
            if (!_texts.TryGetValue(text, out saved)) { saved = new TextState(text); _texts.Add(text, saved); }
            SidebarTextStyle style = Style(text, path);
            Font chosenFont = _sharedActive ? _externalFont : _font;
            if (chosenFont && text.font != chosenFont) text.font = chosenFont;
            if (text.fontSize != style.Size) text.fontSize = style.Size;
            if (text.resizeTextForBestFit != (style.Minimum < style.Size)) text.resizeTextForBestFit = style.Minimum < style.Size;
            if (text.resizeTextMinSize != style.Minimum) text.resizeTextMinSize = style.Minimum;
            if (text.resizeTextMaxSize != style.Size) text.resizeTextMaxSize = style.Size;
            HorizontalWrapMode wrap = style.Role == "input" ? saved.Horizontal : style.Wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            if (text.horizontalOverflow != wrap) text.horizontalOverflow = wrap;
            VerticalWrapMode vertical = style.Role == "input" ? saved.Vertical : VerticalWrapMode.Truncate;
            if (text.verticalOverflow != vertical) text.verticalOverflow = vertical;
            TextAnchor alignment = style.Center ? TextAnchor.MiddleCenter : saved.Alignment;
            if (text.alignment != alignment) text.alignment = alignment;
        }

        private void Apply(TMP_Text text)
        {
            string path = PathOf(text.transform);
            if (!SidebarTextPolicy.Includes(path)) return;
            TmpState saved;
            if (!_tmp.TryGetValue(text, out saved)) { saved = new TmpState(text); _tmp.Add(text, saved); }
            SidebarTextStyle style = Style(text, path);
            KeepFallback(saved.OriginalFont);
            TMP_FontAsset chosenFont = _sharedActive ? _externalTmp : _tmpFont;
            if (chosenFont && text.font != chosenFont) text.font = chosenFont;
            if (saved.LastRoleSize != style.Size)
            {
                text.fontSize = style.Size;
                saved.LastRoleSize = style.Size;
            }
            if (text.enableAutoSizing != (style.Minimum < style.Size)) text.enableAutoSizing = style.Minimum < style.Size;
            if (Math.Abs(text.fontSizeMin - style.Minimum) > 0.01f) text.fontSizeMin = style.Minimum;
            if (Math.Abs(text.fontSizeMax - style.Size) > 0.01f) text.fontSizeMax = style.Size;
            bool wrap = style.Role == "input" ? saved.Wrap : style.Wrap;
            TextOverflowModes overflow = style.Role == "input" ? saved.Overflow : TextOverflowModes.Truncate;
            if (text.enableWordWrapping != wrap) text.enableWordWrapping = wrap;
            if (text.overflowMode != overflow) text.overflowMode = overflow;
            TextAlignmentOptions alignment = style.Center ? TextAlignmentOptions.Midline : saved.Alignment;
            if (text.alignment != alignment) text.alignment = alignment;
        }

        internal void Stop()
        {
            if (_root)
            {
                try
                {
                    PruneDestroyed();
                    foreach (Text text in _root.GetComponentsInChildren<Text>(true)) Apply(text);
                    foreach (TMP_Text text in _root.GetComponentsInChildren<TMP_Text>(true)) Apply(text);
                }
                catch (Exception error) { _logger.LogWarning("STUDIO_SIDEBAR_FINAL_SCAN_PARTIAL " + error.GetType().Name); }
            }
            WriteObservation(true);
            RestoreTracked();
            if (_tmpFont && _ownsTmpFont) UnityEngine.Object.Destroy(_tmpFont);
            if (_font) UnityEngine.Object.Destroy(_font);
        }

        private void PruneDestroyed()
        {
            var oldTexts = new List<Text>();
            foreach (Text text in _texts.Keys) if (!text) oldTexts.Add(text);
            foreach (Text text in oldTexts) _texts.Remove(text);
            var oldTmp = new List<TMP_Text>();
            foreach (TMP_Text text in _tmp.Keys) if (!text) oldTmp.Add(text);
            foreach (TMP_Text text in oldTmp) _tmp.Remove(text);
        }

        private void RestoreTracked()
        {
            foreach (var pair in _texts) if (pair.Key) pair.Value.Restore(pair.Key);
            foreach (var pair in _tmp) if (pair.Key) pair.Value.Restore(pair.Key);
            _texts.Clear();
            _tmp.Clear();
        }

        [Serializable]
        private sealed class Observation
        {
            public int uguiCount, tmpCount, activeCount, activeOverflowCount;
            public bool yaheiAvailable, tmpFontAvailable, tmpYaheiAvailable;
            public string effectiveTmpFamily;
            public bool sharedCjkAtlas;
            public int minimumSize = 13;
            public bool multiAtlas;
            public int atlasCount, atlasSize, fallbackCount, visibleTmpCharactersMissing;
            public string phase;
            public List<OverflowIdentity> activeOverflow = new List<OverflowIdentity>();
            public string limitation = "Includes inactive templates and currently created children at snapshot time. Overflow identity uses instance id and hashed hierarchy, with no text or input values. Pixel layout and unopened dynamic windows require separate checks.";
        }

        [Serializable]
        private sealed class OverflowIdentity
        {
            public int instanceId;
            public string hierarchySha256;
            public string group;
            public string component;
            public float width, height;
        }

        private static OverflowIdentity Overflow(Component component)
        {
            string path = PathOf(component.transform);
            string hash;
            using (var algorithm = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path))).Replace("-", "").ToLowerInvariant();
            string relative = path.Substring(SidebarTextPolicy.Root.Length);
            Rect rect = ((RectTransform)component.transform).rect;
            return new OverflowIdentity { instanceId = component.GetInstanceID(), hierarchySha256 = hash, group = relative.Split('/')[0],
                component = component is TMP_Text ? "TMP" : "uGUI", width = rect.width, height = rect.height };
        }

        private void WriteObservation(bool final)
        {
            string requested = Environment.GetEnvironmentVariable("KKS_SIDEBAR_TYPOGRAPHY_VERIFY_OUTPUT");
            if (string.IsNullOrEmpty(requested) || (final ? _finalReportAttempted : _reportAttempted)) return;
            if (final) _finalReportAttempted = true;
            else _reportAttempted = true;
            try
            {
                string allowed = Path.GetFullPath(Path.Combine(Paths.GameRootPath, ".scratch", "studio-translation-20261126", "full-pass-20261127")) + Path.DirectorySeparatorChar;
                string output = Path.GetFullPath(requested);
                if (final) output = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "-final.json");
                if (!Path.IsPathRooted(requested) || !output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(output) != ".json" || File.Exists(output))
                    throw new InvalidOperationException("Typography report path is not a new task-local JSON");
                PruneDestroyed();
                var observation = new Observation { uguiCount = _texts.Count, tmpCount = _tmp.Count, yaheiAvailable = _font != null,
                    tmpFontAvailable = _tmpFont != null, tmpYaheiAvailable = _tmpFont != null && !_usesSharedCjk,
                    effectiveTmpFamily = _effectiveTmpFamily, sharedCjkAtlas = _usesSharedCjk, phase = final ? "final" : "active-ready" };
                if (_tmpFont)
                {
                    observation.multiAtlas = _tmpFont.isMultiAtlasTexturesEnabled;
                    observation.atlasCount = _tmpFont.atlasTextures.Length;
                    observation.atlasSize = _tmpFont.atlasWidth;
                    observation.fallbackCount = _tmpFont.fallbackFontAssetTable.Count;
                }
                foreach (Text text in _texts.Keys)
                    if (text && text.isActiveAndEnabled)
                    {
                        observation.activeCount++;
                        if (text.preferredHeight > text.rectTransform.rect.height + 1f ||
                            (!text.resizeTextForBestFit && text.preferredWidth > text.rectTransform.rect.width + 1f))
                        {
                            observation.activeOverflowCount++;
                            if (observation.activeOverflow.Count < 256) observation.activeOverflow.Add(Overflow(text));
                        }
                    }
                foreach (TMP_Text text in _tmp.Keys)
                    if (text && text.isActiveAndEnabled)
                    {
                        observation.activeCount++;
                        if (_tmpFont)
                            foreach (char character in text.text ?? "")
                                if (!char.IsWhiteSpace(character) && !_tmpFont.HasCharacter(character, true, true)) observation.visibleTmpCharactersMissing++;
                        if (text.isTextOverflowing)
                        {
                            observation.activeOverflowCount++;
                            if (observation.activeOverflow.Count < 256) observation.activeOverflow.Add(Overflow(text));
                        }
                    }
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(observation, true));
            }
            catch (Exception error) { _logger.LogWarning("STUDIO_SIDEBAR_OBSERVATION_REFUSED " + error.GetType().Name); }
        }

        private sealed class TextState
        {
            private readonly Font _font;
            private readonly int _size, _minimum, _maximum;
            private readonly bool _auto;
            internal readonly HorizontalWrapMode Horizontal;
            internal readonly VerticalWrapMode Vertical;
            internal readonly TextAnchor Alignment;
            internal TextState(Text text)
            {
                _font = text.font; _size = text.fontSize; _minimum = text.resizeTextMinSize; _maximum = text.resizeTextMaxSize;
                _auto = text.resizeTextForBestFit; Horizontal = text.horizontalOverflow; Vertical = text.verticalOverflow; Alignment = text.alignment;
            }
            internal void Restore(Text text)
            {
                text.font = _font; text.fontSize = _size; text.resizeTextMinSize = _minimum; text.resizeTextMaxSize = _maximum;
                text.resizeTextForBestFit = _auto; text.horizontalOverflow = Horizontal; text.verticalOverflow = Vertical; text.alignment = Alignment;
            }
        }

        private sealed class TmpState
        {
            private readonly TMP_FontAsset _font;
            internal TMP_FontAsset OriginalFont { get { return _font; } }
            private readonly Material _material;
            private readonly float _size, _minimum, _maximum;
            private readonly bool _auto;
            internal readonly bool Wrap;
            internal readonly TextOverflowModes Overflow;
            internal readonly TextAlignmentOptions Alignment;
            internal int LastRoleSize;
            internal TmpState(TMP_Text text)
            {
                _font = text.font; _material = text.fontSharedMaterial; _size = text.fontSize; _minimum = text.fontSizeMin; _maximum = text.fontSizeMax;
                _auto = text.enableAutoSizing; Wrap = text.enableWordWrapping; Overflow = text.overflowMode; Alignment = text.alignment;
            }
            internal void Restore(TMP_Text text)
            {
                text.font = _font; text.fontSharedMaterial = _material; text.fontSize = _size; text.fontSizeMin = _minimum; text.fontSizeMax = _maximum;
                text.enableAutoSizing = _auto; text.enableWordWrapping = Wrap; text.overflowMode = Overflow; text.alignment = Alignment;
            }
        }
    }
}
