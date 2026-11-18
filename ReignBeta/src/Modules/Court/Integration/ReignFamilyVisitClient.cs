using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using static ReignBeta.Integration.ReignServerClient;

namespace ReignBeta.Integration
{
    internal static class ReignFamilyVisitClient
    {
        internal static Task<JObject> PostFamilyVisitAsync(JObject payload) => PostJsonAsync("/court/life/family", payload);
    }
}
