using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.PartyAgency;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Dialogue.UI;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.UI;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.UI;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> SearchTargetsAsync(JObject command)
        {
            string mode = NormalizeMode(command.Value<string>("mode"));
            string search = command.Value<string>("search") ?? string.Empty;
            int limit = Math.Max(1, Math.Min(2000, command.Value<int?>("limit") ?? 25));
            JObject data = await ReignMainThread.InvokeAsync(() =>
            {
                IEnumerable<Hero> heroes = AvailableHeroesForMode(mode);
                if (command.Property("isLord") != null)
                {
                    bool required = command.Value<bool>("isLord");
                    heroes = heroes.Where(hero => hero.IsLord == required);
                }
                if (command.Property("minimumAge") != null)
                {
                    double minimumAge = Math.Max(0d, command.Value<double>("minimumAge"));
                    heroes = heroes.Where(hero => hero.Age >= minimumAge);
                }
                if (command.Property("minClanTier") != null)
                {
                    int minimumTier = Math.Max(0, command.Value<int>("minClanTier"));
                    heroes = heroes.Where(hero => (hero.Clan?.Tier ?? 0) >= minimumTier);
                }
                if (command.Property("maxClanTier") != null)
                {
                    int maximumTier = Math.Max(0, command.Value<int>("maxClanTier"));
                    heroes = heroes.Where(hero => (hero.Clan?.Tier ?? 0) <= maximumTier);
                }
                if (command.Property("maxHonor") != null)
                {
                    int maximumHonor = Math.Max(
                        -2, Math.Min(
                            2, command.Value<int>("maxHonor")));
                    heroes = heroes.Where(hero =>
                        hero.GetTraitLevel(DefaultTraits.Honor)
                            <= maximumHonor);
                }
                List<Hero> ranked = RankHeroes(heroes, search).Take(limit).ToList();
                JArray targets = new JArray(ranked.Select(HeroTargetJson));
                JArray events = new JArray();
                if (mode == "social_event")
                {
                    events = new JArray((ReignServices.SocialEvents?.GetLiveTestEventRecords() ?? new List<SocialEventRecord>())
                        .Where(record => record != null && (string.IsNullOrWhiteSpace(search)
                            || (record.DisplayName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                            || (record.EventId ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                        .Take(limit).Select(record => new JObject
                        {
                             ["eventId"] = record.EventId ?? string.Empty, ["name"] = record.DisplayName ?? string.Empty,
                             ["templateId"] = record.TemplateId ?? string.Empty, ["status"] = record.Status.ToString(),
                             ["settlementId"] = record.SettlementStringId ?? string.Empty, ["wilderness"] = record.IsGeneratedWildernessEvent
                             ,["isOpen"] = record.IsOpen((float)CampaignTime.Now.ToDays)
                             ,["attendeeHeroIds"] = new JArray(
                                 record.AttendeeHeroStringIds
                                     ?? new List<string>())
                         }));
                }
                return new JObject
                {
                    ["mode"] = mode,
                    ["search"] = search,
                    ["isLord"] = command.Property("isLord") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<bool>("isLord")),
                    ["minimumAge"] = command.Property("minimumAge") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<double>("minimumAge")),
                    ["minClanTier"] = command.Property("minClanTier") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<int>("minClanTier")),
                    ["maxClanTier"] = command.Property("maxClanTier") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<int>("maxClanTier")),
                    ["maxHonor"] = command.Property("maxHonor") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<int>("maxHonor")),
                    ["targets"] = targets,
                    ["events"] = events
                };
            }).ConfigureAwait(false);
            return LiveCommandResult.Completed("Target search completed.", data);
        }

        private static async Task<LiveCommandResult> OpenAsync(JObject command)
        {
            string runId = command.Value<string>("runId") ?? string.Empty;
            string mode = NormalizeMode(command.Value<string>("mode"));
            if (!GetSupportedModes().Contains(mode)) return LiveCommandResult.Failed("Unsupported or currently unavailable interaction mode.");
            if (!string.IsNullOrWhiteSpace(_activeRunId)) await CloseSessionAsync(false).ConfigureAwait(false);
            _activeRunId = runId;
            _activeMode = mode;
            _lastNpcReplyText = string.Empty;
            _presentation = string.Equals(command.Value<string>("presentation"), "visible", StringComparison.OrdinalIgnoreCase) ? "visible" : "headless";
            CaptureGuardedSettings(command.Value<string>("effects"));
            try
            {
                List<string> searches = ReadSearches(command);
                bool allowDisposableCourtMatter =
                    mode == "court_event"
                    && command.Value<bool?>("createTestEvent") == true;
                List<Hero> heroes = await ReignMainThread.InvokeAsync(() =>
                    ResolveHeroesForMode(mode, searches, allowDisposableCourtMatter)).ConfigureAwait(false);
                switch (mode)
                {
                    case "individual_chat":
                        if (heroes.Count == 0) return FailOpen("No living non-player hero matched the requested target.");
                        if (_presentation == "visible")
                        {
                            await ReignMainThread.InvokeAsync(() => ReignIndividualChatScreenManager.OpenForHero(heroes[0])).ConfigureAwait(false);
                            _individualVm = await WaitForAsync(() => ReignIndividualChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                        }
                        else _individualVm = await ReignMainThread.InvokeAsync(() => new ReignIndividualChatScreenVM(heroes[0], () => { }, null, false)).ConfigureAwait(false);
                        if (_individualVm == null) return FailOpen("The individual conversation controller did not initialize.");
                        break;
                    case "ambassador_official":
                        {
                            if (heroes.Count == 0) return FailOpen("No resident foreign ambassador matched the requested target.");
                            ReignCourtCampaignBehavior court = ReignServices.Court;
                            ForeignAmbassadorPosting posting = court?.ForeignAmbassadors.FirstOrDefault(candidate => candidate?.IsResident == true
                                && string.Equals(candidate.HeroStringId, heroes[0].StringId, StringComparison.OrdinalIgnoreCase));
                            if (court == null) return FailOpen("The production court controller is unavailable.");
                            if (!court.TryBuildOfficialAmbassadorContext(posting, out Hero envoy, out JObject context, out string error))
                                return FailOpen(string.IsNullOrWhiteSpace(error) ? "The official ambassador context was unavailable." : error);
                            heroes = new List<Hero> { envoy };
                            if (_presentation == "visible")
                            {
                                await ReignMainThread.InvokeAsync(() => ReignIndividualChatScreenManager.OpenForHero(envoy, context, null)).ConfigureAwait(false);
                                _individualVm = await WaitForAsync(() => ReignIndividualChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                            }
                            else _individualVm = await ReignMainThread.InvokeAsync(() => new ReignIndividualChatScreenVM(envoy, () => { }, null, false, context)).ConfigureAwait(false);
                            if (_individualVm == null) return FailOpen("The official ambassador conversation controller did not initialize.");
                            break;
                        }
                    case "party_chat":
                        bool targetedReview = command.Value<bool?>("targetedReview") == true;
                        if (targetedReview && heroes.Count != 1)
                            return FailOpen("A targeted temporary-guest review requires exactly one available hero.");
                        if (!targetedReview && heroes.Count < 2)
                            return FailOpen("Party chat requires two or more currently available heroes.");
                        string reviewContext = command.Value<string>("reviewContext") ?? string.Empty;
                        if (_presentation == "visible" && targetedReview)
                        {
                            await ReignMainThread.InvokeAsync(() =>
                                ReignPartyChatScreenManager.OpenTargetedReview(
                                    heroes[0], reviewContext, _ => { })).ConfigureAwait(false);
                            _partyVm = await WaitForAsync(() =>
                                ReignPartyChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                        }
                        else if (_presentation == "visible")
                        {
                            await ReignMainThread.InvokeAsync(ReignPartyChatScreenManager.Open).ConfigureAwait(false);
                            _partyVm = await WaitForAsync(() => ReignPartyChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                        }
                        else if (targetedReview)
                            _partyVm = await ReignMainThread.InvokeAsync(() =>
                                new ReignPartyChatScreenVM(() => { }, heroes[0], reviewContext)).ConfigureAwait(false);
                        else _partyVm = await ReignMainThread.InvokeAsync(() => new ReignPartyChatScreenVM(() => { })).ConfigureAwait(false);
                        if (_partyVm == null || (!targetedReview
                            && !await ReignMainThread.InvokeAsync(() =>
                                _partyVm.SelectAutomationHeroes(heroes)).ConfigureAwait(false)))
                            return FailOpen("The party conversation controller could not select every requested hero.");
                        break;
                    case "social_event":
                        _eventSession = await CreateSocialEventSessionAsync(command, heroes).ConfigureAwait(false);
                        if (_eventSession == null) return FailOpen("No eligible scheduled social event could be opened. Enter a town or specify an existing event.");
                        await InitializeEventVmAsync(heroes.Count > 0 ? heroes : _eventSession.Record.GetAttendees().Take(3).ToList()).ConfigureAwait(false);
                        if (_eventVm == null) return FailOpen("The social-event conversation controller did not initialize.");
                        break;
                    case "wilderness_event":
                        int minimumWildernessParticipants = Math.Max(
                            1,
                            Math.Min(
                                5,
                                command.Value<int?>(
                                    "minimumParticipants") ?? 1));
                        _eventSession = await (ReignServices.SocialEvents?.StartLiveTestWildernessEventAsync(
                                heroes,
                                minimumWildernessParticipants)
                            ?? Task.FromResult<ReignSocialEventSession>(null)).ConfigureAwait(false);
                        if (_eventSession == null) return FailOpen("The production wilderness-event eligibility or generation step rejected this game state.");
                        await InitializeEventVmAsync(_eventSession.Record.GetAttendees().Take(5).ToList()).ConfigureAwait(false);
                        if (_eventVm == null) return FailOpen("The wilderness conversation controller did not initialize.");
                        heroes = _eventSession.Record.GetAttendees().Take(5).ToList();
                        break;
                    case "correspondence":
                        {
                            string error = await OpenCorrespondenceAsync(
                                command,
                                heroes).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(error))
                                return FailOpen(error);
                            heroes = new List<Hero> { _correspondenceHero };
                            break;
                        }
                    case "court_event":
                        {
                            string error = await OpenCourtAsync(
                                command,
                                heroes).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(error))
                                return FailOpen(error);
                            heroes = new List<Hero> { _courtSpeaker };
                            break;
                        }
                }
                return LiveCommandResult.Completed("Production interaction session opened.", new JObject
                {
                    ["runId"] = runId, ["mode"] = mode, ["presentation"] = _presentation,
                    ["sessionHandle"] = runId + ":" + mode, ["targets"] = new JArray(heroes.Select(HeroTargetJson)),
                    ["eventId"] = _eventSession?.Record?.EventId ?? string.Empty
                });
            }
            catch (Exception ex) { return FailOpen(ex.Message); }
        }

        private static async Task<LiveCommandResult> SendAsync(JObject command)
        {
            string requestedText = (command.Value<string>("text") ?? string.Empty).Trim();
            bool contextualPartyAgencyResponse =
                command.Value<bool?>("contextualPartyAgencyResponse") == true;
            string text = contextualPartyAgencyResponse
                ? BuildPartyAgencyContextualPlayerResponse(requestedText, _lastNpcReplyText)
                : requestedText;
            if (string.IsNullOrWhiteSpace(text)) return LiveCommandResult.Failed("No player text was supplied.");
            string commandId = command.Value<string>("commandId") ?? Guid.NewGuid().ToString("N");
            string correlation = "live-" + commandId;
            int scene = command.Value<int?>("sceneIndex") ?? 0;
            int turn = command.Value<int?>("turnIndex") ?? 0;
            bool guardedPromptOverride = command.Value<bool?>("guardedPromptOverride") == true;
            string guardedPromptOverrideDirective =
                (command.Value<string>("guardedPromptOverrideDirective") ?? string.Empty).Trim();
            string guardedPromptOverrideOwnerCommandId =
                (command.Value<string>("guardedPromptOverrideOwnerCommandId") ?? string.Empty).Trim();
            bool governmentCertificationMemoryIsolation =
                command.Value<bool?>("governmentCertificationMemoryIsolation") == true;
            bool actionAcceptanceProbe =
                command.Value<bool?>("actionAcceptanceProbe") == true;
            if ((governmentCertificationMemoryIsolation || actionAcceptanceProbe)
                && guardedPromptOverride
                && string.IsNullOrWhiteSpace(guardedPromptOverrideOwnerCommandId))
            {
                // The server independently verifies this exact live command, run,
                // game instance, and disposable save before honoring the scope.
                guardedPromptOverrideOwnerCommandId = commandId;
            }
            if ((_activeMode == "individual_chat" || _activeMode == "ambassador_official") && _individualVm != null)
            {
                if (!await WaitForIndividualPresentationAsync(_activeRunId, _individualVm, _individualVm.ConversationHero).ConfigureAwait(false))
                    return LiveCommandResult.Failed("The conversation opening did not finish for this run and NPC.");
                Reign.Core.Contracts.Actions.ActionAgreement attachedAction = null;
                if (command["structuredActionDraft"] != null)
                {
                    try
                    {
                        text = await ReignMainThread.InvokeAsync(() =>
                        {
                            var enrollment = command["enrollment"] as JObject;
                            string expected = enrollment?.Value<string>("disposableSaveName") ?? "";
                            string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
                            if (!_armed || _activeMode != "individual_chat" || !actionAcceptanceProbe
                                || command.Value<bool?>("naturalLanguageCase") != true
                                || enrollment?.Value<string>("source") != "reign_mcp_action_probe"
                                || string.IsNullOrWhiteSpace(enrollment?.Value<string>("campaignTestRunId"))
                                || expected.Length == 0 || baseline.Length == 0 || expected == baseline
                                || expected != ActiveSaveName() || command.Value<string>("expectedSaveName") != expected
                                || enrollment.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                                || command.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                                || enrollment.Value<string>("timelineId") != (ReignServices.WorldHistory?.TimelineId ?? "main")
                                || command.Value<string>("gameInstanceId") != _gameInstanceId
                                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop
                                || command.Value<string>("structuredActionActorHeroId") != _individualVm.ConversationHero?.StringId
                                || _individualVm.ActionBusy
                                || (_presentation == "visible" && !ReferenceEquals(_individualVm, ReignIndividualChatScreenManager.ActiveViewModel)))
                                throw new InvalidOperationException("Typed conversations require the exact paused, armed, enrolled Current and open NPC.");
                            var draft = (command["structuredActionDraft"] as JObject)?.ToObject<Reign.Core.Contracts.Actions.ActionAgreement>()
                                ?? throw new InvalidOperationException("Missing typed proposal.");
                            string player = Hero.MainHero.StringId, npc = _individualVm.ConversationHero.StringId;
                            if (draft.Clauses == null || draft.Clauses.Count == 0 || draft.Clauses.Count > 64
                                || draft.Clauses.Any(c => c == null || Reign.Core.Contracts.Actions.ReignActionCatalog.Find(c.ActionId)?.ConversationEligible != true))
                                throw new InvalidOperationException("Only registered conversation actions can be attached.");
                            bool reviewNpcOffer = command.Value<bool?>("reviewNpcOffer") == true;
                            if (draft.PlayerId != player || draft.NpcId != npc
                                || draft.ProposerId != (reviewNpcOffer ? npc : player))
                                throw new InvalidOperationException("The attached proposal must match the current player, open NPC and selected proposal author.");
                            if (reviewNpcOffer)
                            {
                                // Review uses the saved production offer, never caller-supplied
                                // signatures or a substitute proposal. Normal Send supplies
                                // player consent only after this exact rendered-terms check.
                                if (draft.Origin != Reign.Core.Contracts.Actions.ActionOrigin.Conversation || draft.Revision < 1)
                                    throw new InvalidOperationException("The reviewed NPC offer has invalid origin or revision.");
                                ReignBeta.WorldSimulation.Actions.ReignActionWording.Render(draft);
                                var pending = ReignServices.Agreement?.Offers
                                    .FirstOrDefault(a => a.Id == draft.Id);
                                if (pending == null || pending.State != Reign.Core.Contracts.Actions.AgreementState.Counteroffered
                                    || pending.PlayerId != player || pending.NpcId != npc || pending.ProposerId != npc
                                    || pending.Origin != Reign.Core.Contracts.Actions.ActionOrigin.Conversation
                                    || pending.Revision != draft.Revision || pending.TermsHash() != draft.TermsHash()
                                    || pending.NpcAcceptedHash != pending.TermsHash() || pending.PlayerAcceptedHash.Length != 0)
                                    throw new InvalidOperationException("The reviewed NPC offer is missing, changed, unsigned or no longer pending.");
                                draft = ReignBeta.Actions.ReignActionService.Copy(pending);
                            }
                            else
                            {
                                draft.Origin = Reign.Core.Contracts.Actions.ActionOrigin.Conversation;
                                draft.State = Reign.Core.Contracts.Actions.AgreementState.Draft;
                                draft.PlayerAcceptedHash = ""; draft.NpcAcceptedHash = "";
                            }
                            ReignBeta.WorldSimulation.Actions.ReignActionWording.Render(draft);
                            var replayCandidate = ReignBeta.Actions.ReignActionService.Copy(draft);
                            replayCandidate.ConversationId = ReignCampaignIdentity.CurrentCampaignId() + ":"
                                + (ReignServices.WorldHistory?.TimelineId ?? "main") + ":" + npc;
                            var savedJournal = ReignServices.Agreement?.Agreements
                                .FirstOrDefault(j => j.Agreement.Id == draft.Id);
                            // Confirmation of exact completed terms uses normal Send's saved
                            // receipt route. New, changed, unsigned, pending and operator
                            // proposals still require current native execution eligibility.
                            bool completedReplay = !reviewNpcOffer
                                && Reign.Core.Contracts.Actions.AgreementRules.IsExactCompletedReplay(replayCandidate, savedJournal);
                            var errors = completedReplay ? new List<string>()
                                : ReignBeta.Actions.ReignActionService.Preview(draft);
                            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
                            string composed = _individualVm.PrepareAutomationActionLine(draft, text);
                            attachedAction = ReignBeta.Actions.ReignActionService.Copy(_individualVm.ActionDraft);
                            return composed;
                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex) { return LiveCommandResult.Failed("Typed action preflight failed: " + ex.Message); }
                }
                ReignDialogueReply reply = await _individualVm.SendAutomationLineAsync(
                    text, correlation, _activeRunId, scene, turn,
                    guardedPromptOverride, guardedPromptOverrideDirective,
                    guardedPromptOverrideOwnerCommandId, attachedAction).ConfigureAwait(false);
                _lastNpcReplyText = reply.Text ?? string.Empty;
                JObject data = new JObject
                {
                    ["ok"] = reply.Ok, ["text"] = reply.Text ?? string.Empty, ["emotion"] = reply.Emotion ?? string.Empty,
                    ["playerText"] = text,
                    ["attachedAction"] = attachedAction == null ? null : JObject.FromObject(attachedAction),
                    ["contextualPartyAgencyResponse"] = contextualPartyAgencyResponse,
                    ["guardedPromptOverrideRequested"] = guardedPromptOverride,
                    ["governmentCertificationMemoryIsolationRequested"] =
                        governmentCertificationMemoryIsolation,
                    ["intent"] = reply.Intent ?? string.Empty, ["error"] = reply.Error ?? string.Empty,
                    ["correlationId"] = reply.CorrelationId ?? correlation, ["sessionId"] = reply.ConversationSessionId ?? string.Empty,
                    ["exchangeId"] = reply.ExchangeId ?? string.Empty, ["turnIds"] = reply.TurnIds,
                    ["selectedContextPulls"] = reply.SelectedContextPulls, ["contextBundles"] = reply.ContextBundles,
                    ["memoryWrites"] = reply.MemoryWrites, ["rawResponse"] = reply.RawResponse,
                    ["conceptionGateNeeded"] = reply.ConceptionGateNeeded, ["queuedActionCount"] = reply.QueuedActions?.Count ?? 0,
                    ["queuedActions"] = new JArray((reply.QueuedActions
                        ?? new List<ReignWorldActionRecord>())
                        .Where(action => action != null)
                        .Select(action => new JObject
                        {
                            ["actionId"] = action.ActionId ?? string.Empty,
                            ["type"] = action.Type.ToString(),
                            ["status"] = action.Status.ToString(),
                            ["negotiatedCommand"] = action.NegotiatedCommand ?? string.Empty
                        }))
                };
                if (attachedAction != null)
                    data["agreementJournal"] = await ReignMainThread.InvokeAsync(() =>
                    {
                        var journal = ReignServices.Agreement?.Agreements
                            .FirstOrDefault(j => j.Agreement.Id == attachedAction.Id);
                        return journal == null ? null : JObject.FromObject(journal);
                    }).ConfigureAwait(false);
                LiveCommandResult result = reply.Ok ? LiveCommandResult.Completed("Individual dialogue turn completed.", data) : LiveCommandResult.Failed(reply.Error, data);
                result.CorrelationIds.Add(reply.CorrelationId ?? correlation);
                if (reply.ConceptionGateNeeded) result.Status = "needs_input";
                return result;
            }
            if (_activeMode == "party_chat" && _partyVm != null)
            {
                ReignPartyChatBatchResult batch = await _partyVm
                    .SendAutomationLineAsync(
                        text,
                        _activeRunId,
                        scene,
                        turn,
                        correlation)
                    .ConfigureAwait(false);
                _lastNpcReplyText = string.Join("\n", batch.Replies
                    .Where(reply => reply != null && !string.IsNullOrWhiteSpace(reply.Text))
                    .Select(reply => reply.Text));
                JObject data = new JObject
                {
                    ["ok"] = batch.Ok, ["error"] = batch.Error ?? string.Empty, ["sessionId"] = batch.SessionId ?? string.Empty,
                    ["playerText"] = text,
                    ["contextualPartyAgencyResponse"] = contextualPartyAgencyResponse,
                    ["exchangeId"] = batch.ExchangeId ?? string.Empty,
                    ["replies"] = new JArray(batch.Replies.Select(reply => new JObject
                    {
                        ["heroId"] = reply.SpeakerHeroStringId ?? string.Empty, ["heroName"] = reply.SpeakerName ?? string.Empty,
                        ["text"] = reply.Text ?? string.Empty, ["emotion"] = reply.Emotion ?? string.Empty,
                        ["intent"] = reply.Intent ?? string.Empty, ["relationshipSignal"] = reply.RelationshipSignal ?? string.Empty,
                        ["relationshipAssessments"] = reply.RelationshipAssessments,
                        ["participation"] = reply.Participation ?? string.Empty, ["reactionTargetHeroStringId"] = reply.ReactionTargetHeroStringId ?? string.Empty,
                        ["correlationId"] = reply.CorrelationId ?? string.Empty, ["sessionId"] = reply.ConversationSessionId ?? string.Empty,
                        ["exchangeId"] = reply.ExchangeId ?? string.Empty, ["selectedContextPulls"] = reply.SelectedContextPulls,
                        ["queuedActionCount"] = reply.QueuedActions?.Count ?? 0, ["ok"] = reply.Ok, ["error"] = reply.Error ?? string.Empty,
                        ["clientTotalMs"] = reply.ClientTotalMs, ["timingSummary"] = reply.TimingSummary ?? string.Empty,
                        ["rawResponse"] = reply.RawResponse
                    }))
                };
                LiveCommandResult result = batch.Ok ? LiveCommandResult.Completed("Party dialogue turn completed.", data) : LiveCommandResult.Failed(batch.Error, data);
                result.CorrelationIds.AddRange(batch.Replies.Select(reply => reply.CorrelationId).Where(value => !string.IsNullOrWhiteSpace(value)));
                return result;
            }
            if ((_activeMode == "social_event" || _activeMode == "wilderness_event") && _eventVm != null)
            {
                ReignSocialEventTurnReply turnReply = await _eventVm.SendAutomationLineAsync(text, correlation).ConfigureAwait(false);
                JObject data = new JObject
                {
                    ["ok"] = turnReply.Ok, ["idempotent"] = turnReply.Idempotent, ["turnId"] = turnReply.TurnId ?? string.Empty,
                    ["error"] = turnReply.Error ?? string.Empty,
                    ["replies"] = new JArray(turnReply.ParticipantResults.Select(reply => new JObject
                    {
                        ["heroId"] = reply.HeroStringId ?? string.Empty, ["text"] = reply.Text ?? string.Empty,
                        ["correlationId"] = reply.CorrelationId ?? string.Empty,
                        ["participation"] = reply.Participation ?? string.Empty, ["reactionTargetHeroStringId"] = reply.ReactionTargetHeroStringId ?? string.Empty,
                        ["emotion"] = reply.Emotion ?? string.Empty, ["intent"] = reply.Intent ?? string.Empty,
                        ["relationshipSignal"] = reply.RelationshipSignal ?? string.Empty, ["ok"] = reply.Ok, ["error"] = reply.Error ?? string.Empty
                        ,["relationshipAssessments"] = reply.RelationshipAssessments
                        ,["selectedContextPulls"] = reply.SelectedContextPulls
                        ,["contextBundles"] = reply.ContextBundles
                        ,["queuedActionCount"] = reply.QueuedActions?.Count ?? 0
                        ,["clientTotalMs"] = reply.ClientTotalMs
                        ,["timingSummary"] = reply.TimingSummary ?? string.Empty
                        ,["rawResponse"] = reply.RawResponse
                    })),
                    ["addressedHeroIds"] = new JArray(turnReply.AddressedHeroStringIds), ["joinedHeroIds"] = new JArray(turnReply.JoinedHeroStringIds),
                    ["wanderedHeroIds"] = new JArray(turnReply.WanderedHeroStringIds), ["correlationId"] = correlation
                    ,["rawResponse"] = turnReply.RawResponse
                };
                LiveCommandResult result = turnReply.Ok ? LiveCommandResult.Completed("Social-event group turn completed.", data) : LiveCommandResult.Failed(turnReply.Error, data);
                result.CorrelationIds.Add(correlation);
                result.CorrelationIds.AddRange(turnReply.ParticipantResults.Select(reply => reply.CorrelationId).Where(value => !string.IsNullOrWhiteSpace(value)));
                return result;
            }
            if (_activeMode == "correspondence" && _correspondenceHero != null)
                return await SendCorrespondenceAsync(text, correlation).ConfigureAwait(false);
            if (_activeMode == "court_event" && _courtMatter != null && _courtSpeaker != null)
                return await SendCourtAsync(text, correlation).ConfigureAwait(false);
            return LiveCommandResult.Failed("No matching production interaction session is open for this run.");
        }

        private static async Task<LiveCommandResult> SelectParticipantsAsync(JObject command)
        {
            List<Hero> heroes = await ReignMainThread.InvokeAsync(() => ResolveHeroesForMode(_activeMode, ReadSearches(command))).ConfigureAwait(false);
            bool selected = false;
            if (_partyVm != null) selected = await ReignMainThread.InvokeAsync(() => _partyVm.SelectAutomationHeroes(heroes)).ConfigureAwait(false);
            else if (_eventVm != null) selected = await ReignMainThread.InvokeAsync(() => _eventVm.SelectAutomationHeroes(heroes)).ConfigureAwait(false);
            return selected ? LiveCommandResult.Completed("Participants selected.", new JObject { ["targets"] = new JArray(heroes.Select(HeroTargetJson)) }) : LiveCommandResult.Failed("The requested participants could not be selected.");
        }

        private static async Task<LiveCommandResult> NextPhaseAsync()
        {
            if (_eventVm == null) return LiveCommandResult.Failed("Next phase is available only for social events.");
            bool advanced = await _eventVm.AdvanceAutomationPhaseAsync().ConfigureAwait(false);
            return advanced ? LiveCommandResult.Completed("The production event advanced to its next phase.") : LiveCommandResult.Failed("The event has no next phase or is busy.");
        }

        private static async Task<LiveCommandResult> WaitApproachAsync()
        {
            if (_eventVm == null) return LiveCommandResult.Failed("Wait Approach is available only for social events.");
            await ReignMainThread.InvokeAsync(_eventVm.ExecuteWaitApproach).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);
            return LiveCommandResult.Completed("The production Wait Approach control was invoked; subsequent participants are recorded by the event turn audit.");
        }

        private static async Task<LiveCommandResult> ResolveConfirmationAsync(bool confirm)
        {
            ReignIndividualChatScreenVM viewModel = await ReignMainThread.InvokeAsync(() =>
                _individualVm ?? ReignIndividualChatScreenManager.ActiveViewModel).ConfigureAwait(false);
            if (viewModel == null || !viewModel.IsPregnancyWarningVisible)
                return LiveCommandResult.Failed("No individual-chat confirmation is pending.");

            Task<ReignIndividualChatScreenVM.PregnancyDecisionReceipt> completion =
                await ReignMainThread.InvokeAsync(
                    viewModel.ObservePregnancyDecisionCompletionAsync).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (confirm) viewModel.ExecuteProceedPregnancy();
                else viewModel.ExecutePullOut();
            }).ConfigureAwait(false);

            Task boundedDeadline = Task.Delay(TimeSpan.FromSeconds(30));
            Task finished = await Task.WhenAny(completion, boundedDeadline).ConfigureAwait(false);
            if (finished != completion)
            {
                JObject timeoutState = await ReignMainThread.InvokeAsync(
                    viewModel.PregnancyAutomationStateJson).ConfigureAwait(false);
                return LiveCommandResult.Failed(
                    "The pregnancy decision did not reach a terminal view-model state before the bounded deadline.",
                    timeoutState);
            }

            ReignIndividualChatScreenVM.PregnancyDecisionReceipt receipt =
                await completion.ConfigureAwait(false);
            JObject evidence = receipt.ToJson();
            return receipt.Ok
                ? LiveCommandResult.Completed(
                    confirm ? "Pending confirmation was accepted." : "Pending confirmation was denied.",
                    evidence)
                : LiveCommandResult.Failed(
                    string.IsNullOrWhiteSpace(receipt.Error)
                        ? "The pregnancy decision did not complete successfully."
                        : receipt.Error,
                    evidence);
        }
    }
}
