using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

const string Original = "99b38b09709e09a65a4e3e78f4f11ba3745ced0e075f8f06476e66fed8a929ab";
const string Harmony = "1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031";
string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
IEnumerable<TypeDefinition> All(TypeDefinition t) { yield return t; foreach (var n in t.NestedTypes) foreach (var x in All(n)) yield return x; }
void NewOutput(string output, string input)
{
    output = Path.GetFullPath(output); input = Path.GetFullPath(input);
    if (output.Equals(input, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || Directory.Exists(output) || File.Exists(output + ".receipt.json")) throw new InvalidOperationException("A new distinct output is required");
    for (var d = new DirectoryInfo(Path.GetDirectoryName(output)!); d != null; d = d.Parent)
    {
        if (new[] { "BepInEx", "UserData", "mods", "abdata" }.Contains(d.Name, StringComparer.OrdinalIgnoreCase) || d.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Runtime/content output denied");
        if (File.Exists(Path.Combine(d.FullName, ".git")) || Directory.Exists(Path.Combine(d.FullName, ".git"))) throw new InvalidOperationException("Repository output denied");
        if (File.Exists(Path.Combine(d.FullName, "CharaStudio.exe")) && Path.GetRelativePath(d.FullName, output).Split(Path.DirectorySeparatorChar)[0] != ".scratch") throw new InvalidOperationException("Game output must be private scratch");
    }
}
void Write(AssemblyDefinition a, string path)
{
    using var buffer = new MemoryStream(); a.Write(buffer);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    using var s = new FileStream(path, FileMode.CreateNew); buffer.Position = 0; buffer.CopyTo(s);
}
string Fingerprint(MethodDefinition m)
{
    if (!m.HasBody) return "no-body";
    var ins = m.Body.Instructions;
    string Op(object o) => o switch { Instruction i => "@" + ins.IndexOf(i), Instruction[] aa => string.Join(",", aa.Select(i => "@" + ins.IndexOf(i))), _ => o?.ToString() ?? "" };
    return string.Join("\n", ins.Select(i => i.OpCode.Name + ":" + Op(i.Operand))) + "\nV=" + string.Join(",", m.Body.Variables.Select(v => v.VariableType.FullName)) + "\nEH=" + string.Join(";", m.Body.ExceptionHandlers.Select(e => $"{e.HandlerType}:{e.CatchType}:{ins.IndexOf(e.TryStart)}:{ins.IndexOf(e.TryEnd)}:{ins.IndexOf(e.HandlerStart)}:{ins.IndexOf(e.HandlerEnd)}"));
}
string Contract(AssemblyDefinition assembly)
{
    var mm = assembly.MainModule;
    return string.Join("\n", new[] { assembly.Name.FullName }.Concat(mm.AssemblyReferences.Select(r => r.FullName)).Concat(mm.Types.SelectMany(All).SelectMany(t => new[] { t.FullName + ":" + t.Attributes + ":" + t.BaseType?.FullName }.Concat(t.Fields.Select(f => f.FullName + ":" + f.Attributes + ":" + (f.HasConstant ? f.Constant : ""))).Concat(t.Methods.Select(m => m.FullName + ":" + m.Attributes)).Concat(t.CustomAttributes.Select(x => x.AttributeType.FullName + ":" + string.Join(",", x.ConstructorArguments.Select(c => c.Value)))))).Concat(mm.Resources.Select(r => r.Name + ":" + r.Attributes + ":" + (r is EmbeddedResource er ? Convert.ToHexString(SHA256.HashData(er.GetResourceData())) : r.ResourceType.ToString()))));
}
if (args.Length != 4 || args[0] != "patch" && args[0] != "probe") throw new ArgumentException("patch original game-root new-output | probe reviewed-input synthetic-fixture new-output");
string mode = args[0], input = args[1], fixture = args[2], output = args[3]; NewOutput(output, input);
string inputHash = Hash(input);
if (mode == "patch" && inputHash != Original) throw new InvalidOperationException("Unsupported original SHA-256");
using var resolver = new DefaultAssemblyResolver();
if (mode == "patch") foreach (var p in new[] { Path.Combine(fixture, "BepInEx/core"), Path.Combine(fixture, "CharaStudio_Data/Managed") }) resolver.AddSearchDirectory(p);
using var a = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
var module = a.MainModule;
var owner = module.GetType("AntiGravity.TimelinePhysics");
var dispose = owner.Methods.Single(m => m.Name == "Dispose");
if (mode == "probe")
{
    if (inputHash != Original)
    {
        string receipt = input + ".receipt.json"; if (!File.Exists(receipt)) throw new InvalidOperationException("Patch receipt required");
        using var r = JsonDocument.Parse(File.ReadAllText(receipt));
        if (r.RootElement.GetProperty("originalSha256").GetString() != Original || r.RootElement.GetProperty("outputSha256").GetString() != inputHash) throw new InvalidOperationException("Patch receipt mismatch");
    }
    using var f = AssemblyDefinition.ReadAssembly(fixture);
    Clone.Body(dispose, f.MainModule.GetType(owner.FullName).Methods.Single(m => m.Name == "Dispose"), f.MainModule);
    Write(f, output); Console.WriteLine(JsonSerializer.Serialize(new { inputHash, actualCilMethods = 1, syntheticHarmonyBoundary = true, genuineHarmonyExecution = false })); return;
}
string harmonyPath = Path.Combine(fixture, "BepInEx/core/0Harmony.dll");
if (Hash(harmonyPath) != Harmony) throw new InvalidOperationException("Unsupported Harmony SHA-256");
using var h = AssemblyDefinition.ReadAssembly(harmonyPath);
var unpatch = h.MainModule.GetType("HarmonyLib.Harmony").Methods.Single(m => m.Name == "Unpatch" && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Reflection.MethodBase", "System.Reflection.MethodInfo" }));
var methods = module.Types.SelectMany(All).SelectMany(t => t.Methods).ToArray();
var before = methods.ToDictionary(m => m.FullName, Fingerprint); var contract = Contract(a);
if (dispose.Body.Instructions.Count != 4 || dispose.Body.Instructions[2].Operand is not MethodReference self || self.Name != "UnpatchSelf") throw new InvalidOperationException("Unexpected exact Dispose shape");
var constructor = owner.Methods.Single(m => m.Name == ".ctor");
var getType = (MethodReference)constructor.Body.Instructions.First(i => i.Operand is MethodReference mr && mr.Name == "GetTypeFromHandle").Operand;
var accessMethod = (MethodReference)constructor.Body.Instructions.First(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == "HarmonyLib.AccessTools" && mr.Name == "Method").Operand;
var boneType = (TypeReference)constructor.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference t && t.FullName == "DynamicBone").Operand;
var floatType = (TypeReference)constructor.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference t && t.FullName == "System.Single").Operand;
var afterBone = owner.Methods.Single(m => m.Name == "AfterBone");
if (!afterBone.Parameters.Select(p => p.Name).SequenceEqual(new[] { "__exception", "__state", "__runOriginal" }) || afterBone.Parameters[2].ParameterType.FullName != "System.Boolean") throw new InvalidOperationException("Unexpected exact finalizer contract");
var harmonyField = (FieldReference)dispose.Body.Instructions[1].Operand;
var methodBase = new TypeReference("System.Reflection", "MethodBase", module, module.TypeSystem.CoreLibrary);
var runtimeMethodHandle = new TypeReference("System", "RuntimeMethodHandle", module, module.TypeSystem.CoreLibrary, true);
var fromHandle = new MethodReference("GetMethodFromHandle", methodBase, methodBase) { HasThis = false };
fromHandle.Parameters.Add(new ParameterDefinition(runtimeMethodHandle));
// Harmony targets mscorlib 2; use the plugin's existing core scope for the same validated signature.
var unpatchReference = new MethodReference(unpatch.Name, module.TypeSystem.Void, harmonyField.FieldType) { HasThis = true };
unpatchReference.Parameters.Add(new ParameterDefinition(methodBase));
unpatchReference.Parameters.Add(new ParameterDefinition(accessMethod.ReturnType));
dispose.Body = new MethodBody(dispose) { InitLocals = true, MaxStackSize = 8 };
var target = new VariableDefinition(accessMethod.ReturnType); dispose.Body.Variables.Add(target);
var il = dispose.Body.GetILProcessor(); var originalStart = il.Create(OpCodes.Ldarg_0);
void Add(Instruction i) => il.Append(i);
// Resolve the same exact float overload used during installation; absent target follows original cleanup.
Add(il.Create(OpCodes.Ldtoken, boneType)); Add(il.Create(OpCodes.Call, getType)); Add(il.Create(OpCodes.Ldstr, "UpdateDynamicBones"));
Add(il.Create(OpCodes.Ldc_I4_1)); Add(il.Create(OpCodes.Newarr, getType.ReturnType)); Add(il.Create(OpCodes.Dup)); Add(il.Create(OpCodes.Ldc_I4_0));
Add(il.Create(OpCodes.Ldtoken, floatType)); Add(il.Create(OpCodes.Call, getType)); Add(il.Create(OpCodes.Stelem_Ref)); Add(il.Create(OpCodes.Ldnull)); Add(il.Create(OpCodes.Call, accessMethod)); Add(il.Create(OpCodes.Stloc, target));
Add(il.Create(OpCodes.Ldloc, target)); Add(il.Create(OpCodes.Brfalse, originalStart));
Add(il.Create(OpCodes.Ldarg_0)); Add(il.Create(OpCodes.Ldfld, harmonyField)); Add(il.Create(OpCodes.Ldloc, target));
Add(il.Create(OpCodes.Ldtoken, afterBone)); Add(il.Create(OpCodes.Call, fromHandle)); Add(il.Create(OpCodes.Castclass, accessMethod.ReturnType));
Add(il.Create(OpCodes.Callvirt, unpatchReference));
Add(originalStart); Add(il.Create(OpCodes.Ldfld, harmonyField)); Add(il.Create(OpCodes.Callvirt, self)); Add(il.Create(OpCodes.Ret));
var changed = methods.Where(m => before[m.FullName] != Fingerprint(m)).Select(m => m.FullName).ToArray();
if (changed.Length != 1 || changed[0] != dispose.FullName || dispose.Body.ExceptionHandlers.Count != 0) throw new InvalidOperationException("Change scope violated");
Write(a, output);
using (var reread = AssemblyDefinition.ReadAssembly(output, new ReaderParameters { AssemblyResolver = resolver }))
{
    if (Contract(reread) != contract)
    {
        var oldLines = contract.Split('\n'); var newLines = Contract(reread).Split('\n');
        File.WriteAllText(output + ".contract-diff.json", JsonSerializer.Serialize(new { removed = oldLines.Except(newLines), added = newLines.Except(oldLines) }));
        throw new InvalidOperationException("Metadata/resource identity changed");
    }
    var written = reread.MainModule.Types.SelectMany(All).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Fingerprint);
    if (written.Count != before.Count || before.Where(p => p.Key != dispose.FullName).Any(p => written[p.Key] != p.Value)) throw new InvalidOperationException("Unexpected serialized changes");
}
var receiptOut = new { originalSha256 = Original, harmonySha256 = Harmony, outputSha256 = Hash(output), changedMethods = changed, totalMethods = methods.Length, unchangedMethods = methods.Length - 1, finalizerGuardPreserved = true, identityAndResourcesPreserved = true, installed = false, genuineHarmonyVerified = false, runtimeVerified = false };
File.WriteAllText(output + ".receipt.json", JsonSerializer.Serialize(receiptOut, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(receiptOut));
