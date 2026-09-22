using System.Collections.Generic;

namespace ReignBeta.Events
{
    public class SocialEventTemplate
    {
        public string TemplateId { get; }
        public string DisplayName { get; }
        public string AnnouncementNoun { get; }
        public float OpportunityDays { get; }
        public int PhaseChoiceSeconds { get; }
        public IReadOnlyList<SocialEventPhase> Phases { get; }

        public SocialEventTemplate(
            string templateId,
            string displayName,
            string announcementNoun,
            float opportunityDays,
            int phaseChoiceSeconds,
            IReadOnlyList<SocialEventPhase> phases)
        {
            TemplateId = templateId;
            DisplayName = displayName;
            AnnouncementNoun = announcementNoun;
            OpportunityDays = opportunityDays;
            PhaseChoiceSeconds = phaseChoiceSeconds;
            Phases = phases;
        }
    }
}
