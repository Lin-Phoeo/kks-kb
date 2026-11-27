using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Codex.StudioFontController
{
    internal sealed class FontResources
    {
        internal readonly string Path;
        internal readonly long Length, WriteTicks;
        internal readonly Font Font;
        internal readonly Font PathFont;
        internal readonly TMP_FontAsset Tmp;
        internal readonly long BuildMilliseconds;
        internal readonly string Family;
        internal readonly string LegacyFamily;
        internal readonly float LegacyPreferredHeight;
        internal bool ImGuiValidated;
        internal bool Rejected;
        private bool _disposed;
        private int _disposeFailures;
        private readonly GlyphCoverage _coverage;
        internal FontResources(string path, FontSourceResolver resolver, string requestedFamily)
        {
            Path = path;
            var info = new FileInfo(path);
            Length = info.Length; WriteTicks = info.LastWriteTimeUtc.Ticks;
            var watch = Stopwatch.StartNew();
            try
            {
                PathFont = new Font(path);
                Tmp = TMP_FontAsset.CreateFontAsset(PathFont, 48, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (!Tmp) throw new InvalidOperationException("字体无法用于当前游戏的 TextMeshPro。");
                Family = Tmp.faceInfo.familyName;
                LegacyFamily = resolver.RegisteredFamily(path, Family, requestedFamily, Font.GetOSInstalledFontNames());
                Font = Font.CreateDynamicFontFromOSFont(LegacyFamily, 14);
                if (!Font || !Font.dynamic || Font.fontSize <= 0) throw new InvalidOperationException("系统字体未提供可用的界面字形。");
                _coverage = new GlyphCoverage(Font.HasCharacter);
                Font.RequestCharactersInTexture("x中文", 14, FontStyle.Normal);
                using (var generator = new TextGenerator())
                    LegacyPreferredHeight = generator.GetPreferredHeight("x", new TextGenerationSettings {
                        font = Font, fontSize = 14, fontStyle = FontStyle.Normal, color = Color.white,
                        lineSpacing = 1f, scaleFactor = 1f, generationExtents = new Vector2(500f, 500f),
                        horizontalOverflow = HorizontalWrapMode.Overflow, verticalOverflow = VerticalWrapMode.Overflow,
                        textAnchor = TextAnchor.UpperLeft, richText = false, updateBounds = true
                    });
                if (LegacyPreferredHeight <= 1f || float.IsNaN(LegacyPreferredHeight))
                    throw new InvalidOperationException("系统字体行高无效，已保留原字体。");
                Tmp.name = "Studio selected " + Family;
                Tmp.fallbackFontAssetTable = new List<TMP_FontAsset>();
                string missing;
                Tmp.TryAddCharacters("工作室字体设置保存载入确定取消中文0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz", out missing);
                BuildMilliseconds = watch.ElapsedMilliseconds;
            }
            catch { Dispose(); throw; }
        }
        internal bool Matches(string path)
        {
            if (Rejected || _disposed) return false;
            var info = new FileInfo(path);
            return string.Equals(Path, path, StringComparison.OrdinalIgnoreCase) && Length == info.Length && WriteTicks == info.LastWriteTimeUtc.Ticks;
        }
        internal bool Contains(string text)
        {
            return _coverage.Contains(text);
        }
        internal void KeepFallback(TMP_FontAsset font)
        {
            if (!font || font == Tmp || font.name.StartsWith("Studio selected ", StringComparison.Ordinal) || Tmp.fallbackFontAssetTable.Contains(font)) return;
            Tmp.fallbackFontAssetTable.Add(font);
        }
        internal int Dispose()
        {
            if (_disposed) return _disposeFailures;
            _disposed = true;
            int failures = 0;
            try
            {
                if (Tmp)
                {
                    foreach (Texture2D atlas in Tmp.atlasTextures)
                        try { if (atlas) UnityEngine.Object.Destroy(atlas); } catch (Exception) { failures++; }
                    try { if (Tmp.material) UnityEngine.Object.Destroy(Tmp.material); } catch (Exception) { failures++; }
                }
            }
            catch (Exception) { failures++; }
            try { if (Tmp) UnityEngine.Object.Destroy(Tmp); } catch (Exception) { failures++; }
            try { if (Font) UnityEngine.Object.Destroy(Font); } catch (Exception) { failures++; }
            try { if (PathFont) UnityEngine.Object.Destroy(PathFont); } catch (Exception) { failures++; }
            _disposeFailures = failures;
            return failures;
        }
    }
}
