using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        /// <summary>Call on the native main thread, before starting asynchronous work.</summary>
        public static JObject BuildTavernHousePayload(Settlement town, string conversationId)
        {
            var behavior = ReignTavernHouseCampaignBehavior.Instance;
            if (town?.IsTown != true || behavior == null || Hero.MainHero == null || string.IsNullOrWhiteSpace(conversationId))
                throw new InvalidOperationException("A live town, native player and tavern conversation identity are required.");
            var roster = new JArray();
            foreach (var hero in behavior.GetStaff(town))
            {
                JObject profile = BuildHeroProfile(hero);
                profile["isAvailable"] = behavior.IsAvailable(hero, town);
                profile["isMadam"] = behavior.GetMadam(town) == hero;
                profile["tavernHouse"] = behavior.Metadata(hero);
                roster.Add(profile);
            }
            return new JObject {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["conversationSessionId"] = conversationId, ["townId"] = town.StringId, ["townName"] = town.Name.ToString(), ["isTown"] = true,
                ["playerHeroStringId"] = Hero.MainHero.StringId, ["mainHeroStringId"] = Hero.MainHero.StringId, ["player"] = BuildHeroProfile(Hero.MainHero),
                ["playerIdentity"] = BuildPlayerIdentityContext(), ["playerName"] = Hero.MainHero.Name.ToString(),
                ["worldDay"] = CampaignTime.Now.ToDays, ["roster"] = roster,
                ["identityRoster"] = new JArray(behavior.GetStaff(town).Concat(new[] { Hero.MainHero }).Distinct().Select(BuildIdentityRosterEntry)),
                ["madamId"] = behavior.GetMadam(town)?.StringId ?? "", ["sceneInterface"] = "tavern_house",
                ["sceneContext"] = "A private tavern-house conversation in " + town.Name,
                ["sceneOpportunity"] = new JObject { ["private"] = true, ["exposure"] = 0.10d, ["witnessIds"] = new JArray() } };
        }

        public static async Task<JObject> PostTavernHouseAsync(string operation, JObject payload)
        {
            try
            {
                if (!new[] { "respond", "confirm", "scene", "close" }.Contains(operation, StringComparer.Ordinal))
                    throw new InvalidOperationException("Unknown tavern operation.");
                if (payload == null) throw new ArgumentNullException(nameof(payload));
                if (ReignBetaSettings.Instance != null && !ReignBetaSettings.Instance.UseLocalServer)
                    return new JObject { ["ok"] = false, ["error"] = "Local server is disabled." };
                var request = (JObject)payload.DeepClone();
                Hero speaker = null; var participants = new List<Hero>();
                if (operation == "respond")
                {
                    await ReignMainThread.InvokeAsync(() => {
                        if (GetCampaignId() != request.Value<string>("campaignId") || (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") != request.Value<string>("timelineId"))
                            throw new InvalidOperationException("The campaign changed before this conversation turn.");
                        var town = Settlement.Find(request.Value<string>("townId"));
                        var behavior = ReignTavernHouseCampaignBehavior.Instance;
                        if (behavior == null || Hero.MainHero?.CurrentSettlement != town) throw new InvalidOperationException("The player is no longer at this town.");
                        speaker = Hero.Find(request.Value<string>("speakerHeroStringId"));
                        var receipt = behavior.GetOpenVisit(town);
                        participants = receipt?.ServerConfirmed == true
                            ? receipt.ParticipantHeroIds.Select(Hero.Find).Where(x => x != null).ToList()
                            : new[] { behavior.GetMadam(town) }.Where(x => x != null).ToList();
                        if (!participants.Contains(speaker)) throw new InvalidOperationException("This character is not in the current private conversation.");
                        var session = new ReignPartyChatSession(); session.RestoreIdentity(request.Value<string>("conversationSessionId"));
                        var native = BuildPartyChatPayload(session, speaker, participants, request.Value<string>("playerText") ?? "");
                        foreach (var property in native.Properties())
                            if (!new[] { "conversationSessionId", "sceneContext", "eventId", "turnId", "sceneTurnId", "groupTranscript", "transcript" }.Contains(property.Name, StringComparer.Ordinal))
                                request[property.Name] = property.Value.DeepClone();
                        request["sceneTurnId"] = request.Value<string>("playerTurnId") ?? request.Value<string>("requestId");
                        request["turnId"] = request["sceneTurnId"]?.DeepClone();
                        var fresh = BuildTavernHousePayload(town, request.Value<string>("conversationSessionId"));
                        request["roster"] = fresh["roster"]; request["player"] = fresh["player"];
                        request["identityRoster"] = fresh["identityRoster"];
                        request["sceneOpportunity"] = fresh["sceneOpportunity"];
                        request["correlationId"] = request.Value<string>("correlationId") ?? NewCorrelationId("tavern_house", speaker.StringId);
                        request["channel"] = "party_chat"; request["phaseId"] = "tavern_house";
                        request["locationId"] = town.StringId; request["sceneInterface"] = "tavern_house";
                    }).ConfigureAwait(false);
                    await AttachContextBundlesAsync(request, "party_chat", speaker, participants).ConfigureAwait(false);
                }
                JObject response = await PostJsonAsync("/tavern-house/" + operation, request).ConfigureAwait(false);
                if (operation == "respond" && response?.Value<bool?>("ok") == true)
                    await ReignMainThread.InvokeAsync(() => {
                        if (GetCampaignId() == request.Value<string>("campaignId") && (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") == request.Value<string>("timelineId"))
                            ApplyHeroEncyclopediaText(speaker, response.Value<string>("encyclopediaText") ?? "");
                    }).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex) { return new JObject { ["ok"] = false, ["error"] = ex.Message }; }
        }
    }
}
