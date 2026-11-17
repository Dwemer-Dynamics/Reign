using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Core.Contracts.Court;

namespace ReignBeta.Court
{
    public enum ReignDocketPetitionState
    {
        Pending = 0,
        Suspended = 1,
        Granted = 2,
        Refused = 3,
        Invalidated = 4
    }

    public enum ReignDocketGrantMethod
    {
        None = 0,
        Direct = 1,
        GoldSubstitute = 2
    }

    public enum ReignNobleMatterState
    {
        Dormant = 0,
        Active = 1,
        Deferred = 2,
        Ruled = 3,
        Acquitted = 4,
        Convicted = 5,
        Reversed = 6,
        FailedActivation = 7
    }

    public enum ReignNobleRuling
    {
        None = 0,
        SideA = 1,
        SideB = 2,
        DeferToChancellor = 3,
        AcquitAll = 4,
        ConvictParticipant = 5
    }

    public enum ReignChancellorOfficeState
    {
        Vacant = 0,
        Inactive = 1,
        Active = 2,
        EmergencyActive = 3,
        CaptiveContinuity = 4,
        Handoff = 5
    }

    public sealed partial class ReignRulerDocketState
    {
        public const int CurrentVersion = 3;

        public int Version { get; set; } = CurrentVersion;
        public bool LegacyMigrationComplete { get; set; }
        public int LastGeneratedDay { get; set; } = -1;
        public int LastMidnightDecisionDay { get; set; } = -1;
        public int LastOfficeTickDay { get; set; } = -1;
        public int LastCommitmentTickDay { get; set; } = -1;
        public List<ReignDocketPetition> Petitions { get; set; } = new List<ReignDocketPetition>();
        public List<ReignNobleDocketMatter> NobleMatters { get; set; } = new List<ReignNobleDocketMatter>();
        public List<ReignDocketCommitment> Commitments { get; set; } = new List<ReignDocketCommitment>();
        public List<ReignSoldierExpedition> Expeditions { get; set; } = new List<ReignSoldierExpedition>();
        public ReignChancellorOffice Chancellor { get; set; } = new ReignChancellorOffice();
        public List<ReignDocketHistoryRecord> History { get; set; } = new List<ReignDocketHistoryRecord>();
        public List<int> ActiveChancellorDays { get; set; } = new List<int>();
        public bool ChancellorAbsentLordThresholdActive { get; set; }
        public int LastChancellorAbsentLordOccurrenceDay { get; set; } = -1;
        public List<ReignDocketCooldown> Cooldowns { get; set; } = new List<ReignDocketCooldown>();
        public List<ReignDocketSettlementSample> SettlementSamples { get; set; } = new List<ReignDocketSettlementSample>();
        public List<ReignDirectionalRelationAdjustment> PendingRelationAdjustments { get; set; } = new List<ReignDirectionalRelationAdjustment>();
        public List<ReignDocketMemoryJob> PendingMemoryJobs { get; set; } = new List<ReignDocketMemoryJob>();
        public List<ReignDocketWorldHistoryJob> PendingWorldHistoryJobs { get; set; } = new List<ReignDocketWorldHistoryJob>();
        public List<ReignCourtStayLease> CourtStayLeases { get; set; } = new List<ReignCourtStayLease>();

