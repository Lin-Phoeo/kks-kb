using System.Collections;
namespace UnityEngine {
 public class Object {public string name{get;set;}="synthetic";public static bool operator==(Object a,Object b)=>ReferenceEquals(a,b);public static bool operator!=(Object a,Object b)=>!ReferenceEquals(a,b);public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>base.GetHashCode();}
 public class Material:Object{}
 public class Renderer:Object{}
 public class MonoBehaviour:Object{}
 public class GameObject:Object {public GameObject gameObject=>this;public List<Material> Materials=new();public List<Renderer> Renderers=new();}
}
public class ChaFileStatus{public int coordinateType{get;set;}}
public class ChaFileParameter{public string fullname=>"synthetic-fixture";}
public class ChaInfo:UnityEngine.Object{public ChaFileStatus fileStatus{get;}=new();public ChaFileParameter fileParam{get;}=new();}
public class ChaControl:ChaInfo{}
public class ChaFileDefine{public enum ClothesKind{Top,Bottom,Other}}
namespace BepInEx.Logging {public class ManualLogSource{public List<string> Warnings=new();public void LogDebug(object o){}public void LogWarning(object o)=>Warnings.Add(o.ToString());}}
namespace Extension{
 public static class Logger{public static void LogError(string s)=>throw new InvalidOperationException(s);}
 public static class Linq{
  public static object ToListWithoutType(object self)=>new ArrayList((ICollection)self);
  public static List<T> ToList<T>(object self)=>((IEnumerable)self).Cast<T>().ToList();
  public static object Where(object self,Predicate<object> predicate)=>new ArrayList(((IEnumerable)self).Cast<object>().Where(x=>predicate(x)).ToArray());
  public static int Count(object self)=>((ICollection)self).Count;
  public static object ForEach(object self,Action<object> action)=>throw new NotImplementedException("Exact author ForEach CIL required");
 }
 public static class Reflection{
  public static object GetField(object self,string name)=>self.GetType().GetField(name).GetValue(self);
  public static object GetProperty(object self,string name)=>self.GetType().GetProperty(name).GetValue(self);
  public static object InvokeStatic(Type owner,string name,params object[] args)=>owner.GetMethod(name).Invoke(null,args);
  public static object Invoke(object target,string name,params object[] args){var controller=(CoordinateLoadOption.SyntheticController)target;if(!controller.WrapFailure)return controller.Invoke(name,args);try{return controller.Invoke(name,args);}catch{controller.WrapperErrors++;return null;}}
 }
}
namespace CoordinateLoadOption{
 public sealed class SavedProperty{public int ObjectType=1,CoordinateIndex,Slot;public string MaterialName="valid",RendererName="valid",ShaderName="synthetic-shader",Property="P";public object Value=1f;public object RenderQueueOriginal,RenderQueue,OffsetOriginal,Offset,ScaleOriginal,Scale;public int? TexID;}
 public sealed class SyntheticController:UnityEngine.MonoBehaviour{
  public readonly Dictionary<string,object> Lists=MaterialEditor.Empty();public int Writes,WrapperErrors;public bool ThrowNext,WrapFailure;
  public object Invoke(string name,object[] args){if(ThrowNext){ThrowNext=false;throw new InvalidOperationException("synthetic API failure");}Writes++;var list=(ArrayList)Lists[name.Contains("Renderer")?"RendererPropertyList":name.Contains("Float")?"MaterialFloatPropertyList":name.Contains("Color")?"MaterialColorPropertyList":name.Contains("Shader")?"MaterialShaderList":"MaterialTexturePropertyList"];if(name.StartsWith("Remove")){if(list.Count>0)list.RemoveAt(0);}else list.Add(new SavedProperty{Slot=(int)args[0]});return null;}
 }
 public class CharaCustomFunctionController_Support{
  public static BepInEx.Logging.ManualLogSource Logger=new();public ChaControl SourceChaCtrl,TargetChaCtrl;public UnityEngine.MonoBehaviour TargetController=new SyntheticController();public int SnapshotReads;
  public virtual object GetDataFromController(ChaControl c){SnapshotReads++;return ((SyntheticController)TargetController).Lists.ToDictionary(p=>p.Key,p=>(object)new ArrayList((ICollection)p.Value));}
  public virtual bool GetControllerAndBackupData(ChaControl source=null,ChaControl target=null){SourceChaCtrl=source??SourceChaCtrl;TargetChaCtrl=target??TargetChaCtrl;return true;}
 }
 public class MaterialEditor:CharaCustomFunctionController_Support{
  public enum ObjectType{Unknown,Clothing,Accessory,Hair,Character}
  public class StoredValueInfo{public string className,listName,removeFunctionName,setFunctionName;public StoredValueInfo(string name){className=name;listName=name+"List";removeFunctionName="Remove"+name;setFunctionName="Set"+name;}}
  public static StoredValueInfo[] storedValueInfos={new("MaterialShader"),new("RendererProperty"),new("MaterialFloatProperty"),new("MaterialColorProperty"),new("MaterialTextureProperty")};
  public static Type MaterialAPI=typeof(FakeMaterials);public static DirectoryInfo CacheDirectory;public Dictionary<int,object> SourceTextureDictionaryBackup=new(),TargetTextureDictionaryBackup=new();public int TextureSnapshotReads,BackupWrites;
  Dictionary<string,object> backup=Empty();public Dictionary<string,object> SourceBackup{get;set;}=Empty();public Dictionary<string,object> TargetBackup{get=>backup;set{backup=value;BackupWrites++;}}
  public static Dictionary<string,object> Empty()=>new(){["MaterialShaderList"]=new ArrayList(),["RendererPropertyList"]=new ArrayList(),["MaterialFloatPropertyList"]=new ArrayList(),["MaterialColorPropertyList"]=new ArrayList(),["MaterialTexturePropertyList"]=new ArrayList()};
  public Dictionary<int,object> GetTextureDictionaryFromController(ChaControl c){TextureSnapshotReads++;return new();}
  public static object ParseObjectTypeToMEType(ObjectType value)=>value;
  public static void MakeCacheDirectory()=>throw new InvalidOperationException("Fixture never allows texture file IO");
  public void SetMaterialEditorData(ChaControl source,int sourceSlot,ChaControl target,int targetSlot,UnityEngine.GameObject go,ObjectType kind)=>throw new NotImplementedException("Actual CIL required");
  public void RemoveMaterialEditorData(ChaControl target,int targetSlot,UnityEngine.GameObject go,ObjectType kind)=>throw new NotImplementedException("Actual CIL required");
 }
 public static class FakeMaterials{
  public static List<UnityEngine.Renderer> GetRendererList(UnityEngine.GameObject obj)=>obj.Renderers;
  public static List<UnityEngine.Material> GetObjectMaterials(UnityEngine.GameObject obj,string name)=>obj.Materials.Where(m=>m.name==name).ToList();
 }
}
