using Mono.Cecil;
using Mono.Cecil.Cil;

static class ProbeExtractor
{
    internal static void Run(string original, string candidate, string fixture, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists.");
        if (Hash(original) != "1be20737d73ae8d23db5c08d3a86e992bc7b12b171401686ecb264e05b439cf6") throw new InvalidDataException("Unsupported original SHA-256.");
        if (Hash(candidate) != "91874b52329d4fa4bb0b69a21e073d69ed423eacdb3485ee03ba985b0a7cefd0") throw new InvalidDataException("Unsupported candidate SHA-256.");
        using var f = AssemblyDefinition.ReadAssembly(fixture);
        var target = f.MainModule.GetType("Probe");
        Copy(original, "ControlClose"); Copy(candidate, "CandidateClose");
        Capture(original, "ControlCapture"); Capture(candidate, "CandidateCapture");
        using(var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) f.Write(stream);

        void Capture(string source, string targetName)
        {
            using var a = AssemblyDefinition.ReadAssembly(source);
            var run=a.MainModule.GetType("Unity.Console.Program").Methods.Single(m=>m.Name=="Run");
            var alloc=run.Body.Instructions.Single(i=>i.OpCode.Code==Code.Call && i.Operand is MethodReference m && m.Name=="AllocConsole");
            var consume=alloc.Next;
            if(consume.OpCode.Code!=Code.Pop && consume.OpCode.Code!=Code.Stsfld)throw new Exception("Unexpected allocation return use.");
            var dest=target.Methods.Single(m=>m.Name==targetName);
            dest.Body=new MethodBody(dest){MaxStackSize=1};
            dest.Body.Instructions.Add(Instruction.Create(OpCodes.Call,target.Methods.Single(m=>m.Name=="AllocateStub")));
            dest.Body.Instructions.Add(consume.OpCode.Code==Code.Pop?Instruction.Create(OpCodes.Pop):Instruction.Create(OpCodes.Stsfld,target.Fields.Single(x=>x.Name=="Owns")));
            dest.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }

        void Copy(string source, string targetName)
        {
            using var a = AssemblyDefinition.ReadAssembly(source);
            var method = a.MainModule.GetType("Unity.Console.Program").Methods.Single(m => m.Name == "Close");
            var dest = target.Methods.Single(m => m.Name == targetName);
            dest.Body = new MethodBody(dest) { MaxStackSize = method.Body.MaxStackSize };
            var clone = method.Body.Instructions.ToDictionary(x => x, x => Instruction.Create(OpCodes.Nop));
            foreach (var src in method.Body.Instructions)
            {
                var ins = clone[src]; ins.OpCode = src.OpCode;
                ins.Operand = src.Operand switch
                {
                    Instruction jump => clone[jump],
                    FieldReference field when field.Name == "console" => target.Fields.Single(x => x.Name == "ConsoleInstance"),
                    FieldReference field when field.Name == "_ownsNativeConsoleForOwnershipFix" => target.Fields.Single(x => x.Name == "Owns"),
                    FieldReference field when field.FullName == "System.IntPtr System.IntPtr::Zero" => f.MainModule.ImportReference(typeof(IntPtr).GetField("Zero")),
                    MethodReference m when m.Name == "Abort" => f.MainModule.GetType("StubConsole").Methods.Single(x => x.Name == "Abort"),
                    MethodReference m when m.Name == "GetConsoleWindow" => target.Methods.Single(x => x.Name == "GetConsoleWindow"),
                    MethodReference m when m.Name == "FreeConsole" => target.Methods.Single(x => x.Name == "FreeConsoleRecorded"),
                    MethodReference m when m.Name == "op_Inequality" => f.MainModule.ImportReference(typeof(IntPtr).GetMethod("op_Inequality", new[] { typeof(IntPtr), typeof(IntPtr) })),
                    null => null,
                    _ => throw new InvalidDataException("Unexpected Close operand " + src)
                };
                dest.Body.Instructions.Add(ins);
            }
        }
    }
    static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
