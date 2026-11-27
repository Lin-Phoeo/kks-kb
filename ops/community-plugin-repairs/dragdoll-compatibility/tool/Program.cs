using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length==4&&args[0]=="--extract"){ProbeExtractor.Run(args[1],args[2],args[3]);return;}
if(args.Length!=5)throw new ArgumentException("ORIGINAL_DRAG_DLL BASE_AXIS_DLL HELPER_DLL NEW_OUTPUT GAME_ROOT_FOR_METADATA_REFERENCES");
const string expected="5938a51b4db8ede31e459bfe8bc8e0735bcf10957727a230af39ecf69a1e9f1b";
const string knownBase="c75421f9f12e23b797883469b313c794568db8c2242c44e30a1541360170cc74";
const string candidate="72a6c00dfe75a84d046fd7bcbfde50798b85e1f7e6b147666415666587bf8a3a";
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
if(Hash(args[0])!=expected||Hash(args[1])!=knownBase)throw new InvalidOperationException("Unknown original DragDoll or base Axis identity");
if(File.Exists(args[3])||File.Exists(args[3]+".receipt.json"))throw new InvalidOperationException("Output must be new");
using var resolver=new DefaultAssemblyResolver();
foreach(string directory in new[]{Path.Combine(args[4],"CharaStudio_Data/Managed"),Path.Combine(args[4],"BepInEx/core"),Path.Combine(args[4],"BepInEx/plugins")})resolver.AddSearchDirectory(directory);
using var original=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters{AssemblyResolver=resolver});using var helper=AssemblyDefinition.ReadAssembly(args[2]);
var module=original.MainModule;
var imported=Clone.Type(helper.MainModule.GetType("Codex.DragDollCompatibility.AxisBuildIdentity"),module);
var bridge=module.GetType("Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge");
var method=bridge.Methods.Single(m=>m.Name=="Tick");
var anchor=method.Body.Instructions.Single(i=>i.Offset==0x7e);
if(anchor.OpCode!=OpCodes.Ldloc_0||method.Body.ExceptionHandlers.Count!=1)throw new InvalidOperationException("Unexpected bridge Tick shape");
var end=method.Body.Instructions.Last();
var il=method.Body.GetILProcessor();
var known=imported.Methods.Single(m=>m.Name=="IsKnownBase");
var logRef=module.GetMemberReferences().OfType<MethodReference>().FirstOrDefault(m=>m.DeclaringType.FullName=="UnityEngine.Debug"&&m.Name=="Log"&&m.Parameters.Count==1);
if(logRef==null){var unity=module.AssemblyReferences.Single(a=>a.Name=="UnityEngine.CoreModule");var debug=new TypeReference("UnityEngine","Debug",module,unity);logRef=new MethodReference("Log",module.TypeSystem.Void,debug);logRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));}
foreach(var instruction in new[]{il.Create(OpCodes.Ldloc_0),il.Create(OpCodes.Call,known),il.Create(OpCodes.Brfalse,anchor),
    il.Create(OpCodes.Ldc_I4_1),il.Create(OpCodes.Stsfld,bridge.Fields.Single(f=>f.Name=="unavailable")),
    il.Create(OpCodes.Ldstr,"DRAGDOLL_BASE_AXIS_BRIDGE_NOT_APPLICABLE build=base-2.0 nativeGuideHooks=retained"),
    il.Create(OpCodes.Call,logRef),il.Create(OpCodes.Leave,end)})il.InsertBefore(anchor,instruction);
foreach(var instruction in method.Body.Instructions)if(instruction.OpCode.OperandType==OperandType.ShortInlineBrTarget)
    instruction.OpCode=instruction.OpCode.Code switch{Code.Br_S=>OpCodes.Br,Code.Brtrue_S=>OpCodes.Brtrue,Code.Brfalse_S=>OpCodes.Brfalse,Code.Bge_Un_S=>OpCodes.Bge_Un,Code.Blt_S=>OpCodes.Blt,Code.Leave_S=>OpCodes.Leave,_=>throw new InvalidOperationException("Unexpected short branch "+instruction.OpCode)};
using var bytes=new MemoryStream();original.Write(bytes);
string outputHash=Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
if(outputHash!=candidate)throw new InvalidOperationException("Candidate identity differs: "+outputHash);
bytes.Position=0;using var check=AssemblyDefinition.ReadAssembly(bytes);using var source=AssemblyDefinition.ReadAssembly(args[0]);
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types){foreach(var type in types){yield return type;foreach(var nested in All(type.NestedTypes))yield return nested;}}
string Shape(MethodDefinition m)=>!m.HasBody?"none":string.Join("|",m.Body.Variables.Select(v=>v.VariableType.FullName))+string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Name+" "+(i.Operand is Instruction branch?"#"+m.Body.Instructions.IndexOf(branch):i.Operand is MemberReference r?r.FullName:i.Operand)));
var before=All(source.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);var changed=All(check.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>before.TryGetValue(m.FullName,out var old)&&Shape(old)!=Shape(m)).Select(m=>m.FullName).ToArray();
if(changed.Length!=1||changed[0]!=method.FullName)throw new InvalidOperationException("Unexpected changed method scope");
if(!source.MainModule.AssemblyReferences.Select(r=>r.FullName).SequenceEqual(check.MainModule.AssemblyReferences.Select(r=>r.FullName)))throw new InvalidOperationException("New assembly references introduced");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);using(var stream=new FileStream(args[3],FileMode.CreateNew))stream.Write(bytes.ToArray());
var receipt=new{originalSha=expected,baseAxisSha=knownBase,candidateSha=outputHash,changedMethods=changed,unchangedOriginalMethods=before.Count-changed.Length,referencesUnchanged=true,installed=false};
File.WriteAllText(args[3]+".receipt.json",JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(receipt));
