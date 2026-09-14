using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Core.Contracts.Court;

namespace ReignBeta.Court
{
    public enum ReignCourtLifeMatterState
    {
        Pending = 0, Arriving = 1, Active = 2, Deferred = 3,
        Resolved = 4, Expired = 5, Invalidated = 6
    }

    public sealed partial class ReignRulerDocketState
    {
        public List<ReignCourtLifeMatter> CourtLifeMatters { get; set; } = new List<ReignCourtLifeMatter>();
        public int LastCourtLifeTickDay { get; set; } = -1;

        private void NormalizeCourtLife()
        {
            CourtLifeMatters = (CourtLifeMatters ?? new List<ReignCourtLifeMatter>()).Where(x => x != null).ToList();
            foreach (ReignCourtLifeMatter matter in CourtLifeMatters) matter.Normalize();
        }
    }

    public sealed class ReignCourtLifeMatter
    {
        public string MatterId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public string ReignId { get; set; } = string.Empty;
        public ReignDocketSource Source { get; set; }
        public string TemplateId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string SettlementId { get; set; } = string.Empty;
        public string KingdomId { get; set; } = string.Empty;
        public ReignNobleMatterSeverity Severity { get; set; }
        public int ReceivedDay { get; set; }
        public double AvailableDay { get; set; }
        public double ExpiresDay { get; set; } = -1;
        public ReignCourtLifeMatterState State { get; set; }
        public string PayloadJson { get; set; } = "{}";
        public string TranscriptId { get; set; } = string.Empty;
        public string SceneAssetPath { get; set; } = string.Empty;
        public string DecisionSummary { get; set; } = string.Empty;
        public string SelectedOptionId { get; set; } = string.Empty;
        public string ResolutionReceiptId { get; set; } = string.Empty;
        public bool EffectsCommitted { get; set; }
        public bool TechnicalFailure { get; set; }
        public string PendingPlayerTurnId { get; set; } = string.Empty;
        public string PendingPlayerText { get; set; } = string.Empty;
        public List<string> CompletedReplyKeys { get; set; } = new List<string>();
        public Dictionary<string, string> ReplyTextsByKey { get; set; } = new Dictionary<string, string>();
        public List<string> CompletedInterpretationKeys { get; set; } = new List<string>();
        public string AcceptedDecisionJson { get; set; } = "{}";
        public List<ReignCourtLifeParticipant> Participants { get; set; } = new List<ReignCourtLifeParticipant>();
        public List<ReignCourtLifeOption> Options { get; set; } = new List<ReignCourtLifeOption>();
        public List<ReignDocketConversationLine> ConversationLines { get; set; } = new List<ReignDocketConversationLine>();
        public List<string> CompletedPlayerTurnIds { get; set; } = new List<string>();
        public bool IsPending => State == ReignCourtLifeMatterState.Pending || State == ReignCourtLifeMatterState.Arriving
            || State == ReignCourtLifeMatterState.Active || State == ReignCourtLifeMatterState.Deferred;

        public void Normalize()
        {
            CompletedReplyKeys = CompletedReplyKeys ?? new List<string>();
            ReplyTextsByKey = ReplyTextsByKey ?? new Dictionary<string, string>();
            CompletedInterpretationKeys = CompletedInterpretationKeys ?? new List<string>();
            Participants = (Participants ?? new List<ReignCourtLifeParticipant>()).Where(x => x != null)
                .GroupBy(x => x.ActorId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            Options = (Options ?? new List<ReignCourtLifeOption>()).Where(x => x != null).ToList();
            if (Options.Count == 0 && (Source == ReignDocketSource.Family || Source == ReignDocketSource.DomesticNoble))
                Options.Add(new ReignCourtLifeOption { OptionId = "conclude", Label = "Conclude the visit",
                    Description = "The audience has ended. Any later plans can be continued in ordinary conversation." });
            ConversationLines = (ConversationLines ?? new List<ReignDocketConversationLine>()).Where(x => x != null).ToList();
            CompletedPlayerTurnIds = (CompletedPlayerTurnIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).ToList();
        }
    }

    public sealed class ReignCourtLifeParticipant
    {
        public string ActorId { get; set; } = string.Empty;
        public string HeroId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string PrivateContext { get; set; } = string.Empty;
        public double Age { get; set; }
        public bool IsFemale { get; set; }
    }

    public sealed class ReignCourtLifeOption
    {
        public string OptionId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TermsJson { get; set; } = "{}";
    }
}
