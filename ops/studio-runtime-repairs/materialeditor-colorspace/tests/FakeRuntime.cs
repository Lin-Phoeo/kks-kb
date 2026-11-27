using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
namespace BepInEx {
 public class BepInPlugin:Attribute{public BepInPlugin(string a,string b,string c){}}
 public class BepInDependency:Attribute{public enum DependencyFlags{HardDependency} public BepInDependency(string s,DependencyFlags d){}}
 public class BaseUnityPlugin:UnityEngine.MonoBehaviour{ public Logging.ManualLogSource Logger=new Logging.ManualLogSource();public Configuration.ConfigFile Config=new Configuration.ConfigFile();}
 public static class Paths{public static string GameRootPath="unused";}
}
namespace BepInEx.Logging{public class ManualLogSource{public int Warnings;public void LogInfo(object x){}public void LogError(object x){}public void LogWarning(object x){Warnings++;}}}
namespace BepInEx.Configuration{
 public class ConfigFile{public ConfigEntry<T> Bind<T>(string a,string b,T c,string d){return new ConfigEntry<T>{Value=c};}}
 public class ConfigEntry<T>{public T Value;}
 public class KeyboardShortcut{public KeyboardShortcut(UnityEngine.KeyCode k,params UnityEngine.KeyCode[] a){}public bool IsDown(){return false;}}
}
namespace HarmonyLib{
 public class Harmony{public Harmony(string s){} public void Patch(MethodBase m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}public void UnpatchSelf(){}}
 public class HarmonyMethod{public HarmonyMethod(MethodInfo m){}}
 public static class AccessTools{public static MethodInfo Method(Type t,string n,Type[] a=null){return a==null?t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance):t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance,null,a,null);}}
}
namespace UnityEngine.Rendering{public enum TextureDimension{Tex2D} [Flags] public enum CopyTextureSupport{None=0,Basic=1}}
namespace UnityEngine.Experimental.Rendering{
 public enum GraphicsFormat{Linear=1,Srgb=2}
 public static class GraphicsFormatUtility{public static bool IsSRGBFormat(GraphicsFormat f){return f==GraphicsFormat.Srgb;}public static GraphicsFormat GetLinearFormat(GraphicsFormat f){return GraphicsFormat.Linear;}}
}
namespace UnityEngine{
 public class Object{public bool destroyed;public int GetInstanceID(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}public static void Destroy(Object o){if(!ReferenceEquals(o,null))o.destroyed=true;}public static bool operator ==(Object a,Object b){return (ReferenceEquals(a,null)||a.destroyed)==(ReferenceEquals(b,null)||b.destroyed)&&((ReferenceEquals(a,null)||a.destroyed)||ReferenceEquals(a,b));}public static bool operator !=(Object a,Object b){return !(a==b);}public override bool Equals(object o){return ReferenceEquals(this,o);}public override int GetHashCode(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}}
 public class MonoBehaviour:Object{public void StartCoroutine(IEnumerator x){}}
 public class WaitForSeconds{public WaitForSeconds(float x){}}
 public enum KeyCode{F6,LeftControl,LeftShift}
 public enum TextureFormat{ARGB32,RGBA32}
 public enum RenderTextureFormat{Default}
 public enum RenderTextureReadWrite{Default,Linear,sRGB}
 public class GameObject{}
 public class Texture:Object{public Rendering.TextureDimension dimension=Rendering.TextureDimension.Tex2D;public int antiAliasing=1,mipmapCount=1;public int width=4,height=4;public byte[] Pixels=new byte[]{1,16,64,128,200,255};public int filterMode=2,wrapModeU=1,wrapModeV=2,wrapModeW=3,anisoLevel=4;public float mipMapBias=0.25f;public virtual Experimental.Rendering.GraphicsFormat graphicsFormat{get;set;}}
 public class Texture2D:Texture{public static int DecodeCalls;public Texture2D(int w,int h,TextureFormat f,bool m,bool l){width=w;height=h;}public bool LoadImage(byte[] b){DecodeCalls++;Pixels=(byte[])b.Clone();return true;}}
 public struct RenderTextureDescriptor{public Experimental.Rendering.GraphicsFormat graphicsFormat;public int depthBufferBits;public bool autoGenerateMips;public int width,height;public bool useMipMap;}
 public class RenderTexture:Texture{
  public static RenderTexture active;public static bool FailCreate;public bool released,created=true;public bool useMipMap;private RenderTextureDescriptor d;
  public RenderTexture(int w,int h,int z,RenderTextureFormat f,RenderTextureReadWrite r){width=w;height=h;d=new RenderTextureDescriptor{graphicsFormat=r==RenderTextureReadWrite.sRGB?Experimental.Rendering.GraphicsFormat.Srgb:Experimental.Rendering.GraphicsFormat.Linear,width=w,height=h};}
  public RenderTexture(RenderTextureDescriptor x){d=x;width=x.width;height=x.height;useMipMap=x.useMipMap;}
  public RenderTextureDescriptor descriptor{get{return d;}}
  public override Experimental.Rendering.GraphicsFormat graphicsFormat{get{return d.graphicsFormat;}set{d.graphicsFormat=value;}}
  public bool sRGB{get{return Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(d.graphicsFormat);}}
  public bool IsCreated(){return created;}public bool Create(){created=!FailCreate;return created;}public void Release(){released=true;created=false;}
 }
 public static class SystemInfo{public static Rendering.CopyTextureSupport copyTextureSupport=Rendering.CopyTextureSupport.Basic;}
 public static class GL{public static bool sRGBWrite;}
 public static class Graphics{public static int CopyCalls;public static bool FailCopy;public static void CopyTexture(Texture a,Texture b){CopyCalls++;if(FailCopy)throw new InvalidOperationException("simulated GPU copy failure");b.Pixels=(byte[])a.Pixels.Clone();}public static void Blit(Texture a,Texture b){b.Pixels=(byte[])a.Pixels.Clone();}}
}
namespace CodexMEColorSpaceFix{internal static class RoundTripTest{public static void Run(string p){}}}

namespace CodexMEColorSpaceFix{
 internal static class RuntimeStress{public static System.Collections.IEnumerator Run(string p){yield break;}}
 internal static class GpuCopyGate{public static bool Allows(UnityEngine.RenderTextureDescriptor x){return true;}}
}
