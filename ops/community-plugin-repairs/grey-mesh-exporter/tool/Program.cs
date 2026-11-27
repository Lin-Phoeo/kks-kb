using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length == 4 && args[0] == "--extract") { ProbeExtractor.Run(args[1], args[2], args[3]); return; }
if (args.Length != 3) throw new ArgumentException("Input, helper, new output required.");
string input = args[0], helper = args[1], output = args[2];
const string originalSha = "be9a83e1619a7bf332f7c0a54c3f88a5b920135b3c0096ebd2326c3d33238736";
const string candidateSha = "530dc95e00cfb34486687a3de85cb995d4f6ae1cacc7169d58c07b97dacd15d7";
if (Hash(input) != originalSha) throw new InvalidOperationException("Unknown input SHA-256.");
if (File.Exists(output) || File.Exists(output + ".receipt.json")) throw new InvalidOperationException("Output already exists.");
using var assembly = AssemblyDefinition.ReadAssembly(input);
using var helperAssembly = AssemblyDefinition.ReadAssembly(helper);
var module = assembly.MainModule;
TypeDefinition safety = Clone.Type(helperAssembly.MainModule.GetType("GreyExportRepair.ExportSafety"), module);
MethodDefinition Safety(string name) => safety.Methods.Single(method => method.Name == name);
var exporter = module.GetType("Exporter.TextureExporter");
var save = exporter.Methods.Single(method => method.Name == "SaveTexture");
var write = exporter.Methods.Single(method => method.Name == "WriteAllBytes");
var convert = exporter.Methods.Single(method => method.Name == "ConvertToRGBNormalMap");
var export = module.GetType("Grey").Methods.Single(method => method.Name == "Export");
var skinned = module.GetType("Exporter.Fbx").Methods.Single(method => method.Name == "ExportSkinnedMeshOrCloth");
var meshExport = module.GetType("Exporter.Fbx").Methods.Single(method => method.Name == "ExportMesh");

// Keep the original write and its stream finally; truncate an old destination and propagate I/O failure.
var open = write.Body.Instructions.Single(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "System.IO.File" && m.Name == "Open");
if (open.Previous.OpCode != OpCodes.Ldc_I4_4) throw new InvalidOperationException("FileMode shape differs.");
open.Previous.OpCode = OpCodes.Ldc_I4_2;
var writeCatch = write.Body.ExceptionHandlers.Single(handler => handler.HandlerType == ExceptionHandlerType.Catch);
var catchTail = write.Body.Instructions.Single(i => i.Offset == 0x87);
catchTail.OpCode = OpCodes.Rethrow; catchTail.Operand = null;
var writeIl = write.Body.GetILProcessor();
writeIl.Remove(catchTail.Next.Next); writeIl.Remove(catchTail.Next);

// Retain the image algorithm CIL; turn the old catch into resource cleanup on every exit.
if (save.Body.Variables.Count != 10 || save.Body.ExceptionHandlers.Count != 1) throw new InvalidOperationException("Texture-save shape differs.");
var handler = save.Body.ExceptionHandlers[0];
if (handler.HandlerType != ExceptionHandlerType.Catch) throw new InvalidOperationException("Texture-save handler differs.");
var saveIl = save.Body.GetILProcessor();
foreach (var instruction in save.Body.Instructions.Where(i => i.Offset >= 0x119 && i.Offset <= 0x126).ToArray())
{ instruction.OpCode = OpCodes.Nop; instruction.Operand = null; }
foreach (var instruction in save.Body.Instructions.Where(i => i.Operand is MethodReference m && m.Name == "WriteAllBytes").ToArray())
    saveIl.InsertAfter(instruction, saveIl.Create(OpCodes.Call, Safety("RequireWrite")));
Instruction end = handler.HandlerEnd;
foreach (var instruction in save.Body.Instructions.SkipWhile(i => i != handler.HandlerStart).TakeWhile(i => i != end).ToArray()) saveIl.Remove(instruction);
Instruction start = saveIl.Create(OpCodes.Ldloc_0);
saveIl.InsertBefore(end, start);
foreach (var instruction in new[] { saveIl.Create(OpCodes.Ldloc_1), saveIl.Create(OpCodes.Ldloc_2), saveIl.Create(OpCodes.Ldloc_3),
    saveIl.Create(OpCodes.Ldloc, save.Body.Variables[7]), saveIl.Create(OpCodes.Call, Safety("FinishTexture")), saveIl.Create(OpCodes.Endfinally) }) saveIl.InsertBefore(end, instruction);
handler.HandlerType = ExceptionHandlerType.Finally; handler.CatchType = null; handler.TryEnd = start; handler.HandlerStart = start;
// Acquire the previous target before entering the try so a failed getter cannot cause a false restore.
handler.TryStart = save.Body.Instructions.Single(i => i.Offset == 8);

