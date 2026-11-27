using UnityEngine;
public class Singleton<T>{public static T Instance{get;set;}}
public class ChaFileParameter{public string fullname{get;set;}="Synthetic";}
public class ChaInfo:MonoBehaviour{public ChaFileParameter fileParam{get;set;}=new();public Animator animBody{get;set;}=new();}
public class ChaControl:ChaInfo{}
namespace Studio{
public class Studio:MonoBehaviour{public TreeNodeCtrl treeNodeCtrl{get;set;}=new();public Dictionary<TreeNodeObject,ObjectCtrlInfo> dicInfo=new();}
public class TreeNodeCtrl{public TreeNodeObject[] selectNodes{get;set;}=Array.Empty<TreeNodeObject>();}
public class TreeNodeObject:MonoBehaviour{public TreeNodeObject parent{get;set;}public bool visible{get;set;}=true;}
public class ObjectCtrlInfo{public TreeNodeObject treeNodeObject=new();}
public class OICharInfo{public bool[] activeFK=new bool[7];}
public class OIBoneInfo{[Flags]public enum BoneGroup{Body=1,Skirt=1024}}
public class OCIChar:ObjectCtrlInfo{public ChaControl charInfo=new();public List<BoneInfo> listBones=new();public OICharInfo oiCharInfo{get;set;}=new();public class BoneInfo{public OIBoneInfo.BoneGroup boneGroup{get;set;}public GuideObject guideObject{get;set;}}}
public class GuideObject:MonoBehaviour{public Transform transformTarget=new();public bool FailVisibilityWrite;private bool _visible=true;public bool visible{get{if(Destroyed)throw new InvalidOperationException("Destroyed guide read");return _visible;}set{if(Destroyed||FailVisibilityWrite)throw new InvalidOperationException("Guide write failure");_visible=value;}}public bool visibleOutside{get;set;}=true;}
public class GuideBase:MonoBehaviour{protected Color colorNormal;protected Color colorHighlighted;public Material material{get;set;}=new();public void Seed(Color normal,Color highlighted,Color shown){colorNormal=normal;colorHighlighted=highlighted;material.color=shown;material.SetColor("_EmissionColor",shown);}public Color Normal=>colorNormal;public Color Highlight=>colorHighlighted;}
public class GuideSelect:GuideBase{public Color color{set{colorNormal=new Color(value.r*.75f,value.g*.75f,value.b*.75f,.25f);colorHighlighted=new Color(value.r,value.g,value.b,1f);material.color=colorNormal;material.SetColor("_EmissionColor",colorNormal);}}}
}
