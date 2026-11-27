using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Repairs
{
    public const string RenamedCallback = "UnusedPostRenderCallback";
    public const string EndCapName = "Countd360.CharaAnime.KKS.vector";

    public static IEnumerable<TypeDefinition> Types(ModuleDefinition module) => module.Types.SelectMany(Flatten);
    static IEnumerable<TypeDefinition> Flatten(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Flatten));
    public static IEnumerable<MethodDefinition> Methods(ModuleDefinition module) => Types(module).SelectMany(t => t.Methods);
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void AutoKeyframe(AssemblyDefinition assembly)
    {
        var type = Types(assembly.MainModule).Single(t => t.FullName == "AutoKeyframe.Entry");
        var method = type.Methods.Single(m => m.Name == "OnPostRender");
        Require(method.IsPrivate && !method.IsStatic && method.ReturnType.FullName == "System.Void", "Unexpected callback access/signature");
        Require(method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "UnityEngine.Camera", "Unexpected callback parameters");
        Require(method.HasBody && method.Body.Instructions.Count == 1 && method.Body.Instructions[0].OpCode == OpCodes.Ret, "Callback is not the audited empty method");
        Require(!type.Methods.Any(m => m.Name == RenamedCallback), "Replacement name already exists");
        Require(!Methods(assembly.MainModule).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == type.FullName && r.Name == method.Name), "Callback has a direct reference");
        method.Name = RenamedCallback;
    }

    public static void CharaAnime(AssemblyDefinition assembly)
    {
        var methods = Methods(assembly.MainModule).Where(m => m.HasBody).ToArray();
        var occurrences = methods.SelectMany(m => m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, "vector")).Select(i => (m, i))).ToArray();
        Require(occurrences.Length == 4, "Expected exactly four original vector names");
        Require(!methods.SelectMany(m => m.Body.Instructions).Any(i => Equals(i.Operand, EndCapName)), "Replacement cap already exists");
        Require(occurrences.Count(x => x.m.DeclaringType.FullName == "CharaAnime.GizmosManager" && x.m.Name == "Start") == 1, "Expected one manager registration");
        Require(occurrences.Count(x => x.m.DeclaringType.FullName == "CharaAnime.CubeGizmos" && x.m.IsConstructor) == 3, "Expected three cube-axis references");
        foreach (var (method, instruction) in occurrences)
        {
            var nextCall = method.Body.Instructions.Skip(method.Body.Instructions.IndexOf(instruction) + 1).First(i => i.Operand is MethodReference);
            var reference = (MethodReference)nextCall.Operand;
            if (method.Name == "Start") Require(reference.DeclaringType.FullName == "UnityEngine.Texture2D" && reference.Name == "get_whiteTexture", "Registration textures changed");
            else Require(reference.DeclaringType.FullName == "Vectrosity.VectorLine" && reference.Name == "set_endCap", "Unexpected vector consumer");
        }
        foreach (var (_, instruction) in occurrences) instruction.Operand = EndCapName;
    }

    public static string Snapshot(AssemblyDefinition assembly, string kind)
    {
        return JsonSerializer.Serialize(new {
            Assembly = assembly.Name.FullName,
            Attributes = assembly.CustomAttributes.Select(Attribute),
            Resources = assembly.MainModule.Resources.Select(r => new {r.Name, Kind = r.ResourceType.ToString(), Data = r is EmbeddedResource er ? Convert.ToHexString(SHA256.HashData(er.GetResourceData())) : ""}),
            Types = Types(assembly.MainModule).Select(t => new {
                t.FullName, Attributes = t.Attributes.ToString(), Base = t.BaseType?.FullName,
                Custom = t.CustomAttributes.Select(Attribute),
                Fields = t.Fields.Select(f => new {f.Name, Type = f.FieldType.FullName, Attributes = f.Attributes.ToString()}),
                Methods = t.Methods.Select(m => new {
                    Name = kind == "autokeyframe" && t.FullName == "AutoKeyframe.Entry" && m.Name == RenamedCallback ? "OnPostRender" : m.Name,
                    Return = m.ReturnType.FullName, Attributes = m.Attributes.ToString(),
                    Parameters = m.Parameters.Select(p => new {p.Name, Type = p.ParameterType.FullName, Attributes = p.Attributes.ToString()}),
                    Custom = m.CustomAttributes.Select(Attribute),
                    Locals = m.HasBody ? m.Body.Variables.Select(v => v.VariableType.FullName) : [],
                    IL = m.HasBody ? m.Body.Instructions.Select(i => kind == "charaanime" && Equals(i.Operand, EndCapName) ? i.ToString().Replace(EndCapName, "vector", StringComparison.Ordinal) : i.ToString()) : [],
                    Handlers = m.HasBody ? m.Body.ExceptionHandlers.Select(h => $"{h.HandlerType}:{h.TryStart?.Offset}:{h.TryEnd?.Offset}:{h.HandlerStart?.Offset}:{h.HandlerEnd?.Offset}:{h.CatchType?.FullName}") : []
                })
            })
        });
    }
    static string Attribute(CustomAttribute a) => a.AttributeType.FullName + ":" + string.Join("|", a.ConstructorArguments.Select(v => v.Value));
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static void Check(bool value, string message) => Require(value, message);
}

