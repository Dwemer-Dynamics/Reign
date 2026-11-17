using Reign.Core.Contracts.Actions;

namespace ReignBeta.Actions
{
    // Observed terminal receipts never permit the executor to issue an order again.
    // Mutual cancellation is distinct from completed fulfillment.
    internal static class ReignCompletedOrderPolicy
    {
        internal const string StaleAuthorizationFailure = "This action does not match an authorized saved agreement step.";

        // Durable hold for an exact admitted action whose immediate native execution threw
        // after it entered Executing and before any result was recorded. The saved status
        // stays Executing; this exact FailureReason is the authority, and ExecuteAfterDay is
        // set to float.MaxValue only to keep it out of ordinary due selection. Nothing
        // settles or retries a held record automatically; exact reconciliation is separate.
        internal const string UncertainExecutionMarker =
            "Native execution was interrupted; whether it changed the world is unknown, and it will not be retried automatically.";
        internal const string UncertainExecutionResultCode = "native_outcome_unknown";

        internal static bool IsUncertainExecutionHeld(string worldActionStatus, string failureReason) =>
            worldActionStatus == "Executing" && failureReason == UncertainExecutionMarker;

        // statusAfterThrow is the exact admitted record's status once its immediate
        // execution threw. Accepted means the executor never started; a terminal status or
        // a result recorded during this attempt is stronger evidence and is kept as is.
        internal static bool ShouldHoldAfterThrow(string statusAfterThrow, bool resultRecordedThisAttempt) =>
            statusAfterThrow == "Executing" && !resultRecordedThisAttempt;

        internal static bool CanReconcileCancellation(AgreementJournal journal, AgreementStep step,
            bool capturedRecordMatches, bool installmentApplied, string nativeOrderStatus, string worldActionStatus,
            string failureReason)
        {
            var a = journal?.Agreement;
            return !IsUncertainExecutionHeld(worldActionStatus, failureReason)
                && journal != null && a != null && step != null && journal.Steps != null && journal.Steps.Contains(step)
                && capturedRecordMatches && installmentApplied && nativeOrderStatus == "cancelled"
                && (worldActionStatus == "Accepted" || worldActionStatus == "Executing" || worldActionStatus == "Cancelled")
                && step.Reserved && step.Started && !step.Completed && !step.Returned
                && journal.CommittedHash == a.TermsHash()
                && (a.HasRequiredConsents || journal.OperatorAuthorized && a.Origin == ActionOrigin.Console)
                && (a.State == AgreementState.Waiting || a.State == AgreementState.InTransit || a.State == AgreementState.Cancelled);
        }

        internal static bool CanRestoreInProgress(AgreementJournal journal, AgreementStep step, int installment,
            bool capturedRecordMatches, bool installmentApplied, string nativeOrderStatus, string worldActionStatus)
        {
            var agreement = journal?.Agreement;
            return journal != null && agreement != null && step != null && journal.Steps != null
                && journal.Steps.Contains(step) && capturedRecordMatches && installmentApplied
                && worldActionStatus == "Completed"
                && (nativeOrderStatus == "accepted" || nativeOrderStatus == "active" || nativeOrderStatus == "awaiting_guidance")
                && step.Reserved && !step.Returned && step.Started && !step.Completed
                && installment >= 0 && installment == step.Installment && step.FulfilledInstallments <= installment
                && journal.CommittedHash == agreement.TermsHash()
                && (agreement.HasRequiredConsents || journal.OperatorAuthorized && agreement.Origin == ActionOrigin.Console)
                && (agreement.State == AgreementState.Waiting || agreement.State == AgreementState.InTransit);
        }

        internal static bool CanReconcile(AgreementJournal journal, AgreementStep step, int installment,
            bool capturedRecordMatches, bool installmentApplied, string nativeOrderStatus,
            string worldActionStatus, string failureReason)
        {
            var agreement = journal?.Agreement;
            if (journal == null || agreement == null || step == null || journal.Steps == null || !journal.Steps.Contains(step)
                || !capturedRecordMatches || !installmentApplied || nativeOrderStatus != "completed"
                || !step.Reserved || step.Returned || installment < 0
                || !(step.Started && installment == step.Installment || installment < step.FulfilledInstallments)
                || journal.CommittedHash != agreement.TermsHash()
                || !(agreement.HasRequiredConsents
                    || journal.OperatorAuthorized && agreement.Origin == ActionOrigin.Console)
                || (agreement.State != AgreementState.Waiting && agreement.State != AgreementState.InTransit
                    && agreement.State != AgreementState.Completed)
                || IsUncertainExecutionHeld(worldActionStatus, failureReason)) return false;

            return worldActionStatus == "Accepted" || worldActionStatus == "Executing"
                || worldActionStatus == "Failed" && failureReason == StaleAuthorizationFailure;
        }
    }
}
