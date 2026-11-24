using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Government;
using ReignBeta.World;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif

namespace ReignBeta.Platform.Integration
{
    public static partial class ReignServerClient
    {
        internal static readonly AsyncLocal<Action<JObject>> PartyAgencySendOrigin = new AsyncLocal<Action<JObject>>();
        internal static bool IsDelimitedSpeechOnlyResponse(JObject response) =>
            response?["conversationExchange"]?["acceptedUnit"]?["outputContract"]?.Value<string>() == "ollama_delimited_speech";

        public static async Task<bool> IngestSocialEventAnnouncementAsync(SocialEventRecord record)
        {
            try
            {
                if (record == null)
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["eventType"] = "social_event_announced",
                    ["socialEventId"] = record.EventId ?? string.Empty,
                    ["templateId"] = record.TemplateId ?? string.Empty,
                    ["displayName"] = record.DisplayName ?? string.Empty,
                    ["locationId"] = record.SettlementStringId ?? string.Empty,
                    ["summary"] = "A Bannerlord Reign social event was announced: " + (record.DisplayName ?? "social event") + ".",
                    ["actorIds"] = new JArray(record.AttendeeHeroStringIds ?? new List<string>())
                };

                JObject response = await PostJsonAsync("/events/ingest", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event announcement ingest failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<bool> StartSocialEventAsync(ReignSocialEventSession session)
        {
            try
            {
                if (session?.Record == null)
                {
                    return false;
                }

                JObject payload = BuildSocialEventPayload(session, null, string.Empty);
                payload["correlationId"] = NewCorrelationId("social_event_start", session.Record.EventId);
                payload["mode"] = "social_event";
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/social/start", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /events/social/start eventId=" + session.Record.EventId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event start failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<bool> ResolveSocialEventAsync(ReignSocialEventSession session)
        {
            JObject response = await ResolveSocialEventDetailedAsync(session).ConfigureAwait(false);
            return response.Value<bool?>("ok") == true;
        }

        public static async Task<JObject> ResolveSocialEventDetailedAsync(ReignSocialEventSession session)
        {
            try
            {
                if (session?.Record == null)
                {
                    return new JObject { ["ok"] = false, ["error"] = "No social event is active." };
                }

                JObject payload = BuildSocialEventPayload(session, null, string.Empty);
                payload["correlationId"] = NewCorrelationId("social_event_resolve", session.Record.EventId);
                payload["mode"] = "social_event";
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/social/resolve", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /events/social/resolve eventId=" + session.Record.EventId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event resolve failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<bool> FinishSocialEventPhaseAsync(ReignSocialEventSession session)
        {
            if (session?.Record == null) return false;
            string phaseKey = session.XpPhaseKey;
            ReignXpInteraction xp = await ReignXpClient.BeginXpInteractionAsync().ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => xp.Owner?.FinishSocialPhase(phaseKey, xp.Options)).ConfigureAwait(false);
            return true;
        }

        public static async Task<JObject> RollSocialEventApproachesAsync(ReignSocialEventSession session, IEnumerable<Hero> candidates)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject { ["ok"] = false, ["error"] = "Local server is disabled." };
                }

                if (session?.Record == null)
                {
                    return new JObject { ["ok"] = false, ["error"] = "No social event is active." };
                }

                List<Hero> safeCandidates = (candidates ?? Enumerable.Empty<Hero>())
                    .Where(x => x != null && x != Hero.MainHero && !string.IsNullOrWhiteSpace(x.StringId))
                    .Distinct()
                    .ToList();
                JObject payload = BuildSocialEventPayload(session, null, string.Empty);
                payload["correlationId"] = NewCorrelationId("social_event_approach", session.Record.EventId);
                payload["approachCandidates"] = new JArray(safeCandidates.Select(BuildHeroProfile));
                payload["remainingActiveSlots"] = session.RemainingActiveSlots;
                payload["playerIsFemale"] = Hero.MainHero?.IsFemale ?? false;
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/social/approaches/roll", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /events/social/approaches/roll eventId=" + session.Record.EventId
                    + " candidates=" + safeCandidates.Count
                    + " selected=" + (response["approachHeroStringIds"] as JArray)?.Count
                    + " clientMs=" + timer.ElapsedMilliseconds
                    + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event approach roll failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<ReignEventReply> RequestSocialEventResponseAsync(
            ReignSocialEventSession session,
            Hero speaker,
            string playerText,
            bool approachOpening = false,
            Func<bool> requestOwner = null)
        {
            ReignEventReply reply = new ReignEventReply();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    reply.Error = "Local server is disabled.";
                    return reply;
                }

                if (session?.Record == null || speaker == null || string.IsNullOrWhiteSpace(speaker.StringId))
                {
                    reply.Error = "No social event speaker found.";
                    return reply;
                }

                // Native reads, the load fence and the owner share one game-thread capture.
                RequestFence fence = null;
                List<Hero> attendees = null;
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    EnsureRequestOwner(requestOwner, "social event response capture");
                    fence = RequestFence.Capture();
                    ReignHeroKnowledge.MarkMet(speaker);
                    attendees = session.Record.GetAttendees().ToList();
                    return BuildSocialEventPayload(session, speaker, playerText);
                }).ConfigureAwait(false);
                payload["correlationId"] = NewCorrelationId("social_event", speaker.StringId);
                payload["mode"] = "social_event";
                payload["turnType"] = approachOpening ? "npc_approach_opening" : "player_reply";
                payload["approachOpening"] = approachOpening;
                await EnsureRequestOwnerAsync(requestOwner, "social event context selection").ConfigureAwait(false);
                await AttachContextBundlesAsync(payload, "social_event", speaker, attendees).ConfigureAwait(false);
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await ReignWandererClient.PostWandererConversationAsync("/events/social/respond", payload, speaker, approachOpening ? string.Empty : playerText,
                    null, requestOwner).ConfigureAwait(false);
                timer.Stop();
                reply.Ok = response.Value<bool?>("ok") == true;
                reply.HeroStringId = speaker.StringId;
                reply.Text = response.Value<string>("reply") ?? string.Empty;
                reply.Participation = response.Value<string>("participation") ?? "speak";
                reply.ReactionTargetHeroStringId = response.Value<string>("reactionTargetHeroStringId") ?? string.Empty;
                reply.Emotion = response.Value<string>("emotion") ?? string.Empty;
                reply.Intent = response.Value<string>("intent") ?? string.Empty;
                reply.RelationshipSignal = response.Value<string>("relationshipSignal") ?? string.Empty;
                reply.RelationshipAssessments = response["relationshipAssessments"] as JArray ?? new JArray();
                reply.Error = response.Value<string>("error") ?? string.Empty;
                reply.EncyclopediaText = response.Value<string>("encyclopediaText") ?? string.Empty;
                reply.RawResponse = response;
                reply.ClientTotalMs = timer.ElapsedMilliseconds;
                reply.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                if (!reply.Ok && string.IsNullOrWhiteSpace(reply.Error))
                {
                    reply.Error = FriendlyConversationFailure(response, payload.Value<string>("correlationId"));
                }

                Func<bool> current = () => fence.IsCurrent() && RequestOwnerCurrent(requestOwner);
                await ReignMainThread.InvokeAsync(() => { if (current()) ApplyHeroEncyclopediaText(speaker, reply.EncyclopediaText); }).ConfigureAwait(false);
                if (reply.Ok)
                {
                    // The adjudication capture is owner-checked on the game thread.
                    await ReignCorrespondenceClient.ApplyConversationRelationshipAssessmentsAsync(
                        response["conversationExchange"]?["exchangeId"]?.Value<string>() ?? payload.Value<string>("sceneTurnId") ?? string.Empty,
                        approachOpening ? "social_event_npc_initiative" : "social_event_conversation",
                        attendees, new[] { response }, payload.Value<string>("correlationId") ?? string.Empty, current).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!current() || Hero.MainHero == null) return;
                        if (!approachOpening)
                        {
                            _ = ObservePlayerHistoricalClaimForListenersSafelyAsync(speaker, attendees, playerText, "social_event", response, current);
                        }

                        if (!IsDelimitedSpeechOnlyResponse(response) && string.IsNullOrWhiteSpace(response.Value<string>("privateAudienceHeroStringId")))
                            _ = ObserveNpcHistoricalClaimForListenersSafelyAsync(speaker, attendees, reply.Text, "social_event");
                    }).ConfigureAwait(false);
                }

