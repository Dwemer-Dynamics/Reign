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
        public ReignActionResult ExecuteActionForTest(ReignWorldActionRecord action)
        {
            if (action == null)
            {
                return ReignActionResult.ValidationFailed("Action is null.");
            }

            ReignWorldActionRecord queued = EnqueueAction(action);
            if (queued == null)
            {
                return ReignActionResult.ValidationFailed("Action could not be queued.");
            }

            queued.RequiresAcceptance = false;
            queued.Status = ReignWorldActionStatus.Accepted;
            queued.ExecuteAfterDay = CurrentDay();
            return ExecuteAction(queued, CurrentDay());
        }

        private bool TryRestoreObservedPendingCampaignOrder(ReignWorldActionRecord action)
        {
            if (action == null || action.Type != ReignWorldActionType.RegularIssueCampaignOrder
                || action.Status != ReignWorldActionStatus.Completed) return false;
            JObject terms;
            try { terms = JObject.Parse(action.TermsJson ?? "{}"); }
            catch (Newtonsoft.Json.JsonException) { return false; }
            string namedOrder = terms.Value<string>("orderId");
            string orderId = string.IsNullOrWhiteSpace(namedOrder) ? action.ActionId : namedOrder.Trim();
            var order = ReignServices.CampaignCommand?.Orders.FirstOrDefault(o =>
                string.Equals(o.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            string correlation = string.IsNullOrWhiteSpace(action.NegotiationId) ? action.ActionId : action.NegotiationId;
            if (order == null || order.IsTerminal || order.CommanderHeroStringId != action.ActorHeroStringId
                || order.CorrelationId != correlation || order.Steps == null || order.Steps.Count == 0
                || order.CompletedStepCount >= order.Steps.Count
                || ReignServices.Agreement?.MatchesObservedPendingOrder(action, order.Status) != true)
                return false;
            // A duplicate issue receipt used to claim completion while the exact saved
            // native order was still active. Correct only that receipt; do not issue it.
            var progress = ReignActionResult.Progress("The accepted native campaign order remains in progress.")
                .WithResultCode("campaign_order_accepted")
                .WithEffect("campaign_order", "order", order.OrderId, order.Objective, "nativeProgressObserved=true");
            action.Status = ReignWorldActionStatus.Executing;
            action.FailureReason = string.Empty;
            action.ExecuteAfterDay = CurrentDay() + 1f / 24f;
            RememberActionResult(action, progress);
            _ = ReignServerClient.ReportActionAsync(action, "executing", progress.Message, progress);
            return true;
        }

        private bool TryRetireImpossibleTreaty(ReignWorldActionRecord action)
        {
            if (IsUncertainExecutionHeld(action)) return false;
            string kind = action?.Type == ReignWorldActionType.DiplomacyDemilitarizedBorder ? "demilitarized_border"
                : action?.Type == ReignWorldActionType.DiplomacyCaravanProtectionAgreement ? "caravan_protection_agreement"
                : action?.Type == ReignWorldActionType.DiplomacySignDefensivePact ? "defensive_pact" : "";
            if (kind.Length == 0 || (action.ExecutionPhase != "idle" && action.ExecutionPhase != "obsolete")
                || (action.ExecutionSnapshotJson != "{}" && !string.IsNullOrEmpty(action.ExecutionSnapshotJson))) return false;
            bool recorded = _agreements.Any(t => t != null && t.Kind == kind && SameAgreementPair(t, action)
                && t.Reason == action.Reason && t.TermsJson == action.TermsJson);
            if (ReignServices.Agreement?.ReconcileImpossibleTreaty(action, recorded) != true) return false;
            var result = ReignActionResult.Obsolete(ReignBeta.WorldSimulation.Actions.ReignUnfulfilledTreatyPolicy.Message,
                ReignBeta.WorldSimulation.Actions.ReignUnfulfilledTreatyPolicy.ResultCode);
            RememberActionResult(action, result);
            if (action.Status == ReignWorldActionStatus.Cancelled) return true;
            action.Status = ReignWorldActionStatus.Cancelled;
            action.FailureReason = string.Empty;
            action.ExecutionPhase = "obsolete";
            ReignLog.Info("Obsoleted unfulfilled treaty " + action.ActionId + ": " + result.Message);
            _ = ReignServerClient.ReportActionAsync(action, "obsolete", result.Message, result);
            return true;
        }

        internal bool RecordObservedCampaignCancellation(ReignWorldActionRecord action, string orderId, string cancellationId)
        {
            if (action == null || !_actions.Contains(action) || action.Type != ReignWorldActionType.RegularIssueCampaignOrder) return false;
            bool alreadyCancelled = action.Status == ReignWorldActionStatus.Cancelled;
            if (action.IsTerminal && !alreadyCancelled) return false;
            var result = ReignActionResult.NoOp("The supervised campaign order was cancelled by mutual agreement.")
                .WithResultCode("campaign_order_cancelled").WithOutcome("cancelled")
                .WithEffect("campaign_order_cancelled", "order", orderId, "", "cancellationAgreement=" + cancellationId);
            RememberActionResult(action, result);
            // The receipt cache is transient; restore it on reload without reporting or applying cancellation twice.
            if (alreadyCancelled) return true;
            action.Status = ReignWorldActionStatus.Cancelled;
            action.FailureReason = string.Empty;
            action.ExecuteAfterDay = CurrentDay();
            _ = ReignServerClient.ReportActionAsync(action, "cancelled", result.Message, result);
            return true;
        }

        internal bool TryCompleteObservedCampaignOrder(ReignWorldActionRecord action)
        {
            if (action == null || action.Type != ReignWorldActionType.RegularIssueCampaignOrder
                || (action.IsTerminal && !(action.Status == ReignWorldActionStatus.Failed
                    && action.FailureReason == ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.StaleAuthorizationFailure))) return false;
            JObject terms;
            try { terms = JObject.Parse(action.TermsJson ?? "{}"); }
            catch (Newtonsoft.Json.JsonException) { return false; }
            string namedOrder = terms.Value<string>("orderId");
            string orderId = string.IsNullOrWhiteSpace(namedOrder) ? action.ActionId : namedOrder.Trim();
            var order = ReignServices.CampaignCommand?.Orders.FirstOrDefault(o =>
                string.Equals(o.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            string correlation = string.IsNullOrWhiteSpace(action.NegotiationId) ? action.ActionId : action.NegotiationId;
            if (order == null || order.Status != "completed" || order.CommanderHeroStringId != action.ActorHeroStringId
                || order.CorrelationId != correlation || order.Steps == null || order.Steps.Count == 0
                || order.CompletedStepCount != order.Steps.Count || order.Steps.Any(s => s.Status != "completed")
                || ReignServices.Agreement?.MatchesObservedCompletedOrder(action, order.Status) != true)
                return false;
            // Recover only the known stale authorization error after exact native and
            // saved-consent proof. No native executor runs on this receipt-only path.
            if (action.Status == ReignWorldActionStatus.Failed) action.Status = ReignWorldActionStatus.Executing;
            return CompleteActionFromMissionHook(action.ActionId, ReignActionResult.Done("The supervised campaign order completed.")
                .WithResultCode("campaign_order_completed")
                .WithEffect("campaign_order_completed", "order", order.OrderId, order.Objective,
                    "completedSteps=" + order.CompletedStepCount + ";nativeCompletionObserved=true"));
        }

        public bool CompleteActionFromMissionHook(string actionId, ReignActionResult result)
        {
            if (string.IsNullOrWhiteSpace(actionId) || result == null)
            {
                return false;
            }

            ReignWorldActionRecord action = _actions.FirstOrDefault(x => x != null && x.ActionId == actionId);
            if (action == null || action.IsTerminal)
            {
                return false;
            }

            if (IsUncertainExecutionHeld(action))
            {
                // Later native evidence never settles a held record here; exact
                // reconciliation is owned by the separately reviewed receipt path.
                if (_terminalReportDeferralLogged.Add(action.ActionId + ":uncertain_hold_hook"))
                    ReignLog.Warn("Mission-hook result ignored for held uncertain action=" + action.ActionId);
                return false;
            }

            if (RecordRejectedAction(action, result)) return true;

            if (result.Success && result.Completed)
            {
                ReignServices.Government?.RecordCompletedWorldAction(action);
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Completed;
                action.FailureReason = string.Empty;
                action.ExecuteAfterDay = CurrentDay();
                ReignLog.Info("Completed mission-hook action " + action.ActionId + ": " + result.Message);
                ShowDebug("Completed " + action.Type + ".");
                ReportActionOutcome(action, "completed", result.Message, result);
                return true;
            }

            action.FailureReason = result.Message;
            if (!result.Success && !result.Retryable)
            {
                action.Status = ReignWorldActionStatus.Failed;
                ReignLog.Warn("Failed mission-hook action " + action.ActionId + ": " + result.Message);
                ShowDebug("Failed " + action.Type + ": " + result.Message);
                ReportActionOutcome(action, "failed", result.Message, result);
                return true;
            }

            action.Status = ReignWorldActionStatus.Executing;
            action.ExecuteAfterDay = CurrentDay() + (result.NextAttemptDelayDays > 0f ? result.NextAttemptDelayDays : 0.25f);
            ReignLog.Info("Mission-hook action progress " + action.ActionId + ": " + result.Message);
            _ = ReignServerClient.ReportActionAsync(action, "executing", result.Message, result);
            return true;
        }

        private ReignActionResult ExecuteAction(ReignWorldActionRecord action, float now)
        {
            // Authority for every tick, immediate, reconcile or direct start: never re-run a held record.
            if (IsUncertainExecutionHeld(action)) return UncertainExecutionResult();
            if (ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress)
            {
                return ReignActionResult.Progress("Action deferred until the native save completes.")
                    .WithResultCode("native_save_in_progress");
            }

            // Every native world-action execution passes here (both tick loops, immediate
            // dialogue execution, and records restored by the dedup path without re-import).
            // A candidate whose persisted offer no longer matches the loaded scope is deferred
            // untouched; only an exact current-generation re-offer can rebind it.
            string offerMismatch = TerminalReportExecutionMismatch(action);
            if (offerMismatch != null)
            {
                if (_terminalReportDeferralLogged.Add(action.ActionId + ":" + offerMismatch))
                    ReignLog.Warn("Terminal report candidate execution deferred action=" + action.ActionId + " reason=" + offerMismatch
                        + "; reconciliation limit: awaits an exact current-generation re-offer from its original origin");
                return ReignActionResult.Progress("Action deferred until its saved offer matches the loaded campaign scope.")
                    .WithResultCode("terminal_report_offer_scope_mismatch");
            }

            if (TryRetireImpossibleTreaty(action)
                || ReignServices.Agreement?.ReconcileObservedCampaignCancellation(action) == true
                || TryCompleteObservedCampaignOrder(action)) return GetLastActionResult(action.ActionId);

            if (action.RequiresAcceptance && action.Status == ReignWorldActionStatus.Proposed)
            {
                return ReignActionResult.Progress("Action is waiting for acceptance.").WithResultCode("waiting_for_acceptance");
            }

            if (!CanExecuteFamily(action))
            {
                ReignActionResult blocked = ReignActionResult.BlockedBySettings("Action family is disabled in Bannerlord Reign settings.");
                action.Status = ReignWorldActionStatus.Failed;
                action.FailureReason = blocked.Message;
                ReignLog.Warn("Blocked action " + action.ActionId + ": " + blocked.Message);
                ReportActionOutcome(action, "blocked", blocked.Message, blocked);
                return blocked;
            }

            action.Status = ReignWorldActionStatus.Executing;
            action.LastAttemptDay = now;
            if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(action.ExecutionPhase) || action.ExecutionPhase == "idle")
                {
                    Kingdom actorKingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
                    Kingdom targetKingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
                    action.ExecutionSnapshotJson = new JObject
                    {
                        ["negotiationId"] = action.NegotiationId ?? string.Empty, ["termsHash"] = action.TermsHash ?? string.Empty,
                        ["actorKingdomId"] = action.ActorKingdomStringId ?? string.Empty, ["targetKingdomId"] = action.TargetKingdomStringId ?? string.Empty,
                        ["actorRulerGold"] = actorKingdom?.Leader?.Gold ?? 0, ["targetRulerGold"] = targetKingdom?.Leader?.Gold ?? 0,
                        ["termsJson"] = action.TermsJson ?? "{}", ["preflightDay"] = now
                    }.ToString(Newtonsoft.Json.Formatting.None);
                    action.ExecutionPhase = "preflight_complete";
                }
                else if (action.ExecutionPhase == "verified")
                {
                    return ReignActionResult.Done("Negotiated action was already verified.");
                }
                action.ExecutionPhase = "material_terms";
            }

            ReignActionResult result;
            if (action.TypeValue < 100)
            {
                result = ReignDiplomacyExecutor.Execute(action);
            }
            else if (action.TypeValue >= 100 && action.TypeValue < 200)
            {
                result = ReignStrategyExecutor.Execute(action);
            }
            else if (action.TypeValue >= 200 && action.TypeValue < 300)
            {
                result = ReignPoliticsExecutor.Execute(action);
            }
            else if (action.TypeValue >= 300)
            {
                result = ReignRegularActionExecutor.Execute(action);
            }
            else
            {
                result = ReignActionResult.FailTerminal("Unsupported action family.", "unsupported_action_family", "unsupported_action");
            }

            if (RecordRejectedAction(action, result)) return result;

            if (string.Equals(result.Outcome, "obsolete", System.StringComparison.OrdinalIgnoreCase))
            {
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Cancelled;
                action.FailureReason = string.Empty;
                if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase))
                    action.ExecutionPhase = "obsolete";
                ReignLog.Info("Obsoleted action " + action.ActionId + ": " + result.Message);
                ReportActionOutcome(action, "obsolete", result.Message, result);
                return result;
            }

            if (result.Success && result.Completed)
            {
                ReignServices.Government?.RecordCompletedWorldAction(action);
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Completed;
                if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase)) action.ExecutionPhase = "verified";
                action.FailureReason = string.Empty;
                ReignLog.Info("Completed action " + action.ActionId + ": " + result.Message);
                string receipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(result);
                if (string.IsNullOrWhiteSpace(receipt))
                {
                    ShowDebug(result.Outcome == "noop" ? "No change for " + action.Type + "." : "Completed " + action.Type + ".");
                }
                else
                {
                    ShowVerifiedReceipt(receipt);
                }
                ReportActionOutcome(action, "completed", result.Message, result);
                return result;
            }

            if (result.Success)
            {
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Executing;
                action.FailureReason = string.Empty;
                action.ExecuteAfterDay = now + (result.NextAttemptDelayDays > 0f ? result.NextAttemptDelayDays : 0.25f);
                ReignLog.Info("Action progress " + action.ActionId + ": " + result.Message);
                _ = ReignServerClient.ReportActionAsync(action, "executing", result.Message, result);
                return result;
            }

            action.AttemptCount++;
            RememberActionResult(action, result);
            action.FailureReason = result.Message;
            action.ExecuteAfterDay = now + (result.NextAttemptDelayDays > 0f ? result.NextAttemptDelayDays : 0.5f);

            if (!result.Retryable || action.AttemptCount >= action.MaxAttempts)
            {
                action.Status = ReignWorldActionStatus.Failed;
                if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase)) action.ExecutionPhase = "failed";
                ReignLog.Warn("Failed action " + action.ActionId + ": " + result.Message);
                ShowDebug("Failed " + action.Type + ": " + result.Message);
                ReportActionOutcome(action, "failed", result.Message, result);
                return result;
            }

            action.Status = ReignWorldActionStatus.Accepted;
            ReignLog.Warn("Action retry " + action.ActionId + " attempt=" + action.AttemptCount + ": " + result.Message);
            _ = ReignServerClient.ReportActionAsync(action, "retry", result.Message, result);
            return result;
        }

        private bool RecordRejectedAction(ReignWorldActionRecord action, ReignActionResult result)
        {
            if (result == null || result.Success || !result.Completed || result.Retryable
                || !string.Equals(result.Outcome, "rejected", System.StringComparison.OrdinalIgnoreCase))
                return false;
            RememberActionResult(action, result);
            action.Status = ReignWorldActionStatus.Rejected;
            action.FailureReason = result.Message;
            if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase))
                action.ExecutionPhase = "rejected";
            ReignLog.Info("Rejected action " + action.ActionId + ": " + result.Message);
            ReportActionOutcome(action, "rejected", result.Message, result);
            return true;
        }

        private static bool CanExecuteFamily(ReignWorldActionRecord action)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null)
            {
                return true;
            }

            if (action.TypeValue < 100)
            {
                return settings.ExecuteDiplomacyActions;
            }

            if (action.TypeValue >= 100 && action.TypeValue < 200)
            {
                return settings.ExecuteStrategyPlans;
            }

            if (action.TypeValue >= 200 && action.TypeValue < 300)
            {
                return settings.ExecuteInternalPoliticsActions;
            }

            if (action.TypeValue >= 300)
            {
                return settings.ExecuteConversationActions;
            }

            return true;
        }

        public ReignDiplomaticAgreementRecord RecordAgreementFromAction(ReignWorldActionRecord action, string kind, float durationDays, bool isPublic)
        {
            if (action == null)
            {
                return null;
            }

            string canonicalKind = (kind ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(canonicalKind))
            {
                canonicalKind = action.Type.ToString();
            }

            float now = CurrentDay();
            float expire = durationDays > 0f ? now + durationDays : 0f;
            foreach (ReignDiplomaticAgreementRecord existing in _agreements.Where(x => x != null && x.IsActive && SameAgreementPair(x, action) && x.Kind == canonicalKind))
            {
                EndAgreement(existing, "superseded", string.Empty, string.Empty, now);
            }

            ReignDiplomaticAgreementRecord record = new ReignDiplomaticAgreementRecord
            {
                Kind = canonicalKind,
                ActorKingdomStringId = action.ActorKingdomStringId ?? string.Empty,
                TargetKingdomStringId = action.TargetKingdomStringId ?? string.Empty,
                ActorClanStringId = action.ActorClanStringId ?? string.Empty,
                TargetClanStringId = action.TargetClanStringId ?? string.Empty,
                TargetHeroStringId = action.TargetHeroStringId ?? string.Empty,
                TargetSettlementStringId = action.TargetSettlementStringId ?? string.Empty,
                Reason = action.Reason ?? string.Empty,
                TermsJson = action.TermsJson ?? string.Empty,
                CreatedDay = now,
                ExpireDay = expire,
                IsPublic = isPublic,
                IsActive = true
            };

            _agreements.Add(record);
            ReignLog.Info("Recorded diplomatic agreement " + record.AgreementId + " kind=" + record.Kind);
            return record;
        }

        public int EndAgreementsBetween(string actorKingdomId, string targetKingdomId, string kind)
        {
            return EndAgreementsBetween(actorKingdomId, targetKingdomId, kind, "ended", string.Empty, string.Empty);
        }

        public int EndAgreementsBetween(string actorKingdomId, string targetKingdomId, string kind,
            string endReason, string breakerKingdomId, string triggeringWarId)
        {
            int ended = 0;
            foreach (ReignDiplomaticAgreementRecord agreement in _agreements.Where(x => x != null && x.IsActive).ToList())
            {
                bool pairMatches = SameKingdomPair(agreement.ActorKingdomStringId, agreement.TargetKingdomStringId, actorKingdomId, targetKingdomId);
                bool kindMatches = string.IsNullOrWhiteSpace(kind) || string.Equals(agreement.Kind, kind, System.StringComparison.OrdinalIgnoreCase);
                if (pairMatches && kindMatches)
                {
                    EndAgreement(agreement, endReason, breakerKingdomId, triggeringWarId, CurrentDay());
                    ended++;
                }
            }

            if (ended > 0)
            {
                ReignLog.Info("Ended " + ended + " diplomatic agreement(s) between " + actorKingdomId + " and " + targetKingdomId + ".");
            }

            return ended;
        }

        private void PurgeExpiredAgreements()
        {
            float now = CurrentDay();
            int expired = 0;
            foreach (ReignDiplomaticAgreementRecord agreement in _agreements.Where(x => x != null && x.IsActive && x.IsExpired(now)))
            {
                EndAgreement(agreement, "expired", string.Empty, string.Empty, now);
                expired++;
            }

            if (expired > 0)
            {
                ReignLog.Info("Expired " + expired + " diplomatic agreement(s).");
            }
        }

        private static void EndAgreement(ReignDiplomaticAgreementRecord agreement, string reason,
            string breakerKingdomId, string triggeringWarId, float endedDay)
        {
            if (agreement == null || !agreement.IsActive) return;
            agreement.IsActive = false;
            agreement.EndedDay = endedDay;
            agreement.EndReason = reason ?? string.Empty;
            agreement.BreakerKingdomStringId = breakerKingdomId ?? string.Empty;
            agreement.TriggeringWarId = triggeringWarId ?? string.Empty;
        }

        private static bool SameAgreementPair(ReignDiplomaticAgreementRecord agreement, ReignWorldActionRecord action)
        {
            return SameKingdomPair(agreement.ActorKingdomStringId, agreement.TargetKingdomStringId, action.ActorKingdomStringId, action.TargetKingdomStringId)
                && SameNullableId(agreement.ActorClanStringId, action.ActorClanStringId)
                && SameNullableId(agreement.TargetClanStringId, action.TargetClanStringId)
                && SameNullableId(agreement.TargetHeroStringId, action.TargetHeroStringId)
                && SameNullableId(agreement.TargetSettlementStringId, action.TargetSettlementStringId);
        }

        private static bool SameKingdomPair(string a1, string a2, string b1, string b2)
        {
            return (string.Equals(a1 ?? string.Empty, b1 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b2 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase))
                || (string.Equals(a1 ?? string.Empty, b2 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b1 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameNullableId(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                return true;
            }

            return string.Equals(left ?? string.Empty, right ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
