using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static Task<JObject> ReadWandererContactAsync(string campaign, string timeline, string hero, string player)
            => PostJsonAsync("/dialogue/history", new JObject { ["campaignId"] = campaign, ["timelineId"] = timeline,
                ["heroStringId"] = hero, ["playerHeroStringId"] = player, ["wandererContactOnly"] = true });

        private static async Task<JObject> PostWandererConversationAsync(string route, JObject payload, Hero hero, string playerText)
        {
            ReignWandererPopulationCampaignBehavior owner = null;
            string campaign = "", timeline = "";
            await ReignMainThread.InvokeAsync(() => {
                owner = ReignWandererPopulationCampaignBehavior.Instance;
                campaign = GetCampaignId(); timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                owner?.BeginContact(hero, playerText);
            }).ConfigureAwait(false);
            JObject response = null;
            try { response = await PostJsonAsync(route, payload).ConfigureAwait(false); return response; }
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
