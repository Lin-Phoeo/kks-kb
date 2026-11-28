namespace UnityEngine {public class Component {public Transform transform {get;set;}=new();}public class Transform {public Transform Result;public string LastPath;public Transform Find(string path){LastPath=path;return Result;}}}
public class ChaControl:UnityEngine.Component{}
namespace Studio {public class ObjectCtrlInfo{}public class OCIChar:ObjectCtrlInfo{public ChaControl Control=new();}}
namespace KKAPI {public enum GameMode {Unknown,MainGame,Studio}public static class KoikatuAPI {public static GameMode Mode=GameMode.Studio;public static GameMode GetCurrentGameMode()=>Mode;}}
namespace KKAPI.Studio {public static class StudioObjectExtensions {public static ChaControl GetChaControl(global::Studio.OCIChar value)=>value.Control;}}
namespace KKAPI.Studio.SaveLoad {public sealed class ObjectsSelectedEventArgs:EventArgs {public List<global::Studio.ObjectCtrlInfo> SelectedObjects {get;set;}=new();}}
namespace PostProcessingEffectsV3 {public sealed class PostProcessingEffectsV3 {public UnityEngine.Transform charapos;public void ObjectsSelected(object sender,KKAPI.Studio.SaveLoad.ObjectsSelectedEventArgs e)=>throw new NotImplementedException("Actual exact-input CIL required");}}