class Program
{
    static readonly Dictionary<string, string> Supported = new() {
        ["autokeyframe"] = "3E2C29E54268FE76DED1A14F6A6C4FF90275957988DD4BA69C92E77A5440CF86",
        ["charaanime"] = "283B71CDD608AE0780087F3BC9D0C435EDB9ECB199A445FDCAC831C00B9EFB5F"
    };
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "selftest") return Fixtures.Run();
            if (args.Length != 4 || args[0] != "patch" || !Supported.ContainsKey(args[1])) throw new ArgumentException("patch <autokeyframe|charaanime> <input> <new-output> | selftest");
            var kind = args[1]; var input = Path.GetFullPath(args[2]); var output = Path.GetFullPath(args[3]);
            Repairs.Check(!string.Equals(input, output, StringComparison.OrdinalIgnoreCase), "Input and output must differ");
            Repairs.Check(!File.Exists(output), "Output already exists");
            Repairs.Check(Repairs.Hash(input) == Supported[kind], "Input does not match supported SHA256");
            using var assembly = AssemblyDefinition.ReadAssembly(input);
            var before = Repairs.Snapshot(assembly, kind);
            if (kind == "autokeyframe") Repairs.AutoKeyframe(assembly); else Repairs.CharaAnime(assembly);
            Repairs.Check(before == Repairs.Snapshot(assembly, kind), "Unexpected logical change before write");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            assembly.Write(output);
            using var readback = AssemblyDefinition.ReadAssembly(output);
            Repairs.Check(before == Repairs.Snapshot(readback, kind), "Unexpected logical change after write");
            Console.WriteLine(JsonSerializer.Serialize(new { Kind = kind, InputSHA256 = Repairs.Hash(input), OutputSHA256 = Repairs.Hash(output), AllowedChanges = kind == "autokeyframe" ? "one private empty method renamed, original Camera parameter retained" : "four end-cap ldstr operands renamed together", ReadbackLogicalComparison = "pass" }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}

static class Fixtures
{
    static int passed;
    static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static void Refuses(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected refusal"); }
    static AssemblyDefinition Auto(bool empty = true, bool caller = false)
    {
        var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Synthetic.Auto", new Version(1, 0)), "Synthetic.Auto", ModuleKind.Dll);
        var m = a.MainModule;
        var t = new TypeDefinition("AutoKeyframe", "Entry", TypeAttributes.Class | TypeAttributes.Public, m.TypeSystem.Object); m.Types.Add(t);
        var cb = new MethodDefinition("OnPostRender", MethodAttributes.Private, m.TypeSystem.Void);
        cb.Parameters.Add(new ParameterDefinition("cam", ParameterAttributes.None, new TypeReference("UnityEngine", "Camera", m, m.TypeSystem.CoreLibrary)));
        if (!empty) cb.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
        cb.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); t.Methods.Add(cb);
        if (caller) { var c = new MethodDefinition("Call", MethodAttributes.Private, m.TypeSystem.Void); c.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); c.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull)); c.Body.Instructions.Add(Instruction.Create(OpCodes.Call, cb)); c.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); t.Methods.Add(c); }
        return a;
    }
    static AssemblyDefinition Chara()
    {
        var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Synthetic.Chara", new Version(1, 0)), "Synthetic.Chara", ModuleKind.Dll); var m = a.MainModule;
        var texture = new TypeReference("UnityEngine", "Texture2D", m, m.TypeSystem.CoreLibrary);
        var vector = new TypeReference("Vectrosity", "VectorLine", m, m.TypeSystem.CoreLibrary);
        var getter = new MethodReference("get_whiteTexture", texture, texture);
        var setter = new MethodReference("set_endCap", m.TypeSystem.Void, vector); setter.Parameters.Add(new ParameterDefinition(m.TypeSystem.String));
        var manager = new TypeDefinition("CharaAnime", "GizmosManager", TypeAttributes.Class, m.TypeSystem.Object); m.Types.Add(manager);
        var start = new MethodDefinition("Start", MethodAttributes.Private, m.TypeSystem.Void); manager.Methods.Add(start);
        start.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "vector")); start.Body.Instructions.Add(Instruction.Create(OpCodes.Call, getter)); start.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var cube = new TypeDefinition("CharaAnime", "CubeGizmos", TypeAttributes.Class, m.TypeSystem.Object); m.Types.Add(cube);
        var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, m.TypeSystem.Void); cube.Methods.Add(ctor);
        for (int i = 0; i < 3; i++) { ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "vector")); ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, setter)); }
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); return a;
    }
    static bool InvalidUnityMessage(AssemblyDefinition a) => Repairs.Methods(a.MainModule).Any(m => m.Name == "OnPostRender" && m.Parameters.Count != 0);
    static string[] Names(AssemblyDefinition a) => Repairs.Methods(a.MainModule).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
    static bool Register(Dictionary<string, string> registry, string name, string textures)
    {
        if (registry.ContainsKey(name)) return false;
        registry.Add(name, textures); return true;
    }
    static void BothOrders(string charaName)
    {
        foreach (var firstChara in new[] { true, false }) {
            var registry = new Dictionary<string, string>();
            var names = firstChara ? new[] {charaName, "vector"} : new[] {"vector", charaName};
            foreach (var name in names) Repairs.Check(Register(registry, name, name == charaName ? "white-white" : "KKSPE-middle-end"), "Duplicate cap rejected");
            Repairs.Check(registry[charaName] == "white-white" && registry["vector"] == "KKSPE-middle-end", "Cap ownership changed");
        }
    }
    public static int Run()
    {
        Test("baseline Unity message rejects Camera parameter", () => { using var a = Auto(); Repairs.Check(InvalidUnityMessage(a), "Baseline should be red"); });
        Test("rename removes message discovery, retains signature and body", () => { using var a = Auto(); var before = Repairs.Snapshot(a, "autokeyframe"); Repairs.AutoKeyframe(a); Repairs.Check(!InvalidUnityMessage(a) && before == Repairs.Snapshot(a, "autokeyframe"), "Candidate should be green"); });
        Test("nonempty callback refused", () => { using var a = Auto(false); Refuses(() => Repairs.AutoKeyframe(a)); });
        Test("direct callback caller refused", () => { using var a = Auto(caller: true); Refuses(() => Repairs.AutoKeyframe(a)); });
        Test("repeated callback patch refused", () => { using var a = Auto(); Repairs.AutoKeyframe(a); try { Repairs.AutoKeyframe(a); throw new Exception("Expected refusal"); } catch (InvalidOperationException) {} });
        Test("baseline duplicate cap is rejected in either order", () => { foreach (var charaFirst in new[] {true, false}) { var d = new Dictionary<string, string>(); var first = charaFirst ? "white-white" : "KKSPE-middle-end"; var second = charaFirst ? "KKSPE-middle-end" : "white-white"; Repairs.Check(Register(d, "vector", first) && !Register(d, "vector", second) && d["vector"] == first, "Expected first registration to win and second to be rejected"); } });
        Test("four names change together, all other IL retained", () => { using var a = Chara(); var before = Repairs.Snapshot(a, "charaanime"); Repairs.CharaAnime(a); Repairs.Check(Names(a).Length == 4 && Names(a).All(n => n == Repairs.EndCapName) && before == Repairs.Snapshot(a, "charaanime"), "Name consistency failed"); });
        Test("both plugin registration orders preserve distinct caps", () => BothOrders(Repairs.EndCapName));
        Test("missing consumer refuses partial patch", () => { using var a = Chara(); var ctor = Repairs.Methods(a.MainModule).Single(m => m.IsConstructor); ctor.Body.Instructions.RemoveAt(0); Refuses(() => Repairs.CharaAnime(a)); });
        Test("unrelated vector consumer refuses broad replacement", () => { using var a = Chara(); var ctor = Repairs.Methods(a.MainModule).Single(m => m.IsConstructor); ctor.Name = "WrongMethod"; Refuses(() => Repairs.CharaAnime(a)); });
        Test("repeated cap patch refused", () => { using var a = Chara(); Repairs.CharaAnime(a); Refuses(() => Repairs.CharaAnime(a)); });
        Console.WriteLine($"{passed} synthetic regression cases passed. Unity runtime and physical rendering remain untested."); return 0;
    }
}
