using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;

namespace UnityEngine
{
 public static class Time{public static float get_unscaledTime()=>10f;}
 public static class Debug{public static readonly List<string> Messages=new();public static void Log(object value)=>Messages.Add(value.ToString());}
}
namespace HarmonyLib
{
 public enum HarmonyPatchType{All}
 public class HarmonyMethod{public HarmonyMethod(Type type,string method,Type[] arguments){}}
 public sealed class Harmony
 {
  public readonly List<string> Patches=new();
  public MethodInfo Patch(MethodBase original,HarmonyMethod prefix,HarmonyMethod postfix,HarmonyMethod transpiler,HarmonyMethod finalizer,HarmonyMethod ilmanipulator){Patches.Add(original.Name);return original as MethodInfo;}
 }
 public static class AccessTools
 {
  private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
  public static MethodInfo DeclaredMethod(Type type,string name,Type[] args,Type[] generics)=>type.GetMethod(name,All|BindingFlags.DeclaredOnly);
  public static FieldInfo Field(Type type,string name)=>type.GetField(name,All);
  public static PropertyInfo Property(Type type,string name)=>type.GetProperty(name,All);
 }
}
namespace Freelancer604.DragDoll.Standard
{
 internal static class ExtraMoveAxisInputBridge
 {
  internal static bool installed=false,unavailable=false;
  internal static float retryAt=0;
  internal static readonly Dictionary<string,FieldInfo> fields=new();
  internal static PropertyInfo show=null,rotationUndoActive=null;
  internal static readonly string[] required={"_selectedObjects","_extraGizmoDragActive","_rightDragActive","_rightDragGuideObject","_customDragActive","_gizmoRoot","_camera"};
  internal static Exception Error;
  internal static void Tick(HarmonyLib.Harmony harmony)=>throw new InvalidOperationException("Unpopulated actual Tick fixture");
  private static void Disable(Exception error){unavailable=true;Error=error;}
 }
 internal static class FkHingeMarkerDrag{internal static void Install(HarmonyLib.Harmony h,Type type)=>ProbeEnv.Installers.Add("hinge");}
 internal static class ShoulderRotationInput{internal static void Install(HarmonyLib.Harmony h,Type type)=>ProbeEnv.Installers.Add("shoulder");}
 internal static class ExtraMoveAxisCounterRotation{internal static void Install(HarmonyLib.Harmony h,Type type,FieldInfo field)=>ProbeEnv.Installers.Add("counter");}
 internal static class ExtraMoveAxisHingeDrag{internal static void Install(HarmonyLib.Harmony h,Type type,FieldInfo field)=>ProbeEnv.Installers.Add("hinge-drag");}
}
public static class ProbeEnv
{
 public static Type Axis;
 public static string Hash;
 public static readonly List<string> Installers=new();
 public static Type FindAxis()=>Axis;
}
public static class Program
{
 public static int Main(string[] args)
 {
  string mode=args[0],scenario=args[1];bool repaired=mode=="candidate";
  var checks=new List<object>();void Check(bool result,string name){checks.Add(new{name,passed=result});if(!result)throw new Exception(name);}
  bool plus=scenario is "plus" or "unknown-field";
  var dynamicAssembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("KKS_StudioExtraMoveAxis"),AssemblyBuilderAccess.Run);
  var module=dynamicAssembly.DefineDynamicModule("synthetic");var type=module.DefineType("StudioExtraMoveAxis.StudioExtraMoveAxisPlugin",TypeAttributes.Public);
  var update=type.DefineMethod("Update",MethodAttributes.Private,typeof(void),Type.EmptyTypes);update.GetILGenerator().Emit(OpCodes.Ret);
  if(plus){var reset=type.DefineMethod("HandleMiddleClickReset",MethodAttributes.Private,typeof(void),Type.EmptyTypes);reset.GetILGenerator().Emit(OpCodes.Ret);
   foreach(string field in Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge.required){if(scenario=="unknown-field"&&field=="_customDragActive")continue;type.DefineField(field,typeof(object),FieldAttributes.Static|FieldAttributes.Private);}
   var getter=type.DefineMethod("get_ShowGizmo",MethodAttributes.Static|MethodAttributes.Public,typeof(bool),Type.EmptyTypes);getter.GetILGenerator().Emit(OpCodes.Ldc_I4_1);getter.GetILGenerator().Emit(OpCodes.Ret);var property=type.DefineProperty("ShowGizmo",PropertyAttributes.None,typeof(bool),Type.EmptyTypes);property.SetGetMethod(getter);
  }
  ProbeEnv.Axis=scenario=="absent"?null:type.CreateType();
  ProbeEnv.Hash=scenario=="base"?"c75421f9f12e23b797883469b313c794568db8c2242c44e30a1541360170cc74":new string('0',64);
  var harmony=new HarmonyLib.Harmony();Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge.Tick(harmony);
  var bridge=typeof(Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge);
  bool installed=Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge.installed,unavailable=Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge.unavailable;
  var error=Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge.Error;
  if(scenario=="base"){
   Check(!installed&&unavailable,"base never binds a Plus-only input contract");Check(repaired?error==null:error?.Message=="Extra Move Axis input contract changed.","original base failure versus repaired known-base classification");
   Check(harmony.Patches.Count==0&&ProbeEnv.Installers.Count==0,"base does not partially patch input methods");Check(UnityEngine.Debug.Messages.Count==(repaired?1:0),"known-base informational decision emitted once");
   Freelancer604.DragDoll.Standard.ExtraMoveAxisInputBridge.Tick(harmony);Check(UnityEngine.Debug.Messages.Count==(repaired?1:0),"no repeat classification or hash-read loop");
  }else if(scenario=="plus"){
   Check(installed&&!unavailable&&error==null,"complete unknown-hash Plus contract remains supported");Check(harmony.Patches.SequenceEqual(new[]{"HandleMiddleClickReset","Update"}),"original two direct Harmony registrations preserved");
   Check(ProbeEnv.Installers.SequenceEqual(new[]{"hinge","shoulder","counter","hinge-drag"}),"all four original secondary installers preserved");Check(UnityEngine.Debug.Messages.Count==0,"Plus is not mislabeled as known base");
  }else if(scenario=="absent"){
   Check(!installed&&!unavailable&&error==null,"absent optional integration waits normally");Check(harmony.Patches.Count==0,"absent integration writes no patch");
  }else{
   Check(!installed&&unavailable&&error!=null,"unknown missing contract still rejected");Check(harmony.Patches.Count==0,"unknown partial contract cannot patch");Check(UnityEngine.Debug.Messages.Count==0,"unknown build is not hidden as not-applicable");
  }
  var report=new{mode,scenario,passed=true,checks=checks.Count,results=checks,installed,unavailable,error=error?.Message,patches=harmony.Patches,secondaryInstallers=ProbeEnv.Installers,info=UnityEngine.Debug.Messages,
   scope="Actual Tick CIL and actual imported identity helper. Assembly discovery and file-hash input substituted; Harmony registration/native plugins are synthetic. No real Unity, plugin installation or input interaction."};
  File.WriteAllText(args[2],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{mode,scenario,passed=true,checks=checks.Count,error=error?.Message}));return 0;
 }
}
