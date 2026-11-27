using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;

namespace Codex.StudioNativePluginText
{
    [BepInPlugin("codex.studio.nativeplugintext", "Studio Native Plugin Text (zh-CN)", "1.0.0")]
    [BepInProcess("CharaStudio")]
    [BepInDependency("com.joan6694.illusionplugins.videoexport")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string ExpectedSha = "ED153538B453B5DC72DC4FDDC51F4E68FBE770CF00669F4874AD3D509E6A72A7";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private void Awake()
        {
            try
            {
                var info = Chainloader.PluginInfos["com.joan6694.illusionplugins.videoexport"];
                using (var algorithm = SHA256.Create())
                using (var stream = File.OpenRead(info.Location))
                {
                    var actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
                    if (actual != ExpectedSha)
                    {
                        Logger.LogWarning("NATIVE_TEXT_SKIPPED VideoExport build differs.");
                        return;
                    }
                }
                Type plugin = info.Instance.GetType();
                object chinese = plugin.GetField("_chineseDictionary", Flags).GetValue(null);
                object current = plugin.GetField("_currentDictionary", Flags).GetValue(null);
                Type keyType = plugin.GetNestedType("TranslationKey", Flags);
                var strings = (IDictionary)chinese.GetType().GetField("_strings", Flags).GetValue(chinese);
                int changed = NativeTextRules.Apply(strings, keyType);
                bool active = ReferenceEquals(chinese, current);
                if (active)
                {
                    plugin.GetField("_limitDurationNames", Flags).SetValue(info.Instance, new[]
                    {
                        Read(strings, keyType, "LimitByFrames"), Read(strings, keyType, "LimitBySeconds"),
                        Read(strings, keyType, "LimitByAnimation"), Read(strings, keyType, "LimitByTimeline")
                    });
                    RefreshLabels(plugin.GetField("_screenshotPlugins", Flags).GetValue(info.Instance));
                    RefreshLabels(plugin.GetField("_extensions", Flags).GetValue(info.Instance));
                }
                Logger.LogInfo("NATIVE_TEXT_READY VideoExport=" + changed + "/" + NativeTextRules.Corrections.Length
                    + " activeChinese=" + active + " configWritten=False");
            }
            catch (Exception exception)
            {
                Logger.LogError("NATIVE_TEXT_FAILED " + exception);
            }
        }

        private static string Read(IDictionary strings, Type keyType, string key)
        {
            return (string)strings[Enum.Parse(keyType, key)];
        }

        private static void RefreshLabels(object list)
        {
            foreach (object item in (IEnumerable)list)
                item.GetType().GetMethod("UpdateLanguage", Flags).Invoke(item, null);
        }
    }
}