        public void Normalize()
        {
            Version = Math.Max(Version, CurrentVersion);
            Petitions = (Petitions ?? new List<ReignDocketPetition>()).Where(x => x != null).ToList();
            foreach (ReignDocketPetition petition in Petitions)
            {
                petition.NormalizeAudience();
            }
            NobleMatters = (NobleMatters ?? new List<ReignNobleDocketMatter>()).Where(x => x != null).ToList();
            foreach (ReignNobleDocketMatter matter in NobleMatters) matter.Normalize();
            Commitments = (Commitments ?? new List<ReignDocketCommitment>()).Where(x => x != null).ToList();
            Expeditions = (Expeditions ?? new List<ReignSoldierExpedition>()).Where(x => x != null).ToList();
            Chancellor = Chancellor ?? new ReignChancellorOffice();
            History = (History ?? new List<ReignDocketHistoryRecord>()).Where(x => x != null).ToList();
            ActiveChancellorDays = (ActiveChancellorDays ?? new List<int>()).Distinct().OrderBy(x => x).ToList();
            Cooldowns = (Cooldowns ?? new List<ReignDocketCooldown>()).Where(x => x != null).ToList();
            SettlementSamples = (SettlementSamples ?? new List<ReignDocketSettlementSample>()).Where(x => x != null).ToList();
            PendingRelationAdjustments = (PendingRelationAdjustments ?? new List<ReignDirectionalRelationAdjustment>()).Where(x => x != null).ToList();
            PendingMemoryJobs = (PendingMemoryJobs ?? new List<ReignDocketMemoryJob>()).Where(x => x != null).ToList();
            PendingWorldHistoryJobs = (PendingWorldHistoryJobs ?? new List<ReignDocketWorldHistoryJob>()).Where(x => x != null).ToList();
            CourtStayLeases = (CourtStayLeases ?? new List<ReignCourtStayLease>()).Where(x => x != null).ToList();
            NormalizeCourtLife();
        }
    }

    public sealed class ReignNobleDocketMatter
    {
        public string MatterId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public string ReignId { get; set; } = string.Empty;
        public int ReceivedDay { get; set; }
        public string TemplateId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public ReignNobleMatterSeverity Severity { get; set; }
        public ReignNobleMatterCategory Category { get; set; }
        public ReignNobleMatterState State { get; set; } = ReignNobleMatterState.Dormant;
        public string Premise { get; set; } = string.Empty;
        public string DemandA { get; set; } = string.Empty;
        public string DemandB { get; set; } = string.Empty;
        public bool DemandARevealed { get; set; }
        public bool DemandBRevealed { get; set; }
        public string CanonicalTruth { get; set; } = string.Empty;
        public string CanonicalTruthHash { get; set; } = string.Empty;
        public string CanonicalWinningRole { get; set; } = string.Empty;
        public string VictimHeroId { get; set; } = string.Empty;
        public string ActualCulpritHeroId { get; set; } = string.Empty;
        public bool ActivationCommitted { get; set; }
        public string ActivationReceiptId { get; set; } = string.Empty;
        public string ActivationFailure { get; set; } = string.Empty;
        public int ActivatedDay { get; set; } = -1;
        public ReignNobleRuling Ruling { get; set; }
        public string RuledForHeroId { get; set; } = string.Empty;
        public string RuledAgainstHeroId { get; set; } = string.Empty;
        public string ConvictedHeroId { get; set; } = string.Empty;
        public int AcceptanceTier { get; set; }
        public string ImmediateTermsJson { get; set; } = string.Empty;
        public string AppliedEffectsHash { get; set; } = string.Empty;
        public bool EffectsCommitted { get; set; }
        public int DecidedDay { get; set; } = -1;
        public string DecisionSummary { get; set; } = string.Empty;
        public string SupersededByRecordId { get; set; } = string.Empty;
        public bool Irreversible { get; set; }
        public string TranscriptId { get; set; } = string.Empty;
        public string SceneAssetPath { get; set; } = string.Empty;
        public List<ReignNobleMatterParticipant> Participants { get; set; } = new List<ReignNobleMatterParticipant>();
        public List<ReignNobleEvidenceItem> Evidence { get; set; } = new List<ReignNobleEvidenceItem>();
        public List<string> ActiveAudienceHeroIds { get; set; } = new List<string>();
        public List<ReignDocketConversationLine> ConversationLines { get; set; } = new List<ReignDocketConversationLine>();
        public ReignChancellorInvestigation Investigation { get; set; } = new ReignChancellorInvestigation();
        public ReignCourtCustodyHold CustodyHold { get; set; } = new ReignCourtCustodyHold();

        public bool IsPending => State == ReignNobleMatterState.Dormant
            || State == ReignNobleMatterState.Active || State == ReignNobleMatterState.Deferred;
        public bool DemandsRevealed => DemandARevealed && DemandBRevealed;
        public bool IsMurder => string.Equals(TemplateId, "exceptional-murder", StringComparison.OrdinalIgnoreCase);

