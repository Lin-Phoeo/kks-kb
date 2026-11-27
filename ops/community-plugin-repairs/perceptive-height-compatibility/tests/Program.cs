using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
namespace BepInEx { public class PluginInfo { public object Instance {get;set;} } }
namespace BepInEx.Bootstrap { public static class Chainloader { public static Dictionary<string,BepInEx.PluginInfo> PluginInfos {get;}=new(); } }
namespace Screencap
{
 public class ScreenshotManager
 {
  public static Action Pre,Post;
  public static bool FailAddPost,FailRemovePre;
  public static int PreCount=>Pre?.GetInvocationList().Length??0;
  public static int PostCount=>Post?.GetInvocationList().Length??0;
  public static event Action OnPreCapture {add{Pre+=value;}remove{if(FailRemovePre)throw new Exception("remove fault");Pre-=value;}}
  public static event Action OnPostCapture {add{Post+=value;if(FailAddPost)throw new Exception("add fault");}remove{Post-=value;}}
  public static void Reset(){Pre=null;Post=null;FailAddPost=false;FailRemovePre=false;BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();}
 }
}
namespace PerceptiveHeightX
{
 public class PerceptiveHeightXPlugin
 {
  private bool _forceHide; private object _captureHook;
  private void Awake(){}
  private void OnEnable(){}
  private void OnDisable(){}
  private void OnDestroy(){}
  private void BeforeCallback(){_forceHide=true;}
  private void AfterCallback(){_forceHide=false;}
  public bool Hidden=>_forceHide;
  public void Start(){Awake();OnEnable();}
  public void Enable()=>OnEnable();public void Disable()=>OnDisable();public void Destroy()=>OnDestroy();
 }
}
internal static class Program
{
 static int Main(string[] args)
 {
  var results=new List<object>();
  bool fallbackExpected=typeof(Program).Assembly.GetName().Name!="CaptureProbe";
  void Test(string name,Action action){try{Screencap.ScreenshotManager.Reset();action();results.Add(new{name,passed=true});}catch(Exception e){results.Add(new{name,passed=false,error=e.ToString()});}}
  void Check(bool b,string reason){if(!b)throw new Exception(reason);}
  void Register(){BepInEx.Bootstrap.Chainloader.PluginInfos["com.bepis.bepinex.screenshotmanager"]=new(){Instance=new Screencap.ScreenshotManager()};}
  Test("GUID resolves current screenshot assembly and pair",()=>{Register();var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();Check(Screencap.ScreenshotManager.PreCount==1&&Screencap.ScreenshotManager.PostCount==1,"missing or duplicate event pair");Screencap.ScreenshotManager.Pre();Check(p.Hidden,"pre did not hide");Screencap.ScreenshotManager.Post();Check(!p.Hidden,"post did not restore");});
  Test("known assembly fallback or missing optional plugin",()=>{var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();Check(Screencap.ScreenshotManager.PreCount==(fallbackExpected?1:0)&&Screencap.ScreenshotManager.PostCount==(fallbackExpected?1:0),"fallback mismatch");});
  Test("repeated enable does not duplicate",()=>{Register();var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();p.Enable();p.Enable();Check(Screencap.ScreenshotManager.PreCount==1&&Screencap.ScreenshotManager.PostCount==1,"duplicated handlers");});
  Test("disable during capture releases both and unhides",()=>{Register();var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();Screencap.ScreenshotManager.Pre?.Invoke();p.Disable();Check(Screencap.ScreenshotManager.PreCount==0&&Screencap.ScreenshotManager.PostCount==0&&!p.Hidden,"disable failed");});
  Test("re-enable restores exactly one pair",()=>{Register();var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();p.Disable();p.Enable();Check(Screencap.ScreenshotManager.PreCount==1&&Screencap.ScreenshotManager.PostCount==1,"re-enable failed");});
  Test("destroy removes pair",()=>{Register();var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();p.Destroy();p.Destroy();Check(Screencap.ScreenshotManager.PreCount==0&&Screencap.ScreenshotManager.PostCount==0&&!p.Hidden,"destroy failed");});
  Test("second add throws after subscription rolls back both",()=>{Register();Screencap.ScreenshotManager.FailAddPost=true;var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();Check(Screencap.ScreenshotManager.PreCount==0&&Screencap.ScreenshotManager.PostCount==0&&!p.Hidden,"partial subscription leaked");});
  Test("failed removal is inactive and retried",()=>{Register();var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();Screencap.ScreenshotManager.FailRemovePre=true;p.Disable();Screencap.ScreenshotManager.Pre?.Invoke();Check(!p.Hidden&&Screencap.ScreenshotManager.PostCount==0,"stale handler acted");Screencap.ScreenshotManager.FailRemovePre=false;p.Enable();Check(Screencap.ScreenshotManager.PreCount==1&&Screencap.ScreenshotManager.PostCount==1,"retry duplicated or missing");p.Destroy();Check(Screencap.ScreenshotManager.PreCount==0,"retry did not clean");});
  Test("independent plugin lifetimes",()=>{Register();var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();var q=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();q.Start();p.Disable();Check(Screencap.ScreenshotManager.PreCount==1&&Screencap.ScreenshotManager.PostCount==1,"removed other owner");Screencap.ScreenshotManager.Pre();Check(!p.Hidden&&q.Hidden,"owner isolation");q.Destroy();});
  Test("unsupported screenshot API remains harmless",()=>{BepInEx.Bootstrap.Chainloader.PluginInfos["com.bepis.bepinex.screenshotmanager"]=new(){Instance=FakeScreenshot(false)};var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();p.Disable();p.Destroy();Check(!p.Hidden,"unsupported plugin changed HUD");});
  Test("wrong event delegate type remains harmless",()=>{BepInEx.Bootstrap.Chainloader.PluginInfos["com.bepis.bepinex.screenshotmanager"]=new(){Instance=FakeScreenshot(true)};var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();Check(!p.Hidden,"wrong event signature changed HUD");p.Destroy();});
  Test("retry after optional API becomes available",()=>{BepInEx.Bootstrap.Chainloader.PluginInfos["com.bepis.bepinex.screenshotmanager"]=new(){Instance=FakeScreenshot(false)};var p=new PerceptiveHeightX.PerceptiveHeightXPlugin();p.Start();p.Disable();Register();p.Enable();Check(Screencap.ScreenshotManager.PreCount==1&&Screencap.ScreenshotManager.PostCount==1,"later availability not hooked");p.Destroy();});
  int failures=results.Count(x=>!(bool)x.GetType().GetProperty("passed").GetValue(x));var report=new{checks=results.Count,failures,results,scope="Actual extracted subscription/lifecycle/helper CIL; synthetic registry and events. No Unity or screenshot capture."};File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));return failures==0?0:1;
 }
 static object FakeScreenshot(bool wrongEvents)
 {
  var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UnsupportedScreenshot"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.Run);
  var type=assembly.DefineDynamicModule("test").DefineType("Screencap.ScreenshotManager",TypeAttributes.Public);type.DefineDefaultConstructor(MethodAttributes.Public);
  if(wrongEvents)foreach(string name in new[]{"OnPreCapture","OnPostCapture"}){var ev=type.DefineEvent(name,EventAttributes.None,typeof(Action<int>));foreach(bool add in new[]{false,true}){var method=type.DefineMethod((add?"add_":"remove_")+name,MethodAttributes.Public|MethodAttributes.Static|MethodAttributes.SpecialName,typeof(void),new[]{typeof(Action<int>)});method.GetILGenerator().Emit(OpCodes.Ret);if(add)ev.SetAddOnMethod(method);else ev.SetRemoveOnMethod(method);}}
  return Activator.CreateInstance(type.CreateType());
 }
}
