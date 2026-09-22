using System;
using System.Collections.Generic;

namespace ReignBeta.Court
{
    public sealed class ReignEconomicReportState
    {
        public int SnapshotDay = -1;
        public float SnapshotCreatedDay;
        public List<ReignSettlementSupplySnapshot> Settlements = new List<ReignSettlementSupplySnapshot>();
        public List<ReignEconomicShipment> Shipments = new List<ReignEconomicShipment>();
    }

    public sealed class ReignEconomicShipment
    {
        public string ShipmentKind;
        public string ShipmentId;
        public string SourceSettlementStringId;
        public string TargetSettlementStringId;
        public string SourceSettlementName;
        public string TargetSettlementName;
        public string ItemStringId;
        public string ItemName;
        public int Amount;
        public float RequestedFood;
        public int SpoilagePercent;
        public float RoadSpoilage;
        public float SurvivingFood;
        public float CapacityOverflow;
        public float DeliveredFood;
        public float SourceFoodBefore;
        public float SourceFoodAfterDispatch;
        public float TargetFoodBeforeDelivery;
        public float TargetFoodAfterDelivery;
        public string Notes;
        public float RequestedDay;
        public float ConsentDecisionDay;
        public float ArrivalDay;
        public string Status;
        public string Error;
        public bool VassalConsentGranted;
        public List<string> MentionedHeroIds = new List<string>();
        public string SourceClanStringId;
        public string TargetClanStringId;
        public string SourceLeaderHeroStringId;
        public string TargetLeaderHeroStringId;
        public List<string> SourceClanKnownHeroIds = new List<string>();
        public List<string> TargetClanKnownHeroIds = new List<string>();
        public string SourceLetterStatus;
        public string SourceLetterId;
        public string SourceLetterError;
        public string TargetLetterStatus;
        public string TargetLetterId;
        public string TargetLetterError;

        public bool IsFoodStockTransfer => string.Equals(ShipmentKind, "food_stock_v1", StringComparison.OrdinalIgnoreCase);

        public bool IsTerminal =>
            string.Equals(Status, "Delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "Declined", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase);
    }
}
