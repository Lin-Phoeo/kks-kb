using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

static class Program
{
    const string OriginalHash="69c953c7ed5a421db28081f6db0b7029fa60c12aaf48433ecfd07da3cf554c98";
    const string CandidateHash="74612e01c4d2278aca4b4d993dc4d944c223496afca63b87f6a23c1c1f68014d";
    static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length==3&&args[0]=="patch") Patch(args[1],args[2]);
            else if(args.Length==4&&args[0]=="probe") Extract(args[1],args[2],args[3]);
            else throw new ArgumentException("Usage: patch <official.dll> <new-candidate.dll> | probe <reviewed.dll> <fixture.dll> <new-probe.dll>");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void WriteNew(AssemblyDefinition a,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var s=new FileStream(path,FileMode.CreateNew);a.Write(s);
    }
    static void ExpandBranches(MethodDefinition method)
    {
        var map=new Dictionary<OpCode,OpCode>{{OpCodes.Br_S,OpCodes.Br},{OpCodes.Brtrue_S,OpCodes.Brtrue},{OpCodes.Brfalse_S,OpCodes.Brfalse},{OpCodes.Beq_S,OpCodes.Beq},{OpCodes.Bge_S,OpCodes.Bge},{OpCodes.Bge_Un_S,OpCodes.Bge_Un},{OpCodes.Bgt_S,OpCodes.Bgt},{OpCodes.Bgt_Un_S,OpCodes.Bgt_Un},{OpCodes.Ble_S,OpCodes.Ble},{OpCodes.Ble_Un_S,OpCodes.Ble_Un},{OpCodes.Blt_S,OpCodes.Blt},{OpCodes.Blt_Un_S,OpCodes.Blt_Un},{OpCodes.Bne_Un_S,OpCodes.Bne_Un},{OpCodes.Leave_S,OpCodes.Leave}};
        foreach(var i in method.Body.Instructions)if(map.TryGetValue(i.OpCode,out var op))i.OpCode=op;
    }
    static void InsertBefore(ILProcessor il,Instruction target,params Instruction[] added){foreach(var i in added)il.InsertBefore(target,i);}
    static void Patch(string input,string output)
    {
        if(Hash(input)!=OriginalHash)throw new InvalidOperationException("Unsupported official input hash");
        if(Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase)||File.Exists(output))throw new InvalidOperationException("Output must be a new distinct file");
        using var assembly=AssemblyDefinition.ReadAssembly(input);
        var module=assembly.MainModule;var type=module.GetType("LiquidWobbleMPB.WobbleSceneController");
        var load=type.Methods.Single(m=>m.Name=="OnSceneLoad");
        var restore=type.Methods.Single(m=>m.Name=="RestoreWombState");
        var state=type.NestedTypes.Single(t=>t.Name.StartsWith("<RestoreWombState>"));
        var move=state.Methods.Single(m=>m.Name=="MoveNext");
        var globalField=move.Body.Instructions.Single(i=>i.Operand is FieldReference f&&f.FullName.Contains("Studio.Studio::dicObjectCtrl"));
        var dict=(GenericInstanceType)((FieldReference)globalField.Operand).FieldType;
        if(restore.Parameters.Count!=2)throw new InvalidOperationException("Unexpected restore signature");
        var capture=new MethodDefinition("CaptureImportObjects",MethodAttributes.Private|MethodAttributes.Static|MethodAttributes.HideBySig,dict);
        capture.Parameters.Add(new ParameterDefinition("operation",ParameterAttributes.None,load.Parameters[0].ParameterType));
        capture.Parameters.Add(new ParameterDefinition("loadedItems",ParameterAttributes.None,load.Parameters[1].ParameterType));
        type.Methods.Add(capture);
        var ctorEmpty=new MethodReference(".ctor",module.TypeSystem.Void,dict){HasThis=true};
        var idict=new TypeReference("System.Collections.Generic","IDictionary`2",module,module.TypeSystem.CoreLibrary);
        var dictDef=dict.ElementType;
        while(dictDef.GenericParameters.Count<2)dictDef.GenericParameters.Add(new GenericParameter(dictDef.GenericParameters.Count==0?"TKey":"TValue",dictDef));
        var idictGeneric=new GenericInstanceType(idict);idictGeneric.GenericArguments.Add(dictDef.GenericParameters[0]);idictGeneric.GenericArguments.Add(dictDef.GenericParameters[1]);
        var ctorCopy=new MethodReference(".ctor",module.TypeSystem.Void,dict){HasThis=true};ctorCopy.Parameters.Add(new ParameterDefinition(idictGeneric));
        var il=capture.Body.GetILProcessor();var import=il.Create(OpCodes.Ldarg_1);var copy=il.Create(OpCodes.Ldarg_1);
        il.Append(il.Create(OpCodes.Ldarg_0));il.Append(il.Create(OpCodes.Ldc_I4_1));il.Append(il.Create(OpCodes.Beq,import));il.Append(il.Create(OpCodes.Ldnull));il.Append(il.Create(OpCodes.Ret));
        il.Append(import);il.Append(il.Create(OpCodes.Brtrue,copy));il.Append(il.Create(OpCodes.Newobj,ctorEmpty));il.Append(il.Create(OpCodes.Ret));
        il.Append(copy);il.Append(il.Create(OpCodes.Newobj,ctorCopy));il.Append(il.Create(OpCodes.Ret));
        var snapshot=new FieldDefinition("importObjects",FieldAttributes.Public,dict);state.Fields.Add(snapshot);
        restore.Parameters.Add(new ParameterDefinition("importObjects",ParameterAttributes.None,dict));
        var ret=restore.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ret);var ril=restore.Body.GetILProcessor();
        InsertBefore(ril,ret,ril.Create(OpCodes.Dup),ril.Create(OpCodes.Ldarg_3),ril.Create(OpCodes.Stfld,snapshot));
        var call=load.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="RestoreWombState");
        var lil=load.Body.GetILProcessor();InsertBefore(lil,call,lil.Create(OpCodes.Ldarg_1),lil.Create(OpCodes.Ldarg_2),lil.Create(OpCodes.Call,capture));call.Operand=restore;
        var start=globalField.Previous.Previous.Previous.Previous.Previous.Previous.Previous;
        if(start.Operand is not MethodReference sm||sm.Name!="get_Instance")throw new InvalidOperationException("Unexpected global dictionary selection shape");
        var store=globalField.Next;if(store.OpCode!=OpCodes.Stloc_1)throw new InvalidOperationException("Unexpected dictionary local");
        var mil=move.Body.GetILProcessor();InsertBefore(mil,start,mil.Create(OpCodes.Ldarg_0),mil.Create(OpCodes.Ldfld,snapshot),mil.Create(OpCodes.Dup),mil.Create(OpCodes.Brtrue,store),mil.Create(OpCodes.Pop));
        foreach(var m in new[]{load,restore,move,capture})ExpandBranches(m);
        WriteNew(assembly,output);
        Console.WriteLine("Patched official import object mapping; SHA256="+Hash(output));
    }
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types){foreach(var t in types){yield return t;foreach(var n in AllTypes(t.NestedTypes))yield return n;}}
    static void Extract(string input,string fixture,string output)
    {
        string hash=Hash(input);
        if(hash!=OriginalHash&&hash!=CandidateHash)throw new InvalidOperationException("Unsupported CIL extraction input");
        if(File.Exists(output))throw new InvalidOperationException("Probe output exists");
        using var source=AssemblyDefinition.ReadAssembly(input);using var result=AssemblyDefinition.ReadAssembly(fixture);
        var src=source.MainModule.GetType("LiquidWobbleMPB.WobbleSceneController");var dst=result.MainModule.GetType(src.FullName);var module=result.MainModule;
        var methods=new Dictionary<MethodDefinition,MethodDefinition>();var fields=new Dictionary<FieldDefinition,FieldDefinition>();var types=new Dictionary<string,TypeDefinition>();
        foreach(var t in AllTypes(module.Types))types[t.FullName]=t;
        var iterator=src.NestedTypes.Single(t=>t.Name.StartsWith("<RestoreWombState>"));
        foreach(var t in new[]{iterator,src.NestedTypes.Single(t=>t.Name=="<>O")})
        {
            var n=new TypeDefinition(t.Namespace,t.Name,t.Attributes,module.ImportReference(t.BaseType));dst.NestedTypes.Add(n);types[t.FullName]=n;
        }
        TypeReference T(TypeReference t)
        {
            if(t is ByReferenceType br)return new ByReferenceType(T(br.ElementType));
            if(t is ArrayType ar)return new ArrayType(T(ar.ElementType),ar.Rank);
            if(t is GenericInstanceType g){var n=new GenericInstanceType(T(g.ElementType));foreach(var a in g.GenericArguments)n.GenericArguments.Add(T(a));return n;}
            if(t is GenericParameter)return t;
            return types.TryGetValue(t.FullName,out var local)?local:module.ImportReference(t);
        }
        foreach(var st in new[]{src,iterator,src.NestedTypes.Single(t=>t.Name=="<>O")})
        {
            var dt=types[st.FullName];foreach(var f in st.Fields)
            {
                var d=dt.Fields.FirstOrDefault(x=>x.Name==f.Name)??new FieldDefinition(f.Name,f.Attributes,T(f.FieldType));if(!dt.Fields.Contains(d))dt.Fields.Add(d);fields[f]=d;
            }
            if(st!=src)foreach(var iface in st.Interfaces)dt.Interfaces.Add(new InterfaceImplementation(T(iface.InterfaceType)));
        }
        var selected=src.Methods.Where(m=>new[]{"OnSceneLoad","RestoreWombState","CaptureImportObjects","ToIntArray"}.Contains(m.Name)).Concat(iterator.Methods).ToArray();
        foreach(var m in selected)
        {
            var dt=types[m.DeclaringType.FullName];var d=dt.Methods.FirstOrDefault(x=>x.Name==m.Name);if(d!=null)dt.Methods.Remove(d);
            d=new MethodDefinition(m.Name,m.Attributes,T(m.ReturnType));foreach(var p in m.Parameters)d.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,T(p.ParameterType)));dt.Methods.Add(d);methods[m]=d;
        }
        MethodReference M(MethodReference m)
        {
            if(m is GenericInstanceMethod gm){var c=new GenericInstanceMethod(M(gm.ElementMethod));foreach(var a in gm.GenericArguments)c.GenericArguments.Add(T(a));return c;}
            if(m is MethodDefinition md&&methods.TryGetValue(md,out var mapped))return mapped;
            var definingName=m.DeclaringType is GenericInstanceType gi?gi.ElementType.FullName:m.DeclaringType.FullName;
            if(types.TryGetValue(definingName,out var local))
            {
                var matches=local.Methods.Where(x=>x.Name==m.Name&&x.Parameters.Count==m.Parameters.Count&&x.GenericParameters.Count==m.GenericParameters.Count).ToArray();
                var exact=matches.SingleOrDefault(x=>x.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(m.Parameters.Select(p=>p.ParameterType.FullName)));
                var d=exact??(matches.Length==1?matches[0]:throw new InvalidOperationException("Cannot bind local "+m.FullName));
                if(m.DeclaringType is not GenericInstanceType)return d;
            }
            var n=new MethodReference(m.Name,T(m.ReturnType),T(m.DeclaringType)){HasThis=m.HasThis,ExplicitThis=m.ExplicitThis,CallingConvention=m.CallingConvention};
            foreach(var gp in m.GenericParameters)n.GenericParameters.Add(new GenericParameter(gp.Name,n));
            foreach(var p in m.Parameters)n.Parameters.Add(new ParameterDefinition(T(p.ParameterType)));return n;
        }
        FieldReference F(FieldReference f)
        {
            if(f is FieldDefinition fd&&fields.TryGetValue(fd,out var mapped))return mapped;
            if(types.TryGetValue(f.DeclaringType.FullName,out var t))return t.Fields.Single(x=>x.Name==f.Name);
            return new FieldReference(f.Name,T(f.FieldType),T(f.DeclaringType));
        }
        foreach(var m in selected)
        {
            var d=methods[m];foreach(var o in m.Overrides)d.Overrides.Add(M(o));if(!m.HasBody)continue;
            d.Body=new MethodBody(d){InitLocals=m.Body.InitLocals,MaxStackSize=m.Body.MaxStackSize};
            var locals=m.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(T(v.VariableType)));foreach(var v in m.Body.Variables)d.Body.Variables.Add(locals[v]);
            var map=m.Body.Instructions.ToDictionary(i=>i,_=>Instruction.Create(OpCodes.Nop));
            foreach(var i in m.Body.Instructions){var n=map[i];n.OpCode=i.OpCode;n.Operand=i.Operand switch{Instruction x=>map[x],Instruction[] xs=>xs.Select(x=>map[x]).ToArray(),VariableDefinition v=>locals[v],ParameterDefinition p=>d.Parameters[p.Index],MethodReference mr=>M(mr),FieldReference fr=>F(fr),TypeReference tr=>T(tr),_=>i.Operand};d.Body.Instructions.Add(n);}
            foreach(var h in m.Body.ExceptionHandlers)d.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType){CatchType=h.CatchType==null?null:T(h.CatchType),TryStart=map[h.TryStart],TryEnd=h.TryEnd==null?null:map[h.TryEnd],HandlerStart=map[h.HandlerStart],HandlerEnd=h.HandlerEnd==null?null:map[h.HandlerEnd],FilterStart=h.FilterStart==null?null:map[h.FilterStart]});
        }
        WriteNew(result,output);Console.WriteLine("Extracted actual CIL methods="+selected.Length+" sourceSHA="+Hash(input));
    }
}
