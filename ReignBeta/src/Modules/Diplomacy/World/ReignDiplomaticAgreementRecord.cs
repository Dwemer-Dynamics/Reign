using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    public sealed class ReignDiplomaticAgreementRecord
    {
        [SaveableField(1)]
        public string AgreementId;

        [SaveableField(2)]
        public string Kind;

        [SaveableField(3)]
        public string ActorKingdomStringId;

        [SaveableField(4)]
        public string TargetKingdomStringId;

        [SaveableField(5)]
        public string ActorClanStringId;

        [SaveableField(6)]
        public string TargetClanStringId;

        [SaveableField(7)]
        public string TargetHeroStringId;

        [SaveableField(8)]
        public string TargetSettlementStringId;

        [SaveableField(9)]
        public string Reason;

        [SaveableField(10)]
        public string TermsJson;

        [SaveableField(11)]
        public float CreatedDay;

        [SaveableField(12)]
        public float ExpireDay;

        [SaveableField(13)]
        public bool IsPublic;

        [SaveableField(14)]
        public bool IsActive;

        [SaveableField(15)]
        public float EndedDay;

        [SaveableField(16)]
        public string EndReason;

        [SaveableField(17)]
        public string BreakerKingdomStringId;

        [SaveableField(18)]
        public string TriggeringWarId;

        public ReignDiplomaticAgreementRecord()
        {
            AgreementId = Guid.NewGuid().ToString("N");
            Kind = string.Empty;
            ActorKingdomStringId = string.Empty;
            TargetKingdomStringId = string.Empty;
            ActorClanStringId = string.Empty;
            TargetClanStringId = string.Empty;
            TargetHeroStringId = string.Empty;
            TargetSettlementStringId = string.Empty;
            Reason = string.Empty;
            TermsJson = string.Empty;
            EndReason = string.Empty;
            BreakerKingdomStringId = string.Empty;
            TriggeringWarId = string.Empty;
            IsPublic = true;
            IsActive = true;
        }

        public bool IsExpired(float currentDay)
        {
            return ExpireDay > 0f && currentDay > ExpireDay;
        }
    }
}