// A conversion that throws before returning still owns its newly-created Texture2D.
if (convert.Body.ExceptionHandlers.Count != 0 || convert.Body.Variables[2].VariableType.FullName != "UnityEngine.Texture2D")
    throw new InvalidOperationException("Normal conversion shape differs.");
var ci = convert.Body.GetILProcessor();
Instruction conversionTry = convert.Body.Instructions.First();
Instruction conversionReturn = convert.Body.Instructions.Last();
var returned = new VariableDefinition(convert.ReturnType); convert.Body.Variables.Add(returned);
conversionReturn.OpCode = OpCodes.Stloc; conversionReturn.Operand = returned;
var returnLoad = ci.Create(OpCodes.Ldloc, returned);
ci.Append(ci.Create(OpCodes.Leave, returnLoad));
var faultStart = ci.Create(OpCodes.Ldloc_2); ci.Append(faultStart);
ci.Append(ci.Create(OpCodes.Call, Safety("DestroyOwned"))); ci.Append(ci.Create(OpCodes.Endfinally));
ci.Append(returnLoad); ci.Append(ci.Create(OpCodes.Ret));
convert.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Fault) { TryStart = conversionTry, TryEnd = faultStart, HandlerStart = faultStart, HandlerEnd = returnLoad });

// The common directory value feeds FBX and every texture; only its directory gains uniqueness.
var combine = export.Body.Instructions.First(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "System.IO.Path" && m.Name == "Combine");
if (combine.Next.OpCode != OpCodes.Stloc_2 || export.Body.Variables[2].VariableType.FullName != "System.String") throw new InvalidOperationException("Common export directory local differs.");
export.Body.GetILProcessor().InsertAfter(combine, Instruction.Create(OpCodes.Call, Safety("UniqueDirectory")));

// A texture failure occurs while the source renderer's weights are temporarily zero.
// Count completed captures before each mutation, then restore only that owned prefix.
var captured = new VariableDefinition(module.TypeSystem.Int32); skinned.Body.Variables.Add(captured);
var skinIl = skinned.Body.GetILProcessor();
var captureStore = skinned.Body.Instructions.Single(i => i.Offset == 0x1eb);
if (captureStore.OpCode != OpCodes.Stelem_R4 || skinned.Body.Variables[6].VariableType.FullName != "System.Single[]" ||
    skinned.Body.Variables[26].VariableType.FullName != "UnityEngine.Mesh") throw new InvalidOperationException("Skinned ownership shape differs.");
var current = captureStore;
foreach (var instruction in new[] { skinIl.Create(OpCodes.Ldloc, captured), skinIl.Create(OpCodes.Ldc_I4_1), skinIl.Create(OpCodes.Add), skinIl.Create(OpCodes.Stloc, captured) })
{ skinIl.InsertAfter(current, instruction); current = instruction; }
var skinTryStart = skinned.Body.Instructions.Single(i => i.Offset == 0xad);
var cleanupFirst = skinned.Body.Instructions.Single(i => i.Offset == 0x340);
var continuation = skinned.Body.Instructions.Single(i => i.Offset == 0x394);
var removed = skinned.Body.Instructions.Where(i => i.Offset > 0x340 && i.Offset < 0x394).ToArray();
if (skinned.Body.Instructions.Any(i => !removed.Contains(i) && i.Operand is Instruction jump && removed.Contains(jump)))
    throw new InvalidOperationException("Unexpected branch into old skinned cleanup.");
foreach (var i in removed) skinIl.Remove(i);
cleanupFirst.OpCode = OpCodes.Leave; cleanupFirst.Operand = continuation;
var binding = module.GetType("Exporter.UnityFbxExporterBinding");
var skinFinallyStart = skinIl.Create(OpCodes.Ldarg_0);
skinIl.InsertBefore(continuation, skinFinallyStart);
foreach (var i in new[] { skinIl.Create(OpCodes.Ldloc, skinned.Body.Variables[6]), skinIl.Create(OpCodes.Ldloc, captured),
    skinIl.Create(OpCodes.Ldloc, skinned.Body.Variables[26]),
    skinIl.Create(OpCodes.Ldsflda, binding.Fields.Single(f => f.Name == "blendSmr")),
    skinIl.Create(OpCodes.Ldsflda, binding.Fields.Single(f => f.Name == "blendMesh")),
    skinIl.Create(OpCodes.Ldsflda, binding.Fields.Single(f => f.Name == "blendWeights")),
    skinIl.Create(OpCodes.Call, Safety("FinishSkinned")), skinIl.Create(OpCodes.Endfinally) }) skinIl.InsertBefore(continuation, i);
