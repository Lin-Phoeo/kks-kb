using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using KKAPI.Studio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Codex.StudioBustNativeText
{
    [BepInPlugin(Guid, "Studio Bust Native Text (zh-CN)", "1.0.0")]
    [BepInProcess("CharaStudio")]
    [BepInDependency("Vaaskr.StudioBustAdjuster")]
    [BepInDependency("marco.kkapi")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string Guid = "codex.studio.bustnativetext";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static Plugin instance;
        private Harmony harmony;
        private MethodInfo categoryMethod, sliderMethod;
        private readonly List<TextEdit> edits = new List<TextEdit>();
        private ConditionalWeakTable<Component, object> changed = new ConditionalWeakTable<Component, object>();

        private sealed class TextEdit
        {
            internal WeakReference Target;
            internal string Before, After;
        }

        private void Awake()
        {
            try
            {
                NativeTextPolicy.RequireFile(Chainloader.PluginInfos["Vaaskr.StudioBustAdjuster"].Location, NativeTextPolicy.BustSha);
                NativeTextPolicy.RequireFile(typeof(CurrentStateCategory).Assembly.Location, NativeTextPolicy.ApiSha);
                categoryMethod = typeof(CurrentStateCategory).GetMethod("CreateCategory", All, null, new[] { typeof(GameObject) }, null);
                sliderMethod = typeof(CurrentStateCategorySlider).GetMethod("CreateItem", All, null, new[] { typeof(GameObject) }, null);
                if (categoryMethod == null || categoryMethod.ReturnType != typeof(void) || sliderMethod == null || sliderMethod.ReturnType != typeof(GameObject))
                    throw new InvalidOperationException("KKAPI creation signature changed.");
                harmony = new Harmony(Guid);
                instance = this;
                harmony.Patch(categoryMethod, postfix: new HarmonyMethod(typeof(Plugin).GetMethod("HeaderCreated", All)));
                harmony.Patch(sliderMethod, postfix: new HarmonyMethod(typeof(Plugin).GetMethod("SliderCreated", All)));
                Logger.LogInfo("BUST_NATIVE_TEXT_READY labels=13 mode=creation-postfix nodeIdentityPreserved=True");
            }
            catch (Exception error)
            {
                Cleanup();
                Logger.LogWarning("BUST_NATIVE_TEXT_REFUSED " + error.GetType().Name);
            }
        }

        internal static void HeaderCreated(CurrentStateCategory __instance, GameObject containerObject)
        {
            if (instance == null || __instance == null || __instance.CategoryName != NativeTextPolicy.Category || containerObject == null) return;
            Transform header = containerObject.transform.Find(NativeTextPolicy.HeaderNode);
            if (header == null) return;
            TMP_Text text = header.GetComponent<TMP_Text>();
            if (text != null) instance.Apply(text, NativeTextPolicy.Header);
        }

        internal static void SliderCreated(CurrentStateCategorySlider __instance, GameObject categoryObject, GameObject __result)
        {
            if (instance == null || __instance == null || categoryObject == null || __result == null) return;
            string translated;
            if (!NativeTextPolicy.TryLabel(categoryObject.name, __instance.Name, out translated) || __result.name != "CustomSlider " + __instance.Name) return;
            Text text = __result.GetComponentInChildren<Text>(true);
            if (text != null && text.gameObject.name == "Text " + __instance.Name) instance.Apply(text, translated);
        }

        private static string Read(Component target)
        {
            Text text = target as Text;
            if (text != null) return text.text;
            TMP_Text tmp = target as TMP_Text;
            return tmp == null ? null : tmp.text;
        }

        private static void Write(Component target, string value)
        {
            Text text = target as Text;
            if (text != null) text.text = value;
            else ((TMP_Text)target).text = value;
        }

        private void Apply(Component target, string value)
        {
            string before = Read(target);
            object marker;
            if (before == value || changed.TryGetValue(target, out marker)) return;
            changed.Add(target, new object());
            edits.Add(new TextEdit { Target = new WeakReference(target), Before = before, After = value });
            Write(target, value);
        }

        private void OnDestroy() { Cleanup(); }

        private void Cleanup()
        {
            if (harmony != null)
            {
                if (categoryMethod != null) harmony.Unpatch(categoryMethod, HarmonyPatchType.Postfix, Guid);
                if (sliderMethod != null) harmony.Unpatch(sliderMethod, HarmonyPatchType.Postfix, Guid);
            }
            harmony = null;
            if (ReferenceEquals(instance, this)) instance = null;
            foreach (TextEdit edit in edits)
            {
                Component target = edit.Target.Target as Component;
                if (target != null && Read(target) == edit.After) Write(target, edit.Before);
            }
            edits.Clear();
            changed = new ConditionalWeakTable<Component, object>();
        }
    }
}
