using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.InputSystem;

namespace ReignBeta.Integration
{
    // The native global map bar also ticks over modal screens opened above an
    // inventory/party panel. Those screens need not register campaign shortcuts.
    internal static class ReignMapBarInputSafetyPatch
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.mapbar.input_safety";
        private static readonly FieldInfo RegisteredGameKeys =
            AccessTools.Field(typeof(InputContext), "_registeredGameKeys");
        private static Harmony _harmony;

        internal static void Apply()
        {
            if (_harmony != null) return;
            try
            {
                if (RegisteredGameKeys == null || RegisteredGameKeys.FieldType != typeof(List<GameKey>))
                    throw new MissingFieldException("InputContext._registeredGameKeys has an unsupported layout.");

                Type mapBar = Type.GetType("SandBox.GauntletUI.Map.GauntletMapBarGlobalLayer, SandBox.GauntletUI", true);
                Type navalMapBar = Type.GetType("NavalDLC.GauntletUI.Map.GauntletNavalMapBarGlobalLayer, NavalDLC.GauntletUI", false);
                _harmony = new Harmony(HarmonyId);
                Patch(mapBar);
                if (navalMapBar != null) Patch(navalMapBar);
                ReignLog.Info("Installed map-bar input safety patch (Naval DLC: " + (navalMapBar != null) + ").");
            }
            catch (Exception ex)
            {
                Unapply();
                ReignLog.Warn("Map-bar input safety patch failed to load: " + ex);
            }
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static void Patch(Type type)
        {
            MethodInfo target = AccessTools.DeclaredMethod(type, "HandlePanelSwitchingInput", new[] { typeof(InputContext) });
            if (target == null || target.ReturnType != typeof(bool))
                throw new MissingMethodException(type.FullName, "HandlePanelSwitchingInput(InputContext)");
            _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ReignMapBarInputSafetyPatch), nameof(GuardShortcutReads)));
        }

        private static IEnumerable<CodeInstruction> GuardShortcutReads(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(InputContext), nameof(InputContext.IsGameKeyReleased), new[] { typeof(int) });
            MethodInfo guarded = AccessTools.Method(typeof(ReignMapBarInputSafetyPatch), nameof(IsRegisteredGameKeyReleased));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    // Keep branch labels and exception blocks attached to the call.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = guarded;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced == 0)
                throw new InvalidOperationException("No native map-bar shortcut reads were found to guard.");
        }

        internal static bool IsRegisteredGameKeyReleased(InputContext inputContext, int gameKey)
        {
            var keys = inputContext == null ? null : RegisteredGameKeys.GetValue(inputContext) as List<GameKey>;
            if (keys == null || gameKey < 0 || gameKey >= keys.Count || keys[gameKey] == null)
                return false;

            // Preserve native bindings, release tracking, input restrictions and
            // navigation. Do not enable campaign shortcuts on a modal screen.
            return inputContext.IsGameKeyReleased(gameKey);
        }
    }
}
