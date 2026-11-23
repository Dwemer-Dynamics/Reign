using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.Government;
using ReignBeta.Platform;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TerminalActionReportProtocol = Reign.Core.Contracts.Actions.TerminalActionReportProtocol;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Diplomacy.World;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Domain;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Relationships.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;

namespace ReignBeta.Platform.Campaign
{
    public sealed partial class ReignAICampaignBehavior
    {
        private static JObject TerminalReportState(ReignWorldActionRecord action)
        {
            if (string.IsNullOrEmpty(action?.TerminalReportJson)) return null;
            try
            {
                JObject state = JObject.Parse(action.TerminalReportJson);
                if (state.Value<string>("schema") == TerminalReportStateSchema) return state;
            }
            catch (Newtonsoft.Json.JsonException) { }
            // Unreadable state is inert: it never reopens the legacy writer.
            return new JObject { ["schema"] = TerminalReportStateSchema, ["phase"] = "refused", ["reason"] = "unreadable_state" };
        }

        // Exact JSON string value only; other token types never coerce into identity.
        private static string TerminalReportString(JObject source, string key) =>
            source?[key] is JValue value && value.Type == JTokenType.String ? (string)value : null;

        // Classified from the imported record alone (never the loaded hero): an original
        // pa_ independent player gold/item transfer. Everything else keeps legacy reporting.
        private static bool IsKnownTerminalReportCandidate(ReignWorldActionRecord action) =>
            action?.ActionId != null && action.ActionId.StartsWith("pa_", StringComparison.Ordinal)
            && ((action.Type == ReignWorldActionType.RegularTransferGold && action.NegotiatedCommand == "transfer_gold")
                || (action.Type == ReignWorldActionType.RegularTransferItem && action.NegotiatedCommand == "transfer_item"))
            && !string.Equals(action.AuthorizationMode, "negotiated", StringComparison.OrdinalIgnoreCase);

        // Game thread, before ledger admission. Returns false only for a known candidate
        // whose original import owner (fence and its captured shared activity generation)
        // is no longer loaded; the server outbox redelivers it. A known candidate without
        // proof is admitted as retained unproven evidence: it never falls back to legacy.
        private bool OfferTerminalReport(ReignWorldActionRecord action)
        {
            action.TerminalReportJson = string.Empty;
            if (!IsKnownTerminalReportCandidate(action)) return true;
            var fence = action.ImportFence as ReignServerClient.RequestFence;
            string liveGeneration = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
            if (fence != null && (!fence.IsCurrent() || fence.SharedActivityGeneration != liveGeneration))
            {
                ReignLog.Warn("Terminal report candidate not admitted: its import owner is no longer loaded action=" + action.ActionId);
                return false;
            }
            string origin = action.ImportOrigin;
            var state = new JObject
            {
                ["schema"] = TerminalReportStateSchema, ["origin"] = origin ?? string.Empty,
                ["campaignId"] = ReignServerClient.GetCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["generation"] = fence != null ? fence.SharedActivityGeneration : liveGeneration,
                ["actionId"] = action.ActionId, ["termsHash"] = action.TermsHash ?? string.Empty
            };
            string reason = fence == null || origin == null ? "missing_import_proof"
                : origin.Length == 0 ? "origin_unproven"
                : Hero.MainHero == null || action.ActorHeroStringId != Hero.MainHero.StringId ? "actor_not_loaded_player"
                : !TerminalReportScopeKeys.All(key => TerminalActionReportProtocol.IsTrustedIdentifier(TerminalReportString(state, key))) ? "scope_unproven"
                : null;
            state["phase"] = reason == null ? "offered" : "unproven";
            if (reason != null)
            {
                state["reason"] = reason;
                ReignLog.Warn("Terminal report candidate retained without delivery action=" + action.ActionId + " reason=" + reason);
            }
            action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            return true;
        }

        // Game thread. Null when a nonterminal record has no terminal report state, or when its
        // persisted offer still matches the loaded campaign, timeline, player, action/terms and
        // shared activity generation (and an offered report keeps its recognized origin).
        // Otherwise the mismatch reason: native execution is deferred, nothing is mutated.
        // An unproven offer recorded without any generation keeps its earlier legacy execution.
        private static string TerminalReportExecutionMismatch(ReignWorldActionRecord action)
        {
            if (action == null || action.IsTerminal || string.IsNullOrEmpty(action.TerminalReportJson)) return null;
            JObject state = TerminalReportState(action);
            string phase = TerminalReportString(state, "phase");
            if (phase != "offered" && phase != "unproven") return "phase_" + (phase ?? "missing");
            string generation = TerminalReportString(state, "generation") ?? string.Empty;
            string live = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
            if (TerminalReportString(state, "campaignId") != ReignServerClient.GetCampaignId()) return "campaign";
            if (TerminalReportString(state, "timelineId") != (ReignServices.WorldHistory?.TimelineId ?? "main")) return "timeline";
            if (Hero.MainHero == null || TerminalReportString(state, "playerHeroStringId") != Hero.MainHero.StringId) return "player";
            if (TerminalReportString(state, "actionId") != action.ActionId
                || TerminalReportString(state, "termsHash") != (action.TermsHash ?? string.Empty)) return "action";
            if ((phase == "offered" || generation.Length > 0) && (generation.Length == 0 || generation != live)) return "generation";
            if (phase == "offered" && string.IsNullOrEmpty(TerminalReportString(state, "origin"))) return "origin";
            return null;
        }

