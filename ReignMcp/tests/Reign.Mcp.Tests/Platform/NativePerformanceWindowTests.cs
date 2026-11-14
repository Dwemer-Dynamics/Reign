using System.Text.Json;
using Reign.Core.Contracts.Platform;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class NativePerformanceWindowTests
{
    [Fact]
    public void DirectPublicationPreservesEveryWindowAndUsesLessThanHalfTheDomAllocations()
    {
        var phases=Enumerable.Range(0,8).Select(i=>"phase"+i).ToArray();
        var recorder=new NativePerformanceWindows(phases);
        for(int i=0;i<=121;i++) { recorder.Observe(true,i*1000, i,i/3,i/40,true); recorder.RecordPhase(phases[i%8],i/10d); }
        var snapshot=recorder.Snapshot(121000);
        snapshot.SnapshotCapturedUtc="2026-11-08T10:00:00.0000000Z";
        snapshot.ClientModuleMvid=Guid.NewGuid().ToString("D"); snapshot.Measurement="fixture measurement";
        var settings=new Newtonsoft.Json.JsonSerializerSettings { ContractResolver=new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
        string Direct()=>Newtonsoft.Json.JsonConvert.SerializeObject(snapshot,Newtonsoft.Json.Formatting.None,settings);
        string Dom()=>Newtonsoft.Json.Linq.JObject.FromObject(snapshot,Newtonsoft.Json.JsonSerializer.Create(settings)).ToString(Newtonsoft.Json.Formatting.None);
        Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JToken.Parse(Dom()),Newtonsoft.Json.Linq.JToken.Parse(Direct())));
        for(int i=0;i<16;i++) { Direct(); Dom(); }
        long Allocate(Func<string> serialize)
        {
            long started=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<8;i++) GC.KeepAlive(serialize());
            return GC.GetAllocatedBytesForCurrentThread()-started;
        }
        long direct=Allocate(Direct),dom=Allocate(Dom);
        Assert.True(direct*2<dom,$"Direct publication allocated {direct} bytes versus DOM {dom}.");
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(7.999, 0)] [InlineData(8, 1)] [InlineData(15.999, 1)]
    [InlineData(16, 2)] [InlineData(32.999, 2)] [InlineData(33, 3)] [InlineData(50, 4)]
    [InlineData(99.999, 4)] [InlineData(100, 5)] [InlineData(249.999, 5)] [InlineData(250, 6)]
    public void HistogramHasExplicitLowerInclusiveBoundaries(double value, int bucket) => Assert.Equal(bucket, NativePerformanceWindows.Bucket(value));

    [Fact]
    public void WindowsResetMaximaAndBackfillExactlyOnceAfterSkippedPolls()
    {
        var recorder = new NativePerformanceWindows(new[] { "capture" }, capacity: 4, targetWindowMs: 100);
        recorder.Observe(true, 0, 10, 3, 1, true);
        recorder.RecordPhase("capture", 75);
        recorder.Observe(true, 100, 11, 3, 1, true);
        for (int i = 11; i <= 20; i++) recorder.Observe(true, i * 10, 11, 3, 1, true);
        var snapshot = recorder.Snapshot(200);
        Assert.Equal(2, snapshot.Windows.Length);
        Assert.Equal(100, snapshot.Windows[0].MaximumIntervalMs);
        Assert.Equal(10, snapshot.Windows[1].MaximumIntervalMs);
        Assert.Equal(75, snapshot.Windows[0].Phases[0].MaximumMs);
        Assert.Equal(0, snapshot.Windows[1].Phases[0].MaximumMs);
        Assert.Equal(1, snapshot.Windows[0].Gen0Collections);
        Assert.Equal(0, snapshot.Windows[1].Gen0Collections);
        Assert.Single(recorder.Snapshot(200, 1).Windows);
        Assert.Empty(recorder.Snapshot(200, 2).Windows);
        Assert.All(snapshot.Windows, window => Assert.Null(CampaignPerformanceEvidence.WindowError(window)));
    }

    [Fact]
    public void AThreeSecondFreezeIsOneHugeIntervalNotFabricatedSmoothWindows()
    {
        var recorder = new NativePerformanceWindows(Array.Empty<string>());
        recorder.Observe(true, 100, 0, 0, 0, true);
        recorder.Observe(true, 3100, 0, 0, 0, true);
        var window = Assert.Single(recorder.Snapshot(3100).Windows);
        Assert.Equal(1, window.IntervalCount);
        Assert.Equal(3000, window.IntervalSumMs);
        Assert.Equal(3000, window.MaximumIntervalMs);
        Assert.Equal(1, window.Buckets[6]);
        Assert.Null(CampaignPerformanceEvidence.WindowError(window));
    }

    [Fact]
    public void RingOverwritesAreExplicitAndOldSnapshotsCannotMutateTheCollector()
    {
        var recorder = new NativePerformanceWindows(new[] { "capture" }, capacity: 2, targetWindowMs: 100);
        for (int i = 0; i <= 5; i++) recorder.Observe(true, i * 100, 0, 0, 0, true);
        var snapshot = recorder.Snapshot(500, 1);
        Assert.Equal(4, snapshot.OldestSequence);
        Assert.Equal(5, snapshot.LatestSequence);
        Assert.Equal(2, snapshot.MissingRequestedWindows);
        Assert.Equal(3, snapshot.RingOverwrites);
        snapshot.Windows[0].Buckets[5] = 1000;
        snapshot.Windows[0].Phases[0].Count = 1000;
        var fresh = recorder.Snapshot(500);
        Assert.Equal(1, fresh.Windows[0].Buckets[5]);
        Assert.Equal(0, fresh.Windows[0].Phases[0].Count);
    }

    [Fact]
    public void DisabledPeriodsAndClockRegressionDoNotBecomeFastFrames()
    {
        var recorder = new NativePerformanceWindows(Array.Empty<string>(), targetWindowMs: 100);
        recorder.Observe(true, 0, 0, 0, 0, true);
        recorder.Observe(true, 30, 0, 0, 0, false);
        recorder.Observe(false, 50, 0, 0, 0, false);
        recorder.Observe(false, 5000, 0, 0, 0, false);
        recorder.Observe(true, 6000, 0, 0, 0, true);
        recorder.Observe(true, 5900, 0, 0, 0, true);
        recorder.Observe(true, 7000, 0, 0, 0, true);
        recorder.Observe(true, 7100, 0, 0, 0, true);
        var snapshot = recorder.Snapshot(7100);
        Assert.Equal(1, snapshot.InvalidSamples);
        Assert.Equal(2, snapshot.Windows.Length);
        Assert.Equal(30, snapshot.Windows[0].IntervalSumMs);
        Assert.Equal(0, snapshot.Windows[0].ForegroundIntervals);
        Assert.True(snapshot.Windows[1].Segment > snapshot.Windows[0].Segment);
        Assert.Equal(100, snapshot.Windows[1].IntervalSumMs);
    }

    [Fact]
    public void TickAndPhaseRecordingDoNotAllocateAfterConstruction()
    {
        var recorder = new NativePerformanceWindows(new[] { "capture" });
        MeasureRecordingAllocations(recorder, 0);
        MeasureRecordingAllocations(recorder, 100000);
        Assert.Equal(0, MeasureRecordingAllocations(recorder, 200000));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining
        | System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static long MeasureRecordingAllocations(NativePerformanceWindows recorder, int start)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = start; i < start + 100000; i++) { recorder.Observe(true, i, 0, 0, 0, true); recorder.RecordPhase("capture", 1); }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void UnsupportedOrInvalidPhaseDataCannotDisappearSilently()
    {
        var recorder = new NativePerformanceWindows(new[] { "capture" });
        recorder.Observe(true, 0, 0, 0, 0, true);
        recorder.RecordPhase("unknown", 1);
        recorder.RecordPhase("capture", double.NaN);
        Assert.Equal(1, recorder.Snapshot(1).UnknownPhaseObservations);
        Assert.Equal(1, recorder.Snapshot(1).InvalidSamples);
    }

    [Fact]
    public void EvidenceRejectsForgedOrMissingHistogramsAndCounterReset()
    {
        var recorder = new NativePerformanceWindows(Array.Empty<string>(), targetWindowMs: 100);
        recorder.Observe(true, 0, 2, 0, 0, true); recorder.Observe(true, 100, 1, 0, 0, true);
        var window = Assert.Single(recorder.Snapshot(100).Windows);
        Assert.Equal("gc_counter_reset", CampaignPerformanceEvidence.WindowError(window));
        window.GcCountersReset = false;
        window.Buckets[5] = 2;
        Assert.Equal("invalid_histogram", CampaignPerformanceEvidence.WindowError(window));
        window.Buckets = null!;
        Assert.Equal("invalid_window_shape", CampaignPerformanceEvidence.WindowError(window));
    }

    [Theory]
    [InlineData("baseline", "foreign_native_scope", true)]
    [InlineData("Current", null, true)]
    [InlineData("Current", "performance_sampling_unavailable", false)]
    public void CollectorRequiresExactOwnedCurrentAndFreshCachedRuntime(string save, string? error, bool enabled)
    {
        var auth = new CampaignTestAuthorization("run", "campaign", "timeline", "baseline", "prefix", "Current");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { gameOnline = true, armed = true, runtime = new {
            campaignId = "campaign", activeSaveName = save, capturedUtc = DateTime.UtcNow.ToString("o"), gameInstanceId = "game",
            saveSync = new { timelineId = "timeline", ready = true, alignmentPending = false },
            performance = new { schema = NativePerformanceWindows.Schema, samplingEnabled = enabled, snapshotCapturedUtc = DateTime.UtcNow.ToString("o"), clientModuleMvid = Guid.NewGuid().ToString() } } }));
        Assert.Equal(error, CampaignPerformanceEvidence.RuntimeScopeError(json.RootElement, auth, DateTime.UtcNow));
        Assert.Equal(save == "Current" ? "stale_runtime" : "foreign_native_scope", CampaignPerformanceEvidence.RuntimeScopeError(json.RootElement, auth, DateTime.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void CounterProjectionKeepsDebtAndFailureButNotPrivateBodiesOrArrays()
    {
        using var data = JsonDocument.Parse("{\"pending\":3,\"lagDays\":2,\"failed\":0,\"apiKey\":\"secret\",\"beliefs\":[1,2],\"jobBody\":\"private\",\"status\":\"catching_up\",\"dueInTransit\":7,\"durationMs\":2.5,\"mainThread\":false,\"clientOutboxDaysKnown\":false,\"clockEpoch\":\"private\"}");
        var counters = new Dictionary<string, object?>();
        CampaignPerformanceEvidence.ProjectCounters(data.RootElement, "queue", counters);
        Assert.Equal(8, counters.Count);
        Assert.True(counters.ContainsKey("queue.lagDays"));
        Assert.True(counters.ContainsKey("queue.dueInTransit"));
        Assert.True(counters.ContainsKey("queue.durationMs"));
        Assert.True(counters.ContainsKey("queue.mainThread"));
        Assert.True(counters.ContainsKey("queue.clientOutboxDaysKnown"));
        Assert.DoesNotContain(counters.Keys, key => key.Contains("apiKey") || key.Contains("jobBody") || key.Contains("beliefs"));
    }

    [Theory]
    [InlineData(false, 0, 100, 0, 1, 1, 0, false)]
    [InlineData(true, 1, 100, 0, 1, 1, 0, false)]
    [InlineData(true, 0, 1, 0, 0.01, 1, 0, false)]
    [InlineData(true, 0, 100, 1, 1, 1, 0, false)]
    [InlineData(true, 0, 100, 0, 1, 0, 0, false)]
    [InlineData(true, 0, 100, 0, 1, 1, 15001, false)]
    [InlineData(true, 0, 100, 0, 0.9, 0.95, 15000, true)]
    [InlineData(true, 0, 100, 0, 1, 1, 0, true)]
    public void IncompleteFaultedOrNonRepresentativeCollectionCannotReportSuccess(bool completed, int faults, long windows,
        long gaps, double coverage, double foreground, double tail, bool expected) =>
        Assert.Equal(expected, CampaignPerformanceEvidence.CollectionValid(completed, faults, windows, gaps, coverage, foreground, tail));

    [Fact]
    public void SnapshotCopyCostIsBoundedWithAFullHistory()
    {
        var recorder = new NativePerformanceWindows(new[] { "a", "b", "c", "d", "e", "f", "g", "h" });
        for (int i = 0; i <= 121; i++) recorder.Observe(true, i * 1000, 0, 0, 0, true);
        recorder.Snapshot(121000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = recorder.Snapshot(121000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(120, snapshot.Windows.Length);
        Assert.InRange(allocated, 1, 200000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedFailureOrCancellationLeavesAnExplicitFailedReport(bool cancel)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "reign-performance-fault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var options = TestOptions.Create(temporary);
            using var cancellation = new CancellationTokenSource();
            using var http = new HttpClient(new FaultHandler(cancellation, cancel)) { BaseAddress = options.ServerBaseUri };
            var api = new ReignApiClient(http, options, new SensitiveDataRedactor());
            var service = new CampaignTestService(options, null!, api, new TestingCatalogService(options));
            service.Prepare("campaign", "timeline", "Immutable", "owned-run", "Performance fault");
            object result = await TestingTools.MonitorCampaignPerformance(options, api, service,
                "campaign", "owned-run", durationSeconds: 30, cancellationToken: cancellation.Token);
            using var returned = JsonDocument.Parse(JsonSerializer.Serialize(result));
            using var retained = JsonDocument.Parse(await File.ReadAllTextAsync(returned.RootElement.GetProperty("reportPath").GetString()!));
            foreach (var report in new[] { returned.RootElement, retained.RootElement })
            {
                Assert.False(report.GetProperty("ok").GetBoolean());
                Assert.False(report.GetProperty("completedNormally").GetBoolean());
                Assert.Equal(cancel, report.GetProperty("cancelled").GetBoolean());
                Assert.Contains(report.GetProperty("faults").EnumerateArray(), item => item.GetString() ==
                    (cancel ? "observation_cancelled" : "observer_exception:FormatException"));
            }
            Assert.DoesNotContain("Injected private detail", await File.ReadAllTextAsync(returned.RootElement.GetProperty("reportPath").GetString()!));
            Assert.DoesNotContain("Injected private detail", await File.ReadAllTextAsync(returned.RootElement.GetProperty("samplesPath").GetString()!));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private sealed class FaultHandler(CancellationTokenSource cancellation, bool cancel) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancel) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            throw new FormatException("Injected private detail must never enter the evidence.");
        }
    }

    [Fact]
    public void NativeCamelCasePublicationRoundTripsEveryContractPropertyAndRejectsMissingFields()
    {
        var recorder = new NativePerformanceWindows(new[] { "capture" }, capacity: 2, targetWindowMs: 100);
        recorder.Observe(true, 100, 2, 2, 2, true);
        recorder.RecordPhase("capture", 7);
        recorder.RecordPhase("unknown", 1);
        recorder.RecordPhase("capture", double.NaN);
        recorder.Observe(true, 200, 3, 3, 3, true);
        recorder.Observe(false, 200, 3, 3, 3, true);
        recorder.Observe(true, 300, 3, 3, 3, true);
        recorder.RecordPhase("capture", 5);
        recorder.Observe(true, 400, 2, 2, 2, true);
        recorder.Observe(true, 500, 4, 4, 4, true);
        var original = recorder.Snapshot(501, afterSequence: 1);
        original.SnapshotCapturedUtc = DateTime.UtcNow.ToString("o");
        original.ClientModuleMvid = Guid.NewGuid().ToString("D");
        var json = Newtonsoft.Json.Linq.JObject.FromObject(original, Newtonsoft.Json.JsonSerializer.Create(
            new Newtonsoft.Json.JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() }));
        using var wire = JsonDocument.Parse(json.ToString(Newtonsoft.Json.Formatting.None));
        Assert.Null(CampaignPerformanceEvidence.SnapshotShapeError(wire.RootElement));
        var restored = wire.RootElement.Deserialize<NativePerformanceSnapshot>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(restored));
        Assert.True(restored!.InvalidSamples > 0 && restored.UnknownPhaseObservations > 0 && restored.Segment > 1 && restored.RingOverwrites > 0);
        Assert.True(restored.Windows[0].GcCountersReset);
        json.Remove("invalidSamples");
        using var missing = JsonDocument.Parse(json.ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal("invalid_snapshot_shape", CampaignPerformanceEvidence.SnapshotShapeError(missing.RootElement));
    }

    [Fact]
    public async Task AStatusDeadlineDisablesThatSourceWithoutAnotherRequestOrPrivateErrorText()
    {
        int calls = 0;
        using var handler = new QueueHandler(async (request, token) => {
            calls++; Assert.Equal(HttpMethod.Get, request.Method);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable private detail");
        });
        var options = TestOptions.Create();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var api = new ReignApiClient(http, options, new SensitiveDataRedactor());
        var probe = new CampaignPerformanceQueueProbe("world", "/world-test/overview", TimeSpan.FromMilliseconds(50));
        var first = await probe.ObserveAsync(api, null, CancellationToken.None);
        Assert.False(first.Ok); Assert.True(first.Attempted); Assert.Equal("request_deadline", first.ErrorCode);
        var skipped = await probe.ObserveAsync(api, null, CancellationToken.None);
        Assert.False(skipped.Ok); Assert.False(skipped.Attempted); Assert.Equal(1, calls);
        using var status = JsonDocument.Parse(JsonSerializer.Serialize(probe.Status()));
        Assert.Equal(1, status.RootElement.GetProperty("attempts").GetInt32());
        Assert.Equal(1, status.RootElement.GetProperty("skipped").GetInt32());
        Assert.True(status.RootElement.GetProperty("disabledAfterFailure").GetBoolean());
        Assert.DoesNotContain("private detail", status.RootElement.GetRawText());
    }

    [Fact]
    public async Task CompletedHttpFailureRetainsOnlyStatusAndDoesNotBecomeASuccessfulObservation()
    {
        using var handler = new QueueHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) {
            Content = new StringContent("{\"ok\":false,\"error\":\"private job body\"}") }));
        var options = TestOptions.Create();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var probe = new CampaignPerformanceQueueProbe("memory", "/memory/background/status");
        var observed = await probe.ObserveAsync(new ReignApiClient(http, options, new SensitiveDataRedactor()), null, CancellationToken.None);
        Assert.False(observed.Ok); Assert.Equal("http_500", observed.ErrorCode);
        Assert.DoesNotContain("private job body", JsonSerializer.Serialize(probe.Status()));
    }

    [Fact]
    public async Task UnexpectedQueueTransportFailureDisablesOnlyThatSourceAndHidesPrivateDetails()
    {
        using var handler = new QueueHandler((_, _) => throw new FormatException("Private transport detail"));
        var options = TestOptions.Create();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var probe = new CampaignPerformanceQueueProbe("world", "/world-test/overview");
        var observed = await probe.ObserveAsync(new ReignApiClient(http, options, new SensitiveDataRedactor()), null, CancellationToken.None);
        Assert.False(observed.Ok); Assert.Equal("transport_exception", observed.ErrorCode);
        Assert.False((await probe.ObserveAsync(null!, null, CancellationToken.None)).Attempted);
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(probe.Status()));
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    public void QueueReadinessMustBeFreshAndUniquelyIdentified(int ageSeconds)
    {
        var expected = new CampaignTestAuthorization("run", "campaign", "timeline", "baseline", "prefix", "Current");
        using var stale = JsonDocument.Parse(JsonSerializer.Serialize(new { schema = "reign_world_readiness_v1", campaignId = "campaign", timelineId = "timeline",
            campaignGeneration = "generation", observationId = Guid.NewGuid().ToString("N"), observedUtc = DateTime.UtcNow.AddSeconds(ageSeconds).ToString("o") }));
        Assert.Equal("queue_readiness_stale_or_missing", CampaignPerformanceEvidence.QueueScopeError(stale.RootElement, "world", expected));
        using var missing = JsonDocument.Parse("{\"schema\":\"reign_world_readiness_v1\",\"campaignId\":\"campaign\",\"timelineId\":\"timeline\"}");
        Assert.Equal("queue_readiness_stale_or_missing", CampaignPerformanceEvidence.QueueScopeError(missing.RootElement, "world", expected));
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfBeingMislabeledAsASourceDeadline()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var probe = new CampaignPerformanceQueueProbe("world", "/world-test/overview");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.ObserveAsync(null!, null, cancellation.Token));
    }

    [Theory]
    [InlineData("world", "reign_world_readiness_v1")]
    [InlineData("gossip", "reign-gossip-delivery-readiness-v1")]
    public void FreshQueueViewsRequireTheSameExactCampaignAndTimeline(string source, string schema)
    {
        var expected = new CampaignTestAuthorization("run", "campaign", "timeline", "baseline", "prefix", "Current");
        using var correct = JsonDocument.Parse(JsonSerializer.Serialize(new { schema, campaignId = "campaign", timelineId = "timeline",
            campaignGeneration = "generation", observationId = Guid.NewGuid().ToString("N"), observedUtc = DateTime.UtcNow.ToString("o") }));
        Assert.Null(CampaignPerformanceEvidence.QueueScopeError(correct.RootElement, source, expected));
        using var foreign = JsonDocument.Parse(JsonSerializer.Serialize(new { schema, campaignId = "campaign", timelineId = "other" }));
        Assert.Equal("queue_scope_mismatch:" + source, CampaignPerformanceEvidence.QueueScopeError(foreign.RootElement, source, expected));
    }

    private sealed class QueueHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}


