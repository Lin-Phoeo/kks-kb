namespace UnityEngine
{
 public class Object{public bool Destroyed;public string name{get;set;}public static implicit operator bool(Object value)=>!ReferenceEquals(value,null)&&!value.Destroyed;public static bool operator==(Object a,Object b)=>ReferenceEquals(a,b)||(ReferenceEquals(a,null)&&!ReferenceEquals(b,null)&&b.Destroyed)||(ReferenceEquals(b,null)&&!ReferenceEquals(a,null)&&a.Destroyed);public static bool operator!=(Object a,Object b)=>!(a==b);public static void Destroy(Object value){if(!ReferenceEquals(value,null)){value.Destroyed=true;if(value is GameObject go){go.transform.parent?.Children.Remove(go.transform);foreach(var c in go.Components.Values)c.Destroyed=true;}}}public static T Instantiate<T>(T value,Transform parent)where T:Object{var source=value as GameObject??throw new Exception("unsupported fixture clone");var clone=new GameObject(source.name);clone.transform.parent=parent;parent.Children.Add(clone.transform);clone.transform.sizeDelta=source.transform.sizeDelta;if(source.GetComponent<UI.Image>()!=null)clone.AddComponent<UI.Image>();if(source.GetComponent<UI.Button>()!=null)clone.AddComponent<UI.Button>();return clone as T;}public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}
 public class GameObject:Object{public static readonly Dictionary<string,GameObject> Registry=new();public readonly Dictionary<Type,Component> Components=new();public RectTransform transform{get;}public bool activeSelf=true;public void SetActive(bool value)=>activeSelf=value;public GameObject(string name=""){this.name=name;transform=new RectTransform{gameObject=this};Components[typeof(RectTransform)]=transform;}public static GameObject Find(string path)=>Registry.GetValueOrDefault(path);public T GetComponent<T>()where T:class=>Components.Values.OfType<T>().FirstOrDefault();public T AddComponent<T>()where T:Component,new(){var c=new T{gameObject=this};Components[typeof(T)]=c;return c;}}
 public class Component:Object{public GameObject gameObject{get;set;}public Transform transform=>gameObject.transform;public T GetComponent<T>()where T:class=>gameObject.GetComponent<T>();}
 public class MonoBehaviour:Component{}
 public class Transform:Component{public readonly List<Transform> Children=new();public Transform parent{get;set;}public int childCount=>Children.Count;public Transform GetChild(int i)=>Children[i];public Transform Find(string name)=>Children.FirstOrDefault(c=>c.gameObject.name==name);}
 public struct Vector2{public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
 public class RectTransform:Transform{public Vector2 sizeDelta{get;set;}public Vector2 anchoredPosition{get;set;}}
 public static class Mathf{public static int Min(int a,int b)=>Math.Min(a,b);}
 public static class Time{public static float realtimeSinceStartup{get;set;}}
 public struct Color{public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
}
namespace UnityEngine.Events{public delegate void UnityAction();public class UnityEvent{public readonly List<UnityAction> Listeners=new();public void AddListener(UnityAction a)=>Listeners.Add(a);public void RemoveListener(UnityAction a)=>Listeners.RemoveAll(x=>x==a);public void Invoke(){foreach(var a in Listeners.ToArray())a();}}}
namespace UnityEngine.UI{public class Graphic:UnityEngine.Component{public UnityEngine.Color color{get;set;}}public class Image:Graphic{}public class Button:UnityEngine.Component{public class ButtonClickedEvent:UnityEngine.Events.UnityEvent{}public ButtonClickedEvent onClick{get;set;}=new();}}
public class Singleton<T>{public static T Instance{get;set;}}
namespace Studio{public class Studio:UnityEngine.Object{}}
namespace BepInEx.Logging{public class ManualLogSource{public readonly List<string> Lines=new();public void LogInfo(object value)=>Lines.Add("INFO "+value);public void LogWarning(object value)=>Lines.Add("WARN "+value);}}
namespace ExtensibleSaveFormat
{
 public class PluginData{public int version;public Dictionary<string,object> data=new();}
 public static class ExtendedSave{
  public delegate void SceneEventHandler(string path);
  public static event SceneEventHandler SceneBeingSaved,SceneBeingLoaded,SceneBeingImported;
  public static PluginData Incoming,Written;public static int Reads,Writes;
  public static PluginData GetSceneExtendedDataById(string id){Reads++;return Incoming;}
  public static void SetSceneExtendedDataById(string id,PluginData data){Writes++;Written=data;}
  public static void Save()=>SceneBeingSaved?.Invoke("synthetic-scene");public static void Load()=>SceneBeingLoaded?.Invoke("synthetic-scene");public static void Import()=>SceneBeingImported?.Invoke("synthetic-scene");
  public static int SavedCount=>SceneBeingSaved?.GetInvocationList().Length??0;public static int LoadedCount=>SceneBeingLoaded?.GetInvocationList().Length??0;public static int ImportedCount=>SceneBeingImported?.GetInvocationList().Length??0;
 }
}
namespace CameraFXSlots
{
 public class CameraFXSlotsPlugin{public static BepInEx.Logging.ManualLogSource Logger=new();}
 public class CameraFXSlotsRunner:UnityEngine.MonoBehaviour{
  public static string[] dofFloatProps,fogFloatProps,fogColorProps;
  public static float[][] savedDoF,savedFogFloats;public static bool[] savedDoFEnabled,savedFogEnabled;public static UnityEngine.Color[][] savedFogColors;
  public static List<UnityEngine.GameObject> addedButtons;public static UnityEngine.UI.Image[] fxButtonImages;
  public static UnityEngine.Color colorEmpty,colorSaved;public static bool eventsHooked;
  public bool uiInitialized;public int lastKnownSlotCount;
  public static BepInEx.Logging.ManualLogSource Log=>CameraFXSlotsPlugin.Logger;
  static CameraFXSlotsRunner(){}
  public static void HookExtendedSaveEvents()=>throw new Exception("Extract actual CIL");public static void UnhookExtendedSaveEvents()=>throw new Exception("Extract actual CIL");
  public static void OnSceneSave(string path)=>throw new Exception("Extract actual CIL");public static void OnSceneLoad(string path)=>throw new Exception("Extract actual CIL");
  public static float ReadFloat(Dictionary<string,object> data,string key)=>throw new Exception("Extract actual CIL");public static bool SlotHasData(int index)=>throw new Exception("Extract actual CIL");
  public static void UpdateButtonColor(int index)=>throw new Exception("Extract actual CIL");public static void UpdateAllButtonColors()=>throw new Exception("Extract actual CIL");public static void Cleanup()=>throw new Exception("Extract actual CIL");
  public void Update()=>throw new Exception("Extract actual CIL");
  public void InitializeUI(UnityEngine.Transform content)=>throw new Exception("Extract actual CIL");public void AddButtonsForNewSlots(UnityEngine.Transform content,int fromIndex,int toIndex)=>throw new Exception("Extract actual CIL");
  public static void SaveDoF(int i){SavedIndices.Add(i);}public static void SaveFog(int i){SavedIndices.Add(i);}public static void RestoreDoF(int i){RestoredIndices.Add(i);}public static void RestoreFog(int i){RestoredIndices.Add(i);}
  public static List<int> SavedIndices,RestoredIndices;
 }
}
