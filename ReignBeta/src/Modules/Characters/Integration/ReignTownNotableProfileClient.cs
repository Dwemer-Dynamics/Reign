using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.Platform.Integration.ReignServerClient;

namespace ReignBeta.Characters.Integration
{
    internal static class ReignTownNotableProfileClient
    {
        internal static Task<JObject> PrewarmTownNotableProfilesAsync(Settlement town, Func<Task<bool>> current)
        {
            // Called only on the native application thread, before any await.
            var payload = new JObject { ["campaignId"] = GetCampaignId(), ["settlementId"] = town.StringId,
                ["settlementType"] = "town", ["capturedUtc"] = DateTime.UtcNow.ToString("o"), ["heroes"] = new JArray(town.Notables
                    .Where(h => h != null && h.IsAlive && h.IsNotable)
                    .GroupBy(h => h.StringId, StringComparer.OrdinalIgnoreCase).Select(g => BuildHeroProfile(g.First()))) };
            return SendTownProfilesAsync(payload, current);
        }

        private static async Task<JObject> SendTownProfilesAsync(JObject payload, Func<Task<bool>> current)
        {
            // A server-issued generation prevents an old in-transit submission
            // from becoming a fresh job after restoring the same campaign.
            var scope = await PostJsonAsync("/notables/profiles/prewarm", new JObject {
                ["campaignId"] = payload["campaignId"].DeepClone(), ["operation"] = "scope" }, current).ConfigureAwait(false);
            if (scope?.Value<bool?>("ok") != true || scope["generation"] == null) return scope;
            payload["generation"] = scope["generation"].DeepClone();
            var heroes = ((JArray)payload["heroes"]).ToList();
            JObject response = new JObject { ["ok"] = true, ["queued"] = 0 };
            for (int at = 0; at < heroes.Count; at += 128)
            {
                payload["heroes"] = new JArray(heroes.Skip(at).Take(128).Select(h => h.DeepClone()));
                response = await PostJsonAsync("/notables/profiles/prewarm", payload, current).ConfigureAwait(false);
                if (response?.Value<bool?>("ok") != true) return response;
            }
            return response;
        }
    }
}
