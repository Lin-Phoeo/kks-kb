using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class ProbeExtractor
{
    internal static void Run(string input,string fixture,string output)
    {
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
        if(hash!="5938a51b4db8ede31e459bfe8bc8e0735bcf10957727a230af39ecf69a1e9f1b"&&hash!="72a6c00dfe75a84d046fd7bcbfde50798b85e1f7e6b147666415666587bf8a3a")throw new InvalidOperationException("Unknown extraction source");
        if(File.Exists(output))throw new InvalidOperationException("Probe output exists");
        using var assembly=AssemblyDefinition.ReadAssembly(input);using var probe=AssemblyDefinition.ReadAssembly(fixture);
        var module=probe.MainModule;var source=assembly.MainModule.GetType("Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge");var target=module.GetType(source.FullName);
        var helper=assembly.MainModule.GetType("Codex.DragDollCompatibility.AxisBuildIdentity");
        if(helper!=null){var copied=Clone.Type(helper,module);var read=copied.Methods.Single(m=>m.Name=="ReadHash");read.Body=new MethodBody(read);var il=read.Body.GetILProcessor();il.Append(il.Create(OpCodes.Ldsfld,module.GetType("ProbeEnv").Fields.Single(f=>f.Name=="Hash")));il.Append(il.Create(OpCodes.Ret));}
        TypeReference Type(TypeReference type)
        {
            if(type is ArrayType array)return new ArrayType(Type(array.ElementType));
            if(type is GenericInstanceType generic){var mapped=new GenericInstanceType(Type(generic.ElementType));foreach(var argument in generic.GenericArguments)mapped.GenericArguments.Add(Type(argument));return mapped;}
            return module.GetType(type.FullName)??module.ImportReference(type);
        }
        MethodReference Method(MethodReference method)
        {
            var local=module.GetType(method.DeclaringType.FullName);
            if(local!=null)return local.Methods.Single(m=>m.Name==method.Name&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p=>p.ParameterType.FullName)));
            return module.ImportReference(method);
        }
        FieldReference Field(FieldReference field)
        {
            var local=module.GetType(field.DeclaringType.FullName);
            return local==null?module.ImportReference(field):local.Fields.Single(f=>f.Name==field.Name);
        }
        var src=source.Methods.Single(m=>m.Name=="Tick");var dst=target.Methods.Single(m=>m.Name=="Tick");
        var discovery=src.Body.Instructions.First(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="System.AppDomain"&&m.Name=="get_CurrentDomain");
        var discoveryStore=src.Body.Instructions.First(i=>i.OpCode==OpCodes.Stloc_0);
        var removed=src.Body.Instructions.SkipWhile(i=>i!=discovery).TakeWhile(i=>i!=discoveryStore).Skip(1).ToHashSet();
        var selected=src.Body.Instructions.Where(i=>!removed.Contains(i)).ToArray();
        var map=selected.ToDictionary(i=>i,_=>Instruction.Create(OpCodes.Nop));
        dst.Body=new MethodBody(dst){InitLocals=src.Body.InitLocals,MaxStackSize=src.Body.MaxStackSize};
        var variables=src.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(Type(v.VariableType)));foreach(var v in src.Body.Variables)dst.Body.Variables.Add(variables[v]);
        foreach(var i in selected){var copy=map[i];if(i==discovery){copy.OpCode=OpCodes.Call;copy.Operand=module.GetType("ProbeEnv").Methods.Single(m=>m.Name=="FindAxis");}
            else{copy.OpCode=i.OpCode;copy.Operand=i.Operand switch{Instruction branch=>map[branch],VariableDefinition local=>variables[local],ParameterDefinition parameter=>dst.Parameters[parameter.Index],MethodReference method=>Method(method),FieldReference field=>Field(field),TypeReference type=>Type(type),_=>i.Operand};}dst.Body.Instructions.Add(copy);}
        foreach(var h in src.Body.ExceptionHandlers)dst.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType){CatchType=h.CatchType==null?null:Type(h.CatchType),TryStart=map[h.TryStart],TryEnd=map[h.TryEnd],HandlerStart=map[h.HandlerStart],HandlerEnd=map[h.HandlerEnd]});
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using var stream=new FileStream(output,FileMode.CreateNew);probe.Write(stream);
    }
}
