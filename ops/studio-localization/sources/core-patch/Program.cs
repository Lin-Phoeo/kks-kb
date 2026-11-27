using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

const string OriginalSha = "e5c95cb8f78bf208cbf5c552e437d4f51048d9bff6452394ad135e6ca75c8286";
const string HooksName = "XUnity.AutoTranslator.Plugin.Core.Hooks.IMGUIPluginTranslationHooks";
const string CallbackName = "XUnity.AutoTranslator.Plugin.Core.Hooks.PluginCallbacks_Function_Hook`1";
const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

if (args.Length < 3) throw new ArgumentException("patch ORIGINAL OUTDIR | probe CORE REPORT shared|separate GAME");
if (args[0] == "patch") Patch(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
else if (args[0] == "probe" && args.Length == 5) Probe(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), args[3], Path.GetFullPath(args[4]));
else throw new ArgumentException("Unknown mode or argument count");

string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
string FileSha(string path) => Sha(File.ReadAllBytes(path));
string Constant(object value) => value switch
{
    float number => "float-bits:" + BitConverter.SingleToInt32Bits(number).ToString("X8"),
    double number => "double-bits:" + BitConverter.DoubleToInt64Bits(number).ToString("X16"),
    _ => JsonSerializer.Serialize(value)
};
void Save(string path, object value) =>
    File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));

IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
    }
}

