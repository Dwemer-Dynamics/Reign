using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    public sealed class ReignTreatyObligationRecord
    {
        [SaveableField(1)] public string ObligationId;
        [SaveableField(2)] public string AgreementId;
        [SaveableField(3)] public string TriggeringWarId;
        [SaveableField(4)] public string ResultingWarId;
        [SaveableField(5)] public string AllyKingdomStringId;
        [SaveableField(6)] public string AllyRulerHeroStringId;
        [SaveableField(7)] public string DefendedKingdomStringId;
        [SaveableField(8)] public string DefendedRulerHeroStringId;
        [SaveableField(9)] public string AggressorKingdomStringId;
        [SaveableField(10)] public float TriggeredDay;
        [SaveableField(11)] public string Status;
        [SaveableField(12)] public string Reason;

        public ReignTreatyObligationRecord()
        {
            ObligationId = Guid.NewGuid().ToString("N");
            AgreementId = string.Empty;
            TriggeringWarId = string.Empty;
            ResultingWarId = string.Empty;
            AllyKingdomStringId = string.Empty;
            AllyRulerHeroStringId = string.Empty;
            DefendedKingdomStringId = string.Empty;
            DefendedRulerHeroStringId = string.Empty;
            AggressorKingdomStringId = string.Empty;
            Status = "pending";
            Reason = string.Empty;
        }
    }
}
