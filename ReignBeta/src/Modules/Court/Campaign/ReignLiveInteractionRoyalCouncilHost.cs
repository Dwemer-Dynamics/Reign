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
        private static async Task<LiveCommandResult> ExecuteRoyalCouncilTestAsync(JObject command)
        {
            ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
            if (court == null) return LiveCommandResult.Failed("The production court controller is unavailable.");
            string runId = command.Value<string>("fixtureRunId") ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string phase = (command.Value<string>("phase") ?? "save_prepare").Trim().ToLowerInvariant();
            JObject result;
            try
            {
                result = await ReignMainThread.InvokeAsync(() => court.RunRoyalCouncilTestProfile(runId, phase, command, GameInstanceId)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new JObject { ["ok"] = false, ["runId"] = runId, ["profile"] = "royal_council", ["phase"] = phase, ["error"] = ex.Message };
            }
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The Royal Council acceptance phase completed.", result)
                : LiveCommandResult.Failed("The Royal Council acceptance phase did not meet its gate.", result);
        }
    }
}
#endif
