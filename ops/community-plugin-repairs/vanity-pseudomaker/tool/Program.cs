using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
if(args.Length==5&&args[0]=="--extract"){ProbeExtractor.Run(args[1],args[2],args[3],args[4]);return;}
if(args.Length!=5)throw new ArgumentException("VANITY_ORIGINAL PSEUDOMAKER_ORIGINAL HELPER NEW_OUTPUT GAME_ROOT");
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
string inputHash=Hash(args[0]);
var identities=new Dictionary<string,(string guid,string original,string patched,string output)>{
 ["69b355bf1018ab22e55e4ddfd146421b97c66542ad89ed9037c94f8eaa0ee19b"]=("com.freelancer604.wardrobe","1.11.7","1.11.7.1","2dc5cb77fff929814e912d42b5a38de85ad9e5cfd99ebd1a1103967b9ef1e24f"),
 ["0c05fc9066d39c039358bd79283510efff400ae8ac5b3519398ba34504347d6a"]=("com.freelancer604.makeup","1.0.3","1.0.3.1","6f4271ccb0d92b6745f1cdce91c415d87da224d5a81e758395a43d1538fdc222")};
if(!identities.TryGetValue(inputHash,out var identity)||Hash(args[1])!="72e1c3a3dcb2120225f2d2823729cea81b2a2dffc8243de63ac6a4c901061882")throw new InvalidOperationException("Unknown Vanity or PseudoMaker identity");
if(File.Exists(args[3])||File.Exists(args[3]+".receipt.json"))throw new InvalidOperationException("Output must be new");
using var resolver=new DefaultAssemblyResolver();foreach(string dir in new[]{"CharaStudio_Data/Managed","BepInEx/core","BepInEx/plugins"})resolver.AddSearchDirectory(Path.Combine(args[4],dir));
using var assembly=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters{AssemblyResolver=resolver});using var helper=AssemblyDefinition.ReadAssembly(args[2]);
var module=assembly.MainModule;var imported=Clone.Type(helper.MainModule.GetType("Codex.VanityCompatibility.PseudoMakerIdentity"),module);
var guard=module.GetType("Freelancer604.Wardrobe.WardrobePseudoMakerGuards");var method=guard.Methods.Single(m=>m.Name=="EnsureInstalled");
var literal=method.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldstr&&Equals(i.Operand,"1.5.1.1"));var compare=literal.Next;
if(compare.OpCode!=OpCodes.Call||compare.Operand is not MethodReference call||call.FullName!="System.Boolean System.String::op_Equality(System.String,System.String)"||method.Body.Variables[2].VariableType.FullName!="System.Reflection.Assembly")throw new InvalidOperationException("Unexpected exact version gate");
var il=method.Body.GetILProcessor();il.InsertBefore(compare,il.Create(OpCodes.Ldloc_2));compare.Operand=imported.Methods.Single(m=>m.Name=="Supports");
foreach(var instruction in method.Body.Instructions)if(instruction.OpCode.OperandType==OperandType.ShortInlineBrTarget)instruction.OpCode=instruction.OpCode.Code switch{
 Code.Br_S=>OpCodes.Br,Code.Brtrue_S=>OpCodes.Brtrue,Code.Brfalse_S=>OpCodes.Brfalse,Code.Bge_Un_S=>OpCodes.Bge_Un,Code.Blt_S=>OpCodes.Blt,Code.Leave_S=>OpCodes.Leave,_=>throw new InvalidOperationException("Unknown short branch")};
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
var plugin=All(module.Types).SelectMany(t=>t.CustomAttributes).Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");
if((string)plugin.ConstructorArguments[0].Value!=identity.guid||(string)plugin.ConstructorArguments[2].Value!=identity.original)throw new InvalidOperationException("Original plugin version differs");
plugin.ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,identity.patched);
using var bytes=new MemoryStream();assembly.Write(bytes);string outputHash=Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();if(outputHash!=identity.output)throw new InvalidOperationException("Candidate hash differs: "+identity.guid+" "+outputHash);
bytes.Position=0;using var reread=AssemblyDefinition.ReadAssembly(bytes);using var original=AssemblyDefinition.ReadAssembly(args[0]);
string Shape(MethodDefinition m)=>!m.HasBody?"none":string.Join("|",m.Body.Variables.Select(v=>v.VariableType.FullName))+string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Name+" "+(i.Operand is Instruction b?"#"+m.Body.Instructions.IndexOf(b):i.Operand is MemberReference r?r.FullName:i.Operand)));
var before=All(original.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);var changed=All(reread.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>before.TryGetValue(m.FullName,out var old)&&Shape(m)!=Shape(old)).Select(m=>m.FullName).ToArray();
if(changed.Length!=1||changed[0]!=method.FullName)throw new InvalidOperationException("Unexpected method scope");
if(!original.MainModule.AssemblyReferences.Select(r=>r.FullName).SequenceEqual(reread.MainModule.AssemblyReferences.Select(r=>r.FullName)))throw new InvalidOperationException("New runtime reference");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);using(var stream=new FileStream(args[3],FileMode.CreateNew))stream.Write(bytes.ToArray());
var receipt=new{inputHash,outputHash,guid=identity.guid,upstreamVersion=identity.original,localPatchVersion=identity.patched,changedMethods=changed,unchangedOriginalMethods=before.Count-1,referencesUnchanged=true,installed=false};
File.WriteAllText(args[3]+".receipt.json",JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(receipt));
