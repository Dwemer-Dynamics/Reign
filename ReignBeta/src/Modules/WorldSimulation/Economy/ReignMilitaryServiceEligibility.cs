using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace ReignBeta.Economy
{
    internal static class ReignMilitaryServiceEligibility
    {
        // Player-directed establishment and native initial/quest factories retain their own rules.
        internal static bool CanEstablishNpcParty(Hero hero)
        {
            if (hero == null) return false;
            var clan = hero.Clan;
            bool npcKingdom = clan?.Kingdom != null && clan != Clan.PlayerClan
                && clan.Kingdom.RulingClan != Clan.PlayerClan;
            return !npcKingdom || ((clan.IsUnderMercenaryService || clan.Fiefs.Count > 0) && hero.CanLeadParty());
        }

        internal static bool CanReceiveKingdomSupport(Clan clan)
        {
            if (clan?.Kingdom == null || clan.Fiefs.Count == 0) return false;
            return clan.WarPartyComponents.Any(component => IsServingParty(clan, component.MobileParty));
        }

        private static bool IsServingParty(Clan clan, MobileParty party)
        {
            var leader = party?.LeaderHero;
            // An empty army rebuilding its roster still serves; no minimum troop purchase is required.
            return ServingPartyFacts(party?.IsActive == true, party?.IsLordParty == true,
                party?.IsDisbanding == true, party?.ActualClan == clan,
                party?.MapFaction == clan.Kingdom, leader != null && leader.Clan == clan
                    && leader.IsAlive && !leader.IsPrisoner && leader.PartyBelongedTo == party);
        }

        internal static bool ServingPartyFacts(bool active, bool military, bool disbanding,
            bool sameClan, bool sameKingdom, bool availableLeader)
            => active && military && !disbanding && sameClan && sameKingdom && availableLeader;
    }
}
