using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    internal static class ReignInitializationServerClient
    {
        internal static Task<JObject> ReadStatusAsync(string campaignId,
            string timelineId, string generationId)
        {
            return ReignServerClient.PostJsonAsync(
                "/initialization/readiness/status",
                new JObject
                {
                    ["campaignId"] = campaignId ?? string.Empty,
                    ["timelineId"] = timelineId ?? "main",
                    ["generationId"] = generationId ?? string.Empty
                });
        }

        internal static Task<JObject> RetryAsync(string campaignId,
            string timelineId, string generationId)
        {
            return ReignServerClient.PostJsonAsync(
                "/initialization/readiness/retry",
                new JObject
                {
                    ["campaignId"] = campaignId ?? string.Empty,
                    ["timelineId"] = timelineId ?? "main",
                    ["generationId"] = generationId ?? string.Empty
                });
        }
        internal static async Task<JObject> AcknowledgeSealAsync(
            string campaignId,
            string timelineId,
            string generationId,
            long expectedHistorySequence,
            string relationshipPlanId)
        {
            JObject response = await ReignServerClient.PostJsonAsync(
                "/initialization/readiness/seal",
                new JObject
                {
                    ["campaignId"] = campaignId ?? string.Empty,
                    ["timelineId"] = timelineId ?? string.Empty,
                    ["generationId"] = generationId ?? string.Empty,
                    ["sealVersion"] = ReignCampaignReadinessState.CurrentSealVersion,
                    ["expectedHistorySequence"] = expectedHistorySequence,
                    ["relationshipPlanId"] = relationshipPlanId ?? string.Empty
                }).ConfigureAwait(false);
            if (response?.Value<bool?>("ok") != true)
                throw new InvalidOperationException(
                    response?.Value<string>("error")
                    ?? "The server did not acknowledge campaign readiness.");
            return response;
        }
    }
}
