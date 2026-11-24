using System;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Government.Campaign
{
    // Accepted terms are native-save facts. The transient load owner belongs to
    // the observer, not to this durable receipt.
    internal sealed class ReignGovernmentConstructionTerms
    {
        internal readonly string ResolutionId;
        internal readonly string CampaignId;
        internal readonly string TimelineId;
        internal readonly string KingdomId;
        internal readonly string SettlementId;
        internal readonly string PreviousProjectId;
        internal readonly string ProjectId;
        internal readonly int LevelAtAcceptance;
        internal readonly int LevelCapAtAcceptance;
        internal readonly double AcceptedDay;
        internal readonly double DueDay;

        internal ReignGovernmentConstructionTerms(string resolutionId, string campaignId, string timelineId,
            string kingdomId, string settlementId, string previousProjectId, string projectId,
            int levelAtAcceptance, int levelCapAtAcceptance, double acceptedDay, double dueDay)
        {
            ResolutionId = resolutionId;
            CampaignId = campaignId;
            TimelineId = timelineId;
            KingdomId = kingdomId;
            SettlementId = settlementId;
            PreviousProjectId = previousProjectId;
            ProjectId = projectId;
            LevelAtAcceptance = levelAtAcceptance;
            LevelCapAtAcceptance = levelCapAtAcceptance;
            AcceptedDay = acceptedDay;
            DueDay = dueDay;
        }

        internal bool IsValid => Present(ResolutionId) && Present(CampaignId) && Present(TimelineId)
            && Present(KingdomId) && Present(SettlementId) && Present(PreviousProjectId) && Present(ProjectId)
            && PreviousProjectId != ProjectId && LevelAtAcceptance >= 0
            && LevelAtAcceptance < LevelCapAtAcceptance && AcceptedDay >= 0 && AcceptedDay < DueDay
            && InOrder(AcceptedDay, DueDay);

        internal bool IsWithinWindow(double day) => InOrder(AcceptedDay, day) && InOrder(day, DueDay);

        internal bool Matches(ReignGovernmentConstructionTerms other) => other != null
            && ResolutionId == other.ResolutionId && CampaignId == other.CampaignId && TimelineId == other.TimelineId
            && KingdomId == other.KingdomId && SettlementId == other.SettlementId
            && PreviousProjectId == other.PreviousProjectId && ProjectId == other.ProjectId
            && LevelAtAcceptance == other.LevelAtAcceptance && LevelCapAtAcceptance == other.LevelCapAtAcceptance
            && AcceptedDay == other.AcceptedDay && DueDay == other.DueDay;

        internal JObject ToJson() => new JObject
        {
            ["resolutionId"] = ResolutionId, ["campaignId"] = CampaignId, ["timelineId"] = TimelineId,
            ["kingdomId"] = KingdomId, ["settlementId"] = SettlementId, ["previousProjectId"] = PreviousProjectId,
            ["projectId"] = ProjectId, ["levelAtAcceptance"] = LevelAtAcceptance,
            ["levelCapAtAcceptance"] = LevelCapAtAcceptance, ["acceptedDay"] = AcceptedDay, ["dueDay"] = DueDay
        };

        private static bool Present(string value) => !string.IsNullOrWhiteSpace(value);

        private static bool InOrder(double first, double second) =>
            !double.IsNaN(first) && !double.IsInfinity(first)
            && !double.IsNaN(second) && !double.IsInfinity(second) && first <= second;
    }

    internal sealed class ReignGovernmentConstructionObservation
    {
        private const int SchemaVersion = 1;
        internal const string PriorityChanged = "priority_changed";
        internal const string LevelCompleted = "level_completed";
        internal readonly ReignGovernmentConstructionTerms Terms;
        internal readonly string Transition;
        internal readonly double ObservedDay;
        internal readonly int ObservedLevel;

        private ReignGovernmentConstructionObservation(ReignGovernmentConstructionTerms terms, string transition,
            double observedDay, int observedLevel)
        {
            Terms = terms;
            Transition = transition;
            ObservedDay = observedDay;
            ObservedLevel = observedLevel;
        }

        internal static ReignGovernmentConstructionObservation Create(ReignGovernmentConstructionTerms terms,
            string transition, double day, int level)
        {
            if (terms?.IsValid != true || !terms.IsWithinWindow(day) || level > terms.LevelCapAtAcceptance) return null;
            if (transition == PriorityChanged && level == terms.LevelAtAcceptance
                || transition == LevelCompleted && level > terms.LevelAtAcceptance)
                return new ReignGovernmentConstructionObservation(terms, transition, day, level);
            return null;
        }

        internal JObject ToJson() => new JObject
        {
            ["schemaVersion"] = SchemaVersion, ["terms"] = Terms.ToJson(), ["transition"] = Transition,
            ["observedDay"] = ObservedDay, ["observedLevel"] = ObservedLevel
        };

        internal static ReignGovernmentConstructionObservation Read(JToken token,
            ReignGovernmentConstructionTerms expectedTerms)
        {
            if (!(token is JObject row) || expectedTerms?.IsValid != true || row.Count != 5
                || !JToken.DeepEquals(row["schemaVersion"], new JValue(SchemaVersion))
                || !JToken.DeepEquals(row["terms"], expectedTerms.ToJson())
                || row["transition"]?.Type != JTokenType.String
                || row["observedDay"]?.Type != JTokenType.Float
                || row["observedLevel"]?.Type != JTokenType.Integer)
                return null;
            try
            {
                return Create(expectedTerms, (string)row["transition"],
                    (double)row["observedDay"], (int)row["observedLevel"]);
            }
            catch (Exception ex) when (ex is OverflowException || ex is FormatException)
            {
                return null;
            }
        }
    }
}
