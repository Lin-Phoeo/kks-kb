using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using Studio;
using UnityEngine;

namespace Codex.DragDollPlusHistory
{
    public static class InactiveHistory
    {
        private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static readonly ConditionalWeakTable<object,Pose> Poses=new ConditionalWeakTable<object,Pose>();
        private static readonly ConditionalWeakTable<object,Lease> Leases=new ConditionalWeakTable<object,Lease>();
        private static readonly ConditionalWeakTable<object,Pose> Preparing=new ConditionalWeakTable<object,Pose>();
        private static readonly ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("DragDoll PLUS History Compatibility");
        private static object Get(object o,string name)
        {
            var t=o.GetType();var f=t.GetField(name,All);if(f!=null)return f.GetValue(o);
            var p=t.GetProperty(name,All);if(p!=null)return p.GetValue(o,null);
            throw new MissingMemberException(t.FullName,name);
        }
        private static object Call(object o,string name,params object[] args)
        {
            try{return o.GetType().GetMethod(name,All).Invoke(o,args);}
            catch(TargetInvocationException e){throw e.InnerException??e;}
        }
        private static object Static(Type t,string name,params object[] args)
        {
            try{return t.GetMethod(name,All).Invoke(null,args);}
            catch(TargetInvocationException e){throw e.InnerException??e;}
        }
        private static bool Rotation(Vector3 a,Vector3 b){return Quaternion.Angle(Quaternion.Euler(a),Quaternion.Euler(b))<=.01f;}
        private static bool Position(Vector3 a,Vector3 b){return (a-b).sqrMagnitude<=1e-8f;}
        private static bool Finite(Vector3 value){return !(float.IsNaN(value.x)||float.IsNaN(value.y)||float.IsNaN(value.z)||float.IsInfinity(value.x)||float.IsInfinity(value.y)||float.IsInfinity(value.z));}
        private static bool ParentMatches(Transform target,Transform parent){return ReferenceEquals(target.parent,parent)&&(ReferenceEquals(parent,null)||parent);}
        private static bool TimelinePlaying(){var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Timeline.Timeline",false)).FirstOrDefault(t=>t!=null);return type!=null&&(bool)type.GetProperty("isPlaying",All).GetValue(null,null);}
        private sealed class Bone
        {
            internal OCIChar.BoneInfo Info;internal GuideObject Guide;internal ChangeAmount Amount;internal Transform Target,Parent;internal int Key;
            internal bool Valid(OCIChar c){return c.listBones.Contains(Info)&&Guide&&Target&&Info.guideObject==Guide&&Guide.transformTarget==Target&&ParentMatches(Target,Parent)&&Guide.changeAmount==Amount&&Studio.Studio.GetChangeAmount(Key)==Amount;}
        }
        private sealed class IkTarget
        {
            internal OCIChar.IKInfo Info;internal GuideObject Guide;internal ChangeAmount Amount;internal Transform Target,Parent;internal int Key;internal Vector3 Position,Rotation;
            internal bool Valid(OCIChar c){return c.listIKTarget.Contains(Info)&&Guide&&Target&&Info.guideObject==Guide&&Guide.transformTarget==Target&&ParentMatches(Target,Parent)&&Guide.changeAmount==Amount&&Studio.Studio.GetChangeAmount(Key)==Amount;}
        }
        private sealed class Pose
        {
            internal OCIChar Character;internal ChaControl Control;internal GameObject Body;internal object Coordinate;internal FKCtrl Fk;internal Component Ik;
            internal GuideObject Root,Height;internal ChangeAmount RootAmount,HeightAmount;internal Transform RootTarget,RootParent,Pelvis,HeightParent;
            internal Vector3 RootPosition,RootRotation,PelvisWorld,HeightValue;internal bool Solved,HeightReady;internal Transform[] Topology;
            internal readonly Dictionary<int,Bone> Bones=new Dictionary<int,Bone>();internal Dictionary<int,Vector3> Rotations;internal Dictionary<Transform,Vector3> Positions;internal Dictionary<Transform,Transform> PositionParents;
            internal IkTarget[] Targets;internal object[] Hands,ObservedHands;internal Type HandType;
            internal int TimelineEpoch,TimelineIkEpoch;internal bool TimelineOwned;internal Type SessionType;
            internal static Pose Capture(object state,object session)
            {
                var p=new Pose();p.SessionType=session.GetType();p.Character=(OCIChar)Get(session,"Character");var c=p.Character;p.Control=c.charInfo;p.Body=c.charInfo.objBodyBone;p.Coordinate=c.charInfo.nowCoordinate;p.Fk=c.fkCtrl;p.Ik=c.finalIK;
                p.Root=c.guideObject;p.RootAmount=p.Root.changeAmount;p.RootTarget=p.Root.transformTarget;p.RootParent=p.RootTarget.parent;
                p.RootPosition=(Vector3)Get(state,"root");var world=(Quaternion)Get(state,"rootRotation");p.RootRotation=(p.RootParent?Quaternion.Inverse(p.RootParent.rotation)*world:world).eulerAngles;
                p.Solved=(bool)Get(state,"solved");var cache=Call(Get(state,"cache"),"Clone");if(p.Solved)Call(cache,"RebaseSolved");p.Rotations=(Dictionary<int,Vector3>)Call(cache,"InputSnapshot");
                p.Positions=new Dictionary<Transform,Vector3>((Dictionary<Transform,Vector3>)Get(state,"positions"));p.PositionParents=p.Positions.Keys.ToDictionary(x=>x,x=>x.parent);
                var dict=(IDictionary)Get(session,"bones");foreach(DictionaryEntry row in dict){var b=(OCIChar.BoneInfo)row.Value;var guide=b.guideObject;p.Bones.Add((int)row.Key,new Bone{Info=b,Guide=guide,Amount=guide.changeAmount,Target=guide.transformTarget,Parent=guide.transformTarget.parent,Key=guide.dicKey});}
                p.Pelvis=(Transform)Get(Get(session,"Solver"),"rootNode");p.PelvisWorld=p.RootTarget.TransformPoint((Vector3)Get(state,"pelvis"));
                var h=Get(state,"height");p.Height=(GuideObject)Get(h,"guide");p.HeightAmount=Get(h,"amount") as ChangeAmount;p.HeightValue=(Vector3)Get(h,"value");p.HeightParent=p.Height&&p.Height.transformTarget?p.Height.transformTarget.parent:null;
                var heightMethod=session.GetType().GetMethod("NativeHeightEffectReady",All,null,new[]{typeof(OCIChar)},null);p.HeightReady=p.Height&&p.HeightParent&&p.Positions.ContainsKey(p.Height.transformTarget)&&(p.Pelvis==p.Height.transformTarget||p.Pelvis.IsChildOf(p.Height.transformTarget))&&(bool)heightMethod.Invoke(null,new object[]{c});
                p.Targets=c.listIKTarget.Select(t=>new IkTarget{Info=t,Guide=t.guideObject,Amount=t.guideObject.changeAmount,Target=t.guideObject.transformTarget,Parent=t.guideObject.transformTarget.parent,Key=t.guideObject.dicKey,Position=t.guideObject.changeAmount.pos,Rotation=t.guideObject.changeAmount.rot}).ToArray();p.Topology=c.listBones.Where(b=>b.guideObject&&b.guideObject.transformTarget).Select(b=>b.guideObject.transformTarget).ToArray();
                var hands=Get(state,"hands") as Array;p.Hands=hands==null?null:hands.Cast<object>().ToArray();p.HandType=session.GetType().Assembly.GetType("Freelancer604.DragDoll.NativePeHandShape",true);p.ObservedHands=new[]{Activator.CreateInstance(p.HandType,All,null,new object[]{c,0},null),Activator.CreateInstance(p.HandType,All,null,new object[]{c,1},null)};
                p.TimelineEpoch=(int)Get(session,"timelineHistoryEpoch");p.TimelineIkEpoch=(int)Get(session,"timelineIkHistoryEpoch");p.TimelineOwned=((IDictionary)Get(session,"timelineTracks")).Count!=0||((IDictionary)Get(session,"timelineIkPositions")).Count!=0;return p;
            }
            internal bool Valid()
            {
                var c=Character;if(c==null||!Control||c.charInfo!=Control||Control.objBodyBone!=Body||!ReferenceEquals(Control.nowCoordinate,Coordinate)||c.fkCtrl!=Fk||c.finalIK!=Ik||!Fk||!Root||!RootTarget||c.guideObject!=Root||Root.transformTarget!=RootTarget||!ParentMatches(RootTarget,RootParent)||Root.changeAmount!=RootAmount||Studio.Studio.GetChangeAmount(Root.dicKey)!=RootAmount||!Pelvis)return false;
                if(!Bones.Values.All(b=>b.Valid(c))||!Targets.All(t=>t.Valid(c))||!Positions.Keys.All(t=>t&&ParentMatches(t,PositionParents[t]))||!Topology.SequenceEqual(c.listBones.Where(b=>b.guideObject&&b.guideObject.transformTarget).Select(b=>b.guideObject.transformTarget)))return false;
                if(!ReferenceEquals(Height,null)&&(!Height||!Height.transformTarget||Height.changeAmount!=HeightAmount||!ParentMatches(Height.transformTarget,HeightParent)||Studio.Studio.GetChangeAmount(Height.dicKey)!=HeightAmount))return false;
                bool heightNow=Height&&HeightParent&&Positions.ContainsKey(Height.transformTarget)&&(Pelvis==Height.transformTarget||Pelvis.IsChildOf(Height.transformTarget))&&(bool)SessionType.GetMethod("NativeHeightEffectReady",All,null,new[]{typeof(OCIChar)},null).Invoke(null,new object[]{c});if(heightNow!=HeightReady)return false;
                return Rotations.Values.All(Finite)&&Positions.Values.All(Finite)&&Finite(RootPosition)&&Finite(RootRotation)&&Finite(PelvisWorld)&&Finite(HeightValue);
            }
            internal bool SameEndpoint(Pose other)
            {
                return ReferenceEquals(Character,other.Character)&&Solved==other.Solved&&HeightReady==other.HeightReady&&Rotations.Count==other.Rotations.Count&&Rotations.All(x=>other.Rotations.ContainsKey(x.Key)&&Rotation(x.Value,other.Rotations[x.Key]))&&Position(PelvisWorld,other.PelvisWorld)&&Position(RootPosition,other.RootPosition)&&Rotation(RootRotation,other.RootRotation)&&Position(HeightValue,other.HeightValue)&&Positions.Count==other.Positions.Count&&Positions.All(x=>other.Positions.ContainsKey(x.Key)&&Position(x.Value,other.Positions[x.Key]))&&ObservedHands.SequenceEqual(other.ObservedHands)&&Targets.Length==other.Targets.Length&&Targets.Select((t,i)=>t.Guide==other.Targets[i].Guide&&Position(t.Position,other.Targets[i].Position)&&Rotation(t.Rotation,other.Targets[i].Rotation)).All(x=>x)&&TimelineEpoch==other.TimelineEpoch&&TimelineIkEpoch==other.TimelineIkEpoch;
            }
        }
        private sealed class Raw
        {
            internal readonly Dictionary<ChangeAmount,Vector3> Rotations=new Dictionary<ChangeAmount,Vector3>(),Positions=new Dictionary<ChangeAmount,Vector3>();
            internal readonly Dictionary<Transform,Vector3> LocalPositions=new Dictionary<Transform,Vector3>();
            internal readonly Dictionary<Transform,Vector3> LocalScales=new Dictionary<Transform,Vector3>();internal Vector3 RootScale;
            internal Transform Parent;internal Vector3 ParentPosition,ParentScale;internal Quaternion ParentRotation;internal bool FkEnabled,IkEnabled;internal bool[] FkGroups,IkGroups;internal OCIChar Character;internal object[] Hands;
            internal static Raw Capture(Pose pose)
            {
                var r=new Raw();foreach(var b in pose.Bones.Values)r.Rotations[b.Amount]=b.Amount.rot;foreach(var t in pose.Targets){r.Rotations[t.Amount]=t.Amount.rot;r.Positions[t.Amount]=t.Amount.pos;}
                r.Rotations[pose.RootAmount]=pose.RootAmount.rot;r.Positions[pose.RootAmount]=pose.RootAmount.pos;if(pose.HeightAmount!=null)r.Positions[pose.HeightAmount]=pose.HeightAmount.pos;
                foreach(var t in pose.Positions.Keys)r.LocalPositions[t]=t.localPosition;
                foreach(var target in pose.Positions.Keys.Concat(pose.Bones.Values.Select(b=>b.Target)).Concat(new[]{pose.RootTarget,pose.Pelvis}))for(var t=target;t&&t!=pose.RootParent;t=t.parent)r.LocalScales[t]=t.localScale;r.RootScale=pose.RootAmount.scale;
                r.Parent=pose.RootParent;if(r.Parent){r.ParentPosition=r.Parent.position;r.ParentRotation=r.Parent.rotation;r.ParentScale=r.Parent.lossyScale;}
                var c=pose.Character;r.Character=c;r.Hands=new[]{Activator.CreateInstance(pose.HandType,All,null,new object[]{c,0},null),Activator.CreateInstance(pose.HandType,All,null,new object[]{c,1},null)};r.FkEnabled=c.oiCharInfo.enableFK;r.IkEnabled=c.oiCharInfo.enableIK;r.FkGroups=(bool[])c.oiCharInfo.activeFK.Clone();r.IkGroups=(bool[])c.oiCharInfo.activeIK.Clone();return r;
            }
            internal bool Matches(Pose p)
            {
                var c=p.Character;return FkEnabled==c.oiCharInfo.enableFK&&IkEnabled==c.oiCharInfo.enableIK&&FkGroups.SequenceEqual(c.oiCharInfo.activeFK)&&IkGroups.SequenceEqual(c.oiCharInfo.activeIK)&&Hands.Select((h,i)=>(bool)Call(h,"Matches",c,i)).All(x=>x)&&Rotations.All(x=>Rotation(x.Key.rot,x.Value))&&Positions.All(x=>Position(x.Key.pos,x.Value))&&LocalPositions.All(x=>x.Key&&Position(x.Key.localPosition,x.Value))&&LocalScales.All(x=>x.Key&&Position(x.Key.localScale,x.Value))&&Position(p.RootAmount.scale,RootScale)&&(!Parent||Position(Parent.position,ParentPosition)&&Quaternion.Angle(Parent.rotation,ParentRotation)<=.01f&&Position(Parent.lossyScale,ParentScale));
            }
            internal void Restore(){foreach(var row in Rotations)row.Key.rot=row.Value;foreach(var row in Positions)row.Key.pos=row.Value;foreach(var row in LocalPositions)if(row.Key)row.Key.localPosition=row.Value;for(int i=0;i<Hands.Length;i++)Call(Hands[i],"Restore",Character,i);}
        }
        private sealed class Lease{internal Pose Identity,Endpoint;internal Raw Actual;internal bool Retired;}
        public static void Capture(object state,object session)
        {
            try{var pose=Pose.Capture(state,session);if(pose.Valid()){Poses.Remove(state);Poses.Add(state,pose);}}
            catch(Exception e){Log.LogError("DD_HISTORY_CAPTURE_FAILED "+e);}
        }
        public static void Committed(object session)
        {
            try
            {
                Pose endpoint;if(!Preparing.TryGetValue(session,out endpoint))return;Preparing.Remove(session);
                if(!(bool)Get(session,"IsValid"))return;var c=(OCIChar)Get(session,"Character");if(!c.oiCharInfo.enableFK||c.oiCharInfo.enableIK||!c.fkCtrl.enabled||c.finalIK.enabled)return;
                var stateType=session.GetType().GetNestedType("FkHistoryState",BindingFlags.NonPublic);var state=Activator.CreateInstance(stateType,All,null,new object[]{session,false,false},null);Pose pose;if(!Poses.TryGetValue(state,out pose))return;
                Leases.Remove(session);Leases.Add(session,new Lease{Identity=pose,Endpoint=endpoint,Actual=Raw.Capture(pose)});
            }
            catch(Exception e){Log.LogError("DD_HISTORY_COMMIT_CAPTURE_FAILED "+e);}
        }
        public static void PreparingCommit(object session)
        {
            Preparing.Remove(session);Leases.Remove(session);
            try{var type=session.GetType().GetNestedType("FkHistoryState",BindingFlags.NonPublic);var state=Activator.CreateInstance(type,All,null,new object[]{session,false,false},null);Pose pose;if(Poses.TryGetValue(state,out pose))Preparing.Add(session,pose);}
            catch(Exception e){Log.LogError("DD_HISTORY_PRECOMMIT_CAPTURE_FAILED "+e);}
        }
        public static bool TryApply(object command,bool redo)
        {
            var session=Get(command,"session");if(!(bool)Get(session,"disposed"))return false;
            Lease lease;if(!Leases.TryGetValue(session,out lease))return false;
            var state=Get(command,redo?"after":"before");var opposite=Get(command,redo?"before":"after");Pose desired,expected;
            if(lease.Retired||state==null||opposite==null||!Poses.TryGetValue(state,out desired)||!Poses.TryGetValue(opposite,out expected)){Log.LogWarning("DD_HISTORY_INACTIVE_REFUSED missing-history");return true;}
            var c=lease.Identity.Character;var owner=session.GetType().Assembly.GetType("Freelancer604.DragDoll.DragDollPlugin",true);
            bool noNewSession=Static(owner,"Session",c)==null;
            bool valid=noNewSession&&!TimelinePlaying()&&!desired.TimelineOwned&&!expected.TimelineOwned&&desired.TimelineEpoch==expected.TimelineEpoch&&desired.TimelineIkEpoch==expected.TimelineIkEpoch&&desired.Valid()&&expected.Valid()&&lease.Identity.Valid()&&c.oiCharInfo.enableFK&&!c.oiCharInfo.enableIK&&c.fkCtrl.enabled&&!c.finalIK.enabled&&lease.Actual.Matches(lease.Identity);
            bool endpoint=lease.Endpoint!=null&&lease.Endpoint.SameEndpoint(expected);
            if(!valid||!endpoint){lease.Retired=true;Log.LogWarning("DD_HISTORY_INACTIVE_REFUSED identity-or-external-state");return true;}
            var before=Raw.Capture(lease.Identity);
            try
            {
                ApplyPose(desired,session);lease.Actual=Raw.Capture(desired);lease.Identity=desired;lease.Endpoint=desired;Log.LogInfo("DD_HISTORY_INACTIVE_"+(redo?"REDO":"UNDO"));return true;
            }
            catch(Exception e)
            {
                try{before.Restore();EvaluateFk(session,c.fkCtrl);}catch(Exception rollback){Log.LogError("DD_HISTORY_ROLLBACK_FAILED "+rollback);}
                lease.Retired=true;Log.LogError("DD_HISTORY_INACTIVE_FAILED "+e);return true;
            }
        }
        private static void EvaluateFk(object session,FKCtrl fk){Static(session.GetType().Assembly.GetType("Freelancer604.DragDoll.NativeFk",true),"Evaluate",fk);}
        private static void ApplyPose(Pose p,object session)
        {
            var c=p.Character;p.RootAmount.pos=p.RootPosition;p.RootAmount.rot=p.RootRotation;
            if(p.HeightAmount!=null)p.HeightAmount.pos=p.HeightValue;
            foreach(var row in p.Rotations)p.Bones[row.Key].Amount.rot=row.Value;
            foreach(var row in p.Positions)row.Key.localPosition=row.Value;
            foreach(var target in p.Targets){target.Amount.pos=target.Position;target.Amount.rot=target.Rotation;}
            if(p.Hands!=null)for(int i=0;i<p.Hands.Length;i++)Call(p.Hands[i],"Restore",c,i);
            EvaluateFk(session,c.fkCtrl);
            if(p.Solved)
            {
                if(p.HeightReady&&p.Height&&p.HeightParent)
                {
                    Vector3 delta=p.PelvisWorld-p.Pelvis.position,perHeight=p.HeightParent.TransformVector(Vector3.up);
                    if(Finite(delta)&&Finite(perHeight)&&Math.Abs(perHeight.y)>=1e-5f){float difference=delta.y/perHeight.y;if(!float.IsNaN(difference)&&!float.IsInfinity(difference)&&Math.Abs(difference)>=1e-6f){var local=p.Height.transformTarget.localPosition;p.HeightAmount.pos=p.HeightValue+Vector3.up*difference;p.Height.transformTarget.localPosition=local+Vector3.up*difference;}}
                }
                Vector3 residual=p.PelvisWorld-p.Pelvis.position;if(!Finite(residual))throw new InvalidOperationException("Nonfinite history pelvis position");if(residual.sqrMagnitude>1e-12f)p.Root.SetWorldPos(p.RootTarget.position+residual);
            }
        }
    }
}
