using System;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using static ReignBeta.Platform.Integration.ReignServerClient;
using ReignBeta.Characters.Campaign;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Characters.Integration
{
    internal static class ReignWandererClient
    {
        internal static Task<JObject> ReadWandererContactAsync(string campaign, string timeline, string hero, string player)
            => PostJsonAsync("/dialogue/history", new JObject { ["campaignId"] = campaign, ["timelineId"] = timeline,
                ["heroStringId"] = hero, ["playerHeroStringId"] = player, ["wandererContactOnly"] = true });

        internal static async Task<JObject> PostWandererConversationAsync(string route, JObject payload, Hero hero, string playerText,
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
                owner = ReignServices.WandererPopulation;
                campaign = GetCampaignId(); timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
                if (string.IsNullOrWhiteSpace(payload.Value<string>("sceneTurnId"))) payload["sceneTurnId"] = Guid.NewGuid().ToString("N");
                payload["nativePaymentReceipts"] = ReignServices.GoldTransferService?.Snapshot(hero) ?? new JArray();
                payload["nativeActionReceipts"] = ReignServices.AI?.ConversationActionReceipts(hero) ?? new JArray();
                ReignStructuredActionClient.ConfigureStructuredActions(payload, hero, actionDraft, Reign.Core.Contracts.Actions.AgreementChannel.InPerson);
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
                    if (!fence.IsCurrent() || campaign != GetCampaignId() || timeline != (ReignServices.WorldHistory?.TimelineId ?? "main")
                        || owner != ReignServices.WandererPopulation)
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
                    // The accepted NPC reply bytes stay exact. The structured receipt and
                    // native outcomes are kept as separate fields for system-line presentation.
                    ReignStructuredActionClient.ProcessStructuredActionDecision(payload, response, hero, inlineReceipt: false);
                    // Parsed with this request's own fence, so a response cannot rebind to a newer load.
                    var actions = new[] { "queuedDialogueActions", "queuedEventActions", "queuedActions" }
                        .SelectMany(key => ParseQueuedActionRows(response?[key], fence)).GroupBy(a => a.ActionId).Select(g => g.First())
                        .OrderBy(a => a.ActionId.StartsWith("pa_", StringComparison.Ordinal) ? 0 : 1).ToList();
                    if (actions.Count == 0) return;
                    var behavior = ReignServices.AI;
                    var results = new JArray();
                    string heldActionId = null;
                    if (behavior == null || behavior.TryImportForPresentation(actions, out _, out heldActionId))
                    {
                        foreach (var action in actions)
                        {
                            var result = behavior?.GetLastActionResult(action.ActionId);
                            string status = result == null ? "pending"
                                : result.ResultCode == "payment_unknown" || result.ResultCode == ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.UncertainExecutionResultCode ? "unknown"
                                : result.Success && result.Completed ? "applied" : result.Success ? "pending" : "failed";
                            string message = result?.Message ?? "Native execution is pending.";
                            results.Add(new JObject { ["actionId"] = action.ActionId, ["status"] = status, ["message"] = message });
                        }
                    }
                    else if (heldActionId != null)
                    {
                        // Only the exact row whose native execution was interrupted is labelled;
                        // conflicting replays and unattempted neighbors claim no result.
                        results.Add(new JObject { ["actionId"] = heldActionId, ["status"] = "unknown",
                            ["message"] = ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.UncertainExecutionMarker });
                    }
                    response["nativeActionOutcomes"] = results;
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
                    if (owner != ReignServices.WandererPopulation || campaign != GetCampaignId()
                        || timeline != (ReignServices.WorldHistory?.TimelineId ?? "main")) return;
                    // A stale owner settles the contact as uncertain rather than
                    // as a protected (presented) exchange.
                    string receipt = (string)(accepted ?? response)?["conversationExchange"]?["exchangeId"];
                    bool success = !staleRequestOwner && response?.Value<bool?>("ok") == true
                        && (!string.IsNullOrWhiteSpace(response.Value<string>("reply")) || ReignStructuredActionClient.HasNativeStatus(response));
                    owner?.EndContact(hero, success, string.IsNullOrWhiteSpace(receipt)
                        ? "reign-chat:" + ((string)payload["correlationId"] ?? Guid.NewGuid().ToString("N")) : receipt, playerText);
                }).ConfigureAwait(false);
            }
        }
    }
}
