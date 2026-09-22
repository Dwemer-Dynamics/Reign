using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bannerlord.EditorBridge;

internal static class BridgeJson
{
    internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None
    };

    internal static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

    internal static T? Deserialize<T>(string value) => JsonConvert.DeserializeObject<T>(value, Settings);
}
