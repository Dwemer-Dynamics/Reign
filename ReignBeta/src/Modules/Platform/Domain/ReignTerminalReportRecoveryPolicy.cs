#nullable enable
namespace ReignBeta.Platform.Domain
{
    internal enum TerminalReportRoute { None, V1, Recover, Hold }

    internal readonly struct TerminalReportDecision
    {
        internal TerminalReportDecision(TerminalReportRoute route, string reason, bool attachProof)
        {
            Route = route; Reason = reason; AttachProof = attachProof;
        }
        internal TerminalReportRoute Route { get; }
        internal string Reason { get; }
        // Send the current verified load proof beside the envelope (never inside its SHA).
        internal bool AttachProof { get; }
    }

    // Facts the game thread has already established for one saved terminal report.
    internal sealed class TerminalReportRecoveryInput
    {
        internal string? Phase { get; set; }
        // The persisted report bytes still hash to their SHA and restate their exact scope.
        internal bool PayloadExact { get; set; }
        // The report already existed (captured or later, or unreadable) when this save was loaded.
        internal bool Inherited { get; set; }
        // Stage 2 uncertain-execution hold. It blocks execution and rebind only, never delivery.
        internal bool Quarantined { get; set; }
        // An exact server redelivery asked for this settled report's one reconciliation.
        internal bool Redelivered { get; set; }
        // The coordinator verified this load (exact no-op or completed rollback).
        internal bool LoadVerified { get; set; }
        // The server refused this load's proof (a later import or restore replaced it).
        internal bool LoadProofStale { get; set; }
        internal bool GenerationMatchesLive { get; set; }
        internal bool OriginMatchesLoad { get; set; }
        // acknowledged only: what the native step recorded at acknowledgement.
        internal bool HasAckNative { get; set; }
    }

    // Pure decisions for saved terminal action reports. Generation equality is never proof
    // of the same server history: an inherited report is sent only under a verified load,
    // with that load's proof. Holds send nothing and change no phase; the caller still
    // suppresses legacy synthesis and keeps the server's normal redelivery.
    internal static class ReignTerminalReportRecoveryPolicy
    {
        internal const string NativeAttemptStamp = "v3";

        internal static TerminalReportDecision Delivery(TerminalReportRecoveryInput input)
        {
            string phase = input.Phase ?? string.Empty;
            // No captured report (including every quarantined offered/unproven record).
            if (phase.Length == 0 || phase == "offered" || phase == "unproven") return None("no_report");
            // The persisted legacy phase proves only the route choice, never commit or native application.
            if (phase == "legacy") return Hold("legacy_outcome_unknown");
            bool settled = phase == "stale" || phase == "acknowledged";
            if (phase != "captured" && phase != "posted" && phase != "native_pending" && !settled) return None("settled");
            if (settled && !input.Redelivered) return None("settled");
            if (!input.PayloadExact)
                return Hold(input.Quarantined ? "quarantine_payload_unproven" : phase == "acknowledged" ? "acknowledged_unprovable" : "payload_unproven");
            if (phase == "acknowledged" && !input.HasAckNative) return Hold("acknowledged_unprovable");
            if (!input.Inherited)
            {
                // Captured after this load, so its generation is the one this instance loaded.
                if (!input.GenerationMatchesLive) return Hold("generation_changed_in_session");
                // Reconciling settled evidence is allowed only under a verified load's proof.
                if (settled && !input.LoadVerified) return Hold("load_unverified");
                if (phase == "stale") return None("settled");
                if (input.LoadVerified && input.LoadProofStale) return Hold("load_proof_invalidated");
                // Unverified play keeps today's unproven v1 delivery for newly captured reports.
                return new TerminalReportDecision(TerminalReportRoute.V1, "v1", input.LoadVerified);
            }
            if (!input.LoadVerified) return Hold("load_unverified");
            if (input.LoadProofStale) return Hold("load_proof_invalidated");
            if (input.GenerationMatchesLive) return new TerminalReportDecision(TerminalReportRoute.V1, "v1_verified_load", true);
            if (!input.OriginMatchesLoad) return Hold("origin_mismatch");
            return new TerminalReportDecision(TerminalReportRoute.Recover, "recover", true);
        }

        // journal: absent, complete, incomplete, mismatch, unreadable or unpersisted (the
        // persisted list, or a saved unpersisted marker). serverEffect: pending, applied or none.
        // Returns apply, replay, done or hold:<reason>. Server "applied" never certifies a
        // native mutation by itself, and an absent journal proves no mutation only for an
        // attempt stamped before any native call.
        internal static string NativeAction(string? phase, string? journal, string? nativeAttempt, string? serverEffect, string? ackNative)
        {
            if (journal == "mismatch") return "hold:native_parameter_mismatch";
            if (journal == "incomplete") return "hold:native_observation_incomplete";
            if (journal != "absent" && journal != "complete") return "hold:native_journal_" + (journal ?? "unknown");
            bool complete = journal == "complete";
            if (serverEffect == "pending")
            {
                if (complete) return "replay";
                if (phase == "acknowledged") return "hold:native_effect_divergent";
                if (phase == "native_pending") return nativeAttempt == NativeAttemptStamp ? "apply" : "hold:native_pending_premarker";
                return phase == "captured" || phase == "posted" || phase == "stale" ? "apply" : "hold:native_phase_unproven";
            }
            if (serverEffect == "applied") return complete ? "done" : "hold:native_server_applied_journal_absent";
            if (serverEffect == "none")
            {
                if (complete) return "hold:native_effect_divergent";
                return phase != "acknowledged" || ackNative == "none" ? "done" : "hold:native_effect_divergent";
            }
            return "hold:native_effect_status_unrecognized";
        }

        private static TerminalReportDecision None(string reason) => new TerminalReportDecision(TerminalReportRoute.None, reason, false);
        private static TerminalReportDecision Hold(string reason) => new TerminalReportDecision(TerminalReportRoute.Hold, reason, false);
    }
}
