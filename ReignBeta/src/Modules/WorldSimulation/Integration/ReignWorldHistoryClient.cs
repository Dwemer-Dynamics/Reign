using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using static ReignBeta.Platform.Integration.ReignServerClient;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Persistence.Integration;

namespace ReignBeta.WorldSimulation.Integration
{
    internal static class ReignWorldHistoryClient
    {
        internal static async Task<JObject> OpenWorldHistoryTimelineAsync(string timelineId, long savedSequence,
            string savedHeadEventId, float completeFromDay, string ownedOrigin)
        {
            const string route = "/world-history/timeline/open";
            var request = new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = timelineId ?? "main", ["savedSequence"] = savedSequence,
                ["savedHeadEventId"] = savedHeadEventId ?? string.Empty, ["historyCompleteFromWorldDay"] = completeFromDay,
                ["currentWorldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays
            };
            if (string.IsNullOrEmpty(ownedOrigin)) return await PostJsonAsync(route, request).ConfigureAwait(false);
            int alignment = ReignSaveSyncCoordinator.CampaignGeneration;
            return await SendToOwnedHistoryOriginAsync(ownedOrigin, route, request,
                () => ReignSaveSyncCoordinator.CampaignGeneration == alignment).ConfigureAwait(false);
        }

        internal static async Task<JObject> VerifyWorldHistoryAsync(string claim, string claimantId, string speakerId, string speakerKingdomId, float worldDay, string timelineId)
        {
            return await PostJsonAsync("/world-history/verify", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = timelineId ?? "main", ["claim"] = claim ?? string.Empty,
                ["claimantId"] = claimantId ?? string.Empty, ["speakerId"] = speakerId ?? string.Empty,
                ["speakerKingdomId"] = speakerKingdomId ?? string.Empty, ["worldDay"] = worldDay,
                ["lookupOnly"] = string.IsNullOrWhiteSpace(claimantId)
            }).ConfigureAwait(false);
        }

        internal static async Task<JObject> CheckWorldHistoryLieAsync(string claim, Hero claimant, Hero target,
            float worldDay, string timelineId, string mode, string acceptedUnitId = null, Func<bool> requestOwner = null)
        {
            JObject payload = await ReignMainThread.InvokeAsync(() =>
            {
                EnsureRequestOwner(requestOwner, "historical claim payload capture");
                if (!string.IsNullOrWhiteSpace(acceptedUnitId))
                    return new JObject
                    {
                        ["campaignId"] = GetCampaignId(), ["timelineId"] = timelineId ?? "main",
                        ["claimantId"] = claimant?.StringId ?? string.Empty, ["targetId"] = target?.StringId ?? string.Empty,
                        ["mode"] = mode ?? string.Empty, ["acceptedUnitId"] = acceptedUnitId
                    };

                var claimantSettlement = claimant?.CurrentSettlement;
                var targetSettlement = target?.CurrentSettlement;
                return new JObject
                {
                    ["campaignId"] = GetCampaignId(), ["timelineId"] = timelineId ?? "main", ["claim"] = claim ?? string.Empty,
                    ["claimantId"] = claimant?.StringId ?? string.Empty, ["targetId"] = target?.StringId ?? string.Empty,
                    ["targetKingdomId"] = target?.Clan?.Kingdom?.StringId ?? target?.MapFaction?.StringId ?? string.Empty,
                    ["worldDay"] = worldDay, ["mode"] = mode ?? string.Empty,
                    ["claimantSkills"] = BuildSkillProfile(claimant), ["targetSkills"] = BuildSkillProfile(target),
                    ["sceneRisk"] = BuildLieSceneRisk(claimant, target),
                    ["nativeContext"] = new JObject
                    {
                        ["claimantCurrentSettlementId"] = claimantSettlement?.StringId ?? string.Empty,
                        ["claimantCurrentSettlementName"] = claimantSettlement?.Name?.ToString() ?? string.Empty,
                        ["claimantCurrentSettlementOwnerClanId"] = claimantSettlement?.OwnerClan?.StringId ?? string.Empty,
                        ["claimantCurrentSettlementOwnerKingdomId"] = claimantSettlement?.OwnerClan?.Kingdom?.StringId
                            ?? claimantSettlement?.MapFaction?.StringId ?? string.Empty,
                        ["claimantCurrentSettlementOwnerKingdomName"] = claimantSettlement?.OwnerClan?.Kingdom?.Name?.ToString()
                            ?? claimantSettlement?.MapFaction?.Name?.ToString() ?? string.Empty,
                        ["targetCurrentSettlementId"] = targetSettlement?.StringId ?? string.Empty,
                        ["targetCurrentSettlementName"] = targetSettlement?.Name?.ToString() ?? string.Empty,
                        ["targetCurrentSettlementOwnerClanId"] = targetSettlement?.OwnerClan?.StringId ?? string.Empty,
                        ["targetCurrentSettlementOwnerKingdomId"] = targetSettlement?.OwnerClan?.Kingdom?.StringId
                            ?? targetSettlement?.MapFaction?.StringId ?? string.Empty,
                        ["targetCurrentSettlementOwnerKingdomName"] = targetSettlement?.OwnerClan?.Kingdom?.Name?.ToString()
                            ?? targetSettlement?.MapFaction?.Name?.ToString() ?? string.Empty,
                        ["sameCurrentSettlement"] = claimantSettlement != null && targetSettlement != null
                            && string.Equals(claimantSettlement.StringId, targetSettlement.StringId, StringComparison.OrdinalIgnoreCase)
                    }
                };
            }).ConfigureAwait(false);
            return await PostOwnedJsonAsync("/world-history/lie-check", payload, requestOwner).ConfigureAwait(false);
        }

        internal static async Task<JObject> IngestWorldHistoryBatchAsync(string campaignId, string timelineId,
            float completeFromDay, JArray events, string generation, long epoch, string ownedOrigin)
        {
            if (events != null && events.OfType<JObject>().Any(ReignWorldHistoryOutbox.HasOwnedHistoryMarker))
                throw new InvalidOperationException("An owned history record cannot use ordinary aggregate ingest.");
            var request = new JObject
            {
                ["campaignId"] = campaignId, ["clientId"] = "reign_native_campaign", ["timelineId"] = timelineId ?? "main",
                ["sharedActivityGeneration"] = generation, ["_reignHistoryEpoch"] = epoch,
                ["historyCompleteFromWorldDay"] = completeFromDay, ["events"] = events ?? new JArray()
            };
            const string route = "/world-history/ingest-batch";
            bool Current() => ReignWorldHistoryTransport.IsCurrentRequest(request);
            return string.IsNullOrEmpty(ownedOrigin)
                ? await PostJsonAsync(route, request, () => Task.FromResult(Current())).ConfigureAwait(false)
                : await SendToOwnedHistoryOriginAsync(ownedOrigin, route, request, Current).ConfigureAwait(false);
        }

