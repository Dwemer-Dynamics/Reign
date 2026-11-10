using ReignBeta.Shared.Characters;
using Reign.Core.Contracts.Relationships;
using Newtonsoft.Json.Linq;

namespace Reign.Mcp.Tests;

public sealed class PopulationLookupAndUploadTests
{
    [Fact]
    public void MarriageCaptureKeepsNoblesAndStopsBuildingTheUnusedAmbientGraph()
    {
        string root=TestOptions.FindWorkspace();
        string client=File.ReadAllText(Path.Combine(root,"ReignBeta/src/Modules/Relationships/Integration/ReignRelationshipDirectorClient.cs"));
        int start=client.IndexOf("private static JObject BuildRelationshipDirectorSnapshot()",StringComparison.Ordinal);
        string capture=client[start..client.IndexOf("private static List<JObject> BuildRelationshipPresenceGroups",start,StringComparison.Ordinal)];
        Assert.Contains("political_marriage_only_v1",capture);
        Assert.Contains("Select(BuildDirectorHero)",capture);
        Assert.DoesNotContain("BuildRelationshipPresenceGroups(",capture);
        Assert.DoesNotContain("participants.Select",capture);
        string server=File.ReadAllText(Path.Combine(root,"ReignServer/src/Modules/Relationships/ArrangedMarriage.cs"));
        start=server.IndexOf("private static Dictionary<string,object> ProcessArrangedMarriageSystem",StringComparison.Ordinal);
        string processor=server[start..server.IndexOf("private static double ReservedMarriageGold",start,StringComparison.Ordinal)];
        string compact=System.Text.RegularExpressions.Regex.Replace(processor,@"\s+","");
        foreach(string field in new[]{"heroes","presenceGroups","nativeRelations"})
            Assert.DoesNotContain("ReadDictionaryList(payload,\""+field+"\")",compact);
        Assert.Contains("compact_snapshot_mechanical_parity",server);
        string lifecycle=File.ReadAllText(Path.Combine(root,"ReignServer/src/Modules/Relationships/RelationshipLifecycle.cs"));
        start=lifecycle.IndexOf("private static List<Dictionary<string, object>> RunCurrentRelationshipSelfTests()",StringComparison.Ordinal);
        string active=lifecycle[start..lifecycle.IndexOf("private static void RunPreparedLifecycleWriteSelfTests",start,StringComparison.Ordinal)];
        Assert.Contains("results.AddRange(RunArrangedMarriageSelfTests());",active);
    }

    private sealed class Person(string id) { public string Id = id; public bool Alive = true; }

    [Fact]
    public void PassLookupKeepsNativeIdentityAndUsesAuthoritativeResolutionForDuplicatesAndMisses()
    {
        var living = new Person("same"); var dead = new Person("same") { Alive = false };
        var unique = new Person("unique"); var late = new Person("late");
        var original = new Dictionary<string, Person> { ["same"] = dead, ["unique"] = unique, ["late"] = late };
        var index = new TavernHouseHeroLookup<Person>(id => original.GetValueOrDefault(id));
        index.Refresh(new[] { "same", "unique", "late", "missing" }, new[] { living, unique }, new[] { dead }, p => p.Id);
        Assert.Same(dead, index.Find("same")); // Native registry authority, not assumed collection order.
        Assert.Equal(1, index.DuplicateIds);
        unique.Alive = false;
        Assert.Same(unique, index.Find("unique")); Assert.False(index.Find("unique")!.Alive);
        Assert.Same(late, index.Find("late")); Assert.Equal(1, index.FallbackHits);
        Assert.Null(index.Find("missing"));
        var created = new Person("created"); index.Created(created.Id, created);
        Assert.Same(created, index.Find("created"));
        index.Clear(); Assert.Equal(0, index.Count);
        index.Refresh(new[] { "unique" }, new[] { new Person("unique") }, Array.Empty<Person>(), p => p.Id);
        Assert.NotSame(unique, index.Find("unique")); // No stale object after another pass/load.
    }

    [Fact]
    public void ImmutableHeaderIsReadOnceAndInvalidatedOnReplacementOrLoad()
    {
        int reads = 0;
        var cache = new RelationshipSnapshotHeaderCache();
        string first = new string('x', 1_000_000);
        double Reader(string text) { reads++; return text.Length; }
        for (int i = 0; i < 100; i++) Assert.Equal(1_000_000, cache.ReadDay(first, Reader));
        Assert.Equal(1, reads);
        string replacement = new string('y', 500_000);
        Assert.Equal(500_000, cache.ReadDay(replacement, Reader)); Assert.Equal(2, reads);
        cache.Clear(); cache.ReadDay(replacement, Reader); Assert.Equal(3, reads);
        Assert.Throws<InvalidOperationException>(() => cache.ReadDay(first, _ => double.NaN));
        Assert.Equal(1_000_000, cache.ReadDay(first, Reader)); // Failed reads never poison the cache.
    }

