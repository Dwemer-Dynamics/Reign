using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Economy
{
    // All mutations run synchronously on Bannerlord's campaign thread. Quotes never debit resources.
    internal static class ReignWarEconomyTransactions
    {
        internal const string Player = "player_volunteer", Ai = "ai_volunteer", Garrison = "native_auto_garrison",
            BaseGarrison = "native_base_garrison", NavalGarrison = "native_naval_garrison", Guard = "castle_guard", Retinue = "household_retinue", Militia = "militia_conversion";
        internal static ReignEconomyState State => ReignEconomyCampaignBehavior.Instance?.EconomyState;
        private static ReignEconomyState _generationState;
        private static readonly Dictionary<MobileParty, string> Unregistered = new Dictionary<MobileParty, string>(ReignWarEconomyReferenceComparer<MobileParty>.Instance);
        private static void EnsureGenerationState()
        {
            if (ReferenceEquals(_generationState, State)) return;
            Unregistered.Clear();
            _generationState = State;
        }
        internal static string Generation(MobileParty party)
        {
            if (party == null) return null;
            EnsureGenerationState();
            string native = party.Id.ToString();
            if (native != "0") return native;
            if (!Unregistered.TryGetValue(party, out string provisional))
                Unregistered.Add(party, provisional = ReignWarEconomyLedger.ProvisionalGeneration(State));
            return provisional;
        }
        internal static string GenerationKey(MobileParty party) => party?.StringId + ":" + Generation(party);
        internal static void OnPartyRegistered(MobileParty party)
        {
            EnsureGenerationState();
            if (!Unregistered.TryGetValue(party, out string provisional)) return;
            ReignWarEconomyLedger.RegisterGeneration(State, party.StringId, provisional, party.Id.ToString());
            Unregistered.Remove(party);
        }
        internal static void OnPartyCreationFailed(MobileParty party)
        {
            EnsureGenerationState();
            Unregistered.Remove(party);
        }
        internal static void SeedExistingGenerations()
        {
            EnsureGenerationState();
            foreach (var party in MobileParty.All.Where(x => x.IsLordParty && x != MobileParty.MainParty))
            {
                if (party.Id.ToString() == "0") throw new InvalidOperationException("Registered party has zero generation.");
                string key = GenerationKey(party);
                if (!State.PartyGenerations.ContainsKey(key)) State.PartyGenerations[key] = "existing_at_load";
            }
        }
        internal static EconomySourceTotals Totals(string source)
        {
            var state = State;
            if (state == null) return new EconomySourceTotals();
            if (!state.Sources.TryGetValue(source, out var totals)) state.Sources[source] = totals = new EconomySourceTotals();
            return totals;
        }
        internal static IEnumerable<MobileParty> ClanMilitaryParties(Clan clan) => clan == null
            ? Enumerable.Empty<MobileParty>()
            : MobileParty.All.Where(x => x.IsActive && x.IsLordParty && x.ActualClan == clan)
                .Concat(clan.Fiefs.Select(x => x.GarrisonParty).Where(x => x?.IsActive == true)).Distinct();
        internal static double ClanWages(Clan clan) => ClanMilitaryParties(clan).Sum(x => Math.Max(0, x.TotalWage));
        internal static JObject Stress(Kingdom kingdom)
        {
            var holdings = kingdom.Fiefs.Where(x => x?.Settlement != null).ToList();
            var villages = holdings.SelectMany(x => x.Settlement.BoundVillages).Distinct().ToList();
            double capacity = holdings.Sum(x => Math.Max(0, x.FoodStocksUpperLimit()));
            double food = capacity <= 0 ? 0 : holdings.Where(x => x.FoodStocks + 7 * x.FoodChange < .1 * x.FoodStocksUpperLimit())
                .Sum(x => (double)Math.Max(0, x.FoodStocksUpperLimit())) / capacity;
            double recruitment = villages.Count == 0 ? 1 : villages.Average(x => 1 - ReignWarEconomyPolicy.Clamp(
                ReignWarEconomyPolicy.Production(x.Hearth, ReignEconomyCampaignBehavior.Instance?.GetRecoveryModifier(x) ?? 1,
                    x.VillageState == Village.VillageStates.Normal) / 12, 0, 1));
            double wages = 0, unpaid = 0, unfunded = 0;
            foreach (var clan in kingdom.Clans.Where(x => x != null && !x.IsEliminated))
            {
                var parties = ClanMilitaryParties(clan).ToList();
                double owed = parties.Sum(x => (double)Math.Max(0, x.TotalWage));
                wages += owed;
                unpaid += parties.Sum(x => Math.Max(0, x.TotalWage) * ReignWarEconomyPolicy.Clamp(x.HasUnpaidWages, 0, 1));
                if (clan.Gold - clan.AutoRecruitmentExpenses < 7 * owed) unfunded += owed;
            }
            double unpaidShare = wages <= 0 ? 0 : unpaid / wages, unfundedShare = wages <= 0 ? 0 : unfunded / wages;
            return new JObject { ["schemaVersion"] = ReignWarEconomyPolicy.Version, ["observedDay"] = CampaignTime.Now.ToDays,
                ["food"] = food, ["recruitment"] = recruitment, ["unpaidWageShare"] = unpaidShare,
                ["unfundedWageShare"] = unfundedShare, ["financial"] = Math.Max(unpaidShare, unfundedShare),
                ["maximum"] = ReignWarEconomyPolicy.Stress(food, recruitment, unpaidShare, unfundedShare),
                ["wagesOwed"] = wages, ["wagesPaidAtLastNativePaymentRate"] = wages - unpaid };
        }

        internal static void ObserveDailyResources()
        {
            var state = State;
            if (state == null) return;
            foreach (var clan in Clan.All.Where(x => !x.IsEliminated))
            {
                if (state.LastClanCash.TryGetValue(clan.StringId, out double previous)) state.ClanCashChange[clan.StringId] = clan.Gold - previous;
                state.LastClanCash[clan.StringId] = clan.Gold;
            }
            foreach (var village in Settlement.All.Where(x => x.Village != null).Select(x => x.Village))
            {
                if (state.LastVillageHearth.TryGetValue(village.StringId, out double previous)) state.VillageHearthChange[village.StringId] = village.Hearth - previous;
                state.LastVillageHearth[village.StringId] = village.Hearth;
            }
            if (state.PartyGenerations.Count > 8192)
            {
                var live = new HashSet<string>(MobileParty.All.Select(x => x.StringId + ":" + x.Id), StringComparer.Ordinal);
                foreach (string key in state.PartyGenerations.Keys.Where(x => !live.Contains(x)).OrderBy(x => x, StringComparer.Ordinal)
                    .Take(state.PartyGenerations.Count - 8192).ToList()) state.PartyGenerations.Remove(key);
            }
        }
        internal static int Quote(CharacterObject troop, Hero payer) => troop == null ? 0 : Math.Max(0,
            TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(troop, payer).RoundedResultNumber);
        internal static double AdditionalWages(MobileParty party, CharacterObject recruit, int count, CharacterObject upgradedFrom = null)
        {
            var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel;
            if (party == null) return count * Math.Max(0, ReignWarEconomyOptions.BaseTroopWage(recruit));
            var roster = TroopRoster.CreateDummyTroopRoster();
            foreach (var element in party.MemberRoster.GetTroopRoster())
                roster.AddToCounts(element.Character, element.Number, false, element.WoundedNumber, element.Xp);
            int before = (int)model.GetTotalWage(party, roster).ResultNumber;
            if (upgradedFrom != null) roster.AddToCounts(upgradedFrom, -count);
            roster.AddToCounts(recruit, count);
            return Math.Max(0, (int)model.GetTotalWage(party, roster).ResultNumber - before);
        }
        internal static int Allow(string source, Settlement locality, MobileParty party, Hero payer, CharacterObject troop,
            int requested, bool automatic, bool liability = false, bool recordBlocked = true)
        {
            var behavior = ReignEconomyCampaignBehavior.Instance;
            int allowed = behavior == null || locality == null || troop == null || payer?.Clan == null || locality.IsUnderSiege
                || State.Pending.Any(x => x.Party == party?.StringId)
                || (party != null && State.DrawdownPending.Any(x => x.Party == party.StringId && x.Generation == Generation(party)))
                || (automatic && !ReignBeta.World.ReignCampaignCommandBehavior.RecruitmentFitsActiveOrder(party, troop))
                ? 0 : Math.Min(Math.Max(0, requested), behavior.GetRecruitCapacity(locality));
            if (allowed > 0)
            {
                if (automatic)
                {
                    int room = ReignMilitaryReadiness.RecruitmentRoom(party);
                    if (room < allowed) State.ReadinessRecruitmentBlocks += allowed - room;
                    allowed = Math.Min(allowed, room);
                }
                var clan = payer.Clan;
                allowed = ReignWarEconomyPolicy.AffordableCountExact(allowed, liability ? clan.Gold : payer.Gold, clan.Gold,
                    automatic ? ClanWages(clan) + (party != null && party.Id.ToString() == "0" ? Math.Max(0, party.TotalWage) : 0) : 0,
                    automatic ? clan.AutoRecruitmentExpenses : 0, Quote(troop, payer),
                    automatic ? ReignWarEconomyPolicy.Options.ReserveDays(false) : 0, count => automatic ? AdditionalWages(party, troop, count) : 0);
                if (party != null)
                {
                    allowed = Math.Min(allowed, Math.Max(0, party.Party.PartySizeLimit - party.MemberRoster.TotalManCount));
                    if (automatic && party.HasLimitedWage())
                        allowed = ReignWarEconomyOptions.BudgetCount(troop, party, allowed, party.GetAvailableWageBudget());
                }
            }
            if (recordBlocked)
            {
                Totals(source).Blocked += Math.Max(0, requested - allowed);
                behavior?.RecordRecruitmentLimit(requested, allowed);
            }
            return allowed;
        }
        internal static EconomyReceipt Begin(string source, Settlement locality, MobileParty party, Hero payer, CharacterObject troop, int requested, Hero recruitedFrom = null)
        {
            var state = State;
            if (state == null) return null;
            var receipt = new EconomyReceipt { Id = (++state.NextOperation).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Source = source, Settlement = locality?.StringId, Party = party?.StringId, Troop = troop?.StringId, Payer = payer?.StringId,
                Requested = requested, BeforeRoster = troop == null ? party?.MemberRoster.TotalRegulars ?? 0 : party?.MemberRoster.GetTroopCount(troop) ?? 0,
                BeforeGold = payer?.Gold ?? 0, BeforeLiability = payer?.Clan?.AutoRecruitmentExpenses ?? 0,
                QuotedCost = Quote(troop, payer), Day = CampaignTime.Now.ToDays, Status = "pending" };
            receipt.PartyGeneration = Generation(party);
            if (party != null)
                foreach (var element in party.MemberRoster.GetTroopRoster().Where(x => !x.Character.IsHero))
                    receipt.Roster[element.Character.StringId] = new[] { element.Number, element.WoundedNumber, element.Xp };
            var notables = ((IEnumerable<Hero>)locality?.Notables ?? Enumerable.Empty<Hero>()).ToList();
            if (recruitedFrom != null && !notables.Contains(recruitedFrom)) notables.Add(recruitedFrom);
            if (payer != null) receipt.Gold[payer.StringId] = payer.Gold;
            foreach (var notable in notables)
            {
                receipt.Gold[notable.StringId] = notable.Gold;
                receipt.Volunteers[notable.StringId] = notable.VolunteerTypes.Select(x => x?.StringId).ToArray();
            }
            foreach (var village in ReignEconomyCampaignBehavior.Instance.GetEligibleVillages(locality))
                receipt.Population[village.StringId] = new[] { village.Hearth, ReignEconomyCampaignBehavior.Instance.GetMobilizationStrain(village) };
            state.Pending.Add(receipt);
            return receipt;
        }
        internal static void Complete(EconomyReceipt receipt, Settlement locality, MobileParty party, Hero payer,
            CharacterObject troop, bool addLiability = false)
        {
            var state = State;
            if (receipt == null || state == null || receipt.Status != "pending" || !state.Pending.Contains(receipt)) return;
            int after = troop == null ? party?.MemberRoster.TotalRegulars ?? 0 : party?.MemberRoster.GetTroopCount(troop) ?? 0;
            int enlisted = Math.Max(0, after - receipt.BeforeRoster);
            if (enlisted > receipt.Requested || enlisted > ReignEconomyCampaignBehavior.Instance.GetRecruitCapacity(locality))
            {
                Rollback(receipt, "resources changed before recruitment committed");
                return;
            }
            if (enlisted > 0)
            {
                int charged = ReignEconomyCampaignBehavior.Instance.ConsumeRecruitHearth(locality, enlisted);
                if (charged != enlisted) throw new InvalidOperationException("Population conservation mismatch " + receipt.Id);
                if (addLiability && payer?.Clan != null)
                    payer.Clan.AutoRecruitmentExpenses += receipt.QuotedCost * enlisted;
            }
            ReignWarEconomyLedger.Finish(state, receipt, enlisted, enlisted * ReignWarEconomyPolicy.HearthPerRecruit,
                Math.Max(0, receipt.BeforeGold - (payer?.Gold ?? receipt.BeforeGold)),
                Math.Max(0, (payer?.Clan?.AutoRecruitmentExpenses ?? receipt.BeforeLiability) - receipt.BeforeLiability));
        }

        internal static void Rollback(EconomyReceipt receipt, string reason)
        {
            var state = State;
            if (state == null || !ReignWarEconomyLedger.IsPending(state, receipt)) return;
            EnsureGenerationState();
            var party = MobileParty.All.FirstOrDefault(x => x.StringId == receipt.Party && x.Id.ToString() == receipt.PartyGeneration)
                ?? Unregistered.FirstOrDefault(x => x.Key.StringId == receipt.Party && x.Value == receipt.PartyGeneration).Key;
            if (party == null) throw new InvalidOperationException("Cannot reconcile recruitment party generation " + receipt.Party);
            // These before-images cover only the synchronous enlistment operation, never another day's resources.
            foreach (var element in party.MemberRoster.GetTroopRoster().Where(x => !x.Character.IsHero).ToList())
            {
                int[] before = receipt.Roster.TryGetValue(element.Character.StringId, out var value) ? value : new int[3];
                party.MemberRoster.AddToCounts(element.Character, before[0] - element.Number, false,
                    before[1] - element.WoundedNumber, before[2] - element.Xp);
            }
            foreach (var pair in receipt.Roster.Where(x => party.MemberRoster.GetTroopCount(CharacterObject.Find(x.Key)) == 0 && x.Value[0] > 0))
                party.MemberRoster.AddToCounts(CharacterObject.Find(pair.Key), pair.Value[0], false, pair.Value[1], pair.Value[2]);
            foreach (var pair in receipt.Gold)
            {
                var hero = Hero.FindFirst(x => x.StringId == pair.Key) ?? throw new InvalidOperationException("Missing receipt payer " + pair.Key);
                hero.ChangeHeroGold(pair.Value - hero.Gold);
            }
            foreach (var pair in receipt.Volunteers)
            {
                var notable = Hero.FindFirst(x => x.StringId == pair.Key) ?? throw new InvalidOperationException("Missing receipt notable " + pair.Key);
                for (int i = 0; i < pair.Value.Length; i++) notable.VolunteerTypes[i] = pair.Value[i] == null ? null : CharacterObject.Find(pair.Value[i]);
            }
            var payer = Hero.FindFirst(x => x.StringId == receipt.Payer);
            if (payer?.Clan != null) payer.Clan.AutoRecruitmentExpenses = receipt.BeforeLiability;
            foreach (var pair in receipt.Population)
                ReignEconomyCampaignBehavior.Instance.RestorePopulation(pair.Key, pair.Value[0], pair.Value[1]);
            state.LastError = "Rolled back " + receipt.Source + "/" + receipt.Id + ": " + reason;
            ReignWarEconomyLedger.Finish(state, receipt, 0, 0, 0, 0, "rolled_back");
        }

        internal static void ReconcilePending()
        {
            foreach (var receipt in State.Pending.ToList()) Rollback(receipt, "interrupted enlistment restored on load");
        }
        internal static bool Enlist(string source, Settlement locality, MobileParty party, Hero payer, CharacterObject troop,
            Hero notable = null, int volunteerIndex = -1, bool liability = false)
        {
            if (Allow(source, locality, party, payer, troop, 1, true, liability) == 0) return false;
            if (notable != null && (volunteerIndex < 0 || volunteerIndex >= notable.VolunteerTypes.Length
                || notable.VolunteerTypes[volunteerIndex] != troop)) return false;
            var receipt = Begin(source, locality, party, payer, troop, 1, notable);
            if (receipt == null) return false;
            try
            {
                party.MemberRoster.AddToCounts(troop, 1);
                if (notable != null) notable.VolunteerTypes[volunteerIndex] = null;
                if (!liability && receipt.QuotedCost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(payer, null, receipt.QuotedCost, true);
                Complete(receipt, locality, party, payer, troop, liability);
                return receipt.Status == "committed" && receipt.Accepted == 1;
            }
            catch
            {
                Rollback(receipt, "enlistment failed");
                throw;
            }
        }
        internal static void RecordHearthGrant(string source, Village village, float before)
        {
            var state = State;
            if (state == null || village == null) return;
            string key = source + ":" + village.StringId;
            state.SponsoredHearth.TryGetValue(key, out double previous);
            state.SponsoredHearth[key] = previous + village.Hearth - before;
        }

        internal static JObject Diagnostics(bool detailed = true) => new JObject {
            ["schemaVersion"] = ReignWarEconomyPolicy.Version,
            ["sources"] = JObject.FromObject(State?.Sources ?? new Dictionary<string, EconomySourceTotals>()),
            ["sponsoredHearth"] = JObject.FromObject(State?.SponsoredHearth ?? new Dictionary<string, double>()),
            ["financeSinceMigration"] = JObject.FromObject(State?.Finance ?? new Dictionary<string, EconomyFinanceTotals>()),
            ["pendingTransactions"] = (State?.Pending.Count ?? 0) + (State?.NavalPending.Count ?? 0),
            ["navalEconomy"] = ReignWarEconomyNaval.Coverage(),
            ["householdStipends"] = ReignHouseholdStipends.Diagnostics(State?.HouseholdStipends, detailed),
            ["caravanInvestments"] = ReignNpcCaravanInvestments.Diagnostics(detailed),
            ["generationRegistrationVersion"] = 2,
            ["nextProvisionalGeneration"] = State?.NextPartyGeneration ?? 0,
            ["unsupportedEvents"] = State?.UnsupportedEvents ?? 0,
            ["lastError"] = State?.LastError ?? "economy behavior unavailable",
            ["recentReceipts"] = JArray.FromObject(detailed ? State?.Recent ?? new List<EconomyReceipt>() : new List<EconomyReceipt>()) };
    }
}
