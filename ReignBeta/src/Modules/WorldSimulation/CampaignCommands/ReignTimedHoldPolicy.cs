using System;

namespace ReignBeta.World
{
    internal static class ReignTimedHoldPolicy
    {
        internal static bool OwnsNativeDecisions(string objective, string authorityMode,
            string status, bool awaitingGuidance, bool enabled, bool eligibleIndependentParty) =>
            enabled && eligibleIndependentParty && !awaitingGuidance
            && objective == "timed_hold"
            && (authorityMode == "explicit_agreement" || authorityMode == "operator")
            && (status == "accepted" || status == "active");

        internal static double Deadline(double now, double durationHours, bool requiresArrival,
            bool atDestination, double savedDeadline)
        {
            if (double.IsNaN(durationHours) || double.IsInfinity(durationHours) || durationHours <= 0d)
                throw new ArgumentOutOfRangeException(nameof(durationHours));
            if (requiresArrival && !atDestination) return 0d;
            return savedDeadline > 0d ? savedDeadline : now + durationHours / 24d;
        }
    }
}
