using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    public sealed class ReignNegotiatedActionRecord
    {
        [SaveableField(1)] public string NegotiationId;
        [SaveableField(2)] public string BrokerHeroStringId;
        [SaveableField(3)] public string FirstKingdomStringId;
        [SaveableField(4)] public string SecondKingdomStringId;
        [SaveableField(5)] public string FirstRulerHeroStringId;
        [SaveableField(6)] public string SecondRulerHeroStringId;
        [SaveableField(7)] public string Command;
        [SaveableField(8)] public string PoliticalResult;
        [SaveableField(9)] public string TermsJson;
        [SaveableField(10)] public string TermsHash;
        [SaveableField(11)] public string Status;
        [SaveableField(12)] public float CreatedDay;
        [SaveableField(13)] public float UpdatedDay;
        [SaveableField(14)] public float ExpiresDay;
        [SaveableField(15)] public string FirstApprovalHash;
        [SaveableField(16)] public string SecondApprovalHash;
        [SaveableField(17)] public string QueuedActionId;
        [SaveableField(18)] public string ExecutionPhase;
        [SaveableField(19)] public string ExecutionSnapshotJson;
        [SaveableField(20)] public string FailureReason;

        public ReignNegotiatedActionRecord()
        {
            NegotiationId = "neg_" + Guid.NewGuid().ToString("N");
            BrokerHeroStringId = string.Empty;
            FirstKingdomStringId = string.Empty;
            SecondKingdomStringId = string.Empty;
            FirstRulerHeroStringId = string.Empty;
            SecondRulerHeroStringId = string.Empty;
            Command = "diplomatic_package";
            PoliticalResult = string.Empty;
            TermsJson = "{}";
            TermsHash = string.Empty;
            Status = "draft";
            FirstApprovalHash = string.Empty;
            SecondApprovalHash = string.Empty;
            QueuedActionId = string.Empty;
            ExecutionPhase = "idle";
            ExecutionSnapshotJson = "{}";
            FailureReason = string.Empty;
        }
    }
}
