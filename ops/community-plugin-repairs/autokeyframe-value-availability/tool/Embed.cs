using Mono.Cecil;
static class Embed
{
 public static void Add(AssemblyDefinition helper,ModuleDefinition target,string ns){
  var types=helper.MainModule.Types.Where(t=>t.Namespace==ns).ToArray();
  foreach(var type in types){if(type.NestedTypes.Count!=0)throw new InvalidOperationException("Unexpected nested helper type");target.Types.Add(new TypeDefinition(type.Namespace,type.Name,type.Attributes,Clone.TypeRef(type.BaseType,target)));}
  foreach(var type in types){var dest=target.GetType(type.FullName);foreach(var f in type.Fields){var field=new FieldDefinition(f.Name,f.Attributes,Clone.TypeRef(f.FieldType,target));if(f.HasConstant)field.Constant=f.Constant;dest.Fields.Add(field);}foreach(var m in type.Methods){var method=new MethodDefinition(m.Name,m.Attributes,Clone.TypeRef(m.ReturnType,target));foreach(var p in m.Parameters)method.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,Clone.TypeRef(p.ParameterType,target)));dest.Methods.Add(method);}}
  foreach(var type in types){var dest=target.GetType(type.FullName);foreach(var m in type.Methods.Where(m=>m.HasBody))Clone.Body(m,dest.Methods.Single(x=>x.FullName==m.FullName),target);}
 }
}
