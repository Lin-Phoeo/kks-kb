using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length==4&&args[0]=="--extract"){ExtractProbe.Run(args[1],args[2],args[3]);return;}
if (args.Length != 3) throw new ArgumentException("Input, helper and new output required.");
string input=args[0],helper=args[1],output=args[2];
const string expected="a3a8422471cfd71b44d4199caac932809b74d48c8abd809678c580d295e58893";
const string reviewed="55ec9023c84aaa775cb460d38e95d42348d652411b0431084c124eac8172f356";
if(Hash(input)!=expected)throw new Exception("Unsupported input SHA-256.");
if(File.Exists(output)||File.Exists(output+".receipt.json"))throw new Exception("Output exists.");
using var a=AssemblyDefinition.ReadAssembly(input);
using var h=AssemblyDefinition.ReadAssembly(helper);
var module=a.MainModule;
var helperType=Clone.Type(h.MainModule.GetType("KKS_ExpressionStudio.PoseEditorLookupRepair"),module);
var bridge=module.GetType("KKS_ExpressionStudio.KkpeBridge");
var scan=bridge.Methods.Single(m=>m.Name=="ScanUnity");
var editors=bridge.Methods.Single(m=>m.Name=="EditorsFor");
var getEditor=bridge.Methods.Single(m=>m.Name=="get_EditorType");
var faceCall=editors.Body.Instructions.Single(i=>i.OpCode.Code==Code.Call&&i.Operand is MethodReference m&&m.Name=="HarvestFaceMap");
var il=editors.Body.GetILProcessor();
Instruction cursor=faceCall;
foreach(var ins in new[]{il.Create(OpCodes.Ldarg_0),il.Create(OpCodes.Ldloc_0),il.Create(OpCodes.Call,getEditor),il.Create(OpCodes.Call,helperType.Methods.Single(m=>m.Name=="Harvest"))}){il.InsertAfter(cursor,ins);cursor=ins;}
var nulRet=scan.Body.Instructions.Single(i=>i.OpCode.Code==Code.Ret&&i.Offset<0x10);
var guardPoint=scan.Body.Instructions.Single(i=>i.Offset==0x10);
if(guardPoint.OpCode.Code!=Code.Nop||scan.Body.ExceptionHandlers[0].TryStart!=guardPoint.Next)throw new Exception("Unexpected ScanUnity shape.");
var resources=scan.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="FindObjectsOfTypeAll");
var objectType=((ArrayType)((MethodReference)resources.Operand).ReturnType).ElementType;
var si=scan.Body.GetILProcessor();
var typeHandle=module.GetMemberReferences().OfType<MethodReference>().First(m=>m.DeclaringType.FullName=="System.Type"&&m.Name=="GetTypeFromHandle");
var assignable=module.GetMemberReferences().OfType<MethodReference>().First(m=>m.DeclaringType.FullName=="System.Type"&&m.Name=="IsAssignableFrom");
var guard=new[]{si.Create(OpCodes.Ldtoken,objectType),si.Create(OpCodes.Call,typeHandle),si.Create(OpCodes.Ldloc_0),si.Create(OpCodes.Callvirt,assignable),si.Create(OpCodes.Brfalse,nulRet)};
foreach(var ins in scan.Body.Instructions)if(ins.Operand==guardPoint)ins.Operand=guard[0];
foreach(var ins in guard)si.InsertBefore(guardPoint,ins);
// Widen existing short branches before writing; EditorsFor spans the inserted primary lookup.
foreach(var method in new[]{scan,editors})
    foreach(var ins in method.Body.Instructions)
        if(ins.OpCode.OperandType==OperandType.ShortInlineBrTarget)
            ins.OpCode=ins.OpCode.Code switch{Code.Br_S=>OpCodes.Br,Code.Brfalse_S=>OpCodes.Brfalse,Code.Brtrue_S=>OpCodes.Brtrue,Code.Beq_S=>OpCodes.Beq,Code.Bge_S=>OpCodes.Bge,Code.Bge_Un_S=>OpCodes.Bge_Un,Code.Bgt_S=>OpCodes.Bgt,Code.Bgt_Un_S=>OpCodes.Bgt_Un,Code.Ble_S=>OpCodes.Ble,Code.Ble_Un_S=>OpCodes.Ble_Un,Code.Blt_S=>OpCodes.Blt,Code.Blt_Un_S=>OpCodes.Blt_Un,Code.Bne_Un_S=>OpCodes.Bne_Un,Code.Leave_S=>OpCodes.Leave,_=>throw new Exception("Unexpected short branch")};
