#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ReignBeta.WorldSimulation.Campaign
{
    internal static class ReignRelationshipNativePullWire
    {
        internal static bool MatchesAccepted(JObject? acknowledgment, JObject issued, string campaignId, string gameInstanceId)
        {
            if (acknowledgment == null || acknowledgment.Value<bool?>("ok") != true || acknowledgment.Value<bool?>("idempotent") == true
                || !(acknowledgment["command"] is JObject accepted) || !(acknowledgment["run"] is JObject run))
                return false;
            return run.Value<string>("status") == "running" && run.Value<string>("mode") == "structured_actions"
                && run.Value<string>("campaignId") == campaignId && run.Value<string>("gameInstanceId") == gameInstanceId
                && run.Value<string>("runId") == issued.Value<string>("runId")
                && accepted.Value<string>("status") == "accepted"
                && accepted.Value<string>("runId") == issued.Value<string>("runId")
                && accepted.Value<string>("commandId") == issued.Value<string>("commandId")
                && !string.IsNullOrEmpty(issued.Value<string>("leaseId"))
                && accepted.Value<string>("leaseId") == issued.Value<string>("leaseId")
                && JToken.DeepEquals(accepted["testCase"], issued["testCase"])
                && JToken.DeepEquals(accepted["enrollment"], issued["enrollment"]);
        }

        internal static string? LoadIdentitySha256(string? value)
        {
            if (value == null || value.Length == 0) return null;
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
        }
    }
}
