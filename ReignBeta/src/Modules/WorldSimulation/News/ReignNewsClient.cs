using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.World;
using static ReignBeta.Platform.Integration.ReignServerClient;

namespace ReignBeta.WorldSimulation.News
{
    internal static class ReignNewsClient
    {
        internal static ReignNewsOptions NewsOptions { get; private set; } = new ReignNewsOptions();
        internal static bool NewsOptionsConfirmed { get; private set; }
        internal static void SetNewsOptions(JObject values)
        {
            NewsOptions = new ReignNewsOptions(key => values?[key]?.Type != JTokenType.Boolean || values.Value<bool>(key));
            NewsOptionsConfirmed = true;
            WorldSimulation.News.ReignNewsCampaignBehavior.Instance?.ApplyOptions();
        }
    }
}
