using System;using System.Collections.Generic;using System.Reflection;using System.Runtime.CompilerServices;using System.Linq;using HarmonyLib;
namespace UnityEngine{public class Object{public bool Destroyed;public static bool operator==(Object a,Object b)=>ReferenceEquals(a,b)||(ReferenceEquals(a,null)&&!ReferenceEquals(b,null)&&b.Destroyed)||(ReferenceEquals(b,null)&&!ReferenceEquals(a,null)&&a.Destroyed);public static bool operator!=(Object a,Object b)=>!(a==b);public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>RuntimeHelpers.GetHashCode(this);}}
public class FaceBlendShape{public FBSCtrlMouth MouthCtrl;}
public class ChaInfo:UnityEngine.Object{public FaceBlendShape fbsCtrl{get;set;}=new FaceBlendShape();}
public class ChaControl:ChaInfo{}
public class FBSBase{protected float openRate;public float FixedRate=-1;public float Applied;public int BlendCalls;public void CalculateBlendShape(){Applied=FixedRate>=0?FixedRate:openRate;BlendCalls++;}}
public class FBSCtrlMouth:FBSBase{public bool useAjustWidthScale;public int WidthCalls;[MethodImpl(MethodImplOptions.NoInlining)]public void CalcBlend(float openValue){throw new Exception("Actual KKS CalcBlend CIL required");}public bool AdjustWidthScale(){WidthCalls++;return true;}}
static class Program{
 static int Main(){int count=0,failures=0;void Check(string n,bool b){count++;if(!b)failures++;Console.WriteLine((b?"PASS ":"FAIL ")+n);}
 var type=Assembly.GetExecutingAssembly().GetType("KK_StudioVoice.MouthPatch",true);
 var values=(Dictionary<ChaControl,float>)type.GetField("MouthValues").GetValue(null);
 var attribute=type.GetCustomAttributesData().Single(a=>a.AttributeType==typeof(HarmonyPatch));
 var declaring=(Type)attribute.ConstructorArguments[0].Value;var name=(string)attribute.ConstructorArguments[1].Value;
 var target=declaring.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
 var prefix=type.GetMethod("Prefix");
 bool bindable=target!=null&&target.ReturnType==typeof(void)&&target.GetParameters().Length==1&&target.GetParameters()[0].ParameterType==typeof(float)&&prefix!=null&&prefix.GetParameters()[0].Name=="__instance"&&prefix.GetParameters()[0].ParameterType==declaring&&prefix.GetParameters()[1].Name==target.GetParameters()[0].Name&&prefix.GetParameters()[1].ParameterType==typeof(float).MakeByRefType();
 Check("actual target name and prefix argument contract resolves",bindable);
 void Apply(FBSCtrlMouth mouth,float value){if(bindable){object[] arguments={mouth,value};prefix.Invoke(null,arguments);value=(float)arguments[1];}mouth.CalcBlend(value);}
 try{
  void Scenario(string label,float? owned,float input,float expected,bool other=false,bool destroyed=false,bool width=false){values.Clear();var m=new FBSCtrlMouth{useAjustWidthScale=width};if(owned.HasValue){var c=new ChaControl{Destroyed=destroyed};c.fbsCtrl.MouthCtrl=other?new FBSCtrlMouth():m;values[c]=owned.Value;}Apply(m,input);Check(label,m.Applied==expected&&m.BlendCalls==1&&m.WidthCalls==(width?1:0));}
  Scenario("tracked amplitude drives real CalcBlend input",.7f,.2f,.7f);
  Scenario("untracked native value stays",null,.3f,.3f);
  Scenario("different mouth not overwritten",.8f,.4f,.4f,true);
  Scenario("zero value preserves original positive-only rule",0f,.4f,.4f);
  Scenario("negative value preserves native input",-.1f,.5f,.5f);
  Scenario("destroyed character skipped",.9f,.2f,.2f,false,true);
  Scenario("width adjustment still runs once",.6f,.1f,.6f,false,false,true);
  values.Clear();var one=new FBSCtrlMouth();var two=new FBSCtrlMouth();var c1=new ChaControl();var c2=new ChaControl();c1.fbsCtrl.MouthCtrl=one;c2.fbsCtrl.MouthCtrl=two;values[c1]=.2f;values[c2]=.8f;Apply(one,.5f);Apply(two,.5f);Check("two characters retain distinct mouth ownership",one.Applied==.2f&&two.Applied==.8f);
  values.Remove(c1);Apply(one,.35f);Check("removed ownership returns to native value",one.Applied==.35f);
  var fixedMouth=new FBSCtrlMouth{FixedRate=.45f};Apply(fixedMouth,.9f);Check("existing lip sync FixedRate honored by blend collaborator",fixedMouth.Applied==.45f);
 }finally{values.Clear();}
 Console.WriteLine("checks="+count+" failures="+failures);return failures==0?0:1;
 }
}
