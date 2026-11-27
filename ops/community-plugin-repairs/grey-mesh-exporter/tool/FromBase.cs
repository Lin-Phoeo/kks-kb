using Mono.Cecil;using System.Security.Cryptography;using System.Text.Json;
internal static class FromBase
{
 internal static void Run(string input,string output){string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();if(hash!="530dc95e00cfb34486687a3de85cb995d4f6ae1cacc7169d58c07b97dacd15d7")throw new InvalidOperationException("Unknown lifecycle base");if(File.Exists(output))throw new InvalidOperationException("Output exists");using var a=AssemblyDefinition.ReadAssembly(input);var methods=GeneralPatch.Apply(a.MainModule);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));a.Write(output);Console.WriteLine(JsonSerializer.Serialize(new{baseSha256=hash,outputSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant(),changedMethods=methods.Select(m=>m.FullName).ToArray()}));}
}
