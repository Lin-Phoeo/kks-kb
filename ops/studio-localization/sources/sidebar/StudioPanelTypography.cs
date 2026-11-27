using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Codex.StudioPanelTypography
{
    [BepInPlugin("codex.studio.paneltypography", "Studio Sidebar Chinese Typography", "1.2.1")]
    [BepInDependency("codex.studio.fontcontroller", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("starstorm.lightsettings")]
    [BepInProcess("CharaStudio")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string LightSettingsSha = "C2274137AFCBCA2A849274784D316F99CB8A277F15ED9D4BD9DA5372FB88A69F";
        private const int BodySize = 14;
        private static Font font;
        private static readonly ConditionalWeakTable<Dropdown, object> LightDropdowns = new ConditionalWeakTable<Dropdown, object>();
        private static readonly object DropdownMarker = new object();
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Light On/Off"] = "灯光开关",
            [" Light On/Off"] = "灯光开关",
            ["Light controls"] = "灯光控制",
            ["Strength"] = "强度",
            ["Intensity"] = "范围",
            ["Spot Angle"] = "聚光角度",
            ["Shadow Type"] = "阴影类型",
            ["Shadow Resolution"] = "阴影分辨率",
            ["Shadow Custom Resolution"] = "自定义阴影分辨率",
            ["Shadow Strength"] = "阴影强度",
            ["Shadow Bias"] = "阴影偏移",
            ["Shadow Normal Bias"] = "法线偏移",
            ["Shadow Near Plane"] = "阴影近裁剪面",
            ["Light Render Mode"] = "灯光渲染模式",
            ["Culling Mask"] = "照明对象",
            ["Chara"] = "角色",
            ["Map"] = "地图",
            ["Water"] = "水面",
            ["Gizmos"] = "辅助工具",
            ["Cookie Size"] = "投影遮罩尺寸"
        };
        private static readonly Dictionary<string, string> Choices = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["None"] = "无阴影", ["Soft"] = "软阴影", ["Hard"] = "硬阴影",
            ["From Quality Settings"] = "跟随质量设置", ["Low"] = "低", ["Medium"] = "中",
            ["High"] = "高", ["Very High"] = "极高", ["Auto"] = "自动",
            ["Force Pixel"] = "逐像素", ["Force Vertex"] = "逐顶点"
        };

        private Harmony harmony;
        private SidebarTypography sidebar;

        private void Awake()
        {
            sidebar = new SidebarTypography(Logger);
            var related = Chainloader.PluginInfos["starstorm.lightsettings"];
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(related.Location))
            {
                var hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(hash, LightSettingsSha, StringComparison.Ordinal))
                {
                    Logger.LogWarning("LightSettings build differs. Typography hooks were skipped.");
                    return;
                }
            }
            var handler = related.Instance.GetType().Assembly.GetType("LightSettings.Koikatu.UIHandler", true);
            harmony = new Harmony("codex.studio.paneltypography");
            try
            {
                AddPostfix(AccessTools.Method(handler, "MakeToggle"), nameof(TogglePostfix));
                AddPostfix(AccessTools.Method(handler, "MakeSlider"), nameof(SliderPostfix));
                AddPostfix(AccessTools.Method(handler, "MakeLabel"), nameof(LabelPostfix));
                AddPostfix(AccessTools.Method(handler, "MakeDropDown"), nameof(MakeDropdownPostfix));
                AddPostfix(AccessTools.Method(typeof(Dropdown), "RefreshShownValue"), nameof(RefreshDropdownPostfix));
                AddPostfix(AccessTools.Method(typeof(Dropdown), "AddItem"), nameof(AddDropdownItemPostfix));
                Logger.LogInfo("LightSettings typography hooks installed.");
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                Logger.LogError(exception);
            }
        }

        private void Update()
        {
            if (sidebar != null) sidebar.Tick();
        }

        private void AddPostfix(MethodBase original, string methodName)
        {
            if (original == null) throw new MissingMethodException(methodName);
            harmony.Patch(original, postfix: new HarmonyMethod(typeof(Plugin), methodName));
        }

        private static void ApplyText(Text text, string value, bool centered)
        {
            if (!text) return;
            if (!font) font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", BodySize);
            Font sharedFont;
            TMP_FontAsset sharedTmp;
            Font selected = SharedFontAccess.Read(out sharedFont, out sharedTmp) ? sharedFont : font;
            if (selected) text.font = selected;
            text.text = value;
            text.fontSize = BodySize;
            text.alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = BodySize;
        }

        private static void TogglePostfix(string __1, Transform __result)
        {
            if (__result && __result.childCount > 1 && Labels.TryGetValue(__1, out var value))
                ApplyText(__result.GetChild(1).GetComponent<Text>(), value, false);
        }

        private static void SliderPostfix(string __1, Transform __result)
        {
            if (__result && __result.childCount > 0 && Labels.TryGetValue(__1, out var value))
                ApplyText(__result.GetChild(0).GetComponent<Text>(), value, false);
        }

        private static void LabelPostfix(string __1, Transform __result)
        {
            if (__result && Labels.TryGetValue(__1, out var value))
                ApplyText(__result.GetComponent<Text>(), value, false);
        }

        private static void MakeDropdownPostfix(string __1, Transform __result)
        {
            if (!__result || !Labels.TryGetValue(__1, out var value)) return;
            if (__result.childCount > 0)
            {
                var title = __result.GetChild(0).GetComponent<TextMeshProUGUI>();
                if (title)
                {
                    title.text = value;
                    title.fontSize = BodySize;
                    title.fontSizeMin = 11;
                    title.fontSizeMax = BodySize;
                    title.enableAutoSizing = true;
                    title.enableWordWrapping = false;
                    title.alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
            var dropdown = __result.GetComponentInChildren<Dropdown>(true);
            if (!dropdown) return;
            LightDropdowns.GetValue(dropdown, ignored => DropdownMarker);
            RefreshDropdownPostfix(dropdown);
        }

        // Localize the rendered label and leave OptionData.text intact for LightSettings' enum matching.
        private static void RefreshDropdownPostfix(Dropdown __instance)
        {
            if (!LightDropdowns.TryGetValue(__instance, out _) || __instance.value < 0 || __instance.value >= __instance.options.Count) return;
            var source = __instance.options[__instance.value].text;
            var value = source != null && Choices.TryGetValue(source, out var chinese) ? chinese : source;
            ApplyText(__instance.captionText, value, true);
        }

        private static void AddDropdownItemPostfix(Dropdown __instance, Dropdown.OptionData __0, object __result)
        {
            if (!LightDropdowns.TryGetValue(__instance, out _) || __0 == null || !(__result is Component item)) return;
            var source = __0.text;
            var value = source != null && Choices.TryGetValue(source, out var chinese) ? chinese : source;
            ApplyText(item.GetComponentInChildren<Text>(true), value, false);
        }

        private void OnDestroy()
        {
            if (sidebar != null) sidebar.Stop();
            harmony?.UnpatchSelf();
        }
    }
}
