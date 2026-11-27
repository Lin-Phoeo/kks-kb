using System;
using System.Collections.Generic;

namespace Codex.StudioCoordinateNative
{
    public static class NativeCoordinateStrings
    {
        public const string PluginId = "com.jim60105.kks.coordinateloadoption";
        public const string ExpectedAssemblySha256 = "ea95a650360cd7f54c1905276afc3afa4829e2e4bf01d7ffb0b2900924c42cae";
        public const string ResourceName = "CoordinateLoadOption.StringResources.StringResources.resources";
        public const string ExpectedEnglishResourceSha256 = "d05cec0f74b321fc316110a92ed2c8a0a195b52e7123d0902ebbc536f30f0e42";
        public const string TargetType = "CoordinateLoadOption.StringResources.StringResourcesManager";

        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ClothesKind_top", "上装" },
            { "ClothesKind_bra", "胸罩" },
            { "ClothesKind_bot", "下装" },
            { "bodyOverlay", "身体叠加贴图" },
            { "faceOverlay", "面部叠加贴图" },
            { "showSelection", "显示选择项" },
            { "readABMX", "ABMX" },
            { "clearAccWord", "清空饰品" },
            { "irisOverlay", "虹膜叠加贴图" },
            { "addMode", "追加模式" },
            { "makerWarning", "在角色编辑器中切换服装配置时，可能在此遇到错误。\n" +
                "选择性载入服装配置会先将配置载入并应用到临时角色，再取出需要的部分。\n" +
                "KKSAPI 按角色编辑器只载入一个角色的前提设计，因此依赖它的插件可能无法初始化这个临时角色。\n" +
                "遇到错误时可尝试安装 IllusionFixes.NullChecks，以跳过这类检查。\n" +
                "临时角色不使用其他未支持插件的数据，因此这些插件初始化失败不会影响本次服装配置切换的结果。" },
            { "ClothesKind_accessories", "饰品" },
            { "empty", "空" },
            { "ClothesKind_shorts", "内裤" },
            { "replaceMode", "替换模式" },
            { "ClothesKind_gloves", "手套" },
            { "reverseHairAcc", "反选头发饰品" },
            { "lockHairAcc", "锁定头发饰品" },
            { "ClothesKind_shoes_outer", "室外鞋" },
            { "ClothesKind_shoes_inner", "室内鞋" },
            { "unreconized", "未识别" },
            { "charaOverlay", "角色叠加贴图" },
            { "ClothesKind_panst", "连裤袜" },
            { "ClothesKind_socks", "袜子" }
        };

        public static int Count { get { return Values.Count; } }
        public static IEnumerable<string> Keys { get { return Values.Keys; } }

        public static bool TryGet(string key, string requestedLanguage, out string value)
        {
            value = null;
            if (key == null || !string.IsNullOrEmpty(requestedLanguage)) return false;
            return Values.TryGetValue(key, out value);
        }
    }
}
