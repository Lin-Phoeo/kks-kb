using System.Reflection;
using System.Text.Json;
namespace UnityEngine{
 public class Object{public bool Destroyed;public static bool operator ==(Object a,Object b)=>(ReferenceEquals(a,null)||a.Destroyed)&&(ReferenceEquals(b,null)||b.Destroyed)||ReferenceEquals(a,b);public static bool operator !=(Object a,Object b)=>!(a==b);public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>base.GetHashCode();}
 public class MonoBehaviour:Object{}
 public static class Time{public static float get_realtimeSinceStartup()=>10;}
 public static class Mathf{public static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);public static float Repeat(float v,float max)=>v%max;}
}
namespace UnityEngine.UI{public class InputField:UnityEngine.Object{public string text{get;set;}}}
public class ChaFile{public ChaFileCoordinate[] coordinate{get;set;}}
public class ChaFileControl:ChaFile{}
public class ChaFileStatus{public int coordinateType{get;set;}}
public class ChaInfo:UnityEngine.MonoBehaviour{public ChaFileControl chaFile{get;set;}public ChaFileStatus fileStatus{get;set;}}
public class ChaControl:ChaInfo{}
public class ChaFileCoordinate{public ChaFileAccessory accessory;}
public class ChaFileAccessory{public PartsInfo[] parts{get;set;}public class PartsInfo{public int type{get;set;}=120;}}
namespace BepInEx{[AttributeUsage(AttributeTargets.Class)]public class BepInPlugin:Attribute{public string GUID=>ProbeEnv.Guid;public Version Version=>new(ProbeEnv.Version);}}
namespace PseudoMaker{
 [BepInEx.BepInPlugin]public class PseudoMaker{}
 public static class Hooks{public static void ChangeCoordinateTypePrefix(ChaControl c)=>throw new Exception("unpopulated");}
}
namespace PseudoMaker.UI{
 public class InputFieldComponent:UnityEngine.MonoBehaviour{
  private bool shouldNotUpdate=false;private UnityEngine.UI.InputField inputField=new();
  public float MinValue=-1,MaxValue=2;public bool Repeat=false;
  public Func<float> GetCurrentValue;public Action<float> SetValueAction;
  public string Shown=>inputField.text;public void RefreshFlag(bool value)=>shouldNotUpdate=value;
  public void OnEnable()=>throw new Exception("unpopulated");
  public void UpdateValue(float value)=>throw new Exception("unpopulated");
  private float ClampValue(float value)=>throw new Exception("unpopulated");
 }
}
public class HookLambda{public static readonly HookLambda Instance=new();public static Predicate<ChaFileAccessory.PartsInfo> Cached=null;public bool Predicate(ChaFileAccessory.PartsInfo p)=>throw new Exception("unpopulated");}
namespace Freelancer604.Wardrobe{
 public interface ICleanupSourceBackend{}
 public static class WardrobePseudoMakerGuards{
  public static FieldInfo refreshField=null,inputField=null;public static float nextProbe=0;public static bool installed=false,unsupported=false,displayInstalled=false,trimInstalled=false;
  public static void EnsureInstalled()=>throw new Exception("unpopulated");
  public static bool BindInput(Type type)=>throw new Exception("unpopulated");
  public static bool DisplayRefreshPrefix(object instance,float value)=>throw new Exception("unpopulated");
  public static ChaFileCoordinate Current(ChaControl c)=>throw new Exception("unpopulated");
  public static bool CoordinateTrimPrefix(ChaControl c)=>throw new Exception("unpopulated");
  public static int CaptureReservation(ChaControl c)=>ProbeEnv.Reserved;
  private static bool Install(MethodInfo target,string handler){ProbeEnv.Patches.Add(handler);return true;}
 }
}
namespace Freelancer604.MakeUp{
 public static class MakeUpPlugin{public static void LogMessage(string text){}}
 public class LegacyOutfitCleanupBackend:Freelancer604.Wardrobe.ICleanupSourceBackend{
  public MethodInfo traceItem=null,siblings=null,studioItemCount=null,terminate=null,undoLast=null,isItemBanned=null;
  public PropertyInfo pendingCount=null;
  public static int Constructors;
  private LegacyOutfitCleanupBackend(Type type){Constructors++;}
  public static bool TryCreate(Assembly assembly,out Freelancer604.Wardrobe.ICleanupSourceBackend backend){backend=null;throw new Exception("unpopulated");}
 }
}
public static class ProbeEnv{
 public static string Version="1.5.2.0",Guid="com.rikkibalboa.bepinex.studioPseudoMaker",Hash="72e1c3a3dcb2120225f2d2823729cea81b2a2dffc8243de63ac6a4c901061882";
 public static int HashReads,Reserved;public static List<string>Patches=new();
 public static Assembly[] Assemblies()=>new[]{typeof(Program).Assembly};
 public static string ReadHash(){HashReads++;return Hash;}
}
public static class Program{
 public static int Main(string[]args){bool repaired=args[0]=="candidate";string scenario=args[1];if(scenario=="legacy")ProbeEnv.Version="1.5.1.1";if(scenario=="unknown")ProbeEnv.Hash=new string('0',64);if(scenario=="wrong-guid")ProbeEnv.Guid="wrong.guid";if(scenario=="future")ProbeEnv.Version="1.5.3.0";
 var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
 Freelancer604.Wardrobe.WardrobePseudoMakerGuards.EnsureInstalled();bool wanted=scenario=="legacy"||repaired&&scenario=="current";
 Check(Freelancer604.Wardrobe.WardrobePseudoMakerGuards.installed==wanted,"exact supported build is the only extra permitted version");
 Check(ProbeEnv.Patches.Count==(wanted?2:0),"both original compatibility prefixes remain paired");
 int reads=ProbeEnv.HashReads;Freelancer604.Wardrobe.WardrobePseudoMakerGuards.EnsureInstalled();Check(ProbeEnv.HashReads==reads,"subsequent updates do not read the file again");
 Check(reads==((repaired&&scenario is "current" or "unknown")?1:0),"legacy and wrong GUID require no file hash");
 float current=3.4f;int writes=0;var field=new PseudoMaker.UI.InputFieldComponent{GetCurrentValue=()=>current,SetValueAction=v=>{current=v;writes++;}};field.RefreshFlag(true);
 if(!wanted||Freelancer604.Wardrobe.WardrobePseudoMakerGuards.DisplayRefreshPrefix(field,current))field.UpdateValue(current);
 Check(wanted?current==3.4f&&writes==0:current==2f&&writes==1,"display refresh does not mutate an out-of-range existing value when protected");
 if(wanted){Check(field.Shown==3.4f.ToString("0.000"),"protected display reflects actual value");field.RefreshFlag(false);Check(Freelancer604.Wardrobe.WardrobePseudoMakerGuards.DisplayRefreshPrefix(field,8f),"interactive edits still use original handler");field.UpdateValue(8f);Check(current==2f,"interactive bounds remain unchanged");}
 ChaControl Character(int count)=>new(){fileStatus=new(),chaFile=new(){coordinate=new[]{new ChaFileCoordinate{accessory=new(){parts=Enumerable.Range(0,count).Select(_=>new ChaFileAccessory.PartsInfo()).ToArray()}}}}};
 var c=Character(30);ProbeEnv.Reserved=30;if(!wanted||Freelancer604.Wardrobe.WardrobePseudoMakerGuards.CoordinateTrimPrefix(c))PseudoMaker.Hooks.ChangeCoordinateTypePrefix(c);
 Check(c.chaFile.coordinate[0].accessory.parts.Length==(wanted?30:20),"reserved tail slots survive coordinate transition only when protected");
 c=Character(30);ProbeEnv.Reserved=0;if(!wanted||Freelancer604.Wardrobe.WardrobePseudoMakerGuards.CoordinateTrimPrefix(c))PseudoMaker.Hooks.ChangeCoordinateTypePrefix(c);Check(c.chaFile.coordinate[0].accessory.parts.Length==20,"unreserved original trimming stays enabled");
 c=Character(24);ProbeEnv.Reserved=30;Check(Freelancer604.Wardrobe.WardrobePseudoMakerGuards.CoordinateTrimPrefix(c),"reservation beyond current length delegates normally");
 Check(Freelancer604.Wardrobe.WardrobePseudoMakerGuards.Current(null)==null,"absent character stays safe");
 if(args.Length>3&&args[3]=="makeup"){
  Check(typeof(Program).Assembly.GetType("Freelancer604.Wardrobe.WardrobeCleanup")==null,"Free host fixture has no cleanup authority type");
  Check(!Freelancer604.MakeUp.LegacyOutfitCleanupBackend.TryCreate(typeof(Program).Assembly,out var backend)&&backend==null&&Freelancer604.MakeUp.LegacyOutfitCleanupBackend.Constructors==0,"actual cleanup factory stops before initialization when authority type is absent");
 }
 var report=new{mode=args[0],scenario,passed=true,checks=checks.Count,results=checks,hashReads=ProbeEnv.HashReads,patches=ProbeEnv.Patches,scope="Actual Vanity gate/guards and actual installed PseudoMaker UI/trim CIL; discovery/hash/native UI and reserved-count storage are synthetic. No live plugin/Unity or configuration operations."};File.WriteAllText(args[2],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{mode=args[0],scenario,passed=true,checks=checks.Count}));return 0;}
}