                return reply;
            }
            catch (ReignRequestOwnerAbandonedException ex)
            {
                reply.Error = ex.Message;
                ReignLog.Info("Social event response abandoned by its local owner: " + ex.Message);
                return reply;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                ReignLog.Warn("Social event response failed: " + ex.Message);
                return reply;
            }
        }

        public static async Task<JObject> RequestPartyChatTurnPlanAsync(ReignPartyChatSession session,
            List<Hero> presentHeroes, string playerText, bool opening = false, string actionRecipientId = "",
            Func<bool> requestOwner = null)
        {
            if (session == null || presentHeroes == null || presentHeroes.Count == 0)
                return new JObject { ["ok"] = false, ["error"] = "No group audience is available." };
            try
            {
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    EnsureRequestOwner(requestOwner, "group speaking plan capture");
                    return BuildPartyChatPayload(session, presentHeroes[0], presentHeroes, playerText);
                }).ConfigureAwait(false);
                payload["mode"] = "party_chat";
                payload["approachOpening"] = opening;
                payload["structuredActionRecipientId"] = actionRecipientId;
                payload["structuredActionProtocolVersion"] = Reign.Core.Contracts.Actions.ActionNegotiation.ProtocolVersion;
                // Freeze the full audience's response order before context selection.
                // Each NPC's contextual writer decides whether to speak or listen.
                return requestOwner == null ? await PostJsonAsync("/party-chat/turn-plan", payload).ConfigureAwait(false)
                    : await PostOwnedJsonAsync("/party-chat/turn-plan", payload, requestOwner).ConfigureAwait(false);
            }
            catch (ReignRequestOwnerAbandonedException ex)
            {
                ReignLog.Info("Group speaking plan abandoned by its local owner: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = "The conversation closed." };
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Group speaking plan failed: " + ex.GetType().Name);
                return new JObject { ["ok"] = false, ["error"] = "The group speaking plan is temporarily unavailable." };
            }
        }

        public static async Task<ReignPartyChatReply> RequestPartyChatResponseAsync(
            ReignPartyChatSession session,
            Hero speaker,
            List<Hero> activeHeroes,
            string playerText,
            string correlationId = "",
            string auditRunId = "",
            int auditSceneIndex = -1,
            int auditTurnIndex = -1,
            string castleDialoguePrompt = "",
            string castleRoomName = "",
            bool castleOpening = false,
            bool familyChambers = false,
            string childHeroIdsCsv = "",
            string familyScenePlanJson = "{}",
            string residentHomeVenue = "",
            JObject speakingFloor = null,
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null,
            Func<bool> requestOwner = null)
        {
            // The native display name is read in the guarded game-thread capture below.
            ReignPartyChatReply reply = new ReignPartyChatReply
            {
                SpeakerHeroStringId = speaker?.StringId ?? string.Empty
            };
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    reply.Error = "Local server is disabled.";
                    return reply;
                }

                if (session == null || speaker == null || string.IsNullOrWhiteSpace(speaker.StringId))
                {
                    reply.Error = "No party chat speaker found.";
                    return reply;
                }

                RequestFence fence = null;
                JObject visitorContext = null;
                string familyTimeline = "main";
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    // Owner, native capture and the load fence share one game-thread callback.
                    EnsureRequestOwner(requestOwner, "party chat request capture");
                    fence = RequestFence.Capture();
                    reply.SpeakerName = speaker.Name?.ToString() ?? string.Empty;
                    ReignHeroKnowledge.MarkMet(activeHeroes);
                    ReignHeroKnowledge.MarkMet(speaker);
                    visitorContext = ReignServices.Court?.BuildNobleVisitorConversationContext(speaker.StringId);
                    familyTimeline = ReignServices.WorldHistory?.TimelineId ?? "main";
                    return BuildPartyChatPayload(session, speaker, activeHeroes ?? new List<Hero>(), playerText);
                }).ConfigureAwait(false);
                if (speakingFloor != null)
                {
                    payload["structuredActionRecipientId"] = speakingFloor.Value<string>("structuredActionRecipientId") ?? "";
                    var order = (speakingFloor["responseOrder"] as JArray ?? new JArray()).Values<string>().ToList();
                    payload["partySpeakerIndex"] = order.FindIndex(id => string.Equals(id, speaker.StringId, StringComparison.OrdinalIgnoreCase));
                    payload["partySpeakerCount"] = order.Count;
                    payload["groupTurnPlan"] = new JObject {
                        ["responseOrder"] = new JArray(order),
                        ["addressedHeroStringIds"] = speakingFloor["addressedHeroStringIds"]?.DeepClone() ?? new JArray(),
                        ["policy"] = speakingFloor.Value<string>("policy") ?? ""
                    };
                }
                if (visitorContext?.Value<bool?>("enabled") == true) payload["nobleVisitorContext"] = visitorContext;
                payload["correlationId"] = string.IsNullOrWhiteSpace(correlationId)
                    ? NewCorrelationId("party_chat", speaker.StringId)
                    : correlationId;
                payload["mode"] = "party_chat";
                if (!string.IsNullOrWhiteSpace(castleDialoguePrompt))
                {
                    payload["mode"] = "castle_chat";
                    payload["sceneInterface"] = "castle_keep_location";
                    payload["castleDialoguePrompt"] = castleDialoguePrompt;
                    payload["castleRoomName"] = castleRoomName ?? string.Empty;
                    payload["castleOpening"] = castleOpening;
                    payload["playerText"] = castleOpening ? string.Empty : playerText ?? string.Empty;
                }
                if (familyChambers)
                {
                    payload["mode"] = "family_chambers";
                    payload["familyChambers"] = true;
                    payload["childParticipantIds"] = new JArray((childHeroIdsCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    payload["familyScenePlan"] = string.IsNullOrWhiteSpace(familyScenePlanJson) ? "{}" : familyScenePlanJson;
                    payload["timelineId"] = familyTimeline;
                }
                if (!string.IsNullOrWhiteSpace(residentHomeVenue))
                {
                    payload["sceneInterface"] = "settlement_home";
                    payload["sceneOpportunity"] = new JObject { ["private"] = true, ["exposure"] = 0.10d, ["witnessIds"] = new JArray() };
                    foreach (JObject participant in ((JArray)payload["sceneParticipants"]).OfType<JObject>())
                    {
                        participant["nativeLocationDescription"] = residentHomeVenue;
                        participant["nativeLocationClass"] = "private";
                    }
                }
                if (!string.IsNullOrWhiteSpace(auditRunId))
                {
                    payload["auditRunId"] = auditRunId;
                    payload["auditSceneIndex"] = auditSceneIndex;
                    payload["auditTurnIndex"] = auditTurnIndex;
                }
                await EnsureRequestOwnerAsync(requestOwner, "party chat context selection").ConfigureAwait(false);
                await AttachContextBundlesAsync(payload, "party_chat", speaker, activeHeroes ?? new List<Hero>()).ConfigureAwait(false);
                PartyAgencySendOrigin.Value?.Invoke(payload);
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await ReignWandererClient.PostWandererConversationAsync("/party-chat/respond", payload, speaker, castleOpening ? string.Empty : playerText, actionDraft,
                    requestOwner).ConfigureAwait(false);
                timer.Stop();
                reply.Ok = response.Value<bool?>("ok") == true;
                reply.Text = response.Value<string>("reply") ?? string.Empty;
                reply.Emotion = response.Value<string>("emotion") ?? string.Empty;
                reply.Intent = response.Value<string>("intent") ?? string.Empty;
                reply.RelationshipSignal = response.Value<string>("relationshipSignal") ?? string.Empty;
                reply.RelationshipAssessments = response["relationshipAssessments"] as JArray ?? new JArray();
                reply.RawResponse = response;
                reply.Participation = response.Value<string>("participation") ?? "speak";
                reply.ReactionTargetHeroStringId = response.Value<string>("reactionTargetHeroStringId") ?? string.Empty;
                reply.Error = response.Value<string>("error") ?? string.Empty;
                reply.PrivateAudienceHeroStringId = response.Value<string>("privateAudienceHeroStringId") ?? string.Empty;
                reply.EncyclopediaText = response.Value<string>("encyclopediaText") ?? string.Empty;
                reply.CorrelationId = payload.Value<string>("correlationId") ?? string.Empty;
                JObject exchange = response["conversationExchange"] as JObject;
                reply.ConversationSessionId = exchange?.Value<string>("sessionId") ?? session.SessionId ?? string.Empty;
                reply.ExchangeId = exchange?.Value<string>("exchangeId") ?? session.CurrentSceneTurnId ?? string.Empty;
                reply.SelectedContextPulls = response["selectedContextPulls"] as JArray ?? new JArray();
                reply.QueuedActions = response["queuedActions"] as JArray ?? new JArray();
                reply.ClientTotalMs = timer.ElapsedMilliseconds;
                reply.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                ReignLog.Info("HTTP /party-chat/respond hero=" + speaker.StringId
                    + " clientMs=" + reply.ClientTotalMs
                    + " ok=" + reply.Ok
                    + " timing=" + reply.TimingSummary);
                if (!reply.Ok && string.IsNullOrWhiteSpace(reply.Error))
                {
                    reply.Error = FriendlyConversationFailure(response, reply.CorrelationId);
                }

                // Native hero text and the observers' synchronous native reads start only
                // in a game-thread callback for the original load and a current owner.
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!fence.IsCurrent() || !RequestOwnerCurrent(requestOwner)) return;
                    ApplyHeroEncyclopediaText(speaker, reply.EncyclopediaText);
                    if (reply.Ok && Hero.MainHero != null)
                    {
                        if (!familyChambers && !IsDelimitedSpeechOnlyResponse(response) && string.IsNullOrWhiteSpace(reply.PrivateAudienceHeroStringId))
                            _ = ObserveNpcHistoricalClaimForListenersSafelyAsync(speaker, activeHeroes, reply.Text, "party_chat");
                        // Family history stays confined to the responding speaker.
                        _ = ObservePlayerHistoricalClaimForListenersSafelyAsync(speaker,
                            familyChambers ? Enumerable.Empty<Hero>() : activeHeroes, playerText, "party_chat", response,
                            () => fence.IsCurrent() && RequestOwnerCurrent(requestOwner));
                    }
                }).ConfigureAwait(false);

                return reply;
            }
            catch (ReignRequestOwnerAbandonedException ex)
            {
                reply.Error = ex.Message;
                ReignLog.Info("Party chat response abandoned by its local owner: " + ex.Message);
                return reply;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                ReignLog.Warn("Party chat response failed: " + ex.Message);
                return reply;
            }
        }

        public static async Task<ReignGeneratedWildernessScenario> GenerateWildernessScenarioAsync(ReignGeneratedWildernessRequest request)
        {
            ReignGeneratedWildernessScenario result = new ReignGeneratedWildernessScenario();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local server is disabled.";
                    return result;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                    ["terrainKey"] = request?.TerrainKey ?? "plain",
                    ["locationText"] = request?.LocationText ?? "on the road",
                    ["timeOfDayText"] = request?.TimeOfDayText ?? "unknown time of day",
                    ["externalHeroId"] = request?.ExternalHeroId ?? string.Empty,
                    ["externalContext"] = request?.ExternalContext ?? string.Empty
                };
                payload["recentEvents"] = JArray.FromObject(request?.RecentEvents ?? new List<ReignRecentWildernessEvent>());

                JArray participants = new JArray();
                foreach (ReignGeneratedWildernessParticipant participant in request?.Participants ?? new List<ReignGeneratedWildernessParticipant>())
                {
                    if (participant?.Hero == null || string.IsNullOrWhiteSpace(participant.Hero.StringId))
                    {
                        continue;
                    }

                    JObject profile = BuildHeroProfile(participant.Hero);
                    profile["generatedRole"] = participant.Role ?? string.Empty;
                    profile["isOutsideNpc"] = participant.IsOutsideNpc;
                    participants.Add(profile);
                }

                payload["participants"] = participants;
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/generated-wilderness/plan", payload).ConfigureAwait(false);
                timer.Stop();

                result.Ok = response.Value<bool?>("ok") == true;
                result.Error = response.Value<string>("error") ?? string.Empty;
                result.Title = response.Value<string>("title") ?? string.Empty;
                result.VarietyKey = response.Value<string>("varietyKey") ?? string.Empty;
                result.InitiatorHeroStringId = response.Value<string>("initiatorHeroStringId") ?? string.Empty;
                result.ApproachDescription = response.Value<string>("approachDescription") ?? response.Value<string>("approach_description") ?? string.Empty;
                result.OpeningText = response.Value<string>("openingText") ?? response.Value<string>("opening_text") ?? string.Empty;
                result.PlayerHook = response.Value<string>("playerHook") ?? response.Value<string>("player_hook") ?? string.Empty;
                result.ExternalHeroId = response.Value<string>("externalHeroId") ?? response.Value<string>("external_hero_id") ?? string.Empty;
                result.ExternalContext = response.Value<string>("externalContext") ?? response.Value<string>("external_context") ?? string.Empty;
                result.TerrainKey = response.Value<string>("terrainKey") ?? response.Value<string>("terrain_key") ?? string.Empty;
                result.EmotionalPressure = response.Value<string>("emotionalPressure") ?? response.Value<string>("emotional_pressure") ?? string.Empty;
                result.SurfaceClues = response.Value<string>("surfaceClues") ?? response.Value<string>("surface_clues") ?? string.Empty;
                result.HiddenContext = response.Value<string>("hiddenContext") ?? response.Value<string>("hidden_context") ?? string.Empty;
                result.DiscoveryRoutes = response.Value<string>("discoveryRoutes") ?? response.Value<string>("discovery_routes") ?? string.Empty;
                result.ParticipantHeroIds = ReadStringArray(response["participantHeroIds"] ?? response["participant_ids"]);
                result.ClientTotalMs = timer.ElapsedMilliseconds;
                result.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                ReignLog.Info("HTTP /events/generated-wilderness/plan ok=" + result.Ok
                    + " participants=" + participants.Count
                    + " clientMs=" + result.ClientTotalMs
                    + " timing=" + result.TimingSummary);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Generated wilderness scenario failed: " + ex.Message);
                return result;
            }
        }

        public static async Task<List<ReignDialogueLine>> GetDialogueHistoryAsync(Hero hero, int limit = 80, JObject conversationContext = null,
            Func<bool> requestOwner = null)
        {
            List<ReignDialogueLine> lines = new List<ReignDialogueLine>();
            try
            {
                // The native payload and the original owner check share one
                // game-thread callback; inline for callers already on that thread.
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    EnsureRequestOwner(requestOwner, "dialogue history capture");
                    if (hero == null || string.IsNullOrWhiteSpace(hero.StringId)) return null;
                    JObject captured = new JObject
                    {
                        ["campaignId"] = GetCampaignId(),
                        ["correlationId"] = NewCorrelationId("dialogue", hero.StringId),
                        ["mode"] = "dialogue",
                        ["heroStringId"] = hero.StringId,
                        ["playerHeroStringId"] = Hero.MainHero?.StringId ?? "",
                        ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                        ["limit"] = limit
                    };
                    if (conversationContext != null)
                    {
                        captured["conversationMode"] = conversationContext.Value<string>("conversationMode") ?? string.Empty;
                        captured["postingId"] = conversationContext.Value<string>("postingId") ?? string.Empty;
                        captured["representedKingdomId"] = conversationContext.Value<string>("representedKingdomId") ?? string.Empty;
                    }
                    return captured;
                }).ConfigureAwait(false);
                if (payload == null)
                {
                    return lines;
                }

                string heroId = payload.Value<string>("heroStringId");
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = requestOwner == null
                    ? await PostJsonAsync("/dialogue/history", payload).ConfigureAwait(false)
                    : await PostOwnedJsonAsync("/dialogue/history", payload, requestOwner).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /dialogue/history hero=" + heroId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                JArray array = response.Value<bool?>("ok") == true ? response["lines"] as JArray ?? new JArray() : new JArray();

                ReignConversationInputJournal.ReconcileAcceptedHistory(array, payload.Value<string>("campaignId"),
                    payload.Value<string>("timelineId"), payload.Value<string>("playerHeroStringId"), heroId);
                foreach (JToken token in array)
                {
                    JObject obj = token as JObject;
                    if (obj == null)
                    {
                        continue;
                    }
                    // Submitted originals and local drafts are recoverable input,
                    // not accepted player turns paired with a later NPC opener.
                    if (obj.Value<string>("deliveryStatus") == "submitted") continue;

                    lines.Add(new ReignDialogueLine(
                        obj.Value<string>("speaker") ?? string.Empty,
                        obj.Value<string>("text") ?? string.Empty,
                        obj.Value<string>("role") ?? string.Empty,
                        obj.Value<string>("channel") ?? "in_person",
                        obj.Value<string>("letterId") ?? string.Empty,
                        obj.Value<string>("threadId") ?? string.Empty,
                        obj.Value<string>("direction") ?? string.Empty,
                        obj.Value<double?>("worldDay") ?? 0d) { ExchangeId = obj.Value<string>("exchangeId") ?? "" });
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Dialogue history load failed: " + ex.Message);
            }

            return lines;
        }

        public static async Task<string> StartConversationSessionAsync(Hero hero, JObject conversationContext = null)
        {
            var result = await StartConversationSessionWithIdentityAsync(hero, conversationContext).ConfigureAwait(false);
            return result.Ok ? result.SessionId : string.Empty;
        }

        // loadCurrent (optional) is a Bannerlord-thread load fence. When supplied,
        // the native start payload is captured inside the same game-thread callback
        // that checks it; a changed load starts nothing.
        public static async Task<Reign.Core.Contracts.Dialogue.ConversationSessionStart> StartConversationSessionWithIdentityAsync(Hero hero, JObject conversationContext = null,
            Func<bool> loadCurrent = null)
        {
            var result = new Reign.Core.Contracts.Dialogue.ConversationSessionStart();
            try
            {
                JObject payload;
                if (loadCurrent == null) payload = BuildConversationStartPayload(hero, conversationContext);
                else
                {
                    bool stale = false;
                    payload = await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!loadCurrent()) { stale = true; return null; }
                        return BuildConversationStartPayload(hero, conversationContext);
                    }).ConfigureAwait(false);
                    if (stale)
                    {
                        result.Error = "The campaign changed before the conversation session could start.";
                        return result;
                    }
                }
                if (payload == null) return result;
                // The same load fence guards dispatch and its pregnancy subrequests.
                JObject response = loadCurrent == null
                    ? await PostJsonAsync("/memory/conversation/start", payload).ConfigureAwait(false)
                    : await PostOwnedJsonAsync("/memory/conversation/start", payload, loadCurrent).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.SessionId = response.Value<string>("sessionId") ?? string.Empty;
                result.LogicalConversationId = response.Value<string>("logicalConversationId") ?? string.Empty;
                result.Status = response.Value<string>("status") ?? "open";
                result.Error = response.Value<string>("error") ?? string.Empty;
            }
            catch (ReignRequestOwnerAbandonedException)
            {
                result.Error = "The campaign changed before the conversation session could start.";
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Conversation session start failed: " + ex.Message);
                result.Error = ex.Message;
            }
            return result;
        }

        private static JObject BuildConversationStartPayload(Hero hero, JObject conversationContext)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
            {
                return null;
            }
            bool official = IsOfficialAmbassadorContext(conversationContext);
            string representedRulerId = conversationContext?.Value<string>("representedRulerId") ?? string.Empty;
            JObject payload = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["npcId"] = hero.StringId,
                ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["channel"] = official ? "ambassador_official" : "in_person",
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["locationId"] = ResolveDialogueLocationId(hero),
                ["participants"] = new JArray(new[] { hero.StringId, Hero.MainHero?.StringId ?? string.Empty, official ? representedRulerId : string.Empty }.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
            };
            if (official) payload["ambassadorContext"] = conversationContext.DeepClone();
            foreach (string key in new[] { "sessionId", "logicalConversationId", "continuedFromSessionId", "continuationReason" })
                if (conversationContext?[key] != null) payload[key] = conversationContext[key].DeepClone();
            foreach (string key in new[] { "campaignId", "timelineId", "playerId" })
                if (!string.IsNullOrWhiteSpace(conversationContext?.Value<string>(key))) payload[key] = conversationContext[key].DeepClone();
            return payload;
        }

        // loadCurrent (optional) is a Bannerlord-thread load fence. When supplied,
        // the native finish payload and the communication close-work apply each
        // run inside a game-thread callback that checks it first.
        public static async Task<ReignConversationFinishResult> FinishConversationSessionAsync(Hero hero, string sessionId, string reason = "closed_by_game", JObject conversationContext = null,
            Func<bool> loadCurrent = null)
        {
            ReignConversationFinishResult result = new ReignConversationFinishResult { SessionId = sessionId ?? string.Empty };
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    result.Error = "Session id is empty.";
                    return result;
                }
                JObject payload = loadCurrent == null ? BuildConversationFinishPayload(hero, sessionId, reason, conversationContext)
                    : await ReignMainThread.InvokeAsync(() => loadCurrent()
                        ? BuildConversationFinishPayload(hero, sessionId, reason, conversationContext) : null).ConfigureAwait(false);
                if (payload == null)
                {
                    result.Error = "The campaign changed before the conversation session could finish.";
                    return result;
                }
                JObject response = loadCurrent == null
                    ? await PostJsonAsync("/memory/conversation/finish", payload).ConfigureAwait(false)
                    : await PostOwnedJsonAsync("/memory/conversation/finish", payload, loadCurrent).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.SceneSummaryId = response.Value<string>("sceneSummaryId") ?? string.Empty;
                result.Raw = response;
                result.Error = response.Value<string>("error") ?? string.Empty;
                ReignLog.Info("Conversation session finish id=" + sessionId + " ok=" + result.Ok);
                if (result.Ok) await ReignMainThread.InvokeAsync(() =>
                {
                    if (loadCurrent != null && !loadCurrent())
                    {
                        ReignLog.Warn("Conversation close work skipped: the campaign changed session=" + sessionId);
                        return;
                    }
                    ReignCommunicationClient.ApplyCommunicationCloseWork(response);
                }).ConfigureAwait(false);
            }
            catch (ReignRequestOwnerAbandonedException)
            {
                result.Error = "The campaign changed before the conversation session could finish.";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Conversation session finish failed: " + ex.Message);
            }
            return result;
        }

        private static JObject BuildConversationFinishPayload(Hero hero, string sessionId, string reason, JObject conversationContext)
        {
            JObject payload = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["sessionId"] = sessionId,
                ["npcId"] = hero?.StringId ?? string.Empty,
                ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["locationId"] = ResolveDialogueLocationId(hero),
                ["reason"] = reason ?? "closed_by_game"
            };
            if (IsOfficialAmbassadorContext(conversationContext))
                payload["ambassadorContext"] = conversationContext.DeepClone();
            foreach (string key in new[] { "campaignId", "timelineId", "playerId" })
                if (!string.IsNullOrWhiteSpace(conversationContext?.Value<string>(key))) payload[key] = conversationContext[key].DeepClone();
            return payload;
        }

        public static async Task<string> StartPartyChatConversationSessionAsync(ReignPartyChatSession session, IEnumerable<Hero> activeHeroes)
        {
            try
            {
                List<Hero> heroes = (activeHeroes ?? Enumerable.Empty<Hero>())
                    .Where(hero => hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                    .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                Hero anchor = heroes.FirstOrDefault();
                if (session == null || anchor == null || string.IsNullOrWhiteSpace(session.SessionId))
                {
                    return string.Empty;
                }

                Settlement conversationSettlement = Settlement.CurrentSettlement
                    ?? MobileParty.MainParty?.CurrentSettlement
                    ?? anchor.CurrentSettlement
                    ?? anchor.PartyBelongedTo?.CurrentSettlement;
                JObject payload = ReignPartyChatSessionRequest.Create(GetCampaignId(),
                    ReignServices.WorldHistory?.TimelineId ?? "main", session.SessionId,
                    anchor.StringId, Hero.MainHero?.StringId ?? string.Empty, heroes.Select(hero => hero.StringId));
                payload["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays;
                payload["locationId"] = ResolveDialogueLocationId(anchor);
                payload["locationName"] = conversationSettlement?.Name?.ToString() ?? string.Empty;
                payload["sceneContext"] = session.BuildSceneContext(heroes);
                JObject response = await PostJsonAsync("/memory/conversation/start", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true
                    ? response.Value<string>("sessionId") ?? string.Empty
                    : string.Empty;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Party chat conversation session start failed: " + ex.Message);
                return string.Empty;
            }
        }

        public static async Task<ReignConversationFinishResult> FinishPartyChatConversationSessionAsync(
            ReignPartyChatSession session,
            IEnumerable<Hero> activeHeroes,
            string reason = "closed_by_player",
            Func<bool> loadOwner = null)
        {
            string sessionId = session?.SessionId ?? string.Empty;
            ReignConversationFinishResult result = new ReignConversationFinishResult { SessionId = sessionId };
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    result.Error = "Session id is empty.";
                    return result;
                }

                List<Hero> heroes = (activeHeroes ?? Enumerable.Empty<Hero>())
                    .Where(hero => hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                    .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                Hero anchor = heroes.FirstOrDefault();
                JObject response = await PostJsonAsync("/memory/conversation/finish", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["sessionId"] = sessionId,
                    ["npcId"] = anchor?.StringId ?? string.Empty,
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["locationId"] = ResolveDialogueLocationId(anchor),
                    ["reason"] = reason ?? "closed_by_player"
                }).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.SceneSummaryId = response.Value<string>("sceneSummaryId") ?? string.Empty;
                result.Raw = response;
                result.Error = response.Value<string>("error") ?? string.Empty;
                ReignLog.Info("Party chat conversation session finish id=" + sessionId + " ok=" + result.Ok);
                if (result.Ok) await ReignMainThread.InvokeAsync(() => { if (RequestOwnerCurrent(loadOwner)) ReignCommunicationClient.ApplyCommunicationCloseWork(response); }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Party chat conversation session finish failed: " + ex.Message);
            }
            return result;
        }

        public static async Task<bool> RecoverConversationSessionsAsync()
        {
            try
            {
                JObject response = await PostJsonAsync("/memory/conversation/recover", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays
                }).ConfigureAwait(false);
                ReignLog.Info("Conversation recovery ok=" + (response.Value<bool?>("ok") == true) + " count=" + (response.Value<int?>("recoveredCount") ?? 0));
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Conversation recovery failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<ReignDialogueReply> RequestDialogueResponseAsync(
            Hero hero,
            string playerText,
            string conversationSessionId = "",
            string playerPromptLabel = "",
            string correlationId = "",
            string dialogueAuditRunId = "",
            int dialogueAuditSceneIndex = -1,
            int dialogueAuditTurnIndex = -1,
            bool applyNativeEncyclopediaText = true,
            JObject conversationContext = null,
            bool guardedPromptOverride = false,
            string guardedPromptOverrideDirective = "",
            string guardedPromptOverrideOwnerCommandId = "",
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null,
            string retryOfCorrelationId = "", string retrySceneTurnId = "",
            Func<bool> requestOwner = null, bool requestViewInsertionReceipt = false)
        {
            ReignDialogueReply reply = new ReignDialogueReply();
            RequestFence fence = null;
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    reply.Error = "Local server is disabled.";
                    return reply;
                }

                if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                {
                    reply.Error = "No conversation hero found.";
                    return reply;
                }

                // Hero, clan, party, settlement, equipment, and campaign collections are
                // native Bannerlord state. Live-test continuations run on a worker after
                // their first await, so reading that graph directly here can deadlock the
                // campaign thread for a particular hero. Capture one immutable request
                // snapshot on the game thread, then do all network work from the worker.
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    EnsureRequestOwner(requestOwner, "dialogue request capture");
                    fence = RequestFence.Capture();
                    ReignHeroKnowledge.MarkMet(hero);
                    return BuildDialogueRequestPayload(
                        hero,
                        playerText,
                        conversationSessionId,
                        playerPromptLabel,
                        settings?.McmTestModeEnabled ?? false,
                        conversationContext);
                }).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(correlationId)) { payload["correlationId"] = correlationId; payload["contributionId"] = conversationContext?.Value<string>("contributionId") ?? correlationId; }
                if (!string.IsNullOrWhiteSpace(retryOfCorrelationId))
                {
                    payload["retryOfCorrelationId"] = retryOfCorrelationId;
                    if (!string.IsNullOrWhiteSpace(retrySceneTurnId)) payload["sceneTurnId"] = retrySceneTurnId;
                }
                reply.RequestSceneTurnId = payload.Value<string>("sceneTurnId") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(dialogueAuditRunId))
                {
                    payload["dialogueAuditRunId"] = dialogueAuditRunId;
                    payload["dialogueAuditSceneIndex"] = dialogueAuditSceneIndex;
                    payload["dialogueAuditTurnIndex"] = dialogueAuditTurnIndex;
                }
                if (guardedPromptOverride)
                {
                    payload["guardedPromptOverride"] = true;
                    payload["guardedPromptOverrideDirective"] =
                        (guardedPromptOverrideDirective ?? string.Empty).Trim();
                    payload["guardedPromptOverrideOwnerCommandId"] =
                        (guardedPromptOverrideOwnerCommandId ?? string.Empty).Trim();
                }
                string conceptionAttemptId = "player_intimacy_" + Guid.NewGuid().ToString("N");
                bool officialAmbassador = IsOfficialAmbassadorContext(conversationContext);
                // Only the ordinary Individual Chat view opts in; retries resend the same field.
                if (requestViewInsertionReceipt && !officialAmbassador) payload[IndividualViewInsertionOptInField] = IndividualViewInsertionSchema;
                bool suppressRelationshipAssessment =
                    conversationContext?.Value<bool?>("suppressRelationshipAssessment") == true;
                if (!officialAmbassador)
                {
                    await EnsureRequestOwnerAsync(requestOwner, "context bundle selection").ConfigureAwait(false);
                    await AttachContextBundlesAsync(payload, "dialogue", hero, new List<Hero> { hero }).ConfigureAwait(false);
                }
                PartyAgencySendOrigin.Value?.Invoke(payload);
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await ReignWandererClient.PostWandererConversationAsync("/dialogue/respond", payload, hero, playerText, actionDraft, requestOwner).ConfigureAwait(false);
                timer.Stop();
                reply.Ok = response.Value<bool?>("ok") == true;
                reply.Text = response.Value<string>("reply") ?? string.Empty;
                reply.Emotion = response.Value<string>("emotion") ?? string.Empty;
                reply.Intent = response.Value<string>("intent") ?? string.Empty;
                reply.Error = response.Value<string>("error") ?? string.Empty;
                reply.EncyclopediaText = response.Value<string>("encyclopediaText") ?? string.Empty;
                reply.ActionShadowPreview = response.Value<string>("actionShadowPreview") ?? string.Empty;
                JObject conceptionGate = response["conceptionGate"] as JObject;
                reply.ConceptionGateNeeded = conceptionGate?.Value<bool?>("needed") == true
                    && conceptionGate?.Value<bool?>("completed") == true;
                // Native hero text, queued-action parsing (it reads CampaignTime)
                // and the player read share one game-thread callback, for the
                // original load and a still-current local owner. A stale owner
                // receives no locally parsed actions; the server outbox keeps them.
                bool playerPresent = false;
                await ReignMainThread.InvokeAsync(() =>
                {
                    bool current = fence.IsCurrent() && RequestOwnerCurrent(requestOwner);
                    if (current && applyNativeEncyclopediaText && !string.IsNullOrWhiteSpace(reply.EncyclopediaText))
                        ApplyHeroEncyclopediaText(hero, reply.EncyclopediaText);
                    // Dialogue actions were already imported once by the conversation wrapper
                    // under this same fence; only test directives remain for the view import.
                    // QueuedActions keeps the combined evidence (directives, then dialogue
                    // actions) that automation and audit assertions read; parsing never imports.
                    reply.TestDirectiveActions = current ? ParseQueuedActionRows(response["queuedTestDirectiveActions"], fence) : new List<ReignWorldActionRecord>();
                    reply.QueuedActions = new List<ReignWorldActionRecord>(reply.TestDirectiveActions);
                    if (current) reply.QueuedActions.AddRange(ParseQueuedActionRows(response["queuedDialogueActions"], fence));
                    playerPresent = current && Hero.MainHero != null;
                }).ConfigureAwait(false);
                reply.ClientTotalMs = timer.ElapsedMilliseconds;
                reply.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                reply.CorrelationId = response.Value<string>("correlationId") ?? correlationId ?? string.Empty;
                reply.SelectedContextPulls = response["selectedContextPulls"] as JArray ?? new JArray();
                reply.ContextBundles = response["contextBundles"] as JArray ?? new JArray();
                reply.MemoryWrites = response["memoryWrites"] as JArray ?? new JArray();
                reply.ChancellorDecision = payload.Value<int?>("structuredActionProtocolVersion") == 1 ? new JObject() : response["chancellorDecision"] as JObject ?? new JObject();
                reply.RawResponse = response;
                if (reply.Ok && payload.Value<int?>("structuredActionProtocolVersion") != 1 && payload["governmentPrivateContext"] is JObject governmentPrivateContext)
                    response["governmentCommitmentReceipt"] = await ReignMainThread.InvokeAsync(() =>
                        !fence.IsCurrent() || !RequestOwnerCurrent(requestOwner) ? new JObject()
                        : ReignServices.Government
                            ?.ApplyGovernmentPrivateConversationResult(governmentPrivateContext, response)
                        ?? new JObject()).ConfigureAwait(false);
                JObject exchange = response["conversationExchange"] as JObject;
                reply.ConversationSessionId = exchange?.Value<string>("sessionId") ?? conversationSessionId ?? string.Empty;
                reply.ExchangeId = exchange?.Value<string>("exchangeId") ?? string.Empty;
                reply.TurnIds = exchange?["turnIds"] as JArray ?? new JArray();
                ReignLog.Info("HTTP /dialogue/respond hero=" + (payload.Value<string>("heroStringId") ?? string.Empty)
                    + " clientMs=" + reply.ClientTotalMs
                    + " ok=" + reply.Ok
                    + " timing=" + reply.TimingSummary);
                if (!reply.Ok && string.IsNullOrWhiteSpace(reply.Error))
                {
                    reply.Error = FriendlyConversationFailure(response, reply.CorrelationId);
                }

                if (reply.Ok && playerPresent && !officialAmbassador
                    && !suppressRelationshipAssessment)
                {
                    await ReignCorrespondenceClient.ApplyConversationRelationshipAssessmentsAsync(
                        reply.ExchangeId, "in_person_conversation", new[] { hero }, new[] { response }, reply.CorrelationId,
                        requestOwner).ConfigureAwait(false);
                    if (reply.ConceptionGateNeeded && string.IsNullOrWhiteSpace(playerPromptLabel))
                    {
                        // The preview's synchronous prefix reads native heroes and
                        // builds its payload, so it starts inside this game-thread
                        // callback after the original owner check. A stale view
                        // never opens a pregnancy decision preview.
                        Task<ReignConceptionAttemptResult> previewTask = await ReignMainThread.InvokeAsync(() =>
                            fence.IsCurrent() && RequestOwnerCurrent(requestOwner)
                                ? ReignRelationshipDirectorClient.PreviewPlayerConceptionAttemptAsync(hero, reply.Text, conceptionAttemptId)
                                : null).ConfigureAwait(false);
                        ReignConceptionAttemptResult preview = previewTask == null ? null : await previewTask.ConfigureAwait(false);
                        if (preview != null && preview.Ok && preview.Verified && preview.Eligible)
                        {
                            reply.ConceptionAttempt = preview;
                        }
                        else
                        {
                            reply.ConceptionGateNeeded = false;
                        }
                    }
                }

                return reply;
            }
            catch (ReignRequestOwnerAbandonedException ex)
            {
                reply.Ok = false;
                reply.Error = ex.Message;
                ReignLog.Info("Dialogue response abandoned by its local owner: " + ex.Message);
                return reply;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                ReignLog.Exception("Dialogue response failed", ex);
                return reply;
            }
        }

        internal static JObject BuildDialogueRequestPayload(
            Hero hero,
            string playerText,
            string conversationSessionId,
            string playerPromptLabel,
            bool mcmTestMode,
            JObject conversationContext = null)
        {
            List<Hero> participants = new List<Hero> { hero };
            bool official = IsOfficialAmbassadorContext(conversationContext);
            string requestedMode = conversationContext?.Value<string>("conversationMode")
                ?? string.Empty;
            bool rulerPetition = string.Equals(requestedMode, "ruler_petition",
                StringComparison.OrdinalIgnoreCase);
            bool nobleDocket = string.Equals(requestedMode, "noble_docket",
                StringComparison.OrdinalIgnoreCase);
            bool courtLife = string.Equals(requestedMode, "court_life", StringComparison.OrdinalIgnoreCase);
            string sceneContext = BuildSceneContext(hero);
            string sceneContextDirective = conversationContext
                ?.Value<string>("sceneContextDirective") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sceneContextDirective)
                && (conversationContext?["rescueContext"] == null || conversationContext.Value<bool?>("rescueOpening") == true))
                sceneContext = sceneContext + "\n\n" + sceneContextDirective.Trim();
            JObject payload = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["hero"] = BuildHeroProfile(hero),
                ["mcmTestMode"] = mcmTestMode,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerIdentity"] = BuildPlayerIdentityContext(),
                ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["playerKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["speakerClanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["speakerKingdomId"] = hero?.Clan?.Kingdom?.StringId ?? hero?.MapFaction?.StringId ?? string.Empty,
                ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["playerText"] = playerText ?? string.Empty,
                ["playerPromptLabel"] = playerPromptLabel ?? string.Empty,
                ["suppressConceptionGate"] = official || rulerPetition || nobleDocket || courtLife
                    || string.Equals(playerPromptLabel, "pull_out", StringComparison.OrdinalIgnoreCase),
                ["conversationSessionId"] = conversationSessionId ?? string.Empty,
                ["sceneTurnId"] = string.IsNullOrWhiteSpace(conversationSessionId)
                    ? "individual_" + (hero?.StringId ?? "unknown") + "_" + Guid.NewGuid().ToString("N")
                    : conversationSessionId + "_" + Guid.NewGuid().ToString("N"),
                ["sceneParticipants"] = BuildSceneParticipants(participants),
                ["localPresence"] = BuildLocalPresenceSnapshot(participants),
                ["playerAttentionPresence"] = ReignPlayerApproachClient.BuildPlayerAttentionPresence(hero),
                ["gossipPresence"] = new JObject(), // Materialized once after readiness, before sending.
                ["channel"] = official ? "ambassador_official"
                    : rulerPetition ? "ruler_petition"
                    : nobleDocket ? "noble_docket" : courtLife ? "court_life" : "in_person",
                ["conversationMode"] = official ? "ambassador_official"
                    : rulerPetition ? "ruler_petition"
                    : nobleDocket ? "noble_docket" : courtLife ? "court_life" : "in_person",
                ["officialMemoryFirewall"] = official,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["locationId"] = ResolveDialogueLocationId(hero),
                ["sceneContext"] = sceneContext,
                ["nativePoliticalContext"] = BuildNativePoliticalContext(hero),
                ["clanAccords"] = ReignServices.ClanAccords?.ConversationContext(hero) ?? new JObject(),
                ["arrestContext"] = ReignServices.Arrest
                    ?.BuildConversationContext(hero) ?? new JObject(),
                ["temporaryPartyGuest"] = ReignServices.TemporaryPartyGuest
                    ?.BuildConversationContext(hero) ?? new JObject(),
                ["nativeSceneMovement"] = ReignSceneActionMissionBehavior.Context(),
                ["playerPartyId"] = MobileParty.MainParty?.StringId ?? string.Empty,
                ["campaignCalendar"] = new JObject { ["daysPerSeason"] = ReignCalendarService.DaysPerSeason,
                    ["daysPerWeek"] = CampaignTime.DaysInWeek, ["daysPerYear"] = ReignCalendarService.DaysPerYear },
                ["actionResolutionIndex"] = rulerPetition || courtLife
                    ? new JObject()
                    : BuildActionResolutionIndex(hero, participants,
                        RequiresFullActionResolutionIndex(playerText))
            };
            JObject chancellorContext = ReignServices.Court
                ?.BuildChancellorConversationContext(hero) ?? new JObject();
            if (chancellorContext.Value<bool?>("enabled") == true)
                payload["chancellorOfficeContext"] = chancellorContext;
            if (official) payload["ambassadorContext"] = conversationContext.DeepClone();
            JObject visitorContext = ReignServices.Court?.BuildNobleVisitorConversationContext(hero?.StringId ?? "");
            if (visitorContext?.Value<bool?>("enabled") == true) payload["nobleVisitorContext"] = visitorContext;
            if (!official && !rulerPetition && !nobleDocket && !courtLife)
            {
                JObject governmentContext = ReignServices.Government
                    ?.BuildGovernmentPrivateConversationContext(hero, payload.Value<string>("sceneTurnId"), playerText ?? "");
                if (governmentContext?.Value<bool?>("enabled") == true)
                    payload["governmentPrivateContext"] = governmentContext;
            }
            if (courtLife)
            {
                payload["courtLifeContext"] = conversationContext.DeepClone();
                payload["courtLifePhase"] = conversationContext.Value<string>("phase") ?? "";
                foreach (string key in new[] { "phase", "actualPlayerTurn", "matterId", "templateId", "source", "sceneTurnId" })
                    if (conversationContext[key] != null) payload[key] = conversationContext[key].DeepClone();
            }
            if (rulerPetition)
                payload["rulerPetitionContext"] = conversationContext.DeepClone();
            if (nobleDocket)
            {
                payload["nobleDocketContext"] = conversationContext.DeepClone();
                foreach (string property in new[]
                         {
                             "matterId", "templateId", "phase", "activeSpeakerHeroId",
                             "speakerRole", "activeParticipants", "revealedEvidence",
                             "discoverableEvidence", "audienceTranscript"
                         })
                {
                    JToken value = conversationContext[property];
                    if (value != null) payload[property] = value.DeepClone();
                }
            }
            if (conversationContext != null)
                foreach (string key in new[] { "approachRequestId", "approachOpening", "rescueOpening", "rescueContext", "npcStoryId", "npcLifeRequestId", "npcLivesRevision" })
                    if (conversationContext[key] != null) payload[key] = conversationContext[key].DeepClone();
            return payload;
        }

        private static bool IsOfficialAmbassadorContext(JObject conversationContext)
        {
            return string.Equals(conversationContext?.Value<string>("conversationMode"), "ambassador_official", StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<JObject> EvaluateHistoricalClaimBetweenHeroesAsync(Hero claimant, Hero target, string claim, string mode)
        {
            if (claimant == null || target == null || !LooksLikeHistoricalWorldClaim(claim))
            {
                return new JObject { ["ok"] = true, ["outcome"] = "not_checkable" };
            }
            float worldDay = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
            return await ReignWorldHistoryClient.CheckWorldHistoryLieAsync(claim, claimant, target, worldDay,
                ReignServices.WorldHistory?.TimelineId ?? "main", mode).ConfigureAwait(false);
        }

        private static async Task ObserveNpcHistoricalClaimForListenersSafelyAsync(Hero claimant, IEnumerable<Hero> listeners, string claim, string mode)
        {
            try
            {
                if (claimant == null || !LooksLikeHistoricalWorldClaim(claim)) return;
                List<Hero> targets = (listeners ?? Enumerable.Empty<Hero>()).Where(x => x != null && x != claimant && x != Hero.MainHero)
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).Take(12).ToList();
                foreach (Hero target in targets)
                {
                    await EvaluateHistoricalClaimBetweenHeroesAsync(claimant, target, claim, mode + "_npc_to_npc").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("NPC-to-NPC historical claim observation failed: " + ex.Message);
            }
        }

        internal static async Task ObservePlayerHistoricalClaimForListenersSafelyAsync(Hero respondingTarget,
            IEnumerable<Hero> listeners, string claim, string mode, JObject response, Func<bool> requestOwner)
        {
            try
            {
                Hero player = Hero.MainHero;
                var accepted = response?["conversationExchange"]?["acceptedUnit"] as JObject;
                string unitId = accepted?.Value<string>("unitId") ?? string.Empty;
                if (player == null || accepted?["groupHistoryObservation"] is not JObject
                    || string.IsNullOrWhiteSpace(unitId) || !LooksLikeHistoricalWorldClaim(claim)) return;
                string timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
                List<Hero> targets = new[] { respondingTarget }.Concat(listeners ?? Enumerable.Empty<Hero>())
                    .Where(x => x != null && x != player)
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).Take(12).ToList();
                foreach (Hero target in targets)
                {
                    await ReignWorldHistoryClient.CheckWorldHistoryLieAsync(string.Empty, player, target, 0f,
                        timeline, mode + "_player_to_listener", unitId, requestOwner).ConfigureAwait(false);
                }
            }
            catch (ReignRequestOwnerAbandonedException)
            {
                // An ended scene or changed campaign cannot dispatch another listener.
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Player historical claim listener observation failed: " + ex.Message);
            }
        }

        private static bool LooksLikeHistoricalWorldClaim(string text)
        {
            string raw = (text ?? string.Empty).Trim();
            string q = raw.ToLowerInvariant();
            if (q.Length < 6) return false;
            bool namedCurrentLocationAssertion = Regex.IsMatch(raw,
                @"\b(?:I|we)\s+(?:am|are|'m|'re)\s+(?:(?:currently|presently)\s+)?(?:in|at)\s+(?:the\s+)?\p{Lu}[\p{L}'-]{2,}",
                RegexOptions.CultureInvariant);
            bool namedForceStrengthAssertion = Regex.IsMatch(raw,
                @"\b\p{Lu}[\p{L}'-]{2,}\s+(?:has|commands|leads|fields)\s+(?:(?:exactly|about|roughly|nearly|only)\s+)?\d+\s+(?:troops|men|soldiers)\b",
                RegexOptions.CultureInvariant);
            if (namedCurrentLocationAssertion || namedForceStrengthAssertion) return true;
            string[] actors = { "i ", "i've ", "we ", "we've ", "my clan ", "our clan ", "my party ", "our army " };
            string[] acts =
            {
                "fought", "led", "commanded", "won", "defeated", "rescued", "freed", "captured", "killed", "wounded",
                "raided", "besieged", "conquered", "defended", "released", "paid", "bought", "sold", "married",
                "was at", "were at", "entered", "arrived", "never went to", "never been to", "not presently in",
                "not currently in", "took the castle", "took the town", "won the tournament"
            };
            return actors.Any(q.Contains) && acts.Any(q.Contains);
        }

        private static bool LooksLikeWorldHistoryLookupQuestion(string text)
        {
            string q = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (q.Length < 6 || LooksLikeHistoricalWorldClaim(q)) return false;
            string[] explicitHistory =
            {
                "world history", "campaign history", "campaign record", "canonical history", "canonical campaign",
                "historical record", "according to the record", "the record says"
            };
            string[] eventQuestions =
            {
                "what happened to", "who was responsible", "whose party was responsible", "who destroyed",
                "who defeated", "who captured", "who killed", "who conquered", "who besieged", "who rescued",
                "who led", "who fought"
            };
            string[] reportedClaims = { "traveler claims", "traveller claims", "he claims", "she claims", "they claim", "reportedly", "was said to have" };
            return explicitHistory.Any(q.Contains) || eventQuestions.Any(q.Contains) || reportedClaims.Any(q.Contains);
        }
    }
}
