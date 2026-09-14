using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static Task<JObject> SyncClanAccordsAsync(JObject payload) => PostJsonAsync("/clan-accords/sync", payload);
    }
}
