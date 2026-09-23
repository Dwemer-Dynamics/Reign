using System;

namespace ReignBeta.Government
{
    /// <summary>Save-compatible safety policy, shared with provider-free regression tests.</summary>
    internal static class ReignGovernmentNativeSafety
    {
        internal const int CaptureVersion = 1;

        internal static bool IsTrustedCapture(ReignGovernmentBusinessRecord record) => record != null
            && record.NativeCaptureVersion == CaptureVersion
            && !string.IsNullOrWhiteSpace(record.NativeDecisionType)
            && !string.IsNullOrWhiteSpace(record.NativeFingerprint)
            && (record.NativeCaptureSource == "proposal" || record.NativeCaptureSource == "election"
                || record.NativeCaptureSource == "outcome" || record.NativeCaptureSource == "unresolved"
                || record.NativeCaptureSource == "offer" || record.NativeCaptureSource == "reign");

        internal static string ExecutionBlock(ReignGovernmentBusinessRecord record, bool patchesReady,
            string currentFingerprint, string currentOwnerClanId)
        {
            if (!IsTrustedCapture(record))
                return "This saved hearing predates verified native interception. It is held for review; no native effect will be replayed.";
            if (!string.IsNullOrEmpty(record.NativeQuarantineReason)) return record.NativeQuarantineReason;
            if (record.NativeEffectStarted || record.ExecutionApplied)
                return "The native effect has already started or completed. Its outcome must be reviewed before any further action.";
            if (!patchesReady) return "Government native interception is unavailable. This hearing is held for review.";
            if (!string.Equals(record.NativeFingerprint, currentFingerprint, StringComparison.Ordinal))
                return "The saved native decision identity or terms changed. This hearing is held for review.";
            if ((record.Kind == "fief_allocation" || record.Kind == "fief_reassignment")
                && (string.IsNullOrEmpty(record.NativeCapturedOwnerClanId)
                    || !string.Equals(record.NativeCapturedOwnerClanId, currentOwnerClanId, StringComparison.Ordinal)))
                return "The settlement owner changed after this hearing was captured. The old award will not be applied.";
            return string.Empty;
        }

        internal static bool TryBeginEffect(ReignGovernmentBusinessRecord record)
        {
            if (!IsTrustedCapture(record) || record.NativeEffectStarted || record.ExecutionApplied
                || !string.IsNullOrEmpty(record.NativeQuarantineReason)) return false;
            // Persist intent before entering native code. An uncertain partial effect must never be retried.
            record.NativeEffectStarted = true;
            return true;
        }

        internal static bool TryConsumeEffectPermit(ReignGovernmentBusinessRecord record, ref bool permitAvailable)
        {
            if (!permitAvailable || record == null || !record.NativeEffectStarted
                || record.NativeEffectInvocationCount != 0) return false;
            permitAvailable = false;
            record.NativeEffectInvocationCount++;
            return true;
        }
    }
}
