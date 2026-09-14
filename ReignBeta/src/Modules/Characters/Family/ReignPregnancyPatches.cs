using System;
using HarmonyLib;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;

namespace ReignBeta.Family
{
    internal static class ReignPregnancyPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.family";
        private static Harmony _harmony;

        public static void Apply()
        {
            if (_harmony != null) return;
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(AccessTools.Method(typeof(PregnancyCampaignBehavior), "RefreshSpouseVisit"), prefix: new HarmonyMethod(typeof(ReignPregnancyPatches), nameof(RefreshSpouseVisitPrefix)));
                _harmony.Patch(AccessTools.Method(typeof(PregnancyCampaignBehavior), "CheckOffspringsToDeliver"), prefix: new HarmonyMethod(typeof(ReignPregnancyPatches), nameof(CheckOffspringsToDeliverPrefix)));
                _harmony.Patch(AccessTools.Method(typeof(HeroSpawnCampaignBehavior), "SpawnLordParty"), prefix: new HarmonyMethod(typeof(ReignPregnancyPatches), nameof(SpawnLordPartyPrefix)));
                _harmony.Patch(AccessTools.Method(typeof(MobileParty), nameof(MobileParty.ChangePartyLeader)), prefix: new HarmonyMethod(typeof(ReignPregnancyPatches), nameof(ChangePartyLeaderPrefix)));
                ReignLog.Info("Reign pregnancy interception patches loaded.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Reign pregnancy interception failed: " + ex);
            }
        }

        public static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static bool RefreshSpouseVisitPrefix(Hero hero)
        {
            Hero player = Hero.MainHero;
            return player == null || (hero != player && hero?.Spouse != player);
        }

        private static bool CheckOffspringsToDeliverPrefix(Hero hero)
        {
            return ReignFamilyCampaignBehavior.Instance == null || !ReignFamilyCampaignBehavior.Instance.IsManagedPregnancy(hero);
        }

        private static bool SpawnLordPartyPrefix(Hero hero, ref MobileParty __result)
        {
            if (!ReignPregnancyRestrictionPolicy.IsRestrictedNpc(hero))
            {
                return true;
            }

            __result = null;
            ReignLog.Info("Pregnancy restriction blocked native lord-party spawn for " + hero.StringId + ".");
            return false;
        }

        private static bool ChangePartyLeaderPrefix(Hero __0)
        {
            if (!ReignPregnancyRestrictionPolicy.IsRestrictedNpc(__0))
            {
                return true;
            }

            ReignLog.Info("Pregnancy restriction blocked party-leader assignment for " + __0.StringId + ".");
            return false;
        }
    }
}
