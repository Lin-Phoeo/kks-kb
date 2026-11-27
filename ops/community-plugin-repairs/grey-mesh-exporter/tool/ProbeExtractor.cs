using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class ProbeExtractor
{
    internal static void Run(string input, string fixture, string output)
    {
        if (File.Exists(output)) throw new InvalidOperationException("Probe output exists");
        string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
        if (sha != "be9a83e1619a7bf332f7c0a54c3f88a5b920135b3c0096ebd2326c3d33238736" &&
            sha != "530dc95e00cfb34486687a3de85cb995d4f6ae1cacc7169d58c07b97dacd15d7" &&
            sha != "0d983e64424b5baed8cf6d81adc6752876c9e77dd04b0645417958040d5247ad") throw new InvalidOperationException("Unsupported probe input identity");
        using var source = AssemblyDefinition.ReadAssembly(input);
        using var test = AssemblyDefinition.ReadAssembly(fixture);
        var module = test.MainModule;
        var helper = source.MainModule.GetType("GreyExportRepair.ExportSafety");
        if (helper != null) Clone.Type(helper, module);
        foreach (string name in new[] { "SaveTexture", "WriteAllBytes", "EnsureFolderExists", "ConvertToRGBNormalMap" })
            Copy(source.MainModule.GetType("Exporter.TextureExporter").Methods.Single(m => m.Name == name),
                module.GetType("Exporter.TextureExporter").Methods.Single(m => m.Name == name));
        Copy(source.MainModule.GetType("Grey").Methods.Single(m => m.Name == "Export"), module.GetType("Grey").Methods.Single(m => m.Name == "Export"));
        Copy(source.MainModule.GetType("Exporter.Fbx").Methods.Single(m => m.Name == "ExportSkinnedMeshOrCloth"), module.GetType("Exporter.Fbx").Methods.Single(m => m.Name == "ExportSkinnedMeshOrCloth"));
        Copy(source.MainModule.GetType("Exporter.UnityFbxExporterBinding").Methods.Single(m=>m.Name=="ScanHierarchy"),module.GetType("Exporter.UnityFbxExporterBinding").Methods.Single(m=>m.Name=="ScanHierarchy"));
        foreach(string name in new[]{"GetTexture","ExportTextures","calculateTextureUseFromTextureName","calculateChannelFromTextureName","AddTextureToMaterial"})
        {var method=source.MainModule.GetType("Exporter.TextureExporter").Methods.Single(m=>m.Name==name);Copy(method,module.GetType("Exporter.TextureExporter").Methods.Single(m=>m.Name==name&&m.Parameters.Count==method.Parameters.Count));}
        var dataSource=source.MainModule.GetType("Data");var dataTarget=module.GetType("Data");
        foreach(var field in dataSource.Fields)if(!dataTarget.Fields.Any(f=>f.Name==field.Name))dataTarget.Fields.Add(new FieldDefinition(field.Name,field.Attributes,MapType(field.FieldType)));
        Copy(dataSource.Methods.Single(m=>m.Name==".cctor"),dataTarget.Methods.Single(m=>m.Name==".cctor"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        test.Write(output);

        TypeReference MapType(TypeReference type)
        {
            if (type is GenericParameter) return type;
            if (type is ByReferenceType byRef) return new ByReferenceType(MapType(byRef.ElementType));
            if (type is ArrayType array) return new ArrayType(MapType(array.ElementType));
            if (type is GenericInstanceType generic) { var t = new GenericInstanceType(MapType(generic.ElementType)); foreach (var a in generic.GenericArguments) t.GenericArguments.Add(MapType(a)); return t; }
            return module.GetType(type.FullName) ?? module.ImportReference(type);
        }
        MethodReference MapMethod(MethodReference method)
        {
            if(method is GenericInstanceMethod generic) { var mapped = new GenericInstanceMethod(MapMethod(generic.ElementMethod)); foreach(var arg in generic.GenericArguments) mapped.GenericArguments.Add(MapType(arg)); return mapped; }
            var local = module.GetType(method.DeclaringType.FullName);
            if (local != null) return local.Methods.SingleOrDefault(m => m.Name == method.Name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p => p.ParameterType.FullName))) ?? throw new InvalidOperationException("Missing fixture method: " + method.FullName);
            if (method.DeclaringType is GenericInstanceType)
            {
                var mapped = new MethodReference(method.Name, MapType(method.ReturnType), MapType(method.DeclaringType))
                    { HasThis = method.HasThis, ExplicitThis = method.ExplicitThis, CallingConvention = method.CallingConvention };
                foreach (var p in method.Parameters) mapped.Parameters.Add(new ParameterDefinition(MapType(p.ParameterType)));
                return mapped;
            }
            return module.ImportReference(method);
        }
        FieldReference MapField(FieldReference field)
        { var local = module.GetType(field.DeclaringType.FullName); return local == null ? module.ImportReference(field) : local.Fields.SingleOrDefault(f => f.Name == field.Name) ?? throw new InvalidOperationException("Missing fixture field: " + field.FullName); }
        void Copy(MethodDefinition src, MethodDefinition dst)
        {
            dst.Body = new MethodBody(dst) { InitLocals = src.Body.InitLocals, MaxStackSize = src.Body.MaxStackSize };
            var locals = src.Body.Variables.ToDictionary(v => v, v => new VariableDefinition(MapType(v.VariableType)));
            foreach (var v in src.Body.Variables) dst.Body.Variables.Add(locals[v]);
            var instructions = src.Body.Instructions.ToDictionary(i => i, _ => Instruction.Create(OpCodes.Nop));
            foreach (var i in src.Body.Instructions)
            {
                var d = instructions[i]; d.OpCode = i.OpCode;
                d.Operand = i.Operand switch { Instruction jump => instructions[jump], Instruction[] jumps => jumps.Select(j => instructions[j]).ToArray(),
                    VariableDefinition v => locals[v], ParameterDefinition p => dst.Parameters[src.Parameters.IndexOf(p)], MethodReference m => MapMethod(m),
                    TypeReference t => MapType(t), FieldReference f => MapField(f), _ => i.Operand };
                dst.Body.Instructions.Add(d);
            }
            foreach (var h in src.Body.ExceptionHandlers) dst.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType) {
                CatchType = h.CatchType == null ? null : MapType(h.CatchType), TryStart = instructions[h.TryStart], TryEnd = h.TryEnd == null ? null : instructions[h.TryEnd],
                HandlerStart = instructions[h.HandlerStart], HandlerEnd = h.HandlerEnd == null ? null : instructions[h.HandlerEnd]
            });
        }
    }
}
