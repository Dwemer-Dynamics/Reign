using System;
using System.Reflection;
using HarmonyLib;

namespace ReignBeta.Integration
{
    public static class ReignEncyclopediaSafetyPatch
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.encyclopedia.safety";
        private static bool _patched;
        private static FieldInfo _activeLayerField;

        public static void Apply()
        {
            if (_patched)
            {
                return;
            }

            try
            {
                Type encyclopediaDataType = Type.GetType("SandBox.GauntletUI.Encyclopedia.EncyclopediaData, SandBox.GauntletUI");
                MethodInfo onTick = encyclopediaDataType == null ? null : AccessTools.Method(encyclopediaDataType, "OnTick");
                MethodInfo prefix = AccessTools.Method(typeof(ReignEncyclopediaSafetyPatch), nameof(EncyclopediaDataOnTickPrefix));

                if (encyclopediaDataType == null || onTick == null || prefix == null)
                {
                    ReignLog.Warn("Encyclopedia safety patch skipped because EncyclopediaData.OnTick was not found.");
                    return;
                }

                _activeLayerField = AccessTools.Field(encyclopediaDataType, "_activeGauntletLayer");
                if (_activeLayerField == null)
                {
                    ReignLog.Warn("Encyclopedia safety patch skipped because _activeGauntletLayer was not found.");
                    return;
                }

                new Harmony(HarmonyId).Patch(onTick, prefix: new HarmonyMethod(prefix));
                _patched = true;
                ReignLog.Info("Installed encyclopedia null-layer safety patch.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Encyclopedia safety patch failed: " + ex.Message);
            }
        }

        private static bool EncyclopediaDataOnTickPrefix(object __instance)
        {
            try
            {
                if (__instance == null || _activeLayerField == null)
                {
                    return true;
                }

                object activeLayer = _activeLayerField.GetValue(__instance);
                if (activeLayer != null)
                {
                    return true;
                }

                ReignLog.Warn("Skipped EncyclopediaData.OnTick because _activeGauntletLayer is null.");
                return false;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Encyclopedia safety prefix failed: " + ex.Message);
                return true;
            }
        }
    }
}
