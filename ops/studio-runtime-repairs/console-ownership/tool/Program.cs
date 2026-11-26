using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length == 5 && args[0] == "--extract-probe") { ProbeExtractor.Run(args[1], args[2], args[3], args[4]); return; }
const string expected = "1be20737d73ae8d23db5c08d3a86e992bc7b12b171401686ecb264e05b439cf6";
if (args.Length != 2) throw new ArgumentException("Expected input DLL and new output DLL paths.");
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Output already exists.");
if (File.Exists(output + ".receipt.json")) throw new IOException("Receipt already exists.");
if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase)) throw new IOException("Input and output must differ.");
if (Hash(input) != expected) throw new InvalidDataException("Unsupported input SHA-256.");
using var a = AssemblyDefinition.ReadAssembly(input);
var p = a.MainModule.GetType("Unity.Console.Program");
var run = p.Methods.Single(x => x.Name == "Run");
var close = p.Methods.Single(x => x.Name == "Close");
var owns = new FieldDefinition("_ownsNativeConsoleForOwnershipFix", FieldAttributes.Private | FieldAttributes.Static, a.MainModule.TypeSystem.Boolean);
if (p.Fields.Any(x => x.Name == owns.Name)) throw new InvalidDataException("Already patched.");
var allocation = run.Body.Instructions.Single(x => x.OpCode.Code == Code.Call && x.Operand is MethodReference m && m.Name == "AllocConsole");
if (allocation.Next.OpCode.Code != Code.Pop) throw new InvalidDataException("Unexpected allocation result use.");
p.Fields.Add(owns);
allocation.Next.OpCode = OpCodes.Stsfld;
allocation.Next.Operand = owns;
var free = close.Body.Instructions.Single(x => x.OpCode.Code == Code.Call && x.Operand is MethodReference m && m.Name == "FreeConsole");
if (free.Next.OpCode.Code != Code.Pop) throw new InvalidDataException("Unexpected release result use.");
var il = close.Body.GetILProcessor();
var oldWindowCheck = close.Body.Instructions.Single(x => x.OpCode.Code == Code.Ldsfld && x.Operand is FieldReference f && f.FullName == "System.IntPtr System.IntPtr::Zero");
var end = close.Body.Instructions.Last();
if (end.OpCode.Code != Code.Ret) throw new InvalidDataException("Unexpected Close exit.");
// Redirect existing branches to the new ownership guard; null console must not bypass it.
var guard = il.Create(OpCodes.Ldsfld, owns);
foreach (var ins in close.Body.Instructions)
    if (ins.Operand == oldWindowCheck) ins.Operand = guard;
il.InsertBefore(oldWindowCheck, guard);
il.InsertBefore(oldWindowCheck, il.Create(OpCodes.Brfalse, end));
var afterFree = free.Next;
il.InsertAfter(afterFree, il.Create(OpCodes.Ldc_I4_0));
il.InsertAfter(afterFree.Next, il.Create(OpCodes.Stsfld, owns));
using var bytes = new MemoryStream();
a.Write(bytes);
bytes.Position = 0;
using var b = AssemblyDefinition.ReadAssembly(bytes);
using var original = AssemblyDefinition.ReadAssembly(input);
var before = AllTypes(original.MainModule.Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
var changed = AllTypes(b.MainModule.Types).SelectMany(t => t.Methods).Where(m => Shape(m) != Shape(before[m.FullName])).Select(m => m.FullName).ToArray();
if (changed.Length != 2 || !changed.Contains(run.FullName) || !changed.Contains(close.FullName)) throw new Exception("Unexpected method scope.");
if (!original.MainModule.AssemblyReferences.Select(r => r.FullName).SequenceEqual(b.MainModule.AssemblyReferences.Select(r => r.FullName))) throw new Exception("Assembly references changed.");
if (original.MainModule.RuntimeVersion != b.MainModule.RuntimeVersion) throw new Exception("Runtime changed.");
foreach (var t in AllTypes(b.MainModule.Types))
    foreach (var m in t.Methods.Where(x => x.HasBody))
        foreach (var i in m.Body.Instructions.Where(x => x.OpCode.OperandType == OperandType.ShortInlineBrTarget))
        {
            if (i.Operand is not Instruction target) throw new Exception("Invalid short branch target.");
            int displacement = target.Offset - i.Offset - i.GetSize();
            if (displacement < sbyte.MinValue || displacement > sbyte.MaxValue) throw new Exception("Short branch overflow.");
        }
var outputHash = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
if (outputHash != "91874b52329d4fa4bb0b69a21e073d69ed423eacdb3485ee03ba985b0a7cefd0") throw new Exception("Candidate differs from reviewed output SHA-256.");
var receipt = new {
    InputSHA256 = Hash(input), OutputSHA256 = outputHash, Runtime = b.MainModule.RuntimeVersion,
    ChangedMethods = changed, AddedField = owns.FullName, NewAssemblyReferences = Array.Empty<string>(),
    ResourceCount = b.MainModule.Resources.Count,
    ResourceHashesMatch = original.MainModule.Resources.OfType<EmbeddedResource>().Select(r => Convert.ToHexString(SHA256.HashData(r.GetResourceData()))).SequenceEqual(b.MainModule.Resources.OfType<EmbeddedResource>().Select(r => Convert.ToHexString(SHA256.HashData(r.GetResourceData())))),
    Boundary = "Borrowed native console ownership only. No writer recovery, hidden startup, or actual game validation claimed."
};
Directory.CreateDirectory(Path.GetDirectoryName(output));
using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) destination.Write(bytes.ToArray());
using (var destination = new StreamWriter(new FileStream(output + ".receipt.json", FileMode.CreateNew, FileAccess.Write))) destination.Write(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

static string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) { foreach (var t in types) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; } }
static string Shape(MethodDefinition m) => !m.HasBody ? "no-body" : string.Join("\n", m.Body.Instructions.Select(i => i.OpCode.Code + " " + (i.Operand is Instruction target ? "target:" + m.Body.Instructions.IndexOf(target) : i.Operand is Instruction[] targets ? string.Join(",", targets.Select(t => m.Body.Instructions.IndexOf(t))) : i.Operand is MemberReference r ? r.FullName : i.Operand)));
