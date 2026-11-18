using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using static ReignBeta.Integration.ReignServerClient;

namespace ReignBeta.Integration
{
    internal static class ReignClanAccordsClient
    {
        internal static Task<JObject> SyncClanAccordsAsync(JObject payload) => PostJsonAsync("/clan-accords/sync", payload);
    }
}
