using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Government;
using ReignBeta.Integration;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> ExecuteGovernmentTestAsync(JObject command)
        {
            ReignGovernmentCampaignBehavior behavior = ReignGovernmentCampaignBehavior.Instance;
            if (behavior == null) return LiveCommandResult.Failed("The production Government behavior is unavailable.");
            string runId = command.Value<string>("fixtureRunId")
                ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string profile = command.Value<string>("profile")
                ?? command.Value<string>("phase") ?? "preflight";
            JObject result = await ReignMainThread.InvokeAsync(() =>
                behavior.RunGovernmentTestProfile(runId, profile, command, GameInstanceId))
                .ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The Government feature-harness profile completed.", result)
                : LiveCommandResult.Failed("The Government feature-harness profile failed its current gate.", result);
        }
    }
}
