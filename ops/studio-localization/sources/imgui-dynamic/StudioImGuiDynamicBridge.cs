using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace Codex.StudioImGuiDynamicBridge
{
    [BepInPlugin(Guid, "工作室动态文本 / Studio IMGUI Dynamic Text", "1.0.1")]
    [BepInProcess("CharaStudio")]
    [BepInDependency("gravydevsupreme.xunity.autotranslator", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string Guid = "codex.studio.imgui.dynamictext";
        private const BindingFlags All = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        private static Plugin _instance;
        private Harmony _harmony;
        private MethodInfo _hook;
        private FieldInfo _current, _pluginCaches, _globalCache, _callCache, _enabled, _disabled, _translatedMode;
        private readonly ConditionalWeakTable<object, CacheOwner> _owners = new ConditionalWeakTable<object, CacheOwner>();
        private readonly Dictionary<Type,PropertyInfo> _fallbackProperties = new Dictionary<Type,PropertyInfo>();
        private readonly CacheOwner _nullOwner = new CacheOwner{Name="global"};
        private RuleMatcher _matcher;
        private readonly Dictionary<string, Observation> _observations = new Dictionary<string, Observation>(StringComparer.Ordinal);
        private string _output;
        private bool _reported;
        private int _errorCount;
        private long _applied, _nanoticks, _maxTicks;
        private static readonly Regex Slot = new Regex(@"\ASlot ([1-3])  \((?:empty|FOV [0-9]+)\)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20));
        private static readonly Regex ViewHint = new Regex(@"\AViews store the exact look direction and zoom - enter POV \([A-Za-z0-9 +]+\) to use them\.\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20));
        private sealed class CacheOwner { internal string Name; internal PropertyInfo Fallback; }
        private struct State
        {
            internal string Original, Owner, ObservationId;
            internal bool Enabled, Disabled, TranslatedMode, AllowGlobal;
        }
        private sealed class Observation
        {
            public string id, owner, original, afterCore, afterBridge, ruleId;
            public bool coreUnchanged, bridgeMatched, hooksEnabled, temporarilyDisabled, translatedMode;
            public int samples, frame;
            public string[] pluginFrames;
        }

        private void Awake()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Info.Location), "Codex.StudioImGuiDynamicBridge.json");
                if (!File.Exists(path)) { Logger.LogInfo("STUDIO_IMGUI_DYNAMIC_INACTIVE missingLocalRules=True"); return; }
                _matcher = new RuleMatcher(JsonConvert.DeserializeObject<RuleData[]>(File.ReadAllText(path)));
                Assembly core = AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="XUnity.AutoTranslator.Plugin.Core");
                using(var algorithm=SHA256.Create())
                using(var stream=File.OpenRead(core.Location))
                    if(BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!="9700854b20f25b7a81cff2c46da08c12bd5f27cc2544efdfe62dc4cb548bd002")
                        throw new InvalidOperationException("Unverified XUnity Core build");
                Type plugin = core.GetType("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin", true);
                _hook=plugin.GetMethod("Hook_TextChanged", All, null, new[]{typeof(object),typeof(bool)},null);
                _current=plugin.GetField("Current",All); _pluginCaches=plugin.GetField("PluginTextCaches",All);_globalCache=plugin.GetField("TextCache",All);
                _enabled=plugin.GetField("_textHooksEnabled",All);_disabled=plugin.GetField("_temporarilyDisabled",All);
                _translatedMode=plugin.GetField("_isInTranslatedMode",All);
                _callCache=core.GetType("XUnity.AutoTranslator.Plugin.Core.CallOrigin",true).GetField("TextCache",All);
                if(_hook==null||_current==null||_pluginCaches==null||_globalCache==null||_enabled==null||_disabled==null||_translatedMode==null||_callCache==null)
                    throw new InvalidOperationException("Translation hook signature differs");
                string requested=Environment.GetEnvironmentVariable("KKS_IMGUI_DYNAMIC_VERIFY_OUTPUT");
                if(!string.IsNullOrEmpty(requested))
                {
                    string allowed=Path.GetFullPath(Path.Combine(Paths.GameRootPath,".scratch/studio-translation-20261126/distribution-fonts-20261127/ui"))+Path.DirectorySeparatorChar;
                    string full=Path.GetFullPath(requested);
                    if(!Path.IsPathRooted(requested)||!full.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||Path.GetExtension(full)!=".json"||File.Exists(full))
                        throw new InvalidOperationException("Expected a new task-local report path");
                    _output=full;
                }
                _instance=this;_harmony=new Harmony(Guid);
                _harmony.Patch(_hook,prefix:new HarmonyMethod(typeof(Plugin).GetMethod("BeforeText",All)),
                    postfix:new HarmonyMethod(typeof(Plugin).GetMethod("AfterText",All)){priority=Priority.Last});
                Logger.LogInfo("STUDIO_IMGUI_DYNAMIC_READY rules="+_matcher.RuleCount+" exact="+_matcher.ExactCount+" maxRegexInput=400 maxExactInput=12000 lru=2048");
            }
            catch(Exception error){Logger.LogError("STUDIO_IMGUI_DYNAMIC_REFUSED "+error.GetType().Name);Cleanup();}
        }

        private CacheOwner Owner(object cache, object plugin)
        {
            if(cache==null)return _nullOwner;
            CacheOwner remembered;
            if(_owners.TryGetValue(cache,out remembered))return remembered;
            PropertyInfo fallback;
            if(!_fallbackProperties.TryGetValue(cache.GetType(),out fallback))
            {fallback=cache.GetType().GetProperty("AllowFallback",All);_fallbackProperties.Add(cache.GetType(),fallback);}
            if(ReferenceEquals(cache,_globalCache.GetValue(plugin)))
            {remembered=new CacheOwner{Name="global",Fallback=fallback};_owners.Add(cache,remembered);return remembered;}
            object first=cache;
            FieldInfo firstField=cache.GetType().GetField("_first",All);
            if(firstField!=null)first=firstField.GetValue(cache);
            IDictionary caches=(IDictionary)_pluginCaches.GetValue(plugin);
            foreach(DictionaryEntry item in caches)
                if(ReferenceEquals(item.Value,first))
                {
                    string name=(string)item.Key;
                    remembered=new CacheOwner{Name=name,Fallback=fallback};_owners.Add(cache,remembered);
                    return remembered;
                }
            remembered=new CacheOwner{Name=null,Fallback=fallback};_owners.Add(cache,remembered);
            return remembered;
        }
        private static void BeforeText(object __0, out State __state)
        {
            __state=default(State);
            Plugin self=_instance;GUIContent content=__0 as GUIContent;
            if(self==null||content==null||string.IsNullOrEmpty(content.text)||content.text.Length>RuleMatcher.MaximumExactInput)return;
            try
            {
                object plugin=self._current.GetValue(null);
                object activeCache=self._callCache.GetValue(null);
                CacheOwner ownerInfo=self.Owner(activeCache,plugin);
                string owner=ownerInfo.Name;
                bool fallback=activeCache==null||owner=="global"||(ownerInfo.Fallback!=null&&(bool)ownerInfo.Fallback.GetValue(activeCache,null));
                if(!self._matcher.HasOwner(owner)&&!(owner!=null&&fallback&&self._matcher.HasOwner("global")))return;
                __state=new State{Original=content.text,Owner=owner,ObservationId=self.ObservationId(owner,content.text),
                    Enabled=(bool)self._enabled.GetValue(plugin),Disabled=(bool)self._disabled.GetValue(plugin),
                    TranslatedMode=(bool)self._translatedMode.GetValue(plugin),AllowGlobal=fallback};
            }
            catch(Exception error){self.Warn(error);}
        }
        private static void AfterText(object __0, State __state)
        {
            Plugin self=_instance;GUIContent content=__0 as GUIContent;
            if(self==null||content==null||__state.Original==null)return;
            try
            {
                string afterCore=content.text,translated,rule;
                bool matched=false;rule=null;
                long before=self._output==null?0:Stopwatch.GetTimestamp();
                if(__state.Enabled&&!__state.Disabled&&__state.TranslatedMode&&afterCore==__state.Original&&
                    self._matcher.TryTranslate(__state.Owner,__state.Original,__state.AllowGlobal,out translated,out rule))
                {content.text=translated;self._applied++;matched=true;}
                if(self._output!=null){long elapsed=Stopwatch.GetTimestamp()-before;self._nanoticks+=elapsed;self._maxTicks=Math.Max(self._maxTicks,elapsed);}
                self.Observe(__state,afterCore,content.text,rule,matched);
            }
            catch(Exception error){self.Warn(error);}
        }
        private string ObservationId(string owner,string source)
        {
            if(_output==null||owner!="KK_PerspectiveX")return null;
            if(source=="Cozy (60)")return "preset-cozy";
            if(source=="Natural (90)")return "preset-natural";
            if(source=="Action (110)")return "preset-action";
            Match slot=Slot.Match(source);
            if(slot.Success)return "slot-"+slot.Groups[1].Value;
            return ViewHint.IsMatch(source)?"view-hint":null;
        }
        private void Observe(State state,string afterCore,string afterBridge,string rule,bool matched)
        {
            if(state.ObservationId==null||_reported)return;
            Observation prior;_observations.TryGetValue(state.ObservationId,out prior);
            if(prior!=null&&(prior.bridgeMatched||prior.samples>=30||prior.frame==Time.frameCount))return;
            var frames=new List<string>();
            foreach(StackFrame frame in new StackTrace(false).GetFrames()??new StackFrame[0])
            {
                MethodBase method=frame.GetMethod();
                if(method!=null&&method.DeclaringType!=null&&method.DeclaringType.Assembly.GetName().Name=="KK_PerspectiveX")
                    frames.Add(method.DeclaringType.FullName+"."+method.Name);
            }
            var row=new Observation{id=state.ObservationId,owner=state.Owner,original=state.Original,afterCore=afterCore,
                afterBridge=afterBridge,ruleId=rule,coreUnchanged=afterCore==state.Original,bridgeMatched=matched,
                hooksEnabled=state.Enabled,temporarilyDisabled=state.Disabled,translatedMode=state.TranslatedMode,
                frame=Time.frameCount,samples=prior==null?1:prior.samples+1,pluginFrames=frames.ToArray()};
            _observations[state.ObservationId]=row;
            if(prior==null)Logger.LogInfo("STUDIO_IMGUI_DYNAMIC_FIRST id="+row.id+" owner="+row.owner+" coreUnchanged="+row.coreUnchanged+" bridgeMatched="+matched);
            if(_observations.Count==7&&_observations.Values.All(item=>item.bridgeMatched&&item.pluginFrames.Length>0))WriteReport("all-seven-observed");
        }
        private void WriteReport(string phase)
        {
            if(_output==null||_reported)return;
            _reported=true;
            var report=new{phase,rules=_matcher.RuleCount,observed=_observations.Count,rows=_observations.Values.ToArray(),
                applied=_applied,cacheHits=_matcher.Hits,regexEvaluations=_matcher.Evaluations,cacheCount=_matcher.CachedCount,timeouts=_matcher.Timeouts,
                callbackTicks=_nanoticks,maximumCallbackTicks=_maxTicks,ticksPerSecond=Stopwatch.Frequency,
                scope="Actual unchanged GUIContent after XUnity, selected plugin cache owner by reference. No translation APIs, XUnity cache mutations, settings changes or network.",
                limitation="Only whitelisted PerspectiveX control text is recorded. callbackTicks measures the postfix matcher, excluding prefix identity and flag reflection. Counts are not pixel or font verification."};
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_output));string temp=_output+".tmp-"+System.Guid.NewGuid().ToString("N");
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write))
                using(var writer=new StreamWriter(stream))writer.Write(JsonConvert.SerializeObject(report,Formatting.Indented));
                File.Move(temp,_output);
                Logger.LogInfo("STUDIO_IMGUI_DYNAMIC_REPORT observed="+_observations.Count+" applied="+_applied);
            }catch(Exception error){Warn(error);}
        }
        private void Warn(Exception error){if(_errorCount++<3)Logger.LogWarning("STUDIO_IMGUI_DYNAMIC_ERROR "+error.GetType().Name);}
        private void OnDestroy(){if(_matcher!=null)WriteReport("shutdown");Cleanup();}
        private void Cleanup()
        {
            if(_harmony!=null&&_hook!=null)_harmony.Unpatch(_hook,HarmonyPatchType.All,Guid);
            if(ReferenceEquals(_instance,this))_instance=null;
            _harmony=null;_hook=null;
        }
    }
}
