using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;

namespace ReignBeta.Platform.Integration
{
    public static partial class ReignServerClient
    {
        public static async Task<ReignSocialEventTurnReply> RequestSocialEventTurnAsync(
            ReignSocialEventSession session,
            IReadOnlyList<Hero> activeHeroes,
            string playerText,
            string turnId,
            Action<ReignEventReply> onReply = null,
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null,
            Func<bool> requestOwner = null)
        {
            ReignSocialEventTurnReply turn = new ReignSocialEventTurnReply { TurnId = turnId ?? string.Empty };
            try
            {
                if (session?.Record == null || string.IsNullOrWhiteSpace(playerText))
                {
                    turn.Error = "A social-event session, active participants, and player text are required.";
                    return turn;
                }

                // The UI and Live Bridge may enter from a worker. All native reads, the
                // original load fence and the local owner share one game-thread capture.
                RequestFence fence = null;
                List<Hero> speakers = null;
                List<Hero> attendees = null;
                ReignBeta.Relationships.Campaign.ReignRelationshipCampaignBehavior relationBehavior = null;
                bool playerIsFemale = false;
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    EnsureRequestOwner(requestOwner, "social event turn capture");
                    fence = RequestFence.Capture();
                    speakers = (activeHeroes ?? new List<Hero>())
                        .Where(x => x != null && x != Hero.MainHero)
                        .Distinct()
                        .Take(ReignSocialEventSession.MaxActiveParticipants)
                        .ToList();
                    attendees = session.Record.GetAttendees().ToList();
                    relationBehavior = ReignBeta.Relationships.Campaign.ReignRelationshipCampaignBehavior.Instance;
                    playerIsFemale = Hero.MainHero?.IsFemale ?? false;
                    return speakers.Count == 0 ? null : BuildSocialEventPayload(session, null, playerText);
                }).ConfigureAwait(false);
                if (payload == null)
                {
                    turn.Error = "A social-event session, active participants, and player text are required.";
                    return turn;
                }
                // Every later dispatch and local effect belongs to the original load and owner.
                Func<bool> owned = () => fence.IsCurrent() && RequestOwnerCurrent(requestOwner);

                string turnCorrelationId = string.IsNullOrWhiteSpace(turnId)
                    ? Guid.NewGuid().ToString("N")
                    : turnId;
                payload["nativeReceiptProtocol"] = "reign-native-relationship-receipt-v2";
                // The visible UI and the Live Bridge both enter through this method.
                // Use the exactly-once turn id as the correlation root so every
                // participant prompt, adjudication receipt, and audit row can be
                // reconciled to the same player submission without resending it.
                payload["correlationId"] = turnCorrelationId;
                payload["turnId"] = turnCorrelationId;
                turn.TurnId = payload.Value<string>("turnId") ?? string.Empty;
                payload["sceneTurnId"] = turn.TurnId;
                payload["contributionId"] = turn.TurnId;
                payload["mode"] = "social_event";
                payload["playerIsFemale"] = playerIsFemale;
                payload["phaseExchangeCount"] = session.PhaseExchangeCount;
                payload["remainingActiveSlots"] = session.RemainingActiveSlots;
                payload["automaticApproachBlockedHeroIds"] = new JArray(session.AutomaticApproachBlockedHeroStringIds);
                payload["idleCounters"] = JObject.FromObject(session.IgnoredTurnCounts);
                payload["structuredActionProtocolVersion"] = Reign.Core.Contracts.Actions.ActionNegotiation.ProtocolVersion;
                payload["structuredActionRecipientId"] = actionDraft?.NpcId ?? "";
                payload["conversationRelationshipChangesEnabled"] = ReignBetaSettings.Instance?.EventRelationshipChangesEnabled != false;

