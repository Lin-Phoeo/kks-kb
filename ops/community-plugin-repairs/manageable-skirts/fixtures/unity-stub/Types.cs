namespace UnityEngine;
public class Object { public bool Destroyed; public string name {get;set;} public static implicit operator bool(Object o)=>!ReferenceEquals(o,null)&&!o.Destroyed; public static bool operator ==(Object a,Object b)=>(!a&&!b)||ReferenceEquals(a,b); public static bool operator !=(Object a,Object b)=>!(a==b); public override bool Equals(object o)=>ReferenceEquals(this,o); public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
public class GameObject:Object { public readonly List<Component> Children=new(); public T GetComponentInChildren<T>() where T:class=>Children.OfType<T>().FirstOrDefault(); public T[] GetComponentsInChildren<T>(bool includeInactive) where T:class=>Children.OfType<T>().ToArray(); }
public class Component:Object { public GameObject gameObject {get;set;}=new(); private Transform _transform; public Transform transform {get=>this as Transform??(_transform??=new());set=>_transform=value;} public T[] GetComponentsInChildren<T>(bool includeInactive) where T:class=>gameObject.GetComponentsInChildren<T>(includeInactive); }
public class MonoBehaviour:Component{} public class Animator:Component{}
public class Transform:Component { public Vector3 position {get;set;} public Vector3 forward {get;set;}=new(0,0,1); }
public class Camera:Component { public static Camera main {get;set;}=new(); }
public static class Application { public static string companyName {get;set;}="illusion\\KoikatsuSunshine"; }
public static class Time { public static float deltaTime {get;set;}=.16f; }
public enum KeyCode{F9=290,LeftShift=304}
public struct Color:IEquatable<Color>{public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}public bool Equals(Color c)=>r==c.r&&g==c.g&&b==c.b&&a==c.a;public override bool Equals(object o)=>o is Color c&&Equals(c);public override int GetHashCode()=>HashCode.Combine(r,g,b,a);public static bool operator ==(Color a,Color b)=>a.Equals(b);public static bool operator !=(Color a,Color b)=>!a.Equals(b);}
public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;}
public static class Mathf{public static float Min(float a,float b)=>Math.Min(a,b);}
public class Material:Object{public Color color {get;set;} private Color emission;public bool HasProperty(string n)=>n=="_EmissionColor";public void SetColor(string n,Color c){emission=c;}public Color GetColor(string n)=>emission;}
