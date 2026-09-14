using ReignBeta.Settings;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Family
{
    internal static class ReignPregnancyRestrictionPolicy
    {
        internal const string RejectionMessage = "Pregnant NPCs are unavailable for party and combat duty until childbirth.";

        internal static bool IsEnabled
        {
            get
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                return settings == null || (settings.Enabled && settings.PregnancyCombatRestrictionsEnabled);
            }
        }

        internal static bool IsRestrictedNpc(Hero hero)
        {
            return IsEnabled
                && hero != null
                && hero != Hero.MainHero
                && hero.IsAlive
                && hero.IsPregnant;
        }

        internal static bool IsRestrictedAction(ReignWorldActionType type)
        {
            switch (type)
            {
                case ReignWorldActionType.StrategyRecruitAndRecover:
                case ReignWorldActionType.StrategyFormArmy:
                case ReignWorldActionType.StrategyAttackSettlement:
                case ReignWorldActionType.StrategyCaptureSettlement:
                case ReignWorldActionType.RegularFollowOnMap:
                case ReignWorldActionType.RegularStopFollowing:
                case ReignWorldActionType.RegularGoToSettlement:
                case ReignWorldActionType.RegularPatrolAroundSettlement:
                case ReignWorldActionType.RegularWaitNearSettlement:
                case ReignWorldActionType.RegularRaidVillage:
                case ReignWorldActionType.RegularBesiegeSettlement:
                case ReignWorldActionType.RegularCreateParty:
                case ReignWorldActionType.RegularAttackParty:
                case ReignWorldActionType.RegularAttackPlayerParty:
                case ReignWorldActionType.RegularSurrenderToPlayer:
                case ReignWorldActionType.RegularLeavePlayerAlone:
                case ReignWorldActionType.RegularKillCharacter:
                case ReignWorldActionType.RegularDuelPlayer:
                    return true;
                default:
                    return false;
            }
        }
    }
}
