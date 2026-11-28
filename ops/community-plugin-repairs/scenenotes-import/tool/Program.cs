using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

static class Program
{
    const string InputHash = "23ead0fa3d7bc2ddeaa98163929b280f96f417f9d2aacde98cb0c09d0a44da8d";
    const string CandidateHash = "536181e0a3c8ca34d62f12f493bad5282075c065b4580b929b746676ba52d6fa";
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==3 && args[0]=="patch") Patch(args[1],args[2]);
            else if(args.Length==4 && args[0]=="probe") Extract(args[1],args[2],args[3]);
            else throw new ArgumentException("patch <official.dll> <new-output.dll> | probe <source.dll> <synthetic-fixture.dll> <new-probe.dll>");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
    static void NewOutput(AssemblyDefinition assembly,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream=new FileStream(path,FileMode.CreateNew);assembly.Write(stream);
    }
    static void Patch(string input,string output)
    {
        if(Hash(input)!=InputHash)throw new InvalidOperationException("Unsupported SceneNotes original identity.");
        if(Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase)||File.Exists(output))throw new InvalidOperationException("Output must be new and distinct from input.");
        using var asm=AssemblyDefinition.ReadAssembly(input);var module=asm.MainModule;
        var controller=module.GetType("SceneNotes.SceneNotesSaveController");var plugin=module.GetType("SceneNotes.SceneNotesPlugin");
        var load=controller.Methods.Single(m=>m.Name=="OnSceneLoad");var setNotes=plugin.Methods.Single(m=>m.Name=="SetNotes");var notes=plugin.Fields.Single(f=>f.Name=="Notes");
        var calls=load.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.FullName==setNotes.FullName).ToArray();
        if(calls.Length!=3||load.Parameters.Count!=2)throw new InvalidOperationException("Unexpected official scene-load shape.");
        var stringEmpty=module.GetMemberReferences().OfType<MethodReference>().First(m=>m.DeclaringType.FullName=="System.String"&&m.Name=="IsNullOrEmpty");
        var stringEquality=new MethodReference("op_Equality",module.TypeSystem.Boolean,module.TypeSystem.String){HasThis=false};
        stringEquality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));stringEquality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var concat=new MethodReference("Concat",module.TypeSystem.String,module.TypeSystem.String){HasThis=false};
        for(int i=0;i<3;i++)concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var apply=new MethodDefinition("ApplyNotesForOperation",MethodAttributes.Private|MethodAttributes.Static|MethodAttributes.HideBySig,module.TypeSystem.Void);
        apply.Parameters.Add(new ParameterDefinition("incoming",ParameterAttributes.None,module.TypeSystem.String));
        apply.Parameters.Add(new ParameterDefinition("operation",ParameterAttributes.None,load.Parameters[0].ParameterType));controller.Methods.Add(apply);
        var il=apply.Body.GetILProcessor();var ordinary=il.Create(OpCodes.Ldarg_0);var done=il.Create(OpCodes.Ret);
        il.Append(il.Create(OpCodes.Ldarg_1));il.Append(il.Create(OpCodes.Ldc_I4_1));il.Append(il.Create(OpCodes.Bne_Un,ordinary));
        il.Append(il.Create(OpCodes.Ldarg_0));il.Append(il.Create(OpCodes.Call,stringEmpty));il.Append(il.Create(OpCodes.Brtrue,done));
        il.Append(il.Create(OpCodes.Ldsfld,notes));il.Append(il.Create(OpCodes.Call,stringEmpty));il.Append(il.Create(OpCodes.Brtrue,ordinary));
        il.Append(il.Create(OpCodes.Ldsfld,notes));il.Append(il.Create(OpCodes.Ldarg_0));il.Append(il.Create(OpCodes.Call,stringEquality));il.Append(il.Create(OpCodes.Brtrue,done));
        il.Append(il.Create(OpCodes.Ldsfld,notes));il.Append(il.Create(OpCodes.Ldstr,"\n\n"));il.Append(il.Create(OpCodes.Ldarg_0));il.Append(il.Create(OpCodes.Call,concat));il.Append(il.Create(OpCodes.Call,setNotes));il.Append(il.Create(OpCodes.Ret));
        il.Append(ordinary);il.Append(il.Create(OpCodes.Call,setNotes));il.Append(done);
        var processor=load.Body.GetILProcessor();foreach(var call in calls){processor.InsertBefore(call,processor.Create(OpCodes.Ldarg_1));call.Operand=apply;}
        NewOutput(asm,output);
        Console.WriteLine(JsonSerializer.Serialize(new{originalSha256=InputHash,candidateSha256=Hash(output),changedOriginalMethods=1,newPrivateMethods=1,installed=false}));
    }
    static void Extract(string input,string fixture,string output)
    {
        if(Hash(input)!=InputHash&&Hash(input)!=CandidateHash)throw new InvalidOperationException("Unsupported extraction input identity.");
        if(File.Exists(output)||Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(fixture).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Probe output must be new and distinct.");
        using var src=AssemblyDefinition.ReadAssembly(input);using var dst=AssemblyDefinition.ReadAssembly(fixture);var module=dst.MainModule;
        var source=src.MainModule.GetType("SceneNotes.SceneNotesSaveController");var target=module.GetType(source.FullName);
        var selected=source.Methods.Where(m=>new[]{"OnSceneLoad","OnSceneSave","ApplyNotesForOperation"}.Contains(m.Name)).ToArray();
        var methods=new Dictionary<MethodDefinition,MethodDefinition>();
        TypeReference Type(TypeReference type)
        {
            if(type is GenericParameter)return type;
            if(type is ByReferenceType br)return new ByReferenceType(Type(br.ElementType));
            if(type is GenericInstanceType gi){var copy=new GenericInstanceType(Type(gi.ElementType));foreach(var arg in gi.GenericArguments)copy.GenericArguments.Add(Type(arg));return copy;}
            return module.GetType(type.FullName)??module.ImportReference(type);
        }
        foreach(var method in selected)
        {
            var old=target.Methods.FirstOrDefault(m=>m.Name==method.Name);if(old!=null)target.Methods.Remove(old);
            var copy=new MethodDefinition(method.Name,method.Attributes,Type(method.ReturnType));foreach(var p in method.Parameters)copy.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,Type(p.ParameterType)));target.Methods.Add(copy);methods[method]=copy;
        }
        MethodReference Method(MethodReference method)
        {
            if(method is MethodDefinition definition&&methods.TryGetValue(definition,out var copied))return copied;
            var local=module.GetType(method.DeclaringType.FullName);if(local!=null)return local.Methods.Single(m=>m.Name==method.Name&&m.Parameters.Count==method.Parameters.Count);
            var copy=new MethodReference(method.Name,Type(method.ReturnType),Type(method.DeclaringType)){HasThis=method.HasThis,ExplicitThis=method.ExplicitThis,CallingConvention=method.CallingConvention};foreach(var p in method.Parameters)copy.Parameters.Add(new ParameterDefinition(Type(p.ParameterType)));return copy;
        }
        FieldReference Field(FieldReference field)
        {
            var local=module.GetType(field.DeclaringType.FullName);return local!=null?local.Fields.Single(f=>f.Name==field.Name):new FieldReference(field.Name,Type(field.FieldType),Type(field.DeclaringType));
        }
        foreach(var method in selected)
        {
            var copy=methods[method];copy.Body=new MethodBody(copy){InitLocals=method.Body.InitLocals,MaxStackSize=method.Body.MaxStackSize};
            var locals=method.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(Type(v.VariableType)));foreach(var v in method.Body.Variables)copy.Body.Variables.Add(locals[v]);
            var map=method.Body.Instructions.ToDictionary(i=>i,_=>Instruction.Create(OpCodes.Nop));
            foreach(var i in method.Body.Instructions){var n=map[i];n.OpCode=i.OpCode;n.Operand=i.Operand switch{Instruction jump=>map[jump],VariableDefinition v=>locals[v],ParameterDefinition p=>copy.Parameters[p.Index],MethodReference m=>Method(m),FieldReference f=>Field(f),TypeReference t=>Type(t),_=>i.Operand};copy.Body.Instructions.Add(n);}
            if(method.Body.ExceptionHandlers.Count!=0)throw new InvalidOperationException("Unexpected exception region in source methods.");
        }
        NewOutput(dst,output);Console.WriteLine("Extracted actual CIL methods="+selected.Length);
    }
}
