using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static Task<JObject> PostFamilyVisitAsync(JObject payload) => PostJsonAsync("/court/life/family", payload);
    }
}
