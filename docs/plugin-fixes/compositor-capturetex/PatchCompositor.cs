using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchCompositor
{
    private const string ExpectedInput = "5b7f7b5f9a8659d309f849307c535a25bb18175fcdaed156f2168cd0bb346d1f";

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }

    private static object Operand(Instruction instruction, MethodDefinition method)
    {
        if (instruction.Operand == null) return null;
        if (instruction.Operand is Instruction target)
            return "target:" + method.Body.Instructions.IndexOf(target);
        if (instruction.Operand is Instruction[] targets)
            return targets.Select(t => method.Body.Instructions.IndexOf(t)).ToArray();
        if (instruction.Operand is VariableDefinition variable)
            return "local:" + variable.Index + ":" + variable.VariableType.FullName;
        if (instruction.Operand is ParameterDefinition parameter)
            return "param:" + parameter.Index + ":" + parameter.ParameterType.FullName;
        return instruction.Operand.ToString();
    }

    private static string Fingerprint(MethodDefinition method)
    {
        if (!method.HasBody) return "no-body";
        var data = new
        {
            method.FullName,
            method.Attributes,
            method.ImplAttributes,
            method.Body.InitLocals,
            Variables = method.Body.Variables.Select(v => v.VariableType.FullName).ToArray(),
            IL = method.Body.Instructions.Select(i => new { Op = i.OpCode.Name, Arg = Operand(i, method) }).ToArray(),
            EH = method.Body.ExceptionHandlers.Select(h => new
            {
                h.HandlerType,
                Catch = h.CatchType?.FullName,
                TryStart = method.Body.Instructions.IndexOf(h.TryStart),
                TryEnd = method.Body.Instructions.IndexOf(h.TryEnd),
                HandlerStart = method.Body.Instructions.IndexOf(h.HandlerStart),
                HandlerEnd = method.Body.Instructions.IndexOf(h.HandlerEnd),
                Filter = method.Body.Instructions.IndexOf(h.FilterStart)
            }).ToArray()
        };
        return Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
    }

    private static Dictionary<string, string> Methods(ModuleDefinition module) =>
        Types(module.Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Fingerprint);

    private static Dictionary<string, string> Resources(ModuleDefinition module) =>
        module.Resources.OfType<EmbeddedResource>().ToDictionary(r => r.Name, r => Hash(r.GetResourceData()));

    private static string NormalizePath(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) throw new ArgumentException("File paths must not be empty.");
        var path = Path.GetFullPath(argument);
        // A named stream could modify an existing Windows file without replacing its main contents.
        if (OperatingSystem.IsWindows() && path.Substring(Path.GetPathRoot(path).Length).Contains(':'))
            throw new ArgumentException("Alternate data stream paths are not supported.");
        return path;
    }

    private static void RequireNewFile(string path, string role)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new InvalidOperationException(role + " must not already exist.");
        if (!Directory.Exists(Path.GetDirectoryName(path)))
            throw new DirectoryNotFoundException(role + " parent directory must already exist.");
    }

    private static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("CompositorGuard failed: " + error.Message);
            Console.Error.WriteLine("No existing file is replaced. A late I/O failure may leave new partial output/report files; use fresh destinations for a retry.");
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        if (args.Length < 4)
            throw new ArgumentException("Usage: CompositorGuard input.dll output.dll report.json reference-directory [more-reference-directories]");
        var input = NormalizePath(args[0]);
        var output = NormalizePath(args[1]);
        var report = NormalizePath(args[2]);
        if (new[] { input, output, report }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidOperationException("Input, output and report must be three distinct file paths.");
        RequireNewFile(output, "Output");
        RequireNewFile(report, "Report");

        var inputBytes = File.ReadAllBytes(input);
        if (Hash(inputBytes) != ExpectedInput)
            throw new InvalidOperationException("Compositor identity does not match the supported build.");
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(input));
        foreach (var directory in args.Skip(3))
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Reference directory does not exist.");
            resolver.AddSearchDirectory(Path.GetFullPath(directory));
        }
        using var inputStream = new MemoryStream(inputBytes, writable: false);
        using var assembly = AssemblyDefinition.ReadAssembly(inputStream,
            new ReaderParameters { ReadWrite = false, ReadSymbols = false, AssemblyResolver = resolver });
        var module = assembly.MainModule;
        if (assembly.Name.HasPublicKey) throw new InvalidOperationException("Signed input is not supported.");
        var before = Methods(module);
        var resources = Resources(module);
        var references = module.AssemblyReferences.Select(r => r.FullName).ToArray();
        var entry = module.Types.Single(t => t.FullName == "DefaultNamespace.Entry");
        var method = entry.Methods.Single(m => m.Name == "TryPatchScreencap");
        var name = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "CaptureTex");
        var branch = name.Next.Next;
        if (branch.OpCode != OpCodes.Brtrue_S) throw new InvalidOperationException("Unexpected method layout.");
        var nextIteration = (Instruction)branch.Operand;
        var insertion = branch.Next;
        var getType = (MethodReference)method.Body.Instructions.Single(i =>
            i.Operand is MethodReference reference && reference.Name == "GetTypeFromHandle").Operand;
        var textureType = entry.Methods.Single(m => m.Name == "InterceptScreenshot").Parameters.Single().ParameterType;
        if (textureType.FullName != "UnityEngine.Texture2D") throw new InvalidOperationException("Unexpected postfix result type.");
        var methodLocal = method.Body.Variables.Single(v => v.VariableType.FullName == "System.Reflection.MethodInfo");
        var returnType = new MethodReference("get_ReturnType", getType.ReturnType, methodLocal.VariableType) { HasThis = true };
        var assignable = new MethodReference("IsAssignableFrom", module.TypeSystem.Boolean, getType.ReturnType) { HasThis = true };
        assignable.Parameters.Add(new ParameterDefinition(getType.ReturnType));
        var additions = new[]
        {
            Instruction.Create(OpCodes.Ldtoken, textureType),
            Instruction.Create(OpCodes.Call, getType),
            Instruction.Create(OpCodes.Ldloc, methodLocal),
            Instruction.Create(OpCodes.Callvirt, returnType),
            Instruction.Create(OpCodes.Callvirt, assignable),
            Instruction.Create(OpCodes.Brfalse, nextIteration)
        };
        foreach (var instruction in additions) method.Body.GetILProcessor().InsertBefore(insertion, instruction);

        using var candidateStream = new MemoryStream();
        assembly.Write(candidateStream, new WriterParameters { WriteSymbols = false });
        var candidateBytes = candidateStream.ToArray();
        using var verificationStream = new MemoryStream(candidateBytes, writable: false);
        using var reread = AssemblyDefinition.ReadAssembly(verificationStream);
        var after = Methods(reread.MainModule);
        if (before.Count != after.Count || before.Keys.Any(key => !after.ContainsKey(key)))
            throw new InvalidOperationException("Method set changed.");
        var changed = before.Keys.Where(key => before[key] != after[key]).ToArray();
        if (changed.Length != 1 || changed[0] != method.FullName)
            throw new InvalidOperationException("Unexpected methods changed.");
        if (!references.SequenceEqual(reread.MainModule.AssemblyReferences.Select(r => r.FullName)))
            throw new InvalidOperationException("Assembly references changed.");
        if (JsonSerializer.Serialize(resources) != JsonSerializer.Serialize(Resources(reread.MainModule)))
            throw new InvalidOperationException("Resources changed.");
        var result = new
        {
            status = "passed_static_candidate_only",
            input_name = Path.GetFileName(input),
            output_name = Path.GetFileName(output),
            input_sha256 = ExpectedInput,
            output_sha256 = Hash(candidateBytes),
            changed_methods = changed,
            unchanged_method_count = before.Count - 1,
            assembly_references_unchanged = true,
            embedded_resources_unchanged = true,
            resource_count = resources.Count,
            added_il_instructions = additions.Length,
            original_signature_unchanged = true,
            postfix_body_unchanged = true,
            gui_and_render_functions_unchanged = true,
            installed = false,
            runtime_verified = false
        };
        var reportBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        // CreateNew also protects against files appearing after the initial existence checks.
        using (var outputFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var reportFile = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            outputFile.Write(candidateBytes);
            reportFile.Write(reportBytes);
        }
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
