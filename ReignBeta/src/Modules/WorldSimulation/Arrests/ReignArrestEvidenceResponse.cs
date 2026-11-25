#nullable enable

using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ReignBeta.WorldSimulation.Arrests
{
    internal static class ReignArrestEvidenceResponse
    {
        internal const string RequestSchema = "reign-arrest-evidence-request-v2";
        internal const string ResponseSchema = "reign-arrest-evidence-response-v2";

        internal static bool TryRead(JObject request, JObject? response, out string evidenceId)
        {
            evidenceId = string.Empty;
            string? campaignId = ExactText(request, "campaignId");
            string? timelineId = ExactText(request, "timelineId");
            string? accusedId = ExactText(request, "accusedHeroId");
            if (response == null || campaignId == null || timelineId == null || accusedId == null
                || ExactText(request, "schema") != RequestSchema || ExactText(request, "chargeCategory") != "espionage"
                || ExactText(response, "schema") != ResponseSchema || ExactText(response, "chargeCategory") != "espionage"
                || ExactText(response, "campaignId") != campaignId || ExactText(response, "timelineId") != timelineId
                || ExactText(response, "accusedHeroId") != accusedId || ExactText(response, "evidenceStatus") != "established"
                || !ExactBoolean(response, "ok", true) || !ExactBoolean(response, "causeEstablished", true)
                || !ExactBoolean(response, "hiddenAgentInformationRevealed", false)
                || !(response["evidence"] is JArray evidence) || evidence.Count != 1 || !(evidence[0] is JObject row)
                || ExactText(row, "kind") != "foreign_agent_exposure"
                || ExactText(row, "subjectRole") != "agent" || ExactText(row, "subjectHeroId") != accusedId)
                return false;

            string? id = ExactText(row, "evidenceId");
            string prefix = "foreign_agent:" + accusedId + ":";
            if (id == null || !id.StartsWith(prefix, StringComparison.Ordinal) || id.Length <= prefix.Length
                || id.Contains(",") || !(response["evidenceIds"] is JArray ids) || ids.Count != 1
                || ids[0].Type != JTokenType.String || (string?)ids[0] != id)
                return false;

            evidenceId = id;
            return true;
        }

        private static string? ExactText(JObject value, string key)
        {
            if (!(value?[key] is JValue token) || token.Type != JTokenType.String)
                return null;
            string? text = (string?)token;
            return text != null && !string.IsNullOrWhiteSpace(text) && text.Length <= 8192 && text == text.Trim()
                && !text.Any(char.IsControl) ? text : null;
        }

        private static bool ExactBoolean(JObject value, string key, bool expected) =>
            value[key] is JValue token && token.Type == JTokenType.Boolean && (bool)token == expected;
    }
}