        internal static async Task<bool> ObserveOwnedJusticePredecessorAsync(string origin, int alignment,
            string campaign, string timeline, string generation, string loadSession, long sequence, string head)
        {
            string route = "/world-history/head?headProofVersion=1&campaignId=" + Uri.EscapeDataString(campaign)
                + "&timelineId=" + Uri.EscapeDataString(timeline)
                + "&sharedActivityGeneration=" + Uri.EscapeDataString(generation);
            bool Current() => ReignSaveSyncCoordinator.CampaignGeneration == alignment;
            JObject reply = await SendToOwnedHistoryOriginAsync(origin, route, null, Current)
                .ConfigureAwait(false);
            return Current() && reply?["ok"]?.Type == JTokenType.Boolean && reply.Value<bool>("ok")
                && reply["headProofVersion"]?.Type == JTokenType.Integer && reply.Value<int>("headProofVersion") == 1
                && AsExactString(reply, "result") == "observed" && reply["ownership"] is JObject owner
                && AsExactString(owner, "campaignId") == campaign && AsExactString(owner, "timelineId") == timeline
                && AsExactString(owner, "sharedActivityGeneration") == generation
                && AsExactString(reply, "nativeLoadSessionId") == loadSession
                && reply["headSequence"]?.Type == JTokenType.Integer && reply.Value<long>("headSequence") == sequence
                && AsExactString(reply, "headEventId") == head;
        }

        private static async Task<JObject> SendToOwnedHistoryOriginAsync(string origin, string route, JObject request, Func<bool> current)
        {
            await ReignSaveSyncCoordinator.WaitForReadyAsync(route).ConfigureAwait(false);
            await ReignCampaignInitializationGate.WaitForReadyAsync(route).ConfigureAwait(false);
            return await ReignHttpTransport.SendWorkerOwnedPinnedJsonAsync(origin, route, request, current).ConfigureAwait(false);
        }

        private static string AsExactString(JObject value, string key) =>
            value?[key]?.Type == JTokenType.String ? value.Value<string>(key) : null;

        internal static JObject BuildOwnedJusticeHistoryEnvelope(JObject facts)
        {
            string player = facts.Value<string>("playerHeroId");
            string accused = facts.Value<string>("accusedHeroId");
            bool measured = facts["effectIntentId"]?.Type == JTokenType.String;
            var material = new JObject { ["caseId"] = facts["caseId"].DeepClone(), ["accusedHeroId"] = accused, ["playerHeroId"] = player };
            if (measured)
            {
                foreach (string field in new[] { "discoveryEventId", "effectIntentId", "appliedRelationshipDelta",
                    "measuredRelationshipChange", "reputationValue" })
                    material[field] = facts[field].DeepClone();
                material["effectStatus"] = "measured";
            }
            else
            {
                material["evidenceIds"] = facts["evidenceIds"].DeepClone();
                material["effectStatus"] = "pending";
            }
            return new JObject
            {
                ["eventId"] = facts["eventId"].DeepClone(), ["sequence"] = facts["sequence"].DeepClone(),
                ["worldDay"] = facts["worldDay"].DeepClone(), ["category"] = "justice",
                ["eventType"] = measured ? "arrest_reclassification_effect_settled" : "arrest_evidence_reclassified",
                ["phase"] = measured ? "effect_settled" : "evidence_established",
                ["correlationId"] = facts["caseId"].DeepClone(), ["locationId"] = facts["locationId"].DeepClone(),
                ["locationName"] = facts["locationName"].DeepClone(), ["source"] = "reign_system",
                ["disseminationClass"] = "ordinary", ["isComplete"] = true,
                ["summary"] = measured ? "The relationship and reputation effects of reclassification were measured."
                    : "New evidence established cause in an active arrest.",
                ["payload"] = material,
                ["entities"] = new JArray
                {
                    new JObject { ["entityId"] = player, ["entityType"] = "hero", ["role"] = "political actor", ["isPlayer"] = true },
                    new JObject { ["entityId"] = accused, ["entityType"] = "hero", ["role"] = "political participant" }
                }
            };
        }

        // The caller retains the saved event and owns both this scope and the pinned origin.
        // An unavailable proof leaves the saved work pending; this read never mutates the outbox.
        internal static async Task<bool> MatchesWorldHistoryEventAtOriginAsync(
            ReignWorldHistoryEventProof expected, Func<bool> requestOwner, string ownedLoadSession = null)
        {
            if (expected == null || requestOwner == null) return false;
            bool Current() => requestOwner() && ReignHttpTransport.IsAllowedPinnedOrigin(expected.Origin);
            string route = expected.PathAndQuery;
            if (ownedLoadSession != null)
                route += "&ownedAdmissionProofVersion=1&nativeLoadSessionId=" + Uri.EscapeDataString(ownedLoadSession);
            JObject reply = await SendPinnedJsonAsync(expected.Origin, route, null, Current)
                .ConfigureAwait(false);
            bool exact = ownedLoadSession == null ? expected.Matches(reply) : expected.MatchesOwnedAdmission(reply, ownedLoadSession);
            return exact && await ReignMainThread.InvokeAsync(Current).ConfigureAwait(false);
        }

        internal static async Task<JObject> GetSocialCatalogAsync()
        {
            return await GetJsonAsync("/social/catalog?campaignId=" + Uri.EscapeDataString(GetCampaignId()))
                .ConfigureAwait(false);
        }

