using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;

namespace UnityEngine { public sealed class Renderer { public string Name; public Renderer(string n) { Name=n; } } }
namespace BlendShapeEditor {
 public class ShapeEditorWindow {
  public int SelectedRendererIndex; public bool MatEditFilter {get;set;}
  readonly HashSet<UnityEngine.Renderer> _extraSelectedRenderers=new();
  public int Extras=>_extraSelectedRenderers.Count;
  public void Select(UnityEngine.Renderer r){SelectedRendererIndex=1;_extraSelectedRenderers.Add(r);}
  public void CodexClearRendererSelection()=>throw new InvalidOperationException("not extracted");
 }
 public static class StudioUI { public static ShapeEditorWindow Window {get;set;} }
 public static class MakerUI { public static ShapeEditorWindow Window {get;set;} }
 public static class MaterialEditorBridge {
  public class InterfacePopulateArgs:EventArgs {public List<UnityEngine.Renderer> Renderers{get;private set;} public InterfacePopulateArgs(List<UnityEngine.Renderer> r){Renderers=r;} }
  public static EventHandler<InterfacePopulateArgs> UserInterfacePopulate;
  public static List<UnityEngine.Renderer> CurrentlyVisibleRenderers{get;private set;}=new();
  public static void OnPopulate(List<UnityEngine.Renderer> renderers)=>throw new InvalidOperationException("not extracted");
  public static class Hooks {public static void CodexMe5SelectedRenderersPostfix(IList<UnityEngine.Renderer> __result)=>throw new InvalidOperationException("not extracted");}
 }
 public static class CodexMe5ClearHooks {public static void Postfix()=>throw new InvalidOperationException("not extracted");}
}
public static class Program {
 public static int Main(string[]args){
  if(args[0]=="extract"){Extract(args[1],args[2]);return 0;}
  var checks=new List<object>();int failures=0;void Check(string n,bool p){checks.Add(new{name=n,passed=p});if(!p)failures++;}
  var a=new UnityEngine.Renderer("A");var b=new UnityEngine.Renderer("B");
  var sw=new BlendShapeEditor.ShapeEditorWindow{MatEditFilter=true};var mw=new BlendShapeEditor.ShapeEditorWindow{MatEditFilter=true};BlendShapeEditor.StudioUI.Window=sw;BlendShapeEditor.MakerUI.Window=mw;
  int events=0;List<UnityEngine.Renderer> published=null;BlendShapeEditor.MaterialEditorBridge.UserInterfacePopulate+=(_,e)=>{events++;published=e.Renderers;};
  var list=new List<UnityEngine.Renderer>{a,b};
  BlendShapeEditor.MaterialEditorBridge.Hooks.CodexMe5SelectedRenderersPostfix(list);
  Check("real postfix List<Renderer>(IList) generic call JIT",BlendShapeEditor.MaterialEditorBridge.CurrentlyVisibleRenderers.SequenceEqual(list));
  Check("snapshot copy not alias incoming IList",!ReferenceEquals(list,BlendShapeEditor.MaterialEditorBridge.CurrentlyVisibleRenderers));
  sw.Select(b);mw.Select(b);BlendShapeEditor.MaterialEditorBridge.Hooks.CodexMe5SelectedRenderersPostfix(new List<UnityEngine.Renderer>{a,b});
  Check("same sequence keeps Studio selection",sw.SelectedRendererIndex==1&&sw.Extras==1);Check("same sequence keeps Maker selection",mw.SelectedRendererIndex==1&&mw.Extras==1);
  BlendShapeEditor.MaterialEditorBridge.Hooks.CodexMe5SelectedRenderersPostfix(new List<UnityEngine.Renderer>{b,a});
  Check("reordered filtered list clears Studio",sw.SelectedRendererIndex==-1&&sw.Extras==0);Check("reordered filtered list clears Maker",mw.SelectedRendererIndex==-1&&mw.Extras==0);
  sw.MatEditFilter=false;sw.Select(a);mw.Select(a);BlendShapeEditor.MaterialEditorBridge.Hooks.CodexMe5SelectedRenderersPostfix(new List<UnityEngine.Renderer>{a});
  Check("filter off preserves unrelated primary and extras",sw.SelectedRendererIndex==1&&sw.Extras==1);Check("filter on clears stale selection",mw.SelectedRendererIndex==-1&&mw.Extras==0);
  sw.MatEditFilter=true;sw.Select(a);BlendShapeEditor.CodexMe5ClearHooks.Postfix();
  Check("release helper publishes empty",BlendShapeEditor.MaterialEditorBridge.CurrentlyVisibleRenderers.Count==0&&published.Count==0);Check("release clears filtered primary and extras",sw.SelectedRendererIndex==-1&&sw.Extras==0);
  BlendShapeEditor.StudioUI.Window=null;BlendShapeEditor.MakerUI.Window=null;BlendShapeEditor.MaterialEditorBridge.Hooks.CodexMe5SelectedRenderersPostfix(new List<UnityEngine.Renderer>{b});
  Check("no windows safe",BlendShapeEditor.MaterialEditorBridge.CurrentlyVisibleRenderers.Count==1);
  Check("all6 calls publish an event",events==6);
  File.WriteAllText(args[1],JsonSerializer.Serialize(new{passed=checks.Count-failures,failed=failures,checks,scope="Extracted candidate OnPopulate/postfix/clear-selection actual IL; synthetic Renderer/window/event boundaries; no Unity or game target loaded"},new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;
 }
 static void Extract(string input,string output){
  if(File.Exists(output))throw new Exception("output exists");
  if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant()!="cf2c8a6b08c5148e2253dd8d6f32563f2320f70a7184b81ae3e80ac1372239dd")throw new Exception("unreviewed candidate");
  if(Path.GetFileName(output)!="Probe.dll"||Path.GetDirectoryName(Path.GetFullPath(output))==Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))throw new Exception("isolated Probe.dll directory required");
  using var a=AssemblyDefinition.ReadAssembly(input);using var f=AssemblyDefinition.ReadAssembly(Assembly.GetExecutingAssembly().Location);var mod=f.MainModule;
  TypeDefinition Local(string n)=>mod.GetTypes().SingleOrDefault(t=>t.FullName==n);
  TypeReference T(TypeReference t){if(t is GenericParameter)return t;if(t is GenericInstanceType g){var n=new GenericInstanceType(mod.ImportReference(g.ElementType));foreach(var x in g.GenericArguments)n.GenericArguments.Add(T(x));return n;}return Local(t.FullName)??mod.ImportReference(t);}
  MethodReference MR(MethodReference m){if(m is GenericInstanceMethod g){var n=new GenericInstanceMethod(MR(g.ElementMethod));foreach(var x in g.GenericArguments)n.GenericArguments.Add(T(x));return n;}var l=Local(m.DeclaringType.FullName);if(l!=null)return l.Methods.Single(x=>x.Name==m.Name&&x.Parameters.Count==m.Parameters.Count);var c=mod.ImportReference(m);c.DeclaringType=T(m.DeclaringType);return c;}
  FieldReference FR(FieldReference x){var t=Local(x.DeclaringType.FullName);return t==null?mod.ImportReference(x):t.Fields.Single(y=>y.Name==x.Name);}
  foreach(var target in new[]{("BlendShapeEditor.MaterialEditorBridge","OnPopulate"),("BlendShapeEditor.MaterialEditorBridge/Hooks","CodexMe5SelectedRenderersPostfix"),("BlendShapeEditor.CodexMe5ClearHooks","Postfix"),("BlendShapeEditor.ShapeEditorWindow","CodexClearRendererSelection")}){
   var src=a.MainModule.GetTypes().Single(t=>t.FullName==target.Item1).Methods.Single(m=>m.Name==target.Item2);var dst=Local(target.Item1).Methods.Single(m=>m.Name==target.Item2);dst.Body=new Mono.Cecil.Cil.MethodBody(dst){InitLocals=src.Body.InitLocals,MaxStackSize=src.Body.MaxStackSize};var vars=src.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(T(v.VariableType)));foreach(var v in vars.Values)dst.Body.Variables.Add(v);var map=src.Body.Instructions.ToDictionary(i=>i,_=>Instruction.Create(OpCodes.Nop));foreach(var i in src.Body.Instructions){var j=map[i];j.OpCode=i.OpCode;j.Operand=i.Operand switch{Instruction b=>map[b],Instruction[] bs=>bs.Select(b=>map[b]).ToArray(),VariableDefinition v=>vars[v],ParameterDefinition p=>dst.Parameters[p.Index],MethodReference m=>MR(m),FieldReference x=>FR(x),TypeReference t=>T(t),_=>i.Operand};dst.Body.Instructions.Add(j);}if(src.Body.HasExceptionHandlers)throw new Exception("unexpected EH");
  }
  f.Write(output);File.WriteAllText(output+".input-sha256",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant());
 }
}
