using System;
using Reign.Core.Contracts.Actions;

namespace ReignBeta.Actions
{
    // These three adapters only record a treaty. A queued dispatch is not fulfillment.
    // This recovery never invokes an executor, releases custody or grants new consent.
    internal static class ReignUnfulfilledTreatyPolicy
    {
        internal const string ResultCode = "treaty_kingdom_obsolete";
        internal const string Message = "The unfulfilled treaty became impossible because a named kingdom was dissolved. No treaty or payment was applied.";
        private const string RulerFailure = "This settlement or treaty requires the current rulers of both named kingdoms.";

        internal static bool Retire(AgreementJournal journal, bool capturedRecordMatches, bool installmentApplied,
            string worldStatus, string worldFailure, bool kingdomEliminated, bool treatyRecorded, double now)
        {
            var a = journal?.Agreement;
            if (journal == null || a == null || a.Clauses == null || a.Clauses.Count != 1 || journal.Steps == null || journal.Steps.Count != 1
                || journal.Receipts == null
                || journal.OperatorAuthorized || a.Origin != ActionOrigin.Conversation || !a.HasBothConsents
                || journal.CommittedHash.Length == 0 || journal.CommittedHash != a.TermsHash()
                || !capturedRecordMatches || !installmentApplied || !kingdomEliminated || treatyRecorded
                || !string.IsNullOrEmpty(journal.RecoveryDetails)) return false;
            var clause = a.Clauses[0];
            var step = journal.Steps[0];
            if (!(clause.ActionId == "demilitarized_border" || clause.ActionId == "caravan_protection_agreement"
                || clause.ActionId == "sign_defensive_pact")
                || step.ClauseId != clause.Id || !step.Reserved || !step.Started || step.Completed || step.Returned
                || step.Installment != 0 || step.FulfilledInstallments != 0 || clause.Installments != 1) return false;

            if (a.State == AgreementState.Impossible && worldStatus == "Cancelled")
                return journal.Receipts != null && journal.Receipts.Exists(r => r.State == AgreementState.Impossible
                    && r.TermsHash == journal.CommittedHash && r.Message == Message && r.ObservedEffects.Count == 0);
            if (a.State != AgreementState.Waiting && a.State != AgreementState.InTransit
                && a.State != AgreementState.RecoveryRequired) return false;
            if (worldStatus != "Accepted" && worldStatus != "Executing" && worldStatus != "Failed") return false;
            if (worldStatus == "Failed" && !KnownFailure(worldFailure)) return false;
            if (!string.IsNullOrEmpty(a.Failure) && !KnownFailure(a.Failure)) return false;

            // A single zero-effect treaty has neither delivered work nor reserved assets to undo.
            // Keep the original failed receipt, started step, signatures and exact committed terms.
            a.State = AgreementState.Impossible;
            a.Failure = string.IsNullOrEmpty(a.Failure) ? Message : a.Failure + " " + Message;
            journal.Receipts.Add(new ActionReceipt { OperationId = a.Id, TermsHash = journal.CommittedHash,
                State = AgreementState.Impossible, Day = now, Message = Message });
            return true;
        }

        private static bool KnownFailure(string failure) => failure == RulerFailure
            || failure == "Only the current ruler can commit the acting kingdom."
            || failure == "Actor kingdom is missing or eliminated." || failure == "Target kingdom is missing or eliminated.";
    }
}
