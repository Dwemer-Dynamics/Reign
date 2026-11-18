using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Government;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.World;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Integration
{
    public sealed class ReignDialogueLine
    {
        public string ExchangeId { get; set; } = "";
        public ReignDialogueLine(string speaker, string text, string role, string channel = "in_person", string letterId = "", string threadId = "", string direction = "", double worldDay = 0d)
        {
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            Role = role ?? string.Empty;
            Channel = string.IsNullOrWhiteSpace(channel) ? "in_person" : channel;
            LetterId = letterId ?? string.Empty;
            ThreadId = threadId ?? string.Empty;
            Direction = direction ?? string.Empty;
            WorldDay = worldDay;
        }

        public string Speaker { get; }
        public string Text { get; }
        public string Role { get; }
        public string Channel { get; }
        public string LetterId { get; }
        public string ThreadId { get; }
        public string Direction { get; }
        public double WorldDay { get; }
        public bool IsCorrespondence => string.Equals(Channel, "correspondence", StringComparison.OrdinalIgnoreCase);
        public string DisplaySpeaker => IsCorrespondence
            ? Speaker + " (Letter" + (WorldDay > 0d ? ", Day " + WorldDay.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : string.Empty) + ")"
            : Speaker;
    }

    public sealed class ReignDialogueReply
    {
        public bool Ok { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string EncyclopediaText { get; set; } = string.Empty;
        public string ActionShadowPreview { get; set; } = string.Empty;
        public bool ConceptionGateNeeded { get; set; }
        public ReignConceptionAttemptResult ConceptionAttempt { get; set; }
        // Combined evidence: test directives, then dialogue actions. Read-only for audits.
        public List<ReignWorldActionRecord> QueuedActions { get; set; } = new List<ReignWorldActionRecord>();
        // Test directives only; the sole list the Individual view imports.
        public List<ReignWorldActionRecord> TestDirectiveActions { get; set; } = new List<ReignWorldActionRecord>();
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string RequestSceneTurnId { get; set; } = string.Empty;
        public string ConversationSessionId { get; set; } = string.Empty;
        public string ExchangeId { get; set; } = string.Empty;
        public JArray TurnIds { get; set; } = new JArray();
        public JArray SelectedContextPulls { get; set; } = new JArray();
        public JArray ContextBundles { get; set; } = new JArray();
        public JArray MemoryWrites { get; set; } = new JArray();
        public JObject ChancellorDecision { get; set; } = new JObject();
        public JObject RawResponse { get; set; } = new JObject();
    }

    public sealed class ReignConversationFinishResult
    {
        public bool Ok { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string SceneSummaryId { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public JObject Raw { get; set; } = new JObject();
    }

    public sealed class ReignEventReply
    {
        public bool Ok { get; set; }
        public string HeroStringId { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Participation { get; set; } = "speak";
        public string ReactionTargetHeroStringId { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public string RelationshipSignal { get; set; } = string.Empty;
        public JArray RelationshipAssessments { get; set; } = new JArray();
        public JArray SelectedContextPulls { get; set; } = new JArray();
        public JArray ContextBundles { get; set; } = new JArray();
        public JArray QueuedActions { get; set; } = new JArray();
        public JObject RawResponse { get; set; } = new JObject();
        public string Error { get; set; } = string.Empty;
        public string EncyclopediaText { get; set; } = string.Empty;
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
    }

    public sealed class ReignPartyChatReply
    {
        public bool Ok { get; set; }
        public string SpeakerHeroStringId { get; set; } = string.Empty;
        public string SpeakerName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public string RelationshipSignal { get; set; } = string.Empty;
        public JArray RelationshipAssessments { get; set; } = new JArray();
        public string Participation { get; set; } = "speak";
        public string ReactionTargetHeroStringId { get; set; } = string.Empty;
        public string PrivateAudienceHeroStringId { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string EncyclopediaText { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string ConversationSessionId { get; set; } = string.Empty;
        public string ExchangeId { get; set; } = string.Empty;
        public JArray SelectedContextPulls { get; set; } = new JArray();
        public JArray QueuedActions { get; set; } = new JArray();
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
        public JObject RawResponse { get; set; } = new JObject();
    }

    public sealed class ReignHeroGenerationResult
    {
        public bool Ok { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignCharacterConstructionResult
    {
        public bool Ok { get; set; }
        public int Constructed { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public int LlmUsed { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignGeneratedWildernessRequest
    {
        public string TerrainKey { get; set; } = "plain";
        public string LocationText { get; set; } = "on the road";
        public string TimeOfDayText { get; set; } = "unknown time of day";
        public string ExternalHeroId { get; set; } = string.Empty;
        public string ExternalContext { get; set; } = string.Empty;
        public List<ReignGeneratedWildernessParticipant> Participants { get; set; } = new List<ReignGeneratedWildernessParticipant>();
        public List<ReignRecentWildernessEvent> RecentEvents { get; set; } = new List<ReignRecentWildernessEvent>();
    }

    public sealed class ReignRecentWildernessEvent
    {
        [JsonProperty("varietyKey")]
        public string VarietyKey { get; set; } = string.Empty;
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;
        [JsonProperty("approachDescription")]
        public string ApproachDescription { get; set; } = string.Empty;
        [JsonProperty("openingText")]
        public string OpeningText { get; set; } = string.Empty;
    }

    public sealed class ReignGeneratedWildernessParticipant
    {
        public Hero Hero { get; set; }
        public string Role { get; set; } = string.Empty;
        public bool IsOutsideNpc { get; set; }
    }

    public sealed class ReignGeneratedWildernessScenario
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string VarietyKey { get; set; } = string.Empty;
        public string InitiatorHeroStringId { get; set; } = string.Empty;
        public string ApproachDescription { get; set; } = string.Empty;
        public string OpeningText { get; set; } = string.Empty;
        public string PlayerHook { get; set; } = string.Empty;
        public List<string> ParticipantHeroIds { get; set; } = new List<string>();
        public string ExternalHeroId { get; set; } = string.Empty;
        public string ExternalContext { get; set; } = string.Empty;
        public string TerrainKey { get; set; } = string.Empty;
        public string EmotionalPressure { get; set; } = string.Empty;
        public string SurfaceClues { get; set; } = string.Empty;
        public string HiddenContext { get; set; } = string.Empty;
        public string DiscoveryRoutes { get; set; } = string.Empty;
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
    }

    public sealed class ReignPortraitGenerationResult
    {
        public bool Ok { get; set; }
        public byte[] ImageBytes { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Adapter { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public long ClientTotalMs { get; set; }
        public long ServerDurationMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
        public string ProductSchema { get; set; } = string.Empty;
        public int ProductVersion { get; set; }
        public bool ProductAccepted { get; set; }
        public int PortraitWidth { get; set; }
        public int PortraitHeight { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public string PortraitSha256 { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public string PromptSha256 { get; set; } = string.Empty;
        public string EffectivePrompt { get; set; } = string.Empty;
        public string PortraitProductReceiptJson { get; set; } = string.Empty;
        public string PortraitInputJson { get; set; } = string.Empty;
        public byte[] SourceImageBytes { get; set; }
        public PortraitFaceFocus FaceFocus { get; set; }
        public string GenerationOperationId { get; set; } = string.Empty;
        public string GenerationStatus { get; set; } = string.Empty;
        public bool GenerationMayStillComplete { get; set; }
        public bool Recovered { get; set; }
    }
}
