using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

var known = new Dictionary<string, string>
{
    ["LiquidFluid.KKS"] = "edefaaa0418663182b04bf74cbaa59132e1b610014ababb5a467d69c778b74f2",
    ["SoftFlesh.KKS"] = "4ef0d800e979a0b597d95dfadee41d546fd17fef8936d9ec3b007bcddf337cf1"
};
string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
string Plugin(string assembly) => assembly.StartsWith("LiquidFluid") ? "LiquidFluid.LiquidFluidPlugin" : "SoftFlesh.SoftFleshPlugin";
IEnumerable<TypeDefinition> Types(TypeDefinition t) { yield return t; foreach (var n in t.NestedTypes) foreach (var x in Types(n)) yield return x; }
void Guard(string output, params string[] inputs)
{
    string full = Path.GetFullPath(output);
    if (inputs.Any(x => Path.GetFullPath(x).Equals(full, StringComparison.OrdinalIgnoreCase)) || File.Exists(full) || Directory.Exists(full) || File.Exists(full + ".receipt.json")) throw new InvalidOperationException("New distinct output required");
    for (var d = new DirectoryInfo(Path.GetDirectoryName(full)!); d != null; d = d.Parent)
    {
        if (new[] { "BepInEx", "UserData", "mods", "abdata" }.Contains(d.Name, StringComparer.OrdinalIgnoreCase) || d.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(d.FullName, ".git")) || Directory.Exists(Path.Combine(d.FullName, ".git"))) throw new InvalidOperationException("Runtime/content/repository output denied");
        if (File.Exists(Path.Combine(d.FullName, "CharaStudio.exe")) && Path.GetRelativePath(d.FullName, full).Split(Path.DirectorySeparatorChar)[0] != ".scratch") throw new InvalidOperationException("Only private scratch under game allowed");
    }
}
void Write(byte[] bytes, string path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var s = new FileStream(path, FileMode.CreateNew); s.Write(bytes); }
string Shape(MethodDefinition m)
{
    if (!m.HasBody) return "none";
    string O(object? o) => o switch { Instruction i => "@" + i.Offset, Instruction[] a => string.Join(",", a.Select(i => i.Offset)), _ => o?.ToString() ?? "" };
    return m.Body.InitLocals + "/" + string.Join(";", m.Body.Instructions.Select(i => i.Offset + ":" + i.OpCode.Name + ":" + O(i.Operand))) + "/" + string.Join(",", m.Body.Variables.Select(v => v.VariableType.FullName)) + "/" + string.Join(",", m.Body.ExceptionHandlers.Select(e => $"{e.HandlerType}:{e.CatchType}:{e.TryStart.Offset}:{e.TryEnd?.Offset}:{e.HandlerStart.Offset}:{e.HandlerEnd?.Offset}"));
}
void ValidateInput(string path, AssemblyDefinition a, bool allowCandidate)
{
    string hash = Hash(path);
    if (!known.TryGetValue(a.Name.Name, out var original)) throw new InvalidOperationException("Unsupported assembly");
    if (hash == original) return;
    if (!allowCandidate) throw new InvalidOperationException("Exact supported original required");
    using var receipt = JsonDocument.Parse(File.ReadAllText(path + ".receipt.json"));
    if (receipt.RootElement.GetProperty("originalSha256").GetString() != original || receipt.RootElement.GetProperty("candidateSha256").GetString() != hash) throw new InvalidOperationException("Unreviewed candidate");
}
if (args.Length == 4 && args[0] == "patch")
{
    string input = args[1], output = args[3]; Guard(output, input);
    using var a = AssemblyDefinition.ReadAssembly(input);
    ValidateInput(input, a, false);
    var method = a.MainModule.GetType(Plugin(a.Name.Name)).Methods.Single(m => m.Name == "LateUpdate");
    if (method.HasParameters || method.ReturnType.FullName != "System.Void" || method.Body.ExceptionHandlers.Count != 0) throw new InvalidOperationException("Expected parameterless void LateUpdate with no exception table");
    if (a.Name.HasPublicKey || (a.MainModule.Attributes & ModuleAttributes.StrongNameSigned) != 0) throw new InvalidOperationException("Signed input unsupported");
    byte[] original = File.ReadAllBytes(input); byte[] candidate = (byte[])original.Clone();
    using var pe = new PEReader(new MemoryStream(original, false));
    if (pe.PEHeaders.PEHeader!.CertificateTableDirectory.Size != 0) throw new InvalidOperationException("Authenticode input unsupported");
    var section = pe.PEHeaders.SectionHeaders.Single(s => method.RVA >= s.VirtualAddress && method.RVA < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData));
    int header = checked(section.PointerToRawData + (int)method.RVA - section.VirtualAddress);
    int headerSize = (original[header] & 3) == 2 ? 1 : (original[header] & 3) == 3 ? (BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(header, 2)) >> 12) * 4 : throw new InvalidOperationException("Invalid IL header");
    int begin = header + headerSize, length = method.Body.CodeSize;
    if (original[begin] != 0x28 || original[begin + length - 1] != 0x2a) throw new InvalidOperationException("Unexpected original method byte boundary");
    Array.Clear(candidate, begin, length); candidate[begin] = 0x2a; candidate[begin + length - 1] = 0x2a;
    int[] diffs = Enumerable.Range(0, original.Length).Where(i => original[i] != candidate[i]).ToArray();
    if (diffs.Any(i => i < begin || i >= begin + length)) throw new InvalidOperationException("Change escaped LateUpdate code body");
    Write(candidate, output);
    using var b = AssemblyDefinition.ReadAssembly(output);
    var before = a.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Shape);
    var after = b.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Shape);
    var changed = before.Keys.Where(k => before[k] != after[k]).ToArray();
    var bm = b.MainModule.GetType(Plugin(b.Name.Name)).Methods.Single(m => m.Name == "LateUpdate");
    if (changed.Length != 1 || changed[0] != method.FullName || before.Count != after.Count || bm.Body.Instructions[0].OpCode != OpCodes.Ret || bm.Body.Instructions.Last().OpCode != OpCodes.Ret || bm.Body.Instructions.Skip(1).SkipLast(1).Any(i => i.OpCode != OpCodes.Nop)) throw new InvalidOperationException("Unexpected actual method closure");
    var receipt = new { assembly = a.Name.Name, originalSha256 = known[a.Name.Name], candidateSha256 = Hash(output), fileLength = original.Length, changedCodeOffset = begin, changedCodeLength = length, changedBytes = diffs.Length, onlyCodeBodyChanged = true, allMetadataResourcesHeadersByteIdentical = true, totalMethods = before.Count, unchangedMethods = before.Count - 1, changedMethod = method.FullName, installed = false, runtimeVerified = false, limitation = "This plugin no longer intercepts camera controls while its panel is hovered; other UI/solver logic unchanged" };
    File.WriteAllText(output + ".receipt.json", JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(receipt)); return;
}
if (args.Length == 5 && args[0] == "timeline")
{
    string input = args[1], game = args[2], helper = args[3], output = args[4]; Guard(output, input, helper);
    string inputHash = Hash(input);
    if (inputHash != "1c609f8914fb91a6799873a7ef3cd4d864de085eedf6a9ac51165120d5058998" && inputHash != "7f6786bbfc8a97bf224472d91d9c99b1cb73a9ba5c71a8a96af9a3843723313b") throw new InvalidOperationException("Exact camera stage or camera-plus-spy stage required");
    using var resolver = new DefaultAssemblyResolver();
    foreach (var dir in new[] { Path.Combine(game, "BepInEx/core"), Path.Combine(game, "BepInEx/plugins"), Path.Combine(game, "CharaStudio_Data/Managed") }) resolver.AddSearchDirectory(dir);
    using var a = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
    using var h = AssemblyDefinition.ReadAssembly(helper);
    var before = a.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Shape);
    var oldMaxStack = a.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).Where(m => m.HasBody).ToDictionary(m => m.FullName, m => m.Body.MaxStackSize);
    var references = a.MainModule.AssemblyReferences.Select(r => r.FullName).ToArray();
    var resources = a.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r => r.Name, r => Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
    if (a.MainModule.GetType("TimelineUiReadiness") != null) throw new InvalidOperationException("Helper already present");
    var injected = Clone.Type(h.MainModule.GetType("TimelineUiReadiness"), a.MainModule);
    var targetType = a.MainModule.GetType(a.Name.Name.StartsWith("LiquidFluid") ? "LiquidFluid.LfTimeline" : "SoftFlesh.SfTimeline");
    var ready = targetType.Methods.Single(m => m.Name == "EnsureReady");
    if (ready.HasThis || ready.HasParameters || ready.ReturnType.FullName != "System.Boolean") throw new InvalidOperationException("Unexpected EnsureReady signature");
    var first = ready.Body.Instructions[0]; var il = ready.Body.GetILProcessor();
    foreach (var ins in new[] { il.Create(OpCodes.Call, injected.Methods.Single(m => m.Name == "IsReady")), il.Create(OpCodes.Brtrue, first), il.Create(OpCodes.Ldc_I4_0), il.Create(OpCodes.Ret) }) il.InsertBefore(first, ins);
    using var stream = new MemoryStream(); a.Write(stream); var finalBytes = stream.ToArray();
    using (var normalized = AssemblyDefinition.ReadAssembly(new MemoryStream(finalBytes, false)))
    using (var pe = new PEReader(new MemoryStream(finalBytes, false)))
    {
        foreach (var method in normalized.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).Where(m => m.HasBody && oldMaxStack.ContainsKey(m.FullName)))
        {
            if (method.Body.MaxStackSize == oldMaxStack[method.FullName]) continue;
            var section = pe.PEHeaders.SectionHeaders.Single(s => method.RVA >= s.VirtualAddress && method.RVA < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData));
            int offset = checked(section.PointerToRawData + (int)method.RVA - section.VirtualAddress);
            if ((finalBytes[offset] & 3) != 3) throw new InvalidOperationException("Cannot retain original max stack in a tiny header");
            BinaryPrimitives.WriteUInt16LittleEndian(finalBytes.AsSpan(offset + 2, 2), checked((ushort)oldMaxStack[method.FullName]));
        }
    }
    Write(finalBytes, output);
    using var b = AssemblyDefinition.ReadAssembly(output, new ReaderParameters { AssemblyResolver = resolver });
    var after = b.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Shape);
    var drift = before.Where(x => x.Key != ready.FullName && after[x.Key] != x.Value).Select(x => x.Key).ToArray();
    if (drift.Length != 0) throw new InvalidOperationException("Unrelated method drift: " + string.Join(";", drift));
    if (!references.SequenceEqual(b.MainModule.AssemblyReferences.Select(r => r.FullName))) throw new InvalidOperationException("Unexpected added assembly reference");
    if (resources.Any(x => Convert.ToHexString(SHA256.HashData(((EmbeddedResource)b.MainModule.Resources.Single(r => r.Name == x.Key)).GetResourceData())) != x.Value)) throw new InvalidOperationException("Resource content changed");
    var recalculatedStackHeaders = b.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).Where(m => m.HasBody && oldMaxStack.TryGetValue(m.FullName, out int old) && old != m.Body.MaxStackSize).Select(m => new { method = m.FullName, before = oldMaxStack[m.FullName], after = m.Body.MaxStackSize }).ToArray();
    var receipt = new { originalSha256 = known[a.Name.Name], inputSha256 = inputHash, candidateSha256 = Hash(output), stage = "Timeline readiness after camera/optional spy", originalMethods = before.Count, addedMethods = after.Count - before.Count, changedOriginalMethod = ready.FullName, allOtherOriginalInstructionsAndExceptionRegionsPreserved = true, recalculatedStackHeaders, referencesUnchanged = true, embeddedResourcesUnchanged = true, helperSourceRequired = true, installed = false, runtimeVerified = false };
    File.WriteAllText(output + ".receipt.json", JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(receipt)); return;
}
if (args.Length == 5 && args[0] == "probe")
{
    string output = args[4]; Guard(output, args[1], args[2], args[3]);
    using var fixture = AssemblyDefinition.ReadAssembly(args[3]);
    foreach (string input in args.Skip(1).Take(2))
    {
        using var a = AssemblyDefinition.ReadAssembly(input); ValidateInput(input, a, true);
        var originalPlugin = a.MainModule.GetType(Plugin(a.Name.Name)); var targetPlugin = fixture.MainModule.GetType(originalPlugin.FullName);
        var method = originalPlugin.Methods.Single(m => m.Name == "LateUpdate");
        var closureTypes = originalPlugin.NestedTypes.Where(t => t.Methods.Any(m => m.Name.StartsWith("<LateUpdate>"))).ToArray();
        foreach (var ct in closureTypes) targetPlugin.NestedTypes.Add(new TypeDefinition(ct.Namespace, ct.Name, ct.Attributes, fixture.MainModule.TypeSystem.Object));
        foreach (var ct in closureTypes)
        {
            var target = targetPlugin.NestedTypes.Single(t => t.Name == ct.Name);
            foreach (var f in ct.Fields) target.Fields.Add(new FieldDefinition(f.Name, f.Attributes, Clone.TypeRef(f.FieldType, fixture.MainModule)));
            foreach (var m in ct.Methods)
            {
                var n = new MethodDefinition(m.Name, m.Attributes, Clone.TypeRef(m.ReturnType, fixture.MainModule));
                foreach (var p in m.Parameters) n.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Clone.TypeRef(p.ParameterType, fixture.MainModule)));
                target.Methods.Add(n);
            }
        }
        foreach (var ct in closureTypes) foreach (var m in ct.Methods.Where(m => m.HasBody)) Clone.Body(m, targetPlugin.NestedTypes.Single(t => t.Name == ct.Name).Methods.Single(x => x.FullName == m.FullName), fixture.MainModule);
        Clone.Body(method, targetPlugin.Methods.Single(m => m.Name == "LateUpdate"), fixture.MainModule);
    }
    using var bytes = new MemoryStream(); fixture.Write(bytes); Write(bytes.ToArray(), output);
    Console.WriteLine("Actual original/candidate LateUpdate bodies and all camera closures extracted; only external engine/other-wrapper collaborators synthetic"); return;
}
if (args.Length == 7 && args[0] == "readiness-probe")
{
    string output = args[6]; Guard(output, args[1], args[2], args[3], args[4], args[5]);
    using var fixture = AssemblyDefinition.ReadAssembly(args[5]);
    for (int index = 1; index <= 4; index++)
    {
        using var a = AssemblyDefinition.ReadAssembly(args[index]); ValidateInput(args[index], a, true);
        bool guarded = index >= 3;
        var sourceType = a.MainModule.GetType(a.Name.Name.StartsWith("LiquidFluid") ? "LiquidFluid.LfTimeline" : "SoftFlesh.SfTimeline");
        var method = sourceType.Methods.Single(m => m.Name == "EnsureReady");
        var dest = fixture.MainModule.GetType(a.Name.Name.StartsWith("LiquidFluid") ? "FixtureLiquid" : "FixtureSoft");
        var target = dest.Methods.Single(m => m.Name == (guarded ? "EnsureReadyGuarded" : "EnsureReadyOriginal"));
        // Only the synthetic bridge owner is renamed; every opcode, local and EH is copied.
        sourceType.Name = dest.Name; sourceType.Namespace = "";
        Clone.Body(method, target, fixture.MainModule);
        if (guarded)
        {
            var originalHelper = a.MainModule.GetType("TimelineUiReadiness").Methods.Single(m => m.Name == "IsReady");
            Clone.Body(originalHelper, fixture.MainModule.GetType("TimelineUiReadiness").Methods.Single(m => m.Name == "IsReady"), fixture.MainModule);
        }
    }
    using var stream = new MemoryStream(); fixture.Write(stream); Write(stream.ToArray(), output);
    Console.WriteLine("Actual original/final EnsureReady and final injected helper CIL extracted; collaborator discovery and UI state synthetic"); return;
}
throw new ArgumentException("patch exact-original game-root new-output | probe liquid-input soft-input synthetic-fixture new-output");
