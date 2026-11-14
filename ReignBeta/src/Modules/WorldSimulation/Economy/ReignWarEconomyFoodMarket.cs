using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Economy
{
    // Native consumption still removes items, rounds purchases and credits the market.
    // This adapter changes only the allocation of the existing civilian purchasing budget.
    internal static class ReignWarEconomyFoodMarket
    {
        internal const int Version = ReignWarEconomyPolicy.SubsistenceVersion;
        private static NativeCampaign _campaign;
        private static readonly Dictionary<string, JObject> Latest = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private static readonly Dictionary<string, JObject> Census = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private static long _censusDrops, _censusErrors, _censusSuppressed;
        [ThreadStatic] private static bool _fixtureBaseline;
        [ThreadStatic] private static bool _probeActive;

        internal sealed class Consumption
        {
            internal Town Town;
            internal long FoodBefore;
            internal int GoldBefore;
            internal JObject Evidence;
            internal ReignWarEconomyFundingAudit.MarketComponentCapture CashObservation;
        }

        internal static void Reset() { _campaign = null; Latest.Clear(); Census.Clear(); _censusDrops = 0; _censusErrors = 0; _censusSuppressed = 0; _fixtureBaseline = false; _probeActive = false; }
        private static void CheckCampaign()
        {
            if (ReferenceEquals(_campaign, NativeCampaign.Current)) return;
            _campaign = NativeCampaign.Current; Latest.Clear(); Census.Clear(); _censusDrops = 0; _censusErrors = 0; _censusSuppressed = 0;
        }
        private static bool Food(ItemCategory category) => category?.Properties == ItemCategory.Property.BonusToFoodStores;
        private static long FoodUnits(Town town) => town.Owner.ItemRoster.Where(x => Food(x.EquipmentElement.Item?.GetItemCategory())).Sum(x => (long)x.Amount);
        private static double RequiredUnits(Town town) => Math.Ceiling(Math.Max(0,
            -NativeCampaign.Current.Models.SettlementFoodModel.CalculateTownFoodStocksChange(town, false).ResultNumber
            + Math.Max(0, .2 * town.FoodStocksUpperLimit() - town.FoodStocks) / 7));

        internal static Consumption Before(Town town, Dictionary<ItemCategory, float> demand)
        {
            CheckCampaign();
            if (!Latest.ContainsKey(town.StringId) && Latest.Count >= 256)
                Latest.Remove(Latest.OrderBy(x => x.Value.Value<double>("day")).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key);
            var capture = new Consumption { Town = town, FoodBefore = FoodUnits(town), GoldBefore = town.Gold,
                Evidence = new JObject { ["schema"] = "reign-civilian-subsistence-v1", ["version"] = Version,
                    ["day"] = CampaignTime.Now.ToDays, ["townId"] = town.StringId, ["phase"] = "started",
                    ["fixtureBaseline"] = _fixtureBaseline, ["redirectedBudget"] = 0d } };
            Latest[town.StringId] = capture.Evidence;
            capture.CashObservation = ReignWarEconomyFundingAudit.BeginConsumption(town, capture.GoldBefore);
            try
            {
                if (!town.IsTown || town.IsUnderSiege || NativeCampaign.Current?.GameStarted != true)
                { capture.Evidence["reason"] = "native_siege_or_non_town"; return capture; }
                if (demand.Count > 256 || town.Owner.ItemRoster.Count > 4096)
                    throw new InvalidOperationException("Native consumption inputs exceed observation bounds.");
                var model = NativeCampaign.Current.Models.SettlementEconomyModel;
                var categories = demand.Keys.OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
                var factors = new Dictionary<ItemCategory, double>();
                var nativeBudgets = new Dictionary<ItemCategory, double>();
                var budgets = new List<ReignWarEconomyPolicy.CivilianBudget>();
                foreach (var category in categories)
                {
                    double factor = model.CalculateDailySettlementBudgetForItemCategory(town, 1, category);
                    double budget = model.CalculateDailySettlementBudgetForItemCategory(town, demand[category], category);
                    double half = model.CalculateDailySettlementBudgetForItemCategory(town, demand[category] / 2, category);
                    double zero = model.CalculateDailySettlementBudgetForItemCategory(town, 0, category);
                    bool compatible = demand[category] < 0
                        ? ReignWarEconomyPolicy.CompatibleNegativeCivilianBudget(demand[category], factor, zero, half, budget)
                        : ReignWarEconomyPolicy.CompatibleCivilianBudgetQuotes(factor, zero, half, budget);
                    if (!compatible)
                    {
                        capture.Evidence["unsupportedQuote"] = new JObject { ["schemaVersion"] = 1,
                            ["categoryId"] = category.StringId, ["demand"] = demand[category],
                            ["unit"] = factor, ["zero"] = zero, ["half"] = half, ["whole"] = budget,
                            ["model"] = model.GetType().AssemblyQualifiedName };
                        throw new InvalidOperationException("Unsupported nonlinear civilian purchasing budget.");
                    }
                    factors[category] = factor;
                    nativeBudgets[category] = budget;
                    budgets.Add(new ReignWarEconomyPolicy.CivilianBudget { Id = category.StringId,
                        Budget = ReignWarEconomyPolicy.NativeSpendableBudget(budget), Food = Food(category) });
                }
                var food = town.Owner.ItemRoster.Where(x => x.Amount > 0 && Food(x.EquipmentElement.Item.GetItemCategory()))
                    .Select(x => new ReignWarEconomyPolicy.MarketFood { Id = x.EquipmentElement.Item.StringId,
                        Category = x.EquipmentElement.Item.GetItemCategory().StringId, Stock = x.Amount,
                        Price = town.MarketData.GetPrice(x.EquipmentElement.Item) }).ToList();
                double required = RequiredUnits(town);
                double noMarketNet = NativeCampaign.Current.Models.SettlementFoodModel.CalculateTownFoodStocksChange(town, false).ResultNumber;
                var nativePlan = ReignWarEconomyPolicy.PrioritizeSubsistence(budgets, food, 0);
                double projected = ReignWarEconomyPolicy.NativePlanFoodProjection(town.FoodStocks, town.FoodStocksUpperLimit(), noMarketNet, nativePlan.NativePlannedUnits);
                bool threatened = ReignWarEconomyPolicy.NeedsSubsistencePriority(town.FoodStocks, town.FoodStocksUpperLimit(), noMarketNet, nativePlan.NativePlannedUnits);
                bool eligible = !_fixtureBaseline && threatened;
                var plan = eligible ? ReignWarEconomyPolicy.PrioritizeSubsistence(budgets, food, Math.Max(required, nativePlan.NativePlannedUnits)) : nativePlan;
                var changes = categories.ToDictionary(x => x, x => plan.Budgets[x.StringId] > 0
                    || ReignWarEconomyPolicy.NativeSpendableBudget(nativeBudgets[x]) > 0
                        ? (float)(plan.Budgets[x.StringId] / factors[x]) : demand[x]);
                var allocated = changes.ToDictionary(x => x.Key,
                    x => (double)model.CalculateDailySettlementBudgetForItemCategory(town, x.Value, x.Key));
                double after = allocated.Values.Sum(ReignWarEconomyPolicy.NativeSpendableBudget);
                if (changes.Values.Any(x => float.IsNaN(x) || float.IsInfinity(x))
                    || allocated.Any(x => double.IsNaN(x.Value) || double.IsInfinity(x.Value)
                        || Math.Abs(ReignWarEconomyPolicy.NativeSpendableBudget(x.Value) - plan.Budgets[x.Key.StringId])
                            > Math.Max(.001, plan.Budgets[x.Key.StringId] * .00001))
                    || Math.Abs(after - plan.TotalBefore) > Math.Max(.01, plan.TotalBefore * .00001))
                    throw new InvalidOperationException("Civilian purchasing budget was not conserved.");
                capture.Evidence["requiredFoodUnits"] = required;
                capture.Evidence["noMarketFoodNet"] = noMarketNet;
                capture.Evidence["nativePlannedFoodNet"] = noMarketNet + nativePlan.NativePlannedUnits;
                capture.Evidence["nativePlanProjectedStock"] = projected;
                capture.Evidence["subsistenceEligible"] = eligible;
                capture.Evidence["subsistenceTrigger"] = !threatened ? "healthy_current_and_native_plan"
                    : town.FoodStocks < .2 * town.FoodStocksUpperLimit() ? "current_reserves" : "native_plan_projection";
                capture.Evidence["nativePlannedFoodUnits"] = plan.NativePlannedUnits;
                capture.Evidence["plannedFoodUnits"] = plan.PlannedUnits;
                capture.Evidence["unmetPlannedFoodUnits"] = Math.Max(0, required - plan.PlannedUnits);
                capture.Evidence["totalBudgetBefore"] = plan.TotalBefore;
                capture.Evidence["nativeSignedBudget"] = nativeBudgets.Values.Sum();
                capture.Evidence["nativeSkippedCategories"] = nativeBudgets.Count(x => ReignWarEconomyPolicy.NativeSpendableBudget(x.Value) == 0);
                capture.Evidence["negativeDemandFoodPriority"] = nativeBudgets.Any(x => x.Value < 0 && Food(x.Key)
                    && plan.Budgets[x.Key.StringId] > 0);
                capture.Evidence["totalBudgetAfter"] = after;
                capture.Evidence["redirectedBudget"] = plan.Redirected;
                capture.Evidence["reason"] = _fixtureBaseline ? "native_fixture_baseline" : eligible ? "subsistence_priority" : "native_allocation";
                // No partial application on an unsupported quote. The native local dictionary
                // is the only input changed; there are no stock or wallet writes here.
                if (plan.Redirected > 0)
                    foreach (var pair in changes) demand[pair.Key] = pair.Value;
            }
            catch (Exception ex)
            {
                capture.Evidence["unsupported"] = true;
                capture.Evidence["reason"] = ex.GetType().Name + ": " + ex.Message;
            }
            return capture;
        }

        internal static void After(Dictionary<ItemCategory, int> saleLog, Consumption capture)
        {
            if (capture == null) return;
            ReignWarEconomyFundingAudit.EndConsumption(capture.Town, capture.CashObservation);
            capture.Evidence["phase"] = "completed";
            long removed = capture.FoodBefore - FoodUnits(capture.Town);
            long logged = saleLog.Where(x => Food(x.Key)).Sum(x => (long)x.Value);
            capture.Evidence["actualFoodRemoved"] = removed;
            capture.Evidence["nativeFoodSaleLog"] = logged;
            capture.Evidence["actualMarketCredit"] = capture.Town.Gold - capture.GoldBefore;
            capture.Evidence["foodConserved"] = removed == logged && removed >= 0;
            capture.Evidence["actualUnmetFoodUnits"] = capture.Evidence["requiredFoodUnits"] == null ? JValue.CreateNull()
                : JToken.FromObject(Math.Max(0, capture.Evidence.Value<double>("requiredFoodUnits") - removed));
            RecordCensus(capture);
        }

        private static void RecordCensus(Consumption capture)
        {
            try { CheckCampaign(); RecordCensusCore(capture); } catch { _censusErrors++; }
        }
        private static void RecordCensusCore(Consumption capture)
        {
            if (_probeActive) { _censusSuppressed++; return; }
            if (NativeCampaign.Current?.GameStarted != true) return;
            Town town = capture.Town;
            double day = CampaignTime.Now.ToDays, stock = town.FoodStocks;
            if (double.IsNaN(day) || double.IsInfinity(day) || double.IsNaN(stock) || double.IsInfinity(stock))
            { _censusErrors++; return; }
            double capturedDay = capture.Evidence.Value<double>("day");
            if (double.IsNaN(capturedDay) || double.IsInfinity(capturedDay) || Math.Floor(capturedDay) != Math.Floor(day))
            { _censusErrors++; return; }
            string id = town.Settlement.StringId, clan = town.OwnerClan?.StringId ?? "", realm = town.OwnerClan?.Kingdom?.StringId ?? "";
            if (!Census.TryGetValue(id, out var row))
            {
                if (Census.Count >= 256) { _censusDrops++; return; }
                Census[id] = row = new JObject { ["settlementId"] = id, ["isTown"] = town.IsTown,
                    ["firstDay"] = day, ["lastDay"] = day, ["firstFood"] = stock, ["minimumFood"] = stock,
                    ["firstKingdom"] = realm, ["lastKingdom"] = realm, ["firstOwnerClan"] = clan, ["lastOwnerClan"] = clan,
                    ["samples"] = 0, ["zeroObservations"] = 0, ["firstZeroDay"] = -1d,
                    ["maximumGapDays"] = 0d, ["timeRegression"] = false,
                    ["siegeObservations"] = 0, ["raidAffectedObservations"] = 0, ["ownershipChanges"] = 0,
                    ["lastDistinctDay"] = -1L, ["consecutiveZeroDays"] = 0, ["maximumConsecutiveZeroDays"] = 0,
                    ["duplicateDaySamples"] = 0, ["subsistencePolicyVersion"] = Version,
                    ["subsistenceEligibleDays"] = 0, ["subsistenceRedirectedDays"] = 0,
                    ["observedSubsistenceTransitions"] = 0, ["subsistenceUnsupportedDays"] = 0,
                    ["lastSubsistenceEligible"] = false };
            }
            long distinctDay = (long)Math.Floor(day), previousDay = row.Value<long>("lastDistinctDay");
            if (distinctDay != previousDay)
            {
                int consecutive = ReignWarEconomyPolicy.NextFoodZeroStreak(distinctDay, previousDay, stock <= 0, row.Value<int>("consecutiveZeroDays"));
                row["consecutiveZeroDays"] = consecutive;
                row["maximumConsecutiveZeroDays"] = Math.Max(row.Value<int>("maximumConsecutiveZeroDays"), consecutive);
                row["lastDistinctDay"] = distinctDay;
                var consumption = capture.Evidence; //exact callback receipt; never reuse a prior day's Latest entry
                bool eligible = consumption?.Value<bool?>("subsistenceEligible") == true;
                if (previousDay >= 0 && eligible != row.Value<bool>("lastSubsistenceEligible"))
                    row["observedSubsistenceTransitions"] = row.Value<int>("observedSubsistenceTransitions") + 1;
                if (eligible) row["subsistenceEligibleDays"] = row.Value<int>("subsistenceEligibleDays") + 1;
                if ((consumption?.Value<double?>("redirectedBudget") ?? 0) > 0)
                    row["subsistenceRedirectedDays"] = row.Value<int>("subsistenceRedirectedDays") + 1;
                if (consumption?.Value<bool?>("unsupported") == true)
                    row["subsistenceUnsupportedDays"] = row.Value<int>("subsistenceUnsupportedDays") + 1;
                row["lastSubsistenceEligible"] = eligible;
                row["lastSubsistenceTrigger"] = consumption?.Value<string>("subsistenceTrigger") ?? "unavailable";
                row["lastNativePlanFoodNet"] = consumption?["nativePlannedFoodNet"]?.DeepClone();
                row["lastNativePlanProjectedStock"] = consumption?["nativePlanProjectedStock"]?.DeepClone();
            }
            else row["duplicateDaySamples"] = row.Value<int>("duplicateDaySamples") + 1;
            double last = row.Value<double>("lastDay");
            if (day < last) row["timeRegression"] = true;
            row["maximumGapDays"] = Math.Max(row.Value<double>("maximumGapDays"), day - last);
            if (row.Value<string>("lastKingdom") != realm || row.Value<string>("lastOwnerClan") != clan)
                row["ownershipChanges"] = row.Value<int>("ownershipChanges") + 1;
            row["lastDay"] = day; row["lastFood"] = stock; row["capacity"] = town.FoodStocksUpperLimit();
            row["lastKingdom"] = realm; row["lastOwnerClan"] = clan;
            row["samples"] = row.Value<int>("samples") + 1;
            row["minimumFood"] = Math.Min(row.Value<double>("minimumFood"), stock);
            bool raid = town.Settlement.BoundVillages.Any(x => x.VillageState != Village.VillageStates.Normal);
            if (town.IsUnderSiege) row["siegeObservations"] = row.Value<int>("siegeObservations") + 1;
            if (raid) row["raidAffectedObservations"] = row.Value<int>("raidAffectedObservations") + 1;
            if (stock <= 0)
            {
                row["zeroObservations"] = row.Value<int>("zeroObservations") + 1;
                if (row.Value<double>("firstZeroDay") < 0)
                {
                    row["firstZeroDay"] = day; row["firstZeroKingdom"] = realm;
                    row["firstZeroBesieged"] = town.IsUnderSiege; row["firstZeroRaidAffected"] = raid;
                }
            }
        }
        internal static JObject DailyCensus(bool detailed)
        {
            // Snapshot callers and native consumption both capture on the game thread;
            // background upload receives only these cloned immutable values.
            CheckCampaign();
            return new JObject { ["version"] = 2, ["subsistencePolicyVersion"] = Version, ["processLocal"] = true, ["maximumSettlements"] = 256,
                ["available"] = Census.Count > 0, ["observedSettlements"] = Census.Count,
                ["capturedDay"] = NativeCampaign.Current == null ? -1d : CampaignTime.Now.ToDays,
                ["suppressedFixtureSamples"] = _censusSuppressed,
                ["dropped"] = _censusDrops, ["errors"] = _censusErrors,
                ["samplePhase"] = "post MakeConsumption, before caller sell-log update; food-stock updates remain on native schedule",
                ["coverageNote"] = "Observed stock only, not casualties or a completed balance certificate. Zero-day streaks use the first native sample of each distinct day; duplicates are counted separately, missing days break a streak. Check first/last days, gaps, regression and raid/siege/ownership history; reload resets observations. Controlled civilian fixtures are excluded.",
                ["settlements"] = detailed ? new JArray(Census.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value.DeepClone())) : new JArray() };
        }

        internal static JObject Observe(Town town)
        {
            CheckCampaign();
            if (!Latest.TryGetValue(town.StringId, out var value))
                return new JObject { ["version"] = Version, ["available"] = false, ["reason"] = "no_native_consumption_in_this_process" };
            var result = (JObject)value.DeepClone();
            result["available"] = true;
            result["stale"] = CampaignTime.Now.ToDays - value.Value<double>("day") > 1.1;
            return result;
        }

        internal static JObject Probe(JObject command)
        {
            var town = Settlement.Find(command.Value<string>("settlementId"))?.Town;
            if (town == null || !town.IsTown || town.IsUnderSiege || town.OwnerClan == Clan.PlayerClan
                || town.Settlement.Party.MapEvent != null)
                throw new InvalidOperationException("Choose a safe NPC town for the civilian food probe.");
            CheckCampaign();
            var inventory = town.Owner.ItemRoster.ToList(); int gold = town.Gold; float reserves = town.FoodStocks;
            var censusBefore = DailyCensus(true);
            long suppressedBefore = _censusSuppressed;
            bool hadLatest = Latest.TryGetValue(town.StringId, out var originalLatest);
            var grain = DefaultItems.Grain;
            var tools = Game.Current.ObjectManager.GetObjectTypeList<ItemObject>().First(x => x.StringId == "tools");
            var fish = Game.Current.ObjectManager.GetObjectTypeList<ItemObject>().First(x => x.StringId == "fish");
            var observations = new JArray();
            double day = CampaignTime.Now.ToDays;
            bool priorProbe = _probeActive; _probeActive = true;
            try
            {
                town.FoodStocks = 0;
                double maximumNeeded = RequiredUnits(town);
                double baseNet = NativeCampaign.Current.Models.SettlementFoodModel.CalculateTownFoodStocksChange(town, false).ResultNumber;
                if (double.IsNaN(maximumNeeded) || double.IsInfinity(maximumNeeded) || maximumNeeded < 4 || maximumNeeded > 150
                    || !ReignWarEconomyPolicy.NeedsSubsistencePriority(.35 * town.FoodStocksUpperLimit(), town.FoodStocksUpperLimit(), baseNet, .25 * Math.Ceiling(Math.Max(0, -baseNet))))
                    throw new InvalidOperationException("Choose a town needing4-150 daily food units for bounded fixtures.");
                foreach (string scenario in new[] { "supplied", "empty_market", "insufficient_budget", "healthy_reserves",
                    "negative_food_demand", "negative_healthy_reserves", "projected_shortage", "projected_empty_market" })
                {
                    var pair = new List<JObject>();
                    double needed = 0;
                    foreach (bool priority in new[] { false, true })
                    {
                        bool healthy = scenario == "healthy_reserves" || scenario == "negative_healthy_reserves";
                        bool negative = scenario == "negative_food_demand" || scenario == "negative_healthy_reserves";
                        bool projected = scenario == "projected_shortage" || scenario == "projected_empty_market";
                        bool empty = scenario == "empty_market" || scenario == "projected_empty_market";
                        town.FoodStocks = (healthy ? .4f : projected ? .35f : 0) * town.FoodStocksUpperLimit();
                        needed = RequiredUnits(town);
                        if (double.IsNaN(needed) || double.IsInfinity(needed) || needed < 4 || needed > maximumNeeded || needed > 150)
                            throw new InvalidOperationException("Fixture food requirement changed outside its validated4-150-unit bound.");
                        town.Owner.ItemRoster.Clear();
                        int grainStock = empty ? 0 : (int)Math.Ceiling(needed * 3);
                        int fishStock = healthy && negative ? (int)Math.Ceiling(needed) : 0;
                        if (grainStock > 0) town.Owner.ItemRoster.AddToCounts(grain, grainStock);
                        if (fishStock > 0) town.Owner.ItemRoster.AddToCounts(fish, fishStock);
                        town.Owner.ItemRoster.AddToCounts(tools, 200);
                        town.ChangeGold(gold - town.Gold);
                        int grainPrice = town.MarketData.GetPrice(grain), toolsPrice = town.MarketData.GetPrice(tools), fishPrice = town.MarketData.GetPrice(fish);
                        if (new[] { grainPrice, toolsPrice, fishPrice }.Any(x => x <= 0 || x > 100000))
                            throw new InvalidOperationException("Fixture native unit prices must be within1-100000.");
                        var model = NativeCampaign.Current.Models.SettlementEconomyModel;
                        var categories = new[] { grain.GetItemCategory(), tools.GetItemCategory(), fish.GetItemCategory() };
                        if (categories.Any(x => x == null) || categories.Distinct().Count() != 3)
                            throw new InvalidOperationException("Fixture requires distinct grain, tools and fish categories.");
                        double foodBudget = negative ? 0 : (scenario == "insufficient_budget" ? .1 : healthy ? needed : needed / 4) * grainPrice;
                        double otherBudget = (scenario == "insufficient_budget" ? .2 : needed * 2) * grainPrice;
                        double fishBudget = (double)fishStock * fishPrice;
                        if (foodBudget + otherBudget + fishBudget > 100000) throw new InvalidOperationException("Fixture purchasing budget exceeds100000.");
                        var unitBudgets = categories.Select(x => (double)model.CalculateDailySettlementBudgetForItemCategory(town, 1, x)).ToArray();
                        if (unitBudgets.Any(x => double.IsNaN(x) || double.IsInfinity(x) || x <= 0))
                            throw new InvalidOperationException("Fixture requires finite positive native category unit budgets.");
                        var demand = new Dictionary<ItemCategory, float> {
                            [categories[0]] = negative ? -160.116913f : (float)(foodBudget / unitBudgets[0]),
                            [categories[1]] = (float)(otherBudget / unitBudgets[1]), [categories[2]] = (float)(fishBudget / unitBudgets[2]) };
                        if (demand.Values.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                            throw new InvalidOperationException("Fixture native demand exceeds finite input bounds.");
                        var saleLog = new Dictionary<ItemCategory, int>();
                        var persistentDemand = ItemCategories.All.ToDictionary(x => x, x => town.MarketData.GetCategoryData(x).Demand);
                        ReignVillageProductionCalculatorModel.ObserveWithoutRandomAdvance(() => {
                            bool prior = _fixtureBaseline; _fixtureBaseline = !priority;
                            try { AccessTools.Method(typeof(ItemConsumptionBehavior), "MakeConsumption").Invoke(null, new object[] { town, demand, saleLog }); }
                            finally { _fixtureBaseline = prior; }
                            return 0;
                        });
                        var row = Observe(town);
                        row["case"] = scenario; row["priorityEnabled"] = priority;
                        row["forecastEvidencePresent"] = row["subsistenceEligible"]?.Type == JTokenType.Boolean
                            && row["subsistenceTrigger"]?.Type == JTokenType.String
                            && (row["nativePlanProjectedStock"]?.Type == JTokenType.Float || row["nativePlanProjectedStock"]?.Type == JTokenType.Integer);
                        row["persistentDemandUnchanged"] = persistentDemand.All(x => town.MarketData.GetCategoryData(x.Key).Demand == x.Value);
                        row["syntheticGrainUnits"] = grainStock; row["syntheticNonFoodUnits"] = 200;
                        row["syntheticFishUnits"] = fishStock;
                        row["syntheticFoodReserves"] = town.FoodStocks;
                        row["actualGrainRemaining"] = town.Owner.ItemRoster.GetItemNumber(grain);
                        row["actualToolsRemoved"] = 200 - town.Owner.ItemRoster.GetItemNumber(tools);
                        row["actualFishRemoved"] = fishStock - town.Owner.ItemRoster.GetItemNumber(fish);
                        row["cashMatchesRemovedGoods"] = row.Value<int>("actualMarketCredit")
                            == (grainStock - town.Owner.ItemRoster.GetItemNumber(grain)) * grainPrice
                                + row.Value<int>("actualToolsRemoved") * toolsPrice + row.Value<int>("actualFishRemoved") * fishPrice;
                        row["nativeCashWithinRoundingBudget"] = row.Value<int>("actualMarketCredit")
                            <= foodBudget + otherBudget + fishBudget + grainPrice + toolsPrice + fishPrice + 1;
                        pair.Add(row); observations.Add(row);
                    }
                    var before = pair[0]; var after = pair[1];
                    bool expected;
                    switch (scenario)
                    {
                        case "supplied":
                        case "negative_food_demand":
                        case "projected_shortage":
                            expected = after.Value<int>("actualFoodRemoved") >= needed
                                && after.Value<int>("actualFoodRemoved") > before.Value<int>("actualFoodRemoved")
                                && after.Value<int>("actualToolsRemoved") <= before.Value<int>("actualToolsRemoved")
                                && (scenario != "negative_food_demand" || before.Value<int>("actualFoodRemoved") == 0)
                                && (scenario != "projected_shortage" || after.Value<string>("subsistenceTrigger") == "native_plan_projection"
                                    && after.Value<float>("syntheticFoodReserves") > .2f * town.FoodStocksUpperLimit());
                            break;
                        case "insufficient_budget":
                            expected = after.Value<int>("actualFoodRemoved") < needed
                                && after.Value<double>("redirectedBudget") > 0
                                && after.Value<double>("unmetPlannedFoodUnits") > 0;
                            break;
                        case "empty_market":
                        case "projected_empty_market":
                        case "healthy_reserves":
                        case "negative_healthy_reserves":
                            expected = after.Value<double>("redirectedBudget") == 0
                                && after.Value<int>("actualFoodRemoved") == before.Value<int>("actualFoodRemoved")
                                && after.Value<int>("actualMarketCredit") == before.Value<int>("actualMarketCredit")
                                && after.Value<int>("actualToolsRemoved") == before.Value<int>("actualToolsRemoved")
                                && after.Value<int>("actualFishRemoved") == before.Value<int>("actualFishRemoved")
                                && after.Value<int>("actualGrainRemaining") == before.Value<int>("actualGrainRemaining")
                                && ((scenario != "empty_market" && scenario != "projected_empty_market") || after.Value<int>("actualFoodRemoved") == 0)
                                && (scenario == "empty_market" || after.Value<float>("syntheticFoodReserves") >= .2f * town.FoodStocksUpperLimit())
                                && (scenario != "projected_empty_market" || after.Value<string>("subsistenceTrigger") == "native_plan_projection")
                                && ((scenario != "healthy_reserves" && scenario != "negative_healthy_reserves") || !after.Value<bool>("subsistenceEligible"));
                            break;
                        default: throw new InvalidOperationException("Unknown fixed food fixture case.");
                    }
                    after["casePassed"] = expected && pair.All(x => x.Value<bool>("forecastEvidencePresent") && x.Value<string>("phase") == "completed"
                        && !x.Value<bool>("unsupported") && x.Value<bool>("foodConserved") && x.Value<bool>("nativeCashWithinRoundingBudget")
                        && x.Value<int>("actualFishRemoved") >= 0 && x.Value<int>("actualToolsRemoved") >= 0
                        && x.Value<bool>("persistentDemandUnchanged")
                        && x.Value<bool>("cashMatchesRemovedGoods")
                        && Math.Abs(x.Value<double>("totalBudgetBefore") - x.Value<double>("totalBudgetAfter")) < .1);
                }
            }
            finally
            {
                _probeActive = priorProbe;
                town.Owner.ItemRoster.Clear(); foreach (var item in inventory) town.Owner.ItemRoster.AddToCounts(item.EquipmentElement, item.Amount);
                town.ChangeGold(gold - town.Gold); town.FoodStocks = reserves;
                if (hadLatest) Latest[town.StringId] = originalLatest; else Latest.Remove(town.StringId);
            }
            string Key(IEnumerable<ItemRosterElement> rows) => string.Join(";", rows.Select(x => x.EquipmentElement.Item.StringId + ":"
                + x.EquipmentElement.ItemModifier?.StringId + ":" + x.Amount).OrderBy(x => x, StringComparer.Ordinal));
            bool restored = town.Gold == gold && town.FoodStocks == reserves && Key(inventory) == Key(town.Owner.ItemRoster)
                && CampaignTime.Now.ToDays == day;
            var censusAfter = DailyCensus(true);
            long suppressed = _censusSuppressed - suppressedBefore;
            bool censusUnchanged = JToken.DeepEquals(censusBefore["settlements"], censusAfter["settlements"])
                && censusBefore.Value<long>("dropped") == censusAfter.Value<long>("dropped")
                && censusBefore.Value<long>("errors") == censusAfter.Value<long>("errors");
            return new JObject { ["ok"] = restored && censusUnchanged && suppressed == 16 && observations.Count == 16
                    && observations.Count(x => x.Value<bool>("priorityEnabled")) == 8
                    && observations.Where(x => x.Value<bool>("priorityEnabled")).All(x => x.Value<bool>("casePassed")),
                ["foodMarketVersion"] = Version, ["observations"] = observations, ["resourceMutationsRestored"] = restored,
                ["nativeFoodCensusUnchanged"] = censusUnchanged,
                ["suppressedFixtureSamples"] = suppressed,
                ["requiresCheckpointRestore"] = true, ["providerCalls"] = 0, ["timeAdvanced"] = false };
        }
    }
}
