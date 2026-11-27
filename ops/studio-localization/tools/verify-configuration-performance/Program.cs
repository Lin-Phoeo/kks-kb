using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.Json;

internal delegate bool CatalogLookup(string guid,string kind,string section,string key,string source,out string value);
internal static class Program
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static long _sink;
    private sealed record Query(string Guid,string Kind,string Section,string Key,string Source,bool Hit);
    private static void Main(string[] args)
    {
        if(args.Length<3)throw new ArgumentException("GAME ADAPTER REPORT [CATALOG]");
        string game=Path.GetFullPath(args[0]),adapterPath=Path.GetFullPath(args[1]),report=Path.GetFullPath(args[2]);
        string[] folders={Path.GetDirectoryName(adapterPath),Path.Combine(game,"BepInEx/core"),Path.Combine(game,"BepInEx/plugins/ConfigurationManager"),Path.Combine(game,"BepInEx/plugins"),Path.Combine(game,"CharaStudio_Data/Managed")};
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{
            foreach(string folder in folders){string file=Path.Combine(folder,name.Name+".dll");if(File.Exists(file))return context.LoadFromAssemblyPath(file);}
            return null;
        };
        var adapter=AssemblyLoadContext.Default.LoadFromAssemblyPath(adapterPath);
        object regression=Regression.Run(game,adapter,args.Length>3?Path.GetFullPath(args[3]):null);
        var rowType=adapter.GetType("Codex.StudioConfigLocalization.TranslationEntry",true);
        var catalogType=adapter.GetType("Codex.StudioConfigLocalization.TranslationCatalog",true);
        var rows=Array.CreateInstance(rowType,4096);
        var plain=new Query[256];var multiline=new Query[256];
        for(int i=0;i<rows.Length;i++){
            string guid="plugin."+i%32,kind=i%2==0?"setting":"description",section="Category"+i%8,key="Setting"+i;
            string text=i%2==0?"Visible Setting "+i:"Description for setting "+i+". Keep the original values and choose a display option.";
            object row=Activator.CreateInstance(rowType);
            foreach(var pair in new[]{("pluginGuid",guid),("kind",kind),("section",section),("settingKey",key),("source",text),("value","译文 "+i)})rowType.GetField(pair.Item1).SetValue(row,pair.Item2);
            rows.SetValue(row,i);
            if(i<plain.Length)plain[i]=new Query(guid,kind,section,key,text,true);
        }
        object catalog=Activator.CreateInstance(catalogType,new object[]{rows});
        var lookup=(CatalogLookup)catalogType.GetMethod("TryGet").CreateDelegate(typeof(CatalogLookup),catalog);
        var mixed=plain.Select((query,index)=>index%4==0?query with{Guid="missing.plugin",Hit=false}:query).ToArray();
        var multilineRows=Array.CreateInstance(rowType,multiline.Length);
        for(int i=0;i<multiline.Length;i++){
            object row=Activator.CreateInstance(rowType);
            foreach(var field in rowType.GetFields())field.SetValue(row,field.GetValue(rows.GetValue(i)));
            string original=(string)rowType.GetField("source").GetValue(row)+"\nSecond line";
            rowType.GetField("source").SetValue(row,original);
            multilineRows.SetValue(row,i);
            multiline[i]=plain[i] with{Source=original.Replace("\n","\r\n")};
        }
        var multilineCatalog=Activator.CreateInstance(catalogType,new object[]{multilineRows});
        var multilineLookup=(CatalogLookup)catalogType.GetMethod("TryGet").CreateDelegate(typeof(CatalogLookup),multilineCatalog);
        var benchmarks=new List<object>{
            Measure("plain_hits",lookup,plain),Measure("mixed_hits_and_misses",lookup,mixed),Measure("CRLF_description_hits",multilineLookup,multiline)
        };
        var plugin=adapter.GetType("Codex.StudioConfigLocalization.Plugin",true);
        object instance=FormatterServices.GetUninitializedObject(plugin);
        Type unity=plugin.BaseType;
        while(unity!=null&&unity.FullName!="UnityEngine.Object")unity=unity.BaseType;
        var cached=unity.GetField("m_CachedPtr",All);
        if(cached==null)throw new Exception("Unity managed pointer field unavailable");
        cached.SetValue(instance,new IntPtr(1));
        plugin.GetField("_observed",All).SetValue(instance,new HashSet<string>(StringComparer.Ordinal));
        plugin.GetField("_sourceHashes",All).SetValue(instance,new Dictionary<string,string>(StringComparer.Ordinal));
        plugin.GetField("_instance",All).SetValue(null,instance);
        var enabled=plugin.GetField("_auditEnabled",All);
        if(enabled!=null)enabled.SetValue(instance,false);
        var observe=(Action<string,string,string,string,string>)plugin.GetMethod("Observe",All).CreateDelegate(typeof(Action<string,string,string,string,string>));
        benchmarks.Add(MeasureAction("observe_without_diagnostics",()=>observe("plugin.a","General","Shared","setting","Original text")));
        var observed=(HashSet<string>)plugin.GetField("_observed",All).GetValue(instance);
        var sourceHashes=(Dictionary<string,string>)plugin.GetField("_sourceHashes",All).GetValue(instance);
        if(enabled!=null&&(observed.Count!=0||sourceHashes.Count!=0))throw new Exception("Disabled diagnostics recorded observations");
        if(enabled!=null){
            plugin.GetField("_catalog",All).SetValue(null,catalog);
            plugin.GetMethod("Update",All).Invoke(instance,null);
            plugin.GetMethod("ManagerListPostfix",All).Invoke(null,null);
            plugin.GetMethod("WriteAudit",All).Invoke(instance,new object[]{false,false});
            if((bool)plugin.GetField("_audited",All).GetValue(instance)||(bool)plugin.GetField("_managerAudited",All).GetValue(instance))throw new Exception("Disabled diagnostics scheduled an audit");
            plugin.GetField("_catalog",All).SetValue(null,null);
        }
        if(enabled!=null)enabled.SetValue(instance,true);
        benchmarks.Add(MeasureAction("observe_with_diagnostics_repeated_identity",()=>observe("plugin.a","General","Shared","setting","Original text")));
        string expectedHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("Original text"))).ToLowerInvariant();
        if(observed.Count!=1||sourceHashes.Count!=1||!observed.Contains("plugin.a\tGeneral\tShared\tsetting\t"+expectedHash))throw new Exception("Enabled diagnostics changed observation identity");
        plugin.GetField("_instance",All).SetValue(null,null);
        cached.SetValue(instance,IntPtr.Zero);
        var result=new{passed=true,regression,benchmarks,diagnostics=new{gateAvailable=enabled!=null,disabledStatePreserved=enabled==null?(bool?)null:true,enabledIdentityPreserved=true},adapterSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(adapterPath))).ToLowerInvariant(),
            environment=new{framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),stopwatchFrequency=Stopwatch.Frequency,tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")??"runtime default"},
            scope="Actual adapter lookup and Observe methods on managed fixtures. Unity wrapper has a nonzero managed pointer sentinel solely to avoid native null checks; no Unity lifecycle/render/native call is invoked.",
            limitations="Allocation/timing uses .NET 10 on this host, not the game's Mono JIT. GUI drawing, Harmony overhead and game frame rate are not measured."};
        File.WriteAllText(report,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{passed=true,benchmarks}));
    }
    private static object Measure(string name,CatalogLookup lookup,Query[] queries)
    {
        Action work=()=>{
            long count=0;
            for(int i=0;i<200000;i++){
                Query q=queries[i%queries.Length];
                bool found=lookup(q.Guid,q.Kind,q.Section,q.Key,q.Source,out string value);
                if(found!=q.Hit)throw new Exception("Wrong lookup result");
                if(found)count+=value.Length;
            }
            _sink=count;
        };
        return MeasureAction(name,work,200000,false);
    }
    private static object MeasureAction(string name,Action action,int calls=200000,bool repeat=true)
    {
        for(int i=0;i<(repeat?10000:1);i++)action();
        var rounds=new List<(double ms,long bytes)>();
        for(int round=0;round<7;round++){
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            long started=Stopwatch.GetTimestamp();
            if(repeat){for(int i=0;i<calls;i++)action();}else action();
            long elapsed=Stopwatch.GetTimestamp()-started;
            long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            rounds.Add(((double)elapsed*1000/Stopwatch.Frequency,bytes));
        }
        var sorted=rounds.OrderBy(r=>r.ms).ToArray();var median=sorted[sorted.Length/2];
        return new{name,calls,medianMilliseconds=median.ms,medianAllocatedBytes=median.bytes,bytesPerCall=(double)median.bytes/calls,
            rounds=rounds.Select(r=>new{milliseconds=r.ms,allocatedBytes=r.bytes}).ToArray()};
    }
}
