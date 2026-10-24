using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using ReignBeta.Integration;

namespace ReignBeta.Campaign
{
    internal static class ReignHeroKnowledge
    {
        // Call only for a real player encounter on the campaign thread. Merely
        // listing a contact, reading a letter or previewing a UI is not a meeting.
        internal static void MarkMet(Hero hero)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null
                || hero == null || hero == Hero.MainHero) return;
            if (!ReignMainThread.IsMainThread)
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var player = Hero.MainHero;
                _ = ReignMainThread.InvokeAsync(() =>
                {
                    if (ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current)
                        && ReferenceEquals(player, Hero.MainHero)) MarkMet(hero);
                });
                return;
            }
            ApplyKnownState(hero.HasMet, hero.IsKnownToPlayer,
                () => hero.IsKnownToPlayer = true, hero.SetHasMet);
        }

        internal static void MarkMet(IEnumerable<Hero> heroes)
        {
            if (heroes == null) return;
            foreach (Hero hero in heroes) MarkMet(hero);
        }

        internal static void ApplyKnownState(bool hasMet, bool known, Action reveal, Action meet)
        {
            // Native SetHasMet does not set IsKnownToPlayer. Repair either flag
            // independently and preserve the original date for an existing meeting.
            if (!known) reveal();
            if (!hasMet) meet();
        }
    }
}
