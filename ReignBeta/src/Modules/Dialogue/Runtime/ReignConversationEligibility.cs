using System;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Runtime
{
    /// <summary>
    /// Shared eligibility rules for every player-facing Reign conversation surface.
    /// Bannerlord does not expose child heroes through normal player conversation,
    /// so automation and custom UIs must preserve the same invariant.
    /// </summary>
    public static class ReignConversationEligibility
    {
        private const float FallbackAdultAge = 18f;

        public static float AdultAgeThreshold
        {
            get
            {
                try
                {
                    return TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? FallbackAdultAge;
                }
                catch
                {
                    return FallbackAdultAge;
                }
            }
        }

        public static bool IsAdult(Hero hero)
        {
            return hero != null
                && !hero.IsChild
                && !float.IsNaN(hero.Age)
                && hero.Age >= AdultAgeThreshold;
        }

        public static bool IsAdultLivingNpc(Hero hero)
        {
            return hero != null
                && hero != Hero.MainHero
                && hero.IsAlive
                && !string.IsNullOrWhiteSpace(hero.StringId)
                && IsAdult(hero);
        }

        public static bool TryValidateAdultConversationHero(Hero hero, out string reason)
        {
            if (hero == null)
            {
                reason = "No conversation hero was found.";
                return false;
            }

            if (hero == Hero.MainHero)
            {
                reason = "The player cannot be selected as an NPC conversation target.";
                return false;
            }

            if (!hero.IsAlive)
            {
                reason = "The selected character is not alive.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(hero.StringId))
            {
                reason = "The selected character has no stable native identity.";
                return false;
            }

            if (!IsAdult(hero))
            {
                reason = "Child characters cannot be selected for player-facing conversations.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
