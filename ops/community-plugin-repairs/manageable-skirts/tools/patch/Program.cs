using Mono.Cecil;using Mono.Cecil.Cil;using System.Security.Cryptography;using System.Text.Json;
const string Expected="81f517809512b1d5a0971befa40a4c4124ea5fb3e7f0e80339944b56f96f652f";
string original=Path.GetFullPath(args[0]),helper=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
if(Hash(original)!=Expected)throw new InvalidOperationException("Unknown ManageableSkirts source SHA; refused");
if(string.Equals(original,output,StringComparison.OrdinalIgnoreCase)||File.Exists(output))throw new InvalidOperationException("Output must be new and distinct");
using var assembly=AssemblyDefinition.ReadAssembly(original);using var support=AssemblyDefinition.ReadAssembly(helper);
var plugin=assembly.MainModule.GetType("ManageableSkirts.ManageableSkirtsPlugin");var helperType=support.MainModule.GetType("Codex.ManageableSkirtsState.StateRepair");
var identity=plugin.CustomAttributes.Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");
if((string)identity.ConstructorArguments[0].Value!="com.ziglo.ManageableSkirts"||(string)identity.ConstructorArguments[2].Value!="1.1.0")throw new InvalidOperationException("Plugin identity differs");
var map=new Dictionary<string,string>{{"ActivateCharacter","Activate"},{"RestoreCharacter","Restore"},{"UpdateDepthIfActive","Tick"},{"UpdateDepthForChar","FromData"},{"RestoreAll","RestoreAll"},{"Update","Update"}};
foreach(var pair in map){var m=plugin.Methods.Single(m=>m.Name==pair.Key);var replacement=helperType.Methods.Single(m=>m.Name==pair.Value);if(replacement.Parameters.Count!=m.Parameters.Count+1)throw new InvalidOperationException("Hook shape differs");m.Body=new MethodBody(m);var il=m.Body.GetILProcessor();il.Append(il.Create(OpCodes.Ldarg_0));if(m.Parameters.Count==1)il.Append(il.Create(OpCodes.Ldarg_1));il.Append(il.Create(OpCodes.Call,TargetSignature(replacement, m)));il.Append(il.Create(OpCodes.Ret));}
MethodReference TargetSignature(MethodDefinition replacement, MethodDefinition target)
{
    var module=assembly.MainModule;
    var reference=new MethodReference(replacement.Name,module.TypeSystem.Void,module.ImportReference(helperType)){HasThis=false};
    reference.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
    if(target.Parameters.Count==1)
        reference.Parameters.Add(new ParameterDefinition(replacement.Parameters[1].ParameterType.FullName=="System.Object"?module.TypeSystem.Object:target.Parameters[0].ParameterType));
    return reference;
}
identity.ConstructorArguments[2]=new CustomAttributeArgument(assembly.MainModule.TypeSystem.String,"1.1.0.1");
string helperTarget=Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(helper));if(File.Exists(helperTarget)&&Hash(helperTarget)!=Hash(helper))throw new InvalidOperationException("Helper target exists and differs");
Directory.CreateDirectory(Path.GetDirectoryName(output));assembly.Write(output);if(!File.Exists(helperTarget))File.Copy(helper,helperTarget);
var result=new{schemaVersion=2,patchRevision=2,methodReferencePolicy="target-core-types",originalSha256=Expected,patchedSha256=Hash(output),helperSha256=Hash(helperTarget),guid="com.ziglo.ManageableSkirts",version="1.1.0.1",changedMethods=map.Keys.ToArray(),originalUnchanged=Hash(original)==Expected,installed=false};
File.WriteAllText(output+".manifest.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));
