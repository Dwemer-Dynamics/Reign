using System;
using System.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignRecruitedNotableCampaignBehavior
    {
        private static void ReleaseBusinessOwnership(Hero recruit)
        {
            // Resolve all recipients before transferring anything. Never give a
            // recruit's town businesses to the player or another clan member.
            var workshops = recruit.OwnedWorkshops.Where(w => w.Owner == recruit).ToDictionary(
                w => w, w => BusinessSuccessor(recruit, w.Settlement,
                    TaleWorlds.CampaignSystem.Campaign.Current.Models.WorkshopModel.GetNotableOwnerForWorkshop(w)));
            var caravans = recruit.OwnedCaravans.Where(c => c.Owner == recruit).ToDictionary(
                c => c.MobileParty, c => BusinessSuccessor(recruit, c.Settlement, null));

            int workshopCount = ReignBusinessOwnershipRelease.ReleaseAll(recruit.OwnedWorkshops,
                w => w.Owner == recruit, w => TransferWorkshop(recruit, w, workshops[w]));
            int caravanCount = ReignBusinessOwnershipRelease.ReleaseAll(
                recruit.OwnedCaravans.Select(c => c.MobileParty),
                p => p?.CaravanPartyComponent?.Owner == recruit,
                p => TransferCaravan(recruit, p, caravans[p]));
            if (workshopCount + caravanCount > 0)
                ReignLog.Info("Released recruited notable businesses hero=" + recruit.StringId
                    + " workshops=" + workshopCount + " caravans=" + caravanCount + " compensation=0");
        }

        private static bool EligibleBusinessSuccessor(Hero candidate, Hero recruit, Settlement home)
        {
            return candidate != null && candidate != recruit && candidate != Hero.MainHero
                && candidate.IsAlive && candidate.IsActive && candidate.IsNotable
                && !candidate.IsChild && !candidate.IsPrisoner && candidate.Clan == null
                && candidate.CompanionOf == null && candidate.PartyBelongedTo == null
                && home.Notables.Contains(candidate);
        }

        private static Hero BusinessSuccessor(Hero recruit, Settlement home, Hero preferred)
        {
            if (home?.Town == null)
                throw new InvalidOperationException("The recruited notable's business has no valid home town.");
            if (EligibleBusinessSuccessor(preferred, recruit, home)) return preferred;
            Hero successor = home.Notables.Where(h => EligibleBusinessSuccessor(h, recruit, home))
                .OrderBy(h => h.OwnedWorkshops.Count + h.OwnedCaravans.Count)
                .ThenByDescending(h => h.Power).ThenBy(h => h.StringId, StringComparer.Ordinal).FirstOrDefault();
            // A town whose final eligible notable was recruited still needs an
            // owner. Native creation supplies an ordinary merchant and its normal
            // settlement registration; no player-clan property or money is added.
            if (successor == null) successor = HeroCreator.CreateNotable(Occupation.Merchant, home);
            if (!EligibleBusinessSuccessor(successor, recruit, home))
                throw new InvalidOperationException("No eligible successor could be established for the town business.");
            return successor;
        }

        private static void TransferWorkshop(Hero recruit, Workshop workshop, Hero successor)
        {
            int capital = workshop.Capital;
            WorkshopType type = workshop.WorkshopType;
            // This native succession action preserves capital/type, pays zero
            // and emits only ownership change; it does not kill either hero.
            ChangeOwnerOfWorkshopAction.ApplyByDeath(workshop, successor);
            if (workshop.Owner != successor || recruit.OwnedWorkshops.Contains(workshop)
                || !successor.OwnedWorkshops.Contains(workshop)
                || workshop.Capital != capital || workshop.WorkshopType != type)
                throw new InvalidOperationException("Native workshop ownership transfer failed verification.");
        }

        private static void TransferCaravan(Hero recruit, MobileParty party, Hero successor)
        {
            if (party == null || party == MobileParty.MainParty || party.CaravanPartyComponent == null)
                throw new InvalidOperationException("The recruited notable's caravan is unavailable.");
            Settlement home = party.CaravanPartyComponent.Settlement;
            int tradeGold = party.PartyTradeGold;
            Hero leader = party.LeaderHero;
            CaravanPartyComponent.TransferCaravanOwnership(party, successor, home);
            party.ActualClan = successor.Clan;
            if (party.CaravanPartyComponent?.Owner != successor
                || recruit.OwnedCaravans.Any(c => c.MobileParty == party)
                || !successor.OwnedCaravans.Any(c => c.MobileParty == party)
                || party.PartyTradeGold != tradeGold || party.LeaderHero != leader)
                throw new InvalidOperationException("Native caravan ownership transfer failed verification.");
        }
    }
}
