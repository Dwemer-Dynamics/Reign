using System;
using System.Collections.Generic;
using ReignBeta.Save;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Court
{
    public enum ReignCourtAuthority
    {
        Household = 0,
        Clan = 1,
        Royal = 2
    }

    public enum ReignCourtScope
    {
        Local = 0,
        Capital = 1
    }

    public enum ReignCourtSessionState
    {
        Inactive = 0,
        Sitting = 1,
        PausedForMatter = 2,
        InConversation = 3,
        Resolving = 4,
        Interrupted = 5,
        Closed = 6
    }

    public enum ReignCourtMatterState
    {
        Queued = 0,
        Scheduled = 1,
        Active = 2,
        AwaitingDecision = 3,
        Resolving = 4,
        Resolved = 5,
        Refused = 6,
        Expired = 7,
        Invalidated = 8
    }

    public enum ReignCourtMatterKind
    {
        SettlementCrisis = 0,
        Petition = 1,
        MerchantRequest = 2,
        FamilyConflict = 3,
        RelationshipMatter = 4,
        MarriageProposal = 5,
        PrisonerPetition = 6,
        Scandal = 7,
        Obligation = 8,
        Rumor = 9,
        DiplomacyProposal = 10,
        WarCouncil = 11,
        RebellionUltimatum = 12,
        CulturalDispute = 13,
        Performer = 14,
        Artisan = 15,
        PrivateCounsel = 16,
        OfficeBusiness = 17
    }

    public enum ReignCourtOffice
    {
        Steward = 0,
        EconomicAdvisor = 1,
        ForeignAdvisor = 2,
        Marshal = 3,
        Spymaster = 4
    }

    public enum ReignIntelligenceOperationState
    {
        Planned = 0,
        Active = 1,
        Completed = 2,
        Cancelled = 3,
        Failed = 4,
        Exposed = 5
    }

    public sealed class CourtSession
    {
        [SaveableField(1)] public string SessionId;
        [SaveableField(2)] public int StateValue;
        [SaveableField(3)] public int AuthorityValue;
        [SaveableField(4)] public string HostSettlementStringId;
        [SaveableField(5)] public string CampaignId;
        [SaveableField(6)] public string TimelineId;
        [SaveableField(7)] public float OpenedDay;
        [SaveableField(8)] public float ClosedDay;
        [SaveableField(9)] public int LastAgendaDay;
        [SaveableField(10)] public long Revision;
        [SaveableField(11)] public string ActiveMatterId;
        [SaveableField(12)] public int TimeMode;
        [SaveableField(13)] public string InterruptionReason;
        [SaveableField(14)] public int LastPlayerSittingDay;
        [SaveableField(15)] public int ScopeValue;

        public CourtSession()
        {
            SessionId = "court_session_" + Guid.NewGuid().ToString("N");
            StateValue = (int)ReignCourtSessionState.Inactive;
            AuthorityValue = (int)ReignCourtAuthority.Royal;
            HostSettlementStringId = string.Empty;
            CampaignId = "default";
            TimelineId = "main";
            ClosedDay = -1f;
            LastAgendaDay = -1;
            LastPlayerSittingDay = -1;
            ScopeValue = (int)ReignCourtScope.Local;
            ActiveMatterId = string.Empty;
            InterruptionReason = string.Empty;
        }

        public ReignCourtSessionState State { get { return (ReignCourtSessionState)StateValue; } set { StateValue = (int)value; } }
        public ReignCourtAuthority Authority { get { return (ReignCourtAuthority)AuthorityValue; } set { AuthorityValue = (int)value; } }
        public ReignCourtScope Scope { get { return (ReignCourtScope)ScopeValue; } set { ScopeValue = (int)value; } }
    }

    public sealed class KingdomCapitalDesignation
    {
        [SaveableField(1)] public string KingdomStringId;
        [SaveableField(2)] public string SettlementStringId;
        [SaveableField(3)] public bool EverDesignated;
        [SaveableField(4)] public float DesignatedDay;
        [SaveableField(5)] public float ClearedDay;
        [SaveableField(6)] public long Revision;

        public KingdomCapitalDesignation()
        {
            KingdomStringId = string.Empty;
            SettlementStringId = string.Empty;
            DesignatedDay = -1f;
            ClearedDay = -1f;
        }
    }

    public sealed class CourtMatter
    {
        [SaveableField(1)] public string MatterId;
        [SaveableField(2)] public string SourceKey;
        [SaveableField(3)] public int KindValue;
        [SaveableField(4)] public int StateValue;
        [SaveableField(5)] public string Title;
        [SaveableField(6)] public string Summary;
        [SaveableField(7)] public string Confidentiality;
        [SaveableField(8)] public string ParticipantHeroIdsCsv;
        [SaveableField(9)] public string WitnessHeroIdsCsv;
        [SaveableField(10)] public string SettlementStringId;
        [SaveableField(11)] public string KingdomStringId;
        [SaveableField(12)] public float CreatedDay;
        [SaveableField(13)] public float DueDay;
        [SaveableField(14)] public float ScheduledDay;
        [SaveableField(15)] public float ResolvedDay;
        [SaveableField(16)] public int Priority;
        [SaveableField(17)] public bool IsCritical;
        [SaveableField(18)] public bool IsMajorRealmEvent;
        [SaveableField(19)] public bool IsAmbientRoleplay;
        [SaveableField(20)] public string DecisionOptionsJson;
        [SaveableField(21)] public string DefaultOutcomeJson;
        [SaveableField(22)] public string SelectedOptionId;
        [SaveableField(23)] public string TermsHash;
        [SaveableField(24)] public string LastCommandId;
        [SaveableField(25)] public long Revision;
        [SaveableField(26)] public string ResolutionReceiptJson;
        [SaveableField(27)] public string InvalidReason;
        [SaveableField(28)] public string CampaignId;
        [SaveableField(29)] public string TimelineId;
        [SaveableField(30)] public bool RequiresServer;
        [SaveableField(31)] public string ResolutionMode;
        [SaveableField(32)] public float RegentMailSentDay;
        [SaveableField(33)] public string RegentMailLetterId;

        public CourtMatter()
        {
            MatterId = "court_matter_" + Guid.NewGuid().ToString("N");
            SourceKey = string.Empty;
            Title = string.Empty;
            Summary = string.Empty;
            Confidentiality = "public";
            ParticipantHeroIdsCsv = string.Empty;
            WitnessHeroIdsCsv = string.Empty;
            SettlementStringId = string.Empty;
            KingdomStringId = string.Empty;
            DueDay = -1f;
            ScheduledDay = -1f;
            ResolvedDay = -1f;
            DecisionOptionsJson = "[]";
            DefaultOutcomeJson = "{}";
            SelectedOptionId = string.Empty;
            TermsHash = string.Empty;
            LastCommandId = string.Empty;
            ResolutionReceiptJson = "{}";
            InvalidReason = string.Empty;
            CampaignId = "default";
            TimelineId = "main";
            StateValue = (int)ReignCourtMatterState.Queued;
            ResolutionMode = string.Empty;
            RegentMailSentDay = -1f;
            RegentMailLetterId = string.Empty;
        }

        public ReignCourtMatterKind Kind { get { return (ReignCourtMatterKind)KindValue; } set { KindValue = (int)value; } }
        public ReignCourtMatterState State { get { return (ReignCourtMatterState)StateValue; } set { StateValue = (int)value; } }
        public bool IsTerminal { get { return State == ReignCourtMatterState.Resolved || State == ReignCourtMatterState.Refused || State == ReignCourtMatterState.Expired || State == ReignCourtMatterState.Invalidated; } }
    }

    public sealed class CourtAgendaItem
    {
        [SaveableField(1)] public string AgendaItemId;
        [SaveableField(2)] public string MatterId;
        [SaveableField(3)] public int AgendaDay;
        [SaveableField(4)] public int Slot;
        [SaveableField(5)] public bool IsUrgentInterruption;
        [SaveableField(6)] public string Status;

        public CourtAgendaItem()
        {
            AgendaItemId = "agenda_" + Guid.NewGuid().ToString("N");
            MatterId = string.Empty;
            Status = "scheduled";
        }
    }

    public sealed class CourtDecisionOption
    {
        [SaveableField(1)] public string OptionId;
        [SaveableField(2)] public string Label;
        [SaveableField(3)] public string Description;
        [SaveableField(4)] public string ConsequencePackageJson;
        [SaveableField(5)] public string ValidationRule;
        [SaveableField(6)] public string TermsHash;
        [SaveableField(7)] public bool RequiresConfirmation;

        public CourtDecisionOption()
        {
            OptionId = "option_" + Guid.NewGuid().ToString("N");
            Label = string.Empty;
            Description = string.Empty;
            ConsequencePackageJson = "{}";
            ValidationRule = string.Empty;
            TermsHash = string.Empty;
            RequiresConfirmation = true;
        }
    }

    public sealed class CourtOfficeAssignment
    {
        [SaveableField(1)] public string AssignmentId;
        [SaveableField(2)] public int OfficeValue;
        [SaveableField(3)] public string HeroStringId;
        [SaveableField(4)] public float AssignedDay;
        [SaveableField(5)] public float DismissedDay;
        [SaveableField(6)] public bool IsActive;
        [SaveableField(7)] public string DismissalReason;
        [SaveableField(8)] public long Revision;
        [SaveableField(9)] public string LastCommandId;

        public CourtOfficeAssignment()
        {
            AssignmentId = "office_" + Guid.NewGuid().ToString("N");
            HeroStringId = string.Empty;
            DismissedDay = -1f;
            IsActive = true;
            DismissalReason = string.Empty;
            LastCommandId = string.Empty;
        }

        public ReignCourtOffice Office { get { return (ReignCourtOffice)OfficeValue; } set { OfficeValue = (int)value; } }
    }

    public sealed class AmbassadorPosting
    {
        [SaveableField(1)] public string PostingId;
        [SaveableField(2)] public string HeroStringId;
        [SaveableField(3)] public string TargetKingdomStringId;
        [SaveableField(4)] public string MissionType;
        [SaveableField(5)] public string MissionTermsJson;
        [SaveableField(6)] public string Status;
        [SaveableField(7)] public float AssignedDay;
        [SaveableField(8)] public float ArrivalDay;
        [SaveableField(9)] public float LastReportDay;
        [SaveableField(10)] public float RecalledDay;
        [SaveableField(11)] public long Revision;
        [SaveableField(12)] public string LastCommandId;

        public AmbassadorPosting()
        {
            PostingId = "ambassador_" + Guid.NewGuid().ToString("N");
            HeroStringId = string.Empty;
            TargetKingdomStringId = string.Empty;
            MissionType = "public_information";
            MissionTermsJson = "{}";
            Status = "traveling";
            RecalledDay = -1f;
            LastCommandId = string.Empty;
        }
    }

    /// <summary>
    /// Save-compatible inbound posting for a foreign kingdom's resident envoy.
    /// The older AmbassadorPosting type remains untouched for legacy outbound records.
    /// </summary>
    public sealed class ForeignAmbassadorPosting
    {
        [SaveableField(1)] public string PostingId;
        [SaveableField(2)] public string HeroStringId;
        [SaveableField(3)] public string OriginKingdomStringId;
        [SaveableField(4)] public string OriginRulerHeroStringId;
        [SaveableField(5)] public string HostKingdomStringId;
        [SaveableField(6)] public string CapitalSettlementStringId;
        [SaveableField(7)] public string ShelterSettlementStringId;
        [SaveableField(8)] public string Status;
        [SaveableField(9)] public float AssignedDay;
        [SaveableField(10)] public float ArrivalDay;
        [SaveableField(11)] public float EndedDay;
        [SaveableField(12)] public int Charm;
        [SaveableField(13)] public int RulerTrust;
        [SaveableField(14)] public string AuthorityCharterJson;
        [SaveableField(15)] public long AuthorityRevision;
        [SaveableField(16)] public long Revision;
        [SaveableField(17)] public string LastCommandId;
        [SaveableField(18)] public string EndReason;

        public ForeignAmbassadorPosting()
        {
            PostingId = "foreign_ambassador_" + Guid.NewGuid().ToString("N");
            HeroStringId = string.Empty;
            OriginKingdomStringId = string.Empty;
            OriginRulerHeroStringId = string.Empty;
            HostKingdomStringId = string.Empty;
            CapitalSettlementStringId = string.Empty;
            ShelterSettlementStringId = string.Empty;
            Status = "traveling";
            EndedDay = -1f;
            AuthorityCharterJson = "{}";
            LastCommandId = string.Empty;
            EndReason = string.Empty;
        }

        public bool IsActive
        {
            get
            {
                return !string.Equals(Status, "dismissed", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Status, "war_recalled", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Status, "cancelled", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Status, "ended", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsResident
        {
            get { return string.Equals(Status, "resident", StringComparison.OrdinalIgnoreCase); }
        }
    }

    public sealed class CapitalAmbassadorTestLedger
    {
        [SaveableField(1)] public string RunId;
        [SaveableField(2)] public string CampaignId;
        [SaveableField(3)] public string Phase;
        [SaveableField(4)] public string CapitalSettlementStringId;
        [SaveableField(5)] public string AlternateCapitalSettlementStringId;
        [SaveableField(6)] public string PostingId;
        [SaveableField(7)] public string EnvoyHeroStringId;
        [SaveableField(8)] public string EnvoyOriginalSettlementStringId;
        [SaveableField(9)] public string OriginKingdomStringId;
        [SaveableField(10)] public float PreparedDay;
        [SaveableField(11)] public string PreparedFingerprint;
        [SaveableField(12)] public List<string> ObservationJson;
        [SaveableField(13)] public bool DisposableSaveConfirmed;

        public CapitalAmbassadorTestLedger()
        {
            RunId = string.Empty;
            CampaignId = string.Empty;
            Phase = string.Empty;
            CapitalSettlementStringId = string.Empty;
            AlternateCapitalSettlementStringId = string.Empty;
            PostingId = string.Empty;
            EnvoyHeroStringId = string.Empty;
            EnvoyOriginalSettlementStringId = string.Empty;
            OriginKingdomStringId = string.Empty;
            PreparedFingerprint = string.Empty;
            ObservationJson = new List<string>();
        }
    }

    public sealed class IntelligenceOperation
    {
        [SaveableField(1)] public string OperationId;
        [SaveableField(2)] public string OperationType;
        [SaveableField(3)] public string TargetType;
        [SaveableField(4)] public string TargetStringId;
        [SaveableField(5)] public int StateValue;
        [SaveableField(6)] public int GoldCost;
        [SaveableField(7)] public float InfluenceCost;
        [SaveableField(8)] public float StartedDay;
        [SaveableField(9)] public float DueDay;
        [SaveableField(10)] public float Risk;
        [SaveableField(11)] public float Confidence;
        [SaveableField(12)] public string SourceChainJson;
        [SaveableField(13)] public string ResultJson;
        [SaveableField(14)] public bool IsExposed;
        [SaveableField(15)] public long Revision;
        [SaveableField(16)] public string LastCommandId;

        public IntelligenceOperation()
        {
            OperationId = "intel_" + Guid.NewGuid().ToString("N");
            OperationType = "investigate_claim";
            TargetType = string.Empty;
            TargetStringId = string.Empty;
            StateValue = (int)ReignIntelligenceOperationState.Planned;
            SourceChainJson = "[]";
            ResultJson = "{}";
            LastCommandId = string.Empty;
        }

        public ReignIntelligenceOperationState State { get { return (ReignIntelligenceOperationState)StateValue; } set { StateValue = (int)value; } }
    }

    public sealed class CourtObligation
    {
        [SaveableField(1)] public string ObligationId;
        [SaveableField(2)] public string ObligationType;
        [SaveableField(3)] public string OwedByHeroStringId;
        [SaveableField(4)] public string OwedToHeroStringId;
        [SaveableField(5)] public string Description;
        [SaveableField(6)] public float CreatedDay;
        [SaveableField(7)] public float DueDay;
        [SaveableField(8)] public string TermsJson;
        [SaveableField(9)] public string TermsHash;
        [SaveableField(10)] public string BreachRuleJson;
        [SaveableField(11)] public string Status;
        [SaveableField(12)] public string ResolutionJson;
        [SaveableField(13)] public long Revision;

        public CourtObligation()
        {
            ObligationId = "obligation_" + Guid.NewGuid().ToString("N");
            ObligationType = "promise";
            OwedByHeroStringId = string.Empty;
            OwedToHeroStringId = string.Empty;
            Description = string.Empty;
            DueDay = -1f;
            TermsJson = "{}";
            TermsHash = string.Empty;
            BreachRuleJson = "{}";
            Status = "unresolved";
            ResolutionJson = "{}";
        }
    }

    public sealed class CourtPlot
    {
        [SaveableField(1)] public string PlotId;
        [SaveableField(2)] public string PlotType;
        [SaveableField(3)] public string DirectorHeroStringId;
        [SaveableField(4)] public string TargetStringId;
        [SaveableField(5)] public string ParticipantHeroIdsCsv;
        [SaveableField(6)] public string Status;
        [SaveableField(7)] public float CreatedDay;
        [SaveableField(8)] public float DueDay;
        [SaveableField(9)] public float Progress;
        [SaveableField(10)] public string TermsHash;
        [SaveableField(11)] public string SecretKnowledgeId;
        [SaveableField(12)] public bool IsDiscoveredByPlayer;
        [SaveableField(13)] public long Revision;

        public CourtPlot()
        {
            PlotId = "plot_" + Guid.NewGuid().ToString("N");
            PlotType = "scheme";
            DirectorHeroStringId = string.Empty;
            TargetStringId = string.Empty;
            ParticipantHeroIdsCsv = string.Empty;
            Status = "active";
            DueDay = -1f;
            TermsHash = string.Empty;
            SecretKnowledgeId = string.Empty;
        }
    }

    public sealed class CourtCounterSample
    {
        [SaveableField(1)] public int Day;
        [SaveableField(2)] public int Supply;
        [SaveableField(3)] public int Gold;
        [SaveableField(4)] public float Influence;
        [SaveableField(5)] public float Strength;
        [SaveableField(6)] public float Renown;
    }

    public sealed class CastleRoomSessionRecord
    {
        [SaveableField(1)] public string SessionKey;
        [SaveableField(2)] public string CampaignId;
        [SaveableField(3)] public string TimelineId;
        [SaveableField(4)] public string SettlementStringId;
        [SaveableField(5)] public string CultureId;
        [SaveableField(6)] public int CampaignDay;
        [SaveableField(7)] public int TimeBlock;
        [SaveableField(8)] public int Room;
        [SaveableField(9)] public string PrivateGuestHeroStringId;
        [SaveableField(10)] public string OccupantHeroIdsCsv;
        [SaveableField(11)] public string RemovedHeroIdsCsv;
        [SaveableField(12)] public string ImagePromptSnapshot;
        [SaveableField(13)] public string DialoguePromptSnapshot;
        [SaveableField(14)] public string PromptRevision;
        [SaveableField(15)] public string ImageStatus;
        [SaveableField(16)] public string ImageCacheKey;
        [SaveableField(17)] public string ImageLocalPath;
        [SaveableField(18)] public string OpeningStatus;
        [SaveableField(19)] public string ServerConversationSessionId;
        [SaveableField(20)] public string TranscriptJson;
        [SaveableField(21)] public long Revision;
        [SaveableField(22)] public List<string> TranscriptChunks;
        [SaveableField(23)] public string InteractionMode;
        [SaveableField(24)] public string DisplayName;
        [SaveableField(25)] public string ChildHeroIdsCsv;
        [SaveableField(26)] public string ScenePlanJson;

        public CastleRoomSessionRecord()
        {
            SessionKey = string.Empty; CampaignId = string.Empty; TimelineId = string.Empty;
            SettlementStringId = string.Empty; CultureId = "generic";
            PrivateGuestHeroStringId = string.Empty; OccupantHeroIdsCsv = string.Empty;
            RemovedHeroIdsCsv = string.Empty; ImagePromptSnapshot = string.Empty;
            DialoguePromptSnapshot = string.Empty; PromptRevision = string.Empty;
            ImageStatus = "pending"; ImageCacheKey = string.Empty; ImageLocalPath = string.Empty;
            OpeningStatus = "pending"; ServerConversationSessionId = string.Empty;
            TranscriptJson = "[]"; TranscriptChunks = new List<string>();
            InteractionMode = "castle_chat"; DisplayName = string.Empty;
            ChildHeroIdsCsv = string.Empty; ScenePlanJson = "{}";
        }

        public string ReadTranscriptJson()
        {
            return TranscriptChunks != null && TranscriptChunks.Count > 0
                ? ReignSavePayloadCodec.Decode(TranscriptChunks)
                : string.IsNullOrWhiteSpace(TranscriptJson) ? "[]" : TranscriptJson;
        }

        public void WriteTranscriptJson(string transcriptJson)
        {
            TranscriptChunks = ReignSavePayloadCodec.Encode(
                string.IsNullOrWhiteSpace(transcriptJson) ? "[]" : transcriptJson);
            TranscriptJson = string.Empty;
        }
    }

    public sealed class CastleBathHistoryRecord
    {
        [SaveableField(1)] public string HeroStringId;
        [SaveableField(2)] public int SelectionCount;
        [SaveableField(3)] public int LastSelectedDay;
        [SaveableField(4)] public int LastSelectedBlock;

        public CastleBathHistoryRecord()
        {
            HeroStringId = string.Empty;
            LastSelectedDay = int.MinValue;
            LastSelectedBlock = -1;
        }
    }

    /// <summary>
    /// Regent is deliberately separate from the five major offices.  It is a
    /// temporary delegation of ordinary royal court work, not a native party or
    /// kingdom position, so Bannerlord cannot move or replace it on its own.
    /// </summary>
    public sealed class CourtRegentAssignment
    {
        [SaveableField(1)] public string AssignmentId;
        [SaveableField(2)] public string HeroStringId;
        [SaveableField(3)] public float AssignedDay;
        [SaveableField(4)] public float DismissedDay;
        [SaveableField(5)] public bool IsActive;
        [SaveableField(6)] public string DismissalReason;
        [SaveableField(7)] public long Revision;
        [SaveableField(8)] public string LastCommandId;

        public CourtRegentAssignment()
        {
            AssignmentId = "regent_" + Guid.NewGuid().ToString("N");
            HeroStringId = string.Empty;
            DismissedDay = -1f;
            IsActive = true;
            DismissalReason = string.Empty;
            LastCommandId = string.Empty;
        }
    }

}
