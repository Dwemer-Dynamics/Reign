using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> StageNotableStartupReservationAsync(JObject command)
        {
            try
            {
                await ReignMainThread.InvokeAsync(() => RequireNotableFixtureTown(command)).ConfigureAwait(false);
                var request = new JObject
                {
                    ["phase"] = "admit", ["campaignId"] = command["campaignId"], ["gameInstanceId"] = command["gameInstanceId"],
                    ["clientProcessInstanceId"] = PartyAgencyClientProcessInstanceId, ["runId"] = command["runId"],
                    ["commandId"] = command["commandId"], ["leaseId"] = command["leaseId"]
                };
                const string endpoint = "/tests/live/game/notable-startup-fixture";
                var admitted = await ReignServerClient.PostJsonAsync(endpoint, request).ConfigureAwait(false);
                if (admitted?.Value<bool?>("ok") != true)
                    return LiveCommandResult.Failed(admitted?.Value<string>("error") ?? "The provider-isolated fixture was not admitted.");

                var created = await ReignMainThread.InvokeAsync(() =>
                {
                    var town = RequireNotableFixtureTown(command);
                    var hero = HeroCreator.CreateNotable(Occupation.Merchant, town);
                    if (hero == null || !hero.IsAlive || !hero.IsNotable || !town.Notables.Contains(hero))
                        throw new InvalidOperationException("The native merchant was not registered at the current town.");
                    return (Hero: hero, Id: hero.StringId, Day: CampaignTime.Now.ToDays,
                        Traits: ReignCharacterEditorClient.NativePersonalityTraits(hero));
                }).ConfigureAwait(false);

                request["phase"] = "reserve";
                request["heroId"] = created.Id;
                request["worldDay"] = created.Day;
                var reserved = await ReignServerClient.PostJsonAsync(endpoint, request).ConfigureAwait(false);
                if (reserved?.Value<bool?>("ok") != true || reserved.Value<string>("heroId") != created.Id
                    || !(reserved["nativeTraits"] is JObject traits))
                    return LiveCommandResult.Failed(reserved?.Value<string>("error") ?? "Reservation did not match the native merchant. Restore the checkpoint.");

                return await ReignMainThread.InvokeAsync(() =>
                {
                    var town = RequireNotableFixtureTown(command);
                    if (!created.Hero.IsAlive || !created.Hero.IsNotable || !town.Notables.Contains(created.Hero)
                        || !JToken.DeepEquals(created.Traits, ReignCharacterEditorClient.NativePersonalityTraits(created.Hero))
                        || !ExactFixtureNativeTraits(created.Traits, traits))
                        return LiveCommandResult.Failed("The native merchant or its five-field assignment changed. Restore the checkpoint.");
                    ReignCharacterEditorCampaignBehavior.ApplyTraits(created.Hero, traits);
                    var observed = ReignCharacterEditorClient.NativePersonalityTraits(created.Hero);
                    if (!ExactFixtureNativeTraits(observed, traits) || observed.Properties().Any(p => !JToken.DeepEquals(p.Value, traits[p.Name])))
                        return LiveCommandResult.Failed("The native trait application could not be verified. Restore the checkpoint.");

                    // The scenario deliberately stops before the normal startup confirmation.
                    return LiveCommandResult.Completed("Prepared one native merchant with an unconfirmed startup reservation.", new JObject
                    {
                        ["heroId"] = created.Id, ["settlementId"] = town.StringId,
                        ["before"] = created.Traits, ["expected"] = traits.DeepClone(), ["observed"] = observed,
                        ["checkpointSavePointId"] = command["enrollment"]["checkpointSavePointId"],
                        ["startupConfirmed"] = false
                    });
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return LiveCommandResult.Failed(ex.Message + " Restore the fixture checkpoint before retrying.");
            }
        }

        private static Settlement RequireNotableFixtureTown(JObject command)
        {
            var scope = command["enrollment"] as JObject;
            var testCase = command["testCase"] as JObject;
            var args = testCase?["arguments"] as JObject;
            var current = Settlement.CurrentSettlement;
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
            string save = ActiveSaveName();
            string point = scope?.Value<string>("checkpointSavePointId");
            if (!_armed || scope?.Value<string>("source") != "reign_mcp_structured_actions"
                || string.IsNullOrEmpty(scope.Value<string>("campaignTestRunId"))
                || scope.Value<bool?>("recoveryCheckpointVerified") != true || scope.Value<string>("campaignId") != campaignId
                || scope.Value<string>("timelineId") != timeline || scope.Value<string>("disposableSaveName") != save
                || string.IsNullOrEmpty(scope.Value<string>("protectedBaselineSaveName"))
                || string.Equals(scope.Value<string>("protectedBaselineSaveName"), save, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(save) || !Guid.TryParseExact(point, "N", out _)
                || ReignServices.SaveSync?.CurrentSavePointId != point
                || ReignSaveSyncCoordinator.IsAlignmentPending || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || command.Value<string>("campaignId") != campaignId || command.Value<string>("gameInstanceId") != _gameInstanceId
                || campaign?.TimeControlMode != CampaignTimeControlMode.Stop
                || testCase?.Value<string>("phase") != "fixture" || testCase.Value<string>("kind") != "fixture"
                || testCase.Value<string>("actionId") != "fixture.notable_startup_reservation"
                || args?.Count != 1 || current?.Town == null || !current.IsTown || current.StringId != args.Value<string>("settlementId"))
                throw new InvalidOperationException("The Notable fixture requires the paused, enrolled Current recovery point at the exact current town.");
            var expectedOwner = scope["checkpointLoadOwner"] as JObject;
            var observedOwner = ObservePassiveWorldCurrentLoad(command, campaign, campaignId, timeline, save, point);
            if (expectedOwner == null || !JToken.DeepEquals(expectedOwner, observedOwner)
                || !NativeSaveHasPoint(save, campaignId, point)
                || !string.Equals(NativeCheckpointHash(save), scope.Value<string>("checkpointSha256"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The fixture requires the same native load owner and recovery save bytes seen by the controller.");
            return current;
        }

        private static bool ExactFixtureNativeTraits(JObject native, JObject expected) =>
            native?.Count == 5 && expected?.Count == 5 && native.Properties().All(p =>
                p.Value.Type == JTokenType.Integer && p.Value.Value<int>() is >= -2 and <= 2
                && expected[p.Name]?.Type == JTokenType.Integer && expected[p.Name].Value<int>() is >= -2 and <= 2);
    }
}
