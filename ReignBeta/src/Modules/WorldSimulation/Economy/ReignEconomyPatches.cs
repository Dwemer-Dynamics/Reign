using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;
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
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Economy
{
    internal static partial class ReignEconomyPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.economy";
        private const int MinimumPatrolSize = 5;
        private static Harmony _harmony;
        private static readonly HashSet<string> PatchedHooks = new HashSet<string>(StringComparer.Ordinal);
        private static string _lastPatchError = string.Empty;
        private static string _fieldWageUiError = string.Empty;
        [ThreadStatic] private static int _recruitmentDepth;
        [ThreadStatic] private static int _ransomQuoteDepth;
        [ThreadStatic] private static int _quarteringCounterfactual;
        [ThreadStatic] private static bool _exemptWageQuote;
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
                Patch(typeof(HeroSpawnCampaignBehavior), "GetHeroPartyCommandScore", nameof(NpcCommanderEligibilityPrefix), prefix: true);
                Patch(typeof(DefaultClanFinanceModel), "CalculatePartyWage", nameof(NativeWagePaymentPostfix), prefix: false);
                var quartering = AccessTools.Method(typeof(DefaultPartyWageModel), "GetTotalWage");
                _harmony.Patch(quartering, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(GarrisonQuarteringTranspiler)));
                PatchedHooks.Add(nameof(GarrisonQuarteringTranspiler));
                HookTargets[nameof(GarrisonQuarteringTranspiler)] = quartering;
                PatchKingdomWage(typeof(Diplomacy.ClanAccords.ReignClanAccordsWageModel), nameof(KingdomWagePostfix));
                ApplyFieldWageUi();
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
            var wage = NativeNavalWageType;
            if (_harmony != null && wage != null && !HookTargets.ContainsKey(nameof(NavalKingdomWagePostfix)))
                PatchKingdomWage(wage, nameof(NavalKingdomWagePostfix));
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
        private static Type NativeNavalWageType => AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCPartyWageModel");
        private static int ExpectedEconomyHooks => 56 + (NavalRecruitmentType == null ? 0 : 1)
            + (NativeFishingType == null ? 0 : 2) + (NativeNavalFinanceType == null ? 0 : 1)
            + (NativeNavalWageType == null ? 0 : 1);

        private static void PatchKingdomWage(Type type, string patchName)
        {
            var target = AccessTools.DeclaredMethod(type, nameof(PartyWageModel.GetTotalWage),
                new[] { typeof(MobileParty), typeof(TroopRoster), typeof(bool) })
                ?? throw new MissingMethodException(type.FullName, nameof(PartyWageModel.GetTotalWage));
            // Apply after native, naval, Accord and voted garrison modifiers.
            _harmony.Patch(target, postfix: new HarmonyMethod(typeof(ReignEconomyPatches), patchName) { priority = Priority.Last });
            PatchedHooks.Add(patchName);
            HookTargets[patchName] = target;
        }

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
            owner("wages", models?.PartyWageModel, typeof(ReignBeta.Diplomacy.ClanAccords.ReignClanAccordsWageModel), "NavalDLCPartyWageModel");
            owner("finance", models?.ClanFinanceModel, typeof(ReignBeta.Diplomacy.ClanAccords.ReignClanAccordsFinanceModel), "NavalDLCClanFinanceModel");
            return new JObject
            {
                ["raidFood"] = ReignRaidFoodDisruption.Diagnostics(),
                ["active"] = _harmony != null,
                ["schemaVersion"] = 2,
                ["allHooksHealthy"] = _harmony != null && HookTargets.Count == ExpectedEconomyHooks
                    && HookTargets.Keys.All(IsInstalled) && ReignWarEconomyNaval.Healthy && string.IsNullOrEmpty(_fieldWageUiError),
                ["fieldWageUiError"] = _fieldWageUiError,
                ["navalEconomy"] = ReignWarEconomyNaval.Coverage(),
                ["expectedHookCount"] = ExpectedEconomyHooks,
                ["tariffCollectionVersion"] = ReignWarEconomyTariffs.Version,
                ["tariffCollectionHook"] = IsInstalled(nameof(TariffPurchasePrefix)) && IsInstalled(nameof(TariffPurchaseFinalizer))
                    && IsInstalled(nameof(TariffPurchaseTranspiler)) && IsInstalled(nameof(TariffPoolPrefix)) && IsInstalled(nameof(TariffPoolPostfix))
                    && IsInstalled(nameof(FoodProvisionPrefix)) && IsInstalled(nameof(ProvisionFinalizer))
                    && IsInstalled(nameof(HorseProvisionPrefix)) && IsInstalled(nameof(HorseProvisionFinalizer)),
                ["nativeMarketSalesVersion"] = 1,
                ["npcCommanderEligibilityVersion"] = 1,
                ["npcCommanderEligibilityHook"] = IsInstalled(nameof(NpcCommanderEligibilityPrefix)),
                ["kingdomSupportEligibilityVersion"] = 1,
                ["kingdomSupportEligibilityHook"] = IsInstalled(nameof(NativeKingdomSupportPrefix)),
                ["wageObservationVersion"] = 2,
                ["leaderlessWageStoreObserverVersion"] = 1,
                ["leaderlessWageStoreHook"] = IsInstalled(nameof(LeaderlessWageStoreTranspiler))
                    && (NativeNavalFinanceType == null || IsInstalled(nameof(NavalLeaderlessWageStoreTranspiler))),
                ["garrisonWagePayer"] = "current_settlement_owner",
                ["kingdomWageModifierVersion"] = 1,
                ["kingdomWageModifierHook"] = IsInstalled(nameof(KingdomWagePostfix))
                    && (NativeNavalWageType == null || IsInstalled(nameof(NavalKingdomWagePostfix))),
                ["historicalWageCoverageComplete"] = false,
                ["nativeHorseMarketSaleHook"] = IsInstalled(nameof(NativeHorseMarketSaleTranspiler)),
                ["foodMarketVersion"] = ReignWarEconomyFoodMarket.Version,
                ["productionCurveVersion"] = ReignWarEconomyPolicy.ProductionCurveVersion,
                ["economyOptions"] = ReignWarEconomyOptions.Snapshot(),
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
                    ["castle_guard"] = ReignServices.Economy != null,
                    ["militia_conversion"] = "no supported native/Reign conversion discovered; unidentified routes fail coverage"
                },
                ["nativeAssembly"] = typeof(RecruitmentCampaignBehavior).Assembly.ManifestModule.ModuleVersionId.ToString(),
                ["wageModel"] = TaleWorlds.CampaignSystem.Campaign.Current?.Models.PartyWageModel?.GetType().FullName ?? "unavailable"
            };
        }

        // War Sails delegates these model methods to the initialized MBGameModel base.
        // Only the reviewed official wrapper and its immediate, exact Reign instance count.
        // A model elsewhere in the chain does not prove an arbitrary wrapper calls it.
        internal static JObject ModelRoute(object actual, Type expected, string knownNavalWrapper)
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
    }
}
