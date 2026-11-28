using Mono.Cecil;using Mono.Cecil.Cil;using System.Security.Cryptography;using System.Text.Json;
const string expected="b1fa80dc3147b071daf90a91ae2bff4ff1216919ab168bd56f30b976cbc1c789";
const string candidate="e73fb5eebe2913817d89981e798953f1011deafb43e8bffa34e57d46db6d115b";
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
if(args.Length<3)throw new ArgumentException("patch input output | probe input fixture output");
var mode=args[0];var input=args[1];var output=args[^1];
if(File.Exists(output)||File.Exists(output+".receipt.json")||Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Output must be new and distinct");
using var a=AssemblyDefinition.ReadAssembly(input);var module=a.MainModule;var type=module.GetType("KK_PovX.Controller");
if(mode=="probe"){
 if(args.Length!=4)throw new ArgumentException("probe input fixture output");
 if(Hash(input)!=expected&&Hash(input)!=candidate)throw new InvalidOperationException("Unsupported probe input SHA-256");
 using var f=AssemblyDefinition.ReadAssembly(args[2]);var owner=f.MainModule.GetType(type.FullName);
 foreach(var name in new[]{"Update_Cursor","ToggleCursorLock"})Clone.Body(type.Methods.Single(m=>m.Name==name),owner.Methods.Single(m=>m.Name==name),f.MainModule);
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using var stream=new FileStream(output,FileMode.CreateNew);f.Write(stream);return;
}
if(mode!="patch"||args.Length!=3||Hash(input)!=expected)throw new InvalidOperationException("Unsupported original SHA-256 or mode");
string Shape(MethodDefinition m)=>!m.HasBody?"none":string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode+" "+(i.Operand is Instruction j?m.Body.Instructions.IndexOf(j):i.Operand)))+"\n"+string.Join("|",m.Body.Variables.Select(v=>v.VariableType.FullName));
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var c in Types(t.NestedTypes))yield return c;}}
var before=Types(module.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Shape);
var cursor=type.Methods.Single(m=>m.Name=="Update_Cursor");var il=cursor.Body.GetILProcessor();var first=cursor.Body.Instructions[0];
il.InsertBefore(first,il.Create(OpCodes.Call,type.Methods.Single(m=>m.Name=="get_PoVToggled")));
il.InsertBefore(first,il.Create(OpCodes.Brtrue,first));
il.InsertBefore(first,il.Create(OpCodes.Call,type.Methods.Single(m=>m.Name=="get_FreeRoamToggled")));
il.InsertBefore(first,il.Create(OpCodes.Brtrue,first));
il.InsertBefore(first,il.Create(OpCodes.Ret));
var changed=Types(module.Types).SelectMany(t=>t.Methods).Where(m=>before[m.FullName]!=Shape(m)).Select(m=>m.FullName).ToArray();
if(changed.Length!=1||changed[0]!=cursor.FullName)throw new InvalidOperationException("Unexpected method change");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var stream=new FileStream(output,FileMode.CreateNew))a.Write(stream);
File.WriteAllText(output+".receipt.json",JsonSerializer.Serialize(new{originalSha256=expected,candidateSha256=Hash(output),changedMethods=changed,originalMethods=before.Count,unchangedMethods=before.Count-1,installed=false,runtimeVerified=false},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(Hash(output));
