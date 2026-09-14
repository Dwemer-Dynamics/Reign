using System;
using HarmonyLib;
using ReignBeta.Integration;
using ReignBeta.Settings;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace ReignBeta.Family
{
    internal static class ReignMarriagePatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.marriage";
        private static Harmony _harmony;

        public static void Apply()
        {
            if (_harmony != null) return;
            try
            {
                _harmony = new Harmony(HarmonyId);
                var method = AccessTools.Method(typeof(RomanceCampaignBehavior), "CheckNpcMarriages");
                if (method == null) throw new MissingMethodException(typeof(RomanceCampaignBehavior).FullName, "CheckNpcMarriages");
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(ReignMarriagePatches), nameof(CheckNpcMarriagesPrefix)));
                ReignLog.Info("Reign autonomous NPC marriage interception loaded.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Reign marriage interception failed: " + ex);
            }
        }

        public static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static bool CheckNpcMarriagesPrefix()
        {
            return ReignBetaSettings.Instance?.ReignControlledNpcMarriageEnabled == false;
        }
    }
}
