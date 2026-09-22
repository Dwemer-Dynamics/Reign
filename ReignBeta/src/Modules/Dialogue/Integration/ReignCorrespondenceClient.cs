using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static int _correspondenceTickInFlight;

        public static int CorrespondenceTickInFlight =>
            Volatile.Read(ref _correspondenceTickInFlight);

        public static List<Hero> GetKnownCorrespondenceContacts()
        {
            Hero player = Hero.MainHero;
            if (player == null)
            {
                return new List<Hero>();
            }

            IEnumerable<Hero> contacts = Hero.AllAliveHeroes
                .Where(hero => ReignConversationEligibility.IsAdultLivingNpc(hero) && IsLegitimateContact(player, hero));
            return (ReignBeta.UI.HiddenInformation.ReignHiddenInformationPolicy.IsCheatRevealEnabled
                    ? contacts.OrderByDescending(hero => hero.GetRelation(player))
                        .ThenBy(hero => hero.Name?.ToString() ?? hero.StringId)
                    : contacts.OrderBy(hero => hero.Name?.ToString())
                        .ThenBy(hero => hero.StringId))
                .ToList();
        }

        public static async Task<ReignCorrespondenceSnapshot> GetCorrespondenceAsync(IEnumerable<Hero> contacts)
        {
            ReignCorrespondenceSnapshot snapshot = new ReignCorrespondenceSnapshot();
            try
            {
                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["contactIds"] = new JArray((contacts ?? Enumerable.Empty<Hero>()).Where(x => x != null).Select(x => x.StringId))
                };
                JObject response = await PostJsonAsync("/correspondence/threads", payload).ConfigureAwait(false);
                snapshot.Ok = response.Value<bool?>("ok") == true;
                snapshot.Error = response.Value<string>("error") ?? string.Empty;
                snapshot.UnreadCount = response.Value<int?>("unreadCount") ?? 0;
                snapshot.Letters = ParseLetters(response["letters"] as JArray);
            }
            catch (Exception ex)
            {
                snapshot.Error = ex.Message;
                ReignLog.Warn("Correspondence load failed: " + ex.Message);
            }

            return snapshot;
        }

        public static Task<ReignLetterSendResult> SendLetterAsync(Hero recipient, string body)
        {
            return SendLetterAsync(recipient, body, string.Empty);
        }

        public static async Task<ReignLetterSendResult> SendLetterAsync(
            Hero recipient,
            string body,
            string correlationId)
        {
            ReignLetterSendResult result = new ReignLetterSendResult();
            try
            {
                Hero sender = Hero.MainHero;
                if (sender == null || recipient == null || string.IsNullOrWhiteSpace(body))
                {
                    result.Error = "A sender, recipient, and letter text are required.";
                    return result;
                }

                double day = CurrentWorldDay();
                ReignLetterRoute route = CalculateLetterRoute(sender, recipient, day);
                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["senderId"] = sender.StringId,
                    ["senderName"] = sender.Name?.ToString() ?? "Player",
                    ["recipientId"] = recipient.StringId,
                    ["recipientName"] = recipient.Name?.ToString() ?? recipient.StringId,
                    ["body"] = body,
                    ["source"] = "player",
                    ["dispatchDay"] = day,
                    ["deliveryDay"] = route.DeliveryDay,
                    ["originId"] = route.OriginId,
                    ["destinationId"] = route.DestinationId,
                    ["sender"] = BuildHeroProfile(sender),
                    ["recipient"] = BuildHeroProfile(recipient),
                    ["playerHeroStringId"] = sender.StringId,
                    ["playerKingdomId"] = sender.Clan?.Kingdom?.StringId ?? string.Empty,
                    ["playerClanId"] = sender.Clan?.StringId ?? string.Empty,
                    ["speakerHeroStringId"] = recipient.StringId,
                    ["speakerKingdomId"] = recipient.Clan?.Kingdom?.StringId ?? string.Empty,
                    ["speakerClanId"] = recipient.Clan?.StringId ?? string.Empty,
                    ["correlationId"] = correlationId ?? string.Empty,
                    ["actionResolutionIndex"] = BuildActionResolutionIndex(recipient,
                        new List<Hero> { recipient }, true)
                };
                JObject response = await PostJsonAsync("/correspondence/send", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Error = response.Value<string>("error") ?? string.Empty;
                result.LetterId = response.Value<string>("letterId") ?? string.Empty;
                result.ThreadId = response.Value<string>("threadId") ?? string.Empty;
                result.DispatchDay = response.Value<double?>("dispatchDay") ?? day;
                result.DeliveryDay = response.Value<double?>("deliveryDay") ?? route.DeliveryDay;
                result.CorrelationId = response.Value<string>("correlationId") ?? correlationId ?? string.Empty;
                result.QueuedActions = ParseQueuedActionRows(response["queuedActions"]);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Correspondence send failed: " + ex.Message);
            }

            return result;
        }

        public static async Task<ReignLetterSendResult> SendEconomicShipmentLetterAsync(
            Hero recipient,
            string body,
            string shipmentId,
            string notificationRole,
            string originSettlementId,
            string destinationSettlementId,
            double deliveryDay)
        {
            ReignLetterSendResult result = new ReignLetterSendResult();
            try
            {
                Hero sender = Hero.MainHero;
                if (sender == null || recipient == null || sender == recipient || string.IsNullOrWhiteSpace(body)
                    || string.IsNullOrWhiteSpace(shipmentId) || string.IsNullOrWhiteSpace(notificationRole))
                {
                    result.Error = "A valid royal shipment sender, recipient, body, shipment, and notification role are required.";
                    return result;
                }
                double day = CurrentWorldDay();
                string reason = shipmentId + ":" + notificationRole;
                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["senderId"] = sender.StringId,
                    ["senderName"] = sender.Name?.ToString() ?? "Ruler",
                    ["recipientId"] = recipient.StringId,
                    ["recipientName"] = recipient.Name?.ToString() ?? recipient.StringId,
                    ["body"] = body,
                    ["source"] = "court_economic_transfer",
                    ["reason"] = reason,
                    ["dispatchDay"] = day,
                    ["deliveryDay"] = Math.Max(day + 0.25d, deliveryDay),
                    ["originId"] = originSettlementId ?? string.Empty,
                    ["destinationId"] = destinationSettlementId ?? string.Empty,
                    ["sender"] = BuildHeroProfile(sender),
                    ["recipient"] = BuildHeroProfile(recipient),
                    ["playerHeroStringId"] = sender.StringId,
                    ["playerKingdomId"] = sender.Clan?.Kingdom?.StringId ?? string.Empty,
                    ["playerClanId"] = sender.Clan?.StringId ?? string.Empty,
                    ["speakerHeroStringId"] = recipient.StringId,
                    ["speakerKingdomId"] = recipient.Clan?.Kingdom?.StringId ?? string.Empty,
                    ["speakerClanId"] = recipient.Clan?.StringId ?? string.Empty,
                    ["shipmentId"] = shipmentId,
                    ["notificationRole"] = notificationRole,
                    ["correlationId"] = shipmentId
                };
                JObject response = await PostJsonAsync("/correspondence/send", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Error = response.Value<string>("error") ?? string.Empty;
                result.LetterId = response.Value<string>("letterId") ?? response.Value<string>("letter_id") ?? string.Empty;
                result.ThreadId = response.Value<string>("threadId") ?? response.Value<string>("thread_id") ?? string.Empty;
                result.DispatchDay = response.Value<double?>("dispatchDay") ?? response.Value<double?>("dispatch_day") ?? day;
                result.DeliveryDay = response.Value<double?>("deliveryDay") ?? response.Value<double?>("delivery_day") ?? deliveryDay;
                result.CorrelationId = shipmentId;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Economic shipment letter failed: " + ex.Message);
            }
            return result;
        }

        public static async Task<ReignLetterSendResult> SendRegentCourtLetterAsync(Hero regent, CourtMatter matter)
        {
            ReignLetterSendResult result = new ReignLetterSendResult();
            try
            {
                Hero player = Hero.MainHero;
                if (regent == null || player == null || matter == null)
                {
                    result.Error = "A Regent, player, and major court matter are required.";
                    return result;
                }
                double day = CurrentWorldDay();
                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["senderId"] = regent.StringId,["senderName"] = regent.Name?.ToString() ?? "Regent",
                    ["recipientId"] = player.StringId,["recipientName"] = player.Name?.ToString() ?? "Ruler",
                    ["body"] = "A major matter requires your direction: " + matter.Title + "\n\n" + matter.Summary,
                    ["source"] = "court_regent",["reason"] = "court_regent:" + matter.MatterId,
                    ["dispatchDay"] = day,["deliveryDay"] = day + 0.25d,["originId"] = regent.CurrentSettlement?.StringId ?? string.Empty,
                    ["destinationId"] = player.CurrentSettlement?.StringId ?? string.Empty,["matterId"] = matter.MatterId,
                    ["matterRevision"] = matter.Revision,["options"] = JArray.Parse(string.IsNullOrWhiteSpace(matter.DecisionOptionsJson) ? "[]" : matter.DecisionOptionsJson)
                };
                JObject response = await PostJsonAsync("/correspondence/send", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;result.Error = response.Value<string>("error") ?? string.Empty;
                result.LetterId = response.Value<string>("letterId") ?? string.Empty;result.ThreadId = response.Value<string>("threadId") ?? string.Empty;
                result.DispatchDay = response.Value<double?>("dispatchDay") ?? day;result.DeliveryDay = response.Value<double?>("deliveryDay") ?? day + 0.25d;
                if (result.Ok) await ReignMainThread.InvokeAsync(() => matter.RegentMailLetterId = result.LetterId).ConfigureAwait(false);
            }
            catch (Exception ex) { result.Error = ex.Message;ReignLog.Warn("Regent court letter failed: " + ex.Message); }
            return result;
        }

        public static async Task<ReignCorrespondenceTickResult> TickCorrespondenceAsync()
        {
            Interlocked.Increment(ref _correspondenceTickInFlight);
            ReignCorrespondenceTickResult result = new ReignCorrespondenceTickResult();
            try
            {
                JObject response = await PostJsonAsync("/correspondence/tick", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["worldDay"] = CurrentWorldDay()
                }).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Error = response.Value<string>("error") ?? string.Empty;
                result.DeliveredLetters = ParseLetters(response["deliveredLetters"] as JArray);
                result.QueuedReplies = ParseLetters(response["queuedReplies"] as JArray);
                result.NativeRelationChanges = (response["nativeRelationChanges"] as JArray ?? new JArray()).OfType<JObject>()
                    .Select(obj => new ReignNativeRelationChange
                    {
                        SubjectId = ReadFirst(obj, "subject_id", "subjectId"),
                        TargetId = ReadFirst(obj, "target_id", "targetId"),
                        Delta = obj.Value<int?>("delta") ?? 0,
                        EventId = ReadFirst(obj, "event_id", "eventId")
                    }).ToList();
                result.QueuedActions = ParseQueuedActionRows(response["queuedActions"]);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Correspondence tick failed: " + ex.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _correspondenceTickInFlight);
            }

            return result;
        }

        public static async Task<bool> MarkLetterReadAsync(string letterId)
        {
            if (string.IsNullOrWhiteSpace(letterId))
            {
                return false;
            }

            JObject response = await PostJsonAsync("/correspondence/read", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["letterId"] = letterId,
                ["worldDay"] = CurrentWorldDay()
            }).ConfigureAwait(false);
            return response.Value<bool?>("ok") == true;
        }

        public static async Task<ReignRelationshipDevelopment> PollRelationshipDevelopmentAsync(IEnumerable<string> presentHeroIds)
        {
            try
            {
                JObject response = await PostJsonAsync("/relationships/developments/poll", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["worldDay"] = CurrentWorldDay(),
                    ["presentHeroIds"] = new JArray(presentHeroIds ?? Enumerable.Empty<string>())
                }).ConfigureAwait(false);
                return ParseDevelopment(response["development"] as JObject);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Relationship development poll failed: " + ex.Message);
                return null;
            }
        }

        public static async Task ResolveRelationshipDevelopmentAsync(string developmentId, string status)
        {
            if (string.IsNullOrWhiteSpace(developmentId))
            {
                return;
            }

            await PostJsonAsync("/relationships/developments/resolve", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["developmentId"] = developmentId,
                ["status"] = string.IsNullOrWhiteSpace(status) ? "resolved" : status,
                ["worldDay"] = CurrentWorldDay()
            }).ConfigureAwait(false);
        }

        public static async Task EvaluateRelationshipEventAsync(Hero subject, Hero target, string eventType, string summary, string eventId = null, JObject motiveDecision = null)
        {
            if (subject == null || target == null || string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            JObject request = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["eventId"] = string.IsNullOrWhiteSpace(eventId) ? "relationship_" + Guid.NewGuid().ToString("N") : eventId,
                ["eventType"] = eventType ?? "interaction",
                ["subjectId"] = subject.StringId,
                ["targetId"] = target.StringId,
                ["subject"] = BuildHeroProfile(subject),
                ["target"] = BuildHeroProfile(target),
                ["nativeRelation"] = subject.GetRelation(target),
                ["summary"] = summary,
                ["worldDay"] = CurrentWorldDay()
            };
            if (motiveDecision != null) request["motiveDecision"] = motiveDecision.DeepClone();
            JObject response = await PostJsonAsync("/relationships/evaluate", request).ConfigureAwait(false);
            int nativeDelta = response.Value<int?>("nativeRelationDelta") ?? 0;
            if (response.Value<bool?>("ok") == true && nativeDelta != 0)
            {
                await ReignMainThread.InvokeAsync(() => ChangeRelationAction.ApplyRelationChangeBetweenHeroes(subject, target, nativeDelta, true)).ConfigureAwait(false);
            }
        }

        public static Task<JObject> ApplyRulerDocketDirectionalDeltaAsync(
            string adjustmentId,
            Hero observer,
            Hero ruler,
            int delta,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(adjustmentId) || observer == null || ruler == null)
            {
                return Task.FromResult(new JObject
                {
                    ["ok"] = false,
                    ["error"] = "A stable adjustment id, observer, and ruler are required."
                });
            }

            return PostJsonAsync("/relationships/evaluate", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["eventId"] = adjustmentId,
                ["eventType"] = "ruler_docket",
                ["source"] = "ruler_docket",
                ["directionalOnly"] = true,
                ["authoritativeDirectionalDelta"] = delta,
                ["subjectId"] = observer.StringId,
                ["targetId"] = ruler.StringId,
                ["subject"] = BuildHeroProfile(observer),
                ["target"] = BuildHeroProfile(ruler),
                ["nativeRelation"] = observer.GetRelation(ruler),
                ["summary"] = string.IsNullOrWhiteSpace(reason)
                    ? "A ruler docket outcome changed the observer's view of the ruler."
                    : reason,
                ["worldDay"] = CurrentWorldDay()
            });
        }

        public static Task<JObject> RecordRulerDocketMemoryAsync(
            string jobId,
            Hero rememberingHero,
            string eventType,
            string summary,
            int worldDay)
        {
            if (string.IsNullOrWhiteSpace(jobId) || rememberingHero == null
                || string.IsNullOrWhiteSpace(summary))
            {
                return Task.FromResult(new JObject
                {
                    ["ok"] = false,
                    ["error"] = "A stable job id, remembering hero, and summary are required."
                });
            }

            return PostJsonAsync("/memory/event", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["eventId"] = jobId,
                ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["eventType"] = string.IsNullOrWhiteSpace(eventType)
                    ? "ruler_docket_long_term_memory" : eventType,
                ["summary"] = summary,
                ["participants"] = new JArray(rememberingHero.StringId,
                    Hero.MainHero?.StringId ?? string.Empty),
                ["known_by"] = new JArray(rememberingHero.StringId),
                ["visibility"] = "private",
                ["importance"] = 1.0d,
                ["worldDay"] = worldDay,
                ["memoryGroupKey"] = jobId,
                ["source"] = "ruler_docket",
                ["perspectiveHeroId"] = rememberingHero.StringId,
                ["longTerm"] = true,
                ["fullStrength"] = true
            });
        }

        public static async Task<JObject> ApplyConversationRelationshipAssessmentsAsync(
            string exchangeId,
            string mode,
            IEnumerable<Hero> conversationHeroes,
            IEnumerable<JObject> resultRows,
            string correlationId = "")
        {
            JObject empty = new JObject { ["ok"] = true, ["skipped"] = true, ["reason"] = "Conversation relationship changes are disabled or no assessments were supplied." };
            if (ReignBetaSettings.Instance?.EventRelationshipChangesEnabled == false || Hero.MainHero == null)
            {
                return empty;
            }

            List<Hero> heroes = (conversationHeroes ?? Enumerable.Empty<Hero>())
                .Concat(new[] { Hero.MainHero })
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            List<JObject> completedResults = (resultRows ?? Enumerable.Empty<JObject>())
                .Where(x => x != null && x.Value<bool?>("ok") != false)
                .ToList();
            JArray assessments = new JArray();
            JArray speakerResults = new JArray();
            bool sequentialParty = string.Equals(mode, "party_chat", StringComparison.OrdinalIgnoreCase);
            HashSet<string> priorPartySpeakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Hero.MainHero.StringId };
            foreach (JObject result in completedResults)
            {
                string currentObserver = result.Value<string>("heroStringId")
                    ?? result.Value<string>("speakerHeroStringId")
                    ?? string.Empty;
                Hero currentHero = heroes.FirstOrDefault(x => string.Equals(x.StringId, currentObserver, StringComparison.OrdinalIgnoreCase));
                speakerResults.Add(new JObject
                {
                    ["ok"] = true,
                    ["heroStringId"] = currentObserver,
                    ["heroName"] = currentHero?.Name?.ToString() ?? currentObserver,
                    ["reply"] = result.Value<string>("reply") ?? string.Empty,
                    ["participation"] = result.Value<string>("participation") ?? "speak",
                    ["reactionTargetHeroStringId"] = result.Value<string>("reactionTargetHeroStringId") ?? string.Empty,
                    ["relationshipSignal"] = result.Value<string>("relationshipSignal") ?? string.Empty
                });
                foreach (JObject assessment in (result?["relationshipAssessments"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string targetId = assessment.Value<string>("targetHeroStringId") ?? string.Empty;
                    if (sequentialParty && !priorPartySpeakers.Contains(targetId)) continue;
                    JObject normalized = (JObject)assessment.DeepClone();
                    if (sequentialParty) normalized["eligibleTargetIds"] = new JArray(priorPartySpeakers);
                    assessments.Add(normalized);
                }
                if (sequentialParty && !string.IsNullOrWhiteSpace(currentObserver)) priorPartySpeakers.Add(currentObserver);
            }
            if (assessments.Count == 0 && (!sequentialParty || speakerResults.Count == 0)) return empty;

            JArray pairs = new JArray();
            HashSet<string> pairKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < heroes.Count; i++)
            {
                for (int j = i + 1; j < heroes.Count; j++)
                {
                    Hero observer = heroes[i];
                    Hero target = heroes[j];
                    if (observer == null || target == null || observer == target) continue;
                    string key = string.Compare(observer.StringId, target.StringId, StringComparison.OrdinalIgnoreCase) <= 0
                        ? observer.StringId + "|" + target.StringId
                        : target.StringId + "|" + observer.StringId;
                    if (!pairKeys.Add(key)) continue;
                    pairs.Add(new JObject
                    {
                        ["subjectId"] = observer.StringId,
                        ["targetId"] = target.StringId,
                        ["nativeRelation"] = observer.GetRelation(target),
                        ["subject"] = BuildHeroProfile(observer),
                        ["target"] = BuildHeroProfile(target)
                    });
                }
            }

            JObject payload = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["exchangeId"] = string.IsNullOrWhiteSpace(exchangeId) ? "conversation_" + Guid.NewGuid().ToString("N") : exchangeId,
                ["mode"] = mode ?? "conversation",
                ["playerHeroStringId"] = Hero.MainHero.StringId,
                ["worldDay"] = CurrentWorldDay(),
                ["participants"] = new JArray(heroes.Select(x => x.StringId)),
                ["assessments"] = assessments,
                ["relationshipPairs"] = pairs,
                ["participantProfiles"] = new JArray(heroes.Select(BuildHeroProfile)),
                ["speakerResults"] = speakerResults
            };
            if (!string.IsNullOrWhiteSpace(correlationId)) payload["correlationId"] = correlationId;
            JObject response = await PostJsonAsync("/relationships/conversation/evaluate", payload).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true) return response;
            foreach (JObject change in (response["nativeChanges"] as JArray ?? new JArray()).OfType<JObject>())
            {
                Hero subject = heroes.FirstOrDefault(x => string.Equals(x.StringId, change.Value<string>("subjectId"), StringComparison.OrdinalIgnoreCase));
                Hero target = heroes.FirstOrDefault(x => string.Equals(x.StringId, change.Value<string>("targetId"), StringComparison.OrdinalIgnoreCase));
                int delta = change.Value<int?>("delta") ?? 0;
                bool show = change.Value<bool?>("showNotification") == true;
                if (subject != null && target != null && subject != target)
                {
                    int priorNativeRelation = subject.GetRelation(target);
                    if (delta != 0)
                    {
                        await ReignMainThread.InvokeAsync(() => ChangeRelationAction.ApplyRelationChangeBetweenHeroes(subject, target, delta, show)).ConfigureAwait(false);
                    }
                    JArray receiptIds = change["receiptIds"] as JArray ?? new JArray();
                    JObject nativeReceipt = new JObject
                    {
                        ["subjectId"] = subject.StringId,
                        ["targetId"] = target.StringId,
                        ["delta"] = delta,
                        ["priorNativeRelation"] = priorNativeRelation,
                        ["nativeRelation"] = subject.GetRelation(target),
                        ["appliedUtc"] = DateTimeOffset.UtcNow.ToString("o")
                    };
                    JObject acknowledgement = await PostJsonAsync("/relationships/conversation/native-receipt", new JObject
                    {
                        ["campaignId"] = GetCampaignId(),
                        ["receiptIds"] = receiptIds.DeepClone(),
                        ["nativeReceipt"] = nativeReceipt
                    }).ConfigureAwait(false);
                    if (acknowledgement.Value<bool?>("ok") != true)
                    {
                        throw new InvalidOperationException("The server did not acknowledge the native personal-relation receipt.");
                    }
                }
            }
            return response;
        }

        private static bool IsLegitimateContact(Hero player, Hero hero)
        {
            return hero.IsKnownToPlayer
                || hero.HasMet
                || hero.Spouse == player
                || hero.Father == player
                || hero.Mother == player
                || player.Father == hero
                || player.Mother == hero
                || (hero.Clan != null && hero.Clan == player.Clan);
        }

        private static ReignLetterRoute CalculateLetterRoute(Hero sender, Hero recipient, double day)
        {
            Settlement origin = sender.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? Settlement.CurrentSettlement;
            Settlement destination = recipient.CurrentSettlement ?? recipient.PartyBelongedTo?.CurrentSettlement;
            if (origin != null && destination != null && origin == destination)
            {
                return new ReignLetterRoute(origin.StringId, destination.StringId, day + 0.25d);
            }

            double distance = 0d;
            bool hasDistance = false;
            try
            {
                var originPosition = sender.PartyBelongedTo?.GetPosition2D ?? MobileParty.MainParty?.GetPosition2D ?? origin?.GetPosition2D;
                var destinationPosition = recipient.PartyBelongedTo?.GetPosition2D ?? destination?.GetPosition2D;
                if (originPosition.HasValue && destinationPosition.HasValue)
                {
                    distance = Math.Sqrt(originPosition.Value.DistanceSquared(destinationPosition.Value));
                    hasDistance = true;
                }
            }
            catch
            {
                hasDistance = false;
            }

            bool sameKingdom = sender.Clan?.Kingdom != null && sender.Clan.Kingdom == recipient.Clan?.Kingdom;
            double transit = sameKingdom
                ? Math.Min(3d, 1d + (hasDistance ? distance / 80d : 1d))
                : Math.Min(7d, 3d + (hasDistance ? distance / 55d : 2d));
            return new ReignLetterRoute(origin?.StringId ?? string.Empty, destination?.StringId ?? string.Empty, day + Math.Max(0.25d, transit));
        }

        private static List<ReignLetter> ParseLetters(JArray array)
        {
            List<ReignLetter> letters = new List<ReignLetter>();
            foreach (JObject obj in (array ?? new JArray()).OfType<JObject>())
            {
                letters.Add(new ReignLetter
                {
                    LetterId = ReadFirst(obj, "letter_id", "letterId"),
                    ThreadId = ReadFirst(obj, "thread_id", "threadId"),
                    SenderId = ReadFirst(obj, "sender_id", "senderId"),
                    SenderName = ReadFirst(obj, "sender_name", "senderName"),
                    RecipientId = ReadFirst(obj, "recipient_id", "recipientId"),
                    RecipientName = ReadFirst(obj, "recipient_name", "recipientName"),
                    Body = obj.Value<string>("body") ?? string.Empty,
                    Status = obj.Value<string>("status") ?? "in_transit",
                    Source = obj.Value<string>("source") ?? string.Empty,
                    Reason = obj.Value<string>("reason") ?? string.Empty,
                    DispatchDay = ReadFirstDouble(obj, "dispatch_day", "dispatchDay"),
                    DeliveryDay = ReadFirstDouble(obj, "delivery_day", "deliveryDay"),
                    DeliveredDay = ReadFirstDouble(obj, "delivered_day", "deliveredDay"),
                    ReadDay = ReadFirstDouble(obj, "read_day", "readDay")
                });
            }

            return letters;
        }

        private static ReignRelationshipDevelopment ParseDevelopment(JObject obj)
        {
            if (obj == null || !obj.HasValues)
            {
                return null;
            }

            return new ReignRelationshipDevelopment
            {
                DevelopmentId = ReadFirst(obj, "development_id", "developmentId"),
                SubjectId = ReadFirst(obj, "subject_id", "subjectId"),
                TargetId = ReadFirst(obj, "target_id", "targetId"),
                Kind = obj.Value<string>("kind") ?? string.Empty,
                Polarity = obj.Value<string>("polarity") ?? "mixed",
                DeliveryMode = ReadFirst(obj, "delivery_mode", "deliveryMode"),
                Summary = obj.Value<string>("summary") ?? string.Empty,
                Priority = obj.Value<double?>("priority") ?? 0.5d
            };
        }

        private static string ReadFirst(JObject obj, string first, string second)
        {
            return obj?.Value<string>(first) ?? obj?.Value<string>(second) ?? string.Empty;
        }

        private static double ReadFirstDouble(JObject obj, string first, string second)
        {
            return obj?.Value<double?>(first) ?? obj?.Value<double?>(second) ?? 0d;
        }

        private static double CurrentWorldDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays;
        }
    }

    public sealed class ReignCorrespondenceSnapshot
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public int UnreadCount { get; set; }
        public List<ReignLetter> Letters { get; set; } = new List<ReignLetter>();
    }

    public sealed class ReignCorrespondenceTickResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public List<ReignLetter> DeliveredLetters { get; set; } = new List<ReignLetter>();
        public List<ReignLetter> QueuedReplies { get; set; } = new List<ReignLetter>();
        public List<ReignNativeRelationChange> NativeRelationChanges { get; set; } = new List<ReignNativeRelationChange>();
        public List<ReignBeta.World.ReignWorldActionRecord> QueuedActions { get; set; } = new List<ReignBeta.World.ReignWorldActionRecord>();
    }

    public sealed class ReignNativeRelationChange
    {
        public string SubjectId { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public int Delta { get; set; }
        public string EventId { get; set; } = string.Empty;
    }

    public sealed class ReignLetterSendResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string LetterId { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public double DispatchDay { get; set; }
        public double DeliveryDay { get; set; }
        public List<ReignBeta.World.ReignWorldActionRecord> QueuedActions { get; set; } = new List<ReignBeta.World.ReignWorldActionRecord>();
    }

    public sealed class ReignLetter
    {
        public string LetterId { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public string RecipientName { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public double DispatchDay { get; set; }
        public double DeliveryDay { get; set; }
        public double DeliveredDay { get; set; }
        public double ReadDay { get; set; }
    }

    public sealed class ReignRelationshipDevelopment
    {
        public string DevelopmentId { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Polarity { get; set; } = string.Empty;
        public string DeliveryMode { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public double Priority { get; set; }
    }

    internal sealed class ReignLetterRoute
    {
        public ReignLetterRoute(string originId, string destinationId, double deliveryDay)
        {
            OriginId = originId ?? string.Empty;
            DestinationId = destinationId ?? string.Empty;
            DeliveryDay = deliveryDay;
        }

        public string OriginId { get; }
        public string DestinationId { get; }
        public double DeliveryDay { get; }
    }
}
