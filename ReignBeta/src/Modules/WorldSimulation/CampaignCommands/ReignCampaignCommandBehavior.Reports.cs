using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.PartyAgency;
using ReignBeta.Shared.WarCouncil;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.World;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.CampaignCommands
{
    public sealed partial class ReignCampaignCommandBehavior
    {
        private void RequestGuidance(ReignCampaignOrderRecord order, string reason, bool exceptional)
        {
            if (order.AwaitingGuidance) return;
            order.AwaitingGuidance = true;
            order.Status = "awaiting_guidance";
            order.Stage = "reporting";
            order.GuidanceDeadlineDay = CurrentDay() + GuidanceWindow;
            QueueReport(order, reason, true);
            AppendHistory(order, "guidance_requested", reason);
            if (exceptional && !IsBindingOrder(order) && !order.LlmJudgmentRequested)
            {
                order.LlmJudgmentRequested = true;
                _ = ReignCampaignCommandClient.RequestCampaignOrderJudgmentAsync(order, BuildJudgmentPayload(order));
            }
            Publish(order, "guidance_required");
        }

        private void QueueReport(ReignCampaignOrderRecord order, string reason, bool urgent)
        {
            order.PendingReportId = "order_report_" + Guid.NewGuid().ToString("N");
            Hero commander = ReignObjectResolver.FindHero(order.CommanderHeroStringId);
            order.PendingReportText = ReignCampaignReportPolicy.Body(commander?.Name?.ToString(),
                order.Objective, ResolveTargetSettlement(order)?.Name?.ToString(), reason,
                order.Status, order.AwaitingGuidance);
            order.UrgentReportPending = urgent;
            order.LastReportDay = CurrentDay();
            order.ReportLetterPending = true;
            order.NextReportLetterAttemptDay = 0d;
            TryDeliverReportLetter(order);
        }

        private static string BuildScoutingObservation(MobileParty party, Settlement target)
        {
            if (party == null || !party.IsActive)
                return "The scouting party is unavailable; no current observations can be confirmed.";
            List<MobileParty> enemies = MobileParty.All.Where(x => ValidOtherParty(party, x)
                && IsHostile(party, x.MapFaction)
                && party.GetPosition2D.DistanceSquared(x.GetPosition2D) <= NearbyEnemyRadiusSquared)
                .OrderBy(x => party.GetPosition2D.DistanceSquared(x.GetPosition2D)).ToList();
            string summary = "At the end of scouting, our party had " + party.MemberRoster.TotalManCount
                + " troops. I observed " + enemies.Count + " active hostile parties near our current position.";
            if (enemies.Count > 0)
                summary += " The nearest were " + string.Join(", ", enemies.Take(8)
                    .Select(x => x.Name?.ToString() ?? "an unnamed party")) + ".";
            if (target != null)
                summary += " " + target.Name + (target.IsUnderSiege ? " was under siege." : " was not under siege.");
            return summary + " These observations describe the local situation when this report was made.";
        }

        private void DeliverPendingReportLetters(float dt)
        {
            _reportDeliveryElapsed += dt;
            if (_reportDeliveryElapsed < 1f) return;
            _reportDeliveryElapsed = 0f;
            foreach (ReignCampaignOrderRecord order in _orders.Where(x => x != null && x.ReportLetterPending).ToList())
                TryDeliverReportLetter(order);
        }

        private void TryDeliverReportLetter(ReignCampaignOrderRecord order)
        {
            if (order == null || !order.ReportLetterPending || CurrentDay() < order.NextReportLetterAttemptDay
                || string.IsNullOrWhiteSpace(order.PendingReportId) || string.IsNullOrWhiteSpace(order.PendingReportText)
                || !_reportLettersInFlight.Add(order.OrderId)) return;
            // Freeze the exact report before asynchronous dispatch; a later guidance
            // report cannot borrow an older letter's successful receipt.
            var report = new ReignCampaignOrderRecord
            {
                OrderId = order.OrderId, CorrelationId = order.CorrelationId,
                CommanderHeroStringId = order.CommanderHeroStringId,
                PendingReportId = order.PendingReportId, PendingReportText = order.PendingReportText,
                AwaitingGuidance = order.AwaitingGuidance
            };
            _ = DeliverReportLetterAsync(order, report);
        }

        private async Task DeliverReportLetterAsync(ReignCampaignOrderRecord order, ReignCampaignOrderRecord report)
        {
            try
            {
                bool delivered = await ReignCampaignCommandClient.SendCampaignOrderReportLetterAsync(report);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (Instance != this || !_orders.Contains(order)
                        || order.PendingReportId != report.PendingReportId) return;
                    if (!delivered)
                    {
                        order.NextReportLetterAttemptDay = CurrentDay() + Hour;
                        return;
                    }
                    order.ReportLetterPending = false;
                    order.ReportLetterDeliveredId = report.PendingReportId;
                    AppendHistory(order, "report_letter_delivered", report.PendingReportId);
                    if (ReignCampaignReportPolicy.CanFinishScouting(order.Objective, order.Stage,
                        order.ReportLetterPending, order.PendingReportId, order.ReportLetterDeliveredId))
                        Complete(order, "The scouting report was delivered.");
                    else Publish(order, "report_letter_delivered");
                });
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (Instance == this && _orders.Contains(order) && order.PendingReportId == report.PendingReportId)
                        order.NextReportLetterAttemptDay = CurrentDay() + Hour;
                });
                ReignLog.Warn("Campaign report delivery will retry: " + ex.Message);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() => _reportLettersInFlight.Remove(order.OrderId));
            }
        }

        private void DeliverUrgentReport(ReignCampaignOrderRecord order)
        {
            if (order == null || !order.UrgentReportPending || string.IsNullOrWhiteSpace(order.PendingReportText)) return;
            order.UrgentReportPending = false;
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign != null && !campaign.TimeControlModeLock)
                campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            InformationManager.ShowInquiry(new InquiryData(
                "Urgent Campaign Report",
                order.PendingReportText + "\n\nReport ID: " + order.PendingReportId,
                true, false, "Acknowledge", string.Empty, () => { }, null));
            AppendHistory(order, "report_delivered", order.PendingReportId);
            Publish(order, "report_delivered");
        }

        private void ResolveUnansweredReport(ReignCampaignOrderRecord order)
        {
            if (IsBindingOrder(order))
            {
                Fail(order, "agreement_cannot_proceed", "The accepted objective cannot proceed without changing its agreed terms: " + order.PendingReportText);
                return;
            }
            string outcome = ReignCampaignCommandJudgment.IsAllowedOutcome(order.LlmOutcome)
                ? order.LlmOutcome : order.LastDecision;
            if (outcome == "request_guidance" || outcome == "refuse")
            {
                ReignCampaignCommandDecisionInput input = BuildDecisionInput(order,
                    ReignObjectResolver.FindHero(order.CommanderHeroStringId),
                    ReignObjectResolver.FindHeroParty(order.CommanderHeroStringId),
                    ResolveTargetSettlement(order), ResolveTargetParty(order));
                outcome = input.CivilianRelief && input.TargetUrgent ? "continue"
                    : input.SovereignOrder ? "adapt" : "withdraw";
            }
            order.AwaitingGuidance = false;
            order.PendingReportId = string.Empty;
            order.GuidanceDeadlineDay = 0d;
            if (outcome == "withdraw")
            {
                order.Objective = "withdraw";
                order.TargetSettlementStringId = string.Empty;
                order.Stage = "adapting";
            }
            else if (outcome == "adapt" || outcome == "deviate")
            {
                order.Stage = "adapting";
                if (outcome == "deviate") ApplyBoundedDeviation(order);
            }
            else
            {
                order.Stage = "executing";
            }
            order.Status = "active";
            AppendHistory(order, "guidance_timeout", "No response in 12 hours; commander chose " + outcome + ".");
            Publish(order, "guidance_timeout_decision");
            ReviewOrder(order, true);
        }

        private void ApplyBoundedDeviation(ReignCampaignOrderRecord order)
        {
            if (order == null) return;
            string prior = order.Objective;
            if (prior == "raid" || prior == "besiege_capture" || prior == "engage_party"
                || prior == "hunt_enemy_parties")
            {
                order.Objective = "hunt_enemy_parties";
            }
            else
            {
                order.Objective = "defend";
            }
            order.TargetPartyStringId = string.Empty;
            order.TargetHeroStringId = string.Empty;
            order.TargetSettlementStringId = string.Empty;
            if (string.IsNullOrWhiteSpace(order.Region)) order.Region = "frontier";
            order.DeadlineDay = CurrentDay() + 12d / 24d;
            order.DeviationCount++;
            ResolveAndStoreAnchors(order);
            AppendHistory(order, "deviated", prior + " -> " + order.Objective
                + " within known geography after unanswered guidance.");
        }

        internal void ApplyLlmJudgment(string orderId, string outcome, string reason)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null || order.IsTerminal || !ReignCampaignCommandJudgment.IsAllowedOutcome(outcome)) return;
            order.LlmOutcome = outcome.ToLowerInvariant();
            order.LlmReason = reason ?? string.Empty;
            AppendHistory(order, "llm_advisory", order.LlmOutcome + ": " + order.LlmReason);
        }

        internal bool RequestGuidanceForCertification(string orderId, string reason)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null) return false;
            RequestGuidance(order, reason ?? "Certification guidance fixture.", false);
            return order.AwaitingGuidance && order.GuidanceDeadlineDay > CurrentDay();
        }

        internal bool ExpireGuidanceForCertification(string orderId)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null || !order.AwaitingGuidance) return false;
            order.GuidanceDeadlineDay = CurrentDay();
            ResolveUnansweredReport(order);
            return !order.AwaitingGuidance && !order.IsTerminal;
        }

        internal bool ReviewForCertification(string orderId)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null) return false;
            ReviewOrder(order, true);
            return !order.IsTerminal;
        }

        internal bool CompleteCurrentStepForCertification(string orderId, string reason)
        {
            ReignCampaignOrderRecord order = _orders.FirstOrDefault(x => x != null
                && !x.IsTerminal && string.Equals(x.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            if (order == null) return false;
            int before = order.CurrentStepIndex;
            Complete(order, string.IsNullOrWhiteSpace(reason) ? "Certification step transition." : reason);
            return order.IsTerminal || order.CurrentStepIndex == before + 1;
        }

        internal bool VerifySaveRoundTripForCertification()
        {
            try
            {
                string serialized = JsonConvert.SerializeObject(_orders);
                List<ReignCampaignOrderRecord> restored =
                    JsonConvert.DeserializeObject<List<ReignCampaignOrderRecord>>(serialized);
                return restored != null && restored.Count == _orders.Count
                    && restored.Select(x => x.OrderId).SequenceEqual(_orders.Select(x => x.OrderId))
                    && restored.Select(x => x.HistoryJson).SequenceEqual(_orders.Select(x => x.HistoryJson))
                    && restored.Select(x => x.PlanHash).SequenceEqual(_orders.Select(x => x.PlanHash))
                    && restored.Select(x => x.CurrentStepIndex).SequenceEqual(_orders.Select(x => x.CurrentStepIndex))
                    && restored.Select(x => x.Steps?.Count ?? 0).SequenceEqual(_orders.Select(x => x.Steps?.Count ?? 0));
            }
            catch { return false; }
        }
    }
}
