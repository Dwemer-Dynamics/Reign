using System;

namespace ReignBeta.World
{
    public sealed class ReignDiplomacyAnnouncement
    {
        public string EventId = string.Empty;
        public string ActionId = string.Empty;
        public string Title = string.Empty;
        public string Outcome = string.Empty;
        public string Summary = string.Empty;
        public string Terms = string.Empty;
        public string ActorHeroStringId = string.Empty;
        public string ActorName = string.Empty;
        public string ActorKingdomName = string.Empty;
        public string ActorKingdomStringId = string.Empty;
        public string ActorPublicReason = string.Empty;
        public string TargetHeroStringId = string.Empty;
        public string TargetName = string.Empty;
        public string TargetKingdomName = string.Empty;
        public string TargetKingdomStringId = string.Empty;
        public string TargetPublicReason = string.Empty;
        public float WorldDay;
        public bool Accepted;

        public bool IsValid => !string.IsNullOrWhiteSpace(EventId) && !string.IsNullOrWhiteSpace(Title);
    }
}
