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
        start=lifecycle.IndexOf("static List<Dictionary<string, object>> RunCurrentRelationshipSelfTests()",StringComparison.Ordinal);
        string active=lifecycle[start..lifecycle.IndexOf("private static void RunPreparedLifecycleWriteSelfTests",start,StringComparison.Ordinal)];
        Assert.Contains("results.AddRange(RunArrangedMarriageSelfTests());",active);
    }

    private sealed class Person(string id) { public string Id = id; public bool Alive = true; }

    [Fact]
    public void FaceSchemaCachePreservesFreshWeightsAndCandidateRandomDrawOrder()
    {
        var key = new PopulationFaceSchemaKey(0, false, 30, 6);
        string[] names = { "age", "face_a", "height", "face_b", "weight", "build" };
        int[] Fresh() => Enumerable.Range(0, names.Length)
            .Where(i => names[i] != "age" && names[i] != "height" && names[i] != "weight" && names[i] != "build").ToArray();
        int reads = 0;
        var cache = new PopulationFaceSchemaCache();
        for (int generation = 0; generation < 600; generation++)
        {
            // Fractional age/body decoding can change every vector; only positions are cached.
            float[] weights = Enumerable.Range(0, 6).Select(i => (float)(generation * .001 + i)).ToArray();
            var actual = cache.Get(key, _ => { reads++; return Fresh(); });
            Assert.Equal(Fresh().Select(i => weights[i]), actual.Select(i => weights[i]));
            var oldRng = new Random(generation); var newRng = new Random(generation);
            var expectedWeights = (float[])weights.Clone(); var actualWeights = (float[])weights.Clone();
            foreach (int i in Fresh()) expectedWeights[i] = .2f + (float)oldRng.NextDouble() * .6f;
            foreach (int i in actual) actualWeights[i] = .2f + (float)newRng.NextDouble() * .6f;
            Assert.Equal(expectedWeights, actualWeights);
            Assert.Equal(oldRng.NextDouble(), newRng.NextDouble());
        }
        Assert.Equal(4, reads); // Initial read, first reuse, reuse256 and512.
        Assert.Equal(3, cache.VerificationReads);
        Assert.Equal(0, cache.Mismatches);
    }

    [Fact]
    public void FaceSchemaKeySeparatesEveryNativeSchemaArgumentAndOwnsReadonlyIndices()
    {
        var cache = new PopulationFaceSchemaCache(); var key = new PopulationFaceSchemaKey(1, false, 30, 8);
        int[] supplied = { 1, 3 };
        var result = cache.Get(key, _ => supplied);
        supplied[0] = 7;
        Assert.Throws<NotSupportedException>(() => ((IList<int>)result)[0] = 5);
        Assert.Equal(new[] { 1, 3 }, cache.Get(key, _ => new[] { 1, 3 }));
        foreach (var different in new[] { new PopulationFaceSchemaKey(2, false, 30, 8),
            new PopulationFaceSchemaKey(1, true, 30, 8), new PopulationFaceSchemaKey(1, false, 31, 8),
            new PopulationFaceSchemaKey(1, false, 30, 9) })
            Assert.Equal(new[] { 2 }, cache.Get(different, _ => new[] { 2 }));
        Assert.Equal(5, cache.Count);
        Assert.Equal(5, cache.Misses);
    }

    [Fact]
    public void FaceSchemaMismatchUsesFreshIndicesAndDisablesAllSessionMemoization()
    {
        var cache = new PopulationFaceSchemaCache(); var key = new PopulationFaceSchemaKey(0, false, 30, 8);
        Assert.Equal(new[] { 1 }, cache.Get(key, _ => new[] { 1 }));
        Assert.Equal(new[] { 2 }, cache.Get(key, _ => new[] { 2 }));
        Assert.True(cache.Disabled); Assert.Equal(1, cache.Mismatches);
        Assert.Equal(0, cache.Count); Assert.Equal(0, cache.PayloadBytes);
        Assert.Equal(new[] { 3 }, cache.Get(key, _ => new[] { 3 }));
        cache.Clear();
        Assert.False(cache.Disabled); Assert.Equal(0, cache.Mismatches);
        Assert.Equal(new[] { 4 }, cache.Get(key, _ => new[] { 4 }));
    }

    [Fact]
    public void FaceSchemaPeriodicVerificationDetectsChangeWithoutUsingRandomness()
    {
        var cache = new PopulationFaceSchemaCache(); var key = new PopulationFaceSchemaKey(0, true, 20, 8);
        int reads = 0;
        int[] Read() { reads++; return new[] { reads < 3 ? 1 : 2 }; }
        cache.Get(key, _ => Read());
        for (int i = 1; i < 256; i++) Assert.Equal(new[] { 1 }, cache.Get(key, _ => Read()));
        Assert.Equal(new[] { 2 }, cache.Get(key, _ => Read()));
        Assert.Equal(3, reads); Assert.True(cache.Disabled);
    }

    [Fact]
    public void FailedFaceSchemaVerificationMustRetryBeforeReusingPositions()
    {
        var cache = new PopulationFaceSchemaCache(); var key = new PopulationFaceSchemaKey(0, false, 20, 8);
        cache.Get(key, _ => new[] { 1 });
        Assert.Throws<InvalidOperationException>(() => cache.Get(key, _ => throw new InvalidOperationException()));
        Assert.Equal(new[] { 2 }, cache.Get(key, _ => new[] { 2 }));
        Assert.Equal(2, cache.VerificationReads); Assert.True(cache.Disabled);
    }

    [Fact]
    public void FaceSchemaCacheBoundsEntriesAndPayloadAndNeverCachesFailedReads()
    {
        var first = new PopulationFaceSchemaKey(0, false, 20, 8);
        var second = new PopulationFaceSchemaKey(0, false, 21, 8);
        var cache = new PopulationFaceSchemaCache(1, 72);
        Assert.Throws<InvalidOperationException>(() => cache.Get(first, _ => throw new InvalidOperationException()));
        Assert.Equal(0, cache.Count);
        cache.Get(first, _ => new[] { 1, 3 });
        Assert.Equal(72, cache.PayloadBytes);
        cache.Get(second, _ => new[] { 1, 3 });
        Assert.Equal(1, cache.Count); Assert.Equal(72, cache.PayloadBytes);
        var byteBound = new PopulationFaceSchemaCache(20, 72);
        byteBound.Get(first, _ => new[] { 1, 3 }); byteBound.Get(second, _ => new[] { 1, 3 });
        Assert.Equal(1, byteBound.Count);
        byteBound.Get(first, _ => new[] { 1, 2, 3 }); // Oversized entry is observed, never retained.
        Assert.Equal(1, byteBound.Count); Assert.Equal(72, byteBound.PayloadBytes);
    }

    [Fact]
    public void LedgerIdentityLookupPreservesFirstMatchCaseAndLiveStateAcrossAppendAndLoad()
    {
        var first = new Person("same"); var duplicate = new Person("same");
        var people = new List<Person> { first, duplicate, new Person("SAME"), new Person(""), new Person(null!) };
        var index = new PopulationIdentityIndex<Person>(p => p.Id);
        foreach (string? id in new string?[] { "same", "SAME", "", null, "missing" })
            Assert.Same(people.FirstOrDefault(p => p.Id == id), index.Find(people, id));
        first.Alive = false;
        Assert.False(index.Find(people, "same")!.Alive); // Eligibility is never cached.
        var appended = new Person("appended"); people.Add(appended);
        Assert.Same(appended, index.Find(people, appended.Id));
        var loaded = people.Select(p => new Person(p.Id)).ToList();
        Assert.Same(loaded[0], index.Find(loaded, "same")); // Same count, different load.
        Assert.NotSame(first, index.Find(loaded, "same"));
    }

    [Fact]
    public void ReservedLedgerIdentityAssignmentRequiresInvalidationAndCannotRetainOldKey()
    {
        var reserved = new Person(""); var people = new List<Person> { reserved };
        var index = new PopulationIdentityIndex<Person>(p => p.Id);
        Assert.Same(reserved, index.Find(people, ""));
        reserved.Id = "native-created";
        index.Invalidate();
        Assert.Null(index.Find(people, ""));
        Assert.Same(reserved, index.Find(people, "native-created"));
        people.Clear();
        Assert.Null(index.Find(people, "native-created"));
    }

    [Fact]
    public void FailedIdentityIndexBuildDoesNotPublishPartialStateAndExplicitReplacementCanReload()
    {
        var people = new List<Person> { new Person("first"), new Person("last") };
        bool fail = true;
        var index = new PopulationIdentityIndex<Person>(p =>
        {
            if (p.Id == "last" && fail) throw new InvalidOperationException("Interrupted build");
            return p.Id;
        });
        Assert.Throws<InvalidOperationException>(() => index.Find(people, "first"));
        fail = false;
        Assert.Same(people[1], index.Find(people, "last"));
        people[1] = new Person("replacement");
        index.Invalidate(); // Explicit same-count replacement contract.
        Assert.Null(index.Find(people, "last"));
        Assert.Same(people[1], index.Find(people, "replacement"));
    }

    [Fact]
    public void RepeatedPopulationLookupsDoNotRescanTheLedgerForEveryWanderer()
    {
        var people = Enumerable.Range(0, 10000).Select(i => new Person(i.ToString())).ToList();
        int identityReads = 0;
        var index = new PopulationIdentityIndex<Person>(p => { identityReads++; return p.Id; });
        Assert.Same(people[9999], index.Find(people, "9999"));
        Assert.Equal(10000, identityReads);
        for (int i = 0; i < people.Count; i++)
            Assert.Same(people[i], index.Find(people, people[i].Id));
        Assert.Equal(10000, identityReads); // 10k lookups add no ledger enumeration.
        people.Add(new Person("new"));
        Assert.Same(people[10000], index.Find(people, "new"));
        Assert.Equal(20001, identityReads); // One rebuild after an append.
    }

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
