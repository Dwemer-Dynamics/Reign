using System;

namespace ReignBeta.Government
{
    /// <summary>Saved in bounded compressed JSON chunks, not a new native save type.</summary>
    public sealed class ReignGovernmentBusinessRecord
    {
        public string BusinessId = "government_business_" + Guid.NewGuid().ToString("N");
        public string KingdomStringId = string.Empty;
        public string Kind = string.Empty;
        public string Title = string.Empty;
        public string Summary = string.Empty;
        public string Status = "hearing";
        public string PetitionerHeroStringId = string.Empty;
        public string SponsorHeroStringId = string.Empty;
        public string SponsorReason = string.Empty;
        public string TargetId = string.Empty;
        public string NativeDecisionType = string.Empty;
        public string NativeFingerprint = string.Empty;
        public int NativeCaptureVersion;
        public string NativeCaptureSource = string.Empty;
        public string NativeCapturedOwnerClanId = string.Empty;
        public string NativeQuarantineReason = string.Empty;
        public bool NativeEffectStarted;
        public int NativeEffectInvocationCount;
        public int NativeOwnerChangeEventCount;
        public System.Collections.Generic.List<int> NativeDecisionAliasIndices = new System.Collections.Generic.List<int>();
        public string ActionCorrelationId = string.Empty;
        public string ActionPayloadJson = string.Empty;
        public string CounterpartOfBusinessId = string.Empty;
        public string CounterpartBusinessId = string.Empty;
        public int ActionKindValue;
        public int NativeDecisionIndex = -1;
        public string OptionsJson = "[]";
        public string RecommendedOptionId = string.Empty;
        public string RecommendationRulerHeroStringId = string.Empty;
        public string DecisionRulerHeroStringId = string.Empty;
        public int AuthorityLevelAtDecision;
        public string WinningOptionId = string.Empty;
        public string ExecutedOptionId = string.Empty;
        public string RequestedOptionId = string.Empty;
        public string StatusQuoOptionId = string.Empty;
        public string VotesJson = "[]";
        public string TranscriptJson = "[]";
        public string ParticipantsJson = "[]";
        public string ConsequenceReceiptsJson = "[]";
        public bool OverrideSettlementConsequenceApplied;
        public string Outcome = string.Empty;
        public float CreatedDay;
        public float RecessUntilDay = -1;
        public float ReconsiderUntilDay = -1;
        public float ResolvedDay = -1;
        public bool IsUrgent;
        public bool Postponed;
        public bool ConsequencesApplied;
        public bool ExecutionApplied;
        public bool RulerOverrode;
        public bool IsRepeal;
        public int SessionProtocolVersion;
        public float PresentationDay = -1;
        public float DecisionDay = -1;
        public int FrozenAuthorityLevel;
        public string FrozenSeatsJson = "[]";
        public string PublicPositionsJson = "[]";
        public string IntroductionPartyId = "";
        public string CrownActorHeroId = "";
        public string CrownOptionId = "";
        public string CrownVoteSource = "";
        public string CrownVoteStatus = "unrecorded";
        public bool DecisionNewsRecorded;
        public bool LegacySessionMigration;
        public long Revision = 1;
    }
}
