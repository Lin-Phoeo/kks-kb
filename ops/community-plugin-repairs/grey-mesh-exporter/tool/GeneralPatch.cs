using Mono.Cecil;using Mono.Cecil.Cil;
internal static class GeneralPatch
{
 internal static MethodDefinition[] Apply(ModuleDefinition module)
 {
  var binding=module.GetType("Exporter.UnityFbxExporterBinding");var scan=binding.Methods.Single(m=>m.Name=="ScanHierarchy");
  var getter=scan.Body.Instructions.Single(i=>i.Operand is GenericInstanceMethod m&&m.Name=="GetComponent"&&m.GenericArguments.Single().FullName=="Exporter.FbxNodeCustomProperties");
  var compare=getter.Next.Next.Next.Next;
  if(compare.OpCode!=OpCodes.Call||compare.Operand is not MethodReference equality||equality.Name!="op_Equality"||equality.DeclaringType.FullName!="UnityEngine.Object")throw new InvalidOperationException("Root property comparison differs");
  var inequality=module.GetMemberReferences().OfType<MethodReference>().First(m=>m.DeclaringType.FullName=="UnityEngine.Object"&&m.Name=="op_Inequality");
  compare.Operand=inequality;
  var exporter=module.GetType("Exporter.TextureExporter");var add=exporter.Methods.Single(m=>m.Name=="AddTextureToMaterial");var export=exporter.Methods.Single(m=>m.Name=="ExportTextures");
  if(add.Parameters.Count!=4||add.Parameters[1].Name!="textureName")throw new InvalidOperationException("Texture binding signature differs");
  var all=All(module.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
  var references=all.SelectMany(m=>m.Body.Instructions.Where(i=>i.Operand is MethodReference mr&&mr.FullName==add.FullName).Select(i=>(m,i))).ToArray();
  if(references.Length!=1||references[0].m!=export||references[0].i.OpCode!=OpCodes.Call)throw new InvalidOperationException("Unexpected texture binding callers");
  if(all.SelectMany(m=>m.Body.Instructions).Any(i=>i.OpCode==OpCodes.Ldstr&&Equals(i.Operand,"AddTextureToMaterial")))throw new InvalidOperationException("Reflective texture binding reference");
  var albedo=add.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldsfld&&i.Operand is FieldReference f&&f.DeclaringType.FullName=="Data"&&f.Name=="Albedo");
  if(albedo.Next.OpCode!=OpCodes.Ldarg_1||albedo.Next.Next.Operand is not MethodReference contains||contains.Name!="Contains")throw new InvalidOperationException("Albedo identity test differs");
  var property=new ParameterDefinition("propertyName",ParameterAttributes.None,module.TypeSystem.String);add.Parameters.Add(property);
  albedo.Next.OpCode=OpCodes.Ldarg;albedo.Next.Operand=property;
  var call=references[0].i;var il=export.Body.GetILProcessor();
  if(export.Body.Variables[0].VariableType.FullName!="System.Int32"||export.Parameters[0].ParameterType.FullName!="System.Collections.Generic.List`1<System.String>")throw new InvalidOperationException("Texture name loop differs");
  var indexer=export.Body.Instructions.First(i=>i.Operand is MethodReference m&&m.Name=="get_Item").Operand as MethodReference;
  il.InsertBefore(call,il.Create(OpCodes.Ldarg_1));il.InsertBefore(call,il.Create(OpCodes.Ldloc_0));il.InsertBefore(call,il.Create(OpCodes.Callvirt,indexer));
  foreach(var method in new[]{scan,export,add})Widen(method);
  return new[]{scan,export,add};
 }
 internal static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types){foreach(var t in types){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
 internal static void Widen(MethodDefinition m){foreach(var i in m.Body.Instructions)if(i.OpCode.OperandType==OperandType.ShortInlineBrTarget)i.OpCode=i.OpCode.Code switch{Code.Br_S=>OpCodes.Br,Code.Brtrue_S=>OpCodes.Brtrue,Code.Brfalse_S=>OpCodes.Brfalse,Code.Beq_S=>OpCodes.Beq,Code.Bne_Un_S=>OpCodes.Bne_Un,Code.Leave_S=>OpCodes.Leave,_=>throw new InvalidOperationException("Unsupported branch")};}
}
