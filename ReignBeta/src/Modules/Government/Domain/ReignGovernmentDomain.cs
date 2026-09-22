using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Government
{
    public sealed class ReignGovernmentStateRecord
    {
        [SaveableField(1)] public string GovernmentId;
        [SaveableField(2)] public string KingdomStringId;
        [SaveableField(3)] public string CultureStringId;
        [SaveableField(4)] public string InstitutionName;
        [SaveableField(5)] public int InstitutionKindValue;
        [SaveableField(6)] public int Level;
        [SaveableField(7)] public int Trust;
        [SaveableField(8)] public float InitializedDay;
        [SaveableField(9)] public float LastMeetingDay;
        [SaveableField(10)] public float NextMeetingDay;
        [SaveableField(11)] public float TemporaryBenefitEndDay;
        [SaveableField(12)] public float TemporaryLoyaltyPerDay;
        [SaveableField(13)] public float TemporaryProsperityPerDay;
        [SaveableField(14)] public float TemporaryHearthPerDay;
        [SaveableField(15)] public string DominantPartyId;
        [SaveableField(16)] public string RulerHeroStringId;
        [SaveableField(17)] public int StanceTowardRuler;
        [SaveableField(18)] public long Revision;
        [SaveableField(19)] public string LastMeetingSummary;
        [SaveableField(20)] public bool InitialBenefitsSuppressed;
        [SaveableField(21)] public string CoalitionPartyIdsCsv;
        [SaveableField(22)] public int ReductionConsentFromLevel;
        [SaveableField(23)] public string ReductionConsentHeroIdsCsv;
        [SaveableField(24)] public string ReductionConsentClanIdsCsv;

        public ReignGovernmentStateRecord()
        {
            GovernmentId = "government_" + Guid.NewGuid().ToString("N");
            KingdomStringId = string.Empty;
            CultureStringId = string.Empty;
            InstitutionName = string.Empty;
            DominantPartyId = string.Empty;
            RulerHeroStringId = string.Empty;
            LastMeetingSummary = string.Empty;
            CoalitionPartyIdsCsv = string.Empty;
            ReductionConsentHeroIdsCsv = string.Empty;
            ReductionConsentClanIdsCsv = string.Empty;
            Level = 1;
            Trust = 50;
            LastMeetingDay = -1f;
            NextMeetingDay = -1f;
            TemporaryBenefitEndDay = -1f;
            InitialBenefitsSuppressed = true;
        }
    }

    public sealed class ReignGovernmentPartyRecord
    {
        [SaveableField(1)] public string KingdomStringId;
        [SaveableField(2)] public string PartyId;
        [SaveableField(3)] public string Name;
        [SaveableField(4)] public string PlanksCsv;
        [SaveableField(5)] public string SpeakerHeroStringId;
        [SaveableField(6)] public int SeatCount;
        [SaveableField(7)] public int PartyLoyalty;
        [SaveableField(8)] public bool IsDominant;
        [SaveableField(9)] public string LastStatement;
        [SaveableField(10)] public float LastStatementDay;
        [SaveableField(11)] public long Revision;
        [SaveableField(12)] public bool IsGoverningCoalition;

        public ReignGovernmentPartyRecord()
        {
            KingdomStringId = string.Empty;
            PartyId = string.Empty;
            Name = string.Empty;
            PlanksCsv = string.Empty;
            SpeakerHeroStringId = string.Empty;
            PartyLoyalty = 50;
            LastStatement = string.Empty;
            LastStatementDay = -1f;
        }
    }

    public sealed class ReignGovernmentSeatRecord
    {
        [SaveableField(1)] public string KingdomStringId;
        [SaveableField(2)] public string HeroStringId;
        [SaveableField(3)] public string SettlementStringId;
        [SaveableField(4)] public string ClanStringId;
        [SaveableField(5)] public int SeatSourceValue;
        [SaveableField(6)] public string PartyId;
        [SaveableField(7)] public int PartyLoyalty;
        [SaveableField(8)] public int GovernmentLoyalty;
        [SaveableField(9)] public int LastReductionVoteScore;
        [SaveableField(10)] public bool LastReductionVoteSupported;
        [SaveableField(11)] public float LastVoteDay;
        [SaveableField(12)] public long Revision;

        public ReignGovernmentSeatRecord()
        {
            KingdomStringId = string.Empty;
            HeroStringId = string.Empty;
            SettlementStringId = string.Empty;
            ClanStringId = string.Empty;
            PartyId = string.Empty;
            PartyLoyalty = 50;
            GovernmentLoyalty = 50;
            LastVoteDay = -1f;
        }
    }

    public sealed class ReignGovernmentResolutionRecord
    {
        [SaveableField(1)] public string ResolutionId;
        [SaveableField(2)] public string KingdomStringId;
        [SaveableField(3)] public string TemplateId;
        [SaveableField(4)] public string PartyId;
        [SaveableField(5)] public string SpeakerHeroStringId;
        [SaveableField(6)] public string TargetSettlementStringId;
        [SaveableField(7)] public string TargetKingdomStringId;
        [SaveableField(8)] public string TargetClanStringId;
        [SaveableField(9)] public string TargetHeroStringId;
        [SaveableField(10)] public int SelectedRoute;
        [SaveableField(11)] public int RouteActionValue;
        [SaveableField(12)] public int RequiredAmount;
        [SaveableField(13)] public float BaselineValue;
        [SaveableField(14)] public float CurrentValue;
        [SaveableField(15)] public float ProposedDay;
        [SaveableField(16)] public float AcceptedDay;
        [SaveableField(17)] public float DueDay;
        [SaveableField(18)] public float ResolvedDay;
        [SaveableField(19)] public string Status;
        [SaveableField(20)] public string Outcome;
        [SaveableField(21)] public string EvidenceJson;
        [SaveableField(22)] public int ConsequenceLevel;
        [SaveableField(23)] public long Revision;
        [SaveableField(24)] public string TargetPolicyStringId;

        public ReignGovernmentResolutionRecord()
        {
            ResolutionId = "government_resolution_" + Guid.NewGuid().ToString("N");
            KingdomStringId = string.Empty;
            TemplateId = string.Empty;
            PartyId = string.Empty;
            SpeakerHeroStringId = string.Empty;
            TargetSettlementStringId = string.Empty;
            TargetKingdomStringId = string.Empty;
            TargetClanStringId = string.Empty;
            TargetHeroStringId = string.Empty;
            TargetPolicyStringId = string.Empty;
            SelectedRoute = -1;
            AcceptedDay = -1f;
            DueDay = -1f;
            ResolvedDay = -1f;
            Status = "proposed";
            Outcome = string.Empty;
            EvidenceJson = "{}";
        }
    }

    public sealed class ReignGovernmentPressureRecord
    {
        [SaveableField(1)] public string PressureId;
        [SaveableField(2)] public string KingdomStringId;
        [SaveableField(3)] public string ActionCorrelationId;
        [SaveableField(4)] public int ActionKindValue;
        [SaveableField(5)] public string TargetId;
        [SaveableField(6)] public float RequestedDay;
        [SaveableField(7)] public float ReconsiderUntilDay;
        [SaveableField(8)] public string Status;
        [SaveableField(9)] public bool GovernmentApproved;
        [SaveableField(10)] public bool RulerOverrode;
        [SaveableField(11)] public int SupportPercent;
        [SaveableField(12)] public string Reason;
        [SaveableField(13)] public long Revision;

        public ReignGovernmentPressureRecord()
        {
            PressureId = "government_pressure_" + Guid.NewGuid().ToString("N");
            KingdomStringId = string.Empty;
            ActionCorrelationId = string.Empty;
            TargetId = string.Empty;
            RequestedDay = -1f;
            ReconsiderUntilDay = -1f;
            Status = "pending";
            Reason = string.Empty;
        }
    }

    public sealed class ReignGovernmentLobbyRecord
    {
        [SaveableField(1)] public string LobbyId;
        [SaveableField(2)] public string KingdomStringId;
        [SaveableField(3)] public string ResolutionId;
        [SaveableField(4)] public string MemberHeroStringId;
        [SaveableField(5)] public string Method;
        [SaveableField(6)] public string Terms;
        [SaveableField(7)] public int GoldPaid;
        [SaveableField(8)] public int PositionShift;
        [SaveableField(9)] public bool Succeeded;
        [SaveableField(10)] public float CreatedDay;
        [SaveableField(11)] public float ExpiresDay;
        [SaveableField(12)] public bool Fulfilled;
        [SaveableField(13)] public long Revision;

        public ReignGovernmentLobbyRecord()
        {
            LobbyId = "government_lobby_" + Guid.NewGuid().ToString("N");
            KingdomStringId = string.Empty;
            ResolutionId = string.Empty;
            MemberHeroStringId = string.Empty;
            Method = string.Empty;
            Terms = string.Empty;
            CreatedDay = -1f;
            ExpiresDay = -1f;
        }
    }

    public sealed class ReignGovernmentMeetingRecord
    {
        [SaveableField(1)] public string MeetingId;
        [SaveableField(2)] public string KingdomStringId;
        [SaveableField(3)] public float MeetingDay;
        [SaveableField(4)] public string PartyIdsCsv;
        [SaveableField(5)] public string SpeakerHeroIdsCsv;
        [SaveableField(6)] public string ResolutionIdsCsv;
        [SaveableField(7)] public string SpeakerStatementsJson;
        [SaveableField(8)] public int ProviderCallCount;
        [SaveableField(9)] public bool PlayerAttended;
        [SaveableField(10)] public string Summary;
        [SaveableField(11)] public long Revision;

        public ReignGovernmentMeetingRecord()
        {
            MeetingId = "government_meeting_" + Guid.NewGuid().ToString("N");
            KingdomStringId = string.Empty;
            PartyIdsCsv = string.Empty;
            SpeakerHeroIdsCsv = string.Empty;
            ResolutionIdsCsv = string.Empty;
            SpeakerStatementsJson = "{}";
            Summary = string.Empty;
        }
    }
}
