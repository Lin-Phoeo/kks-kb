using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine {
 public class Object {public bool Destroyed; public string name {get;set;}="synthetic"; public static implicit operator bool(Object value)=>value!=null&&!value.Destroyed;}
 public class Transform:Object{}
 public class MonoBehaviour:Object{}
 public class GameObject:Object {public DynamicBone[] Bones=Array.Empty<DynamicBone>(); public T[] GetComponentsInChildren<T>(bool includeInactive)=>Bones.Cast<T>().ToArray();}
}
public sealed class DynamicBone:UnityEngine.Object {public UnityEngine.Transform m_Root;}
public class ChaFileStatus {public int coordinateType {get;set;}}
public class ChaInfo:UnityEngine.Object {public ChaFileStatus fileStatus {get;}=new();}
public class ChaControl:ChaInfo{}
namespace KKAPI.Chara {public class CharaCustomFunctionController:UnityEngine.MonoBehaviour {public ChaControl ChaControl {get;}=new();}}
namespace DynamicBoneDistributionEditor {
 public class DBDEUI:UnityEngine.MonoBehaviour {public bool UpdateUIWhileOpen=true;}
 public static class DBDE {public static DBDEUI UI=new();}
 public class DBDEDynamicBoneEdit {
  public DynamicBone PrimaryDynamicBone {get;set;}
  public int ReferCalls,ApplyCalls,UpdateCalls;public bool FailApply;
  public DynamicBone LastBound;
  public void ReferInitialsToDynamicBone(DynamicBone value){ReferCalls++;LastBound=value;}
  public void ApplyAll(){ApplyCalls++;if(FailApply)throw new InvalidOperationException("synthetic apply failure");}
  public void UpdateActiveStack(bool always=false){UpdateCalls++;}
 }
 public class DBDECharaController:KKAPI.Chara.CharaCustomFunctionController {
  public bool IsLoading {get;set;}
  public Dictionary<int,Dictionary<string,List<DynamicBone>>> _dynamicBoneCache=new();
  public Dictionary<int,List<DBDEDynamicBoneEdit>> DistributionEdits=new();
  public int RefreshCalls; public bool FailRefresh;public DBDEUI ReplaceUiDuringRefresh;
  public void RefreshBoneList(string reason,bool removeDead=true){RefreshCalls++;if(ReplaceUiDuringRefresh!=null)DBDE.UI=ReplaceUiDuringRefresh;if(FailRefresh)throw new InvalidOperationException("synthetic refresh failure");}
  public void ClothesChanged(UnityEngine.GameObject clothGO)=>throw new NotImplementedException("Actual input CIL must be transplanted before running.");
 }
}
