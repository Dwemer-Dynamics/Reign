using System;

namespace ReignBeta.Integration
{
    internal static class ReignWorldHistoryFlushPolicy
    {
        internal const int NormalUploadBatchSize = 1000;
        internal const int UrgentUploadBatchSize = 5000;
        internal const int NormalMinimumBatchSize = 128;
        internal const int NormalMaximumCoalesceDelayMs = 2000;
        internal const int NormalCoalescePollDelayMs = 100;

        internal static TimeSpan CriticalSaveBoundaryBudget =>
            TimeSpan.FromSeconds(30);

        internal static int UploadBatchSize(bool urgent)
        {
            return urgent ? UrgentUploadBatchSize : NormalUploadBatchSize;
        }

        internal static bool ShouldCoalesceNormalUpload(
            bool urgent, int stagedCount, long elapsedMilliseconds)
        {
            return !urgent
                && stagedCount > 0
                && stagedCount < NormalMinimumBatchSize
                && elapsedMilliseconds < NormalMaximumCoalesceDelayMs;
        }

        internal static bool IsSequenceWithinBoundary(
            long? sequence,
            long maximumSequence)
        {
            // An event without a sequence cannot be proven to fall after the
            // save boundary, so retain the conservative behavior and wait for it.
            return !sequence.HasValue || sequence.Value <= maximumSequence;
        }

        internal static bool ShouldPersistEvent(string eventType)
        {
            return !string.Equals(
                eventType,
                "unattributed_state_change",
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ShouldKickWorker(
            bool memoryPending,
            bool diskPending,
            bool workerRunning)
        {
            return (memoryPending || diskPending) && !workerRunning;
        }
    }
}
