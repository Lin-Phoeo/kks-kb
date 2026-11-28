using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;

public static class BindingProbe
{
    public static int Run(string ignored1,string ignored2,string game,string mode)
    {
        ResolveEventHandler resolver=(s,e)=>{string path=Path.Combine(game,"BepInEx/core",new AssemblyName(e.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
        AppDomain.CurrentDomain.AssemblyResolve+=resolver;
        try{return Mechanism.Run(mode);}
        catch(Exception e){Console.WriteLine("UNEXPECTED "+e);return 1;}
        finally{AppDomain.CurrentDomain.AssemblyResolve-=resolver;}
    }
}
// These types belong to this fixture assembly; no game or AntiGravity assembly is loaded.
public sealed class DynamicBone
{
    public int Calls;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdateDynamicBones(float step){Calls++;}
}
namespace AntiGravity
{
    public sealed class TimelinePhysics
    {
        internal readonly Harmony _harmony;
        public TimelinePhysics(Harmony harmony){_harmony=harmony;}
        public void Dispose(){throw new NotImplementedException("Original or candidate Dispose CIL must be inserted");}
        public sealed class BoneRestore{}
        public static int BeforeCalls,AfterCalls;
        public static bool Restored,OriginalRan;
        public static void BeforeBone(DynamicBone __instance,out BoneRestore __state){BeforeCalls++;__state=new BoneRestore();}
        private static Exception AfterBone(Exception __exception,BoneRestore __state,bool __runOriginal)
        {AfterCalls++;Restored=__state!=null;OriginalRan=__runOriginal;return __exception;}
    }
}
public static class Mechanism
{
    const string Own="codex.antigravity.actual-cil-probe";
    const string Foreign="codex.antigravity.actual-cil-probe.foreign";
    static int foreignCalls,passed,failed;
    public static void Other(){foreignCalls++;}
    static void Check(string name,bool ok){if(ok)passed++;else failed++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);}
    static bool Owner(MethodInfo target,string id){var p=Harmony.GetPatchInfo(target);return p!=null&&p.Owners.Contains(id);}
    public static int Run(string mode)
    {
        bool candidate=mode=="candidate";
        if(!candidate&&mode!="original")throw new ArgumentException("Expected original or candidate");
        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(Harmony).Assembly.Location))).Replace("-","").ToLowerInvariant();
        Check("exact Harmony",hash=="1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031");if(failed!=0)return 1;
        var target=typeof(DynamicBone).GetMethod("UpdateDynamicBones",BindingFlags.NonPublic|BindingFlags.Instance);
        var before=typeof(AntiGravity.TimelinePhysics).GetMethod("BeforeBone");
        var after=typeof(AntiGravity.TimelinePhysics).GetMethod("AfterBone",BindingFlags.NonPublic|BindingFlags.Static);
        Check("synthetic target and finalizer same fixture only",target.DeclaringType.Assembly==typeof(Mechanism).Assembly&&after.DeclaringType.Assembly==typeof(Mechanism).Assembly);
        var h=new Harmony(Own);var foreign=new Harmony(Foreign);var physics=new AntiGravity.TimelinePhysics(h);
        try
        {
            h.Patch(target,prefix:new HarmonyMethod(before){priority=0},finalizer:new HarmonyMethod(after){priority=800});
            if(candidate)foreign.Patch(target,postfix:new HarmonyMethod(typeof(Mechanism).GetMethod("Other")));
            var subject=new DynamicBone();target.Invoke(subject,new object[]{.02f});
            Check("original prefix finalizer execution",subject.Calls==1&&AntiGravity.TimelinePhysics.BeforeCalls==1&&AntiGravity.TimelinePhysics.AfterCalls==1&&AntiGravity.TimelinePhysics.Restored&&AntiGravity.TimelinePhysics.OriginalRan);
            Exception error=null;try{physics.Dispose();}catch(Exception e){error=e;}
            Console.WriteLine("DISPOSE_ERROR "+(error==null?"none":error.ToString()));
            if(!candidate)Check("original exact __runOriginal failure",error!=null&&error.ToString().Contains("Parameter __runOriginal does not contain a valid index"));
            else
            {
                Check("actual candidate Dispose succeeds",error==null);Check("own owner gone",!Owner(target,Own));Check("foreign owner preserved",Owner(target,Foreign));
                target.Invoke(subject,new object[]{.02f});Check("original and foreign remain callable",subject.Calls==2&&AntiGravity.TimelinePhysics.BeforeCalls==1&&AntiGravity.TimelinePhysics.AfterCalls==1&&foreignCalls==2);
                physics.Dispose();Check("repeat actual Dispose preserves foreign",!Owner(target,Own)&&Owner(target,Foreign));
            }
        }
        finally
        {
            try{h.Unpatch(target,after);h.UnpatchSelf();}catch(Exception e){Check("own cleanup succeeded",false);Console.WriteLine(e);}
            try{foreign.UnpatchSelf();}catch(Exception e){Check("foreign cleanup succeeded",false);Console.WriteLine(e);}
            Check("synthetic hooks all cleaned",!Owner(target,Own)&&!Owner(target,Foreign));
        }
        Console.WriteLine("RESULT mode="+mode+" passed="+passed+" failed="+failed+" actualDisposeCil=True gameMethodsPatched=False runtime="+Environment.Version);
        return failed==0?0:1;
    }
}
