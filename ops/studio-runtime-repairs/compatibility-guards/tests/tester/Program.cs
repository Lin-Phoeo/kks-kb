using Mono.Cecil;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
if (args.Length != 2) throw new ArgumentException("fixture.dll output-directory");
var fixture = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new InvalidOperationException("Use a new output directory.");
Directory.CreateDirectory(output);
foreach (var mode in new[]{"shallty","sexfaces","kls"})
{
    var original = Path.Combine(output, mode, "original");
    var patched = Path.Combine(output, mode, "patched");
    Directory.CreateDirectory(original);
    Directory.CreateDirectory(patched);
    File.Copy(fixture, Path.Combine(original, "Fixture.dll"));
    using var assembly = AssemblyDefinition.ReadAssembly(fixture);
    GuardPatches.Apply(assembly, mode);
    assembly.Write(Path.Combine(patched, "Fixture.dll"));
}
var results=new List<object>(); var failures=0;
foreach(var mode in new[]{"shallty","sexfaces","kls"}) foreach(var variant in new[]{"original","patched"}) {
 var alc=new AssemblyLoadContext(mode+variant,true);
 var path=Path.GetFullPath(Path.Combine(output,mode,variant,"Fixture.dll"));
 var a=alc.LoadFromAssemblyPath(path);
 void Check(string name,bool pass) { results.Add(new{mode,variant,name,pass}); if(!pass)failures++; }
 bool Throws(Action action,Type expected) { try{action();return false;}catch(Exception ex){while(ex is TargetInvocationException && ex.InnerException!=null)ex=ex.InnerException;return expected.IsAssignableFrom(ex.GetType());} }
 if(mode=="shallty") {
  var t=a.GetType("ShalltyUtils.KeyframesGroups")!;var data=a.GetType("ExtensibleSaveFormat.PluginData")!;var method=t.GetMethod("LoadSceneData")!;
  method.Invoke(null,new object[]{null}); Check("null plugin supported",(int)t.GetField("Clears")!.GetValue(null)==0);
  var d=Activator.CreateInstance(data)!;
  var missing=Throws(()=>method.Invoke(null,new[]{d}),typeof(KeyNotFoundException));Check("missing optional field",missing==(variant=="original"));
  var dict=(Dictionary<string,object>)data.GetField("data")!.GetValue(d)!;dict["keyframesGroupsData"]="<root/>";
  method.Invoke(null,new[]{d});Check("valid field retained",(string)t.GetField("Last")!.GetValue(null)=="<root/>");
  dict["keyframesGroupsData"]=42;var invalid=Throws(()=>method.Invoke(null,new[]{d}),typeof(InvalidCastException));Check("nonstring optional field",invalid==(variant=="original"));
  data.GetField("data")!.SetValue(d,null);var absent=Throws(()=>method.Invoke(null,new[]{d}),typeof(NullReferenceException));Check("null dictionary",absent==(variant=="original"));
 }
 if(mode=="sexfaces") {
  var t=a.GetType("UnityEngine.RangeSlider")!;var gui=a.GetType("UnityEngine.GUI")!;
  var early=Throws(()=>RuntimeHelpers.RunClassConstructor(t.TypeHandle),typeof(TypeInitializationException));Check("type initialization outside GUI",early==(variant=="original"));
  gui.GetField("OnGUI")!.SetValue(null,true);
  var poisoned=Throws(()=>t.GetMethod("Create")!.Invoke(null,null),typeof(TypeInitializationException));Check("first GUI after early type initialization",poisoned==(variant=="original"));
  if(variant=="patched") {var reads=(int)gui.GetField("Reads")!.GetValue(null);t.GetMethod("Create")!.Invoke(null,null);Check("styles cached after first draw",reads==(int)gui.GetField("Reads")!.GetValue(null)&&reads==2);}
 }
 if(mode=="kls") {
  var t=a.GetType("Buggsy.Quill.KoikatsuSunshineLightingSuite.LightingRegistryProbe")!;var map=a.GetType("Studio.Map")!;var sun=a.GetType("Studio.SunLightInfo")!;var obj=Activator.CreateInstance(t)!;
  var empty=Activator.CreateInstance(map)!;t.GetField("Map")!.SetValue(null,empty);
  var bad=Throws(()=>t.GetMethod("DumpEnvironmentRecordCore")!.Invoke(obj,null),typeof(NullReferenceException));Check("empty map has no sun data",bad==(variant=="original"));
  map.GetProperty("sunLightInfo")!.SetValue(empty,Activator.CreateInstance(sun));
  t.GetMethod("DumpEnvironmentRecordCore")!.Invoke(obj,null);Check("valid sun data retained",(int)t.GetField("Success")!.GetValue(null)>=1);
 }
 alc.Unload();
}
File.WriteAllText(Path.Combine(output,"test-results.json"),JsonSerializer.Serialize(new{failures,results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{failures,total=results.Count,results}));
return failures==0?0:1;