MethodDefinition Selector(AssemblyDefinition assembly) =>
    assembly.MainModule.GetType(HooksName).Methods.Single(method =>
        method.Name == "HookIfConfigured" && method.ReturnType.FullName == "System.Void" &&
        method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Reflection.MethodInfo");

int SelectorIndex(MethodDefinition method)
{
    var stores = method.Body.Instructions.Where(instruction =>
        instruction.OpCode.Code == Code.Stloc_S && instruction.Operand is VariableDefinition variable &&
        variable.Index == 4 && variable.VariableType.FullName == "System.Type").ToArray();
    if (stores.Length != 1) throw new InvalidOperationException("Expected one assignment to selected generic Type local 4");
    int index = method.Body.Instructions.IndexOf(stores[0]) - 1;
    if (method.Body.Instructions[index - 1].OpCode.Code != Code.Ldloc_3)
        throw new InvalidOperationException("Expected selected MonoBehaviour Type local 3");
    return index;
}

string Operand(MethodDefinition method, object operand) => operand switch
{
    null => "",
    Instruction target => "instruction:" + method.Body.Instructions.IndexOf(target),
    Instruction[] targets => string.Join(",", targets.Select(target => "instruction:" + method.Body.Instructions.IndexOf(target))),
    VariableDefinition variable => "local:" + variable.Index + ":" + variable.VariableType.FullName,
    ParameterDefinition parameter => "parameter:" + parameter.Index + ":" + parameter.ParameterType.FullName,
    MemberReference member => member.FullName,
    CallSite call => call.FullName,
    _ => Constant(operand)
};

string[] Body(MethodDefinition method)
{
    if (!method.HasBody) return Array.Empty<string>();
    var result = method.Body.Instructions.Select(instruction => instruction.OpCode.Code + " " + Operand(method, instruction.Operand)).ToList();
    result.Add("INITLOCALS " + method.Body.InitLocals);
    result.Add("LOCALS " + string.Join("|", method.Body.Variables.Select(variable => variable.VariableType.FullName)));
    foreach (var handler in method.Body.ExceptionHandlers)
        result.Add("EH " + handler.HandlerType + "|" + handler.CatchType?.FullName + "|" +
            Operand(method, handler.TryStart) + "|" + Operand(method, handler.TryEnd) + "|" +
            Operand(method, handler.HandlerStart) + "|" + Operand(method, handler.HandlerEnd) + "|" + Operand(method, handler.FilterStart));
    return result.ToArray();
}

string Attribute(CustomAttribute attribute) => attribute.Constructor.FullName + ":" + Convert.ToHexString(attribute.GetBlob());
string Attributes(Mono.Cecil.ICustomAttributeProvider provider) => string.Join("|", provider.CustomAttributes.Select(Attribute));

string[] Metadata(AssemblyDefinition assembly)
{
    var rows = new List<string> {
        "ASSEMBLY " + assembly.Name.FullName + "|" + assembly.Name.Attributes + "|" + Attributes(assembly),
        "MODULE " + assembly.MainModule.Name + "|" + assembly.MainModule.Kind + "|" + assembly.MainModule.RuntimeVersion +
            "|" + assembly.MainModule.Architecture + "|" + assembly.MainModule.Attributes + "|" + assembly.MainModule.Mvid +
            "|" + Attributes(assembly.MainModule)
    };
    foreach (var reference in assembly.MainModule.AssemblyReferences)
        rows.Add("REFERENCE " + reference.FullName + "|" + reference.Attributes);
    foreach (var reference in assembly.MainModule.ModuleReferences) rows.Add("MODREF " + reference.Name);
    foreach (var type in AllTypes(assembly.MainModule.Types))
    {
        rows.Add("TYPE " + type.FullName + "|" + type.Attributes + "|" + type.BaseType?.FullName + "|" + type.PackingSize +
            "|" + type.ClassSize + "|" + Attributes(type));
        foreach (var item in type.Interfaces) rows.Add("INTERFACE " + type.FullName + "|" + item.InterfaceType.FullName + "|" + Attributes(item));
        foreach (var field in type.Fields)
            rows.Add("FIELD " + field.FullName + "|" + field.Attributes + "|" + Constant(field.Constant) +
                "|" + Sha(field.InitialValue) + "|" + Attributes(field));
        foreach (var method in type.Methods)
        {
            rows.Add("METHOD " + method.FullName + "|" + method.Attributes + "|" + method.ImplAttributes + "|" +
                method.CallingConvention + "|" + Attributes(method));
            foreach (var parameter in method.Parameters)
                rows.Add("PARAM " + method.FullName + "|" + parameter.Index + "|" + parameter.Name + "|" + parameter.Attributes +
                    "|" + Constant(parameter.Constant) + "|" + Attributes(parameter));
            foreach (var generic in method.GenericParameters)
                rows.Add("GENERIC " + method.FullName + "|" + generic.Name + "|" + generic.Attributes + "|" +
                    string.Join(",", generic.Constraints.Select(constraint => constraint.FullName)) + "|" + Attributes(generic));
        }
        foreach (var property in type.Properties)
            rows.Add("PROPERTY " + property.FullName + "|" + property.Attributes + "|" + property.GetMethod?.FullName +
                "|" + property.SetMethod?.FullName + "|" + Attributes(property));
        foreach (var item in type.Events)
            rows.Add("EVENT " + item.FullName + "|" + item.Attributes + "|" + item.AddMethod?.FullName + "|" +
                item.RemoveMethod?.FullName + "|" + Attributes(item));
    }
    return rows.ToArray();
}

object[] Resources(AssemblyDefinition assembly) => assembly.MainModule.Resources.Select(resource =>
{
    if (resource is not EmbeddedResource embedded) throw new InvalidOperationException("Unexpected linked resource");
    var bytes = embedded.GetResourceData();
    return (object)new { name = resource.Name, attributes = resource.Attributes.ToString(), bytes = bytes.Length, sha256 = Sha(bytes) };
}).ToArray();

bool HasOfficialWrapperFix(AssemblyDefinition assembly)
{
    var wrapper = assembly.MainModule.GetType("XUnity.AutoTranslator.Plugin.Core.Hooks.IMGUIWindowFunctions");
    if (wrapper == null) return false;
    var overload = assembly.MainModule.GetType(HooksName).Methods.Single(method =>
        method.Name == "HookIfConfigured" && method.Parameters[0].ParameterType.FullName == "UnityEngine.GUI/WindowFunction");
    return overload.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference &&
        reference.DeclaringType.FullName == wrapper.FullName && reference.Name == "GetPluginFunction");
}