        // Game thread, dedup path. Rebinds only an untouched (never attempted, no result)
        // nonterminal offered candidate whose persisted offer is stale solely by shared
        // activity generation, when the same original origin re-offers the identical action
        // (ids, terms, type, command, actor/target) under a current loaded fence and the
        // current generation and scope. The prior generation is kept as evidence. Anything
        // else stays deferred by TerminalReportExecutionMismatch for reconciliation.
        private bool RefreshTerminalReportOffer(ReignWorldActionRecord existing, ReignWorldActionRecord incoming)
        {
            if (existing == null || incoming == null || existing.IsTerminal
                || TerminalReportExecutionMismatch(existing) != "generation"
                || !IsKnownTerminalReportCandidate(existing) || !IsKnownTerminalReportCandidate(incoming)) return false;
            JObject state = TerminalReportState(existing);
            if (TerminalReportString(state, "phase") != "offered") return false;
            bool untouched = existing.AttemptCount == 0 && existing.LastAttemptDay <= 0f
                && existing.Status != ReignWorldActionStatus.Executing
                && !_lastActionResults.ContainsKey(existing.ActionId);
            bool identical = incoming.ActionId == existing.ActionId && incoming.TermsHash == existing.TermsHash
                && incoming.Type == existing.Type && incoming.NegotiatedCommand == existing.NegotiatedCommand
                && incoming.ActorHeroStringId == existing.ActorHeroStringId && incoming.TargetHeroStringId == existing.TargetHeroStringId
                && string.Equals(incoming.AuthorizationMode, existing.AuthorizationMode, StringComparison.Ordinal);
            var fence = incoming.ImportFence as ReignServerClient.RequestFence;
            string live = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
            string origin = TerminalReportString(state, "origin") ?? string.Empty;
            if (!untouched || !identical || fence == null || !fence.IsCurrent() || live.Length == 0
                || fence.SharedActivityGeneration != live || origin.Length == 0 || incoming.ImportOrigin != origin
                || existing.ActorHeroStringId != Hero.MainHero?.StringId
                || !TerminalActionReportProtocol.IsTrustedIdentifier(live)) return false;
            state["priorGeneration"] = TerminalReportString(state, "generation");
            state["generation"] = live;
            state["reason"] = "reoffered_current_generation";
            existing.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            ReignLog.Info("Terminal report candidate rebound to the current generation on an exact re-offer action=" + existing.ActionId);
            return true;
        }

        // A re-offer never synthesizes a protocol candidate's legacy report: a pending exact
        // report is hinted (its backoff stands, so the 30 s redelivery cannot re-POST it on
        // every poll), settled stale/acknowledged evidence gets at most one reconciliation per
        // loaded instance, and every other settled phase suppresses the synthesized report.
        private bool HandleTerminalReportReoffer(ReignWorldActionRecord existing)
        {
            JObject state = TerminalReportState(existing);
            string phase = state?.Value<string>("phase");
            if (state == null || ((phase == "offered" || phase == "unproven") && !existing.IsTerminal)) return false;
            if (phase == "captured" || phase == "posted" || phase == "native_pending")
                HintTerminalReport(existing);
            else if ((phase == "stale" || phase == "acknowledged") && !_terminalReportReconciled.Contains(existing.ActionId)
                && _terminalReportReconcileRequested.Add(existing.ActionId))
                HintTerminalReport(existing);
            else if (phase == "legacy")
                HoldTerminalReport(existing.ActionId, "legacy_outcome_unknown");
            return true;
        }

        // Game thread. Sends nothing for this report again in this loaded instance; one diagnostic.
        private void HoldTerminalReport(string actionId, string reason)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            _terminalReportHolds[actionId] = reason ?? string.Empty;
            if (_terminalReportDeferralLogged.Add("hold:" + actionId))
                ReignLog.Info("Terminal action report held without delivery action=" + actionId + " reason=" + BoundedReason(reason));
        }

        // Game thread. Read-only snapshot of the reports held in this loaded instance, for the
        // runtime diagnostics. Holds are never expired here; durable work stays saved.
        internal JObject TerminalReportHoldDiagnostics()
        {
            var result = new JObject { ["schema"] = "reign-terminal-report-holds-v1" };
            if (!ReignMainThread.IsMainThread)
            {
                result["available"] = false;
                result["reason"] = "off_main_thread";
                return result;
            }
            var reasons = new JObject();
            foreach (var group in _terminalReportHolds.Values.GroupBy(value => BoundedReason(value)).OrderBy(group => group.Key, StringComparer.Ordinal))
                reasons[group.Key] = group.Count();
            result["available"] = true;
            result["heldCount"] = _terminalReportHolds.Count;
            result["reasons"] = reasons;
            result["sampleActionIds"] = new JArray(_terminalReportHolds.Keys.OrderBy(id => id, StringComparer.Ordinal).Take(10));
            return result;
        }

        // Game thread. The recovery policy's view of one saved report under the current load.
        private TerminalReportRecoveryInput TerminalReportRecoveryInputFor(ReignWorldActionRecord action, JObject state, string phase,
            ReignSaveSyncCoordinator.VerifiedLoad load, bool redelivered)
        {
            string generation = TerminalReportString(state, "generation") ?? string.Empty;
            string live = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
            string origin = TerminalReportString(state, "origin") ?? string.Empty;
            bool exact = TerminalReportPayloadExact(state, TerminalReportString(state, "reportJson"), TerminalReportString(state, "status"), out string sha)
                && sha == TerminalReportString(state, "reportSha256");
            return new TerminalReportRecoveryInput
            {
                Phase = phase, PayloadExact = exact, Inherited = _inheritedTerminalReportPhase.ContainsKey(action),
                Quarantined = IsUncertainExecutionHeld(action), Redelivered = redelivered,
                LoadVerified = load != null, LoadProofStale = load != null && _terminalReportStaleProofSession == load.LoadSessionId,
                GenerationMatchesLive = generation.Length > 0 && generation == live,
                OriginMatchesLoad = load != null && origin.Length > 0 && origin == load.Origin,
                HasAckNative = !string.IsNullOrEmpty(TerminalReportString(state, "ackNative"))
            };
        }

        private static JObject TerminalReportLoadProof(ReignSaveSyncCoordinator.VerifiedLoad load) => load == null ? null : new JObject
        {
            ["loadSessionId"] = load.LoadSessionId, ["savePointId"] = load.SavePointId, ["restoreGeneration"] = load.RestoreGeneration
        };

        // The persisted exact report restates the persisted scope and status it is filed
        // under, within the protocol size bound; sha is its recomputed digest.
        private static bool TerminalReportPayloadExact(JObject state, string json, string status, out string sha)
        {
            sha = string.Empty;
            if (state == null || string.IsNullOrEmpty(json) || !TerminalReportStatuses.Contains(status ?? string.Empty)
                || !TerminalReportScopeKeys.All(key => TerminalActionReportProtocol.IsTrustedIdentifier(TerminalReportString(state, key)))
                || !TerminalActionReportProtocol.TryReportSha256(json, out sha, out _)) return false;
            JObject report;
            try { report = JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return false; }
            string player = TerminalReportString(state, "playerHeroStringId"), actionId = TerminalReportString(state, "actionId");
            return TerminalReportString(report, "status") == status
                && TerminalReportString(report, "campaignId") == TerminalReportString(state, "campaignId")
                && TerminalReportString(report, "timelineId") == TerminalReportString(state, "timelineId")
                && TerminalReportString(report, "playerHeroStringId") == player
                && TerminalReportString(report, "actorHeroStringId") == player
                && TerminalReportString(report, "sharedActivityGeneration") == TerminalReportString(state, "generation")
                && TerminalReportString(report, "termsHash") == TerminalReportString(state, "termsHash")
                && TerminalReportString(report, "serverActionId") == actionId
                && (report["actionId"] == null || TerminalReportString(report, "actionId") == actionId);
        }

