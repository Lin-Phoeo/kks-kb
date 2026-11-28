using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
const string expected="17ae25d3f4654687b6b180abc04ca7528790363ecf24b7f461081f30b2d5fc4d";
if(args.Length!=3)throw new ArgumentException("Usage: patch original helper new-output");
string input=args[0],helper=args[1],output=args[2];
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
if(Hash(input)!=expected)throw new InvalidOperationException("Unknown V2 original SHA");
string target=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,Path.GetFileName(helper));
if(string.Equals(Path.GetFullPath(input),Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase)||File.Exists(output)||File.Exists(output+".receipt.json")||File.Exists(target))throw new InvalidOperationException("Output and helper must be new");
if(!File.Exists(helper))throw new FileNotFoundException("Helper assembly not found",helper);
using var a=AssemblyDefinition.ReadAssembly(input);using var h=AssemblyDefinition.ReadAssembly(helper);var module=a.MainModule;
var p=module.GetType("ManageableSkirts.ManageableSkirtsPlugin");var support=h.MainModule.GetType("Codex.ManageableSkirtsV2State.StateRepair");
if(h.Name.Name!="Codex.ManageableSkirtsV2State"||support==null)throw new InvalidOperationException("Unexpected helper identity");
var initial=All(module.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,m=>Shape(m));
MethodReference Import(string name)
{
 var m=support.Methods.Single(x=>x.Name==name);var r=new MethodReference(m.Name,Map(m.ReturnType),module.ImportReference(support)){HasThis=false};foreach(var q in m.Parameters)r.Parameters.Add(new ParameterDefinition(Map(q.ParameterType)));return r;
}
TypeReference Map(TypeReference t)
{
 if(t.MetadataType==MetadataType.Void)return module.TypeSystem.Void;if(t.MetadataType==MetadataType.Object)return module.TypeSystem.Object;if(t.MetadataType==MetadataType.Boolean)return module.TypeSystem.Boolean;if(t.MetadataType==MetadataType.Int32)return module.TypeSystem.Int32;
 return module.GetTypeReferences().FirstOrDefault(r=>r.FullName==t.FullName)??throw new InvalidOperationException("Missing target type "+t.FullName);
}
var replacements=new Dictionary<string,string>{{"RestoreCharacter","Restore"},{"ApplyNodeVisibility","VisibilityForData"},{"ApplyChainColor","Color"},{"UpdateDepthIfActive","Tick"},{"Update","Update"},{"RestoreAll","RestoreAll"},{"SyncSkirtGuideActive","SyncActive"}};
foreach(var row in replacements){var m=p.Methods.Single(x=>x.Name==row.Key);m.Body=new MethodBody(m);var il=m.Body.GetILProcessor();if(!m.IsStatic)il.Append(il.Create(OpCodes.Ldarg_0));foreach(var arg in m.Parameters)il.Append(il.Create(OpCodes.Ldarg,arg));il.Append(il.Create(OpCodes.Call,Import(row.Value)));il.Append(il.Create(OpCodes.Ret));}
int visibleWrites=0,rendererSuppress=0,rendererRestore=0;
foreach(var m in p.Methods.Where(x=>x.HasBody))foreach(var i in m.Body.Instructions){
 if(i.Operand is not MethodReference call)continue;
 if(call.DeclaringType.FullName=="Studio.GuideObject"&&call.Name=="set_visible") {i.OpCode=OpCodes.Call;i.Operand=Import("SetVisible");visibleWrites++;}
 if(call.DeclaringType.FullName=="UnityEngine.Renderer"&&call.Name=="set_enabled") {i.OpCode=OpCodes.Call;i.Operand=Import(m.Name=="DestroyDirectionalGuide"?"RestoreRenderer":"SuppressRenderer");if(m.Name=="DestroyDirectionalGuide")rendererRestore++;else rendererSuppress++;}
}
if(visibleWrites!=1||rendererSuppress!=2||rendererRestore!=1)throw new InvalidOperationException("Unexpected setter shape");
var activate=p.Methods.Single(x=>x.Name=="ActivateCharacter");var guard=activate.Body.GetILProcessor();var begin=activate.Body.Instructions[0];guard.InsertBefore(begin,guard.Create(OpCodes.Ldarg_1));guard.InsertBefore(begin,guard.Create(OpCodes.Call,Import("CanActivate")));guard.InsertBefore(begin,guard.Create(OpCodes.Brtrue,begin));guard.InsertBefore(begin,guard.Create(OpCodes.Ldc_I4_0));guard.InsertBefore(begin,guard.Create(OpCodes.Ret));
var cones=p.Methods.Single(x=>x.Name=="UpdateConeGuidesForChar");var ci=cones.Body.GetILProcessor();var first=cones.Body.Instructions[0];ci.InsertBefore(first,ci.Create(OpCodes.Ldarg_1));ci.InsertBefore(first,ci.Create(OpCodes.Call,Import("IsLive")));ci.InsertBefore(first,ci.Create(OpCodes.Brtrue,first));ci.InsertBefore(first,ci.Create(OpCodes.Ret));
var id=p.CustomAttributes.Single(x=>x.AttributeType.FullName=="BepInEx.BepInPlugin");if((string)id.ConstructorArguments[2].Value!="2.0.0")throw new InvalidOperationException("Version mismatch");id.ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,"2.0.0.1");
var changed=All(module.Types).SelectMany(t=>t.Methods).Where(m=>initial[m.FullName]!=Shape(m)).Select(m=>m.FullName).ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);a.Write(output);File.Copy(helper,target);
var receipt=new{inputSha256=expected,outputSha256=Hash(output),helperSha256=Hash(target),originalMethods=initial.Count,changedMethods=changed,unchangedOriginalMethods=initial.Count-changed.Length,coneGeometryMethodsRetained=true,visibleWrites,rendererSuppress,rendererRestore,installed=false};File.WriteAllText(output+".receipt.json",JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(receipt));
static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
static string Shape(MethodDefinition m)=>!m.HasBody?"none":string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode+" "+(i.Operand is Instruction j?m.Body.Instructions.IndexOf(j):i.Operand)))+string.Join("|",m.Body.Variables.Select(x=>x.VariableType.FullName))+string.Join("|",m.Body.ExceptionHandlers.Select(x=>x.HandlerType.ToString()));
