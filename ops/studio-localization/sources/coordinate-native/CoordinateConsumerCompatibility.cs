using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Codex.StudioCoordinateNative
{
    internal static class CoordinateConsumerCompatibility
    {
        internal const string DbdeId = "org.njaecha.plugins.dbde";
        internal const string AssetImportId = "org.njaecha.plugins.assetimport";
        internal const string DbdeSha = "baa0fcc5c5658182385eb59694e34f5264fd68d87a2885b1ec91f15ee1d98f19";
        internal const string AssetImportSha = "2a8ecb9df5a919c68c91322d01f4979d3dec08ada30deaf3e940312aeaeea441";
        internal static Func<bool> AddMode;
        internal static Action<string, bool> FirstDecision;
        internal static long ReplacementChecks, AdditionChecks, MissingButtonChecks, EmptyChecks;
        private static int _reportedDecisions;
        private static readonly MethodInfo Inequality = typeof(string).GetMethod("op_Inequality", new[] { typeof(string), typeof(string) });
        private static readonly MethodInfo Equality = typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) });
        private static readonly MethodInfo ModeCheck = typeof(CoordinateConsumerCompatibility).GetMethod("IsUnsupportedMode", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo EmptyCheck = typeof(CoordinateConsumerCompatibility).GetMethod("IsEmptySlot", BindingFlags.Static | BindingFlags.NonPublic);

        internal static string ExpectedHash(string pluginId)
        {
            if (pluginId == DbdeId) return DbdeSha;
            if (pluginId == AssetImportId) return AssetImportSha;
            throw new InvalidOperationException("Unknown coordinate consumer");
        }

        internal static void RequireIdentity(string pluginId, string actualHash)
        {
            if (!string.Equals(ExpectedHash(pluginId), actualHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Coordinate consumer build differs");
        }

        internal static Func<bool> BindMode(FieldInfo field)
        {
            if (field == null || !field.IsStatic || field.FieldType != typeof(bool))
                throw new InvalidOperationException("Coordinate mode field differs");
            var method = new DynamicMethod("ReadCoordinateAddMode", typeof(bool), Type.EmptyTypes, field.DeclaringType, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<bool>)method.CreateDelegate(typeof(Func<bool>));
        }

        private static bool IsUnsupportedMode(string displayed, string expected)
        {
            if (string.IsNullOrEmpty(displayed) || AddMode == null)
            {
                MissingButtonChecks++;
                ObserveOnce(1, "missing-button-or-mode", true);
                return displayed != expected;
            }
            bool addition = AddMode();
            if (addition) AdditionChecks++; else ReplacementChecks++;
            ObserveOnce(addition ? 2 : 4, "add-mode", addition);
            return addition;
        }

        private static bool IsEmptySlot(string displayed, string expected)
        {
            bool empty = displayed == expected || displayed == "空";
            if (empty) { EmptyChecks++; ObserveOnce(8, "empty-slot", true); }
            return empty;
        }

        private static void ObserveOnce(int flag, string kind, bool value)
        {
            if ((_reportedDecisions & flag) != 0 || FirstDecision == null) return;
            _reportedDecisions |= flag;
            // A failing diagnostic sink must not interrupt a coordinate transfer.
            try { FirstDecision(kind, value); } catch (Exception) { }
        }

        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            int modeCount = 0, emptyCount = 0;
            for (int index = 1; index < result.Count; index++)
            {
                CodeInstruction previous = result[index - 1], current = result[index];
                if (previous.opcode != OpCodes.Ldstr || current.opcode != OpCodes.Call) continue;
                MethodInfo replacement = null;
                if (Equals(previous.operand, "Replace Mode") && Equals(current.operand, Inequality))
                { replacement = ModeCheck; modeCount++; }
                else if (Equals(previous.operand, "Empty") && Equals(current.operand, Equality))
                { replacement = EmptyCheck; emptyCount++; }
                if (replacement != null)
                {
                    // Preserve branch labels and exception regions on the replaced call.
                    var patched = new CodeInstruction(current) { operand = replacement };
                    result[index] = patched;
                }
            }
            if (modeCount != 1 || emptyCount != 1)
                throw new InvalidOperationException("Coordinate consumer comparison shape differs");
            return result;
        }
    }
}
