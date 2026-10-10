using TaleWorlds.CampaignSystem;

namespace ArenaOverhaul.Tournament
{
    internal static class TournamentHeroEligibility
    {
        public static bool IsAvailable(Hero hero) =>
            hero != null && !hero.IsDead && !hero.IsChild && !hero.IsWounded && !hero.IsPrisoner;

        public static bool CanParticipate(Hero hero, bool allowNotables)
        {
            if (!IsAvailable(hero))
                return false;

            // Recruitment can preserve a civilian's native occupation/noncombatant
            // flags. Confirmed companion membership takes precedence over those
            // flags, without admitting unrecruited residents or temporary guests.
            return hero.CompanionOf != null
                || (!hero.IsNoncombatant && (allowNotables || !hero.IsNotable));
        }
    }
}
