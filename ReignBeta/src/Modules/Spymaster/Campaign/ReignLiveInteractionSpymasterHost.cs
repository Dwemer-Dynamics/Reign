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
        private static async Task<LiveCommandResult> ExecuteSpymasterTestAsync(JObject command)
        {
            ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
            if (court == null) return LiveCommandResult.Failed("The production court controller is unavailable.");
            string runId = command.Value<string>("fixtureRunId") ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string profile = command.Value<string>("profile") ?? command.Value<string>("text") ?? "feature";
            int seed = command.Value<int?>("seed") ?? 147147;
            JObject result = await ReignMainThread.InvokeAsync(() => court.RunSpymasterInGameProfile(runId, profile, seed)).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The deterministic Spymaster in-game profile completed.", result)
                : LiveCommandResult.Failed("One or more deterministic Spymaster assertions failed.", result);
        }

        private static async Task<LiveCommandResult> ExecuteOrganicSpymasterTestAsync(JObject command)
        {
            ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
            if (court == null) return LiveCommandResult.Failed("The production court controller is unavailable.");
            string runId = command.Value<string>("fixtureRunId") ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string phase = command.Value<string>("phase") ?? command.Value<string>("profile") ?? command.Value<string>("text") ?? "preflight";
            JObject result = await ReignMainThread.InvokeAsync(() => court.RunSpymasterOrganicProfile(runId, phase, command)).ConfigureAwait(false);
            JObject agentRequest = result["agentRequest"] as JObject;
            if (agentRequest != null)
            {
                try
                {
                    JObject serverStatus = await ReignSpymasterServerClient.GetForeignAgentStatusAsync(agentRequest).ConfigureAwait(false);
                    result["serverAgentStatus"] = serverStatus;
                    if (serverStatus?.Value<bool?>("ok") != true && result.Value<bool?>("ok") == true)
                    {
                        result["ok"] = false;
                        result["error"] = "The server-side foreign-agent status projection was unavailable.";
                    }
                }
                catch (Exception ex)
                {
                    result["serverAgentStatus"] = new JObject { ["ok"] = false, ["error"] = ex.Message };
                    if (result.Value<bool?>("ok") == true)
                    {
                        result["ok"] = false;
                        result["error"] = "The server-side foreign-agent status projection failed: " + ex.Message;
                    }
                }
            }
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The organic Spymaster acceptance phase completed.", result)
                : LiveCommandResult.Failed("The organic Spymaster acceptance phase did not meet its current gate.", result);
        }
    }
}
#endif
