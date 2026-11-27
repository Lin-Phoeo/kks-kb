using Mono.Cecil;using Mono.Cecil.Cil;using System.Text.Json;using System.Security.Cryptography;
string original=args[0],candidate=args[1],helper=args[2],game=args[3],report=args[4];
using var a=AssemblyDefinition.ReadAssembly(original);using var b=AssemblyDefinition.ReadAssembly(candidate);
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types){foreach(var t in types){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
string Operand(Instruction i,IList<Instruction> body)=>i.Operand switch{null=>"",Instruction target=>"#"+body.IndexOf(target),Instruction[] ts=>string.Join(",",ts.Select(x=>"#"+body.IndexOf(x))),_=>i.Operand.ToString()};
string Body(MethodDefinition m)=>!m.HasBody?"none":string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Name+" "+Operand(i,m.Body.Instructions)))+"\nVARS:"+string.Join("|",m.Body.Variables.Select(v=>v.VariableType.FullName))+"\nEH:"+string.Join("|",m.Body.ExceptionHandlers.Select(h=>h.HandlerType+":"+h.TryStart?.Offset+":"+h.TryEnd?.Offset+":"+h.HandlerStart?.Offset+":"+h.HandlerEnd?.Offset));
var left=All(a.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);var right=All(b.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
var changes=left.Keys.Where(k=>!right.ContainsKey(k)||Body(left[k])!=Body(right[k])).ToArray();
var expected=new HashSet<string>{"ActivateCharacter","RestoreCharacter","UpdateDepthIfActive","UpdateDepthForChar","RestoreAll","Update"};
bool onlyExpected=changes.Length==6&&changes.All(k=>expected.Contains(left[k].Name))&&left.Count==right.Count;
using var resolver=new DefaultAssemblyResolver();foreach(var d in new[]{Path.Combine(game,"CharaStudio_Data/Managed"),Path.Combine(game,"BepInEx/core"),Path.GetDirectoryName(helper)})resolver.AddSearchDirectory(d);
using var h=AssemblyDefinition.ReadAssembly(helper,new ReaderParameters{AssemblyResolver=resolver});
var instructions=All(h.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions.Select(i=>(method:m.FullName,instruction:i))).ToArray();
var prohibited=instructions.Where(p=>p.instruction.Operand is MethodReference m&&((m.DeclaringType.FullName.Contains("List`1<Studio.OCIChar/BoneInfo>")&&new[]{"Add","AddRange","Remove","RemoveAt","Clear","Insert","set_Item"}.Contains(m.Name))||(m.DeclaringType.FullName=="UnityEngine.Transform"&&m.Name.StartsWith("set_")))||p.instruction.OpCode==OpCodes.Stfld&&p.instruction.Operand is FieldReference f&&f.Name=="listBones").Select(p=>p.method+" "+p.instruction).ToArray();
var unresolved=new List<string>();int resolved=0;
foreach(var member in h.MainModule.GetMemberReferences()){
 string scope=member.DeclaringType.Scope.Name;
 if(scope!="Assembly-CSharp"&&scope!="BepInEx"&&!scope.StartsWith("UnityEngine"))continue;
 try{object target=member is MethodReference m?m.Resolve():member is FieldReference f?f.Resolve():null;if(target==null)unresolved.Add(member.FullName);else resolved++;}catch(Exception e){unresolved.Add(member.FullName+": "+e.GetType().Name);}
}
object helperComparison=null;
bool helperEquivalent=true;
if(args.Length>5)
{
    using var prior=AssemblyDefinition.ReadAssembly(args[5]);
    var priorMethods=All(prior.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
    var currentMethods=All(h.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
    var helperChanges=priorMethods.Keys.Where(k=>!currentMethods.ContainsKey(k)||Body(priorMethods[k])!=Body(currentMethods[k])).ToArray();
    helperEquivalent=helperChanges.Length==0&&priorMethods.Count==currentMethods.Count;
    helperComparison=new{passed=helperEquivalent,methods=priorMethods.Count,changedMethods=helperChanges,priorSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[5]))).ToLowerInvariant(),currentSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(helper))).ToLowerInvariant()};
}
var priorReferences=a.MainModule.AssemblyReferences.Select(r=>r.FullName).ToArray();
var finalReferences=b.MainModule.AssemblyReferences.Select(r=>r.FullName).ToArray();
var addedReferences=finalReferences.Except(priorReferences).ToArray();
var removedReferences=priorReferences.Except(finalReferences).ToArray();
bool referenceContract=removedReferences.Length==0&&addedReferences.Length==1&&addedReferences[0].StartsWith("Codex.ManageableSkirtsState,",StringComparison.Ordinal);
bool passed=onlyExpected&&prohibited.Length==0&&unresolved.Count==0&&helperEquivalent&&referenceContract;
var data=new{passed,helperComparison,referenceContract,addedReferences,removedReferences,originalMethodCount=left.Count,unchangedMethods=left.Count-changes.Length,changedMethods=changes,onlySixExpectedMethodsChanged=onlyExpected,helperProhibitedListOrTransformWrites=prohibited,helperResolvedGameMembers=resolved,helperUnresolvedMembers=unresolved,helperPInvokeMethods=All(h.MainModule.Types).SelectMany(t=>t.Methods).Count(m=>m.IsPInvokeImpl),scope="Static IL and installed API metadata resolution only; no installed assembly execution."};
File.WriteAllText(report,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(data));if(!passed)Environment.ExitCode=1;