void Patch(string original, string outputDirectory)
{
    if (FileSha(original) != OriginalSha) throw new InvalidOperationException("Original SHA-256 mismatch");
    Directory.CreateDirectory(outputDirectory);
    string candidate = Path.Combine(outputDirectory, "XUnity.AutoTranslator.Plugin.Core.dll");
    if (File.Exists(candidate)) throw new InvalidOperationException("Candidate already exists");
    using var assembly = AssemblyDefinition.ReadAssembly(original);
    if (assembly.Name.HasPublicKey) throw new InvalidOperationException("Refusing to invalidate a signed assembly");
    var selector = Selector(assembly);
    int index = SelectorIndex(selector);
    var instruction = selector.Body.Instructions[index];
    if (instruction.OpCode.Code != Code.Callvirt || instruction.Operand is not MethodReference reference ||
        reference.FullName != "System.Type System.Type::GetType()")
        throw new InvalidOperationException("Selector signature differs from reviewed input");
    if (selector.Body.Instructions.Count(item => item.Operand is MethodReference call &&
        call.FullName == "System.Type System.Type::GetType()") != 1)
        throw new InvalidOperationException("Unexpected duplicate selector call");
    if (!HasOfficialWrapperFix(assembly)) throw new InvalidOperationException("Official Unity window wrapper fix missing");
    string oldInstruction = Body(selector)[index];
    int originalOffset = instruction.Offset;
    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Nop;
    instruction.Operand = null;
    assembly.Write(candidate);

    using var before = AssemblyDefinition.ReadAssembly(original);
    using var after = AssemblyDefinition.ReadAssembly(candidate);
    var beforeMethods = AllTypes(before.MainModule.Types).SelectMany(type => type.Methods).ToDictionary(method => method.FullName);
    var afterMethods = AllTypes(after.MainModule.Types).SelectMany(type => type.Methods).ToDictionary(method => method.FullName);
    if (!beforeMethods.Keys.SequenceEqual(afterMethods.Keys)) throw new InvalidOperationException("Method identity/order changed");
    var differences = new List<object>();
    foreach (var entry in beforeMethods)
    {
        string[] oldBody = Body(entry.Value), newBody = Body(afterMethods[entry.Key]);
        if (oldBody.Length != newBody.Length) throw new InvalidOperationException("Instruction/local/EH count changed");
        for (int position = 0; position < oldBody.Length; position++)
            if (oldBody[position] != newBody[position])
                differences.Add(new { method = entry.Key, index = position, before = oldBody[position], after = newBody[position] });
    }
    if (differences.Count != 1 || Body(Selector(after))[index] != "Nop ")
        throw new InvalidOperationException("Expected exactly one normalized instruction difference");
    string[] oldMetadata = Metadata(before), newMetadata = Metadata(after);
    if (!oldMetadata.SequenceEqual(newMetadata)) throw new InvalidOperationException("Metadata, references or signatures changed");
    object[] oldResources = Resources(before), newResources = Resources(after);
    if (JsonSerializer.Serialize(oldResources) != JsonSerializer.Serialize(newResources))
        throw new InvalidOperationException("Resource content changed");
    Save(Path.Combine(outputDirectory, "patch-report.json"), new {
        status = "isolated_candidate_verified_not_installed",
        original, original_sha256 = OriginalSha, candidate, candidate_sha256 = FileSha(candidate),
        target_method = selector.FullName, original_il_offset = "0x" + originalOffset.ToString("x4"),
        instruction_index = index, old_instruction = oldInstruction, new_instruction = "Nop",
        normalized_differences = differences, method_count = beforeMethods.Count,
        metadata_rows_equal = oldMetadata.Length, resources = newResources,
        assembly_references_unchanged = true, locals_and_exception_handlers_unchanged = true,
        official_window_wrapper_fix_preserved = HasOfficialWrapperFix(after),
        limitation = "Managed IL and metadata verification only; Unity and Mono runtime/UI validation remains separate."
    });
    Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", candidate, sha256 = FileSha(candidate), changed_instructions = differences.Count }));
}

