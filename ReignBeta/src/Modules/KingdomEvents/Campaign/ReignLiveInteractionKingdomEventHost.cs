using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> ExecuteKingdomEventTestAsync(JObject command)
        {
            ReignKingdomEventsCampaignBehavior behavior = ReignKingdomEventsCampaignBehavior.Instance;
            if (behavior == null) return LiveCommandResult.Failed("The production kingdom catastrophe/boon behavior is unavailable.");
            string runId = command.Value<string>("fixtureRunId") ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string phase = command.Value<string>("phase") ?? command.Value<string>("profile") ?? "preflight";
            JObject result = await ReignMainThread.InvokeAsync(() =>
                behavior.RunKingdomEventTestProfile(runId, phase, command)).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The forced production kingdom-event acceptance phase completed.", result)
                : LiveCommandResult.Failed("The kingdom-event acceptance phase failed its current gate.", result);
        }
    }
}
