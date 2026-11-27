using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;

namespace Codex.StudioFontController
{
    internal static class ImGuiMetricObservation
    {
        [ThreadStatic] internal static bool Measuring;
        private static bool _written;
        private static readonly MethodInfo Constrained = typeof(GUIStyle).GetMethod("CalcSizeWithConstraints",
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(GUIContent), typeof(Vector2) }, null);

        internal static void Capture(string outputBase, string sourcePath)
        {
            if (_written || outputBase == null || !GUI.skin) return;
            _written = true;
            Measuring = true;
            var created = new List<Font>();
            try
            {
                GUIStyle template = GUI.skin.textField;
                var rows = new List<object>();
                string systemFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                var paths = new List<string> { sourcePath, Path.Combine(systemFonts, "msyh.ttc"), Path.Combine(systemFonts, "simsun.ttc"), Path.Combine(systemFonts, "simkai.ttf") };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                rows.Add(Measure("original-style", new GUIStyle(template)));
                if (FontSelection.UguiFont)
                    rows.Add(Measure("selected-os-font/" + FontSelection.Family, new GUIStyle(template) { font = FontSelection.UguiFont, fontSize = 14 }));
                foreach (string path in paths)
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path) || !seen.Add(path)) continue;
                    var font = new Font(path);
                    created.Add(font);
                    var zero = new GUIStyle(template) { font = font, fontSize = 0 };
                    var explicitSize = new GUIStyle(template) { font = font, fontSize = 14 };
                    rows.Add(Measure(Path.GetFileName(path) + "/implicit-size", zero));
                    rows.Add(Measure(Path.GetFileName(path) + "/size-14", explicitSize));
                }
                foreach (string family in new[] { "Microsoft YaHei", "SimSun", "KaiTi" })
                {
                    Font font = Font.CreateDynamicFontFromOSFont(family, 14);
                    created.Add(font);
                    rows.Add(Measure("os-family/" + family, new GUIStyle(template) { font = font, fontSize = 14 }));
                }
                string output = Path.Combine(Path.GetDirectoryName(outputBase), Path.GetFileNameWithoutExtension(outputBase) + "-imgui-metrics.json");
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonConvert.SerializeObject(new {
                    schema_version = 1, rows, onGuiEvent = Event.current.type.ToString(),
                    uiStyleAssignments = 0, inputTextRead = false,
                    scope = "Actual native metrics of cloned GUI.skin.textField with empty text and fixed x. Original UI style is not mutated."
                }, Formatting.Indented));
            }
            finally
            {
                foreach (Font font in created) if (font) UnityEngine.Object.Destroy(font);
                Measuring = false;
            }
        }

        private static object Measure(string source, GUIStyle style)
        {
            var empty = new GUIContent("");
            var letter = new GUIContent("x");
            var chinese = new GUIContent("工作室");
            Vector2 emptySize = (Vector2)Constrained.Invoke(style, new object[] { empty, Vector2.zero });
            Vector2 letterSize = (Vector2)Constrained.Invoke(style, new object[] { letter, Vector2.zero });
            Vector2 chineseSize = (Vector2)Constrained.Invoke(style, new object[] { chinese, Vector2.zero });
            return new { source, styleFontSize = style.fontSize, fontPointSize = style.font ? style.font.fontSize : 0,
                dynamicFont = style.font && style.font.dynamic,
                lineHeight = style.lineHeight, fixedHeight = style.fixedHeight,
                emptyHeight = emptySize.y, letterHeight = letterSize.y, chineseHeight = chineseSize.y,
                paddingTop = style.padding.top, paddingBottom = style.padding.bottom };
        }
    }
}
