using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Economy
{
    public sealed class ReignEconomyCampaignBehavior : CampaignBehaviorBase
    {
        internal const int MaximumRecoveryStep = 10;
        internal const float HearthCostPerRecruit = 0.5f;
        internal const float MinimumVillageHearth = 10f;
        internal const float MinimumRecruitmentRecoveryModifier = 0.3f;
        internal const float MobilizationDailyDecayFactor = 0.95f;
        internal const float MobilizationDailyFlatRecovery = 0.5f;
        internal const float ReliefDonorEntryRatio = 0.7f;
        internal const float ReliefDonorReserveRatio = 0.6f;
        internal const float ReliefRecipientEntryRatio = 0.3f;
        internal const float ReliefRecipientExitRatio = 0.5f;
        internal const float ReliefMaximumDonorShipment = 10f;
        internal const float ReliefMaximumRecipientShipment = 15f;
        internal const float ReliefMaximumRouteDistance = 180f;

        private Dictionary<string, int> _villageRecoverySteps = new Dictionary<string, int>(StringComparer.Ordinal);
        private Dictionary<string, float> _villageMobilizationStrain = new Dictionary<string, float>(StringComparer.Ordinal);
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

        public static ReignEconomyCampaignBehavior Instance { get; private set; }

        public ReignEconomyCampaignBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.VillageStateChanged.AddNonSerializedListener(this, OnVillageStateChanged);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(this, OnSiegeAftermathApplied);
        }

        public override void SyncData(IDataStore dataStore)
        {
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
        }

        internal int GetRecoveryStep(Village village)
        {
            if (village == null)
            {
                return MaximumRecoveryStep;
            }

            if (village.VillageState == Village.VillageStates.Looted)
            {
                return 0;
            }

            if (_villageRecoverySteps.TryGetValue(village.StringId, out int step))
            {
                return Math.Max(0, Math.Min(MaximumRecoveryStep, step));
            }

            // Backward compatibility: normal villages from saves created before
            // this feature enter at full production rather than being damaged.
            return MaximumRecoveryStep;
        }

        internal float GetRecoveryModifier(Village village)
        {
            return GetRecoveryStep(village) / (float)MaximumRecoveryStep;
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
            if (village == null || village.VillageState != Village.VillageStates.Normal)
            {
                return 0f;
            }

            float ratio = GetMobilizationStrain(village) / Math.Max(1f, village.Hearth);
            if (ratio < 0.1f) return 0f;
            if (ratio < 0.2f) return 1f;
            if (ratio < 0.35f) return 2f;
            return 4f;
        }

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
            double availableHearth = GetEligibleVillages(settlement)
                .Sum(village => Math.Max(0f, village.Hearth - MinimumVillageHearth));
            return Math.Max(0, (int)Math.Floor(availableHearth / HearthCostPerRecruit + 0.0001d));
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
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            if (_lastReliefDay == day)
            {
                return;
            }

            _lastReliefDay = day;
            DecayMobilizationStrain();
            ApplyRealmRelief();
        }

        private void DecayMobilizationStrain()
        {
            foreach (string villageId in _villageMobilizationStrain.Keys.ToList())
            {
                float strain = Math.Max(0f,
                    _villageMobilizationStrain[villageId] * MobilizationDailyDecayFactor
                    - MobilizationDailyFlatRecovery);
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
            _reliefLoyaltyModifiers.Clear();
            _currentReliefDonors = 0;
            _currentReliefRecipients = 0;
            _currentReliefUnmet = 0;
            _currentReliefFoodShipped = 0f;
            _currentReliefFoodDelivered = 0f;
            _currentReliefFoodLost = 0f;

            List<Town> fortifications = Settlement.All
                .Where(x => x?.Town != null && (x.IsTown || x.IsCastle) && x.OwnerClan != null)
                .Select(x => x.Town).ToList();
            foreach (IGrouping<string, Town> network in fortifications
                .GroupBy(ReliefNetworkKey).Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                RouteReliefNetwork(network.ToList());
            }
        }

        private void RouteReliefNetwork(List<Town> network)
        {
            Dictionary<Town, float> donorRemaining = network
                .Where(IsReliefDonor)
                .ToDictionary(town => town, town => Math.Min(ReliefMaximumDonorShipment,
                    Math.Max(0f, town.FoodStocks - town.FoodStocksUpperLimit() * ReliefDonorReserveRatio)));
            List<Town> recipients = network.Where(IsReliefRecipient)
                .OrderBy(FoodRatio).ThenBy(x => x.FoodChange)
                .ThenBy(x => x.Settlement.StringId).ToList();
            HashSet<Town> usedDonors = new HashSet<Town>();
            _currentReliefRecipients += recipients.Count;

            foreach (Town recipient in recipients)
            {
                float received = 0f;
                float target = recipient.FoodStocksUpperLimit() * ReliefRecipientExitRatio;
                float needed = Math.Max(0f, target - recipient.FoodStocks);
                if (recipient.IsUnderSiege)
                {
                    _reliefBlockedRequests++;
                }
                else
                {
                    foreach (Town donor in donorRemaining.Keys
                        .Where(x => donorRemaining[x] > 0.001f && !recipients.Contains(x))
                        .OrderBy(x => x.Settlement.GetPosition2D.DistanceSquared(recipient.Settlement.GetPosition2D))
                        .ThenBy(x => x.Settlement.StringId).ToList())
                    {
                        float efficiency = ReliefRouteEfficiency(donor, recipient);
                        if (efficiency <= 0f || received >= needed - 0.001f
                            || received >= ReliefMaximumRecipientShipment - 0.001f)
                        {
                            continue;
                        }

                        float shipment = Math.Min(donorRemaining[donor],
                            (ReliefMaximumRecipientShipment - received) / efficiency);
                        shipment = Math.Min(shipment, (needed - received) / efficiency);
                        if (shipment <= 0.001f)
                        {
                            continue;
                        }

                        float delivered = shipment * efficiency;
                        donor.FoodStocks -= shipment;
                        recipient.FoodStocks += delivered;
                        donorRemaining[donor] -= shipment;
                        received += delivered;
                        usedDonors.Add(donor);
                        RecordReliefShipment(shipment, delivered);
                    }
                }

                if (received > 0.001f)
                {
                    AddReliefLoyalty(recipient, 0.05f);
                }
                else if (needed > 0.001f && !recipient.IsUnderSiege)
                {
                    _reliefBlockedRequests++;
                }
                if (recipient.FoodStocks <= 0f && recipient.FoodChange < 0f)
                {
                    AddReliefLoyalty(recipient, -0.1f);
                    _currentReliefUnmet++;
                }
            }

            _currentReliefDonors += usedDonors.Count;
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

        private static string ReliefNetworkKey(Town town)
        {
            if (town?.OwnerClan?.Kingdom != null)
                return "kingdom:" + town.OwnerClan.Kingdom.StringId;
            return town?.OwnerClan == null ? string.Empty : "clan:" + town.OwnerClan.StringId;
        }

        private static bool IsReliefDonor(Town town)
        {
            return town != null && !town.IsUnderSiege && FoodRatio(town) > ReliefDonorEntryRatio
                && town.FoodChange >= 0f;
        }

        private static bool IsReliefRecipient(Town town)
        {
            if (town == null) return false;
            float capacity = Math.Max(1f, town.FoodStocksUpperLimit());
            return FoodRatio(town) < ReliefRecipientEntryRatio
                || (town.FoodChange < 0f
                    && town.FoodStocks + town.FoodChange * 7f < capacity * ReliefRecipientEntryRatio);
        }

        private static float FoodRatio(Town town)
        {
            return town == null ? 0f : town.FoodStocks / Math.Max(1f, town.FoodStocksUpperLimit());
        }

        private static float ReliefRouteEfficiency(Town donor, Town recipient)
        {
            float distance = (float)Math.Sqrt(
                donor.Settlement.GetPosition2D.DistanceSquared(recipient.Settlement.GetPosition2D));
            if (distance > ReliefMaximumRouteDistance)
            {
                return 0f;
            }

            float efficiency = distance > 120f ? 0.7f : distance > 60f ? 0.8f : 0.9f;
            float routeSecurity = Math.Min(donor.Security, recipient.Security);
            if (routeSecurity < 25f) efficiency -= 0.1f;
            else if (routeSecurity < 50f) efficiency -= 0.05f;
            return Math.Max(0.7f, Math.Min(0.9f, efficiency));
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement?.Village == null)
            {
                return;
            }

            Village village = settlement.Village;
            if (village.VillageState == Village.VillageStates.Looted)
            {
                _villageRecoverySteps[village.StringId] = 0;
                return;
            }

            if (village.VillageState != Village.VillageStates.Normal)
            {
                return;
            }

            if (!_villageRecoverySteps.TryGetValue(village.StringId, out int step))
            {
                _villageRecoverySteps[village.StringId] = MaximumRecoveryStep;
                return;
            }

            step = Math.Max(0, Math.Min(MaximumRecoveryStep, step));
            if (step < MaximumRecoveryStep && MBRandom.RandomInt(1, 11) >= 8)
            {
                _villageRecoverySteps[village.StringId] = step + 1;
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

            if (newState == Village.VillageStates.Looted
                || (newState == Village.VillageStates.Normal && oldState == Village.VillageStates.Looted))
            {
                _villageRecoverySteps[village.StringId] = 0;
            }
        }

        private void OnTroopRecruited(
            Hero recruiter,
            Settlement settlement,
            Hero recruitmentSource,
            CharacterObject troop,
            int amount)
        {
            // Native prisoner and map/mercenary recruitment have no notable source.
            // Only ordinary volunteers consume local village hearth.
            if (settlement != null && recruitmentSource != null && amount > 0)
            {
                ConsumeRecruitHearth(settlement, amount);
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

        private List<Village> GetEligibleVillages(Settlement settlement)
        {
            if (settlement?.Village != null)
            {
                return settlement.Village.VillageState == Village.VillageStates.Normal
                    && GetRecoveryModifier(settlement.Village) >= MinimumRecruitmentRecoveryModifier
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
                    && GetRecoveryModifier(village) >= MinimumRecruitmentRecoveryModifier)
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