        // Terminal game-thread result. A candidate captures its one exact report and SHA;
        // delivery belongs to the drain. Everything else keeps the existing one-shot report.
        private void ReportActionOutcome(ReignWorldActionRecord action, string status, string message, ReignActionResult result)
        {
            JObject state = TerminalReportState(action);
            if (state == null || !TerminalReportStatuses.Contains(status ?? string.Empty))
            {
                _ = ReignServerClient.ReportActionAsync(action, status, message, result);
                return;
            }
            string phase = state.Value<string>("phase");
            if (phase != "offered" && phase != "unproven")
            {
                ReignLog.Warn("Terminal report already captured; a later terminal result was not reported action=" + action.ActionId);
                return;
            }
            string json = ReignServerClient.BuildTerminalReportJson(action, status, message, result);
            bool exact = TerminalReportPayloadExact(state, json, status, out string sha);
            if (exact) { state["reportJson"] = json; state["reportSha256"] = sha; }
            state["status"] = status;
            state["phase"] = !exact ? "refused" : phase == "offered" ? "captured" : "unproven";
            if (!exact) state["reason"] = "report_scope_or_size";
            action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            _terminalReportBackoff.Remove(action.ActionId);
            if (exact && phase == "offered") HintTerminalReport(action);
            ReignLog.Info("Terminal action report captured action=" + action.ActionId + " phase=" + state.Value<string>("phase"));
        }

        private void TickTerminalReports()
        {
            long now = DateTime.UtcNow.Ticks;
            if (now < _nextTerminalReportScanUtcTicks) return;
            _nextTerminalReportScanUtcTicks = now + TerminalReportScanTicks;
            string generation = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
            if (ReignBetaSettings.Instance?.UseLocalServer == false || generation.Length == 0) return;
            // Backoff and the in-flight slot belong to one loaded owner; a new owner starts clean.
            if (_terminalReportFence == null || !_terminalReportFence.IsCurrent() || _terminalReportFence.SharedActivityGeneration != generation)
            {
                _terminalReportFence = ReignServerClient.RequestFence.Capture();
                _terminalReportBackoff.Clear();
            }
            if (_terminalReportInFlightToken != null && ReferenceEquals(_terminalReportInFlightFence, _terminalReportFence)) return;
            // Hinted records first; each hint is consumed once and must still be retained.
            while (_terminalReportHints.Count > 0)
            {
                ReignWorldActionRecord hinted = _terminalReportHints[0];
                _terminalReportHints.RemoveAt(0);
                if (_actions.IndexOf(hinted) >= 0 && TryStartTerminalReport(hinted, now)) return;
            }
            int count = _actions.Count;
            if (count == 0) { _terminalReportScanCursor = 0; return; }
            if (_terminalReportScanCursor >= count) _terminalReportScanCursor = 0;
            for (int examined = 0; examined < Math.Min(count, TerminalReportScanBatch); examined++)
            {
                ReignWorldActionRecord action = _actions[_terminalReportScanCursor];
                _terminalReportScanCursor = (_terminalReportScanCursor + 1) % count;
                if (TryStartTerminalReport(action, now)) return;
            }
        }

        private void HintTerminalReport(ReignWorldActionRecord action)
        {
            // Overflow falls back to the rotating cursor, which still reaches every record.
            if (action != null && _terminalReportHints.Count < TerminalReportScanBatch && !_terminalReportHints.Contains(action))
                _terminalReportHints.Add(action);
        }

        private bool TryStartTerminalReport(ReignWorldActionRecord action, long now)
        {
            if (string.IsNullOrEmpty(action?.TerminalReportJson) || _terminalReportHolds.ContainsKey(action.ActionId)
                || (_terminalReportBackoff.TryGetValue(action.ActionId, out var wait) && wait.Key > now)) return false;
            JObject state = TerminalReportState(action);
            string phase = state?.Value<string>("phase");
            bool redelivered = _terminalReportReconcileRequested.Contains(action.ActionId);
            if (phase != "captured" && phase != "posted" && phase != "native_pending"
                && !(redelivered && (phase == "stale" || phase == "acknowledged"))) return false;
            ReignSaveSyncCoordinator.VerifiedLoad load = ReignSaveSyncCoordinator.CurrentVerifiedLoad;
            TerminalReportDecision decision = ReignTerminalReportRecoveryPolicy.Delivery(
                TerminalReportRecoveryInputFor(action, state, phase, load, redelivered));
            if (decision.Route == TerminalReportRoute.Hold) HoldTerminalReport(action.ActionId, decision.Reason);
            // A reconciliation request is consumed by a hold or a dispatch, never by a deferral.
            if (redelivered && decision.Route != TerminalReportRoute.V1 && decision.Route != TerminalReportRoute.Recover)
                ConsumeTerminalReportReconcile(action.ActionId);
            if (decision.Route != TerminalReportRoute.V1 && decision.Route != TerminalReportRoute.Recover) return false;
            // Recovery is bound to one verified load of this exact campaign and live generation.
            // A live generation that has not caught up with the load yet only defers (no request).
            string live = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
            if (decision.Route == TerminalReportRoute.Recover && (load.RestoreGeneration != live
                || load.CampaignId != ReignServerClient.GetCampaignId() || _terminalReportFence?.SharedActivityGeneration != live))
            {
                if (_terminalReportDeferralLogged.Add("recovery_wait:" + action.ActionId))
                    ReignLog.Info("Terminal action report recovery waits for the verified load generation action=" + action.ActionId);
                return false;
            }
            if (redelivered) ConsumeTerminalReportReconcile(action.ActionId);
            var token = new object();
            _terminalReportInFlightToken = token;
            _terminalReportInFlightFence = _terminalReportFence;
            _ = AdvanceTerminalReportAsync(action, state, _terminalReportFence, token,
                decision.AttachProof ? load : null, decision.Route == TerminalReportRoute.Recover,
                _inheritedTerminalReportPhase.ContainsKey(action));
            return true;
        }

