using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using static ReignBeta.Platform.Integration.ReignServerClient;

namespace ReignBeta.Government.Integration
{
    internal static class ReignGovernmentHearingServerClient
    {
        internal static Task<JObject> RequestGovernmentCrownDecisionAsync(JObject context)
        {
            var payload = (JObject)context.DeepClone();
            payload["campaignId"] = GetCampaignId();
            payload["timelineId"] = ReignBeta.WorldSimulation.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            return PostJsonAsync("/government/crown/decision", payload);
        }

        internal static Task<JObject> RequestGovernmentHearingDiscussionAsync(JObject context)
        {
            var payload = (JObject)context.DeepClone();
            payload["campaignId"] = GetCampaignId();
            payload["timelineId"] = ReignBeta.WorldSimulation.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            payload["correlationId"] = "government_hearing_" + Guid.NewGuid().ToString("N");
            return PostJsonAsync("/government/hearing/discussion", payload);
        }
    }
}
