using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length < 3) throw new ArgumentException("native GAME OUTPUT | import GAME REQUESTS OUTPUT ROOT... | encode GAME ROWS OUTPUT | rules GAME FILES OUTPUT");
string game = Path.GetFullPath(args[1]);
string corePath = Path.Combine(game, "BepInEx/plugins/XUnity.AutoTranslator/XUnity.AutoTranslator.Plugin.Core.dll");
string[] directories = { Path.GetDirectoryName(corePath), Path.Combine(game, "BepInEx/core"), Path.Combine(game, "BepInEx/plugins/XUnity.Common"), Path.Combine(game, "CharaStudio_Data/Managed") };
AssemblyLoadContext.Default.Resolving += (context, name) => {
    foreach (string directory in directories) {
        string candidate = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
    }
    return null;
};
var options = new JsonSerializerOptions { WriteIndented = true };
string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, options) + "\n", new UTF8Encoding(false));
if (args[0] == "native") { Native(game, Path.GetFullPath(args[2])); return; }
if (args.Length < 4) throw new ArgumentException("Missing output path");
var core = AssemblyLoadContext.Default.LoadFromAssemblyPath(corePath);
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
var helper = core.GetType("XUnity.AutoTranslator.Plugin.Core.Utilities.TextHelper", true);
var parse = helper.GetMethod("ReadTranslationLineAndDecode", flags);
var encode = helper.GetMethod("Encode", flags);
string[] Parse(string line) => (string[])parse.Invoke(null, new object[] { line });
using var input = JsonDocument.Parse(File.ReadAllText(args[2], Encoding.UTF8).TrimStart('\uFEFF'));
var rows = input.RootElement.EnumerateArray().ToArray();
if (args[0] == "encode") {
    var lines = new List<string>();
    foreach (var row in rows) {
        string key = row.GetProperty("key").GetString(), value = row.GetProperty("value").GetString();
        string line = (string)encode.Invoke(null, new object[] { key }) + "=" + (string)encode.Invoke(null, new object[] { value });
        var pair = Parse(line);
        if (pair == null || pair.Length != 2 || pair[0] != key || pair[1] != value) throw new InvalidDataException("Encoding round trip failed");
        lines.Add(line);
    }
    File.WriteAllLines(args[3], lines, new UTF8Encoding(false));
    return;
}
if (args[0] == "rules") {
    var result = new Dictionary<string, object>(StringComparer.Ordinal);
    foreach (var row in rows.OrderByDescending(r => r.GetProperty("destination").GetString(), StringComparer.OrdinalIgnoreCase)) {
        string destination = row.GetProperty("destination").GetString().Replace('\\', '/');
        const string prefix = "BepInEx/Translation/zh-CN/Text/";
        if (!destination.StartsWith(prefix, StringComparison.Ordinal)) continue;
        string relative = destination.Substring(prefix.Length);
        string owner = relative.StartsWith("Plugins/") ? relative.Split('/')[1] : "global";
        foreach (string line in File.ReadLines(row.GetProperty("path").GetString(), Encoding.UTF8)) {
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;
            var pair = Parse(line);
            if (pair == null || pair.Length != 2) continue;
            string pattern = null, source = pair[0], replacement = pair[1];
            if (source.StartsWith("r:\"") && source.EndsWith('"') && replacement.StartsWith("r:\"") && replacement.EndsWith('"')) {
                pattern = source.Substring(3, source.Length - 4); replacement = replacement.Substring(3, replacement.Length - 4); source = null;
                if (!pattern.StartsWith("\\A") || !pattern.EndsWith("\\z")) continue;
            } else if (source.StartsWith("r:") || source.StartsWith("sr:") || source.Length > 12000) continue;
            string identity = owner + "\n" + (source ?? pattern);
            result[identity] = new { id = "PKG-" + Hash(Encoding.UTF8.GetBytes(identity)).Substring(0, 16), owner, source, pattern, replacement };
        }
    }
    Save(args[3], result.Values.ToArray()); return;
}
if (args[0] != "import" || args.Length < 5) throw new ArgumentException("Unknown mode");
var requests = rows.GroupBy(r => r.GetProperty("key").GetString(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
var found = new Dictionary<string, string>();
void ReadLines(TextReader reader) {
    string line;
    while ((line = reader.ReadLine()) != null) {
        if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;
        var pair = Parse(line);
        if (pair == null || pair.Length != 2 || !requests.TryGetValue(pair[0], out var matches)) continue;
        string hash = Hash(Encoding.UTF8.GetBytes(pair[1]));
        foreach (var row in matches) if (hash == row.GetProperty("value_sha256").GetString()) found[row.GetProperty("id").GetString()] = pair[1];
    }
}
bool Allowed(string name) => !name.StartsWith('_') && !name.EndsWith("resizer.txt", StringComparison.OrdinalIgnoreCase);
void ReadZip(string path) {
    using var zip = ZipFile.OpenRead(path);
    foreach (var entry in zip.Entries) if (entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && Allowed(entry.Name)) {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true); ReadLines(reader);
    }
}
foreach (string source in args.Skip(4)) {
    string path = Path.GetFullPath(source);
    if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) ReadZip(path);
    else if (Directory.Exists(path)) {
        foreach (string file in Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories)) if (Allowed(Path.GetFileName(file))) { using var reader = File.OpenText(file); ReadLines(reader); }
        foreach (string file in Directory.GetFiles(path, "*.zip", SearchOption.AllDirectories)) ReadZip(file);
    } else throw new FileNotFoundException("Translation source not found", path);
}
Save(args[3], found);

