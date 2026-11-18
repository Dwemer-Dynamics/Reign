using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ReignBeta.WorldSimulation.World
{
    internal static class ReignWorldParsing
    {
        internal static JObject ParseTerms(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            try { return JObject.Parse(json); }
            catch { return new JObject(); }
        }

        internal static IEnumerable<string> SplitCsv(string value)
        {
            return (value ?? string.Empty)
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
        }
    }
}
