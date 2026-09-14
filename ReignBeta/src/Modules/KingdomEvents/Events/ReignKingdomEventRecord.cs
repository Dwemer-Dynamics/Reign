using System;
using System.Collections.Generic;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Events
{
    public sealed class ReignKingdomEventRecord
    {
        [SaveableField(1)] public string EventId;
        [SaveableField(2)] public string ArchetypeId;
        [SaveableField(3)] public string Title;
        [SaveableField(4)] public string Polarity;
        [SaveableField(5)] public string KingdomStringId;
        [SaveableField(6)] public string SecondaryKingdomStringId;
        [SaveableField(7)] public string RulerHeroStringId;
        [SaveableField(8)] public string HeirHeroStringId;
        [SaveableField(9)] public float StartDay;
        [SaveableField(10)] public float EndDay;
        [SaveableField(11)] public string Status;
        [SaveableField(12)] public string Outcome;
        [SaveableField(13)] public float Magnitude;
        [SaveableField(14)] public bool IsTimed;
        [SaveableField(15)] public bool WasForced;
        [SaveableField(16)] public int RollDay;
        [SaveableField(17)] public int TriggerRollBasisPoints;
        [SaveableField(18)] public string Description;

        public ReignKingdomEventRecord()
        {
            EventId = "kingdom_event_" + Guid.NewGuid().ToString("N");
            ArchetypeId = string.Empty;
            Title = string.Empty;
            Polarity = string.Empty;
            KingdomStringId = string.Empty;
            SecondaryKingdomStringId = string.Empty;
            RulerHeroStringId = string.Empty;
            HeirHeroStringId = string.Empty;
            Status = "active";
            Outcome = string.Empty;
            Description = string.Empty;
        }

        public bool IsActiveAt(float day)
        {
            return IsTimed
                && string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase)
                && day >= StartDay
                && day < EndDay;
        }
    }

    public sealed class ReignKingdomEventTestLedger
    {
        [SaveableField(1)] public string RunId;
        [SaveableField(2)] public string CampaignId;
        [SaveableField(3)] public string ArchetypeId;
        [SaveableField(4)] public string TargetVariant;
        [SaveableField(5)] public string EventId;
        [SaveableField(6)] public string KingdomStringId;
        [SaveableField(7)] public string OriginalRulerHeroStringId;
        [SaveableField(8)] public int PreviousLastRolledDay;
        [SaveableField(9)] public float StartedDay;
        [SaveableField(10)] public string BeforeSnapshotJson;
        [SaveableField(11)] public List<string> ObservationJson;
        [SaveableField(12)] public bool DisposableSaveConfirmed;

        public ReignKingdomEventTestLedger()
        {
            RunId = string.Empty;
            CampaignId = string.Empty;
            ArchetypeId = string.Empty;
            TargetVariant = string.Empty;
            EventId = string.Empty;
            KingdomStringId = string.Empty;
            OriginalRulerHeroStringId = string.Empty;
            BeforeSnapshotJson = "{}";
            ObservationJson = new List<string>();
        }
    }
}
