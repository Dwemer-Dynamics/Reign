using System;
using System.Reflection;
using HarmonyLib;
using ReignBeta.Integration;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace ReignBeta.Campaign
{
    internal static class ReignWandererPatches
    {
        private const string Owner = "com.bannerlordreign.wanderer.population.v1";
        internal static bool Ready { get; private set; }
        internal static void Install()
        {
            if (Ready) return;
            var harmony = new Harmony(Owner);
            try
            {
                foreach (string method in new[] { "TryKillCompanion", "SwapCompanions", "TrySpawnNewCompanion", "CreateCompanionAndAddToSettlement" })
                    harmony.Patch(Required(typeof(CompanionsCampaignBehavior), method), prefix: new HarmonyMethod(typeof(ReignWandererPatches), nameof(NativePopulation)));
                harmony.Patch(Required(typeof(Hero), "CanDie"), postfix: new HarmonyMethod(typeof(ReignWandererPatches), nameof(CanDie)));
                harmony.Patch(Required(typeof(KillCharacterAction), "ApplyInternal"), prefix: new HarmonyMethod(typeof(ReignWandererPatches), nameof(Kill)));
                Ready = true;
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(Owner); Ready = false;
                ReignLog.Exception("Native wanderer hooks could not be installed; Reign population disabled", ex);
            }
        }

        private static MethodInfo Required(Type type, string name) => AccessTools.Method(type, name)
            ?? throw new MissingMethodException(type.FullName, name);
        private static bool NativePopulation() => ReignWandererPopulationCampaignBehavior.Instance?.OwnsPopulation != true;
        private static void CanDie(Hero __instance, KillCharacterAction.KillCharacterActionDetail causeOfDeath, ref bool __result)
        {
            if (!WandererPopulationRules.AllowsDeath(ReignWandererPopulationCampaignBehavior.Instance?.IsProtected(__instance) == true, causeOfDeath.ToString())) __result = false;
            if (causeOfDeath == KillCharacterAction.KillCharacterActionDetail.Lost
                && ReignWandererPopulationCampaignBehavior.Instance?.BlocksRemoval(__instance) == true) __result = false;
        }
        private static bool Kill(Hero victim, KillCharacterAction.KillCharacterActionDetail actionDetail)
        {
            // Forced Native cleanup also goes through this gate. Other heroes and natural old age remain untouched.
            if (!WandererPopulationRules.AllowsDeath(ReignWandererPopulationCampaignBehavior.Instance?.IsProtected(victim) == true, actionDetail.ToString())) return false;
            return actionDetail != KillCharacterAction.KillCharacterActionDetail.Lost
                || ReignWandererPopulationCampaignBehavior.Instance?.BlocksRemoval(victim) != true;
        }
    }
}
