using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class PatchBseMe5
{
    static string Hash(string p) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-", "").ToLowerInvariant(); }
    static void Need(bool ok, string why) { if(!ok) throw new InvalidOperationException(why); }
    static MethodDefinition Find(TypeDefinition t, string name) { return t.Methods.Single(m=>m.Name==name); }
    static void Body(MethodDefinition m) { m.Body=new Mono.Cecil.Cil.MethodBody(m); m.Body.InitLocals=false; m.Body.MaxStackSize=8; }
    static void Attr(ModuleDefinition mod, MethodDefinition m, AssemblyDefinition harmony, string name) { m.CustomAttributes.Add(new CustomAttribute(mod.ImportReference(harmony.MainModule.GetType("HarmonyLib."+name).Methods.Single(x=>x.IsConstructor&&!x.HasParameters)))); }
    static string BytesHash(byte[] bytes) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static bool Within(string path,string root) { return path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase); }
    static void SafeOutput(string output,string repo,params string[] inputs) {
        Need(!Within(output,repo),"Output cannot be inside the repository");
        foreach(string input in inputs)for(var d=new DirectoryInfo(Path.GetDirectoryName(input));d!=null;d=d.Parent)if(Directory.Exists(Path.Combine(d.FullName,"BepInEx"))||File.Exists(Path.Combine(d.FullName,"CharaStudio.exe")))Need(!Within(output,d.FullName),"Output cannot be inside a game installation");
        for(var d=new DirectoryInfo(output);d!=null;d=d.Parent)if(d.Exists)Need((d.Attributes&FileAttributes.ReparsePoint)==0,"Output may not traverse a link or junction");
    }
    static string Shape(MethodDefinition m) {
        if(!m.HasBody)return "none";
        return string.Join("|",m.Body.Variables.Select(v=>v.VariableType.FullName).ToArray())+string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Name+" "+(i.Operand is Instruction?"#"+m.Body.Instructions.IndexOf((Instruction)i.Operand):i.Operand is MemberReference?((MemberReference)i.Operand).FullName:i.Operand)).ToArray());
    }
    static string J(string s) { return "\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\""; }
    static void WriteReceipt(string input,string me,string directory,AssemblyDefinition candidate,string hash) {
        using(var original=AssemblyDefinition.ReadAssembly(input)){
            var before=original.MainModule.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
            var after=candidate.MainModule.GetTypes().SelectMany(t=>t.Methods).ToArray();
            var changed=after.Where(m=>before.ContainsKey(m.FullName)&&Shape(m)!=Shape(before[m.FullName])).Select(m=>m.FullName).ToArray();
            var added=after.Where(m=>!before.ContainsKey(m.FullName)).Select(m=>m.FullName).ToArray();
            var removed=before.Keys.Where(n=>!after.Any(m=>m.FullName==n)).ToArray();
            Need(changed.Length==4&&added.Length==4&&removed.Length==1,"Method delta exceeds reviewed scope");
            var originalResources=original.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>BytesHash(r.GetResourceData()));
            Need(candidate.MainModule.Resources.OfType<EmbeddedResource>().All(r=>originalResources[r.Name]==BytesHash(r.GetResourceData())),"Embedded resources changed");
            Directory.CreateDirectory(directory);
            string receipt="{\"inputSha256\":"+J(Hash(input))+",\"materialEditorSha256\":"+J(Hash(me))+",\"outputSha256\":"+J(hash)+",\"changedMethods\":["+string.Join(",",changed.Select(J).ToArray())+"],\"addedMethods\":["+string.Join(",",added.Select(J).ToArray())+"],\"removedMethods\":["+string.Join(",",removed.Select(J).ToArray())+"],\"assemblyReferences\":["+string.Join(",",candidate.MainModule.AssemblyReferences.Select(r=>J(r.FullName)).ToArray())+"],\"resourcesUnchanged\":true,\"installed\":false,\"runtimeVerified\":false}";
            using(var w=new StreamWriter(new FileStream(Path.Combine(directory,"receipt.json"),FileMode.CreateNew),new System.Text.UTF8Encoding(false)))w.WriteLine(receipt);
        }
    }
    static void Normalize(TypeReference t, AssemblyNameReference core)
    {
        if(t==null||t is GenericParameter)return;
        if(t is TypeSpecification)Normalize(((TypeSpecification)t).ElementType,core);
        if(t is GenericInstanceType)foreach(var g in ((GenericInstanceType)t).GenericArguments)Normalize(g,core);
        if(!(t is TypeSpecification)&&t.Scope is AssemblyNameReference&&t.Scope.Name=="mscorlib")t.Scope=core;
    }
    static void NormalizeMember(MemberReference r, AssemblyNameReference core)
    {
        Normalize(r.DeclaringType,core);
        var m=r as MethodReference;if(m!=null){Normalize(m.ReturnType,core);foreach(var p in m.Parameters)Normalize(p.ParameterType,core);var gm=m as GenericInstanceMethod;if(gm!=null)foreach(var a in gm.GenericArguments)Normalize(a,core);}
        var f=r as FieldReference;if(f!=null)Normalize(f.FieldType,core);
    }
    public static void Main(string[] args)
    {
        Need(args.Length==5,"Arguments: INPUT_BSE MATERIAL_EDITOR OUTPUT_DIRECTORY HARMONY REPOSITORY_ROOT");
        string input=Path.GetFullPath(args[0]), mePath=Path.GetFullPath(args[1]), outputDir=Path.GetFullPath(args[2]), harmonyPath=Path.GetFullPath(args[3]), repo=Path.GetFullPath(args[4]);
        Need(!Directory.Exists(outputDir)&&!File.Exists(outputDir),"Output directory must be new");
        SafeOutput(outputDir,repo,input,mePath);
        Need(Hash(input)=="2212e3d27114fc83a578ffb6dec313642ea31e72381efc3f6a2c5427e96fbdfa","Unsupported BSE input");
        Need(Hash(mePath)=="9e72d250ca262df8a3c3aa93922113e3cdc0103ca1b22ee6cf9c44b04981278b","Unsupported Material Editor input");
        var resolver=new DefaultAssemblyResolver();
        foreach(string d in new[]{Path.GetDirectoryName(input),Path.GetDirectoryName(mePath),Path.GetDirectoryName(harmonyPath),System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()})resolver.AddSearchDirectory(d);
        using(var a=AssemblyDefinition.ReadAssembly(input,new ReaderParameters{AssemblyResolver=resolver}))
        using(var me=AssemblyDefinition.ReadAssembly(mePath,new ReaderParameters{AssemblyResolver=resolver}))
        using(var harmony=AssemblyDefinition.ReadAssembly(harmonyPath))
        {
            var mod=a.MainModule;var bridge=mod.GetType("BlendShapeEditor.MaterialEditorBridge");var hooks=bridge.NestedTypes.Single(t=>t.Name=="Hooks");
            var meUi=me.MainModule.GetType("MaterialEditorAPI.MaterialEditorUI");var presenter=me.MainModule.GetType("MaterialEditorAPI.MaterialEditorPresenter");
            Need(meUi.Properties.Any(p=>p.Name=="CurrentGameObject"&&p.PropertyType.FullName=="UnityEngine.GameObject"),"ME getter absent");
            var select=Find(presenter,"SelectRenderers");Need(select.Parameters.Count==2&&select.ReturnType.FullName=="System.Collections.Generic.IList`1<UnityEngine.Renderer>","ME selection contract changed");
            Need(Find(meUi,"ReleaseRetainedTargetContext").Parameters.Count==0,"ME release contract changed");
            var getter=Find(bridge,"GetCurrentMaterialEditorObject");
            Need(getter.Body.Instructions.Count(i=>i.OpCode==OpCodes.Ldfld&&i.Operand.ToString()=="UnityEngine.GameObject MaterialEditorAPI.MaterialEditorUI::CurrentGameObject")==1,"original bridge getter changed");
            var singletonCall=(MethodReference)getter.Body.Instructions.Single(i=>i.OpCode==OpCodes.Call).Operand;
            var propField=new FieldDefinition("CodexMe5CurrentObject",Mono.Cecil.FieldAttributes.Private|Mono.Cecil.FieldAttributes.Static|Mono.Cecil.FieldAttributes.InitOnly,mod.ImportReference(typeof(PropertyInfo)));bridge.Fields.Add(propField);
            var cctor=Find(bridge,".cctor");var ci=cctor.Body.GetILProcessor();var ret=cctor.Body.Instructions.Last();Need(ret.OpCode==OpCodes.Ret,"bridge cctor tail changed");
            var init=new[]{Instruction.Create(OpCodes.Ldtoken,mod.ImportReference(meUi)),Instruction.Create(OpCodes.Call,mod.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle"))),Instruction.Create(OpCodes.Ldstr,"CurrentGameObject"),Instruction.Create(OpCodes.Ldc_I4,(int)(BindingFlags.Instance|BindingFlags.NonPublic)),Instruction.Create(OpCodes.Callvirt,mod.ImportReference(typeof(Type).GetMethod("GetProperty",new[]{typeof(string),typeof(BindingFlags)}))),Instruction.Create(OpCodes.Stsfld,propField)};
            foreach(var i in init)ci.InsertBefore(ret,i);
            Body(getter);var gi=getter.Body.GetILProcessor();gi.Emit(OpCodes.Ldsfld,propField);gi.Emit(OpCodes.Call,singletonCall);gi.Emit(OpCodes.Ldnull);gi.Emit(OpCodes.Callvirt,mod.ImportReference(typeof(PropertyInfo).GetMethod("GetValue",new[]{typeof(object),typeof(object[])})));gi.Emit(OpCodes.Castclass,getter.ReturnType);gi.Emit(OpCodes.Ret);
            var old=Find(hooks,"MaterialEditorPopulateListTranspiler");Need(old.CustomAttributes.Any(x=>x.AttributeType.Name=="HarmonyTranspiler"),"original transpiler changed");
            old.Name="CodexMe5SelectedRenderersPostfix";old.CustomAttributes.Clear();old.ReturnType=mod.TypeSystem.Void;old.Parameters.Clear();
            var list=(GenericInstanceType)Find(bridge,"OnPopulate").Parameters[0].ParameterType;var renderer=list.GenericArguments[0];
            var ilist=new GenericInstanceType(mod.ImportReference(typeof(System.Collections.Generic.IList<>)));ilist.GenericArguments.Add(renderer);
            old.Parameters.Add(new ParameterDefinition("__result",Mono.Cecil.ParameterAttributes.None,ilist));
            var enumerable=new GenericInstanceType(mod.ImportReference(typeof(System.Collections.Generic.IEnumerable<>)));enumerable.GenericArguments.Add(renderer);
            var listCtor=new MethodReference(".ctor",mod.TypeSystem.Void,list){HasThis=true};
            listCtor.Parameters.Add(new ParameterDefinition(new GenericInstanceType(mod.ImportReference(typeof(System.Collections.Generic.IEnumerable<>)))));
            ((GenericInstanceType)listCtor.Parameters[0].ParameterType).GenericArguments.Add(list.ElementType.Resolve().GenericParameters[0]);
            Body(old);var oi=old.Body.GetILProcessor();oi.Emit(OpCodes.Ldarg_0);oi.Emit(OpCodes.Newobj,listCtor);oi.Emit(OpCodes.Call,Find(bridge,"OnPopulate"));oi.Emit(OpCodes.Ret);Attr(mod,old,harmony,"HarmonyPostfix");
            var target=new MethodDefinition("CodexMe5Target",Mono.Cecil.MethodAttributes.Private|Mono.Cecil.MethodAttributes.Static,mod.ImportReference(typeof(MethodBase)));hooks.Methods.Add(target);Body(target);Attr(mod,target,harmony,"HarmonyTargetMethod");
            var ti=target.Body.GetILProcessor();ti.Emit(OpCodes.Ldtoken,mod.ImportReference(meUi));ti.Emit(OpCodes.Call,mod.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle")));ti.Emit(OpCodes.Callvirt,mod.ImportReference(typeof(Type).GetProperty("Assembly").GetGetMethod()));ti.Emit(OpCodes.Ldstr,"MaterialEditorAPI.MaterialEditorPresenter");ti.Emit(OpCodes.Ldc_I4_1);ti.Emit(OpCodes.Callvirt,mod.ImportReference(typeof(Assembly).GetMethod("GetType",new[]{typeof(string),typeof(bool)})));ti.Emit(OpCodes.Ldstr,"SelectRenderers");ti.Emit(OpCodes.Ldc_I4,(int)(BindingFlags.Instance|BindingFlags.NonPublic));ti.Emit(OpCodes.Callvirt,mod.ImportReference(typeof(Type).GetMethod("GetMethod",new[]{typeof(string),typeof(BindingFlags)})));ti.Emit(OpCodes.Ret);
            var clearType=new TypeDefinition("BlendShapeEditor","CodexMe5ClearHooks",Mono.Cecil.TypeAttributes.NotPublic|Mono.Cecil.TypeAttributes.Abstract|Mono.Cecil.TypeAttributes.Sealed,mod.TypeSystem.Object);mod.Types.Add(clearType);
            var window=mod.GetType("BlendShapeEditor.ShapeEditorWindow");
            var clearSelection=new MethodDefinition("CodexClearRendererSelection",Mono.Cecil.MethodAttributes.Assembly,mod.TypeSystem.Void);window.Methods.Add(clearSelection);Body(clearSelection);
            var sel=clearSelection.Body.GetILProcessor();var keepSelection=Instruction.Create(OpCodes.Ret);sel.Emit(OpCodes.Ldarg_0);sel.Emit(OpCodes.Call,Find(window,"get_MatEditFilter"));sel.Emit(OpCodes.Brfalse,keepSelection);sel.Emit(OpCodes.Ldarg_0);sel.Emit(OpCodes.Ldc_I4_M1);sel.Emit(OpCodes.Stfld,window.Fields.Single(f=>f.Name=="SelectedRendererIndex"));sel.Emit(OpCodes.Ldarg_0);
            var extra=window.Fields.Single(f=>f.Name=="_extraSelectedRenderers");sel.Emit(OpCodes.Ldfld,extra);sel.Emit(OpCodes.Callvirt,new MethodReference("Clear",mod.TypeSystem.Void,extra.FieldType){HasThis=true});sel.Append(keepSelection);
            var populate=Find(bridge,"OnPopulate");var pi=populate.Body.GetILProcessor();var first=populate.Body.Instructions.First();
            var seq=new GenericInstanceMethod(mod.ImportReference(typeof(Enumerable).GetMethods().Single(m=>m.Name=="SequenceEqual"&&m.GetParameters().Length==2)));seq.GenericArguments.Add(renderer);
            pi.InsertBefore(first,Instruction.Create(OpCodes.Call,Find(bridge,"get_CurrentlyVisibleRenderers")));pi.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));pi.InsertBefore(first,Instruction.Create(OpCodes.Call,seq));pi.InsertBefore(first,Instruction.Create(OpCodes.Brtrue,first));
            foreach(string name in new[]{"StudioUI","MakerUI"}){
                var next=Instruction.Create(OpCodes.Nop);var exists=Instruction.Create(OpCodes.Call,clearSelection);
                pi.InsertBefore(first,Instruction.Create(OpCodes.Call,Find(mod.GetType("BlendShapeEditor."+name),"get_Window")));pi.InsertBefore(first,Instruction.Create(OpCodes.Dup));pi.InsertBefore(first,Instruction.Create(OpCodes.Brtrue,exists));pi.InsertBefore(first,Instruction.Create(OpCodes.Pop));pi.InsertBefore(first,Instruction.Create(OpCodes.Br,next));pi.InsertBefore(first,exists);pi.InsertBefore(first,next);
            }
            var clear=new MethodDefinition("Postfix",Mono.Cecil.MethodAttributes.Public|Mono.Cecil.MethodAttributes.Static,mod.TypeSystem.Void);clearType.Methods.Add(clear);Body(clear);var cli=clear.Body.GetILProcessor();cli.Emit(OpCodes.Newobj,new MethodReference(".ctor",mod.TypeSystem.Void,list){HasThis=true});cli.Emit(OpCodes.Call,Find(bridge,"OnPopulate"));cli.Emit(OpCodes.Ret);Attr(mod,clear,harmony,"HarmonyPostfix");
            var patchCtor=harmony.MainModule.GetType("HarmonyLib.HarmonyPatch").Methods.Single(m=>m.IsConstructor&&m.Parameters.Count==2&&m.Parameters[0].ParameterType.FullName=="System.Type"&&m.Parameters[1].ParameterType.FullName=="System.String");
            var patch=new CustomAttribute(mod.ImportReference(patchCtor));patch.ConstructorArguments.Add(new CustomAttributeArgument(mod.ImportReference(typeof(Type)),mod.ImportReference(meUi)));patch.ConstructorArguments.Add(new CustomAttributeArgument(mod.TypeSystem.String,"ReleaseRetainedTargetContext"));clear.CustomAttributes.Add(patch);
            var awake=Find(mod.GetType("BlendShapeEditor.BlendShapeEditorPlugin"),"Awake");var install=awake.Body.Instructions.Single(i=>i.OpCode==OpCodes.Call&&i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="CreateAndPatchAll");var pop=install.Next;Need(pop.OpCode==OpCodes.Pop,"patch install result changed");
            var ai=awake.Body.GetILProcessor();var insertion=new[]{Instruction.Create(OpCodes.Ldtoken,clearType),Instruction.Create(OpCodes.Call,mod.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle"))),Instruction.Create(OpCodes.Ldstr,"org.njaecha.plugins.blendshapeeditor"),Instruction.Create(OpCodes.Call,(MethodReference)install.Operand),Instruction.Create(OpCodes.Pop)};foreach(var i in insertion){ai.InsertAfter(pop,i);pop=i;}
            var core=mod.AssemblyReferences.Single(r=>r.Name=="mscorlib"&&r.Version.Major==4);
            foreach(var tr in mod.GetTypeReferences())Normalize(tr,core);
            foreach(var ty in mod.GetTypes()){
                foreach(var fld in ty.Fields)Normalize(fld.FieldType,core);
                foreach(var method in ty.Methods){
                    NormalizeMember(method,core);
                    foreach(var attr in method.CustomAttributes){NormalizeMember(attr.Constructor,core);foreach(var arg in attr.ConstructorArguments){Normalize(arg.Type,core);if(arg.Value is TypeReference)Normalize((TypeReference)arg.Value,core);}}
                    if(method.HasBody){foreach(var variable in method.Body.Variables)Normalize(variable.VariableType,core);foreach(var op in method.Body.Instructions){if(op.Operand is MemberReference)NormalizeMember((MemberReference)op.Operand,core);if(op.Operand is TypeReference)Normalize((TypeReference)op.Operand,core);}}
                }
            }
            foreach(var ar in mod.AssemblyReferences.Where(r=>r.Name=="mscorlib"&&!ReferenceEquals(r,core)).ToArray())mod.AssemblyReferences.Remove(ar);
            using(var stream=new MemoryStream()){
                a.Write(stream);
                string outputHash=BytesHash(stream.ToArray());
                Need(outputHash=="cf2c8a6b08c5148e2253dd8d6f32563f2320f70a7184b81ae3e80ac1372239dd","Output differs from reviewed artifact");
                stream.Position=0;
                using(var check=AssemblyDefinition.ReadAssembly(stream)){
                    Need(check.MainModule.AssemblyReferences.Count(r=>r.Name=="mscorlib")==1&&check.MainModule.AssemblyReferences.Single(r=>r.Name=="mscorlib").Version.Major==4,"Core library scope changed");
                    Need(!check.MainModule.AssemblyReferences.Any(r=>r.Name=="System.Private.CoreLib"||r.Name=="System.Runtime"),"Host framework leaked into target");
                    WriteReceipt(input,mePath,outputDir,check,outputHash);
                }
                string output=Path.Combine(outputDir,"KKS_BlendShapeEditor.dll");
                using(var dest=new FileStream(output,FileMode.CreateNew,FileAccess.Write))dest.Write(stream.ToArray(),0,(int)stream.Length);
                Console.WriteLine("Candidate SHA256 "+outputHash);
            }
        }
        resolver.Dispose();
    }
}
