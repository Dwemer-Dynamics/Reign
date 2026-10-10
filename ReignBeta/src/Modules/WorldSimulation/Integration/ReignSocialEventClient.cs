using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        public static async Task<ReignSocialEventTurnReply> RequestSocialEventTurnAsync(
            ReignSocialEventSession session,
            IReadOnlyList<Hero> activeHeroes,
            string playerText,
            string turnId,
            Action<ReignEventReply> onReply = null,
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null)
        {
            ReignSocialEventTurnReply turn = new ReignSocialEventTurnReply { TurnId = turnId ?? string.Empty };
            try
            {
                List<Hero> speakers = (activeHeroes ?? new List<Hero>())
                    .Where(x => x != null && x != Hero.MainHero)
                    .Distinct()
                    .Take(ReignSocialEventSession.MaxActiveParticipants)
                    .ToList();
                if (session?.Record == null || speakers.Count == 0 || string.IsNullOrWhiteSpace(playerText))
                {
                    turn.Error = "A social-event session, active participants, and player text are required.";
                    return turn;
                }

                string turnCorrelationId = string.IsNullOrWhiteSpace(turnId)
                    ? Guid.NewGuid().ToString("N")
                    : turnId;
                JObject payload = BuildSocialEventPayload(session, null, playerText);
                var relationCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
                payload["nativeReceiptProtocol"] = "reign-native-relationship-receipt-v2";
                var relationBehavior = ReignBeta.Campaign.ReignRelationshipCampaignBehavior.Instance;
                string relationTimeline = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
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
                payload["playerIsFemale"] = Hero.MainHero?.IsFemale ?? false;
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
                JObject floor = await PostJsonAsync("/events/social/turn-plan", payload).ConfigureAwait(false);
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
                    JObject request = await ReignMainThread.InvokeAsync(() => BuildSocialEventPayload(session, speaker, playerText)).ConfigureAwait(false);
                    request["correlationId"] = turnCorrelationId + "-npc-" + speaker.StringId;
                    request["turnId"] = turn.TurnId;
                    request["sceneTurnId"] = turn.TurnId;
                    request["contributionId"] = turn.TurnId;
                    request["mode"] = "social_event";
                    request["turnType"] = "player_reply";
                    request["approachOpening"] = false;
                    request["structuredActionRecipientId"] = actionDraft?.NpcId ?? "";
                    await ReignMainThread.InvokeAsync(() => ConfigureStructuredActions(request, speaker,
                        actionDraft?.NpcId == speaker.StringId ? actionDraft : null)).ConfigureAwait(false);
                    request["playerIsFemale"] = Hero.MainHero?.IsFemale ?? false;
                    await AttachContextBundlesAsync(request, "social_event", speaker, session.Record.GetAttendees()).ConfigureAwait(false);
                    participantRequests.Add(request);
                }
                payload["participantRequests"] = participantRequests;

                JArray relationshipPairs = await ReignMainThread.InvokeAsync(() =>
                {
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
                    response = await PostJsonAsync("/events/social/turn", payload).ConfigureAwait(false);
                    await DeliverSocialEventRepliesAsync(response, session, playerText, turn, delivered, onReply, participantRequests,
                        relationCampaign, relationTimeline).ConfigureAwait(false);
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
                        Hero subject = ReignObjectResolver.FindHero(receipt.Value<string>("subjectId") ?? string.Empty);
                        Hero target = ReignObjectResolver.FindHero(receipt.Value<string>("targetId") ?? string.Empty);
                        int delta = receipt.Value<int?>("nativeRelationDelta") ?? 0;
                        bool showNotification = receipt.Value<bool?>("showNotification") == true;
                        if (subject != null && target != null)
                        {
                            JArray adjudicationReceiptIds = receipt["receiptIds"] as JArray ?? new JArray();
                            JObject nativeReceipt = null;
                            await ReignMainThread.InvokeAsync(() =>
                            {
                                if (relationBehavior == null || !ReferenceEquals(relationBehavior, ReignBeta.Campaign.ReignRelationshipCampaignBehavior.Instance)
                                    || !ReferenceEquals(relationCampaign, TaleWorlds.CampaignSystem.Campaign.Current)
                                    || relationTimeline != (ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"))
                                    throw new InvalidOperationException("The campaign changed before the social relationship effect could be applied.");
                                nativeReceipt = relationBehavior.ApplyConversationRelationOnce(adjudicationReceiptIds.Values<string>(), subject, target, delta, showNotification);
                            }).ConfigureAwait(false);
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
                ReignLog.Warn("Social event group turn failed: " + ex.Message);
            }
            return turn;
        }

        private static async Task DeliverSocialEventRepliesAsync(JObject response,
            ReignSocialEventSession session, string playerText, ReignSocialEventTurnReply turn,
            HashSet<string> delivered, Action<ReignEventReply> onReply, JArray participantRequests,
            TaleWorlds.CampaignSystem.Campaign expectedCampaign, string expectedTimeline)
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
                    Hero speaker = ReignObjectResolver.FindHero(heroId);
                    if (!ReferenceEquals(expectedCampaign, TaleWorlds.CampaignSystem.Campaign.Current)
                        || expectedTimeline != (ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"))
                        throw new InvalidOperationException("The campaign changed before the action response arrived.");
                    var request = participantRequests.OfType<JObject>().FirstOrDefault(r => r["structuredActionContext"]?.Value<string>("npcId") == heroId);
                    if (request != null) { ProcessStructuredActionDecision(request, row, speaker); reply.Text = row.Value<string>("reply") ?? ""; }
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
