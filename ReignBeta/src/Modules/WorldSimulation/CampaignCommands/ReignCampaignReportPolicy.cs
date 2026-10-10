using System;

namespace ReignBeta.World
{
    public static class ReignCampaignReportPolicy
    {
        public static bool CanFinishScouting(string objective, string stage, bool pending,
            string reportId, string deliveredId) =>
            objective == "scout_report" && stage == "awaiting_report" && !pending
            && !string.IsNullOrWhiteSpace(reportId)
            && string.Equals(reportId, deliveredId, StringComparison.Ordinal);

        public static string LetterSource(bool awaitingGuidance) => awaitingGuidance
            ? "campaign_command_urgent_report" : "campaign_command_report";

        public static bool LetterDelivered(bool ok, string letterId, string status) => ok
            && !string.IsNullOrWhiteSpace(letterId) && (status == "delivered" || status == "read");

        public static string Body(string commander, string objective, string destination,
            string reason, string status, bool awaitingGuidance)
        {
            string subject = (objective ?? "campaign order").Replace('_', ' ');
            string place = string.IsNullOrWhiteSpace(destination) ? string.Empty : " around " + destination;
            string closing = awaitingGuidance
                ? "The order is paused for guidance. If no answer arrives within 12 campaign hours, I will decide according to duty, loyalty, and conditions."
                : status == "completed" ? "The order is complete."
                : status == "failed" ? "The order ended without completion."
                : "Here is my report.";
            return (string.IsNullOrWhiteSpace(commander) ? "Your commander" : commander)
                + " reports on " + subject + place + ":\n\n" + (reason ?? string.Empty)
                + "\n\n" + closing;
        }
    }
}
