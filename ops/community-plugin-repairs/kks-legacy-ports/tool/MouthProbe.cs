using Mono.Cecil;
using System.Security.Cryptography;
static class MouthProbe
{
 public static void WriteWrongTarget(string output){
  if(File.Exists(output))throw new Exception("Fixture output exists");
  using var a=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp",new Version(1,0)),"Assembly-CSharp",ModuleKind.Dll);
  var module=a.MainModule;var type=new TypeDefinition("","FBSCtrlMouth",TypeAttributes.Public|TypeAttributes.Class,module.TypeSystem.Object);module.Types.Add(type);
  var method=new MethodDefinition("CalcBlend",MethodAttributes.Public,module.TypeSystem.Void);method.Parameters.Add(new ParameterDefinition("openValue",ParameterAttributes.None,module.TypeSystem.Int32));type.Methods.Add(method);method.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using var stream=new FileStream(output,FileMode.CreateNew);a.Write(stream);
 }
 public static void Extract(string input,string gameAssembly,string fixture,string output){
  var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
  if(hash!="1283d17f92e24d8d4b735b4d2d95c5c550b25e4b0f93ab3b36b83ae20a611df0"&&hash!="e541c704f215f0639f99d3419ca2e33cb89a296fd3a8dd0f97cf64b71a80a80b"&&hash!="bd61e6522ae5bce52797e34069c0d427bb4fc96b0a0f38a8ba2ff70cd05b16b7")throw new Exception("Unknown mouth probe input");
  if(File.Exists(output)||Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new Exception("Probe output exists");
  using var source=AssemblyDefinition.ReadAssembly(input);using var target=AssemblyDefinition.ReadAssembly(fixture);using var game=AssemblyDefinition.ReadAssembly(gameAssembly);
  var src=source.MainModule.GetType("KK_StudioVoice.MouthPatch");var module=target.MainModule;
  var mapping=new Dictionary<TypeDefinition,TypeDefinition>();
  void Add(TypeDefinition st,TypeDefinition parent){var dt=new TypeDefinition(st.Namespace,st.Name,st.Attributes,Clone.TypeRef(st.BaseType,module));if(parent==null)module.Types.Add(dt);else parent.NestedTypes.Add(dt);mapping[st]=dt;foreach(var child in st.NestedTypes)Add(child,dt);}
  Add(src,null);
  foreach(var pair in mapping){var st=pair.Key;var dt=pair.Value;foreach(var field in st.Fields)dt.Fields.Add(new FieldDefinition(field.Name,field.Attributes,Clone.TypeRef(field.FieldType,module)));foreach(var method in st.Methods){var dm=new MethodDefinition(method.Name,method.Attributes,Clone.TypeRef(method.ReturnType,module));foreach(var p in method.Parameters)dm.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,Clone.TypeRef(p.ParameterType,module)));dt.Methods.Add(dm);}}
  foreach(var pair in mapping)foreach(var method in pair.Key.Methods.Where(m=>m.HasBody))Clone.Body(method,pair.Value.Methods.Single(m=>m.Name==method.Name&&m.Parameters.Count==method.Parameters.Count),module);
  foreach(var attr in src.CustomAttributes.Where(c=>c.AttributeType.FullName=="HarmonyLib.HarmonyPatch")){var copy=new CustomAttribute(Clone.MethodRef(attr.Constructor,module));foreach(var arg in attr.ConstructorArguments)copy.ConstructorArguments.Add(new CustomAttributeArgument(Clone.TypeRef(arg.Type,module),arg.Value is TypeReference tr?Clone.TypeRef(tr,module):arg.Value));mapping[src].CustomAttributes.Add(copy);}
  var real=game.MainModule.GetType("FBSCtrlMouth").Methods.Single(m=>m.Name=="CalcBlend");var dest=module.GetType("FBSCtrlMouth").Methods.Single(m=>m.Name=="CalcBlend");Clone.Body(real,dest,module);
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using var stream=new FileStream(output,FileMode.CreateNew);target.Write(stream);
 }
}
