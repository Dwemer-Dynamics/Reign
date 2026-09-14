namespace ReignBeta.Events
{
    public class SocialEventPhase
    {
        public string PhaseId { get; }
        public string Title { get; }
        public string SettingSummary { get; }
        public int SoftAdvanceScore { get; }
        public int HardAdvanceScore { get; }
        public float MinorIncidentChance { get; }
        public float ModerateIncidentChance { get; }
        public float MajorIncidentChance { get; }

        public SocialEventPhase(
            string phaseId,
            string title,
            string settingSummary,
            int softAdvanceScore,
            int hardAdvanceScore,
            float minorIncidentChance,
            float moderateIncidentChance,
            float majorIncidentChance)
        {
            PhaseId = phaseId;
            Title = title;
            SettingSummary = settingSummary;
            SoftAdvanceScore = softAdvanceScore;
            HardAdvanceScore = hardAdvanceScore;
            MinorIncidentChance = minorIncidentChance;
            ModerateIncidentChance = moderateIncidentChance;
            MajorIncidentChance = majorIncidentChance;
        }
    }
}
