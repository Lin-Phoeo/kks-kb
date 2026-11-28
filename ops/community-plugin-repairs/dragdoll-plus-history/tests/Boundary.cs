using Studio;using UnityEngine;using Codex.DragDollPlusHistory;
namespace Freelancer604.DragDoll{
public class EditAuthority{public bool IsExporting{get;set;}public bool HasPendingFk{get;set;}public IDisposable InternalWrite()=>new Scope();private class Scope:IDisposable{public void Dispose(){}}}
public class PairHandles{public static void EndFor(DragDollSession s){}}
public class NativeBreastRefreshHandoff{public static bool IsTimelineCommit(OCIChar c)=>false;public static void QueueTimelineCommit(DragDollSession s,Vector3 shift){}}
public class SessionCommitLifecycle{public static void WithRollback(Action commit,Action restore){try{commit();}catch{restore();throw;}}}
public static class DragDollPlugin{public static readonly Dictionary<OCIChar,DragDollSession> Sessions=new();public static DragDollSession Session(OCIChar c)=>Sessions.GetValueOrDefault(c);}
public static class NativeFk{public static bool FailOnce;public static void Evaluate(FKCtrl fk){if(FailOnce){FailOnce=false;throw new InvalidOperationException("injected native FK failure");}foreach(var bone in fk.Owner.listBones)bone.guideObject.transformTarget.localEulerAngles=bone.guideObject.changeAmount.rot;}}
public struct NativePeHandShape{int value;public NativePeHandShape(OCIChar c,int side){value=c.charInfo.Hands[side];}public bool Matches(OCIChar c,int side)=>c.charInfo.Hands[side]==value;public void Restore(OCIChar c,int side){c.charInfo.Hands[side]=value;}}
public class PoseCache{public Dictionary<int,Vector3> Input=new(),Solved=new(),Pending=new();public PoseCache Clone()=>new(){Input=new(Input),Solved=new(Solved),Pending=new(Pending)};public void RebaseSolved(){Input=new(Solved);foreach(var row in Pending)Input[row.Key]=row.Value;}public Dictionary<int,Vector3> InputSnapshot()=>new(Input);}
public class DragDollSession{
 public struct TimelineGuideValue{}
 public OCIChar Character;public bool disposed;public EditAuthority Authority=new();public object externalPoseBatch;public bool targetDirty;public int fkHistoryGeneration,timelineHistoryEpoch,timelineIkHistoryEpoch;public Dictionary<int,Func<bool>> timelineTracks=new();public Dictionary<OCIChar.IKInfo,TimelineGuideValue> timelineIkPositions=new();public Dictionary<int,OCIChar.BoneInfo> bones=new();public PoseCache pose=new();public bool capturedSolve=true,HeightReady;public Dictionary<Transform,Vector3> inputPositions=new();public GuideObject Height;
 public Solver Solver=>Character.finalIK.solver;
 public bool[] originalIkGroups=new bool[5];public List<GuideState> guideStates=new();public bool FailBake;public int BakeCalls;
 public class GuideState{public GuideObject Guide;public void RestoreVisibility(){}}
 public bool ContainsIk(GuideObject guide)=>Character.listIKTarget.Any(t=>t.guideObject==guide);
 public bool IsValid=>!disposed;public void ResolvePendingEdits(){targetDirty=false;}public void CancelExternalPoseBatch(){externalPoseBatch=null;}public void Commit()=>throw new Exception("actual Commit required");public void RestoreWeights(){}
 public Vector3 BakeVisible(){BakeCalls++;if(FailBake)throw new InvalidOperationException("injected bake failure");var wanted=Solver.rootNode.position;var clone=pose.Clone();if(capturedSolve)clone.RebaseSolved();foreach(var r in clone.InputSnapshot())bones[r.Key].guideObject.changeAmount.rot=r.Value;foreach(var p in inputPositions)p.Key.localPosition=p.Value;NativeFk.Evaluate(Character.fkCtrl);if(HeightReady){var axis=Height.transformTarget.parent.TransformVector(Vector3.up);var delta=wanted-Solver.rootNode.position;var d=delta.y/axis.y;var prior=Height.transformTarget.localPosition;Height.changeAmount.pos+=Vector3.up*d;Height.transformTarget.localPosition=prior+Vector3.up*d;inputPositions[Height.transformTarget]=Height.transformTarget.localPosition;}Character.guideObject.SetWorldPos(Character.guideObject.transformTarget.position+wanted-Solver.rootNode.position);return Vector3.zero;}
 public class EditorSnapshot{readonly bool fk,ik;public EditorSnapshot(DragDollSession s){fk=s.Character.oiCharInfo.enableFK;ik=s.Character.oiCharInfo.enableIK;}public void Restore(DragDollSession s){s.Character.oiCharInfo.enableFK=fk;s.Character.oiCharInfo.enableIK=ik;}}
 public static bool NativeHeightEffectReady(OCIChar c)=>c.HeightEffect;
 public class HeightChannelSnapshot{public GuideObject guide;public ChangeAmount amount;public Vector3 value;public HeightChannelSnapshot(DragDollSession s){guide=s.Height;amount=guide?.changeAmount;value=amount?.pos??Vector3.zero;}}
 internal class FkHistoryState{
  public bool HasHands=>hands!=null;public bool RestoreCanonicalFk;public PoseCache cache;public bool solved;public Dictionary<Transform,Vector3> positions;public Vector3 root,pelvis;public Quaternion rootRotation;public HeightChannelSnapshot height;public NativePeHandShape[] hands;public float Value;
  public FkHistoryState(DragDollSession s,bool captureHands=false,bool canonical=false){RestoreCanonicalFk=canonical;Value=s.Character.listIKTarget[0].guideObject.changeAmount.pos.x;cache=s.pose.Clone();solved=s.capturedSolve;positions=new(s.inputPositions);root=s.Character.guideObject.changeAmount.pos;rootRotation=s.Character.guideObject.transformTarget.rotation;pelvis=s.Character.guideObject.transformTarget.InverseTransformPoint(s.Solver.rootNode.position);height=new(s);if(captureHands)hands=new[]{new NativePeHandShape(s.Character,0),new NativePeHandShape(s.Character,1)};InactiveHistory.Capture(this,s);}
  public bool Matches(DragDollSession s)=>Math.Abs(s.Character.listIKTarget[0].guideObject.changeAmount.pos.x-Value)<.0001f;
  public void ReplayFkDelta(DragDollSession s,FkHistoryState other,IEnumerable<int> keys){Restore(s);}
  public void Restore(DragDollSession s){s.Character.listIKTarget[0].guideObject.changeAmount.pos=new(Value,0,0);foreach(var row in cache.Solved)s.bones[row.Key].guideObject.changeAmount.rot=row.Value;NativeFk.Evaluate(s.Character.fkCtrl);}
 }
 internal class FkHistoryCommand:ICommand{
  public DragDollSession session;public ICommand native;public Dictionary<int,ChangeAmount> amounts;public FkHistoryState before,after;public Action completeNative;public int generation,timelineEpoch,timelineIkEpoch;public bool nativeFkOnly;
  public FkHistoryCommand(DragDollSession s,ICommand n,FkHistoryState b,FkHistoryState a){session=s;native=n;amounts=new();before=b;after=a;generation=s.fkHistoryGeneration;timelineEpoch=s.timelineHistoryEpoch;timelineIkEpoch=s.timelineIkHistoryEpoch;nativeFkOnly=false;completeNative=null;}
  public void Do()=>throw new Exception("actual required");public void Undo()=>throw new Exception("actual required");public void Redo()=>throw new Exception("actual required");public void Apply(bool redo)=>throw new Exception("actual required");
 }
}
}
