using BepInEx.Configuration;using UnityEngine;using KK_PovX;
public class Singleton<T>{public static bool Present;public static T Instance{get;set;}public static bool IsInstance()=>Present;}
public class GameCursor{public void SetCursorLock(bool value){Cursor.lockState=value?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!value;}}
namespace UnityEngine{public enum CursorLockMode{None,Locked}public static class Cursor{public static bool visible{get;set;}public static CursorLockMode lockState{get;set;}}public static class Input{public static bool Released;public static bool GetMouseButtonUp(int key)=>key==0&&Released;}}
namespace BepInEx.Configuration{public class ConfigEntry<T>{public T Value{get;set;}}public struct KeyboardShortcut{public bool IsDown()=>Fixture.KeyDown;}}
namespace KK_PovX{public static class KK_PovX{public static ConfigEntry<KeyboardShortcut> ToggleCursorKey{get;set;}=new();}public static class Controller{public static bool PoVToggled{get;set;}public static bool FreeRoamToggled{get;set;}public static bool IsCursorLocked()=>Cursor.lockState==CursorLockMode.Locked;public static void Update_Cursor()=>throw new Exception("actual CIL required");public static void ToggleCursorLock(bool flag)=>throw new Exception("actual CIL required");}}
public static class Fixture{
 public static bool KeyDown;
 public static int Main(){int checks=0,failed=0;
 void Test(string name,bool pov,bool roam,bool present,bool down,bool release,bool initial,bool expected){checks++;Controller.PoVToggled=pov;Controller.FreeRoamToggled=roam;Singleton<GameCursor>.Present=present;Singleton<GameCursor>.Instance=new();KeyDown=down;Input.Released=release;Cursor.lockState=initial?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!initial;try{Controller.Update_Cursor();if((Cursor.lockState==CursorLockMode.Locked)!=expected||Cursor.visible==expected)throw new Exception("cursor state differs");Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
 foreach(bool native in new[]{false,true}){
 Test("inactive release preserves foreign lock "+native,false,false,native,false,true,true,true);
 Test("inactive hotkey preserves foreign lock "+native,false,false,native,true,false,true,true);
 Test("inactive hotkey preserves visible pointer "+native,false,false,native,true,false,false,false);
 Test("active POV release unlocks "+native,true,false,native,false,true,true,false);
 Test("active free roam release unlocks "+native,false,true,native,false,true,true,false);
 Test("active POV hotkey locks "+native,true,false,native,true,false,false,true);
 Test("active free roam hotkey unlocks "+native,false,true,native,true,false,true,false);
 Test("active hotkey retains priority over release "+native,true,false,native,true,true,false,true);
 Test("inactive idle no change "+native,false,false,native,false,false,true,true);
 }
 Console.WriteLine($"checks={checks} failures={failed}");return failed==0?0:1;}
}