        public void Normalize()
        {
            Participants = (Participants ?? new List<ReignNobleMatterParticipant>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.HeroId))
                .GroupBy(x => x.HeroId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            Evidence = (Evidence ?? new List<ReignNobleEvidenceItem>()).Where(x => x != null).ToList();
            ActiveAudienceHeroIds = (ActiveAudienceHeroIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
            List<ReignDocketConversationLine> lines = (ConversationLines ?? new List<ReignDocketConversationLine>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text)).ToList();
            ConversationLines = lines.Skip(Math.Max(0, lines.Count - 60)).ToList();
            Investigation = Investigation ?? new ReignChancellorInvestigation();
            CustodyHold = CustodyHold ?? new ReignCourtCustodyHold();
        }
    }

    public sealed class ReignNobleMatterParticipant
    {
        public string HeroId { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string ClanId { get; set; } = string.Empty;
        public string BirthClanId { get; set; } = string.Empty;
        public string ClanLeaderHeroId { get; set; } = string.Empty;
        public bool IsPrincipal { get; set; } = true;
        public bool IsClanLeader { get; set; }
        public bool KnowsCanonicalTruth { get; set; }
        public string PrivateKnowledge { get; set; } = string.Empty;
    }

    public sealed class ReignNobleEvidenceItem
    {
        public string EvidenceId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string PublicDescription { get; set; } = string.Empty;
        public string PrivateMeaning { get; set; } = string.Empty;
        public string SourceHeroId { get; set; } = string.Empty;
        public string ImplicatesHeroId { get; set; } = string.Empty;
        public bool Reliable { get; set; }
        public bool CompleteChain { get; set; }
        public bool Revealed { get; set; }
    }

    public sealed class ReignChancellorInvestigation
    {
        public bool Scheduled { get; set; }
        public bool Resolved { get; set; }
        public int DueDay { get; set; } = -1;
        public string ChancellorHeroId { get; set; } = string.Empty;
        public int Charm { get; set; }
        public int Leadership { get; set; }
        public int Steward { get; set; }
        public double SuccessChance { get; set; }
        public bool SuccessRoll { get; set; }
        public string FailureCandidateHeroId { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
    }

    public sealed class ReignCourtCustodyHold
    {
        public bool Active { get; set; }
        public string PrisonerHeroId { get; set; } = string.Empty;
        public string CapitalSettlementId { get; set; } = string.Empty;
        public int StartedDay { get; set; } = -1;
        public string JudgmentRecordId { get; set; } = string.Empty;
        public bool Executed { get; set; }
        public bool ReleasedByReversal { get; set; }
    }

    public sealed class ReignCourtStayLease
    {
        public string LeaseId { get; set; } = string.Empty;
        public string MatterId { get; set; } = string.Empty;
        public string HeroId { get; set; } = string.Empty;
        public string CapitalSettlementId { get; set; } = string.Empty;
        public int StartDay { get; set; }
        public int ReleaseDay { get; set; }
        public bool Released { get; set; }
    }

    public sealed class ReignDocketPetition
    {
        public string PetitionId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public string ReignId { get; set; } = string.Empty;
        public int ReceivedDay { get; set; }
        public ReignPetitionKind Kind { get; set; }
        public ReignPetitionSeverity Severity { get; set; }
        public ReignDocketPetitionState State { get; set; } = ReignDocketPetitionState.Pending;
        public ReignDocketGrantMethod GrantMethod { get; set; }
        public string PetitionerHeroId { get; set; } = string.Empty;
        public string PetitionerName { get; set; } = string.Empty;
        public string TargetSettlementId { get; set; } = string.Empty;
        public string TargetSettlementName { get; set; } = string.Empty;
        public string ParentTownId { get; set; } = string.Empty;
        public string ParentTownName { get; set; } = string.Empty;
        public double NeedValue { get; set; }
        public double HealthyExpectedValue { get; set; }
        public double NormalizedNeed { get; set; }
        public int GoldCost { get; set; }
        public int FoodStockCost { get; set; }
        public int SoldierCount { get; set; }
        public int DurationDays { get; set; }
        public int RequesterRelationDelta { get; set; }
        public int AssociatedRelationDelta { get; set; }
        public double DailyEffect { get; set; }
        public int GoldSubstituteCost { get; set; }
        public int HiddenCasualtyCount { get; set; }
        public string DangerLabel { get; set; } = string.Empty;
        public string ProblemSummary { get; set; } = string.Empty;
        public string TechnicalFailure { get; set; } = string.Empty;
        public string TranscriptId { get; set; } = string.Empty;
        public string SceneAssetPath { get; set; } = string.Empty;
        public int DecidedDay { get; set; } = -1;
        public string DecisionReason { get; set; } = string.Empty;
        public string TermsHash { get; set; } = string.Empty;
        public List<string> ActiveAudienceHeroIds { get; set; } = new List<string>();
        public List<ReignDocketConversationLine> ConversationLines { get; set; } = new List<ReignDocketConversationLine>();

