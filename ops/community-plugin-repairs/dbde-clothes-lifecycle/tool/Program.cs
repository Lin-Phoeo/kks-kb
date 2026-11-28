using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
const string Original="baa0fcc5c5658182385eb59694e34f5264fd68d87a2885b1ec91f15ee1d98f19";
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
IEnumerable<TypeDefinition> All(TypeDefinition t){yield return t;foreach(var n in t.NestedTypes)foreach(var x in All(n))yield return x;}
void NewOutput(string output,string input){
 output=Path.GetFullPath(output);input=Path.GetFullPath(input);
 if(output.Equals(input,StringComparison.OrdinalIgnoreCase)||File.Exists(output)||Directory.Exists(output)||File.Exists(output+".receipt.json"))throw new InvalidOperationException("A new distinct output is required");
 for(var d=new DirectoryInfo(Path.GetDirectoryName(output)!);d!=null;d=d.Parent){
  if(new[]{"BepInEx","UserData","mods","abdata"}.Contains(d.Name,StringComparer.OrdinalIgnoreCase)||d.Name.EndsWith("_Data",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Runtime/content output denied");
  if(File.Exists(Path.Combine(d.FullName,".git"))||Directory.Exists(Path.Combine(d.FullName,".git")))throw new InvalidOperationException("Repository output denied");
  if(File.Exists(Path.Combine(d.FullName,"CharaStudio.exe"))&&Path.GetRelativePath(d.FullName,output).Split(Path.DirectorySeparatorChar)[0]!=".scratch")throw new InvalidOperationException("Game output must be private scratch");
 }
}
void Write(AssemblyDefinition a,string path){using var buffer=new MemoryStream();a.Write(buffer);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);using var s=new FileStream(path,FileMode.CreateNew);buffer.Position=0;buffer.CopyTo(s);}
void LongBranches(MethodDefinition m){foreach(var i in m.Body.Instructions){i.OpCode=i.OpCode.Code switch{Code.Br_S=>OpCodes.Br,Code.Brfalse_S=>OpCodes.Brfalse,Code.Brtrue_S=>OpCodes.Brtrue,Code.Leave_S=>OpCodes.Leave,_=>i.OpCode};}}
string Fingerprint(MethodDefinition m){if(!m.HasBody)return "no-body";var ins=m.Body.Instructions;string Op(object o)=>o switch{Instruction i=>"@"+ins.IndexOf(i),Instruction[] aa=>string.Join(",",aa.Select(i=>"@"+ins.IndexOf(i))),_=>o?.ToString()??""};return string.Join("\n",ins.Select(i=>i.OpCode.Name+":"+Op(i.Operand)))+"\nV="+string.Join(",",m.Body.Variables.Select(v=>v.VariableType.FullName))+"\nEH="+string.Join(";",m.Body.ExceptionHandlers.Select(e=>$"{e.HandlerType}:{ins.IndexOf(e.TryStart)}:{ins.IndexOf(e.TryEnd)}:{ins.IndexOf(e.HandlerStart)}:{ins.IndexOf(e.HandlerEnd)}"));}
string Contract(AssemblyDefinition assembly){var mm=assembly.MainModule;return string.Join("\n",new[]{assembly.Name.FullName}.Concat(mm.AssemblyReferences.Select(r=>r.FullName)).Concat(mm.Types.SelectMany(All).SelectMany(t=>new[]{t.FullName+":"+t.Attributes+":"+t.BaseType?.FullName}.Concat(t.Fields.Select(f=>f.FullName+":"+f.Attributes+":"+(f.HasConstant?f.Constant:""))).Concat(t.CustomAttributes.Select(x=>x.AttributeType.FullName+":"+string.Join(",",x.ConstructorArguments.Select(c=>c.Value)))))).Concat(mm.Resources.Select(r=>r.Name+":"+r.Attributes+":"+(r is EmbeddedResource er?Convert.ToHexString(SHA256.HashData(er.GetResourceData())):r.ResourceType.ToString()))));}
if(args.Length!=4||args[0]!="patch"&&args[0]!="probe")throw new ArgumentException("patch original game-root new-output | probe reviewed-input synthetic-fixture new-output");
string mode=args[0],input=args[1],fixture=args[2],output=args[3];NewOutput(output,input);
string inputHash=Hash(input);
if(mode=="patch"&&inputHash!=Original)throw new InvalidOperationException("Unsupported original SHA-256");
using var resolver=new DefaultAssemblyResolver();
if(mode=="patch")foreach(var path in new[]{Path.Combine(fixture,"BepInEx/core"),Path.Combine(fixture,"CharaStudio_Data/Managed"),Path.Combine(fixture,"BepInEx/plugins")}.Concat(Directory.GetDirectories(Path.Combine(fixture,"BepInEx/plugins"),"*",SearchOption.AllDirectories)))resolver.AddSearchDirectory(path);
using var a=AssemblyDefinition.ReadAssembly(input,new ReaderParameters{AssemblyResolver=resolver});var module=a.MainModule;
var owner=module.GetType("DynamicBoneDistributionEditor.DBDECharaController");
var main=owner.Methods.Single(m=>m.Name=="ClothesChanged");
var lambda=owner.NestedTypes.SelectMany(t=>t.Methods).Single(m=>m.Name=="<ClothesChanged>b__0");
if(mode=="probe"){
 if(inputHash!=Original){
  string receipt=input+".receipt.json";if(!File.Exists(receipt))throw new InvalidOperationException("Reviewed patch receipt required");
  using var r=JsonDocument.Parse(File.ReadAllText(receipt));if(r.RootElement.GetProperty("originalSha256").GetString()!=Original||r.RootElement.GetProperty("outputSha256").GetString()!=inputHash)throw new InvalidOperationException("Patch receipt mismatch");
 }
 using var f=AssemblyDefinition.ReadAssembly(fixture);var dest=f.MainModule.GetType(owner.FullName);
 var nested=lambda.DeclaringType;var copy=new TypeDefinition(nested.Namespace,nested.Name,nested.Attributes,Clone.TypeRef(nested.BaseType,f.MainModule));dest.NestedTypes.Add(copy);
 foreach(var field in nested.Fields)copy.Fields.Add(new FieldDefinition(field.Name,field.Attributes,Clone.TypeRef(field.FieldType,f.MainModule)));
 foreach(var method in nested.Methods){var m=new MethodDefinition(method.Name,method.Attributes,Clone.TypeRef(method.ReturnType,f.MainModule));foreach(var p in method.Parameters)m.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,Clone.TypeRef(p.ParameterType,f.MainModule)));copy.Methods.Add(m);}
 foreach(var method in nested.Methods)Clone.Body(method,copy.Methods.Single(m=>m.Name==method.Name),f.MainModule);
 Clone.Body(main,dest.Methods.Single(m=>m.Name==main.Name),f.MainModule);Write(f,output);
 Console.WriteLine(JsonSerializer.Serialize(new{inputHash,actualCilMethods=1+nested.Methods.Count,syntheticCollaborators=true}));return;
}
var all=module.Types.SelectMany(All).SelectMany(t=>t.Methods).ToArray();var before=all.ToDictionary(m=>m.FullName,Fingerprint);var metadataBefore=Contract(a);
var uiField=(FieldReference)main.Body.Instructions.Single(i=>i.Offset==9).Operand;
var updateUi=(FieldReference)main.Body.Instructions.Single(i=>i.Offset==15).Operand;
var unityBool=(MethodReference)main.Body.Instructions.Single(i=>i.Operand is MethodReference mr&&mr.Name=="op_Implicit").Operand;
var editField=(FieldReference)lambda.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldfld&&((FieldReference)i.Operand).Name=="edit").Operand;
var rootField=(FieldReference)lambda.Body.Instructions.First(i=>i.OpCode==OpCodes.Ldfld&&((FieldReference)i.Operand).Name=="m_Root").Operand;
var primary=(MethodReference)lambda.Body.Instructions.Single(i=>i.Operand is MethodReference mr&&mr.Name=="get_PrimaryDynamicBone").Operand;
var nameGetter=(MethodReference)lambda.Body.Instructions.First(i=>i.Operand is MethodReference mr&&mr.Name=="get_name").Operand;
var equals=(MethodReference)lambda.Body.Instructions.Single(i=>i.Operand is MethodReference mr&&mr.Name=="op_Equality").Operand;
if(main.Body.Instructions.Count!=79||lambda.Body.Instructions.Count!=10||main.Body.ExceptionHandlers.Count!=1)throw new InvalidOperationException("Unexpected exact-input method shape");
// Preserve name equality and First/Find ordering; only invalid Unity objects become nonmatches.
lambda.Body=new MethodBody(lambda){InitLocals=true,MaxStackSize=3};var primaryLocal=new VariableDefinition(primary.ReturnType);lambda.Body.Variables.Add(primaryLocal);var li=lambda.Body.GetILProcessor();var no=li.Create(OpCodes.Ldc_I4_0);
void L(Instruction i)=>li.Append(i);
L(li.Create(OpCodes.Ldarg_1));L(li.Create(OpCodes.Call,unityBool));L(li.Create(OpCodes.Brfalse,no));
L(li.Create(OpCodes.Ldarg_1));L(li.Create(OpCodes.Ldfld,rootField));L(li.Create(OpCodes.Call,unityBool));L(li.Create(OpCodes.Brfalse,no));
L(li.Create(OpCodes.Ldarg_0));L(li.Create(OpCodes.Ldfld,editField));L(li.Create(OpCodes.Brfalse,no));
L(li.Create(OpCodes.Ldarg_0));L(li.Create(OpCodes.Ldfld,editField));L(li.Create(OpCodes.Callvirt,primary));L(li.Create(OpCodes.Stloc,primaryLocal));
L(li.Create(OpCodes.Ldloc,primaryLocal));L(li.Create(OpCodes.Call,unityBool));L(li.Create(OpCodes.Brfalse,no));
L(li.Create(OpCodes.Ldloc,primaryLocal));L(li.Create(OpCodes.Ldfld,rootField));L(li.Create(OpCodes.Call,unityBool));L(li.Create(OpCodes.Brfalse,no));
L(li.Create(OpCodes.Ldarg_1));L(li.Create(OpCodes.Ldfld,rootField));L(li.Create(OpCodes.Callvirt,nameGetter));
L(li.Create(OpCodes.Ldloc,primaryLocal));L(li.Create(OpCodes.Ldfld,rootField));L(li.Create(OpCodes.Callvirt,nameGetter));L(li.Create(OpCodes.Call,equals));L(li.Create(OpCodes.Ret));L(no);L(li.Create(OpCodes.Ret));
var body=main.Body;var il=body.GetILProcessor();LongBranches(main);
var entry=body.Instructions[0];var disable=body.Instructions.Single(i=>i.Offset==9);var firstWork=body.Instructions.Single(i=>i.Offset==20);
var finish=body.Instructions.Single(i=>i.Offset==245);var lastRet=body.Instructions.Last();var loopNext=body.Instructions.Single(i=>i.Offset==220);
var firstFind=body.Instructions.Single(i=>i.Offset==158);var closure=body.Variables[2];
var uiLocal=new VariableDefinition(uiField.FieldType);var previousUi=new VariableDefinition(module.TypeSystem.Boolean);body.Variables.Add(uiLocal);body.Variables.Add(previousUi);body.InitLocals=true;
foreach(var i in new[]{il.Create(OpCodes.Ldarg_1),il.Create(OpCodes.Call,unityBool),il.Create(OpCodes.Brtrue,entry),il.Create(OpCodes.Ret)})il.InsertBefore(entry,i);
// Retain the original block entry target, replacing just its UI acquire instructions.
disable.OpCode=OpCodes.Nop;disable.Operand=null;var oldZero=disable.Next;var oldStore=oldZero.Next;oldZero.OpCode=OpCodes.Nop;oldZero.Operand=null;oldStore.OpCode=OpCodes.Nop;oldStore.Operand=null;
var prepareDone=il.Create(OpCodes.Nop);
foreach(var i in new[]{il.Create(OpCodes.Ldsfld,uiField),il.Create(OpCodes.Stloc,uiLocal),il.Create(OpCodes.Ldloc,uiLocal),il.Create(OpCodes.Call,unityBool),il.Create(OpCodes.Brfalse,prepareDone),il.Create(OpCodes.Ldloc,uiLocal),il.Create(OpCodes.Ldfld,updateUi),il.Create(OpCodes.Stloc,previousUi),il.Create(OpCodes.Ldloc,uiLocal),il.Create(OpCodes.Ldc_I4_0),il.Create(OpCodes.Stfld,updateUi),prepareDone})il.InsertBefore(firstWork,i);
// A missing saved edit is left in its container for the author recovery paths.
foreach(var i in new[]{il.Create(OpCodes.Ldloc,closure),il.Create(OpCodes.Ldfld,editField),il.Create(OpCodes.Brfalse,loopNext)})il.InsertBefore(firstFind,i);
// Existing loop disposal remains nested; the outer finally restores only the captured live UI.
finish.OpCode=OpCodes.Leave;finish.Operand=lastRet;var oldTrue=finish.Next;var oldEndStore=oldTrue.Next;body.Instructions.Remove(oldTrue);body.Instructions.Remove(oldEndStore);
var finallyStart=il.Create(OpCodes.Ldloc,uiLocal);var endFinally=il.Create(OpCodes.Endfinally);
foreach(var i in new[]{finallyStart,il.Create(OpCodes.Call,unityBool),il.Create(OpCodes.Brfalse,endFinally),il.Create(OpCodes.Ldloc,uiLocal),il.Create(OpCodes.Ldloc,previousUi),il.Create(OpCodes.Stfld,updateUi),endFinally})il.InsertBefore(lastRet,i);
body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally){TryStart=firstWork,TryEnd=finallyStart,HandlerStart=finallyStart,HandlerEnd=lastRet});body.MaxStackSize=Math.Max(body.MaxStackSize,3);
var changed=all.Where(m=>before[m.FullName]!=Fingerprint(m)).Select(m=>m.FullName).ToArray();
if(changed.Length!=2||!changed.Contains(main.FullName)||!changed.Contains(lambda.FullName))throw new InvalidOperationException("Unexpected change closure");
Write(a,output);using(var reread=AssemblyDefinition.ReadAssembly(output,new ReaderParameters{AssemblyResolver=resolver})){if(Contract(reread)!=metadataBefore)throw new InvalidOperationException("Metadata/resource identity changed");var written=reread.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Fingerprint);if(written.Count!=before.Count||before.Where(p=>!changed.Contains(p.Key)).Any(p=>written[p.Key]!=p.Value))throw new InvalidOperationException("Unexpected serialized method changes");}
var receiptOut=new{originalSha256=Original,outputSha256=Hash(output),changedMethods=changed,totalMethods=all.Length,unchangedMethods=all.Length-changed.Length,resourcesUnchanged=true,identityAndConfigUnchanged=true,installed=false,runtimeVerified=false};
File.WriteAllText(output+".receipt.json",JsonSerializer.Serialize(receiptOut,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(receiptOut));
