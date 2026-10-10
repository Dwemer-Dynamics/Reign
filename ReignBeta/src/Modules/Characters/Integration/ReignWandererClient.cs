using System;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static Task<JObject> ReadWandererContactAsync(string campaign, string timeline, string hero, string player)
            => PostJsonAsync("/dialogue/history", new JObject { ["campaignId"] = campaign, ["timelineId"] = timeline,
                ["heroStringId"] = hero, ["playerHeroStringId"] = player, ["wandererContactOnly"] = true });

        private static async Task<JObject> PostWandererConversationAsync(string route, JObject payload, Hero hero, string playerText,
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null)
        {
            ReignWandererPopulationCampaignBehavior owner = null;
            string campaign = "", timeline = "";
            await ReignMainThread.InvokeAsync(() => {
                owner = ReignWandererPopulationCampaignBehavior.Instance;
                campaign = GetCampaignId(); timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                if (string.IsNullOrWhiteSpace(payload.Value<string>("sceneTurnId"))) payload["sceneTurnId"] = Guid.NewGuid().ToString("N");
                payload["nativePaymentReceipts"] = ReignGoldTransferService.Instance?.Snapshot(hero) ?? new JArray();
                payload["nativeActionReceipts"] = ReignAICampaignBehavior.Instance?.ConversationActionReceipts(hero) ?? new JArray();
                ConfigureStructuredActions(payload, hero, actionDraft);
                owner?.BeginContact(hero, playerText);
            }).ConfigureAwait(false);
            JObject response = null;
            try
            {
                response = await PostJsonAsync(route, payload).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => {
                    if (campaign != GetCampaignId() || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                        || owner != ReignWandererPopulationCampaignBehavior.Instance)
                    {
                        response = new JObject { ["ok"] = false, ["error"] = "The campaign changed while this conversation was in progress." }; return;
                    }
                    ProcessStructuredActionDecision(payload, response, hero);
                    var actions = new[] { "queuedDialogueActions", "queuedEventActions", "queuedActions" }
                        .SelectMany(key => ParseQueuedActionRows(response?[key])).GroupBy(a => a.ActionId).Select(g => g.First())
                        .OrderBy(a => a.ActionId.StartsWith("pa_", StringComparison.Ordinal) ? 0 : 1).ToList();
                    if (actions.Count == 0) return;
                    var behavior = ReignAICampaignBehavior.Instance;
                    behavior?.EnqueueAndExecuteServerActions(actions);
                    var results = new JArray();
                    foreach (var action in actions)
                    {
                        var result = behavior?.GetLastActionResult(action.ActionId);
                        string status = result == null ? "pending" : result.ResultCode == "payment_unknown" ? "unknown" : result.Success && result.Completed ? "applied" : result.Success ? "pending" : "failed";
                        string message = result?.Message ?? "Native execution is pending.";
                        results.Add(new JObject { ["actionId"] = action.ActionId, ["status"] = status, ["message"] = message });
                    }
                    response["nativeActionOutcomes"] = results;
                    response["reply"] = (response.Value<string>("reply") ?? "") + "\n\n" + string.Join("\n", results.Select(r => "[" + r.Value<string>("status") + ": " + r.Value<string>("message") + "]"));
                }).ConfigureAwait(false);
                return response;
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() => {
                    if (owner != ReignWandererPopulationCampaignBehavior.Instance || campaign != GetCampaignId()
                        || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")) return;
                    string receipt = (string)response?["conversationExchange"]?["exchangeId"];
                    bool success = response?.Value<bool?>("ok") == true && !string.IsNullOrWhiteSpace(response.Value<string>("reply"));
                    owner?.EndContact(hero, success, string.IsNullOrWhiteSpace(receipt)
                        ? "reign-chat:" + ((string)payload["correlationId"] ?? Guid.NewGuid().ToString("N")) : receipt, playerText);
                }).ConfigureAwait(false);
            }
        }
    }
}
