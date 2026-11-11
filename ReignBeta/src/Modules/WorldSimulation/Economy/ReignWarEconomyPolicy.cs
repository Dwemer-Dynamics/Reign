#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Economy
{
    // No game, server, clock, random or provider dependencies. Native adapters own mutations.
    internal static class ReignWarEconomyPolicy
    {
        internal const int Version = 2;
        internal const int ProductionCurveVersion = 3;
        internal const int RecruitmentPriceVersion = 3;
        private static readonly double HouseholdOutputExponent = Math.Log(6) / Math.Log(30);
        private static readonly double HealthyHouseholdOutputExponent = Math.Log(5.5) / Math.Log(30);
        internal static int NavalCommission(int authored, int actualPrice, int marketGold, int governorGold, bool sameClan)
            => sameClan ? 0 : (int)Math.Min(Math.Max(0L, Math.Min((long)authored, Math.Min((long)actualPrice, marketGold))),
                Math.Max(0L, (long)int.MaxValue - governorGold));
        internal const double HearthFloor = 10;
        internal const double HearthPerRecruit = .5;
        internal const double RecruitingRecovery = .3;
        internal const double GarrisonWageFactor = .75;
        internal const int GarrisonMenPerFood = 25;
        internal const double HearthReturnPerDischarge = .25;
        internal static bool StandingOrderApplies(string recordedRuler, string currentRuler)
            => !string.IsNullOrEmpty(currentRuler) && string.Equals(recordedRuler, currentRuler, StringComparison.Ordinal);
        internal static double FieldReadiness(bool atWar, bool orderedWar, int hostilePressure, bool frontier, bool fresh,
            bool playerSovereign = false, int? peacePercent = null)
            => orderedWar || (atWar && !playerSovereign) ? 1 : peacePercent.HasValue ? Clamp(peacePercent.Value, 40, 90) / 100d
                : .45 + (frontier ? .30 : .15) * (fresh ? Clamp((hostilePressure - 10d) / 90d, 0, 1) : 0);
        internal static bool PressureFresh(double observed, double now)
            => !double.IsNaN(observed) && !double.IsNaN(now) && observed <= now && now - observed <= 1;
        internal static int DrawdownBatch(int members, int target, int nativeCapacity, bool safe)
            => !safe || nativeCapacity <= 0 ? 0 : Math.Min(Math.Max(0, members - target), Math.Min(10, Math.Max(1, (int)Math.Ceiling(nativeCapacity * .05))));
        internal sealed class DischargeCandidate
        {
            internal string Id;
            internal int Tier, Healthy;
            internal double Experience;
        }
        internal static List<DischargeCandidate> SelectDischarges(IEnumerable<DischargeCandidate> candidates, int limit)
        {
            var plan = new List<DischargeCandidate>();
            foreach (var candidate in candidates.Where(x => x.Healthy > 0).OrderBy(x => x.Tier).ThenBy(x => x.Experience).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                int count = Math.Min(candidate.Healthy, Math.Max(0, limit));
                if (count > 0) plan.Add(new DischargeCandidate { Id = candidate.Id, Tier = candidate.Tier, Experience = candidate.Experience, Healthy = count });
                limit -= count;
                if (limit <= 0) break;
            }
            return plan;
        }
        internal sealed class HearthRecipient
        {
            internal string Id;
            internal float Hearth;
        }
        internal static Dictionary<string, float> PlanHearthReturn(int discharged, IEnumerable<HearthRecipient> recipients)
        {
            double remaining = Math.Max(0, discharged) * HearthReturnPerDischarge;
            var villages = recipients.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var after = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < villages.Count && remaining > 0; i++)
            {
                var village = villages[i];
                float next = (float)(village.Hearth + remaining / (villages.Count - i));
                double actual = (double)next - village.Hearth;
                // Native hearths are floats: preserve the fixed pool even at a rounding boundary.
                if (actual <= 0 || actual > remaining || float.IsInfinity(next) || float.IsNaN(next)) continue;
                after.Add(village.Id, next);
                remaining -= actual;
            }
            return after;
        }
        internal static double Clamp(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(maximum, value));
        internal static double HealthyFood(double hearth)
        {
            hearth = Math.Max(0, hearth);
            // Preserve subsistence below ten hearths; trim healthy output by at most one twelfth.
            // Anchors: (0,0), (10,2), (300,11), (600,16.5). Large villages keep growing.
            if (hearth <= 10) return 12 * Math.Pow(hearth / 300, HouseholdOutputExponent);
            if (hearth <= 300) return 2 * Math.Pow(hearth / 10, HealthyHouseholdOutputExponent);
            return hearth <= 600 ? 11 + (5.5 / 300) * (hearth - 300) : 16.5 + (5.5 / 600) * (hearth - 600);
        }
        internal static double Production(double hearth, double recovery, bool functioning, double additiveFactor = 0)
            => functioning ? HealthyFood(hearth) * Clamp(recovery, 0, 1) * Math.Max(0, 1 + additiveFactor) : 0;
        internal static double DecayStrain(double strain) => Math.Max(0, .95 * strain - .5);
        internal static double NaturalGrowth(double hearth, double strain, bool stressed)
            => 2 / (1 + Math.Pow(Math.Max(0, hearth) / 250, 2)) / (1 + Math.Max(0, strain) / 10) * (stressed ? .5 : 1);
        internal static double Recover(double recovery, bool functioning, bool stressed)
            => Clamp(recovery + (functioning ? (stressed ? .0125 : .025) : 0), 0, 1);
        internal static int RecruitCapacity(double hearth, double recovery, bool functioning)
            => !functioning || recovery + 1e-7 < RecruitingRecovery ? 0 : Math.Max(0, (int)Math.Floor((hearth - HearthFloor) / HearthPerRecruit + 1e-7));
        internal static double WageMultiplier(int tier) => tier <= 1 ? 1 : tier == 2 ? 1.15 : tier == 3 ? 1.5 : tier == 4 ? 2 : tier == 5 ? 3 : 4;
        // Native upgrade gold uses the difference of these hire quotes; do not multiply upgrades again.
        internal static double HireMultiplier(int tier) => tier <= 1 ? 3 : tier == 2 ? 3.6 : tier == 3 ? 4.5 : tier == 4 ? 6 : tier == 5 ? 8.25 : 10.5;
        internal static bool InternalProvisioning(string ownerClan, string buyerClan, string ownerKingdom, string buyerKingdom, bool military,
            bool verifiedPurpose, bool eligibleItem, bool started)
            => started && military && verifiedPurpose && eligibleItem && !string.IsNullOrEmpty(ownerClan)
                && !string.IsNullOrEmpty(buyerClan) && (string.Equals(ownerClan, buyerClan, StringComparison.Ordinal)
                    || (!string.IsNullOrEmpty(ownerKingdom) && string.Equals(ownerKingdom, buyerKingdom, StringComparison.Ordinal)));
        internal static bool TariffConserved(int requested, int received, int removed, long buyerDebit,
            long marketChange, long commission, long poolCredit, bool exempt, long nativePrice, int quotedUnits)
            => requested > 0 && received == requested && removed == requested && quotedUnits == requested
                && nativePrice >= 0 && buyerDebit == nativePrice
                && commission >= 0 && marketChange + commission == buyerDebit
                && poolCredit >= 0 && poolCredit <= commission && (!exempt || (commission == 0 && poolCredit == 0));
        internal static bool CanPayQuotedUnit(long cash, long acceptedCost, int price)
            => price >= 0 && acceptedCost >= 0 && price <= Math.Max(0, cash) - acceptedCost;
        internal static bool GuardedPurchaseConserved(int requested, int fulfilled, int blocked, int received, int removed,
            long buyerDebit, long marketChange, long commission, long poolCredit, bool exempt,
            long attemptedQuote, int quotedUnits, long fulfilledQuote)
            => requested > 0 && fulfilled >= 0 && fulfilled <= requested && blocked == requested - fulfilled
                && received == fulfilled && removed == fulfilled && quotedUnits == requested
                && fulfilledQuote >= 0 && attemptedQuote >= fulfilledQuote && buyerDebit == fulfilledQuote
                && commission >= 0 && marketChange + commission == buyerDebit
                && poolCredit >= 0 && poolCredit <= commission && (!exempt || (commission == 0 && poolCredit == 0));
        internal static int AffordableCount(int requested, double payerCash, double clanCash, double wage, double liabilities,
            double unitCost, double marginalWage, int reserveDays)
        {
            if (requested <= 0) return 0;
            unitCost = Math.Max(0, unitCost);
            double spendable = Math.Min(Math.Max(0, payerCash), Math.Max(0, clanCash) - Math.Max(0, liabilities) - reserveDays * Math.Max(0, wage));
            if (spendable < 0) return 0;
            double burden = unitCost + reserveDays * Math.Max(0, marginalWage);
            return burden <= 0 ? requested : Math.Min(requested, Math.Max(0, (int)Math.Min(int.MaxValue, Math.Floor((spendable + 1e-7) / burden))));
        }
        internal static double BlockedCargo(double cargo, double blockedDays) => Math.Max(0, cargo) * Math.Pow(.98, Math.Max(0, blockedDays));
        internal static int AffordableCountExact(int requested, double payerCash, double clanCash, double wage, double liabilities,
            double unitCost, int reserveDays, Func<int, double> additionalWages)
        {
            double spendable = Math.Min(Math.Max(0, payerCash), Math.Max(0, clanCash) - Math.Max(0, liabilities) - reserveDays * Math.Max(0, wage));
            if (requested <= 0 || spendable < 0) return 0;
            int low = 0, high = requested;
            while (low < high)
            {
                int middle = low + (int)(((long)high - low + 1) / 2);
                double cost = middle * Math.Max(0, unitCost) + reserveDays * Math.Max(0, additionalWages(middle));
                if (!double.IsNaN(cost) && !double.IsInfinity(cost) && cost <= spendable + 1e-7) low = middle;
                else high = middle - 1;
            }
            return low;
        }
        internal static double Stress(double food, double recruits, double unpaid, double unfunded)
            => Clamp(Math.Max(Math.Max(food, recruits), Math.Max(unpaid, unfunded)), 0, 1);

        internal sealed class CivilianBudget
        {
            public string Id;
            public double Budget;
            public bool Food;
        }
        internal sealed class MarketFood
        {
            public string Id, Category;
            public double Price;
            public int Stock;
        }
        internal sealed class SubsistencePlan
        {
            public Dictionary<string, double> Budgets = new Dictionary<string, double>(StringComparer.Ordinal);
            public double RequiredUnits, NativePlannedUnits, PlannedUnits, Redirected, TotalBefore, TotalAfter, UnmetUnits;
        }
        internal static bool CompatibleCivilianBudgetQuotes(double unit, double zero, double half, double whole)
            => new[] { unit, zero, half, whole }.All(x => !double.IsNaN(x) && !double.IsInfinity(x))
                && unit > 0 && half >= 0 && whole >= 0 && Math.Abs(zero) <= .001
                && Math.Abs(half * 2 - whole) <= Math.Max(.001, whole * .00001);
        internal static bool CompatibleNegativeCivilianBudget(double demand, double unit, double zero, double half, double whole)
            => new[] { demand, unit, zero, half, whole }.All(x => !double.IsNaN(x) && !double.IsInfinity(x))
                && demand < 0 && unit > 0 && half < 0 && whole < 0 && Math.Abs(zero) <= .001
                && Math.Abs(half * 2 - whole) <= Math.Max(.001, Math.Abs(whole) * .00001)
                && Math.Abs(unit * demand - whole) <= Math.Max(.001, Math.Abs(whole) * .00001);
        // Match the native float comparison, including its exact threshold representation.
        internal static double NativeSpendableBudget(double budget) => budget > .01f ? budget : 0;
        internal const int SubsistenceVersion = 3;
        internal const double SubsistenceReserveRatio = .2, SubsistenceForecastDays = 7;
        // Forecast the unmodified purchasing plan, not yesterday's redirected SoldItems.
        // A successful food purchase must not turn its own necessity off the following day.
        internal static double NativePlanFoodProjection(double stock, double capacity, double noMarketNet, double nativePlannedUnits)
        {
            if (new[] { stock, capacity, noMarketNet, nativePlannedUnits }.Any(x => double.IsNaN(x) || double.IsInfinity(x))
                || stock < 0 || capacity <= 0 || nativePlannedUnits < 0)
                throw new ArgumentException("Subsistence forecasts require bounded finite stocks and an inventory-capped native plan.");
            double projected = stock + SubsistenceForecastDays * (noMarketNet + nativePlannedUnits);
            if (double.IsNaN(projected) || double.IsInfinity(projected))
                throw new ArgumentException("Subsistence forecast overflowed.");
            return projected;
        }
        internal static bool NeedsSubsistencePriority(double stock, double capacity, double noMarketNet, double nativePlannedUnits)
            => Math.Min(stock, NativePlanFoodProjection(stock, capacity, noMarketNet, nativePlannedUnits)) < SubsistenceReserveRatio * capacity;
        internal static void ConsolidateBudgetDust(Dictionary<string, double> budgets)
        {
            var dust = budgets.Where(x => x.Value > 0 && NativeSpendableBudget(x.Value) == 0).ToList();
            if (dust.Count == 0) return;
            double total = dust.Sum(x => x.Value);
            string target = budgets.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;
            foreach (var pair in dust) budgets[pair.Key] = 0;
            budgets[target] += total;
        }
        // Redirect existing household purchasing power. No food or money is created by a quote.
        // The native adapter admits one edible stack per category so its normal budget/rounding
        // semantics remain observable rather than guessing about a foreign recipe/category route.
        internal static SubsistencePlan PrioritizeSubsistence(IEnumerable<CivilianBudget> input,
            IEnumerable<MarketFood> available, double requiredUnits)
        {
            var budgets = input.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var food = available.OrderBy(x => x.Price).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            if (double.IsNaN(requiredUnits) || double.IsInfinity(requiredUnits) || requiredUnits < 0
                || budgets.Any(x => string.IsNullOrEmpty(x.Id) || double.IsNaN(x.Budget) || double.IsInfinity(x.Budget) || x.Budget < 0)
                || budgets.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != budgets.Count
                || food.Any(x => string.IsNullOrEmpty(x.Id) || x.Stock < 0 || double.IsNaN(x.Price) || double.IsInfinity(x.Price) || x.Price <= 0
                    || !budgets.Any(b => b.Id == x.Category && b.Food))
                || food.Select(x => x.Category).Distinct(StringComparer.Ordinal).Count() != food.Count)
                throw new ArgumentException("Subsistence needs finite budgets and one priced edible stack per category.");
            var plan = new SubsistencePlan { RequiredUnits = requiredUnits,
                Budgets = budgets.ToDictionary(x => x.Id, x => x.Budget, StringComparer.Ordinal),
                TotalBefore = budgets.Sum(x => x.Budget) };
            var baseline = food.ToDictionary(x => x.Id, x => Math.Min(x.Stock, plan.Budgets[x.Category] / x.Price), StringComparer.Ordinal);
            plan.NativePlannedUnits = plan.PlannedUnits = baseline.Values.Sum();
            double nonFood = budgets.Where(x => !x.Food).Sum(x => x.Budget), remaining = nonFood;
            foreach (var offer in food)
            {
                double extra = Math.Min(Math.Max(0, requiredUnits - plan.PlannedUnits),
                    Math.Min(offer.Stock - baseline[offer.Id], remaining / offer.Price));
                double spend = Math.Max(0, extra) * offer.Price;
                plan.Budgets[offer.Category] += spend;
                plan.PlannedUnits += Math.Max(0, extra);
                plan.Redirected += spend; remaining = Math.Max(0, remaining - spend);
            }
            if (nonFood > 0)
                foreach (var budget in budgets.Where(x => !x.Food))
                    plan.Budgets[budget.Id] *= remaining / nonFood;
            if (plan.Redirected > 0)
            {
                ConsolidateBudgetDust(plan.Budgets);
                plan.PlannedUnits = food.Sum(x => Math.Min(x.Stock, plan.Budgets[x.Category] / x.Price));
                plan.Redirected = Math.Max(0, nonFood - budgets.Where(x => !x.Food).Sum(x => plan.Budgets[x.Id]));
            }
            plan.TotalAfter = plan.Budgets.Values.Sum();
            plan.UnmetUnits = Math.Max(0, requiredUnits - plan.PlannedUnits);
            return plan;
        }

        internal sealed class Holding
        {
            public string Id = "";
            public double Stock = 0, Capacity = 0, Change = 0, X = 0, Y = 0, Security = 0;
            public bool Besieged;
            public double Projected => Stock + 7 * Change;
        }
        internal sealed class Shipment
        {
            public string Donor, Recipient;
            public double Shipped, Delivered;
        }
        internal const int ReliefVersion = 2;
        internal const double ReliefDonorCapacityFraction = .075, ReliefDonorMinimum = 10, ReliefDonorMaximum = 30,
            ReliefRecipientMinimum = 15, ReliefRecipientCapacityFraction = .25;
        internal sealed class ReliefPlan
        {
            public int Version = ReliefVersion;
            public bool LegacyFixedLimits;
            public double ProjectedRatio, DonorFloor, Target, UnmetFood;
            public int Recipients, UnmetRecipients, BlockedRoutes;
            public List<Shipment> Shipments = new List<Shipment>();
            public List<ReliefDecision> Decisions = new List<ReliefDecision>();
            public Dictionary<string, double> DonorLimits = new Dictionary<string, double>(StringComparer.Ordinal);
        }
        internal sealed class ReliefDecision
        {
            // Availability at this recipient; earlier recipients may already have spent donor budgets.
            public string Recipient;
            public double Demand, Delivered, DailyLimit, DestinationSpace, ReachableAtStart;
            public int RouteBlockedDonors, SiegeBlockedDonors, FloorBlockedDonors, ExhaustedDonorBudgets, DonorDailyLimitBindings, ProjectionRatioBlockedDonors;
            public bool DailyLimitBinding, DestinationCapacityBinding;
        }
        internal static double ReliefDonorLimit(Holding donor)
            => Clamp(ReliefDonorCapacityFraction * donor.Capacity, ReliefDonorMinimum, ReliefDonorMaximum);
        internal static int NextFoodZeroStreak(long day, long previousDay, bool empty, int previousStreak)
            => day == previousDay ? previousStreak : empty ? (day == previousDay + 1 ? previousStreak + 1 : 1) : 0;
        internal static double ReliefRecipientLimit(Holding recipient, double target)
        {
            // Change is the native net BEFORE incoming relief. Use current stock for
            // rebuilding: using seven-day projected stock here would count the deficit twice.
            double need = Math.Max(0, -recipient.Change);
            double rebuilding = Math.Max(0, target * recipient.Capacity - recipient.Stock) / 7;
            return Math.Min(Math.Max(ReliefRecipientMinimum, ReliefRecipientCapacityFraction * recipient.Capacity),
                Math.Max(ReliefRecipientMinimum, Math.Ceiling(need + rebuilding)));
        }
        internal static ReliefPlan AllocateRelief(IEnumerable<Holding> input, bool legacyFixedLimits = false)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var all = input.ToList();
            if (all.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)
                || new[] { x.Stock, x.Capacity, x.Change, x.X, x.Y, x.Security }.Any(v => double.IsNaN(v) || double.IsInfinity(v))))
                throw new ArgumentException("Relief inputs require finite values and exact holding IDs.", nameof(input));
            if (all.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != all.Count)
                throw new ArgumentException("Relief holding IDs must be unique.", nameof(input));
            var holdings = all.Where(x => x.Capacity > 0).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var result = new ReliefPlan { LegacyFixedLimits = legacyFixedLimits };
            if (holdings.Count == 0) return result;
            double capacity = holdings.Sum(x => x.Capacity);
            result.ProjectedRatio = holdings.Sum(x => Clamp(x.Projected, 0, x.Capacity)) / capacity;
            bool scarce = holdings.Sum(x => x.Change) < 0;
            result.DonorFloor = scarce ? Clamp(result.ProjectedRatio, .2, .6) : .6;
            result.Target = scarce ? Clamp(result.ProjectedRatio, .2, .5) : .5;
            var stocks = holdings.ToDictionary(x => x.Id, x => Math.Max(0, x.Stock));
            foreach (var donor in holdings) result.DonorLimits[donor.Id] = legacyFixedLimits ? 10 : ReliefDonorLimit(donor);
            // Frozen donor budgets prevent same-pass re-export of incoming aid.
            var budgets = holdings.ToDictionary(x => x.Id, x => x.Besieged ? 0 : Math.Max(0,
                Math.Min(result.DonorLimits[x.Id], Math.Min(x.Stock, x.Projected) - result.DonorFloor * x.Capacity)));
            var initialBudgets = new Dictionary<string, double>(budgets, StringComparer.Ordinal);
            foreach (var recipient in holdings.Where(x => Math.Min(x.Stock, x.Projected) < result.Target * x.Capacity)
                .OrderBy(x => x.Projected / x.Capacity).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                result.Recipients++;
                double demand = Math.Max(0, result.Target * recipient.Capacity - Math.Min(recipient.Stock, recipient.Projected));
                double dailyLimit = legacyFixedLimits ? 15 : ReliefRecipientLimit(recipient, result.Target);
                var decision = new ReliefDecision { Recipient = recipient.Id, Demand = demand, DailyLimit = dailyLimit,
                    DestinationSpace = Math.Max(0, recipient.Capacity - stocks[recipient.Id]) };
                foreach (var donor in holdings.Where(x => x.Id != recipient.Id))
                {
                    double efficiency = RouteEfficiency(donor, recipient);
                    if (donor.Besieged || recipient.Besieged) decision.SiegeBlockedDonors++;
                    else if (efficiency <= 0) decision.RouteBlockedDonors++;
                    else if (initialBudgets[donor.Id] <= 1e-6) decision.FloorBlockedDonors++;
                    else decision.ReachableAtStart += budgets[donor.Id] * efficiency;
                }
                double received = 0;
                var blocked = new HashSet<string>(StringComparer.Ordinal);
                // Water filling redistributes a capped donor's remainder without order-dependent nearest-donor drain.
                for (int pass = 0; pass < holdings.Count + 2 && received < Math.Min(dailyLimit, demand) - 1e-6; pass++)
                {
                    var eligible = new List<Tuple<Holding, double, double>>();
                    foreach (var donor in holdings.Where(x => x.Id != recipient.Id && budgets[x.Id] > 1e-6))
                    {
                        double efficiency = RouteEfficiency(donor, recipient);
                        if (efficiency <= 0) { blocked.Add(donor.Id); continue; }
                        double donorProjection = stocks[donor.Id] + 7 * donor.Change;
                        double recipientProjection = stocks[recipient.Id] + 7 * recipient.Change;
                        double equalization = (donorProjection / donor.Capacity - recipientProjection / recipient.Capacity)
                            / (1 / donor.Capacity + efficiency / recipient.Capacity);
                        double safe = Math.Min(budgets[donor.Id], Math.Max(0, equalization));
                        if (safe > 1e-6) eligible.Add(Tuple.Create(donor, safe, efficiency));
                    }
                    double deliveredCapacity = eligible.Sum(x => x.Item2 * x.Item3);
                    double need = Math.Min(Math.Min(dailyLimit - received, demand - received), recipient.Capacity - stocks[recipient.Id]);
                    if (need <= 1e-6 || deliveredCapacity <= 1e-6) break;
                    double scale = Math.Min(1, need / deliveredCapacity);
                    double before = received;
                    foreach (var candidate in eligible)
                    {
                        var donor = candidate.Item1;
                        double efficiency = candidate.Item3;
                        // Recheck after earlier donors changed the recipient's projected reserve.
                        double equalization = ((stocks[donor.Id] + 7 * donor.Change) / donor.Capacity
                            - (stocks[recipient.Id] + 7 * recipient.Change) / recipient.Capacity)
                            / (1 / donor.Capacity + efficiency / recipient.Capacity);
                        double shipped = Math.Min(candidate.Item2 * scale, Math.Max(0, equalization));
                        if (shipped <= 1e-6) continue;
                        double delivered = shipped * efficiency;
                        stocks[donor.Id] -= shipped;
                        stocks[recipient.Id] += delivered;
                        budgets[donor.Id] -= shipped;
                        received += delivered;
                        result.Shipments.Add(new Shipment { Donor = donor.Id, Recipient = recipient.Id, Shipped = shipped, Delivered = delivered });
                    }
                    if (received - before < 1e-6) break;
                }
                result.BlockedRoutes += blocked.Count + (recipient.Besieged && blocked.Count == 0 ? 1 : 0);
                if (demand - received > 1e-5) { result.UnmetRecipients++; result.UnmetFood += demand - received; }
                decision.Delivered = received;
                decision.DailyLimitBinding = demand > received + 1e-5 && received >= dailyLimit - 1e-5;
                decision.DestinationCapacityBinding = demand > received + 1e-5 && received >= decision.DestinationSpace - 1e-5;
                foreach (var donor in holdings.Where(x => x.Id != recipient.Id && RouteEfficiency(x, recipient) > 0))
                {
                    if (initialBudgets[donor.Id] > 1e-6 && budgets[donor.Id] <= 1e-6)
                    {
                        decision.ExhaustedDonorBudgets++;
                        if (Math.Min(donor.Stock, donor.Projected) - result.DonorFloor * donor.Capacity
                            > result.DonorLimits[donor.Id] + 1e-6) decision.DonorDailyLimitBindings++;
                    }
                    else if (budgets[donor.Id] > 1e-6 && (stocks[donor.Id] + 7 * donor.Change) / donor.Capacity
                        <= (stocks[recipient.Id] + 7 * recipient.Change) / recipient.Capacity + 1e-6)
                        decision.ProjectionRatioBlockedDonors++;
                }
                result.Decisions.Add(decision);
            }
            return result;
        }
        internal static double RouteEfficiency(Holding donor, Holding recipient)
        {
            double distance = Math.Sqrt(Math.Pow(donor.X - recipient.X, 2) + Math.Pow(donor.Y - recipient.Y, 2));
            if (donor.Besieged || recipient.Besieged || distance > 180) return 0;
            double efficiency = distance > 120 ? .7 : distance > 60 ? .8 : .9;
            double security = Math.Min(donor.Security, recipient.Security);
            return Clamp(efficiency - (security < 25 ? .1 : security < 50 ? .05 : 0), .7, .9);
        }
    }
}
