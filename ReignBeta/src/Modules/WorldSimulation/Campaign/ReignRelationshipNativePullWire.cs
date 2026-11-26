#nullable enable
using System;
using System.Globalization;
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

        internal static bool IsPaidCourtPhase(string? phase)
            => phase == "court_life_paid_supply_preflight" || phase == "court_life_paid_supply_apply"
                || phase == "court_life_paid_supply_verify";

        internal static bool MatchesPaidCourtAcknowledgment(JObject? acknowledgment, JObject issued,
            string campaignId, string gameInstanceId, string processId, bool dispatch, DateTimeOffset now, JObject? nativeTerms = null)
        {
            if (acknowledgment?["ok"]?.Type != JTokenType.Boolean || acknowledgment.Value<bool>("ok") != true
                || (acknowledgment["idempotent"] is JToken duplicate
                    && (duplicate.Type != JTokenType.Boolean || duplicate.Value<bool>()))
                || !(acknowledgment["command"] is JObject accepted) || !(acknowledgment["run"] is JObject run))
                return false;
            string? runId = PaidCourtText(issued, "runId");
            string? commandId = PaidCourtText(issued, "commandId");
            string? phase = PaidCourtText(issued, "phase");
            if (!IsPaidCourtIdentifier(runId) || !IsPaidCourtIdentifier(commandId) || !IsPaidCourtPhase(phase)
                || !Guid.TryParseExact(PaidCourtText(issued, "leaseId"), "N", out _) || PaidCourtText(issued, "status") != "leased"
                || !Guid.TryParseExact(processId, "N", out _) || PaidCourtText(issued, "paidCourtLeasedProcessId") != processId
                || PaidCourtText(accepted, "paidCourtLeasedProcessId") != processId
                || PaidCourtText(accepted, "paidCourtAcceptedProcessId") != processId
                || accepted["paidCourtAuthorityVersion"]?.Type != JTokenType.Integer
                || accepted.Value<long>("paidCourtAuthorityVersion") != 1
                || accepted["paidCourtNativeDispatch"]?.Type != JTokenType.Boolean
                || accepted.Value<bool>("paidCourtNativeDispatch") != dispatch
                || (dispatch && (phase != "court_life_paid_supply_apply" || nativeTerms == null
                    || !JToken.DeepEquals(accepted["paidCourtNativeDispatchTerms"], nativeTerms)))
                || !TryPaidCourtLeaseWindow(issued, out var deadline) || !TryPaidCourtLeaseWindow(accepted, out var ackDeadline)
                || deadline != ackDeadline || now >= deadline
                || PaidCourtText(issued, "operation") != "ruler_docket_test"
                || PaidCourtText(issued, "campaignId") != campaignId || PaidCourtText(issued, "gameInstanceId") != gameInstanceId
                || PaidCourtText(run, "runId") != runId || PaidCourtText(run, "status") != "running"
                || PaidCourtText(run, "mode") != "spymaster" || PaidCourtText(run, "campaignId") != campaignId
                || PaidCourtText(run, "gameInstanceId") != gameInstanceId || PaidCourtText(accepted, "status") != (dispatch ? "running" : "accepted"))
                return false;
            foreach (string key in new[] { "runId", "commandId", "leaseId", "operation", "phase", "fixtureRunId",
                "campaignTestRunId", "expectedSaveName", "paidSupplyTargetSettlementId", "campaignId", "gameInstanceId",
                "expectedPaidTimelineId", "expectedPaidSavePointId" })
            {
                string? value = PaidCourtText(issued, key);
                if (value == null || (value.Length == 0 && key != "paidSupplyTargetSettlementId")
                    || value != PaidCourtText(accepted, key))
                    return false;
            }
            return true;
        }

        internal const int PaidCourtLeaseSeconds = 45;

        internal static bool TryPaidCourtLeaseWindow(JObject issued, out DateTimeOffset expires)
        {
            expires = default;
            return TryPaidCourtTime(issued["leaseStartedUtc"], out var started)
                && TryPaidCourtTime(issued["leaseExpiresUtc"], out expires)
                && expires - started == TimeSpan.FromSeconds(PaidCourtLeaseSeconds);
        }

        private static bool TryPaidCourtTime(JToken? token, out DateTimeOffset time)
        {
            time = default;
            if (token is JValue date && date.Type == JTokenType.Date)
            {
                if (date.Value is DateTimeOffset offset) { time = offset; return true; }
                if (date.Value is DateTime utc && utc.Kind != DateTimeKind.Unspecified)
                { time = new DateTimeOffset(utc); return true; }
            }
            return token?.Type == JTokenType.String && DateTimeOffset.TryParseExact(token.Value<string>(),
                "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }

        internal static bool MatchesPaidCourtGrantBinding(JObject grant, JObject command, JObject owner, long now)
        {
            string? Text(string name) => PaidCourtText(grant, name);
            bool Binds(string field, string issuedField) =>
                Text(field) is string value && value == PaidCourtText(command, issuedField);
            if (grant.Count != 16 || Text("schema") != "reign-court-paid-supply-admission-v1"
                || grant["issuedUtc"]?.Type != JTokenType.Integer || grant["expiresUtc"]?.Type != JTokenType.Integer
                || !Binds("runId", "runId") || !Binds("commandId", "commandId") || !Binds("phase", "phase")
                || !IsPaidCourtPhase(Text("phase")) || !Binds("fixtureRunId", "fixtureRunId")
                || !Binds("targetSettlementId", "paidSupplyTargetSettlementId")
                || !Binds("campaignTestRunId", "campaignTestRunId") || !Binds("saveName", "expectedSaveName")
                || !Binds("checkpointSavePointId", "expectedPaidSavePointId")
                || string.IsNullOrEmpty(PaidCourtText(owner, "timelineId"))
                || PaidCourtText(owner, "timelineId") != PaidCourtText(command, "expectedPaidTimelineId")
                || !Guid.TryParseExact(Text("checkpointAttemptId"), "N", out _)
                || !JToken.DeepEquals(grant["owner"], owner))
                return false;
            long issued = grant.Value<long>("issuedUtc"), expires = grant.Value<long>("expiresUtc");
            return issued > now - 120 && issued <= now && expires > now && expires == issued + 120
                && (Text("phase") == "court_life_paid_supply_preflight") == string.IsNullOrEmpty(Text("targetSettlementId"));
        }

        internal static string? PaidCourtText(JObject value, string key)
            => value[key]?.Type == JTokenType.String ? value.Value<string>(key) : null;

        internal static bool IsPaidCourtIdentifier(string? value)
            => value != null && value.StartsWith("court_paid_", StringComparison.Ordinal)
                && value.Length == 43 && Guid.TryParseExact(value.Substring(11), "N", out _);

        internal static string? LoadIdentitySha256(string? value)
        {
            if (value == null || value.Length == 0) return null;
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
        }
    }
}
