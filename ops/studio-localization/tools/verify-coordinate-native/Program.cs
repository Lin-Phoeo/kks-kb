using System.Collections;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Codex.StudioCoordinateNative;
using Mono.Cecil;

if (args.Length != 3)
    throw new ArgumentException("Usage: GAME_ROOT INSTALLED_PLUGIN_DLL BUILT_ADAPTER_DLL");
string game = Path.GetFullPath(args[0]);
string installedPlugin = Path.GetFullPath(args[1]);
string adapterPath = Path.GetFullPath(args[2]);
string[] dependencies = {
    Path.Combine(game, "BepInEx/core"), Path.Combine(game, "CharaStudio_Data/Managed")
};
AssemblyLoadContext.Default.Resolving += (context, name) => {
    foreach (string directory in dependencies) {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
Run();

void Run()
{
    var checks = new List<object>();
    void Check(string name, bool passed) {
        checks.Add(new { name, passed });
        if (!passed) throw new InvalidOperationException(name);
    }
    string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    string originalHash = Hash(File.ReadAllBytes(installedPlugin));
    Check("installed plugin identity", originalHash == NativeCoordinateStrings.ExpectedAssemblySha256);
    using var metadata = AssemblyDefinition.ReadAssembly(installedPlugin);
    var manager = metadata.MainModule.GetType(NativeCoordinateStrings.TargetType);
    var method = manager.Methods.Single(item => item.Name == "GetString");
    Check("exact resource getter signature", method.IsStatic && method.ReturnType.FullName == "System.String" &&
        method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.String" }));
    var modeField = metadata.MainModule.GetType("CoordinateLoadOption.Patches").Fields.Single(item => item.Name == "addAccModeFlag");
    Check("actual mode field is static Boolean", modeField.IsStatic && modeField.FieldType.FullName == "System.Boolean");
    var resource = (EmbeddedResource)metadata.MainModule.Resources.Single(item => item.Name == NativeCoordinateStrings.ResourceName);
    Check("English resource identity", Hash(resource.GetResourceData()) == NativeCoordinateStrings.ExpectedEnglishResourceSha256);
    var originals = new Dictionary<string,string>(StringComparer.Ordinal);
    using (var stream = new MemoryStream(resource.GetResourceData()))
    using (var reader = new ResourceReader(stream))
        foreach (DictionaryEntry entry in reader) originals.Add((string)entry.Key, (string)entry.Value);
    Check("24 resource identities retained", originals.Count == 24 && originals.Keys.ToHashSet().SetEquals(NativeCoordinateStrings.Keys));
    foreach (string key in originals.Keys) {
        bool translated = NativeCoordinateStrings.TryGet(key, "", out string value);
        Check("native lookup " + key, translated && !string.IsNullOrEmpty(value));
    }
    Check("unknown resource delegates to plugin", !NativeCoordinateStrings.TryGet("Unlisted", "", out _));
    Check("explicit language delegates to plugin", !NativeCoordinateStrings.TryGet("showSelection", "ja", out _));
    Check("null key delegates to plugin", !NativeCoordinateStrings.TryGet(null, "", out _));
    Check("case sensitive identities preserved", !NativeCoordinateStrings.TryGet("SHOWSELECTION", "", out _));
    Check("ABMX proper name retained", NativeCoordinateStrings.TryGet("readABMX", "", out string abmx) && abmx == "ABMX");
    Check("mode labels remain distinct", NativeCoordinateStrings.TryGet("addMode", "", out string add) &&
        NativeCoordinateStrings.TryGet("replaceMode", "", out string replace) && add != replace);

    var callers = new List<object>();
    void Visit(TypeDefinition type) {
        foreach (MethodDefinition current in type.Methods) {
            if (!current.HasBody) continue;
            foreach (var instruction in current.Body.Instructions) {
                if (instruction.Operand is MethodReference target && target.FullName == method.FullName)
                    callers.Add(new { method = current.FullName, offset = instruction.Offset });
            }
        }
        foreach (var nested in type.NestedTypes) Visit(nested);
    }
    foreach (var type in metadata.MainModule.Types) Visit(type);
    Check("resource calls limited to known display modules", callers.Count > 0 &&
        callers.All(item => item.ToString().Contains("CoordinateLoadOption.Patches") || item.ToString().Contains("CoordinateLoadOption.CoordinateLoad")));
    var init = metadata.MainModule.GetType("CoordinateLoadOption.Patches").Methods.Single(item => item.Name == "InitPostfix");
    Check("English override present in actual UI builder", init.Body.Instructions.Any(instruction =>
        instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && (string)instruction.Operand == "en-US"));
    Check("toggle identity remains enum based", init.Body.Instructions.Any(instruction =>
        instruction.Operand is MethodReference target && target.FullName.Contains("System.Enum::GetValues")));
    Assembly adapter = AssemblyLoadContext.Default.LoadFromAssemblyPath(adapterPath);
    Type plugin = adapter.GetType("Codex.StudioCoordinateNative.Plugin", true);
    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    MethodInfo prefix = plugin.GetMethod("LookupPrefix", flags);
    object[] disabled = { "replaceMode", "", "untouched" };
    Check("not-ready adapter preserves semantic source", (bool)prefix.Invoke(null, disabled) && (string)disabled[2] == "untouched");
    plugin.GetField("_ready", flags).SetValue(null, true);
    object[] known = { "showSelection", "", null };
    Check("actual prefix returns native Chinese", !(bool)prefix.Invoke(null, known) && (string)known[2] == "显示选择项");
    object[] unknown = { "Unlisted", "", "untouched" };
    Check("actual prefix preserves unknown original path", (bool)prefix.Invoke(null, unknown) && (string)unknown[2] == "untouched");
    object[] explicitLanguage = { "showSelection", "en-US", "untouched" };
    Check("actual prefix preserves explicit language call", (bool)prefix.Invoke(null, explicitLanguage) && (string)explicitLanguage[2] == "untouched");
    using var resolver = new DefaultAssemblyResolver();
    foreach (string directory in dependencies) resolver.AddSearchDirectory(directory);
    using var adapterMetadata = AssemblyDefinition.ReadAssembly(adapterPath, new ReaderParameters { AssemblyResolver = resolver });
    var pluginMetadata = adapterMetadata.MainModule.GetType("Codex.StudioCoordinateNative.Plugin");
    var filters = pluginMetadata.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == "BepInEx.BepInProcess")
        .Select(attribute => (string)attribute.ConstructorArguments[0].Value).ToArray();
    Check("Studio only process filter", filters.SequenceEqual(new[] { "CharaStudio" }));
    var consumerDependencies = pluginMetadata.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == "BepInEx.BepInDependency")
        .ToDictionary(attribute => (string)attribute.ConstructorArguments[0].Value,
            attribute => Convert.ToInt32(attribute.ConstructorArguments[1].Value));
    Check("present consumers load before native adapter", consumerDependencies["org.njaecha.plugins.dbde"] == 2 &&
        consumerDependencies["org.njaecha.plugins.assetimport"] == 2);
    Check("Coordinate dependency remains required", consumerDependencies[NativeCoordinateStrings.PluginId] == 1);
    var prefixMetadata = pluginMetadata.Methods.Single(item => item.Name == "LookupPrefix");
    Check("prefix never writes config or scene state", prefixMetadata.Body.Instructions.All(instruction =>
        instruction.Operand is not MethodReference target || !(target.FullName.Contains("ConfigEntry") ||
        target.FullName.Contains("SaveBytes") || target.FullName.Contains("LoadBytes") || target.FullName.Contains("SetUICulture"))));
    Check("source binary unchanged", Hash(File.ReadAllBytes(installedPlugin)) == originalHash);
    object compatibility = ConsumerRegression.Run(game, adapter);
    plugin.GetField("_ready", flags).SetValue(null, false);
    Console.WriteLine(JsonSerializer.Serialize(new {
        passed = true, checks = checks.Count, results = checks, resourceKeys = originals.Count,
        compatibility,
        nativeGetterCallsites = callers, installedDllSha256 = originalHash,
        adapterSha256 = Hash(File.ReadAllBytes(adapterPath)),
        nativeUnityInvoked = false, sceneOrConfigValuesRead = false, installedWrites = false,
        limitation = "Actual DLL metadata/resource signatures and adapter prefix tested in managed isolation. Studio UI creation and display remain runtime acceptance."
    }, new JsonSerializerOptions { WriteIndented = true }));
}