using var buffer=new MemoryStream();a.Write(buffer);buffer.Position=0;
using var old=AssemblyDefinition.ReadAssembly(input);
using var patched=AssemblyDefinition.ReadAssembly(buffer);
var before=AllTypes(old.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
var changed=AllTypes(patched.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>before.ContainsKey(m.FullName)&&Shape(m)!=Shape(before[m.FullName])).Select(m=>m.FullName).ToArray();
if(changed.Length!=2||!changed.Contains(scan.FullName)||!changed.Contains(editors.FullName))throw new Exception("Unexpected changed method scope.");
if(!old.MainModule.AssemblyReferences.Select(r=>r.FullName).SequenceEqual(patched.MainModule.AssemblyReferences.Select(r=>r.FullName)))throw new Exception("New reference introduced: " + string.Join(" | ", patched.MainModule.AssemblyReferences.Select(r=>r.FullName).Except(old.MainModule.AssemblyReferences.Select(r=>r.FullName))));
var outputHash=Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
if(outputHash!=reviewed)throw new Exception("Generated candidate does not match reviewed SHA-256: " + outputHash);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
using(var destination=new FileStream(output,FileMode.CreateNew,FileAccess.Write))destination.Write(buffer.ToArray());
var receipt=new{InputSHA256=Hash(input),OutputSHA256=outputHash,ChangedMethods=changed,AddedMethods=helperType.Methods.Select(m=>m.FullName),Runtime=patched.MainModule.RuntimeVersion,ReferencesUnchanged=true,Boundary="Specific owning guide transform lookup; existing fallbacks retained. No game/Mono/editor-write validation."};
using(var destination=new StreamWriter(new FileStream(output+".receipt.json",FileMode.CreateNew,FileAccess.Write)))destination.Write(JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));

static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types){foreach(var t in types){yield return t;foreach(var n in AllTypes(t.NestedTypes))yield return n;}}
static string Shape(MethodDefinition m)=>!m.HasBody?"none":string.Join("\n",m.Body.Instructions.Select(i=>Normalize(i.OpCode.Code)+" "+(i.Operand is Instruction target?"target:"+m.Body.Instructions.IndexOf(target):i.Operand is Instruction[] jumps?string.Join(",",jumps.Select(t=>m.Body.Instructions.IndexOf(t))):i.Operand is MemberReference r?r.FullName:i.Operand)));
static string Normalize(Code c)=>c.ToString().EndsWith("_S")?c.ToString()[..^2]:c.ToString();

