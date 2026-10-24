using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static bool PersonalGossipImmediateFamily(Hero a, Hero b) => a != null && b != null
            && (a == b || a.Spouse == b || a.Father == b || a.Mother == b || b.Father == a || b.Mother == a
                || a.Father != null && a.Father == b.Father || a.Mother != null && a.Mother == b.Mother);

        private static bool PersonalGossipHasQuestDuty(Hero hero)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            return hero != null && (hero.Issue != null || campaign?.QuestManager?.IsQuestGiver(hero) == true
                || campaign?.QuestManager?.TrackedObjects?.ContainsKey(hero) == true
                || campaign?.IssueManager?.IssueSolvingCompanionList?.Contains(hero) == true);
        }

        private static void GuardPersonalGossipResidentActivation(Hero hero, Hero actor)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (hero == null || actor == null || hero == Hero.MainHero || !hero.IsAlive || hero.IsActive || hero.IsPrisoner
                || hero.Age < Math.Max(18, campaign?.Models.AgeModel.HeroComesOfAge ?? 18)
                || hero.PartyBelongedTo != null || hero.PartyBelongedToAsPrisoner != null || hero.GovernorOf != null
                || hero.IsTraveling || hero.CurrentSettlement?.IsUnderSiege == true || PersonalGossipHasQuestDuty(hero)
                || !PersonalGossipImmediateFamily(actor, hero))
                throw new InvalidOperationException("Resident activation requires an inactive living adult speaker or immediate family/spouse, free of party, captivity, travel, governor, siege and quest duties.");
        }

        private static JObject PersonalGossipNativeState(Hero hero) => new JObject {
            ["heroId"] = hero.StringId, ["heroState"] = hero.HeroState.ToString(), ["isActive"] = hero.IsActive,
            ["isAlive"] = hero.IsAlive, ["age"] = hero.Age, ["isPrisoner"] = hero.IsPrisoner,
            ["settlementId"] = hero.CurrentSettlement?.StringId ?? "", ["partyId"] = hero.PartyBelongedTo?.StringId ?? "",
            ["hasPrisonerParty"] = hero.PartyBelongedToAsPrisoner != null, ["isTraveling"] = hero.IsTraveling,
            ["governorOfSettlementId"] = hero.GovernorOf?.StringId ?? "", ["settlementUnderSiege"] = hero.CurrentSettlement?.IsUnderSiege == true,
            ["hasQuestDuty"] = PersonalGossipHasQuestDuty(hero)
        };

        private static void GuardPersonalGossipTest(JObject command)
        {
            var enrollment = command["enrollment"] as JObject;
            string expected = enrollment?.Value<string>("disposableSaveName") ?? "";
            string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
            if (!_armed || command.Value<string>("mode") != "personal_gossip" || string.IsNullOrEmpty(command.Value<string>("runId"))
                || enrollment?.Value<string>("source") != "reign_mcp_personal_gossip"
                || string.IsNullOrEmpty(enrollment.Value<string>("campaignTestRunId"))
                || enrollment.Value<bool?>("recoveryCheckpointVerified") != true
                || expected.Length == 0 || baseline.Length == 0 || expected.Equals(baseline, StringComparison.OrdinalIgnoreCase)
                || expected != ActiveSaveName() || command.Value<string>("gameInstanceId") != _gameInstanceId
                || command.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                || enrollment.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                || enrollment.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress || Mission.Current != null || MobileParty.MainParty?.MapEvent != null)
                throw new InvalidOperationException("Personal gossip requires the exact enrolled paused Current, timeline, instance and recovery checkpoint outside a mission/battle.");
        }

        private static Tuple<JObject, ReignServerClient.PersonalGossipPresenceSnapshot> PersonalGossipNativeRequest(JObject command)
        {
            GuardPersonalGossipTest(command);
            var presence = ReignServerClient.CapturePersonalGossipPresence();
            var request = new JObject {
                ["campaignId"] = command["campaignId"]?.DeepClone(), ["runId"] = command["runId"]?.DeepClone(),
                ["commandId"] = command["commandId"]?.DeepClone(), ["leaseId"] = command["leaseId"]?.DeepClone(),
                ["gameInstanceId"] = _gameInstanceId, ["activeSaveName"] = ActiveSaveName(), ["timeMode"] = "Stop",
                ["nativeSaveInProgress"] = false, ["playerId"] = Hero.MainHero.StringId,
                ["timelineId"] = presence.Timeline, ["worldDay"] = presence.WorldDay,
                ["testCase"] = command["testCase"]?.DeepClone()
            };
            var activationIds = (command["testCase"]?["activateResidents"] as JArray ?? new JArray()).Values<string>().ToList();
            if (activationIds.Count > 0) request["nativeActivationStates"] = new JArray(activationIds.Select(id =>
                PersonalGossipNativeState(Hero.AllAliveHeroes.Single(h => h.StringId == id))));
            return Tuple.Create(request, presence);
        }

        private static async Task<LiveCommandResult> PersonalGossipTestAsync(JObject command)
        {
            try
            {
                var test = command["testCase"] as JObject ?? throw new InvalidOperationException("Missing bounded case.");
                string phase = test.Value<string>("phase") ?? "";
                // Resolve every destination before the durable server journal or any mutation.
                await ReignMainThread.InvokeAsync(() => {
                    GuardPersonalGossipTest(command);
                    var activationIds = (test["activateResidents"] as JArray ?? new JArray()).Values<string>().ToList();
                    Hero actor = Hero.AllAliveHeroes.SingleOrDefault(h => h.StringId == test.Value<string>("actorHeroId"));
                    if (activationIds.Count > 4 || activationIds.Distinct().Count() != activationIds.Count
                        || activationIds.Any(id => (test["placements"] as JArray ?? new JArray()).OfType<JObject>().Count(p => p.Value<string>("heroId") == id) != 1))
                        throw new InvalidOperationException("Activation requires at most four distinct explicit placements.");
                    foreach (string id in activationIds)
                        GuardPersonalGossipResidentActivation(Hero.AllAliveHeroes.SingleOrDefault(h => h.StringId == id), actor);
                    foreach (var item in (test["placements"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        Hero hero = Hero.AllAliveHeroes.SingleOrDefault(h => h.StringId == item.Value<string>("heroId"));
                        if (hero == null || hero == Hero.MainHero || !hero.IsActive && !activationIds.Contains(hero.StringId) || hero.IsPrisoner
                            || hero.PartyBelongedTo?.LeaderHero == hero || hero.PartyBelongedTo?.MapEvent != null)
                            throw new InvalidOperationException("Placements require a living free non-player resident/companion, outside a leading-party or battle role.");
                        if (item["settlementId"] != null)
                        {
                            var town = Settlement.All.SingleOrDefault(s => s.StringId == item.Value<string>("settlementId"));
                            if (town == null || town.IsUnderSiege) throw new InvalidOperationException("Choose an actual settlement outside siege.");
                        }
                        else if (item.Value<string>("party") != "main" || MobileParty.MainParty == null)
                            throw new InvalidOperationException("Only the actual main party is a supported party destination.");
                    }
                    if (phase == "conversation")
                    {
                        var requested = (test["participantHeroIds"] as JArray ?? new JArray()).Values<string>().ToList();
                        if (test.Value<string>("conversationMode") == "party_chat" && requested.Count < 2)
                            throw new InvalidOperationException("Party chat needs at least two explicitly selected exact participants.");
                        if (requested.Count == 0) requested.Add(test.Value<string>("actorHeroId") ?? "");
                        var resolved = ResolveHeroesForMode(test.Value<string>("conversationMode") ?? "individual_chat", requested);
                        if (resolved.Count != requested.Count || !resolved.Select(h => h.StringId).OrderBy(id => id, StringComparer.Ordinal)
                            .SequenceEqual(requested.OrderBy(id => id, StringComparer.Ordinal)))
                            throw new InvalidOperationException("Every requested gossip participant must be an exact currently available production conversation target.");
                    }
                }).ConfigureAwait(false);
                if (phase == "tick") await ReignServerClient.TickPersonalGossipAsync().ConfigureAwait(false);
                var captured = await ReignMainThread.InvokeAsync(() => PersonalGossipNativeRequest(command)).ConfigureAwait(false);
                var request = captured.Item1;
                request["gossipPresence"] = await ReignServerClient.EncodePersonalGossipPresenceAsync(captured.Item2).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => GuardPersonalGossipTest(command)).ConfigureAwait(false);
                var evidence = await ReignServerClient.PersonalGossipTestRequestAsync(request, captured.Item2).ConfigureAwait(false);
                if (evidence?.Value<bool?>("ok") != true) return LiveCommandResult.Failed("Server rejected gossip fixture authorization.", evidence);
                evidence["nativeObservations"] = await ReignMainThread.InvokeAsync(() =>
                {
                    GuardPersonalGossipTest(command);
                    var ids = (test["heroIds"] as JArray ?? new JArray()).Values<string>().ToList();
                    ids.AddRange((test["participantHeroIds"] as JArray ?? new JArray()).Values<string>());
                    ids.AddRange((test["activateResidents"] as JArray ?? new JArray()).Values<string>());
                    if (!string.IsNullOrEmpty(test.Value<string>("actorHeroId"))) ids.Add(test.Value<string>("actorHeroId"));
                    return new JArray(Hero.AllAliveHeroes.Where(h => h != null && ids.Contains(h.StringId)).Take(16)
                        .Select(h => new JObject { ["heroId"] = h.StringId, ["age"] = h.Age,
                            ["nativeRelationToPlayer"] = Hero.MainHero == null ? 0 : h.GetRelation(Hero.MainHero),
                            ["spouseId"] = h.Spouse?.StringId ?? "", ["heroState"] = h.HeroState.ToString(), ["isActive"] = h.IsActive }));
                }).ConfigureAwait(false);
                if (phase == "presence_contract")
                {
                    var comparison = await ReignMainThread.InvokeAsync(() => {
                        GuardPersonalGossipTest(command);
                        return ReignServerClient.CapturePersonalGossipComparison();
                    }).ConfigureAwait(false);
                    var proof = await ReignServerClient.PersonalGossipPresenceContractAsync(comparison).ConfigureAwait(false);
                    var refused = (JObject)request.DeepClone();
                    refused["gossipPresence"] = await ReignServerClient.PersonalGossipIncompleteFixtureAsync(captured.Item2.WorldDay, captured.Item2.Timeline).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() => GuardPersonalGossipTest(command)).ConfigureAwait(false);
                    bool rejected = false;
                    try
                    {
                        var answer = await ReignServerClient.PersonalGossipTestRequestAsync(refused, captured.Item2).ConfigureAwait(false);
                        rejected = answer?.Value<bool?>("ok") == false
                            && (answer.Value<string>("error") ?? "").Contains("A complete current native roster is required.");
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("A complete current native roster is required.")) { rejected = true; }
                    proof["incompleteRosterRejectedByServer"] = rejected;
                    evidence["presenceContract"] = proof;
                }
                if (phase == "prepare")
                {
                    var after = await ReignMainThread.InvokeAsync(() => {
                        GuardPersonalGossipTest(command);
                        Hero actor = Hero.AllAliveHeroes.Single(h => h.StringId == test.Value<string>("actorHeroId"));
                        foreach (string id in (test["activateResidents"] as JArray ?? new JArray()).Values<string>())
                        {
                            Hero resident = Hero.AllAliveHeroes.Single(h => h.StringId == id);
                            GuardPersonalGossipResidentActivation(resident, actor);
                            resident.ChangeState(Hero.CharacterStates.Active);
                        }
                        foreach (var item in (test["placements"] as JArray ?? new JArray()).OfType<JObject>())
                        {
                            Hero hero = Hero.AllAliveHeroes.Single(h => h.StringId == item.Value<string>("heroId"));
                            if (item["settlementId"] != null)
                                TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, Settlement.All.Single(s => s.StringId == item.Value<string>("settlementId")));
                            else if (hero.PartyBelongedTo != MobileParty.MainParty) AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
                            if (!hero.IsActive || (item["settlementId"] != null
                                ? hero.CurrentSettlement?.StringId != item.Value<string>("settlementId")
                                : hero.PartyBelongedTo != MobileParty.MainParty))
                                throw new InvalidOperationException("The native resident placement did not reach its exact requested active destination. Restore the retained checkpoint before retrying.");
                        }
                        return ReignServerClient.CapturePersonalGossipPresence();
                    }).ConfigureAwait(false);
                    evidence["afterNativePresence"] = await ReignServerClient.EncodePersonalGossipPresenceAsync(after).ConfigureAwait(false);
                    evidence["afterNativeActivationStates"] = await ReignMainThread.InvokeAsync(() => new JArray(
                        (test["activateResidents"] as JArray ?? new JArray()).Values<string>().Select(id =>
                            PersonalGossipNativeState(Hero.AllAliveHeroes.Single(h => h.StringId == id))))).ConfigureAwait(false);
                }
                if (phase == "conversation")
                {
                    if ((command["enrollment"] as JObject)?.Value<bool?>("allowProviderCalls") != true)
                        throw new InvalidOperationException("Production conversation requires provider authorization.");
                    string actor = test.Value<string>("actorHeroId") ?? "";
                    var turns = test["playerTurns"] as JArray;
                    if (turns == null || turns.Count < 1 || turns.Count > 8 || turns.Any(t => t.Type != JTokenType.String || t.Value<string>().Length > 1800))
                        throw new InvalidOperationException("Supply one through eight bounded natural player turns.");
                    // Do not forward transport extras into production Send: even a forged
                    // stored command cannot attach a prompt override or action draft here.
                    var conversation = new JObject {
                        ["runId"] = command["runId"]?.DeepClone(), ["campaignId"] = command["campaignId"]?.DeepClone(),
                        ["gameInstanceId"] = command["gameInstanceId"]?.DeepClone(),
                        ["commandId"] = command["commandId"]?.DeepClone(),
                        ["mode"] = test.Value<string>("conversationMode") ?? "individual_chat",
                        ["targetSearch"] = actor, ["presentation"] = "visible", ["effects"] = "full"
                    };
                    if (test["participantHeroIds"] is JArray group)
                    {
                        conversation.Remove("targetSearch");
                        conversation["targetSearches"] = group.DeepClone();
                    }
                    if (!new[] { "individual_chat", "party_chat", "social_event", "wilderness_event" }.Contains(conversation.Value<string>("mode")))
                        throw new InvalidOperationException("Unsupported production adapter.");
                    var observed = new JArray();
                    try
                    {
                        var opened = await OpenAsync(conversation).ConfigureAwait(false);
                        evidence["open"] = opened.Data;
                        if (opened.Status != "completed") return LiveCommandResult.Failed(opened.Error, evidence);
                        int index = 0;
                        foreach (var turn in turns)
                        {
                            await ReignMainThread.InvokeAsync(() => GuardPersonalGossipTest(command)).ConfigureAwait(false);
                            var send = (JObject)conversation.DeepClone(); send["text"] = turn.DeepClone();
                            send["commandId"] = command.Value<string>("commandId") + "-turn-" + index;
                            send["turnIndex"] = index++;
                            var reply = await SendAsync(send).ConfigureAwait(false);
                            var acceptedTurn = new JObject { ["playerText"] = turn.DeepClone(), ["status"] = reply.Status, ["error"] = reply.Error, ["result"] = reply.Data };
                            observed.Add(acceptedTurn);
                            if (reply.Status != "completed") return LiveCommandResult.Failed(reply.Error, evidence);
                            // Observe the accepted source before closing the production session.
                            // This is read-only operator evidence and never enters a participant prompt.
                            var turnCapture = await ReignMainThread.InvokeAsync(() => PersonalGossipNativeRequest(command)).ConfigureAwait(false);
                            turnCapture.Item1["gossipPresence"] = await ReignServerClient.EncodePersonalGossipPresenceAsync(turnCapture.Item2).ConfigureAwait(false);
                            var scheduling = await ReignServerClient.PersonalGossipTestRequestAsync(turnCapture.Item1, turnCapture.Item2).ConfigureAwait(false);
                            if (scheduling?.Value<bool?>("ok") != true)
                                return LiveCommandResult.Failed("Accepted turn completed, but its guarded scheduling observation failed; do not replay the accepted turn.", evidence);
                            acceptedTurn["gossipWhileOpen"] = scheduling["conversationScheduling"]?.DeepClone();
                            acceptedTurn["conversationStillOpen"] = await ReignMainThread.InvokeAsync(() => {
                                GuardPersonalGossipTest(command);
                                return _activeRunId == command.Value<string>("runId") && _activeMode == conversation.Value<string>("mode");
                            }).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        evidence["turns"] = observed;
                        // Each case is a complete player conversation. Use the
                        // shipping Leave action after the while-open observation;
                        // a scene-boundary finish deliberately preserves drafts.
                        await ReignMainThread.InvokeAsync(() => {
                            GuardPersonalGossipTest(command);
                            if (_individualVm != null) _individualVm.ExecuteClose();
                            else if (_partyVm != null) _partyVm.ExecuteClose();
                        }).ConfigureAwait(false);
                        evidence["conversationEnd"] = await CloseSessionAsync(false).ConfigureAwait(false);
                    }
                }
                await ReignMainThread.InvokeAsync(() => GuardPersonalGossipTest(command)).ConfigureAwait(false);
                return LiveCommandResult.Completed("Observed native gossip case. Evaluate the recorded evidence; completion is not a behavioral pass.", evidence);
            }
            catch (Exception ex) { return LiveCommandResult.Failed(ex.Message); }
        }
    }
}