// Nest this cleanup inside the pre-existing logging/rethrow handler.
skinned.Body.ExceptionHandlers.Insert(1, new ExceptionHandler(ExceptionHandlerType.Finally) {
    TryStart = skinTryStart, TryEnd = skinFinallyStart, HandlerStart = skinFinallyStart, HandlerEnd = continuation });
foreach (var method in new[] { skinned, meshExport })
{
    var process = method.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "ProcessMesh");
    method.Body.GetILProcessor().InsertAfter(process, Instruction.Create(OpCodes.Call, Safety("RequireMesh")));
}

foreach (var method in new[] { save, write, convert, export, skinned, meshExport }) Widen(method);
using var bytes = new MemoryStream(); assembly.Write(bytes);
using var source = AssemblyDefinition.ReadAssembly(input);
bytes.Position = 0; using var candidate = AssemblyDefinition.ReadAssembly(bytes);
var old = Types(source.MainModule.Types).SelectMany(type => type.Methods).ToDictionary(method => method.FullName);
var changed = Types(candidate.MainModule.Types).SelectMany(type => type.Methods)
    .Where(method => old.ContainsKey(method.FullName) && Shape(method) != Shape(old[method.FullName])).Select(method => method.FullName).Order().ToArray();
var expectedMethods = new[] { save.FullName, write.FullName, convert.FullName, export.FullName, skinned.FullName, meshExport.FullName }.Order().ToArray();
if (!changed.SequenceEqual(expectedMethods)) throw new InvalidOperationException("Unexpected changed methods.");
if (!source.MainModule.AssemblyReferences.Select(r => r.FullName).SequenceEqual(candidate.MainModule.AssemblyReferences.Select(r => r.FullName)))
    throw new InvalidOperationException("New runtime references introduced.");
string outputHash = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
if (outputHash != candidateSha) throw new InvalidOperationException("Output differs from reviewed candidate identity: " + outputHash);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
using (var stream = new FileStream(output, FileMode.CreateNew)) stream.Write(bytes.ToArray());
var receipt = new { inputSha256 = originalSha, outputSha256 = outputHash, changedMethods = changed,
    unchangedOriginalMethods = old.Count - changed.Length, addedMethods = safety.Methods.Select(method => method.FullName),
    referencesUnchanged = true, runtime = candidate.MainModule.RuntimeVersion,
    scope = "File truncation/failure propagation, texture and skinned-state ownership cleanup, zero-mesh refusal and common unique output directory. Image processing and FBX algorithms retained." };
File.WriteAllText(output + ".receipt.json", JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) { foreach (var t in types) { yield return t; foreach (var n in Types(t.NestedTypes)) yield return n; } }
static string Shape(MethodDefinition method) => !method.HasBody ? "none" :
    method.Body.InitLocals + "|" + string.Join("|", method.Body.Variables.Select(v => v.VariableType.FullName)) + "\n" +
    string.Join("\n", method.Body.Instructions.Select(i => i.OpCode.Code.ToString().Replace("_S", "") + " " +
    (i.Operand is Instruction target ? "target:" + method.Body.Instructions.IndexOf(target) : i.Operand is MemberReference r ? r.FullName : i.Operand))) +
    string.Join("\n", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":" + h.CatchType?.FullName + ":" +
        method.Body.Instructions.IndexOf(h.TryStart) + ":" + method.Body.Instructions.IndexOf(h.TryEnd) + ":" +
        method.Body.Instructions.IndexOf(h.HandlerStart) + ":" + method.Body.Instructions.IndexOf(h.HandlerEnd)));
static void Widen(MethodDefinition method)
{
    foreach (var i in method.Body.Instructions)
        if (i.OpCode.OperandType == OperandType.ShortInlineBrTarget)
            i.OpCode = i.OpCode.Code switch { Code.Br_S => OpCodes.Br, Code.Brfalse_S => OpCodes.Brfalse, Code.Brtrue_S => OpCodes.Brtrue,
                Code.Leave_S => OpCodes.Leave, Code.Beq_S => OpCodes.Beq, Code.Bne_Un_S => OpCodes.Bne_Un, Code.Blt_S => OpCodes.Blt,
                Code.Blt_Un_S => OpCodes.Blt_Un, Code.Bgt_S => OpCodes.Bgt, Code.Bgt_Un_S => OpCodes.Bgt_Un,
                Code.Ble_S => OpCodes.Ble, Code.Ble_Un_S => OpCodes.Ble_Un, Code.Bge_S => OpCodes.Bge, Code.Bge_Un_S => OpCodes.Bge_Un,
                _ => throw new InvalidOperationException("Unknown short branch") };
}
