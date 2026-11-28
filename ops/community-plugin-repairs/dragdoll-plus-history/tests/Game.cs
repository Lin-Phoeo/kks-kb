using UnityEngine;
namespace BepInEx.Logging{public class ManualLogSource{public readonly List<string> Lines=new();public void LogInfo(object v)=>Lines.Add("INFO "+v);public void LogWarning(object v)=>Lines.Add("WARN "+v);public void LogError(object v)=>Lines.Add("ERROR "+v);}public static class Logger{public static ManualLogSource CreateLogSource(string name)=>new();}}
public class ChaFileCoordinate{}
public class ChaInfo:Behaviour{public GameObject objBodyBone{get;set;}=new();}
public class ChaControl:ChaInfo{public ChaFileCoordinate nowCoordinate{get;set;}=new();public int[] Hands=new int[2];}
namespace RootMotion.FinalIK{public class FullBodyBipedIK:Behaviour{public Studio.Solver solver=new();}}
namespace Studio{
 public interface ICommand{void Do();void Undo();void Redo();}
 public class ChangeAmount{Vector3 p,r;public Vector3 scale{get;set;}=Vector3.one;public Action<Vector3> SetPosition,SetRotation;public bool FailRotationOnce;public Vector3 pos{get=>p;set{p=value;SetPosition?.Invoke(value);}}public Vector3 rot{get=>r;set{if(FailRotationOnce){FailRotationOnce=false;throw new InvalidOperationException("injected callback failure");}r=value;SetRotation?.Invoke(value);}}}
 public class GuideCommand{public struct EqualsInfo{}}
 public class GuideObject:Component{public int dicKey{get;set;}public Transform transformTarget=new();public ChangeAmount changeAmount{get;set;}=new();public bool visible{get;set;}=true;public GuideCommand.EqualsInfo SetWorldPos(Vector3 value){transformTarget.position=value;changeAmount.pos=transformTarget.localPosition;return default;}}
 public class FKCtrl:Behaviour{public OCIChar Owner;}
 public class Solver{public Transform rootNode;}
 public class OICharInfo{public bool enableFK=true,enableIK=true;public bool[] activeFK=new bool[7],activeIK=new bool[5];}
 public class ObjectCtrlInfo{public GuideObject guideObject;}
 public class OCIChar:ObjectCtrlInfo{
  public class BoneInfo{public GuideObject guideObject{get;set;}public bool active{get;set;}=true;}
  public class IKInfo{public GuideObject guideObject{get;set;}public Transform targetObject=>guideObject.transformTarget;public bool active{get;set;}=true;}
  public bool HeightEffect;
  public ChaControl charInfo=new();public FKCtrl fkCtrl=new();public RootMotion.FinalIK.FullBodyBipedIK finalIK=new();public OICharInfo oiCharInfo{get;set;}=new();public List<BoneInfo> listBones=new();public List<IKInfo> listIKTarget=new();
 }
 public static class Studio{public static readonly Dictionary<int,ChangeAmount> Amounts=new();public static ChangeAmount GetChangeAmount(int key)=>Amounts.GetValueOrDefault(key);}
}
