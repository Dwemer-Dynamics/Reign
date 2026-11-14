#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;

namespace ReignBeta.Government
{
    // Finish a finite cache sweep before restarting its refresh timer. Large
    // populations must not make early batches expire while later ones still wait.
    internal sealed class GovernmentPoliticalRefreshCycle
    {
        private DateTime _startedUtc;
        private DateTime _nextUtc;
        private int? _interval;
        private bool _active;
        internal bool Active => _active;
        internal DateTime StartedUtc => _startedUtc;

        // An active finite sweep is already bounded by one wire batch and the
        // native capture budget. Do not add idle time between successful batches.
        // The next selection census completes the sweep and retains its five-minute timer.
        internal DateTime RetryAfterBatch(DateTime utc, bool succeeded, bool acknowledgedProgress = false) =>
            utc.AddSeconds(!succeeded ? 30d : _active && acknowledgedProgress ? 0d : .75d);

        // A repeated valid response may still carry unseen evidence. Only its
        // first acknowledgement in this sweep advances the finite cohort.
        internal bool AcknowledgementAdvancesSweep(DateTime? previousSuccessfulSync) =>
            _active && (!previousSuccessfulSync.HasValue || previousSuccessfulSync.Value < _startedUtc);

        internal void Observe(DateTime utc, int interval)
        {
            if (_interval == interval && (_active || utc < _nextUtc)) return;
            _interval = interval;
            _startedUtc = utc;
            _active = true;
        }

        internal bool NeedsRefresh(DateTime? lastSuccessfulSync, bool required = false) =>
            required || !lastSuccessfulSync.HasValue || _active && lastSuccessfulSync.Value < _startedUtc;

        internal bool CompleteIfAcknowledged(IEnumerable<DateTime?> livingAcknowledgements, DateTime utc)
        {
            if (!_active || livingAcknowledgements.Any(last => !last.HasValue || last.Value < _startedUtc)) return false;
            _active = false;
            _nextUtc = utc.AddMinutes(5);
            return true;
        }
    }

    // Managed response preparation runs on a worker. Applied client profiles are
    // owned by the campaign and replaced as complete issued revisions; callers
    // receive JSON copies. Keep native state and object checks in the commit.
    internal sealed class GovernmentPoliticalPreparedProfile
    {
        internal GovernmentPoliticalProfile Profile { get; }
        internal bool ChangedFromSent { get; }
        internal GovernmentPoliticalPreparedProfile(GovernmentPoliticalProfile profile, bool changed)
        { Profile = profile; ChangedFromSent = changed; }
    }

    internal static class GovernmentPoliticalBatchPreparation
    {
        internal static IReadOnlyList<GovernmentPoliticalPreparedProfile> Prepare(JObject payload, JObject response)
        {
            var rows = response["profiles"] as JArray ?? new JArray();
            if (rows.Count > 64) throw new InvalidOperationException("Political synchronization returned an oversized batch.");
            string? campaign = response.Value<string>("campaignId");
            string? timeline = response.Value<string>("timelineId");
            var sent = (payload["heroes"] as JArray ?? new JArray()).OfType<JObject>()
                .GroupBy(x => x.Value<string>("heroStringId") ?? "", StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var prepared = new List<GovernmentPoliticalPreparedProfile>(rows.Count);
            foreach (var item in rows.OfType<JObject>())
            {
                var profile = item.ToObject<GovernmentPoliticalProfile>();
                if (profile == null || !GovernmentPoliticalBeliefs.Valid(profile) || !sent.TryGetValue(profile.HeroId, out JObject? source)
                    || source == null
                    || profile.CampaignId != campaign || profile.TimelineId != timeline) continue;
                // Normalize through the DTO just as the former native commit did.
                bool changed = !JToken.DeepEquals(source["appliedPoliticalProfile"], JObject.FromObject(profile));
                prepared.Add(new GovernmentPoliticalPreparedProfile(profile, changed));
            }
            return prepared;
        }

        internal static bool BaseStillCurrent(GovernmentPoliticalProfile? current,
            GovernmentPoliticalProfile? captured, long capturedRevision) =>
            ReferenceEquals(current, captured) && (current == null || current.Revision == capturedRevision);
    }
}
