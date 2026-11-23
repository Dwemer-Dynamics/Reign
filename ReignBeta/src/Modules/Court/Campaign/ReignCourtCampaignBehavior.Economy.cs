using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Characters.World;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Court.Integration;
using ReignBeta.Court.UI;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        public ReignEconomicReportState EnsureEconomicReportState()
        {
            _economicReportState = _economicReportState ?? new ReignEconomicReportState();
            _economicReportState.Settlements = _economicReportState.Settlements ?? new List<ReignSettlementSupplySnapshot>();
            _economicReportState.Shipments = (_economicReportState.Shipments ?? new List<ReignEconomicShipment>())
                .Where(x => x != null).ToList();
            int day = CurrentDay();
            if (_economicReportState.Settlements.Count == 0 || (CurrentHour() >= 8 && _economicReportState.SnapshotDay < day))
            {
                _economicReportState.SnapshotDay = day;
                _economicReportState.SnapshotCreatedDay = CurrentDayFloat();
                _economicReportState.Settlements = ReignCourtSupplyService
                    .GetScopedSnapshots(_session?.Authority ?? ReignCourtAuthority.Royal)
                    .OrderByDescending(x => string.Equals(x.OwnerClanStringId, Clan.PlayerClan?.StringId, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.Name)
                    .ToList();
            }
            return _economicReportState;
        }

        public ReignEconomicShipment SubmitEconomicFoodTransfer(string sourceId, string targetId, int amount, string notes)
        {
            EnsureEconomicReportState();
            Settlement source = FindEconomicSettlement(sourceId);
            Settlement target = FindEconomicSettlement(targetId);
            if (source == null || target == null || source == target || source.Town == null || target.Town == null)
                throw new InvalidOperationException("Choose two different current towns or castles.");
            if (source.Town.IsUnderSiege || target.Town.IsUnderSiege)
                throw new InvalidOperationException("Food shipments cannot be dispatched through a siege.");
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null || source.OwnerClan?.Kingdom != playerKingdom)
                throw new InvalidOperationException("The origin must belong to the player's kingdom.");
            if (amount <= 0) throw new InvalidOperationException("Choose a positive food amount.");
            if (source.Town.FoodStocks + 0.001f < amount)
                throw new InvalidOperationException("The origin does not have that much food supply.");

            float now = CurrentDayFloat();
            float distance = (float)Math.Sqrt(source.GetPosition2D.DistanceSquared(target.GetPosition2D));
            float travelDays = Math.Max(0.175f, distance / 50f);
            int spoilagePercent = MBRandom.RandomInt(5, 26);
            float roadSpoilage = amount * spoilagePercent / 100f;
            Clan sourceClan = source.OwnerClan;
            Clan targetClan = target.OwnerClan;
            ReignEconomicShipment shipment = new ReignEconomicShipment
            {
                ShipmentKind = "food_stock_v1",
                ShipmentId = "economic_food_" + Guid.NewGuid().ToString("N"),
                SourceSettlementStringId = source.StringId,
                TargetSettlementStringId = target.StringId,
                SourceSettlementName = source.Name?.ToString() ?? source.StringId,
                TargetSettlementName = target.Name?.ToString() ?? target.StringId,
                ItemName = "Food supply",
                Amount = amount,
                RequestedFood = amount,
                SpoilagePercent = spoilagePercent,
                RoadSpoilage = roadSpoilage,
                SurvivingFood = Math.Max(0f, amount - roadSpoilage),
                SourceFoodBefore = source.Town.FoodStocks,
                Notes = (notes ?? string.Empty).Trim(),
                RequestedDay = now,
                ArrivalDay = now + travelDays,
                Status = "In transit",
                SourceClanStringId = sourceClan?.StringId ?? string.Empty,
                TargetClanStringId = targetClan?.StringId ?? string.Empty,
                SourceLeaderHeroStringId = sourceClan?.Leader?.StringId ?? string.Empty,
                TargetLeaderHeroStringId = targetClan?.Leader?.StringId ?? string.Empty,
                SourceClanKnownHeroIds = LivingClanHeroIds(sourceClan),
                TargetClanKnownHeroIds = LivingClanHeroIds(targetClan),
                SourceLetterStatus = RequiresShipmentLetter(sourceClan?.Leader, sourceClan) ? "Pending" : "NotRequired",
                TargetLetterStatus = RequiresShipmentLetter(targetClan?.Leader, targetClan) ? "Pending" : "NotRequired"
            };
            source.Town.FoodStocks = Math.Max(0f, source.Town.FoodStocks - amount);
            shipment.SourceFoodAfterDispatch = source.Town.FoodStocks;
            _economicReportState.Shipments.Add(shipment);
            RefreshEconomicSnapshots();
            RecordFoodShipmentKnowledge(shipment, "dispatched", "source", source, target);
            RecordFoodShipmentKnowledge(shipment, "dispatched", "destination", source, target);
            QueueEconomicShipmentLetters(shipment);
            StateChanged?.Invoke();
            return shipment;
        }

        internal void ProcessEconomyShipmentArrivals() => ProcessEconomicShipments();

        private void ProcessEconomicShipments()
        {
            List<ReignEconomicShipment> shipments = (_economicReportState?.Shipments ?? new List<ReignEconomicShipment>())
                .Where(x => x != null).ToList();
            foreach (ReignEconomicShipment shipment in shipments.Where(x => x.IsFoodStockTransfer))
                QueueEconomicShipmentLetters(shipment);

            float now = CurrentDayFloat();
            bool changed = false;
            foreach (ReignEconomicShipment shipment in shipments.Where(x => !x.IsTerminal).ToList())
            {
                if (!shipment.IsFoodStockTransfer)
                {
                    changed |= ProcessLegacyEconomicShipment(shipment, now);
                    continue;
                }
                if ((!string.Equals(shipment.Status, "In transit", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(shipment.Status, "Blocked by siege", StringComparison.OrdinalIgnoreCase)) || now < shipment.ArrivalDay)
                    continue;
                Settlement source = FindEconomicSettlement(shipment.SourceSettlementStringId);
                Settlement target = FindEconomicSettlement(shipment.TargetSettlementStringId);
                if (target?.Town == null)
                {
                    shipment.Status = "Failed";
                    shipment.Error = "The destination no longer exists; the dispatched food was lost.";
                    RecordFoodShipmentKnowledge(shipment, "failed", "source", source, target);
                    RecordFoodShipmentKnowledge(shipment, "failed", "destination", source, target);
                    changed = true;
                    continue;
                }
                if (shipment.BlockedSpoilageLastDay > 0)
                {
                    float before = shipment.SurvivingFood;
                    shipment.SurvivingFood = (float)ReignBeta.WorldSimulation.Economy.ReignWarEconomyPolicy.BlockedCargo(before, now - shipment.BlockedSpoilageLastDay);
                    shipment.SiegeSpoilage += before - shipment.SurvivingFood;
                    shipment.BlockedSpoilageLastDay = now;
                    changed = true;
                }
                if (target.Town.IsUnderSiege)
                {
                    shipment.Status = "Blocked by siege";
                    if (shipment.BlockedSpoilageLastDay <= 0) shipment.BlockedSpoilageLastDay = now;
                    shipment.Error = "Cargo is waiting outside the siege and loses 2% of remaining food each blocked day.";
                    changed = true;
                    continue;
                }
                shipment.TargetFoodBeforeDelivery = target.Town.FoodStocks;
                float freeCapacity = Math.Max(0f, target.Town.FoodStocksUpperLimit() - target.Town.FoodStocks);
                shipment.DeliveredFood = Math.Min(Math.Max(0f, shipment.SurvivingFood), freeCapacity);
                shipment.CapacityOverflow = Math.Max(0f, shipment.SurvivingFood - shipment.DeliveredFood);
                target.Town.FoodStocks += shipment.DeliveredFood;
                shipment.TargetFoodAfterDelivery = target.Town.FoodStocks;
                shipment.Status = "Delivered";
                shipment.Error = string.Empty;
                RecordFoodShipmentKnowledge(shipment, "delivered", "source", source, target);
                RecordFoodShipmentKnowledge(shipment, "delivered", "destination", source, target);
                changed = true;
            }
            if (changed)
            {
                RefreshEconomicSnapshots();
                StateChanged?.Invoke();
            }
        }

        private bool ProcessLegacyEconomicShipment(ReignEconomicShipment shipment, float now)
        {
            Settlement source = FindEconomicSettlement(shipment.SourceSettlementStringId);
            Settlement target = FindEconomicSettlement(shipment.TargetSettlementStringId);
            if (source == null || target == null)
            {
                shipment.Status = "Failed";
                shipment.Error = "A settlement no longer exists.";
                return true;
            }
            if (string.Equals(shipment.Status, "Awaiting consent", StringComparison.OrdinalIgnoreCase) && now >= shipment.ConsentDecisionDay)
            {
                ItemObject consentItem = TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>(shipment.ItemStringId);
                Hero lord = source.OwnerClan?.Leader;
                int relation = lord?.GetRelation(Hero.MainHero) ?? 0;
                int available = consentItem == null ? 0 : source.ItemRoster.GetItemNumber(consentItem);
                bool accepted = relation + 55 >= Math.Min(90, shipment.Amount * 2) && available >= shipment.Amount;
                shipment.VassalConsentGranted = accepted;
                shipment.Status = accepted ? "In transit" : "Declined";
                if (!accepted) shipment.Error = (lord?.Name?.ToString() ?? "The source clan") + " declined the export request.";
            }
            if (!string.Equals(shipment.Status, "In transit", StringComparison.OrdinalIgnoreCase) || now < shipment.ArrivalDay) return false;
            ReignSupplyTransferResult result = ReignCourtSupplyService.ApplyTransfer(new ReignSupplyTransferRequest
            {
                CommandId = shipment.ShipmentId,
                SourceSettlementStringId = shipment.SourceSettlementStringId,
                TargetSettlementStringId = shipment.TargetSettlementStringId,
                ItemStringId = shipment.ItemStringId,
                ItemAmount = shipment.Amount,
                VassalConsentGranted = shipment.VassalConsentGranted
            }, _session?.Authority ?? ReignCourtAuthority.Royal);
            shipment.Status = result.Success ? "Delivered" : "Failed";
            shipment.Error = result.Error ?? string.Empty;
            RecordLegacyEconomicHistory(shipment, result.Success ? "delivered" : "failed", source, target);
            return true;
        }

        private void RefreshEconomicSnapshots()
        {
            _economicReportState.Settlements = ReignCourtSupplyService
                .GetScopedSnapshots(_session?.Authority ?? ReignCourtAuthority.Royal)
                .OrderByDescending(x => x.OwnerClanStringId == Clan.PlayerClan?.StringId)
                .ThenBy(x => x.Name).ToList();
        }

        private static Settlement FindEconomicSettlement(string id) => Settlement.All.FirstOrDefault(x =>
            string.Equals(x?.StringId, id, StringComparison.OrdinalIgnoreCase) && x.IsFortification);

        private static List<string> LivingClanHeroIds(Clan clan) => clan == null
            ? new List<string>()
            : clan.Heroes.Where(x => x?.IsAlive == true && !string.IsNullOrWhiteSpace(x.StringId))
                .Select(x => x.StringId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static bool RequiresShipmentLetter(Hero leader, Clan clan) => leader != null && leader.IsAlive
            && leader != Hero.MainHero && clan != Clan.PlayerClan;

        private void QueueEconomicShipmentLetters(ReignEconomicShipment shipment)
        {
            if (shipment == null || !shipment.IsFoodStockTransfer || !_serverAvailable) return;
            QueueEconomicShipmentLetter(shipment, "source");
            QueueEconomicShipmentLetter(shipment, "destination");
        }

        private void QueueEconomicShipmentLetter(ReignEconomicShipment shipment, string role)
        {
            bool sourceSide = string.Equals(role, "source", StringComparison.OrdinalIgnoreCase);
            string status = sourceSide ? shipment.SourceLetterStatus : shipment.TargetLetterStatus;
            if (string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "NotRequired", StringComparison.OrdinalIgnoreCase)) return;
            string requestKey = shipment.ShipmentId + ":" + role;
            lock (_economicLetterRequestsInFlight)
            {
                if (!_economicLetterRequestsInFlight.Add(requestKey)) return;
            }
            _ = SendEconomicShipmentLetterAsync(shipment, role, requestKey);
        }

        private async Task SendEconomicShipmentLetterAsync(ReignEconomicShipment shipment, string role, string requestKey)
        {
            bool sourceSide = string.Equals(role, "source", StringComparison.OrdinalIgnoreCase);
            try
            {
                string heroId = sourceSide ? shipment.SourceLeaderHeroStringId : shipment.TargetLeaderHeroStringId;
                Hero recipient = Hero.AllAliveHeroes.FirstOrDefault(x => string.Equals(x.StringId, heroId, StringComparison.OrdinalIgnoreCase));
                if (recipient == null || recipient == Hero.MainHero)
                {
                    await ReignMainThread.InvokeAsync(() => SetEconomicLetterResult(shipment, role, "NotRequired", string.Empty, string.Empty)).ConfigureAwait(false);
                    return;
                }
                float transit = Math.Max(0.175f, shipment.ArrivalDay - shipment.RequestedDay);
                string body = sourceSide
                    ? "By order of the Crown, " + shipment.RequestedFood.ToString("0.#") + " food supply departed "
                        + shipment.SourceSettlementName + " for " + shipment.TargetSettlementName + ". Estimated travel time: "
                        + transit.ToString("0.0") + " days."
                    : "By order of the Crown, " + shipment.RequestedFood.ToString("0.#") + " food supply has been dispatched from "
                        + shipment.SourceSettlementName + " to " + shipment.TargetSettlementName + ". Estimated travel time: "
                        + transit.ToString("0.0") + " days."
                        + (string.IsNullOrWhiteSpace(shipment.Notes) ? string.Empty : "\n\nA note from the ruler:\n" + shipment.Notes);
                ReignLetterSendResult result = await ReignCorrespondenceClient.SendEconomicShipmentLetterAsync(
                    recipient, body, shipment.ShipmentId, role, shipment.SourceSettlementStringId,
                    shipment.TargetSettlementStringId, shipment.RequestedDay + 0.25f).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => SetEconomicLetterResult(shipment, role,
                    result.Ok ? "Sent" : "Error", result.LetterId, result.Error)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => SetEconomicLetterResult(shipment, role, "Error", string.Empty, ex.Message)).ConfigureAwait(false);
            }
            finally
            {
                lock (_economicLetterRequestsInFlight) _economicLetterRequestsInFlight.Remove(requestKey);
            }
        }

        private void SetEconomicLetterResult(ReignEconomicShipment shipment, string role, string status, string letterId, string error)
        {
            if (shipment == null) return;
            if (string.Equals(role, "source", StringComparison.OrdinalIgnoreCase))
            {
                shipment.SourceLetterStatus = status;
                shipment.SourceLetterId = letterId ?? string.Empty;
                shipment.SourceLetterError = error ?? string.Empty;
            }
            else
            {
                shipment.TargetLetterStatus = status;
                shipment.TargetLetterId = letterId ?? string.Empty;
                shipment.TargetLetterError = error ?? string.Empty;
            }
            StateChanged?.Invoke();
        }

        private static void RecordFoodShipmentKnowledge(ReignEconomicShipment shipment, string phase, string side,
            Settlement source, Settlement target)
        {
            bool sourceSide = string.Equals(side, "source", StringComparison.OrdinalIgnoreCase);
            IEnumerable<string> participantIds = sourceSide ? shipment.SourceClanKnownHeroIds : shipment.TargetClanKnownHeroIds;
            JArray participants = new JArray((participantIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
            string sourceName = source?.Name?.ToString() ?? shipment.SourceSettlementName ?? shipment.SourceSettlementStringId;
            string targetName = target?.Name?.ToString() ?? shipment.TargetSettlementName ?? shipment.TargetSettlementStringId;
            string summary = phase == "dispatched"
                ? shipment.RequestedFood.ToString("0.#") + " food supply was dispatched from " + sourceName + " to " + targetName + "."
                : phase == "delivered"
                    ? shipment.DeliveredFood.ToString("0.#") + " food supply reached " + targetName + " from " + sourceName
                        + "; " + shipment.RoadSpoilage.ToString("0.#") + " spoiled on the road and "
                        + shipment.CapacityOverflow.ToString("0.#") + " exceeded storage capacity."
                    : "The food shipment from " + sourceName + " to " + targetName + " was lost.";
            if (!sourceSide && !string.IsNullOrWhiteSpace(shipment.Notes)) summary += " Ruler's note: " + shipment.Notes;
            ReignServices.WorldHistory?.RecordReignSystemEvent(
                "court_food_shipment", phase + "_" + side, "economic_transfer", shipment.ShipmentId + ":" + side,
                summary, "private", Hero.MainHero?.StringId ?? string.Empty,
                (sourceSide ? source?.OwnerClan?.Kingdom : target?.OwnerClan?.Kingdom)?.StringId ?? string.Empty,
                new JObject
                {
                    ["knowledgeSide"] = side,
                    ["sourceSettlementId"] = shipment.SourceSettlementStringId ?? string.Empty,
                    ["sourceSettlement"] = sourceName,
                    ["destinationSettlementId"] = shipment.TargetSettlementStringId ?? string.Empty,
                    ["destinationSettlement"] = targetName,
                    ["sourceClanId"] = shipment.SourceClanStringId ?? string.Empty,
                    ["destinationClanId"] = shipment.TargetClanStringId ?? string.Empty,
                    ["knownByHeroIds"] = participants.DeepClone(),
                    ["requestedFood"] = shipment.RequestedFood,
                    ["spoilagePercent"] = shipment.SpoilagePercent,
                    ["roadSpoilage"] = shipment.RoadSpoilage,
                    ["capacityOverflow"] = shipment.CapacityOverflow,
                    ["deliveredFood"] = shipment.DeliveredFood,
                    ["notes"] = sourceSide ? string.Empty : shipment.Notes ?? string.Empty,
                    ["requestedDay"] = shipment.RequestedDay,
                    ["arrivalDay"] = shipment.ArrivalDay
                }, participants);
        }

        private static void RecordLegacyEconomicHistory(ReignEconomicShipment shipment, string phase, Settlement source, Settlement target)
        {
            ReignServices.WorldHistory?.RecordReignSystemEvent(
                "court_economic_transfer", phase, "economic_transfer", shipment.ShipmentId,
                shipment.Amount + " " + shipment.ItemName + " transferred from " + source.Name + " to " + target.Name + ".",
                "private", Hero.MainHero?.StringId ?? string.Empty, target.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                new JObject { ["legacyItemTransfer"] = true, ["itemId"] = shipment.ItemStringId ?? string.Empty,
                    ["amount"] = shipment.Amount, ["notes"] = shipment.Notes ?? string.Empty },
                new JArray(shipment.MentionedHeroIds ?? new List<string>()));
        }

        public static bool IsPlayerKingdomRuler()
        {
            Hero player = Hero.MainHero;
            Clan playerClan = Clan.PlayerClan;
            Kingdom kingdom = playerClan?.Kingdom;
            return Hero.MainHero != null
                && kingdom != null
                && !kingdom.IsEliminated
                // RulingClan and Kingdom.Leader are the native kingdom-authority
                // signals. Clan.Leader can lag or be replaced independently in
                // modded saves, so requiring it ejects a legitimate player ruler
                // after hourly/fast-forward ticks.
                && (kingdom.RulingClan == playerClan
                    || kingdom.RulingClan?.Leader == player
                    || kingdom.Leader == player);
        }
    }
}
