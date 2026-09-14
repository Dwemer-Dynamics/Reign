using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        public static async Task ReportCampaignOrderStateAsync(ReignCampaignOrderRecord order,
            string eventType)
        {
            if (order == null) return;
            try
            {
                await PostJsonAsync("/campaign-commands/report", JObject.FromObject(new
                {
                    campaignId = GetCampaignId(),
                    eventType = eventType ?? string.Empty,
                    order
                })).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Campaign command evidence report failed: " + ex.Message);
            }
        }

        public static async Task RequestCampaignOrderJudgmentAsync(ReignCampaignOrderRecord order,
            JObject state)
        {
            if (order == null || state == null) return;
            try
            {
                state["campaignId"] = GetCampaignId();
                state["correlationId"] = order.CorrelationId;
                JObject response = await PostJsonAsync("/campaign-commands/judge", state)
                    .ConfigureAwait(false);
                string outcome = response.Value<string>("outcome") ?? string.Empty;
                string reason = response.Value<string>("reason") ?? string.Empty;
                if (!ReignCampaignCommandJudgment.IsAllowedOutcome(outcome)) return;
                await ReignMainThread.InvokeAsync(() =>
                    ReignCampaignCommandBehavior.Instance?.ApplyLlmJudgment(order.OrderId,
                        outcome, reason)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Exceptional judgment is an advisory enhancement. Native state and
                // the deterministic policy remain authoritative during provider loss.
                ReignLog.Warn("Campaign command LLM advisory unavailable: " + ex.Message);
            }
        }

        public static async Task SendCampaignOrderReportLetterAsync(ReignCampaignOrderRecord order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.PendingReportId)
                || string.IsNullOrWhiteSpace(order.PendingReportText)) return;
            try
            {
                Hero sender = ReignObjectResolver.FindHero(order.CommanderHeroStringId);
                Hero recipient = Hero.MainHero;
                if (sender == null || recipient == null) return;
                double day = TaleWorlds.CampaignSystem.Campaign.Current == null
                    ? 0d : CampaignTime.Now.ToDays;
                JObject response = await PostJsonAsync("/correspondence/send", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["senderId"] = sender.StringId ?? string.Empty,
                    ["senderName"] = sender.Name?.ToString() ?? "Commander",
                    ["recipientId"] = recipient.StringId ?? string.Empty,
                    ["recipientName"] = recipient.Name?.ToString() ?? "Sovereign",
                    ["body"] = order.PendingReportText,
                    ["source"] = "campaign_command_urgent_report",
                    ["reason"] = order.PendingReportId,
                    ["dispatchDay"] = day,
                    ["deliveryDay"] = day,
                    ["originId"] = sender.CurrentSettlement?.StringId ?? string.Empty,
                    ["destinationId"] = recipient.CurrentSettlement?.StringId ?? string.Empty,
                    ["correlationId"] = order.CorrelationId ?? string.Empty,
                    ["orderId"] = order.OrderId ?? string.Empty,
                    ["reportId"] = order.PendingReportId
                }).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true)
                    ReignLog.Warn("Campaign command report letter was not accepted: "
                        + (response.Value<string>("error") ?? "unknown error"));
            }
            catch (Exception ex)
            {
                // The immediate in-game report remains authoritative and is shown even
                // when the correspondence service is temporarily unavailable.
                ReignLog.Warn("Campaign command report letter fallback used: " + ex.Message);
            }
        }

        public static Task<JObject> VerifyCampaignCommandSaveAsync(JObject payload)
        {
            payload = payload ?? new JObject();
            payload["campaignId"] = GetCampaignId();
            return PostJsonAsync("/tests/campaign-command/save/verify", payload);
        }

        public static Task<JObject> ReportCampaignCommandProfileAsync(JObject payload)
        {
            payload = payload ?? new JObject();
            payload["campaignId"] = GetCampaignId();
            return PostJsonAsync("/tests/campaign-command/profile/result", payload);
        }

        public static Task<JObject> StartCampaignCommandVerificationAsync(string tier, int seed)
        {
            return PostJsonAsync("/verification/run", new JObject
            {
                ["tier"] = tier, ["suite"] = "campaign_command", ["seed"] = seed,
                ["repeat"] = 1, ["failFast"] = true,
                ["liveCaseCap"] = tier == "live-llm" ? 300 : 20,
                ["requestedBy"] = "Campaign Command armed live harness"
            });
        }

        public static Task<JObject> GetCampaignCommandVerificationStatusAsync()
        {
            return GetJsonAsync("/verification/status");
        }

        public static Task<JObject> GetCampaignCommandVerificationResultAsync(string runId)
        {
            return GetJsonAsync("/verification/results?runId=" + Uri.EscapeDataString(runId ?? string.Empty));
        }
    }
}
