#if !REIGN_EXCLUDE_COURT
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> ExecuteCapitalAmbassadorTestAsync(JObject command)
        {
            ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
            if (court == null) return LiveCommandResult.Failed("The production court controller is unavailable.");
            string runId = command.Value<string>("fixtureRunId") ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string phase = (command.Value<string>("phase") ?? command.Value<string>("profile") ?? command.Value<string>("text") ?? "preflight").Trim().ToLowerInvariant();
            JObject result;
            try
            {
                if (phase == "prepare")
                    result = await court.PrepareCapitalAmbassadorFixtureAsync(runId, command).ConfigureAwait(false);
                else if (phase == "server_matrix")
                {
                    ReignCourtServerResponse response = await ReignCourtServerClient.RunCapitalAmbassadorServerMatrixAsync(runId).ConfigureAwait(false);
                    result = response.Raw ?? new JObject();
                    if (result.Property("ok") == null) result["ok"] = response.Ok;
                    if (!response.Ok && result.Property("error") == null) result["error"] = response.Error;
                }
                else
                    result = await ReignMainThread.InvokeAsync(() => court.RunCapitalAmbassadorTestProfile(runId, phase, command)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new JObject
                {
                    ["ok"] = false,
                    ["runId"] = runId,
                    ["profile"] = "capital_ambassador",
                    ["phase"] = phase,
                    ["error"] = ex.Message
                };
            }
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The capital/ambassador acceptance phase completed.", result)
                : LiveCommandResult.Failed("The capital/ambassador acceptance phase did not meet its gate.", result);
        }
    }
}
#endif