public sealed class CampaignPerformanceProviderObservationTests
{
    private static Dictionary<string, object?> Guard(long allowed = 0, string? instance = null) => new() {
        ["schema"] = "reign-native-provider-free-guard-v1", ["armed"] = false, ["error"] = "",
        ["allowedDispatchesSinceProcessStart"] = allowed, ["blockedAttemptsSinceProcessStart"] = 0,
        ["serverInstance"] = instance ?? "1de0d35e-e136-44b5-a593-ae970804b0ae", ["cleanProviderFreeRun"] = false };
    private static string? Observe(CampaignPerformanceProviderObservation observer, Dictionary<string, object?> guard)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { nativeProviderTestGuard = guard }));
        return observer.Observe(json.RootElement);
    }

    [Fact]
    public void Default_policy_still_rejects_a_configured_workload_and_requires_the_clean_tripwire()
    {
        var observer = new CampaignPerformanceProviderObservation("provider_free");
        var guard = Guard();
        Assert.Equal("provider_isolation_failed", Observe(observer, guard));
        guard["armed"] = true; guard["cleanProviderFreeRun"] = true;
        Assert.Null(Observe(observer, guard));
        using var missing = JsonDocument.Parse("{}");
        Assert.Equal("provider_isolation_missing", observer.Observe(missing.RootElement));
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(observer.Snapshot()));
        Assert.True(report.RootElement.GetProperty("providerFreeRequired").GetBoolean());
    }

    [Fact]
    public void Configured_policy_observes_real_dispatch_growth_without_claiming_zero_dispatch_or_cost()
    {
        var observer = new CampaignPerformanceProviderObservation("observe_configured");
        Assert.Null(Observe(observer, Guard(42)));
        Assert.Null(Observe(observer, Guard(45)));
        Assert.Null(Observe(observer, Guard(45)));
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(observer.Snapshot()));
        Assert.False(report.RootElement.GetProperty("providerFreeRequired").GetBoolean());
        Assert.Equal(3, report.RootElement.GetProperty("allowedDispatchDeltaSinceFirstObservation").GetInt64());
        Assert.Equal(3, report.RootElement.GetProperty("observations").GetInt32());
        Assert.Contains("not billable calls", report.RootElement.GetProperty("limits").GetString());
    }

    [Theory]
    [InlineData("armed", true)]
    [InlineData("error", "Injected private detail")]
    [InlineData("blockedAttemptsSinceProcessStart", 1)]
    [InlineData("allowedDispatchesSinceProcessStart", -1)]
    [InlineData("allowedDispatchesSinceProcessStart", "45")]
    [InlineData("schema", "future-schema")]
    [InlineData("serverInstance", "invalid")]
    [InlineData("serverInstance", "00000000-0000-0000-0000-000000000000")]
    public void Configured_policy_fails_closed_on_armed_blocked_malformed_or_private_errors(string key, object value)
    {
        var observer = new CampaignPerformanceProviderObservation("observe_configured");
        var guard = Guard(); guard[key] = value;
        Assert.Equal("provider_observation_invalid", Observe(observer, guard));
        Assert.DoesNotContain("Injected private detail", JsonSerializer.Serialize(observer.Snapshot()));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("armed")]
    [InlineData("error")]
    [InlineData("blockedAttemptsSinceProcessStart")]
    [InlineData("allowedDispatchesSinceProcessStart")]
    [InlineData("serverInstance")]
    public void Configured_policy_requires_every_runtime_guard_evidence_field(string key)
    {
        var guard = Guard(); guard.Remove(key);
        Assert.Equal("provider_observation_invalid", Observe(new("observe_configured"), guard));
    }

    [Fact]
    public void Configured_policy_rejects_counter_reset_and_provider_process_replacement()
    {
        var observer = new CampaignPerformanceProviderObservation("observe_configured");
        Assert.Null(Observe(observer, Guard(45)));
        Assert.Equal("provider_counter_regressed", Observe(observer, Guard(44)));
        Assert.Equal("provider_instance_changed", Observe(observer, Guard(46, Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task Unknown_policy_is_rejected_before_enrollment_http_or_evidence_writes()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.MonitorCampaignPerformance(null!, null!, null!,
            "campaign", "owned-run", providerPolicy: "arbitrary"));
    }
}
