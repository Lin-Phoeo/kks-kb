using Mono.Cecil;using Mono.Cecil.Cil;using System.Security.Cryptography;using System.Text.Json;
static class Program {
 const string Original="1187a494a9f9d467c07db5c41d014c72f7a0771a7b31f1eb9b2fe92de832a033";
 const string Candidate="2c10f57c6405e800a6a95d2fbf972f2e7b8891e14f49706dc3779ab04c6e6c20";
 const string ApiHash="866464809d1bd04450a35850c23f9d17a0d1e5a166ed9f5dc525c506fc04ef01";
 public static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
 static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
 static void Write(AssemblyDefinition a,string path){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);using var s=new FileStream(path,FileMode.CreateNew);a.Write(s);}
 static bool Same(string a,string b)=>Path.GetFullPath(a).Equals(Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
 static int Main(string[] args){try{
  if(args.Length!=4)throw new ArgumentException("patch original KKSAPI new-output | probe reviewed fixture new-output");
  string mode=args[0],input=args[1],aux=args[2],output=args[3];
  if(File.Exists(output)||Same(input,output)||Same(aux,output))throw new InvalidOperationException("Output must be new and distinct.");
  if(mode=="patch"){
   if(Hash(input)!=Original)throw new InvalidOperationException("Unreviewed original.");
   using var a=AssemblyDefinition.ReadAssembly(input);using var api=AssemblyDefinition.ReadAssembly(aux);
   if(api.Name.FullName!="KKSAPI, Version=1.48.1.0, Culture=neutral, PublicKeyToken=null"||Hash(aux)!=ApiHash)throw new InvalidOperationException("Reviewed KKSAPI identity required.");
   var r=a.MainModule.AssemblyReferences.Single(x=>x.Name=="KKAPI");string before=r.FullName;
   r.Name=api.Name.Name;r.Version=api.Name.Version;r.PublicKeyToken=api.Name.PublicKeyToken;
   Write(a,output);Console.WriteLine(JsonSerializer.Serialize(new{originalSha256=Original,candidateSha256=Hash(output),referenceFrom=before,referenceTo=r.FullName,changedMethodBodies=0,installed=false}));return 0;
  }
  if(mode!="probe")throw new ArgumentException("Unknown mode.");
  using var src=AssemblyDefinition.ReadAssembly(input);using var dst=AssemblyDefinition.ReadAssembly(aux);
  if(Hash(input)!=Original&&Hash(input)!=Candidate)throw new InvalidOperationException("Unsupported extraction identity.");
  Extract(src,dst);Write(dst,output);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static void Extract(AssemblyDefinition src,AssemblyDefinition dst){
  var target=dst.MainModule;var all=Types(src.MainModule.Types).Where(t=>t.Name!="<Module>").ToArray();
  var types=new Dictionary<string,TypeDefinition>();foreach(var t in Types(target.Types))types[t.FullName]=t;
  var tmap=new Dictionary<TypeDefinition,TypeDefinition>();var fmap=new Dictionary<FieldDefinition,FieldDefinition>();var mmap=new Dictionary<MethodDefinition,MethodDefinition>();
  foreach(var t in all){var d=new TypeDefinition(t.Namespace,t.Name,t.Attributes,null);if(t.IsNested)tmap[t.DeclaringType].NestedTypes.Add(d);else target.Types.Add(d);foreach(var gp in t.GenericParameters)d.GenericParameters.Add(new GenericParameter(gp.Name,d){Attributes=gp.Attributes});tmap[t]=d;types[t.FullName]=d;}
  TypeReference T(TypeReference t){
   switch(t.MetadataType){case MetadataType.Void:return target.TypeSystem.Void;case MetadataType.Boolean:return target.TypeSystem.Boolean;case MetadataType.Int32:return target.TypeSystem.Int32;case MetadataType.Single:return target.TypeSystem.Single;case MetadataType.IntPtr:return target.TypeSystem.IntPtr;case MetadataType.String:return target.TypeSystem.String;case MetadataType.Object:return target.TypeSystem.Object;}
   if(t is GenericParameter gp){if(gp.Owner is TypeReference owner&&types.TryGetValue(owner.FullName,out var dt))return dt.GenericParameters[gp.Position];return gp;}
   if(t is ByReferenceType br)return new ByReferenceType(T(br.ElementType));
   if(t is ArrayType ar)return new ArrayType(T(ar.ElementType),ar.Rank);
   if(t is GenericInstanceType gi){var g=new GenericInstanceType(T(gi.ElementType));foreach(var a in gi.GenericArguments)g.GenericArguments.Add(T(a));return g;}
   if(types.TryGetValue(t.FullName,out var found))return found;
   if(t.Scope is AssemblyNameReference scope&&scope.Name is "mscorlib" or "System" or "System.Core"){string name=t.FullName.Replace('/','+');var runtime=Type.GetType(name)??new[]{typeof(Enumerable).Assembly,typeof(Stack<>).Assembly,typeof(HashSet<>).Assembly,typeof(Uri).Assembly}.Select(a=>a.GetType(name)).FirstOrDefault(x=>x!=null);if(runtime!=null)return target.ImportReference(runtime);throw new InvalidOperationException("Runtime type mapping missing "+t.FullName);}
   return target.ImportReference(t);
  }
  foreach(var t in all){var d=tmap[t];d.BaseType=T(t.BaseType);foreach(var inf in t.Interfaces)d.Interfaces.Add(new InterfaceImplementation(T(inf.InterfaceType)));foreach(var f in t.Fields){var n=new FieldDefinition(f.Name,f.Attributes,T(f.FieldType));if(f.HasConstant)n.Constant=f.Constant;d.Fields.Add(n);fmap[f]=n;}foreach(var m in t.Methods){var n=new MethodDefinition(m.Name,m.Attributes,T(m.ReturnType));foreach(var p in m.Parameters)n.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,T(p.ParameterType)));d.Methods.Add(n);mmap[m]=n;}}
  MethodReference M(MethodReference m){
   if(m is GenericInstanceMethod gm){var g=new GenericInstanceMethod(M(gm.ElementMethod));foreach(var a in gm.GenericArguments)g.GenericArguments.Add(T(a));return g;}
   if(m is MethodDefinition md&&mmap.TryGetValue(md,out var copied))return copied;
   string owner=m.DeclaringType is GenericInstanceType gi?gi.ElementType.FullName:m.DeclaringType.FullName;
   if(types.TryGetValue(owner,out var local)&&m.DeclaringType is not GenericInstanceType){var candidates=local.Methods.Where(x=>x.Name==m.Name&&x.Parameters.Count==m.Parameters.Count&&x.GenericParameters.Count==m.GenericParameters.Count).ToArray();var exact=candidates.SingleOrDefault(x=>x.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(m.Parameters.Select(p=>p.ParameterType.FullName)));if(exact!=null)return exact;if(candidates.Length==1)return candidates[0];throw new InvalidOperationException("Fixture boundary missing "+m.FullName);}
   var r=new MethodReference(m.Name,T(m.ReturnType),T(m.DeclaringType)){HasThis=m.HasThis,ExplicitThis=m.ExplicitThis,CallingConvention=m.CallingConvention};foreach(var gp in m.GenericParameters)r.GenericParameters.Add(new GenericParameter(gp.Name,r));foreach(var p in m.Parameters)r.Parameters.Add(new ParameterDefinition(T(p.ParameterType)));return r;
  }
  FieldReference F(FieldReference f){if(f is FieldDefinition fd&&fmap.TryGetValue(fd,out var copied))return copied;if(types.TryGetValue(f.DeclaringType.FullName,out var local))return local.Fields.SingleOrDefault(x=>x.Name==f.Name)??throw new InvalidOperationException("Fixture field missing "+f.FullName);return new FieldReference(f.Name,T(f.FieldType),T(f.DeclaringType));}
  var excluded=new HashSet<string>{"Start","BindToggle","DrawToggle","PatchEdit","OnDestroy","Detach"};int copiedCount=0;
  foreach(var t in all)foreach(var m in t.Methods){var d=mmap[m];if(!m.HasBody)continue;if(t.FullName=="J694BindingRestore.Plugin"&&excluded.Contains(m.Name)){var il=d.Body.GetILProcessor();il.Append(il.Create(OpCodes.Ldstr,"Fixture excludes startup/config/Harmony wiring"));il.Append(il.Create(OpCodes.Newobj,target.ImportReference(typeof(InvalidOperationException).GetConstructor(new[]{typeof(string)})!)));il.Append(il.Create(OpCodes.Throw));continue;}
   d.Body=new MethodBody(d){InitLocals=m.Body.InitLocals,MaxStackSize=m.Body.MaxStackSize};var locals=m.Body.Variables.ToDictionary(x=>x,x=>new VariableDefinition(T(x.VariableType)));foreach(var x in m.Body.Variables)d.Body.Variables.Add(locals[x]);var map=m.Body.Instructions.ToDictionary(x=>x,_=>Instruction.Create(OpCodes.Nop));foreach(var x in m.Body.Instructions){var n=map[x];n.OpCode=x.OpCode;n.Operand=x.Operand switch{Instruction i=>map[i],Instruction[] a=>a.Select(i=>map[i]).ToArray(),VariableDefinition v=>locals[v],ParameterDefinition p=>d.Parameters[p.Index],MethodReference mr=>M(mr),FieldReference fr=>F(fr),TypeReference tr=>T(tr),_=>x.Operand};d.Body.Instructions.Add(n);}foreach(var h in m.Body.ExceptionHandlers)d.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType){CatchType=h.CatchType==null?null:T(h.CatchType),TryStart=map[h.TryStart],TryEnd=h.TryEnd==null?null:map[h.TryEnd],HandlerStart=map[h.HandlerStart],HandlerEnd=h.HandlerEnd==null?null:map[h.HandlerEnd],FilterStart=h.FilterStart==null?null:map[h.FilterStart]});copiedCount++;
  }
  Console.WriteLine("Actual CIL extracted methods="+copiedCount+" excluded startup/wiring="+excluded.Count);
 }
}