internal static class Clone
{
    internal static TypeDefinition Type(TypeDefinition source,ModuleDefinition target)
    {
        TypeReference MapType(TypeReference type)
        {
            if(type is ArrayType array)return new ArrayType(MapType(array.ElementType));
            if(type is GenericInstanceType generic){var mapped=new GenericInstanceType(MapType(generic.ElementType));foreach(var item in generic.GenericArguments)mapped.GenericArguments.Add(MapType(item));return mapped;}
            switch(type.MetadataType)
            {
                case MetadataType.Void:return target.TypeSystem.Void;
                case MetadataType.Boolean:return target.TypeSystem.Boolean;
                case MetadataType.Int32:return target.TypeSystem.Int32;
                case MetadataType.String:return target.TypeSystem.String;
                case MetadataType.Object:return target.TypeSystem.Object;
            }
            var existing=target.GetTypeReferences().FirstOrDefault(item=>item.FullName==type.FullName);
            if(existing!=null)return existing;
            if(type.Scope is AssemblyNameReference framework&&framework.Name=="mscorlib"&&framework.Version.Major==2)
                return new TypeReference(type.Namespace,type.Name,target,target.TypeSystem.CoreLibrary,type.IsValueType);
            return target.GetType(type.FullName)??target.ImportReference(type);
        }
        var result=new TypeDefinition(source.Namespace,source.Name,source.Attributes,MapType(source.BaseType));target.Types.Add(result);
        var methods=source.Methods.ToDictionary(m=>m,m=>new MethodDefinition(m.Name,m.Attributes,MapType(m.ReturnType)));
        foreach(var pair in methods){foreach(var p in pair.Key.Parameters)pair.Value.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));result.Methods.Add(pair.Value);}
        MethodReference MapMethod(MethodReference method)
        {
            if(method.DeclaringType.FullName==source.FullName)return methods[source.Methods.Single(m=>m.FullName==method.FullName)];
            var local=target.GetType(method.DeclaringType.FullName);
            if(local!=null)return local.Methods.Single(m=>m.Name==method.Name&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p=>p.ParameterType.FullName)));
            if(method.DeclaringType.Namespace!="UnityEngine")return target.ImportReference(method);
            var reference=new MethodReference(method.Name,MapType(method.ReturnType),MapType(method.DeclaringType))
                {HasThis=method.HasThis,ExplicitThis=method.ExplicitThis,CallingConvention=method.CallingConvention};
            foreach(var parameter in method.Parameters)reference.Parameters.Add(new ParameterDefinition(MapType(parameter.ParameterType)));
            foreach(var generic in method.GenericParameters)reference.GenericParameters.Add(new GenericParameter(generic.Name,reference));
            return target.ImportReference(reference);
        }
        foreach(var pair in methods)
        {
            var src=pair.Key;var dest=pair.Value;if(!src.HasBody)continue;
            dest.Body=new MethodBody(dest){InitLocals=src.Body.InitLocals,MaxStackSize=src.Body.MaxStackSize};
            var locals=src.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(MapType(v.VariableType)));
            foreach(var v in src.Body.Variables)dest.Body.Variables.Add(locals[v]);
            var instr=src.Body.Instructions.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
            foreach(var i in src.Body.Instructions)
            {
                var d=instr[i];d.OpCode=i.OpCode;
                d.Operand=i.Operand switch{Instruction jump=>instr[jump],Instruction[] jumps=>jumps.Select(j=>instr[j]).ToArray(),VariableDefinition local=>locals[local],ParameterDefinition p=>dest.Parameters[src.Parameters.IndexOf(p)],MethodDefinition own when methods.ContainsKey(own)=>methods[own],MethodReference m=>MapMethod(m),FieldReference f=>target.ImportReference(f),TypeReference t=>MapType(t),_=>i.Operand};dest.Body.Instructions.Add(d);
            }
            if(src.Body.ExceptionHandlers.Count!=0)throw new Exception("Unexpected helper handlers.");
        }
        return result;
    }
}

