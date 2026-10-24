using System;

namespace ReignBeta.Campaign
{
    // Process-local ownership only. This never changes a campaign or grants a test lease.
    internal sealed class ReignPassiveWorldAdvanceBoundary
    {
        private readonly object _campaign;
        private readonly string _runId;
        internal double TargetDay { get; }
        internal DateTime DeadlineUtc { get; }
        internal bool TargetReached { get; private set; }
        internal bool TimedOut { get; private set; }

        internal ReignPassiveWorldAdvanceBoundary(object campaign, string runId,
            double targetDay, DateTime deadlineUtc)
        {
            if (campaign == null) throw new ArgumentNullException(nameof(campaign));
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("An owned run is required.", nameof(runId));
            if (double.IsNaN(targetDay) || double.IsInfinity(targetDay))
                throw new ArgumentOutOfRangeException(nameof(targetDay));
            _campaign = campaign;
            _runId = runId;
            TargetDay = targetDay;
            DeadlineUtc = deadlineUtc;
        }

        internal bool Owns(object campaign, string runId) => ReferenceEquals(_campaign, campaign)
            && string.Equals(_runId, runId, StringComparison.Ordinal);

        internal bool Observe(object campaign, string runId, double worldDay, DateTime utcNow)
        {
            if (!Owns(campaign, runId)) return false;
            if (!TargetReached && !TimedOut)
            {
                TargetReached = worldDay + 0.000001d >= TargetDay;
                TimedOut = !TargetReached && utcNow >= DeadlineUtc;
            }
            return TargetReached || TimedOut;
        }
    }
}