        private void ConsumeTerminalReportReconcile(string actionId)
        {
            _terminalReportReconcileRequested.Remove(actionId);
            _terminalReportReconciled.Add(actionId);
        }

        // One delivery step: v1 (carrying the current verified load proof when the policy says so)
        // or, for an inherited report captured under another generation, the recovery schema bound
        // to that verified load. Settled stale/acknowledged evidence is reconciled at most once per
        // loaded instance and never falls back. A hold sends nothing more for this instance.
        private async Task AdvanceTerminalReportAsync(ReignWorldActionRecord action, JObject state, ReignServerClient.RequestFence fence, object token,
            ReignSaveSyncCoordinator.VerifiedLoad load = null, bool recovery = false, bool inherited = false)
        {
            string actionId = action.ActionId, origin = TerminalReportString(state, "origin") ?? string.Empty;
            string sha = TerminalReportString(state, "reportSha256") ?? string.Empty, phase = state.Value<string>("phase");
            string reason = "pending", hold = null, staleProofSession = null;
            bool progressed = false;
            bool reconcile = phase == "acknowledged";
            ReignSaveSyncCoordinator.VerifiedLoad recoveryLoad = recovery ? load : null;
            // Game-thread guard for every pinned request: same loaded owner, scope, phase,
            // origin and an exact persisted report (rechecked immediately before dispatch).
            // Recovery replaces only generation equality with the exact current verified load.
            Func<bool> owner = () => TerminalReportStepOwner(action, fence, sha, phase, origin, recoveryLoad);
            try
            {
                if (recovery)
                {
                    string capability = await TerminalReportRecoveryCapabilityAsync(origin, owner).ConfigureAwait(false);
                    if (capability != "supported")
                    {
                        reason = "recovery_capability_" + capability;
                        // An exact older server: never posted the recovery schema, never Stale, never legacy.
                        if (capability == "unsupported") hold = "server_recovery_unsupported";
                        return;
                    }
                }
                // Inherited, recovered or reconciled dispatch is persisted before its POST; a stale
                // report reopens as posted with its prior phase kept, never as legacy. An inherited
                // captured report never takes the proof-less preflight (whose legacy, stale or
                // conflict answer could settle it or apply journal-less deltas): the proof-carrying
                // POST decides.
                if (recovery || phase == "stale" || reconcile || (inherited && phase == "captured"))
                {
                    string from = phase, to = phase == "captured" || phase == "stale" ? "posted" : phase;
                    if (!await TransitionTerminalReportAsync(action, fence, sha, from, origin, to,
                        recovery ? "recovery_dispatched" : "reconcile_dispatched",
                        s => RecordTerminalReportDispatch(s, from, load, recovery, reconcile), recoveryLoad).ConfigureAwait(false)) return;
                    phase = to;
                }
                if (phase == "captured")
                {
                    string preflight = await PreflightTerminalReportAsync(state, owner).ConfigureAwait(false);
                    reason = "preflight_" + preflight;
                    if (preflight == "legacy")
                    {
                        // Verified missing capability or known current ineligibility before any
                        // protocol POST: the existing one-shot report, same origin, marked first.
                        if (!await TransitionTerminalReportAsync(action, fence, sha, phase, origin, "legacy", reason).ConfigureAwait(false)) return;
                        progressed = true;
                        phase = "legacy";
                        JObject legacy = await ReignServerClient.SendPinnedJsonAsync(origin, "/actions/report",
                            JObject.Parse(TerminalReportString(state, "reportJson")), owner).ConfigureAwait(false);
                        await ReignServerClient.ApplyLegacyReportRelationChangesAsync(legacy, fence, actionId, owner).ConfigureAwait(false);
                        return;
                    }
                    if (preflight == "stale" || preflight == "conflict")
                    {
                        progressed = await TransitionTerminalReportAsync(action, fence, sha, phase, origin, preflight, reason).ConfigureAwait(false);
                        return;
                    }
                    // An exact authoritative eligible reply starts the first POST. So does an
                    // exact protocol_owned reply (same scope and report type): a save may hold
                    // this captured report from before its first POST while another live
                    // instance already posted it, so a same-generation reload restores
                    // "captured". The pinned replay carries only this exact body; the server
                    // revalidates hash, type and generation and refuses any different report.
                    // Posted is persisted before dispatch, so no legacy fallback follows.
                    if (preflight != "eligible" && preflight != "protocol_owned") return;
                    if (!await TransitionTerminalReportAsync(action, fence, sha, phase, origin, "posted", reason).ConfigureAwait(false)) return;
                    phase = "posted";
                }

                JObject ack = await ReignServerClient.SendPinnedJsonAsync(origin, TerminalActionReportProtocol.ProposedRoute,
                    TerminalReportEnvelope(state, load, recovery), owner).ConfigureAwait(false);
                string disposition = TerminalReportAckDisposition(ack, state);
                string ackReason = TerminalReportString(ack, "reason") ?? string.Empty;
                reason = "ack_" + disposition + ":" + ackReason;
                if (disposition == TerminalActionReportProtocol.Conflict && ackReason == TerminalActionReportProtocol.LoadProofStale)
                {
                    // A later import or restore replaced this verified load; the server mutated nothing.
                    hold = "load_proof_invalidated";
                    staleProofSession = load?.LoadSessionId;
                    return;
                }
                if (disposition == TerminalActionReportProtocol.Accepted && recovery && !TerminalReportAdmissionMatches(ack, load))
                {
                    reason = "ack_admission_mismatch";
                    return;
                }
                if (disposition != TerminalActionReportProtocol.Accepted)
                {
                    // After a POST nothing falls back. Only exact stale/conflict settles a v1 report;
                    // invalid, mismatched, ineligible or unknown replies stay pending. Recovery never
                    // goes stale and a reconciliation never changes its settled phase: unproven,
                    // ineligible or unknown history is held for this load.
                    string settled = disposition == TerminalActionReportProtocol.Stale && !recovery ? "stale"
                        : disposition == TerminalActionReportProtocol.Conflict ? "conflict" : null;
                    if (reconcile)
                    {
                        if (settled != null || disposition == TerminalActionReportProtocol.Unknown)
                            hold = "reconcile_" + disposition + ":" + ackReason;
                    }
                    else if (settled != null)
                        progressed = await TransitionTerminalReportAsync(action, fence, sha, phase, origin, settled, reason, null, recoveryLoad).ConfigureAwait(false);
                    else if (recovery && (disposition == TerminalActionReportProtocol.Unknown || disposition == TerminalActionReportProtocol.Stale))
                        hold = ackReason.Length > 0 ? ackReason : "recovery_" + disposition;
                    return;
                }
                string authoritative = TerminalReportString(ack, "authoritativeStatus") ?? string.Empty;
                if (!TerminalReportStatuses.Contains(authoritative)) { reason = "ack_authoritative_status_invalid"; return; }
                bool memoryDone = TerminalReportString(ack, "memoryProjectionStatus") == "completed";
                string effectStatus = TerminalReportString(ack, "nativeEffectStatus") ?? string.Empty;
                JToken effectToken = ack["nativeEffect"];
                JObject effect = effectToken as JObject;
                if (effectToken != null && effectToken.Type != JTokenType.Null && effect == null) { reason = "effect_invalid_effect"; return; }
                // R3: the persisted native journal, never the server status alone, decides. The
                // classification, the native_pending stamp and the ledger write run in one
                // synchronous game-thread block, so no save can split them.
                JObject observation = null;
                string outcome = "pending:owner_changed", ackNative = "none";
                await ReignMainThread.InvokeAsync(() =>
                {
                    observation = ResolveTerminalReportEffect(action, fence, sha, phase, origin, effect, effectStatus,
                        recoveryLoad, reconcile, out outcome, out ackNative);
                }).ConfigureAwait(false);
                bool nativeDone = outcome == "done";
                if (outcome.StartsWith("hold:", StringComparison.Ordinal))
                {
                    hold = outcome.Substring("hold:".Length);
                    return;
                }
                if (outcome == "ack")
                {
                    if (!reconcile) phase = "native_pending";
                    string receiptId = TerminalReportString(effect, "receiptId");
                    var receiptBody = new JObject
                    {
                        ["campaignId"] = TerminalReportString(state, "campaignId"),
                        ["receiptIds"] = new JArray(receiptId),
                        ["nativeReceipt"] = observation
                    };
                    if (load != null) receiptBody[TerminalActionReportProtocol.LoadProofField] = TerminalReportLoadProof(load);
                    JObject receiptAck = await ReignServerClient.SendPinnedJsonAsync(origin, "/relationships/conversation/native-receipt",
                        receiptBody, owner).ConfigureAwait(false);
                    if (receiptAck?.Value<bool?>("ok") != true && TerminalReportString(receiptAck, "reason") == TerminalActionReportProtocol.LoadProofStale)
                    {
                        hold = "load_proof_invalidated";
                        staleProofSession = load?.LoadSessionId;
                        return;
                    }
                    nativeDone = receiptAck?.Value<bool?>("ok") == true
                        && TerminalReportString(receiptAck, "campaignId") == TerminalReportString(state, "campaignId")
                        && receiptAck["receiptIds"] is JArray acked
                        && acked.Any(id => id.Type == JTokenType.String && (string)id == receiptId);
                    if (!nativeDone) reason = "native_receipt_unacknowledged";
                    else ackNative = "applied:" + receiptId;
                }
                else if (!nativeDone)
                {
                    // No native mutation happened in this step; the report stays pending.
                    reason = "effect_" + outcome.Substring(outcome.IndexOf(':') + 1);
                    return;
                }
                if (reconcile)
                {
                    // Settled evidence keeps its phase; only the reconciliation result is recorded.
                    if (nativeDone) progressed = await TransitionTerminalReportAsync(action, fence, sha, phase, origin, phase, "reconciled",
                        s => { if (s["reconcile"] is JObject r) r["result"] = "done"; }, recoveryLoad).ConfigureAwait(false);
                    return;
                }
                if (nativeDone && memoryDone)
                {
                    progressed = await TransitionTerminalReportAsync(action, fence, sha, phase, origin, "acknowledged", reason,
                        s => { s["authoritativeStatus"] = authoritative; s["ackNative"] = ackNative; }, recoveryLoad).ConfigureAwait(false);
                }
                else if (nativeDone) reason = "memory_projection_pending";
            }
            catch (Exception ex)
            {
                reason = "error_" + ex.GetType().Name;
            }
            finally
            {
                if (hold != null) reason = "hold_" + hold;
                if (!progressed || reason.StartsWith("error_", StringComparison.Ordinal))
                    ReignLog.Info("Terminal action report step unsettled action=" + actionId + " reason=" + BoundedReason(reason));
                await ReignMainThread.InvokeAsync(() =>
                {
                    // Release only this step's own slot. A step whose owner was replaced
                    // touches nothing else: no backoff, no state.
                    if (ReferenceEquals(_terminalReportInFlightToken, token))
                    {
                        _terminalReportInFlightToken = null;
                        _terminalReportInFlightFence = null;
                    }
                    if (!ReferenceEquals(Instance, this) || !ReferenceEquals(_terminalReportFence, fence) || !fence.IsCurrent()) return;
                    if (hold != null)
                    {
                        if (staleProofSession != null) _terminalReportStaleProofSession = staleProofSession;
                        HoldTerminalReport(actionId, hold);
                        return;
                    }
                    long now = DateTime.UtcNow.Ticks;
                    if (progressed) { _terminalReportBackoff.Remove(actionId); return; }
                    int failures = _terminalReportBackoff.TryGetValue(actionId, out var prior) ? prior.Value + 1 : 1;
                    long delay = TimeSpan.FromSeconds(Math.Min(300d, 5d * Math.Pow(2d, Math.Min(failures, 6)))).Ticks;
                    _terminalReportBackoff[actionId] = new KeyValuePair<long, int>(now + delay, failures);
                }).ConfigureAwait(false);
            }
        }

