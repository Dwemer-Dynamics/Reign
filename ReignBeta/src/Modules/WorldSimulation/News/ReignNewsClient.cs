using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.World;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static ReignNewsOptions NewsOptions { get; private set; } = new ReignNewsOptions();
        internal static bool NewsOptionsConfirmed { get; private set; }
        private static void SetNewsOptions(JObject values)
        {
            NewsOptions = new ReignNewsOptions(key => values?[key]?.Type != JTokenType.Boolean || values.Value<bool>(key));
            NewsOptionsConfirmed = true;
            Campaign.ReignNewsCampaignBehavior.Instance?.ApplyOptions();
        }
    }
}
