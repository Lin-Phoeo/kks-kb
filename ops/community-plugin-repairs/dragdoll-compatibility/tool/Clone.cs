using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class Clone
{
    internal static TypeDefinition Type(TypeDefinition source,ModuleDefinition target)
    {
        TypeReference MapType(TypeReference type)
        {
            if(type is ByReferenceType byRef)return new ByReferenceType(MapType(byRef.ElementType));
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
            foreach(var eh in src.Body.ExceptionHandlers) dest.Body.ExceptionHandlers.Add(new ExceptionHandler(eh.HandlerType) { CatchType = eh.CatchType == null ? null : MapType(eh.CatchType), TryStart = instr[eh.TryStart], TryEnd = eh.TryEnd == null ? null : instr[eh.TryEnd], HandlerStart = instr[eh.HandlerStart], HandlerEnd = eh.HandlerEnd == null ? null : instr[eh.HandlerEnd] });
        }
        return result;
    }
}
