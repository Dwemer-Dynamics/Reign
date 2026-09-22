using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static Task<JObject> RequestGovernmentHearingDiscussionAsync(JObject context)
        {
            var payload = (JObject)context.DeepClone();
            payload["campaignId"] = GetCampaignId();
            payload["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            payload["correlationId"] = "government_hearing_" + Guid.NewGuid().ToString("N");
            return PostJsonAsync("/government/hearing/discussion", payload);
        }
    }
}
