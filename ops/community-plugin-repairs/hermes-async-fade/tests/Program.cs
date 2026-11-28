using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

namespace UnityEngine {
 public struct Color { }
 public static class ColorUtility {public static bool TryParseHtmlString(string s,out Color c){c=new();return false;}}
 public static class Mathf {public static float Clamp(float v,float min,float max)=>v<min?min:v>max?max:v;public static float Max(float a,float b)=>Math.Max(a,b);}
}
namespace ExtensibleSaveFormat { public class PluginData {public Dictionary<string,object> data=new();public int version;} }
namespace BepInEx.Logging { public class ManualLogSource {public readonly List<object> Errors=new();public void LogError(object x)=>Errors.Add(x);public void LogInfo(object x){}public void LogWarning(object x){}} }
namespace FoxAudioLib {
 public enum AudioFormat { Wav=0 }
 public class AudioData {public double Duration{get;set;}public int SampleRate{get;set;}=8000;public float[] Samples{get;set;}=new float[0];}
 public static class AudioCodec {public static byte[] Encode(AudioData d,AudioFormat f)=>new byte[]{1,2,3};}
}
namespace TheBirdOfHermes.Audio {
 public class AudioManager {
  public FoxAudioLib.AudioData Data{get;private set;} public bool HasAudio=>Data!=null;public float[] MonoSamples=>Data?.Samples;
  public void InitFromDecodedData(FoxAudioLib.AudioData data,string name){Data=data;}
  public void ReloadFromData(){} public void Clear(){Data=null;}
 }
 public class AsyncTrackOperation {
  public enum OperationType {Decode,Filter}
  public bool IsComplete{get;set;}public Exception Error{get;set;}public OperationType Type{get;set;}public string Description{get;set;}="fixture";public FoxAudioLib.AudioData Result{get;set;}
  public static AsyncTrackOperation StartDecode(byte[] b,string ext,string name)=>new(){Type=OperationType.Decode};
 }
}
namespace TheBirdOfHermes.Waveform {public class WaveformRenderer {public int Sets;public void SetSamples(float[] s,int rate){Sets++;}public void Clear(){}}}
namespace TheBirdOfHermes {
 public enum WaveformMode {Normal}
 public class AudioLane {public float Volume{get;set;}=1;public bool IsMuted{get;set;}public string Name{get;set;}public UnityEngine.Color Color{get;set;}public bool HasColor{get;set;}}
 public class AudioTrack {
  Audio.AsyncTrackOperation _asyncOp;
  public Audio.AudioManager Audio{get;private set;}=new();public Waveform.WaveformRenderer Waveform{get;private set;}=new();
  public string Name{get;set;}public string FileName{get;set;}public byte[] RawBytes{get;private set;}public float Offset{get;set;}public float TrimStart{get;set;}public float TrimEnd{get;set;}public float FadeInDuration{get;set;}public float FadeOutDuration{get;set;}public WaveformMode NormalizationMode{get;set;}public UnityEngine.Color TrackColor{get;set;}public AudioLane Lane{get;set;}=new();
  public float FullDuration=>throw new Exception("extract");public float EffectiveDuration=>throw new Exception("extract");public bool HasAudio=>throw new Exception("extract");
  public void ClampTrim()=>throw new Exception("extract");public void ClampFade()=>throw new Exception("extract");public bool PollAsyncCompletion()=>throw new Exception("extract");public void LoadFromBytesAsync(byte[] b,string n)=>throw new Exception("extract");public void Clear()=>throw new Exception("extract");
  public void Finish(double duration,Exception error=null){_asyncOp.Result=new(){Duration=duration};_asyncOp.Error=error;_asyncOp.IsComplete=true;}
  public void FilterReady(double duration){Audio.InitFromDecodedData(new(){Duration=duration},"filter.wav");_asyncOp=new(){Type=TheBirdOfHermes.Audio.AsyncTrackOperation.OperationType.Filter,IsComplete=true};}
 }
 public class TrackManager {
  public List<AudioTrack> Tracks=new();public IEnumerable<AudioTrack> AllTracks=>Tracks;public float MasterVolume{get;set;}=1;
  public AudioTrack AddTrackFromBytes(byte[] b,string n){var t=new AudioTrack();t.LoadFromBytesAsync(b,n);Tracks.Add(t);return t;}
  public void MoveTrackToLane(AudioTrack t,AudioLane lane){t.Lane=lane;}public void SelectTrack(AudioTrack t,bool additive){}
 }
 public class HermesDAW {public TrackManager TrackManager{get;set;}=new();public static BepInEx.Logging.ManualLogSource Logger{get;}=new();}
 public class SceneController {public static HermesDAW Plugin{get;set;}public void LoadV4(ExtensibleSaveFormat.PluginData p)=>throw new Exception("extract");public void LoadV5(ExtensibleSaveFormat.PluginData p)=>throw new Exception("extract");public void LoadV6(ExtensibleSaveFormat.PluginData p)=>throw new Exception("extract");}
}

