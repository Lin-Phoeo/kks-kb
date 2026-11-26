using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using CodexMEColorSpaceFix;
class ManagedRegression{
 static int passed;
 static void Check(bool x,string n){if(!x)throw new Exception("FAIL "+n);Console.WriteLine("PASS "+n);passed++;}
 static RenderTexture Source(){return new RenderTexture(4,4,0,RenderTextureFormat.Default,RenderTextureReadWrite.sRGB);}
 static int Main(){
  MEColorSpaceFixPlugin.Log=new ManualLogSource();
  var src=Source();byte[] bytes=src.Pixels;
  MEColorSpaceFixPlugin.Remember(src,bytes,TextureFormat.ARGB32,true);
  var copy=MEColorSpaceFixPlugin.BuildLinear(src);
#if BASELINE
  Check(Texture2D.DecodeCalls==1,"baseline reproduces the second image decode");
  var fields=typeof(MEColorSpaceFixPlugin).GetFields(BindingFlags.NonPublic|BindingFlags.Static);
  Check(fields.Any(x=>x.Name=="SourceOf")&&fields.Any(x=>x.Name=="LinearCache"),"baseline retains source bytes and generated GPU texture in static caches");
  Console.WriteLine("BASELINE RED expected: safe-path requirement must reject the second image decode");
  if (Texture2D.DecodeCalls != 0) { Console.WriteLine("EXPECTED_RED_SECOND_DECODE"); return 23; }
  throw new Exception("Baseline unexpectedly passed the no-second-decode requirement.");
#else
  Check(copy!=null&&!MEColorSpaceFixPlugin.IsSrgbTexture(copy),"linear output created");
  Check(copy.Pixels.SequenceEqual(src.Pixels),"copy submits unchanged stored data to the GPU API boundary");
  Check(Texture2D.DecodeCalls==0&&Graphics.CopyCalls==1,"no image re-decode");
  Check(copy.filterMode==src.filterMode&&copy.wrapModeU==src.wrapModeU&&copy.wrapModeV==src.wrapModeV&&copy.wrapModeW==src.wrapModeW&&copy.anisoLevel==src.anisoLevel&&copy.mipMapBias==src.mipMapBias,"sampling properties preserved");
  Check(ReferenceEquals(copy,MEColorSpaceFixPlugin.BuildLinear(src))&&Graphics.CopyCalls==1,"same source reuses one GPU copy");
  MEColorSpaceFixPlugin.Release(src);
  Check(copy.destroyed&&((RenderTexture)copy).released&&MEColorSpaceFixPlugin.BuildLinear(src)==null,"token release destroys cached GPU output and removes source");
  MEColorSpaceFixPlugin.Release(src);Check(true,"duplicate release safe");
  src=Source();MEColorSpaceFixPlugin.Remember(src,src.Pixels,TextureFormat.ARGB32,true);Graphics.FailCopy=true;
  int warns=MEColorSpaceFixPlugin.Log.Warnings;
  Check(MEColorSpaceFixPlugin.BuildLinear(src)==null,"copy failure preserves caller original");
  Check(MEColorSpaceFixPlugin.BuildLinear(src)==null&&MEColorSpaceFixPlugin.Log.Warnings==warns+1,"failed source is not retried or repeatedly logged");
  Graphics.FailCopy=false;MEColorSpaceFixPlugin.Release(src);
  src=Source();MEColorSpaceFixPlugin.Remember(src,src.Pixels,TextureFormat.ARGB32,true);UnityEngine.SystemInfo.copyTextureSupport=UnityEngine.Rendering.CopyTextureSupport.None;
  Check(MEColorSpaceFixPlugin.BuildLinear(src)==null,"unsupported GPU falls back without decoding");
  MEColorSpaceFixPlugin.Release(src);UnityEngine.SystemInfo.copyTextureSupport=UnityEngine.Rendering.CopyTextureSupport.Basic;
  src=Source();src.graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.Linear;MEColorSpaceFixPlugin.Remember(src,src.Pixels,TextureFormat.ARGB32,true);
  Check(ReferenceEquals(src,MEColorSpaceFixPlugin.BuildLinear(src)),"already-linear source remains unchanged");MEColorSpaceFixPlugin.Release(src);
  src=Source();MEColorSpaceFixPlugin.Remember(src,src.Pixels,TextureFormat.ARGB32,true);copy=MEColorSpaceFixPlugin.BuildLinear(src);UnityEngine.Object.Destroy(src);MEColorSpaceFixPlugin.Release(src);
  Check(copy.destroyed,"destroyed Unity source wrapper still releases its GPU copy");
  Check(Texture2D.DecodeCalls==0,"all candidate paths avoid image decoder");
  Console.WriteLine("Managed assertions passed: "+passed+"; GPU pixel semantics require real Unity runtime verification.");
  return 0;
#endif
 }
}
