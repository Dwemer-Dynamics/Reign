using System;

namespace ReignBeta.Spymaster.Campaign
{
    public sealed class ReignForeignAgentRecord
    {
        public string AgentHeroStringId = string.Empty;
        public string SponsorKingdomStringId = string.Empty;
        public string SettlementStringId = string.Empty;
        public bool IsNotable;
        public bool Activated;
        public bool Exposed;
        public string AssignmentId = string.Empty;
        public string AssignmentTimelineId = string.Empty;
        public bool ExposurePending;
        public float ExposedDay = -1f;
        public float RecruitedDay;
        public float NextActionDay;
    }

    public sealed class ReignForeignAgentActionRecord
    {
        public string ActionId = string.Empty;
        public string AgentHeroStringId = string.Empty;
        public string SponsorKingdomStringId = string.Empty;
        public string SettlementStringId = string.Empty;
        public string EffectType = string.Empty;
        public float WorldDay;
        public float DurationDays;
        public float Magnitude;
        public bool Detected;
        public bool SponsorAttributed;
        public string Summary = string.Empty;
        public string AssignmentCampaignId = string.Empty;
        public string AssignmentTimelineId = string.Empty;
        public string AssignmentId = string.Empty;
        public string TargetKingdomStringId = string.Empty;

        internal string PublicSabotageArrestEvidenceId(string campaignId, string timelineId,
            string targetKingdomId, string accusedId, ReignForeignAgentRecord agent)
        {
            if (agent?.Exposed != true || agent.ExposurePending || !Detected || !SponsorAttributed
                || string.IsNullOrWhiteSpace(campaignId) || AssignmentCampaignId != campaignId
                || string.IsNullOrWhiteSpace(timelineId) || AssignmentTimelineId != timelineId
                || agent.AssignmentTimelineId != timelineId || string.IsNullOrWhiteSpace(AssignmentId)
                || agent.AssignmentId != AssignmentId || string.IsNullOrWhiteSpace(accusedId)
                || agent.AgentHeroStringId != accusedId || AgentHeroStringId != accusedId
                || string.IsNullOrWhiteSpace(targetKingdomId) || TargetKingdomStringId != targetKingdomId
                || string.IsNullOrWhiteSpace(SponsorKingdomStringId) || SponsorKingdomStringId == targetKingdomId
                || agent.SponsorKingdomStringId != SponsorKingdomStringId || string.IsNullOrWhiteSpace(SettlementStringId)
                || agent.SettlementStringId != SettlementStringId
                || string.IsNullOrWhiteSpace(ActionId) || ActionId.Contains(",")
                || !(Magnitude < 0f) || float.IsInfinity(Magnitude)
                || EffectType is not ("food" or "construction" or "security" or "loyalty"))
                return string.Empty;

            return "foreign_agent_sabotage:" + ActionId;
        }
    }
}