internal static class ExtractProbe
{
    internal static void Run(string input,string fixture,string output)
    {
        if(File.Exists(output))throw new Exception("Probe output exists.");
        string inputHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
        if(inputHash!="a3a8422471cfd71b44d4199caac932809b74d48c8abd809678c580d295e58893"&&inputHash!="512b889ca694f973ceab440f49a88a74978fed988f8ae3e4f45012a4ba21f387"&&inputHash!="55ec9023c84aaa775cb460d38e95d42348d652411b0431084c124eac8172f356")throw new Exception("Unsupported probe input SHA-256.");
        using var a=AssemblyDefinition.ReadAssembly(input);using var f=AssemblyDefinition.ReadAssembly(fixture);
        var module=f.MainModule;var source=a.MainModule.GetType("KKS_ExpressionStudio.KkpeBridge");var target=module.GetType("KKS_ExpressionStudio.KkpeBridge");
        var helper=a.MainModule.GetType("KKS_ExpressionStudio.PoseEditorLookupRepair");TypeDefinition copiedHelper=helper==null?null:Clone.Type(helper,module);
        Copy(source.Methods.Single(m=>m.Name=="ScanUnity"),target.Methods.Single(m=>m.Name=="ScanUnity"),false);
        Copy(source.Methods.Single(m=>m.Name=="EditorsFor"),target.Methods.Single(m=>m.Name=="PrimaryLookup"),true);
        Copy(source.Methods.Single(m=>m.Name=="EditorsFor"),target.Methods.Single(m=>m.Name=="EditorsFor"),false);
        Directory.CreateDirectory(Path.GetDirectoryName(output));using(var destination=new FileStream(output,FileMode.CreateNew,FileAccess.Write))f.Write(destination);

        TypeReference Type(TypeReference type)
        {
            if(type is ArrayType ar)return new ArrayType(Type(ar.ElementType));
            if(type is GenericInstanceType generic){var result=new GenericInstanceType(Type(generic.ElementType));foreach(var t in generic.GenericArguments)result.GenericArguments.Add(Type(t));return result;}
            return module.GetType(type.FullName)??module.ImportReference(type);
        }
        MethodReference Method(MethodReference method)
        {
            if(method is GenericInstanceMethod generic){var mapped=new GenericInstanceMethod(Method(generic.ElementMethod));foreach(var item in generic.GenericArguments)mapped.GenericArguments.Add(Type(item));return mapped;}
            if(method.DeclaringType.FullName=="KKS_ExpressionStudio.KkpeBridge")return target.Methods.Single(m=>m.Name==method.Name);
            if(method.DeclaringType.FullName=="KKS_ExpressionStudio.PoseEditorLookupRepair")return copiedHelper.Methods.Single(m=>m.Name==method.Name);
            var local=module.GetType(method.DeclaringType.FullName);if(local!=null)return local.Methods.Single(m=>m.Name==method.Name&&m.Parameters.Count==method.Parameters.Count&&m.GenericParameters.Count==method.GenericParameters.Count);
            return module.ImportReference(method);
        }
        FieldReference Field(FieldReference field)
        {
            var local=module.GetType(field.DeclaringType.FullName);
            return local==null?module.ImportReference(field):local.Fields.Single(f=>f.Name==field.Name);
        }
        void Copy(MethodDefinition src,MethodDefinition dst,bool primary)
        {
            var all=src.Body.Instructions;int cutoff=primary?all.ToList().FindIndex(i=>i.OpCode.Code==Code.Ret)+1:all.Count;
            var selected=all.Take(cutoff).ToArray();var mapping=selected.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
            dst.Body=new MethodBody(dst){InitLocals=src.Body.InitLocals,MaxStackSize=src.Body.MaxStackSize};
            var sourceLocals=primary?src.Body.Variables.Take(1).ToArray():src.Body.Variables.ToArray();
            var locals=sourceLocals.ToDictionary(v=>v,v=>new VariableDefinition(Type(v.VariableType)));foreach(var local in sourceLocals)dst.Body.Variables.Add(locals[local]);
            var emptyLoad=Instruction.Create(OpCodes.Ldloc_0);var emptyReturn=Instruction.Create(OpCodes.Ret);
            foreach(var s in selected)
            {
                var d=mapping[s];d.OpCode=s.OpCode;
                d.Operand=s.Operand switch{Instruction jump=>mapping.TryGetValue(jump,out var dest)?dest:primary?emptyLoad:throw new Exception("External branch"),Instruction[] jumps=>jumps.Select(j=>mapping[j]).ToArray(),VariableDefinition v=>locals[v],ParameterDefinition p=>dst.Parameters[src.Parameters.IndexOf(p)],MethodReference m=>Method(m),TypeReference t=>Type(t),FieldReference field=>Field(field),_=>s.Operand};dst.Body.Instructions.Add(d);
            }
            if(primary){dst.Body.Instructions.Add(emptyLoad);dst.Body.Instructions.Add(emptyReturn);}
            else foreach(var eh in src.Body.ExceptionHandlers)dst.Body.ExceptionHandlers.Add(new ExceptionHandler(eh.HandlerType){CatchType=Type(eh.CatchType),TryStart=mapping[eh.TryStart],TryEnd=mapping[eh.TryEnd],HandlerStart=mapping[eh.HandlerStart],HandlerEnd=mapping[eh.HandlerEnd]});
        }
    }
}
