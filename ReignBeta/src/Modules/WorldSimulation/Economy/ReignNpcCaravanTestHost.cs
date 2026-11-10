using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Economy
{
    internal static class ReignNpcCaravanTestHost
    {
        // The caller enforces the exact paused armed Current and retained
        // recovery checkpoint. Native RNG, hero duty and counters require reload.
        internal static JObject Probe(JObject command)
        {
            var clan = Clan.All.SingleOrDefault(x => x.StringId == command.Value<string>("partyId"));
            var town = Settlement.Find(command.Value<string>("settlementId"));
            var state = ReignWarEconomyTransactions.State.CaravanInvestments;
            if (!ReignNpcCaravanInvestments.CanLaunchAt(clan, town) || state.Active.Count != 0 || state.Pending != null)
                throw new InvalidOperationException("The exact NPC clan and friendly town must be ready on a clean trade test branch.");
            bool convoy = town.HasPort;
            var options = ReignWarEconomyPolicy.Options;
            if (!options.NpcTradeEnabled(convoy))
                throw new InvalidOperationException("Enable the corresponding NPC Clan Trade option for the test.");

            var bandit = MobileParty.All.Where(p => p.IsActive && p.IsBandit && p.MapEvent == null
                    && p.HasNavalNavigationCapability == convoy)
                .OrderBy(p => p.StringId, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("An existing active native bandit party is required.");
            var owner = clan.Leader;
            int fee = ReignNpcCaravanInvestments.Fee(owner, convoy);
            int capital = ReignNpcCaravanInvestments.InitialTradeGold(convoy);
            long rejectedCash = (long)fee * 100 / options.NpcTradeCashPercent;
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            double fundedAmount = Math.Max(rejectedCash + 1, fee + Math.Max(ReignWarEconomyPolicy.HouseholdStipendPayerFloor,
                Math.Ceiling(7 * ReignWarEconomyTransactions.ClanWages(clan)) + clan.AutoRecruitmentExpenses));
            if (fundedAmount > 250000 || fee <= 0 || capital <= 0 || town.Town.Gold > int.MaxValue - fee)
                throw new InvalidOperationException("The payer and price are outside the fixture bounds.");

            int originalOwner = owner.Gold;
            int originalMarket = town.Town.Gold;
            var hearths = Village.All.ToDictionary(x => x, x => x.Hearth);
            var warParties = clan.WarPartyComponents.Count;
            var heroDuties = clan.Heroes.ToDictionary(hero => hero, hero => hero.PartyBelongedTo);
            MobileParty created = null;
            try
            {
                owner.Gold = (int)rejectedCash;
                bool cashShareBlocked = ReignNpcCaravanInvestments.TryLaunch(clan, town, day) == null
                    && owner.Gold == rejectedCash && town.Town.Gold == originalMarket && state.Pending == null;
                if (!cashShareBlocked) throw new InvalidOperationException("The cash-share refusal changed native state.");

                owner.Gold = (int)fundedAmount;
                bool abortRestored = ReignNpcCaravanInvestments.TryLaunch(clan, town, day, exerciseRollback: true) == null
                    && owner.Gold == fundedAmount && town.Town.Gold == originalMarket && state.Pending == null
                    && state.Recent.LastOrDefault()?.Status == "rolled_back" && state.Active.Count == 0;
                if (!abortRestored) throw new InvalidOperationException("Native partial-formation rollback did not reconcile.");

                created = ReignNpcCaravanInvestments.TryLaunch(clan, town, day);
                if (created == null || !state.Active.TryGetValue(created.StringId, out var receipt))
                    throw new InvalidOperationException("Funded native caravan formation failed: " + state.LastError);
                bool accounted = receipt.WalletBefore - owner.Gold == fee && !receipt.CapitalFundedBySponsor
                    && town.Town.Gold - originalMarket == fee && created.PartyTradeGold == capital
                    && created.CaravanPartyComponent.Owner == owner && created.CaravanPartyComponent.CanHaveNavalNavigationCapability == convoy;
                bool merchantCrew = created.LeaderHero == null && created.MemberRoster.GetTroopCount(owner.Culture.CaravanMaster) > 0
                    && heroDuties.All(x => x.Key.PartyBelongedTo == x.Value);
                bool duplicateBlocked = ReignNpcCaravanInvestments.TryLaunch(clan, town, day) == null;
                int beforeLoss = owner.Gold;
                string id = created.StringId;
                DestroyPartyAction.Apply(bandit.Party, created);
                bool noRefund = owner.Gold == beforeLoss && !MobileParty.All.Contains(created)
                    && state.Active.Count == 0 && receipt.Status == "lost" && receipt.Destroyer == bandit.StringId
                    && state.AvailableDay.TryGetValue(clan.StringId, out int available)
                    && available == day + ReignWarEconomyPolicy.CaravanDefeatCooldownDays;
                bool nonMilitary = clan.WarPartyComponents.Count == warParties && hearths.All(x => x.Key.Hearth == x.Value);
                return new JObject { ["ok"] = cashShareBlocked && abortRestored && accounted && merchantCrew
                        && duplicateBlocked && noRefund && nonMilitary,
                    ["formationPricingVersion"] = 2, ["settlementId"] = town.StringId, ["clanId"] = clan.StringId, ["convoy"] = convoy,
                    ["landless"] = clan.Fiefs.Count == 0, ["nativePartyId"] = id, ["banditPartyId"] = bandit.StringId,
                    ["cashSharePercent"] = options.NpcTradeCashPercent, ["rejectedCash"] = rejectedCash,
                    ["cashShareRejected"] = cashShareBlocked, ["partialCreationRolledBack"] = abortRestored,
                    ["singleChargeAndNativeSeed"] = accounted, ["genericCaravanMaster"] = merchantCrew,
                    ["duplicateBlocked"] = duplicateBlocked,
                    ["nativeDestructionNoRefund"] = noRefund, ["zeroWarPartyAndHearthEffects"] = nonMilitary,
                    ["receipt"] = JObject.FromObject(receipt), ["fullCurrentRestoreRequired"] = true,
                    ["combatOrCaptivityCertified"] = false, ["providerCalls"] = 0 };
            }
            finally
            {
                if (created?.IsActive == true) DestroyPartyAction.Apply(null, created);
                owner.Gold = originalOwner;
                town.Town.ChangeGold(originalMarket - town.Town.Gold);
            }
        }
    }
}
