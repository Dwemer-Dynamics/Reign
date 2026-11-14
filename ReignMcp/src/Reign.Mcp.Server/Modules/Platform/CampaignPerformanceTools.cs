using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reign.Core.Contracts.Platform;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    [McpServerTool(Name = "reign_monitor_campaign_performance", ReadOnly = true,
        Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Records bounded cached native timing windows and queue trends for an enrolled disposable Current. Never requests a native snapshot, advances, pauses, saves, changes settings or dispatches providers. Writes run-owned observation evidence; does not certify overall performance by itself.")]
    public static async Task<object> MonitorCampaignPerformance(ReignMcpOptions options,
        ReignApiClient api, CampaignTestService campaignTests, string campaignId, string campaignTestRunId,
        int durationSeconds = 120, int pollSeconds = 5,
        [AllowedValues("provider_free", "observe_configured")]
        [Description("provider_free retains the clean tripwire requirement. observe_configured records an already authorized real workload with healthy unarmed guard and stable counters. Observation grants no provider or budget authority.")]
        string providerPolicy = "provider_free", CancellationToken cancellationToken = default)
    {
        if (durationSeconds < 30 || durationSeconds > 3600) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (pollSeconds < 2 || pollSeconds > 30) throw new ArgumentOutOfRangeException(nameof(pollSeconds));
        var providerObservation = new CampaignPerformanceProviderObservation(providerPolicy);
        var authorization = campaignTests.RequireEnrollment(campaignTestRunId, campaignId);
        string root = Path.Combine(options.BuildRoot, "performance-monitor");
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Redirected monitor storage is unsupported.");
        string directory = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string samplesPath = Path.Combine(directory, "observations.jsonl");
        string reportPath = Path.Combine(directory, "report.json");
        var summary = new CampaignPerformanceSummary();
        var timer = Stopwatch.StartNew();
        DateTime startedUtc = DateTime.UtcNow;
        string instance = "", module = "";
        string? queueGeneration = null, previousReadinessObservation = null;
        long watermark = 0;
        long initialInvalid = 0, initialUnknown = 0;
        int initialSegment = 0;
        int polls = 0;
        bool cancelled = false;
        bool completedNormally = false;
        double startCutoffMs = 0;
        double finalTailMs = double.PositiveInfinity;
        double coveredRequestedMs = 0;
        double lastWindowEndMs = double.NaN;
        var faults = new HashSet<string>(StringComparer.Ordinal);
        var phases = new Dictionary<string, NativePhaseTiming>(StringComparer.Ordinal);
        var query = new Dictionary<string, string?> { ["campaignId"] = authorization.CampaignId };
        var queueProbes = new[] {
            new CampaignPerformanceQueueProbe("world", "/world-test/overview"),
            new CampaignPerformanceQueueProbe("background", "/api/background/status"),
            new CampaignPerformanceQueueProbe("memory", "/memory/background/status"),
            new CampaignPerformanceQueueProbe("provider", "/api/provider/status"),
            new CampaignPerformanceQueueProbe("gossip", "/gossip/status") };
        object? report = null;
        await using var output = new StreamWriter(new FileStream(samplesPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        await output.WriteLineAsync(JsonSerializer.Serialize(new { schema = "reign-campaign-performance-observations-v1", startedUtc,
            authorization.CampaignTestRunId, authorization.CampaignId, authorization.TimelineId, authorization.CurrentSaveName,
            durationSeconds, pollSeconds, providerPolicy, observer = "cached native runtime and fresh queue readiness GETs; no native commands", mcpAssembly = typeof(TestingTools).Assembly.Location })).ConfigureAwait(false);
        try
        {
            while (true)
            {
                bool finalPoll = timer.Elapsed.TotalSeconds >= durationSeconds;
                double pollStart = timer.Elapsed.TotalMilliseconds;
                ApiEnvelope runtimeReply = await api.GetAsync("/tests/live/runtime", query, cancellationToken).ConfigureAwait(false);
                if (!runtimeReply.Ok || runtimeReply.Data is not JsonElement body) { faults.Add("runtime_unavailable"); break; }
                string? scopeError = CampaignPerformanceEvidence.RuntimeScopeError(body, authorization, DateTime.UtcNow);
                if (scopeError != null) { faults.Add(scopeError); break; }
                JsonElement runtime = body.GetProperty("runtime");
                JsonElement performance = runtime.GetProperty("performance");
                string? shapeError = CampaignPerformanceEvidence.SnapshotShapeError(performance);
                if (shapeError != null) { faults.Add(shapeError); break; }
                var snapshot = performance.Deserialize<NativePerformanceSnapshot>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                string observedInstance = runtime.GetProperty("gameInstanceId").GetString() ?? "";
                string observedModule = performance.GetProperty("clientModuleMvid").GetString() ?? "";
                if (snapshot == null || snapshot.Windows == null || snapshot.Windows.Length > 120 || snapshot.Windows.Any(x => x == null)) { faults.Add("invalid_snapshot_shape"); break; }
                if (polls == 0) { instance = observedInstance; module = observedModule; watermark = snapshot.LatestSequence;
                    initialInvalid = snapshot.InvalidSamples; initialUnknown = snapshot.UnknownPhaseObservations; initialSegment = snapshot.Segment;
                    var captured = DateTimeOffset.Parse(performance.GetProperty("snapshotCapturedUtc").GetString()!);
                    startCutoffMs = snapshot.CapturedMonotonicMs + (startedUtc - captured.UtcDateTime).TotalMilliseconds; }
                else if (instance != observedInstance || module != observedModule) { faults.Add("native_identity_changed"); break; }
                else if (initialSegment != snapshot.Segment || initialInvalid != snapshot.InvalidSamples || initialUnknown != snapshot.UnknownPhaseObservations)
                { faults.Add("sampling_scope_changed_or_invalid"); break; }
                var fresh = new List<NativePerformanceWindow>();
                bool frameFault = false;
                if (polls > 0)
                {
                    if (snapshot.LatestSequence < watermark) { faults.Add("sequence_regressed"); break; }
                    foreach (NativePerformanceWindow window in snapshot.Windows)
                    {
                        if (window.Sequence <= watermark) continue;
                        string? error = CampaignPerformanceEvidence.WindowError(window);
                        if (error != null) { faults.Add(error); frameFault = true; break; }
                        if (window.Sequence > watermark + 1) { summary.SequenceGaps += window.Sequence - watermark - 1; faults.Add("sequence_gap"); frameFault = true; break; }
                        watermark = window.Sequence;
                        if (window.Segment != initialSegment) { faults.Add("window_segment_changed"); frameFault = true; break; }
                        if (double.IsFinite(lastWindowEndMs) && Math.Abs(lastWindowEndMs - window.StartMonotonicMs) > 0.1) { faults.Add("window_time_gap_or_overlap"); frameFault = true; break; }
                        lastWindowEndMs = window.EndMonotonicMs;
                        if (window.EndMonotonicMs <= startCutoffMs || window.StartMonotonicMs >= startCutoffMs + durationSeconds * 1000d) continue;
                        coveredRequestedMs += Math.Max(0, Math.Min(window.EndMonotonicMs, startCutoffMs + durationSeconds * 1000d) - Math.Max(window.StartMonotonicMs, startCutoffMs));
                        summary.Add(window);
                        fresh.Add(window);
                        foreach (var phase in window.Phases)
                        {
                            if (!phases.TryGetValue(phase.Name, out var aggregate)) phases[phase.Name] = aggregate = new NativePhaseTiming { Name = phase.Name };
                            aggregate.Count += phase.Count; aggregate.TotalMs += phase.TotalMs; aggregate.MaximumMs = Math.Max(aggregate.MaximumMs, phase.MaximumMs);
                        }
                    }
                }
                if (frameFault) break;
                finalTailMs = snapshot.Windows.Length == 0 ? double.PositiveInfinity
                    : Math.Max(0, snapshot.CapturedMonotonicMs - snapshot.Windows[^1].EndMonotonicMs);
                // Failed sources are disabled for the rest of this run: client
                // cancellation cannot guarantee the server abandoned its work.
                CampaignPerformanceQueueObservation[] queues = await Task.WhenAll(
                    queueProbes[0].ObserveAsync(api, new Dictionary<string, string?> { ["campaignId"] = authorization.CampaignId, ["timelineId"] = authorization.TimelineId, ["view"] = "readiness" }, cancellationToken),
                    queueProbes[1].ObserveAsync(api, null, cancellationToken),
                    queueProbes[2].ObserveAsync(api, query, cancellationToken),
                    queueProbes[3].ObserveAsync(api, null, cancellationToken),
                    queueProbes[4].ObserveAsync(api, new Dictionary<string, string?> { ["campaignId"] = authorization.CampaignId, ["timelineId"] = authorization.TimelineId,
                        ["deliveryOnly"] = "true", ["worldDay"] = runtime.GetProperty("worldDay").GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken)).ConfigureAwait(false);
                bool criticalQueueFault = false;
                foreach (var queue in queues)
                    if (!queue.Ok) faults.Add("queue_observation_unavailable:" + queue.Source + ":" + queue.ErrorCode);
                var counters = new Dictionary<string, object?>();
                var nativeCounters = new Dictionary<string, object?>();
                foreach (string field in new[] { "relationshipClient", "worldHistory", "diplomacy", "government", "rulers", "clanAccords", "personalGossip", "wandererPopulation", "tavernHousePopulation", "diagnosticSections", "httpTransport", "mainThreadDispatch", "courtLife", "nativeSave", "processMemory" })
                    if (runtime.TryGetProperty(field, out var value)) CampaignPerformanceEvidence.ProjectCounters(value, "native." + field, nativeCounters);
                foreach (var counter in nativeCounters) counters[counter.Key == "projectionTruncated" ? "native.projectionTruncated" : counter.Key] = counter.Value;
                for (int i = 0; i < queues.Length; i++)
                    if (queues[i].Ok && queues[i].Reply?.Data is JsonElement data)
                    {
                        string source = queueProbes[i].Source;
                        if (source is "world" or "gossip")
                        {
                            string? queueScopeError = CampaignPerformanceEvidence.QueueScopeError(data, source, authorization);
                            if (queueScopeError != null)
                            { faults.Add(queueScopeError); queueProbes[i].Reject(queueScopeError); queues[i] = queues[i] with { Ok = false, ErrorCode = queueScopeError }; continue; }
                            if (source == "world")
                            {
                                string generation = data.GetProperty("campaignGeneration").GetString()!;
                                string observationId = data.GetProperty("observationId").GetString()!;
                                if (queueGeneration != null && generation != queueGeneration)
                                { faults.Add("queue_generation_changed"); criticalQueueFault = true; continue; }
                                if (observationId == previousReadinessObservation)
                                { faults.Add("queue_readiness_reused"); queueProbes[i].Reject("queue_readiness_reused"); queues[i] = queues[i] with { Ok = false, ErrorCode = "queue_readiness_reused" }; continue; }
                                queueGeneration = generation; previousReadinessObservation = observationId;
                            }
                        }
                        var projected = new Dictionary<string, object?>();
                        CampaignPerformanceEvidence.ProjectCounters(data, source, projected);
                        if (source == "provider" && data.TryGetProperty("providers", out var providers) && providers.ValueKind == JsonValueKind.Array)
                        {
                            int index = 0;
                            foreach (var providerCounters in providers.EnumerateArray())
                            {
                                if (index == 32) { projected["projectionTruncated"] = true; break; }
                                CampaignPerformanceEvidence.ProjectCounters(providerCounters, source + ".providers." + index++, projected);
                            }
                        }
                        foreach (var counter in projected) counters[counter.Key == "projectionTruncated" ? source + ".projectionTruncated" : counter.Key] = counter.Value;
                    }
                if (queues[3].Ok && queues[3].Reply?.Data is JsonElement provider)
                {
                    string? providerError = providerObservation.Observe(provider);
                    if (providerError != null) { faults.Add(providerError); criticalQueueFault = true; }
                }
                else faults.Add("provider_isolation_unobserved");
                await output.WriteLineAsync(JsonSerializer.Serialize(new { elapsedMs = timer.Elapsed.TotalMilliseconds, observedUtc = DateTime.UtcNow,
                    nativeCapturedUtc = runtime.GetProperty("capturedUtc"), worldDay = runtime.GetProperty("worldDay"),
                    snapshot.LatestSequence, snapshot.OldestSequence, snapshot.InvalidSamples, snapshot.UnknownPhaseObservations,
                    snapshot.CapturedMonotonicMs, snapshotCapturedUtc = performance.GetProperty("snapshotCapturedUtc"),
                    windows = fresh, queueCounters = counters, queueObservations = queues.Select(x => new { x.Source, x.Attempted, x.Ok, x.ErrorCode, x.DurationMs, statusCode = x.Reply?.StatusCode }),
                    windowDelivery = fresh.Select(x => new { x.Sequence, nativeSnapshotLagMs = Math.Max(0, snapshot.CapturedMonotonicMs - x.EndMonotonicMs) }),
                    timingDelivery = "sequence backfill from native ring; window end timestamps identify delayed delivery", pollDurationMs = timer.Elapsed.TotalMilliseconds - pollStart,
                    errors = faults.ToArray() })).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                polls++;
                if (criticalQueueFault) break;
                if (finalPoll && lastWindowEndMs >= startCutoffMs + durationSeconds * 1000d) { completedNormally = true; break; }
                if (finalPoll && timer.Elapsed.TotalSeconds >= durationSeconds + 15) { faults.Add("terminal_boundary_unobserved"); break; }
                double remaining = Math.Min((finalPoll ? Math.Min(pollSeconds, 2) : pollSeconds) * 1000d - (timer.Elapsed.TotalMilliseconds - pollStart),
                    (durationSeconds + (finalPoll ? 15 : 0)) * 1000d - timer.Elapsed.TotalMilliseconds);
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled = true; faults.Add("observation_cancelled"); }
        catch (Exception exception) { faults.Add("observer_exception:" + exception.GetType().Name); }
        finally
        {
            double coverageRatio = coveredRequestedMs / (durationSeconds * 1000d);
            double foregroundRatio = summary.Intervals == 0 ? 0 : summary.ForegroundIntervals / (double)summary.Intervals;
            if (coverageRatio < 0.9 || foregroundRatio < 0.95 || !double.IsFinite(finalTailMs) || finalTailMs > 15000) faults.Add("insufficient_coverage");
            report = new { schema = "reign-campaign-performance-report-v1", ok = CampaignPerformanceEvidence.CollectionValid(completedNormally, faults.Count,
                    summary.Windows, summary.SequenceGaps, coverageRatio, foregroundRatio, finalTailMs),
                assessment = "OBSERVATIONS_ONLY", completedUtc = DateTime.UtcNow, elapsedSeconds = timer.Elapsed.TotalSeconds,
                completedNormally, coverageRatio, foregroundRatio, finalWindowTailMs = double.IsFinite(finalTailMs) ? (double?)finalTailMs : null,
                boundaryPolicy = "whole windows intersecting requested span; coverage clips to requested span; up to15s cached closing-boundary grace",
                authorization.CampaignTestRunId, authorization.CampaignId, authorization.TimelineId, authorization.CurrentSaveName,
                gameInstanceId = instance, clientModuleMvid = module, queueGeneration, polls, cancelled, summary, phases = phases.Values.ToArray(), faults,
                providerObservation = providerObservation.Snapshot(),
                queueSources = queueProbes.Select(x => x.Status()).ToArray(),
                samplesPath, reportPath, limits = "No overall PASS: evaluate against criteria frozen before holdout. Queue counters include future/cumulative work; eligibility and service-rate interpretation remain required. AppTick intervals include native/render waits and do not measure GPU presents. GC counts are not pause durations." };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None).ConfigureAwait(false);
        }
        return report!;
    }
}

internal sealed record CampaignPerformanceQueueObservation(string Source, bool Attempted, bool Ok,
    string? ErrorCode, double DurationMs, ApiEnvelope? Reply);

internal sealed class CampaignPerformanceQueueProbe
{
    public string Source { get; }
    private readonly string path;
    private readonly TimeSpan deadline;
    private string? failure;
    private int attempts, successful, skipped, cancelled;
    private DateTime? failedUtc, lastSkippedUtc;
    internal CampaignPerformanceQueueProbe(string source, string path, TimeSpan? requestDeadline = null)
    {
        Source = source; this.path = path; deadline = requestDeadline ?? TimeSpan.FromSeconds(4);
        if (deadline <= TimeSpan.Zero || deadline > TimeSpan.FromSeconds(4)) throw new ArgumentOutOfRangeException(nameof(requestDeadline));
    }
    internal void Reject(string reason)
    {
        failure ??= reason; failedUtc ??= DateTime.UtcNow;
    }
    internal async Task<CampaignPerformanceQueueObservation> ObserveAsync(ReignApiClient api,
        IReadOnlyDictionary<string, string?>? query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (failure != null) { skipped++; lastSkippedUtc = DateTime.UtcNow; return new(Source, false, false, failure, 0, null); }
        attempts++;
        var timer = Stopwatch.StartNew();
        using var local = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        local.CancelAfter(deadline);
        ApiEnvelope? reply = null;
        string? error = null;
        try
        {
            reply = await api.GetAsync(path, query, local.Token).ConfigureAwait(false);
            if (!reply.Ok) error = reply.StatusCode.HasValue ? "http_" + reply.StatusCode.Value : "transport_unavailable";
            else if (reply.Data is not JsonElement body || body.ValueKind != JsonValueKind.Object) error = "invalid_body";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = "request_deadline"; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled++; throw; }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { error = "transport_exception"; }
        if (error != null) Reject(error); else successful++;
        return new(Source, true, error == null, error, timer.Elapsed.TotalMilliseconds, reply);
    }
    internal object Status() => new { source = Source, endpoint = path, attempts, successfulHttpReads = successful, failed = failure == null ? 0 : 1,
        skipped, cancelled, firstFailureUtc = failedUtc, lastUnavailableUtc = lastSkippedUtc ?? failedUtc, errorCode = failure,
        disabledAfterFailure = failure != null, requestDeadlineMs = deadline.TotalMilliseconds };
}

internal sealed class CampaignPerformanceSummary
{
    public long Windows { get; private set; }
    public long SequenceGaps { get; set; }
    public long Intervals { get; private set; }
    public long ForegroundIntervals { get; private set; }
    public double CoveredMs { get; private set; }
    public double MaximumIntervalMs { get; private set; }
    public long[] Buckets { get; } = new long[7];
    public void Add(NativePerformanceWindow window)
    {
        Windows++; Intervals += window.IntervalCount; ForegroundIntervals += window.ForegroundIntervals;
        CoveredMs += window.IntervalSumMs; MaximumIntervalMs = Math.Max(MaximumIntervalMs, window.MaximumIntervalMs);
        for (int i = 0; i < Buckets.Length; i++) Buckets[i] += window.Buckets[i];
    }
}

internal static class CampaignPerformanceEvidence
{
    internal static string? QueueScopeError(JsonElement data, string source, CampaignTestAuthorization expected)
    {
        string schema = source == "world" ? "reign_world_readiness_v1" : "reign-gossip-delivery-readiness-v1";
        if (Text(data, "schema") != schema || Text(data, "campaignId") != expected.CampaignId || Text(data, "timelineId") != expected.TimelineId)
            return "queue_scope_mismatch:" + source;
        if (source == "world" && (string.IsNullOrEmpty(Text(data, "campaignGeneration")) || !Guid.TryParse(Text(data, "observationId"), out _)
            || !DateTimeOffset.TryParse(Text(data, "observedUtc"), out var observed)
            || DateTime.UtcNow - observed.UtcDateTime > TimeSpan.FromSeconds(15) || observed.UtcDateTime - DateTime.UtcNow > TimeSpan.FromSeconds(2)))
            return "queue_readiness_stale_or_missing";
        return null;
    }
    internal static string? SnapshotShapeError(JsonElement value)
    {
        if (!Numbers(value, "segment", "capturedMonotonicMs", "targetWindowMs", "oldestSequence", "latestSequence", "ringCapacity",
                "ringOverwrites", "missingRequestedWindows", "invalidSamples", "unknownPhaseObservations")
            || !value.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Array || windows.GetArrayLength() > 120)
            return "invalid_snapshot_shape";
        long previous = 0;
        foreach (var window in windows.EnumerateArray())
        {
            if (!Numbers(window, "sequence", "segment", "startMonotonicMs", "endMonotonicMs", "intervalCount", "intervalSumMs",
                    "maximumIntervalMs", "foregroundIntervals", "gen0Collections", "gen1Collections", "gen2Collections")
                || !window.TryGetProperty("gcCountersReset", out var reset) || reset.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !window.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array
                || !window.TryGetProperty("phases", out var phases) || phases.ValueKind != JsonValueKind.Array)
                return "invalid_window_shape";
            if (!window.GetProperty("sequence").TryGetInt64(out long sequence) || sequence <= previous) return "window_sequence_not_ascending";
            previous = sequence;
            foreach (var phase in phases.EnumerateArray())
                if (!Numbers(phase, "count", "totalMs", "maximumMs") || string.IsNullOrEmpty(Text(phase, "name"))) return "invalid_phase";
        }
        return null;
    }

    private static bool Numbers(JsonElement value, params string[] names) => value.ValueKind == JsonValueKind.Object
        && names.All(name => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number
            && field.TryGetDouble(out double number) && double.IsFinite(number) && number >= 0);

    private static readonly string[] CounterTerms = { "pending", "queue", "outbox", "backlog", "lag", "fail", "missing", "dirty", "running", "completed", "processed", "oldest", "day", "bytes", "collection", "dispatch", "cleanprovider", "inflight", "error", "status", "enabled", "count", "sequence", "requests", "successes", "active", "waiting", "refresh", "due", "transit", "clock", "blocked", "duration", "wait", "known", "chars", "thread" };
    internal static bool CollectionValid(bool completedNormally, int faults, long windows, long gaps, double coverage, double foreground, double tailMs) =>
        completedNormally && faults == 0 && windows > 0 && gaps == 0 && double.IsFinite(coverage) && coverage >= 0.9 && coverage <= 1.000001
        && double.IsFinite(foreground) && foreground >= 0.95 && foreground <= 1 && double.IsFinite(tailMs) && tailMs >= 0 && tailMs <= 15000;
    internal static string? RuntimeScopeError(JsonElement body, CampaignTestAuthorization expected, DateTime now)
    {
        if (!body.TryGetProperty("gameOnline", out var online) || online.ValueKind != JsonValueKind.True) return "game_offline";
        if (!body.TryGetProperty("armed", out var armed) || armed.ValueKind != JsonValueKind.True) return "bridge_not_armed";
        if (!body.TryGetProperty("runtime", out var runtime) || runtime.ValueKind != JsonValueKind.Object) return "runtime_missing";
        if (Text(runtime, "campaignId") != expected.CampaignId || Text(runtime, "activeSaveName") != expected.CurrentSaveName
            || !runtime.TryGetProperty("saveSync", out var save) || Text(save, "timelineId") != expected.TimelineId) return "foreign_native_scope";
        if (!save.TryGetProperty("ready", out var ready) || ready.ValueKind != JsonValueKind.True
            || !save.TryGetProperty("alignmentPending", out var alignment) || alignment.ValueKind != JsonValueKind.False) return "save_sync_unaligned";
        if (!DateTimeOffset.TryParse(Text(runtime, "capturedUtc"), out var captured) || now.ToUniversalTime() - captured.UtcDateTime > TimeSpan.FromSeconds(15)
            || captured.UtcDateTime - now.ToUniversalTime() > TimeSpan.FromSeconds(2)) return "stale_runtime";
        if (!runtime.TryGetProperty("performance", out var performance) || Text(performance, "schema") != NativePerformanceWindows.Schema
            || !performance.TryGetProperty("samplingEnabled", out var enabled) || enabled.ValueKind != JsonValueKind.True) return "performance_sampling_unavailable";
        if (!DateTimeOffset.TryParse(Text(performance, "snapshotCapturedUtc"), out var snapshotTime)
            || now.ToUniversalTime() - snapshotTime.UtcDateTime > TimeSpan.FromSeconds(15)
            || snapshotTime.UtcDateTime - now.ToUniversalTime() > TimeSpan.FromSeconds(2)) return "stale_performance_publication";
        if (string.IsNullOrEmpty(Text(runtime, "gameInstanceId")) || !Guid.TryParse(Text(performance, "clientModuleMvid"), out _)) return "native_identity_missing";
        return null;
    }

    internal static string? WindowError(NativePerformanceWindow window)
    {
        if (window.Sequence <= 0 || window.IntervalCount <= 0 || window.Buckets == null || window.Phases == null || window.Buckets.Length != 7 || window.Phases.Length > 8
            || window.ForegroundIntervals < 0 || window.ForegroundIntervals > window.IntervalCount) return "invalid_window_shape";
        if (!double.IsFinite(window.StartMonotonicMs) || !double.IsFinite(window.EndMonotonicMs) || window.StartMonotonicMs < 0
            || window.EndMonotonicMs < window.StartMonotonicMs || !double.IsFinite(window.IntervalSumMs) || !double.IsFinite(window.MaximumIntervalMs)
            || window.MaximumIntervalMs < 0 || window.MaximumIntervalMs > window.IntervalSumMs
            || Math.Abs(window.IntervalSumMs - (window.EndMonotonicMs - window.StartMonotonicMs)) > 0.1) return "invalid_window_time";
        long sum = 0;
        foreach (long bucket in window.Buckets) { if (bucket < 0 || bucket > window.IntervalCount - sum) return "invalid_histogram"; sum += bucket; }
        if (sum != window.IntervalCount) return "invalid_histogram";
        if (window.GcCountersReset) return "gc_counter_reset";
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phase in window.Phases)
            if (phase == null || string.IsNullOrWhiteSpace(phase.Name) || phase.Name.Length > 64 || !names.Add(phase.Name) || phase.Count < 0
                || !double.IsFinite(phase.TotalMs) || !double.IsFinite(phase.MaximumMs) || phase.MaximumMs < 0 || phase.MaximumMs > phase.TotalMs
                || phase.Count == 0 && phase.TotalMs != 0) return "invalid_phase";
        return null;
    }

    // A bounded scalar projection retains queue trends without copying NPC profiles, narrative, credentials or job bodies.
    internal static void ProjectCounters(JsonElement value, string prefix, IDictionary<string, object?> output, int depth = 0)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        if (depth > 7 || output.Count >= 512) { output["projectionTruncated"] = true; return; }
        foreach (var property in value.EnumerateObject())
        {
            if (output.Count >= 512) { output["projectionTruncated"] = true; return; }
            string path = prefix + "." + property.Name;
            if (property.Name.Length > 64 || path.Length > 256) { output["projectionTruncated"] = true; continue; }
            if (property.Value.ValueKind == JsonValueKind.Object) { ProjectCounters(property.Value, path, output, depth + 1); continue; }
            string name = property.Name.ToLowerInvariant();
            if (!CounterTerms.Any(name.Contains)) continue;
            if (property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) output[path] = property.Value.Clone();
            else if (property.Value.ValueKind == JsonValueKind.String && name.Contains("error", StringComparison.Ordinal))
                output[path + ".present"] = !string.IsNullOrEmpty(property.Value.GetString());
            else if (property.Value.ValueKind == JsonValueKind.String && (name.EndsWith("utc", StringComparison.Ordinal) || name.Contains("status")))
            { string text = property.Value.GetString() ?? ""; if (text.Length <= 64) output[path] = text; }
        }
    }

    private static string Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
}


// Read-only evidence policy. This never enables, releases or budgets providers.
// BeforeDispatch counters include uploads/downloads and are not billable-call totals.
internal sealed class CampaignPerformanceProviderObservation
{
    internal const string ProviderFree = "provider_free", ObserveConfigured = "observe_configured";
    internal string Policy { get; }
    private string? instance;
    private long? initialAllowed, lastAllowed;
    private int observations;

    internal CampaignPerformanceProviderObservation(string policy)
    {
        if (policy is not (ProviderFree or ObserveConfigured))
            throw new ArgumentException("Unsupported provider observation policy.", nameof(policy));
        Policy = policy;
    }

    internal string? Observe(JsonElement provider)
    {
        if (provider.ValueKind != JsonValueKind.Object
            || !provider.TryGetProperty("nativeProviderTestGuard", out var guard) || guard.ValueKind != JsonValueKind.Object)
            return "provider_isolation_missing";
        if (Policy == ProviderFree)
        {
            if (!guard.TryGetProperty("cleanProviderFreeRun", out var clean) || clean.ValueKind != JsonValueKind.True)
                return "provider_isolation_failed";
            observations++;
            return null;
        }
        if (!guard.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String
            || schema.GetString() != "reign-native-provider-free-guard-v1"
            || !guard.TryGetProperty("armed", out var armed) || armed.ValueKind != JsonValueKind.False
            || !guard.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.String || error.GetString() != ""
            || !guard.TryGetProperty("blockedAttemptsSinceProcessStart", out var blocked) || blocked.ValueKind != JsonValueKind.Number
            || !blocked.TryGetInt64(out long blockedCount) || blockedCount != 0
            || !guard.TryGetProperty("allowedDispatchesSinceProcessStart", out var allowed) || allowed.ValueKind != JsonValueKind.Number
            || !allowed.TryGetInt64(out long allowedCount) || allowedCount < 0
            || !guard.TryGetProperty("serverInstance", out var identity) || identity.ValueKind != JsonValueKind.String
            || !Guid.TryParse(identity.GetString(), out var parsedIdentity) || parsedIdentity == Guid.Empty)
            return "provider_observation_invalid";
        string observed = parsedIdentity.ToString("N");
        if (instance != null && instance != observed) return "provider_instance_changed";
        if (lastAllowed.HasValue && allowedCount < lastAllowed.Value) return "provider_counter_regressed";
        instance = observed;
        initialAllowed ??= allowedCount;
        lastAllowed = allowedCount;
        observations++;
        return null;
    }

    internal object Snapshot() => new { policy = Policy, providerFreeRequired = Policy == ProviderFree,
        observations, serverInstance = instance, initialAllowedDispatches = initialAllowed, lastAllowedDispatches = lastAllowed,
        allowedDispatchDeltaSinceFirstObservation = lastAllowed - initialAllowed,
        limits = "Process-wide dispatch observations, not billable calls, spend accounting or provider authorization. No marker, setting or provider is changed." };
}
