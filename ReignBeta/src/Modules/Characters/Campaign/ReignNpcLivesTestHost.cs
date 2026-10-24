using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.World;
using ReignBeta.Save;
using ReignBeta.Actions;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static TaleWorlds.CampaignSystem.Campaign _npcLivesCleanupCampaign;
        internal static bool OwnsNpcLivesSession => _armed && _activeMode == "npc_lives" && !string.IsNullOrEmpty(_activeRunId);
        internal static void HoldNpcLivesEncounterCleanup(TaleWorlds.CampaignSystem.Campaign campaign)
        {
            if (!OwnsNpcLivesSession || !ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current))
                throw new InvalidOperationException("The native cleanup no longer owns this test campaign.");
            _npcLivesCleanupCampaign = campaign;
            PauseNpcLivesEncounterCleanup(campaign);
        }
        private static void PauseNpcLivesEncounterCleanup(TaleWorlds.CampaignSystem.Campaign campaign)
        {
            if (_npcLivesCleanupCampaign == null) return;
            if (!OwnsNpcLivesSession || !ReferenceEquals(_npcLivesCleanupCampaign, campaign))
                _npcLivesCleanupCampaign = null;
            else if (Mission.Current == null) campaign.TimeControlMode = CampaignTimeControlMode.Stop;
        }
        internal static void ReleaseNpcLivesEncounterCleanup(TaleWorlds.CampaignSystem.Campaign campaign)
        {
            if (!ReferenceEquals(_npcLivesCleanupCampaign, campaign)) return;
            PauseNpcLivesEncounterCleanup(TaleWorlds.CampaignSystem.Campaign.Current);
            _npcLivesCleanupCampaign = null;
        }
        private static async Task<LiveCommandResult> NpcLivesTestAsync(JObject command)
        {
            try
            {
                var c = command["testCase"] as JObject ?? throw new InvalidOperationException("Missing bounded case.");
                await ReignMainThread.InvokeAsync(() => {
                    var enrollment = command["enrollment"] as JObject;
                    string expected = enrollment?.Value<string>("disposableSaveName") ?? "";
                    string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
                    if (!_armed || command.Value<string>("mode") != "npc_lives" || string.IsNullOrEmpty(command.Value<string>("runId"))
                        || enrollment?.Value<string>("source") != "reign_mcp_npc_lives"
                        || string.IsNullOrEmpty(enrollment.Value<string>("campaignTestRunId"))
                        || enrollment.Value<bool?>("recoveryCheckpointVerified") != true
                        || expected.Length == 0 || baseline.Length == 0 || expected.Equals(baseline, StringComparison.OrdinalIgnoreCase)
                        || expected != ActiveSaveName() || command.Value<string>("gameInstanceId") != _gameInstanceId
                        || command.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                        || enrollment.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                        || enrollment.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                        || TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode != CampaignTimeControlMode.Stop || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress)
                        throw new InvalidOperationException("NPC Lives requires the exact enrolled paused Current, timeline, game instance and retained recovery checkpoint.");
                    _activeRunId = command.Value<string>("runId"); _activeMode = "npc_lives";
                }).ConfigureAwait(false);
                var owner = ReignNpcLivesBehavior.Instance ?? throw new InvalidOperationException("NPC Lives is not initialized.");
                await owner.RefreshLifeSnapshotAsync().ConfigureAwait(false);
                if (c.Value<string>("phase") == "prepare")
                    return LiveCommandResult.Completed("Created an unaccepted authored invitation. No consent, progress or reward was granted.",
                        await owner.PrepareNativeLifeCaseAsync(command).ConfigureAwait(false));
                if (c.Value<string>("phase") == "encounter")
                    return LiveCommandResult.Completed("Observed both real native participants and requested the bounded outcome. Verify the production callback and rewards.",
                        await owner.RunNativeLifeEncounterCaseAsync(c).ConfigureAwait(false));
                return await ReignMainThread.InvokeAsync(() => {
                    var evidence = owner.RunNativeLifeCase(c);
                    return LiveCommandResult.Completed("Native fixture requested. Inspect the production receipt and resulting story phase before recording a pass.", evidence);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { return LiveCommandResult.Failed(ex.Message); }
        }
    }
    public sealed partial class ReignNpcLivesBehavior
    {
        internal async Task<JObject> RunNativeLifeEncounterCaseAsync(JObject c)
        {
            string storyId = c.Value<string>("storyId") ?? "", objectiveId = c.Value<string>("objectiveId") ?? "";
            string outcome = c.Value<string>("outcome") ?? "";
            if (!new[] { "won", "lost", "withdrawn" }.Contains(outcome)) throw new InvalidOperationException("Choose a bounded native outcome.");
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current; int epoch = _epoch;
            var priorEncounters = new System.Collections.Generic.HashSet<string>();
            var before = await ReignMainThread.InvokeAsync(() => {
                if (!ReignLiveInteractionTestHost.OwnsNpcLivesSession || !_initialized || Mission.Current != null)
                    throw new InvalidOperationException("A verified NPC Lives session without an existing mission is required.");
                var evidence = NativeLifeEvidence(storyId);
                priorEncounters.UnionWith(_storyEncounters.Keys);
                StartStoryEncounter(storyId, objectiveId);
                return evidence;
            }).ConfigureAwait(false);
            // Resolve in this leased command as soon as actual agents exist. A
            // second transport poll can arrive after a real practice duel ends.
            try
            {
              for (int i = 0; i < 600; i++)
              {
                JObject result = await ReignMainThread.InvokeAsync(() => {
                    if (!Current(campaign, epoch) || !ReignLiveInteractionTestHost.OwnsNpcLivesSession)
                        throw new InvalidOperationException("The native test session changed before the outcome.");
                    var story = State.Stories.Single(s => s.Id == storyId);
                    var entry = _storyEncounters.LastOrDefault(p => !priorEncounters.Contains(p.Key) && p.Value.Value<string>("storyId") == storyId
                        && p.Value.Value<string>("objectiveId") == objectiveId && p.Value.Value<int>("revision") == story.Revision);
                    if (entry.Value == null) return null;
                    if (entry.Value.Value<string>("status") == "resolved")
                        throw new InvalidOperationException("The encounter resolved naturally before the controlled outcome; inspect it and do not claim fixture coverage.");
                    var mission = Mission.Current;
                    if (entry.Value.Value<string>("status") != "active" || story.NativeEncounterId != entry.Key || mission?.MainAgent == null
                        || !mission.MainAgent.IsActive() || !mission.AllAgents.Any(a => a.IsActive()
                            && a.Character is CharacterObject ch && ch.HeroObject?.StringId == story.HeroId)) return null;
                    if (entry.Value.Value<string>("kind") == "hideout" && entry.Value.Value<bool?>("participantWitnessed") != true) return null;
                    var participantEvidence = new JObject { ["encounterId"] = entry.Key, ["playerAgentIndex"] = mission.MainAgent.Index,
                        ["companionAgentIndex"] = mission.AllAgents.First(a => a.IsActive()
                            && a.Character is CharacterObject ch && ch.HeroObject?.StringId == story.HeroId).Index };
                    // Native defeat may resume captive fast-forward during mission
                    // teardown. Keep the clock paused after the mission exits until
                    // the ordinary callback and its queued receipt have settled.
                    ReignLiveInteractionTestHost.HoldNpcLivesEncounterCleanup(campaign);
                    var finished = RunNativeLifeCase(new JObject { ["caseId"] = c["caseId"], ["phase"] = "finish",
                        ["storyId"] = storyId, ["encounterId"] = entry.Key, ["outcome"] = outcome });
                    return new JObject { ["schema"] = "reign-npc-lives-controlled-encounter-v1", ["origin"] = "operator_native_fixture",
                        ["before"] = before, ["participants"] = participantEvidence, ["finish"] = finished };
                }).ConfigureAwait(false);
                if (result != null)
                {
                    string encounterId = result["participants"].Value<string>("encounterId");
                    for (int cleanup = 0; cleanup < 2400; cleanup++)
                    {
                        JObject settled = await ReignMainThread.InvokeAsync(() => {
                            if (!Current(campaign, epoch) || !ReignLiveInteractionTestHost.OwnsNpcLivesSession)
                                throw new InvalidOperationException("The native test session changed during encounter cleanup.");
                            ReignLiveInteractionTestHost.HoldNpcLivesEncounterCleanup(campaign);
                            string status = _storyEncounters.TryGetValue(encounterId, out var record) ? record.Value<string>("status") : "";
                            if (!ReignBeta.Shared.Characters.NpcLivesPolicy.NativeEncounterSettled(Mission.Current != null,
                                MobileParty.MainParty?.MapEvent != null, status) || _outbox.Count != 0 || _busy
                                || State.Effects.Any(e => e.StoryId == storyId && e.Kind == "skill_xp" && !_nativeEffects.Contains(e.Id))) return null;
                            return NativeLifeEvidence(storyId);
                        }).ConfigureAwait(false);
                        if (settled != null)
                        {
                            result["settled"] = settled;
                            result["cleanup"] = new JObject { ["timeMode"] = "Stop", ["nativeCallbackObserved"] = true,
                                ["missionActive"] = false, ["battleActive"] = false, ["outboxCount"] = 0 };
                            return result;
                        }
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                    throw new InvalidOperationException("The native encounter callback, mission/battle teardown and receipts did not settle within 120 seconds. Campaign time was paused; inspect before retrying.");
                }
                await Task.Delay(50).ConfigureAwait(false);
              }
              throw new InvalidOperationException("No exact acknowledged native encounter with both active participants was observed within 30 seconds. Inspect before retrying.");
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() => ReignLiveInteractionTestHost.ReleaseNpcLivesEncounterCleanup(campaign)).ConfigureAwait(false);
            }
        }

        internal async Task<JObject> PrepareNativeLifeCaseAsync(JObject command)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current; int epoch = _epoch;
            var p = await ReignMainThread.InvokeAsync(() => {
                if (!ReignLiveInteractionTestHost.OwnsNpcLivesSession || !Current(campaign, epoch)
                    || campaign.TimeControlMode != CampaignTimeControlMode.Stop || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress)
                    throw new InvalidOperationException("The invitation fixture no longer owns a paused native campaign.");
                var request = ReignServerClient.CaptureNpcLivesContext(Generation, true);
                request["locations"] = ReignServerClient.CaptureNpcStoryLocations();
                foreach (string key in new[] { "runId", "commandId", "leaseId", "gameInstanceId", "testCase" }) request[key] = command[key]?.DeepClone();
                request["activeSaveName"] = ReignServerClient.ActiveNativeSaveName();
                request["timeMode"] = campaign.TimeControlMode.ToString(); request["nativeSaveInProgress"] = false;
                return request;
            }).ConfigureAwait(false);
            var response = await ReignServerClient.NpcLivesRequestAsync("test-fixture", p).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true) throw new InvalidOperationException(response.Value<string>("error") ?? "Invitation preparation failed.");
            await RefreshLifeSnapshotAsync().ConfigureAwait(false);
            return await ReignMainThread.InvokeAsync(() => {
                if (!Current(campaign, epoch)) throw new InvalidOperationException("The native campaign changed during preparation.");
                string id = response.Value<string>("invitationId");
                if (command["testCase"]?.Value<string>("invitationKind") == "leave")
                    return new JObject { ["schema"] = "reign-npc-lives-invitation-fixture-v1", ["origin"] = "operator_invitation_setup",
                        ["caseId"] = command["testCase"]?["caseId"], ["invitationKind"] = "leave", ["leaveId"] = id,
                        ["consentGranted"] = false, ["earlierChaptersCompleted"] = false,
                        ["evidence"] = NativeLifeCharacterEvidence(command["testCase"].Value<string>("heroId")) };
                return new JObject { ["schema"] = "reign-npc-lives-invitation-fixture-v1", ["origin"] = "operator_invitation_setup",
                    ["caseId"] = command["testCase"]?["caseId"], ["storyId"] = id, ["consentGranted"] = false,
                    ["earlierChaptersCompleted"] = false, ["evidence"] = NativeLifeEvidence(id) };
            }).ConfigureAwait(false);
        }
        internal JObject RunNativeLifeCase(JObject c)
        {
            if (!ReignLiveInteractionTestHost.OwnsNpcLivesSession || !_initialized || c.ToString().Length > 16384)
                throw new InvalidOperationException("A verified NPC Lives session is required.");
            string storyId = c.Value<string>("storyId") ?? "", phase = c.Value<string>("phase") ?? "";
            if (phase == "snapshot") return NativeLifeCharacterEvidence(c.Value<string>("heroId") ?? "");
            if (phase == "place") return PrepareNativeStoryPlace(c);
            var story = State.Stories.SingleOrDefault(s => s.Id == storyId) ?? throw new InvalidOperationException("No exact story instance exists.");
            var before = NativeLifeEvidence(storyId);
            if (phase == "start") StartStoryEncounter(storyId, c.Value<string>("objectiveId"));
            else if (phase == "finish")
            {
                string id = c.Value<string>("encounterId") ?? "", outcome = c.Value<string>("outcome") ?? "";
                if (!new[] { "won", "lost", "withdrawn" }.Contains(outcome)
                    || !_storyEncounters.TryGetValue(id, out var row) || row.Value<string>("storyId") != storyId
                    || row.Value<string>("status") != "active" || row.Value<int>("revision") != story.Revision
                    || story.NativeEncounterId != id || Mission.Current == null)
                    throw new InvalidOperationException("Finish requires the exact active, acknowledged native story encounter and revision.");
                if (row.Value<string>("testOutcome") is string prior && prior.Length > 0)
                {
                    if (prior != outcome) throw new InvalidOperationException("This encounter already has another fixture outcome.");
                    return new JObject { ["before"] = before, ["after"] = NativeLifeEvidence(storyId), ["replayed"] = true };
                }
                var mission = Mission.Current;
                Agent player = mission.MainAgent;
                Agent companion = mission.AllAgents.FirstOrDefault(a => a.Character is CharacterObject ch && ch.HeroObject?.StringId == story.HeroId && a.IsActive());
                if (player == null || companion == null) throw new InvalidOperationException("Both actual mission participants must be observed first.");
                if (row.Value<string>("kind") == "duel")
                {
                    row["testOutcome"] = outcome;
                    if (outcome == "withdrawn")
                    {
                        ResolveStoryEncounter(id, "withdrawn"); ReignDuelService.Complete(id);
                        mission.EndMission();
                    }
                    else
                    {
                        Agent loser = outcome == "won" ? companion : player, winner = outcome == "won" ? player : companion;
                        // Real native removal runs ArenaDuelMissionController's result callback.
                        loser.Die(new Blow(winner.Index) { DamageType = DamageTypes.Blunt, InflictedDamage = (int)loser.Health + 1 });
                    }
                }
                else
                {
                    if (!_storyBattles.TryGetValue(id, out var battle) || !ReferenceEquals(battle, MobileParty.MainParty.MapEvent)
                        || !battle.IsHideoutBattle || row.Value<bool?>("participantWitnessed") != true)
                        throw new InvalidOperationException("The actual story hideout battle and companion witness must match.");
                    row["testOutcome"] = outcome;
                    if (outcome != "withdrawn")
                    {
                        BattleSideEnum winner = outcome == "won" ? battle.PlayerSide : battle.PlayerSide.GetOppositeSide();
                        battle.SetOverrideWinner(winner); battle.DoSurrender(winner.GetOppositeSide());
                    }
                    mission.EndMission(); // Only the normal hideout completion callback can credit the story.
                }
            }
            else if (phase != "observe") throw new InvalidOperationException("Unknown NPC Lives phase.");
            return new JObject { ["schema"] = "reign-npc-lives-native-evidence-v1", ["caseId"] = c["caseId"],
                ["origin"] = "operator_native_fixture", ["before"] = before, ["after"] = NativeLifeEvidence(storyId) };
        }
        private JObject NativeLifeCharacterEvidence(string heroId)
        {
            Hero hero = ReignActionService.HeroById(heroId) ?? throw new InvalidOperationException("No exact native NPC exists.");
            if (hero == Hero.MainHero) throw new InvalidOperationException("Choose an NPC identity.");
            var stories = State.Stories.Where(s => s.HeroId == heroId).ToList();
            var person = State.Characters.FirstOrDefault(n => n.HeroId == heroId);
            var character = person == null ? null : JObject.FromObject(person);
            if (character != null) character["Facts"] = JArray.FromObject(person.Facts.AsEnumerable().Reverse().Take(128).Reverse());
            int leaveCount = State.Leaves.Count(l => l.HeroId == heroId), announcementCount = State.Announcements.Count(a => a.CanRead(heroId)),
                meetingCount = State.Meetings.Count(m => m.IssuerId == heroId || m.TargetId == heroId);
            var party = hero.PartyBelongedTo;
            var journeys = _meetingTravel.Values.Where(j => j.Meeting.IssuerId == heroId || j.Meeting.TargetId == heroId).ToList();
            return new JObject { ["schema"] = "reign-npc-lives-character-evidence-v1", ["heroId"] = heroId,
                ["native"] = new JObject { ["name"] = hero.Name.ToString(), ["alive"] = hero.IsAlive, ["prisoner"] = hero.IsPrisoner,
                    ["wounded"] = hero.IsWounded, ["partyId"] = hero.PartyBelongedTo?.StringId ?? "", ["settlementId"] = hero.CurrentSettlement?.StringId ?? "",
                    ["active"] = hero.IsActive, ["travelling"] = hero.IsTraveling,
                    ["canMoveToSettlement"] = hero.CanMoveToSettlement(), ["isPregnant"] = hero.IsPregnant,
                    ["partyLeaderId"] = party?.LeaderHero?.StringId ?? "",
                    ["partyActive"] = party?.IsActive ?? false, ["partyDecisionLock"] = party?.Ai.DoNotMakeNewDecisions ?? false,
                    ["partyContents"] = party == null ? null : JObject.FromObject(ObserveLifePartyContents(party)),
                    ["partyTargetSettlementId"] = party?.TargetSettlement?.StringId ?? "",
                    ["partyPosition"] = party == null ? null : new JObject { ["x"] = party.Position.ToVec2().X,
                        ["y"] = party.Position.ToVec2().Y, ["onLand"] = party.Position.IsOnLand },
                    ["inMainParty"] = MobileParty.MainParty.MemberRoster.Contains(hero.CharacterObject), ["nativeRelation"] = hero.GetRelation(Hero.MainHero),
                    ["skills"] = new JObject(typeof(DefaultSkills).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .Where(p => p.PropertyType == typeof(SkillObject)).Select(p => (SkillObject)p.GetValue(null))
                        .Where(skill => skill != null).Select(skill => new JProperty(skill.StringId, hero.GetSkillValue(skill)))) },
                ["character"] = character, ["factCount"] = person?.Facts.Count ?? 0,
                ["stories"] = JArray.FromObject(stories.Take(64)), ["storyCount"] = stories.Count,
                ["leaves"] = JArray.FromObject(State.Leaves.Where(l => l.HeroId == heroId).Take(64)),
                ["announcements"] = JArray.FromObject(State.Announcements.Where(a => a.CanRead(heroId)).Reverse().Take(64).Reverse()),
                ["meetings"] = JArray.FromObject(State.Meetings.Where(m => m.IssuerId == heroId || m.TargetId == heroId).Take(64)),
                ["meetingHeld"] = IsMeetingHeld(heroId), ["meetingSettlementHeld"] = MustStayInMeetingSettlement(heroId),
                ["communicationHostReservations"] = JArray.FromObject(_communicationHosts.Values.Where(h => h.HostId == heroId).OrderBy(h => h.Id).Take(64)),
                ["communicationHostReservationCount"] = _communicationHosts.Values.Count(h => h.HostId == heroId),
                ["meetingInterception"] = ReignNpcLivesPatches.Snapshot(),
                ["meetingMovement"] = MeetingMovementSnapshot(hero), ["nativeTeleportation"] = NativeMeetingTeleportSnapshot(hero),
                ["summonsCamp"] = SummonsCampEvidence(heroId),
                ["relocationCommerceSuppressed"] = party != null && NpcPartyRelocationScope.SuppressesCommerceFor(party.StringId),
                ["meetingDuty"] = _lifeDuties.TryGetValue(heroId, out var duty) ? JObject.FromObject(duty) : null,
                ["meetingJourneys"] = JArray.FromObject(journeys.AsEnumerable().Reverse().Take(64).Reverse()), ["meetingJourneyCount"] = journeys.Count,
                ["leaveCount"] = leaveCount, ["announcementCount"] = announcementCount, ["meetingCount"] = meetingCount,
                ["truncated"] = stories.Count > 64 || leaveCount > 64 || announcementCount > 64 || meetingCount > 64 || journeys.Count > 64 || person?.Facts.Count > 128,
                ["nativeRewardReceipts"] = JArray.FromObject(State.Effects.Where(e => e.HeroId == heroId && _nativeEffects.Contains(e.Id)).Select(e => e.Id).Reverse().Take(128).Reverse()),
                ["outboxCount"] = _outbox.Count, ["failures"] = JObject.FromObject(_failedOperations),
                ["worldDay"] = CampaignTime.Now.ToDays, ["campaignId"] = _campaign, ["timelineId"] = _timeline, ["generation"] = Generation };
        }
        private JObject NativeLifeEvidence(string storyId) => new JObject {
            ["story"] = JObject.FromObject(State.Stories.Single(s => s.Id == storyId)),
            ["characterEvidence"] = NativeLifeCharacterEvidence(State.Stories.Single(s => s.Id == storyId).HeroId),
            ["encounters"] = new JArray(_storyEncounters.Where(p => p.Value.Value<string>("storyId") == storyId)
                .Select(p => new JObject { ["id"] = p.Key, ["record"] = p.Value.DeepClone() })),
            ["effects"] = JArray.FromObject(State.Effects.Where(e => e.StoryId == storyId)),
            ["outboxCount"] = _outbox.Count, ["failures"] = JObject.FromObject(_failedOperations),
            ["campaignId"] = _campaign, ["timelineId"] = _timeline, ["generation"] = Generation,
            ["missionActive"] = Mission.Current != null, ["worldDay"] = CampaignTime.Now.ToDays };

        private JObject PrepareNativeStoryPlace(JObject c)
        {
            string storyId = c.Value<string>("storyId") ?? "";
            string operationId = "npc_story_place:" + c.Value<string>("caseId");
            var journal = ReignAgreementBehavior.Instance ?? throw new InvalidOperationException("Native fixture journal is unavailable.");
            var retained = journal.NativeOperation(operationId);
            if (retained != null)
            {
                if (retained.Value<string>("storyId") != storyId)
                    throw new InvalidOperationException("This placement identity already belongs to another story.");
                return retained; // An uncertain or completed move is never repeated.
            }
            var story = State.Stories.SingleOrDefault(s => s.Id == storyId);
            var party = MobileParty.MainParty;
            var hero = ReignActionService.HeroById(story?.HeroId ?? "");
            var place = Settlement.Find(story?.LocationId ?? "");
            if (story == null || story.Status != "active" || story.Definition == null
                || story.PhaseIndex < 0 || story.PhaseIndex >= story.Definition.Phases.Count
                || place == null || (!place.IsTown && !place.IsVillage && !place.IsCastle && !place.IsHideout)
                || party == null || !party.IsActive || party.IsDisbanding || party.LeaderHero != Hero.MainHero
                || Hero.MainHero.IsDead || Hero.MainHero.IsChild || Hero.MainHero.IsPrisoner
                || party.Army != null || party.MapEvent != null || party.SiegeEvent != null
                || party.IsCurrentlyAtSea || party.IsTransitionInProgress || Mission.Current != null
                || hero == null || !hero.IsAlive || hero.IsChild || hero.IsPrisoner || hero.PartyBelongedTo != party
                || !party.MemberRoster.Contains(hero.CharacterObject) || place.IsUnderSiege || place.Party.MapEvent != null
                || (PlayerEncounter.Current != null && PlayerEncounter.EncounterSettlement == null)
                || _startingStoryEncounter || _storyEncounters.Values.Any(e => e.Value<string>("status") != "resolved"))
                throw new InvalidOperationException("Placement requires an accepted bound story, its actual free party companion and a safe independent land player outside encounters in progress.");
            if (place.IsHideout)
            {
                if (!story.Definition.Phases[story.PhaseIndex].Objectives.Any(o => o.Kind == "hideout")
                    || !place.Parties.Any(p => p.IsBandit) || !place.Hideout.NextPossibleAttackTime.IsPast)
                    throw new InvalidOperationException("The bound hideout must contain bandits and permit a real story attack now.");
            }
            else if (FactionManager.IsAtWarAgainstFaction(party.MapFaction, place.MapFaction))
                throw new InvalidOperationException("The bound settlement is hostile to the player.");
            var receipt = new JObject { ["schema"] = "reign-npc-lives-place-fixture-v1", ["origin"] = "operator_location_setup",
                ["operationId"] = operationId, ["storyId"] = story.Id, ["revision"] = story.Revision,
                ["locationId"] = place.StringId, ["status"] = "started", ["invocations"] = 1,
                ["before"] = NativeStoryPlaceEvidence(), ["semanticVerification"] = "fixture_only" };
            journal.SaveNativeOperation(operationId, receipt); // Persist before ending or moving any native encounter.
            try
            {
                if (PlayerEncounter.Current != null) PlayerEncounter.Finish();
                // A retained test save may have a different party and menu settlement.
                if (party.CurrentSettlement != null) TaleWorlds.CampaignSystem.Actions.LeaveSettlementAction.ApplyForParty(party);
                party.SetPositionAfterMapChange(place.GatePosition); party.SetMoveModeHold();
                EncounterManager.StartSettlementEncounter(party, place);
                if (PlayerEncounter.Current == null || PlayerEncounter.EncounterSettlement != place)
                    throw new InvalidOperationException("The bound native encounter did not initialize.");
                if (party.CurrentSettlement != place || PlayerEncounter.LocationEncounter == null) PlayerEncounter.EnterSettlement();
                GameMenu.ActivateGameMenu(place.IsHideout ? "hideout_place" : place.IsTown ? "town" : place.IsVillage ? "village" : "castle");
                if (party.CurrentSettlement != place || Settlement.CurrentSettlement != place || Hero.MainHero.CurrentSettlement != place
                    || PlayerEncounter.EncounterSettlement != place || PlayerEncounter.LocationEncounter == null || party.MapEvent != null
                    || hero.PartyBelongedTo != party || !party.MemberRoster.Contains(hero.CharacterObject))
                    throw new InvalidOperationException("Native location pointers or party presence did not agree after placement.");
                receipt["status"] = "prepared";
            }
            catch (Exception ex) { receipt["status"] = "uncertain"; receipt["error"] = ex.Message; }
            finally
            {
                TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                receipt["after"] = NativeStoryPlaceEvidence(); journal.SaveNativeOperation(operationId, receipt);
            }
            return receipt;
        }
        private static JObject NativeStoryPlaceEvidence() => new JObject {
            ["partySettlementId"] = MobileParty.MainParty?.CurrentSettlement?.StringId ?? "",
            ["heroSettlementId"] = Hero.MainHero?.CurrentSettlement?.StringId ?? "",
            ["menuSettlementId"] = Settlement.CurrentSettlement?.StringId ?? "",
            ["encounterSettlementId"] = PlayerEncounter.EncounterSettlement?.StringId ?? "",
            ["encounterActive"] = PlayerEncounter.Current != null, ["locationEncounterReady"] = PlayerEncounter.LocationEncounter != null,
            ["missionActive"] = Mission.Current != null, ["battleActive"] = MobileParty.MainParty?.MapEvent != null,
            ["position"] = MobileParty.MainParty?.Position.ToString(), ["worldDay"] = CampaignTime.Now.ToDays };
    }
}
