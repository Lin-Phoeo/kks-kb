using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public string name {get;set;}
        public bool Destroyed;
        public static bool op_Equality(Object a,Object b){return (ReferenceEquals(a,null)||a.Destroyed)?(ReferenceEquals(b,null)||b.Destroyed):ReferenceEquals(a,b);}
        public static bool op_Inequality(Object a,Object b){return !op_Equality(a,b);}
        public static T FindObjectOfType<T>() where T:class {return ProbeEnv.Objects.OfType<T>().FirstOrDefault();}
    }
    public class Component:Object
    {
        public List<Component> Children=new List<Component>();
        public T GetComponentInChildren<T>(bool include) where T:class{return Children.OfType<T>().FirstOrDefault();}
        public T[] GetComponentsInChildren<T>(bool include) where T:class{return Children.OfType<T>().ToArray();}
    }
    public class Transform:Component{}
    public class Material:Object{}
    public class Coroutine {public IEnumerator Routine; public bool Stopped;}
    public class MonoBehaviour:Component
    {
        public Coroutine StartCoroutine(IEnumerator value){var c=new Coroutine{Routine=value};ProbeEnv.Coroutines.Add(c);return c;}
        public void StopCoroutine(Coroutine c){if(c!=null)c.Stopped=true;}
    }
    public static class Time {public static float Now;public static float unscaledTime {get{return Now;}}}
    public class WaitForSecondsRealtime {public WaitForSecondsRealtime(float seconds){}}
}
public static class Singleton<T> {public static T Instance {get;set;}}
namespace Studio
{
    public class Studio:UnityEngine.Object {public Dictionary<int,ObjectCtrlInfo> dicObjectCtrl=new Dictionary<int,ObjectCtrlInfo>();}
    public class GuideObject:UnityEngine.Object {public UnityEngine.Transform transformTarget;}
    public class ObjectCtrlInfo {public GuideObject guideObject;}
}
namespace ExtensibleSaveFormat {public class PluginData {public Dictionary<string,object> data=new Dictionary<string,object>();}}
namespace BepInEx.Logging
{
    public class ManualLogSource {public void LogError(object x){ProbeEnv.Log.Add("error:"+x);}public void LogInfo(object x){ProbeEnv.Log.Add("info:"+x);}public void LogWarning(object x){ProbeEnv.Log.Add("warning:"+x);}}
}
namespace KKAPI.Utilities
{
    public class ReadOnlyDictionary<TKey,TValue>:IDictionary<TKey,TValue>
    {
        readonly IDictionary<TKey,TValue> map;
        public ReadOnlyDictionary(IDictionary<TKey,TValue> value){map=value;}
        public TValue this[TKey key]{get{return map[key];}set{throw new NotSupportedException();}}
        public ICollection<TKey> Keys{get{return map.Keys;}}public ICollection<TValue> Values{get{return map.Values;}}
        public int Count{get{return map.Count;}}public bool IsReadOnly{get{return true;}}
        public bool ContainsKey(TKey key){return map.ContainsKey(key);}public bool TryGetValue(TKey key,out TValue value){return map.TryGetValue(key,out value);}
        public IEnumerator<KeyValuePair<TKey,TValue>> GetEnumerator(){return map.GetEnumerator();}IEnumerator IEnumerable.GetEnumerator(){return GetEnumerator();}
        public bool Contains(KeyValuePair<TKey,TValue> item){return map.Contains(item);}public void CopyTo(KeyValuePair<TKey,TValue>[] array,int index){map.CopyTo(array,index);}
        public void Add(TKey key,TValue value){throw new NotSupportedException();}public void Add(KeyValuePair<TKey,TValue> item){throw new NotSupportedException();}
        public bool Remove(TKey key){throw new NotSupportedException();}public bool Remove(KeyValuePair<TKey,TValue> item){throw new NotSupportedException();}public void Clear(){throw new NotSupportedException();}
    }
}
namespace KKAPI.Studio.SaveLoad
{
    public enum SceneOperationKind {Load=0,Import=1,Clear=2}
    public class SceneCustomFunctionController:UnityEngine.MonoBehaviour
    {
        public ExtensibleSaveFormat.PluginData Data;
        public ExtensibleSaveFormat.PluginData GetExtendedData(){return Data;}
        protected virtual void OnSceneLoad(SceneOperationKind op,KKAPI.Utilities.ReadOnlyDictionary<int,global::Studio.ObjectCtrlInfo> loaded){}
    }
}
namespace LiquidWobbleMPB
{
    public static class LiquidWobbleMPBPlugin {public static BepInEx.Logging.ManualLogSource _logger=new BepInEx.Logging.ManualLogSource();public static bool CfgEnabled {get{return false;}}}
    public class WombExpandEffect:UnityEngine.Component
    {
        public int Pair;public bool Upgrade;public float Collapsed;public int Writes;
        public bool Ready {get{return true;}}
        public float HandShape(string key){return Collapsed;}
        public bool SetHandShape(string key,float value){Collapsed=value;Writes++;return true;}
    }
    public static class AutoBodyReveal
    {
        public static int Marks;public static void MarkSceneLoadStarted(){Marks++;}
        public static int ApplyWombState(WombExpandEffect w,int pair,bool upgrade){return ApplyWombState(w,pair,upgrade,false);}
        public static int ApplyWombState(WombExpandEffect w,int pair,bool upgrade,bool adoptOnly){w.Pair=pair;w.Upgrade=upgrade;w.Writes++;return 1;}
        public static UnityEngine.Component ResolveWearer(WombExpandEffect w,out string why){why="fixture";return w;}
    }
    public static class MEBridge
    {
        public static UnityEngine.Material FindBodyStamp(UnityEngine.Component c){return new UnityEngine.Material();}
        public static int ReadBodyStencilRef(UnityEngine.Component c){return 44;}
        public static bool StampIsUpgraded(UnityEngine.Material m){return false;}
    }
    public static class ShaderCarryOver {public static int Clears;public static void ForgetAll(){Clears++;}}
    public class WobbleSceneController:KKAPI.Studio.SaveLoad.SceneCustomFunctionController
    {
        public class BpRebindPair{}
        public static List<BpRebindPair> _bpRebindPairs=new List<BpRebindPair>();
        public static WobbleSceneController _inst;
        public bool _penisBendInFlight;
        public UnityEngine.Coroutine _capturePoll;
        protected override void OnSceneLoad(KKAPI.Studio.SaveLoad.SceneOperationKind op,KKAPI.Utilities.ReadOnlyDictionary<int,Studio.ObjectCtrlInfo> map){throw new Exception("actual CIL not extracted");}
        public void Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind op,KKAPI.Utilities.ReadOnlyDictionary<int,Studio.ObjectCtrlInfo> map){OnSceneLoad(op,map);}
        public IEnumerator WearerCapturePoll(){yield break;}
        public IEnumerator ReassertPenisBendWhenBPReady(){yield break;}
        public IEnumerator ReattachWhenLiquid(int[] ids,KKAPI.Utilities.ReadOnlyDictionary<int,Studio.ObjectCtrlInfo> map){foreach(int id in ids){Studio.ObjectCtrlInfo item;if(map.TryGetValue(id,out item))ProbeEnv.WobbleObjects.Add(item);}yield break;}
    }
}
public static class ProbeEnv
{
    public static List<UnityEngine.Object> Objects=new List<UnityEngine.Object>();
    public static List<UnityEngine.Coroutine> Coroutines=new List<UnityEngine.Coroutine>();
    public static List<Studio.ObjectCtrlInfo> WobbleObjects=new List<Studio.ObjectCtrlInfo>();
    public static List<string> Log=new List<string>();
    public static void Reset(){Objects.Clear();Coroutines.Clear();WobbleObjects.Clear();Log.Clear();UnityEngine.Time.Now=0;Singleton<Studio.Studio>.Instance=new Studio.Studio();LiquidWobbleMPB.AutoBodyReveal.Marks=0;LiquidWobbleMPB.ShaderCarryOver.Clears=0;}
    public static Studio.ObjectCtrlInfo Item(int id,string name,out LiquidWobbleMPB.WombExpandEffect w)
    {
        w=new LiquidWobbleMPB.WombExpandEffect{name=name,Pair=4};Objects.Add(w);
        var t=new UnityEngine.Transform();t.Children.Add(w);var item=new Studio.ObjectCtrlInfo{guideObject=new Studio.GuideObject{transformTarget=t}};Singleton<Studio.Studio>.Instance.dicObjectCtrl[id]=item;return item;
    }
    public static void Drain()
    {
        for(int tick=0;tick<35;tick++) {UnityEngine.Time.Now+=0.5f;foreach(var c in Coroutines.ToArray())if(!c.Stopped&&!c.Routine.MoveNext())c.Stopped=true;}
        if(Coroutines.Any(c=>!c.Stopped))throw new Exception("Coroutine did not settle");
    }
}
static class Tests
{
    static int checks;static int failures;
    static void Check(bool ok,string message){checks++;if(!ok){failures++;Console.WriteLine("FAIL "+message);}}
    static KKAPI.Utilities.ReadOnlyDictionary<int,Studio.ObjectCtrlInfo> Map(Dictionary<int,Studio.ObjectCtrlInfo> m){return new KKAPI.Utilities.ReadOnlyDictionary<int,Studio.ObjectCtrlInfo>(m);}
    static LiquidWobbleMPB.WobbleSceneController Controller(int[] pair,int[] collapsed,object wobble)
    {
        var c=new LiquidWobbleMPB.WobbleSceneController{Data=new ExtensibleSaveFormat.PluginData()};if(pair!=null)c.Data.data["wombPairStack"]=pair;if(collapsed!=null)c.Data.data["wombCollapsed"]=collapsed;if(wobble!=null)c.Data.data["wobbleItemIds"]=wobble;return c;
    }
    static void Collision()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect existing,imported;ProbeEnv.Item(10,"existing",out existing);var item=ProbeEnv.Item(25,"imported",out imported);
        var map=new Dictionary<int,Studio.ObjectCtrlInfo>{{10,item}};var c=Controller(new[]{10,48,1},new[]{10},new object[]{10});c.Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(map));ProbeEnv.Drain();
        Check(existing.Writes==0,"Import collision leaves existing object untouched");Check(imported.Pair==48&&imported.Upgrade,"Import restores pair and stack to mapped object");Check(imported.Collapsed==100,"Import restores collapse to mapped object");Check(ProbeEnv.WobbleObjects.SequenceEqual(new[]{item}),"Legacy wobble IDs still use imported map");Check(LiquidWobbleMPB.ShaderCarryOver.Clears==0,"Import keeps original carry-over policy");
    }
    static void SnapshotAndOverlap()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect a,b,existing;var ia=ProbeEnv.Item(25,"a",out a);var ib=ProbeEnv.Item(26,"b",out b);ProbeEnv.Item(10,"existing",out existing);
        var ma=new Dictionary<int,Studio.ObjectCtrlInfo>{{10,ia}};var mb=new Dictionary<int,Studio.ObjectCtrlInfo>{{10,ib}};
        Controller(new[]{10,40,0},null,null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(ma));
        Controller(new[]{10,52,1},null,null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(mb));ma.Clear();mb.Clear();Singleton<Studio.Studio>.Instance.dicObjectCtrl.Clear();ProbeEnv.Drain();
        Check(a.Pair==40&&!a.Upgrade,"First import owns immutable object-map snapshot");Check(b.Pair==52&&b.Upgrade,"Second overlapping import owns separate snapshot");Check(existing.Writes==0,"Async imports never use old global collision object");
    }
    static void LegacyWithoutPair()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect existing,imported;ProbeEnv.Item(10,"existing",out existing);var item=ProbeEnv.Item(25,"imported",out imported);
        Controller(null,null,null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(new Dictionary<int,Studio.ObjectCtrlInfo>{{10,item}}));ProbeEnv.Drain();
        Check(existing.Writes==0,"Legacy import inference excludes pre-existing wombs");Check(imported.Pair==44,"Legacy import inference still processes imported womb");
    }
    static void FullLoadAndClear()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect loaded;var item=ProbeEnv.Item(10,"loaded",out loaded);
        Controller(new[]{10,48,1},new[]{10},new[]{10}).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Load,Map(new Dictionary<int,Studio.ObjectCtrlInfo>{{10,item}}));ProbeEnv.Drain();
        Check(loaded.Pair==48&&loaded.Upgrade&&loaded.Collapsed==100,"Full load retains original restore behavior");Check(LiquidWobbleMPB.ShaderCarryOver.Clears==1,"Full load keeps original cache clear");
        ProbeEnv.Reset();ProbeEnv.Item(10,"untouched",out loaded);Controller(new[]{10,48,1},new[]{10},null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Clear,Map(new Dictionary<int,Studio.ObjectCtrlInfo>()));ProbeEnv.Drain();
        Check(loaded.Writes==0,"Clear never restores saved object state");Check(LiquidWobbleMPB.AutoBodyReveal.Marks==0,"Clear preserves original scene-load bracket");
    }
    static void MissingAndEmpty()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect existing;ProbeEnv.Item(10,"existing",out existing);
        Controller(new[]{10,48,1,999,52,0,42},new[]{10,999},null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(new Dictionary<int,Studio.ObjectCtrlInfo>()));ProbeEnv.Drain();Check(existing.Writes==0,"Empty map cannot restore colliding global IDs");
        ProbeEnv.Reset();ProbeEnv.Item(10,"existing",out existing);Controller(null,null,null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,null);ProbeEnv.Drain();Check(existing.Writes==0,"Null import map fails closed without global scan");
    }
    static void PartialAndDeadObjects()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect existing,imported,dead;ProbeEnv.Item(999,"existing",out existing);var item=ProbeEnv.Item(25,"imported",out imported);var deadItem=ProbeEnv.Item(26,"destroyed",out dead);dead.Destroyed=true;
        var map=new Dictionary<int,Studio.ObjectCtrlInfo>{{10,item},{11,deadItem},{12,null},{13,new Studio.ObjectCtrlInfo()}};
        Controller(new[]{10,52,0,11,48,1,12,48,1,13,48,1,999,48,1,17},new[]{11,12,13,999},null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(map));ProbeEnv.Drain();
        Check(imported.Pair==52&&!imported.Upgrade,"Partial triple tail does not lose valid imported record");Check(existing.Writes==0,"Missing imported ID never falls back to global object");Check(dead.Writes==0,"Destroyed imported target is skipped");
    }
    static void LoadRetainsGlobalTiming()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect original,replacement;var before=ProbeEnv.Item(10,"before",out original);
        Controller(new[]{10,48,1},null,null).Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Load,Map(new Dictionary<int,Studio.ObjectCtrlInfo>{{10,before}}));
        ProbeEnv.Item(10,"replacement",out replacement);ProbeEnv.Drain();
        Check(original.Writes==0,"Load keeps original deferred global lookup");Check(replacement.Pair==48&&replacement.Upgrade,"Load does not adopt import-only snapshot behavior");
    }
    static void MissingDataAndGenericConversion()
    {
        ProbeEnv.Reset();LiquidWobbleMPB.WombExpandEffect old,imported;ProbeEnv.Item(10,"old",out old);var item=ProbeEnv.Item(25,"imported",out imported);
        var c=Controller(null,null,null);c.Data=null;c.Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(new Dictionary<int,Studio.ObjectCtrlInfo>{{10,item}}));ProbeEnv.Drain();
        Check(old.Writes==0,"No plugin data import only infers imported objects");Check(imported.Pair==44,"No plugin data import retains inference");
        ProbeEnv.Reset();ProbeEnv.Item(10,"old",out old);item=ProbeEnv.Item(25,"imported",out imported);c=Controller(null,null,null);c.Data.data["wombPairStack"]=new object[]{10,40,1};c.Data.data["wombCollapsed"]=new object[]{10};c.Invoke(KKAPI.Studio.SaveLoad.SceneOperationKind.Import,Map(new Dictionary<int,Studio.ObjectCtrlInfo>{{10,item}}));ProbeEnv.Drain();
        Check(old.Writes==0,"Object-array serialized record excludes existing object");Check(imported.Pair==40&&imported.Upgrade&&imported.Collapsed==100,"Actual ToIntArray restores object-array data");
    }
    public static int Main()
    {
        try{Collision();SnapshotAndOverlap();LegacyWithoutPair();FullLoadAndClear();MissingAndEmpty();PartialAndDeadObjects();LoadRetainsGlobalTiming();MissingDataAndGenericConversion();Console.WriteLine("checks="+checks+" failures="+failures);return failures==0?0:1;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 2;}
    }
}