        // "eligible", "protocol_owned" (exact scope/type already owned by the protocol; the
        // caller may replay only the pinned exact body), "legacy" (verified missing capability
        // or current ineligibility), "stale", "conflict" or "unknown". Only the pinned origin is read.
        private static async Task<string> PreflightTerminalReportAsync(JObject state, Func<bool> owner)
        {
            string origin = TerminalReportString(state, "origin") ?? string.Empty;
            JObject health = await ReignServerClient.SendPinnedJsonAsync(origin, "/health", null, owner).ConfigureAwait(false);
            if (health.Value<bool?>("ok") != true || TerminalReportString(health, "service") != "BannerlordReignServer") return "unknown";
            JToken capability = health[TerminalActionReportProtocol.HealthCapabilityField];
            if (capability == null) return "legacy";
            if (capability.Type != JTokenType.String || (string)capability != TerminalActionReportProtocol.RequestSchema) return "unknown";
            JObject reply = await ReignServerClient.SendPinnedJsonAsync(origin, TerminalActionReportProtocol.ProposedRoute + "?"
                + string.Join("&", TerminalReportScopeKeys.Select(key => key + "=" + Uri.EscapeDataString(TerminalReportString(state, key) ?? string.Empty))),
                null, owner).ConfigureAwait(false);
            if (reply.Value<bool?>("ok") != true || TerminalReportString(reply, "schema") != TerminalActionReportProtocol.PreflightSchema
                || TerminalReportString(reply, "service") != "BannerlordReignServer" || TerminalReportString(reply, "protocol") != TerminalActionReportProtocol.RequestSchema
                || TerminalReportScopeKeys.Any(key => TerminalReportString(reply, key) != TerminalReportString(state, key))) return "unknown";
            string disposition = TerminalReportString(reply, "disposition"), reason = TerminalReportString(reply, "reason");
            string reportType = TerminalReportString(JObject.Parse(TerminalReportString(state, "reportJson") ?? "{}"), "type") ?? string.Empty;
            bool sameType = reportType.Length > 0 && TerminalReportString(reply, "reportType") == reportType;
            if (disposition == TerminalActionReportProtocol.Eligible && sameType) return "eligible";
            if (disposition == TerminalActionReportProtocol.Accepted && reason == "protocol_owned" && sameType) return "protocol_owned";
            if (disposition == TerminalActionReportProtocol.Ineligible) return "legacy";
            if (disposition == TerminalActionReportProtocol.Stale) return "stale";
            return disposition == TerminalActionReportProtocol.Conflict ? "conflict" : "unknown";
        }

