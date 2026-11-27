using System.Reflection;
using Codex.StudioBustNativeText;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,null)||a.Destroyed?ReferenceEquals(b,null)||b.Destroyed:ReferenceEquals(a,b);
        public static bool operator !=(Object a,Object b)=>!(a==b);
        public override bool Equals(object obj)=>ReferenceEquals(this,obj);
        public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
    }
    public class Transform:Component
    {
        public List<Transform> Children=new();
        public Transform Find(string name)=>Children.FirstOrDefault(x=>x.gameObject.name==name);
    }
    public class GameObject:Object
    {
        public Transform transform;
        public List<Component> Components=new();
        public GameObject(string name){this.name=name;transform=new Transform{gameObject=this};}
        public T Add<T>(T component) where T:Component{component.gameObject=this;Components.Add(component);return component;}
        public T GetComponent<T>() where T:Component=>Components.OfType<T>().FirstOrDefault();
        public T GetComponentInChildren<T>(bool include) where T:Component=>GetComponent<T>()??transform.Children.Select(x=>x.gameObject.GetComponentInChildren<T>(include)).FirstOrDefault(x=>x!=null);
        public void AddChild(GameObject child)=>transform.Children.Add(child.transform);
    }
}
namespace UnityEngine.UI {public class Text:UnityEngine.Component {public string text;public int fontSize=17;public object font=new object();}}
namespace TMPro {public class TMP_Text:UnityEngine.Component {public string text;public float fontSize=15;public object font=new object();}}
namespace BepInEx
{
    public class BaseUnityPlugin:UnityEngine.Component {public Log Logger=new();}
    public class Log {public void LogInfo(object x){}public void LogWarning(object x){}}
    [AttributeUsage(AttributeTargets.Class)]public class BepInPlugin:Attribute{public BepInPlugin(string guid,string name,string version){}}
    [AttributeUsage(AttributeTargets.Class)]public class BepInProcess:Attribute{public BepInProcess(string name){}}
    [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)]public class BepInDependency:Attribute{public BepInDependency(string guid){}}
}
namespace BepInEx.Bootstrap
{
    public class PluginInfo {public string Location;}
    public static class Chainloader {public static Dictionary<string,PluginInfo> PluginInfos=new();}
}
namespace HarmonyLib
{
    public enum HarmonyPatchType {Postfix}
    public class HarmonyMethod {public HarmonyMethod(MethodInfo method){}}
    public class Harmony
    {
        public static List<(string owner,MethodInfo method)> Patched=new(),Unpatched=new();
        readonly string owner;
        public Harmony(string owner){this.owner=owner;}
        public void Patch(MethodInfo method,HarmonyMethod postfix){Patched.Add((owner,method));}
        public void Unpatch(MethodInfo method,HarmonyPatchType type,string owner){Unpatched.Add((owner,method));}
    }
}
namespace KKAPI.Studio.UI
{
    public class CurrentStateCategory
    {
        public string CategoryName{get;}
        public CurrentStateCategory(string name){CategoryName=name;}
        protected void CreateCategory(UnityEngine.GameObject container){}
    }
    public class CurrentStateCategorySlider
    {
        public string Name{get;}
        public CurrentStateCategorySlider(string name){Name=name;}
        protected UnityEngine.GameObject CreateItem(UnityEngine.GameObject category)=>null;
    }
}
static class Tests
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
    static Plugin Activate()
    {
        var plugin=new Plugin();typeof(Plugin).GetField("instance",All).SetValue(null,plugin);
        typeof(Plugin).GetField("harmony",All).SetValue(plugin,new HarmonyLib.Harmony("codex.studio.bustnativetext"));
        typeof(Plugin).GetField("categoryMethod",All).SetValue(plugin,typeof(KKAPI.Studio.UI.CurrentStateCategory).GetMethod("CreateCategory",All));
        typeof(Plugin).GetField("sliderMethod",All).SetValue(plugin,typeof(KKAPI.Studio.UI.CurrentStateCategorySlider).GetMethod("CreateItem",All));
        return plugin;
    }
    static void Destroy(Plugin p)=>typeof(Plugin).GetMethod("OnDestroy",All).Invoke(p,null);
    static (UnityEngine.GameObject root,UnityEngine.UI.Text text) Slider(string name)
    {
        var root=new UnityEngine.GameObject("CustomSlider "+name);var child=new UnityEngine.GameObject("Text "+name);var text=child.Add(new UnityEngine.UI.Text{text=name});root.AddChild(child);return(root,text);
    }
    static void AllLabelsAndCleanup()
    {
        HarmonyLib.Harmony.Unpatched.Clear();var p=Activate();var container=new UnityEngine.GameObject("CurrentStateContainer");var header=new UnityEngine.GameObject(NativeTextPolicy.HeaderNode);var tmp=header.Add(new TMPro.TMP_Text{text=NativeTextPolicy.Category});container.AddChild(header);var category=new KKAPI.Studio.UI.CurrentStateCategory(NativeTextPolicy.Category);var font=tmp.font;
        Plugin.HeaderCreated(category,container);Check(tmp.text==NativeTextPolicy.Header,"native bilingual header");Check(category.CategoryName==NativeTextPolicy.Category&&header.name==NativeTextPolicy.HeaderNode,"header/category identity unchanged");Check(ReferenceEquals(font,tmp.font)&&tmp.fontSize==15,"header font properties unchanged");
        var parent=new UnityEngine.GameObject(NativeTextPolicy.ItemsNode);var rows=new List<(UnityEngine.GameObject root,UnityEngine.UI.Text text,string before)>();
        foreach(var pair in NativeTextPolicy.Labels)
        {
            var r=Slider(pair.Key);var control=new KKAPI.Studio.UI.CurrentStateCategorySlider(pair.Key);var oldFont=r.text.font;
            Plugin.SliderCreated(control,parent,r.root);Plugin.SliderCreated(control,parent,r.root);
            Check(r.text.text==pair.Value,"mapped label "+pair.Key);Check(control.Name==pair.Key&&r.root.name=="CustomSlider "+pair.Key&&r.text.gameObject.name=="Text "+pair.Key,"control/node identity "+pair.Key);Check(ReferenceEquals(oldFont,r.text.font)&&r.text.fontSize==17,"font preserved "+pair.Key);rows.Add((r.root,r.text,pair.Key));
        }
        rows[0].text.text="另一翻译器后改";rows[1].text.Destroyed=true;
        Destroy(p);Check(tmp.text==NativeTextPolicy.Category,"owned header restored");Check(rows[0].text.text=="另一翻译器后改","later translation preserved");Check(rows[1].text.text==NativeTextPolicy.Labels[rows[1].before],"destroyed target skipped");
        foreach(var row in rows.Skip(2))Check(row.text.text==row.before,"owned label restored "+row.before);
        Check(HarmonyLib.Harmony.Unpatched.Count==2&&HarmonyLib.Harmony.Unpatched.All(x=>x.owner=="codex.studio.bustnativetext"),"only two own postfixes removed");
        Check(typeof(Plugin).GetField("instance",All).GetValue(null)==null,"static owner cleared");Destroy(p);Check(HarmonyLib.Harmony.Unpatched.Count==2,"repeat cleanup no-op");
    }
    static void WrongTargetsAndCreatedLater()
    {
        var p=Activate();var row=Slider("   Size");var control=new KKAPI.Studio.UI.CurrentStateCategorySlider("   Size");
        Plugin.SliderCreated(control,new UnityEngine.GameObject("Other_Items_SAPI"),row.root);Check(row.text.text=="   Size","same short word elsewhere excluded");
        Plugin.SliderCreated(new KKAPI.Studio.UI.CurrentStateCategorySlider("Unknown"),new UnityEngine.GameObject(NativeTextPolicy.ItemsNode),row.root);Check(row.text.text=="   Size","unknown source excluded");
        row.root.name="RenamedControl";Plugin.SliderCreated(control,new UnityEngine.GameObject(NativeTextPolicy.ItemsNode),row.root);Check(row.text.text=="   Size","changed control identity excluded");row.root.name="CustomSlider    Size";
        row.text.gameObject.name="OtherText";Plugin.SliderCreated(control,new UnityEngine.GameObject(NativeTextPolicy.ItemsNode),row.root);Check(row.text.text=="   Size","unexpected text node excluded");row.text.gameObject.name="Text    Size";
        Plugin.SliderCreated(null,null,null);Plugin.HeaderCreated(null,null);Check(true,"null lifecycle inputs ignored");
        Plugin.SliderCreated(control,new UnityEngine.GameObject(NativeTextPolicy.ItemsNode),row.root);Check(row.text.text=="   大小","late AddControl event translated");
        Destroy(p);Check(row.text.text=="   Size","late control restored");
        p=Activate();var pre=Slider("   Size");pre.text.text="   大小";Plugin.SliderCreated(control,new UnityEngine.GameObject(NativeTextPolicy.ItemsNode),pre.root);Destroy(p);Check(pre.text.text=="   大小","pre-existing Chinese not claimed");
        Check(!typeof(Plugin).GetMethods(All).Any(m=>new[]{"Update","LateUpdate","FixedUpdate"}.Contains(m.Name)),"no per-frame observer");
    }
    static void Refusals()
    {
        var path=Path.GetTempFileName();try
        {
            File.WriteAllText(path,"synthetic unrelated plugin");NativeTextPolicy.RequireFile(path,NativeTextPolicy.Hash(path));Check(true,"exact file hash accepted");
            bool refused=false;try{NativeTextPolicy.RequireFile(path,NativeTextPolicy.BustSha);}catch(InvalidOperationException){refused=true;}Check(refused,"unknown plugin refused");
            refused=false;try{NativeTextPolicy.RequireFile(path,NativeTextPolicy.ApiSha);}catch(InvalidOperationException){refused=true;}Check(refused,"unknown API refused");
            HarmonyLib.Harmony.Patched.Clear();BepInEx.Bootstrap.Chainloader.PluginInfos["Vaaskr.StudioBustAdjuster"]=new BepInEx.Bootstrap.PluginInfo{Location=path};typeof(Plugin).GetMethod("Awake",All).Invoke(new Plugin(),null);Check(HarmonyLib.Harmony.Patched.Count==0,"unknown build Awake installs no hooks");
        }finally{File.Delete(path);}
    }
    static int Main(){try{AllLabelsAndCleanup();WrongTargetsAndCreatedLater();Refusals();Console.WriteLine("PASS checks="+checks+" synthetic creation callbacks; no Unity runtime.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
