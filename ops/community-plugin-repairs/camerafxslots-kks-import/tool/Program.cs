using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
const string Original="432aa2935d257b11d7fff680a2fb357b0ac6cca08f72f9e24780083b28e7685b";
const string Candidate="0aa7bb2db66b86b4e03251d7506c734e4f3c046fccb9188c36cceddc27d834ef";
const string LifecycleCandidate="5cf6b7b8f410055067f81c2c013eeca5f8bf47c5f7117e7ad2745564c0e4cfd8";
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
if(args.Length==2&&args[0]=="wrong-event-fixture"){
 string target=args[1];if(File.Exists(target))throw new InvalidOperationException("Fixture must be new");
 using var fake=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("KKS_ExtensibleSaveFormat",new Version(21,1,4,1)),"KKS_ExtensibleSaveFormat",ModuleKind.Dll);var module=fake.MainModule;
 var owner=new TypeDefinition("ExtensibleSaveFormat","ExtendedSave",TypeAttributes.Public|TypeAttributes.Class,module.TypeSystem.Object);module.Types.Add(owner);var handler=new TypeDefinition("","SceneEventHandler",TypeAttributes.NestedPublic|TypeAttributes.Class,module.TypeSystem.Object);owner.NestedTypes.Add(handler);
 var invoke=new MethodDefinition("Invoke",MethodAttributes.Public,module.TypeSystem.Void);invoke.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));handler.Methods.Add(invoke);
 foreach(var name in new[]{"SceneBeingSaved","SceneBeingLoaded","SceneBeingImported"}){var add=new MethodDefinition("add_"+name,MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Void);add.Parameters.Add(new ParameterDefinition(handler));add.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));owner.Methods.Add(add);var remove=new MethodDefinition("remove_"+name,MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Void);remove.Parameters.Add(new ParameterDefinition(handler));remove.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));owner.Methods.Add(remove);owner.Events.Add(new EventDefinition(name,EventAttributes.None,handler){AddMethod=add,RemoveMethod=remove});}
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);using var stream=new FileStream(target,FileMode.CreateNew);fake.Write(stream);return;
}
if(args.Length==4&&args[0]=="lifecycle"){
 string sourcePath=args[1],helperPath=args[2],outPath=args[3];if(Hash(sourcePath)!=Candidate)throw new InvalidOperationException("Expected reviewed import control SHA");if(File.Exists(outPath)||File.Exists(outPath+".receipt.json")||Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(outPath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("New output required");
 using var control=AssemblyDefinition.ReadAssembly(sourcePath);using var helper=AssemblyDefinition.ReadAssembly(helperPath);var module=control.MainModule;Embed.Add(helper,module);
 var runner=module.GetType("CameraFXSlots.CameraFXSlotsRunner");var life=module.GetType("CameraFXSlotsRepair.SlotLifecycle");MethodDefinition Life(string name)=>life.Methods.Single(m=>m.Name==name);
 foreach(string name in new[]{"Update","InitializeUI"}){var m=runner.Methods.Single(x=>x.Name==name);m.Body=new MethodBody(m);var il=m.Body.GetILProcessor();il.Append(il.Create(OpCodes.Ldarg_0));if(name=="InitializeUI")il.Append(il.Create(OpCodes.Ldarg_1));il.Append(il.Create(OpCodes.Call,Life(name=="Update"?"Update":"Reconcile")));il.Append(il.Create(OpCodes.Ret));}
 var add=runner.Methods.Single(m=>m.Name=="AddButtonsForNewSlots");var calls=add.Body.Instructions.Where(i=>i.Operand is MethodReference mr&&mr.Name=="AddListener").ToArray();if(calls.Length!=2)throw new InvalidOperationException("Unexpected listener count");var native=calls[1];if(native.Previous.Previous.Operand is not MethodReference target||target.Name!="<AddButtonsForNewSlots>b__1")throw new InvalidOperationException("Unexpected native listener");
 var ai=add.Body.GetILProcessor();ai.InsertBefore(native,ai.Create(OpCodes.Ldarg_0));ai.InsertBefore(native,ai.Create(OpCodes.Ldloc,add.Body.Variables[0]));native.OpCode=OpCodes.Call;native.Operand=Life("AttachLoad");
 var cleanup=runner.Methods.Single(m=>m.Name=="Cleanup");var ci=cleanup.Body.GetILProcessor();ci.InsertBefore(cleanup.Body.Instructions[0],ci.Create(OpCodes.Call,Life("ReleaseAll")));int writes=0;foreach(var i in cleanup.Body.Instructions)if(i.Operand is MethodReference mr&&mr.DeclaringType.FullName=="UnityEngine.RectTransform"&&mr.Name=="set_sizeDelta"){i.OpCode=OpCodes.Call;i.Operand=Life("IgnoreLegacyLayoutWrite");writes++;}if(writes!=2)throw new InvalidOperationException("Expected two legacy layout writes");
 var attr=module.GetType("CameraFXSlots.CameraFXSlotsPlugin").CustomAttributes.Single(x=>x.AttributeType.FullName=="BepInEx.BepInPlugin");attr.ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,"1.0.0.2");
 using var buffer=new MemoryStream();control.Write(buffer);var bytes=buffer.ToArray();var finalHash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();if(finalHash!=LifecycleCandidate)throw new InvalidOperationException("Unexpected helper or lifecycle candidate bytes");
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);using(var stream=new FileStream(outPath,FileMode.CreateNew))stream.Write(bytes);File.WriteAllText(outPath+".receipt.json",JsonSerializer.Serialize(new{baseSha256=Candidate,helperBuildSha256=Hash(helperPath),outputSha256=Hash(outPath),helperEmbedded=true,installed=false}));Console.WriteLine(Hash(outPath));return;
}
if(args.Length!=4||args[0]!="probe"&&args[0]!="patch")throw new ArgumentException("probe supported-input fixture new-output | patch original game-root new-output");
string mode=args[0],input=args[1],fixture=args[2],output=args[3];
if(Hash(input)!=Original&&(mode!="probe"||Hash(input)!=Candidate&&Hash(input)!=LifecycleCandidate&&Hash(input)!="c183b7b2d33e320b1abd98272f58341a059fff4452681414dcb7a41412d9f0ec"))throw new InvalidOperationException("Unknown input SHA-256");
if(File.Exists(output)||File.Exists(output+".receipt.json")||Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("New distinct output required");
if(mode=="patch"){
 using var assembly=AssemblyDefinition.ReadAssembly(input);var module=assembly.MainModule;
 using var dependency=AssemblyDefinition.ReadAssembly(Path.Combine(fixture,"BepInEx/plugins/KKS_BepisPlugins/KKS_ExtensibleSaveFormat.dll"));
 if(dependency.Name.Name!="KKS_ExtensibleSaveFormat")throw new InvalidOperationException("Wrong KKS ExtendedSave identity");
 var events=dependency.MainModule.GetType("ExtensibleSaveFormat.ExtendedSave");
 foreach(string name in new[]{"SceneBeingSaved","SceneBeingLoaded","SceneBeingImported"})if(events.Events.Single(e=>e.Name==name).EventType.FullName!="ExtensibleSaveFormat.ExtendedSave/SceneEventHandler")throw new InvalidOperationException("Changed scene event contract");
 var invoke=events.NestedTypes.Single(t=>t.Name=="SceneEventHandler").Methods.Single(m=>m.Name=="Invoke");
 if(invoke.ReturnType.FullName!="System.Void"||invoke.Parameters.Count!=1||invoke.Parameters[0].ParameterType.FullName!="System.String")throw new InvalidOperationException("Changed scene handler signature");
 var reference=module.AssemblyReferences.Single(r=>r.Name=="ExtensibleSaveFormat");reference.Name=dependency.Name.Name;reference.Version=dependency.Name.Version;reference.PublicKeyToken=dependency.Name.PublicKeyToken;
 var runner=module.GetType("CameraFXSlots.CameraFXSlotsRunner");
 foreach(string name in new[]{"HookExtendedSaveEvents","UnhookExtendedSaveEvents"}){
  var method=runner.Methods.Single(m=>m.Name==name);var instruction=method.Body.Instructions.Single(i=>i.Operand is MethodReference mr&&mr.Name==(name.StartsWith("Un")?"remove_":"add_")+"SceneBeingImported");
  var remove=new[]{instruction.Previous.Previous.Previous,instruction.Previous.Previous,instruction.Previous,instruction};
  if(remove[0].OpCode!=OpCodes.Ldnull||remove[1].OpCode!=OpCodes.Ldftn||remove[1].Operand is not MethodReference target||target.Name!="OnSceneLoad"||remove[2].OpCode!=OpCodes.Newobj||remove[2].Operand is not MethodReference ctor||ctor.DeclaringType.FullName!="ExtensibleSaveFormat.ExtendedSave/SceneEventHandler"||instruction.OpCode!=OpCodes.Call)throw new InvalidOperationException("Unknown import delegate shape");
  if(method.Body.Instructions.Any(i=>i.Operand is Instruction branch&&remove.Contains(branch)||i.Operand is Instruction[] branches&&branches.Any(remove.Contains)))throw new InvalidOperationException("Import removal has branch targets");
  foreach(var i in remove)method.Body.Instructions.Remove(i);
 }
 var attr=module.GetType("CameraFXSlots.CameraFXSlotsPlugin").CustomAttributes.Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");if((string)attr.ConstructorArguments[2].Value!="1.0.0")throw new InvalidOperationException("Plugin version changed");attr.ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,"1.0.0.1");
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var stream=new FileStream(output,FileMode.CreateNew))assembly.Write(stream);
 var receipt=new{originalSha256=Original,candidateSha256=Hash(output),adaptedAssembly=dependency.Name.FullName,importPolicy="Preserve current scene camera FX slots; native import does not replace camera slots.",installed=false,runtimeVerified=false};
 File.WriteAllText(output+".receipt.json",JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(receipt));return;
}
using var a=AssemblyDefinition.ReadAssembly(input);using var f=AssemblyDefinition.ReadAssembly(fixture);
if(a.MainModule.GetType("CameraFXSlotsRepair.SlotLifecycle")!=null)Embed.Add(a,f.MainModule);
var source=a.MainModule.GetType("CameraFXSlots.CameraFXSlotsRunner");var dest=f.MainModule.GetType(source.FullName);
foreach(var nested in source.NestedTypes){var copy=new TypeDefinition(nested.Namespace,nested.Name,nested.Attributes,Clone.TypeRef(nested.BaseType,f.MainModule));dest.NestedTypes.Add(copy);foreach(var field in nested.Fields)copy.Fields.Add(new FieldDefinition(field.Name,field.Attributes,Clone.TypeRef(field.FieldType,f.MainModule)));foreach(var method in nested.Methods){var m=new MethodDefinition(method.Name,method.Attributes,Clone.TypeRef(method.ReturnType,f.MainModule));foreach(var p in method.Parameters)m.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,Clone.TypeRef(p.ParameterType,f.MainModule)));copy.Methods.Add(m);}}
foreach(var nested in source.NestedTypes)foreach(var method in nested.Methods.Where(m=>m.HasBody))Clone.Body(method,dest.NestedTypes.Single(t=>t.Name==nested.Name).Methods.Single(m=>m.Name==method.Name),f.MainModule);
string[] names={".cctor","HookExtendedSaveEvents","UnhookExtendedSaveEvents","OnSceneSave","OnSceneLoad","ReadFloat","SlotHasData","UpdateButtonColor","UpdateAllButtonColors","Cleanup","Update","InitializeUI","AddButtonsForNewSlots"};
foreach(var name in names)Clone.Body(source.Methods.Single(m=>m.Name==name),dest.Methods.Single(m=>m.Name==name),f.MainModule);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var stream=new FileStream(output,FileMode.CreateNew))f.Write(stream);Console.WriteLine("Actual CIL methods extracted="+names.Length+" inputSHA="+Hash(input));
