using System.Reflection;
using System.Text.Json;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a,Object b)
        {
            bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;
            return an||bn?an==bn:ReferenceEquals(a,b);
        }
        public static bool operator !=(Object a,Object b)=>!(a==b);
        public static implicit operator bool(Object value)=>value!=null;
        public override bool Equals(object value)=>ReferenceEquals(this,value);
        public override int GetHashCode()=>base.GetHashCode();
    }
    public class Component:Object
    {
        public Component Owner;
        public int NativeCalls;
        public bool ThrowBusinessError;
        public Component GetComponent(Type type)
        {
            NativeCalls++;
            if(Destroyed)throw new MissingReferenceException("synthetic destroyed native component");
            if(ThrowBusinessError)throw new InvalidOperationException("synthetic unrelated business failure");
            return !ReferenceEquals(Owner,null)&&type.IsInstanceOfType(Owner)?Owner:null;
        }
        public T GetComponentInChildren<T>(bool includeInactive) where T:Component=>null;
    }
    public class Transform:Component { }
    public class MonoBehaviour:Component { }
    public class MissingReferenceException:Exception { public MissingReferenceException(string message):base(message){} }
    public class Resources
    {
        public static int Calls;
        public static Object[] Items=Array.Empty<Object>();
        public static Object[] FindObjectsOfTypeAll(Type type)
        {
            Calls++;
            if(!typeof(Object).IsAssignableFrom(type))throw new ArgumentException("Invalid non-Unity object type");
            return Items.Where(type.IsInstanceOfType).ToArray();
        }
    }
}
public class ChaControl:UnityEngine.Component { }
public class FaceBlendShape:UnityEngine.Component { }
namespace HSPE
{
    public class PoseController:UnityEngine.MonoBehaviour { internal object _blendShapesEditor; }
    public class CharaPoseController:PoseController { }
}
namespace HSPE.AMModules
{
    public class AdvancedModeModule { }
    public class BlendShapesEditor:AdvancedModeModule { public string Id; public BlendShapesEditor(string id){Id=id;} }
}
namespace Studio
{
    public class ObjectCtrlInfo { public GuideObject guideObject; }
    public class OCIChar:ObjectCtrlInfo { public ChaControl charInfo=new ChaControl(); }
    public class GuideObject:UnityEngine.MonoBehaviour { public UnityEngine.Transform transformTarget; }
}
namespace KKS_ExpressionStudio
{
    public class Log { public static int Warnings; public static void Warn(string text){Warnings++;} }
    public class TimelineBridge { public static List<object> All()=>null; }
    public class KkpeBridge
    {
        public static Type CurrentEditorType;
        public static object FaceEditor,CharacterFallback,StaticFallback,SceneFallback;
        public static readonly List<string> Trace=new List<string>();
        public static Type EditorType=>CurrentEditorType;
        public static void HarvestFaceMap(Studio.OCIChar oci,List<object> found){Trace.Add("face");if(FaceEditor!=null)found.Add(FaceEditor);}
        public static void HarvestFromCharacter(ChaControl character,List<object> found){Trace.Add("character");if(character!=null&&CharacterFallback!=null)found.Add(CharacterFallback);}
        public static void HarvestStatics(List<object> found){Trace.Add("statics");if(StaticFallback!=null)found.Add(StaticFallback);}
        public static void HarvestScene(List<object> found){Trace.Add("scene");if(SceneFallback!=null)found.Add(SceneFallback);}
        public static object GetField(object value,string name)=>null;
        public static void DigEditor(object value,Type editor,List<object> found){ }
        public static void Collect(object value,Type editor,FaceBlendShape face,List<object> found){ }
        public static List<object> PrimaryLookup(Studio.OCIChar oci)=>throw new Exception("Unpopulated CIL fixture");
        public static List<object> EditorsFor(Studio.OCIChar oci)=>throw new Exception("Unpopulated CIL fixture");
        public static void ScanUnity(List<object> found)=>throw new Exception("Unpopulated CIL fixture");
    }
}
public class SyntheticUnityEditor:UnityEngine.Object { }
public class Program
{
    public static int Main(string[] args)
    {
        var cases=new List<object>();
        void Check(string name,bool pass){cases.Add(new{name,pass});if(!pass)throw new Exception(name);}
        bool repaired=args[0]!="control",lifecycle=args[0]=="candidate-v3";
        var type=typeof(HSPE.AMModules.BlendShapesEditor);
        KKS_ExpressionStudio.KkpeBridge.CurrentEditorType=type;
        var e0=new HSPE.AMModules.BlendShapesEditor("char0");var e1=new HSPE.AMModules.BlendShapesEditor("char1");
        var p0=new HSPE.CharaPoseController{_blendShapesEditor=e0};var p1=new HSPE.CharaPoseController{_blendShapesEditor=e1};
        var t0=new UnityEngine.Transform{Owner=p0};var t1=new UnityEngine.Transform{Owner=p1};
        var c0=new Studio.OCIChar{guideObject=new Studio.GuideObject{transformTarget=t0}};
        var c1=new Studio.OCIChar{guideObject=new Studio.GuideObject{transformTarget=t1}};
        List<object> A(Studio.OCIChar c)=>KKS_ExpressionStudio.KkpeBridge.PrimaryLookup(c);
        List<object> F(Studio.OCIChar c){KKS_ExpressionStudio.KkpeBridge.Trace.Clear();return KKS_ExpressionStudio.KkpeBridge.EditorsFor(c);}
        bool Throws(Action action){try{action();return false;}catch(Exception error){while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;return error is UnityEngine.MissingReferenceException||error is InvalidOperationException;}}
        var a=A(c0);var b=A(c1);
        Check("owning-root-separates-two-characters",repaired?a.Count==1&&ReferenceEquals(a[0],e0)&&b.Count==1&&ReferenceEquals(b[0],e1):a.Count==0&&b.Count==0);
        p0._blendShapesEditor=null;Check("not-ready-keeps-empty",A(c0).Count==0);
        p0._blendShapesEditor=e0;Check("later-call-retries",repaired?A(c0).Count==1:A(c0).Count==0);
        KKS_ExpressionStudio.KkpeBridge.FaceEditor=e1;
        var face=A(c0);Check("face-map-remains-authoritative",face.Count==1&&ReferenceEquals(face[0],e1));KKS_ExpressionStudio.KkpeBridge.FaceEditor=null;
        Check("null-oci-keeps-empty",A(null).Count==0);
        p0._blendShapesEditor=new object();Check("wrong-editor-type-keeps-empty",A(c0).Count==0);p0._blendShapesEditor=e0;
        var list=new List<object>();UnityEngine.Resources.Calls=0;KKS_ExpressionStudio.Log.Warnings=0;
        KKS_ExpressionStudio.KkpeBridge.ScanUnity(list);
        Check("non-Unity-editor-type-does-not-enter-native-search",repaired?UnityEngine.Resources.Calls==0&&KKS_ExpressionStudio.Log.Warnings==0:UnityEngine.Resources.Calls==1&&KKS_ExpressionStudio.Log.Warnings==1);
        var unityEditor=new SyntheticUnityEditor();UnityEngine.Resources.Items=new[]{unityEditor,unityEditor};KKS_ExpressionStudio.KkpeBridge.CurrentEditorType=typeof(SyntheticUnityEditor);UnityEngine.Resources.Calls=0;
        list.Clear();list.Add(unityEditor);KKS_ExpressionStudio.KkpeBridge.ScanUnity(list);
        Check("valid-unity-fallback-preserves-dedup",UnityEngine.Resources.Calls==1&&list.Count==1&&ReferenceEquals(list[0],unityEditor));
        KKS_ExpressionStudio.KkpeBridge.CurrentEditorType=null;UnityEngine.Resources.Calls=0;KKS_ExpressionStudio.KkpeBridge.ScanUnity(list);
        Check("null-editor-type-no-search",UnityEngine.Resources.Calls==0);KKS_ExpressionStudio.KkpeBridge.CurrentEditorType=type;

        var character=c0.charInfo;c0.charInfo=null;t0.NativeCalls=0;
        Check("null-character-no-owner-native-call",lifecycle||!repaired?A(c0).Count==0&&t0.NativeCalls==0:A(c0).Count==1&&t0.NativeCalls==1);c0.charInfo=character;
        character.Destroyed=true;t0.NativeCalls=0;
        Check("destroyed-character-no-owner-native-call",lifecycle||!repaired?A(c0).Count==0&&t0.NativeCalls==0:A(c0).Count==1&&t0.NativeCalls==1);character.Destroyed=false;
        var guide=c0.guideObject;c0.guideObject=null;Check("null-guide-keeps-empty",A(c0).Count==0);c0.guideObject=guide;
        guide.Destroyed=true;t0.NativeCalls=0;
        Check("destroyed-guide-no-owner-native-call",lifecycle||!repaired?A(c0).Count==0&&t0.NativeCalls==0:A(c0).Count==1&&t0.NativeCalls==1);guide.Destroyed=false;
        guide.transformTarget=null;Check("null-transform-keeps-empty",A(c0).Count==0);guide.transformTarget=t0;
        t0.Destroyed=true;t0.NativeCalls=0;
        bool destroyedEscapes=Throws(()=>A(c0));
        Check("destroyed-transform-does-not-escape",lifecycle||!repaired?!destroyedEscapes&&t0.NativeCalls==0:destroyedEscapes&&t0.NativeCalls==1);
        KKS_ExpressionStudio.KkpeBridge.FaceEditor=e1;t0.NativeCalls=0;
        face=A(c0);Check("face-map-priority-does-not-touch-destroyed-owner",face.Count==1&&ReferenceEquals(face[0],e1)&&t0.NativeCalls==0);
        KKS_ExpressionStudio.KkpeBridge.FaceEditor=null;t0.Destroyed=false;
        t0.Owner=null;Check("absent-controller-keeps-empty",A(c0).Count==0);t0.Owner=p0;
        p0.Destroyed=true;Check("destroyed-controller-not-returned",lifecycle||!repaired?A(c0).Count==0:A(c0).Count==1);p0.Destroyed=false;
        t0.ThrowBusinessError=true;
        Check("unrelated-native-business-failure-not-swallowed",repaired?Throws(()=>A(c0)):!Throws(()=>A(c0)));t0.ThrowBusinessError=false;

        KKS_ExpressionStudio.KkpeBridge.CharacterFallback=e1;
        var full=F(c0);Check("full-EditorsFor-owner-precedes-character-fallback",repaired?full.Count==1&&ReferenceEquals(full[0],e0)&&!KKS_ExpressionStudio.KkpeBridge.Trace.Contains("character"):full.Count==1&&ReferenceEquals(full[0],e1));
        p0._blendShapesEditor=null;
        full=F(c0);Check("full-EditorsFor-not-ready-preserves-character-fallback",full.Count==1&&ReferenceEquals(full[0],e1));
        KKS_ExpressionStudio.KkpeBridge.CharacterFallback=null;KKS_ExpressionStudio.KkpeBridge.StaticFallback=e1;
        full=F(c0);Check("full-EditorsFor-preserves-static-fallback",full.Count==1&&ReferenceEquals(full[0],e1)&&KKS_ExpressionStudio.KkpeBridge.Trace.SequenceEqual(new[]{"face","character","statics"}));
        p0._blendShapesEditor=e0;t0.Destroyed=true;
        bool fullDestroyedEscapes=Throws(()=>F(c0));
        Check("full-EditorsFor-destroyed-owner-resumes-existing-fallback",lifecycle||!repaired?!fullDestroyedEscapes&&KKS_ExpressionStudio.KkpeBridge.Trace.Contains("statics"):fullDestroyedEscapes&&!KKS_ExpressionStudio.KkpeBridge.Trace.Contains("statics"));
        t0.Destroyed=false;KKS_ExpressionStudio.KkpeBridge.StaticFallback=null;KKS_ExpressionStudio.KkpeBridge.SceneFallback=e1;p0._blendShapesEditor=null;
        full=F(c0);Check("full-EditorsFor-preserves-scene-fallback",full.Count==1&&ReferenceEquals(full[0],e1)&&KKS_ExpressionStudio.KkpeBridge.Trace.SequenceEqual(new[]{"face","character","statics","scene"}));
        KKS_ExpressionStudio.KkpeBridge.SceneFallback=null;p0._blendShapesEditor=e0;
        full=F(c1);Check("full-EditorsFor-second-owner-remains-distinct",repaired?full.Count==1&&ReferenceEquals(full[0],e1):full.Count==0);
        var output=JsonSerializer.Serialize(new{mode=args[0],passed=true,cases,owningRootResultCounts=new[]{a.Count,b.Count},destroyedTransformEscaped=destroyedEscapes,fullDestroyedOwnerEscaped=fullDestroyedEscapes,scope="Actual primary-prefix, full EditorsFor control flow and entire ScanUnity CIL; imported actual helper. Unity fake-null/native calls, face map and fallback callees are synthetic; does not execute real Unity or editor mutations."},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(args[1],output);Console.WriteLine(output);return 0;
    }
}
