using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Save;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Economy
{
    public sealed class ReignEconomyCampaignBehavior : CampaignBehaviorBase
    {
        // Native object references avoid matching separate hulls by random-value collisions.
        internal List<TaleWorlds.CampaignSystem.MapEvents.MapEvent> MedicalEvents = new List<TaleWorlds.CampaignSystem.MapEvents.MapEvent>();
        internal List<long> MedicalIds = new List<long>();
        internal List<Ship> NavalShips = new List<Ship>();
        internal List<string> NavalShipIds = new List<string>();
        internal readonly Dictionary<Ship, string> NavalShipIndex = new Dictionary<Ship, string>(ReignWarEconomyReferenceComparer<Ship>.Instance);
        internal const int MaximumRecoveryStep = 10;
        internal const float HearthCostPerRecruit = 0.5f;
        internal const float MinimumVillageHearth = 10f;
        internal const float MinimumRecruitmentRecoveryModifier = 0.3f;
        internal const float MobilizationDailyDecayFactor = 0.95f;
        internal const float MobilizationDailyFlatRecovery = 0.5f;
        internal const float ReliefMaximumDonorShipment = (float)ReignWarEconomyPolicy.ReliefDonorMaximum;
        internal const float ReliefMinimumRecipientShipment = (float)ReignWarEconomyPolicy.ReliefRecipientMinimum;
        internal const float ReliefMaximumRouteDistance = 180f;

        private Dictionary<string, int> _villageRecoverySteps = new Dictionary<string, int>(StringComparer.Ordinal);
        private Dictionary<string, float> _villageMobilizationStrain = new Dictionary<string, float>(StringComparer.Ordinal);
        private List<string> _economyStateChunks = new List<string>();
        internal ReignEconomyState EconomyState { get; private set; } = new ReignEconomyState();
        private Dictionary<string, float> _reliefLoyaltyModifiers = new Dictionary<string, float>(StringComparer.Ordinal);
        private int _lastReliefDay = -1;
        private int _recruitRequests;
        private int _recruitsGranted;
        private int _recruitFloorBlocks;
        private float _recruitHearthDebited;
        private int _recoveryTransitions;
        private int _siegeShockCount;
        private float _siegeSecurityLost;
        private int _retinueCreations;
        private int _retinueTroops;
        private int _retinueDuplicatePartyInitializations;
        private int _patrolTroopsTransferred;
        private int _patrolTroopsReturned;
        private int _patrolConservationMismatches;
        private int _reliefShipmentCount;
        private float _reliefFoodShipped;
        private float _reliefFoodDelivered;
        private float _reliefFoodLost;
        private int _reliefBlockedRequests;
        private int _currentReliefDonors;
        private int _currentReliefRecipients;
        private int _currentReliefUnmet;
        private float _currentReliefFoodShipped;
        private float _currentReliefFoodDelivered;
        private float _currentReliefFoodLost;
        // Last daily pass only; operator evidence is process-local, never replayed from a save.
        private readonly JArray _reliefLastPasses = new JArray();
        private ReliefEvidenceBudget _reliefEvidenceBudget = new ReliefEvidenceBudget();
        private double _reliefEvidenceDay = -1;
        private int _reliefEvidenceErrors, _reliefEvidenceDroppedTotal;
        internal bool ReliefFixtureActive;
        internal long ReliefFixtureSuppressedPasses;
        private sealed class ReliefEvidenceBudget
        {
            internal int Networks = 64, Holdings = 256, Shipments = 2048, Decisions = 256, Dropped;
        }

        public static ReignEconomyCampaignBehavior Instance { get; private set; }

        public ReignEconomyCampaignBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            ReignMedicalDischarge.Install();
            ReignNpcCaravanInvestments.Install();
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, ReignMedicalDischarge.Started);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, ReignMedicalDischarge.Prune);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ =>
            {
                _reliefLastPasses.Clear(); _reliefEvidenceBudget = new ReliefEvidenceBudget();
                _reliefEvidenceDay = -1; _reliefEvidenceErrors = 0; _reliefEvidenceDroppedTotal = 0;
                ReliefFixtureActive = false; ReliefFixtureSuppressedPasses = 0;
                ReignEconomyPatches.ApplyNavalRecruitment();
                ReignRaidFoodDisruption.Install();
                ReignRaidFoodDisruption.Initialize();
                ReignWarEconomyTransactions.SeedExistingGenerations();
                ReignWarEconomyTransactions.ReconcilePending();
            });
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, ReignWarEconomyTransactions.OnPartyRegistered);
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, ReignNpcCaravanInvestments.OnCreated);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, ReignNpcCaravanInvestments.OnDestroyed);
            CampaignEvents.OnPartyDisbandedEvent.AddNonSerializedListener(this, ReignNpcCaravanInvestments.OnDisbanded);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.VillageStateChanged.AddNonSerializedListener(this, OnVillageStateChanged);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnEconomyOwnershipChanged);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnSabotageClanChangedRealm);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(this, OnSiegeAftermathApplied);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                ReignMedicalDischarge.Prune();
                ReignWarEconomyNaval.PruneAcquisitions();
                _economyStateChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(EconomyState));
            }
            dataStore.SyncData("_reignEconomy_payload_v2", ref _economyStateChunks);
            dataStore.SyncData("_reignEconomy_medicalEvents_v1", ref MedicalEvents);
            dataStore.SyncData("_reignEconomy_medicalIds_v1", ref MedicalIds);
            MedicalEvents = MedicalEvents ?? new List<TaleWorlds.CampaignSystem.MapEvents.MapEvent>();
            MedicalIds = MedicalIds ?? new List<long>();
            dataStore.SyncData("_reignEconomy_navalShips_v1", ref NavalShips);
            dataStore.SyncData("_reignEconomy_navalShipIds_v1", ref NavalShipIds);
            dataStore.SyncData("_reignEconomy_villageRecoverySteps", ref _villageRecoverySteps);
            dataStore.SyncData("_reignEconomy_villageMobilizationStrain", ref _villageMobilizationStrain);
            dataStore.SyncData("_reignEconomy_reliefLoyaltyModifiers", ref _reliefLoyaltyModifiers);
            dataStore.SyncData("_reignEconomy_lastReliefDay", ref _lastReliefDay);
            dataStore.SyncData("_reignEconomy_recruitRequests", ref _recruitRequests);
            dataStore.SyncData("_reignEconomy_recruitsGranted", ref _recruitsGranted);
            dataStore.SyncData("_reignEconomy_recruitFloorBlocks", ref _recruitFloorBlocks);
            dataStore.SyncData("_reignEconomy_recruitHearthDebited", ref _recruitHearthDebited);
            dataStore.SyncData("_reignEconomy_recoveryTransitions", ref _recoveryTransitions);
            dataStore.SyncData("_reignEconomy_siegeShockCount", ref _siegeShockCount);
            dataStore.SyncData("_reignEconomy_siegeSecurityLost", ref _siegeSecurityLost);
            dataStore.SyncData("_reignEconomy_retinueCreations", ref _retinueCreations);
            dataStore.SyncData("_reignEconomy_retinueTroops", ref _retinueTroops);
            dataStore.SyncData("_reignEconomy_retinueDuplicatePartyInitializations", ref _retinueDuplicatePartyInitializations);
            dataStore.SyncData("_reignEconomy_patrolTroopsTransferred", ref _patrolTroopsTransferred);
            dataStore.SyncData("_reignEconomy_patrolTroopsReturned", ref _patrolTroopsReturned);
            dataStore.SyncData("_reignEconomy_patrolConservationMismatches", ref _patrolConservationMismatches);
            dataStore.SyncData("_reignEconomy_reliefShipmentCount", ref _reliefShipmentCount);
            dataStore.SyncData("_reignEconomy_reliefFoodShipped", ref _reliefFoodShipped);
            dataStore.SyncData("_reignEconomy_reliefFoodDelivered", ref _reliefFoodDelivered);
            dataStore.SyncData("_reignEconomy_reliefFoodLost", ref _reliefFoodLost);
            dataStore.SyncData("_reignEconomy_reliefBlockedRequests", ref _reliefBlockedRequests);
            dataStore.SyncData("_reignEconomy_currentReliefDonors", ref _currentReliefDonors);
            dataStore.SyncData("_reignEconomy_currentReliefRecipients", ref _currentReliefRecipients);
            dataStore.SyncData("_reignEconomy_currentReliefUnmet", ref _currentReliefUnmet);
            dataStore.SyncData("_reignEconomy_currentReliefFoodShipped", ref _currentReliefFoodShipped);
            dataStore.SyncData("_reignEconomy_currentReliefFoodDelivered", ref _currentReliefFoodDelivered);
            dataStore.SyncData("_reignEconomy_currentReliefFoodLost", ref _currentReliefFoodLost);
            if (_villageRecoverySteps == null)
            {
                _villageRecoverySteps = new Dictionary<string, int>(StringComparer.Ordinal);
            }
            if (_villageMobilizationStrain == null)
            {
                _villageMobilizationStrain = new Dictionary<string, float>(StringComparer.Ordinal);
            }
            if (_reliefLoyaltyModifiers == null)
            {
                _reliefLoyaltyModifiers = new Dictionary<string, float>(StringComparer.Ordinal);
            }
            if (dataStore.IsLoading)
            {
                EconomyState = _economyStateChunks?.Count > 0
                    ? JsonConvert.DeserializeObject<ReignEconomyState>(ReignSavePayloadCodec.Decode(_economyStateChunks))
                    : new ReignEconomyState();
                if (EconomyState == null || EconomyState.Version != ReignWarEconomyPolicy.Version)
                    throw new InvalidOperationException("Unsupported Reign economy save payload; refusing to discard its receipts.");
                if (EconomyState.DrawdownCarry == null || EconomyState.DrawdownCarry.Count > ReignWarEconomyPolicy.DrawdownPartyLimit)
                    throw new InvalidOperationException("Unsupported Reign drawdown carry payload.");
                if (EconomyState.RaidFood == null || EconomyState.RaidFood.Version != ReignRaidFoodPolicy.Version)
                    throw new InvalidOperationException("Unsupported raid food save payload; refusing to discard its damage.");
                ReignRaidFoodPolicy.NormalizeCollections(EconomyState.RaidFood);
                if (EconomyState.NpcFoodSabotage?.Version != ReignNpcFoodSabotagePolicy.Version || EconomyState.NpcFoodSabotage.Recent == null
                    || EconomyState.NpcFoodSabotage.Recent.Count > ReignNpcFoodSabotagePolicy.ReceiptLimit
                    || EconomyState.NpcFoodSabotage.Recent.Any(x => x == null))
                    throw new InvalidOperationException("Unsupported NPC food sabotage journal; refusing to discard its receipts.");
                foreach (var pair in _villageRecoverySteps)
                    if (!EconomyState.Recovery.ContainsKey(pair.Key))
                        EconomyState.Recovery[pair.Key] = (float)ReignWarEconomyPolicy.Clamp(pair.Value / 10d, 0, 1);
                // Native transactions are synchronous. A persisted interrupted receipt is evidence,
                // never authority to replay a payment or enlistment on load.
                if (EconomyState.NavalVersion != ReignWarEconomyNaval.Version)
                    throw new InvalidOperationException("Unsupported naval economy receipt payload.");
                ReignWarEconomyNaval.RestoreAcquisitions(this);
                if (EconomyState.Pending.Count > 0 || EconomyState.NavalPending.Count > 0)
                    EconomyState.LastError = "Interrupted native economy transactions require reconciliation; no automatic replay.";
            }
        }

        internal int GetRecoveryStep(Village village)
        {
            return (int)Math.Floor(GetRecoveryModifier(village) * MaximumRecoveryStep + .0001f);
        }

        internal float GetFoodRecoveryModifier(Village village) => ReignRaidFoodDisruption.Effective(village);

        internal float GetRecoveryModifier(Village village)
        {
            if (village == null) return 1f;
            if (village.VillageState == Village.VillageStates.Looted) return 0f;
            return GetStoredRecoveryModifier(village);
        }
        internal float GetStoredRecoveryModifier(Village village)
        {
            if(village==null)return 1f;
            if (EconomyState.Recovery.TryGetValue(village.StringId, out float recovery))
                return (float)ReignWarEconomyPolicy.Clamp(recovery, 0, 1);
            return _villageRecoverySteps.TryGetValue(village.StringId, out int step)
                ? (float)ReignWarEconomyPolicy.Clamp(step / 10d, 0, 1) : 1f;
        }

        internal float GetMobilizationStrain(Village village)
        {
            if (village == null || !_villageMobilizationStrain.TryGetValue(village.StringId, out float strain))
            {
                return 0f;
            }

            return Math.Max(0f, strain);
        }

        internal float GetMobilizationFoodPenalty(Village village)
        {
            // Retained for old telemetry consumers. Strain now slows household recovery.
            return 0f;
        }

        internal static bool IsAgricultureStressed(Village village) => village?.Bound?.Town != null
            && (village.Bound.Town.FoodChange < 0 || village.Bound.Town.Security < 50);

        internal float GetNaturalGrowth(Village village) => village?.VillageState == Village.VillageStates.Normal
            ? (float)ReignWarEconomyPolicy.NaturalGrowth(village.Hearth, GetMobilizationStrain(village), IsAgricultureStressed(village)) : 0;

        internal float GetReliefLoyaltyModifier(Town town)
        {
            if (town?.Settlement == null
                || !_reliefLoyaltyModifiers.TryGetValue(town.Settlement.StringId, out float modifier))
            {
                return 0f;
            }

            return Math.Max(-0.2f, Math.Min(0.2f, modifier));
        }

        internal int GetRecruitCapacity(Settlement settlement)
        {
            return GetEligibleVillages(settlement).Sum(village => ReignWarEconomyPolicy.RecruitCapacity(
                village.Hearth, GetRecoveryModifier(village), true));
        }

        internal int ConsumeRecruitHearth(Settlement settlement, int requestedTroops)
        {
            int affordable = Math.Min(Math.Max(0, requestedTroops), GetRecruitCapacity(settlement));
            _recruitRequests += Math.Max(0, requestedTroops);
            if (affordable < Math.Max(0, requestedTroops)) _recruitFloorBlocks++;
            for (int i = 0; i < affordable; i++)
            {
                List<Village> villages = GetEligibleVillages(settlement)
                    .Where(village => village.Hearth - MinimumVillageHearth >= HearthCostPerRecruit)
                    .ToList();
                if (villages.Count == 0)
                {
                    return i;
                }

                Village source = ChooseWeightedHearthVillage(villages);
                source.Hearth = Math.Max(MinimumVillageHearth, source.Hearth - HearthCostPerRecruit);
                _villageMobilizationStrain[source.StringId] = GetMobilizationStrain(source) + 1f;
                _recruitsGranted++;
                _recruitHearthDebited += HearthCostPerRecruit;
            }

            return affordable;
        }

        internal void RestorePopulation(string villageId, float hearth, float strain)
        {
            // Saved population/recovery keys are Village component IDs, not Settlement IDs in native campaigns.
            var village = Settlement.All.Select(x => x.Village).FirstOrDefault(x => x?.StringId == villageId)
                ?? Settlement.Find(villageId)?.Village
                ?? throw new InvalidOperationException("Missing receipt village " + villageId);
            float restored = Math.Max(0, hearth - village.Hearth);
            _recruitHearthDebited -= restored;
            _recruitsGranted -= (int)Math.Round(restored / HearthCostPerRecruit);
            village.Hearth = hearth;
            _villageMobilizationStrain[villageId] = strain;
        }

        internal void RecordRecruitmentLimit(int requested, int allowed)
        {
            if (requested > 0 && allowed < requested) _recruitFloorBlocks++;
        }

        internal void RecordRetinue(int troops)
        {
            _retinueCreations++;
            _retinueTroops += Math.Max(0, troops);
        }

        internal void RecordDuplicateRetinueInitialization()
        {
            _retinueDuplicatePartyInitializations++;
        }

        internal void RecordPatrolTransfer(int troops, bool conservationMismatch)
        {
            _patrolTroopsTransferred += Math.Max(0, troops);
            if (conservationMismatch) _patrolConservationMismatches++;
        }

        internal void RecordPatrolReturn(int troops, bool conservationMismatch)
        {
            _patrolTroopsReturned += Math.Max(0, troops);
            if (conservationMismatch) _patrolConservationMismatches++;
        }

        internal int RecruitRequests => _recruitRequests;
        internal int RecruitsGranted => _recruitsGranted;
        internal int RecruitFloorBlocks => _recruitFloorBlocks;
        internal float RecruitHearthDebited => _recruitHearthDebited;
        internal int RecoveryTransitions => _recoveryTransitions;
        internal int SiegeShockCount => _siegeShockCount;
        internal float SiegeSecurityLost => _siegeSecurityLost;
        internal int RetinueCreations => _retinueCreations;
        internal int RetinueTroops => _retinueTroops;
        internal int RetinueDuplicateInitializationsBlocked => _retinueDuplicatePartyInitializations;
        internal int PatrolTroopsTransferred => _patrolTroopsTransferred;
        internal int PatrolTroopsReturned => _patrolTroopsReturned;
        internal int PatrolConservationMismatches => _patrolConservationMismatches;
        internal int ReliefShipmentCount => _reliefShipmentCount;
        internal float ReliefFoodShipped => _reliefFoodShipped;
        internal float ReliefFoodDelivered => _reliefFoodDelivered;
        internal float ReliefFoodLost => _reliefFoodLost;
        internal int ReliefBlockedRequests => _reliefBlockedRequests;
        internal int CurrentReliefDonors => _currentReliefDonors;
        internal int CurrentReliefRecipients => _currentReliefRecipients;
        internal int CurrentReliefUnmet => _currentReliefUnmet;
        internal float CurrentReliefFoodShipped => _currentReliefFoodShipped;
        internal float CurrentReliefFoodDelivered => _currentReliefFoodDelivered;
        internal float CurrentReliefFoodLost => _currentReliefFoodLost;

        private void OnDailyTick()
        {
            ReignWarEconomyNaval.AssertCampaignScope();
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            if (_lastReliefDay == day)
            {
                return;
            }

            _lastReliefDay = day;
            ReignRaidFoodDisruption.PruneBoats();
            ReignMilitaryReadiness.UpdateNpcStandings();
            ReignMilitaryReadiness.DrawdownDaily();
            ReignHouseholdStipends.Tick(EconomyState.HouseholdStipends, CampaignTime.Now.ToDays);
            ReignNpcCaravanInvestments.Tick(day);
            ReignWarEconomyTransactions.ObserveDailyResources();
            DecayMobilizationStrain();
            ReignNpcFoodSabotage.Tick(day);
            ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.ProcessEconomyAidDaily();
            ApplyRealmRelief();
        }

        private void OnEconomyOwnershipChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            ReignNpcFoodSabotage.OwnershipChanged(settlement);
            ReignHouseholdStipends.OwnershipChanged(EconomyState.HouseholdStipends, settlement, CampaignTime.Now.ToDays);
        }

        private void OnSabotageClanChangedRealm(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
            => ReignNpcFoodSabotage.ClanChangedRealm(clan, newKingdom);

        private void OnHourlyTick()
        {
            ReignWarEconomyNaval.AssertCampaignScope();
#if !REIGN_EXCLUDE_COURT
            ReignBeta.Court.ReignCourtCampaignBehavior.Instance?.ProcessEconomyShipmentArrivals();
#endif
        }

        private void DecayMobilizationStrain()
        {
            foreach (string villageId in _villageMobilizationStrain.Keys.ToList())
            {
                float strain = (float)ReignWarEconomyPolicy.DecayStrain(_villageMobilizationStrain[villageId]);
                if (strain < 0.01f)
                {
                    _villageMobilizationStrain.Remove(villageId);
                }
                else
                {
                    _villageMobilizationStrain[villageId] = strain;
                }
            }
        }

        private void ApplyRealmRelief()
        {
            _reliefLastPasses.Clear(); _reliefEvidenceBudget = new ReliefEvidenceBudget();
            _reliefEvidenceDay = CampaignTime.Now.ToDays;
            _reliefLoyaltyModifiers.Clear();
            _currentReliefDonors = 0;
            _currentReliefRecipients = 0;
            _currentReliefUnmet = 0;
            _currentReliefFoodShipped = 0f;
            _currentReliefFoodDelivered = 0f;
            _currentReliefFoodLost = 0f;
            EconomyState.UnmetReliefFood = 0;

            List<Town> fortifications = Settlement.All
                .Where(x => x?.Town != null && (x.IsTown || x.IsCastle) && x.OwnerClan != null)
                .Select(x => x.Town).ToList();
            foreach (IGrouping<string, Town> network in fortifications
                .GroupBy(ReliefNetworkKey).Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                RouteReliefNetwork(network.ToList());
            }
        }

        internal static ReignWarEconomyPolicy.Holding ReliefHolding(Town town) => new ReignWarEconomyPolicy.Holding {
            Id = town.Settlement.StringId, Stock = town.FoodStocks, Capacity = town.FoodStocksUpperLimit(), Change = town.FoodChange,
            X = town.Settlement.GetPosition2D.X, Y = town.Settlement.GetPosition2D.Y, Security = town.Security, Besieged = town.IsUnderSiege };

        internal void RouteReliefNetwork(List<Town> network)
        {
            Dictionary<string, Town> towns;
            List<ReignWarEconomyPolicy.Holding> inputs;
            ReignWarEconomyPolicy.ReliefPlan plan;
            try
            {
                towns = network.ToDictionary(x => x.Settlement.StringId, StringComparer.Ordinal);
                inputs = network.Select(ReliefHolding).ToList(); plan = ReignWarEconomyPolicy.AllocateRelief(inputs);
            }
            catch (Exception ex)
            {
                if (ReliefFixtureActive) throw;
                _reliefEvidenceErrors++; ReignLog.Warn("Realm relief inputs rejected: " + ex.GetType().Name); return;
            }
            _currentReliefRecipients += plan.Recipients;
            _currentReliefUnmet += plan.UnmetRecipients;
            _reliefBlockedRequests += plan.BlockedRoutes;
            EconomyState.UnmetReliefFood += (float)plan.UnmetFood;
            foreach (var shipment in plan.Shipments)
            {
                towns[shipment.Donor].FoodStocks -= (float)shipment.Shipped;
                towns[shipment.Recipient].FoodStocks += (float)shipment.Delivered;
                RecordReliefShipment((float)shipment.Shipped, (float)shipment.Delivered);
            }
            _currentReliefDonors += plan.Shipments.Select(x => x.Donor).Distinct().Count();
            foreach (var recipient in plan.Shipments.Select(x => x.Recipient).Distinct())
                AddReliefLoyalty(towns[recipient], .05f);
            foreach (var town in network.Where(x => x.FoodStocks <= 0 && x.FoodChange < 0))
                AddReliefLoyalty(town, -.1f);
            if (ReliefFixtureActive) ReliefFixtureSuppressedPasses++;
            else
            {
                int droppedBefore = _reliefEvidenceBudget.Dropped;
                try
                {
                    if (_reliefEvidenceBudget.Networks <= 0) _reliefEvidenceBudget.Dropped++;
                    else
                    {
                        _reliefEvidenceBudget.Networks--;
                        var evidence = ReliefEvidence(inputs, plan,
                            network.ToDictionary(x => x.Settlement.StringId, x => (double)x.FoodStocks, StringComparer.Ordinal),
                            _reliefEvidenceBudget);
                        evidence["realmKey"] = ReliefNetworkKey(network[0]); evidence["observedDay"] = CampaignTime.Now.ToDays;
                        if (evidence.Value<bool?>("nativeStockConservationVerified") != true) _reliefEvidenceErrors++;
                        _reliefLastPasses.Add(evidence);
                    }
                }
                catch { _reliefEvidenceErrors++; } // Diagnostics cannot interrupt native stock processing.
                finally { _reliefEvidenceDroppedTotal += _reliefEvidenceBudget.Dropped - droppedBefore; }
            }
        }

        private static JObject ReliefEvidence(List<ReignWarEconomyPolicy.Holding> inputs,
            ReignWarEconomyPolicy.ReliefPlan plan, Dictionary<string, double> actualAfter, ReliefEvidenceBudget budget)
        {
            int holdings = Math.Min(budget.Holdings, inputs.Count), shipments = Math.Min(budget.Shipments, plan.Shipments.Count),
                decisions = Math.Min(budget.Decisions, plan.Decisions.Count);
            budget.Holdings -= holdings; budget.Shipments -= shipments; budget.Decisions -= decisions;
            int omitted = inputs.Count - holdings + plan.Shipments.Count - shipments + plan.Decisions.Count - decisions;
            budget.Dropped += omitted;
            double before = inputs.Sum(x => x.Stock), sent = plan.Shipments.Sum(x => x.Shipped),
                delivered = plan.Shipments.Sum(x => x.Delivered);
            double residual = actualAfter == null ? 0 : actualAfter.Values.Sum() - before + sent - delivered;
            bool stocksMatch = actualAfter != null && inputs.All(x => actualAfter.TryGetValue(x.Id, out double stock)
                && Math.Abs(stock - (x.Stock - plan.Shipments.Where(s => s.Donor == x.Id).Sum(s => s.Shipped)
                    + plan.Shipments.Where(s => s.Recipient == x.Id).Sum(s => s.Delivered))) < .002);
            return new JObject { ["policyVersion"] = plan.Version, ["legacyFixedLimits"] = plan.LegacyFixedLimits,
                ["actualDispatch"] = actualAfter != null, ["truncated"] = omitted > 0,
                ["holdingCount"] = inputs.Count, ["stockBefore"] = before,
                ["stockAfter"] = actualAfter == null ? JValue.CreateNull() : JToken.FromObject(actualAfter.Values.Sum()),
                ["plannedShipped"] = sent, ["plannedDelivered"] = delivered, ["plannedSpoilage"] = sent - delivered,
                ["actualConservationResidual"] = actualAfter == null ? JValue.CreateNull() : JToken.FromObject(residual),
                ["nativeStockConservationVerified"] = actualAfter == null ? JValue.CreateNull()
                    : JToken.FromObject(stocksMatch && Math.Abs(residual) < .02 && actualAfter.Values.Sum() <= before + .02
                        && plan.Shipments.All(s => s.Delivered >= 0 && s.Delivered <= s.Shipped)),
                ["projectedRatio"] = plan.ProjectedRatio, ["donorFloor"] = plan.DonorFloor,
                ["recipientTarget"] = plan.Target, ["projectedTargetUnmetFood"] = plan.UnmetFood,
                ["blockedRoutes"] = plan.BlockedRoutes,
                ["holdings"] = new JArray(inputs.OrderBy(x => x.Id, StringComparer.Ordinal).Take(holdings).Select(x => new JObject {
                    ["id"] = x.Id, ["stock"] = x.Stock, ["capacity"] = x.Capacity, ["nativeNetBeforeRelief"] = x.Change,
                    ["projected"] = x.Projected, ["x"] = x.X, ["y"] = x.Y, ["security"] = x.Security, ["besieged"] = x.Besieged,
                    ["eligibleCapacity"] = x.Capacity > 0,
                    ["donorDailyLimit"] = plan.DonorLimits.TryGetValue(x.Id, out double limit) ? limit : 0,
                    ["actualStockAfter"] = actualAfter != null && actualAfter.TryGetValue(x.Id, out double actualStock)
                        ? JToken.FromObject(actualStock) : JValue.CreateNull() })),
                ["shipments"] = JArray.FromObject(plan.Shipments.Take(shipments)),
                ["recipientDecisions"] = JArray.FromObject(plan.Decisions.Take(decisions)) };
        }

        internal JObject ReliefDiagnostics(bool detailed)
        {
            var result = new JObject { ["version"] = ReignWarEconomyPolicy.ReliefVersion, ["processLocal"] = true, ["lastDailyPassDay"] = _reliefEvidenceDay,
                ["available"] = _reliefEvidenceDay >= 0 && _reliefLastPasses.Count > 0,
                ["stale"] = _reliefEvidenceDay < 0 || CampaignTime.Now.ToDays < _reliefEvidenceDay
                    || CampaignTime.Now.ToDays - _reliefEvidenceDay > 1.1,
                ["capturedDay"] = CampaignTime.Now.ToDays, ["errors"] = _reliefEvidenceErrors,
                ["dropped"] = _reliefEvidenceDroppedTotal, ["lastDailyPassDropped"] = _reliefEvidenceBudget.Dropped,
                ["dropCountUnit"] = "omitted networks or rows; zero is required for complete capture",
                ["suppressedFixturePasses"] = ReliefFixtureSuppressedPasses,
                ["maximumNetworks"] = 64, ["maximumHoldingsPerView"] = 256, ["maximumShipmentsPerView"] = 2048,
                ["coverageNote"] = "Actual last daily passes are separate from current-input counterfactuals. Projected target unmet food is not a starvation count. Native float stock residuals are reported. Missing/stale/truncated evidence cannot certify coverage." };
            if (!detailed) return result;
            result["lastDailyPasses"] = _reliefLastPasses.DeepClone();
            var currentBudget = new ReliefEvidenceBudget(); var legacyBudget = new ReliefEvidenceBudget();
            var current = new JArray(); var legacy = new JArray();
            try
            {
                foreach (var network in Settlement.All.Where(x => x?.Town != null && (x.IsTown || x.IsCastle) && x.OwnerClan != null)
                    .Select(x => x.Town).GroupBy(ReliefNetworkKey).Where(x => !string.IsNullOrWhiteSpace(x.Key))
                    .OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (currentBudget.Networks <= 0) { currentBudget.Dropped++; legacyBudget.Dropped++; continue; }
                    currentBudget.Networks--;
                    var inputs = network.Select(ReliefHolding).ToList();
                    if (inputs.Count > currentBudget.Holdings)
                    { currentBudget.Dropped += inputs.Count; legacyBudget.Dropped += inputs.Count; continue; }
                    var active = ReliefEvidence(inputs, ReignWarEconomyPolicy.AllocateRelief(inputs), null, currentBudget);
                    var prior = ReliefEvidence(inputs, ReignWarEconomyPolicy.AllocateRelief(inputs, true), null, legacyBudget);
                    active["realmKey"] = network.Key; prior["realmKey"] = network.Key; current.Add(active); legacy.Add(prior);
                }
                result["currentInputForecast"] = current; result["sameInputLegacyForecast"] = legacy;
                result["forecastDropped"] = currentBudget.Dropped + legacyBudget.Dropped;
                result["forecastAvailable"] = current.Count > 0;
                result["forecastComplete"] = currentBudget.Dropped == 0 && legacyBudget.Dropped == 0;
                result["forecastError"] = "";
            }
            catch (Exception ex) { result["forecastError"] = ex.GetType().Name; }
            return result;
        }
        private void RecordReliefShipment(float shipped, float delivered)
        {
            float lost = Math.Max(0f, shipped - delivered);
            _reliefShipmentCount++;
            _reliefFoodShipped += shipped;
            _reliefFoodDelivered += delivered;
            _reliefFoodLost += lost;
            _currentReliefFoodShipped += shipped;
            _currentReliefFoodDelivered += delivered;
            _currentReliefFoodLost += lost;
        }

        private void AddReliefLoyalty(Town town, float change)
        {
            string id = town?.Settlement?.StringId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) return;
            _reliefLoyaltyModifiers[id] = Math.Max(-0.2f, Math.Min(0.2f,
                (_reliefLoyaltyModifiers.TryGetValue(id, out float current) ? current : 0f) + change));
        }

        internal static string ReliefNetworkKey(Town town)
        {
            if (town?.OwnerClan?.Kingdom != null)
                return "kingdom:" + town.OwnerClan.Kingdom.StringId;
            return town?.OwnerClan == null ? string.Empty : "clan:" + town.OwnerClan.StringId;
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement?.Village == null)
            {
                return;
            }

            Village village = settlement.Village;
            ReignRaidFoodDisruption.Settle(village);
            if (village.VillageState == Village.VillageStates.Looted)
            {
                _villageRecoverySteps[village.StringId] = 0;
                EconomyState.Recovery[village.StringId] = 0;
                return;
            }

            if (village.VillageState != Village.VillageStates.Normal)
            {
                return;
            }

            float recovery = GetRecoveryModifier(village);
            if (recovery < 1f)
            {
                EconomyState.Recovery[village.StringId] = (float)ReignWarEconomyPolicy.Recover(recovery, true, IsAgricultureStressed(village));
                _villageRecoverySteps[village.StringId] = GetRecoveryStep(village);
                _recoveryTransitions++;
            }
        }

        private void OnVillageStateChanged(
            Village village,
            Village.VillageStates oldState,
            Village.VillageStates newState,
            MobileParty raiderParty)
        {
            if (village == null)
            {
                return;
            }

            ReignRaidFoodDisruption.VillageChanged(village, oldState, newState);

            if (newState == Village.VillageStates.Looted
                || (newState == Village.VillageStates.Normal && oldState == Village.VillageStates.Looted))
            {
                _villageRecoverySteps[village.StringId] = 0;
                EconomyState.Recovery[village.StringId] = 0;
            }
        }

        private void OnTroopRecruited(
            Hero recruiter,
            Settlement settlement,
            Hero recruitmentSource,
            CharacterObject troop,
            int amount)
        {
            // Known native adapters own the debit, after observing successful roster changes.
            // Unexpected events are unsupported evidence, never an implicit free-enlistment exemption.
            if (!ReignEconomyPatches.IsObservedRecruitment && amount > 0
                && !ReignEconomyPatches.TryObserveAiPrisonerTransfer(recruiter, settlement, recruitmentSource, troop, amount))
            {
                EconomyState.UnsupportedEvents++;
                EconomyState.LastError = "Unattributed recruitment event: " + (troop?.StringId ?? "unknown");
            }
        }

        private void OnSiegeAftermathApplied(
            MobileParty attackerParty,
            Settlement settlement,
            SiegeAftermathAction.SiegeAftermath aftermath,
            Clan previousSettlementOwner,
            Dictionary<MobileParty, float> partyContributions)
        {
            if (settlement?.Town == null)
            {
                return;
            }

            float securityLoss;
            switch (aftermath)
            {
                case SiegeAftermathAction.SiegeAftermath.Devastate:
                    securityLoss = 30f;
                    break;
                case SiegeAftermathAction.SiegeAftermath.Pillage:
                    securityLoss = 15f;
                    break;
                default:
                    securityLoss = 5f;
                    break;
            }

            settlement.Town.Security -= securityLoss;
            _siegeShockCount++;
            _siegeSecurityLost += securityLoss;
            ReignLog.Info("Economy security shock: " + settlement.StringId + " lost " + securityLoss + " after " + aftermath + ".");
        }

        internal List<Village> GetEligibleVillages(Settlement settlement)
        {
            if (settlement?.Village != null)
            {
                return settlement.Village.VillageState == Village.VillageStates.Normal
                    && GetRecoveryModifier(settlement.Village) + .000001f >= MinimumRecruitmentRecoveryModifier
                    ? new List<Village> { settlement.Village }
                    : new List<Village>();
            }

            if (settlement == null)
            {
                return new List<Village>();
            }

            return settlement.BoundVillages
                .Where(village => village != null
                    && village.VillageState == Village.VillageStates.Normal
                    && GetRecoveryModifier(village) + .000001f >= MinimumRecruitmentRecoveryModifier)
                .ToList();
        }

        private static Village ChooseWeightedHearthVillage(List<Village> villages)
        {
            float totalWeight = villages.Sum(village => Math.Max(0f, village.Hearth - MinimumVillageHearth));
            if (totalWeight <= 0f)
            {
                return villages[0];
            }

            float roll = MBRandom.RandomFloat * totalWeight;
            foreach (Village village in villages)
            {
                roll -= Math.Max(0f, village.Hearth - MinimumVillageHearth);
                if (roll <= 0f)
                {
                    return village;
                }
            }

            return villages[villages.Count - 1];
        }
    }
}
