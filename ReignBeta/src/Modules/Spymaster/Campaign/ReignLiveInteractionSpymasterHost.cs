#if !REIGN_EXCLUDE_COURT
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

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
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                if (command.Value<bool?>("actionControl") != true)
                    return court.RunSpymasterOrganicProfile(runId, phase, command);
                JObject enrollment = command["enrollment"] as JObject;
                bool destructive = command.Value<bool?>("destructiveActionControl") == true;
                string source = destructive ? "reign_mcp_spymaster_destructive_action_control" : "reign_mcp_spymaster_action_control";
                string expected = enrollment?.Value<string>("disposableSaveName") ?? "";
                string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
                if (!_armed || enrollment == null || enrollment.Value<string>("source") != source
                    || destructive && (command.Value<string>("irreversibleConfirmation") != "run irreversible Reign Spymaster test on disposable save"
                        || command.Value<string>("controlConfirmation") != "control Reign Spymaster action test on disposable save")
                    || string.IsNullOrWhiteSpace(enrollment.Value<string>("campaignTestRunId"))
                    || expected.Length == 0 || baseline.Length == 0 || expected == baseline || expected != ActiveSaveName()
                    || enrollment.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                    || command.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                    || enrollment.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                    || command.Value<string>("gameInstanceId") != _gameInstanceId
                    || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop)
                    return new JObject { ["ok"] = false, ["error"] = "Spymaster action controls require the exact paused, armed, verified disposable Current campaign and timeline." };
                return destructive ? court.RunSpymasterDestructiveActionControlProfile(runId, phase, command)
                    : court.RunSpymasterActionControlProfile(runId, phase, command);
            }).ConfigureAwait(false);
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
