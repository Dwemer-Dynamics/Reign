using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    public static partial class ReignCourtServerClient
    {
        internal static Task<ReignCourtServerResponse> InternationalCourtPendingAsync(JObject world)
        {
            JObject payload = BasePayload(string.Empty, 0);
            payload["worldSnapshot"] = world;
            return PostAsync("/court/life/international/pending", payload);
        }
        internal static Task<ReignCourtServerResponse> InternationalCourtResolveAsync(JObject receipt)
        {
            JObject payload = BasePayload(receipt?.Value<string>("receiptId") ?? string.Empty, 0);
            foreach (JProperty property in receipt?.Properties() ?? System.Linq.Enumerable.Empty<JProperty>())
                payload[property.Name] = property.Value.DeepClone();
            return PostAsync("/court/life/international/resolve", payload);
        }
        internal static Task<ReignCourtServerResponse> InternationalCourtReferAsync(string matterId, string postingId, JObject candidate)
        {
            JObject payload = BasePayload("refer_" + matterId, 0);
            payload["matterId"] = matterId;
            payload["postingId"] = postingId;
            payload["candidate"] = candidate;
            return PostAsync("/court/life/international/refer", payload);
        }
    }
}
