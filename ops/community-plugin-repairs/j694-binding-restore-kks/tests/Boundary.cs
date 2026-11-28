using System.Collections;using System.Reflection;
namespace UnityEngine {
 public class Object {static int next;readonly int id=++next;public string name{get;set;}="";public bool Destroyed;public int GetInstanceID()=>id;public static bool operator==(Object a,Object b)=>ReferenceEquals(a,b)||((ReferenceEquals(a,null)||a.Destroyed)&&(ReferenceEquals(b,null)||b.Destroyed));public static bool operator!=(Object a,Object b)=>!(a==b);public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>id;}
 public class Component:Object {public GameObject gameObject{get;set;}public Transform transform=>gameObject.transform;public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();public Component GetComponent(Type t)=>gameObject.GetComponent(t);public T[] GetComponentsInChildren<T>(bool inactive) where T:Component=>gameObject.All().OfType<T>().Where(x=>!x.Destroyed).ToArray();}
 public class Transform:Component {public Transform parent{get;set;}public bool IsChildOf(Transform other){for(var t=this;t!=null;t=t.parent)if(t==other)return true;return false;}public Transform Find(string path){var t=this;foreach(var bit in path.Split('/')){t=t.gameObject.Children.FirstOrDefault(x=>x.name==bit)?.transform;if(t==null)return null;}return t;}}
 public class GameObject:Object {public List<GameObject> Children=new();public List<Component> Components=new();public Transform transform{get;}public GameObject(string n=""){name=n;transform=new(){gameObject=this,name=n};Components.Add(transform);}public GameObject Child(string name){var o=new GameObject(name);Children.Add(o);o.transform.parent=transform;return o;}public T Add<T>() where T:Component,new(){var x=new T{gameObject=this,name=name};Components.Add(x);return x;}public T GetComponent<T>() where T:Component=>Components.OfType<T>().FirstOrDefault();public Component GetComponent(Type t)=>Components.FirstOrDefault(x=>t.IsInstanceOfType(x));public IEnumerable<Component> All()=>Components.Concat(Children.SelectMany(x=>x.All()));}
 public class Behaviour:Component {public bool enabled{get;set;}=true;}
 public class MonoBehaviour:Behaviour {}
 public static class Time {public static int frameCount{get;set;}}
}
public class DynamicBoneCollider:UnityEngine.Component {}
public class DynamicBone:UnityEngine.Component {public UnityEngine.Transform m_Root;public List<DynamicBoneCollider> m_Colliders=new();}
public class DynamicBone_Ver02:UnityEngine.Component {public UnityEngine.Transform Root;public List<DynamicBoneCollider> Colliders=new();}
public class ListInfoBase {public int Category{get;set;}public int Id{get;set;}}
public class ListInfoComponent:UnityEngine.Component {public ListInfoBase data;}
public class ChaFileHair {public class PartsInfo {public int id{get;set;}}public PartsInfo[] parts{get;set;}=Array.Empty<PartsInfo>();}
public class ChaFileClothes {public class PartsInfo {public int id{get;set;}}public PartsInfo[] parts{get;set;}=Array.Empty<PartsInfo>();public int[] subPartsId{get;set;}=Array.Empty<int>();}
public class ChaFileAccessory {public class PartsInfo {public int type{get;set;}public int id{get;set;}}public PartsInfo[] parts{get;set;}=Array.Empty<PartsInfo>();}
public class ChaFileCoordinate {public ChaFileClothes clothes=new();public ChaFileAccessory accessory=new();}
public class ChaInfo:UnityEngine.MonoBehaviour {public enum DynamicBoneKind {Left,Right,LeftButt,RightButt}public UnityEngine.GameObject[] objHair{get;set;}=Array.Empty<UnityEngine.GameObject>();public UnityEngine.GameObject[] objClothes{get;set;}=Array.Empty<UnityEngine.GameObject>();public UnityEngine.GameObject[] objAccessory{get;set;}=Array.Empty<UnityEngine.GameObject>();public ListInfoBase[] infoHair{get;set;}=Array.Empty<ListInfoBase>();public ListInfoBase[] infoClothes{get;set;}=Array.Empty<ListInfoBase>();public ListInfoBase[] infoAccessory{get;set;}=Array.Empty<ListInfoBase>();public UnityEngine.GameObject objBody{get;set;}public UnityEngine.GameObject objHead{get;set;}public ChaFileHair fileHair{get;set;}=new();}
public class ChaControl:ChaInfo {public ChaFileCoordinate nowCoordinate{get;set;}=new();public Dictionary<DynamicBoneKind,DynamicBone_Ver02> BodyBones=new();public DynamicBone_Ver02 getDynamicBoneBust(DynamicBoneKind kind)=>BodyBones.GetValueOrDefault(kind);}
public class ChaFileDefine {public enum CoordinateType {First,Second}}
public class Singleton<T> {public static T Instance{get;set;}}
namespace Studio {
 public class ObjectInfo {public int dicKey;}
 public class ObjectCtrlInfo {public GuideObject guideObject=new();public ObjectInfo objectInfo=new();public TreeNodeObject treeNodeObject=new();}
 public class GuideObject {public UnityEngine.Transform transformTarget;}
 public class TreeNodeObject:UnityEngine.Component {public string textName{get;set;}="Synthetic";}
 public class OCIChar:ObjectCtrlInfo {public ChaControl charInfo;}
 public class OIItemInfo {public int group{get;set;}public int category{get;set;}public int no{get;set;}}
 public class OCIItem:ObjectCtrlInfo {public OIItemInfo itemInfo{get;set;}=new();}
 public class Studio:UnityEngine.Component {public Dictionary<int,ObjectCtrlInfo> dicObjectCtrl=new();}
 public class Info:UnityEngine.Component {public class LoadCommonInfo {public string name;}public class FileInfo:LoadCommonInfo {public string bundlePath;}public class ItemLoadInfo:FileInfo {}public Dictionary<int,Dictionary<int,Dictionary<int,ItemLoadInfo>>> dicItemLoadInfo=new();}
}
namespace BepInEx.Logging {public class ManualLogSource {public List<string> Errors=new();public void LogError(object s){Errors.Add(s.ToString());Console.Error.WriteLine(s);}public void LogWarning(object s){}public void LogInfo(object s){}}}
namespace BepInEx.Configuration {public class ConfigEntryBase {}public class ConfigEntry<T>:ConfigEntryBase {public T Value{get;set;}public ConfigEntry(T v){Value=v;}}}
namespace BepInEx {public class BaseUnityPlugin:UnityEngine.MonoBehaviour {public BepInEx.Logging.ManualLogSource Logger{get;}=new();}}
namespace HarmonyLib {public class Harmony {}public class HarmonyMethod {}public static class AccessTools {public static FieldInfo Field(Type type,string name){while(type!=null){var f=type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly);if(f!=null)return f;type=type.BaseType;}return null;}public static MethodInfo Method(Type type,string name,Type[] signature=null,Type[] generics=null)=>type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance,null,signature??Type.EmptyTypes,null);}}
namespace KKAPI.Studio.SaveLoad {public enum SceneOperationKind {Load,Import,Clear}public class SceneLoadEventArgs:EventArgs {public SceneOperationKind Operation{get;set;}}public class ObjectDeletedEventArgs:EventArgs {public global::Studio.ObjectCtrlInfo DeletedObject{get;set;}}public static class StudioSaveLoadApi {public static bool LoadInProgress{get;set;}public static bool ImportInProgress{get;set;}}}
namespace KKAPI.Chara {public class CoordinateEventArgs:EventArgs {public ChaControl Character{get;set;}}public class CharaReloadEventArgs:EventArgs {public ChaControl ReloadedCharacter{get;set;}}}
namespace HSPE {public class PoseController:UnityEngine.MonoBehaviour {public HashSet<UnityEngine.GameObject> _childObjects=new();public AMModules.DynamicBonesEditor _dynamicBonesEditor;}
 public class CharaPoseController:PoseController {public AMModules.BoobsEditor _boobsEditor;}}
