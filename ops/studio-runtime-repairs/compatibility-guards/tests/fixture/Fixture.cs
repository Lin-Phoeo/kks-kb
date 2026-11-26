namespace ExtensibleSaveFormat { public class PluginData { public Dictionary<string,object> data = new(); } }
namespace ShalltyUtils {
 public class KeyframesGroups {
  public static int Clears, Loaded; public static string Last;
  public static void ClearSceneData() { Clears++; Loaded=0; }
  public static void LoadSceneData(ExtensibleSaveFormat.PluginData pluginData) {
   if(pluginData == null) return; ClearSceneData();
   string data=(string)pluginData.data["keyframesGroupsData"];
   if(string.IsNullOrEmpty(data)) return; Last=data; Loaded++;
  }
 }
}
namespace UnityEngine {
 public static class GUI { public static bool OnGUI; public static int Reads; public static object Skin { get { Reads++; if(!OnGUI) throw new ArgumentException("You can only call GUI functions from inside OnGUI."); return new(); } } }
 public struct RangeSlider {
  private static readonly object sliderStyle = GUI.Skin;
  private static readonly object thumbStyle = GUI.Skin;
  private static readonly object bandStyle = new();
  public static bool Create() => sliderStyle != null && thumbStyle != null && bandStyle != null;
 }
}
namespace Studio {
 public class SunLightInfo { public string Target="sun"; }
 public class Map { public SunLightInfo sunLightInfo {get;set;} public string mapEffectInfo => sunLightInfo.Target; }
}
namespace Buggsy.Quill.KoikatsuSunshineLightingSuite {
 public class LightingRegistryProbe {
  public static Studio.Map Map; public static int Success;
  private static Studio.Map FindMap()=>Map;
  private static string GetMapSunLight(Studio.Map map) { if(map==null || map.sunLightInfo==null)return null; return map.sunLightInfo.Target; }
  public void DumpEnvironmentRecordCore() { var map=FindMap(); GetMapSunLight(map); _=map?.mapEffectInfo; Success++; }
 }
}
