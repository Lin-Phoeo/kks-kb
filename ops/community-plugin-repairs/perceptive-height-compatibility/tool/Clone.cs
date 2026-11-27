using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class Clone
{
 internal static TypeReference TypeRef(TypeReference t,ModuleDefinition target)
 {
  switch(t.MetadataType){case MetadataType.Void:return target.TypeSystem.Void;case MetadataType.Boolean:return target.TypeSystem.Boolean;case MetadataType.Int32:return target.TypeSystem.Int32;case MetadataType.IntPtr:return target.TypeSystem.IntPtr;case MetadataType.String:return target.TypeSystem.String;case MetadataType.Object:return target.TypeSystem.Object;}
  if(t is GenericParameter)return t;
  if(t is ByReferenceType b)return new ByReferenceType(TypeRef(b.ElementType,target));
  if(t is ArrayType ar)return new ArrayType(TypeRef(ar.ElementType,target));
  if(t is GenericInstanceType g){var x=new GenericInstanceType(TypeRef(g.ElementType,target));foreach(var a in g.GenericArguments)x.GenericArguments.Add(TypeRef(a,target));return x;}
  var existing=target.GetType(t.FullName)??target.GetTypeReferences().FirstOrDefault(x=>x.FullName==t.FullName);if(existing!=null)return existing;
  if(t.Scope is AssemblyNameReference assembly){var scope=assembly.Name=="mscorlib"?target.TypeSystem.CoreLibrary:target.AssemblyReferences.FirstOrDefault(x=>x.Name==assembly.Name);if(scope!=null)return new TypeReference(t.Namespace,t.Name,target,scope,t.IsValueType){DeclaringType=t.DeclaringType==null?null:TypeRef(t.DeclaringType,target)};}
  return target.ImportReference(t);
 }
 internal static MethodReference MethodRef(MethodReference m,ModuleDefinition target)
 {
  if(m is GenericInstanceMethod g){var x=new GenericInstanceMethod(MethodRef(g.ElementMethod,target));foreach(var a in g.GenericArguments)x.GenericArguments.Add(TypeRef(a,target));return x;}
  var owner=target.GetType(m.DeclaringType.FullName);
  if(owner!=null)return owner.Methods.Single(x=>x.Name==m.Name&&x.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(m.Parameters.Select(p=>p.ParameterType.FullName)));
  var r=new MethodReference(m.Name,TypeRef(m.ReturnType,target),TypeRef(m.DeclaringType,target)){HasThis=m.HasThis,ExplicitThis=m.ExplicitThis,CallingConvention=m.CallingConvention};foreach(var p in m.Parameters)r.Parameters.Add(new ParameterDefinition(TypeRef(p.ParameterType,target)));foreach(var gp in m.GenericParameters)r.GenericParameters.Add(new GenericParameter(gp.Name,r));return r;
 }
 internal static void Body(MethodDefinition src,MethodDefinition dest,ModuleDefinition target,IEnumerable<Instruction> selected=null)
 {
  var list=(selected??src.Body.Instructions).ToArray();
  dest.Body=new MethodBody(dest){InitLocals=src.Body.InitLocals,MaxStackSize=src.Body.MaxStackSize};
  var locals=src.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(TypeRef(v.VariableType,target)));foreach(var v in locals.Values)dest.Body.Variables.Add(v);
  var map=list.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
  foreach(var i in list){var d=map[i];d.OpCode=i.OpCode;d.Operand=i.Operand switch{
   Instruction j=>map[j],Instruction[] a=>a.Select(j=>map[j]).ToArray(),VariableDefinition v=>locals[v],ParameterDefinition p=>dest.Parameters[src.Parameters.IndexOf(p)],
   MethodReference m=>MethodRef(m,target),FieldReference f=>target.GetType(f.DeclaringType.FullName)?.Fields.Single(x=>x.Name==f.Name)??new FieldReference(f.Name,TypeRef(f.FieldType,target),TypeRef(f.DeclaringType,target)),TypeReference t=>TypeRef(t,target),_=>i.Operand};dest.Body.Instructions.Add(d);}
  foreach(var e in src.Body.ExceptionHandlers)dest.Body.ExceptionHandlers.Add(new ExceptionHandler(e.HandlerType){CatchType=e.CatchType==null?null:TypeRef(e.CatchType,target),TryStart=map[e.TryStart],TryEnd=e.TryEnd==null?null:map[e.TryEnd],HandlerStart=map[e.HandlerStart],HandlerEnd=e.HandlerEnd==null?null:map[e.HandlerEnd],FilterStart=e.FilterStart==null?null:map[e.FilterStart]});
 }
 internal static TypeDefinition Type(TypeDefinition src,ModuleDefinition target)
 {
  if(src.NestedTypes.Count!=0)throw new InvalidOperationException("Nested helper not supported");
  var result=new TypeDefinition(src.Namespace,src.Name,src.Attributes,TypeRef(src.BaseType,target));target.Types.Add(result);
  foreach(var f in src.Fields)result.Fields.Add(new FieldDefinition(f.Name,f.Attributes,TypeRef(f.FieldType,target)));
  foreach(var m in src.Methods){var d=new MethodDefinition(m.Name,m.Attributes,TypeRef(m.ReturnType,target));foreach(var p in m.Parameters)d.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,TypeRef(p.ParameterType,target)));result.Methods.Add(d);}
  foreach(var m in src.Methods.Where(m=>m.HasBody))Body(m,result.Methods.Single(x=>x.FullName==m.FullName),target);
  return result;
 }
}