void Probe(string corePath, string reportPath, string expectation, string game)
{
    if (expectation != "shared" && expectation != "separate") throw new ArgumentException("Unknown probe expectation");
    string[] search = { Path.GetDirectoryName(corePath), Path.Combine(game, "BepInEx/core"), Path.Combine(game, "CharaStudio_Data/Managed") };
    AssemblyLoadContext.Default.Resolving += (context, name) =>
    {
        foreach (string folder in search)
        {
            string path = Path.Combine(folder, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
        }
        return null;
    };
    var core = AssemblyLoadContext.Default.LoadFromAssemblyPath(corePath);
    using var metadata = AssemblyDefinition.ReadAssembly(corePath);
    var selector = Selector(metadata);
    var choice = selector.Body.Instructions[SelectorIndex(selector)];
    var selectionMethod = new DynamicMethod("ActualSelectorInstruction", typeof(Type), new[] { typeof(Type) }, typeof(Program).Module, true);
    var emit = selectionMethod.GetILGenerator();
    emit.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
    if (choice.OpCode.Code == Code.Callvirt)
    {
        var reference = (MethodReference)choice.Operand;
        var target = (MethodInfo)core.ManifestModule.ResolveMethod(reference.MetadataToken.ToInt32());
        emit.Emit(System.Reflection.Emit.OpCodes.Callvirt, target);
    }
    else if (choice.OpCode.Code == Code.Nop) emit.Emit(System.Reflection.Emit.OpCodes.Nop);
    else throw new InvalidOperationException("Unrecognized selector instruction");
    emit.Emit(System.Reflection.Emit.OpCodes.Ret);
    var chooseType = (Func<Type, Type>)selectionMethod.CreateDelegate(typeof(Func<Type, Type>));
    var generic = core.GetType(CallbackName, true);
    var cacheType = core.GetType("XUnity.AutoTranslator.Plugin.Core.TextTranslationCache", true);
    var callOrigin = core.GetType("XUnity.AutoTranslator.Plugin.Core.CallOrigin", true).GetField("TextCache", Flags);
    object cacheA = FormatterServices.GetUninitializedObject(cacheType), cacheB = FormatterServices.GetUninitializedObject(cacheType);
    Type markerA = typeof(StringBuilder), markerB = typeof(MemoryStream);
    Type selectedA = chooseType(markerA), selectedB = chooseType(markerB);
    Type hookA = generic.MakeGenericType(selectedA), hookB = generic.MakeGenericType(selectedB);
    hookA.GetMethod("SetTextCache", Flags).Invoke(null, new[] { cacheA });
    hookB.GetMethod("SetTextCache", Flags).Invoke(null, new[] { cacheB });
    hookA.GetMethod("Prefix", Flags).Invoke(null, null);
    bool firstCorrect = ReferenceEquals(callOrigin.GetValue(null), cacheA);
    bool firstOverwritten = ReferenceEquals(callOrigin.GetValue(null), cacheB);
    hookA.GetMethod("Finalizer", Flags).Invoke(null, null);
    bool firstCleared = callOrigin.GetValue(null) == null;
    hookB.GetMethod("Prefix", Flags).Invoke(null, null);
    bool secondCorrect = ReferenceEquals(callOrigin.GetValue(null), cacheB);
    hookB.GetMethod("Finalizer", Flags).Invoke(null, null);
    bool secondCleared = callOrigin.GetValue(null) == null;
    var targetMethod = typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes);
    hookA.GetMethod("Register", Flags).Invoke(null, new object[] { targetMethod });
    bool registerWorks = Equals(hookA.GetMethod("TargetMethod", Flags).Invoke(null, new object[] { null }), targetMethod);
    hookA.GetMethod("Clean", Flags).Invoke(null, null);
    bool cleanPreservesCache = hookA.GetField("_nextMethod", Flags).GetValue(null) == null &&
        ReferenceEquals(hookA.GetField("_cache", Flags).GetValue(null), expectation == "separate" ? cacheA : cacheB);
    bool passed = secondCorrect && firstCleared && secondCleared && registerWorks && cleanPreservesCache &&
        (expectation == "separate" ? hookA != hookB && firstCorrect : hookA == hookB && !firstCorrect && firstOverwritten);
    var report = new {
        passed, expectation, core_sha256 = FileSha(corePath), runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        selector_instruction = choice.OpCode.Code.ToString(), selected_type_a = selectedA.AssemblyQualifiedName,
        selected_type_b = selectedB.AssemblyQualifiedName, closed_hook_types_equal = hookA == hookB,
        first_prefix_correct = firstCorrect, first_prefix_overwritten_by_second = firstOverwritten,
        second_prefix_correct = secondCorrect, finalizers_clear = firstCleared && secondCleared,
        register_works = registerWorks, clean_preserves_cache = cleanPreservesCache,
        official_window_wrapper_fix_present = HasOfficialWrapperFix(metadata),
        native_unity_api_invoked = false, live_install_writes = false,
        limitation = "The selector instruction is copied from this exact DLL into a managed probe. Actual callback methods are invoked directly, without Harmony installation or Unity rendering."
    };
    Save(reportPath, report);
    Console.WriteLine(JsonSerializer.Serialize(report));
    if (!passed) throw new InvalidOperationException("Regression probe failed");
}
