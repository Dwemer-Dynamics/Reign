using System;
using Reign.Core.Contracts.Government;

namespace ReignBeta.Government
{
    /// <summary>Save-compatible safety policy, shared with provider-free regression tests.</summary>
    internal static class ReignGovernmentNativeSafety
    {
        internal const int CaptureVersion = 1;

        internal static bool IsDecisionDue(ReignGovernmentBusinessRecord record, double nativeDay)
        {
            if (record == null || nativeDay < 0 || double.IsNaN(nativeDay) || double.IsInfinity(nativeDay)
                || record.DecisionDay < 0 || float.IsNaN(record.DecisionDay) || float.IsInfinity(record.DecisionDay)) return false;
            if (nativeDay >= record.DecisionDay) return true;
            // Saved days are floats; a hearing opened while paused can round above the exact
            // native clock. Only an already-open extraordinary hearing shares that same bucket.
            // Regular sessions and urgent matters deferred to a later session retain exact deadlines.
            return record.IsUrgent && !record.LegacySessionMigration && record.Status == "hearing"
                && record.PresentationDay == record.DecisionDay && (float)nativeDay == record.DecisionDay;
        }

        // Old saves may close the initiating hearing without copying the counterpart's
        // rejection. Derive that outcome only from the linked, identical, effect-free record.
        internal static bool IsRecordedCounterpartRefusal(ReignGovernmentBusinessRecord source,
            ReignGovernmentBusinessRecord response) => source != null && response != null
            && source.Kind == "world_action" && response.Kind == "world_action"
            && source.Status == "decided" && string.IsNullOrEmpty(source.ExecutedOptionId)
            && GovernmentBusinessRules.IsRecordedAuthorizationRefusal(response.Status, response.ExecutedOptionId)
            && !string.IsNullOrEmpty(source.BusinessId)
            && !string.IsNullOrEmpty(source.CounterpartBusinessId)
            && source.CounterpartBusinessId == response.BusinessId
            && response.CounterpartOfBusinessId == source.BusinessId
            && string.IsNullOrEmpty(source.CounterpartOfBusinessId)
            && !string.IsNullOrEmpty(source.KingdomStringId)
            && !string.IsNullOrEmpty(response.KingdomStringId)
            && source.KingdomStringId != response.KingdomStringId
            && !string.IsNullOrEmpty(source.ActionCorrelationId)
            && source.ActionCorrelationId == response.ActionCorrelationId
            && !string.IsNullOrEmpty(source.ActionPayloadJson)
            && string.Equals(source.ActionPayloadJson, response.ActionPayloadJson, StringComparison.Ordinal)
            && !source.ExecutionApplied && !source.NativeEffectStarted && source.NativeEffectInvocationCount == 0
            && string.IsNullOrEmpty(source.NativeQuarantineReason)
            // FinishBusiness finalizes a world-action rejection with ExecutionApplied=true;
            // only the native intent/invocation fields indicate an actual treaty effect.
            && response.ExecutionApplied && !response.NativeEffectStarted && response.NativeEffectInvocationCount == 0
            && string.IsNullOrEmpty(response.NativeQuarantineReason);

        internal static bool AllowNativeIssueDiscovery(bool isHeadman, bool isRuralNotable,
            bool hasCurrentSettlement, bool hasVillageContext, bool isGangLeader, bool hasTownContext) =>
            (!isGangLeader || hasCurrentSettlement && hasTownContext)
            && ((!isHeadman && !isRuralNotable) || !hasCurrentSettlement || hasVillageContext);

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
