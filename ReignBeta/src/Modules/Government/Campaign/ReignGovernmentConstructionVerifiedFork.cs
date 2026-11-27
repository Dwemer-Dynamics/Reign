using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Government.Domain;
using ReignBeta.WorldSimulation.Integration;

namespace ReignBeta.Government.Campaign
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private const int MaxConstructionForkHops = 16;
        private readonly List<(ReignGovernmentResolutionRecord Record, string Evidence, long Revision)>
            _loadedConstructionResolutions = new List<(ReignGovernmentResolutionRecord, string, long)>();

        private void CaptureLoadedConstructionResolutions()
        {
            _loadedConstructionResolutions.Clear();
            if (_resolutions == null) return;
            foreach (var record in _resolutions)
            {
                if (record != null && HasActiveConstructionScope(record, record.TargetSettlementStringId))
                    _loadedConstructionResolutions.Add((record, record.EvidenceJson, record.Revision));
            }
        }

        internal bool HasLoadedConstructionForTimeline(string campaign, string timeline) =>
            _loadedConstructionResolutions.Any(row => ReadLoadedConstruction(
                row.Record, row.Evidence, row.Revision, campaign, timeline, out _) != null);

        internal void AdoptLoadedConstructionFork(ReignWorldHistoryVerifiedFork fork)
        {
            if (fork == null)
            {
                _loadedConstructionResolutions.Clear();
                return;
            }

            foreach (var row in _loadedConstructionResolutions)
            {
                var terms = ReadLoadedConstruction(row.Record, row.Evidence, row.Revision,
                    fork.CampaignId, fork.SourceTimelineId, out var offer);
                if (terms == null || terms.AcceptedDay > fork.CheckpointDay) continue;
                if (!TryReadConstructionForks(offer, terms, out var chain)
                    || chain.Count >= MaxConstructionForkHops || chain.Contains(fork.ChildTimelineId)) continue;

                if (offer["observation"] != null)
                {
                    var observation = ReadConstructionObservation(row.Record, offer);
                    if (observation == null || observation.ObservedDay > fork.CheckpointDay) continue;
                }

                var hops = (JArray)offer["verifiedForks"] ?? new JArray();
                var nativeEdge = new JObject
                {
                    ["sourceTimelineId"] = fork.SourceTimelineId,
                    ["childTimelineId"] = fork.ChildTimelineId,
                    ["savePointId"] = fork.SavePointId,
                    ["sourceSequence"] = fork.SourceSequence,
                    ["sourceHeadEventId"] = fork.SourceHeadEventId,
                    ["checkpointDay"] = fork.CheckpointDay
                };
                if (hops.Count > 0 && ReadCheckpoint((JObject)hops.Last) > fork.CheckpointDay) continue;
                hops.Add(nativeEdge);
                offer["verifiedForks"] = hops;
                row.Record.EvidenceJson = offer.Parent.Parent.ToString(Formatting.None);
                row.Record.Revision++;
            }
            _loadedConstructionResolutions.Clear();
        }

        private ReignGovernmentConstructionTerms ReadLoadedConstruction(ReignGovernmentResolutionRecord record,
            string loadedEvidence, long loadedRevision, string campaign, string timeline, out JObject offer)
        {
            offer = null;
            if (record.Revision != loadedRevision || record.EvidenceJson != loadedEvidence
                || !HasActiveConstructionScope(record, record.TargetSettlementStringId)
                || string.IsNullOrWhiteSpace(record.TargetSettlementStringId)
                || !ReferenceEquals(FindUniqueSeasonalResolution(record.ResolutionId), record)) return null;
            offer = ReadConstructionOffer(record, ReadResolutionEvidence(record));
            var effectiveTerms = ReadEffectiveConstructionTerms(record, offer);
            return effectiveTerms?.CampaignId == campaign && effectiveTerms.TimelineId == timeline
                && (offer["observation"] == null || ReadConstructionObservation(record, offer) != null)
                ? ReadConstructionTerms(record, offer) : null;
        }

        private static ReignGovernmentConstructionTerms ReadEffectiveConstructionTerms(
            ReignGovernmentResolutionRecord record, JObject offer)
        {
            var terms = ReadConstructionTerms(record, offer);
            return terms != null && TryReadConstructionForks(offer, terms, out var chain)
                ? terms.ForTimeline(chain[chain.Count - 1]) : null;
        }

        private static ReignGovernmentConstructionObservation ReadConstructionObservation(
            ReignGovernmentResolutionRecord record, JObject offer)
        {
            if (!(offer?["observation"] is JObject saved)) return null;
            var original = ReadConstructionTerms(record, offer);
            if (original == null || !TryReadConstructionForks(offer, original, out var chain)) return null;
            string timeline = ReadConstructionString(saved["terms"] as JObject, "timelineId");
            int index = chain.IndexOf(timeline);
            if (index < 0) return null;

            var observation = ReignGovernmentConstructionObservation.Read(saved, original.ForTimeline(timeline));
            if (observation == null) return null;
            if (offer["verifiedForks"] is JArray hops
                && ((index > 0 && observation.ObservedDay < ReadCheckpoint((JObject)hops[index - 1]))
                    || (index < hops.Count && observation.ObservedDay > ReadCheckpoint((JObject)hops[index]))))
                return null;
            return observation;
        }

        private static bool TryReadConstructionForks(JObject offer, ReignGovernmentConstructionTerms original,
            out List<string> chain)
        {
            chain = new List<string> { original.TimelineId };
            if (offer["verifiedForks"] == null) return true;
            if (!(offer["verifiedForks"] is JArray hops) || hops.Count == 0 || hops.Count > MaxConstructionForkHops)
                return false;

            double previousCheckpoint = original.AcceptedDay;
            foreach (JToken token in hops)
            {
                if (!(token is JObject hop) || hop.Count != 6
                    || ReadConstructionString(hop, "sourceTimelineId") != chain[chain.Count - 1]) return false;
                string child = ReadConstructionString(hop, "childTimelineId");
                double checkpoint = ReadCheckpoint(hop);
                if (!ReignWorldHistoryVerifiedFork.IsChildTimelineId(child) || chain.Contains(child)
                    || string.IsNullOrWhiteSpace(ReadConstructionString(hop, "savePointId"))
                    || string.IsNullOrWhiteSpace(ReadConstructionString(hop, "sourceHeadEventId"))
                    || hop["sourceSequence"]?.Type != JTokenType.Integer || !long.TryParse(hop["sourceSequence"].ToString(), out long sequence)
                    || sequence <= 0 || double.IsNaN(checkpoint) || double.IsInfinity(checkpoint)
                    || checkpoint < previousCheckpoint) return false;
                chain.Add(child);
                previousCheckpoint = checkpoint;
            }
            return true;
        }

        private static double ReadCheckpoint(JObject row) =>
            row["checkpointDay"]?.Type == JTokenType.Float ? row.Value<double>("checkpointDay") : double.NaN;
    }
}
