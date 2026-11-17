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
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null, Func<bool> requestOwner = null)
        {
            ReignWandererPopulationCampaignBehavior owner = null;
            string campaign = "", timeline = "";
            bool staleRequestOwner = false;
            RequestFence fence = null;
            await ReignMainThread.InvokeAsync(() => {
                // Same game-thread callback as the structured-action capture and contact start.
                EnsureRequestOwner(requestOwner, "structured action capture");
                fence = RequestFence.Capture();
                owner = ReignWandererPopulationCampaignBehavior.Instance;
                campaign = GetCampaignId(); timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                if (string.IsNullOrWhiteSpace(payload.Value<string>("sceneTurnId"))) payload["sceneTurnId"] = Guid.NewGuid().ToString("N");
                payload["nativePaymentReceipts"] = ReignGoldTransferService.Instance?.Snapshot(hero) ?? new JArray();
                payload["nativeActionReceipts"] = ReignAICampaignBehavior.Instance?.ConversationActionReceipts(hero) ?? new JArray();
                ConfigureStructuredActions(payload, hero, actionDraft, Reign.Core.Contracts.Actions.AgreementChannel.InPerson);
                owner?.BeginContact(hero, playerText);
            }).ConfigureAwait(false);
            JObject response = null, accepted = null;
            try
            {
                response = requestOwner == null ? await PostJsonAsync(route, payload).ConfigureAwait(false)
                    : await PostOwnedJsonAsync(route, payload, requestOwner).ConfigureAwait(false);
                accepted = response;
                await ReignMainThread.InvokeAsync(() => {
                    // Stale callbacks keep the accepted raw response under
                    // acceptedResponse (durable reconciliation only) and skip every
                    // local native effect; ok=false claims no presentation.
                    if (!fence.IsCurrent() || campaign != GetCampaignId() || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                        || owner != ReignWandererPopulationCampaignBehavior.Instance)
                    {
                        staleRequestOwner = true;
                        response = new JObject { ["ok"] = false, ["error"] = "The campaign changed while this conversation was in progress.",
                            ["acceptedResponse"] = accepted }; return;
                    }
                    if (!RequestOwnerCurrent(requestOwner))
                    {
                        // The journal acknowledgement already ran inside the transport.
                        // A stale view imports no immediate actions; undelivered server
                        // actions remain in the existing outbox for redelivery.
                        staleRequestOwner = true;
                        response = new JObject { ["ok"] = false, ["error"] = "The conversation closed while this reply was in progress.",
                            ["acceptedResponse"] = accepted }; return;
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
                    // An old-load callback is skipped before it touches the current
                    // behavior or any Hero object. Within the same load the begun
                    // contact is always balanced, including after the view closed.
                    if (!fence.IsCurrent()) return;
                    if (owner != ReignWandererPopulationCampaignBehavior.Instance || campaign != GetCampaignId()
                        || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")) return;
                    // A stale owner settles the contact as uncertain rather than
                    // as a protected (presented) exchange.
                    string receipt = (string)(accepted ?? response)?["conversationExchange"]?["exchangeId"];
                    bool success = !staleRequestOwner && response?.Value<bool?>("ok") == true && !string.IsNullOrWhiteSpace(response.Value<string>("reply"));
                    owner?.EndContact(hero, success, string.IsNullOrWhiteSpace(receipt)
                        ? "reign-chat:" + ((string)payload["correlationId"] ?? Guid.NewGuid().ToString("N")) : receipt, playerText);
                }).ConfigureAwait(false);
            }
        }
    }
}
