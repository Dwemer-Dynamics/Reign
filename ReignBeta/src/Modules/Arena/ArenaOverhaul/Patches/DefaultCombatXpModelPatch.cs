using ArenaOverhaul.CampaignBehaviors.BehaviorManagers;
using ArenaOverhaul.ModSettings;

using HarmonyLib;

using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace ArenaOverhaul.Patches
{
    [HarmonyPatch(typeof(DefaultCombatXpModel))]
    public static class DefaultCombatXpModelPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetXpfMultiplierForMissionType")]
        public static bool GetXpfMultiplierForMissionTypePrefix(CombatXpModel.MissionTypeEnum missionType, ref float __result)
        {
            switch (missionType)
            {
                case CombatXpModel.MissionTypeEnum.Tournament:
                    __result = GetTournamentXPRate();
                    return false;
                case CombatXpModel.MissionTypeEnum.PracticeFight:
                    __result = GetPracticeFightXPRate();
                    return false;
                default:
                    return true;
            }
        }

        internal static float GetTournamentXPRate()
        {
            return Settings.Instance!.TournamentExperienceRate;
        }

        internal static float GetPracticeFightXPRate()
        {
            return AOArenaBehaviorManager.Instance!.GetPracticeExperienceRate();
        }
    }
}
