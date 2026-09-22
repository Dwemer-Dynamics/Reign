using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.Runtime;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static readonly HashSet<string> RebellionTestProfiles = new HashSet<string>(
            new[] { "smoke", "feature", "pledges", "reporting", "summons", "verdicts",
                "declaration", "save_prepare", "save_verify", "resolution", "native_setup",
                "language_observe", "travel_prepare", "travel_verify", "mixed_transfer",
                "foreign_reintegration", "cleanup" },
            StringComparer.OrdinalIgnoreCase);

        private static async Task<LiveCommandResult> ExecuteRebellionPreparationTestAsync(JObject command)
        {
            Stopwatch timer = Stopwatch.StartNew();
            string profile = (command?.Value<string>("profile") ?? "smoke").Trim().ToLowerInvariant();
            if (!RebellionTestProfiles.Contains(profile))
                return LiveCommandResult.Failed("Unsupported rebellion profile '" + profile + "'.");
            JObject data = await ReignMainThread.InvokeAsync(() =>
                BuildRebellionPreparationTestSnapshot(profile, command)).ConfigureAwait(false);
            timer.Stop();
            data["durationMilliseconds"] = timer.ElapsedMilliseconds;
            return data.Value<bool?>("passed") == true
                ? LiveCommandResult.Completed("Rebellion " + profile + " profile passed.", data)
                : LiveCommandResult.Failed(data.Value<string>("error") ?? "Rebellion assertions failed.", data);
        }

        private static JObject BuildRebellionPreparationTestSnapshot(string profile, JObject command)
        {
            ReignRebellionCampaignBehavior behavior = ReignRebellionCampaignBehavior.Instance;
            if (behavior == null)
                return new JObject
                {
                    ["schemaVersion"] = 2,
                    ["scenario"] = "rebellion_release_acceptance",
                    ["profile"] = profile,
                    ["passed"] = false,
                    ["assertions"] = new JArray(new JObject
                    {
                        ["name"] = "behavior_registered", ["passed"] = false,
                        ["detail"] = "The saved rebellion behavior must be registered."
                    }),
                    ["error"] = "The saved rebellion behavior is unavailable."
                };
            return behavior.RunPreparationHarnessProfile(profile,
                command?.Value<string>("fixtureRunId") ?? "rebellion_release_matrix",
                command?.Value<string>("disposableSaveName") ?? string.Empty,
                command);
        }
    }
}
