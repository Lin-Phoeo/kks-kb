using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
using System.Security.Cryptography;

string original=args[0],candidate=args[1],helper=args[2],game=args[3],report=args[4];
using var resolver=new DefaultAssemblyResolver();
foreach(var d in new[]{Path.Combine(game,"CharaStudio_Data/Managed"),Path.Combine(game,"BepInEx/core"),Path.GetDirectoryName(helper)})resolver.AddSearchDirectory(d);
var parameters=new ReaderParameters{AssemblyResolver=resolver};
using var a=AssemblyDefinition.ReadAssembly(original,parameters);
using var b=AssemblyDefinition.ReadAssembly(candidate,parameters);
using var h=AssemblyDefinition.ReadAssembly(helper,parameters);
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types){foreach(var t in types){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
string Operand(Instruction i,IList<Instruction> body)=>i.Operand switch{null=>"",Instruction target=>"#"+body.IndexOf(target),Instruction[] ts=>string.Join(",",ts.Select(x=>"#"+body.IndexOf(x))),_=>i.Operand.ToString()};
string Body(MethodDefinition m)=>!m.HasBody?"none":string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Name+" "+Operand(i,m.Body.Instructions)))+"\nVARS:"+string.Join("|",m.Body.Variables.Select(v=>v.VariableType.FullName))+"\nEH:"+string.Join("|",m.Body.ExceptionHandlers.Select(e=>e.HandlerType+":"+e.TryStart?.Offset+":"+e.TryEnd?.Offset+":"+e.HandlerStart?.Offset+":"+e.HandlerEnd?.Offset));
var left=All(a.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
var right=All(b.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
var changes=left.Keys.Where(k=>!right.ContainsKey(k)||Body(left[k])!=Body(right[k])).ToArray();
var expected=new HashSet<string>{"Update","ActivateCharacter","ApplyChainColor","RestoreCharacter","UpdateDepthIfActive","ApplyNodeVisibility","SyncSkirtGuideActive","CreateConeGuide","DestroyDirectionalGuide","UpdateConeGuidesForChar","RestoreAll"};
bool onlyExpected=changes.Length==11&&changes.All(k=>expected.Contains(left[k].Name))&&left.Count==right.Count;
var allInstructions=All(b.MainModule.Types).Concat(All(h.MainModule.Types)).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions.Select(i=>(method:m.FullName,instruction:i))).ToArray();
var nativeListWrites=allInstructions.Where(p=>p.instruction.Operand is MethodReference m&&m.DeclaringType.FullName.Contains("List`1<Studio.OCIChar/BoneInfo>")&&new[]{"Add","AddRange","Remove","RemoveAt","Clear","Insert","set_Item"}.Contains(m.Name)||p.instruction.OpCode==OpCodes.Stfld&&p.instruction.Operand is FieldReference f&&f.Name=="listBones").Select(p=>p.method+" "+p.instruction).ToArray();
var helperTransformWrites=All(h.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="UnityEngine.Transform"&&r.Name.StartsWith("set_")).Select(i=>i.ToString()).ToArray();
var unresolved=new List<string>();int resolved=0;
foreach(var member in h.MainModule.GetMemberReferences()){
 string scope=member.DeclaringType.Scope.Name;
 if(scope!="Assembly-CSharp"&&scope!="BepInEx"&&!scope.StartsWith("UnityEngine"))continue;
 try{object target=member is MethodReference m?m.Resolve():member is FieldReference f?f.Resolve():null;if(target==null)unresolved.Add(member.FullName);else resolved++;}catch(Exception e){unresolved.Add(member.FullName+": "+e.GetType().Name);}
}
var calls=b.MainModule.GetMemberReferences().OfType<MethodReference>().Where(m=>m.DeclaringType.Scope.Name==h.Name.Name).ToArray();
var badCalls=new List<string>();
foreach(var call in calls){var target=h.MainModule.GetType(call.DeclaringType.FullName)?.Methods.SingleOrDefault(m=>m.FullName==call.FullName);if(target==null||target.HasThis||!target.IsPublic)badCalls.Add(call.FullName);}
var scopeFailures=new List<string>();var helperNames=new HashSet<string>(All(h.MainModule.Types).Select(t=>t.FullName));var pluginNames=new HashSet<string>(All(b.MainModule.Types).Select(t=>t.FullName));
foreach(var type in b.MainModule.GetTypeReferences())if(helperNames.Contains(type.FullName)&&(!(type.Scope is AssemblyNameReference reference)||reference.FullName!=h.Name.FullName))scopeFailures.Add("plugin helper TypeRef "+type.FullName+" scope="+type.Scope);
foreach(var type in h.MainModule.GetTypeReferences())if(pluginNames.Contains(type.FullName))scopeFailures.Add("helper unexpectedly references plugin type "+type.FullName);
foreach(var call in calls)
{
 var target=h.MainModule.GetType(call.DeclaringType.FullName)?.Methods.SingleOrDefault(m=>m.FullName==call.FullName);if(target==null)continue;
 var actual=new[]{call.ReturnType}.Concat(call.Parameters.Select(p=>p.ParameterType));var expectedTypes=new[]{target.ReturnType}.Concat(target.Parameters.Select(p=>p.ParameterType));
 foreach(var pair in actual.Zip(expectedTypes))if(pair.First.Namespace!="System"&&pair.First.Scope.Name!=pair.Second.Scope.Name)scopeFailures.Add(call.FullName+" signature scope "+pair.First.FullName+":"+pair.First.Scope+" expected "+pair.Second.Scope);
 try{if(call.Resolve()?.DeclaringType.Module.Assembly.Name.FullName!=h.Name.FullName)scopeFailures.Add("call resolves outside helper "+call.FullName);}catch(Exception e){scopeFailures.Add("helper call cannot resolve "+call.FullName+":"+e.GetType().Name);}
}
var plugin=b.MainModule.GetType("ManageableSkirts.ManageableSkirtsPlugin");
var reflectionFailures=new List<string>();
void Require(bool condition,string name){if(!condition)reflectionFailures.Add(name);}
foreach(var field in new[]{"tracked","nodeSaturation","nodeBrightness","nodeOpacity","enableColoring","enableDirectionalCones","enableHideLastVisibleNode","enableDepthHiding","toggleHotkey","depthTimer"})Require(plugin.Fields.Any(f=>f.Name==field),"plugin."+field);
foreach(var pair in new[]{("CharData",new[]{"allEntries","ociChar","spine","hiddenChains","hiddenLastNodeIndices","lastVisibleNodesHidden","chainColored"}),("BoneEntry",new[]{"boneInfo","isSkirt","guideSelect","chainIdx","nodeIdx"})}){
 var type=plugin.NestedTypes.Single(t=>t.Name==pair.Item1);
 foreach(var field in pair.Item2)Require(type.Fields.Any(f=>f.Name==field),pair.Item1+"."+field);
}
void Method(string name,string returns,params string[] args){var m=plugin.Methods.Single(x=>x.Name==name);Require(m.ReturnType.FullName==returns&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(args),m.FullName);}
Method("GetChainColor","UnityEngine.Color","System.Int32","System.Single","System.Single","System.Single");
Method("GetHighlightColor","UnityEngine.Color","UnityEngine.Color");
Method("TryParseSkirtBone","System.Boolean","System.String","System.Int32&","System.Int32&");
Method("FindSelectedCharacter","Studio.OCIChar");
Method("DestroyDirectionalGuide","System.Void",plugin.NestedTypes.Single(t=>t.Name=="BoneEntry").FullName);
Method("SetDirectionalGuideMode","System.Int32",plugin.NestedTypes.Single(t=>t.Name=="CharData").FullName,"System.Boolean");
Method("SetLastVisibleNodesHidden","System.Void",plugin.NestedTypes.Single(t=>t.Name=="CharData").FullName,"System.Boolean");
Require(plugin.Properties.Single(p=>p.Name=="ShouldHideTails").PropertyType.FullName=="System.Boolean","ShouldHideTails");
var oldRefs=a.MainModule.AssemblyReferences.Select(r=>r.FullName).ToArray();var newRefs=b.MainModule.AssemblyReferences.Select(r=>r.FullName).ToArray();
var added=newRefs.Except(oldRefs).ToArray();var removed=oldRefs.Except(newRefs).ToArray();
bool refs=removed.Length==0&&added.Length==1&&added[0].StartsWith("Codex.ManageableSkirtsV2State,",StringComparison.Ordinal);
bool coneMeshUnchanged=left.Values.Where(m=>new[]{"GetConeMesh","UpdateConeGuideTransform"}.Contains(m.Name)).All(m=>Body(m)==Body(right[m.FullName]));
bool noPInvoke=All(h.MainModule.Types).SelectMany(t=>t.Methods).All(m=>!m.IsPInvokeImpl);
bool passed=onlyExpected&&nativeListWrites.Length==0&&helperTransformWrites.Length==0&&unresolved.Count==0&&badCalls.Count==0&&scopeFailures.Count==0&&reflectionFailures.Count==0&&refs&&coneMeshUnchanged&&noPInvoke;
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
var result=new{passed,originalSha256=Hash(original),candidateSha256=Hash(candidate),helperSha256=Hash(helper),originalMethodCount=left.Count,unchangedMethods=left.Count-changes.Length,changedMethods=changes,onlyElevenExpectedMethodsChanged=onlyExpected,nativeListWrites,helperTransformWrites,helperResolvedGameMembers=resolved,helperUnresolvedMembers=unresolved,helperCallReferences=calls.Select(m=>m.FullName),helperInvalidCalls=badCalls,serializedTypeRefScopeFailures=scopeFailures,reflectionFailures,referenceContract=refs,addedReferences=added,removedReferences=removed,coneMeshUnchanged,noHelperPInvoke=noPInvoke,scope="Static IL and installed metadata only. Managed fixture execution and real Unity rendering are separate evidence."};
File.WriteAllText(report,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));if(!passed)Environment.ExitCode=1;
