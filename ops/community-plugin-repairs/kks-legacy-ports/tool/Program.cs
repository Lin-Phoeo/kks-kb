using Mono.Cecil;using Mono.Cecil.Cil;using System.Security.Cryptography;using System.Text.Json;
if(args.Length==2&&args[0]=="wrong-mouth-target"){MouthProbe.WriteWrongTarget(args[1]);return;}
if(args.Length==5&&args[0]=="mouth-probe"){MouthProbe.Extract(args[1],args[2],args[3],args[4]);return;}
if(args.Length!=3)throw new ArgumentException("Usage: original game-root new-output");
string input=args[0],game=args[1],output=args[2];
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
var original=Hash(input);
if(!new[]{"36daa827fccce2165297a0944dd4087c59ebdc02a12fa077f8739bf61eea2775","1283d17f92e24d8d4b735b4d2d95c5c550b25e4b0f93ab3b36b83ae20a611df0"}.Contains(original))throw new Exception("Unknown input hash");
if(File.Exists(output)||File.Exists(output+".receipt.json")||Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new Exception("Output must be new and distinct");
using var a=AssemblyDefinition.ReadAssembly(input);using var api=AssemblyDefinition.ReadAssembly(Path.Combine(game,"BepInEx/plugins/KKSAPI.dll"));using var img=AssemblyDefinition.ReadAssembly(Path.Combine(game,"CharaStudio_Data/Managed/UnityEngine.ImageConversionModule.dll"));
var module=a.MainModule;var oldApi=module.AssemblyReferences.Single(r=>r.Name=="KKAPI");
oldApi.Name=api.Name.Name;oldApi.Version=api.Name.Version;oldApi.PublicKeyToken=api.Name.PublicKeyToken;
var imageRef=new AssemblyNameReference(img.Name.Name,img.Name.Version);module.AssemblyReferences.Add(imageRef);
var conversion=new TypeReference("UnityEngine","ImageConversion",module,imageRef);
var target=img.MainModule.GetType("UnityEngine.ImageConversion").Methods.Single(m=>m.Name=="LoadImage"&&m.Parameters.Count==2);
if(!target.IsStatic||target.Parameters[0].ParameterType.FullName!="UnityEngine.Texture2D"||target.Parameters[1].ParameterType.FullName!="System.Byte[]")throw new Exception("Unexpected image API");
var image=new MethodReference("LoadImage",module.TypeSystem.Boolean,conversion){HasThis=false};
var texture=module.GetTypeReferences().Single(t=>t.FullName=="UnityEngine.Texture2D");image.Parameters.Add(new ParameterDefinition(texture));image.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Byte)));
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var c in Types(t.NestedTypes))yield return c;}}
var replacements=new List<string>();
foreach(var m in Types(module.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody))foreach(var i in m.Body.Instructions)if(i.Operand is MethodReference r&&r.DeclaringType.FullName=="UnityEngine.Texture2D"&&r.Name=="LoadImage"){
 if(!r.HasThis||r.Parameters.Count!=1||r.Parameters[0].ParameterType.FullName!="System.Byte[]")throw new Exception("Unknown LoadImage signature");i.OpCode=OpCodes.Call;i.Operand=image;replacements.Add(m.FullName);
}
if(replacements.Count!=(a.Name.Name=="KK_StudioVoice"?2:1))throw new Exception("Unexpected LoadImage call count");
bool mouthAdapted=false;
if(a.Name.Name=="KK_StudioVoice"){
 using var gameAssembly=AssemblyDefinition.ReadAssembly(Path.Combine(game,"CharaStudio_Data/Managed/Assembly-CSharp.dll"));
 var mouth=gameAssembly.MainModule.GetType("FBSCtrlMouth");
 var targets=mouth.Methods.Where(m=>m.Name=="CalcBlend").ToArray();
 if(targets.Length!=1||targets[0].IsStatic||targets[0].ReturnType.FullName!="System.Void"||targets[0].Parameters.Count!=1||targets[0].Parameters[0].ParameterType.FullName!="System.Single"||targets[0].Parameters[0].Name!="openValue")throw new Exception("Unknown KKS mouth target");
 var patch=module.GetType("KK_StudioVoice.MouthPatch");
 var attr=patch.CustomAttributes.Single(c=>c.AttributeType.FullName=="HarmonyLib.HarmonyPatch");
 if(attr.ConstructorArguments.Count!=2||(attr.ConstructorArguments[0].Value as TypeReference)?.FullName!="FBSCtrlMouth"||(string)attr.ConstructorArguments[1].Value!="CalcMouthOpen")throw new Exception("Unknown mouth patch annotation");
 var method=patch.Methods.Single(m=>m.Name=="Postfix");
 if(method.ReturnType.FullName!="System.Void"||method.Parameters.Count!=2||method.Parameters[0].Name!="__instance"||method.Parameters[0].ParameterType.FullName!="FBSCtrlMouth"||method.Parameters[1].Name!="__result"||method.Parameters[1].ParameterType.FullName!="System.Single&")throw new Exception("Unknown mouth patch ABI");
 attr.ConstructorArguments[1]=new CustomAttributeArgument(module.TypeSystem.String,"CalcBlend");
 method.Name="Prefix";method.Parameters[1].Name="openValue";mouthAdapted=true;
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var stream=new FileStream(output,FileMode.CreateNew))a.Write(stream);
var receipt=new{originalSha256=original,candidateSha256=Hash(output),api=api.Name.FullName,image=img.Name.FullName,replacedCalls=replacements,mouthAdapted,methods=Types(module.Types).SelectMany(t=>t.Methods).Count(),installed=false,runtimeVerified=false};File.WriteAllText(output+".receipt.json",JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(receipt));
