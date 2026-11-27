using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Explicit read-only inspection: GAME_ROOT CANDIDATE_DLL OUTPUT_JSON");
string game = Path.GetFullPath(args[0]), adapterPath = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
AssemblyLoadContext.Default.Resolving += (context, name) => {
    foreach (string folder in new[] { "BepInEx/core", "CharaStudio_Data/Managed", "BepInEx/plugins" }) {
        string path = Path.Combine(game, folder, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
var adapter = AssemblyLoadContext.Default.LoadFromAssemblyPath(adapterPath);
var pluginType = adapter.GetType("Codex.StudioFontController.Plugin", true);
var resolverType = adapter.GetType("Codex.StudioFontController.FontSourceResolver", true);
var selection = adapter.GetType("Codex.StudioFontController.FontSelection", true);
object instance = RuntimeHelpers.GetUninitializedObject(pluginType);
object resolver = RuntimeHelpers.GetUninitializedObject(resolverType);
resolverType.GetField("_paths", all).SetValue(resolver, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
resolverType.GetField("_customRoot", all).SetValue(resolver, "synthetic-font-root");
pluginType.GetField("_resolver", all).SetValue(instance, resolver);
foreach (string field in new[] { "_texts", "_tmp", "_styles", "_textRefs", "_tmpRefs", "_styleRefs", "_cache" })
    pluginType.GetField(field, all).SetValue(instance, Activator.CreateInstance(pluginType.GetField(field, all).FieldType));
var bep = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "BepInEx/core/BepInEx.dll"));
var cfgType = bep.GetType("BepInEx.Configuration.ConfigFile", true);
var descriptionType = bep.GetType("BepInEx.Configuration.ConfigDescription", true);
string syntheticPath = Path.Combine(Path.GetTempPath(), "font-fault-fixture-" + Guid.NewGuid().ToString("N") + ".cfg");
object cfg = Activator.CreateInstance(cfgType, new object[] { syntheticPath, false, null });
cfgType.GetProperty("SaveOnConfigSet").SetValue(cfg, false);
var bind = cfgType.GetMethods().Single(method => method.Name == "Bind" && method.IsGenericMethodDefinition &&
    method.GetParameters().Length == 4 && method.GetParameters()[3].ParameterType == descriptionType);
object Entry<T>(string key, T value) => bind.MakeGenericMethod(typeof(T)).Invoke(cfg, new object[] { "Synthetic", key, value, null });
object enabled = Entry("enabled", true), bold = Entry("bold", true), delta = Entry("delta", 1), family = Entry("family", "missing-font"), file = Entry("file", "");
foreach (var item in new[] { ("_enabled", enabled), ("_bold", bold), ("_sizeDelta", delta), ("_family", family), ("_file", file) })
    pluginType.GetField(item.Item1, all).SetValue(instance, item.Item2);
object Value(object entry) => entry.GetType().GetProperty("Value").GetValue(entry);
void Set(object entry, object value) => entry.GetType().GetProperty("Value").SetValue(entry, value);
var logger = Activator.CreateInstance(bep.GetType("BepInEx.Logging.ManualLogSource", true), new object[] { "synthetic-font-fault" });
pluginType.BaseType.GetField("<Logger>k__BackingField", all).SetValue(instance, logger);
var lastSettings = pluginType.GetField("_lastSuccessfulSettings", all);
if (lastSettings != null) {
    var snapshotType = lastSettings.FieldType;
    lastSettings.SetValue(instance, Activator.CreateInstance(snapshotType, all, null, new object[] { false, "known-family", "", false, 0 }, null));
}
var events = new List<Delegate>();
foreach (object entry in new[] { enabled, bold, delta, family, file }) {
    var info = entry.GetType().GetEvent("SettingChanged");
    Delegate callback = Delegate.CreateDelegate(info.EventHandlerType, instance, pluginType.GetMethod("Changed", all));
    info.AddEventHandler(entry, callback); events.Add(callback);
}
selection.GetProperty("Bold").GetSetMethod(true).Invoke(null, new object[] { false });
selection.GetProperty("SizeDelta").GetSetMethod(true).Invoke(null, new object[] { 0 });
pluginType.GetField("_changeAt", all).SetValue(instance, -1f);
var checks = new List<object>();
var failures = new List<string>();
void Check(string name, bool passed) { checks.Add(new { name, passed }); if (!passed) failures.Add(name); }
pluginType.GetMethod("Switch", all).Invoke(instance, null);
Check("failed request restores last successful configuration", (bool)Value(enabled) == false && (string)Value(family) == "known-family" &&
    (bool)Value(bold) == false && (int)Value(delta) == 0);
Check("rollback does not schedule another switch", (float)pluginType.GetField("_changeAt", all).GetValue(instance) == -1f);
Check("failure status remains visible after config events", ((string)pluginType.GetField("_status", all).GetValue(instance)).StartsWith("未应用，已恢复上次设置"));
Check("effective typography unchanged after failure", !(bool)selection.GetProperty("Bold").GetValue(null) && (int)selection.GetProperty("SizeDelta").GetValue(null) == 0);

// A synthetic Text with a null serialized font field makes its actual managed getter throw.
var ui = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "CharaStudio_Data/Managed/UnityEngine.UI.dll"));
Type textType = ui.GetType("UnityEngine.UI.Text", true), unityObject = textType;
while (unityObject.FullName != "UnityEngine.Object") unityObject = unityObject.BaseType;
var invalidTexts = new List<object>();
var table = pluginType.GetField("_texts", all).GetValue(instance);
var textOriginal = pluginType.GetNestedType("TextOriginal", BindingFlags.NonPublic);
for (int index = 0; index < 2; index++) {
    object text = RuntimeHelpers.GetUninitializedObject(textType);
    unityObject.GetField("m_CachedPtr", all).SetValue(text, new IntPtr(index + 1));
    invalidTexts.Add(text);
    table.GetType().GetMethod("Add").Invoke(table, new[] { text, Activator.CreateInstance(textOriginal, true) });
    ((List<WeakReference>)pluginType.GetField("_textRefs", all).GetValue(instance)).Add(new WeakReference(text));
}
int restoreFailures = -1; string escaped = null;
try {
    MethodInfo restore = pluginType.GetMethod("Restore", all);
    object result = restore.Invoke(instance, restore.GetParameters().Length == 0 ? null : new object[] { null });
    if (result is int count) restoreFailures = count;
} catch (TargetInvocationException error) { escaped = error.InnerException.GetType().Name; }
Check("restore attempts both faulty controls and counts failures", escaped == null && restoreFailures == 2);
// Set without events; this probe must never enter native Time or Resources APIs.
foreach (var pair in new[] { (enabled, events[0]), (bold, events[1]), (delta, events[2]), (family, events[3]), (file, events[4]) })
    pair.Item1.GetType().GetEvent("SettingChanged").RemoveEventHandler(pair.Item1, pair.Item2);
