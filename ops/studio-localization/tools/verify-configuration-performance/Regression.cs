using System.Reflection;
using System.Text.Json;
using ConfigurationManager;
using BepInEx;
using HarmonyLib;
using Mono.Cecil;

internal static class Regression
{
    private const BindingFlags All = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    internal static object Run(string game,Assembly adapter,string catalogPath)
    {
        Type catalogType=adapter.GetType("Codex.StudioConfigLocalization.TranslationCatalog",true);
        Type rowType=adapter.GetType("Codex.StudioConfigLocalization.TranslationEntry",true);
        Type plugin=adapter.GetType("Codex.StudioConfigLocalization.Plugin",true);
        var checks=new List<object>();
        void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)throw new Exception(name);}
        object Row(string guid,string kind,string section,string key,string source,string value)
        {
            object row=Activator.CreateInstance(rowType);
            string[] fields={"pluginGuid","kind","section","settingKey","source","value"};
            string[] values={guid,kind,section,key,source,value};
            for(int i=0;i<fields.Length;i++)rowType.GetField(fields[i]).SetValue(row,values[i]);
            return row;
        }
        object[] entries={
            Row("plugin.a","setting","General","Shared","Shared","甲标签"),
            Row("plugin.b","setting","General","Shared","Shared","乙标签"),
            Row("plugin.a","setting","Other","Shared","Shared","其他分组"),
            Row("plugin.a","description","General","Shared","Original tooltip","完整中文说明"),
            Row("plugin.a","category",null,null,"General","常规"),
            Row("plugin.a","enum","General","Shared","Enabled","启用旗标")
        };
        Array rows=Array.CreateInstance(rowType,entries.Length);
        for(int i=0;i<entries.Length;i++)rows.SetValue(entries[i],i);
        object catalog=Activator.CreateInstance(catalogType,new object[]{rows});
        string Lookup(string guid,string section,string key,string source)
        {
            object[] parameters={guid,"setting",section,key,source,null};
            bool result=(bool)catalogType.GetMethod("TryGet").Invoke(catalog,parameters);
            return result?(string)parameters[5]:null;
        }
        Check("owner A exact",Lookup("plugin.a","General","Shared","Shared")=="甲标签");
        Check("owner B isolated",Lookup("plugin.b","General","Shared","Shared")=="乙标签");
        Check("section isolated",Lookup("plugin.a","Other","Shared","Shared")=="其他分组");
        Check("unknown source unchanged",Lookup("plugin.a","General","Shared","Custom display name")==null);
        Check("unknown owner unchanged",Lookup("plugin.c","General","Shared","Shared")==null);
        var setting=new FakeSetting("Shared","Original tooltip");
        plugin.GetField("_catalog",All).SetValue(null,catalog);
        Type identityType=plugin.GetNestedType("SettingIdentity",BindingFlags.NonPublic);
        object identity=Activator.CreateInstance(identityType,true);
        identityType.GetField("Guid",All).SetValue(identity,"plugin.a");
        identityType.GetField("Section",All).SetValue(identity,"General");
        identityType.GetField("Key",All).SetValue(identity,"Shared");
        plugin.GetField("_currentSetting",All).SetValue(null,identity);
        object[] state={setting,null};
        plugin.GetMethod("NamePrefix",All).Invoke(null,state);
        Check("actual prefix changes display metadata",setting.DispName=="甲标签"&&setting.Description=="完整中文说明");
        string copiedTooltip=setting.Description;
        plugin.GetMethod("NameFinalizer",All).Invoke(null,new[]{state[1]});
        Check("actual finalizer restores metadata",setting.DispName=="Shared"&&setting.Description=="Original tooltip");
        Check("tooltip copy survives restoration",copiedTooltip=="完整中文说明");
        Check("value access untouched",setting.Reads==0&&setting.Writes==0);
        Check("flags exact-context text", (string)plugin.GetMethod("FlagsText",All).Invoke(null,new object[]{"Enabled"})=="启用旗标");
        Check("flags unknown text unchanged", (string)plugin.GetMethod("FlagsText",All).Invoke(null,new object[]{"UnlistedFlag"})=="UnlistedFlag");
        MethodInfo flagsMethod=typeof(SettingEntryBase).Assembly.GetType("ConfigurationManager.SettingFieldDrawer",true).GetMethod("DrawFlagsField",All);
        using var managerMetadata=AssemblyDefinition.ReadAssembly(typeof(SettingEntryBase).Assembly.Location);
        var flagsBody=managerMetadata.MainModule.GetType("ConfigurationManager.SettingFieldDrawer").Methods.Single(method=>method.Name=="DrawFlagsField").Body;
        var instructionOpcodes=typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)
            .Select(field=>(System.Reflection.Emit.OpCode)field.GetValue(null)).ToDictionary(op=>op.Value);
        List<CodeInstruction> originalInstructions=flagsBody.Instructions.Select(instruction=>
        {
            object operand=instruction.Operand;
            if(operand is MethodReference methodReference)
                operand=flagsMethod.Module.ResolveMethod(methodReference.MetadataToken.ToInt32());
            return new CodeInstruction(instructionOpcodes[instruction.OpCode.Value],operand);
        }).ToList();
        var transformed=((IEnumerable<CodeInstruction>)plugin.GetMethod("FlagsTranspiler",All).Invoke(null,new object[]{originalInstructions})).ToList();
        MethodInfo translateFlags=plugin.GetMethod("FlagsText",All);
        Check("actual flags IL adds two display translations",transformed.Count==originalInstructions.Count+2 &&
            transformed.Count(instruction=>Equals(instruction.operand,translateFlags))==2);
        Check("actual flags original IL preserved",transformed.Where(instruction=>!Equals(instruction.operand,translateFlags)).SequenceEqual(originalInstructions));
        Type displayState=adapter.GetType("Codex.StudioConfigLocalization.DisplayState",true);
        object outer=Activator.CreateInstance(displayState,All,null,new object[]{setting,"外层","外层说明"},null);
        object inner=Activator.CreateInstance(displayState,All,null,new object[]{setting,"内层","内层说明"},null);
        displayState.GetMethod("Restore",All).Invoke(inner,null);
        Check("nested restoration",setting.DispName=="外层"&&setting.Description=="外层说明");
        displayState.GetMethod("Restore",All).Invoke(outer,null);
        Check("outer restoration",setting.DispName=="Shared"&&setting.Description=="Original tooltip");
        bool threw=false;
        Array duplicate=Array.CreateInstance(rowType,2);
        duplicate.SetValue(entries[0],0);duplicate.SetValue(Row("plugin.a","setting","General","Shared","Shared","冲突"),1);
        try{Activator.CreateInstance(catalogType,new object[]{duplicate});}catch(TargetInvocationException e){threw=e.InnerException is ArgumentException;}
        Check("conflicting identity rejected",threw);
        Array multilineRows=Array.CreateInstance(rowType,1);
        multilineRows.SetValue(Row("plugin.a","description","General","Shared","Line 1\nLine 2","两行说明"),0);
        object multiline=Activator.CreateInstance(catalogType,new object[]{multilineRows});
        foreach(string source in new[]{"Line 1\r\nLine 2","Line 1\rLine 2"})
        {
            object[] lookup={"plugin.a","description","General","Shared",source,null};
            Check("newline source normalization "+(source.Contains("\r\n")?"CRLF":"CR"),
                (bool)catalogType.GetMethod("TryGet").Invoke(multiline,lookup)&&(string)lookup[5]=="两行说明");
        }
        Array normalizedConflict=Array.CreateInstance(rowType,2);
        normalizedConflict.SetValue(Row("plugin.a","description","General","Shared","Line 1\nLine 2","甲"),0);
        normalizedConflict.SetValue(Row("plugin.a","description","General","Shared","Line 1\r\nLine 2","乙"),1);
        bool normalizedConflictRejected=false;
        try{Activator.CreateInstance(catalogType,new object[]{normalizedConflict});}
        catch(TargetInvocationException error){normalizedConflictRejected=error.InnerException is ArgumentException;}
        Check("normalized conflicting identity rejected",normalizedConflictRejected);
        object MakeCatalog(params object[] items)
        {
            Array typed=Array.CreateInstance(rowType,items.Length);
            for(int i=0;i<items.Length;i++)typed.SetValue(items[i],i);
            return Activator.CreateInstance(catalogType,new object[]{typed});
        }
        string Read(object target,string guid,string kind,string section,string key,string source)
        {
            object[] parameters={guid,kind,section,key,source,"sentinel"};
            bool found=(bool)catalogType.GetMethod("TryGet").Invoke(target,parameters);
            if(!found&&parameters[5]!=null)throw new Exception("Miss must clear the output");
            return found?(string)parameters[5]:null;
        }
        var boundaries=MakeCatalog(
            Row("plugin.a","setting","ab","c","d","first"),
            Row("plugin.a","setting","a","bc","d","second"),
            Row("plugin.a","setting","a:b\t","c\0","d:e\t\0","delimiters"),
            Row("plugin.a","category",null,null,"General","category"),
            Row("plugin.a","setting","","","","empty"),
            Row("plugin.a","setting","组","键","𠮷中文\ud800","unicode"));
        Check("composite field boundaries isolated",Read(boundaries,"plugin.a","setting","ab","c","d")=="first"&&Read(boundaries,"plugin.a","setting","a","bc","d")=="second");
        Check("delimiter and null characters retained",Read(boundaries,"plugin.a","setting","a:b\t","c\0","d:e\t\0")=="delimiters");
        Check("null and empty optional fields equivalent",Read(boundaries,"plugin.a","category","","","General")=="category");
        Check("empty strings accepted",Read(boundaries,"plugin.a","setting",null,null,"")=="empty");
        Check("ordinal case sensitivity",Read(boundaries,"Plugin.a","setting","ab","c","d")==null&&Read(boundaries,"plugin.a","setting","ab","c","D")==null);
        Check("unicode code units retained",Read(boundaries,"plugin.a","setting","组","键","𠮷中文\ud800")=="unicode");
        Check("null source and owner rejected",Read(boundaries,null,"setting","ab","c","d")==null&&Read(boundaries,"plugin.a","setting","ab","c",null)==null);
        object duplicateIdentical=MakeCatalog(Row("plugin.a","category",null,null,"A\r\nB","same"),Row("plugin.a","category","","","A\nB","same"));
        Check("identical normalized duplicate accepted",(int)catalogType.GetProperty("Count").GetValue(duplicateIdentical)==1);
        string[] newlineVariants={"\r", "\r\n", "\n", "A\r\r\nB\n", "\r\n\rX\r"};
        foreach(string source in newlineVariants)
        {
            string normalized=source.Replace("\r\n","\n").Replace("\r","\n");
            object newlineCatalog=MakeCatalog(Row("plugin.a","description","General","Shared",source,"normalized"));
            Check("mixed newline equivalence "+Array.IndexOf(newlineVariants,source),Read(newlineCatalog,"plugin.a","description","General","Shared",normalized)=="normalized");
        }
        int catalogEntries=0;
        if(catalogPath!=null)
        {
            using JsonDocument data=JsonDocument.Parse(File.ReadAllText(catalogPath));
            Array loaded=Array.CreateInstance(rowType,data.RootElement.GetArrayLength());
            int index=0;
            foreach(JsonElement item in data.RootElement.EnumerateArray())
            {
                string ReadProperty(string name)=>item.GetProperty(name).ValueKind==JsonValueKind.Null?null:item.GetProperty(name).GetString();
                loaded.SetValue(Row(ReadProperty("pluginGuid"),ReadProperty("kind"),ReadProperty("section"),ReadProperty("settingKey"),ReadProperty("source"),ReadProperty("value")),index++);
            }
            object whole=Activator.CreateInstance(catalogType,new object[]{loaded});
            Check("full shipped catalog accepted",(int)catalogType.GetProperty("Count").GetValue(whole)==loaded.Length);
            foreach(object item in loaded)
            {
                string Field(string name)=>(string)rowType.GetField(name).GetValue(item);
                object[] lookup={Field("pluginGuid"),Field("kind"),Field("section"),Field("settingKey"),Field("source"),null};
                if(!(bool)catalogType.GetMethod("TryGet").Invoke(whole,lookup)||(string)lookup[5]!=Field("value"))
                    throw new Exception("Full catalog exact readback");
            }
            catalogEntries=loaded.Length;
        }
        plugin.GetField("_catalog",All).SetValue(null,null);
        plugin.GetField("_currentSetting",All).SetValue(null,null);
        var output=new{passed=true,checks=checks.Count,catalogEntries,results=checks,settingReads=setting.Reads,settingWrites=setting.Writes,
            realConfigurationManagerBase=true,realAdapterPrefixFinalizer=true,unityNativeCalls=false,liveFilesWritten=false,
            limitation="Actual display transaction and identity lookup tested on a synthetic SettingEntryBase. Harmony draw path, dropdown clicks and pixels require Studio."};
        return output;
    }
    private sealed class FakeSetting:SettingEntryBase
    {
        public int Reads,Writes;
        public FakeSetting(string name,string description){DispName=name;Description=description;PluginInfo=new BepInPlugin("plugin.a","Synthetic","1.0.0");}
        public override Type SettingType=>typeof(bool);
        public override object Get(){Reads++;throw new InvalidOperationException("Value read");}
        protected override void SetValue(object value){Writes++;throw new InvalidOperationException("Value write");}
    }
}
