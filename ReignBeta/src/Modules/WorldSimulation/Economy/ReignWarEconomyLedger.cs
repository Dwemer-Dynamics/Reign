#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ReignBeta.Economy
{
    internal sealed class EconomyNavalReceipt
    {
        public int Version = 1;
        public long Id;
        public string Hull = "", Party = "", Market = "", Hero = "", Status = "pending", Error = "";
        public int ShipRandom, Quote, WalletBefore, WalletAfter, MarketBefore, MarketAfter;
        public int CommissionQuoted = 0, CommissionPaid = 0;
        public int Paid = 0, Returned = 0;
        public long WalletPaymentDelta = 0, MarketPaymentDelta = 0;
        public bool Buying;
        public double Day = 0;
    }
    internal sealed class EconomyShipAcquisition
    {
        public string Id = "", Kind = "", Hull = "", Party = "";
        public int Paid;
        public double Day = 0;
    }

    // MBObjectBase.GetHashCode follows its mutable native ID. Unregistered objects require reference identity.
    internal sealed class ReignWarEconomyReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReignWarEconomyReferenceComparer<T> Instance = new ReignWarEconomyReferenceComparer<T>();
        public bool Equals(T first, T second) => ReferenceEquals(first, second);
        public int GetHashCode(T value) => value == null ? 0 : RuntimeHelpers.GetHashCode(value);
    }

    internal sealed class ReignEconomyState
    {
        public long MedicalSequence = 0, MedicalDischarged = 0, MedicalExcluded = 0;
        public double MedicalHearthReturned = 0;
        public Dictionary<long, MedicalEvent> MedicalActive = new Dictionary<long, MedicalEvent>();
        public List<MedicalEvent> MedicalRecent = new List<MedicalEvent>();
        public int Version = ReignWarEconomyPolicy.Version;
        public long NextOperation { get; set; }
        // Additive payload field: native assigns MBGUID only after lord-party initialization.
        public long NextPartyGeneration { get; set; }
        public Dictionary<string, float> Recovery = new Dictionary<string, float>(StringComparer.Ordinal);
        public Dictionary<string, string> PartyGenerations = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, EconomySourceTotals> Sources = new Dictionary<string, EconomySourceTotals>(StringComparer.Ordinal);
        public List<EconomyReceipt> Pending = new List<EconomyReceipt>();
        public List<EconomyReceipt> Recent = new List<EconomyReceipt>();
        public int NavalVersion = 1;
        public List<EconomyNavalReceipt> NavalPending = new List<EconomyNavalReceipt>();
        public List<EconomyNavalReceipt> NavalRecent = new List<EconomyNavalReceipt>();
        public Dictionary<string, EconomyShipAcquisition> NavalAcquisitions = new Dictionary<string, EconomyShipAcquisition>(StringComparer.Ordinal);
        public long NavalAcquisitionFailures = 0;
        public float UnmetReliefFood { get; set; }
        public int UnsupportedEvents { get; set; }
        public long UnattributedWagePayments { get; set; }
        public Dictionary<string, EconomyStandingOrder> MilitaryStandings = new Dictionary<string, EconomyStandingOrder>(StringComparer.Ordinal);
        public List<EconomyPressurePair> MilitaryPressure = new List<EconomyPressurePair>();
        public string MilitaryPressureCampaign = "", MilitaryPressureTimeline = "";
        public double MilitaryPressureDay = -1;
        public long ReadinessRecruitmentBlocks { get; set; }
        public Dictionary<string, int> DrawdownDays = new Dictionary<string, int>(StringComparer.Ordinal);
        public long DemobilizedRegulars { get; set; }
        public double DemobilizationHearthReturned { get; set; }
        public Dictionary<string, double> DemobilizationHearthByVillage = new Dictionary<string, double>(StringComparer.Ordinal);
        public long DrawdownSequence { get; set; }
        public List<EconomyDrawdownReceipt> DrawdownRecent = new List<EconomyDrawdownReceipt>();
        public List<EconomyDrawdownReceipt> DrawdownPending = new List<EconomyDrawdownReceipt>();
        // Additive fiscal observation1. Existing balances and historical counters are never reconstructed.
        public long TariffSequence { get; set; }
        public long TariffUnsupported { get; set; }
        public Dictionary<string, EconomyTariffTotals> TariffSales = new Dictionary<string, EconomyTariffTotals>(StringComparer.Ordinal);
        public Dictionary<string, EconomyTariffPoolTotals> TariffPools = new Dictionary<string, EconomyTariffPoolTotals>(StringComparer.Ordinal);
        public List<EconomyTariffReceipt> TariffRecent = new List<EconomyTariffReceipt>();
        public string LastError = "";
        public Dictionary<string, double> NativeArrivalDays = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, int> RecruitmentDays = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string> NativeTestReceipts = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, double> SponsoredHearth = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, double> LastClanCash = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, double> ClanCashChange = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, double> LastVillageHearth = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, double> VillageHearthChange = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, EconomyFinanceTotals> Finance = new Dictionary<string, EconomyFinanceTotals>(StringComparer.Ordinal);
    }
    internal sealed class EconomyDrawdownReceipt
    {
        public long Id { get; set; }
        public string Party { get; set; }
        public string Generation { get; set; }
        public string Kingdom { get; set; }
        public string Status { get; set; }
        public int Day { get; set; }
        public int Before { get; set; }
        public int After { get; set; }
        public int Planned { get; set; }
        public int Removed { get; set; }
        public double HearthReturned { get; set; }
        public double HearthUnallocated { get; set; }
        public Dictionary<string, double> HearthCredits = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, int> Troops = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Observed = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> ExperienceRemoved = new Dictionary<string, int>(StringComparer.Ordinal);
    }
    internal sealed class EconomyStandingOrder
    {
        public string Mode = "peace", Ruler = "", Reason = "";
        public int? PeacePercent { get; set; }
        public bool Automatic { get; set; }
        public double Day { get; set; }
        public long Revision { get; set; }
    }
    internal sealed class EconomyPressurePair
    {
        public string Actor = "", Target = "", Channel = "";
        public int Value { get; set; }
    }
    internal sealed class EconomyTariffReceipt
    {
        public int Version { get; set; }
        public long Id { get; set; }
        public double Day { get; set; }
        public string Town { get; set; }
        public string OwnerClan { get; set; }
        public string BuyerClan { get; set; }
        public string BuyerPartyClan { get; set; }
        public string PayerHero { get; set; }
        public string OwnerKingdom { get; set; }
        public string BuyerKingdom { get; set; }
        public string BuyerParty { get; set; }
        public string BuyerClass { get; set; }
        public string Purpose { get; set; }
        public string Item { get; set; }
        public int Quantity { get; set; }
        public int Received { get; set; }
        public int Removed { get; set; }
        public long BuyerDebit { get; set; }
        public long NativePrice { get; set; }
        public int QuotedUnits { get; set; }
        public bool AffordabilityGuarded { get; set; }
        public int FulfilledUnits { get; set; }
        public int AffordabilityBlockedUnits { get; set; }
        public int MinimumBlockedNativePrice { get; set; }
        public long FulfilledNativePrice { get; set; }
        public long MarketChange { get; set; }
        public long CommissionDebit { get; set; }
        public long PoolCredit { get; set; }
        public float NativeRatio { get; set; }
        public string TaxModel { get; set; }
        public bool Exempt { get; set; }
        public bool Supported { get; set; }
        public string Error { get; set; }
    }
    internal sealed class EconomyTariffTotals
    {
        public long Purchases { get; set; }
        public long Quantity { get; set; }
        public long BuyerDebit { get; set; }
        public long FulfilledNativePrice { get; set; }
        public long AffordabilityBlockedUnits { get; set; }
        public long MarketChange { get; set; }
        public long CommissionDebit { get; set; }
        public long PoolCredit { get; set; }
        public long ExemptPurchases { get; set; }
        public long Unsupported { get; set; }
        public double LastDay { get; set; }
    }
    internal sealed class EconomyTariffPoolTotals
    {
        public long Credits { get; set; }
        public long Debits { get; set; }
        public long Changes { get; set; }
        public long OutsideObservedSaleCredits { get; set; }
        public double LastDay { get; set; }
    }
    internal sealed class EconomyFinanceTotals
    {
        public double WagesOwed { get; set; }
        public double WagesPaid { get; set; }
        public double TournamentGold { get; set; }
        public double NativeClanPayoutNet { get; set; }
        public long NativeClanPayouts { get; set; }
        public double NativeKingdomBudgetCredits { get; set; }
        public long NativeKingdomBudgetCreditCount { get; set; }
        public double NativeKingdomSupportReceived { get; set; }
        public double NativeCaravanRemittances { get; set; }
        public double NativeMilitaryPartyRemittances { get; set; }
        public double NativeLordPurseTopUps { get; set; }
        public double NativeTownMarketCredits { get; set; }
        public double NativeTownMarketDebits { get; set; }
        public double NativePrisonerAwardsToClanLeader { get; set; }
        public double NativePrisonerAwardsToOtherHeroes { get; set; }
        public double NativePrisonerAwardsToSettlement { get; set; }
        public double NativeLordLootSalesToClanLeader { get; set; }
        public double NativeLordLootSalesToOtherLords { get; set; }
        public double NativeLordHorseMarketSalesToClanLeader { get; set; }
        public double NativeLordHorseMarketSalesToOtherLords { get; set; }
        public long WagePayments { get; set; }
        // Additive observation2 totals. Older unclassified payments stay historical.
        public long FieldWagePayments { get; set; }
        public long GarrisonWagePayments { get; set; }
        public double FieldWagesOwed { get; set; }
        public double FieldWagesPaid { get; set; }
        public double GarrisonWagesOwed { get; set; }
        public double GarrisonWagesPaid { get; set; }
        public double LastPaymentDay { get; set; }
    }
    internal sealed class EconomySourceTotals
    {
        public long Enlisted { get; set; }
        public long Blocked { get; set; }
        public long Exempt { get; set; }
        public double Hearth { get; set; }
        public double CashExpense { get; set; }
        public double Liability { get; set; }
    }
    internal sealed class EconomyReceipt
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Settlement { get; set; }
        public string Party { get; set; }
        public string Troop { get; set; }
        public string Payer { get; set; }
        public string Status { get; set; }
        public int Requested { get; set; }
        public int Accepted { get; set; }
        public int BeforeRoster { get; set; }
        public int BeforeGold { get; set; }
        public int BeforeLiability { get; set; }
        public int QuotedCost { get; set; }
        public int QuotedTotal { get; set; }
        public string PartyGeneration { get; set; }
        public Dictionary<string, int[]> Roster = new Dictionary<string, int[]>(StringComparer.Ordinal);
        public Dictionary<string, int> Gold = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string[]> Volunteers = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public Dictionary<string, float[]> Population = new Dictionary<string, float[]>(StringComparer.Ordinal);
        public double Day { get; set; }
        public double Hearth { get; set; }
        public double CashExpense { get; set; }
        public double Liability { get; set; }
    }

    // Receipt transitions are independent of native objects and survive save/reload.
    internal static class ReignWarEconomyLedger
    {
        internal static void RecordTariff(ReignEconomyState state, EconomyTariffReceipt receipt)
        {
            if (state == null || receipt == null) return;
            receipt.Id = ++state.TariffSequence;
            string key = receipt.Town + "|" + receipt.OwnerClan + "|" + receipt.OwnerKingdom + "|" + receipt.BuyerClass + "|" + receipt.Purpose;
            if (!state.TariffSales.TryGetValue(key, out var totals))
            {
                if (state.TariffSales.Count >= 8192) { state.TariffUnsupported++; return; }
                state.TariffSales[key] = totals = new EconomyTariffTotals();
            }
            totals.Purchases++; totals.Quantity += receipt.Received;
            totals.BuyerDebit += receipt.BuyerDebit; totals.MarketChange += receipt.MarketChange;
            totals.FulfilledNativePrice += receipt.FulfilledNativePrice;
            totals.AffordabilityBlockedUnits += receipt.AffordabilityBlockedUnits;
            totals.CommissionDebit += receipt.CommissionDebit; totals.PoolCredit += receipt.PoolCredit;
            if (receipt.Exempt) totals.ExemptPurchases++;
            if (!receipt.Supported) { totals.Unsupported++; state.TariffUnsupported++; }
            totals.LastDay = receipt.Day;
            state.TariffRecent.Add(receipt);
            if (state.TariffRecent.Count > 128) state.TariffRecent.RemoveRange(0, state.TariffRecent.Count - 128);
        }

        // One receipt per real arrival time; future arrivals retain the authored perk cadence.
        internal static bool BeginNativeArrival(ReignEconomyState state, string key, double day)
        {
            if (state == null || string.IsNullOrEmpty(key) || double.IsNaN(day) || double.IsInfinity(day)) return false;
            if (state.NativeArrivalDays.TryGetValue(key, out double previous) && previous >= day) return false;
            if (state.NativeArrivalDays.Count >= 8192 && !state.NativeArrivalDays.ContainsKey(key))
            {
                foreach (string stale in state.NativeArrivalDays.Where(x => x.Value < day - 3)
                    .Select(x => x.Key).ToList()) state.NativeArrivalDays.Remove(stale);
                if (state.NativeArrivalDays.Count >= 8192)
                { state.UnsupportedEvents++; state.LastError = "Native arrival receipt capacity exceeded"; return false; }
            }
            state.NativeArrivalDays[key] = day;
            return true;
        }

        internal static bool TryNativeCreditRemainder(int actualBefore, int actualAfter,
            long accountedBefore, long accountedAfter, out long remainder)
        {
            remainder = 0;
            if (actualBefore < 0 || actualAfter < actualBefore) return false;
            long accounted;
            try { accounted = checked(accountedAfter - accountedBefore); }
            catch (OverflowException) { return false; }
            long actual = (long)actualAfter - actualBefore;
            if (accounted < 0 || accounted > actual) return false;
            remainder = actual - accounted;
            return true;
        }

        internal static string ProvisionalGeneration(ReignEconomyState state)
            => "unregistered-v1-" + checked(++state.NextPartyGeneration).ToString(System.Globalization.CultureInfo.InvariantCulture);

        internal static void RegisterGeneration(ReignEconomyState state, string partyId, string provisional, string native)
        {
            if (string.IsNullOrEmpty(native) || native == "0" || native == provisional)
                throw new InvalidOperationException("Party generation registration requires a permanent native ID.");
            string previous = partyId + ":" + provisional, current = partyId + ":" + native;
            if (!state.PartyGenerations.TryGetValue(previous, out string status))
                throw new InvalidOperationException("Missing provisional household creation receipt: " + previous);
            if (state.PartyGenerations.ContainsKey(current))
                throw new InvalidOperationException("Duplicate permanent household generation: " + current);
            state.PartyGenerations.Add(current, status);
            state.PartyGenerations.Remove(previous);
            foreach (var receipt in state.Pending.Concat(state.Recent).Where(x => x.Party == partyId && x.PartyGeneration == provisional))
                receipt.PartyGeneration = native;
        }

        internal static string WagePayer(bool garrison, string actualClan, string settlementOwner)
            => garrison ? settlementOwner : actualClan;

        internal static void ObserveWages(ReignEconomyState state, string clanId, double day, int owed, int paid, bool applyWithdrawals, bool garrison = false)
        {
            if (state == null || !applyWithdrawals) return;
            if (string.IsNullOrEmpty(clanId)) { state.UnattributedWagePayments++; return; }
            if (!state.Finance.TryGetValue(clanId, out var totals)) state.Finance[clanId] = totals = new EconomyFinanceTotals();
            totals.WagesOwed += Math.Max(0, owed);
            totals.WagesPaid += Math.Max(0, paid);
            totals.WagePayments++;
            if (garrison)
            {
                totals.GarrisonWagesOwed += Math.Max(0, owed);
                totals.GarrisonWagesPaid += Math.Max(0, paid);
                totals.GarrisonWagePayments++;
            }
            else
            {
                totals.FieldWagesOwed += Math.Max(0, owed);
                totals.FieldWagesPaid += Math.Max(0, paid);
                totals.FieldWagePayments++;
            }
            totals.LastPaymentDay = day;
        }

        internal static bool IsPending(ReignEconomyState state, EconomyReceipt receipt) => receipt != null
            && receipt.Status == "pending" && state.Pending.Any(x => x.Id == receipt.Id);

        internal static bool Finish(ReignEconomyState state, EconomyReceipt receipt, int enlisted,
            double hearth, double cash, double liability, string status = null)
        {
            if (!IsPending(state, receipt)) return false;
            if (double.IsNaN(hearth) || double.IsInfinity(hearth) || double.IsNaN(cash) || double.IsInfinity(cash)
                || double.IsNaN(liability) || double.IsInfinity(liability) || enlisted < 0 || enlisted > receipt.Requested || Math.Abs(hearth - enlisted * .5) > .00001
                || cash < 0 || liability < 0 || (cash > 0 && liability > 0)
                || (enlisted > 0 && Math.Abs(cash + liability - (receipt.QuotedCost < 0 ? receipt.QuotedTotal : receipt.QuotedCost * enlisted)) > .00001)
                || (enlisted == 0 && (cash != 0 || liability != 0)))
                throw new InvalidOperationException("Recruitment receipt violates resource conservation: " + receipt.Id);
            receipt.Accepted = enlisted; receipt.Hearth = hearth; receipt.CashExpense = cash; receipt.Liability = liability;
            receipt.Status = status ?? (enlisted == 0 ? "cancelled" : "committed");
            if (!state.Sources.TryGetValue(receipt.Source, out var totals))
                state.Sources[receipt.Source] = totals = new EconomySourceTotals();
            totals.Enlisted += enlisted; totals.Hearth += hearth; totals.CashExpense += cash; totals.Liability += liability;
            state.Pending.RemoveAll(x => x.Id == receipt.Id);
            // Before-images are required only while pending. Keep a bounded operational journal.
            receipt.Roster.Clear(); receipt.Gold.Clear(); receipt.Volunteers.Clear(); receipt.Population.Clear();
            state.Recent.Add(receipt);
            if (state.Recent.Count > 512) state.Recent.RemoveRange(0, state.Recent.Count - 512);
            return true;
        }
    }
}
