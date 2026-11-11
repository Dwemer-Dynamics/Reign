using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Economy
{
    internal static class ReignEconomyPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.economy";
        private const int MinimumPatrolSize = 5;
        private static Harmony _harmony;
        private static readonly HashSet<string> PatchedHooks = new HashSet<string>(StringComparer.Ordinal);
        private static string _lastPatchError = string.Empty;
        [ThreadStatic] private static int _recruitmentDepth;
        [ThreadStatic] private static int _ransomQuoteDepth;
        [ThreadStatic] private static int _quarteringCounterfactual;
        internal static bool QuarteringCounterfactual => _quarteringCounterfactual > 0;
        internal static float ObserveWithoutQuartering(Func<float> quote)
        {
            _quarteringCounterfactual++;
            try { return quote(); }
            finally { _quarteringCounterfactual--; }
        }
        internal static bool IsRansomQuote => _ransomQuoteDepth > 0;
        [ThreadStatic] private static AiPrisonerTransfer _aiPrisonerTransfer;
        [ThreadStatic] private static (Hero Owner, Settlement Locality) _nativeAiSpawn;
        internal static bool IsObservedRecruitment => _recruitmentDepth > 0;
        private static readonly Dictionary<string, MethodBase> HookTargets = new Dictionary<string, MethodBase>();

        private sealed class PlayerRecruitmentCommitState
        {
            internal Settlement Settlement;
            internal int PartyCount;
            internal EconomyReceipt Receipt;
        }

        private sealed class AiPrisonerTransfer
        {
            internal AiPrisonerTransfer Previous;
            internal MobileParty Party;
            internal CharacterObject Troop;
            internal int Requested;
            internal int PrisonersBefore;
            internal int MembersBefore;
            internal bool Reported;
        }

        internal static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                PatchedHooks.Clear();
                HookTargets.Clear();
                _lastPatchError = string.Empty;
                _harmony = new Harmony(HarmonyId);
                Patch(typeof(RecruitmentCampaignBehavior), "ApplyInternal", nameof(AiRecruitmentPrefix), prefix: true);
                Patch(typeof(RecruitmentCampaignBehavior), "ApplyInternal", nameof(AiRecruitmentPostfix), prefix: false);
                PatchFinalizer(typeof(RecruitmentCampaignBehavior), "ApplyInternal", nameof(AiRecruitmentFinalizer));
                Patch(typeof(RecruitPrisonersCampaignBehavior), "RecruitPrisonersAi", nameof(AiPrisonerTransferPrefix), prefix: true);
                PatchFinalizer(typeof(RecruitPrisonersCampaignBehavior), "RecruitPrisonersAi", nameof(AiPrisonerTransferFinalizer));
                PatchFinalizer(typeof(RecruitmentVM), "OnDone", nameof(PlayerRecruitmentFinalizer));
                Patch(typeof(RecruitmentVM), "OnRecruit", nameof(PlayerRecruitmentSelectionPrefix), prefix: true);
                Patch(typeof(RecruitmentVM), "OnDone", nameof(PlayerRecruitmentCommitPrefix), prefix: true);
                Patch(typeof(RecruitmentVM), "OnDone", nameof(PlayerRecruitmentCommitPostfix), prefix: false);
                Patch(typeof(LordPartyComponent.InitializationArgs), "InitializeLordPartyProperties", nameof(LordPartyInitializedPostfix), prefix: false);
                Patch(typeof(LordPartyComponent.InitializationArgs), "InitializeLordPartyProperties", nameof(LordPartyInitializedPrefix), prefix: true);
                PatchFinalizer(typeof(LordPartyComponent.InitializationArgs), "InitializeLordPartyProperties", nameof(LordPartyInitializationFinalizer));
                var lordSpawn = AccessTools.Method(typeof(HeroSpawnCampaignBehavior), "SpawnLordParty", new[] { typeof(Hero), typeof(bool) })
                    ?? throw new MissingMethodException(typeof(HeroSpawnCampaignBehavior).FullName, "SpawnLordParty");
                _harmony.Patch(lordSpawn, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(NativeAiSpawnLocalityTranspiler)));
                PatchedHooks.Add(nameof(NativeAiSpawnLocalityTranspiler));
                HookTargets[nameof(NativeAiSpawnLocalityTranspiler)] = lordSpawn;
                Patch(typeof(DefaultClanFinanceModel), "CalculatePartyWage", nameof(NativeWagePaymentPostfix), prefix: false);
                var quartering = AccessTools.Method(typeof(DefaultPartyWageModel), "GetTotalWage");
                _harmony.Patch(quartering, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(GarrisonQuarteringTranspiler)));
                PatchedHooks.Add(nameof(GarrisonQuarteringTranspiler));
                HookTargets[nameof(GarrisonQuarteringTranspiler)] = quartering;
                var clanDailyFinance = AccessTools.Method(typeof(ClanVariablesCampaignBehavior), "DailyTickClan")
                    ?? throw new MissingMethodException(typeof(ClanVariablesCampaignBehavior).FullName, "DailyTickClan");
                _harmony.Patch(clanDailyFinance, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(NativeDailyFinanceTranspiler)));
                PatchedHooks.Add(nameof(NativeDailyFinanceTranspiler));
                HookTargets[nameof(NativeDailyFinanceTranspiler)] = clanDailyFinance;
                Patch(typeof(DefaultClanFinanceModel), "AddIncomeFromKingdomBudget", nameof(NativeKingdomSupportPrefix), prefix: true);
                Patch(typeof(DefaultClanFinanceModel), "AddIncomeFromKingdomBudget", nameof(NativeKingdomSupportPostfix), prefix: false);
                Patch(typeof(DefaultClanFinanceModel), "AddIncomeFromParty", nameof(NativePartyIncomePostfix), prefix: false);
                Patch(typeof(DefaultClanFinanceModel), "AddPartyExpense", nameof(NativeLordPurseTopUpPostfix), prefix: false);
                PatchFinanceTranspiler(typeof(DefaultClanFinanceModel), "AddPartyExpense", nameof(LeaderlessWageStoreTranspiler));
                Patch(typeof(ItemConsumptionBehavior), "UpdateTownGold", nameof(NativeTownMarketBalancePrefix), prefix: true);
                Patch(typeof(ItemConsumptionBehavior), "UpdateTownGold", nameof(NativeTownMarketBalancePostfix), prefix: false);
                Patch(typeof(ItemConsumptionBehavior), "MakeConsumption", nameof(CivilianConsumptionPrefix), prefix: true);
                Patch(typeof(ItemConsumptionBehavior), "MakeConsumption", nameof(CivilianConsumptionPostfix), prefix: false);
                PatchFinanceTranspiler(typeof(SellPrisonersAction), "ApplyInternal", nameof(NativePrisonerPayoutTranspiler));
                PatchFinanceTranspiler(typeof(PartiesSellLootCampaignBehavior), "OnSettlementEntered", nameof(NativeLootSaleTranspiler));
                PatchFinanceTranspiler(typeof(PartiesBuyHorseCampaignBehavior), "OnSettlementEntered", nameof(NativeHorseMarketSaleTranspiler));
                Patch(typeof(DefaultRansomValueCalculationModel), nameof(DefaultRansomValueCalculationModel.PrisonerRansomValue), nameof(RansomQuotePrefix), prefix: true);
                PatchFinalizer(typeof(DefaultRansomValueCalculationModel), nameof(DefaultRansomValueCalculationModel.PrisonerRansomValue), nameof(RansomQuoteFinalizer));
                ReignWarEconomyFundingAudit.Install(_harmony);
                Patch(typeof(ArenaOverhaul.Tournament.TournamentRewardManager), "ResolveTournament", nameof(TournamentPaymentPrefix), prefix: true);
                Patch(typeof(ArenaOverhaul.Tournament.TournamentRewardManager), "ResolveTournament", nameof(TournamentPaymentPostfix), prefix: false);
                Patch(typeof(ArenaOverhaul.Patches.TournamentManagerPatch), "ExecuteShowPrizeItemTooltipPrefix", nameof(TournamentPrizePaymentPrefix), prefix: true);
                Patch(typeof(ArenaOverhaul.Patches.TournamentManagerPatch), "ExecuteShowPrizeItemTooltipPrefix", nameof(TournamentPrizePaymentPostfix), prefix: false);
                Patch(typeof(GarrisonRecruitmentCampaignBehavior), "TickAutoRecruitmentGarrisonChange", nameof(GarrisonRecruitmentPrefix), prefix: true);
                Patch(typeof(GarrisonRecruitmentCampaignBehavior), "TickGarrisonChangeForTown", nameof(BaseGarrisonRecruitmentPrefix), prefix: true);
                Patch(typeof(PartyUpgraderCampaignBehavior), "UpgradeTroop", nameof(AutomaticUpgradePrefix), prefix: true);
                Patch(typeof(PartiesBuyFoodCampaignBehavior), "BuyFoodInternal", nameof(FoodProvisionPrefix), prefix: true);
                PatchFinalizer(typeof(PartiesBuyFoodCampaignBehavior), "BuyFoodInternal", nameof(ProvisionFinalizer));
                Patch(typeof(PartiesBuyHorseCampaignBehavior), "BuyHorses", nameof(HorseProvisionPrefix), prefix: true);
                PatchFinalizer(typeof(PartiesBuyHorseCampaignBehavior), "BuyHorses", nameof(HorseProvisionFinalizer));
                Patch(typeof(SellItemsAction), "ApplyInternal", nameof(TariffPurchasePrefix), prefix: true);
                PatchFinalizer(typeof(SellItemsAction), "ApplyInternal", nameof(TariffPurchaseFinalizer));
                PatchFinanceTranspiler(typeof(SellItemsAction), "ApplyInternal", nameof(TariffPurchaseTranspiler));
                var poolSetter = AccessTools.PropertySetter(typeof(Town), nameof(Town.TradeTaxAccumulated))
                    ?? throw new MissingMethodException(typeof(Town).FullName, "set_TradeTaxAccumulated");
                _harmony.Patch(poolSetter, prefix: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(TariffPoolPrefix)),
                    postfix: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(TariffPoolPostfix)));
                foreach (string hook in new[] { nameof(TariffPoolPrefix), nameof(TariffPoolPostfix) })
                { PatchedHooks.Add(hook); HookTargets[hook] = poolSetter; }
                var production = AccessTools.Method(typeof(DefaultVillageProductionCalculatorModel), "CalculateDailyProductionAmount");
                _harmony.Patch(production, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(CommodityBaselineTranspiler)));
                PatchedHooks.Add(nameof(CommodityBaselineTranspiler));
                HookTargets[nameof(CommodityBaselineTranspiler)] = production;
                Patch(typeof(PatrolPartyComponent), nameof(PatrolPartyComponent.CreatePatrolParty), nameof(PatrolCreationPrefix), prefix: true);
                Patch(typeof(PatrolPartyComponent), nameof(PatrolPartyComponent.CreatePatrolParty), nameof(PatrolCreationPostfix), prefix: false);
                Patch(typeof(PatrolPartiesCampaignBehavior), "ReplenishParty", nameof(PatrolReplenishmentPrefix), prefix: true);
                Patch(typeof(PatrolPartiesCampaignBehavior), "RemoveSettlementParties", nameof(PatrolRemovalPrefix), prefix: true);
                ApplyNavalRecruitment();
                ReignLog.Info("Reign economy, manpower, food, and security patches loaded.");
            }
            catch (Exception ex)
            {
                _lastPatchError = ex.ToString();
                ReignLog.Warn("Reign economy patch setup failed: " + ex);
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
            }
        }

        private const string NavalShipTradeType = "NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior";
        internal static Type NavalRecruitmentType => AccessTools.TypeByName(NavalShipTradeType);
        internal static void ApplyNavalRecruitment()
        {
            ReignWarEconomyNaval.Install();
            ApplyCivilianFishingPopulation();
            var finance = NativeNavalFinanceType;
            if (_harmony != null && finance != null && !HookTargets.ContainsKey(nameof(NavalLeaderlessWageStoreTranspiler)))
                PatchFinanceTranspiler(finance, "AddPartyExpense", nameof(NavalLeaderlessWageStoreTranspiler));
            var type = NavalRecruitmentType;
            if (_harmony == null || type == null || HookTargets.ContainsKey(nameof(NavalGarrisonTranspiler))) return;
            var target = AccessTools.Method(type, "OnSettlementEntered",
                new[] { typeof(MobileParty), typeof(Settlement), typeof(Hero) })
                ?? throw new MissingMethodException(NavalShipTradeType, "OnSettlementEntered");
            _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(NavalGarrisonTranspiler)));
            HookTargets[nameof(NavalGarrisonTranspiler)] = target;
            PatchedHooks.Add(nameof(NavalGarrisonTranspiler));
        }

        internal static Type NativeFishingType => AccessTools.TypeByName("NavalDLC.CampaignBehaviors.FishingPartyCampaignBehavior");
        internal static Type NativeNavalFinanceType => AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCClanFinanceModel");
        private static int ExpectedEconomyHooks => 51 + (NavalRecruitmentType == null ? 0 : 1)
            + (NativeFishingType == null ? 0 : 2) + (NativeNavalFinanceType == null ? 0 : 1);

        private static IEnumerable<CodeInstruction> LeaderlessWageStoreTranspiler(IEnumerable<CodeInstruction> instructions)
            => RewriteLeaderlessWageStore(instructions);
        private static IEnumerable<CodeInstruction> NavalLeaderlessWageStoreTranspiler(IEnumerable<CodeInstruction> instructions)
            => RewriteLeaderlessWageStore(instructions);
        private static IEnumerable<CodeInstruction> RewriteLeaderlessWageStore(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var setter = AccessTools.PropertySetter(typeof(Hero), nameof(Hero.Gold));
            var sites = code.Where(x => x.Calls(setter)).ToArray();
            if (sites.Length != 1) throw new InvalidOperationException("Native leaderless-party wage store changed; expected one exact hero cash write.");
            sites[0].opcode = OpCodes.Call;
            sites[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(ObserveLeaderlessWageStore));
            return code;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ObserveLeaderlessWageStore(Hero payer, int value)
        {
            var capture = ReignWarEconomyFundingAudit.BeginLeaderlessWageStore(payer);
            try { payer.Gold = value; }
            finally { ReignWarEconomyFundingAudit.EndLeaderlessWageStore(payer, capture, value); }
        }

        private static void ApplyCivilianFishingPopulation()
        {
            var type = NativeFishingType;
            if (_harmony == null || type == null) return;
            foreach (string name in new[] { "OnDailySettlementTick", "TryReinforceParty" })
            {
                string key = name == "OnDailySettlementTick" ? nameof(FishingCreationHearthTranspiler) : nameof(FishingReinforcementHearthTranspiler);
                if (HookTargets.ContainsKey(key)) continue;
                var target = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
                _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), key));
                HookTargets[key] = target; PatchedHooks.Add(key);
            }
        }

        private static IEnumerable<CodeInstruction> FishingCreationHearthTranspiler(IEnumerable<CodeInstruction> instructions)
            => CivilianFishingHearthTranspiler(instructions);
        private static IEnumerable<CodeInstruction> FishingReinforcementHearthTranspiler(IEnumerable<CodeInstruction> instructions)
            => CivilianFishingHearthTranspiler(instructions);

        private static IEnumerable<CodeInstruction> CivilianFishingHearthTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var setter = AccessTools.PropertySetter(typeof(Village), nameof(Village.Hearth));
            var sites = code.Where(x => x.Calls(setter)).ToArray();
            if (sites.Length != 1) throw new InvalidOperationException("Native fishing population debit changed; expected one exact hearth write per callback.");
            // Keep native creation, cadence, crew size, fish output, ships and all other effects.
            // Only the native civilian crew's direct population subtraction is suppressed.
            sites[0].opcode = OpCodes.Call;
            sites[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(KeepCivilianFishingHearths));
            return code;
        }

        internal static long CivilianFishingSuppressedWrites { get; private set; }
        private static void KeepCivilianFishingHearths(Village village, float proposedHearths)
        { CivilianFishingSuppressedWrites++; }

        private static IEnumerable<CodeInstruction> NavalGarrisonTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var add = AccessTools.Method(typeof(TroopRoster), nameof(TroopRoster.AddToCounts),
                new[] { typeof(CharacterObject), typeof(int), typeof(bool), typeof(int), typeof(int), typeof(bool), typeof(int) });
            var sites = code.Where(x => x.Calls(add)).ToArray();
            if (sites.Length != 1) throw new InvalidOperationException("Native naval garrison enlistment shape changed; expected one roster call.");
            var site = sites[0];
            var caravan = new CodeInstruction(OpCodes.Ldarg_1);
            caravan.labels.AddRange(site.labels); caravan.blocks.AddRange(site.blocks);
            site.labels.Clear(); site.blocks.Clear();
            int index = code.IndexOf(site);
            code.Insert(index, caravan);
            code.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_2));
            site.opcode = OpCodes.Call;
            site.operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(EnlistNavalGarrison));
            return code;
        }

        private static int EnlistNavalGarrison(TroopRoster roster, CharacterObject troop, int count,
            bool insertAtFront, int woundedCount, int xpChange, bool removeDepleted, int index,
            MobileParty caravan, Settlement locality)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            {
                ReignWarEconomyTransactions.Totals("initial_campaign_naval_garrison").Exempt += Math.Max(0, count);
                return roster.AddToCounts(troop, count, insertAtFront, woundedCount, xpChange, removeDepleted, index);
            }
            var party = locality?.Town?.GarrisonParty;
            var state = ReignWarEconomyTransactions.State;
            if (party == null || !ReferenceEquals(party.MemberRoster, roster) || caravan?.IsCaravan != true
                || !caravan.HasNavalNavigationCapability || troop != locality.MapFaction.BasicTroop
                || count < 0 || count > 20 || woundedCount != 0 || xpChange != 0 || insertAtFront || !removeDepleted || index != -1)
            {
                if (state != null) { state.UnsupportedEvents++; state.LastError = "Unsupported native naval enlistment arguments"; }
                throw new InvalidOperationException("Unsupported native naval enlistment arguments; no troops created.");
            }
            string source = ReignWarEconomyTransactions.NavalGarrison;
            string arrival = source + ":" + ReignWarEconomyTransactions.GenerationKey(caravan) + ":" + locality.StringId;
            if (!ReignWarEconomyLedger.BeginNativeArrival(state, arrival, CampaignTime.Now.ToDays))
                return roster.FindIndexOfTroop(troop);
            int enlisted = 0;
            if (!locality.IsUnderSiege && locality.Party.MapEvent == null)
                for (; enlisted < count; enlisted++)
                    if (!ReignWarEconomyTransactions.Enlist(source, locality, party, locality.OwnerClan?.Leader, troop)) break;
            // Allow records the first failed recruit; record the rest of the refused native request once.
            int refused = count - enlisted;
            if (refused > 0) ReignWarEconomyTransactions.Totals(source).Blocked +=
                locality.IsUnderSiege || locality.Party.MapEvent != null ? refused : refused - 1;
            return roster.FindIndexOfTroop(troop);
        }

        internal static void Unapply()
        {
            ReignWarEconomyNaval.Reset();
            _harmony?.UnpatchAll(HarmonyId);
            ReignWarEconomyFundingAudit.Reset();
            ReignWarEconomyFoodMarket.Reset();
            ReignWarEconomyTariffs.Reset();
            _ransomQuoteDepth = 0;
            _harmony = null;
            HookTargets.Clear();
            PatchedHooks.Clear();
        }

        private static void PatchFinalizer(Type type, string method, string finalizer)
        {
            var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
            _harmony.Patch(target, finalizer: new HarmonyMethod(typeof(ReignEconomyPatches), finalizer));
            PatchedHooks.Add(finalizer);
            HookTargets[finalizer] = target;
        }

        private static bool IsInstalled(string name)
        {
            if (!HookTargets.TryGetValue(name, out var target)) return false;
            var patches = Harmony.GetPatchInfo(target);
            return patches != null && patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers)
                .Any(x => x.owner == HarmonyId && x.PatchMethod == AccessTools.Method(typeof(ReignEconomyPatches), name));
        }

        private static void Patch(Type targetType, string targetName, string patchName, bool prefix)
        {
            var target = AccessTools.Method(targetType, targetName);
            if (target == null)
            {
                throw new MissingMethodException(targetType.FullName, targetName);
            }

            HarmonyMethod patch = new HarmonyMethod(typeof(ReignEconomyPatches), patchName);
            if (prefix)
            {
                _harmony.Patch(target, prefix: patch);
            }
            else
            {
                _harmony.Patch(target, postfix: patch);
            }
            PatchedHooks.Add(patchName);
            HookTargets[patchName] = target;
        }

        internal static JObject BuildDiagnostics()
        {
            var models = TaleWorlds.CampaignSystem.Campaign.Current?.Models;
            var modelOwners = new JObject();
            Action<string, object, Type, string> owner = (name, actual, expected, naval) =>
                modelOwners[name] = ModelRoute(actual, expected, naval);
            owner("production", models?.VillageProductionCalculatorModel, typeof(ReignVillageProductionCalculatorModel), "NavalDLCVillageProductionCalculatorModel");
            owner("food", models?.SettlementFoodModel, typeof(ReignSettlementFoodModel), null);
            owner("security", models?.SettlementSecurityModel, typeof(ReignSettlementSecurityModel), "NavalDLCSettlementSecurityModel");
            owner("loyalty", models?.SettlementLoyaltyModel, typeof(ReignSettlementLoyaltyModel), null);
            owner("prosperityAndHearth", models?.SettlementProsperityModel, typeof(ReignSettlementProsperityModel), "NavalDLCSettlementProsperityModel");
            owner("construction", models?.BuildingConstructionModel, typeof(ReignBuildingConstructionModel), "NavalDLCBuildingConstructionModel");
            owner("wages", models?.PartyWageModel, typeof(ReignBeta.ClanAccords.ReignClanAccordsWageModel), "NavalDLCPartyWageModel");
            owner("finance", models?.ClanFinanceModel, typeof(ReignBeta.ClanAccords.ReignClanAccordsFinanceModel), "NavalDLCClanFinanceModel");
            return new JObject
            {
                ["active"] = _harmony != null,
                ["schemaVersion"] = 2,
                ["allHooksHealthy"] = _harmony != null && HookTargets.Count == ExpectedEconomyHooks
                    && HookTargets.Keys.All(IsInstalled) && ReignWarEconomyNaval.Healthy,
                ["navalEconomy"] = ReignWarEconomyNaval.Coverage(),
                ["expectedHookCount"] = ExpectedEconomyHooks,
                ["tariffCollectionVersion"] = ReignWarEconomyTariffs.Version,
                ["tariffCollectionHook"] = IsInstalled(nameof(TariffPurchasePrefix)) && IsInstalled(nameof(TariffPurchaseFinalizer))
                    && IsInstalled(nameof(TariffPurchaseTranspiler)) && IsInstalled(nameof(TariffPoolPrefix)) && IsInstalled(nameof(TariffPoolPostfix))
                    && IsInstalled(nameof(FoodProvisionPrefix)) && IsInstalled(nameof(ProvisionFinalizer))
                    && IsInstalled(nameof(HorseProvisionPrefix)) && IsInstalled(nameof(HorseProvisionFinalizer)),
                ["nativeMarketSalesVersion"] = 1,
                ["wageObservationVersion"] = 2,
                ["leaderlessWageStoreObserverVersion"] = 1,
                ["leaderlessWageStoreHook"] = IsInstalled(nameof(LeaderlessWageStoreTranspiler))
                    && (NativeNavalFinanceType == null || IsInstalled(nameof(NavalLeaderlessWageStoreTranspiler))),
                ["garrisonWagePayer"] = "current_settlement_owner",
                ["historicalWageCoverageComplete"] = false,
                ["nativeHorseMarketSaleHook"] = IsInstalled(nameof(NativeHorseMarketSaleTranspiler)),
                ["foodMarketVersion"] = ReignWarEconomyFoodMarket.Version,
                ["productionCurveVersion"] = ReignWarEconomyPolicy.ProductionCurveVersion,
                ["recruitmentPriceVersion"] = ReignWarEconomyPolicy.RecruitmentPriceVersion,
                ["civilianConsumptionHook"] = IsInstalled(nameof(CivilianConsumptionPrefix)) && IsInstalled(nameof(CivilianConsumptionPostfix)),
                ["ransomPricingHook"] = IsInstalled(nameof(RansomQuotePrefix)) && IsInstalled(nameof(RansomQuoteFinalizer)),
                ["ransomModel"] = models?.RansomValueCalculationModel?.GetType().AssemblyQualifiedName ?? "unavailable",
                ["modelOwners"] = modelOwners,
                ["allModelsHealthy"] = modelOwners.Properties().All(x => x.Value.Value<bool>("healthy")),
                ["behavioralCoverageComplete"] = false,
                ["nativeFinanceObservationHook"] = IsInstalled(nameof(NativeDailyFinanceTranspiler))
                    && IsInstalled(nameof(NativeKingdomSupportPrefix)) && IsInstalled(nameof(NativeKingdomSupportPostfix))
                    && IsInstalled(nameof(NativePartyIncomePostfix)) && IsInstalled(nameof(NativeLordPurseTopUpPostfix))
                    && IsInstalled(nameof(NativeTownMarketBalancePrefix))
                    && IsInstalled(nameof(NativeTownMarketBalancePostfix)) && IsInstalled(nameof(NativePrisonerPayoutTranspiler))
                    && IsInstalled(nameof(NativeLootSaleTranspiler)) && IsInstalled(nameof(NativeHorseMarketSaleTranspiler)),
                ["coverageNote"] = "Installed hooks are structural evidence only; native source fixtures and save/reload receipts are required.",
                ["patchedHookCount"] = PatchedHooks.Count,
                ["patchedHooks"] = new JArray(PatchedHooks.OrderBy(x => x)),
                ["lastPatchError"] = _lastPatchError ?? string.Empty,
                ["sourceCoverage"] = new JObject {
                    ["player_volunteer"] = IsInstalled(nameof(PlayerRecruitmentCommitPostfix)),
                    ["ai_volunteer"] = IsInstalled(nameof(AiRecruitmentPostfix)),
                    ["native_auto_garrison"] = IsInstalled(nameof(GarrisonRecruitmentPrefix)),
                    ["native_base_garrison"] = IsInstalled(nameof(BaseGarrisonRecruitmentPrefix)),
                    ["native_naval_garrison"] = NavalRecruitmentType == null ? (JToken)"not_applicable_no_naval_module" : IsInstalled(nameof(NavalGarrisonTranspiler)),
                    ["native_naval_pirate_pardon"] = "real_party_transfer_no_enlistment",
                    ["native_fishing_crews"] = NativeFishingType == null ? "not_applicable_no_naval_module" : "civilian_production_zero_hearth_debit",
                    ["native_caravan_crews"] = "commercial_population_zero_hearth_debit",
                    ["native_naval_story"] = "explicit_story_quest_exceptions",
                    ["ai_prisoner_transfer"] = IsInstalled(nameof(AiPrisonerTransferPrefix)) && IsInstalled(nameof(AiPrisonerTransferFinalizer)),
                    ["household_retinue"] = IsInstalled(nameof(LordPartyInitializedPrefix)) && IsInstalled(nameof(LordPartyInitializedPostfix)) && IsInstalled(nameof(LordPartyInitializationFinalizer)) && IsInstalled(nameof(NativeAiSpawnLocalityTranspiler)),
                    ["castle_guard"] = ReignEconomyCampaignBehavior.Instance != null,
                    ["militia_conversion"] = "no supported native/Reign conversion discovered; unidentified routes fail coverage"
                },
                ["nativeAssembly"] = typeof(RecruitmentCampaignBehavior).Assembly.ManifestModule.ModuleVersionId.ToString(),
                ["wageModel"] = TaleWorlds.CampaignSystem.Campaign.Current?.Models.PartyWageModel?.GetType().FullName ?? "unavailable"
            };
        }

        // War Sails delegates these model methods to the initialized MBGameModel base.
        // Only the reviewed official wrapper and its immediate, exact Reign instance count.
        // A model elsewhere in the chain does not prove an arbitrary wrapper calls it.
        private static JObject ModelRoute(object actual, Type expected, string knownNavalWrapper)
        {
            Type actualType = actual?.GetType();
            Type baseType = null;
            if (actualType != null && !string.IsNullOrEmpty(knownNavalWrapper)
                && actualType.Assembly.GetName().Name == "NavalDLC"
                && actualType.FullName == "NavalDLC.GameComponents." + knownNavalWrapper)
            {
                for (Type type = actualType.BaseType; type != null; type = type.BaseType)
                {
                    if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(MBGameModel<>)) continue;
                    var baseProperty = type.GetProperty("BaseModel", BindingFlags.Instance | BindingFlags.NonPublic);
                    baseType = baseProperty?.GetValue(actual, null)?.GetType();
                    break;
                }
            }
            bool direct = actualType == expected;
            bool delegated = baseType == expected;
            return new JObject {
                ["actual"] = actualType?.FullName ?? "unavailable", ["expected"] = expected.FullName,
                ["baseModel"] = baseType?.FullName, ["healthy"] = direct || delegated,
                ["route"] = direct ? "direct" : delegated ? "reviewed_naval_base" : "unsupported",
                ["outerAssemblyMvid"] = actualType?.Assembly.ManifestModule.ModuleVersionId.ToString()
            };
        }

        private static bool AiRecruitmentPrefix(
            MobileParty side1Party,
            Settlement settlement,
            Hero individual,
            CharacterObject troop,
            ref int number,
            int bitCode,
            RecruitmentCampaignBehavior.RecruitingDetail detail,
            out EconomyReceipt __state)
        {
            __state = null;
            _recruitmentDepth++;
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            {
                ReignWarEconomyTransactions.Totals("initial_campaign").Exempt += Math.Max(0, number);
                return true;
            }
            if (detail != RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividual
                && detail != RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividualToGarrison)
            {
                bool known = detail == RecruitmentCampaignBehavior.RecruitingDetail.MercenaryFromTavern
                    || (detail == RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromMap && side1Party.MapFaction?.IsMinorFaction == true);
                if (known) ReignWarEconomyTransactions.Totals("native_" + detail).Exempt += Math.Max(0, number);
                else if (ReignWarEconomyTransactions.State != null)
                { ReignWarEconomyTransactions.State.UnsupportedEvents++; ReignWarEconomyTransactions.State.LastError = "Unsupported recruitment detail " + detail; }
                return known;
            }

            if (individual == null || bitCode < 0 || bitCode >= individual.VolunteerTypes.Length
                || individual.VolunteerTypes[bitCode] != troop)
            { ReignWarEconomyTransactions.Totals(ReignWarEconomyTransactions.Ai).Blocked += Math.Max(0, number); return false; }
            int requested = number;
            bool garrison = detail == RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividualToGarrison;
            Hero payer = garrison ? side1Party.ActualClan?.Leader : side1Party.LeaderHero;
            number = ReignWarEconomyTransactions.Allow(ReignWarEconomyTransactions.Ai, settlement, side1Party, payer, troop,
                Math.Min(1, requested), side1Party != MobileParty.MainParty, garrison);
            if (number > 0)
                __state = ReignWarEconomyTransactions.Begin(ReignWarEconomyTransactions.Ai, settlement, side1Party, payer, troop, number, individual);
            return number > 0;
        }

        private static void AiRecruitmentPostfix(MobileParty side1Party, Settlement settlement, CharacterObject troop,
            RecruitmentCampaignBehavior.RecruitingDetail detail, EconomyReceipt __state)
        {
            bool garrison = detail == RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividualToGarrison;
            ReignWarEconomyTransactions.Complete(__state, settlement, side1Party,
                garrison ? side1Party.ActualClan?.Leader : side1Party.LeaderHero, troop, garrison);
        }

        private static Exception AiRecruitmentFinalizer(Exception __exception, EconomyReceipt __state)
        {
            try
            {
                if (__exception != null) ReignWarEconomyTransactions.Rollback(__state, "native recruitment failed");
            }
            finally { _recruitmentDepth = Math.Max(0, _recruitmentDepth - 1); }
            return __exception;
        }

        private static void AiPrisonerTransferPrefix(MobileParty mobileParty, CharacterObject troop, int num,
            out AiPrisonerTransfer __state)
        {
            __state = new AiPrisonerTransfer {
                Previous = _aiPrisonerTransfer,
                Party = mobileParty, Troop = troop, Requested = num,
                PrisonersBefore = troop == null ? 0 : mobileParty?.PrisonRoster.GetTroopCount(troop) ?? 0,
                MembersBefore = troop == null ? 0 : mobileParty?.MemberRoster.GetTroopCount(troop) ?? 0
            };
            _aiPrisonerTransfer = __state;
        }

        private static void AiPrisonerTransferFinalizer(AiPrisonerTransfer __state)
        {
            if (__state != null && ReferenceEquals(_aiPrisonerTransfer, __state))
                _aiPrisonerTransfer = __state.Previous;
        }

        internal static bool TryObserveAiPrisonerTransfer(Hero recruiter, Settlement locality, Hero source,
            CharacterObject troop, int amount)
        {
            var transfer = _aiPrisonerTransfer;
            if (transfer?.Party == null || troop == null || locality != null || source != null || recruiter != transfer.Party.LeaderHero
                || troop != transfer.Troop || amount != transfer.Requested
                || transfer.PrisonersBefore - transfer.Party.PrisonRoster.GetTroopCount(troop) != amount
                || transfer.Party.MemberRoster.GetTroopCount(troop) - transfer.MembersBefore != amount) return false;
            if (!transfer.Reported)
            {
                transfer.Reported = true;
                ReignWarEconomyTransactions.Totals("native_ai_prisoner_transfer").Exempt += amount;
            }
            return true;
        }

        private static Exception PlayerRecruitmentFinalizer(Exception __exception, PlayerRecruitmentCommitState __state)
        {
            if (__exception != null) ReignWarEconomyTransactions.Rollback(__state?.Receipt, "player recruitment failed");
            return __exception;
        }

        private static bool PlayerRecruitmentSelectionPrefix(RecruitmentVM __instance)
        {
            ReignEconomyCampaignBehavior behavior = ReignEconomyCampaignBehavior.Instance;
            Settlement settlement = Settlement.CurrentSettlement;
            if (behavior == null || settlement == null)
            {
                return true;
            }

            int alreadySelected = __instance.TroopsInCart?.Count ?? 0;
            if (alreadySelected < behavior.GetRecruitCapacity(settlement))
            {
                return true;
            }

            InformationManager.DisplayMessage(new InformationMessage(
                "The local villages cannot provide more recruits without exhausting their recovered hearth reserve."));
            return false;
        }

        private static bool PlayerRecruitmentCommitPrefix(RecruitmentVM __instance, out PlayerRecruitmentCommitState __state)
        {
            __state = null;
            int count = __instance.TroopsInCart.Count;
            if (ReignWarEconomyTransactions.State?.Pending.Any(x => x.Party == MobileParty.MainParty.StringId) == true
                || count > (ReignEconomyCampaignBehavior.Instance?.GetRecruitCapacity(Settlement.CurrentSettlement) ?? 0)
                || __instance.TroopsInCart.Sum(x => x.Cost) > Hero.MainHero.Gold
                || __instance.TroopsInCart.Any(x => x.Owner.OwnerHero.VolunteerTypes[x.Index] != x.Character))
            {
                ReignWarEconomyTransactions.Totals(ReignWarEconomyTransactions.Player).Blocked += count;
                InformationManager.DisplayMessage(new InformationMessage("Recruitment conditions changed. Review the available volunteers and local hearth reserve."));
                return false;
            }
            __state = new PlayerRecruitmentCommitState
            {
                Settlement = Settlement.CurrentSettlement,
                PartyCount = MobileParty.MainParty?.MemberRoster.TotalManCount ?? 0,
                Receipt = ReignWarEconomyTransactions.Begin(ReignWarEconomyTransactions.Player, Settlement.CurrentSettlement,
                    MobileParty.MainParty, Hero.MainHero, null, count)
            };
            __state.Receipt.QuotedCost = -1;
            __state.Receipt.QuotedTotal = __instance.TroopsInCart.Sum(x => x.Cost);
            return true;
        }

        private static void PlayerRecruitmentCommitPostfix(PlayerRecruitmentCommitState __state)
        {
            if (__state?.Settlement == null || MobileParty.MainParty == null)
            {
                return;
            }

            ReignWarEconomyTransactions.Complete(__state.Receipt, __state.Settlement, MobileParty.MainParty, Hero.MainHero, null);
        }

        private static bool LordPartyInitializedPrefix(MobileParty mobileParty, Hero owner)
        {
            if (mobileParty == null || mobileParty == MobileParty.MainParty || owner?.Clan == null) return true;
            if (mobileParty.IsCurrentlyUsedByAQuest)
            { ReignWarEconomyTransactions.Totals("native_quest_party_initialization").Exempt++; return true; }
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            { ReignWarEconomyTransactions.Totals("initial_campaign_retinue").Exempt++; return true; }
            var state = ReignWarEconomyTransactions.State;
            if (state == null) return true;
            // CreateParty registers MBGUID after this initializer. Use object-local provisional identity until then.
            string key = ReignWarEconomyTransactions.GenerationKey(mobileParty);
            if (state.PartyGenerations.ContainsKey(key))
            {
                ReignEconomyCampaignBehavior.Instance.RecordDuplicateRetinueInitialization();
                return false;
            }
            state.PartyGenerations[key] = "pending";
            return true;
        }

        private static Exception LordPartyInitializationFinalizer(Exception __exception, MobileParty mobileParty)
        {
            if (__exception == null || mobileParty == null) return __exception;
            var state = ReignWarEconomyTransactions.State;
            string key = ReignWarEconomyTransactions.GenerationKey(mobileParty);
            if (__exception != null && state != null && state.PartyGenerations.TryGetValue(key, out string status) && status == "pending")
            {
                // Native initialization failed before funded replacement could run. Retain no free template troops.
                foreach (var element in mobileParty.MemberRoster.GetTroopRoster().Where(x => !x.Character.IsHero).ToList())
                    mobileParty.MemberRoster.AddToCounts(element.Character, -element.Number, false, -element.WoundedNumber, -element.Xp);
                state.PartyGenerations[key] = "failed:0";
                state.LastError = "Household initialization failed for " + key;
            }
            if (__exception != null && mobileParty != null) ReignWarEconomyTransactions.OnPartyCreationFailed(mobileParty);
            return __exception;
        }

        private static void NativeWagePaymentPostfix(MobileParty mobileParty, bool applyWithdrawals, int __result)
        {
            if (mobileParty == null || (!mobileParty.IsLordParty && !mobileParty.IsGarrison)) return;
            string payer = ReignWarEconomyLedger.WagePayer(mobileParty.IsGarrison, mobileParty.ActualClan?.StringId,
                mobileParty.GarrisonPartyComponent?.Settlement?.OwnerClan?.StringId);
            ReignWarEconomyLedger.ObserveWages(ReignWarEconomyTransactions.State, payer,
                CampaignTime.Now.ToDays, mobileParty.TotalWage, __result, applyWithdrawals, mobileParty.IsGarrison);
        }

        private static void TournamentPaymentPrefix(CharacterObject winner, MBReadOnlyList<CharacterObject> participants, out Dictionary<Hero, int> __state)
        {
            __state = participants.Where(x => x.IsHero).Select(x => x.HeroObject)
                .Concat(winner.IsHero ? new[] { winner.HeroObject } : Enumerable.Empty<Hero>())
                .Distinct().ToDictionary(x => x, x => x.Gold);
        }
        private static void TournamentPrizePaymentPrefix(Hero winner, out Dictionary<Hero, int> __state)
        {
            __state = new Dictionary<Hero, int>();
            if (winner?.Clan?.Leader != null) __state[winner.Clan.Leader] = winner.Clan.Leader.Gold;
        }
        private static void TournamentPrizePaymentPostfix(Dictionary<Hero, int> __state) => TournamentPaymentPostfix(__state);
        private static void TournamentPaymentPostfix(Dictionary<Hero, int> __state)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null || __state == null) return;
            foreach (var pair in __state.Where(x => x.Key.Clan != null))
            {
                string id = pair.Key.Clan.StringId;
                if (!state.Finance.TryGetValue(id, out var totals)) state.Finance[id] = totals = new EconomyFinanceTotals();
                totals.TournamentGold += Math.Max(0, pair.Key.Gold - pair.Value);
            }
        }

        private static void LordPartyInitializedPostfix(LordPartyComponent.InitializationArgs __instance, MobileParty mobileParty, Hero owner)
        {
            if (mobileParty == null
                || owner?.Clan == null
                || mobileParty == MobileParty.MainParty
                || mobileParty.IsCurrentlyUsedByAQuest
                || TaleWorlds.CampaignSystem.Campaign.Current == null
                || !TaleWorlds.CampaignSystem.Campaign.Current.GameStarted)
            {
                return;
            }

            var state = ReignWarEconomyTransactions.State;
            string generation = ReignWarEconomyTransactions.GenerationKey(mobileParty);
            if (state == null || !state.PartyGenerations.TryGetValue(generation, out string status) || status != "pending") return;

            TroopRoster roster = mobileParty.MemberRoster;
            for (int index = roster.Count - 1; index >= 0; index--)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(index);
                if (!element.Character.IsHero)
                {
                    roster.AddToCounts(element.Character, -element.Number, false, -element.WoundedNumber, -element.Xp);
                }
            }

            PartyTemplateObject template = owner.Clan.IsRebelClan
                ? owner.Clan.Culture.RebelsPartyTemplate
                : owner.Clan.DefaultPartyTemplate;
            List<(CharacterObject Troop, float Weight)> candidates = new List<(CharacterObject, float)>();
            if (template != null)
            {
                foreach (PartyTemplateStack stack in template.Stacks)
                {
                    if (stack.Character != null && !stack.Character.IsHero && stack.Character.Tier >= 1 && stack.Character.Tier <= 3
                        && stack.Character.Culture == owner.Clan.Culture)
                    {
                        candidates.Add((stack.Character, Math.Max(1f, (stack.MinValue + stack.MaxValue) * 0.5f)));
                    }
                }
            }

            if (candidates.Count == 0 && owner.Clan.BasicTroop != null && owner.Clan.BasicTroop.Tier >= 1 && owner.Clan.BasicTroop.Tier <= 3)
            {
                candidates.Add((owner.Clan.BasicTroop, 1f));
            }

            int retinueSize = MBRandom.RandomInt(10, 16), enlisted = 0;
            Settlement locality = __instance.SpawnSettlement ?? mobileParty.CurrentSettlement
                ?? (_nativeAiSpawn.Owner == owner ? _nativeAiSpawn.Locality : null);
            // No distant home-village debit when the party actually spawns in the field.
            try
            {
                for (int i = 0; i < retinueSize && candidates.Count > 0; i++)
                {
                    var supported = candidates.Where(candidate => ReignWarEconomyTransactions.Allow(
                        ReignWarEconomyTransactions.Retinue, locality, mobileParty, owner.Clan.Leader,
                        candidate.Troop, 1, true, recordBlocked: false) > 0).ToList();
                    if (supported.Count == 0) break;
                    if (!ReignWarEconomyTransactions.Enlist(ReignWarEconomyTransactions.Retinue, locality,
                        mobileParty, owner.Clan.Leader, ChooseWeightedTroop(supported))) break;
                    enlisted++;
                }
            }
            finally { state.PartyGenerations[generation] = "committed:" + enlisted; }
            ReignWarEconomyTransactions.Totals(ReignWarEconomyTransactions.Retinue).Blocked += retinueSize - enlisted;
            ReignEconomyCampaignBehavior.Instance?.RecordRetinue(enlisted);
            ReignLog.Info("Funded " + enlisted + " of " + retinueSize + " household troops for " + owner.StringId + ".");
        }

        private static bool BeginDailyGarrison(string source, Town town)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null || town == null) return false;
            string key = source + ":" + town.Settlement.StringId;
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            if (state.RecruitmentDays.TryGetValue(key, out int last) && last == day) return false;
            state.RecruitmentDays[key] = day;
            return true;
        }

        private static bool GarrisonRecruitmentPrefix(GarrisonRecruitmentCampaignBehavior __instance, Town town)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            { ReignWarEconomyTransactions.Totals("initial_campaign_garrison").Exempt++; return true; }
            if (town?.OwnerClan?.Leader == null || town.IsUnderSiege || town.FoodChange <= 0
                || !town.GarrisonAutoRecruitmentIsEnabled || town.Settlement.Party.MapEvent != null) return false;
            if (!BeginDailyGarrison(ReignWarEconomyTransactions.Garrison, town)) return false;
            var getter = AccessTools.Method(typeof(GarrisonRecruitmentCampaignBehavior), "GetAutoRecruitmentGarrisonChangeExplainedNumber");
            float native = ((ExplainedNumber)getter.Invoke(__instance, new object[] { town })).ResultNumber;
            var field = AccessTools.Field(typeof(GarrisonRecruitmentCampaignBehavior), "_volunteerListCache");
            var volunteers = ((IEnumerable<GarrisonRecruitmentCampaignBehavior.VolunteerTroop>)field.GetValue(__instance)).ToList();
            if (native <= 0) return false;
            if (town.GarrisonParty == null) town.Settlement.AddGarrisonParty();
            int remaining = Math.Min(volunteers.Count, (int)Math.Ceiling(native));
            foreach (var volunteer in volunteers)
            {
                if (remaining <= 0) break;
                var notable = volunteer.OwnerNotable;
                int index = volunteer.NotableVolunteerArrayIndex;
                var troop = notable.VolunteerTypes[index];
                var locality = notable.CurrentSettlement ?? town.Settlement;
                if (ReignWarEconomyTransactions.Enlist(ReignWarEconomyTransactions.Garrison, locality,
                    town.GarrisonParty, town.OwnerClan.Leader, troop, notable, index, true)) remaining--;
            }
            return false;
        }

        private static bool BaseGarrisonRecruitmentPrefix(GarrisonRecruitmentCampaignBehavior __instance, Town town)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true)
            { ReignWarEconomyTransactions.Totals("initial_campaign_garrison").Exempt++; return true; }
            if (town?.OwnerClan?.Leader == null || town.IsUnderSiege || town.Settlement.Party.MapEvent != null) return false;
            if (!BeginDailyGarrison(ReignWarEconomyTransactions.BaseGarrison, town)) return false;
            var getter = AccessTools.Method(typeof(GarrisonRecruitmentCampaignBehavior), "GetBaseGarrisonChangeExplainedNumber");
            int native = (int)((ExplainedNumber)getter.Invoke(__instance, new object[] { town, false })).ResultNumber;
            if (native <= 0) return false;
            if (town.GarrisonParty == null) town.Settlement.AddGarrisonParty();
            for (int i = 0; i < native; i++)
                if (!ReignWarEconomyTransactions.Enlist(ReignWarEconomyTransactions.BaseGarrison, town.Settlement,
                    town.GarrisonParty, town.OwnerClan.Leader, town.MapFaction.BasicTroop, liability: true)) break;
            return false;
        }

        private static void FoodProvisionPrefix(MobileParty mobileParty, Settlement settlement,
            out ReignWarEconomyTariffs.Scope __state)
            => __state = ReignWarEconomyTariffs.Enter(mobileParty, settlement, "native_food");
        private static void HorseProvisionPrefix(MobileParty mobileParty, Town town,
            out ReignWarEconomyTariffs.Scope __state)
            => __state = ReignWarEconomyTariffs.Enter(mobileParty, town?.Settlement, "native_horses");
        private static Exception ProvisionFinalizer(Exception __exception, ReignWarEconomyTariffs.Scope __state)
        { ReignWarEconomyTariffs.Leave(__state); return __exception; }
        private static Exception HorseProvisionFinalizer(Exception __exception, ReignWarEconomyTariffs.Scope __state)
        { ReignWarEconomyTariffs.Leave(__state); return __exception; }
        private static void TariffPurchasePrefix(PartyBase sellerParty, PartyBase buyerParty,
            ItemRosterElement itemRosterElement, int number, out ReignWarEconomyTariffs.Sale __state)
            => __state = ReignWarEconomyTariffs.Before(sellerParty, buyerParty, itemRosterElement, number);
        private static Exception TariffPurchaseFinalizer(Exception __exception, ReignWarEconomyTariffs.Sale __state)
        { ReignWarEconomyTariffs.After(__state, __exception); return __exception; }
        private static void TariffPoolPrefix(Town __instance, out int? __state)
            => __state = ReignWarEconomyTariffs.PoolBefore(__instance);
        private static void TariffPoolPostfix(Town __instance, int? __state)
        {
            try { ReignWarEconomyTariffs.PoolAfter(__instance, __state); }
            catch (Exception ex)
            {
                if (ReignWarEconomyTransactions.State != null) ReignWarEconomyTransactions.State.TariffUnsupported++;
                ReignLog.Warn("Tariff pool observation failed: " + ex.GetType().Name);
            }
        }
        private static IEnumerable<CodeInstruction> TariffPurchaseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ratio = AccessTools.Method(typeof(SettlementTaxModel), nameof(SettlementTaxModel.GetTownTaxRatio));
            var price = AccessTools.Method(typeof(SettlementComponent), nameof(SettlementComponent.GetItemPrice),
                new[] { typeof(EquipmentElement), typeof(MobileParty), typeof(bool) });
            var stock = AccessTools.Method(typeof(ItemRoster), nameof(ItemRoster.AddToCounts),
                new[] { typeof(EquipmentElement), typeof(int) });
            var rounding = AccessTools.Method(typeof(MBRandom), nameof(MBRandom.RoundRandomized), new[] { typeof(float) });
            RequirePurchaseLoop(code, price, stock);
            int ratios = 0, rounds = 0, prices = 0, stocks = 0;
            foreach (var instruction in code)
            {
                if (instruction.Calls(price))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.QuotePrice));
                    prices++;
                }
                else if (instruction.Calls(ratio))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.TaxRatio));
                    ratios++;
                }
                else if (instruction.Calls(rounding))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.RoundCommission));
                    rounds++;
                }
                else if (instruction.Calls(stock))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.TransferQuotedUnit));
                    stocks++;
                }
            }
            if (prices != 1 || ratios != 1 || rounds != 1 || stocks != 2)
                throw new InvalidOperationException("Native purchase shape changed; expected one price, two stock writes, one town ratio and one commission rounding call.");
            return code;
        }

        private static int PurchaseLocal(CodeInstruction instruction, bool store)
        {
            var op = instruction.opcode;
            var shortCodes = store ? new[] { OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3 }
                : new[] { OpCodes.Ldloc_0, OpCodes.Ldloc_1, OpCodes.Ldloc_2, OpCodes.Ldloc_3 };
            for (int i = 0; i < shortCodes.Length; i++) if (op == shortCodes[i]) return i;
            if (op != (store ? OpCodes.Stloc : OpCodes.Ldloc) && op != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
            return PurchaseLocalOperand(instruction.operand);
        }
        private static int PurchaseLocalOperand(object operand) => operand is LocalBuilder builder ? builder.LocalIndex
            : operand is LocalVariableInfo variable ? variable.LocalIndex
            : operand is int index ? index : operand is byte small ? small : -1;
        private static bool PurchaseArgumentAddress(CodeInstruction instruction, int index)
            => (instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
                && (instruction.operand is ParameterInfo parameter ? parameter.Position : PurchaseLocalOperand(instruction.operand)) == index;
        // A rejected quote may be zeroed only when its sole consumer is the native cost sum,
        // and both stock results are discarded. Keep the original loop and its buyer-null branch.
        private static void RequirePurchaseLoop(List<CodeInstruction> code, MethodInfo price, MethodInfo stock)
        {
            var prices = code.Select((x, i) => new { x, i }).Where(x => x.x.Calls(price)).ToArray();
            var stocks = code.Select((x, i) => new { x, i }).Where(x => x.x.Calls(stock)).ToArray();
            var target = AccessTools.Method(typeof(SellItemsAction), "ApplyInternal",
                new[] { typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement) });
            if (target == null || !target.IsStatic) throw new InvalidOperationException("Native purchase signature changed.");
            if (prices.Length != 1 || stocks.Length != 2) throw new InvalidOperationException("Unsupported native purchase loop calls.");
            int p = prices[0].i;
            if (p < 10 || p + 28 >= code.Count || stocks[0].i != p + 11 || stocks[1].i != p + 20)
                throw new InvalidOperationException("Unsupported native purchase stock ordering.");
            if (code.Take(p + 29).Any(x => x.operand is MethodInfo method
                && (method.DeclaringType == typeof(GiveGoldAction)
                    || (method.DeclaringType == typeof(SettlementComponent) && method.Name == nameof(SettlementComponent.ChangeGold))
                    || (method.DeclaringType == typeof(Hero) && (method.Name == nameof(Hero.ChangeHeroGold) || method.Name == "set_Gold"))
                    || (method.DeclaringType == typeof(MobileParty) && method.Name == "set_PartyTradeGold"))))
                throw new InvalidOperationException("Native purchase payment moved ahead of the complete item loop.");
            if (!code.Skip(p + 29).Any(x => x.operand is MethodInfo method && method.DeclaringType == typeof(GiveGoldAction)))
                throw new InvalidOperationException("Native purchase payment is missing after the item loop.");
            int unit = PurchaseLocal(code[p + 1], true), total = PurchaseLocal(code[p + 2], false);
            int iteration = PurchaseLocal(code[p + 22], false);
            var roster = AccessTools.PropertyGetter(typeof(PartyBase), nameof(PartyBase.ItemRoster));
            var equipment = AccessTools.PropertyGetter(typeof(ItemRosterElement), nameof(ItemRosterElement.EquipmentElement));
            if (unit < 0 || total < 0 || PurchaseLocal(code[p + 3], false) != unit
                || code.Count(x => PurchaseLocal(x, false) == unit) != 1
                || code.Count(x => PurchaseLocal(x, true) == unit) != 1
                || code.Count(x => PurchaseLocal(x, true) == total) != 2
                || code.Any(x => (x.opcode == OpCodes.Ldloca || x.opcode == OpCodes.Ldloca_S)
                    && (PurchaseLocalOperand(x.operand) == unit || PurchaseLocalOperand(x.operand) == total))
                || code[p - 10].opcode != OpCodes.Ldc_I4_0 || PurchaseLocal(code[p - 9], true) != total
                || code[p - 8].opcode != OpCodes.Ldc_I4_0 || PurchaseLocal(code[p - 7], true) != iteration
                || (code[p - 6].opcode != OpCodes.Br && code[p - 6].opcode != OpCodes.Br_S)
                || !(code[p - 6].operand is Label check) || !code[p + 26].labels.Contains(check)
                || PurchaseLocal(code[p - 5], false) < 0 || !PurchaseArgumentAddress(code[p - 4], 2)
                || !code[p - 3].Calls(equipment) || PurchaseLocal(code[p - 2], false) < 0 || PurchaseLocal(code[p - 1], false) < 0
                || code[p + 4].opcode != OpCodes.Add || PurchaseLocal(code[p + 5], true) != total
                || code[p + 6].opcode != OpCodes.Ldarg_0 || !code[p + 7].Calls(roster)
                || !PurchaseArgumentAddress(code[p + 8], 2) || !code[p + 9].Calls(equipment)
                || code[p + 10].opcode != OpCodes.Ldc_I4_M1 || code[p + 12].opcode != OpCodes.Pop
                || code[p + 13].opcode != OpCodes.Ldarg_1
                || (code[p + 14].opcode != OpCodes.Brfalse && code[p + 14].opcode != OpCodes.Brfalse_S)
                || !(code[p + 14].operand is Label skip) || !code[p + 22].labels.Contains(skip)
                || code[p + 15].opcode != OpCodes.Ldarg_1 || !code[p + 16].Calls(roster)
                || !PurchaseArgumentAddress(code[p + 17], 2) || !code[p + 18].Calls(equipment)
                || code[p + 19].opcode != OpCodes.Ldc_I4_1 || code[p + 21].opcode != OpCodes.Pop
                || iteration < 0 || code[p + 23].opcode != OpCodes.Ldc_I4_1 || code[p + 24].opcode != OpCodes.Add
                || PurchaseLocal(code[p + 25], true) != iteration || PurchaseLocal(code[p + 26], false) != iteration
                || code[p + 27].opcode != OpCodes.Ldarg_3
                || (code[p + 28].opcode != OpCodes.Blt && code[p + 28].opcode != OpCodes.Blt_S)
                || !(code[p + 28].operand is Label loop) || !code[p - 5].labels.Contains(loop)
                || code.Skip(p - 5).Take(34).Any(x => x.blocks.Count != 0)
                || code.Skip(p - 4).Take(26).Any(x => x.labels.Count != 0))
                throw new InvalidOperationException("Native purchase price or stock consumer shape changed.");
        }

        private static int WageInQuarters(int nativeStackWage, MobileParty party)
        {
            return !QuarteringCounterfactual && party?.IsGarrison == true && party.CurrentSettlement?.IsFortification == true
                && party.GarrisonPartyComponent?.Settlement == party.CurrentSettlement
                ? GarrisonStackWage(nativeStackWage) : nativeStackWage;
        }
        private static int GarrisonStackWage(int nativeStackWage)
            => MathF.Round((float)(nativeStackWage * ReignWarEconomyPolicy.GarrisonWageFactor));

        private static IEnumerable<CodeInstruction> GarrisonQuarteringTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(CharacterObject), "TroopWage");
            var count = AccessTools.PropertyGetter(typeof(TroopRosterElement), "Number");
            var wrapper = AccessTools.Method(typeof(ReignEconomyPatches), nameof(WageInQuarters));
            var code = instructions.ToList();
            var wageSites = code.Select((instruction, index) => new { instruction, index }).Where(x => x.instruction.Calls(getter)).ToList();
            if (wageSites.Count != 3)
                throw new InvalidOperationException("Native garrison wage shape changed; expected two hero quotes and one regular stack quote.");
            int regular = wageSites[2].index;
            if (regular + 4 >= code.Count || (code[regular + 1].opcode != OpCodes.Ldloca_S && code[regular + 1].opcode != OpCodes.Ldloca)
                || !code[regular + 2].Calls(count) || code[regular + 3].opcode != OpCodes.Mul
                || !code[regular + 4].opcode.Name.StartsWith("stloc", StringComparison.Ordinal)
                || code[regular + 4].labels.Count != 0)
                throw new InvalidOperationException("Native regular stack wage multiplication changed; quartering is unsupported.");
            for (int index = 0; index < code.Count; index++)
            {
                yield return code[index];
                if (index == regular + 3)
                {
                    // Keep both hero salary branches and all native unit quotes intact.
                    // Round once after multiplying the regular stack, before native category modifiers.
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, wrapper);
                }
            }
        }

        internal static Hero UpgradePayer(Hero owner, Hero leader)
            => owner?.IsAlive == true ? owner : leader?.IsAlive == true ? leader : null;

        private static bool AutomaticUpgradePrefix(PartyBase party, object[] __args)
        {
            if (party == PartyBase.MainParty) return true;
            var mobile = party?.MobileParty;
            var payer = UpgradePayer(party?.Owner, party?.LeaderHero);
            if (mobile == null || payer?.Clan == null) return true; // Native bandits/other non-clan parties retain their rules.
            object upgradeArgs = __args[2];
            Type type = upgradeArgs.GetType();
            int count = (int)AccessTools.Field(type, "PossibleUpgradeCount").GetValue(upgradeArgs);
            int cost = (int)AccessTools.Field(type, "UpgradeGoldCost").GetValue(upgradeArgs);
            var from = (CharacterObject)AccessTools.Field(type, "Target").GetValue(upgradeArgs);
            var to = (CharacterObject)AccessTools.Field(type, "UpgradeTarget").GetValue(upgradeArgs);
            int allowed = ReignWarEconomyPolicy.AffordableCountExact(count, payer.Gold, payer.Clan.Gold,
                ReignWarEconomyTransactions.ClanWages(payer.Clan), payer.Clan.AutoRecruitmentExpenses, cost,
                14, amount => ReignWarEconomyTransactions.AdditionalWages(mobile, to, amount, from));
            if (allowed < count)
            {
                ReignWarEconomyTransactions.Totals("automatic_upgrade").Blocked += count - allowed;
                if (allowed <= 0) return false;
                int xp = (int)AccessTools.Field(type, "UpgradeXpCost").GetValue(upgradeArgs);
                float chance = (float)AccessTools.Field(type, "UpgradeChance").GetValue(upgradeArgs);
                __args[2] = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { from, to, allowed, cost, xp, chance }, null);
            }
            return true;
        }

        private static IEnumerable<CodeInstruction> CommodityBaselineTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var level = AccessTools.Method(typeof(Village), "GetHearthLevel");
            int replacements = 0;
            for (int i = 0; i + 5 < code.Count; i++)
            {
                if (!code[i].Calls(level) || code[i + 1].opcode != OpCodes.Ldc_I4_1 || code[i + 2].opcode != OpCodes.Add
                    || code[i + 3].opcode != OpCodes.Conv_R4 || code[i + 4].opcode != OpCodes.Ldc_R4
                    || !Equals(code[i + 4].operand, .5f) || code[i + 5].opcode != OpCodes.Mul) continue;
                code[i].opcode = OpCodes.Call;
                code[i].operand = AccessTools.Method(typeof(ReignVillageProductionCalculatorModel), nameof(ReignVillageProductionCalculatorModel.CommodityBaseline));
                for (int j = 1; j <= 5; j++) { code[i + j].opcode = OpCodes.Nop; code[i + j].operand = null; }
                replacements++;
            }
            if (replacements != 1) throw new InvalidOperationException("Native commodity hearth baseline changed; expected one exact IL match, got " + replacements);
            return code;
        }

        private static IEnumerable<CodeInstruction> NativeAiSpawnLocalityTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var gate = AccessTools.PropertyGetter(typeof(Settlement), nameof(Settlement.GatePosition));
            var spawn = AccessTools.Method(typeof(MobilePartyHelper), nameof(MobilePartyHelper.SpawnLordParty),
                new[] { typeof(Hero), typeof(CampaignVec2), typeof(float) });
            var gates = code.Where(x => x.Calls(gate)).ToArray();
            var spawns = code.Where(x => x.Calls(spawn)).ToArray();
            if (gates.Length != 1 || spawns.Length != 1)
                throw new InvalidOperationException("Native AI lord spawn locality call sites changed.");
            int gateIndex = code.IndexOf(gates[0]), spawnIndex = code.IndexOf(spawns[0]);
            if (spawnIndex <= gateIndex || code.Skip(gateIndex + 1).Take(spawnIndex - gateIndex)
                .Any(x => x.labels.Count != 0 || x.blocks.Count != 0 || x.opcode.FlowControl == FlowControl.Branch
                    || x.opcode.FlowControl == FlowControl.Cond_Branch))
                throw new InvalidOperationException("Native AI lord spawn locality no longer follows a straight call path.");
            // Leave the exact settlement on the stack alongside its native coordinates.
            var dup = new CodeInstruction(OpCodes.Dup);
            dup.labels.AddRange(gates[0].labels);
            dup.blocks.AddRange(gates[0].blocks);
            gates[0].labels.Clear();
            gates[0].blocks.Clear();
            code.Insert(gateIndex, dup);
            spawns[0].opcode = OpCodes.Call;
            spawns[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(SpawnLordPartyWithLocality));
            return code;
        }

        private static MobileParty SpawnLordPartyWithLocality(Hero owner, Settlement locality, CampaignVec2 position, float radius)
        {
            var previous = _nativeAiSpawn;
            _nativeAiSpawn = (owner, locality);
            try { return MobilePartyHelper.SpawnLordParty(owner, position, radius); }
            finally { _nativeAiSpawn = previous; }
        }

        private static void RansomQuotePrefix(out int __state)
        {
            __state = _ransomQuoteDepth;
            _ransomQuoteDepth++;
        }

        private static void CivilianConsumptionPrefix(Town town, Dictionary<ItemCategory, float> categoryDemand,
            out ReignWarEconomyFoodMarket.Consumption __state)
            => __state = ReignWarEconomyFoodMarket.Before(town, categoryDemand);

        private static void CivilianConsumptionPostfix(Dictionary<ItemCategory, int> saleLog,
            ReignWarEconomyFoodMarket.Consumption __state)
            => ReignWarEconomyFoodMarket.After(saleLog, __state);

        private static Exception RansomQuoteFinalizer(Exception __exception, int __state)
        {
            _ransomQuoteDepth = __state;
            return __exception;
        }

        private static IEnumerable<CodeInstruction> NativeDailyFinanceTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var give = AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyBetweenCharacters),
                new[] { typeof(Hero), typeof(Hero), typeof(int), typeof(bool) });
            var wallet = AccessTools.PropertySetter(typeof(Kingdom), nameof(Kingdom.KingdomBudgetWallet));
            var payments = code.Where(x => x.Calls(give)).ToArray();
            var credits = code.Where(x => x.Calls(wallet)).ToArray();
            if (payments.Length != 1 || credits.Length != 1)
                throw new InvalidOperationException("Native clan finance payout or treasury credit call sites changed.");
            payments[0].opcode = OpCodes.Call;
            payments[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(ObserveNativeClanDailyPayout));
            credits[0].opcode = OpCodes.Call;
            credits[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(ObserveNativeKingdomBudgetCredit));
            return code;
        }

        private static EconomyFinanceTotals LiveFinanceTotals(Clan clan)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null || clan == null || TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true) return null;
            if (!state.Finance.TryGetValue(clan.StringId, out var totals))
                state.Finance[clan.StringId] = totals = new EconomyFinanceTotals();
            return totals;
        }

        private static void ObserveNativeClanDailyPayout(Hero giver, Hero receiver, int amount, bool disableNotification)
        {
            int before = receiver?.Gold ?? 0;
            GiveGoldAction.ApplyBetweenCharacters(giver, receiver, amount, disableNotification);
            if (giver != null || receiver == null || receiver.Clan?.Leader != receiver) return;
            var totals = LiveFinanceTotals(receiver.Clan);
            if (totals == null) return;
            totals.NativeClanPayoutNet += (double)receiver.Gold - before;
            totals.NativeClanPayouts++;
        }

        private static void ObserveNativeKingdomBudgetCredit(Kingdom kingdom, int value)
        {
            int before = kingdom.KingdomBudgetWallet;
            kingdom.KingdomBudgetWallet = value;
            double added = (double)kingdom.KingdomBudgetWallet - before;
            if (added <= 0) return;
            var totals = LiveFinanceTotals(kingdom.RulingClan);
            if (totals == null) return;
            totals.NativeKingdomBudgetCredits += added;
            totals.NativeKingdomBudgetCreditCount++;
        }

        private static void NativeKingdomSupportPrefix(Clan clan, bool applyWithdrawals, out int? __state)
            => __state = applyWithdrawals ? clan?.Kingdom?.KingdomBudgetWallet : null;

        private static void NativeKingdomSupportPostfix(Clan clan, int? __state)
        {
            if (!__state.HasValue || clan?.Kingdom == null) return;
            double withdrawal = (double)__state.Value - clan.Kingdom.KingdomBudgetWallet;
            if (withdrawal <= 0) return;
            var totals = LiveFinanceTotals(clan);
            if (totals != null) totals.NativeKingdomSupportReceived += withdrawal;
        }

        private static void NativePartyIncomePostfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
        {
            if (!applyWithdrawals || __result <= 0 || party == null) return;
            var totals = LiveFinanceTotals(clan);
            if (totals == null) return;
            if (party.IsCaravan) totals.NativeCaravanRemittances += __result;
            else totals.NativeMilitaryPartyRemittances += __result;
        }

        private static void NativeLordPurseTopUpPostfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
        {
            // The negative native result is the amount just credited to PartyTradeGold and billed to the clan.
            var lord = party?.LeaderHero;
            if (!applyWithdrawals || __result >= 0 || clan == null || party?.IsLordParty != true
                || lord == null || lord == clan.Leader || lord.Clan != clan) return;
            var totals = LiveFinanceTotals(clan);
            if (totals != null) totals.NativeLordPurseTopUps -= (double)__result;
        }

        private static void NativeTownMarketBalancePrefix(Town town, out int __state) => __state = town?.Gold ?? 0;

        private static void NativeTownMarketBalancePostfix(Town town, int __state)
        {
            if (town == null) return;
            double delta = (double)town.Gold - __state;
            if (delta == 0) return;
            var totals = LiveFinanceTotals(town.OwnerClan);
            if (totals == null) return;
            totals.NativeTownMarketCredits += Math.Max(0, delta);
            totals.NativeTownMarketDebits += Math.Max(0, -delta);
        }

        private static void PatchFinanceTranspiler(Type type, string nativeMethod, string patchMethod)
        {
            var method = AccessTools.Method(type, nativeMethod) ?? throw new MissingMethodException(type.FullName, nativeMethod);
            _harmony.Patch(method, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), patchMethod));
            PatchedHooks.Add(patchMethod);
            HookTargets[patchMethod] = method;
        }

        private static void ObserveCall(List<CodeInstruction> code, MethodInfo original, string observer, int expectedCalls = 1)
        {
            var calls = original == null ? Array.Empty<CodeInstruction>() : code.Where(x => x.Calls(original)).ToArray();
            if (calls.Length != expectedCalls) throw new InvalidOperationException("Native finance call site changed: " + observer);
            foreach (var call in calls)
            {
                call.opcode = OpCodes.Call;
                call.operand = AccessTools.Method(typeof(ReignEconomyPatches), observer);
            }
        }

        private static IEnumerable<CodeInstruction> NativePrisonerPayoutTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            ObserveCall(code, AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyBetweenCharacters),
                new[] { typeof(Hero), typeof(Hero), typeof(int), typeof(bool) }), nameof(ObservePrisonerHeroPayout));
            ObserveCall(code, AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyForPartyToSettlement),
                new[] { typeof(PartyBase), typeof(Settlement), typeof(int), typeof(bool) }), nameof(ObservePrisonerSettlementPayout));
            return code;
        }

        private static void ObservePrisonerHeroPayout(Hero giver, Hero receiver, int amount, bool disableNotification)
        {
            int before = receiver?.Gold ?? 0;
            GiveGoldAction.ApplyBetweenCharacters(giver, receiver, amount, disableNotification);
            if (giver != null || receiver?.Clan == null) return;
            var totals = LiveFinanceTotals(receiver.Clan);
            double credit = Math.Max(0, (double)receiver.Gold - before);
            if (totals == null || credit <= 0) return;
            if (receiver == receiver.Clan.Leader) totals.NativePrisonerAwardsToClanLeader += credit;
            else totals.NativePrisonerAwardsToOtherHeroes += credit;
        }

        private static void ObservePrisonerSettlementPayout(PartyBase giver, Settlement receiver, int amount, bool disableNotification)
        {
            int before = receiver?.Town?.Gold ?? 0;
            GiveGoldAction.ApplyForPartyToSettlement(giver, receiver, amount, disableNotification);
            if (giver != null || receiver?.Town == null) return;
            var totals = LiveFinanceTotals(receiver.OwnerClan);
            if (totals != null) totals.NativePrisonerAwardsToSettlement += Math.Max(0, (double)receiver.Town.Gold - before);
        }

        private static IEnumerable<CodeInstruction> NativeLootSaleTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            ObserveCall(code, AccessTools.Method(typeof(SellItemsAction), nameof(SellItemsAction.Apply),
                new[] { typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement) }), nameof(ObserveLordLootSale));
            return code;
        }

        private static void ObserveLordLootSale(PartyBase seller, PartyBase buyer, ItemRosterElement item, int amount, Settlement settlement)
            => ObserveLordMarketSale(seller, buyer, item, amount, settlement, false);

        private static IEnumerable<CodeInstruction> NativeHorseMarketSaleTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            // This native method sells non-food cargo as well as spare mounts. Its separate BuyHorses method stays native.
            ObserveCall(code, AccessTools.Method(typeof(SellItemsAction), nameof(SellItemsAction.Apply),
                new[] { typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement) }),
                nameof(ObserveLordHorseMarketSale), 2);
            return code;
        }

        private static void ObserveLordHorseMarketSale(PartyBase seller, PartyBase buyer, ItemRosterElement item, int amount, Settlement settlement)
            => ObserveLordMarketSale(seller, buyer, item, amount, settlement, true);

        private static void ObserveLordMarketSale(PartyBase seller, PartyBase buyer, ItemRosterElement item, int amount, Settlement settlement, bool horseMarket)
        {
            var receiver = seller?.LeaderHero;
            int before = receiver?.Gold ?? 0;
            if (seller?.MobileParty != null && settlement?.Town != null && buyer?.Settlement == settlement)
            {
                // Native market sales may overdraw town gold across successive stacks. Quote
                // and sell one actual item at a time against the remaining wallet, so an
                // exhausted market cannot create gold and unsold equipment stays with its owner.
                for (int i = 0; i < Math.Min(Math.Max(0, amount), item.Amount); i++)
                {
                    int price = settlement.Town.GetItemPrice(item.EquipmentElement, seller.MobileParty, true);
                    if (price < 0 || price > settlement.Town.Gold) break;
                    SellItemsAction.Apply(seller, buyer, item, 1, settlement);
                }
            }
            else SellItemsAction.Apply(seller, buyer, item, amount, settlement);
            if (receiver?.Clan == null || seller.MobileParty?.IsLordParty != true) return;
            var totals = LiveFinanceTotals(receiver.Clan);
            double credit = Math.Max(0, (double)receiver.Gold - before);
            if (totals == null || credit <= 0) return;
            if (horseMarket)
            {
                if (receiver == receiver.Clan.Leader) totals.NativeLordHorseMarketSalesToClanLeader += credit;
                else totals.NativeLordHorseMarketSalesToOtherLords += credit;
            }
            else if (receiver == receiver.Clan.Leader) totals.NativeLordLootSalesToClanLeader += credit;
            else totals.NativeLordLootSalesToOtherLords += credit;
        }

        private static bool PatrolCreationPrefix(Settlement homeSettlement)
        {
            return homeSettlement?.Town?.GarrisonParty != null
                && homeSettlement.Town.GarrisonParty.MemberRoster.TotalHealthyCount >= MinimumPatrolSize;
        }

        private static void PatrolCreationPostfix(ref MobileParty __result)
        {
            if (__result?.PatrolPartyComponent?.HomeSettlement == null)
            {
                return;
            }

            int targetSize = __result.MemberRoster.TotalRegulars;
            __result.MemberRoster.Clear();
            int transferred = TransferHealthyGarrisonTroops(
                __result.PatrolPartyComponent.HomeSettlement,
                __result,
                targetSize);
            if (transferred < MinimumPatrolSize)
            {
                ReignLog.Warn("Patrol creation produced fewer than " + MinimumPatrolSize + " transferred garrison troops.");
            }
        }

        private static bool PatrolReplenishmentPrefix(MobileParty party)
        {
            if (party?.PatrolPartyComponent?.HomeSettlement == null)
            {
                return true;
            }

            Settlement home = party.PatrolPartyComponent.HomeSettlement;
            PartyTemplateObject template = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementPatrolModel
                .GetPartyTemplateForPatrolParty(home, party.PatrolPartyComponent.IsNaval);
            int targetSize = TaleWorlds.CampaignSystem.Campaign.Current.Models.PartySizeLimitModel
                .FindAppropriateInitialRosterForMobileParty(party, template)
                .TotalManCount;

            ReturnPatrolTroopsToGarrison(party, home);
            TransferHealthyGarrisonTroops(home, party, targetSize);
            party.PatrolPartyComponent.SortRoster();
            return false;
        }

        private static void PatrolRemovalPrefix(Settlement settlement)
        {
            MobileParty patrol = settlement?.PatrolParty?.MobileParty;
            if (patrol != null && patrol.MapEvent == null)
            {
                ReturnPatrolTroopsToGarrison(patrol, settlement);
            }
        }

        private static int TransferHealthyGarrisonTroops(Settlement home, MobileParty patrol, int targetSize)
        {
            MobileParty garrison = home?.Town?.GarrisonParty;
            if (garrison == null || patrol == null || targetSize <= 0)
            {
                return 0;
            }

            int totalBefore = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            int remaining = targetSize;
            List<TroopRosterElement> candidates = garrison.MemberRoster.GetTroopRoster()
                .Where(element => !element.Character.IsHero && element.Number > element.WoundedNumber)
                .OrderBy(element => element.Character.Tier)
                .ToList();
            foreach (TroopRosterElement element in candidates)
            {
                int healthy = element.Number - element.WoundedNumber;
                int transfer = Math.Min(healthy, remaining);
                if (transfer <= 0)
                {
                    continue;
                }

                garrison.MemberRoster.AddToCounts(element.Character, -transfer);
                patrol.MemberRoster.AddToCounts(element.Character, transfer);
                remaining -= transfer;
                if (remaining == 0)
                {
                    break;
                }
            }

            int transferred = targetSize - remaining;
            int totalAfter = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            ReignEconomyCampaignBehavior.Instance?.RecordPatrolTransfer(
                transferred, totalBefore != totalAfter);
            return transferred;
        }

        private static void ReturnPatrolTroopsToGarrison(MobileParty patrol, Settlement home)
        {
            MobileParty garrison = home?.Town?.GarrisonParty;
            if (patrol == null || garrison == null)
            {
                return;
            }

            int totalBefore = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            List<TroopRosterElement> returning = patrol.MemberRoster.GetTroopRoster()
                .Where(element => !element.Character.IsHero)
                .ToList();
            int returned = 0;
            foreach (TroopRosterElement element in returning)
            {
                returned += element.Number;
                garrison.MemberRoster.AddToCounts(
                    element.Character,
                    element.Number,
                    false,
                    element.WoundedNumber,
                    element.Xp);
                patrol.MemberRoster.AddToCounts(
                    element.Character,
                    -element.Number,
                    false,
                    -element.WoundedNumber,
                    -element.Xp);
            }
            int totalAfter = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            ReignEconomyCampaignBehavior.Instance?.RecordPatrolReturn(
                returned, totalBefore != totalAfter);
        }

        private static CharacterObject ChooseWeightedTroop(List<(CharacterObject Troop, float Weight)> candidates)
        {
            float total = candidates.Sum(candidate => candidate.Weight);
            float roll = MBRandom.RandomFloat * total;
            foreach (var candidate in candidates)
            {
                roll -= candidate.Weight;
                if (roll <= 0f)
                {
                    return candidate.Troop;
                }
            }

            return candidates[candidates.Count - 1].Troop;
        }
    }
}