        internal static async Task<JObject> GetSocialCharacterStatusAsync(
            string subjectId, string timelineId, double worldDay)
        {
            return await PostJsonAsync("/rumors/status", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = string.IsNullOrWhiteSpace(timelineId)
                    ? "main" : timelineId,
                ["subjectId"] = subjectId ?? string.Empty,
                ["worldDay"] = worldDay
            }).ConfigureAwait(false);
        }
    }

    internal static class ReignWorldHistoryTransport
    {
        private const long MaximumOutboxBytes = 32L * 1024L * 1024L;
        private const long RetainedOutboxBytes = 16L * 1024L * 1024L;
        private static readonly ConcurrentQueue<JObject> Pending = new ConcurrentQueue<JObject>();
        private static readonly ConcurrentDictionary<string, JObject> SharedPending = new ConcurrentDictionary<string, JObject>(StringComparer.Ordinal);
        private static readonly object StateLock = new object();
        private static readonly object FileLock = new object();
        private static readonly object RoutineLock = new object();
        private static readonly Dictionary<string, JObject> RoutineBuckets =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> RoutineEventTypes =
            new HashSet<string>(new[]
            {
                "settlement_entered", "settlement_left", "troops_recruited",
                "troops_given_to_settlement", "troops_deserted", "items_looted",
                "item_sold", "loot_distributed", "caravan_transaction_completed",
                "prisoners_sold", "mobile_party_created", "mobile_party_destroyed",
                "party_leader_changed", "ship_created", "ship_destroyed",
                "ship_repaired", "ship_owner_changed", "quest_started",
                "issue_updated", "unattributed_state_change"
            }, StringComparer.OrdinalIgnoreCase);
        private static int _workerRunning;
        private static int _retryScheduled;
        private static int _urgentFlush;
        private static long _activeSaveBoundarySequence = long.MaxValue;
        private static int _consecutiveUploadFailures;
        private static long _completedUploadBatches;
        private static long _acknowledgedEventCount;
        private static long _lastUploadRequested;
        private static long _lastUploadAccepted;
        private static long _lastUploadDuplicates;
        private static long _lastUploadEphemeral;
        private static long _lastUploadClientDurationMs;
        private static long _lastUploadServerDurationMs;
        private static long _lastCompactionDurationMs;
        private static long _lastCompactedEvents;
        private static long _lastCompactionBacklog;
        private static DateTime _nextUploadAttemptUtc;
        private static string _campaignId = "default";
        private static string _timelineId = "main";
        private static float _completeFromDay;
        private static long _epoch;
        private sealed class OwnedJusticeDelivery
        {
            internal OwnedJusticeDelivery(JObject record, string origin, int alignment, long epoch,
                long sequence, string loadSession, JObject request, ReignWorldHistoryEventProof proof)
            {
                Record = record;
                Origin = origin;
                Alignment = alignment;
                Epoch = epoch;
                Sequence = sequence;
                LoadSession = loadSession;
                Request = request;
                Proof = proof;
            }
            internal JObject Record { get; }
            internal string Origin { get; }
            internal int Alignment { get; }
            internal long Epoch { get; }
            internal long Sequence { get; }
            internal string LoadSession { get; }
            internal JObject Request { get; }
            internal ReignWorldHistoryEventProof Proof { get; }
        }
        private static OwnedJusticeDelivery _ownedJusticeDelivery;

        internal static bool IsCurrentRequest(JObject payload) =>
            payload?.Value<long?>("_reignHistoryEpoch") == Interlocked.Read(ref _epoch);

        internal static void Initialize(string campaignId, string timelineId, float completeFromDay)
        {
            FlushRoutineBuckets(true);
            lock (StateLock)
            {
                if (_campaignId != SafeSegment(campaignId, "default") || _timelineId != SafeSegment(timelineId, "main"))
                {
                    SharedPending.Clear();
                    Volatile.Write(ref _ownedJusticeDelivery, null);
                    Interlocked.Increment(ref _epoch);
                }
                _campaignId = SafeSegment(campaignId, "default");
                _timelineId = SafeSegment(timelineId, "main");
                _completeFromDay = completeFromDay;
            }
            Kick();
        }

        private static bool IsPublicHistoryEvent(JObject item) => item != null &&
            (item.Value<string>("eventType") == "public_announcement" && item.Value<string>("source") == "reign_public_news"
                || Reign.Core.Contracts.World.ReignPublicNews.IsNativePublicDiplomacy(item.Value<string>("eventType"), item.Value<string>("source"), item.Value<string>("disseminationClass")));

        internal static long CaptureQueueEpoch() => Interlocked.Read(ref _epoch);

        internal static bool HasExactQueueKey(string key) =>
            !string.IsNullOrWhiteSpace(key) && SafeSegment(key, string.Empty) == key;

        internal static bool UsesOrdinarySystemQueue(string eventType) =>
            !string.IsNullOrWhiteSpace(eventType) && eventType != "public_announcement"
            && !RoutineEventTypes.Contains(eventType);

        internal static bool TryStageOwnedJusticeDelivery(JObject historyEvent, JObject request,
            string origin, int alignment, long epoch, Action publishHead)
        {
            if (historyEvent == null || request == null || publishHead == null
                || request["campaignId"]?.Type != JTokenType.String || request["timelineId"]?.Type != JTokenType.String
                || request["sharedActivityGeneration"]?.Type != JTokenType.String || request["nativeLoadSessionId"]?.Type != JTokenType.String
                || historyEvent.Value<long?>("sequence") is not long sequence
                || !ReignWorldHistoryEventProof.TryCreate(origin, request.Value<string>("campaignId"),
                    request.Value<string>("timelineId"), request.Value<string>("sharedActivityGeneration"), historyEvent, out var proof))
                return false;

            string campaign = request.Value<string>("campaignId");
            string timeline = request.Value<string>("timelineId");
            string loadSession = request.Value<string>("nativeLoadSessionId");
            if (alignment != ReignSaveSyncCoordinator.CampaignGeneration || !ReignHttpTransport.IsAllowedPinnedOrigin(origin))
                return false;
            FlushRoutineBuckets(true);
            lock (StateLock)
            lock (FileLock)
            {
                if (epoch != _epoch || campaign != _campaignId || timeline != _timelineId
                    || _ownedJusticeDelivery != null || !Pending.IsEmpty || !SharedPending.IsEmpty)
                    return false;
                string path = PendingPath(timeline, campaign);
                if (File.Exists(path) && new FileInfo(path).Length != 0) return false;
                JObject immutableRequest = (JObject)request.DeepClone();
                var diskRecord = (JObject)historyEvent.DeepClone();
                diskRecord[ReignWorldHistoryOutbox.OwnedHistoryMarker] = new JObject
                {
                    ["version"] = 1,
                    ["origin"] = origin,
                    ["request"] = immutableRequest.DeepClone()
                };
                var delivery = new OwnedJusticeDelivery(diskRecord, origin, alignment, epoch, sequence, loadSession, immutableRequest, proof);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ReignWorldHistoryOutbox.AppendRecords(path, diskRecord.ToString(Formatting.None) + Environment.NewLine, flushToDisk: true);
                publishHead();
                Volatile.Write(ref _ownedJusticeDelivery, delivery);
            }
            Kick();
            return true;
        }

        // A ready native system event is complete before admission. Keep its scope
        // check, staging and head publication together while resets are excluded.
        internal static bool TryStageReadySystemEvent(JObject historyEvent, string campaignId,
            string timelineId, long epoch, Action publishHead)
        {
            if (historyEvent == null || publishHead == null
                || historyEvent.Value<string>("source") != "reign_system"
                || !UsesOrdinarySystemQueue(historyEvent.Value<string>("eventType"))
                || historyEvent.Value<bool?>("isComplete") != true
                || historyEvent.Value<long?>("sequence") is not long sequence || sequence <= 0
                || !HasExactQueueKey(campaignId) || !HasExactQueueKey(timelineId)) return false;
            string eventId = historyEvent.Value<string>("eventId");
            if (string.IsNullOrWhiteSpace(eventId)) return false;

            var envelope = (JObject)historyEvent.DeepClone();
            lock (StateLock)
            {
                if (epoch != _epoch || campaignId != _campaignId || timelineId != _timelineId) return false;
                envelope["_reignCampaignId"] = _campaignId;
                envelope["_reignTimelineId"] = _timelineId;
                envelope["_reignHistoryEpoch"] = _epoch;
                Pending.Enqueue(envelope);
                if (!string.IsNullOrWhiteSpace(envelope["payload"]?.Value<string>("playerHeroId")))
                    SharedPending[eventId] = envelope;
                publishHead();
            }
            Kick();
            return true;
        }

        internal static void Enqueue(JObject historyEvent)
        {
            if (historyEvent == null || string.IsNullOrWhiteSpace(historyEvent.Value<string>("eventId")))
            {
                return;
            }
            JObject envelope = (JObject)historyEvent.DeepClone();
            lock (StateLock)
            {
                envelope["_reignCampaignId"] = _campaignId;
                envelope["_reignTimelineId"] = _timelineId;
                envelope["_reignHistoryEpoch"] = _epoch;
            }
            string eventType = envelope.Value<string>("eventType") ?? string.Empty;
            if (eventType == "public_announcement" && envelope.Value<string>("source") == "reign_public_news")
            {
                // Publication must survive a crash before the async queue drains.
                // Use the same outbox, campaign/epoch lock and uploader as native
                // history, and never add a second mutable announcement store.
                lock (StateLock)
                {
                    if (envelope.Value<long?>("_reignHistoryEpoch") != _epoch
                        || envelope.Value<string>("_reignCampaignId") != _campaignId || envelope.Value<string>("_reignTimelineId") != _timelineId)
                        throw new InvalidOperationException("Public announcement belongs to an older campaign history generation.");
                    Persist(PendingPath(_timelineId, _campaignId), new[] { envelope }, _epoch);
                    SharedPending[envelope.Value<string>("eventId")] = envelope;
                }
                Kick();
                return;
            }
            bool shared = IsPublicHistoryEvent(envelope) || !string.IsNullOrWhiteSpace(envelope["payload"]?.Value<string>("playerHeroId"));
            if (shared) SharedPending[envelope.Value<string>("eventId")] = envelope;
            if (RoutineEventTypes.Contains(eventType) && !shared)
            {
                envelope["retentionClass"] = "routine";
                CoalesceRoutineEvent(envelope);
                return;
            }
            Pending.Enqueue(envelope);
            Kick();
        }

        internal static async Task<JObject> SynchronizeSharedActivitiesAsync()
        {
            string timeline, campaign, generation, ownedOrigin;
            float completeFrom;
            long epoch;
            lock (StateLock) { timeline = _timelineId; campaign = _campaignId; completeFrom = _completeFromDay;
                epoch = _epoch; generation = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
                ownedOrigin = ReignServices.WorldHistory?.OwnedJusticeHistoryOrigin; }
            string path = PendingPath(timeline, campaign);
            // Recover relevant durable outbox entries without making the next
            // reply wait for unrelated world traffic or the normal upload cadence.
            List<JObject> eligible;
            bool ownedHistoryPending;
            lock (StateLock)
            {
                if (epoch != _epoch)
                    return new JObject { ["complete"] = false, ["timelineId"] = timeline, ["generation"] = generation,
                        ["eventIds"] = new JArray(), ["pendingPublicNews"] = 0, ["reason"] = "activity_sync_stale_owner" };
                lock (FileLock)
                {
                    ownedHistoryPending = Pending.Any(ReignWorldHistoryOutbox.HasOwnedHistoryMarker)
                        || SharedPending.Values.Any(ReignWorldHistoryOutbox.HasOwnedHistoryMarker);
                    if (File.Exists(path))
                        foreach (string line in File.ReadLines(path))
                        {
                            JObject item;
                            if (!ReignWorldHistoryOutbox.TryReadOrdinaryRecord(line, out item, out bool owned))
                            {
                                ownedHistoryPending |= owned;
                                continue;
                            }
                            if ((!string.IsNullOrWhiteSpace(item["payload"]?.Value<string>("playerHeroId"))
                                    || IsPublicHistoryEvent(item))
                                && !string.IsNullOrWhiteSpace(item.Value<string>("eventId")))
                                SharedPending[item.Value<string>("eventId")] = item;
                        }
                }
                long boundary = Volatile.Read(ref _activeSaveBoundarySequence);
                eligible = SharedPending.Values.Where(e => !ReignWorldHistoryOutbox.HasOwnedHistoryMarker(e)
                    && e.Value<string>("_reignTimelineId") == timeline
                    && e.Value<string>("_reignCampaignId") == campaign
                    && (e.Value<long?>("sequence") ?? 0) <= boundary).ToList();
            }
            int publicNewsCount = eligible.Count(IsPublicHistoryEvent);
            if (ownedHistoryPending)
                return new JObject { ["complete"] = false, ["timelineId"] = timeline, ["generation"] = generation,
                    ["eventIds"] = new JArray(), ["pendingPublicNews"] = publicNewsCount, ["reason"] = "activity_sync_owned_history_pending" };
            var pending = eligible.OrderBy(e => !IsPublicHistoryEvent(e))
                .ThenBy(e => e.Value<string>("eventType") == "party_contact_interval")
                .ThenByDescending(e => e.Value<long?>("sequence") ?? 0).Take(256).OrderBy(e => e.Value<long?>("sequence") ?? 0).ToList();
            var ids = new JArray(pending.Select(e => e.Value<string>("eventId")));
            if (pending.Count == 0) return new JObject { ["complete"] = epoch == Interlocked.Read(ref _epoch), ["timelineId"] = timeline, ["generation"] = generation, ["eventIds"] = ids, ["pendingPublicNews"] = publicNewsCount };
            var upload = ReignWorldHistoryClient.IngestWorldHistoryBatchAsync(campaign, timeline, completeFrom,
                new JArray(pending), generation, epoch, ownedOrigin);
            if (await Task.WhenAny(upload, Task.Delay(4000)).ConfigureAwait(false) != upload)
            {
                // Observe any late failure; the durable outbox owns retry.
                _ = upload.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return new JObject { ["complete"] = false, ["timelineId"] = timeline, ["eventIds"] = ids, ["pendingPublicNews"] = publicNewsCount, ["reason"] = "activity_sync_pending" };
            }
            JObject response;
            try { response = await upload.ConfigureAwait(false); }
            catch { return new JObject { ["complete"] = false, ["timelineId"] = timeline, ["eventIds"] = ids, ["pendingPublicNews"] = publicNewsCount, ["reason"] = "activity_sync_unavailable" }; }
            bool complete = response?.Value<bool?>("ok") == true
                && AcknowledgeCurrentEpoch(path, pending, epoch);
            if (complete)
            {
                publicNewsCount -= pending.Count(IsPublicHistoryEvent);
                complete = pending.Count == eligible.Count;
            }
            return new JObject { ["complete"] = complete, ["timelineId"] = timeline, ["generation"] = generation, ["eventIds"] = ids, ["pendingPublicNews"] = Math.Max(0, publicNewsCount) };
        }

        internal static bool FlushAndWait(TimeSpan timeout)
        {
            return FlushAndWaitInternal(timeout, long.MaxValue);
        }

        internal static void BeginSaveBoundary(long maximumSequence)
        {
            Interlocked.Exchange(ref _activeSaveBoundarySequence,
                Math.Max(0L, maximumSequence));
        }

        internal static void EndSaveBoundary(long maximumSequence)
        {
            Interlocked.CompareExchange(ref _activeSaveBoundarySequence,
                long.MaxValue, Math.Max(0L, maximumSequence));
            Kick();
        }

        internal static bool FlushAndWaitThroughSequence(
            long maximumSequence,
            TimeSpan timeout)
        {
            return FlushAndWaitInternal(timeout, Math.Max(0L, maximumSequence));
        }

        private static bool FlushAndWaitInternal(
            TimeSpan timeout,
            long maximumSequence)
        {
            FlushRoutineBuckets(true);
            Interlocked.Exchange(ref _urgentFlush, 1);
            Kick();
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                bool diskPending, memoryPending;
                // Inspect both sides of the memory-to-disk handoff under the
                // same owner lock as the append and removal.
                lock (StateLock)
                {
                    diskPending = HasPendingThroughSequenceOnDisk(PendingPath(_timelineId), maximumSequence);
                    memoryPending = HasPendingThroughSequenceInMemory(maximumSequence);
                }
                bool workerRunning = Volatile.Read(ref _workerRunning) != 0;
                if (!memoryPending && !diskPending)
                {
                    Interlocked.Exchange(ref _urgentFlush, 0);
                    return true;
                }
                if (ReignWorldHistoryFlushPolicy.ShouldKickWorker(
                    memoryPending,
                    diskPending,
                    workerRunning)
                    && CanAttemptUpload())
                {
                    Kick();
                }
                Thread.Sleep(50);
            }
            Interlocked.Exchange(ref _urgentFlush, 0);
            return false;
        }

        private static bool HasPendingThroughSequenceInMemory(
            long maximumSequence)
        {
            return Pending.Any(item => ReignWorldHistoryOutbox.HasOwnedHistoryMarker(item)
                ? ReignWorldHistoryOutbox.RetainOwnedThroughSequence(item, maximumSequence)
                : ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(item?.Value<long?>("sequence"), maximumSequence));
        }

        private static bool HasPendingThroughSequenceOnDisk(
            string path,
            long maximumSequence)
        {
            lock (FileLock)
            {
                if (!File.Exists(path)) return false;
                foreach (string line in File.ReadLines(path))
                {
                    try
                    {
                        JObject item = JObject.Parse(line);
                        if (ReignWorldHistoryOutbox.HasOwnedHistoryMarker(item)
                            ? ReignWorldHistoryOutbox.RetainOwnedThroughSequence(item, maximumSequence)
                            : ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(item.Value<long?>("sequence"), maximumSequence))
                            return true;
                    }
                    catch
                    {
                        // A malformed durable line cannot be proven newer than
                        // the save boundary. Preserve the fail-closed behavior.
                        return true;
                    }
                }
                return false;
            }
        }

        internal static int PendingCount()
        {
            FlushRoutineBuckets(true);
            int count = Pending.Count;
            string path;
            lock (StateLock) path = PendingPath(_timelineId);
            lock (FileLock)
            {
                if (File.Exists(path))
                    count += File.ReadLines(path).Count();
            }
            return count;
        }

        internal static bool UploadInFlight => Volatile.Read(ref _workerRunning) != 0;

        internal static JObject UploadDiagnostics()
        {
            return new JObject
            {
                ["completedBatches"] = Interlocked.Read(ref _completedUploadBatches),
                ["acknowledgedEvents"] = Interlocked.Read(ref _acknowledgedEventCount),
                ["lastRequested"] = Interlocked.Read(ref _lastUploadRequested),
                ["lastAccepted"] = Interlocked.Read(ref _lastUploadAccepted),
                ["lastDuplicates"] = Interlocked.Read(ref _lastUploadDuplicates),
                ["lastEphemeral"] = Interlocked.Read(ref _lastUploadEphemeral),
                ["lastClientDurationMs"] = Interlocked.Read(ref _lastUploadClientDurationMs),
                ["lastServerDurationMs"] = Interlocked.Read(ref _lastUploadServerDurationMs),
                ["lastCompactionDurationMs"] = Interlocked.Read(ref _lastCompactionDurationMs),
                ["lastCompactedEvents"] = Interlocked.Read(ref _lastCompactedEvents),
                ["lastCompactionBacklog"] = Interlocked.Read(ref _lastCompactionBacklog)
            };
        }

        internal static void ResetToSavePoint(string campaignId, string timelineId, long maximumSequence)
        {
            FlushRoutineBuckets(true);
            string campaign = SafeSegment(campaignId, "default");
            string timeline = SafeSegment(timelineId, "main");
            lock (StateLock)
            {
                SharedPending.Clear();
                Volatile.Write(ref _ownedJusticeDelivery, null);
                Interlocked.Increment(ref _epoch);
                _campaignId = campaign;
                _timelineId = timeline;

                List<JObject> retained = new List<JObject>();
                while (Pending.TryDequeue(out JObject queued))
                {
                    string queuedCampaign = queued.Value<string>("_reignCampaignId") ?? string.Empty;
                    string queuedTimeline = queued.Value<string>("_reignTimelineId") ?? string.Empty;
                    bool throughSave = ReignWorldHistoryOutbox.HasOwnedHistoryMarker(queued)
                        ? ReignWorldHistoryOutbox.RetainOwnedThroughSequence(queued, maximumSequence)
                        : (queued.Value<long?>("sequence") ?? long.MaxValue) <= maximumSequence;
                    if (queuedCampaign.Equals(campaign, StringComparison.OrdinalIgnoreCase)
                        && queuedTimeline.Equals(timeline, StringComparison.OrdinalIgnoreCase)
                        && throughSave) retained.Add(queued);
                }
                IEnumerable<JObject> restored = retained.Any(ReignWorldHistoryOutbox.HasOwnedHistoryMarker)
                    ? retained : retained.OrderBy(x => x.Value<long?>("sequence") ?? 0L);
                foreach (JObject item in restored) Pending.Enqueue(item);

                string path = PendingPath(timeline);
                lock (FileLock)
                {
                    if (!File.Exists(path)) return;
                    ReignWorldHistoryOutbox.RestoreThroughSequence(path, maximumSequence);
                }
            }
        }

        internal static void Flush()
        {
            FlushRoutineBuckets(false);
            Kick();
        }

        internal static void FlushAll()
        {
            FlushRoutineBuckets(true);
            Kick();
        }

        private static void CoalesceRoutineEvent(JObject incoming)
        {
            int day = (int)Math.Floor(incoming.Value<double?>("worldDay") ?? 0d);
            JArray entities = incoming["entities"] as JArray ?? new JArray();
            JObject primary = entities.OfType<JObject>().FirstOrDefault(entity =>
                (entity.Value<string>("role") ?? string.Empty).IndexOf("actor", StringComparison.OrdinalIgnoreCase) >= 0
                || (entity.Value<string>("role") ?? string.Empty).IndexOf("leader", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? entities.OfType<JObject>().FirstOrDefault();
            string actor = primary?.Value<string>("entityId") ?? string.Empty;
            string key = (incoming.Value<string>("eventType") ?? string.Empty) + "|"
                + day.ToString(CultureInfo.InvariantCulture) + "|"
                + (incoming.Value<string>("locationId") ?? string.Empty) + "|" + actor;
            lock (RoutineLock)
            {
                FlushRoutineBucketsLocked(false, day);
                if (!RoutineBuckets.TryGetValue(key, out JObject bucket))
                {
                    JObject payload = bucketPayload(incoming);
                    payload["coalescedCount"] = 1;
                    payload["quantityTotal"] = EntityQuantityTotal(entities);
                    incoming["payload"] = payload;
                    RoutineBuckets[key] = incoming;
                    return;
                }
                JObject mergedPayload = bucketPayload(bucket);
                mergedPayload["coalescedCount"] = (mergedPayload.Value<int?>("coalescedCount") ?? 1) + 1;
                mergedPayload["quantityTotal"] =
                    (mergedPayload.Value<double?>("quantityTotal") ?? 0d) + EntityQuantityTotal(entities);
                bucket["payload"] = mergedPayload;
                bucket["eventId"] = incoming["eventId"];
                bucket["sequence"] = incoming["sequence"];
                bucket["worldDay"] = incoming["worldDay"];
                bucket["summary"] = (bucket.Value<string>("summary") ?? incoming.Value<string>("eventType") ?? "routine event")
                    + " (coalesced x" + mergedPayload.Value<int>("coalescedCount").ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private static JObject bucketPayload(JObject value)
        {
            return value["payload"] as JObject ?? new JObject();
        }

        private static double EntityQuantityTotal(JArray entities)
        {
            return (entities ?? new JArray()).OfType<JObject>()
                .Sum(entity => Math.Abs(entity.Value<double?>("quantity") ?? 0d));
        }

        private static void FlushRoutineBuckets(bool includeNewestDay)
        {
            lock (RoutineLock)
            {
                int newestDay = RoutineBuckets.Count == 0
                    ? int.MinValue
                    : RoutineBuckets.Values.Max(value =>
                        (int)Math.Floor(value.Value<double?>("worldDay") ?? 0d));
                FlushRoutineBucketsLocked(includeNewestDay, newestDay);
            }
        }

        private static void FlushRoutineBucketsLocked(bool includeNewestDay, int newestDay)
        {
            List<string> ready = RoutineBuckets
                .Where(pair => includeNewestDay
                    || (int)Math.Floor(pair.Value.Value<double?>("worldDay") ?? 0d) < newestDay)
                .Select(pair => pair.Key).ToList();
            foreach (string key in ready)
            {
                Pending.Enqueue(RoutineBuckets[key]);
                RoutineBuckets.Remove(key);
            }
            if (ready.Count > 0) Kick();
        }

        private static void Kick()
        {
            if (ReignCampaignInitializationGate.IsPending
                && !ReignCampaignInitializationGate.IsInitializationRequestScopeActive)
                return;
            if (Interlocked.CompareExchange(ref _workerRunning, 1, 0) == 0)
            {
                _ = Task.Run(ProcessAsync);
            }
        }

        private static async Task ProcessAsync()
        {
            bool heldOwnedMarker = false;
            try
            {
                if (Volatile.Read(ref _urgentFlush) == 0)
                    await Task.Delay(250).ConfigureAwait(false);
                while (true)
                {
                    string timeline, campaign, generation, ownedOrigin;
                    long epoch;
                    float completeFrom;
                    lock (StateLock)
                    {
                        timeline = _timelineId;
                        campaign = _campaignId;
                        epoch = _epoch;
                        generation = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty;
                        ownedOrigin = ReignServices.WorldHistory?.OwnedJusticeHistoryOrigin;
                        completeFrom = _completeFromDay;
                    }
                    string path = PendingPath(timeline, campaign);
                    bool urgent = Volatile.Read(ref _urgentFlush) != 0;
                    int uploadBatchSize =
                        ReignWorldHistoryFlushPolicy.UploadBatchSize(urgent);
                    PersistPendingPrefix(path, uploadBatchSize, epoch);

                    if (!CanAttemptUpload())
                    {
                        break;
                    }

                    long maximumSequence = Volatile.Read(
                        ref _activeSaveBoundarySequence);
                    List<JObject> persisted = ReadPendingBatch(path,
                        uploadBatchSize, maximumSequence, out bool ownedHistoryPending);
                    if (persisted.Count > 0 && !urgent)
                    {
                        Stopwatch coalesceTimer = Stopwatch.StartNew();
                        int stagedCount = persisted.Count;
                        while (ReignWorldHistoryFlushPolicy
                            .ShouldCoalesceNormalUpload(
                                Volatile.Read(ref _urgentFlush) != 0,
                                stagedCount,
                                coalesceTimer.ElapsedMilliseconds))
                        {
                            await Task.Delay(ReignWorldHistoryFlushPolicy
                                .NormalCoalescePollDelayMs)
                                .ConfigureAwait(false);
                            stagedCount += PersistPendingPrefix(path, uploadBatchSize - stagedCount, epoch);
                        }
                        coalesceTimer.Stop();
                        urgent = Volatile.Read(ref _urgentFlush) != 0;
                        uploadBatchSize = ReignWorldHistoryFlushPolicy
                            .UploadBatchSize(urgent);
                        maximumSequence = Volatile.Read(
                            ref _activeSaveBoundarySequence);
                        persisted = ReadPendingBatch(path, uploadBatchSize,
                            maximumSequence, out ownedHistoryPending);
                    }
                    if (persisted.Count == 0)
                    {
                        if (ownedHistoryPending)
                        {
                            var delivery = Volatile.Read(ref _ownedJusticeDelivery);
                            if (delivery != null && WorkerOwns(delivery, epoch))
                            {
                                if (await TryDeliverOwnedJusticeAsync(path, delivery, epoch).ConfigureAwait(false)) continue;
                                if (WorkerOwns(delivery, epoch)) RegisterUploadFailure();
                            }
                            heldOwnedMarker = true;
                            break;
                        }
                        if (!HasPendingThroughSequenceInMemory(maximumSequence))
                        {
                            break;
                        }
                        continue;
                    }

                    try
                    {
                        Stopwatch uploadTimer = Stopwatch.StartNew();
                        if (epoch != Interlocked.Read(ref _epoch)) continue;
                        JObject response = await ReignWorldHistoryClient.IngestWorldHistoryBatchAsync(campaign, timeline, completeFrom,
                            new JArray(persisted), generation, epoch, ownedOrigin).ConfigureAwait(false);
                        uploadTimer.Stop();
                        if (epoch != Interlocked.Read(ref _epoch)) continue;
                        if (response?.Value<bool?>("ok") != true)
                        {
                            RegisterUploadFailure();
                            break;
                        }
                        if (!AcknowledgeCurrentEpoch(path, persisted, epoch)) continue;
                        RegisterUploadSuccess();
                        RecordUploadTelemetry(response, persisted.Count,
                            uploadTimer.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("World-history upload deferred: " + ex.Message);
                        RegisterUploadFailure();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("World-history outbox worker failed: " + ex);
                RegisterUploadFailure();
            }
            finally
            {
                Interlocked.Exchange(ref _workerRunning, 0);
                if (!heldOwnedMarker && !Pending.IsEmpty && CanAttemptUpload())
                {
                    Kick();
                }
            }
        }

        private static bool WorkerOwns(OwnedJusticeDelivery delivery, long epoch) =>
            delivery.Epoch == epoch && epoch == Interlocked.Read(ref _epoch)
            && ReferenceEquals(delivery, Volatile.Read(ref _ownedJusticeDelivery))
            && delivery.Sequence <= Volatile.Read(ref _activeSaveBoundarySequence)
            && ReignSaveSyncCoordinator.CampaignGeneration == delivery.Alignment;

        private static async Task<bool> TryDeliverOwnedJusticeAsync(string path, OwnedJusticeDelivery delivery, long epoch)
        {
            bool Current() => WorkerOwns(delivery, epoch);
            lock (FileLock)
                if (!ReignWorldHistoryOutbox.HasExactOwnedFirstRecord(path, delivery.Record)) return false;
            JObject reply;
            try
            {
                reply = await ReignHttpTransport.SendWorkerOwnedPinnedJsonAsync(delivery.Origin,
                    "/world-history/owned-justice/append", delivery.Request, Current).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!Current()) { return false; }
            if (!Current() || !delivery.Proof.MatchesAppend(reply, delivery.LoadSession)) return false;
            lock (StateLock)
            lock (FileLock)
            {
                if (epoch != _epoch || !ReferenceEquals(delivery, _ownedJusticeDelivery)
                    || delivery.Sequence > Volatile.Read(ref _activeSaveBoundarySequence)
                    || ReignSaveSyncCoordinator.CampaignGeneration != delivery.Alignment
                    || !ReignWorldHistoryOutbox.AcknowledgeExactOwnedFirstRecord(path, delivery.Record))
                    return false;
                Volatile.Write(ref _ownedJusticeDelivery, null);
            }
            RegisterUploadSuccess();
            return true;
        }

        private static int PersistPendingPrefix(string path, int limit, long epoch)
        {
            lock (StateLock)
            {
                if (epoch != _epoch) return 0;
                return ReignWorldHistoryOutbox.PersistQueuePrefix(Pending, limit,
                    prefix => Persist(path, prefix, epoch));
            }
        }

        private static void Persist(string path, IEnumerable<JObject> events, long epoch)
        {
            lock (StateLock)
            lock (FileLock)
            {
                if (epoch != _epoch)
                    throw new InvalidOperationException("A stale world-history epoch cannot append to the current outbox.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder builder = new StringBuilder();
                foreach (JObject item in events)
                {
                    if (item.Value<string>("_reignCampaignId") != _campaignId || item.Value<string>("_reignTimelineId") != _timelineId) continue;
                    builder.AppendLine(item.ToString(Formatting.None));
                }
                ReignWorldHistoryOutbox.AppendRecords(path, builder.ToString());
                TrimOutboxIfNeeded(path);
            }
        }

        private static void TrimOutboxIfNeeded(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaximumOutboxBytes) return;
            // Trimming around an owned record could erase the unsent predecessor or the barrier itself.
            if (ReignWorldHistoryOutbox.FindOwnedSequence(File.ReadLines(path), Array.Empty<JObject>()).HasValue) return;

            long start = Math.Max(0L, info.Length - RetainedOutboxBytes);
            ReignWorldHistoryOutbox.Rewrite(path, (reader, writer) =>
            {
                // Personal outcomes are not disposable world noise. Retain them
                // even if an extended outage exceeds the ordinary outbox bound.
                foreach (string earlier in File.ReadLines(path))
                {
                    try
                    {
                        JObject item = JObject.Parse(earlier);
                        if (!string.IsNullOrWhiteSpace(item["payload"]?.Value<string>("playerHeroId"))
                            || IsPublicHistoryEvent(item)) writer.WriteLine(earlier);
                    }
                    catch { }
                }
                reader.BaseStream.Seek(start, SeekOrigin.Begin);
                if (start > 0L) reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    try {
                        var item = JObject.Parse(line);
                        if (!string.IsNullOrWhiteSpace(item["payload"]?.Value<string>("playerHeroId"))
                            || IsPublicHistoryEvent(item)) continue;
                    } catch { }
                    writer.WriteLine(line);
                }
            });
            ReignLog.Warn("World-history outbox exceeded 32 MB; personal activity and public announcements were preserved alongside the newest 16 MB of ordinary events.");
        }

        // Callers hold StateLock and FileLock so the on-disk and queued records are one observation.
        private static long? OwnedSequenceOnDiskOrInQueue(string path) =>
            ReignWorldHistoryOutbox.FindOwnedSequence(File.Exists(path) ? File.ReadLines(path) : Enumerable.Empty<string>(),
                Pending.Concat(SharedPending.Values));

        private static List<JObject> ReadPendingBatch(
            string path, int limit, long maximumSequence, out bool ownedHistoryPending)
        {
            lock (StateLock)
            lock (FileLock)
            {
                long? ownedSequence = OwnedSequenceOnDiskOrInQueue(path);
                ownedHistoryPending = ownedSequence.HasValue;
                return File.Exists(path)
                    ? ReignWorldHistoryOutbox.ReadOrdinaryPrefix(File.ReadLines(path), limit, maximumSequence, ownedSequence)
                    : new List<JObject>();
            }
        }

        private static bool AcknowledgeCurrentEpoch(string path, IEnumerable<JObject> events, long epoch)
        {
            lock (StateLock)
            lock (FileLock)
            {
                if (epoch != _epoch) return false;
                long? ownedSequence = OwnedSequenceOnDiskOrInQueue(path);
                if (events.Any(item => ReignWorldHistoryOutbox.HasOwnedHistoryMarker(item)
                        || !ReignWorldHistoryOutbox.PrecedesOwnedSequence(item.Value<long?>("sequence"), ownedSequence)))
                    return false;

                var ids = new HashSet<string>(events.Select(item => item.Value<string>("eventId"))
                    .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
                if (File.Exists(path)) ReignWorldHistoryOutbox.RemoveOrdinaryAcknowledged(path, ids, ownedSequence);
                foreach (string id in ids)
                {
                    if (!ownedSequence.HasValue)
                        SharedPending.TryRemove(id, out _);
                    else if (SharedPending.TryGetValue(id, out var cached) && !ReignWorldHistoryOutbox.HasOwnedHistoryMarker(cached)
                        && ReignWorldHistoryOutbox.PrecedesOwnedSequence(cached.Value<long?>("sequence"), ownedSequence))
                        ((ICollection<KeyValuePair<string, JObject>>)SharedPending).Remove(new KeyValuePair<string, JObject>(id, cached));
                }
                return true;
            }
        }

        private static bool CanAttemptUpload()
        {
            lock (StateLock)
            {
                return DateTime.UtcNow >= _nextUploadAttemptUtc;
            }
        }

        private static void RegisterUploadSuccess()
        {
            lock (StateLock)
            {
                _consecutiveUploadFailures = 0;
                _nextUploadAttemptUtc = DateTime.MinValue;
            }
        }

        private static void RecordUploadTelemetry(
            JObject response,
            int requested,
            long clientDurationMs)
        {
            Interlocked.Increment(ref _completedUploadBatches);
            Interlocked.Add(ref _acknowledgedEventCount, requested);
            Interlocked.Exchange(ref _lastUploadRequested,
                response?.Value<long?>("requested") ?? requested);
            Interlocked.Exchange(ref _lastUploadAccepted,
                response?.Value<long?>("accepted") ?? 0L);
            Interlocked.Exchange(ref _lastUploadDuplicates,
                response?.Value<long?>("duplicates") ?? 0L);
            Interlocked.Exchange(ref _lastUploadEphemeral,
                response?.Value<long?>("ephemeral") ?? 0L);
            Interlocked.Exchange(ref _lastUploadClientDurationMs,
                Math.Max(0L, clientDurationMs));
            Interlocked.Exchange(ref _lastUploadServerDurationMs,
                Math.Max(0L, response?.Value<long?>("durationMs") ?? 0L));
            JObject compaction = response?["compaction"] as JObject;
            Interlocked.Exchange(ref _lastCompactionDurationMs,
                Math.Max(0L, compaction?.Value<long?>("durationMs") ?? 0L));
            Interlocked.Exchange(ref _lastCompactedEvents,
                Math.Max(0L, compaction?.Value<long?>("compacted") ?? 0L));
            Interlocked.Exchange(ref _lastCompactionBacklog,
                Math.Max(0L, compaction?.Value<long?>("remainingBacklog") ?? 0L));
        }

        private static void RegisterUploadFailure()
        {
            int delaySeconds;
            lock (StateLock)
            {
                _consecutiveUploadFailures = Math.Min(8, _consecutiveUploadFailures + 1);
                delaySeconds = Math.Min(120, 5 * (1 << Math.Min(4, _consecutiveUploadFailures - 1)));
                _nextUploadAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            }
            ScheduleRetry(delaySeconds);
        }

        private static void ScheduleRetry(int delaySeconds)
        {
            if (Interlocked.CompareExchange(ref _retryScheduled, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delaySeconds))).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _retryScheduled, 0);
                    Kick();
                }
            });
        }

        private static string PendingPath(string timeline, string campaignOverride = null)
        {
            string campaign;
            lock (StateLock) campaign = campaignOverride ?? _campaignId;
            return Path.Combine(BasePath.Name, "Modules", "ReignBeta", "HistoryOutbox", campaign, SafeSegment(timeline, "main"), "pending.jsonl");
        }

        private static string SafeSegment(string value, string fallback)
        {
            string safe = new string((value ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }
    }
}
