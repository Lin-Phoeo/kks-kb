using Mono.Cecil;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("mode input.dll output.dll");
var mode = args[0];
var input = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (StringComparer.OrdinalIgnoreCase.Equals(input, output) || File.Exists(output))
    throw new InvalidOperationException("Output must be a new path distinct from input.");
var expectedInputHash = mode switch {
    "shallty" => "8D399B206AA812E4968AAEF6065F54F09C9FF11B8A6EEBEBAA0C297F9C5529C7",
    "sexfaces" => "8C3A9CED83022489AAE8A66D01A9D9307949B701E356321817831E4E2CB0E13E",
    "kls" => "7450C453A6AF8E2205543BFEE5429D843A7F384B1CC8716F65E06118D342EF3A",
    _ => throw new ArgumentException("Unknown mode")
};
var actualInputHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
if (!StringComparer.Ordinal.Equals(actualInputHash, expectedInputHash))
    throw new InvalidOperationException("Unsupported original DLL hash. No output was written.");
using var asm = AssemblyDefinition.ReadAssembly(input);
var changes = GuardPatches.Apply(asm, mode);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
asm.Write(output);
using var check = AssemblyDefinition.ReadAssembly(output);
var report = new { mode, input, output, inputSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))), outputSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))), changes };
File.WriteAllText(output+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));