void Native(string root, string output) {
    if (Directory.Exists(output)) throw new IOException("Output already exists");
    Directory.CreateDirectory(output);
    var specs = new[] {
        (path:"BepInEx/plugins/KKS_Plugins/KKS_ShaderSwapper.dll", before:"e4e61707bfea9bfca1b6ec3333a016d0d99d90ad17896ad91a5c6dff8b7af4f1", after:"09263465160d4db9bcea1beb1501308561d2f7b7b29604c19beed6c543e2424e", method:"System.Void KK_Plugins.ShaderSwapper::FileInputDrawer(BepInEx.Configuration.ConfigEntryBase)", indexes:new[]{34,36}, old:new[]{"Open 3D file","XML (*.xml)|*.xml| All files (*.*)|*.*"}, translated:new[]{"选择着色器映射 XML 文件","XML 文件 (*.xml)|*.xml|所有文件 (*.*)|*.*"}),
        (path:"BepInEx/plugins/DefaultStudioScene.KoikatuSunshine.dll", before:"31566b24640a9ad7f8be5bf3a82e9e2530e7a890d87a1a96f3d68532d2def398", after:"62b0cbb4b8836daa3e9a27bc2fd48099786326f5370638c709a2368cfe491561", method:"System.Void DefaultStudioScene.Koikatu.DefaultStudioScene::SceneButtonDrawer(BepInEx.Configuration.ConfigEntryBase)", indexes:new[]{13,16}, old:new[]{"Select a scene","Scenes (*.png)|*.png|All files|*.*"}, translated:new[]{"选择场景","场景 (*.png)|*.png|所有文件|*.*"})
    };
    var result = new List<object>();
    foreach (var spec in specs) {
        string source = Path.Combine(root, spec.path);
        if (!File.Exists(source)) { result.Add(new { path=spec.path, status="plugin_not_installed" }); continue; }
        string hash = Hash(File.ReadAllBytes(source));
        if (hash == spec.after) { result.Add(new { path=spec.path, status="already_patched" }); continue; }
        if (hash != spec.before) { result.Add(new { path=spec.path, status="version_not_matched" }); continue; }
        using var assembly = AssemblyDefinition.ReadAssembly(source);
        var method = assembly.MainModule.Types.SelectMany(t => t.Methods).Single(m => m.FullName == spec.method);
        for (int i=0;i<spec.indexes.Length;i++) {
            var instruction = method.Body.Instructions[spec.indexes[i]];
            if (instruction.OpCode.Code != Code.Ldstr || (string)instruction.Operand != spec.old[i]) throw new InvalidDataException("Dialog IL differs");
            instruction.Operand = spec.translated[i];
        }
        string target = Path.Combine(output, Path.GetFileName(source)); assembly.Write(target);
        if (Hash(File.ReadAllBytes(target)) != spec.after) throw new InvalidDataException("Dialog result hash differs");
        result.Add(new { path=spec.path, status="candidate_prepared", before=spec.before, after=spec.after, candidate=Path.GetFileName(target), changed=2 });
    }
    Save(Path.Combine(output,"native-dialog-candidates.json"),result);
}
