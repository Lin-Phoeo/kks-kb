using Mono.Cecil;using Mono.Cecil.Cil;using System.Security.Cryptography;using System.Text.Json;
const string Original="0ee625fec99a884aff322bb0e87b33d7105f531ccd88f99b4818711873edc0c7";
const string Candidate="703c6d58acf73d000ea0566dc78fd352e5f30d95b19980331b58d861e4700df5";
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
if(args.Length!=4)throw new ArgumentException("patch exact-input helper new-output | probe reviewed-input fixture new-output | audit original candidate game-root");
var mode=args[0];var input=args[1];var aux=args[2];var output=args[3];
if(mode=="audit"){
 if(Hash(input)!=Original||Hash(aux)!=Candidate)throw new InvalidOperationException("Audit requires exact reviewed files");
 using var before=AssemblyDefinition.ReadAssembly(input);var resolver=new DefaultAssemblyResolver();foreach(var p in new[]{Path.Combine(output,"CharaStudio_Data/Managed"),Path.Combine(output,"BepInEx/core"),Path.Combine(output,"BepInEx/plugins")}.Concat(Directory.GetDirectories(Path.Combine(output,"BepInEx/plugins"),"*",SearchOption.AllDirectories)))resolver.AddSearchDirectory(p);
 using var after=AssemblyDefinition.ReadAssembly(aux,new ReaderParameters{AssemblyResolver=resolver});
 IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var c in Types(t.NestedTypes))yield return c;}}
 string Shape(MethodDefinition m){if(!m.HasBody)return "none";string Op(object o)=>o is Instruction i?"#"+m.Body.Instructions.IndexOf(i):o is Instruction[] a?string.Join(",",a.Select(x=>m.Body.Instructions.IndexOf(x))):o?.ToString()??"";return string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Code.ToString().Replace("_S","")+" "+Op(i.Operand)))+"|"+string.Join(";",m.Body.Variables.Select(v=>v.VariableType.FullName));}
 var oldMethods=Types(before.MainModule.Types).SelectMany(t=>t.Methods).ToArray();var newMethods=Types(after.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);var changes=oldMethods.Where(m=>!newMethods.ContainsKey(m.FullName)||Shape(m)!=Shape(newMethods[m.FullName])).Select(m=>m.FullName).ToArray();
 if(changes.Length!=1||!changes[0].Contains("KeyframeService::UpdateChangedKeyframes"))throw new InvalidOperationException("Unexpected original method delta");
 var oldFields=Types(before.MainModule.Types).SelectMany(t=>t.Fields).Select(f=>f.FullName).ToArray();var newFields=Types(after.MainModule.Types).Where(t=>t.Namespace!="AutoKeyframeRepair").SelectMany(t=>t.Fields).Select(f=>f.FullName).ToArray();if(!oldFields.SequenceEqual(newFields))throw new InvalidOperationException("Original fields changed");
 byte[] Data(Resource r)=>((EmbeddedResource)r).GetResourceData();if(!before.MainModule.Resources.Select(r=>r.Name+":"+Convert.ToHexString(SHA256.HashData(Data(r)))).SequenceEqual(after.MainModule.Resources.Select(r=>r.Name+":"+Convert.ToHexString(SHA256.HashData(Data(r))))))throw new InvalidOperationException("Resources changed");
 var missing=new List<string>();int refs=0;foreach(var member in after.MainModule.GetMemberReferences()){if(member.DeclaringType.Scope.Name=="mscorlib"||member.DeclaringType.Scope.Name.StartsWith("System"))continue;refs++;try{if(member.Resolve()==null)missing.Add(member.FullName);}catch{missing.Add(member.FullName);}}
 if(missing.Count>0)throw new InvalidOperationException("Unresolved members: "+string.Join(";",missing));Console.WriteLine(JsonSerializer.Serialize(new{originalMethods=oldMethods.Length,changedMethods=changes,unchangedMethods=oldMethods.Length-1,originalFieldsUnchanged=true,resourcesUnchanged=true,resolvedMembers=refs,unresolvedMembers=missing}));return;
}
if(File.Exists(output)||File.Exists(output+".receipt.json")||Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Output must be new and distinct");
using var a=AssemblyDefinition.ReadAssembly(input);using var other=AssemblyDefinition.ReadAssembly(aux);
if(mode=="patch"){
 if(Hash(input)!=Original)throw new InvalidOperationException("Unknown input SHA");
 if(other.MainModule.Types.Any(t=>t.Namespace=="AutoKeyframeRepair"&&t.NestedTypes.Count>0))throw new InvalidOperationException("Unexpected helper structure");
 Embed.Add(other,a.MainModule,"AutoKeyframeRepair");
 var method=a.MainModule.GetType("AutoKeyframe.KeyframeService").Methods.Single(m=>m.Name=="UpdateChangedKeyframes");
 var call=method.Body.Instructions.Single(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="Timeline.Interpolable"&&r.Name=="GetValue");
 var pair=method.Body.Variables.Single(v=>v.VariableType.FullName=="System.Collections.Generic.KeyValuePair`2<Timeline.Interpolable,Timeline.Keyframe>");
 var getFrame=method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().First(r=>r.Name=="get_Value"&&r.DeclaringType.FullName==pair.VariableType.FullName);
 var il=method.Body.GetILProcessor();il.InsertBefore(call,il.Create(OpCodes.Ldloca,pair));il.InsertBefore(call,il.Create(OpCodes.Call,getFrame));
 call.OpCode=OpCodes.Call;call.Operand=a.MainModule.GetType("AutoKeyframeRepair.ValueAvailability").Methods.Single(m=>m.Name=="ReadOrKeep");
 foreach(var instruction in method.Body.Instructions){if(instruction.OpCode==OpCodes.Br_S)instruction.OpCode=OpCodes.Br;else if(instruction.OpCode==OpCodes.Brtrue_S)instruction.OpCode=OpCodes.Brtrue;else if(instruction.OpCode==OpCodes.Brfalse_S)instruction.OpCode=OpCodes.Brfalse;else if(instruction.OpCode==OpCodes.Leave_S)instruction.OpCode=OpCodes.Leave;}
 using var memory=new MemoryStream();a.Write(memory);var bytes=memory.ToArray();if(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=Candidate)throw new InvalidOperationException("Candidate identity differs; helper or writer contract changed");
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var stream=new FileStream(output,FileMode.CreateNew))stream.Write(bytes);
 var receipt=new{originalSha256=Original,candidateSha256=Hash(output),changedOriginalMethod="AutoKeyframe.KeyframeService.UpdateChangedKeyframes",installed=false};File.WriteAllText(output+".receipt.json",JsonSerializer.Serialize(receipt));Console.WriteLine(JsonSerializer.Serialize(receipt));
}else if(mode=="probe"){
 if(Hash(input)!=Original&&Hash(input)!=Candidate)throw new InvalidOperationException("Unknown probe input");
 var target=other.MainModule;var source=a.MainModule.GetType("AutoKeyframe.KeyframeService").Methods.Single(m=>m.Name=="UpdateChangedKeyframes");
 if(a.MainModule.Types.Any(t=>t.Namespace=="AutoKeyframeRepair"))Embed.Add(a,target,"AutoKeyframeRepair");
 Clone.Body(source,target.GetType("AutoKeyframe.KeyframeService").Methods.Single(m=>m.Name==source.Name),target);
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var stream=new FileStream(output,FileMode.CreateNew))other.Write(stream);
}else throw new ArgumentException("Unknown mode");