                // Fix the server-owned floor before requesting any model-selected
                // context. Keep the full audience in every selected writer payload.
                payload["activeHeroIds"] = new JArray(speakers.Select(hero => hero.StringId));
                JObject floor = await PostOwnedJsonAsync("/events/social/turn-plan", payload, owned).ConfigureAwait(false);
                if (floor.Value<bool?>("ok") != true || !(floor["responseOrder"] is JArray floorOrder))
                    throw new InvalidOperationException(floor.Value<string>("error") ?? "The event speaking plan is unavailable.");
                var selectedIds = floorOrder.Values<string>().ToList();
                if (selectedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedIds.Count
                    || selectedIds.Any(id => !speakers.Any(hero => hero.StringId.Equals(id, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException("The event speaking plan contains an unavailable participant.");
                var selectedSpeakers = selectedIds.Select(id => speakers.First(hero => hero.StringId.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList();

                JArray participantRequests = new JArray();
                foreach (Hero speaker in selectedSpeakers)
                {
                    // One owner-checked game-thread capture per selected speaker, in plan order.
                    JObject request = await ReignMainThread.InvokeAsync(() =>
                    {
                        EnsureRequestOwner(owned, "social event speaker capture");
                        JObject built = BuildSocialEventPayload(session, speaker, playerText);
                        built["correlationId"] = turnCorrelationId + "-npc-" + speaker.StringId;
                        built["turnId"] = turn.TurnId;
                        built["sceneTurnId"] = turn.TurnId;
                        built["contributionId"] = turn.TurnId;
                        built["mode"] = "social_event";
                        built["turnType"] = "player_reply";
                        built["approachOpening"] = false;
                        built["structuredActionRecipientId"] = actionDraft?.NpcId ?? "";
                        ReignStructuredActionClient.ConfigureStructuredActions(built, speaker,
                            actionDraft?.NpcId == speaker.StringId ? actionDraft : null, Reign.Core.Contracts.Actions.AgreementChannel.InPerson);
                        built["playerIsFemale"] = Hero.MainHero?.IsFemale ?? false;
                        return built;
                    }).ConfigureAwait(false);
                    await AttachContextBundlesAsync(request, "social_event", speaker, attendees).ConfigureAwait(false);
                    participantRequests.Add(request);
                }
                payload["participantRequests"] = participantRequests;

                JArray relationshipPairs = await ReignMainThread.InvokeAsync(() =>
                {
                    EnsureRequestOwner(owned, "social event relationship capture");
                    var pairs = new JArray();
                    List<Hero> possibleTargets = speakers.Concat(new[] { Hero.MainHero }).Where(x => x != null).Distinct().ToList();
                    foreach (Hero subject in speakers)
                    {
                        foreach (Hero target in possibleTargets.Where(x => x != subject))
                        {
                            pairs.Add(new JObject {
                                ["subjectId"] = subject.StringId, ["targetId"] = target.StringId,
                                ["nativeRelation"] = subject.GetRelation(target),
                                ["subject"] = BuildHeroProfile(subject), ["target"] = BuildHeroProfile(target) });
                        }
                    }
                    return pairs;
                }).ConfigureAwait(false);
                payload["relationshipPairs"] = relationshipPairs;
                payload["progressiveReplies"] = true;
                payload["replyCursor"] = 0;
                var delivered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                JObject response = null;
                bool retriedFailure = false;
                // One request per speaker plus final adjudication, with one
                // bounded retry under the original exactly-once turn identity.
                for (int step = 0; step < selectedSpeakers.Count + 2; step++)
                {
                    // Rechecked before every progressive dispatch: a stale owner sends nothing more.
                    response = await PostOwnedJsonAsync("/events/social/turn", payload, owned).ConfigureAwait(false);
                    await DeliverSocialEventRepliesAsync(response, session, playerText, turn, delivered, onReply, participantRequests,
                        fence, requestOwner).ConfigureAwait(false);
                    if (response.Value<bool?>("ok") == true && response.Value<bool?>("hasMore") == true)
                    {
                        int nextCursor = response.Value<int?>("replyCursor") ?? 0;
                        if (nextCursor <= payload.Value<int>("replyCursor") || nextCursor > speakers.Count)
                            throw new InvalidOperationException("The group conversation did not advance to its next reply.");
                        payload["replyCursor"] = nextCursor;
                        continue;
                    }
                    if (response.Value<bool?>("ok") != true && !retriedFailure
                        && string.Equals(response.Value<string>("status"), "in_progress", StringComparison.OrdinalIgnoreCase))
                    {
                        retriedFailure = true;
                        // A failed legacy response can also contain completed
                        // speakers. Acknowledge those before resuming the rest.
                        payload["replyCursor"] = delivered.Count;
                        continue;
                    }
                    break;
                }
                turn.Ok = response?.Value<bool?>("ok") == true && response.Value<bool?>("hasMore") != true;
                turn.Error = response.Value<string>("error") ?? string.Empty;
                if (!turn.Ok && string.IsNullOrWhiteSpace(turn.Error))
                    turn.Error = "The group conversation could not finish; completed replies have been retained.";
                turn.Idempotent = response.Value<bool?>("idempotent") == true;
                turn.RawResponse = response;
                turn.AddressedHeroStringIds.AddRange(StringValues(response["addressedHeroStringIds"]));
                turn.JoinedHeroStringIds.AddRange(StringValues(response["joinedHeroStringIds"]));
                turn.WanderedHeroStringIds.AddRange(StringValues(response["wanderedHeroStringIds"]));
                JObject idle = response["nextIdleCounters"] as JObject;
                if (idle != null)
                {
                    foreach (JProperty property in idle.Properties())
                    {
                        turn.NextIdleCounters[property.Name] = property.Value.Value<int?>() ?? 0;
                    }
                }

                if (turn.Ok)
                {
                    foreach (JObject receipt in (response["relationshipReceipts"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        string receiptId = receipt.Value<string>("receiptId") ?? string.Empty;
                        if (receipt.Value<bool?>("ok") != true || string.IsNullOrWhiteSpace(receiptId))
                        {
                            continue;
                        }
                        int delta = receipt.Value<int?>("nativeRelationDelta") ?? 0;
                        bool showNotification = receipt.Value<bool?>("showNotification") == true;
                        JArray adjudicationReceiptIds = receipt["receiptIds"] as JArray ?? new JArray();
                        JObject nativeReceipt = null;
                        // Hero resolution and the native effect run in one game-thread callback for
                        // the original load and a still-current owner.
                        bool applied = await ReignMainThread.InvokeAsync(() =>
                        {
                            if (relationBehavior == null || !ReferenceEquals(relationBehavior, ReignBeta.Relationships.Campaign.ReignRelationshipCampaignBehavior.Instance)
                                || !fence.IsCurrent())
                                throw new InvalidOperationException("The campaign changed before the social relationship effect could be applied.");
                            EnsureRequestOwner(requestOwner, "social relationship effect");
                            Hero subject = ReignObjectResolver.FindHero(receipt.Value<string>("subjectId") ?? string.Empty);
                            Hero target = ReignObjectResolver.FindHero(receipt.Value<string>("targetId") ?? string.Empty);
                            if (subject == null || target == null) return false;
                            nativeReceipt = relationBehavior.ApplyConversationRelationOnce(adjudicationReceiptIds.Values<string>(), subject, target, delta, showNotification);
                            return true;
                        }).ConfigureAwait(false);
                        if (applied)
                        {
                            JObject acknowledgement = await PostJsonAsync("/relationships/conversation/native-receipt", new JObject
                            {
                                ["campaignId"] = payload.Value<string>("campaignId"),
                                ["receiptIds"] = adjudicationReceiptIds.DeepClone(),
                                ["nativeReceipt"] = nativeReceipt
                            }).ConfigureAwait(false);
                            if (acknowledgement.Value<bool?>("ok") != true)
                            {
                                throw new InvalidOperationException("The server did not acknowledge the social-event personal-relation receipt.");
                            }
                            session.TryRegisterRelationshipReceipt(receiptId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                turn.Error = ex.Message;
                // A closed screen or changed load is an expected abandonment, not a failure.
                if (ex is ReignRequestOwnerAbandonedException)
                    ReignLog.Info("Social event group turn abandoned: " + ex.Message);
                else
                    ReignLog.Warn("Social event group turn failed: " + ex.Message);
            }
            return turn;
        }

        private static async Task DeliverSocialEventRepliesAsync(JObject response,
            ReignSocialEventSession session, string playerText, ReignSocialEventTurnReply turn,
            HashSet<string> delivered, Action<ReignEventReply> onReply, JArray participantRequests,
            RequestFence fence, Func<bool> requestOwner)
        {
            foreach (JObject row in (response["participantResults"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string heroId = row.Value<string>("heroStringId") ?? string.Empty;
                if (row.Value<bool?>("ok") != true || string.IsNullOrWhiteSpace(heroId) || delivered.Contains(heroId)) continue;
                var reply = new ReignEventReply
                {
                    Ok = true, HeroStringId = heroId,
                    CorrelationId = row.Value<string>("correlationId") ?? string.Empty,
                    Text = row.Value<string>("reply") ?? string.Empty,
                    Participation = row.Value<string>("participation") ?? "speak",
                    ReactionTargetHeroStringId = row.Value<string>("reactionTargetHeroStringId") ?? string.Empty,
                    Emotion = row.Value<string>("emotion") ?? string.Empty,
                    Intent = row.Value<string>("intent") ?? string.Empty,
                    RelationshipSignal = row.Value<string>("relationshipSignal") ?? string.Empty,
                    RelationshipAssessments = row["relationshipAssessments"] as JArray ?? new JArray(),
                    SelectedContextPulls = row["selectedContextPulls"] as JArray ?? new JArray(),
                    ContextBundles = row["contextBundles"] as JArray ?? new JArray(),
                    QueuedActions = row["queuedActions"] as JArray ?? new JArray(),
                    RawResponse = row,
                    EncyclopediaText = row.Value<string>("encyclopediaText") ?? string.Empty
                };
                await ReignMainThread.InvokeAsync(() =>
                {
                    // The original load is tested before any native lookup; a stale local
                    // owner then stops every local effect and later dispatch of this turn.
                    if (!fence.IsCurrent())
                        throw new InvalidOperationException("The campaign changed before the action response arrived.");
                    EnsureRequestOwner(requestOwner, "social event reply presentation");
                    Hero speaker = ReignObjectResolver.FindHero(heroId);
                    var request = participantRequests.OfType<JObject>().FirstOrDefault(r => r["structuredActionContext"]?.Value<string>("npcId") == heroId);
                    // The reply bytes stay exact; the receipt is presented as a system line.
                    if (request != null) ReignStructuredActionClient.ProcessStructuredActionDecision(request, row, speaker, inlineReceipt: false);
                    if (speaker != null)
                    {
                        ApplyHeroEncyclopediaText(speaker, reply.EncyclopediaText);
                        if (!string.IsNullOrWhiteSpace(reply.Text))
                        {
                            _ = ObserveNpcHistoricalClaimForListenersSafelyAsync(speaker, session.Record.GetAttendees(), reply.Text, "social_event");
                            _ = ObservePlayerHistoricalClaimForListenersSafelyAsync(speaker, session.Record.GetAttendees(), playerText, "social_event");
                        }
                    }
                    onReply?.Invoke(reply);
                }).ConfigureAwait(false);
                delivered.Add(heroId);
                turn.ParticipantResults.Add(reply);
            }
        }

        private static IEnumerable<string> StringValues(JToken token)
        {
            return (token as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x));
        }
    }

    public sealed class ReignSocialEventTurnReply
    {
        public bool Ok { get; set; }
        public bool Idempotent { get; set; }
        public string TurnId { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public JObject RawResponse { get; set; } = new JObject();
        public List<ReignEventReply> ParticipantResults { get; } = new List<ReignEventReply>();
        public List<string> AddressedHeroStringIds { get; } = new List<string>();
        public List<string> JoinedHeroStringIds { get; } = new List<string>();
        public List<string> WanderedHeroStringIds { get; } = new List<string>();
        public Dictionary<string, int> NextIdleCounters { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
