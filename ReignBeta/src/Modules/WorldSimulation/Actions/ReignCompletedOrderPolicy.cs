using Reign.Core.Contracts.Actions;

namespace ReignBeta.Actions
{
    // Observed terminal receipts never permit the executor to issue an order again.
    // Mutual cancellation is distinct from completed fulfillment.
    internal static class ReignCompletedOrderPolicy
    {
        internal const string StaleAuthorizationFailure = "This action does not match an authorized saved agreement step.";

        internal static bool CanReconcileCancellation(AgreementJournal journal, AgreementStep step,
            bool capturedRecordMatches, bool installmentApplied, string nativeOrderStatus, string worldActionStatus)
        {
            var a = journal?.Agreement;
            return journal != null && a != null && step != null && journal.Steps != null && journal.Steps.Contains(step)
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
                    && agreement.State != AgreementState.Completed)) return false;

            return worldActionStatus == "Accepted" || worldActionStatus == "Executing"
                || worldActionStatus == "Failed" && failureReason == StaleAuthorizationFailure;
        }
    }
}
