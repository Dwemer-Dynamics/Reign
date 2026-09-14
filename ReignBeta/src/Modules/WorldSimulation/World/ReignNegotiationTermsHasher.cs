using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.World
{
    internal static class ReignNegotiationTermsHasher
    {
        public static string Compute(string command, string termsJson)
        {
            JObject terms;
            try { terms = string.IsNullOrWhiteSpace(termsJson) ? new JObject() : JObject.Parse(termsJson); }
            catch { return string.Empty; }
            terms.Remove("authorizationMode");
            terms.Remove("negotiationId");
            terms.Remove("termsHash");
            string canonical = Canonical(terms);
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes((command ?? string.Empty) + "|" + canonical)).Select(x => x.ToString("x2")));
        }

        private static string Canonical(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "null";
            if (token is JObject obj)
                return "{" + string.Join(",", obj.Properties().OrderBy(x => x.Name, StringComparer.Ordinal)
                    .Select(x => JsonConvert.SerializeObject(x.Name) + ":" + Canonical(x.Value))) + "}";
            if (token is JArray array) return "[" + string.Join(",", array.Select(Canonical)) + "]";
            return token.ToString(Formatting.None);
        }
    }
}