    [Fact]
    public async Task SameScopeUploadJoinsOneWorkerWithoutRunningPreAwaitWorkOnCallerThread()
    {
        var gate = new RelationshipUploadGate<int>();
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? first = null, second = null; int callerThread = 0, calls = 0;
        var caller = new Thread(() => {
            try
            {
                callerThread = Environment.CurrentManagedThreadId;
                first = gate.Run("campaign\0timeline", async () => {
                    Interlocked.Increment(ref calls); entered.SetResult(Environment.CurrentManagedThreadId);
                    await release.Task; return 42;
                });
                second = gate.Run("campaign\0timeline", () => throw new InvalidOperationException("Duplicate factory"));
                launched.SetResult(true);
            }
            catch (Exception ex) { launched.SetException(ex); }
        }) { IsBackground = true };
        caller.Start();
        try
        {
            await launched.Task.WaitAsync(TimeSpan.FromSeconds(10));
            int workerThread = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(callerThread, workerThread); Assert.Same(first, second); Assert.Equal(1, calls);
        }
        finally { release.TrySetResult(true); }
        Assert.Equal(42, await first!); Assert.Equal(42, await second!);
    }

    [Fact]
    public async Task FailedUploadsCanRetryAndIndependentScopesCannotShareResults()
    {
        var gate = new RelationshipUploadGate<int>(2);
        await Assert.ThrowsAsync<FormatException>(() => gate.Run("first", () => throw new FormatException()));
        Assert.Equal(7, await gate.Run("first", () => Task.FromResult(7)));
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = gate.Run("a", async () => { await release.Task; return 1; });
        var b = gate.Run("b", async () => { await release.Task; return 2; });
        try { Assert.Throws<InvalidOperationException>(() => { _ = gate.Run("c", () => Task.FromResult(3)); }); }
        finally { release.TrySetResult(true); }
        Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(a, b));
    }

    [Fact]
    public void UploadReceiptCannotDeleteARewrittenDayOrANewDay()
    {
        using var files = new OutboxFixture(); var sync = new object();
        string day = files.Write("00000001.json", "{\"value\":1}");
        var accepted = RelationshipInputOutbox.Capture(files.Root, sync);
        lock (sync) File.WriteAllText(day, "{\"value\":2}"); // Same size, exact-text guard matters.
        string next = files.Write("00000002.json", "{\"value\":3}");
        accepted.RetireAccepted(ex => throw ex);
        Assert.True(File.Exists(day)); Assert.True(File.Exists(next));
        var latest = RelationshipInputOutbox.Capture(files.Root, sync);
        Assert.Equal(new[] { 2, 3 }, latest.Parse(x => JObject.Parse(x).Value<int>("value")));
        latest.RetireAccepted(ex => throw ex);
        Assert.Empty(Directory.GetFiles(files.Root));
    }

    [Fact]
    public void InputOrderingLimitsAndMalformedInputKeepOriginalDurabilitySemantics()
    {
        using var files = new OutboxFixture(); var sync = new object();
        for (int i = 10; i >= 1; i--) files.Write(i.ToString("D8") + ".json", "{\"day\":" + i + "}");
        files.Write("00000000.json.tmp", "incomplete");
        var batch = RelationshipInputOutbox.Capture(files.Root, sync);
        Assert.Equal(Enumerable.Range(1, 8), batch.Parse(x => JObject.Parse(x).Value<int>("day")));
        Assert.Equal(11, Directory.GetFiles(files.Root).Length); // Reading/parsing/failure is never an ACK.
        files.Write("00000001.json", "{broken");
        var bad = RelationshipInputOutbox.Capture(files.Root, sync);
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => bad.Parse(JObject.Parse));
        Assert.Equal(11, Directory.GetFiles(files.Root).Length);
    }

    [Fact]
    public void CanonicalPaddedDaysCrossDigitBoundariesInChronologicalOrder()
    {
        using var files = new OutboxFixture();
        files.Write(100.ToString("D8") + ".json", "{\"day\":100}");
        files.Write(99.ToString("D8") + ".json", "{\"day\":99}");
        var batch = RelationshipInputOutbox.Capture(files.Root, new object());
        Assert.Equal(new[] { 99, 100 }, batch.Parse(x => JObject.Parse(x).Value<int>("day")));
    }

    [Fact]
    public async Task ParsingDetachedTextsDoesNotRetainThePublishersMutationLock()
    {
        using var files = new OutboxFixture(); var sync = new object();
        files.Write("00000001.json", "{}");
        var batch = RelationshipInputOutbox.Capture(files.Root, sync);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var parsing = Task.Run(() => batch.Parse(text => { entered.SetResult(true); release.Wait(TimeSpan.FromSeconds(10)); return JObject.Parse(text); }));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bool available = Monitor.TryEnter(sync);
            if (available) Monitor.Exit(sync);
            Assert.True(available);
        }
        finally { release.Set(); }
        Assert.Single(await parsing);
    }

    private sealed class OutboxFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "reign-input-outbox-" + Guid.NewGuid().ToString("N"));
        public OutboxFixture() => Directory.CreateDirectory(Root);
        public string Write(string name, string text) { string path = Path.Combine(Root, name); File.WriteAllText(path, text); return path; }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
