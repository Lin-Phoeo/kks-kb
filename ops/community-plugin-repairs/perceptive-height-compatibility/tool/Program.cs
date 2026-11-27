using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

const string original="a7de711fae624aa1a780eb6e041c4e9c0c4b066f987b024ff52c4c2f067de35b";
const string candidate="d2016fa189a6ec3108282a47ca184ca4bcef4f6992345298b310d91237b16931";
const string pluginName="PerceptiveHeightX.PerceptiveHeightXPlugin",hookName="PerceptiveHeightCompatibility.CaptureHook";
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
if(args.Length!=4)throw new ArgumentException("patch|probe input helper-or-template new-output");
string mode=args[0],input=args[1],other=args[2],output=args[3];
if(File.Exists(output)||File.Exists(output+".receipt.json")||Path.GetFullPath(input)==Path.GetFullPath(output))throw new InvalidOperationException("Output exists or aliases input");
if(mode=="patch"&&Hash(input)!=original)throw new InvalidOperationException("Unsupported input SHA-256");
using var a=AssemblyDefinition.ReadAssembly(input);var m=a.MainModule;var p=m.GetType(pluginName);var awake=p.Methods.Single(x=>x.Name=="Awake");
var callbacks=p.Methods.Where(x=>x.Name.StartsWith("<Awake>b__")).OrderBy(x=>x.Name).ToArray();
if(callbacks.Length!=2)throw new InvalidOperationException("Unexpected callbacks");
if(mode=="probe")
{
 using var fixture=AssemblyDefinition.ReadAssembly(other);var target=fixture.MainModule;var owner=target.GetType(pluginName);
 owner.Methods.Single(x=>x.Name=="BeforeCallback").Name=callbacks[0].Name;owner.Methods.Single(x=>x.Name=="AfterCallback").Name=callbacks[1].Name;
 var hook=m.GetType(hookName);if(hook!=null){var copied=Clone.Type(hook,target);owner.Fields.Single(x=>x.Name=="_captureHook").FieldType=copied;}
 foreach(string name in new[]{"OnEnable","OnDisable","OnDestroy"}){var from=p.Methods.SingleOrDefault(x=>x.Name==name);if(from!=null)Clone.Body(from,owner.Methods.Single(x=>x.Name==name),target);}
 foreach(var from in callbacks)Clone.Body(from,owner.Methods.Single(x=>x.Name==from.Name),target);
 var start=hook==null?awake.Body.Instructions.Single(x=>x.OpCode==OpCodes.Ldstr&&Equals(x.Operand,"Screencap.ScreenshotManager, Screencap")):awake.Body.Instructions[awake.Body.Instructions.Count-3];
 Clone.Body(awake,owner.Methods.Single(x=>x.Name=="Awake"),target,awake.Body.Instructions.SkipWhile(x=>x!=start));
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);fixture.Write(output);Console.WriteLine("Extracted actual capture block and lifecycle CIL");return;
}
if(mode!="patch"||Hash(input)!=original)throw new InvalidOperationException("Unsupported input");
var originalShape=p.Methods.ToDictionary(x=>x.FullName,x=>Shape(x));
var beforeReferences=m.AssemblyReferences.Select(x=>x.FullName).ToArray();
using var helper=AssemblyDefinition.ReadAssembly(other);var h=Clone.Type(helper.MainModule.GetType(hookName),m);
var state=new FieldDefinition("_captureHook",FieldAttributes.Private,h);p.Fields.Add(state);
var actionCtor=(MethodReference)awake.Body.Instructions.First(x=>x.OpCode==OpCodes.Newobj&&x.Operand is MethodReference r&&r.DeclaringType.FullName=="System.Action").Operand;
var enable=new MethodDefinition("OnEnable",MethodAttributes.Private|MethodAttributes.HideBySig,m.TypeSystem.Void);p.Methods.Add(enable);
var il=enable.Body.GetILProcessor();var callEnable=il.Create(OpCodes.Ldarg_0);
foreach(var i in new[]{il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldfld,state),il.Create(OpCodes.Brtrue,callEnable),il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldftn,callbacks[0]),il.Create(OpCodes.Newobj,actionCtor),il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldftn,callbacks[1]),il.Create(OpCodes.Newobj,actionCtor),il.Create(OpCodes.Newobj,h.Methods.Single(x=>x.IsConstructor))})il.Append(i);
il.Append(il.Create(OpCodes.Stfld,state));il.Append(callEnable);il.Append(il.Create(OpCodes.Ldfld,state));il.Append(il.Create(OpCodes.Callvirt,h.Methods.Single(x=>x.Name=="Enable")));il.Append(il.Create(OpCodes.Ret));
foreach(string name in new[]{"OnDisable","OnDestroy"}){
 if(p.Methods.Any(x=>x.Name==name))throw new InvalidOperationException("Existing lifecycle method");
 var d=new MethodDefinition(name,MethodAttributes.Private|MethodAttributes.HideBySig,m.TypeSystem.Void);p.Methods.Add(d);var b=d.Body.GetILProcessor();var end=b.Create(OpCodes.Ret);
 foreach(var i in new[]{b.Create(OpCodes.Ldarg_0),b.Create(OpCodes.Ldfld,state),b.Create(OpCodes.Brfalse,end),b.Create(OpCodes.Ldarg_0),b.Create(OpCodes.Ldfld,state),b.Create(OpCodes.Callvirt,h.Methods.Single(x=>x.Name=="Disable")),end})b.Append(i);
}
var first=awake.Body.Instructions.Single(x=>x.OpCode==OpCodes.Ldstr&&Equals(x.Operand,"Screencap.ScreenshotManager, Screencap"));
var tail=awake.Body.Instructions.SkipWhile(x=>x!=first).ToArray();
if(awake.Body.ExceptionHandlers.Count!=0||awake.Body.Instructions.Except(tail).Any(x=>x.Operand is Instruction jump&&tail.Contains(jump)))throw new InvalidOperationException("Unexpected subscription block boundary");
foreach(var i in tail)awake.Body.Instructions.Remove(i);
var ai=awake.Body.GetILProcessor();ai.Append(ai.Create(OpCodes.Ldarg_0));ai.Append(ai.Create(OpCodes.Call,enable));ai.Append(ai.Create(OpCodes.Ret));
var changed=p.Methods.Where(x=>originalShape.TryGetValue(x.FullName,out var shape)&&shape!=Shape(x)).Select(x=>x.FullName).ToArray();
if(changed.Length!=1||changed[0]!=awake.FullName)throw new InvalidOperationException("Original method change boundary");
if(!beforeReferences.SequenceEqual(m.AssemblyReferences.Select(x=>x.FullName)))throw new InvalidOperationException("New AssemblyRefs");
using var bytes=new MemoryStream();a.Write(bytes);if(Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant()!=candidate)throw new InvalidOperationException("Reviewed candidate hash differs");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var stream=new FileStream(output,FileMode.CreateNew))stream.Write(bytes.ToArray());
var receipt=new{inputSha256=original,outputSha256=Hash(output),changedOriginalMethods=changed,unchangedPluginMethods=originalShape.Count-1,addedPluginMethods=3,helperMethods=h.Methods.Count,newAssemblyReferences=0,scope="Capture subscription only; original HUD calculations, source callbacks, config, metadata retained. No installation."};
File.WriteAllText(output+".receipt.json",JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(receipt));

static string Shape(MethodDefinition method)=>!method.HasBody?"none":string.Join("\n",method.Body.Instructions.Select(i=>i.OpCode+" "+(i.Operand is Instruction jump?method.Body.Instructions.IndexOf(jump):i.Operand)))+string.Join("|",method.Body.ExceptionHandlers.Select(e=>e.HandlerType.ToString()));
