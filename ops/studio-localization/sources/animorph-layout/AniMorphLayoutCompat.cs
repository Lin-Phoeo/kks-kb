using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Codex.StudioAniMorphLayoutCompat
{
    [BepInPlugin(PluginGuid, "动作形变布局修正 / Anisotropic Morph Layout", "1.0.0")]
    [BepInProcess("CharaStudio")]
    [BepInDependency("AniMorph.ABMX", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string PluginGuid = "codex.studio.animorph.layoutcompat";
        private const string AniHash = "b3af1a04664a9db48f476d939f69278ce2971647c97a5a721ebfc5c36dc3a6b0";
        private const string ManagerHash = "91b1c972d397df93681c197f77badcbfcd39f8957e200eaf7ee989cbbf74636c";
        private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static object _manager;
        private static PropertyInfo _width;
        private Harmony _harmony;
        private MethodInfo _target;

        private void Awake()
        {
            try
            {
                var ani=Chainloader.PluginInfos["AniMorph.ABMX"];
                var manager=Chainloader.PluginInfos["com.bepis.bepinex.configurationmanager"];
                RequireHash(ani.Location,AniHash);RequireHash(manager.Location,ManagerHash);
                _manager=manager.Instance;
                _width=manager.Instance.GetType().GetProperty("RightColumnWidth",All);
                if(_width==null||_width.PropertyType!=typeof(int))throw new InvalidOperationException("Manager width signature differs");
                Type closure=ani.Instance.GetType().Assembly.GetType("AniMorph.CNLocalization+<>c__DisplayClass9_0",true);
                _target=closure.GetMethod("<MakeFlagsDrawer>b__2",All,null,new[]{typeof(BepInEx.Configuration.ConfigEntryBase)},null);
                if(_target==null||_target.ReturnType!=typeof(void))throw new InvalidOperationException("Flags drawer signature differs");
                _harmony=new Harmony(PluginGuid);
                _harmony.Patch(_target,transpiler:new HarmonyMethod(typeof(Plugin).GetMethod("Transpile",All)));
                Logger.LogInfo("STUDIO_ANIMORPH_LAYOUT_READY stableWidth=ConfigurationManager.RightColumnWidth");
            }
            catch(Exception error)
            {
                Cleanup();
                Logger.LogWarning("STUDIO_ANIMORPH_LAYOUT_REFUSED "+error.GetType().Name);
            }
        }
        private static void RequireHash(string path,string expected)
        {
            using(var hash=SHA256.Create())
            using(var stream=File.OpenRead(path))
                if(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=expected)
                    throw new InvalidOperationException("Dependency build differs");
        }
        private static float StableWidth(float ignoredGUILayoutWidth)
        {
            if(_manager==null||_width==null)return WidthPolicy.Resolve(0);
            return WidthPolicy.Resolve((int)_width.GetValue(_manager,null));
        }
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var result=new List<CodeInstruction>();
            MethodInfo readWidth=typeof(Rect).GetProperty("width").GetGetMethod();
            MethodInfo stable=typeof(Plugin).GetMethod("StableWidth",All);
            int replaced=0;
            foreach(CodeInstruction instruction in instructions)
            {
                result.Add(instruction);
                if(instruction.Calls(readWidth))
                {
                    result.Add(new CodeInstruction(OpCodes.Call,stable));
                    replaced++;
                }
            }
            if(replaced!=1)throw new InvalidOperationException("Expected one drawer width read");
            return result;
        }
        private void OnDestroy(){Cleanup();}
        private void Cleanup()
        {
            if(_harmony!=null&&_target!=null)_harmony.Unpatch(_target,HarmonyPatchType.Transpiler,PluginGuid);
            _harmony=null;_target=null;_manager=null;_width=null;
        }
    }
    public static class WidthPolicy
    {
        public static float Resolve(int rightColumnWidth){return rightColumnWidth>=40?rightColumnWidth:260f;}
    }
}