Set(enabled, false); Set(bold, false); Set(delta, 0);
try { pluginType.GetMethod("Switch", all).Invoke(instance, null); escaped = null; }
catch (TargetInvocationException error) { escaped = error.InnerException.GetType().Name; }
Check("disabled restore failure stays inside transaction", escaped == null && ((string)pluginType.GetField("_status", all).GetValue(instance)).StartsWith("未应用"));
foreach (object text in invalidTexts) unityObject.GetField("m_CachedPtr", all).SetValue(text, IntPtr.Zero);
((List<WeakReference>)pluginType.GetField("_textRefs", all).GetValue(instance)).Clear();

MethodInfo reject = pluginType.GetMethod("RejectCandidate", all);
if (reject != null) {
    Type resourceType = adapter.GetType("Codex.StudioFontController.FontResources", true);
    object resource = RuntimeHelpers.GetUninitializedObject(resourceType);
    var cache = (IList)pluginType.GetField("_cache", all).GetValue(instance); cache.Add(resource);
    int result = (int)reject.Invoke(instance, new[] { resource });
    Check("failed unused candidate released and removed", result == 0 && cache.Count == 0 && (bool)resourceType.GetField("Rejected", all).GetValue(resource));
    Check("rejected candidate cannot match another selection", !(bool)resourceType.GetMethod("Matches", all).Invoke(resource, new object[] { "synthetic.ttf" }));
    Check("resource release remains idempotent", (int)resourceType.GetMethod("Dispose", all).Invoke(resource, null) == 0);
} else Check("failed unused candidate released and removed", false);

var rememberFont = pluginType.GetMethod("RememberExternalTmpFont", all);
if (rememberFont != null) {
    Type tmpOriginal = pluginType.GetNestedType("TmpOriginal", BindingFlags.NonPublic);
    object original = Activator.CreateInstance(tmpOriginal, true);
    Type fontType = tmpOriginal.GetField("Font", all).FieldType, materialType = tmpOriginal.GetField("Material", all).FieldType;
    object font = RuntimeHelpers.GetUninitializedObject(fontType), material = RuntimeHelpers.GetUninitializedObject(materialType);
    unityObject.GetField("m_CachedPtr", all).SetValue(font, new IntPtr(3));
    unityObject.GetField("m_CachedPtr", all).SetValue(material, new IntPtr(4));
    tmpOriginal.GetField("Font", all).SetValue(original, font); tmpOriginal.GetField("Material", all).SetValue(original, material);
    rememberFont.Invoke(null, new[] { original, font });
    Check("same original TMP font keeps custom material", ReferenceEquals(tmpOriginal.GetField("Material", all).GetValue(original), material));
    unityObject.GetField("m_CachedPtr", all).SetValue(font, IntPtr.Zero); unityObject.GetField("m_CachedPtr", all).SetValue(material, IntPtr.Zero);
} else Check("same original TMP font keeps custom material", false);
Check("synthetic configuration never written", !File.Exists(syntheticPath));
string json = JsonSerializer.Serialize(new {
    passed = failures.Count == 0, checks = checks.Count, failures, results = checks,
    adapterSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(adapterPath))).ToLowerInvariant(),
    actualSwitchInvoked = true, actualRestoreInvoked = true, unityNativeInvoked = false, gameWrites = false,
    limitation = "Calls actual compiled controller methods with synthetic BepInEx config and managed Unity object sentinels. Verifies failure boundaries; no real font loading, pixels or GPU resource release is exercised."
}, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(output, json); Console.WriteLine(json);
return failures.Count == 0 ? 0 : 1;
