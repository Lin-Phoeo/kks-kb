using System;
using System.Collections.Generic;

namespace ExtensibleSaveFormat {public class PluginData {public int version;public Dictionary<string,object> data=new();}}
namespace Studio {public class ObjectCtrlInfo {}}
namespace KKAPI.Utilities {public class ReadOnlyDictionary<TKey,TValue>:Dictionary<TKey,TValue>{}}
namespace KKAPI.Studio.SaveLoad
{
    public enum SceneOperationKind {Load,Import,Clear}
    public class SceneCustomFunctionController
    {
        public ExtensibleSaveFormat.PluginData Incoming,Saved;
        public ExtensibleSaveFormat.PluginData GetExtendedData()=>Incoming;
        public void SetExtendedData(ExtensibleSaveFormat.PluginData value)=>Saved=value;
        protected virtual void OnSceneLoad(SceneOperationKind op,KKAPI.Utilities.ReadOnlyDictionary<int,global::Studio.ObjectCtrlInfo> loaded){}
        protected virtual void OnSceneSave(){}
    }
}
namespace SceneNotes
{
    public static class SceneNotesPlugin {public static string Notes="";public static int SetCalls;public static void SetNotes(string notes){Notes=notes??string.Empty;SetCalls++;}}
    public class SceneNotesSaveController:KKAPI.Studio.SaveLoad.SceneCustomFunctionController
    {
        protected override void OnSceneLoad(KKAPI.Studio.SaveLoad.SceneOperationKind op,KKAPI.Utilities.ReadOnlyDictionary<int,Studio.ObjectCtrlInfo> loaded)=>throw new Exception("Unextracted load CIL");
        protected override void OnSceneSave()=>throw new Exception("Unextracted save CIL");
        public void Load(KKAPI.Studio.SaveLoad.SceneOperationKind op)=>OnSceneLoad(op,new());
        public void Save()=>OnSceneSave();
    }
}
static class Tests
{
    static int count,failures;
    static void Check(bool value,string name){count++;if(!value){failures++;Console.WriteLine("FAIL "+name);}}
    static ExtensibleSaveFormat.PluginData Data(object value)=>new(){version=1,data=new(){{"notes",value}}};
    static void Case(string name,KKAPI.Studio.SaveLoad.SceneOperationKind operation,string before,ExtensibleSaveFormat.PluginData incoming,string expected,int writes)
    {
        SceneNotes.SceneNotesPlugin.Notes=before;SceneNotes.SceneNotesPlugin.SetCalls=0;
        var c=new SceneNotes.SceneNotesSaveController{Incoming=incoming};c.Load(operation);
        Check(SceneNotes.SceneNotesPlugin.Notes==expected,name+" text");Check(SceneNotes.SceneNotesPlugin.SetCalls==writes,name+" write count");
        c.Save();
        if(string.IsNullOrEmpty(expected))Check(c.Saved==null,name+" empty data stays null");
        else Check(c.Saved!=null&&c.Saved.version==1&&c.Saved.data.Count==1&&c.Saved.data.TryGetValue("notes",out var value)&&(string)value==expected,name+" version1 single notes field");
    }
    static int Main()
    {
        try
        {
            var import=KKAPI.Studio.SaveLoad.SceneOperationKind.Import;var load=KKAPI.Studio.SaveLoad.SceneOperationKind.Load;var clear=KKAPI.Studio.SaveLoad.SceneOperationKind.Clear;
            Case("import existing",import,"Current note",Data("Incoming note"),"Current note\n\nIncoming note",1);
            Case("import absent data",import,"Keep me",null,"Keep me",0);
            Case("import missing key",import,"Keep me",new(){data=new(){{"other","ignored"}}},"Keep me",0);
            Case("import null dictionary",import,"Keep me",new(){data=null},"Keep me",0);
            Case("import empty note",import,"Keep me",Data(""),"Keep me",0);
            Case("import null note",import,"Keep me",Data(null),"Keep me",0);
            Case("import wrong type",import,"Keep me",Data(12),"Keep me",0);
            Case("import into empty",import,"",Data("New"),"New",1);
            Case("import into null",import,null,Data("New"),"New",1);
            Case("identical note",import,"Same",Data("Same"),"Same",0);
            Case("Unicode CRLF retained",import,"中文\r\nα",Data("日本語\nΩ"),"中文\r\nα\n\n日本語\nΩ",1);
            Case("whitespace preserved",import,"A",Data("  \t"),"A\n\n  \t",1);
            Case("load replaces",load,"Old",Data("Loaded"),"Loaded",1);
            Case("load no data clears",load,"Old",null,"",1);
            Case("load wrong type clears",load,"Old",Data(1),"",1);
            Case("clear ignores incoming",clear,"Old",Data("Ignored"),"",1);
            SceneNotes.SceneNotesPlugin.Notes="Root";var c=new SceneNotes.SceneNotesSaveController{Incoming=Data("First")};c.Load(import);c.Incoming=Data("Second");c.Load(import);
            Check(SceneNotes.SceneNotesPlugin.Notes=="Root\n\nFirst\n\nSecond","sequential imports preserve arrival order");c.Save();var saved=c.Saved;SceneNotes.SceneNotesPlugin.Notes="Different";var d=new SceneNotes.SceneNotesSaveController{Incoming=saved};d.Load(load);Check(SceneNotes.SceneNotesPlugin.Notes=="Root\n\nFirst\n\nSecond","actual save/load roundtrip preserves merged plain text");
            Console.WriteLine($"checks={count} failures={failures}");return failures==0?0:1;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 2;}
    }
}
