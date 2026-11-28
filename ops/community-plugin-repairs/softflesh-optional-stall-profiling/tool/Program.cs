using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

const string Original = "4ef0d800e979a0b597d95dfadee41d546fd17fef8936d9ec3b007bcddf337cf1";
const string CameraOnly = "2830695d778ae6b6c99d66aa4d63825efa26d6cbd8ff0eebf5e782877d409c2b";
const string PluginName = "SoftFlesh.SoftFleshPlugin";
const string SpyName = "SoftFlesh.SFStallSpy";
string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
IEnumerable<TypeDefinition> Types(TypeDefinition t) { yield return t; foreach (var n in t.NestedTypes) foreach (var x in Types(n)) yield return x; }
void Guard(string output, params string[] inputs)
{
    var full = Path.GetFullPath(output);
    if (inputs.Any(p => full.Equals(Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase)) || File.Exists(full) || Directory.Exists(full) || File.Exists(full + ".receipt.json")) throw new InvalidOperationException("New distinct output required");
    for (var d = new DirectoryInfo(Path.GetDirectoryName(full)!); d != null; d = d.Parent)
    {
        if (new[] { "BepInEx", "UserData", "mods", "abdata" }.Contains(d.Name, StringComparer.OrdinalIgnoreCase) || d.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(d.FullName, ".git")) || Directory.Exists(Path.Combine(d.FullName, ".git"))) throw new InvalidOperationException("Runtime/content/repository output denied");
        if (File.Exists(Path.Combine(d.FullName, "CharaStudio.exe")) && Path.GetRelativePath(d.FullName, full).Split(Path.DirectorySeparatorChar)[0] != ".scratch") throw new InvalidOperationException("Only private game scratch output allowed");
    }
}
void Write(byte[] bytes, string output) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); using var s = new FileStream(output, FileMode.CreateNew); s.Write(bytes); }
string Shape(MethodDefinition m)
{
    if (!m.HasBody) return "none";
    string Operand(object? o) => o switch { Instruction i => "@" + i.Offset, Instruction[] xs => string.Join(",", xs.Select(i => i.Offset)), _ => o?.ToString() ?? "" };
    return m.Body.MaxStackSize + "/" + m.Body.InitLocals + "/" + string.Join(";", m.Body.Instructions.Select(i => i.Offset + ":" + i.OpCode.Name + ":" + Operand(i.Operand))) + "/" + string.Join(",", m.Body.Variables.Select(v => v.VariableType.FullName)) + "/" + string.Join(",", m.Body.ExceptionHandlers.Select(e => $"{e.HandlerType}:{e.CatchType}:{e.TryStart.Offset}:{e.TryEnd?.Offset}:{e.HandlerStart.Offset}:{e.HandlerEnd?.Offset}"));
}
if (args.Length != 4 || !new[] { "patch", "probe" }.Contains(args[0])) throw new ArgumentException("patch exact-original game-root new-output | probe reviewed-input synthetic-fixture new-output");
string mode = args[0], input = args[1], aux = args[2], output = args[3];
Guard(output, input, aux);
using var resolver = new DefaultAssemblyResolver();
if (mode == "patch") foreach (var d in new[] { Path.Combine(aux, "BepInEx/core"), Path.Combine(aux, "BepInEx/plugins"), Path.Combine(aux, "CharaStudio_Data/Managed") }) resolver.AddSearchDirectory(d);
using var source = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
var awake = source.MainModule.GetType(PluginName).Methods.Single(x => x.Name == "Awake");
var spy = source.MainModule.GetType(SpyName);
var apply = spy.Methods.Single(x => x.Name == "Apply");
var remove = spy.Methods.Single(x => x.Name == "Remove");
if (mode == "patch")
{
    if (Hash(input) != Original && Hash(input) != CameraOnly) throw new InvalidOperationException("Exact supported original or camera-stage input required");
    if (source.Name.HasPublicKey || (source.MainModule.Attributes & ModuleAttributes.StrongNameSigned) != 0) throw new InvalidOperationException("Signed managed input unsupported");
    if (apply.HasThis || apply.Parameters.Count != 0 || apply.ReturnType.FullName != "System.Void") throw new InvalidOperationException("Expected static parameterless void diagnostic call");
    var calls = awake.Body.Instructions.Where(i => i.OpCode.Code == Code.Call && i.Operand is MethodReference m && m.FullName == apply.FullName).ToArray();
    if (calls.Length != 1 || calls[0].Offset != 11 || calls[0].GetSize() != 5 || calls[0].Next.Offset != 16) throw new InvalidOperationException("Unexpected exact Awake call site");
    var call = calls[0];
    if (awake.Body.ExceptionHandlers.Any(e => e.TryStart.Offset <= call.Offset && (e.TryEnd == null || call.Offset < e.TryEnd.Offset) || e.HandlerStart.Offset <= call.Offset && (e.HandlerEnd == null || call.Offset < e.HandlerEnd.Offset)) || awake.Body.Instructions.Any(i => i.Operand is Instruction target && target == call || i.Operand is Instruction[] targets && targets.Contains(call))) throw new InvalidOperationException("Diagnostic call is a branch/exception target");
    byte[] bytes = File.ReadAllBytes(input);
    using var pe = new PEReader(new MemoryStream(bytes, false));
    if (pe.PEHeaders.PEHeader!.CertificateTableDirectory.Size != 0) throw new InvalidOperationException("Authenticode input unsupported");
    var section = pe.PEHeaders.SectionHeaders.Single(s => awake.RVA >= s.VirtualAddress && awake.RVA < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData));
    int headerOffset = checked(section.PointerToRawData + (int)awake.RVA - section.VirtualAddress);
    int flags = bytes[headerOffset] & 3;
    int headerSize = flags == 2 ? 1 : flags == 3 ? ((BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(headerOffset, 2)) >> 12) * 4) : throw new InvalidOperationException("Invalid IL method header");
    int site = checked(headerOffset + headerSize + call.Offset);
    int token = ((MethodReference)call.Operand).MetadataToken.ToInt32();
    if (bytes[site] != 0x28 || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(site + 1, 4)) != token) throw new InvalidOperationException("Raw IL call token mismatch");
    byte[] candidate = (byte[])bytes.Clone();
    Array.Clear(candidate, site, 5);
    var differences = Enumerable.Range(0, bytes.Length).Where(i => bytes[i] != candidate[i]).ToArray();
    if (differences.Any(i => i < site || i >= site + 5)) throw new InvalidOperationException("Out of scope byte difference");
    Write(candidate, output);
    using var result = AssemblyDefinition.ReadAssembly(output, new ReaderParameters { AssemblyResolver = resolver });
    var before = source.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Shape);
    var after = result.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Shape);
    var changed = before.Keys.Where(k => before[k] != after[k]).ToArray();
    var resultAwake = result.MainModule.GetType(PluginName).Methods.Single(m => m.Name == "Awake");
    if (changed.Length != 1 || changed[0] != awake.FullName || after.Count != before.Count) throw new InvalidOperationException("Unexpected changed method set");
    if (!resultAwake.Body.Instructions.Where(i => i.Offset >= 11 && i.Offset < 16).All(i => i.OpCode == OpCodes.Nop) || resultAwake.Body.Instructions.Count != awake.Body.Instructions.Count + 4) throw new InvalidOperationException("Expected exactly five NOP bytes");
    if (source.Name.FullName != result.Name.FullName || source.MainModule.Mvid != result.MainModule.Mvid || !source.MainModule.AssemblyReferences.Select(x => x.FullName).SequenceEqual(result.MainModule.AssemblyReferences.Select(x => x.FullName))) throw new InvalidOperationException("Identity/reference drift");
    var receipt = new { originalSha256 = Original, inputSha256 = Hash(input), cameraStageAlreadyApplied = Hash(input) == CameraOnly, candidateSha256 = Hash(output), byteLength = bytes.Length, changedByteOffsets = differences, replacementRangeOffset = site, replacementRangeLength = 5, totalMethods = before.Count, unchangedMethods = before.Count - 1, changedMethod = awake.FullName, originalApplyCalls = 1, candidateApplyCalls = 0, metadataAndResourcesByteIdentical = true, originalMethodHeaderAndExceptionTableByteIdentical = true, noNewAssemblyReferences = true, nativeRuntimeVerified = false, installed = false };
    File.WriteAllText(output + ".receipt.json", JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(receipt));
    return;
}
if (Hash(input) != Original)
{
    var receiptPath = input + ".receipt.json";
    using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
    if (receipt.RootElement.GetProperty("originalSha256").GetString() != Original || receipt.RootElement.GetProperty("candidateSha256").GetString() != Hash(input)) throw new InvalidOperationException("Unreviewed extraction input");
}
using var fixture = AssemblyDefinition.ReadAssembly(aux);
var fm = fixture.MainModule;
var fp = fm.GetType("Synthetic.Plugin"); var fs = fm.GetType("Synthetic.Spy"); var fh = fm.GetType("Synthetic.HarmonyStub");
var prefix = fp.Methods.Single(m => m.Name == "AwakePrefix");
prefix.Body = new MethodBody(prefix);
foreach (var ins in awake.Body.Instructions.Where(i => i.Offset < 16))
{
    var copy = Instruction.Create(OpCodes.Nop); copy.OpCode = ins.OpCode;
    copy.Operand = ins.Operand switch
    {
        null => null,
        FieldReference f when f.Name == "SLog" => fp.Fields.Single(x => x.Name == "SLog"),
        MethodReference m when m.Name == "get_Logger" => fp.Methods.Single(x => x.Name == "get_Logger"),
        MethodReference m when m.FullName == apply.FullName => fs.Methods.Single(x => x.Name == "Apply"),
        _ => throw new InvalidOperationException("Unexpected Awake prefix operand")
    };
    prefix.Body.Instructions.Add(copy);
}
prefix.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
var fr = fs.Methods.Single(x => x.Name == "Remove");
fr.Body = new MethodBody(fr) { InitLocals = remove.Body.InitLocals, MaxStackSize = remove.Body.MaxStackSize };
var map = remove.Body.Instructions.ToDictionary(x => x, _ => Instruction.Create(OpCodes.Nop));
foreach (var ins in remove.Body.Instructions)
{
    var copy = map[ins]; copy.OpCode = ins.OpCode;
    copy.Operand = ins.Operand switch
    {
        null => null,
        Instruction target => map[target],
        FieldReference f when f.Name == "_h" => fs.Fields.Single(x => x.Name == "Handle"),
        MethodReference m when m.Name == "UnpatchSelf" => fh.Methods.Single(x => x.Name == "UnpatchSelf"),
        _ => throw new InvalidOperationException("Unexpected Remove operand")
    };
    fr.Body.Instructions.Add(copy);
}
foreach (var e in remove.Body.ExceptionHandlers) fr.Body.ExceptionHandlers.Add(new ExceptionHandler(e.HandlerType)
{
    TryStart = map[e.TryStart], TryEnd = e.TryEnd == null ? null : map[e.TryEnd], HandlerStart = map[e.HandlerStart], HandlerEnd = e.HandlerEnd == null ? null : map[e.HandlerEnd], CatchType = e.CatchType == null ? null : e.CatchType.FullName == "System.Object" ? fm.TypeSystem.Object : fm.ImportReference(typeof(Exception))
});
using var outputStream = new MemoryStream(); fixture.Write(outputStream); Write(outputStream.ToArray(), output);
Console.WriteLine(JsonSerializer.Serialize(new { actualAwakePrefix = true, actualRemoveBody = true, targetExecuted = false, collaborators = "synthetic logger and Harmony diagnostic counters only" }));
