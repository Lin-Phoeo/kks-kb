using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using ConfigurationManager;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codex.StudioConfigLocalization
{
    [BepInPlugin(Guid, "Studio Configuration Chinese Display", "1.0.0")]
    [BepInProcess("CharaStudio")]
    [BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string Guid = "codex.studio.configuration.chinese.display";
        private const string ManagerHash = "91b1c972d397df93681c197f77badcbfcd39f8957e200eaf7ee989cbbf74636c";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static TranslationCatalog _catalog;
        private static PropertyInfo _entry;
        private static FieldInfo _info;
        private static Plugin _instance;
        [ThreadStatic] private static string _currentPlugin;
        [ThreadStatic] private static SettingIdentity _currentSetting;
        private readonly List<MethodBase> _patched = new List<MethodBase>();
        private readonly HashSet<string> _observed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _sourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        private Harmony _harmony;
        private float _auditAt;
        private bool _audited;
        private bool _managerAudited;

        private sealed class SettingIdentity
        {
            internal string Guid, Section, Key;
        }

        private void Awake()
        {
            try
            {
                Assembly manager = typeof(SettingEntryBase).Assembly;
                using (SHA256 hash = SHA256.Create())
                using (FileStream stream = File.OpenRead(manager.Location))
                    if (BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != ManagerHash)
                        throw new InvalidOperationException("ConfigurationManager build differs");
                string data = Path.Combine(Path.GetDirectoryName(Info.Location), "Codex.StudioConfigLocalization.json");
                _catalog = new TranslationCatalog(JsonConvert.DeserializeObject<TranslationEntry[]>(File.ReadAllText(data)));
                Type managerType = manager.GetType("ConfigurationManager.ConfigurationManager", true);
                Type drawer = manager.GetType("ConfigurationManager.SettingFieldDrawer", true);
                Type configSetting = manager.GetType("ConfigurationManager.ConfigSettingEntry", true);
                Type pluginSettings = managerType.GetNestedType("PluginSettingsData", BindingFlags.NonPublic);
                _entry = configSetting.GetProperty("Entry", All);
                _info = pluginSettings.GetField("Info", All);
                if (_entry == null || _info == null) throw new InvalidOperationException("ConfigurationManager identity fields differ");
                _instance = this;
                _harmony = new Harmony(Guid);
                Patch(managerType, "DrawSinglePlugin", "PluginPrefix", null, "PluginFinalizer");
                Patch(managerType, "BuildSettingList", null, "ManagerListPostfix", null);
                Patch(managerType, "DrawSingleSetting", "SettingPrefix", null, "SettingFinalizer");
                Patch(managerType, "DrawSettingName", "NamePrefix", null, "NameFinalizer");
                Patch(drawer, "DrawCategoryHeader", "CategoryPrefix", null, null);
                Patch(drawer, "DrawPluginHeader", "HeaderPrefix", null, null);
                Patch(drawer, "ObjectToGuiContent", null, "ChoicePostfix", null);
                MethodInfo flagsMethod = drawer.GetMethod("DrawFlagsField", All);
                if (flagsMethod == null) throw new MissingMethodException("DrawFlagsField");
                _harmony.Patch(flagsMethod, transpiler: new HarmonyMethod(typeof(Plugin).GetMethod("FlagsTranspiler", All)));
                _patched.Add(flagsMethod);
                _auditAt = Time.unscaledTime + 5f;
                Logger.LogInfo("STUDIO_CONFIG_LOCALIZATION_READY entries=" + _catalog.Count + " identity=plugin-guid+section+key");
            }
            catch (Exception error)
            {
                Cleanup();
                Logger.LogError("STUDIO_CONFIG_LOCALIZATION_REFUSED " + error.GetType().Name);
            }
        }

        private void Patch(Type type, string name, string prefix, string postfix, string finalizer)
        {
            MethodInfo target = type.GetMethod(name, All);
            if (target == null) throw new MissingMethodException(type.FullName, name);
            _harmony.Patch(target, prefix: Hook(prefix), postfix: Hook(postfix), finalizer: Hook(finalizer));
            _patched.Add(target);
        }
        private static HarmonyMethod Hook(string name)
        {
            return name == null ? null : new HarmonyMethod(typeof(Plugin).GetMethod(name, All));
        }

        private static void ManagerListPostfix()
        {
            if (_instance == null || _instance._managerAudited) return;
            _instance._managerAudited = true;
            _instance.WriteAudit(false, true);
        }

        private static void PluginPrefix(object __0, out string __state)
        {
            __state = _currentPlugin;
            BepInPlugin info = (BepInPlugin)_info.GetValue(__0);
            _currentPlugin = info.GUID;
        }
        private static void PluginFinalizer(string __state) { _currentPlugin = __state; }

        private static void SettingPrefix(SettingEntryBase __0, out SettingIdentity __state)
        {
            __state = _currentSetting;
            _currentSetting = null;
            if (!_entry.DeclaringType.IsInstanceOfType(__0)) return;
            ConfigEntryBase entry = (ConfigEntryBase)_entry.GetValue(__0, null);
            if (entry == null || __0.PluginInfo == null) return;
            _currentSetting = new SettingIdentity { Guid = __0.PluginInfo.GUID, Section = entry.Definition.Section, Key = entry.Definition.Key };
        }
        private static void SettingFinalizer(SettingIdentity __state) { _currentSetting = __state; }

        private static void NamePrefix(SettingEntryBase __0, out DisplayState __state)
        {
            __state = null;
            SettingIdentity identity = _currentSetting;
            if (identity == null) return;
            string name, description;
            bool translatedName = _catalog.TryGet(identity.Guid, "setting", identity.Section, identity.Key, __0.DispName, out name);
            bool translatedDescription = _catalog.TryGet(identity.Guid, "description", identity.Section, identity.Key, __0.Description, out description);
            if (!translatedName && !translatedDescription) return;
            string originalName = __0.DispName, originalDescription = __0.Description;
            __state = new DisplayState(__0, name, description);
            if (translatedName) Observe(identity.Guid, identity.Section, identity.Key, "setting", originalName);
            if (translatedDescription) Observe(identity.Guid, identity.Section, identity.Key, "description", originalDescription);
        }
        private static void NameFinalizer(DisplayState __state) { if (__state != null) __state.Restore(); }

        private static void CategoryPrefix(ref string __0)
        {
            string value;
            if (_catalog.TryGet(_currentPlugin, "category", null, null, __0, out value))
            {
                string source = __0;
                __0 = value;
                Observe(_currentPlugin, "", "", "category", source);
            }
        }
        private static void HeaderPrefix(ref GUIContent __0)
        {
            if (__0 == null) return;
            string value;
            if (_catalog.TryGet(_currentPlugin, "header", null, null, __0.text, out value))
            {
                string source = __0.text;
                __0 = new GUIContent(__0) { text = value };
                Observe(_currentPlugin, "", "", "header", source);
            }
        }
        private static void ChoicePostfix(ref GUIContent __result)
        {
            SettingIdentity identity = _currentSetting;
            if (identity == null || __result == null) return;
            string value;
            if (_catalog.TryGet(identity.Guid, "enum", identity.Section, identity.Key, __result.text, out value))
            {
                string source = __result.text;
                __result = new GUIContent(__result) { text = value };
                Observe(identity.Guid, identity.Section, identity.Key, "enum", source);
            }
        }

        private static IEnumerable<CodeInstruction> FlagsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            int replacements = 0;
            MethodInfo translate = typeof(Plugin).GetMethod("FlagsText", All);
            foreach (CodeInstruction instruction in instructions)
            {
                result.Add(instruction);
                MethodInfo method = instruction.operand as MethodInfo;
                if (instruction.opcode == OpCodes.Callvirt && method != null && method.Name == "get_name" &&
                    method.ReturnType == typeof(string) && method.DeclaringType.IsGenericType &&
                    method.DeclaringType.GetGenericTypeDefinition().Name == "<>f__AnonymousType1`2")
                {
                    result.Add(new CodeInstruction(OpCodes.Call, translate));
                    replacements++;
                }
            }
            if (replacements != 2) throw new InvalidOperationException("Flags display shape differs");
            return result;
        }

        private static string FlagsText(string source)
        {
            SettingIdentity identity = _currentSetting;
            string translated;
            if (identity == null || !_catalog.TryGet(identity.Guid, "enum", identity.Section, identity.Key, source, out translated)) return source;
            Observe(identity.Guid, identity.Section, identity.Key, "enum", source);
            return translated;
        }

        private static void Observe(string guid, string section, string key, string kind, string source)
        {
            if (_instance == null) return;
            string sourceHash;
            if (!_instance._sourceHashes.TryGetValue(source, out sourceHash))
            {
                using (SHA256 algorithm = SHA256.Create())
                    sourceHash = BitConverter.ToString(algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source))).Replace("-", "").ToLowerInvariant();
                _instance._sourceHashes.Add(source, sourceHash);
            }
            _instance._observed.Add(guid + "\t" + section + "\t" + key + "\t" + kind + "\t" + sourceHash);
        }

        private void Update()
        {
            if (_audited || _catalog == null || Time.unscaledTime < _auditAt || SceneManager.GetActiveScene().name != "Studio") return;
            _audited = true;
            WriteAudit(false);
        }

        private void WriteAudit(bool final, bool managerReady = false)
        {
            string requested = Environment.GetEnvironmentVariable("KKS_CONFIG_LOCALIZATION_VERIFY_OUTPUT");
            if (string.IsNullOrEmpty(requested)) return;
            try
            {
                string allowed = Path.GetFullPath(Path.Combine(Paths.GameRootPath, ".scratch", "studio-translation-20261126", "full-pass-20261127")) + Path.DirectorySeparatorChar;
                string output = Path.GetFullPath(requested);
                if (!Path.IsPathRooted(requested) || !output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(output) != ".json")
                    throw new InvalidOperationException("Metadata audit path differs");
                if (final) output = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "-drawn.json");
                else if (managerReady) output = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "-manager-ready.json");
                var definitions = new List<object>();
                foreach (var pair in Chainloader.PluginInfos)
                {
                    if (!pair.Value.Instance) continue;
                    foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> setting in pair.Value.Instance.Config)
                    {
                        string translated;
                        bool label = _catalog.TryGet(pair.Key, "setting", setting.Key.Section, setting.Key.Key, setting.Key.Key, out translated);
                        bool description = _catalog.TryGet(pair.Key, "description", setting.Key.Section, setting.Key.Key,
                            setting.Value.Description.Description, out translated);
                        definitions.Add(new { pluginGuid = pair.Key, section = setting.Key.Section, settingKey = setting.Key.Key,
                            settingType = setting.Value.SettingType.FullName, labelTranslation = label, descriptionTranslation = description });
                    }
                }
                var managerDefinitions = new List<object>();
                BepInEx.PluginInfo managerInfo;
                bool managerListBuilt = false;
                if (Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.configurationmanager", out managerInfo) && managerInfo.Instance)
                {
                    var collected = managerInfo.Instance.GetType().GetField("_allSettings", All).GetValue(managerInfo.Instance) as IEnumerable<SettingEntryBase>;
                    if (collected != null)
                    {
                        managerListBuilt = true;
                        foreach (SettingEntryBase setting in collected)
                        {
                            if (!_entry.DeclaringType.IsInstanceOfType(setting) || setting.PluginInfo == null) continue;
                            ConfigEntryBase entry = (ConfigEntryBase)_entry.GetValue(setting, null);
                            string translated;
                            bool label = _catalog.TryGet(setting.PluginInfo.GUID, "setting", entry.Definition.Section, entry.Definition.Key, setting.DispName, out translated);
                            bool description = _catalog.TryGet(setting.PluginInfo.GUID, "description", entry.Definition.Section, entry.Definition.Key, setting.Description, out translated);
                            managerDefinitions.Add(new { pluginGuid = setting.PluginInfo.GUID, section = entry.Definition.Section, settingKey = entry.Definition.Key,
                                displayNameMatchesKey = setting.DispName == entry.Definition.Key, labelTranslation = label, descriptionTranslation = description });
                        }
                    }
                }
                var report = new { schema_version = 1, phase = final ? "drawn" : managerReady ? "manager-ready" : "startup", candidateEntries = _catalog.Count,
                    loadedDefinitions = definitions, managerListBuilt, managerDefinitions, drawnIdentities = _observed.OrderBy(item => item).ToArray(),
                    valuesRead = false, configMutated = false,
                    limitation = "Loaded config definitions plus observed display-entry calls. Does not prove every registered setting is visible or visually fits." };
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonConvert.SerializeObject(report, Formatting.Indented));
                Logger.LogInfo("STUDIO_CONFIG_LOCALIZATION_AUDIT definitions=" + definitions.Count + " observed=" + _observed.Count + " final=" + final);
            }
            catch (Exception error) { Logger.LogWarning("STUDIO_CONFIG_LOCALIZATION_AUDIT_REFUSED " + error.GetType().Name); }
        }

        private void OnDestroy() { if (_catalog != null) WriteAudit(true); Cleanup(); }
        private void Cleanup()
        {
            if (_harmony != null)
                foreach (MethodBase method in _patched) _harmony.Unpatch(method, HarmonyPatchType.All, Guid);
            _patched.Clear();
            _currentPlugin = null;
            _currentSetting = null;
            _catalog = null;
            _instance = null;
            _harmony = null;
        }
    }
}