        // The v1 envelope is unchanged (generation is always the report's own). A verified load's
        // proof and, for recovery, the load it is recovered under are siblings outside the SHA.
        private static JObject TerminalReportEnvelope(JObject state, ReignSaveSyncCoordinator.VerifiedLoad load = null, bool recovery = false)
        {
            var envelope = new JObject
            {
                ["schema"] = recovery ? TerminalActionReportProtocol.RecoverySchema : TerminalActionReportProtocol.RequestSchema
            };
            foreach (string key in TerminalReportScopeKeys) envelope[key] = TerminalReportString(state, key);
            envelope["status"] = TerminalReportString(state, "status");
            envelope["reportJson"] = TerminalReportString(state, "reportJson");
            envelope["reportSha256"] = TerminalReportString(state, "reportSha256");
            if (load != null) envelope[TerminalActionReportProtocol.LoadProofField] = TerminalReportLoadProof(load);
            if (recovery && load != null)
                envelope[TerminalActionReportProtocol.RecoveryField] = new JObject
                {
                    ["liveGeneration"] = load.RestoreGeneration, ["savePointId"] = load.SavePointId, ["loadSessionId"] = load.LoadSessionId
                };
            return envelope;
        }

        // Mutable dispatch progress only; the report bytes, SHA, generation and scope never change.
        private static void RecordTerminalReportDispatch(JObject state, string priorPhase, ReignSaveSyncCoordinator.VerifiedLoad load,
            bool recovery, bool reconcile)
        {
            if (priorPhase == "stale") state["priorPhase"] = "stale";
            if (reconcile) state["reconcile"] = new JObject { ["phaseAtLoad"] = priorPhase, ["dispatched"] = true, ["result"] = "pending" };
            if (recovery && load != null)
                state["recovery"] = new JObject
                {
                    ["schema"] = TerminalActionReportProtocol.RecoverySchema, ["liveGeneration"] = load.RestoreGeneration,
                    ["savePointId"] = load.SavePointId, ["loadSessionId"] = load.LoadSessionId,
                    ["priorPhase"] = priorPhase, ["dispatched"] = true
                };
        }

        // "supported", "unsupported" (an exact Reign server health without the recovery capability)
        // or "unknown". Only the pinned origin is read.
        private static async Task<string> TerminalReportRecoveryCapabilityAsync(string origin, Func<bool> owner)
        {
            JObject health = await ReignServerClient.SendPinnedJsonAsync(origin, "/health", null, owner).ConfigureAwait(false);
            if (health.Value<bool?>("ok") != true || TerminalReportString(health, "service") != "BannerlordReignServer") return "unknown";
            JToken capability = health[TerminalActionReportProtocol.RecoveryHealthCapabilityField];
            if (capability == null) return "unsupported";
            return capability.Type == JTokenType.String && (string)capability == TerminalActionReportProtocol.RecoverySchema ? "supported" : "unknown";
        }

        // The accepted recovery reply names exactly the verified load this step was sent under.
        private static bool TerminalReportAdmissionMatches(JObject ack, ReignSaveSyncCoordinator.VerifiedLoad load) =>
            load != null && ack?[TerminalActionReportProtocol.AdmissionField] is JObject admission
            && TerminalReportString(admission, "generation") == load.RestoreGeneration
            && TerminalReportString(admission, "savePointId") == load.SavePointId
            && TerminalReportString(admission, "loadSessionId") == load.LoadSessionId;

        // The disposition of an ack that echoes this exact report identity, else "invalid".
        private static string TerminalReportAckDisposition(JObject ack, JObject state) =>
            ack?.Value<bool?>("ok") != true || TerminalReportString(ack, "schema") != TerminalActionReportProtocol.ResponseSchema
            || TerminalReportString(ack, "campaignId") != TerminalReportString(state, "campaignId")
            || TerminalReportString(ack, "actionId") != TerminalReportString(state, "actionId")
            || TerminalReportString(ack, "reportSha256") != TerminalReportString(state, "reportSha256")
            || TerminalReportString(ack, "generation") != TerminalReportString(state, "generation")
                ? "invalid" : TerminalReportString(ack, "disposition") ?? "invalid";

