using System;
using Newtonsoft.Json.Linq;
using ReignBeta.Persistence.Integration;

namespace ReignBeta.WorldSimulation.Integration
{
    internal sealed class ReignWorldHistoryVerifiedFork
    {
        internal string CampaignId { get; }
        internal string SourceTimelineId { get; }
        internal string ChildTimelineId { get; }
        internal string SavePointId { get; }
        internal long SourceSequence { get; }
        internal string SourceHeadEventId { get; }
        internal double CheckpointDay { get; }

        private ReignWorldHistoryVerifiedFork(string campaignId, string sourceTimelineId, string childTimelineId,
            string savePointId, long sourceSequence, string sourceHeadEventId, double checkpointDay)
        {
            CampaignId = campaignId;
            SourceTimelineId = sourceTimelineId;
            ChildTimelineId = childTimelineId;
            SavePointId = savePointId;
            SourceSequence = sourceSequence;
            SourceHeadEventId = sourceHeadEventId;
            CheckpointDay = checkpointDay;
        }

        internal static ReignWorldHistoryVerifiedFork ReadOpenReply(JObject reply,
            ReignSaveSyncCoordinator.VerifiedLoad load, string source, long sequence, string head)
        {
            string opened = ExactString(reply, "timelineId");
            if (load == null || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(head) || sequence <= 0
                || !IsFlag(reply, "ok", true) || !IsFlag(reply, "historyMissing", false)
                || ExactString(reply, "campaignId") != load.CampaignId
                || ExactString(reply, "sharedActivityGeneration") != load.RestoreGeneration
                || !IsInteger(reply, "savedSequence", sequence))
                throw new InvalidOperationException("The timeline reply did not match the current verified save cursor.");

            if (opened == source && IsFlag(reply, "branched", false) && reply["verifiedFork"] == null)
                return null;

            if (!IsChildTimelineId(opened) || opened == source || !IsFlag(reply, "branched", true)
                || ExactString(reply, "parentTimelineId") != source || !(reply["verifiedFork"] is JObject proof)
                || proof.Count != 11 || !IsInteger(proof, "schemaVersion", 1)
                || ExactString(proof, "campaignId") != load.CampaignId
                || ExactString(proof, "savePointId") != load.SavePointId
                || ExactString(proof, "loadSessionId") != load.LoadSessionId
                || ExactString(proof, "restoreGeneration") != load.RestoreGeneration
                || ExactString(proof, "sourceTimelineId") != source || !IsInteger(proof, "sourceSequence", sequence)
                || ExactString(proof, "sourceHeadEventId") != head || ExactString(proof, "childTimelineId") != opened
                || !IsFlag(proof, "nativeMembershipVerified", false)
                || !(proof["checkpointDay"]?.Type == JTokenType.Float || proof["checkpointDay"]?.Type == JTokenType.Integer))
                throw new InvalidOperationException("The timeline reply did not prove a verified child.");

            double checkpoint = proof.Value<double>("checkpointDay");
            if (double.IsNaN(checkpoint) || double.IsInfinity(checkpoint) || checkpoint < 0d)
                throw new InvalidOperationException("The verified child omitted a valid checkpoint day.");
            return new ReignWorldHistoryVerifiedFork(load.CampaignId, source, opened, load.SavePointId,
                sequence, head, checkpoint);
        }

        internal static bool IsChildTimelineId(string timeline) => timeline?.Length == 46
            && timeline.StartsWith("verified_fork_", StringComparison.Ordinal)
            && Guid.TryParseExact(timeline.Substring(14), "N", out _);

        private static string ExactString(JObject value, string key) =>
            value?[key]?.Type == JTokenType.String ? value.Value<string>(key) : null;

        private static bool IsFlag(JObject value, string key, bool expected) =>
            value?[key]?.Type == JTokenType.Boolean && value.Value<bool>(key) == expected;

        private static bool IsInteger(JObject value, string key, long expected) =>
            value?[key]?.Type == JTokenType.Integer
            && long.TryParse(value[key].ToString(), out long result) && result == expected;
    }
}
