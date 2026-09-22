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
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static async Task<JObject> OpenWorldHistoryTimelineAsync(string timelineId, long savedSequence, string savedHeadEventId, float completeFromDay)
        {
            return await PostJsonAsync("/world-history/timeline/open", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = timelineId ?? "main", ["savedSequence"] = savedSequence,
                ["savedHeadEventId"] = savedHeadEventId ?? string.Empty, ["historyCompleteFromWorldDay"] = completeFromDay,
                ["currentWorldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays
            }).ConfigureAwait(false);
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

        internal static async Task<JObject> CheckWorldHistoryLieAsync(string claim, Hero claimant, Hero target, float worldDay, string timelineId, string mode)
        {
            JObject payload = await ReignMainThread.InvokeAsync(() =>
            {
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
            return await PostJsonAsync("/world-history/lie-check", payload).ConfigureAwait(false);
        }

        internal static async Task<JObject> IngestWorldHistoryBatchAsync(string timelineId, float completeFromDay, JArray events)
        {
            return await PostJsonAsync("/world-history/ingest-batch", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["clientId"] = "reign_native_campaign", ["timelineId"] = timelineId ?? "main",
                ["historyCompleteFromWorldDay"] = completeFromDay, ["events"] = events ?? new JArray()
            }).ConfigureAwait(false);
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

        internal static void Initialize(string campaignId, string timelineId, float completeFromDay)
        {
            FlushRoutineBuckets(true);
            lock (StateLock)
            {
                _campaignId = SafeSegment(campaignId, "default");
                _timelineId = SafeSegment(timelineId, "main");
                _completeFromDay = completeFromDay;
            }
            Kick();
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
            }
            string eventType = envelope.Value<string>("eventType") ?? string.Empty;
            if (RoutineEventTypes.Contains(eventType))
            {
                envelope["retentionClass"] = "routine";
                CoalesceRoutineEvent(envelope);
                return;
            }
            Pending.Enqueue(envelope);
            Kick();
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
                string path;
                lock (StateLock) path = PendingPath(_timelineId);
                bool diskPending = HasPendingThroughSequenceOnDisk(
                    path, maximumSequence);
                bool memoryPending = HasPendingThroughSequenceInMemory(
                    maximumSequence);
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
            return Pending.Any(item =>
                ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(
                    item?.Value<long?>("sequence"), maximumSequence));
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
                        if (ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(
                                item.Value<long?>("sequence"), maximumSequence))
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
                _campaignId = campaign;
                _timelineId = timeline;
            }

            List<JObject> retained = new List<JObject>();
            while (Pending.TryDequeue(out JObject queued))
            {
                string queuedCampaign = queued.Value<string>("_reignCampaignId") ?? string.Empty;
                string queuedTimeline = queued.Value<string>("_reignTimelineId") ?? string.Empty;
                long sequence = queued.Value<long?>("sequence") ?? long.MaxValue;
                if (queuedCampaign.Equals(campaign, StringComparison.OrdinalIgnoreCase)
                    && queuedTimeline.Equals(timeline, StringComparison.OrdinalIgnoreCase)
                    && sequence <= maximumSequence) retained.Add(queued);
            }
            foreach (JObject item in retained.OrderBy(x => x.Value<long?>("sequence") ?? 0L)) Pending.Enqueue(item);

            string path = PendingPath(timeline);
            lock (FileLock)
            {
                if (!File.Exists(path)) return;
                string temp = path + ".save_sync_" + Guid.NewGuid().ToString("N");
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
                using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        try
                        {
                            JObject item = JObject.Parse(line);
                            if ((item.Value<long?>("sequence") ?? long.MaxValue) <= maximumSequence) writer.WriteLine(line);
                        }
                        catch { }
                    }
                }
                File.Replace(temp, path, null);
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
            try
            {
                if (Volatile.Read(ref _urgentFlush) == 0)
                    await Task.Delay(250).ConfigureAwait(false);
                while (true)
                {
                    string timeline;
                    float completeFrom;
                    lock (StateLock)
                    {
                        timeline = _timelineId;
                        completeFrom = _completeFromDay;
                    }
                    string path = PendingPath(timeline);
                    bool urgent = Volatile.Read(ref _urgentFlush) != 0;
                    int uploadBatchSize =
                        ReignWorldHistoryFlushPolicy.UploadBatchSize(urgent);
                    List<JObject> newlyQueued = new List<JObject>();
                    while (newlyQueued.Count < uploadBatchSize
                        && Pending.TryDequeue(out JObject item))
                    {
                        newlyQueued.Add(item);
                    }
                    if (newlyQueued.Count > 0)
                    {
                        Persist(path, newlyQueued);
                    }

                    if (!CanAttemptUpload())
                    {
                        break;
                    }

                    long maximumSequence = Volatile.Read(
                        ref _activeSaveBoundarySequence);
                    List<JObject> persisted = ReadPendingBatch(path,
                        uploadBatchSize, maximumSequence);
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
                            List<JObject> coalesced = new List<JObject>();
                            while (stagedCount + coalesced.Count
                                    < uploadBatchSize
                                && Pending.TryDequeue(out JObject next))
                            {
                                coalesced.Add(next);
                            }
                            if (coalesced.Count > 0)
                            {
                                Persist(path, coalesced);
                                stagedCount += coalesced.Count;
                            }
                        }
                        coalesceTimer.Stop();
                        urgent = Volatile.Read(ref _urgentFlush) != 0;
                        uploadBatchSize = ReignWorldHistoryFlushPolicy
                            .UploadBatchSize(urgent);
                        maximumSequence = Volatile.Read(
                            ref _activeSaveBoundarySequence);
                        persisted = ReadPendingBatch(path, uploadBatchSize,
                            maximumSequence);
                    }
                    if (persisted.Count == 0)
                    {
                        if (!HasPendingThroughSequenceInMemory(maximumSequence))
                        {
                            break;
                        }
                        continue;
                    }

                    try
                    {
                        Stopwatch uploadTimer = Stopwatch.StartNew();
                        JObject response = await ReignServerClient.IngestWorldHistoryBatchAsync(timeline, completeFrom, new JArray(persisted)).ConfigureAwait(false);
                        uploadTimer.Stop();
                        if (response?.Value<bool?>("ok") != true)
                        {
                            RegisterUploadFailure();
                            break;
                        }
                        RegisterUploadSuccess();
                        RecordUploadTelemetry(response, persisted.Count,
                            uploadTimer.ElapsedMilliseconds);
                        RemoveAcknowledged(path, persisted.Select(x => x.Value<string>("eventId")).Where(x => !string.IsNullOrWhiteSpace(x)));
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
            }
            finally
            {
                Interlocked.Exchange(ref _workerRunning, 0);
                if (!Pending.IsEmpty)
                {
                    Kick();
                }
            }
        }

        private static void Persist(string path, IEnumerable<JObject> events)
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder builder = new StringBuilder();
                foreach (JObject item in events)
                {
                    builder.AppendLine(item.ToString(Formatting.None));
                }
                File.AppendAllText(path, builder.ToString(), new UTF8Encoding(false));
                TrimOutboxIfNeeded(path);
            }
        }

        private static void TrimOutboxIfNeeded(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaximumOutboxBytes) return;

            string temp = path + ".bounded_" + Guid.NewGuid().ToString("N");
            long start = Math.Max(0L, info.Length - RetainedOutboxBytes);
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(input, Encoding.UTF8, true))
            using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
            {
                input.Seek(start, SeekOrigin.Begin);
                if (start > 0L) reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null) writer.WriteLine(line);
            }
            File.Replace(temp, path, null);
            ReignLog.Warn("World-history outbox exceeded 32 MB; oldest deferred events were discarded and the newest 16 MB retained.");
        }

        private static List<JObject> ReadPendingBatch(
            string path,
            int limit,
            long maximumSequence)
        {
            lock (FileLock)
            {
                if (!File.Exists(path)) return new List<JObject>();
                Dictionary<string, JObject> unique = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadLines(path))
                {
                    try
                    {
                        JObject item = JObject.Parse(line);
                        if (!ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(
                                item.Value<long?>("sequence"), maximumSequence))
                            continue;
                        string id = item.Value<string>("eventId");
                        if (!string.IsNullOrWhiteSpace(id)) unique[id] = item;
                    }
                    catch { }

                    if (unique.Count >= Math.Max(1, limit))
                    {
                        break;
                    }
                }
                return unique.Values.OrderBy(x => x.Value<long?>("sequence") ?? 0L).ToList();
            }
        }

        private static void RemoveAcknowledged(string path, IEnumerable<string> ids)
        {
            HashSet<string> acknowledged = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            lock (FileLock)
            {
                if (!File.Exists(path)) return;
                string temp = path + ".tmp";
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
                using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        try
                        {
                            JObject item = JObject.Parse(line);
                            if (!acknowledged.Contains(item.Value<string>("eventId") ?? string.Empty)) writer.WriteLine(line);
                        }
                        catch { }
                    }
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
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

        private static string PendingPath(string timeline)
        {
            string campaign;
            lock (StateLock) campaign = _campaignId;
            return Path.Combine(BasePath.Name, "Modules", "ReignBeta", "HistoryOutbox", campaign, SafeSegment(timeline, "main"), "pending.jsonl");
        }

        private static string SafeSegment(string value, string fallback)
        {
            string safe = new string((value ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }
    }
}
