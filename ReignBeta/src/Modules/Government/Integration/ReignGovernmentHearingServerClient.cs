using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using static ReignBeta.Platform.Integration.ReignServerClient;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Government.Integration
{
    internal static class ReignGovernmentHearingServerClient
    {
        internal static Task<JObject> RequestGovernmentCrownDecisionAsync(JObject context)
        {
            var payload = (JObject)context.DeepClone();
            payload["campaignId"] = GetCampaignId();
            payload["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main";
            return PostJsonAsync("/government/crown/decision", payload);
        }

        internal static Task<JObject> RequestGovernmentHearingDiscussionAsync(JObject context)
        {
            var payload = (JObject)context.DeepClone();
            payload["campaignId"] = GetCampaignId();
            payload["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main";
            payload["correlationId"] = "government_hearing_" + Guid.NewGuid().ToString("N");
            return PostJsonAsync("/government/hearing/discussion", payload);
        }
    }
}