namespace HSPE.AMModules {
 public class AdvancedModeModule {public HSPE.PoseController _parent;}
 public class DynamicBonesEditor:AdvancedModeModule {public List<DynamicBone> _dynamicBones=new();private void RefreshDynamicBoneList(){}}
 public class BoobsEditor {public DynamicBone_Ver02[] _dynamicBones=Array.Empty<DynamicBone_Ver02>();public string GetID(DynamicBone_Ver02 db)=>"fixture-"+db.GetInstanceID();}
 public class CollidersEditor {
  public class ColliderDataBase {public Dictionary<HSPE.PoseController,Dictionary<object,bool>> allDynamicBones=new();}
  public static Dictionary<DynamicBoneCollider,CollidersEditor> _loneColliders=new();public Dictionary<DynamicBoneCollider,ColliderDataBase> _dirtyColliders=new();public bool _addNewDynamicBonesAsDefault=true;public int Writes;
  public void SetIgnoreDynamicBone(DynamicBoneCollider collider,HSPE.PoseController pose,object bone,bool ignore){Writes++;if(!_dirtyColliders.TryGetValue(collider,out var data))_dirtyColliders[collider]=data=new();if(!data.allDynamicBones.TryGetValue(pose,out var table))data.allDynamicBones[pose]=table=new();table[bone]=!ignore;IList list=bone is DynamicBone d?d.m_Colliders:((DynamicBone_Ver02)bone).Colliders;if(ignore){if(list.Contains(collider))list.Remove(collider);}else if(!list.Contains(collider))list.Add(collider);}
 }
}
