using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Runtime
{
    // Serialized only by the native campaign save. Never fetch a newer transcript
    // from a server/cache when restoring an older native checkpoint.
    public sealed class ReignPartyChatContinuation
    {
        public int Version { get; set; } = 1;
        public string CampaignId { get; set; } = "";
        public string PlayerId { get; set; } = "";
        public string SourceSessionId { get; set; } = "";
        public string LocationId { get; set; } = "";
        public string RoomId { get; set; } = "";
        public double MapX { get; set; }
        public double MapY { get; set; }
        public int PlayerInputCount { get; set; }
        public int TranscriptSequence { get; set; }
        public List<string> ParticipantIds { get; set; } = new List<string>();
        public List<string> SelectedIds { get; set; } = new List<string>();
        public List<ReignPartyChatSavedLine> DisplayLines { get; set; } = new List<ReignPartyChatSavedLine>();
        public List<ReignPartyChatSavedLine> ContextLines { get; set; } = new List<ReignPartyChatSavedLine>();

        public string UnavailableReason(string campaign, string player, string location, string room,
            double x, double y, IEnumerable<string> available)
        {
            if (Version != 1 || string.IsNullOrWhiteSpace(CampaignId) || CampaignId != campaign
                || string.IsNullOrWhiteSpace(PlayerId) || PlayerId != player
                || string.IsNullOrWhiteSpace(SourceSessionId) || PlayerInputCount < 0 || TranscriptSequence < 0
                || ParticipantIds == null || ParticipantIds.Count == 0 || ParticipantIds.Any(string.IsNullOrWhiteSpace)
                || SelectedIds == null || SelectedIds.Except(ParticipantIds, StringComparer.OrdinalIgnoreCase).Any()
                || DisplayLines == null || !DisplayLines.Any(l => l != null && (l.Role == "player" || l.Role == "npc"))
                || ContextLines == null || ContextLines.Any(l => l == null))
                return "There is no saved party chat to continue.";
            if (string.IsNullOrWhiteSpace(LocationId) || LocationId != location || RoomId != (room ?? "")
                || !Finite(MapX) || !Finite(MapY) || !Finite(x) || !Finite(y)
                || (LocationId == "overland" && ((MapX - x) * (MapX - x) + (MapY - y) * (MapY - y) > 0.000001d)))
                return "Return to the location of the previous party chat to continue.";
            var present = new HashSet<string>(available ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (ParticipantIds.Any(id => !present.Contains(id)))
                return "All members of the previous party chat must still be present and available.";
            return "";
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class ReignPartyChatSavedLine
    {
        public int Sequence { get; set; }
        public string SessionId { get; set; } = "";
        public string ExchangeId { get; set; } = "";
        public string SpeakerHeroStringId { get; set; } = "";
        public string Speaker { get; set; } = "";
        public string Role { get; set; } = "";
        public string AudienceHeroStringId { get; set; } = "";
        public string Text { get; set; } = "";
        public string InlineImagePath { get; set; } = "";
        public string FullImagePath { get; set; } = "";
    }
}
