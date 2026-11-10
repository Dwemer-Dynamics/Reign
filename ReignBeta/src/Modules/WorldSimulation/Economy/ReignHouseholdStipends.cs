using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Economy
{
    internal static class ReignHouseholdStipends
    {
        internal static void Tick(EconomyHouseholdStipendState state, double day)
        {
            if (state == null || NativeCampaign.Current?.GameStarted != true) return;
            int week = (int)Math.Floor(day / ReignWarEconomyPolicy.HouseholdStipendDays);
            bool due = state.BeginWeek(week);
            var candidates = new List<(Clan Clan, Settlement Home)>();
            foreach (var clan in Clan.NonBanditFactions)
            {
                var home = PermanentHome(clan);
                if (home == null) continue;
                var tenure = state.ObserveOwner(home.StringId, home.OwnerClan?.StringId ?? "", day);
                if (due && tenure.HasFullWeek(day) && IsAvailableLeader(clan)
                    && clan.Gold < ReignWarEconomyPolicy.HouseholdStipendLeaderLimit)
                    candidates.Add((clan, home));
            }

            if (!due) return;
            state.LastError = "";
            var paidByHost = new Dictionary<Clan, int>();
            foreach (var candidate in candidates.OrderBy(x => x.Clan.Gold)
                .ThenBy(x => x.Clan.StringId, StringComparer.Ordinal))
            {
                Clan recipient = candidate.Clan;
                Settlement home = candidate.Home;
                Clan host = home.OwnerClan;
                if (host == null) continue;
                paidByHost.TryGetValue(host, out int alreadyPaid);
                int amount = Pay(state, week, day, home, host, recipient, alreadyPaid);
                if (amount < 0) break;
                if (amount > 0) paidByHost[host] = alreadyPaid + amount;
            }
        }

        private static int Pay(EconomyHouseholdStipendState state, int week, double day, Settlement home,
            Clan host, Clan recipient, int alreadyPaid)
        {
            if (host == recipient || host.IsEliminated || host.Fiefs.Count == 0 || home.OwnerClan != host
                || host.Kingdom != recipient.Kingdom || !IsAvailableLeader(host) || !IsAvailableLeader(recipient)
                || (host == Clan.PlayerClan && ReignBetaSettings.Instance?.FundLocalHouseholdStipends != true)
                || !state.ObserveOwner(home.StringId, host.StringId, day).HasFullWeek(day))
                return 0;

            long householdGold = recipient.Heroes.Where(x => x.IsAlive).Sum(x => (long)x.Gold);
            if (householdGold >= ReignWarEconomyPolicy.HouseholdStipendFamilyLimit) return 0;
            int amount = ReignWarEconomyPolicy.HouseholdStipendAmount(recipient.Gold, householdGold,
                host.Gold, ReignWarEconomyTransactions.ClanWages(host), host.AutoRecruitmentExpenses, alreadyPaid);
            if (amount == 0) return 0;
            return Transfer(state, week, home, host, recipient, amount) ? amount : -1;
        }

        internal static JObject Probe(JObject command)
        {
            var recipient = Clan.All.SingleOrDefault(x => x.StringId == command.Value<string>("partyId"));
            var home = PermanentHome(recipient);
            var host = home?.OwnerClan;
            if (home == null || home.StringId != command.Value<string>("settlementId")
                || host == null || host == Clan.PlayerClan || host.Kingdom != recipient.Kingdom
                || !IsAvailableLeader(host) || !IsAvailableLeader(recipient))
                throw new InvalidOperationException("An exact same-realm NPC stipend household and permanent home are required.");

            var members = recipient.Heroes.Where(x => x.IsAlive).ToList();
            var other = members.FirstOrDefault(h => h != recipient.Leader);
            if (members.Count > 20 || other == null || !members.Contains(recipient.Leader))
                throw new InvalidOperationException("Stipend fixture needs 2–20 living household members.");
            var original = members.ToDictionary(h => h, h => h.Gold);
            int hostGold = host.Gold;
            int treasury = host.Kingdom.KingdomBudgetWallet;
            double reserve = Math.Max(ReignWarEconomyPolicy.HouseholdStipendPayerFloor,
                Math.Ceiling(7 * ReignWarEconomyTransactions.ClanWages(host)) + host.AutoRecruitmentExpenses);
            if (reserve > 249000) throw new InvalidOperationException("Stipend fixture reserve exceeds its bounds.");

            int week = (int)Math.Floor(CampaignTime.Now.ToDays / 7);
            double day = CampaignTime.Now.ToDays;
            var state = new EconomyHouseholdStipendState();
            state.BeginWeek(week - 1);
            try
            {
                foreach (var h in members) h.Gold = 0;
                recipient.Leader.Gold = 2000;
                host.Leader.Gold = (int)reserve + 1000;
                state.ObserveOwner(home.StringId, host.StringId, day - 6);
                bool recentOwnerBlocked = Pay(state, week, day, home, host, recipient, 0) == 0;
                state.Ownership.Clear();
                state.ObserveOwner(home.StringId, host.StringId, day - 7);
                other.Gold = 13000;
                bool familyBlocked = Pay(state, week, day, home, host, recipient, 0) == 0;
                other.Gold = 0;
                host.Leader.Gold = (int)reserve;
                bool reserveBlocked = Pay(state, week, day, home, host, recipient, 0) == 0;
                host.Leader.Gold = (int)reserve + 1000;
                bool paid = state.BeginWeek(week) && Pay(state, week, day, home, host, recipient, 0) == 140;
                bool duplicateBlocked = !state.BeginWeek(week);
                bool conserved = host.Leader.Gold == (int)reserve + 860 && recipient.Leader.Gold == 2140
                    && state.Payments == 1 && host.Kingdom.KingdomBudgetWallet == treasury;
                return new JObject { ["ok"] = recentOwnerBlocked && familyBlocked && reserveBlocked && paid && duplicateBlocked && conserved,
                    ["recipientClanId"] = recipient.StringId, ["settlementId"] = home.StringId,
                    ["recentOwnerBlocked"] = recentOwnerBlocked, ["familyWealthBlocked"] = familyBlocked,
                    ["reserveBlocked"] = reserveBlocked, ["duplicateWeekBlocked"] = duplicateBlocked,
                    ["walletsConserved"] = conserved, ["receipt"] = JArray.FromObject(state.Recent),
                    ["fullCurrentRestoreRequired"] = true, ["providerCalls"] = 0 };
            }
            finally
            {
                foreach (var row in original) row.Key.Gold = row.Value;
                host.Leader.Gold = hostGold;
            }
        }

        private static Settlement PermanentHome(Clan clan)
        {
            if (clan == null || clan == Clan.PlayerClan || clan.IsEliminated || !clan.IsNoble
                || clan.IsMinorFaction || clan.IsUnderMercenaryService || clan.Kingdom == null || clan.Fiefs.Count != 0)
                return null;

            var original = clan.InitialHomeSettlement;
            if (original == null) return null;
            // Reign court houses use their authored home. A native clan must still
            // use its original home, since the native fallback can change after conquest.
            bool authored = clan.StringId == "reign_house_" + original.StringId;
            if (!authored && clan.HomeSettlement != original) return null;
            var holding = original.IsVillage ? original.Village?.Bound : original;
            return holding?.Town != null ? holding : null;
        }

        private static bool IsAvailableLeader(Clan clan)
            => clan.Leader is Hero leader && leader.Clan == clan && leader.IsAlive && leader.IsActive && !leader.IsPrisoner;

        internal static void OwnershipChanged(EconomyHouseholdStipendState state, Settlement settlement, double day)
        {
            if (settlement?.Town == null || state?.Ownership.ContainsKey(settlement.StringId) != true) return;
            state.ObserveOwner(settlement.StringId, settlement.OwnerClan?.StringId ?? "", day);
        }

        private static bool Transfer(EconomyHouseholdStipendState state, int week, Settlement home,
            Clan host, Clan recipient, int amount)
        {
            Hero payer = host.Leader;
            Hero payee = recipient.Leader;
            int payerBefore = payer.Gold;
            int payeeBefore = payee.Gold;
            try
            {
                // The native gold setters are synchronous. A direct paired transfer
                // also leaves relations, influence and notification state untouched.
                payer.Gold = payerBefore - amount;
                payee.Gold = payeeBefore + amount;
                if (payerBefore - payer.Gold != amount || payee.Gold - payeeBefore != amount)
                    throw new InvalidOperationException("Household stipend gold change did not conserve the transfer.");
            }
            catch (Exception ex)
            {
                payer.Gold = payerBefore;
                payee.Gold = payeeBefore;
                state.LastError = ex.Message;
                return false;
            }

            state.Record(new EconomyStipendReceipt { Week = week, Amount = amount, Settlement = home.StringId,
                PayerClan = host.StringId, RecipientClan = recipient.StringId, PayerHero = payer.StringId,
                RecipientHero = payee.StringId, PayerBefore = payerBefore, PayerAfter = payer.Gold,
                RecipientBefore = payeeBefore, RecipientAfter = payee.Gold });
            return true;
        }

        internal static JObject Diagnostics(EconomyHouseholdStipendState state, bool detailed) => new JObject
        {
            ["version"] = state?.Version ?? 0,
            ["lastWeek"] = state?.LastWeek ?? -1,
            ["paid"] = state?.Paid ?? 0,
            ["payments"] = state?.Payments ?? 0,
            ["lastError"] = state?.LastError ?? "",
            ["recent"] = detailed && state != null ? JArray.FromObject(state.Recent) : new JArray()
        };
    }
}