        // Game thread: the same loaded owner and ledger record, the exact live campaign,
        // timeline, player and shared activity generation, and the same report identity,
        // phase, action, terms and original pinned origin.
        private JObject CurrentTerminalReport(ReignWorldActionRecord action, ReignServerClient.RequestFence fence, string sha, string phase, string origin)
        {
            if (!ReferenceEquals(Instance, this) || fence == null || !fence.IsCurrent() || !_actions.Contains(action)
                || Hero.MainHero == null || string.IsNullOrEmpty(sha) || string.IsNullOrEmpty(origin)) return null;
            JObject state = TerminalReportState(action);
            string generation = TerminalReportString(state, "generation");
            return state != null && TerminalReportString(state, "reportSha256") == sha && TerminalReportString(state, "phase") == phase
                && TerminalReportString(state, "origin") == origin
                && TerminalReportString(state, "campaignId") == ReignServerClient.GetCampaignId()
                && TerminalReportString(state, "timelineId") == (ReignServices.WorldHistory?.TimelineId ?? "main")
                && TerminalReportString(state, "playerHeroStringId") == Hero.MainHero.StringId
                && !string.IsNullOrEmpty(generation) && generation == fence.SharedActivityGeneration
                && generation == (ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty)
                && TerminalReportString(state, "actionId") == action.ActionId
                && TerminalReportString(state, "termsHash") == (action.TermsHash ?? string.Empty) ? state : null;
        }

        // Game thread: CurrentTerminalReport plus the persisted report still hashing to its
        // SHA and restating its exact encoded scope. Every pinned request requires it.
        private bool TerminalReportRequestOwner(ReignWorldActionRecord action, ReignServerClient.RequestFence fence, string sha, string phase, string origin)
        {
            JObject state = CurrentTerminalReport(action, fence, sha, phase, origin);
            return state != null && TerminalReportPayloadExact(state, TerminalReportString(state, "reportJson"),
                TerminalReportString(state, "status"), out string recomputed) && recomputed == sha;
        }

        // Game thread, recovery only: CurrentTerminalReport's checks with generation equality
        // replaced by the exact current verified load. The report's own generation differs from
        // the live one; the live generation, the loaded fence and the load proof agree; and the
        // origin that answered that load is the report's pinned origin.
        private bool TerminalReportRecoveryOwner(ReignWorldActionRecord action, ReignServerClient.RequestFence fence, string sha, string phase,
            string origin, ReignSaveSyncCoordinator.VerifiedLoad load)
        {
            if (!ReferenceEquals(Instance, this) || fence == null || !fence.IsCurrent() || !_actions.Contains(action)
                || Hero.MainHero == null || string.IsNullOrEmpty(sha) || string.IsNullOrEmpty(origin) || load == null
                || !ReferenceEquals(ReignSaveSyncCoordinator.CurrentVerifiedLoad, load)) return false;
            JObject state = TerminalReportState(action);
            string generation = TerminalReportString(state, "generation");
            string live = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
            return state != null && TerminalReportString(state, "reportSha256") == sha && TerminalReportString(state, "phase") == phase
                && TerminalReportString(state, "origin") == origin && origin == load.Origin
                && TerminalReportString(state, "campaignId") == ReignServerClient.GetCampaignId()
                && TerminalReportString(state, "campaignId") == load.CampaignId
                && TerminalReportString(state, "timelineId") == (ReignServices.WorldHistory?.TimelineId ?? "main")
                && TerminalReportString(state, "playerHeroStringId") == Hero.MainHero.StringId
                && !string.IsNullOrEmpty(generation) && live.Length > 0 && generation != live
                && live == fence.SharedActivityGeneration && live == load.RestoreGeneration
                && TerminalReportString(state, "actionId") == action.ActionId
                && TerminalReportString(state, "termsHash") == (action.TermsHash ?? string.Empty)
                && TerminalReportPayloadExact(state, TerminalReportString(state, "reportJson"), TerminalReportString(state, "status"), out string recomputed)
                && recomputed == sha;
        }

        private bool TerminalReportStepOwner(ReignWorldActionRecord action, ReignServerClient.RequestFence fence, string sha, string phase,
            string origin, ReignSaveSyncCoordinator.VerifiedLoad recoveryLoad) => recoveryLoad == null
                ? TerminalReportRequestOwner(action, fence, sha, phase, origin)
                : TerminalReportRecoveryOwner(action, fence, sha, phase, origin, recoveryLoad);

        private Task<bool> TransitionTerminalReportAsync(ReignWorldActionRecord action, ReignServerClient.RequestFence fence,
            string sha, string fromPhase, string origin, string toPhase, string reason, Action<JObject> update = null,
            ReignSaveSyncCoordinator.VerifiedLoad recoveryLoad = null) =>
            ReignMainThread.InvokeAsync(() =>
            {
                if (!TerminalReportStepOwner(action, fence, sha, fromPhase, origin, recoveryLoad)) return false;
                JObject state = TerminalReportState(action);
                state["phase"] = toPhase;
                state["reason"] = BoundedReason(reason);
                update?.Invoke(state);
                // The exact report bytes stay at every phase, acknowledged included, so a later
                // verified load can still prove and reconcile them.
                action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
                ReignLog.Info("Terminal action report action=" + action.ActionId + " phase=" + toPhase + " reason=" + BoundedReason(reason));
                return true;
            });