public static class Program {
 public static int Main(string[] args){
  if(args[0]=="extract"){Extract(args[1],args[2]);return 0;}
  bool control=args[0]=="control";var tests=new List<object>();int fail=0;
  void C(string name,bool ok){tests.Add(new{name,passed=ok});if(!ok)fail++;}
  bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
  for(int version=4;version<=6;version++){
   var p=new ExtensibleSaveFormat.PluginData{version=version};var pre=version==4?"track_0_":"lane_0_track_0_";
   p.data[version==4?"trackCount":"laneCount"]=1;if(version!=4)p.data["lane_0_trackCount"]=1;
   p.data[pre+"audio"]=new byte[]{1};p.data[pre+"name"]="synthetic.wav";p.data[pre+"trimStart"]=.05f;p.data[pre+"trimEnd"]=.05f;p.data[pre+"fadeIn"]=.02f;p.data[pre+"fadeOut"]=.03f;
   TheBirdOfHermes.SceneController.Plugin=new();var sc=new TheBirdOfHermes.SceneController();typeof(TheBirdOfHermes.SceneController).GetMethod("LoadV"+version).Invoke(sc,new object[]{p});var t=TheBirdOfHermes.SceneController.Plugin.TrackManager.Tracks.Single();
   C("V"+version+" pending not decoded",!t.HasAudio);C("V"+version+" pending trim retained",Near(t.TrimStart,.05f)&&Near(t.TrimEnd,.05f));
   if(version>4)C("V"+version+" pending fade retained",Near(t.FadeInDuration,.02f)&&Near(t.FadeOutDuration,.03f));
   C("V"+version+" incomplete poll no action",!t.PollAsyncCompletion());t.Finish(.5);C("V"+version+" completed poll true",t.PollAsyncCompletion());C("V"+version+" audio now ready",t.HasAudio&&Near(t.FullDuration,.5f));C("V"+version+" trim preserved after decode",Near(t.TrimStart,.05f)&&Near(t.TrimEnd,.05f));
   if(version>4)C("V"+version+" fade restored after decode",Near(t.FadeInDuration,.02f)&&Near(t.FadeOutDuration,.03f));
   else C("V4 no fade fields default remainszero",t.FadeInDuration==0&&t.FadeOutDuration==0);
   C("V"+version+" second poll false",!t.PollAsyncCompletion());
  }
  var pending=new TheBirdOfHermes.AudioTrack();pending.LoadFromBytesAsync(new byte[]{1},"pending.wav");pending.TrimStart=2;pending.TrimEnd=3;pending.FadeInDuration=2;pending.FadeOutDuration=1;pending.ClampTrim();pending.ClampFade();pending.Finish(1);pending.PollAsyncCompletion();
  C("decode completion clamps oversized trim",Near(pending.TrimStart,.99f)&&Near(pending.TrimEnd,0));C("decode completion clamps fades to remaining length",pending.FadeInDuration>=0&&pending.FadeOutDuration>=0&&pending.FadeInDuration+pending.FadeOutDuration<=.0101f);
  var ready=new TheBirdOfHermes.AudioTrack();ready.FilterReady(2);ready.TrimStart=.25f;ready.TrimEnd=.25f;ready.FadeInDuration=2;ready.FadeOutDuration=2;ready.ClampTrim();ready.ClampFade();C("already decoded proportional fade limits preserved",Near(ready.FadeInDuration,.75f)&&Near(ready.FadeOutDuration,.75f));
  ready.FadeInDuration=-1;ready.FadeOutDuration=9;ready.ClampFade();C("negative and oversized fades normal clamp",Near(ready.FadeInDuration,0)&&Near(ready.FadeOutDuration,1.5f));
  ready.FadeInDuration=3;ready.FadeOutDuration=3;ready.PollAsyncCompletion();C("filter completion still clamps",Near(ready.FadeInDuration,.75f)&&Near(ready.FadeOutDuration,.75f));
  ready.Clear();C("Clear explicitly zeros fades",ready.FadeInDuration==0&&ready.FadeOutDuration==0&&!ready.HasAudio);
  var bad=new TheBirdOfHermes.AudioTrack();bad.LoadFromBytesAsync(new byte[]{1},"bad.wav");bad.FadeInDuration=.2f;bad.Finish(0,new Exception("expected decode failure"));C("decode error returns without audio",bad.PollAsyncCompletion()&&!bad.HasAudio);C("decode error not repeatedly processed",!bad.PollAsyncCompletion());
  var zero=new TheBirdOfHermes.AudioTrack();zero.LoadFromBytesAsync(new byte[]{1},"zero.wav");zero.FadeInDuration=.2f;zero.FadeOutDuration=.3f;zero.ClampFade();zero.Finish(0);zero.PollAsyncCompletion();
  var exploratoryZeroDuration=new { invalidFixture=true, duration=0, zero.TrimStart, zero.TrimEnd, zero.FadeInDuration, zero.FadeOutDuration, zero.EffectiveDuration, scope="Existing ClampTrim minimum behavior on invalid zero-length decoded data; observation only, excluded from pass counts" };

  var result=new{mode=args[0],passed=tests.Count-fail,failed=fail,checks=tests,exploratoryZeroDuration,scope="Actual SceneController LoadV4/V5/V6 and AudioTrack load/clear/clamp/poll/getter IL; synthetic async decoder, audio device, waveform, TrackManager allocation, Unity Mathf and logging boundaries; no real DLL execution or audio playback"};File.WriteAllText(args[1],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{checks=tests.Count,failed=fail,mode=args[0]}));return control?(fail>0?0:1):(fail==0?0:1);
 }
 static void Extract(string source,string output){
  if(File.Exists(output))throw new Exception("Output exists");
  string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();
  if(hash!="d044e20742b7cfdf6dcf9ecf1c10c66a7376dfe263d42c50a33cb8cdbece3743"&&hash!="4a038039a29d930bd34799cc8c177f76817526bbeba1123f2af833e92aa3b9ec")throw new Exception("Unsupported actual-IL input hash");
  if(Path.GetFileName(output)!="Probe.dll"||Path.GetDirectoryName(Path.GetFullPath(output))==Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))throw new Exception("Use isolated output directory with Probe.dll identity");
  using var input=AssemblyDefinition.ReadAssembly(source);using var fixture=AssemblyDefinition.ReadAssembly(Assembly.GetExecutingAssembly().Location);var mod=fixture.MainModule;
  TypeDefinition Local(string n)=>mod.GetTypes().SingleOrDefault(t=>t.FullName==n);
  TypeReference T(TypeReference t){if(t is GenericParameter)return t;if(t is ByReferenceType br)return new ByReferenceType(T(br.ElementType));if(t is ArrayType ar)return new ArrayType(T(ar.ElementType),ar.Rank);if(t is GenericInstanceType g){var n=new GenericInstanceType(mod.ImportReference(g.ElementType));foreach(var x in g.GenericArguments)n.GenericArguments.Add(T(x));return n;}return Local(t.FullName)??mod.ImportReference(t);}
  MethodReference MR(MethodReference m){if(m is GenericInstanceMethod g){var n=new GenericInstanceMethod(MR(g.ElementMethod));foreach(var x in g.GenericArguments)n.GenericArguments.Add(T(x));return n;}var local=Local(m.DeclaringType.FullName);if(local!=null)return local.Methods.Single(x=>x.Name==m.Name&&x.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(m.Parameters.Select(p=>p.ParameterType.FullName)));var c=mod.ImportReference(m);c.DeclaringType=T(m.DeclaringType);return c;}
  FieldReference FR(FieldReference f){var local=Local(f.DeclaringType.FullName);return local==null?mod.ImportReference(f):local.Fields.Single(x=>x.Name==f.Name);}
  foreach(string tn in new[]{"TheBirdOfHermes.AudioTrack","TheBirdOfHermes.SceneController"}){
   var methods=tn.EndsWith("AudioTrack")?new[]{"ClampTrim","ClampFade","PollAsyncCompletion","get_FullDuration","get_EffectiveDuration","get_HasAudio","LoadFromBytesAsync","Clear"}:new[]{"LoadV4","LoadV5","LoadV6"};
   foreach(string name in methods){var src=input.MainModule.GetType(tn).Methods.Single(m=>m.Name==name);var dst=Local(tn).Methods.Single(m=>m.Name==name);dst.Body=new Mono.Cecil.Cil.MethodBody(dst){InitLocals=src.Body.InitLocals,MaxStackSize=src.Body.MaxStackSize};var locals=src.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(T(v.VariableType)));foreach(var v in locals.Values)dst.Body.Variables.Add(v);var map=src.Body.Instructions.ToDictionary(i=>i,_=>Instruction.Create(OpCodes.Nop));foreach(var i in src.Body.Instructions){var d=map[i];d.OpCode=i.OpCode;d.Operand=i.Operand switch{Instruction b=>map[b],Instruction[] bs=>bs.Select(b=>map[b]).ToArray(),VariableDefinition v=>locals[v],ParameterDefinition p=>dst.Parameters[p.Index],MethodReference m=>MR(m),FieldReference f=>FR(f),TypeReference t=>T(t),_=>i.Operand};dst.Body.Instructions.Add(d);}foreach(var h in src.Body.ExceptionHandlers)dst.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType){CatchType=h.CatchType==null?null:T(h.CatchType),TryStart=map[h.TryStart],TryEnd=h.TryEnd==null?null:map[h.TryEnd],HandlerStart=map[h.HandlerStart],HandlerEnd=h.HandlerEnd==null?null:map[h.HandlerEnd]});}
  }
  fixture.Write(output);File.WriteAllText(output+".input-sha256",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
 }
}