        public bool IsPending => State == ReignDocketPetitionState.Pending || State == ReignDocketPetitionState.Suspended;

        public void NormalizeAudience()
        {
            ActiveAudienceHeroIds = (ActiveAudienceHeroIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<ReignDocketConversationLine> lines = (ConversationLines
                ?? new List<ReignDocketConversationLine>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                .ToList();
            ConversationLines = lines.Skip(Math.Max(0, lines.Count - 40)).ToList();
        }
    }

    public sealed class ReignDocketConversationLine
    {
        public string Speaker { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public sealed class ReignPetitionDecisionQuote
    {
        public string PetitionId { get; set; } = string.Empty;
        public bool Valid { get; set; }
        public bool CanGrantDirect { get; set; }
        public bool CanGrantWithGold { get; set; }
        public int DirectGoldCost { get; set; }
        public int GoldSubstituteCost { get; set; }
        public int FoodStockCost { get; set; }
        public int SoldierCount { get; set; }
        public int HealthyGarrison { get; set; }
        public int RequiredGarrisonReserve { get; set; }
        public string Warning { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignDocketCommitment
    {
        public string CommitmentId { get; set; } = string.Empty;
        public string PetitionId { get; set; } = string.Empty;
        public ReignPetitionKind Kind { get; set; }
        public ReignPetitionSeverity Severity { get; set; }
        public string TargetSettlementId { get; set; } = string.Empty;
        public string TargetSettlementName { get; set; } = string.Empty;
        public int StartDay { get; set; }
        public int EndDay { get; set; }
        public double DailyEffect { get; set; }
        public int LastAppliedDay { get; set; } = -1;
        public bool Active { get; set; } = true;
        public string EndReason { get; set; } = string.Empty;
    }

    public sealed class ReignSoldierExpedition
    {
        public string ExpeditionId { get; set; } = string.Empty;
        public string PetitionId { get; set; } = string.Empty;
        public string OriginalOwnerClanId { get; set; } = string.Empty;
        public string OriginalCapitalId { get; set; } = string.Empty;
        public string TargetSettlementId { get; set; } = string.Empty;
        public int DepartureDay { get; set; }
        public int ReturnDay { get; set; }
        public int HiddenCasualtyCount { get; set; }
        public string DangerLabel { get; set; } = string.Empty;
        public bool Returned { get; set; }
        public string ReturnSettlementId { get; set; } = string.Empty;
        public string ReturnStatus { get; set; } = string.Empty;
        public List<ReignSoldierManifestEntry> Manifest { get; set; } = new List<ReignSoldierManifestEntry>();
    }

    public sealed class ReignSoldierManifestEntry
    {
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Casualties { get; set; }
    }

    public sealed class ReignChancellorOffice
    {
        public string TermId { get; set; } = string.Empty;
        public string HeroId { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public ReignChancellorOfficeState State { get; set; } = ReignChancellorOfficeState.Vacant;
        public int Salary { get; set; }
        public bool DesiredActive { get; set; }
        public int DesiredActiveEffectiveDay { get; set; } = -1;
        public int AppointedDay { get; set; } = -1;
        public int LastPaidDay { get; set; } = -1;
        public int EndedDay { get; set; } = -1;
        public string EndReason { get; set; } = string.Empty;
        public bool Emergency { get; set; }
        public string EmergencyReason { get; set; } = string.Empty;
        public int EmergencyStartedDay { get; set; } = -1;
        public int RulerReleasedDay { get; set; } = -1;
        public int HandoffDueDay { get; set; } = -1;
        public ReignChancellorOfficeState PreCaptureState { get; set; } = ReignChancellorOfficeState.Inactive;
        public bool PreCaptureDesiredActive { get; set; }
        public int ServiceStartedDay { get; set; } = -1;
        public int TotalPaidGold { get; set; }
        public int PaidActiveDays { get; set; }
        public int FailedPaymentDays { get; set; }
        public string OriginalHomeSettlementId { get; set; } = string.Empty;
        public string RelinquishedDuties { get; set; } = string.Empty;
        public string EmergencyCorrelationId { get; set; } = string.Empty;
        public bool EmergencyTakeoverHistoryRecorded { get; set; }
        public bool EmergencyConclusionHistoryRecorded { get; set; }

        public bool IsVacant => string.IsNullOrWhiteSpace(HeroId) || State == ReignChancellorOfficeState.Vacant;
        public bool SuppressesPetitions => State == ReignChancellorOfficeState.Active
            || State == ReignChancellorOfficeState.EmergencyActive
            || State == ReignChancellorOfficeState.CaptiveContinuity
            || State == ReignChancellorOfficeState.Handoff;
        public bool IsPaidActive => State == ReignChancellorOfficeState.Active;
    }

    public sealed class ReignDocketCooldown
    {
        public string TargetSettlementId { get; set; } = string.Empty;
        public ReignPetitionKind Kind { get; set; }
        public int UntilDay { get; set; }
    }

    public sealed class ReignDocketSettlementSample
    {
        public string SettlementId { get; set; } = string.Empty;
        public int Day { get; set; }
        public double Prosperity { get; set; }
        public double FoodStocks { get; set; }
        public double Security { get; set; }
        public double Hearth { get; set; }
        public double ActualVillageOutput { get; set; }
        public double HealthyVillageOutput { get; set; }
    }

    public sealed class ReignDirectionalRelationAdjustment
    {
        public string AdjustmentId { get; set; } = string.Empty;
        public string ObserverHeroId { get; set; } = string.Empty;
        public string SubjectHeroId { get; set; } = string.Empty;
        public int Delta { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool Applied { get; set; }
        public bool Skipped { get; set; }
        public string ReceiptId { get; set; } = string.Empty;
    }

    public sealed class ReignDocketMemoryJob
    {
        public string JobId { get; set; } = string.Empty;
        public string HeroId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public int WorldDay { get; set; }
        public bool Applied { get; set; }
        public bool Skipped { get; set; }
        public string ReceiptId { get; set; } = string.Empty;
        public string LastError { get; set; } = string.Empty;
    }

    public sealed class ReignDocketWorldHistoryJob
    {
        public string JobId { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ChancellorHeroId { get; set; } = string.Empty;
        public string RulerHeroId { get; set; } = string.Empty;
        public string KingdomId { get; set; } = string.Empty;
        public int StartDay { get; set; }
        public int EndDay { get; set; } = -1;
        public string Outcome { get; set; } = string.Empty;
        public bool Applied { get; set; }
    }

    public sealed class ReignDocketHistoryRecord
    {
        public string RecordId { get; set; } = string.Empty;
        public string ReignId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string PetitionId { get; set; } = string.Empty;
        public ReignPetitionKind? PetitionKind { get; set; }
        public ReignPetitionSeverity? Severity { get; set; }
        public int Day { get; set; }
        public string PetitionerHeroId { get; set; } = string.Empty;
        public string PetitionerName { get; set; } = string.Empty;
        public string SettlementId { get; set; } = string.Empty;
        public string SettlementName { get; set; } = string.Empty;
        public string ChancellorHeroId { get; set; } = string.Empty;
        public string ChancellorName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string TranscriptId { get; set; } = string.Empty;
        public string SceneAssetPath { get; set; } = string.Empty;
        public string LinkedWorldHistoryEventId { get; set; } = string.Empty;
        public string LinkedMemoryId { get; set; } = string.Empty;
    }
}