        // Game thread, one synchronous block. Server acceptance never proves a native mutation:
        // the persisted journal (plus any saved unpersisted marker) is classified for the exact
        // receipt identity, the pure policy decides, and only an apply/replay reaches the ledger.
        // outcome: ack (observation returned), done, hold:<reason> or pending:<reason> (nothing
        // mutated; retried). ackNative is what an acknowledgement records about the effect.
        private JObject ResolveTerminalReportEffect(ReignWorldActionRecord action, ReignServerClient.RequestFence fence, string sha,
            string phase, string origin, JObject effect, string effectStatus, ReignSaveSyncCoordinator.VerifiedLoad recoveryLoad,
            bool keepPhase, out string outcome, out string ackNative)
        {
            outcome = "pending:owner_changed";
            ackNative = "none";
            if (!TerminalReportStepOwner(action, fence, sha, phase, origin, recoveryLoad)) return null;
            JObject state = TerminalReportState(action);
            string timeline = TerminalReportString(state, "timelineId"), generation = TerminalReportString(state, "generation");
            string expected;
            try { expected = TerminalActionReportProtocol.ReceiptId(action.ActionId, timeline, generation, sha); }
            catch (ArgumentException) { outcome = "pending:invalid_effect"; return null; }
            if (effect != null && TerminalReportString(effect, "receiptId") != expected) { outcome = "pending:invalid_effect"; return null; }
            // Without a restated effect the report's own pair names the identity (delta unknown).
            string subjectId = effect != null ? TerminalReportString(effect, "subjectId") ?? string.Empty : action.ActorHeroStringId ?? string.Empty;
            string targetId = effect != null ? TerminalReportString(effect, "targetId") ?? string.Empty : action.TargetHeroStringId ?? string.Empty;
            JToken deltaToken = effect?["delta"];
            int? delta = deltaToken != null && deltaToken.Type == JTokenType.Integer
                && (long)deltaToken >= int.MinValue && (long)deltaToken <= int.MaxValue ? (int)(long)deltaToken : (int?)null;
            ReignRelationshipCampaignBehavior relations = ReignServices.Relationship;
            if (relations == null) { outcome = "pending:participant_unavailable"; return null; }
            string savedMarker = TerminalReportString(state, "nativeJournal");
            string journal = !string.IsNullOrEmpty(savedMarker) ? savedMarker
                : relations.ClassifyConversationRelation(expected, subjectId, targetId, delta);
            string decision = ReignTerminalReportRecoveryPolicy.NativeAction(phase, journal,
                TerminalReportString(state, "nativeAttempt"), effectStatus, TerminalReportString(state, "ackNative"));
            if (decision == "done")
            {
                outcome = "done";
                ackNative = effectStatus == "applied" ? "applied:" + expected : "none";
                return null;
            }
            // An unrecognized server status is not evidence about the native world; retry later.
            if (decision == "hold:native_effect_status_unrecognized") { outcome = "pending:native_effect_status_unrecognized"; return null; }
            if (decision != "apply" && decision != "replay") { outcome = decision; return null; }
            JObject observation = ApplyTerminalReportEffect(action, fence, sha, phase, origin, effect, recoveryLoad, keepPhase, expected, out string reason);
            if (observation == null)
            {
                outcome = reason.StartsWith("hold:", StringComparison.Ordinal) ? reason : "pending:" + reason;
                return null;
            }
            // A recovered report is acknowledged only with an exact echo of its admitting load.
            if (recoveryLoad != null)
                observation[TerminalActionReportProtocol.AdmissionField] = new JObject
                {
                    ["generation"] = recoveryLoad.RestoreGeneration, ["loadSessionId"] = recoveryLoad.LoadSessionId
                };
            outcome = "ack";
            return observation;
        }

        // Game thread. Validates the staged receipt against the original action and exact
        // report before any native mutation, then applies it once through the save-backed
        // native relation ledger (a replay returns the saved observation). The native_pending
        // write carries the attempt stamp, so a later absent journal proves no native call.
        private JObject ApplyTerminalReportEffect(ReignWorldActionRecord action, ReignServerClient.RequestFence fence,
            string sha, string phase, string origin, JObject effect, ReignSaveSyncCoordinator.VerifiedLoad recoveryLoad,
            bool keepPhase, string expectedReceipt, out string reason)
        {
            reason = "owner_changed";
            if (!TerminalReportStepOwner(action, fence, sha, phase, origin, recoveryLoad)) return null;
            JObject state = TerminalReportState(action);
            reason = "invalid_effect";
            string timeline = TerminalReportString(state, "timelineId"), generation = TerminalReportString(state, "generation");
            string expected;
            try { expected = TerminalActionReportProtocol.ReceiptId(action.ActionId, timeline, generation, sha); }
            catch (ArgumentException) { return null; }
            string subjectId = TerminalReportString(effect, "subjectId") ?? string.Empty, targetId = TerminalReportString(effect, "targetId") ?? string.Empty;
            JToken deltaToken = effect?["delta"];
            long delta = deltaToken != null && deltaToken.Type == JTokenType.Integer ? (long)deltaToken : 0L;
            bool pair = subjectId.Length > 0 && targetId.Length > 0 && subjectId != targetId
                && ((subjectId == action.ActorHeroStringId && targetId == action.TargetHeroStringId)
                    || (subjectId == action.TargetHeroStringId && targetId == action.ActorHeroStringId));
            // Server action relation deltas are bounded to -25..15 and staged as pending.
            if (effect == null || TerminalReportString(effect, "receiptId") != expected || TerminalReportString(effect, "timelineId") != timeline
                || TerminalReportString(effect, "reportGeneration") != generation || TerminalReportString(effect, "reportSha256") != sha
                || TerminalReportString(effect, "nativeApplicationStatus") != "pending"
                || !pair || delta == 0 || delta < -25 || delta > 15) return null;
            reason = "participant_unavailable";
            Hero subject = ReignObjectResolver.FindHero(subjectId), target = ReignObjectResolver.FindHero(targetId);
            ReignRelationshipCampaignBehavior relations = ReignServices.Relationship;
            if (subject == null || target == null || subject == target || relations == null) return null;
            if (expectedReceipt != expected) return null;
            if (phase != "native_pending" && !keepPhase)
            {
                state["phase"] = "native_pending";
                state["nativeAttempt"] = ReignTerminalReportRecoveryPolicy.NativeAttemptStamp;
                action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            }
            reason = "measurement_incomplete";
            JObject observation;
            try { observation = relations.ApplyConversationRelationOnce(new[] { expected }, subject, target, (int)delta, true); }
            catch (Exception)
            {
                // What the persisted journal now proves. An entry the replay cache recorded but the
                // persisted list lost may follow a native mutation: mark it in the same synchronous
                // call so it is never applied again, even after a save discards the cache.
                string after = relations.ClassifyConversationRelation(expected, subject.StringId, target.StringId, (int)delta);
                if (after == "unpersisted")
                {
                    state["nativeJournal"] = "unpersisted";
                    action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
                    reason = "hold:native_journal_unpersisted";
                }
                else if (after == "absent") reason = "native_apply_failed_before_mutation";
                else if (after == "incomplete") reason = "hold:native_observation_incomplete";
                else if (after == "mismatch") reason = "hold:native_parameter_mismatch";
                else reason = "hold:native_journal_" + after;
                return null;
            }
            observation["timelineId"] = timeline;
            observation["reportGeneration"] = generation;
            observation["reportSha256"] = sha;
            reason = string.Empty;
            return observation;
        }

        private static string BoundedReason(string reason) => new string((reason ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == ':').Take(96).ToArray());
    }
}
