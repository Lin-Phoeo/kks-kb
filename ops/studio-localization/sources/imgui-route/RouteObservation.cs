using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

internal sealed class RouteObservation
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Dictionary<string, string> Expected = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "Confirm Delete", "删除前确认" },
        { "Default Sort Order", "默认排序方式" },
        { "Date Descending", "修改时间：最新优先" }
    };
    private static RouteObservation _active;
    private readonly Dictionary<string, ObservationRow> _rows = new Dictionary<string, ObservationRow>(StringComparer.Ordinal);
    private readonly Harmony _harmony;
    private readonly ManualLogSource _logger;
    private readonly Func<string> _lastCallback;
    private readonly string _output;
    private readonly MethodInfo _hook;
    private readonly FieldInfo _callCache;
    private readonly FieldInfo _current;
    private readonly FieldInfo _pluginCaches;
    private readonly FieldInfo _globalCache;
    private readonly FieldInfo _enabled;
    private readonly FieldInfo _disabled;
    private bool _written;
    private bool _writeAttempted;
    private bool _warned;
    private bool _stopped;

    internal sealed class ObservationState
    {
        internal string key;
        internal string cacheOwner;
        internal bool configurationCache;
        internal string[] configurationFrames;
        internal string lastUnwrappedCallback;
        internal bool textHooksEnabled;
        internal bool temporarilyDisabled;
    }

    private sealed class ObservationRow
    {
        public string key;
        public string expected;
        public string renderedContent;
        public bool matched;
        public string actualCacheOwner;
        public bool configurationCache;
        public string[] configurationFrames;
        public string lastUnwrappedCallback;
        public bool textHooksEnabled;
        public bool temporarilyDisabled;
        public int observationCount;
    }

    private RouteObservation(string output, Assembly core, Harmony harmony, ManualLogSource logger, Func<string> lastCallback)
    {
        _output = output;
        _harmony = harmony;
        _logger = logger;
        _lastCallback = lastCallback;
        Type plugin = core.GetType("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin", true);
        _hook = plugin.GetMethod("Hook_TextChanged", All, null, new Type[] { typeof(object), typeof(bool) }, null);
        _current = plugin.GetField("Current", All);
        _pluginCaches = plugin.GetField("PluginTextCaches", All);
        _globalCache = plugin.GetField("TextCache", All);
        _enabled = plugin.GetField("_textHooksEnabled", All);
        _disabled = plugin.GetField("_temporarilyDisabled", All);
        _callCache = core.GetType("XUnity.AutoTranslator.Plugin.Core.CallOrigin", true).GetField("TextCache", All);
        if (_hook == null || _current == null || _pluginCaches == null || _globalCache == null ||
            _enabled == null || _disabled == null || _callCache == null)
            throw new InvalidOperationException("Observer reflection signature mismatch");
    }

    internal static RouteObservation Start(Assembly core, Harmony harmony, ManualLogSource logger, Func<string> lastCallback)
    {
        string requested = Environment.GetEnvironmentVariable("KKS_IMGUI_ROUTE_VERIFY_OUTPUT");
        if (string.IsNullOrWhiteSpace(requested)) return null;
        if (_active != null) throw new InvalidOperationException("Observer already active");
        string allowed = Path.GetFullPath(Path.Combine(Paths.GameRootPath,
            ".scratch", "studio-translation-20261126", "engine-scope-fix-20261127")) + Path.DirectorySeparatorChar;
        string output = Path.GetFullPath(requested);
        if (!Path.IsPathRooted(requested) || !output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase) || File.Exists(output))
            throw new InvalidOperationException("Observer output must be a new task-local JSON path");
        RouteObservation observer = new RouteObservation(output, core, harmony, logger, lastCallback);
        _active = observer;
        try
        {
            harmony.Patch(observer._hook,
                prefix: new HarmonyMethod(typeof(RouteObservation).GetMethod("BeforeText", All)),
                postfix: new HarmonyMethod(typeof(RouteObservation).GetMethod("AfterText", All)));
        }
        catch
        {
            observer.Stop();
            throw;
        }
        logger.LogInfo("STUDIO_IMGUI_ROUTE_OBSERVER_READY keys=3");
        return observer;
    }

    private static void BeforeText(object __0, out ObservationState __state)
    {
        __state = null;
        RouteObservation observer = _active;
        GUIContent content = __0 as GUIContent;
        if (observer == null || observer._written || content == null || !Expected.ContainsKey(content.text ?? string.Empty)) return;
        try { __state = observer.Capture(content.text); }
        catch (Exception error) { observer.Warn(error); }
    }

    private ObservationState Capture(string key)
    {
        object plugin = _current.GetValue(null);
        IDictionary caches = (IDictionary)_pluginCaches.GetValue(plugin);
        object activeCache = _callCache.GetValue(null);
        object firstCache = activeCache;
        if (activeCache != null)
        {
            FieldInfo first = activeCache.GetType().GetField("_first", All);
            if (first != null) firstCache = first.GetValue(activeCache);
        }
        object expectedCache = caches["ConfigurationManager"];
        bool configCache = expectedCache != null && ReferenceEquals(firstCache, expectedCache);
        string owner = activeCache == null ? "global-fallback" : "unidentified";
        if (ReferenceEquals(firstCache, _globalCache.GetValue(plugin))) owner = "global";
        foreach (DictionaryEntry entry in caches)
            if (ReferenceEquals(firstCache, entry.Value)) { owner = (string)entry.Key; break; }
        var frames = new List<string>();
        foreach (StackFrame frame in new StackTrace(false).GetFrames() ?? new StackFrame[0])
        {
            MethodBase method = frame.GetMethod();
            if (method != null && method.DeclaringType != null &&
                method.DeclaringType.Assembly.GetName().Name == "ConfigurationManager")
                frames.Add(method.DeclaringType.FullName + "." + method.Name);
        }
        return new ObservationState
        {
            key = key, cacheOwner = owner, configurationCache = configCache,
            configurationFrames = frames.ToArray(), lastUnwrappedCallback = _lastCallback(),
            textHooksEnabled = (bool)_enabled.GetValue(plugin), temporarilyDisabled = (bool)_disabled.GetValue(plugin)
        };
    }

    private static void AfterText(object __0, ObservationState __state)
    {
        RouteObservation observer = _active;
        if (observer == null || observer._written || __state == null) return;
        try
        {
            GUIContent content = __0 as GUIContent;
            if (content == null) return;
            observer.Record(__state, content.text);
        }
        catch (Exception error) { observer.Warn(error); }
    }

    private void Record(ObservationState state, string text)
    {
        ObservationRow previous;
        _rows.TryGetValue(state.key, out previous);
        bool matched = text == Expected[state.key] && state.configurationCache && state.configurationFrames.Length > 0;
        // Once observed correctly, another incidental caller cannot replace the evidence.
        if (previous != null && previous.matched && !matched) return;
        _rows[state.key] = new ObservationRow
        {
            key = state.key, expected = Expected[state.key], renderedContent = text, matched = matched,
            actualCacheOwner = state.cacheOwner, configurationCache = state.configurationCache,
            configurationFrames = state.configurationFrames, lastUnwrappedCallback = state.lastUnwrappedCallback,
            textHooksEnabled = state.textHooksEnabled, temporarilyDisabled = state.temporarilyDisabled,
            observationCount = previous == null ? 1 : previous.observationCount + 1
        };
        if (previous == null)
            _logger.LogInfo("STUDIO_IMGUI_ROUTE_FIRST key=" + state.key + " owner=" + state.cacheOwner +
                " matched=" + matched + " configurationFrames=" + state.configurationFrames.Length);
        if (_rows.Count != Expected.Count) return;
        foreach (ObservationRow row in _rows.Values) if (!row.matched) return;
        WriteReport(true);
    }

    private void WriteReport(bool allMatched)
    {
        if (_written || _writeAttempted) return;
        _writeAttempted = true;
        Directory.CreateDirectory(Path.GetDirectoryName(_output));
        string temporary = _output + ".tmp-" + Guid.NewGuid().ToString("N");
        var report = new
        {
            schema_version = 1, completed_utc = DateTime.UtcNow.ToString("o"),
            allMatched = allMatched, expectedCount = Expected.Count, observedCount = _rows.Count,
            rows = new List<ObservationRow>(_rows.Values),
            scope = "Read-only observations of three whitelisted GUIContent strings passed through the real XUnity Hook_TextChanged call. Cache identity is reference-compared and ConfigurationManager stack frames are recorded.",
            limitation = "Confirms GUIContent after translation on the real draw path, not pixels, clipping, font layout, or user interaction. lastUnwrappedCallback is context only."
        };
        using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (StreamWriter writer = new StreamWriter(stream))
            writer.Write(JsonConvert.SerializeObject(report, Formatting.Indented));
        File.Move(temporary, _output);
        _written = true;
        _logger.LogInfo("STUDIO_IMGUI_ROUTE_OBSERVED matched=" + allMatched + " observed=" + _rows.Count + "/3");
    }

    private void Warn(Exception error)
    {
        if (_warned) return;
        _warned = true;
        _logger.LogWarning("STUDIO_IMGUI_ROUTE_OBSERVER_ERROR " + error.GetType().Name);
    }

    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        if (ReferenceEquals(_active, this)) _active = null;
        try
        {
            _harmony.Unpatch(_hook, typeof(RouteObservation).GetMethod("BeforeText", All));
            _harmony.Unpatch(_hook, typeof(RouteObservation).GetMethod("AfterText", All));
        }
        catch (Exception error) { Warn(error); }
        try { WriteReport(false); }
        catch (Exception error) { Warn(error); }
    }
}
