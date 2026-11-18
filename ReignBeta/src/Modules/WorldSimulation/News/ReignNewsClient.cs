using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.World;
using static ReignBeta.Integration.ReignServerClient;

namespace ReignBeta.Integration
{
    internal static class ReignNewsClient
    {
        internal static ReignNewsOptions NewsOptions { get; private set; } = new ReignNewsOptions();
        internal static bool NewsOptionsConfirmed { get; private set; }
        internal static void SetNewsOptions(JObject values)
        {
            NewsOptions = new ReignNewsOptions(key => values?[key]?.Type != JTokenType.Boolean || values.Value<bool>(key));
            NewsOptionsConfirmed = true;
            Campaign.ReignNewsCampaignBehavior.Instance?.ApplyOptions();
        }
    }
}
