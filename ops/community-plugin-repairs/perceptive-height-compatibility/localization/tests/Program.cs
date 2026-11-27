using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if(args.Length!=6)throw new ArgumentException("CONFIG_DLL EXISTING_CATALOG ADDITIONS DYNAMIC_DLL RULES OUTPUT");
var options=new JsonSerializerOptions{IncludeFields=true};var checks=new List<object>();
void Check(bool value,string name){checks.Add(new{name,passed=value});if(!value)throw new InvalidOperationException(name);}
var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
var entry=assembly.GetType("Codex.StudioConfigLocalization.TranslationEntry",true);var type=assembly.GetType("Codex.StudioConfigLocalization.TranslationCatalog",true);
Array Rows(string path,Type t)=>(Array)JsonSerializer.Deserialize(File.ReadAllText(path),t.MakeArrayType(),options);
var old=Rows(args[1],entry);var incoming=Rows(args[2],entry);var merged=Array.CreateInstance(entry,old.Length+incoming.Length);Array.Copy(old,merged,old.Length);Array.Copy(incoming,0,merged,old.Length,incoming.Length);
var catalog=Activator.CreateInstance(type,new object[]{merged});var lookup=type.GetMethod("TryGet");
foreach(object row in incoming){object[] q={entry.GetField("pluginGuid").GetValue(row),entry.GetField("kind").GetValue(row),entry.GetField("section").GetValue(row),entry.GetField("settingKey").GetValue(row),entry.GetField("source").GetValue(row),null};Check((bool)lookup.Invoke(catalog,q)&&Equals(q[5],entry.GetField("value").GetValue(row)),"catalog "+q[1]+" "+q[3]+" "+q[4]);}
object[] wrong={"unrelated.plugin","setting","General","Units","Units",null};Check(!(bool)lookup.Invoke(catalog,wrong),"F1 GUID boundary");
object[] key={"bucky.kk.perceptiveheightx","setting","Other","Units","Units",null};Check(!(bool)lookup.Invoke(catalog,key),"F1 section boundary");
var dynamic=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[3]));var rule=dynamic.GetType("Codex.StudioImGuiDynamicBridge.RuleData",true);var matcherType=dynamic.GetType("Codex.StudioImGuiDynamicBridge.RuleMatcher",true);var matcher=Activator.CreateInstance(matcherType,new object[]{Rows(args[4],rule)});
var translate=matcherType.GetMethod("TryTranslate",new[]{typeof(string),typeof(string),typeof(string).MakeByRefType(),typeof(string).MakeByRefType()});
void Text(string source,string expected,string owner="PerceptiveHeightX.KKS"){object[] q={owner,source,null,null};bool ok=(bool)translate.Invoke(matcher,q);Check(expected==null?!ok:ok&&Equals(q[2],expected),"HUD "+owner+" "+source);}
Text("PerceptiveHeightX","感知身高 / PerceptiveHeightX");
foreach(string n in new[]{"180.0 cm","180,0 cm","0.1 cm","10000.0 cm","5'10.5\"","5'10,5\""}){Text("POV: "+n,"基准: "+n);foreach(string name in new[]{"Alice_01","角色 A","A to POV: B","角色$1"})Text(name+" to POV: "+n,name+" 看似: "+n);}
foreach(string bad in new[]{"POV: NaN cm","POV: 10 m","POV: -1.0 cm","POV: 10 cm\nextra","A\nB to POV: 10 cm","POV: arbitrary"," to POV: 10 cm","10 cm"})Text(bad,null);
Text("POV: 180.0 cm",null,"KK_PerspectiveX");Text("Alice to POV: 180.0 cm",null,"global");Text("Alice to POV: 180.0 cm",null,"PerceptiveHeightX");
var result=new{passed=true,checks=checks.Count,results=checks,catalogAdditions=incoming.Length,scope="Actual existing TranslationCatalog and RuleMatcher methods with in-memory merge; no game writes, no Unity GUI layout or route verification."};
File.WriteAllText(args[5],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=checks.Count,catalogAdditions=incoming.Length}));
