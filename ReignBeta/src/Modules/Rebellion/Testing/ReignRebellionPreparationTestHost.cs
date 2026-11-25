using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Campaign;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static readonly HashSet<string> RebellionTestProfiles = new HashSet<string>(
            new[] { "smoke", "feature", "pledges", "reporting", "summons", "verdicts",
                "declaration", "save_prepare", "save_verify", "resolution", "native_setup",
                "language_observe", "travel_prepare", "travel_verify", "mixed_transfer",
                "foreign_reintegration", "outbreak_reload_prepare", "outbreak_reload_verify", "cleanup" },
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
            ReignRebellionCampaignBehavior behavior = ReignServices.Rebellion;
            if (profile == "outbreak_reload_prepare" || profile == "outbreak_reload_verify")
                return BuildOutbreakReloadSnapshot(behavior, profile, command);
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

        private static JObject BuildOutbreakReloadSnapshot(ReignRebellionCampaignBehavior behavior, string profile, JObject command)
        {
            bool verifying = profile == "outbreak_reload_verify";
            if (!verifying)
                return new JObject { ["passed"] = false, ["error"] = "Outbreak preparation is held pending independent operator authority." };
            var enrollment = command["enrollment"] as JObject;
            var load = ReignSaveSyncCoordinator.CurrentVerifiedLoad;
            var nativeCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string campaign = enrollment?.Value<string>("campaignId");
            string timeline = enrollment?.Value<string>("timelineId");
            string save = enrollment?.Value<string>("disposableSaveName");
            string baseline = enrollment?.Value<string>("protectedBaselineSaveName");
            string runId = enrollment?.Value<string>("certificationRunId");
            string enrollmentId = enrollment?.Value<string>("campaignTestRunId");
            string point = enrollment?.Value<string>("checkpointSavePointId");
            string hash = enrollment?.Value<string>("checkpointSha256");
            if (enrollment?.Value<string>("source") != "reign_mcp_rebellion_outbreak_reload"
                || enrollment["recoveryCheckpointVerified"]?.Type != JTokenType.Boolean
                || !enrollment.Value<bool>("recoveryCheckpointVerified")
                || command.Value<string>("effects") != "guarded"
                || command.Value<string>("caseId") != "RB-NATIVE-004"
                || command.Value<string>("fixtureRunId") != runId
                || command.Value<string>("disposableSaveName") != save
                || command.Value<string>("campaignId") != campaign || command.Value<string>("gameInstanceId") != _gameInstanceId
                || string.IsNullOrEmpty(runId) || string.IsNullOrEmpty(enrollmentId)
                || string.IsNullOrEmpty(baseline) || save?.EndsWith("_Current", StringComparison.Ordinal) != true
                || !save.StartsWith("ReignTest_", StringComparison.OrdinalIgnoreCase) && !save.StartsWith("Reign_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(save, baseline, StringComparison.OrdinalIgnoreCase) || ActiveSaveName() != save
                || behavior == null || !ReferenceEquals(behavior, ReignRebellionCampaignBehavior.Instance)
                || nativeCampaign == null || !ReferenceEquals(nativeCampaign, _observedCampaign)
                || TaleWorlds.MountAndBlade.Mission.Current != null
                || nativeCampaign.TimeControlMode != CampaignTimeControlMode.Stop || ReignMainThread.CurrentGameScope == null
                || ReignSaveSyncCoordinator.IsAlignmentPending || ReignSaveSyncCoordinator.IsSaveFinalizationPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || load == null || load.CampaignId != campaign || load.SavePointId != point
                || string.IsNullOrEmpty(load.LoadSessionId) || string.IsNullOrEmpty(load.Origin)
                || string.IsNullOrEmpty(timeline) || timeline != ReignServices.WorldHistory?.TimelineId
                || string.IsNullOrEmpty(load.RestoreGeneration) || load.RestoreGeneration != ReignServices.WorldHistory?.SharedActivityGeneration
                || string.IsNullOrEmpty(point) || point != ReignServices.SaveSync?.CurrentSavePointId
                || !NativeSaveHasPoint(save, campaign, point) || hash?.Length != 64 || NativeCheckpointHash(save) != hash)
                return new JObject { ["passed"] = false, ["error"] = "The test requires the exact armed and paused native Current checkpoint." };
            using var current = Process.GetCurrentProcess();
            string process = current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
                + current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return behavior.RunOutbreakReloadFixture(verifying, runId, enrollmentId, save, campaign, timeline, point,
                load.LoadSessionId, load.Origin, process);
        }
    }
}
