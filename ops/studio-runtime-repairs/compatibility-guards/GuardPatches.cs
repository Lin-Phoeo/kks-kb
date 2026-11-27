using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class GuardPatches
{
    internal static List<string> Apply(AssemblyDefinition asm, string mode)
    {
        var module = asm.MainModule;
        var changes = new List<string>();
        var touched = new List<MethodDefinition>();
        if (mode == "shallty")
        {
            var type = module.GetType("ShalltyUtils.KeyframesGroups") ?? throw new Exception("type mismatch");
            var method = type.Methods.Single(x => x.Name == "LoadSceneData");
            touched.Add(method);
            var ins = method.Body.Instructions;
            var clear = ins.Single(x => x.Operand is MethodReference m && m.Name == "ClearSceneData");
            var field = ins.Select(x => x.Operand).OfType<FieldReference>().Single(x => x.Name == "data" && x.DeclaringType.FullName == "ExtensibleSaveFormat.PluginData");
            var get = ins.Select(x => x.Operand).OfType<MethodReference>().Single(x => x.Name == "get_Item");
            var contains = new MethodReference("ContainsKey", module.TypeSystem.Boolean, get.DeclaringType) { HasThis = true };
            contains.Parameters.Add(new ParameterDefinition(get.Parameters[0].ParameterType));
            var resume = clear.Next;
            var ret = Instruction.Create(OpCodes.Ret);
            var il = method.Body.GetILProcessor();
            var added = new[] {
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, field), Instruction.Create(OpCodes.Brfalse, ret),
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, field), Instruction.Create(OpCodes.Ldstr, "keyframesGroupsData"),
                Instruction.Create(OpCodes.Callvirt, contains), Instruction.Create(OpCodes.Brtrue, resume), ret
            };
            foreach (var i in added) il.InsertBefore(resume, i);
            var cast = ins.Single(x => x.OpCode.Code == Code.Castclass && ((TypeReference)x.Operand).FullName == "System.String");
            cast.OpCode = OpCodes.Isinst;
            changes.Add("Clear existing groups, return for null dictionary/missing optional key; non-string optional value becomes null.");
        }
        else if (mode == "sexfaces")
        {
            var type = module.GetType("UnityEngine.RangeSlider") ?? throw new Exception("type mismatch");
            var cctor = type.Methods.Single(x => x.Name == ".cctor");
            var create = type.Methods.Single(x => x.Name == "Create");
            var field = type.Fields.Single(x => x.Name == "sliderStyle");
            var init = new MethodDefinition("EnsureStylesInsideOnGUI", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
            type.Methods.Add(init);
            touched.Add(init);
            touched.Add(create);
            touched.Add(cctor);
            init.Body = cctor.Body;
            var il = init.Body.GetILProcessor();
            var first = init.Body.Instructions[0];
            var ret = Instruction.Create(OpCodes.Ret);
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, field));
            il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, ret);
            cctor.Body = new MethodBody(cctor);
            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            foreach (var f in type.Fields.Where(x => x.IsStatic && new[]{"sliderStyle","thumbStyle","bandStyle"}.Contains(x.Name)))
                f.IsInitOnly = false;
            create.Body.GetILProcessor().InsertBefore(create.Body.Instructions[0], Instruction.Create(OpCodes.Call, init));
            changes.Add("Static constructor no longer reads GUI.skin; initialize styles on first RangeSlider.Create inside OnGUI.");
        }
        else if (mode == "kls")
        {
            var type = module.GetType("Buggsy.Quill.KoikatsuSunshineLightingSuite.LightingRegistryProbe") ?? throw new Exception("type mismatch");
            var method = type.Methods.Single(x => x.Name == "DumpEnvironmentRecordCore");
            touched.Add(method);
            var call = method.Body.Instructions.Single(x => x.Operand is MethodReference m && m.Name == "get_mapEffectInfo");
            var sun = type.Methods.Single(x => x.Name == "GetMapSunLight").Body.Instructions.Select(x => x.Operand).OfType<MethodReference>().First(x => x.Name == "get_sunLightInfo");
            var il = method.Body.GetILProcessor();
            var start = Instruction.Create(OpCodes.Dup);
            foreach(var branch in method.Body.Instructions.Where(x=>ReferenceEquals(x.Operand,call)).ToArray()) branch.Operand=start;
            foreach (var i in new[] {start,Instruction.Create(OpCodes.Callvirt,sun),Instruction.Create(OpCodes.Brtrue,call),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Ret)})
                il.InsertBefore(call, i);
            changes.Add("Do not evaluate mapEffectInfo when a map has no sunLightInfo, including empty map and transition.");
        }
        else throw new ArgumentException("Unknown mode");
        foreach(var method in touched.Where(x=>x.HasBody))
            foreach(var i in method.Body.Instructions) i.OpCode = i.OpCode.Code switch {
                Code.Br_S=>OpCodes.Br, Code.Brfalse_S=>OpCodes.Brfalse, Code.Brtrue_S=>OpCodes.Brtrue,
                Code.Beq_S=>OpCodes.Beq, Code.Bge_S=>OpCodes.Bge, Code.Bge_Un_S=>OpCodes.Bge_Un,
                Code.Bgt_S=>OpCodes.Bgt, Code.Bgt_Un_S=>OpCodes.Bgt_Un, Code.Ble_S=>OpCodes.Ble,
                Code.Ble_Un_S=>OpCodes.Ble_Un, Code.Blt_S=>OpCodes.Blt, Code.Blt_Un_S=>OpCodes.Blt_Un,
                Code.Bne_Un_S=>OpCodes.Bne_Un, Code.Leave_S=>OpCodes.Leave, _=>i.OpCode };
        return changes;
    }
}
